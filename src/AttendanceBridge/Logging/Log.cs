using System;
using System.IO;
using System.Text;

namespace AttendanceBridge.Logging
{
    public enum LogLevel { Info, Warn, Error }

    /// <summary>
    /// Minimal thread-safe logger: writes timestamped lines to the console and
    /// to a daily rolling file under the configured directory. Kept dependency
    /// free for Phase 1; can be swapped for Serilog later without changing call
    /// sites.
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _directory = "logs";

        public static void Init(string directory)
        {
            _directory = string.IsNullOrWhiteSpace(directory) ? "logs" : directory;
            Directory.CreateDirectory(_directory);
        }

        public static void Info(string message) => Write(LogLevel.Info, message);
        public static void Warn(string message) => Write(LogLevel.Warn, message);
        public static void Error(string message) => Write(LogLevel.Error, message);

        public static void Error(string message, Exception ex) =>
            Write(LogLevel.Error, message + " :: " + ex.GetType().Name + ": " + ex.Message);

        private static void Write(LogLevel level, string message)
        {
            var now = DateTime.Now;
            var line = string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1,-5}] {2}",
                now, level.ToString().ToUpperInvariant(), message);

            lock (Gate)
            {
                var prev = Console.ForegroundColor;
                Console.ForegroundColor =
                    level == LogLevel.Error ? ConsoleColor.Red :
                    level == LogLevel.Warn ? ConsoleColor.Yellow : prev;
                Console.WriteLine(line);
                Console.ForegroundColor = prev;

                try
                {
                    Directory.CreateDirectory(_directory);
                    var file = Path.Combine(_directory, "bridge-" + now.ToString("yyyyMMdd") + ".log");
                    File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Never let a logging failure crash the bridge.
                }
            }
        }
    }
}
