using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MySqlConnector;
using AttendanceBridge.Logging;

namespace AttendanceBridge.Data
{
    /// <summary>
    /// Creates the bio_* tables automatically on first use by running the
    /// bundled schema (which uses CREATE TABLE IF NOT EXISTS). Safe to run on
    /// every startup. The database named in the connection string must already
    /// exist - this creates tables, not the database.
    /// </summary>
    public static class SchemaInitializer
    {
        private const string ResourceName = "AttendanceBridge.schema.sql";

        public static void EnsureSchema(string connectionString)
        {
            string sql = LoadEmbeddedSchema();
            int applied = 0;

            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();
                foreach (var statement in SplitStatements(sql))
                {
                    using (var cmd = new MySqlCommand(statement, conn))
                        cmd.ExecuteNonQuery();
                    applied++;
                }
            }

            Log.Info("Database schema verified (" + applied + " table statement(s); created if missing).");
        }

        private static string LoadEmbeddedSchema()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        "Embedded schema resource '" + ResourceName + "' was not found in the assembly.");
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        // Splits the script into individual statements on ';' and drops chunks
        // that contain only comments/whitespace. Our DDL has no ';' inside any
        // statement, so this simple split is sufficient.
        private static IEnumerable<string> SplitStatements(string sql)
        {
            foreach (var part in sql.Split(';'))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0) continue;

                bool hasSql = false;
                foreach (var line in trimmed.Split('\n'))
                {
                    var l = line.Trim();
                    if (l.Length == 0 || l.StartsWith("--")) continue;
                    hasSql = true;
                    break;
                }
                if (hasSql) yield return trimmed;
            }
        }
    }
}
