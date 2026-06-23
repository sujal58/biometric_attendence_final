using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using AttendanceBridge.Config;
using AttendanceBridge.Data;
using AttendanceBridge.Device;
using AttendanceBridge.Logging;

namespace AttendanceBridge.Http
{
    /// <summary>
    /// Small HttpListener that serves the school-facing page and a tiny JSON API:
    ///   GET  /             -> the web UI (Fetch button + status)
    ///   GET  /api/health   -> liveness + tenant/device identity
    ///   GET  /api/status   -> last pull, last punch, punch counts
    ///   POST /api/fetch    -> trigger an immediate pull, returns the result
    ///
    /// Bind to http://localhost:8080/ for this PC only, or http://+:8080/ for the
    /// whole school LAN (the latter needs a one-time netsh urlacl - see README).
    /// </summary>
    public sealed class LocalHttpServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly BridgeConfig _cfg;
        private readonly PunchRepository _repo;
        private readonly FetchService _fetch;
        private Thread _thread;
        private volatile bool _running;

        public LocalHttpServer(BridgeConfig cfg, PunchRepository repo, FetchService fetch)
        {
            _cfg = cfg;
            _repo = repo;
            _fetch = fetch;
            _listener.Prefixes.Add(cfg.web.url);
        }

        public void Start()
        {
            try
            {
                _listener.Start();
            }
            catch (Exception ex)
            {
                Log.Error("Web UI could not start on " + _cfg.web.url +
                          ". For LAN access use http://+:8080/ and add a urlacl (see README).", ex);
                return;
            }
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
            Log.Info("Web UI listening on " + _cfg.web.url);
        }

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { if (!_running) break; continue; }

                try { Handle(ctx); }
                catch (Exception ex) { Log.Warn("HTTP handler error: " + ex.Message); }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();

            if (path == string.Empty) { ServePage(ctx); return; }

            switch (path)
            {
                case "/api/health":
                    WriteJson(ctx, new { ok = true, tenant = _cfg.tenant.tenantId, device = _cfg.database.deviceId });
                    break;

                case "/api/status":
                    WriteJson(ctx, BuildStatus());
                    break;

                case "/api/fetch":
                    if (ctx.Request.HttpMethod == "POST")
                    {
                        var r = _fetch.Fetch("web ui");
                        WriteJson(ctx, new { ok = r.Ok, read = r.Read, inserted = r.Inserted, message = r.Message });
                    }
                    else
                    {
                        ctx.Response.StatusCode = 405;
                        ctx.Response.Close();
                    }
                    break;

                default:
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    break;
            }
        }

        private object BuildStatus()
        {
            BridgeStatus s;
            try { s = _repo.GetStatus(); }
            catch (Exception ex) { return new { ok = false, error = ex.Message }; }

            return new
            {
                ok = true,
                tenant = _cfg.tenant.tenantId,
                device = _cfg.database.deviceId,
                deviceIp = _cfg.device.ipAddress,
                lastPullAt = Fmt(s.LastPullAt),
                lastPunchAt = Fmt(s.LastPunchAt),
                lastStatus = s.LastStatus,
                totalPunches = s.TotalPunches,
                todayPunches = s.TodayPunches,
            };
        }

        private static string Fmt(DateTime? dt) =>
            dt.HasValue ? dt.Value.ToString("yyyy-MM-dd HH:mm:ss") : null;

        private void ServePage(HttpListenerContext ctx)
        {
            string html = LoadResource("AttendanceBridge.webui.html")
                .Replace("{{TENANT}}", WebUtility.HtmlEncode(_cfg.tenant.tenantId))
                .Replace("{{DEVICE}}", _cfg.database.deviceId.ToString());

            byte[] bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static string LoadResource(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(name))
            {
                if (s == null) return "<h1>AttendanceBridge</h1><p>UI resource missing.</p>";
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        private void WriteJson(HttpListenerContext ctx, object obj)
        {
            string json = new JavaScriptSerializer().Serialize(obj);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        public void Stop()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
        }

        public void Dispose()
        {
            Stop();
            try { _listener.Close(); } catch { }
        }
    }
}
