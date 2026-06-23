using System;
using System.IO;
using System.Linq;
using System.Threading;
using AttendanceBridge.Config;
using AttendanceBridge.Data;
using AttendanceBridge.Device;
using AttendanceBridge.Logging;

namespace AttendanceBridge
{
    /// <summary>
    /// Console entry point.
    ///
    ///   AttendanceBridge.exe info   - connect, print device info, sync clock
    ///   AttendanceBridge.exe pull   - connect, sync clock, pull logs into MySQL once
    ///   AttendanceBridge.exe poll   - keep pulling on a fixed interval until Ctrl+C
    ///   AttendanceBridge.exe serve  - unattended agent: scheduled pulls + on-demand
    ///                                 fetch commands from Shikzya (run via Task Scheduler)
    ///
    /// No command defaults to "info". The local web UI is hosted by `serve`.
    /// </summary>
    internal static class Program
    {
        private static volatile bool _stop;

        private static int Main(string[] args)
        {
            if (Environment.Is64BitProcess)
            {
                Console.Error.WriteLine(
                    "FATAL: this process is 64-bit. The TimeWatch native SDK is x86 only. " +
                    "Build/run AttendanceBridge as x86.");
                return 2;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string command = args.FirstOrDefault(a => !a.StartsWith("--"))?.ToLowerInvariant() ?? "info";
            string configPath = ResolveConfigPath(baseDir, args);

            BridgeConfig cfg;
            try
            {
                cfg = BridgeConfig.Load(configPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Could not load configuration: " + ex.Message);
                return 3;
            }

            Log.Init(Path.IsPathRooted(cfg.logging.directory)
                ? cfg.logging.directory
                : Path.Combine(baseDir, cfg.logging.directory));

            Log.Info("AttendanceBridge starting. Command: " + command);
            Log.Info("Using config: " + configPath);

            switch (command)
            {
                case "info": return RunInfo(cfg);
                case "pull": return RunPull(cfg);
                case "poll": return RunPoll(cfg);
                case "serve": return RunServe(cfg);
                default:
                    Console.Error.WriteLine("Unknown command '" + command + "'. Use: info | pull | poll | serve.");
                    return 4;
            }
        }

        // ---- info: Phase 1 behaviour ---------------------------------------
        private static int RunInfo(BridgeConfig cfg)
        {
            using (var conn = new DeviceConnection(cfg.device))
            {
                if (!conn.Connect()) return 1;
                try
                {
                    var info = new DeviceInfoModule(conn);
                    info.LogSnapshot(info.Read());
                    SyncClock(conn, cfg);
                }
                catch (Exception ex)
                {
                    Log.Error("Error while reading device state.", ex);
                    return 1;
                }
            }
            Log.Info("Done.");
            return 0;
        }

        // ---- pull: one-shot log download into MySQL ------------------------
        private static int RunPull(BridgeConfig cfg)
        {
            if (!RequireDatabase(cfg, out var repo)) return 5;

            using (var conn = new DeviceConnection(cfg.device))
            {
                if (!conn.Connect()) return 1;
                try
                {
                    SyncClock(conn, cfg);
                    PullOnce(conn, cfg, repo);
                }
                catch (Exception ex)
                {
                    Log.Error("Pull failed.", ex);
                    repo.WriteBridgeLog("ERROR", "pull", ex.Message);
                    return 1;
                }
            }
            Log.Info("Done.");
            return 0;
        }

        // ---- poll: pull on an interval until Ctrl+C -------------------------
        private static int RunPoll(BridgeConfig cfg)
        {
            if (!RequireDatabase(cfg, out var repo)) return 5;

            Console.CancelKeyPress += (s, e) => { e.Cancel = true; _stop = true; Log.Info("Stop requested..."); };
            Log.Info("Polling every " + cfg.poll.intervalSeconds + "s. Press Ctrl+C to stop.");

            int backoff = 2;
            using (var conn = new DeviceConnection(cfg.device))
            {
                while (!_stop)
                {
                    try
                    {
                        if (!conn.IsConnected && !conn.Connect())
                        {
                            Sleep(backoff);
                            backoff = Math.Min(backoff * 2, 60); // exponential backoff, capped
                            continue;
                        }

                        backoff = 2;
                        PullOnce(conn, cfg, repo);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Poll iteration failed; will reconnect.", ex);
                        repo.WriteBridgeLog("ERROR", "poll", ex.Message);
                        conn.Disconnect(); // force a clean reconnect next loop
                    }

                    Sleep(cfg.poll.intervalSeconds);
                }
            }
            Log.Info("Polling stopped.");
            return 0;
        }

        // ---- shared helpers ------------------------------------------------

        private static PullResult PullOnce(DeviceConnection conn, BridgeConfig cfg, PunchRepository repo)
        {
            var pulledAt = DateTime.Now;
            var poller = new LogPoller(conn);
            var records = poller.Read(cfg.poll.readMark, cfg.poll.verbose);

            int inserted = repo.UpsertBatch(records);
            DateTime? lastPunch = records.Count > 0 ? records.Max(r => r.PunchTime) : (DateTime?)null;

            string status = "read " + records.Count + ", inserted " + inserted;
            Log.Info("Pull complete: " + status + (records.Count - inserted > 0
                ? " (" + (records.Count - inserted) + " already present)" : ""));

            repo.UpdateDeviceCursor(pulledAt, lastPunch, status);
            repo.WriteBridgeLog("INFO", "pull", status);

            if (cfg.poll.emptyAfterPull && records.Count > 0)
            {
                Log.Warn("emptyAfterPull is enabled - clearing the device log now that records are saved.");
                poller.EmptyLog();
            }

            return new PullResult { Read = records.Count, Inserted = inserted };
        }

        // ---- serve: unattended agent (scheduled pulls + on-demand commands) -
        private static int RunServe(BridgeConfig cfg)
        {
            if (!RequireDatabase(cfg, out var repo)) return 5;

            Console.CancelKeyPress += (s, e) => { e.Cancel = true; _stop = true; Log.Info("Stop requested..."); };
            Log.Info("Agent (serve) started. Tenant='" + cfg.tenant.tenantId + "', device=" + cfg.database.deviceId + ".");
            Log.Info("Scheduled pull times: [" + string.Join(", ", cfg.schedule.pullTimes) + "]" +
                     (cfg.schedule.pullTimes.Length == 0 ? " (none set)" : "") + ".");
            Log.Info("On-demand commands: " + (cfg.command.enabled
                ? "polling every " + cfg.command.pollSeconds + "s" : "disabled") + ".");

            var scheduler = new DailyScheduler(cfg.schedule.pullTimes);
            int tick = cfg.command.enabled ? cfg.command.pollSeconds : 30;

            using (var conn = new DeviceConnection(cfg.device))
            {
                if (cfg.schedule.catchUpOnStart)
                    SafePull(conn, cfg, repo, "startup", null);

                while (!_stop)
                {
                    foreach (var due in scheduler.DueNow(DateTime.Now))
                        SafePull(conn, cfg, repo, "schedule " + due, null);

                    if (cfg.command.enabled)
                    {
                        FetchCommand cmd;
                        while (!_stop && (cmd = TryClaim(repo)) != null)
                            SafePull(conn, cfg, repo, "command#" + cmd.Id, cmd);
                    }

                    Sleep(tick);
                }
            }
            Log.Info("Agent stopped.");
            return 0;
        }

        // Connects if needed, syncs the clock on a fresh connect, pulls, and (if the
        // pull was triggered by a Shikzya command) reports the result back to the queue.
        private static void SafePull(DeviceConnection conn, BridgeConfig cfg, PunchRepository repo, string reason, FetchCommand cmd)
        {
            Log.Info("Pull trigger: " + reason);
            try
            {
                if (!conn.IsConnected)
                {
                    if (!conn.Connect())
                    {
                        if (cmd != null) repo.CompleteCommand(cmd.Id, false, 0, 0, "device connect failed");
                        return;
                    }
                    SyncClock(conn, cfg); // only right after a fresh connect
                }

                var res = PullOnce(conn, cfg, repo);
                if (cmd != null)
                    repo.CompleteCommand(cmd.Id, true, res.Read, res.Inserted,
                        "read " + res.Read + ", inserted " + res.Inserted);
            }
            catch (Exception ex)
            {
                Log.Error("Pull failed (" + reason + ").", ex);
                repo.WriteBridgeLog("ERROR", "pull", ex.Message);
                if (cmd != null) repo.CompleteCommand(cmd.Id, false, 0, 0, ex.Message);
                conn.Disconnect(); // force a clean reconnect next time
            }
        }

        private static FetchCommand TryClaim(PunchRepository repo)
        {
            try { return repo.ClaimNextCommand(); }
            catch (Exception ex) { Log.Warn("Could not poll fetch commands: " + ex.Message); return null; }
        }

        private static void SyncClock(DeviceConnection conn, BridgeConfig cfg)
        {
            var time = new TimeSyncModule(conn);
            if (cfg.timeSync.syncOnStartup)
                time.SyncIfDrift(cfg.timeSync.maxDriftSeconds);
            else
                Log.Info("Device clock: " + time.ReadDeviceTime().ToString("yyyy-MM-dd HH:mm:ss") +
                         " (sync disabled).");
        }

        private static bool RequireDatabase(BridgeConfig cfg, out PunchRepository repo)
        {
            repo = null;
            if (!cfg.database.IsConfigured)
            {
                Console.Error.WriteLine(
                    "database.connectionString is not set (or still contains CHANGE_ME). " +
                    "Set it in appsettings.json before using pull/poll.");
                return false;
            }
            repo = new PunchRepository(cfg.database.connectionString, cfg.tenant.tenantId, cfg.database.deviceId);
            try
            {
                repo.EnsureReachable();
                // Auto-create the bio_* tables if they don't exist yet.
                SchemaInitializer.EnsureSchema(cfg.database.connectionString);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Cannot reach MySQL or set up the schema: " + ex.Message);
                Console.Error.WriteLine("(The database named in the connection string must already exist.)");
                return false;
            }
            return true;
        }

        private static void Sleep(int seconds)
        {
            for (int i = 0; i < seconds * 10 && !_stop; i++)
                Thread.Sleep(100); // responsive to Ctrl+C
        }

        // Config resolution order: --config <path>, then appsettings.json next to
        // the exe, then appsettings.json in the current working directory.
        private static string ResolveConfigPath(string baseDir, string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];

            var candidates = new[]
            {
                Path.Combine(baseDir, "appsettings.json"),
                Path.Combine(Environment.CurrentDirectory, "appsettings.json"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;

            // Return the primary location so the not-found error names where to put it.
            return candidates[0];
        }
    }

    internal sealed class PullResult
    {
        public int Read;
        public int Inserted;
    }

    /// <summary>
    /// Fires once per configured HH:mm time per day. DueNow returns the times that
    /// have arrived since the last check today; the set resets at midnight.
    /// </summary>
    internal sealed class DailyScheduler
    {
        private readonly System.Collections.Generic.List<TimeSpan> _times =
            new System.Collections.Generic.List<TimeSpan>();
        private readonly System.Collections.Generic.HashSet<string> _firedToday =
            new System.Collections.Generic.HashSet<string>();
        private DateTime _day = DateTime.MinValue.Date;

        public DailyScheduler(string[] times)
        {
            foreach (var t in times ?? new string[0])
                if (TimeSpan.TryParse(t, out var ts))
                    _times.Add(ts);
        }

        public System.Collections.Generic.IEnumerable<string> DueNow(DateTime now)
        {
            if (now.Date != _day) { _day = now.Date; _firedToday.Clear(); }

            var due = new System.Collections.Generic.List<string>();
            foreach (var ts in _times)
            {
                string key = ts.ToString();
                if (now.TimeOfDay >= ts && !_firedToday.Contains(key))
                {
                    _firedToday.Add(key);
                    due.Add(string.Format("{0:00}:{1:00}", ts.Hours, ts.Minutes));
                }
            }
            return due;
        }
    }
}
