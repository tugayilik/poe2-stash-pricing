using System;
using System.IO;

namespace PoeStashPricer
{
    /// <summary>
    /// A small diagnostic log in %APPDATA%\PoeStashPricer\log.txt. When something doesn't work on
    /// someone's PC, this file shows what the app saw (hotkeys, game window, stash detection).
    /// </summary>
    public static class Log
    {
        const long MaxSize = 256 * 1024;
        static readonly object sync = new object();

        public static string FilePath { get { return Path.Combine(AppSettings.Dir, "log.txt"); } }

        public static void Write(string message)
        {
            try
            {
                lock (sync)
                {
                    Directory.CreateDirectory(AppSettings.Dir);
                    FileInfo fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > MaxSize)
                    {
                        // Keep the newer half so the file doesn't grow without end.
                        string all = File.ReadAllText(FilePath);
                        File.WriteAllText(FilePath, all.Substring(all.Length / 2));
                    }
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
