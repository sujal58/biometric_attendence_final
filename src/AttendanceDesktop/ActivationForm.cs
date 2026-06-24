using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using AttendanceDesktop.Activation;

namespace AttendanceDesktop
{
    /// <summary>
    /// Shown when the app is not activated. The technician enters the school's
    /// license key (issued by the Shikzya super admin) and activates online.
    /// </summary>
    public sealed class ActivationForm : Form
    {
        private readonly DesktopConfig _cfg;
        private TextBox _url, _key;
        private Label _msg;
        private Button _activate;

        public ActivationForm(DesktopConfig cfg)
        {
            _cfg = cfg;
            Text = "Activate — Shikzya Device Tool";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(520, 230);

            Controls.Add(new Label { Text = "Enter the license key issued by your Shikzya administrator.", Location = new Point(16, 14), AutoSize = true });

            Controls.Add(new Label { Text = "Shikzya URL:", Location = new Point(16, 52), AutoSize = true });
            _url = new TextBox { Location = new Point(120, 49), Size = new Size(380, 24), Text = string.IsNullOrWhiteSpace(cfg.ApiBaseUrl) ? "https://app.shikzya.com" : cfg.ApiBaseUrl };
            Controls.Add(_url);

            Controls.Add(new Label { Text = "License key:", Location = new Point(16, 86), AutoSize = true });
            _key = new TextBox { Location = new Point(120, 83), Size = new Size(380, 24), Text = cfg.LicenseKey };
            Controls.Add(_key);

            _activate = new Button { Text = "Activate", Location = new Point(120, 122), Size = new Size(110, 32) };
            var quit = new Button { Text = "Exit", DialogResult = DialogResult.Cancel, Location = new Point(240, 122), Size = new Size(90, 32) };
            _activate.Click += async (s, e) => await DoActivate();
            Controls.Add(_activate); Controls.Add(quit);
            AcceptButton = _activate; CancelButton = quit;

            _msg = new Label { Location = new Point(16, 168), Size = new Size(490, 50), ForeColor = Color.Firebrick };
            Controls.Add(_msg);
        }

        private async Task DoActivate()
        {
            _activate.Enabled = false; _msg.ForeColor = Color.DimGray; _msg.Text = "Activating…";
            _cfg.ApiBaseUrl = _url.Text.Trim();
            _cfg.LicenseKey = _key.Text.Trim();
            _cfg.Save();

            var gate = await LicenseGate.CheckAsync(_cfg);
            if (gate.Allowed)
            {
                DialogResult = DialogResult.OK;
                return;
            }
            _msg.ForeColor = Color.Firebrick;
            _msg.Text = gate.Message;
            _activate.Enabled = true;
        }
    }
}
