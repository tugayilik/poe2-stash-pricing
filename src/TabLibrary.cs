using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;

namespace PoeStashPricer
{
    public class TabDef
    {
        public string Key;
        public string Name;
        public TabDef(string key, string name) { Key = key; Name = name; }
    }

    /// <summary>
    /// What we learned from the user's screenshot of one tab: where its slots are and how it looks.
    /// Everything is stored relative to the stash area, so it keeps working after a resolution change.
    /// </summary>
    public class TabProfile
    {
        public string Key { get; set; }
        public DateTime Saved { get; set; }
        public double CellFrac { get; set; }          // cell size / stash width
        public List<double[]> Slots { get; set; }     // x, y, w, h as fractions of the stash area
        public List<double> Signature { get; set; }   // coarse greyscale picture of the stash area
        public List<double> ItemMask { get; set; }    // 1 where a signature cell showed items (ignored when comparing)
        public double[] FrameColor { get; set; }      // colour of the tab's frame, or null

        public TabProfile() { Slots = new List<double[]>(); Signature = new List<double>(); }

        public List<Rectangle> SlotsIn(Size area)
        {
            List<Rectangle> res = new List<Rectangle>();
            foreach (double[] s in Slots)
                res.Add(new Rectangle((int)Math.Round(s[0] * area.Width), (int)Math.Round(s[1] * area.Height),
                                      (int)Math.Round(s[2] * area.Width), (int)Math.Round(s[3] * area.Height)));
            return res;
        }
    }

    public static class TabLibrary
    {
        public static readonly TabDef[] Tabs =
        {
            new TabDef("currency", "Currency"),
            new TabDef("fragments", "Fragments"),
            new TabDef("expedition", "Expedition"),
            new TabDef("breach", "Breach"),
            new TabDef("abyss", "Abyss"),
            new TabDef("essence", "Essence"),
            new TabDef("delirium", "Delirium"),
            new TabDef("runes", "Runes › Runes"),
            new TabDef("kalguuran", "Runes › Kalguuran Runes"),
            new TabDef("soulcores", "Runes › Soul Cores"),
            new TabDef("idols", "Runes › Idols"),
            new TabDef("augments", "Runes › Ancient Augments"),
            new TabDef("ritual", "Ritual"),
        };

        const int SigSize = 48;
        // Differences are 1 - correlation of the item-free parts of the picture (0 = identical).
        // Measured on 12 saved tabs: different tabs 0.11 (Runes vs Kalguuran Runes, same artwork) to 0.96;
        // the same tab with half its items changed and faded 0.00 to 0.65, always clearly the closest.
        const double SureMatch = 0.15;      // this close: the same tab
        const double MaxDifferent = 0.5;    // up to this: the same tab if clearly closer than any other
        const double MinMargin = 0.1;       // "clearly closer"
        const double ExactMatch = 0.05;     // near-identical picture (saving the same tab twice)
        const double MaxFrameHue = 0.15;    // frame colours further apart than this belong to different tabs

        static string Dir { get { return Path.Combine(AppSettings.Dir, "tabs"); } }

        public static string NameOf(string key)
        {
            foreach (TabDef t in Tabs) if (t.Key == key) return t.Name;
            return key;
        }

        public static Dictionary<string, TabProfile> LoadAll()
        {
            Dictionary<string, TabProfile> res = new Dictionary<string, TabProfile>();
            JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (TabDef t in Tabs)
            {
                string f = Path.Combine(Dir, t.Key + ".json");
                try
                {
                    if (!File.Exists(f)) continue;
                    TabProfile p = js.Deserialize<TabProfile>(File.ReadAllText(f));
                    // Profiles saved by an older version have no item mask: rebuild it from the saved picture.
                    string png = Path.Combine(Dir, t.Key + ".png");
                    if ((p.ItemMask == null || p.ItemMask.Count == 0) && File.Exists(png))
                    {
                        using (Bitmap bmp = new Bitmap(png))
                        {
                            PixelBuffer pb = new PixelBuffer(bmp);
                            p.Signature = Signature(pb);
                            p.ItemMask = ItemMask(pb);
                        }
                        File.WriteAllText(f, js.Serialize(p));
                    }
                    res[t.Key] = p;
                }
                catch { }
            }
            return res;
        }

