using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using AttendanceBridge.Api;
using AttendanceBridge.Data;
using AttendanceBridge.Logging;
using AttendanceBridge.Scheduling;

namespace AttendanceBridge.Agent
{
    /// <summary>
    /// The fleet agent. One per school site. It periodically reads its device
    /// list from Shikzya and, for every device, pulls attendance on schedule and
    /// on demand (Shikzya fetch commands), uploads to the API, queues offline if
    /// the internet is down, and heartbeats health back. Self-configuring: adding
    /// a device in Shikzya is enough.
    /// </summary>
    public sealed class FleetAgent : BackgroundService
    {
        private const string AgentVersion = "1.0.0";

        private readonly AgentConfig _cfg;
        private readonly ApiClient _api;
        private readonly PunchSpool _spool;
        private readonly DeviceWorker _worker = new DeviceWorker();

        private DeviceDef[] _devices = Array.Empty<DeviceDef>();
        private Dictionary<int, DailyScheduler> _schedulers = new Dictionary<int, DailyScheduler>();
        private readonly Dictionary<int, (string lastPullAt, string lastStatus)> _state =
            new Dictionary<int, (string, string)>();

        private DateTime _lastDeviceRefresh = DateTime.MinValue;
        private DateTime _lastHeartbeat = DateTime.MinValue;
        private DateTime _lastSpoolRetry = DateTime.MinValue;

        public FleetAgent(AgentConfig cfg)
        {
            _cfg = cfg;
            _api = new ApiClient(cfg.ApiBaseUrl, cfg.SiteToken);
            string spoolDir = Path.IsPathRooted(cfg.SpoolDirectory)
                ? cfg.SpoolDirectory
                : Path.Combine(AppContext.BaseDirectory, cfg.SpoolDirectory);
            _spool = new PunchSpool(spoolDir);
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            Log.Info("Fleet agent started. API=" + _cfg.ApiBaseUrl);
            int tick = Math.Max(5, Math.Min(_cfg.CommandPollSeconds, 30));

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await RefreshDevicesIfDue(ct);
                    await RunSchedules(ct);
                    await RunCommands(ct);
                    await RetrySpoolIfDue(ct);
                    await HeartbeatIfDue(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log.Error("Agent loop error.", ex); }

                try { await Task.Delay(TimeSpan.FromSeconds(tick), ct); }
                catch (OperationCanceledException) { break; }
            }
            Log.Info("Fleet agent stopped.");
        }

        private async Task RefreshDevicesIfDue(CancellationToken ct)
        {
            if (_devices.Length > 0 &&
                (DateTime.UtcNow - _lastDeviceRefresh).TotalSeconds < _cfg.DeviceRefreshSeconds)
                return;

            try
            {
                var list = await _api.GetDevicesAsync(ct);
                _devices = list.Where(d => d.Active && !string.IsNullOrWhiteSpace(d.Ip)).ToArray();
                _lastDeviceRefresh = DateTime.UtcNow;

                // Reuse existing schedulers (keeps "already fired today" state).
                var rebuilt = new Dictionary<int, DailyScheduler>();
                foreach (var d in _devices)
                    rebuilt[d.DeviceId] = _schedulers.TryGetValue(d.DeviceId, out var s)
                        ? s : new DailyScheduler(d.PullTimes);
                _schedulers = rebuilt;

                Log.Info("Device list refreshed: " + _devices.Length + " active device(s).");
            }
            catch (Exception ex)
            {
                Log.Warn("Could not refresh device list: " + ex.Message +
                         (_devices.Length > 0 ? " (keeping cached list)" : ""));
            }
        }

        private async Task RunSchedules(CancellationToken ct)
        {
            var now = DateTime.Now;
            foreach (var d in _devices)
            {
                if (!_schedulers.TryGetValue(d.DeviceId, out var sch)) continue;
                foreach (var due in sch.DueNow(now))
                {
                    Log.Info("Scheduled pull " + due + " for device " + d.DeviceId);
                    await PullAndUpload(d, ct);
                }
            }
        }

