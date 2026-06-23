using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AttendanceBridge.Agent;
using AttendanceBridge.Device;
using AttendanceBridge.Logging;

namespace AttendanceBridge
{
    /// <summary>
    /// Entry point.
    ///
    ///   AttendanceBridge.exe              run the fleet agent (as a Windows Service
    ///                                     when installed as one, else as a console app)
    ///   AttendanceBridge.exe test --ip 192.168.1.33 [--port 5005] [--license 1261]
    ///                                     connect to one device and print its info +
    ///                                     time (field diagnostic; no API needed)
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (Environment.Is64BitProcess)
            {
                Console.Error.WriteLine("FATAL: this process is 64-bit. The TimeWatch native SDK is x86 only.");
                return 2;
            }

            if (args.Length > 0 && args[0].Equals("test", StringComparison.OrdinalIgnoreCase))
                return RunTest(args);

            string baseDir = AppContext.BaseDirectory;
            string cfgPath = ResolveConfigPath(baseDir, args);

            AgentConfig cfg;
            try { cfg = AgentConfig.Load(cfgPath); }
            catch (Exception ex) { Console.Error.WriteLine("Could not load configuration: " + ex.Message); return 3; }

            Log.Init(Path.IsPathRooted(cfg.Logging.Directory)
                ? cfg.Logging.Directory
                : Path.Combine(baseDir, cfg.Logging.Directory));
            Log.Info("AttendanceBridge fleet agent starting. Config: " + cfgPath);

            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddWindowsService(o => o.ServiceName = "AttendanceBridge");
            builder.Services.AddSingleton(cfg);
            builder.Services.AddHostedService<FleetAgent>();
            builder.Build().Run();
            return 0;
        }

        // Connect to one device by CLI args and print its identity + time. Lets a
        // field installer verify a freshly mounted device without any API/config.
        private static int RunTest(string[] args)
        {
            Log.Init(Path.Combine(AppContext.BaseDirectory, "logs"));
            var p = new DeviceParams
            {
                IpAddress = GetArg(args, "--ip", "192.168.1.33"),
                NetPort = IntArg(args, "--port", 5005),
                NetPassword = IntArg(args, "--password", 0),
                License = IntArg(args, "--license", 1261),
                MachineNo = IntArg(args, "--machine", 1),
                TimeoutMs = IntArg(args, "--timeout", 5000),
            };

            Log.Info("Test connect to " + p.IpAddress + ":" + p.NetPort);
            using var conn = new DeviceConnection(p);
            if (!conn.Connect()) return 1;
            try
            {
                var info = new DeviceInfoModule(conn);
                info.LogSnapshot(info.Read());
                Log.Info("Device time: " + new TimeSyncModule(conn).ReadDeviceTime().ToString("yyyy-MM-dd HH:mm:ss"));
            }
            finally { conn.Disconnect(); }
            return 0;
        }

        private static string ResolveConfigPath(string baseDir, string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];

            string c1 = Path.Combine(baseDir, "appsettings.json");
            if (File.Exists(c1)) return c1;
            string c2 = Path.Combine(Environment.CurrentDirectory, "appsettings.json");
            if (File.Exists(c2)) return c2;
            return c1;
        }

        private static string GetArg(string[] args, string name, string def)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return def;
        }

        private static int IntArg(string[] args, string name, int def) =>
            int.TryParse(GetArg(args, name, def.ToString()), out var v) ? v : def;
    }
}
