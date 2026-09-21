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
            Application.Run(new MainForm());
        }
    }
}
