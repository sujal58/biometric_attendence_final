using System;
using System.IO;
using System.Web.Script.Serialization;

namespace AttendanceBridge.Config
{
    /// <summary>
    /// Strongly-typed view of appsettings.json. Parsed with the framework's
    /// built-in JavaScriptSerializer so Phase 1 needs no NuGet dependencies.
    /// </summary>
    public sealed class BridgeConfig
    {
        public DeviceConfig device { get; set; }
        public TimeSyncConfig timeSync { get; set; }
        public DatabaseConfig database { get; set; }
        public PollConfig poll { get; set; }
        public LoggingConfig logging { get; set; }

        public static BridgeConfig Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "Configuration file not found at '" + path + "'. Copy appsettings.example.json " +
                    "to appsettings.json (next to the executable) and fill in your device/network " +
                    "details. If you just created/edited it in the project folder, rebuild so it " +
                    "copies to the output folder.", path);

            var json = File.ReadAllText(path);
            var cfg = new JavaScriptSerializer().Deserialize<BridgeConfig>(json);
            if (cfg == null)
                throw new InvalidDataException("appsettings.json could not be parsed.");

            cfg.ApplyDefaults();
            cfg.Validate();
            return cfg;
        }

        private void ApplyDefaults()
        {
            device = device ?? new DeviceConfig();
            timeSync = timeSync ?? new TimeSyncConfig();
            database = database ?? new DatabaseConfig();
            poll = poll ?? new PollConfig();
            logging = logging ?? new LoggingConfig();
            if (string.IsNullOrWhiteSpace(logging.directory))
                logging.directory = "logs";
            // Default the logical device id to the device's machine number.
            if (database.deviceId <= 0)
                database.deviceId = device.machineNo;
            if (poll.intervalSeconds <= 0)
                poll.intervalSeconds = 60;
        }

        private void Validate()
        {
            if (string.IsNullOrWhiteSpace(device.ipAddress))
                throw new InvalidDataException("device.ipAddress must be set in appsettings.json.");
            if (device.netPort <= 0 || device.netPort > 65535)
                throw new InvalidDataException("device.netPort must be a valid TCP port.");
            if (device.timeoutMs <= 0)
                device.timeoutMs = 5000;
        }
    }

    public sealed class DeviceConfig
    {
        public int machineNo { get; set; } = 1;
        public string ipAddress { get; set; } = "192.168.1.33";
        public int netPort { get; set; } = 5005;
        public int timeoutMs { get; set; } = 5000;
        public int protocolType { get; set; } = 0;   // 0 = TCP/IP, 1 = UDP
        public int netPassword { get; set; } = 0;
        public int license { get; set; } = 1261;      // vendor default; supplied by TimeWatch
    }

    public sealed class TimeSyncConfig
    {
        public bool syncOnStartup { get; set; } = true;
        public int maxDriftSeconds { get; set; } = 30;
    }

    public sealed class DatabaseConfig
    {
        /// <summary>MySQL connection string, e.g. "Server=localhost;Port=3306;Database=school;Uid=bridge;Pwd=...;".</summary>
        public string connectionString { get; set; } = "";
        /// <summary>Logical device id used in bio_punch / bio_device. Defaults to device.machineNo.</summary>
        public int deviceId { get; set; } = 0;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(connectionString) &&
            !connectionString.Contains("CHANGE_ME");
    }

    public sealed class PollConfig
    {
        public int intervalSeconds { get; set; } = 60;
        /// <summary>0 = read all logs (recommended; dedup in DB), 1 = only unread.</summary>
        public int readMark { get; set; } = 0;
        /// <summary>Clear the device log after a successful pull. Leave false for safety.</summary>
        public bool emptyAfterPull { get; set; } = false;
        /// <summary>Log each decoded record as it is read (handy for verifying data).</summary>
        public bool verbose { get; set; } = false;
    }

    public sealed class LoggingConfig
    {
        public string directory { get; set; } = "logs";
    }
}
