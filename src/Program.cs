using System;
using System.Net;
using System.Windows.Forms;

namespace PoeStashPricer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Built without a target-framework config, so opt into modern TLS explicitly (1.2 | 1.3).
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 12288); }
            catch (NotSupportedException) { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // A second copy couldn't get the hotkeys (the first one holds them) and would seem dead.
            bool first;
            using (System.Threading.Mutex single = new System.Threading.Mutex(true, "PoeStashPricer.SingleInstance", out first))
            {
                if (!first)
                {
                    MessageBox.Show("PoE2 Stash Pricer is already running (look for its window or taskbar button).",
                                    "PoE2 Stash Pricer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new MainForm());
            }
        }
    }
}
