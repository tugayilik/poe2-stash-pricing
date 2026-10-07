using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PoeStashPricer;

// Portable, offline geometric regression; contains no screenshot/profile data.
// Compile with the same sources/references as tools/detect-test.ps1, replacing
// DetectTest.cs with this file. Run without arguments to check the candidate;
// --baseline checks the expected omission against c38f8d0, --pr checks 200b695.
// This proves a learned-layout geometry failure, not the historical screenshot's cause.
// Hypothesis: overlap in (25%,30%] suppresses a detected item without snapping the
// saved hover into it. Test identical generated pixels/rectangles on all revisions.
// Narrow correction: retain detected single-item probes only; leave saved/lattice
// overlap policy intact. Blank overlapping learned positions expose the old PR cost.
// Git owns source/fixture history. Existing Windows Framework compiler suffices;
// Nix/Docker add no useful boundary to this Windows/System.Drawing-only test.
class CurrencySlotTest
{
    static bool baseline, originalPr;
    static int Main(string[] args)
    {
        try
        {
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--baseline" && args[0] != "--pr"))
                throw new Exception("Usage: CurrencySlotTest [--baseline|--pr]");
            baseline = args.Length == 1 && args[0] == "--baseline";
            originalPr = args.Length == 1 && args[0] == "--pr";
            Rectangle item = new Rectangle(60, 40, 40, 40);
            Rectangle stale = new Rectangle(31, 40, 40, 40);
            Run("learned-gap", new[] { item }, new[] { stale }, true, false, baseline ? 1 : 2, baseline ? 0 : 1);
            Run("duplicate-learned-gap", new[] { item }, new[] { stale, stale }, true, false, baseline ? 1 : 2, baseline ? 0 : 1);
            // Width 40 makes shifts 30/29/28/27 exercise 25/27.5/30/32.5%
            // overlap, including both strict thresholds in the production planner.
            foreach (int shift in new[] { 31, 30, 29, 28, 27, 20, 0 })
            {
                bool gap = shift == 29 || shift == 28;
                bool snapped = shift < 28;
                Run("overlap-shift-" + shift, new[] { item }, new[] { new Rectangle(60 - shift, 40, 40, 40) },
                    true, false, snapped || (baseline && gap) ? 1 : 2, baseline && gap ? 0 : 1);
            }
            Run("aligned-learned", new[] { item }, new[] { item }, true, false, 1, 1);
            Run("duplicate-aligned", new[] { item }, new[] { item, item }, true, false, 1, 1);
            Rectangle other = new Rectangle(140, 40, 40, 40);
            Run("learned-gap-with-neighbor", new[] { item, other }, new[] { stale, other }, true, false, baseline ? 2 : 3, baseline ? 1 : 2);
            Run("blank-overlap", new Rectangle[0], new[] { stale, item }, true, false, originalPr ? 2 : 1, 0);
            Run("no-learned", new[] { item }, null, true, false, 1, 1);
            Run("built-in-bypass", new[] { item }, new[] { stale }, true, true, 1, 0);
            Run("built-in-aligned", new[] { item }, new[] { item }, true, true, 1, 1);
            Run("gear", new[] { item }, new[] { stale }, false, false, 1, 0);
            Rectangle[] row = { new Rectangle(20, 40, 40, 40), new Rectangle(80, 40, 40, 40), new Rectangle(140, 40, 40, 40) };
            Run("learned-row-lattice", row, row, true, false, 3, 3);
            Run("ordinary-gear-row", row, null, false, false, 3, 3);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }

    static void Run(string name, Rectangle[] occupied, Rectangle[] known, bool fixedLayout, bool exact, int expectedProbes, int expectedCovered)
    {
        // Deliberately offset the screen region: learned rectangles/pixels remain local.
        Point origin = new Point(111, 207);
        using (Bitmap bitmap = new Bitmap(240, 120))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Brush tint = new SolidBrush(Color.FromArgb(8, 7, 30)))
            {
                graphics.Clear(Color.Black);
                foreach (Rectangle rectangle in occupied) graphics.FillRectangle(tint, rectangle);
            }
            ScanConfig config = new ScanConfig { Region = new Rectangle(origin, bitmap.Size), CellSize = 40, FixedLayout = fixedLayout, TintSensitivity = 8, Threshold = 12 };
            List<Point> probes = new List<Point>();
            foreach (ProbeGroup group in Grid.Plan(new PixelBuffer(bitmap), config, known == null ? null : known.ToList(), exact))
                for (int r = 0; r < group.Rows; r++)
                    for (int c = 0; c < group.Cols; c++)
                        if (group.Active[r, c])
                        {
                            Rectangle rectangle = group.Rects[r, c];
                            probes.Add(new Point(rectangle.X + rectangle.Width / 2 - origin.X, rectangle.Y + rectangle.Height / 2 - origin.Y));
                        }
            int covered = occupied.Count(rectangle => probes.Any(point => rectangle.Contains(point)));
            int repeated = occupied.Sum(rectangle => Math.Max(0, probes.Count(point => rectangle.Contains(point)) - 1));
            Console.WriteLine("{\"case\":\"" + name + "\",\"probes\":" + probes.Count + ",\"occupied\":" + occupied.Length + ",\"covered\":" + covered + ",\"repeatedOccupiedHovers\":" + repeated + "}");
            if (probes.Count != expectedProbes || covered != expectedCovered || repeated != 0)
                throw new Exception(name + ": expected probes=" + expectedProbes + ", covered=" + expectedCovered + ", no repeated occupied hovers");
        }
    }
}
