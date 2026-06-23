using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AttendanceBridge.Api;
using AttendanceBridge.Data;
using AttendanceBridge.Device;

namespace AttendanceDesktop
{
    /// <summary>
    /// Simple school/technician tool: shows device info + status, fetches
    /// attendance (auto on open + button), and pushes punches to Shikzya (the API
    /// tags them by tenant via the site token). Reuses the proven device layer.
    /// </summary>
    public sealed class MainForm : Form
    {
        private DesktopConfig _cfg = DesktopConfig.Load();
        private DeviceConnection _conn;
        private bool _busy;

        private Button _btnConnect, _btnFetch, _btnSync, _btnSettings;
        private Label _status;
        private TextBox _info;
        private DataGridView _grid;

        public MainForm()
        {
            Text = "Shikzya Attendance — Device Tool";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(820, 560);
            MinimumSize = new Size(640, 420);
            BuildUi();
            Shown += async (s, e) => await ConnectAndRefresh(autoFetch: true);
            FormClosing += (s, e) => { try { _conn?.Dispose(); } catch { } };
        }

        private void BuildUi()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 8, 8, 0) };
            _btnConnect = MakeButton("Connect", async () => await ConnectAndRefresh(false));
            _btnFetch = MakeButton("Fetch attendance", async () => await DoFetch());
            _btnSync = MakeButton("Sync time", async () => await DoSyncTime());
            _btnSettings = MakeButton("Settings…", OpenSettings);
            bar.Controls.AddRange(new Control[] { _btnConnect, _btnFetch, _btnSync, _btnSettings });

            _info = new TextBox { Dock = DockStyle.Top, Height = 130, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F), BackColor = Color.White };

            _grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
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
            b.Click += async (s, e) => await Guarded(handler);
            return b;
        }
        private Button MakeButton(string text, Action handler)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(4, 0, 4, 0) };
            b.Click += (s, e) => handler();
            return b;
        }

        // Run one device operation at a time; keep the UI responsive.
        private async Task Guarded(Func<Task> work)
        {
            if (_busy) return;
            _busy = true;
            SetButtons(false);
            try { await work(); }
            catch (Exception ex) { SetStatus("Error: " + ex.Message); }
            finally { _busy = false; SetButtons(true); }
        }

        private void SetButtons(bool on)
        {
            _btnConnect.Enabled = _btnFetch.Enabled = _btnSync.Enabled = _btnSettings.Enabled = on;
        }

        private void SetStatus(string s) => _status.Text = s;

        private bool EnsureParams()
        {
            if (!string.IsNullOrWhiteSpace(_cfg.Device.Ip)) return true;
            SetStatus("Set the device IP in Settings first.");
            return false;
        }

        private DeviceParams Params() => new DeviceParams
        {
            IpAddress = _cfg.Device.Ip, NetPort = _cfg.Device.Port, MachineNo = _cfg.Device.MachineNo,
            NetPassword = _cfg.Device.NetPassword, License = _cfg.Device.License, TimeoutMs = 5000,
        };

        private async Task<bool> EnsureConnected()
        {
            if (_conn != null && _conn.IsConnected) return true;
            if (!EnsureParams()) return false;

            SetStatus("Connecting to " + _cfg.Device.Ip + " …");
            _conn?.Dispose();
            _conn = new DeviceConnection(Params());
            bool ok = await Task.Run(() => _conn.Connect());
            SetStatus(ok ? "Connected to " + _cfg.Device.Ip + "." : "Could not connect to " + _cfg.Device.Ip + " (check IP / license / network).");
            return ok;
        }

        private async Task ConnectAndRefresh(bool autoFetch)
        {
            await Guarded(async () =>
            {
                if (!await EnsureConnected()) return;
                await ShowInfo();
                if (autoFetch) await FetchAndUpload();
            });
        }

        private async Task ShowInfo()
        {
            var snap = await Task.Run(() => new DeviceInfoModule(_conn).Read());
            DateTime devTime;
            try { devTime = await Task.Run(() => new TimeSyncModule(_conn).ReadDeviceTime()); }
            catch { devTime = DateTime.MinValue; }

            _info.Text = string.Join(Environment.NewLine, new[]
            {
                "Status        : Connected to " + _cfg.Device.Ip + ":" + _cfg.Device.Port,
                "Product       : " + snap.ProductName + " (" + snap.ProductCode + ")",
                "Serial number : " + snap.SerialNumber,
                "Machine no.   : " + snap.MachineNumber + "    MAC: " + snap.MacAddress,
                "Users / Mgrs  : " + snap.Users + " / " + snap.Managers + "    Fingerprints: " + snap.Fingerprints + "    Faces: " + snap.Faces,
                "Attendance logs on device: " + snap.GeneralLogs,
                "Device time   : " + (devTime == DateTime.MinValue ? "(n/a)" : devTime.ToString("yyyy-MM-dd HH:mm:ss")) + "    PC time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            });
        }

        private async Task DoFetch()
        {
            if (!await EnsureConnected()) return;
            await FetchAndUpload();
        }

        private async Task FetchAndUpload()
        {
            SetStatus("Fetching attendance from the device …");
            var records = await Task.Run(() => new LogPoller(_conn).Read(0));
            ShowRecords(records);

            if (records.Count == 0) { SetStatus("No attendance records on the device."); return; }

            if (string.IsNullOrWhiteSpace(_cfg.Upload.SiteToken))
            {
                SetStatus("Fetched " + records.Count + " record(s). (No site token set — not uploaded. Add it in Settings.)");
                return;
            }

            SetStatus("Fetched " + records.Count + " — uploading to Shikzya …");
            try
            {
                var api = new ApiClient(_cfg.Upload.ApiBaseUrl, _cfg.Upload.SiteToken);
                var upload = new PunchUpload { DeviceId = _cfg.Upload.DeviceId, Punches = records.Select(ToDto).ToArray() };
                int inserted = await api.UploadPunchesAsync(upload, CancellationToken.None);
                SetStatus("Fetched " + records.Count + ", uploaded " + inserted + " new to Shikzya.");
            }
            catch (Exception ex)
            {
                SetStatus("Fetched " + records.Count + ". Upload failed: " + ex.Message);
            }
        }

        private async Task DoSyncTime()
        {
            if (!await EnsureConnected()) return;
            SetStatus("Syncing device clock to this PC …");
            bool changed = await Task.Run(() => new TimeSyncModule(_conn).SyncIfDrift(30));
            await ShowInfo();
            SetStatus(changed ? "Device clock corrected." : "Device clock already within tolerance.");
        }

        private void OpenSettings()
        {
            using (var dlg = new SettingsForm(_cfg))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _conn?.Dispose(); _conn = null;   // reconnect with new params
                    _ = ConnectAndRefresh(false);
                }
            }
        }

        private void ShowRecords(System.Collections.Generic.List<PunchRecord> records)
        {
            _grid.Rows.Clear();
            foreach (var r in records)
                _grid.Rows.Add(r.EnrollNumber, r.PunchTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    r.VerifyLabel, r.IoMode, r.Temperature.HasValue ? (r.Temperature.Value / 10.0).ToString("0.0") : "");
        }

        private static PunchDto ToDto(PunchRecord r) => new PunchDto
        {
            EnrollNumber = r.EnrollNumber,
            PunchTime = r.PunchTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            VerifyMode = r.VerifyMode, VerifyLabel = r.VerifyLabel,
            InOutMode = r.InOutMode, IoMode = r.IoMode, DoorMode = r.DoorMode,
            Temperature = r.Temperature,
        };
    }
}
