using System;
using AttendanceBridge.Interop;

namespace AttendanceBridge.Data
{
    /// <summary>
    /// One attendance transaction read from the device's general log.
    ///
    /// The raw verify / in-out codes are stored losslessly, alongside decoded
    /// helpers (verify label, io mode, door mode) computed via
    /// <see cref="FkLogDecoder"/> so the PHP side does not have to repeat the
    /// bit-unpacking.
    /// </summary>
    public sealed class PunchRecord
    {
        public int EnrollNumber { get; set; }
        public int VerifyMode { get; set; }     // raw (may be bit-packed, e.g. 0x10000000)
        public int InOutMode { get; set; }       // raw (low byte = io, high bytes = door)
        public DateTime PunchTime { get; set; }

        /// <summary>Body temperature (tenths of a degree on supporting firmware), else null.</summary>
        public int? Temperature { get; set; }

        // Decoded views (populated by the poller).
        public string VerifyLabel { get; set; }
        public int IoMode { get; set; }
        public int DoorMode { get; set; }

        /// <summary>Fills the decoded fields from the raw verify / in-out codes.</summary>
        public void Decode()
        {
            VerifyLabel = FkLogDecoder.VerifyLabel(VerifyMode);
            FkLogDecoder.DecodeIo(InOutMode, out int io, out int door);
            IoMode = io;
            DoorMode = door;
        }

        public override string ToString() =>
            string.Format("user={0} {1:yyyy-MM-dd HH:mm:ss} verify={2} io={3} door={4}{5}",
                EnrollNumber, PunchTime, VerifyLabel ?? VerifyMode.ToString(), IoMode, DoorMode,
                Temperature.HasValue ? " temp=" + (Temperature.Value / 10.0).ToString("0.0") : "");
    }
}
