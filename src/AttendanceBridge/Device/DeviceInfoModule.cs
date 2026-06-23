using System;
using System.Text;
using AttendanceBridge.Interop;
using AttendanceBridge.Logging;

namespace AttendanceBridge.Device
{
    /// <summary>Snapshot of identity / config / counters read from the device.</summary>
    public sealed class DeviceInfoSnapshot
    {
        public string SerialNumber;
        public string ProductName;
        public string ProductCode;
        public int MachineNumber;
        public string MacAddress;
        public int Users;
        public int Managers;
        public int Fingerprints;
        public int Faces;
        public int GeneralLogs;
        public int SuperLogs;
    }

    /// <summary>
    /// Reads product data, device configuration and record counters. Used to
    /// confirm the link is healthy and to surface device state to the admin UI.
    /// </summary>
    public sealed class DeviceInfoModule
    {
        private readonly DeviceConnection _conn;

        public DeviceInfoModule(DeviceConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        public DeviceInfoSnapshot Read()
        {
            _conn.EnsureConnected();
            var snap = new DeviceInfoSnapshot();

            using (_conn.EnableScope())
            {
                snap.SerialNumber = GetProduct(ProductInfoIndex.SerialNumber);
                snap.ProductName = GetProduct(ProductInfoIndex.Name);
                snap.ProductCode = GetProduct(ProductInfoIndex.Code);

                snap.MachineNumber = GetInfo(DeviceInfoIndex.MachineNumber);
                snap.MacAddress = ReadMacAddress();

                snap.Users = GetStatus(DeviceStatusIndex.Users);
                snap.Managers = GetStatus(DeviceStatusIndex.Managers);
                snap.Fingerprints = GetStatus(DeviceStatusIndex.Fingerprints);
                snap.Faces = GetStatus(DeviceStatusIndex.Faces);
                snap.GeneralLogs = GetStatus(DeviceStatusIndex.GeneralLogs);
                snap.SuperLogs = GetStatus(DeviceStatusIndex.SuperLogs);
            }

            return snap;
        }

        public void LogSnapshot(DeviceInfoSnapshot s)
        {
            Log.Info("---- Device info ----");
            Log.Info("  Product       : " + s.ProductName + " (" + s.ProductCode + ")");
            Log.Info("  Serial number : " + s.SerialNumber);
            Log.Info("  Machine no.   : " + s.MachineNumber);
            Log.Info("  MAC address   : " + s.MacAddress);
            Log.Info("  Users/Managers: " + s.Users + " / " + s.Managers);
            Log.Info("  Fingerprints  : " + s.Fingerprints);
            Log.Info("  Faces         : " + s.Faces);
            Log.Info("  Attendance logs: " + s.GeneralLogs);
            Log.Info("  Super logs     : " + s.SuperLogs);
            Log.Info("---------------------");
        }

        private string GetProduct(ProductInfoIndex index)
        {
            string value = new string('\0', 256); // pre-size the buffer for the LPStr marshaller
            int rc = FkAttend.FK_GetProductData(_conn.Handle, (int)index, ref value);
            if (rc != (int)FkError.Success)
            {
                Log.Warn("FK_GetProductData(" + index + ") returned " + DeviceConnection.Describe(rc));
                return "(n/a)";
            }
            return (value ?? string.Empty).Trim('\0', ' ');
        }

        private int GetInfo(DeviceInfoIndex index)
        {
            int value = 0;
            int rc = FkAttend.FK_GetDeviceInfo(_conn.Handle, (int)index, ref value);
            if (rc != (int)FkError.Success)
                Log.Warn("FK_GetDeviceInfo(" + index + ") returned " + DeviceConnection.Describe(rc));
            return value;
        }

        private int GetStatus(DeviceStatusIndex index)
        {
            int value = 0;
            int rc = FkAttend.FK_GetDeviceStatus(_conn.Handle, (int)index, ref value);
            if (rc != (int)FkError.Success)
                Log.Warn("FK_GetDeviceStatus(" + index + ") returned " + DeviceConnection.Describe(rc));
            return value;
        }

        // The MAC address is exposed as 6 consecutive device-info indices.
        private string ReadMacAddress()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 6; i++)
            {
                int value = 0;
                int rc = FkAttend.FK_GetDeviceInfo(_conn.Handle, (int)DeviceInfoIndex.MacAddr0 + i, ref value);
                if (rc != (int)FkError.Success)
                    return "(n/a)";
                if (sb.Length > 0) sb.Append(':');
                sb.AppendFormat("{0:X2}", value & 0xFF);
            }
            return sb.ToString();
        }
    }
}
