using System;
using System.IO;
using System.Text.Json;

namespace AttendanceBridge.Agent
{
    /// <summary>
    /// The only configuration on a school PC. Everything device-specific comes
    /// from the Shikzya API; this just says which API to call and with what site
    /// token. Baked into the per-school installer.
    /// </summary>
    public sealed class AgentConfig
    {
        public string ApiBaseUrl { get; set; } = "";
        public string SiteToken { get; set; } = "";

        public int DeviceRefreshSeconds { get; set; } = 300;  // re-read the device list
        public int CommandPollSeconds { get; set; } = 15;     // check for Shikzya fetch buttons
        public int HeartbeatSeconds { get; set; } = 300;      // report health to Shikzya
        public int SpoolRetrySeconds { get; set; } = 60;      // retry queued (offline) uploads
        public string SpoolDirectory { get; set; } = "spool";

        public LoggingSection Logging { get; set; } = new LoggingSection();

        public sealed class LoggingSection
        {
            public string Directory { get; set; } = "logs";
        }

        public static AgentConfig Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "Configuration file not found at '" + path + "'. Copy appsettings.example.json " +
                    "to appsettings.json (next to the exe) and set apiBaseUrl + siteToken.", path);

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var cfg = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path), opts)
                      ?? throw new InvalidDataException("appsettings.json could not be parsed.");

            if (cfg.Logging == null) cfg.Logging = new LoggingSection();
            if (string.IsNullOrWhiteSpace(cfg.Logging.Directory)) cfg.Logging.Directory = "logs";
            if (string.IsNullOrWhiteSpace(cfg.SpoolDirectory)) cfg.SpoolDirectory = "spool";
            if (cfg.DeviceRefreshSeconds <= 0) cfg.DeviceRefreshSeconds = 300;
            if (cfg.CommandPollSeconds <= 0) cfg.CommandPollSeconds = 15;
            if (cfg.HeartbeatSeconds <= 0) cfg.HeartbeatSeconds = 300;
            if (cfg.SpoolRetrySeconds <= 0) cfg.SpoolRetrySeconds = 60;

            if (string.IsNullOrWhiteSpace(cfg.ApiBaseUrl))
                throw new InvalidDataException("apiBaseUrl must be set in appsettings.json.");
            if (string.IsNullOrWhiteSpace(cfg.SiteToken) || cfg.SiteToken.Contains("PASTE"))
                throw new InvalidDataException("siteToken must be set in appsettings.json (the per-school token).");

            return cfg;
        }
    }
}
