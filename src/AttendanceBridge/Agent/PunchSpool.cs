using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AttendanceBridge.Api;

namespace AttendanceBridge.Agent
{
    /// <summary>
    /// Local on-disk queue for punch batches that couldn't be uploaded (school
    /// internet down). Each batch is one JSON file; a retry loop re-sends them
    /// and deletes them once the API confirms, so no punch is ever lost.
    /// </summary>
    public sealed class PunchSpool
    {
        private readonly string _dir;
        private readonly object _gate = new object();
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        public PunchSpool(string dir)
        {
            _dir = dir;
            Directory.CreateDirectory(_dir);
        }

        public void Save(PunchUpload upload)
        {
            lock (_gate)
            {
                string name = Path.Combine(_dir,
                    "punch-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" +
                    Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
                File.WriteAllText(name, JsonSerializer.Serialize(upload, _json));
            }
        }

        public IReadOnlyList<string> Pending()
        {
            lock (_gate)
            {
                if (!Directory.Exists(_dir)) return Array.Empty<string>();
                return Directory.GetFiles(_dir, "punch-*.json").OrderBy(f => f).ToList();
            }
        }

        public PunchUpload Read(string file) =>
            JsonSerializer.Deserialize<PunchUpload>(File.ReadAllText(file), _json);

        public void Remove(string file)
        {
            try { File.Delete(file); } catch { /* will be retried */ }
        }
    }
}
