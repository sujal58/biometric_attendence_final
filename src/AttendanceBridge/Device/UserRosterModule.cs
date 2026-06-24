using System;
using System.Collections.Generic;
using AttendanceBridge.Interop;
using AttendanceBridge.Logging;

namespace AttendanceBridge.Device
{
    /// <summary>One enrolled user on the device.</summary>
    public sealed class DeviceUser
    {
        public int EnrollNumber { get; set; }
        public string Name { get; set; }
        public int Privilege { get; set; }
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// Reads the enrolled-user roster (enroll number + name) from the device:
    /// FK_ReadAllUserID -> loop FK_GetAllUserID (one row per credential, so we
    /// de-duplicate by enroll number) -> FK_GetUserName for each distinct user.
    /// </summary>
    public sealed class UserRosterModule
    {
        private readonly DeviceConnection _conn;

        public UserRosterModule(DeviceConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        public List<DeviceUser> ReadUsers()
        {
            _conn.EnsureConnected();
            var byEnroll = new Dictionary<int, DeviceUser>();

            using (_conn.EnableScope())
            {
                int rc = FkAttend.FK_ReadAllUserID(_conn.Handle);
                if (rc != (int)FkError.Success)
                {
                    if (rc == (int)FkError.DataArrayNone || rc == (int)FkError.DataArrayEnd)
                        return new List<DeviceUser>();
                    throw new InvalidOperationException("FK_ReadAllUserID returned " + DeviceConnection.Describe(rc));
                }

                while (true)
                {
                    int enroll = 0, backup = 0, priv = 0, enable = 0;
                    int r = FkAttend.FK_GetAllUserID(_conn.Handle, ref enroll, ref backup, ref priv, ref enable);
                    if (r != (int)FkError.Success)
                    {
                        if (r == (int)FkError.DataArrayEnd) break;
                        throw new InvalidOperationException("FK_GetAllUserID returned " + DeviceConnection.Describe(r));
                    }
                    if (!byEnroll.ContainsKey(enroll))
                        byEnroll[enroll] = new DeviceUser { EnrollNumber = enroll, Privilege = priv, Enabled = enable != 0 };
                }

                foreach (var u in byEnroll.Values)
                {
                    string name = new string('\0', 256); // pre-size the LPStr buffer
                    int rn = FkAttend.FK_GetUserName(_conn.Handle, u.EnrollNumber, ref name);
                    u.Name = rn == (int)FkError.Success ? (name ?? "").Trim('\0', ' ') : "";
                }
            }

            Log.Info("Read " + byEnroll.Count + " user(s) from device.");
            return new List<DeviceUser>(byEnroll.Values);
        }
    }
}
