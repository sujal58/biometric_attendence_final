using System;

namespace AttendanceBridge.Api
{
    // DTOs for the Shikzya bridge API (see docs/phase4-fleet.md). JSON uses
    // camelCase; the server derives tenant + site from the bearer site token.

    public sealed class DeviceListResponse
    {
        public DeviceDef[] Devices { get; set; }
    }

    /// <summary>A device this site must service, as configured centrally in Shikzya.</summary>
    public sealed class DeviceDef
    {
        public int DeviceId { get; set; }
        public string Name { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; } = 5005;
        public int MachineNo { get; set; } = 1;
        public int NetPassword { get; set; }
        public int License { get; set; } = 1261;
        public int TimeoutMs { get; set; } = 5000;
        public int Protocol { get; set; }
        public string[] PullTimes { get; set; } = Array.Empty<string>();
        public int TimeSyncMaxDriftSeconds { get; set; } = 30;
        public bool Active { get; set; } = true;
    }

    public sealed class PunchDto
    {
        public int EnrollNumber { get; set; }
        public string PunchTime { get; set; }      // "yyyy-MM-ddTHH:mm:ss" (device local)
        public int VerifyMode { get; set; }
        public string VerifyLabel { get; set; }
        public int InOutMode { get; set; }
        public int IoMode { get; set; }
        public int DoorMode { get; set; }
        public int? Temperature { get; set; }
    }

    public sealed class PunchUpload
    {
        public int DeviceId { get; set; }
        public PunchDto[] Punches { get; set; }
    }

    public sealed class UploadResponse
    {
        public int Inserted { get; set; }
    }

    public sealed class CommandListResponse
    {
        public CommandDto[] Commands { get; set; }
    }

    public sealed class CommandDto
    {
        public long Id { get; set; }
        public int DeviceId { get; set; }
    }

    public sealed class CommandResult
    {
        public bool Ok { get; set; }
        public int RecordsRead { get; set; }
        public int RecordsInserted { get; set; }
        public string Message { get; set; }
    }

    public sealed class HeartbeatUpload
    {
        public string AgentVersion { get; set; }
        public HeartbeatDevice[] Devices { get; set; }
    }

    public sealed class HeartbeatDevice
    {
        public int DeviceId { get; set; }
        public string LastPullAt { get; set; }
        public string LastStatus { get; set; }
    }
}
