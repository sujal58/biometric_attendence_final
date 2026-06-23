using System;
using AttendanceBridge.Interop;

namespace AttendanceBridge.Data
{
    /// <summary>
    /// One attendance transaction read from the device's general log.
    /// verify/in-out codes are stored raw (lossless); they are translated to
    /// labels at display time via the <see cref="VerifyMode"/> / <see cref="IoMode"/>
    /// enums.
    /// </summary>
    public sealed class PunchRecord
    {
        public int EnrollNumber { get; set; }
        public int VerifyMode { get; set; }
        public int InOutMode { get; set; }
        public DateTime PunchTime { get; set; }

        /// <summary>Body temperature (tenths of a degree on supporting firmware), else null.</summary>
        public int? Temperature { get; set; }

        public string VerifyModeName =>
            Enum.IsDefined(typeof(VerifyMode), VerifyMode)
                ? ((VerifyMode)VerifyMode).ToString()
                : VerifyMode.ToString();

        public string InOutModeName =>
            Enum.IsDefined(typeof(IoMode), InOutMode)
                ? ((IoMode)InOutMode).ToString()
                : InOutMode.ToString();

        public override string ToString() =>
            string.Format("user={0} {1:yyyy-MM-dd HH:mm:ss} verify={2} io={3}{4}",
                EnrollNumber, PunchTime, VerifyModeName, InOutModeName,
                Temperature.HasValue ? " temp=" + (Temperature.Value / 10.0).ToString("0.0") : "");
    }
}
