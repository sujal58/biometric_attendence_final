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
        public TenantConfig tenant { get; set; }
        public DeviceConfig device { get; set; }
        public TimeSyncConfig timeSync { get; set; }
        public DatabaseConfig database { get; set; }
        public PollConfig poll { get; set; }
        public ScheduleConfig schedule { get; set; }
        public CommandConfig command { get; set; }
        public WebConfig web { get; set; }
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
            tenant = tenant ?? new TenantConfig();
            device = device ?? new DeviceConfig();
            timeSync = timeSync ?? new TimeSyncConfig();
            database = database ?? new DatabaseConfig();
            poll = poll ?? new PollConfig();
            schedule = schedule ?? new ScheduleConfig();
            command = command ?? new CommandConfig();
            web = web ?? new WebConfig();
            logging = logging ?? new LoggingConfig();
            if (string.IsNullOrWhiteSpace(logging.directory))
                logging.directory = "logs";
            // Default the logical device id to the device's machine number.
            if (database.deviceId <= 0)
                database.deviceId = device.machineNo;
            if (poll.intervalSeconds <= 0)
                poll.intervalSeconds = 60;
            if (command.pollSeconds <= 0)
                command.pollSeconds = 15;
            schedule.pullTimes = schedule.pullTimes ?? new string[0];
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

    public sealed class TenantConfig
    {
        /// <summary>Identifies the school in the multi-tenant Shikzya platform.
        /// Every punch / command is tagged with this so one shared database can
        /// hold many schools. Set per deployment.</summary>
        public string tenantId { get; set; } = "";
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

    /// <summary>Unattended scheduled pulls (used by the `serve` agent).</summary>
    public sealed class ScheduleConfig
    {
        /// <summary>Local times (HH:mm) at which to pull, e.g. ["12:00","17:00"].</summary>
        public string[] pullTimes { get; set; } = new string[0];
        /// <summary>Pull once when the agent starts.</summary>
        public bool catchUpOnStart { get; set; } = true;
    }

    /// <summary>On-demand fetch triggered from Shikzya via the bio_fetch_command table.</summary>
    public sealed class CommandConfig
    {
        public bool enabled { get; set; } = true;
        /// <summary>How often the agent checks for pending fetch commands.</summary>
        public int pollSeconds { get; set; } = 15;
    }

    /// <summary>Local web UI + REST API hosted at the school (built in increment 3b).</summary>
    public sealed class WebConfig
    {
        public bool enabled { get; set; } = true;
        /// <summary>HttpListener prefix. Use http://localhost:8080/ for this PC only,
        /// or http://+:8080/ to allow other PCs on the school LAN (needs a urlacl).</summary>
        public string url { get; set; } = "http://localhost:8080/";
    }

    public sealed class LoggingConfig
    {
        public string directory { get; set; } = "logs";
    }
}
