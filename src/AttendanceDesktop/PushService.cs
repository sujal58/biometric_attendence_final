using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceBridge.Api;
using AttendanceBridge.Data;
using AttendanceBridge.Device;
using AttendanceDesktop.Api;

namespace AttendanceDesktop
{
    public sealed class PushResult
    {
        public bool Ok = true;
        public int Inserted;
        public string Message = "";
    }

    /// <summary>
    /// Pushes punches + the user roster to the authenticated Shikzya endpoints
    /// (bearer = license key). No DB credentials on the client.
    /// </summary>
    public sealed class PushService
    {
        private readonly DesktopConfig _cfg;

        public PushService(DesktopConfig cfg) { _cfg = cfg; }

        public async Task<PushResult> Push(string deviceSerial, string deviceMac, List<PunchRecord> records, List<DeviceUser> users)
        {
            var result = new PushResult();
            records ??= new List<PunchRecord>();
            users ??= new List<DeviceUser>();

            try
            {
                var api = new DesktopApiClient(_cfg.ApiBaseUrl, _cfg.LicenseKey);
                int ins = await api.UploadPunchesAsync(deviceSerial, deviceMac, records.Select(ToDto).ToList(), CancellationToken.None);
                result.Inserted = ins;

                string usersMsg = "";
                if (users.Count > 0)
                {
                    try { await api.UploadUsersAsync(deviceSerial, users.Select(ToUserDto).ToList(), CancellationToken.None); usersMsg = ", " + users.Count + " users"; }
                    catch (Exception ex) { usersMsg = ", users failed (" + ex.Message + ")"; }
                }
                result.Message = "uploaded " + ins + " new" + usersMsg;
            }
            catch (Exception ex) { result.Ok = false; result.Message = "upload failed: " + ex.Message; }

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

        private static UserDto ToUserDto(DeviceUser u) => new UserDto
        {
            EnrollNumber = u.EnrollNumber,
            Name = u.Name,
            Privilege = u.Privilege,
            Enabled = u.Enabled,
        };
    }
}
