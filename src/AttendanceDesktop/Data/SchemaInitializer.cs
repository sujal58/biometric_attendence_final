using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
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
            // Strip "--" line comments first, so a ';' inside a comment can never
            // break statement splitting (our DDL has no '--' inside string literals).
            var sb = new StringBuilder();
            foreach (var rawLine in sql.Split('\n'))
            {
                var line = rawLine;
                int c = line.IndexOf("--", StringComparison.Ordinal);
                if (c >= 0) line = line.Substring(0, c);
                sb.Append(line).Append('\n');
            }

            foreach (var part in sb.ToString().Split(';'))
            {
                var stmt = part.Trim();
                if (stmt.Length > 0) yield return stmt;
            }
        }
    }
}
