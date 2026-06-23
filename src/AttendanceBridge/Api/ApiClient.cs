using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AttendanceBridge.Api
{
    /// <summary>
    /// Talks to the Shikzya bridge API over HTTPS. The site token authenticates
    /// the agent; the server derives tenant + site from it. Outbound only.
    /// </summary>
    public sealed class ApiClient
    {
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        public ApiClient(string baseUrl, string token)
        {
            _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _http.DefaultRequestHeaders.Add("User-Agent", "AttendanceBridge/1.0");
        }

        public async Task<DeviceDef[]> GetDevicesAsync(CancellationToken ct)
        {
            var resp = await _http.GetAsync("api/bridge/v1/devices", ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<DeviceListResponse>(body, _json)?.Devices ?? Array.Empty<DeviceDef>();
        }

        /// <summary>Returns rows actually inserted (idempotent server-side).</summary>
        public async Task<int> UploadPunchesAsync(PunchUpload upload, CancellationToken ct)
        {
            var resp = await PostJsonAsync("api/bridge/v1/punches", upload, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<UploadResponse>(body, _json)?.Inserted ?? 0;
        }

        public async Task<CommandDto[]> GetCommandsAsync(CancellationToken ct)
        {
            var resp = await _http.GetAsync("api/bridge/v1/commands", ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<CommandListResponse>(body, _json)?.Commands ?? Array.Empty<CommandDto>();
        }

        public async Task PostCommandResultAsync(long id, CommandResult result, CancellationToken ct)
        {
            var resp = await PostJsonAsync("api/bridge/v1/commands/" + id + "/result", result, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
        }

        public async Task PostHeartbeatAsync(HeartbeatUpload hb, CancellationToken ct)
        {
            var resp = await PostJsonAsync("api/bridge/v1/heartbeat", hb, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
        }

        private Task<HttpResponseMessage> PostJsonAsync(string url, object body, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(body, _json);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return _http.PostAsync(url, content, ct);
        }
    }
}
