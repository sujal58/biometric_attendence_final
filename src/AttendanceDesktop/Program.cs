using System;
using System.Windows.Forms;

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
            Application.Run(new MainForm());
        }
    }
}
