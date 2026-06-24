using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AttendanceDesktop
{
    public enum PushTarget { Mysql, Api, Both }

    /// <summary>
    /// Local settings (saved next to the exe as desktop-settings.json). Holds a
    /// list of devices for one client (tenant) and where to push punches. The
    /// MySQL connection string is stored DPAPI-encrypted, never plaintext.
    /// </summary>
    public sealed class DesktopConfig
    {
        public string TenantId { get; set; } = "";
        public long SiteId { get; set; } = 0;            // tags bio_punch.site_id
        public PushTarget Push { get; set; } = PushTarget.Mysql;
        public DbCfg Db { get; set; } = new DbCfg();
        public ApiCfg Api { get; set; } = new ApiCfg();
        public List<DeviceEntry> Devices { get; set; } = new List<DeviceEntry>();

        public sealed class DbCfg
        {
            /// <summary>Serialized form: DPAPI-encrypted, base64. Not human-readable.</summary>
            public string EncConnectionString { get; set; } = "";

            /// <summary>In-memory plaintext, never serialized.</summary>
            [JsonIgnore]
            public string ConnectionString { get; set; } =
                "Server=localhost;Port=3306;Database=shikzya;Uid=bridge;Pwd=;SslMode=Preferred;";
        }

        public sealed class ApiCfg
        {
            public string ApiBaseUrl { get; set; } = "https://app.shikzya.com";
            public string SiteToken { get; set; } = "";
        }

        public sealed class DeviceEntry
        {
            public string Name { get; set; } = "";
            public string Ip { get; set; } = "";
            public int Port { get; set; } = 5005;
            public int MachineNo { get; set; } = 1;
            public int NetPassword { get; set; } = 0;
            public int License { get; set; } = 1261;
            public int DeviceId { get; set; } = 1;
        }

        private const string DefaultConn = "Server=localhost;Port=3306;Database=shikzya;Uid=bridge;Pwd=;SslMode=Preferred;";

        private static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "desktop-settings.json");

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static DesktopConfig Load()
        {
            var cfg = new DesktopConfig();
            try
            {
                if (File.Exists(Path))
                    cfg = JsonSerializer.Deserialize<DesktopConfig>(File.ReadAllText(Path), Opts) ?? new DesktopConfig();
            }
            catch { /* fall back to defaults */ }

            cfg.Db ??= new DbCfg();
            cfg.Api ??= new ApiCfg();
            cfg.Devices ??= new List<DeviceEntry>();

            cfg.Db.ConnectionString = Dpapi.Unprotect(cfg.Db.EncConnectionString);
            if (string.IsNullOrWhiteSpace(cfg.Db.ConnectionString))
                cfg.Db.ConnectionString = DefaultConn;
            return cfg;
        }

        public void Save()
        {
            Db.EncConnectionString = Dpapi.Protect(Db.ConnectionString);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Opts));
        }
    }

    /// <summary>Windows DPAPI helpers (current-user scope) for secrets at rest.</summary>
    internal static class Dpapi
    {
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(enc);
            }
            catch { return ""; }
        }

        public static string Unprotect(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }  // wrong user/machine, or corrupt -> treat as unset
        }
    }
}
