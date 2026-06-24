using System;
using System.Windows.Forms;
using AttendanceDesktop.Activation;

namespace AttendanceDesktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            if (Environment.Is64BitProcess)
            {
                MessageBox.Show("This tool must run as 32-bit (x86) because the device SDK is 32-bit.",
                    "Shikzya Device Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var cfg = DesktopConfig.Load();

            // License gate: validate online (or use the offline grace cache). If not
            // activated, show the activation screen; exit if the user can't activate.
            var gate = LicenseGate.CheckAsync(cfg).GetAwaiter().GetResult();
            if (!gate.Allowed)
            {
                using var act = new ActivationForm(cfg);
                if (act.ShowDialog() != DialogResult.OK) return;
            }

            Application.Run(new MainForm(cfg));
        }
    }
}