        public static void Save(TabProfile p, PixelBuffer snapshot)
        {
            Directory.CreateDirectory(Dir);
            JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(Path.Combine(Dir, p.Key + ".json"), js.Serialize(p));
            // Keep the screenshot too: handy for checking what was learned.
            using (Bitmap bmp = snapshot.ToBitmap()) bmp.Save(Path.Combine(Dir, p.Key + ".png"), ImageFormat.Png);
        }

        public static void Delete(string key)
        {
            foreach (string ext in new[] { ".json", ".png" })
            {
                string f = Path.Combine(Dir, key + ext);
                if (File.Exists(f)) File.Delete(f);
            }
        }

        /// <summary>Learns a tab's slots from a clean screenshot of its stash area.</summary>
        public static TabProfile Learn(string key, PixelBuffer pb, double[] frameColor, ScanConfig template)
        {
            ScanConfig cfg = new ScanConfig
            {
                Region = new Rectangle(0, 0, pb.Width, pb.Height),
                TintSensitivity = template.TintSensitivity,
                Threshold = template.Threshold,
                FixedLayout = true   // every supported tab has fixed slots
            };
            cfg.CellSize = Grid.CellSizeFor(pb.Width);

            // Filled slots are what the scan would hover; empty ones sit on the same rows/columns
            // (the slot lattice) and look like dark squares instead of panel stone.
            List<Rectangle> slots = new List<Rectangle>();
            foreach (ProbeGroup g in Grid.Plan(pb, cfg, null))
                for (int r = 0; r < g.Rows; r++)
                    for (int c = 0; c < g.Cols; c++)
                    {
                        Rectangle rc = g.Rects[r, c];
                        bool slot = g.Active[r, c] || (g.Priority == 1 && SlotDetector.DarkFill(pb, rc, cfg.TintSensitivity) >= 0.6);
                        if (slot && !Overlaps(slots, rc)) slots.Add(rc);
                    }

            TabProfile p = new TabProfile { Key = key, Saved = DateTime.Now, CellFrac = cfg.CellSize / pb.Width };
            foreach (Rectangle rc in slots)
                p.Slots.Add(new[] { (double)rc.X / pb.Width, (double)rc.Y / pb.Height, (double)rc.Width / pb.Width, (double)rc.Height / pb.Height });
            p.Signature = Signature(pb);
            p.ItemMask = ItemMask(pb);
            p.FrameColor = frameColor;
            return p;
        }

        static bool Overlaps(List<Rectangle> list, Rectangle rc)
        {
            foreach (Rectangle t in list)
            {
                Rectangle x = Rectangle.Intersect(t, rc);
                if ((double)x.Width * x.Height > 0.25 * Math.Min(t.Width * t.Height, rc.Width * rc.Height)) return true;
            }
            return false;
        }

        /// <summary>
        /// Which saved tab is on screen. The frame colour must match, then the item-free parts of the
        /// picture (panel art, frames, empty slots) are compared: they stay the same while items come and go.
        /// </summary>
        public static TabProfile Identify(PixelBuffer pb, double[] frameColor, IEnumerable<TabProfile> profiles, out double difference)
        {
            double second;
            TabProfile best = Closest(pb, frameColor, profiles, out difference, out second);
            // A clear match, or the best by a margin (many items changed since the tab was saved).
            if (difference <= SureMatch || (difference <= MaxDifferent && second - difference >= MinMargin)) return best;
            return null;
        }