        private async Task RunCommands(CancellationToken ct)
        {
            CommandDto[] cmds;
            try { cmds = await _api.GetCommandsAsync(ct); }
            catch (Exception ex) { Log.Warn("Could not fetch commands: " + ex.Message); return; }

            foreach (var c in cmds)
            {
                var d = _devices.FirstOrDefault(x => x.DeviceId == c.DeviceId);
                CommandResult result;
                if (d == null)
                {
                    result = new CommandResult { Ok = false, Message = "device " + c.DeviceId + " not assigned to this site" };
                }
                else
                {
                    Log.Info("Command #" + c.Id + " -> pull device " + c.DeviceId);
                    var r = await PullAndUpload(d, ct);
                    result = new CommandResult { Ok = r.ok, RecordsRead = r.read, RecordsInserted = r.inserted, Message = r.message };
                }

                try { await _api.PostCommandResultAsync(c.Id, result, ct); }
                catch (Exception ex) { Log.Warn("Could not post command result #" + c.Id + ": " + ex.Message); }
            }
        }

        private async Task<(int read, int inserted, bool ok, string message)> PullAndUpload(DeviceDef d, CancellationToken ct)
        {
            var pull = _worker.Pull(d); // blocking native call (one device at a time)
            if (!pull.Ok)
            {
                SetState(d.DeviceId, "error: " + pull.Message);
                return (0, 0, false, pull.Message);
            }

            int read = pull.Records.Count;
            if (read == 0) { SetState(d.DeviceId, "read 0"); return (0, 0, true, "read 0"); }

            var upload = new PunchUpload { DeviceId = d.DeviceId, Punches = pull.Records.Select(ToDto).ToArray() };

            try
            {
                int inserted = await _api.UploadPunchesAsync(upload, ct);
                SetState(d.DeviceId, "read " + read + ", inserted " + inserted);
                return (read, inserted, true, "read " + read + ", inserted " + inserted);
            }
            catch (Exception ex)
            {
                _spool.Save(upload);
                Log.Warn("Upload failed for device " + d.DeviceId + " (" + ex.Message + "); queued " + read + " offline.");
                SetState(d.DeviceId, "read " + read + ", queued offline");
                return (read, 0, true, "read " + read + ", queued offline");
            }
        }

        private async Task RetrySpoolIfDue(CancellationToken ct)
        {
            if ((DateTime.UtcNow - _lastSpoolRetry).TotalSeconds < _cfg.SpoolRetrySeconds) return;
            _lastSpoolRetry = DateTime.UtcNow;

            var pending = _spool.Pending();
            if (pending.Count == 0) return;
            Log.Info("Retrying " + pending.Count + " queued upload(s)...");

            foreach (var file in pending)
            {
                if (ct.IsCancellationRequested) break;
                PunchUpload up;
                try { up = _spool.Read(file); }
                catch { _spool.Remove(file); continue; } // unreadable -> drop

                try
                {
                    int ins = await _api.UploadPunchesAsync(up, ct);
                    _spool.Remove(file);
                    Log.Info("Flushed queued batch (" + (up.Punches?.Length ?? 0) + " punches, inserted " + ins + ").");
                }
                catch (Exception ex)
                {
                    Log.Warn("Spool retry still failing: " + ex.Message);
                    break; // internet still down; try again later
                }
            }
        }

        private async Task HeartbeatIfDue(CancellationToken ct)
        {
            if ((DateTime.UtcNow - _lastHeartbeat).TotalSeconds < _cfg.HeartbeatSeconds) return;
            _lastHeartbeat = DateTime.UtcNow;

            var hb = new HeartbeatUpload
            {
                AgentVersion = AgentVersion,
                Devices = _devices.Select(d =>
                {
                    _state.TryGetValue(d.DeviceId, out var st);
                    return new HeartbeatDevice { DeviceId = d.DeviceId, LastPullAt = st.lastPullAt, LastStatus = st.lastStatus };
                }).ToArray()
            };

            try { await _api.PostHeartbeatAsync(hb, ct); }
            catch (Exception ex) { Log.Warn("Heartbeat failed: " + ex.Message); }
        }

        private void SetState(int deviceId, string status) =>
            _state[deviceId] = (DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), status);

        private static PunchDto ToDto(PunchRecord r) => new PunchDto
        {
            EnrollNumber = r.EnrollNumber,
            PunchTime = r.PunchTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            VerifyMode = r.VerifyMode,
            VerifyLabel = r.VerifyLabel,
            InOutMode = r.InOutMode,
            IoMode = r.IoMode,
            DoorMode = r.DoorMode,
            Temperature = r.Temperature,
        };
    }
}
