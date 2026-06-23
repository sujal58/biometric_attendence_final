using System;
using System.IO;
using System.Reflection;
using AttendanceBridge.Config;
using AttendanceBridge.Device;
using AttendanceBridge.Logging;

namespace AttendanceBridge
{
    /// <summary>
    /// Phase 1 entry point. Runs as a console app that connects to the
    /// TimeWatch device, prints its identity / configuration / record counts,
    /// reads the device clock and (optionally) corrects drift.
    ///
    /// Later phases add the log poller, the MySQL writer and the local REST
    /// listener, and a Windows-Service host. For now --console is the only mode.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // The native FKAttend DLLs are 32-bit. Fail fast and clearly rather
            // than dying with BadImageFormatException deep inside interop.
            if (Environment.Is64BitProcess)
            {
                Console.Error.WriteLine(
                    "FATAL: this process is 64-bit. The TimeWatch native SDK is x86 only. " +
                    "Build/run AttendanceBridge as x86.");
                return 2;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
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

            Log.Info("AttendanceBridge starting (Phase 1: connect + device info + time sync).");
            Log.Info("Using config: " + configPath);

            using (var conn = new DeviceConnection(cfg.device))
            {
                if (!conn.Connect())
                    return 1;

                try
                {
                    var info = new DeviceInfoModule(conn);
                    info.LogSnapshot(info.Read());

                    var time = new TimeSyncModule(conn);
                    if (cfg.timeSync.syncOnStartup)
                        time.SyncIfDrift(cfg.timeSync.maxDriftSeconds);
                    else
                        Log.Info("Device clock: " +
                                 time.ReadDeviceTime().ToString("yyyy-MM-dd HH:mm:ss") +
                                 " (sync on startup disabled).");
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

        // Config resolution order: --config <path> argument, else appsettings.json
        // next to the exe, else the source tree copy (handy during development).
        private static string ResolveConfigPath(string baseDir, string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];

            string next = Path.Combine(baseDir, "appsettings.json");
            if (File.Exists(next)) return next;

            return next; // returned even if missing so Load() can emit a helpful error
        }
    }
}
