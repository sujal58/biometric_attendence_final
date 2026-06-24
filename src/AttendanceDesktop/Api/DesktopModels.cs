using System.Collections.Generic;
using AttendanceBridge.Api; // reuse PunchDto

namespace AttendanceDesktop.Api
{
    // DTOs for the Shikzya endpoints the desktop tool calls. The license key is the
    // bearer token; the server derives the tenant from it (never trusted from body).

    public sealed class ActivationResponse
    {
        public bool Valid { get; set; }
        public string TenantId { get; set; }
        public string ExpiresAt { get; set; }   // ISO date/time, or null/"" for none
        public string Message { get; set; }
        public List<DeviceRef> Devices { get; set; } = new List<DeviceRef>();
    }

    public sealed class UserDto
    {
        public int EnrollNumber { get; set; }
        public string Name { get; set; }
        public int Privilege { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class PunchUploadDto
    {
        public string DeviceSerial { get; set; }
        public string DeviceMac { get; set; }
        public List<PunchDto> Punches { get; set; } = new List<PunchDto>();
    }

    public sealed class UserUploadDto
    {
        public string DeviceSerial { get; set; }
        public List<UserDto> Users { get; set; } = new List<UserDto>();
    }

    public sealed class UploadResult
    {
        public int Inserted { get; set; }
    }
}
