using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceBridge.Api;
using AttendanceBridge.Data;
using AttendanceBridge.Device;
using AttendanceDesktop.Data;

namespace AttendanceDesktop
{
    public sealed class PushResult
    {
        public bool Ok = true;
        public int Inserted;
        public string Message = "";
    }

    /// <summary>
    /// Routes fetched data to MySQL and/or the Shikzya API per config. MySQL gets
    /// punches + the user roster (bio_user) + a device status row (bio_device);
    /// the API gets punches.
    /// </summary>
    public sealed class PushService
    {
        private readonly DesktopConfig _cfg;
        private PunchDbWriter _db;

        public PushService(DesktopConfig cfg) { _cfg = cfg; }

        public async Task<PushResult> Push(DesktopConfig.DeviceEntry device, List<PunchRecord> records, List<DeviceUser> users)
        {
            var result = new PushResult();
            var parts = new List<string>();
            records ??= new List<PunchRecord>();
            users ??= new List<DeviceUser>();

            if (_cfg.Push == PushTarget.Mysql || _cfg.Push == PushTarget.Both)
            {
                try
                {
                    _db ??= new PunchDbWriter(_cfg.Db.ConnectionString);
                    int ins = await Task.Run(() =>
                    {
                        int n = _db.UpsertBatch(_cfg.TenantId, _cfg.SiteId, device.DeviceId, records);
                        _db.UpsertUsers(_cfg.TenantId, device.DeviceId, users);
                        _db.UpsertDevice(_cfg.TenantId, _cfg.SiteId, device,
                            "read " + records.Count + ", new " + n + ", users " + users.Count);
                        return n;
                    });
                    result.Inserted = ins;
                    parts.Add("MySQL " + ins + " new" + (users.Count > 0 ? ", " + users.Count + " users" : ""));
                }
                catch (Exception ex) { result.Ok = false; parts.Add("MySQL failed: " + ex.Message); }
            }

            if (_cfg.Push == PushTarget.Api || _cfg.Push == PushTarget.Both)
            {
                try
                {
                    var api = new ApiClient(_cfg.Api.ApiBaseUrl, _cfg.Api.SiteToken);
                    var upload = new PunchUpload { DeviceId = device.DeviceId, Punches = records.Select(ToDto).ToArray() };
                    int n = await api.UploadPunchesAsync(upload, CancellationToken.None);
                    if (_cfg.Push == PushTarget.Api) result.Inserted = n;
                    parts.Add("API " + n + " new");
                }
                catch (Exception ex) { result.Ok = false; parts.Add("API failed: " + ex.Message); }
            }

            result.Message = parts.Count > 0 ? string.Join(", ", parts) : "nothing to push";
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
