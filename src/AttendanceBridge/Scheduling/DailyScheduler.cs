using System;
using System.Collections.Generic;

namespace AttendanceBridge.Scheduling
{
    /// <summary>
    /// Fires once per configured HH:mm time per day. DueNow returns the times
    /// that have arrived since the last check today; the set resets at midnight.
    /// One instance per device.
    /// </summary>
    public sealed class DailyScheduler
    {
        private readonly List<TimeSpan> _times = new List<TimeSpan>();
        private readonly HashSet<string> _firedToday = new HashSet<string>();
        private DateTime _day = DateTime.MinValue.Date;

        public DailyScheduler(string[] times)
        {
            foreach (var t in times ?? Array.Empty<string>())
                if (TimeSpan.TryParse(t, out var ts))
                    _times.Add(ts);
        }

        public IEnumerable<string> DueNow(DateTime now)
        {
            if (now.Date != _day) { _day = now.Date; _firedToday.Clear(); }

            var due = new List<string>();
            foreach (var ts in _times)
            {
                string key = ts.ToString();
                if (now.TimeOfDay >= ts && !_firedToday.Contains(key))
                {
                    _firedToday.Add(key);
                    due.Add(string.Format("{0:00}:{1:00}", ts.Hours, ts.Minutes));
                }
            }
            return due;
        }
    }
}
