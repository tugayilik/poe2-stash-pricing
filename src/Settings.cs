using System;
using System.IO;
using System.Web.Script.Serialization;

namespace PoeStashPricer
{
    public class AppSettings
    {
        public string League { get; set; }
        public string DisplayCurrency { get; set; }   // auto | divine | exalted | chaos
        public int TintSensitivity { get; set; }      // item-background detection, see SlotDetector
        public int Threshold { get; set; }            // "detailed cell" threshold, see Grid.Busyness
        public int ScanKey { get; set; }              // System.Windows.Forms.Keys incl. Ctrl/Alt/Shift (default F7)
        public int OverlayKey { get; set; }           // default F8
        public int HoverDelay { get; set; }
        public int CopyTimeout { get; set; }

        public AppSettings()
        {
            DisplayCurrency = "auto";
            TintSensitivity = 8;
            Threshold = 12;
            ScanKey = 0x76;      // Keys.F7
            OverlayKey = 0x77;   // Keys.F8
            HoverDelay = 45;
            CopyTimeout = 150;
        }

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PoeStashPricer"); }
        }

        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    AppSettings s = new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(FilePath));
                    if (s != null) return s;
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this));
            }
            catch { }
        }
    }
}