        /// <summary>Only a near-identical picture: used to warn about saving the same tab twice.</summary>
        public static TabProfile IdentifyExact(PixelBuffer pb, double[] frameColor, IEnumerable<TabProfile> profiles)
        {
            double d, second;
            TabProfile best = Closest(pb, frameColor, profiles, out d, out second);
            return d <= ExactMatch ? best : null;
        }

        static TabProfile Closest(PixelBuffer pb, double[] frameColor, IEnumerable<TabProfile> profiles, out double difference, out double second)
        {
            List<double> sig = Signature(pb), mask = ItemMask(pb);
            TabProfile best = null;
            difference = 1;
            second = 1;
            foreach (TabProfile p in profiles)
            {
                if (p.Signature == null || p.Signature.Count != sig.Count) continue;
                if (frameColor != null && p.FrameColor != null && StashLocator.ColorDistance(frameColor, p.FrameColor) > MaxFrameHue) continue;
                bool hasMask = p.ItemMask != null && p.ItemMask.Count == sig.Count;
                List<int> cells = new List<int>();
                for (int i = 0; i < sig.Count; i++)
                    if (mask[i] == 0 && !(hasMask && p.ItemMask[i] > 0)) cells.Add(i);
                if (cells.Count < sig.Count / 5) continue;   // almost everything covered by items: can't tell

                // Correlation of the item-free cells: follows the panel's pattern of light and dark, and is
                // unaffected by the game fading a tab in (darker or washed-out picture). 0 = identical.
                double d = 1 - Correlation(sig, p.Signature, cells);
                if (d < difference) { second = difference; difference = d; best = p; }
                else if (d < second) second = d;
            }
            return best;
        }

        static double Correlation(List<double> a, List<double> b, List<int> cells)
        {
            double ma = 0, mb = 0;
            foreach (int i in cells) { ma += a[i]; mb += b[i]; }
            ma /= cells.Count; mb /= cells.Count;
            double sab = 0, saa = 0, sbb = 0;
            foreach (int i in cells)
            {
                double da = a[i] - ma, db = b[i] - mb;
                sab += da * db; saa += da * da; sbb += db * db;
            }
            return saa > 0 && sbb > 0 ? sab / Math.Sqrt(saa * sbb) : 0;
        }

        /// <summary>Signature cells that show an item background (their look depends on the items, not the tab).</summary>
        public static List<double> ItemMask(PixelBuffer pb)
        {
            List<double> mask = new List<double>(SigSize * SigSize);
            for (int gy = 0; gy < SigSize; gy++)
                for (int gx = 0; gx < SigSize; gx++)
                {
                    Rectangle cell = Rectangle.FromLTRB(gx * pb.Width / SigSize, gy * pb.Height / SigSize, (gx + 1) * pb.Width / SigSize, (gy + 1) * pb.Height / SigSize);
                    mask.Add(SlotDetector.TintFraction(pb, cell, 8, 0) >= 0.15 ? 1 : 0);
                }
            return mask;
        }
        public static List<double> Signature(PixelBuffer pb)
        {
            List<double> sig = new List<double>(SigSize * SigSize);
            for (int gy = 0; gy < SigSize; gy++)
                for (int gx = 0; gx < SigSize; gx++)
                {
                    int x0 = gx * pb.Width / SigSize, x1 = (gx + 1) * pb.Width / SigSize;
                    int y0 = gy * pb.Height / SigSize, y1 = (gy + 1) * pb.Height / SigSize;
                    double sum = 0; int n = 0;
                    for (int y = y0; y < y1; y += 2)
                        for (int x = x0; x < x1; x += 2)
                        {
                            int o = y * pb.Stride + x * 4;
                            sum += 0.114 * pb.Px[o] + 0.587 * pb.Px[o + 1] + 0.299 * pb.Px[o + 2];
                            n++;
                        }
                    sig.Add(n == 0 ? 0 : Math.Round(sum / n, 1));
                }
            return sig;
        }
    }
}
