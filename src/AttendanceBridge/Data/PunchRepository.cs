using System;
using System.Collections.Generic;
using MySqlConnector;
using AttendanceBridge.Logging;

namespace AttendanceBridge.Data
{
    /// <summary>
    /// Writes punches into MySQL. Inserts are idempotent: a UNIQUE index on
    /// bio_punch.dedup_key plus INSERT ... ON DUPLICATE KEY UPDATE means
    /// re-pulling the whole log never creates duplicate rows. The PHP school
    /// system reads bio_punch (joined to bio_enroll_map).
    /// </summary>
    public sealed class PunchRepository
    {
        private readonly string _connectionString;
        private readonly int _deviceId;

        public PunchRepository(string connectionString, int deviceId)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("A MySQL connection string is required.", nameof(connectionString));
            _connectionString = connectionString;
            _deviceId = deviceId;
        }

        /// <summary>Quick connectivity check; throws if the DB is unreachable.</summary>
        public void EnsureReachable()
        {
            using (var conn = new MySqlConnection(_connectionString))
                conn.Open();
        }

        /// <summary>
        /// Upserts a batch of punches in a single transaction. Returns the
        /// number of NEW rows actually inserted (duplicates are no-ops).
        /// </summary>
        public int UpsertBatch(IReadOnlyList<PunchRecord> records)
        {
            if (records == null || records.Count == 0)
                return 0;

            int inserted = 0;
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    const string sql =
                        "INSERT INTO bio_punch " +
                        "(device_id, enroll_number, punch_time, verify_mode, in_out_mode, dedup_key, raw_temperature) " +
                        "VALUES (@dev, @enroll, @time, @verify, @io, @key, @temp) " +
                        "ON DUPLICATE KEY UPDATE id = id;"; // no-op on repeat

                    foreach (var r in records)
                    {
                        using (var cmd = new MySqlCommand(sql, conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@dev", _deviceId);
                            cmd.Parameters.AddWithValue("@enroll", r.EnrollNumber);
                            cmd.Parameters.AddWithValue("@time", r.PunchTime);
                            cmd.Parameters.AddWithValue("@verify", r.VerifyMode);
                            cmd.Parameters.AddWithValue("@io", r.InOutMode);
                            cmd.Parameters.AddWithValue("@key",
                                Dedup.Key(_deviceId, r.EnrollNumber, r.PunchTime, r.InOutMode));
                            cmd.Parameters.AddWithValue("@temp",
                                (object)r.Temperature ?? DBNull.Value);

                            // MySQL returns 1 for a fresh insert, 0 for an ignored duplicate.
                            if (cmd.ExecuteNonQuery() == 1)
                                inserted++;
                        }
                    }
                    tx.Commit();
                }
            }
            return inserted;
        }

        /// <summary>Records the result of a pull on the device row (for the admin health view).</summary>
        public void UpdateDeviceCursor(DateTime pulledAt, DateTime? lastPunch, string status)
        {
            const string sql =
                "INSERT INTO bio_device (device_id, name, ip_address, last_pull_at, last_punch_at, last_status) " +
                "VALUES (@dev, CONCAT('device-', @dev), '', @pulled, @punch, @status) " +
                "ON DUPLICATE KEY UPDATE last_pull_at = @pulled, " +
                "last_punch_at = GREATEST(COALESCE(last_punch_at, @punch), COALESCE(@punch, last_punch_at)), " +
                "last_status = @status;";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@dev", _deviceId);
                    cmd.Parameters.AddWithValue("@pulled", pulledAt);
                    cmd.Parameters.AddWithValue("@punch", (object)lastPunch ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@status", Trim(status, 255));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void WriteBridgeLog(string level, string @event, string message)
        {
            try
            {
                const string sql =
                    "INSERT INTO bio_bridge_log (level, event, message) VALUES (@lvl, @evt, @msg);";
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@lvl", level);
                        cmd.Parameters.AddWithValue("@evt", Trim(@event, 64));
                        cmd.Parameters.AddWithValue("@msg", (object)message ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                // The DB log is best-effort; the file log is the source of truth.
                Log.Warn("Could not write bio_bridge_log: " + ex.Message);
            }
        }

        private static string Trim(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
    }
}
