using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AttendanceBridge.Data;
using AttendanceBridge.Device;
using AttendanceDesktop.Security;

namespace AttendanceDesktop
{
    /// <summary>
    /// API-only tool for ONE school with MULTIPLE devices. Reads attendance + the
    /// user roster from each device and POSTs to the authenticated Shikzya
    /// endpoints. Only devices registered to this school's license are processed.
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly DesktopConfig _cfg;
        private PushService _push;
        private bool _busy;

        private ComboBox _deviceSelect;
        private Button _btnFetchAll, _btnFetchOne, _btnInfo, _btnSync, _btnExport, _btnSettings;
        private Label _status;
        private TextBox _info;
        private DataGridView _grid;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private NotifyIcon _tray;

        public MainForm(DesktopConfig cfg)
        {
            _cfg = cfg;
            _push = new PushService(_cfg);
            Text = "Shikzya Attendance — Device Tool";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(980, 600);
            MinimumSize = new Size(780, 460);

            BuildUi();
            BuildTray();
            ReloadDevices();
            _timer.Tick += async (s, e) => { SetStatus("Auto-fetch run at " + DateTime.Now.ToString("HH:mm:ss") + " …"); if (!_busy) await Guarded(FetchAllAsync); };
            ConfigureTimer();
            Shown += async (s, e) => await Guarded(FetchAllAsync);
            Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) Hide(); };
            FormClosing += (s, e) => { try { _tray?.Dispose(); } catch { } };
        }

        private void BuildUi()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 8, 8, 0) };
            _deviceSelect = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Margin = new Padding(4, 4, 12, 0) };
            _deviceSelect.SelectedIndexChanged += async (s, e) => { if (!_busy) await Guarded(ShowSelectedInfoAsync); };
            _btnFetchAll = MakeButton("Fetch all devices", () => Guarded(FetchAllAsync));
            _btnFetchOne = MakeButton("Fetch selected", () => Guarded(FetchSelectedAsync));
            _btnInfo = MakeButton("Refresh info", () => Guarded(ShowSelectedInfoAsync));
            _btnSync = MakeButton("Sync time", () => Guarded(SyncSelectedAsync));
            _btnExport = MakeButton("Export CSV", ExportCsv);
            _btnSettings = MakeButton("Settings…", OpenSettings);
            bar.Controls.AddRange(new Control[] { _deviceSelect, _btnFetchAll, _btnFetchOne, _btnInfo, _btnSync, _btnExport, _btnSettings });

            _info = new TextBox { Dock = DockStyle.Top, Height = 120, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F), BackColor = Color.White };

            _grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
            foreach (var c in new[] { "Device", "User #", "Name", "Time", "Verify", "In/Out", "Temp" }) _grid.Columns.Add(c, c);

            _status = new Label { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), BackColor = SystemColors.ControlLight, Text = "Ready." };

            Controls.Add(_grid);
            Controls.Add(_info);
            Controls.Add(bar);
            Controls.Add(_status);
        }

        private void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, (s, e) => RestoreFromTray());
            menu.Items.Add("Fetch now", null, async (s, e) => { RestoreFromTray(); if (!_busy) await Guarded(FetchAllAsync); });
            menu.Items.Add("Exit", null, (s, e) => { _tray.Visible = false; Application.Exit(); });
            _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Shikzya Device Tool", Visible = true, ContextMenuStrip = menu };
            _tray.DoubleClick += (s, e) => RestoreFromTray();
        }

        private void RestoreFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        private void ConfigureTimer()
        {
            _timer.Stop();
            if (_cfg.AutoFetchMinutes > 0)
            {
                _timer.Interval = Math.Max(1, _cfg.AutoFetchMinutes) * 60000;
                _timer.Start();
                Text = "Shikzya Attendance — Device Tool  (auto-fetch every " + _cfg.AutoFetchMinutes + " min)";
                SetStatus("Auto-fetch ON: every " + _cfg.AutoFetchMinutes + " min.");
            }
            else
            {
                Text = "Shikzya Attendance — Device Tool";
            }
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
            foreach (var d in _cfg.Devices) _deviceSelect.Items.Add(DevName(d));
            if (_deviceSelect.Items.Count > 0) _deviceSelect.SelectedIndex = 0;
        }

        private DeviceEntry Selected()
        {
            int i = _deviceSelect.SelectedIndex;
            return (i >= 0 && i < _cfg.Devices.Count) ? _cfg.Devices[i] : null;
        }

        private static DeviceParams ToParams(DeviceEntry d) => new DeviceParams
        {
            IpAddress = d.Ip, NetPort = d.Port, MachineNo = d.MachineNo,
            NetPassword = d.NetPassword, License = d.License, TimeoutMs = 5000,
        };

        private async Task Guarded(Func<Task> work)
        {
            if (_busy) return;
            _busy = true; SetButtons(false);
            try { await work(); }
            catch (Exception ex) { SetStatus("Error: " + ex.Message); }
            finally { _busy = false; SetButtons(true); }
        }

        private void SetButtons(bool on) =>
            _btnFetchAll.Enabled = _btnFetchOne.Enabled = _btnInfo.Enabled = _btnSync.Enabled = _btnExport.Enabled = _btnSettings.Enabled = _deviceSelect.Enabled = on;

        private void SetStatus(string s) => _status.Text = s;

        // ---- device allow-list (from activation) ----
        private bool IsAllowed(string serial, string mac)
        {
            string ns = (serial ?? "").Trim();
            string nm = NormMac(mac);
            foreach (var dev in _cfg.Cache.AllowedDevices)
            {
                if (ns.Length > 0 && string.Equals((dev.Serial ?? "").Trim(), ns, StringComparison.OrdinalIgnoreCase)) return true;
                if (nm.Length > 0 && NormMac(dev.Mac) == nm) return true;
            }
            return false;
        }
        private static string NormMac(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        // ---- operations ----

        private async Task FetchAllAsync()
        {
            if (_cfg.Devices.Count == 0) { SetStatus("No devices configured — open Settings to add some."); return; }
            _grid.Rows.Clear();
            var summary = new List<string>();
            foreach (var d in _cfg.Devices) { SetStatus("Fetching " + DevName(d) + " …"); summary.Add(await FetchOneAsync(d)); }
            SetStatus(DateTime.Now.ToString("HH:mm:ss") + "  —  " + string.Join("   |   ", summary));
        }

        private async Task FetchSelectedAsync()
        {
            var d = Selected();
            if (d == null) { SetStatus("Pick a device first."); return; }
            _grid.Rows.Clear();
            SetStatus(await FetchOneAsync(d));
        }

        private async Task<string> FetchOneAsync(DeviceEntry d)
        {
            (DeviceInfoSnapshot snap, List<PunchRecord> recs, List<DeviceUser> users, bool allowed) res;
            try
            {
                res = await Task.Run(() =>
                {
                    using var conn = new DeviceConnection(ToParams(d));
                    if (!conn.Connect()) throw new Exception("connect failed");
                    try
                    {
                        var s = new DeviceInfoModule(conn).Read();
                        if (!IsAllowed(s.SerialNumber, s.MacAddress))
                            return (s, (List<PunchRecord>)null, (List<DeviceUser>)null, false);
                        var r = new LogPoller(conn).Read(0);
                        List<DeviceUser> us; try { us = new UserRosterModule(conn).ReadUsers(); } catch { us = new List<DeviceUser>(); }
                        return (s, r, us, true);
                    }
                    finally { conn.Disconnect(); }
                });
            }
            catch (Exception ex) { return DevName(d) + ": connect/read failed (" + ex.Message + ")"; }

            if (!res.allowed)
                return DevName(d) + ": device serial " + res.snap.SerialNumber + " / MAC " + res.snap.MacAddress +
                       " is NOT registered for this school — give these IDs to your admin.";

            var names = res.users.GroupBy(u => u.EnrollNumber).ToDictionary(g => g.Key, g => g.First().Name);
            AddRows(DevName(d), res.recs, names);
            var pr = await _push.Push(res.snap.SerialNumber, res.snap.MacAddress, res.recs, res.users);
            return DevName(d) + ": " + res.recs.Count + " punches, " + res.users.Count + " users (" + pr.Message + ")";
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
                        DateTime t; try { t = new TimeSyncModule(conn).ReadDeviceTime(); } catch { t = DateTime.MinValue; }
                        return (s, t);
                    }
                    finally { conn.Disconnect(); }
                });
                bool reg = IsAllowed(snap.SerialNumber, snap.MacAddress);
                _info.Text = string.Join(Environment.NewLine, new[]
                {
                    "Device       : " + DevName(d) + "  (" + d.Ip + ":" + d.Port + ")",
                    "Serial / MAC : " + snap.SerialNumber + "  /  " + snap.MacAddress + "   [" + (reg ? "registered" : "NOT registered — give to admin") + "]",
                    "Product      : " + snap.ProductName + " (" + snap.ProductCode + ")",
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

        private void ExportCsv()
        {
            if (_grid.Rows.Count == 0) { SetStatus("Nothing to export — fetch first."); return; }
            using var dlg = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "attendance-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var sb = new StringBuilder();
            sb.AppendLine("Device,User,Name,Time,Verify,InOut,Temp");
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                sb.AppendLine(string.Join(",", row.Cells.Cast<DataGridViewCell>().Select(c => Csv(c.Value?.ToString()))));
            }
            try { File.WriteAllText(dlg.FileName, sb.ToString()); SetStatus("Exported " + _grid.Rows.Count + " rows."); }
            catch (Exception ex) { SetStatus("Export failed: " + ex.Message); }
        }

        private static string Csv(string s)
        {
            s ??= "";
            return (s.Contains(',') || s.Contains('"') || s.Contains('\n')) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private void OpenSettings()
        {
            // Admin password gate.
            if (!_cfg.HasAdminPassword)
            {
                using var set = new PasswordDialog("Set admin password", "Set a password to protect Settings.", true);
                if (set.ShowDialog(this) != DialogResult.OK) return;
                var (salt, hash) = PasswordHasher.Hash(set.Password);
                _cfg.AdminPasswordSalt = salt; _cfg.AdminPasswordHash = hash; _cfg.Save();
            }
            else
            {
                using var enter = new PasswordDialog("Admin password", "Enter the admin password to open Settings.", false);
                if (enter.ShowDialog(this) != DialogResult.OK) return;
                if (!PasswordHasher.Verify(enter.Password, _cfg.AdminPasswordSalt, _cfg.AdminPasswordHash))
                { MessageBox.Show("Wrong password.", "Settings"); return; }
            }

            using (var dlg = new SettingsForm(_cfg))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _push = new PushService(_cfg);
                    ReloadDevices();
                    ConfigureTimer();
                    _ = Guarded(_cfg.AutoFetchMinutes > 0 ? FetchAllAsync : ShowSelectedInfoAsync);
                }
            }
        }

        private void AddRows(string deviceName, List<PunchRecord> records, Dictionary<int, string> names)
        {
            foreach (var r in records)
            {
                names.TryGetValue(r.EnrollNumber, out var nm);
                _grid.Rows.Add(deviceName, r.EnrollNumber, nm ?? "", r.PunchTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    r.VerifyLabel, r.IoMode, r.Temperature.HasValue ? (r.Temperature.Value / 10.0).ToString("0.0") : "");
            }
        }

        private static string DevName(DeviceEntry d) => string.IsNullOrWhiteSpace(d.Name) ? d.Ip : d.Name;
    }
}
