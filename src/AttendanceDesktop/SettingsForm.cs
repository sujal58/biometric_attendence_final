using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AttendanceBridge.Device;
using AttendanceDesktop.Activation;
using AttendanceDesktop.Security;

namespace AttendanceDesktop
{
    /// <summary>Technician settings: Shikzya URL, license, devices, auto-fetch. No DB creds.</summary>
    public sealed class SettingsForm : Form
    {
        private readonly DesktopConfig _cfg;
        private readonly BindingList<DeviceEntry> _devices;

        private TextBox _url, _key, _auto;
        private Label _school;
        private DataGridView _grid;

        public SettingsForm(DesktopConfig cfg)
        {
            _cfg = cfg;
            _devices = new BindingList<DeviceEntry>(cfg.Devices.Select(Clone).ToList());

            Text = "Settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(700, 560);
            MinimumSize = new Size(640, 500);

            BuildUi();
            Load += (s, e) => LoadValues();
        }

        private void BuildUi()
        {
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, Height = 175, ColumnCount = 2, Padding = new Padding(10) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _url = AddRow(fields, "Shikzya URL:");
            _key = AddRow(fields, "License key:");
            _school = AddLabelRow(fields, "Activated school:");
            _auto = AddRow(fields, "Auto-fetch (min, 0=off):");

            var licBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(150, 0, 0, 0) };
            var reactivate = new Button { Text = "Re-activate", AutoSize = true };
            var changePwd = new Button { Text = "Change admin password", AutoSize = true };
            reactivate.Click += async (s, e) => await ReactivateAsync();
            changePwd.Click += (s, e) => ChangePassword();
            licBar.Controls.AddRange(new Control[] { reactivate, changePwd });

            var devLabel = new Label { Dock = DockStyle.Top, Height = 22, Text = "Devices (one row per machine):", Padding = new Padding(10, 2, 0, 0) };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                DataSource = _devices,
                AutoGenerateColumns = true,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };

            var devBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 4, 0, 0) };
            var add = new Button { Text = "Add device", AutoSize = true };
            var remove = new Button { Text = "Remove selected", AutoSize = true };
            var testDev = new Button { Text = "Test selected device", AutoSize = true };
            add.Click += (s, e) => _devices.Add(new DeviceEntry { Name = "Device " + (_devices.Count + 1) });
            remove.Click += (s, e) => { foreach (DataGridViewRow row in _grid.SelectedRows.Cast<DataGridViewRow>().ToList()) if (!row.IsNewRow) _grid.Rows.Remove(row); };
            testDev.Click += async (s, e) => await TestDeviceAsync();
            devBar.Controls.AddRange(new Control[] { add, remove, testDev });

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 10, 0) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(90, 30) };
            var ok = new Button { Text = "Save", Size = new Size(90, 30) };
            ok.Click += (s, e) => { if (Apply()) DialogResult = DialogResult.OK; };
            bottom.Controls.AddRange(new Control[] { ok, cancel });
            AcceptButton = ok; CancelButton = cancel;

            Controls.Add(_grid);
            Controls.Add(devBar);
            Controls.Add(devLabel);
            Controls.Add(licBar);
            Controls.Add(fields);
            Controls.Add(bottom);
        }

        private int _row;
        private TextBox AddRow(TableLayoutPanel t, string label)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) }, 0, _row);
            var tb = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            t.Controls.Add(tb, 1, _row); _row++;
            return tb;
        }
        private Label AddLabelRow(TableLayoutPanel t, string label)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) }, 0, _row);
            var lb = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0), ForeColor = Color.DarkGreen };
            t.Controls.Add(lb, 1, _row); _row++;
            return lb;
        }

        private void LoadValues()
        {
            _url.Text = _cfg.ApiBaseUrl;
            _key.Text = _cfg.LicenseKey;
            _auto.Text = _cfg.AutoFetchMinutes.ToString();
            _school.Text = string.IsNullOrEmpty(_cfg.Cache.TenantId)
                ? "(not activated)"
                : _cfg.Cache.TenantId + "   —   " + _cfg.Cache.AllowedDevices.Count + " registered device(s)";
        }

        private bool Apply()
        {
            _cfg.ApiBaseUrl = _url.Text.Trim();
            _cfg.LicenseKey = _key.Text.Trim();
            _cfg.AutoFetchMinutes = int.TryParse(_auto.Text.Trim(), out var m) && m > 0 ? m : 0;
            _cfg.Devices = _devices.Where(d => !string.IsNullOrWhiteSpace(d.Ip)).Select(Clone).ToList();
            try { _cfg.Save(); }
            catch (Exception ex) { MessageBox.Show("Could not save: " + ex.Message, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
            return true;
        }

        private async Task ReactivateAsync()
        {
            _cfg.ApiBaseUrl = _url.Text.Trim();
            _cfg.LicenseKey = _key.Text.Trim();
            _cfg.Save();
            var gate = await LicenseGate.CheckAsync(_cfg);
            LoadValues();
            MessageBox.Show(gate.Message, "Re-activate", MessageBoxButtons.OK,
                gate.Allowed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void ChangePassword()
        {
            if (_cfg.HasAdminPassword)
            {
                using var cur = new PasswordDialog("Current password", "Enter the current admin password.", false);
                if (cur.ShowDialog(this) != DialogResult.OK) return;
                if (!PasswordHasher.Verify(cur.Password, _cfg.AdminPasswordSalt, _cfg.AdminPasswordHash))
                { MessageBox.Show("Wrong password.", "Change password"); return; }
            }
            using var set = new PasswordDialog("New password", "Set the new admin password.", true);
            if (set.ShowDialog(this) != DialogResult.OK) return;
            var (salt, hash) = PasswordHasher.Hash(set.Password);
            _cfg.AdminPasswordSalt = salt; _cfg.AdminPasswordHash = hash; _cfg.Save();
            MessageBox.Show("Admin password updated.", "Change password");
        }

        private async Task TestDeviceAsync()
        {
            _grid.EndEdit();
            var d = _grid.CurrentRow?.DataBoundItem as DeviceEntry;
            if (d == null || string.IsNullOrWhiteSpace(d.Ip)) { MessageBox.Show("Select a device row with an IP.", "Test device"); return; }

            string err = null; bool ok = false; string ids = null;
            try
            {
                (ok, ids) = await Task.Run<(bool, string)>(() =>
                {
                    using var c = new DeviceConnection(new DeviceParams { IpAddress = d.Ip, NetPort = d.Port, MachineNo = d.MachineNo, NetPassword = d.NetPassword, License = d.License, TimeoutMs = 5000 });
                    if (!c.Connect()) return (false, null);
                    try { var s = new DeviceInfoModule(c).Read(); return (true, "Serial " + s.SerialNumber + " / MAC " + s.MacAddress); }
                    finally { c.Disconnect(); }
                });
            }
            catch (Exception ex) { err = ex.Message; }

            MessageBox.Show(ok ? "Connected to " + d.Ip + "  ✓\n" + ids + "\n\nGive these IDs to your admin to register this device."
                              : "Could not connect to " + d.Ip + (err != null ? ": " + err : "."),
                "Test device", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private static DeviceEntry Clone(DeviceEntry d) => new DeviceEntry
        {
            Name = d.Name, Ip = d.Ip, Port = d.Port, MachineNo = d.MachineNo, NetPassword = d.NetPassword, License = d.License,
        };
    }
}
