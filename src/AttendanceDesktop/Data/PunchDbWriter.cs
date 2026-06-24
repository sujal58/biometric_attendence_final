using System;
using System.Collections.Generic;
using MySqlConnector;
using AttendanceBridge.Data;
using AttendanceBridge.Device;

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

        /// <summary>Upserts the device's user roster into bio_user. Returns count written.</summary>
        public int UpsertUsers(string tenantId, int deviceId, IReadOnlyList<DeviceUser> users)
        {
            if (users == null || users.Count == 0) return 0;

            using var conn = new MySqlConnection(_cs);
            conn.Open();
            if (!_schemaEnsured) { SchemaInitializer.EnsureSchema(conn); _schemaEnsured = true; }

            const string sql =
                "INSERT INTO bio_user (tenant_id, device_id, enroll_number, name, privilege, enabled, updated_at) " +
                "VALUES (@t,@d,@e,@n,@p,@en,NOW()) " +
                "ON DUPLICATE KEY UPDATE name=@n, privilege=@p, enabled=@en, updated_at=NOW();";

            int n = 0;
            using var tx = conn.BeginTransaction();
            foreach (var u in users)
            {
                using var cmd = new MySqlCommand(sql, conn, tx);
                cmd.Parameters.AddWithValue("@t", tenantId ?? "");
                cmd.Parameters.AddWithValue("@d", deviceId);
                cmd.Parameters.AddWithValue("@e", u.EnrollNumber);
                cmd.Parameters.AddWithValue("@n", (object)u.Name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@p", u.Privilege);
                cmd.Parameters.AddWithValue("@en", u.Enabled ? 1 : 0);
                cmd.ExecuteNonQuery();
                n++;
            }
            tx.Commit();
            return n;
        }

        /// <summary>Upserts a row into bio_device with the latest fetch status.</summary>
        public void UpsertDevice(string tenantId, long siteId, DesktopConfig.DeviceEntry d, string lastStatus)
        {
            using var conn = new MySqlConnection(_cs);
            conn.Open();
            if (!_schemaEnsured) { SchemaInitializer.EnsureSchema(conn); _schemaEnsured = true; }

            const string sql =
                "INSERT INTO bio_device (device_id, site_id, tenant_id, name, ip, port, machine_no, net_password, license, last_pull_at, last_status) " +
                "VALUES (@d,@s,@t,@n,@ip,@port,@mn,@np,@lic,NOW(),@st) " +
                "ON DUPLICATE KEY UPDATE name=@n, ip=@ip, port=@port, machine_no=@mn, net_password=@np, license=@lic, last_pull_at=NOW(), last_status=@st;";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@d", d.DeviceId);
            cmd.Parameters.AddWithValue("@s", siteId);
            cmd.Parameters.AddWithValue("@t", tenantId ?? "");
            cmd.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(d.Name) ? d.Ip : d.Name);
            cmd.Parameters.AddWithValue("@ip", d.Ip ?? "");
            cmd.Parameters.AddWithValue("@port", d.Port);
            cmd.Parameters.AddWithValue("@mn", d.MachineNo);
            cmd.Parameters.AddWithValue("@np", d.NetPassword);
            cmd.Parameters.AddWithValue("@lic", d.License);
            cmd.Parameters.AddWithValue("@st", lastStatus ?? "");
            cmd.ExecuteNonQuery();
        }
    }
}
