using System;
using PoeStashPricer;
class RetryTest
{
    static int checks;
    static void Check(bool value, string context)
    {
        if (!value) throw new Exception("Retry regression: " + context);
        checks++;
    }
    static void Main()
    {
        // Golden values from the original retry policy, including UI hover bounds
        // and both sides of each policy threshold. No mouse or clipboard access.
        int[] hover = { 10, 44, 45, 46, 100, 500 };
        int[] occupiedHover = { 90, 90, 90, 92, 200, 1000 };
        for (int i = 0; i < hover.Length; i++)
        {
            Check(RetryTiming.HoverDelay(hover[i], true) == occupiedHover[i], "occupied hover " + hover[i]);
            Check(RetryTiming.HoverDelay(hover[i], false) == hover[i], "other hover " + hover[i]);
        }

        int[] configured = { 100, 150, 249, 250, 251, 500 };
        int[] occupiedCopy = { 250, 250, 250, 250, 251, 500 };
        int[] latency = { 0, 59, 60, 61, 73, 350 };
        int[] measuredCopy = { 120, 120, 120, 122, 146, 700 };
        for (int i = 0; i < configured.Length; i++)
            for (int j = 0; j < latency.Length; j++)
            {
                string context = " copy configured=" + configured[i] + " latency=" + latency[j];
                Check(RetryTiming.CopyTimeout(configured[i], latency[j], false, true) == occupiedCopy[i], "unmeasured occupied" + context);
                Check(RetryTiming.CopyTimeout(configured[i], latency[j], false, false) == configured[i], "unmeasured other" + context);
                Check(RetryTiming.CopyTimeout(configured[i], latency[j], true, true) == measuredCopy[j], "measured occupied" + context);
                // Scanner skips measured, unlikely cells; keep the pure policy equivalent even for this combination.
                Check(RetryTiming.CopyTimeout(configured[i], latency[j], true, false) == measuredCopy[j], "measured other" + context);
            }

        // Exercise the updater's version comparison offline, without calling Check().
        Version current = Version.Parse(MainForm.Version);
        Check(current.ToString() == MainForm.Version, "current version parses without rewriting");
        Check(!(new Version(current.ToString()) > current), "same release is not newer");
        Check(new Version(current.Major, current.Minor, current.Build + 1) > current, "next patch is newer");
        Console.WriteLine("PASS: " + checks + " offline checks; conservative retry policy and update-version compatibility. No live recovery or speed evidence.");
    }
}
