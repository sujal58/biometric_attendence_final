using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AttendanceBridge.Data;
using AttendanceBridge.Device;

namespace AttendanceDesktop
{
    /// <summary>
    /// School/technician tool for ONE client with MULTIPLE devices. Shows device
    /// info/status, fetches attendance (auto on open + buttons) from each device
    /// in turn, and pushes punches to MySQL and/or the Shikzya API.
    /// </summary>
    public sealed class MainForm : Form
    {
        private DesktopConfig _cfg = DesktopConfig.Load();
        private PushService _push;
        private bool _busy;

        private ComboBox _deviceSelect;
        private Button _btnFetchAll, _btnFetchOne, _btnInfo, _btnSync, _btnSettings;
        private Label _status;
        private TextBox _info;
        private DataGridView _grid;

        public MainForm()
        {
            Text = "Shikzya Attendance — Device Tool";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 600);
            MinimumSize = new Size(700, 460);
            _push = new PushService(_cfg);
            BuildUi();
            ReloadDevices();
            Shown += async (s, e) => await Guarded(FetchAllAsync);
        }

        private void BuildUi()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 8, 8, 0) };
            _deviceSelect = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(4, 4, 12, 0) };
            _deviceSelect.SelectedIndexChanged += async (s, e) => { if (!_busy) await Guarded(ShowSelectedInfoAsync); };
            _btnFetchAll = MakeButton("Fetch all devices", () => Guarded(FetchAllAsync));
            _btnFetchOne = MakeButton("Fetch selected", () => Guarded(FetchSelectedAsync));
            _btnInfo = MakeButton("Refresh info", () => Guarded(ShowSelectedInfoAsync));
            _btnSync = MakeButton("Sync time", () => Guarded(SyncSelectedAsync));
            _btnSettings = MakeButton("Settings…", OpenSettings);
            bar.Controls.AddRange(new Control[] { _deviceSelect, _btnFetchAll, _btnFetchOne, _btnInfo, _btnSync, _btnSettings });

            _info = new TextBox { Dock = DockStyle.Top, Height = 120, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F), BackColor = Color.White };

            _grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
            _grid.Columns.Add("device", "Device");
            _grid.Columns.Add("user", "User #");
            _grid.Columns.Add("time", "Time");
            _grid.Columns.Add("verify", "Verify");
            _grid.Columns.Add("io", "In/Out");
            _grid.Columns.Add("temp", "Temp");

            _status = new Label { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), BackColor = SystemColors.ControlLight, Text = "Ready." };

            Controls.Add(_grid);
            Controls.Add(_info);
            Controls.Add(bar);
            Controls.Add(_status);
        }

        private Button MakeButton(string text, Func<Task> handler)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(4, 0, 4, 0) };
            b.Click += async (s, e) => await handler();
            return b;
        }
        private Button MakeButton(string text, Action handler)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(4, 0, 4, 0) };
            b.Click += (s, e) => handler();
            return b;
        }

        private void ReloadDevices()
        {
            _deviceSelect.Items.Clear();
            foreach (var d in _cfg.Devices)
                _deviceSelect.Items.Add(string.IsNullOrWhiteSpace(d.Name) ? d.Ip : d.Name);
            if (_deviceSelect.Items.Count > 0) _deviceSelect.SelectedIndex = 0;
        }

        private DesktopConfig.DeviceEntry Selected()
        {
            int i = _deviceSelect.SelectedIndex;
            return (i >= 0 && i < _cfg.Devices.Count) ? _cfg.Devices[i] : null;
        }

        private static DeviceParams ToParams(DesktopConfig.DeviceEntry d) => new DeviceParams
        {
            IpAddress = d.Ip, NetPort = d.Port, MachineNo = d.MachineNo,
            NetPassword = d.NetPassword, License = d.License, TimeoutMs = 5000,
        };

        // Only one device operation at a time (the SDK is single-session).
        private async Task Guarded(Func<Task> work)
        {
            if (_busy) return;
            _busy = true;
            SetButtons(false);
            try { await work(); }
            catch (Exception ex) { SetStatus("Error: " + ex.Message); }
            finally { _busy = false; SetButtons(true); }
        }

        private void SetButtons(bool on) =>
            _btnFetchAll.Enabled = _btnFetchOne.Enabled = _btnInfo.Enabled = _btnSync.Enabled = _btnSettings.Enabled = _deviceSelect.Enabled = on;

        private void SetStatus(string s) => _status.Text = s;

        // ---- operations ----

        private async Task FetchAllAsync()
        {
            if (_cfg.Devices.Count == 0) { SetStatus("No devices configured — open Settings to add some."); return; }
            _grid.Rows.Clear();
            var summary = new List<string>();
            foreach (var d in _cfg.Devices)
            {
                SetStatus("Fetching " + DevName(d) + " …");
                summary.Add(await FetchOneAsync(d));
            }
            SetStatus(string.Join("   |   ", summary));
        }

        private async Task FetchSelectedAsync()
        {
            var d = Selected();
            if (d == null) { SetStatus("Pick a device first."); return; }
            _grid.Rows.Clear();
            SetStatus("Fetching " + DevName(d) + " …");
            SetStatus(await FetchOneAsync(d));
        }

        private async Task<string> FetchOneAsync(DesktopConfig.DeviceEntry d)
        {
            List<PunchRecord> records;
            try
            {
                records = await Task.Run(() =>
                {
                    using var conn = new DeviceConnection(ToParams(d));
                    if (!conn.Connect()) throw new Exception("connect failed");
                    try { return new LogPoller(conn).Read(0); }
                    finally { conn.Disconnect(); }
                });
            }
            catch (Exception ex) { return DevName(d) + ": connect/read failed (" + ex.Message + ")"; }

            AddRows(DevName(d), records);
            var pr = await _push.Push(d.DeviceId, records);
            return DevName(d) + ": read " + records.Count + (records.Count > 0 ? " (" + pr.Message + ")" : "");
        }

        private async Task ShowSelectedInfoAsync()
        {
            var d = Selected();
            if (d == null) { _info.Text = ""; return; }
            try
            {
                var (snap, devTime) = await Task.Run(() =>
                {
                    using var conn = new DeviceConnection(ToParams(d));
                    if (!conn.Connect()) throw new Exception("connect failed");
                    try
                    {
                        var s = new DeviceInfoModule(conn).Read();
                        DateTime t;
                        try { t = new TimeSyncModule(conn).ReadDeviceTime(); } catch { t = DateTime.MinValue; }
                        return (s, t);
                    }
                    finally { conn.Disconnect(); }
                });
                _info.Text = string.Join(Environment.NewLine, new[]
                {
                    "Device       : " + DevName(d) + "  (" + d.Ip + ":" + d.Port + ", id " + d.DeviceId + ")",
                    "Product      : " + snap.ProductName + " (" + snap.ProductCode + ")   Serial: " + snap.SerialNumber,
                    "Users / Mgrs : " + snap.Users + " / " + snap.Managers + "   Fingerprints: " + snap.Fingerprints + "   Faces: " + snap.Faces,
                    "Logs on dev  : " + snap.GeneralLogs,
                    "Device time  : " + (devTime == DateTime.MinValue ? "(n/a)" : devTime.ToString("yyyy-MM-dd HH:mm:ss")) + "   PC time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                });
                SetStatus("Connected to " + DevName(d) + ".");
            }
            catch (Exception ex) { _info.Text = DevName(d) + ": " + ex.Message; SetStatus("Could not reach " + DevName(d) + "."); }
        }

        private async Task SyncSelectedAsync()
        {
            var d = Selected();
            if (d == null) { SetStatus("Pick a device first."); return; }
            bool changed = await Task.Run(() =>
            {
                using var conn = new DeviceConnection(ToParams(d));
                if (!conn.Connect()) throw new Exception("connect failed");
                try { return new TimeSyncModule(conn).SyncIfDrift(30); }
                finally { conn.Disconnect(); }
            });
            await ShowSelectedInfoAsync();
            SetStatus(changed ? DevName(d) + ": clock corrected." : DevName(d) + ": clock already in tolerance.");
        }

        private void OpenSettings()
        {
            using (var dlg = new SettingsForm(_cfg))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _push = new PushService(_cfg);   // pick up new connection string / targets
                    ReloadDevices();
                    _ = Guarded(ShowSelectedInfoAsync);
                }
            }
        }

        private void AddRows(string deviceName, List<PunchRecord> records)
        {
            foreach (var r in records)
                _grid.Rows.Add(deviceName, r.EnrollNumber, r.PunchTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    r.VerifyLabel, r.IoMode, r.Temperature.HasValue ? (r.Temperature.Value / 10.0).ToString("0.0") : "");
        }

        private static string DevName(DesktopConfig.DeviceEntry d) => string.IsNullOrWhiteSpace(d.Name) ? d.Ip : d.Name;
    }
}
