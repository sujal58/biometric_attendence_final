using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MySqlConnector;

namespace AttendanceDesktop.Data
{
    /// <summary>
    /// Creates the bio_* tables if missing by running the bundled db/schema.sql
    /// (CREATE TABLE IF NOT EXISTS). Safe to run repeatedly.
    /// </summary>
    public static class SchemaInitializer
    {
        private const string ResourceName = "ShikzyaDeviceTool.schema.sql";

        public static void EnsureSchema(MySqlConnection conn)
        {
            foreach (var stmt in SplitStatements(LoadEmbedded()))
            {
                using var cmd = new MySqlCommand(stmt, conn);
                cmd.ExecuteNonQuery();
            }
        }

        private static string LoadEmbedded()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream(ResourceName);
            if (s == null)
                throw new InvalidOperationException("Embedded schema '" + ResourceName + "' not found.");
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        private static IEnumerable<string> SplitStatements(string sql)
        {
            foreach (var part in sql.Split(';'))
            {
                var stmt = part.Trim();
                if (stmt.Length == 0) continue;

                bool hasSql = false;
                foreach (var line in stmt.Split('\n'))
                {
                    var l = line.Trim();
                    if (l.Length > 0 && !l.StartsWith("--")) { hasSql = true; break; }
                }
                if (hasSql) yield return stmt;
            }
        }
    }
}
