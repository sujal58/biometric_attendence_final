using System;
using System.Drawing;
using System.Windows.Forms;

namespace AttendanceDesktop
{
    /// <summary>Technician settings: which device to talk to + where to push punches.</summary>
    public sealed class SettingsForm : Form
    {
        private readonly DesktopConfig _cfg;
        private TextBox _ip, _port, _machine, _pass, _license, _api, _token, _deviceId;

        public SettingsForm(DesktopConfig cfg)
        {
            _cfg = cfg;
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(420, 430);

            int y = 16;
            AddHeader("Device (on the school's LAN)", ref y);
            _ip = AddField("IP address", _cfg.Device.Ip, ref y);
            _port = AddField("Port", _cfg.Device.Port.ToString(), ref y);
            _machine = AddField("Machine #", _cfg.Device.MachineNo.ToString(), ref y);
            _pass = AddField("Comm password", _cfg.Device.NetPassword.ToString(), ref y);
            _license = AddField("License", _cfg.Device.License.ToString(), ref y);

            y += 8;
            AddHeader("Push to Shikzya", ref y);
            _api = AddField("API base URL", _cfg.Upload.ApiBaseUrl, ref y);
            _token = AddField("Site token", _cfg.Upload.SiteToken, ref y);
            _deviceId = AddField("Device id", _cfg.Upload.DeviceId.ToString(), ref y);

            var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(230, y + 10), Size = new Size(80, 30) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(320, y + 10), Size = new Size(80, 30) };
            ok.Click += (s, e) => Apply();
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }

        private void AddHeader(string text, ref int y)
        {
            Controls.Add(new Label { Text = text, Font = new Font(Font, FontStyle.Bold), Location = new Point(14, y), AutoSize = true });
            y += 24;
        }

        private TextBox AddField(string label, string value, ref int y)
        {
            Controls.Add(new Label { Text = label, Location = new Point(20, y + 3), Size = new Size(110, 20) });
            var tb = new TextBox { Text = value, Location = new Point(140, y), Size = new Size(260, 24) };
            Controls.Add(tb);
            y += 30;
            return tb;
        }

        private static int I(TextBox t, int def) => int.TryParse(t.Text.Trim(), out var v) ? v : def;

        private void Apply()
        {
            _cfg.Device.Ip = _ip.Text.Trim();
            _cfg.Device.Port = I(_port, 5005);
            _cfg.Device.MachineNo = I(_machine, 1);
            _cfg.Device.NetPassword = I(_pass, 0);
            _cfg.Device.License = I(_license, 1261);
            _cfg.Upload.ApiBaseUrl = _api.Text.Trim();
            _cfg.Upload.SiteToken = _token.Text.Trim();
            _cfg.Upload.DeviceId = I(_deviceId, 1);
            _cfg.Save();
        }
    }
}
