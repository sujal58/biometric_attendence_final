using System;
using System.Collections.Generic;
using AttendanceBridge.Api;
using AttendanceBridge.Data;
using AttendanceBridge.Device;

namespace AttendanceBridge.Agent
{
    public sealed class PullResult
    {
        public bool Ok;
        public string Message;
        public List<PunchRecord> Records;
    }

    /// <summary>
    /// Pulls attendance from a single device: connect -> time sync -> read log ->
    /// disconnect. One device at a time (the loop is sequential). Reuses the
    /// proven device layer from Phases 1-2.
    /// </summary>
    public sealed class DeviceWorker
    {
        public PullResult Pull(DeviceDef d)
        {
            var conn = new DeviceConnection(Map(d));
            if (!conn.Connect())
            {
                conn.Dispose();
                return new PullResult { Ok = false, Message = "device connect failed" };
            }

            try
            {
                new TimeSyncModule(conn).SyncIfDrift(d.TimeSyncMaxDriftSeconds);
                var records = new LogPoller(conn).Read(0); // readMark 0 = all; server de-duplicates
                return new PullResult { Ok = true, Records = records, Message = "read " + records.Count };
            }
            catch (Exception ex)
            {
                return new PullResult { Ok = false, Message = ex.Message };
            }
            finally
            {
                conn.Disconnect();
                conn.Dispose();
            }
        }

        private static DeviceParams Map(DeviceDef d) => new DeviceParams
        {
            MachineNo = d.MachineNo,
            IpAddress = d.Ip,
            NetPort = d.Port,
            TimeoutMs = d.TimeoutMs,
            ProtocolType = d.Protocol,
            NetPassword = d.NetPassword,
            License = d.License,
        };
    }
}
