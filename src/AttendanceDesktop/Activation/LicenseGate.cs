using System;
using System.Threading;
using System.Threading.Tasks;
using AttendanceDesktop.Api;

namespace AttendanceDesktop.Activation
{
    public sealed class GateResult
    {
        public bool Allowed;
        public string Message;
    }

    /// <summary>
    /// Validates the license online against Shikzya and updates the cached result.
    /// Falls back to the cache within a 7-day offline grace. Blocks on
    /// revoked/expired/unknown.
    /// </summary>
    public static class LicenseGate
    {
        public const string AppVersion = "1.0.0";
        private const int GraceDays = 7;

        public static async Task<GateResult> CheckAsync(DesktopConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(cfg.LicenseKey))
                return new GateResult { Allowed = false, Message = "Enter your school's license key to activate." };

            ActivationResponse r;
            try
            {
                r = await new DesktopApiClient(cfg.ApiBaseUrl, cfg.LicenseKey).ActivateAsync(AppVersion, CancellationToken.None);
            }
            catch
            {
                r = new ActivationResponse { Valid = false, Message = "network error" };
            }

            if (r.Valid)
            {
                cfg.Cache = new LicenseCache
                {
                    Valid = true,
                    TenantId = r.TenantId ?? "",
                    ExpiresAt = r.ExpiresAt ?? "",
                    LastCheckAt = DateTime.UtcNow.ToString("o"),
                    AllowedDevices = r.Devices ?? new System.Collections.Generic.List<DeviceRef>(),
                };
                cfg.Save();
                return new GateResult { Allowed = true, Message = "Activated for " + cfg.Cache.TenantId };
            }

            // Server reachable and said NOT valid -> hard block (clear cache).
            // Distinguish "couldn't reach server" (offline grace) from an explicit rejection.
            bool networkProblem = string.Equals(r.Message, "network error", StringComparison.OrdinalIgnoreCase)
                                  || (r.Message != null && r.Message.StartsWith("HTTP 5"));
            if (networkProblem && CacheUsable(cfg.Cache))
                return new GateResult { Allowed = true, Message = "Offline — using cached license (grace)." };

            if (!networkProblem)
            {
                cfg.Cache = new LicenseCache { Valid = false };
                cfg.Save();
            }
            return new GateResult { Allowed = false, Message = string.IsNullOrEmpty(r.Message) ? "License is not valid." : r.Message };
        }

        private static bool CacheUsable(LicenseCache c)
        {
            if (c == null || !c.Valid) return false;
            if (!string.IsNullOrEmpty(c.ExpiresAt) && DateTime.TryParse(c.ExpiresAt, out var exp)
                && DateTime.UtcNow > exp.ToUniversalTime()) return false;
            if (!DateTime.TryParse(c.LastCheckAt, out var last)) return false;
            return (DateTime.UtcNow - last.ToUniversalTime()).TotalDays <= GraceDays;
        }
    }
}
