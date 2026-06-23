using System;
using System.Linq;
using AttendanceBridge.Config;
using AttendanceBridge.Data;
using AttendanceBridge.Logging;

namespace AttendanceBridge.Device
{
    public sealed class PullOutcome
    {
        public bool Ok;
        public int Read;
        public int Inserted;
        public string Message;
    }

    /// <summary>
    /// Owns the single device session and serializes every fetch behind a lock,
    /// so the scheduler, the on-demand command poller and the web UI can all ask
    /// for a pull without ever talking to the (single-client) device at the same
    /// time. Connects lazily and re-syncs the clock on each fresh connect.
    /// </summary>
    public sealed class FetchService : IDisposable
    {
        private readonly object _gate = new object();
        private readonly BridgeConfig _cfg;
        private readonly PunchRepository _repo;
        private readonly DeviceConnection _conn;

        public FetchService(BridgeConfig cfg, PunchRepository repo)
        {
            _cfg = cfg;
            _repo = repo;
            _conn = new DeviceConnection(cfg.device);
        }

        public PullOutcome Fetch(string reason, FetchCommand cmd = null)
        {
            lock (_gate)
            {
                Log.Info("Fetch trigger: " + reason);
                try
                {
                    if (!_conn.IsConnected)
                    {
                        if (!_conn.Connect())
                        {
                            if (cmd != null) _repo.CompleteCommand(cmd.Id, false, 0, 0, "device connect failed");
                            return new PullOutcome { Ok = false, Message = "device connect failed" };
                        }
                        SyncClock();
                    }

                    var outcome = PullOnce();
                    if (cmd != null)
                        _repo.CompleteCommand(cmd.Id, true, outcome.Read, outcome.Inserted, outcome.Message);
                    return outcome;
                }
                catch (Exception ex)
                {
                    Log.Error("Fetch failed (" + reason + ").", ex);
                    _repo.WriteBridgeLog("ERROR", "fetch", ex.Message);
                    if (cmd != null) _repo.CompleteCommand(cmd.Id, false, 0, 0, ex.Message);
                    _conn.Disconnect(); // force a clean reconnect next time
                    return new PullOutcome { Ok = false, Message = ex.Message };
                }
            }
        }

        private PullOutcome PullOnce()
        {
            var pulledAt = DateTime.Now;
            var poller = new LogPoller(_conn);
            var records = poller.Read(_cfg.poll.readMark, _cfg.poll.verbose);

            int inserted = _repo.UpsertBatch(records);
            DateTime? lastPunch = records.Count > 0 ? records.Max(r => r.PunchTime) : (DateTime?)null;

            string status = "read " + records.Count + ", inserted " + inserted;
            Log.Info("Pull complete: " + status + (records.Count - inserted > 0
                ? " (" + (records.Count - inserted) + " already present)" : ""));

            _repo.UpdateDeviceCursor(pulledAt, lastPunch, status);
            _repo.WriteBridgeLog("INFO", "pull", status);

            if (_cfg.poll.emptyAfterPull && records.Count > 0)
            {
                Log.Warn("emptyAfterPull is enabled - clearing the device log now that records are saved.");
                poller.EmptyLog();
            }

            return new PullOutcome { Ok = true, Read = records.Count, Inserted = inserted, Message = status };
        }

        private void SyncClock()
        {
            var time = new TimeSyncModule(_conn);
            if (_cfg.timeSync.syncOnStartup)
                time.SyncIfDrift(_cfg.timeSync.maxDriftSeconds);
        }

        public void Dispose()
        {
            lock (_gate) _conn.Dispose();
        }
    }
}
