using System;
using System.Collections.Generic;
using AttendanceBridge.Data;
using AttendanceBridge.Interop;
using AttendanceBridge.Logging;

namespace AttendanceBridge.Device
{
    /// <summary>
    /// Reads attendance (general) log records off the device.
    ///
    /// Mirrors the vendor sample (frmLog.cs:911-952): load the dataset, then
    /// loop reading records until RUNERR_DATAARRAY_END (-7). Each iteration
    /// first tries FK_GetTemperatureLogData and falls back to
    /// FK_GetGeneralLogData when the firmware reports RUNERR_NOSUPPORT, so the
    /// same code works on both temperature and non-temperature devices.
    /// </summary>
    public sealed class LogPoller
    {
        private readonly DeviceConnection _conn;

        public LogPoller(DeviceConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        /// <param name="readMark">0 = read all logs (recommended; dedup happens
        /// in the DB), 1 = only records not yet marked read.</param>
        /// <param name="verbose">log each decoded record as it is read.</param>
        public List<PunchRecord> Read(int readMark, bool verbose = false)
        {
            _conn.EnsureConnected();
            var records = new List<PunchRecord>();

            using (_conn.EnableScope())
            {
                int rc = FkAttend.FK_LoadGeneralLogData(_conn.Handle, readMark);
                if (rc != (int)FkError.Success)
                {
                    // No data is a normal, empty result - not an error.
                    if (rc == (int)FkError.DataArrayNone || rc == (int)FkError.DataArrayEnd)
                    {
                        Log.Info("No attendance logs to read.");
                        return records;
                    }
                    throw new InvalidOperationException(
                        "FK_LoadGeneralLogData returned " + DeviceConnection.Describe(rc));
                }

                while (true)
                {
                    int enroll = 0, verify = 0, inOut = 0, temp = 0;
                    DateTime when = DateTime.MinValue;
                    bool hasTemp = true;

                    int r = FkAttend.FK_GetTemperatureLogData(
                        _conn.Handle, ref enroll, ref verify, ref inOut, ref when, ref temp);

                    if (r == (int)FkError.NoSupport)
                    {
                        hasTemp = false;
                        r = FkAttend.FK_GetGeneralLogData(
                            _conn.Handle, ref enroll, ref verify, ref inOut, ref when);
                    }

                    if (r != (int)FkError.Success)
                    {
                        if (r == (int)FkError.DataArrayEnd)
                            break; // normal end of the dataset
                        throw new InvalidOperationException(
                            "Reading attendance log failed with " + DeviceConnection.Describe(r));
                    }

                    var record = new PunchRecord
                    {
                        EnrollNumber = enroll,
                        VerifyMode = verify,
                        InOutMode = inOut,
                        PunchTime = when,
                        Temperature = hasTemp ? (int?)temp : null,
                    };
                    record.Decode();
                    records.Add(record);

                    if (verbose)
                        Log.Info("  " + record);
                }
            }

            Log.Info("Read " + records.Count + " attendance record(s) from device.");
            return records;
        }

        /// <summary>
        /// Destructive: clears the device's general log. Only call this from an
        /// explicit admin action AFTER the records are safely persisted - never
        /// as part of routine polling.
        /// </summary>
        public bool EmptyLog()
        {
            _conn.EnsureConnected();
            using (_conn.EnableScope())
            {
                int rc = FkAttend.FK_EmptyGeneralLogData(_conn.Handle);
                if (rc != (int)FkError.Success)
                {
                    Log.Error("FK_EmptyGeneralLogData returned " + DeviceConnection.Describe(rc));
                    return false;
                }
            }
            Log.Warn("Device attendance log was cleared.");
            return true;
        }
    }
}
