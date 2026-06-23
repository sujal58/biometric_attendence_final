using System;
using System.Security.Cryptography;
using System.Text;

namespace AttendanceBridge.Data
{
    /// <summary>
    /// Builds the natural de-duplication key for a punch. Because the bridge
    /// re-reads the whole log (readMark=0) every poll, the key makes inserts
    /// idempotent: the same physical punch always maps to the same 40-char
    /// SHA-1 hex, and the unique index on bio_punch.dedup_key drops repeats.
    ///
    /// verify_mode is deliberately NOT part of the key: a single accepted
    /// verification produces one log record, and the same event must hash the
    /// same regardless of how the device labelled the verification method.
    /// </summary>
    public static class Dedup
    {
        public static string Key(int deviceId, int enrollNumber, DateTime punchTime, int inOutMode)
        {
            // Stable, culture-invariant components joined with a separator that
            // cannot appear inside any of them.
            string raw = string.Join("|",
                deviceId.ToString(),
                enrollNumber.ToString(),
                punchTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                inOutMode.ToString());

            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
