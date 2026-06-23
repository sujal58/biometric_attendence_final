using System;
using System.IO;
using System.Text.Json;

namespace AttendanceDesktop
{
    /// <summary>Local settings the technician sets once (saved next to the exe).</summary>
    public sealed class DesktopConfig
    {
        public DeviceCfg Device { get; set; } = new DeviceCfg();
        public UploadCfg Upload { get; set; } = new UploadCfg();

        public sealed class DeviceCfg
        {
            public string Ip { get; set; } = "192.168.1.33";
            public int Port { get; set; } = 5005;
            public int MachineNo { get; set; } = 1;
            public int NetPassword { get; set; } = 0;
            public int License { get; set; } = 1261;
        }

        public sealed class UploadCfg
        {
            public string ApiBaseUrl { get; set; } = "https://app.shikzya.com";
            public string SiteToken { get; set; } = "";
            public int DeviceId { get; set; } = 1;
        }

        private static string Path =>
            System.IO.Path.Combine(AppContext.BaseDirectory, "desktop-settings.json");

        private static readonly JsonSerializerOptions Opts =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };

        public static DesktopConfig Load()
        {
            try
            {
                if (File.Exists(Path))
                    return JsonSerializer.Deserialize<DesktopConfig>(File.ReadAllText(Path), Opts) ?? new DesktopConfig();
            }
            catch { /* fall back to defaults */ }
            return new DesktopConfig();
        }

        public void Save() => File.WriteAllText(Path, JsonSerializer.Serialize(this, Opts));
    }
}
