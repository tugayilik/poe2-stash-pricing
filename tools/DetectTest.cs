using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using PoeStashPricer;

/// <summary>
/// Offline check of the whole pipeline on full-screen stash screenshots in samples\:
///  1. find the stash panel, 2. learn the tab's slots (as "Kaydet" does), 3. recognise every screenshot
///  against every learned tab, also after simulated changes (items taken out, stacks changed).
/// Annotated images go to samples\out\: magenta = learned slot, green + red dot = would be hovered now.
/// Usage: DetectTest.exe <samples dir>
/// </summary>
static class DetectTest
{
    static PixelBuffer Load(string file)
    {
        using (Bitmap src = new Bitmap(file))
        using (Bitmap bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, src.Width, src.Height);
            return new PixelBuffer(bmp);
        }
    }

    /// <summary>Paints over a few filled slots with the empty-slot colour and scribbles over stack numbers.</summary>
    static PixelBuffer Disturb(PixelBuffer pb, TabProfile p)
    {
        using (Bitmap bmp = pb.ToBitmap())
        {
            List<Rectangle> slots = p.SlotsIn(new Size(pb.Width, pb.Height));
            using (Graphics g = Graphics.FromImage(bmp))
                for (int i = 0; i < slots.Count; i += 4)
                {
                    Rectangle r = slots[i];
                    if (i % 8 == 0) g.FillRectangle(new SolidBrush(Color.FromArgb(14, 14, 14)), r);          // item removed
                    else g.FillRectangle(Brushes.White, r.X + 4, r.Y + 4, r.Width / 3, r.Height / 5);    // stack changed
                }
            return new PixelBuffer(bmp);
        }
    }

    static void Main(string[] args)
    {
        string dir = args[0], outDir = Path.Combine(dir, "out");
        Directory.CreateDirectory(outDir);
        ScanConfig template = new ScanConfig { TintSensitivity = 8, Threshold = 12 };
        Dictionary<string, PixelBuffer> areas = new Dictionary<string, PixelBuffer>();
        List<TabProfile> profiles = new List<TabProfile>();
        Dictionary<string, double[]> frames = new Dictionary<string, double[]>();

        foreach (string file in Directory.GetFiles(dir, "*.png"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            PixelBuffer full = Load(file);
            StashLocator.Result loc = StashLocator.Locate(full);
            Console.WriteLine("{0,-12} {1}x{2}  stash {3} (edges {4}/4)", name, full.Width, full.Height, loc.Region, loc.EdgesFound);
            if (!loc.StashVisible) continue;

            PixelBuffer area = full.Crop(loc.Region);
            areas[name] = area;
            TabProfile p = TabLibrary.Learn(name, name, area, loc.FrameColor, template, null);
            frames[name] = loc.FrameColor;
            profiles.Add(p);

            // What a scan of this very screenshot would hover, using the learned slots.
            ScanConfig cfg = new ScanConfig { Region = new Rectangle(0, 0, area.Width, area.Height), TintSensitivity = 8, Threshold = 12, CellSize = p.CellFrac * area.Width, FixedLayout = true };
            List<ProbeGroup> groups = Grid.Plan(area, cfg, p.SlotsIn(new Size(area.Width, area.Height)));
            int probes = 0;
            using (Bitmap bmp = area.ToBitmap())
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (Pen slotPen = new Pen(Color.Magenta, 1))
                using (Pen on = new Pen(Color.Lime, 2))
                {
                    foreach (Rectangle r in p.SlotsIn(new Size(area.Width, area.Height))) g.DrawRectangle(slotPen, r);
                    foreach (ProbeGroup pg in groups)
                        for (int r = 0; r < pg.Rows; r++)
                            for (int c = 0; c < pg.Cols; c++)
                            {
                                if (!pg.Active[r, c]) continue;
                                probes++;
                                Rectangle rc = pg.Rects[r, c];
                                rc.Inflate(-3, -3);
                                g.DrawRectangle(on, rc);
                                g.FillEllipse(Brushes.Red, rc.X + rc.Width / 2 - 3, rc.Y + rc.Height / 2 - 3, 6, 6);
                            }
                }
                bmp.Save(Path.Combine(outDir, name + ".png"));
            }
            Console.WriteLine("             cell {0:0.0}px, learned slots {1}, would hover {2}", cfg.CellSize, p.Slots.Count, probes);
        }

        Console.WriteLine("\nRecognition (share of differing cells; lower = more alike, <= 0.12 counts as a match; 1.00 = frame colour differs):");
        foreach (var kv in areas)
        {
            TabProfile orig = profiles.Find(x => x.Key == kv.Key);
            foreach (var variant in new[] { "as saved", "changed" })
            {
                PixelBuffer pb = variant == "as saved" ? kv.Value : Disturb(kv.Value, orig);
                double diff;
                TabProfile hit = TabLibrary.Identify(pb, frames[kv.Key], profiles, out diff);
                string scores = "";
                foreach (TabProfile p in profiles)
                {
                    double d;
                    TabLibrary.Identify(pb, frames[kv.Key], new[] { p }, out d);
                    double dNoColor; TabLibrary.Identify(pb, null, new[] { p }, out dNoColor);
                    scores += string.Format("  {0}={1:0.00} (renksiz {2:0.00})", p.Key, d, dNoColor);
                }
                Console.WriteLine("  {0,-10} {1,-9} -> {2,-10}{3}", kv.Key, variant, hit == null ? "(none)" : hit.Key, scores);
            }
        }
    }
}
