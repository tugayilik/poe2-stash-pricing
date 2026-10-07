using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using PoeStashPricer;

class ArchiveTest
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static Dictionary<string, object> Read(string path)
    { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path)); }
    static void Main(string[] args)
    {
        string dir = Path.GetFullPath(args[1]);
        var prices = new PriceTable { League = "Test league", LoadedAt = new DateTime(2026, 10, 7, 6, 0, 0, DateTimeKind.Utc) };
        var scan = new ScanResult { CellsTried = 3, CellsCopied = 1, CellsRetried = 2, CellsRecovered = 1 };
        var result = new TabResult { Key = "_unknown", ScannedAt = DateTime.Now };
        result.Items.Add(new SavedItem { Text = "Rarity: Rare\r\nTest Armour\r\n+80 to maximum Life\r\nUnicode: æ", X = 0.25, Y = 0.5, W = 0.1, H = 0.2, Count = 2 });
        if (args[0] == "crash")
        {
            int samples = 0;
            ScanArchive.SaveCompleted(result, prices, scan, () => { if (++samples == 2) Environment.Exit(23); return false; }, dir);
            throw new Exception("Crash did not execute");
        }
        if (args[0] == "write")
        {
            Check(ScanArchive.DirectoryPath == Path.Combine(AppSettings.Dir, "scan-exports"), "Wrong default location");
            Check(ScanArchive.Quality(result, scan) == "read", "Empty probes incorrectly classified as failed reads");
            string a = ScanArchive.SaveCompleted(result, prices, scan, () => false, dir);
            result.Items[0].Text = "Second tab";
            string b = ScanArchive.SaveCompleted(result, prices, scan, () => false, dir);
            Check(a != b && File.Exists(a) && File.Exists(b), "Scans overwrite each other");
            scan.Aborted = true;
            Check(ScanArchive.SaveCompleted(result, prices, scan, () => false, dir) == null, "Aborted scan exported");
            scan.Aborted = false;
            Check(ScanArchive.SaveCompleted(result, prices, scan, () => true, dir) == null, "Initial cancellation exported");
            int samples = 0;
            Check(ScanArchive.SaveCompleted(result, prices, scan, () => ++samples == 2, dir) == null, "Late cancellation exported");
            Check(samples == 2 && Directory.GetFiles(dir, "*.json").Length == 2 && Directory.GetFiles(dir, "*.tmp").Length == 0, "Cancellation left a file");
            bool failed = false;
            try { ScanArchive.SaveCompleted(result, prices, scan, () => false, a); }
            catch (IOException ex) { failed = ex.Message.Contains(a); }
            Check(failed, "Destination error lacks context");
            samples = 0; failed = false;
            try { ScanArchive.SaveCompleted(result, prices, scan, () => { if (++samples == 2) throw new IOException("Injected failure after flush"); return false; }, dir); }
            catch (IOException ex) { failed = ex.Message.Contains(dir) && ex.Message.Contains("Injected failure"); }
            Check(failed && Directory.GetFiles(dir, "*.json").Length == 2 && Directory.GetFiles(dir, "*.tmp").Length == 0, "Failure before publication left final JSON or temp");
            string blocked = Path.Combine(dir, "blocked"); samples = 0; failed = false;
            try
            {
                ScanArchive.SaveCompleted(result, prices, scan, () => {
                    if (++samples == 2) {
                        string temporary = Directory.GetFiles(blocked, "*.tmp")[0];
                        Directory.CreateDirectory(temporary.Substring(0, temporary.Length - 4));
                    }
                    return false;
                }, blocked);
            }
            catch (IOException ex) { failed = ex.Message.Contains(blocked); }
            Check(failed && Directory.GetFiles(blocked).Length == 0, "Rename failure published a file or left temp");
            var empty = new TabResult { Key = "_unknown", ScannedAt = DateTime.Now };
            string emptyPath = ScanArchive.SaveCompleted(empty, prices, new ScanResult(), () => false, dir);
            var data = Read(emptyPath);
            Check((bool)data["Complete"] && (string)data["Quality"] == "empty", "Empty not distinguished");
            string unreadPath = ScanArchive.SaveCompleted(empty, prices, new ScanResult { CellsTried = 2 }, () => false, dir);
            data = Read(unreadPath);
            Check(!(bool)data["Complete"] && (string)data["Quality"] == "read-warnings", "Unread scan claims empty/complete");
            Check(ScanArchive.Quality(empty, new ScanResult { CellsTried = 2, CellsCopied = 2 }) == "read-warnings", "Parse failure claims complete");
            result.Items[0].CountUnread = true;
            Check(ScanArchive.Quality(result, scan) == "read-warnings", "Unread count not disclosed");
            // The user selects another league while its asynchronous price load is pending.
            // StartScan still captures the loaded table, even if a new table arrives before export.
            var settings = new AppSettings { League = "Pending league" };
            PriceTable currentPrices = prices;
            PriceTable capturedPrices = currentPrices;
            var pendingResult = new TabResult { Key = "_unknown", ScannedAt = DateTime.Now };
            pendingResult.Items.Add(new SavedItem { Text = "Rarity: Rare\r\nTest Armour\r\n+80 to maximum Life", Count = 1 });
            pendingResult.ValueAtScan = ResultStore.Total(pendingResult, capturedPrices);
            pendingResult.PricesAtScan = capturedPrices.LoadedAt;
            currentPrices = new PriceTable { League = settings.League, LoadedAt = prices.LoadedAt.AddMinutes(1) };
            string pendingPath = ScanArchive.SaveCompleted(pendingResult, capturedPrices, scan, () => false, Path.Combine(dir, "pending-league"));
            data = Read(pendingPath);
            var pendingSaved = new JavaScriptSerializer().ConvertToType<TabResult>(data["Result"]);
            Check((string)data["League"] == capturedPrices.League && (string)data["League"] != settings.League,
                "Pending league selection replaced the captured pricing league");
            Check(pendingSaved.PricesAtScan == capturedPrices.LoadedAt && pendingSaved.PricesAtScan != currentPrices.LoadedAt,
                "Later price load replaced the captured price timestamp");
            Check(pendingSaved.ValueAtScan == pendingResult.ValueAtScan, "Export changed the captured scan value");
            string crashDir = Path.Combine(dir, "crash");
            using (var child = Process.Start(new ProcessStartInfo(typeof(ArchiveTest).Assembly.Location, "crash \"" + crashDir + "\"") { UseShellExecute = false, CreateNoWindow = true }))
            {
                child.WaitForExit();
                Check(child.ExitCode == 23, "Crash child did not stop at boundary");
            }
            Check(Directory.GetFiles(crashDir, "*.json").Length == 0 && Directory.GetFiles(crashDir, "*.tmp").Length == 1, "Crash exposed completed JSON");
            ScanArchive.SaveCompleted(result, prices, scan, () => false, crashDir);
            Check(Directory.GetFiles(crashDir, "*.json").Length == 1 && Directory.GetFiles(crashDir, "*.tmp").Length == 1, "Subsequent write published or removed orphan temp");
        }
        else if (args[0] == "read")
        {
            bool first = false, second = false;
            foreach (string path in Directory.GetFiles(dir, "*.json"))
            {
                var data = Read(path);
                Check((string)data["League"] == "Test league" && (string)data["AppVersion"] == MainForm.Version, "Missing metadata");
                var saved = new JavaScriptSerializer().ConvertToType<TabResult>(data["Result"]);
                if (saved.Items.Count == 0) continue;
                first |= saved.Items[0].Text.Contains("+80 to maximum Life\r\nUnicode: æ");
                second |= saved.Items[0].Text == "Second tab";
                Check((bool)data["Complete"] && (int)data["CellsTried"] == 3 && (int)data["CellsRecovered"] == 1, "Lost scan counters");
                Check(saved.Items[0].Count == 2 && saved.Items[0].X == 0.25 && saved.Items[0].Y == 0.5 && saved.Items[0].W == 0.1 && saved.Items[0].H == 0.2, "Lost item details");
                Check(saved.ScannedAt.Year == DateTime.Now.Year, "Lost scan timestamp");
            }
            Check(first && second, "Snapshots did not survive separate process");
        }
        else throw new Exception("Unknown mode");
        Console.WriteLine("PASS " + args[0] + ": " + checks + " assertions");
    }
}
