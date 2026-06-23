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
    ///   AttendanceBridge.exe info   - connect, print device info, sync clock (Phase 1)
    ///   AttendanceBridge.exe pull   - connect, sync clock, pull logs into MySQL once (Phase 2)
    ///   AttendanceBridge.exe poll   - keep pulling on an interval until Ctrl+C (Phase 2)
    ///
    /// No command defaults to "info". A Windows-Service host and the local REST
    /// API arrive in Phase 3.
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
                default:
                    Console.Error.WriteLine("Unknown command '" + command + "'. Use: info | pull | poll.");
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

        private static void PullOnce(DeviceConnection conn, BridgeConfig cfg, PunchRepository repo)
        {
            var pulledAt = DateTime.Now;
            var poller = new LogPoller(conn);
            var records = poller.Read(cfg.poll.readMark);

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
            repo = new PunchRepository(cfg.database.connectionString, cfg.database.deviceId);
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

        // Config resolution: --config <path>, else appsettings.json next to exe.
        private static string ResolveConfigPath(string baseDir, string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return Path.Combine(baseDir, "appsettings.json");
        }
    }
}
