using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AttendanceBridge.Api;

namespace AttendanceDesktop.Api
{
    /// <summary>
    /// Calls the authenticated Shikzya endpoints. The license key is the bearer
    /// token; no DB credentials are ever held by the client.
    /// </summary>
    public sealed class DesktopApiClient
    {
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        public DesktopApiClient(string baseUrl, string licenseKey)
        {
            _http = new HttpClient { BaseAddress = new Uri((baseUrl ?? "").TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
            if (!string.IsNullOrEmpty(licenseKey))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", licenseKey);
            _http.DefaultRequestHeaders.Add("User-Agent", "ShikzyaDeviceTool/1.0");
        }

        /// <summary>Validates the license + returns the school's allowed devices. Never throws.</summary>
        public async Task<ActivationResponse> ActivateAsync(string appVersion, CancellationToken ct)
        {
            try
            {
                var resp = await Post("api/bridge/v1/activate", new { appVersion }, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return new ActivationResponse { Valid = false, Message = "HTTP " + (int)resp.StatusCode + ": " + Trunc(body) };
                return JsonSerializer.Deserialize<ActivationResponse>(body, _json) ?? new ActivationResponse { Valid = false, Message = "empty response" };
            }
            catch (Exception ex) { return new ActivationResponse { Valid = false, Message = ex.Message }; }
        }

        public async Task<int> UploadPunchesAsync(string deviceSerial, string deviceMac, List<PunchDto> punches, CancellationToken ct)
        {
            var resp = await Post("api/bridge/v1/punches",
                new PunchUploadDto { DeviceSerial = deviceSerial, DeviceMac = deviceMac, Punches = punches }, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<UploadResult>(body, _json)?.Inserted ?? 0;
        }

        public async Task<int> UploadUsersAsync(string deviceSerial, List<UserDto> users, CancellationToken ct)
        {
            var resp = await Post("api/bridge/v1/users",
                new UserUploadDto { DeviceSerial = deviceSerial, Users = users }, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<UploadResult>(body, _json)?.Inserted ?? 0;
        }

        private Task<HttpResponseMessage> Post(string url, object body, CancellationToken ct)
        {
            var content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
            return _http.PostAsync(url, content, ct);
        }

        private static string Trunc(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 200 ? s.Substring(0, 200) : s);
    }
}
