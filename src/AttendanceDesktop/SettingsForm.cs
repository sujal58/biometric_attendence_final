using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AttendanceBridge.Device;
using AttendanceDesktop.Data;

namespace AttendanceDesktop
{
    /// <summary>Technician settings: client (tenant), push target, DB/API, devices.</summary>
    public sealed class SettingsForm : Form
    {
        private readonly DesktopConfig _cfg;
        private readonly BindingList<DesktopConfig.DeviceEntry> _devices;

        private TextBox _tenant, _site, _conn, _api, _token, _auto;
        private ComboBox _push;
        private DataGridView _grid;

        public SettingsForm(DesktopConfig cfg)
        {
            _cfg = cfg;
            _devices = new BindingList<DesktopConfig.DeviceEntry>(cfg.Devices.Select(Clone).ToList());

            Text = "Settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(700, 580);
            MinimumSize = new Size(640, 520);

            BuildUi();
            Load += (s, e) => LoadValues();
        }

        private void BuildUi()
        {
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, Height = 232, ColumnCount = 2, Padding = new Padding(10) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _tenant = AddRow(fields, "Tenant id (client):");
            _site = AddRow(fields, "Site id (optional):");
            _push = AddCombo(fields, "Push to:", new[] { "MySQL (direct)", "API only", "Both" });
            _conn = AddRow(fields, "MySQL connection:");
            _api = AddRow(fields, "API base URL:");
            _token = AddRow(fields, "Site token:");
            _auto = AddRow(fields, "Auto-fetch (min, 0=off):");

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
            var testDb = new Button { Text = "Test MySQL", AutoSize = true };
            add.Click += (s, e) => _devices.Add(new DesktopConfig.DeviceEntry { Name = "Device " + (_devices.Count + 1) });
            remove.Click += (s, e) => { foreach (DataGridViewRow row in _grid.SelectedRows.Cast<DataGridViewRow>().ToList()) if (!row.IsNewRow) _grid.Rows.Remove(row); };
            testDev.Click += async (s, e) => await TestDeviceAsync();
            testDb.Click += async (s, e) => await TestMysqlAsync();
            devBar.Controls.AddRange(new Control[] { add, remove, testDev, testDb });

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 10, 0) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(90, 30) };
            var ok = new Button { Text = "Save", Size = new Size(90, 30) };
            ok.Click += (s, e) => { if (Apply()) DialogResult = DialogResult.OK; };
            bottom.Controls.AddRange(new Control[] { ok, cancel });
            AcceptButton = ok; CancelButton = cancel;

            Controls.Add(_grid);
            Controls.Add(devBar);
            Controls.Add(devLabel);
            Controls.Add(fields);
            Controls.Add(bottom);
        }

        private int _row;
        private TextBox AddRow(TableLayoutPanel t, string label)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) }, 0, _row);
            var tb = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            t.Controls.Add(tb, 1, _row);
            _row++;
            return tb;
        }
        private ComboBox AddCombo(TableLayoutPanel t, string label, string[] items)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) }, 0, _row);
            var cb = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 0, 3) };
            cb.Items.AddRange(items);
            t.Controls.Add(cb, 1, _row);
            _row++;
            return cb;
        }

        private void LoadValues()
        {
            _tenant.Text = _cfg.TenantId;
            _site.Text = _cfg.SiteId.ToString();
            _push.SelectedIndex = (int)_cfg.Push;
            _conn.Text = _cfg.Db.ConnectionString;
            _api.Text = _cfg.Api.ApiBaseUrl;
            _token.Text = _cfg.Api.SiteToken;
            _auto.Text = _cfg.AutoFetchMinutes.ToString();
        }

        private bool Apply()
        {
            _cfg.TenantId = _tenant.Text.Trim();
            _cfg.SiteId = long.TryParse(_site.Text.Trim(), out var sid) ? sid : 0;
            _cfg.Push = (PushTarget)Math.Max(0, _push.SelectedIndex);
            _cfg.Db.ConnectionString = _conn.Text.Trim();
            _cfg.Api.ApiBaseUrl = _api.Text.Trim();
            _cfg.Api.SiteToken = _token.Text.Trim();
            _cfg.AutoFetchMinutes = int.TryParse(_auto.Text.Trim(), out var m) && m > 0 ? m : 0;
            _cfg.Devices = _devices.Where(d => !string.IsNullOrWhiteSpace(d.Ip)).Select(Clone).ToList();

            try { _cfg.Save(); }
            catch (Exception ex) { MessageBox.Show("Could not save settings: " + ex.Message, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
            return true;
        }

        private async Task TestDeviceAsync()
        {
            _grid.EndEdit();
            var d = _grid.CurrentRow?.DataBoundItem as DesktopConfig.DeviceEntry;
            if (d == null || string.IsNullOrWhiteSpace(d.Ip)) { MessageBox.Show("Select a device row that has an IP.", "Test device"); return; }

            string err = null; bool ok = false;
            try
            {
                ok = await Task.Run(() =>
                {
                    using var c = new DeviceConnection(new DeviceParams { IpAddress = d.Ip, NetPort = d.Port, MachineNo = d.MachineNo, NetPassword = d.NetPassword, License = d.License, TimeoutMs = 5000 });
                    return c.Connect();
                });
            }
            catch (Exception ex) { err = ex.Message; }

            MessageBox.Show(ok ? "Connected to " + d.Ip + "  ✓" : "Could not connect to " + d.Ip + (err != null ? ": " + err : " (check IP / license / network)."),
                "Test device", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private async Task TestMysqlAsync()
        {
            var cs = _conn.Text.Trim();
            if (cs.Length == 0) { MessageBox.Show("Enter a MySQL connection string.", "Test MySQL"); return; }

            string err = null;
            try { await Task.Run(() => new PunchDbWriter(cs).TestConnection()); }
            catch (Exception ex) { err = ex.Message; }

            MessageBox.Show(err == null ? "MySQL OK — connected and tables ready  ✓" : "MySQL failed: " + err,
                "Test MySQL", MessageBoxButtons.OK, err == null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private static DesktopConfig.DeviceEntry Clone(DesktopConfig.DeviceEntry d) => new DesktopConfig.DeviceEntry
        {
            Name = d.Name, Ip = d.Ip, Port = d.Port, MachineNo = d.MachineNo,
            NetPassword = d.NetPassword, License = d.License, DeviceId = d.DeviceId,
        };
    }
}
