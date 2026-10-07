using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace PoeStashPricer
{
    // Independent snapshots: ordinary tabs cannot be reliably identified across scans.
    public static class ScanArchive
    {
        public static string DirectoryPath { get { return Path.Combine(AppSettings.Dir, "scan-exports"); } }

        // Probe counts are evidence of read quality, not a guarantee that all items were detected.
        public static string Quality(TabResult result, ScanResult scan)
        {
            if ((scan.CellsTried > 0 && (scan.CellsCopied == 0 || result.Items.Count == 0))
                || result.Items.Any(i => i.CountUnread)) return "read-warnings";
            return result.Items.Count == 0 ? "empty" : "read";
        }

        // prices is the captured table used for result.ValueAtScan and result.PricesAtScan.
        public static string SaveCompleted(TabResult result, PriceTable prices, ScanResult scan,
                                           Func<bool> cancelled, string directory = null)
        {
            if (scan.Aborted || cancelled()) return null;
            directory = directory ?? DirectoryPath;
            string path = Path.Combine(directory, "scan-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")
                + "-" + Guid.NewGuid().ToString("N") + ".json");
            string temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                string quality = Quality(result, scan);
                var snapshot = new { SchemaVersion = 1, AppVersion = MainForm.Version,
                    League = prices.League, Complete = quality != "read-warnings", Quality = quality,
                    scan.CellsTried, scan.CellsCopied, scan.CellsRetried, scan.CellsRecovered,
                    Result = result };
                string json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(snapshot);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }
                // Completion boundary: a cancellation sampled here prevents publication.
                // A later cancellation cannot retract this completed transaction.
                if (cancelled()) return null;
                File.Move(temporary, path); // same-directory atomic rename; never overwrite
                return path;
            }
            catch (Exception ex)
            {
                throw new IOException("Could not export scan to '" + path + "': " + ex.Message, ex);
            }
            finally
            {
                // This call owns its temp file. Crash/kill can leave .tmp files; retain those
                // for manual removal, never publish or sweep another process's active file.
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex)
                {
                    throw new IOException("Could not remove unfinished scan export '" + temporary
                        + "'; remove it manually after closing the app: " + ex.Message, ex);
                }
            }
        }
    }
}
