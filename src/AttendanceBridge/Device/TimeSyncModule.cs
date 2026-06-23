using System;
using AttendanceBridge.Interop;
using AttendanceBridge.Logging;

namespace AttendanceBridge.Device
{
    /// <summary>
    /// Reads the device clock and, when it drifts beyond a threshold, sets it
    /// to the host's local time. The host clock itself should be NTP-synced.
    /// </summary>
    public sealed class TimeSyncModule
    {
        private readonly DeviceConnection _conn;

        public TimeSyncModule(DeviceConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        public DateTime ReadDeviceTime()
        {
            _conn.EnsureConnected();
            DateTime deviceTime = DateTime.Now;
            using (_conn.EnableScope())
            {
                int rc = FkAttend.FK_GetDeviceTime(_conn.Handle, ref deviceTime);
                if (rc != (int)FkError.Success)
                    throw new InvalidOperationException(
                        "FK_GetDeviceTime returned " + DeviceConnection.Describe(rc));
            }
            return deviceTime;
        }

        public bool SetDeviceTime(DateTime when)
        {
            _conn.EnsureConnected();
            using (_conn.EnableScope())
            {
                int rc = FkAttend.FK_SetDeviceTime(_conn.Handle, when);
                if (rc != (int)FkError.Success)
                {
                    Log.Error("FK_SetDeviceTime returned " + DeviceConnection.Describe(rc));
                    return false;
                }
            }
            Log.Info("Device clock set to " + when.ToString("yyyy-MM-dd HH:mm:ss"));
            return true;
        }

        /// <summary>
        /// Compares device time to host time and corrects only if the absolute
        /// drift exceeds <paramref name="maxDriftSeconds"/>. Returns true if a
        /// correction was applied.
        /// </summary>
        public bool SyncIfDrift(int maxDriftSeconds)
        {
            var host = DateTime.Now;
            var device = ReadDeviceTime();
            var drift = (host - device).TotalSeconds;

            Log.Info(string.Format(
                "Device time {0:yyyy-MM-dd HH:mm:ss}, host time {1:yyyy-MM-dd HH:mm:ss}, drift {2:0.#}s.",
                device, host, drift));

            if (Math.Abs(drift) <= maxDriftSeconds)
            {
                Log.Info("Drift within tolerance (" + maxDriftSeconds + "s); no correction needed.");
                return false;
            }

            Log.Warn("Drift exceeds tolerance; correcting device clock.");
            return SetDeviceTime(DateTime.Now);
        }
    }
}
