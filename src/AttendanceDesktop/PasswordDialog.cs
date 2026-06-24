using System;
using System.Drawing;
using System.Windows.Forms;

namespace AttendanceDesktop
{
    /// <summary>Prompt to set a new password (with confirm) or enter an existing one.</summary>
    public sealed class PasswordDialog : Form
    {
        private readonly bool _confirm;
        private TextBox _p1, _p2;
        public string Password => _p1.Text;

        public PasswordDialog(string title, string prompt, bool confirm)
        {
            _confirm = confirm;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(360, confirm ? 170 : 130);

            Controls.Add(new Label { Text = prompt, Location = new Point(14, 12), AutoSize = true });
            Controls.Add(new Label { Text = "Password:", Location = new Point(14, 44), AutoSize = true });
            _p1 = new TextBox { Location = new Point(110, 41), Size = new Size(230, 24), UseSystemPasswordChar = true };
            Controls.Add(_p1);

            int y = 41;
            if (confirm)
            {
                Controls.Add(new Label { Text = "Confirm:", Location = new Point(14, 76), AutoSize = true });
                _p2 = new TextBox { Location = new Point(110, 73), Size = new Size(230, 24), UseSystemPasswordChar = true };
                Controls.Add(_p2);
                y = 73;
            }

            var ok = new Button { Text = "OK", Location = new Point(170, y + 38), Size = new Size(80, 30) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(260, y + 38), Size = new Size(80, 30) };
            ok.Click += (s, e) => OnOk();
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }

        private void OnOk()
        {
            if (string.IsNullOrEmpty(_p1.Text)) { MessageBox.Show("Enter a password.", Text); return; }
            if (_confirm && _p1.Text != _p2.Text) { MessageBox.Show("Passwords do not match.", Text); return; }
            DialogResult = DialogResult.OK;
        }
    }
}
