using System;
using System.Collections.Generic;
using MySqlConnector;
using AttendanceBridge.Logging;

namespace AttendanceBridge.Data
{
    /// <summary>A pending on-demand fetch request raised from Shikzya.</summary>
    public sealed class FetchCommand
    {
        public long Id;
        public string RequestedBy;
    }

    /// <summary>
    /// Writes punches into MySQL and services the on-demand fetch queue. Inserts
    /// are idempotent: a UNIQUE index on bio_punch.dedup_key plus
    /// INSERT ... ON DUPLICATE KEY UPDATE means re-pulling never duplicates rows.
    /// Every row is tagged with tenant_id so a shared multi-tenant database can
    /// hold many schools.
    /// </summary>
    public sealed class PunchRepository
    {
        private readonly string _connectionString;
        private readonly string _tenantId;
        private readonly int _deviceId;

        public PunchRepository(string connectionString, string tenantId, int deviceId)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("A MySQL connection string is required.", nameof(connectionString));
            _connectionString = connectionString;
            _tenantId = tenantId ?? "";
            _deviceId = deviceId;
        }

        public void EnsureReachable()
        {
            using (var conn = new MySqlConnection(_connectionString))
                conn.Open();
        }

        /// <summary>
        /// Upserts a batch of punches in a single transaction. Returns the number
        /// of NEW rows actually inserted (duplicates are no-ops).
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
                        "(tenant_id, device_id, enroll_number, punch_time, verify_mode, verify_label, " +
                        " in_out_mode, io_mode, door_mode, dedup_key, raw_temperature) " +
                        "VALUES (@tenant, @dev, @enroll, @time, @verify, @verifyLabel, " +
                        " @io, @ioMode, @doorMode, @key, @temp) " +
                        "ON DUPLICATE KEY UPDATE id = id;"; // no-op on repeat

                    foreach (var r in records)
                    {
                        using (var cmd = new MySqlCommand(sql, conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@tenant", _tenantId);
                            cmd.Parameters.AddWithValue("@dev", _deviceId);
                            cmd.Parameters.AddWithValue("@enroll", r.EnrollNumber);
                            cmd.Parameters.AddWithValue("@time", r.PunchTime);
                            cmd.Parameters.AddWithValue("@verify", r.VerifyMode);
                            cmd.Parameters.AddWithValue("@verifyLabel", (object)r.VerifyLabel ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@io", r.InOutMode);
                            cmd.Parameters.AddWithValue("@ioMode", r.IoMode);
                            cmd.Parameters.AddWithValue("@doorMode", r.DoorMode);
                            cmd.Parameters.AddWithValue("@key",
                                Dedup.Key(_tenantId, _deviceId, r.EnrollNumber, r.PunchTime, r.InOutMode));
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

        /// <summary>Records the result of a pull on the device row (for the admin/health view).</summary>
        public void UpdateDeviceCursor(DateTime pulledAt, DateTime? lastPunch, string status)
        {
            const string sql =
                "INSERT INTO bio_device (tenant_id, device_id, name, ip_address, last_pull_at, last_punch_at, last_status) " +
                "VALUES (@tenant, @dev, CONCAT('device-', @dev), '', @pulled, @punch, @status) " +
                "ON DUPLICATE KEY UPDATE last_pull_at = @pulled, " +
                "last_punch_at = GREATEST(COALESCE(last_punch_at, @punch), COALESCE(@punch, last_punch_at)), " +
                "last_status = @status;";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@tenant", _tenantId);
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
                    "INSERT INTO bio_bridge_log (tenant_id, level, event, message) " +
                    "VALUES (@tenant, @lvl, @evt, @msg);";
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@tenant", _tenantId);
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

        // ---- On-demand fetch queue (bio_fetch_command) ---------------------

        /// <summary>
        /// Atomically claims the oldest pending fetch command for this tenant/device
        /// (marks it 'running'). Returns null if there is nothing to do.
        /// </summary>
        public FetchCommand ClaimNextCommand()
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    long id = -1;
                    string requestedBy = null;

                    const string select =
                        "SELECT id, requested_by FROM bio_fetch_command " +
                        "WHERE tenant_id = @tenant AND device_id = @dev AND status = 'pending' " +
                        "ORDER BY id LIMIT 1 FOR UPDATE;";
                    using (var cmd = new MySqlCommand(select, conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@tenant", _tenantId);
                        cmd.Parameters.AddWithValue("@dev", _deviceId);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                id = r.GetInt64(0);
                                requestedBy = r.IsDBNull(1) ? null : r.GetString(1);
                            }
                        }
                    }

                    if (id < 0)
                    {
                        tx.Commit();
                        return null;
                    }

                    const string claim =
                        "UPDATE bio_fetch_command SET status = 'running', started_at = NOW() WHERE id = @id;";
                    using (var cmd = new MySqlCommand(claim, conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                    return new FetchCommand { Id = id, RequestedBy = requestedBy };
                }
            }
        }

        public void CompleteCommand(long id, bool ok, int recordsRead, int recordsInserted, string message)
        {
            const string sql =
                "UPDATE bio_fetch_command SET status = @status, finished_at = NOW(), " +
                "records_read = @read, records_inserted = @ins, result_message = @msg WHERE id = @id;";
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@status", ok ? "done" : "error");
                    cmd.Parameters.AddWithValue("@read", recordsRead);
                    cmd.Parameters.AddWithValue("@ins", recordsInserted);
                    cmd.Parameters.AddWithValue("@msg", Trim(message, 255));
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static string Trim(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
    }
}
