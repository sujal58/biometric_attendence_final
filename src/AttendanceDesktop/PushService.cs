using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceBridge.Api;
using AttendanceBridge.Data;
using AttendanceDesktop.Data;

namespace AttendanceDesktop
{
    public sealed class PushResult
    {
        public bool Ok = true;
        public int Inserted;
        public string Message = "";
    }

    /// <summary>Routes fetched punches to MySQL and/or the Shikzya API per config.</summary>
    public sealed class PushService
    {
        private readonly DesktopConfig _cfg;
        private PunchDbWriter _db;

        public PushService(DesktopConfig cfg) { _cfg = cfg; }

        public async Task<PushResult> Push(int deviceId, List<PunchRecord> records)
        {
            var result = new PushResult();
            if (records == null || records.Count == 0) { result.Message = "0 records"; return result; }

            var parts = new List<string>();

            if (_cfg.Push == PushTarget.Mysql || _cfg.Push == PushTarget.Both)
            {
                try
                {
                    _db ??= new PunchDbWriter(_cfg.Db.ConnectionString);
                    int n = await Task.Run(() => _db.UpsertBatch(_cfg.TenantId, _cfg.SiteId, deviceId, records));
                    result.Inserted = n;
                    parts.Add("MySQL " + n + " new");
                }
                catch (Exception ex) { result.Ok = false; parts.Add("MySQL failed: " + ex.Message); }
            }

            if (_cfg.Push == PushTarget.Api || _cfg.Push == PushTarget.Both)
            {
                try
                {
                    var api = new ApiClient(_cfg.Api.ApiBaseUrl, _cfg.Api.SiteToken);
                    var upload = new PunchUpload { DeviceId = deviceId, Punches = records.Select(ToDto).ToArray() };
                    int n = await api.UploadPunchesAsync(upload, CancellationToken.None);
                    if (_cfg.Push == PushTarget.Api) result.Inserted = n;
                    parts.Add("API " + n + " new");
                }
                catch (Exception ex) { result.Ok = false; parts.Add("API failed: " + ex.Message); }
            }

            result.Message = string.Join(", ", parts);
            return result;
        }

        private static PunchDto ToDto(PunchRecord r) => new PunchDto
        {
            EnrollNumber = r.EnrollNumber,
            PunchTime = r.PunchTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            VerifyMode = r.VerifyMode,
            VerifyLabel = r.VerifyLabel,
            InOutMode = r.InOutMode,
            IoMode = r.IoMode,
            DoorMode = r.DoorMode,
            Temperature = r.Temperature,
        };
    }
}
