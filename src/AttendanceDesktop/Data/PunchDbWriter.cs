using System;
using System.Collections.Generic;
using MySqlConnector;
using AttendanceBridge.Data;

namespace AttendanceDesktop.Data
{
    /// <summary>
    /// Writes punches straight into MySQL bio_punch. Idempotent: the UNIQUE key
    /// (tenant_id, device_id, enroll_number, punch_time, in_out_mode) +
    /// ON DUPLICATE KEY UPDATE means re-fetches never duplicate. Auto-creates the
    /// tables on first use.
    /// </summary>
    public sealed class PunchDbWriter
    {
        private readonly string _cs;
        private bool _schemaEnsured;

        public PunchDbWriter(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("MySQL connection string is required.", nameof(connectionString));
            _cs = connectionString;
        }

        /// <summary>Opens a connection (and ensures schema) - used to validate settings.</summary>
        public void TestConnection()
        {
            using var conn = new MySqlConnection(_cs);
            conn.Open();
            if (!_schemaEnsured) { SchemaInitializer.EnsureSchema(conn); _schemaEnsured = true; }
        }

        /// <summary>Returns the number of NEW rows inserted (duplicates are no-ops).</summary>
        public int UpsertBatch(string tenantId, long siteId, int deviceId, IReadOnlyList<PunchRecord> records)
        {
            if (records == null || records.Count == 0) return 0;

            using var conn = new MySqlConnection(_cs);
            conn.Open();
            if (!_schemaEnsured) { SchemaInitializer.EnsureSchema(conn); _schemaEnsured = true; }

            const string sql =
                "INSERT INTO bio_punch " +
                "(tenant_id, site_id, device_id, enroll_number, punch_time, verify_mode, verify_label, " +
                " in_out_mode, io_mode, door_mode, raw_temperature) " +
                "VALUES (@t,@s,@d,@e,@pt,@vm,@vl,@io,@iom,@dm,@temp) " +
                "ON DUPLICATE KEY UPDATE id = id;";

            int inserted = 0;
            using var tx = conn.BeginTransaction();
            foreach (var r in records)
            {
                using var cmd = new MySqlCommand(sql, conn, tx);
                cmd.Parameters.AddWithValue("@t", tenantId ?? "");
                cmd.Parameters.AddWithValue("@s", siteId);
                cmd.Parameters.AddWithValue("@d", deviceId);
                cmd.Parameters.AddWithValue("@e", r.EnrollNumber);
                cmd.Parameters.AddWithValue("@pt", r.PunchTime);
                cmd.Parameters.AddWithValue("@vm", r.VerifyMode);
                cmd.Parameters.AddWithValue("@vl", (object)r.VerifyLabel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@io", r.InOutMode);
                cmd.Parameters.AddWithValue("@iom", r.IoMode);
                cmd.Parameters.AddWithValue("@dm", r.DoorMode);
                cmd.Parameters.AddWithValue("@temp", (object)r.Temperature ?? DBNull.Value);
                if (cmd.ExecuteNonQuery() == 1) inserted++;
            }
            tx.Commit();
            return inserted;
        }
    }
}
