using System;
using System.Diagnostics;
using System.IO;

namespace AttendanceBridge.Agent
{
    /// <summary>
    /// Self-install: the single exe can register itself as a Windows Service and
    /// write its pre-keyed config, so a GUI installer (or one command) is all that
    /// is needed - no PowerShell. Requires Administrator (sc.exe).
    ///
    ///   AttendanceBridge.exe install --token &lt;siteToken&gt; [--api &lt;url&gt;]
    ///   AttendanceBridge.exe uninstall
    /// </summary>
    public static class ServiceInstaller
    {
        private const string ServiceName = "AttendanceBridge";
        private const string DisplayName = "Shikzya Attendance Bridge";

        public static int Install(string[] args)
        {
            string token = Arg(args, "--token");
            string api = Arg(args, "--api") ?? "https://app.shikzya.com";
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.Error.WriteLine("A site token is required:  install --token <siteToken> [--api <url>]");
                return 1;
            }

            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string dir = Path.GetDirectoryName(exe);
            WriteConfig(Path.Combine(dir, "appsettings.json"), api, token);

            Sc("stop " + ServiceName);
            Sc("delete " + ServiceName);

            int rc = Sc("create " + ServiceName + " binPath= \"" + exe + "\" start= auto DisplayName= \"" + DisplayName + "\"");
            if (rc != 0)
            {
                Console.Error.WriteLine("Could not create the service (exit " + rc +
                    "). Run this as Administrator.");
                return rc;
            }
            Sc("description " + ServiceName + " \"Bridges TimeWatch biometric devices to Shikzya.\"");
            Sc("failure " + ServiceName + " reset= 86400 actions= restart/60000/restart/60000/restart/60000");
            Sc("start " + ServiceName);

            Console.WriteLine("Installed and started the " + ServiceName + " service. It runs on boot.");
            return 0;
        }

        public static int Uninstall(string[] args)
        {
            Sc("stop " + ServiceName);
            int rc = Sc("delete " + ServiceName);
            Console.WriteLine(rc == 0 ? "Service removed." : "Service not found (exit " + rc + ").");
            return 0;
        }

        private static void WriteConfig(string path, string api, string token)
        {
            string json =
                "{\n" +
                "  \"apiBaseUrl\": \"" + Escape(api) + "\",\n" +
                "  \"siteToken\": \"" + Escape(token) + "\",\n" +
                "  \"deviceRefreshSeconds\": 300,\n" +
                "  \"commandPollSeconds\": 15,\n" +
                "  \"heartbeatSeconds\": 300,\n" +
                "  \"spoolRetrySeconds\": 60,\n" +
                "  \"spoolDirectory\": \"spool\",\n" +
                "  \"logging\": { \"directory\": \"logs\" }\n" +
                "}\n";
            File.WriteAllText(path, json);
        }

        private static int Sc(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                var p = Process.Start(psi);
                p.WaitForExit();
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("sc.exe failed: " + ex.Message);
                return -1;
            }
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
