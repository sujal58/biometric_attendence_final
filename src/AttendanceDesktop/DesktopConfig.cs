using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AttendanceDesktop
{
    /// <summary>One biometric device the tool connects to on the LAN.</summary>
    public sealed class DeviceEntry
    {
        public string Name { get; set; } = "";
        public string Ip { get; set; } = "";
        public int Port { get; set; } = 5005;
        public int MachineNo { get; set; } = 1;
        public int NetPassword { get; set; } = 0;
        public int License { get; set; } = 1261;   // device SDK license int (FK_ConnectNet)
    }

    /// <summary>A device the school is allowed to use (returned by activation).</summary>
    public sealed class DeviceRef
    {
        public string Mac { get; set; } = "";
        public string Serial { get; set; } = "";
    }

    /// <summary>Cached activation result (stored DPAPI-encrypted).</summary>
    public sealed class LicenseCache
    {
        public bool Valid { get; set; }
        public string TenantId { get; set; } = "";
        public string ExpiresAt { get; set; } = "";     // ISO; "" = no expiry
        public string LastCheckAt { get; set; } = "";   // ISO
        public List<DeviceRef> AllowedDevices { get; set; } = new List<DeviceRef>();
    }

    /// <summary>
    /// Local settings (desktop-settings.json). API-only: NO database credentials.
    /// The license key + the cached activation are stored DPAPI-encrypted.
    /// </summary>
    public sealed class DesktopConfig
    {
        public string ApiBaseUrl { get; set; } = "https://app.shikzya.com";
        public int AutoFetchMinutes { get; set; } = 0;
        public List<DeviceEntry> Devices { get; set; } = new List<DeviceEntry>();

        // Admin password (PBKDF2 hash; non-reversible).
        public string AdminPasswordSalt { get; set; } = "";
        public string AdminPasswordHash { get; set; } = "";

        // License key (DPAPI-encrypted at rest).
        public string EncLicenseKey { get; set; } = "";
        [JsonIgnore] public string LicenseKey { get; set; } = "";

        // Activation cache (DPAPI-encrypted JSON of LicenseCache).
        public string LicenseCacheEnc { get; set; } = "";
        [JsonIgnore] public LicenseCache Cache { get; set; } = new LicenseCache();

        [JsonIgnore] public bool HasAdminPassword => !string.IsNullOrEmpty(AdminPasswordHash);

        private static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "desktop-settings.json");

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        public static DesktopConfig Load()
        {
            var cfg = new DesktopConfig();
            try
            {
                if (File.Exists(Path))
                    cfg = JsonSerializer.Deserialize<DesktopConfig>(File.ReadAllText(Path), Opts) ?? new DesktopConfig();
            }
            catch { /* defaults */ }

            cfg.Devices ??= new List<DeviceEntry>();
            cfg.LicenseKey = Dpapi.Unprotect(cfg.EncLicenseKey);

            var cacheJson = Dpapi.Unprotect(cfg.LicenseCacheEnc);
            if (!string.IsNullOrEmpty(cacheJson))
            {
                try { cfg.Cache = JsonSerializer.Deserialize<LicenseCache>(cacheJson, Opts) ?? new LicenseCache(); }
                catch { cfg.Cache = new LicenseCache(); }
            }
            return cfg;
        }

        public void Save()
        {
            EncLicenseKey = Dpapi.Protect(LicenseKey);
            LicenseCacheEnc = Dpapi.Protect(JsonSerializer.Serialize(Cache, Opts));
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Opts));
        }
    }

    /// <summary>Windows DPAPI (current-user) for secrets at rest.</summary>
    internal static class Dpapi
    {
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }

        public static string Unprotect(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
    }
}
