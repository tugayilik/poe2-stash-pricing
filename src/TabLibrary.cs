using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace PoeStashPricer
{
    /// <summary>
    /// What was learned about one stash tab on its first scan: its name, where its slots are and how it looks.
    /// Everything is stored relative to the stash area, so it keeps working after a resolution change.
    /// </summary>
    public class TabProfile
    {
        public string Key { get; set; }
        public string Name { get; set; }              // shown in the app, e.g. "Essence" (guessed from the items)
        public string Kind { get; set; }              // the guess from the items when learned (Name can be renamed); null before 1.3
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
        // Names of tabs saved by versions before 1.3 (which had a fixed list and stored no name).
        static readonly Dictionary<string, string> LegacyNames = new Dictionary<string, string>
        {
            { "currency", "Currency" }, { "fragments", "Fragments" }, { "expedition", "Expedition" },
            { "breach", "Breach" }, { "abyss", "Abyss" }, { "essence", "Essence" }, { "delirium", "Delirium" },
            { "runes", "Runes" }, { "kalguuran", "Kalguuran Runes" }, { "soulcores", "Soul Cores" },
            { "idols", "Idols" }, { "augments", "Ancient Augments" }, { "ritual", "Ritual" },
        };

        // poe.ninja category of the items -> name of the special tab that holds them.
        static readonly Dictionary<string, string> CategoryTabs = new Dictionary<string, string>
        {
            { "Currency", "Currency" }, { "Fragments", "Fragments" }, { "Essences", "Essence" },
            { "Delirium", "Delirium" }, { "Ritual", "Ritual" }, { "Expedition", "Expedition" },
            { "Breach", "Breach" }, { "Abyss", "Abyss" }, { "Runes", "Runes" }, { "SoulCores", "Soul Cores" },
            { "Idols", "Idols" }, { "UncutGems", "Gems" }, { "LineageSupportGems", "Gems" },
            { "Verisium", "Expedition" },   // Verisium and alloys sit in the Expedition tab
        };

        static readonly Dictionary<string, string> names = new Dictionary<string, string>();

        const int SigSize = 48;
        // Differences are 1 - correlation of the item-free parts of the picture (0 = identical).
        // Measured on 12 saved tabs: different tabs 0.10 (Runes vs Kalguuran Runes, same artwork) to 0.96;
        // the same tab, captured after the fade-in, 0.00 to 0.03 (0.65 when half faded, before CaptureStable).
        // The sub-tabs of Runes share frame colour and artwork, so a loose match took Soul Cores for Runes.
        public const double SureMatch = 0.07;   // this close: the same tab
        const double MaxDifferent = 0.3;    // up to this: the same tab if clearly closer than another candidate
        const double MinMargin = 0.1;       // "clearly closer"
        const double ExactMatch = 0.05;     // near-identical picture (saving the same tab twice)
        const double MaxFrameHue = 0.15;    // frame colours further apart than this belong to different tabs

        static string Dir { get { return Path.Combine(AppSettings.Dir, "tabs"); } }

        public static string NameOf(string key)
        {
            string n;
            if (key != null && names.TryGetValue(key, out n)) return n;
            if (key != null && LegacyNames.TryGetValue(key, out n)) return n;
            return key;
        }

        public static Dictionary<string, TabProfile> LoadAll()
        {
            Dictionary<string, TabProfile> res = new Dictionary<string, TabProfile>();
            if (!Directory.Exists(Dir)) return res;
            JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (string f in Directory.GetFiles(Dir, "*.json"))
            {
                string key = Path.GetFileNameWithoutExtension(f);
                try
                {
                    TabProfile p = js.Deserialize<TabProfile>(File.ReadAllText(f));
                    p.Key = key;
                    if (string.IsNullOrEmpty(p.Name)) p.Name = NameOf(key);
                    // Profiles saved by an older version have no item mask: rebuild it from the saved picture.
                    string png = Path.Combine(Dir, key + ".png");
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
                    // Versions before 1.2 kept a screenshot of every tab; everything needed is in the JSON now.
                    if (File.Exists(png) && p.ItemMask != null && p.ItemMask.Count > 0) File.Delete(png);
                    names[key] = p.Name;
                    res[key] = p;
                }
                catch { }
            }
            return res;
        }

        /// <summary>
        /// Stores what was learned (slot positions, signature, frame colour). The screenshot itself is not
        /// kept: nothing needs it later, and no pictures of the user's stash are left on disk.
        /// </summary>
        public static void Save(TabProfile p)
        {
            Directory.CreateDirectory(Dir);
            JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(Path.Combine(Dir, p.Key + ".json"), js.Serialize(p));
            names[p.Key] = p.Name;
            string png = Path.Combine(Dir, p.Key + ".png");   // left by versions before 1.2
            if (File.Exists(png)) File.Delete(png);
        }

        /// <summary>
        /// Adds the places where items were found that the tab doesn't know yet (slots that were empty when it
        /// was learned), so later scans hover them even when they look empty. Returns how many were added.
        /// </summary>
        public static int AddSlots(TabProfile p, IEnumerable<Rectangle> found, Size area)
        {
            if (area.Width <= 0 || area.Height <= 0) return 0;
            List<Rectangle> known = p.SlotsIn(area);
            int added = 0;
            foreach (Rectangle f in found)
            {
                bool isKnown = false;
                foreach (Rectangle k in known)
                {
                    Rectangle x = Rectangle.Intersect(k, f);
                    if ((double)x.Width * x.Height > 0.25 * Math.Min(k.Width * k.Height, f.Width * f.Height)) { isKnown = true; break; }
                }
                if (isKnown) continue;
                known.Add(f);
                p.Slots.Add(new[] { (double)f.X / area.Width, (double)f.Y / area.Height, (double)f.Width / area.Width, (double)f.Height / area.Height });
                added++;
            }
            return added;
        }

        public static void Delete(string key)
        {
            foreach (string ext in new[] { ".json", ".png" })
            {
                string f = Path.Combine(Dir, key + ext);
                if (File.Exists(f)) File.Delete(f);
            }
            names.Remove(key);
        }

        /// <summary>Removes every saved tab (and screenshots older versions kept).</summary>
        public static void DeleteAll()
        {
            names.Clear();
            if (!Directory.Exists(Dir)) return;
            foreach (string f in Directory.GetFiles(Dir, "*.json")) File.Delete(f);
            foreach (string f in Directory.GetFiles(Dir, "*.png")) File.Delete(f);
        }

        /// <summary>
        /// Decides from the first scan of an unknown tab whether it is a special (fixed-slot) tab worth learning,
        /// and what to call it. Returns the tab name, or null with the reason in <paramref name="why"/>.
        /// Normal and quad tabs are not learned: they can hold two stacks of the same item, which the
        /// "one slot per item type" logic of saved tabs would count once.
        /// </summary>
        public static string GuessSpecialTab(ScanResult res, Rectangle region, out string why)
        {
            List<ScanItem> items = res.Items;
            List<ScanItem> priced = items.Where(i => i.Price != null).ToList();
            double cellSize = region.Width / 12.0;
            if (priced.Count == 0) { why = "no priced items to tell which tab this is"; return null; }
            if (priced.Count < 3)
            {
                // A nearly empty tab (a couple of Breach splinters) can still be told apart: its items are of a
                // kind that only that special tab holds, and they sit off the grid a normal tab would put them on.
                if (priced.Count < items.Count || priced.Any(i => !CategoryTabs.ContainsKey(i.Price.Category) || !OwnTab.Contains(i.Price.Category)))
                { why = "too few priced items to tell which tab this is"; return null; }
                if (items.Any(i => OnGrid(i.Bounds, region)))
                { why = "too few items, and they sit on a normal tab's grid"; return null; }
            }
            if (items.Count - priced.Count > items.Count * 0.3) { why = "many unpriced items (gear): a normal tab"; return null; }

            // The same item at two places apart: a normal tab (special tabs have one slot per item type).
            // Neighbouring reads of one item don't count: they can be the cells of one big (2x2) slot.
            for (int a = 0; a < items.Count; a++)
                for (int b = a + 1; b < items.Count; b++)
                {
                    if (items[a].Text != items[b].Text) continue;
                    double dx = (items[a].Bounds.X + items[a].Bounds.Width / 2.0) - (items[b].Bounds.X + items[b].Bounds.Width / 2.0);
                    double dy = (items[a].Bounds.Y + items[a].Bounds.Height / 2.0) - (items[b].Bounds.Y + items[b].Bounds.Height / 2.0);
                    if (Math.Sqrt(dx * dx + dy * dy) >= cellSize * 2.5) { why = "the same item is in two places: a normal tab"; return null; }
                }

            // Items on a regular 12x12 or 24x24 grid: a normal or quad tab.
            foreach (double cell in new[] { region.Width / 12.0, region.Width / 24.0 })
            {
                int aligned = 0;
                foreach (ScanItem si in items)
                {
                    double fx = ((si.Bounds.X - region.X) / cell) % 1, fy = ((si.Bounds.Y - region.Y) / cell) % 1;
                    if ((fx < 0.12 || fx > 0.88) && (fy < 0.12 || fy > 0.88)) aligned++;
                }
                if (items.Count >= 5 && aligned >= items.Count * 0.8) { why = "items sit on a regular grid: a normal or quad tab"; return null; }
            }

            // Named after the kind of item most of it holds. Omens (poe.ninja: Ritual) also have their own
            // place in the Abyss tab, so they count half: Abyss bones next to Abyss omens make it Abyss.
            var best = priced.Where(i => CategoryTabs.ContainsKey(i.Price.Category))
                             .GroupBy(i => CategoryTabs[i.Price.Category])
                             .OrderByDescending(g => g.Sum(i => i.Price.Category == "Ritual" ? 0.5 : 1.0)).FirstOrDefault();
            if (best == null) { why = "its items don't belong to a special tab"; return null; }
            why = null;
            return best.Key;
        }

        // Categories whose items have a place only in their own special tab (Currency, Omens and gems also
        // turn up in other tabs).
        static readonly HashSet<string> OwnTab = new HashSet<string>
        {
            "Fragments", "Essences", "Delirium", "Expedition", "Verisium", "Breach", "Abyss", "Runes", "SoulCores", "Idols"
        };

        /// <summary>True when the item sits exactly on the cells of a normal (12 wide) or quad (24 wide) tab.</summary>
        static bool OnGrid(Rectangle bounds, Rectangle region)
        {
            foreach (double cell in new[] { region.Width / 12.0, region.Width / 24.0 })
            {
                double fx = ((bounds.X - region.X) / cell) % 1, fy = ((bounds.Y - region.Y) / cell) % 1;
                if ((fx < 0.12 || fx > 0.88) && (fy < 0.12 || fy > 0.88)) return true;
            }
            return false;
        }

        /// <summary>What a saved tab holds: the guess made when it was learned, or its name for older profiles.</summary>
        public static string KindOf(TabProfile p)
        {
            if (!string.IsNullOrEmpty(p.Kind)) return p.Kind;
            string n = p.Name ?? "";
            int sp = n.LastIndexOf(' ');
            if (sp > 0 && n.Substring(sp + 1).All(char.IsDigit)) n = n.Substring(0, sp);   // "Runes 2" -> "Runes"
            return n;
        }

        /// <summary>A free key and display name for a new tab called <paramref name="name"/> ("Runes", "Runes 2"...).</summary>
        public static void NewKey(string name, ICollection<string> existingKeys, out string key, out string displayName)
        {
            string slug = new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            key = slug;
            displayName = name;
            for (int n = 2; existingKeys.Contains(key); n++)
            {
                key = slug + n;
                displayName = name + " " + n;
            }
        }

        public static void Rename(TabProfile p, string name)
        {
            p.Name = name;
            Save(p);
        }

        /// <summary>
        /// Learns a tab from its first scan: <paramref name="pb"/> is the clean screenshot of the stash area and
        /// <paramref name="itemSlots"/> (area coordinates) where items were actually read, which are certain slots.
        /// </summary>
        public static TabProfile Learn(string key, string name, PixelBuffer pb, double[] frameColor, ScanConfig template, IEnumerable<Rectangle> itemSlots)
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
            if (itemSlots != null)
                foreach (Rectangle rc in itemSlots)
                    if (!Overlaps(slots, rc)) slots.Add(rc);
            foreach (ProbeGroup g in Grid.Plan(pb, cfg, null))
                for (int r = 0; r < g.Rows; r++)
                    for (int c = 0; c < g.Cols; c++)
                    {
                        Rectangle rc = g.Rects[r, c];
                        bool slot = g.Active[r, c] || (g.Priority == 1 && SlotDetector.DarkFill(pb, rc, cfg.TintSensitivity) >= 0.6);
                        if (slot && !Overlaps(slots, rc)) slots.Add(rc);
                    }

            TabProfile p = new TabProfile { Key = key, Name = name, Saved = DateTime.Now, CellFrac = cfg.CellSize / pb.Width };
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
            // A clear match, or clearly the closest of several look-alikes. With a single candidate a loose
            // match is not trusted: an unsaved sub-tab (Soul Cores next to a saved Runes) looks alike too.
            if (difference <= SureMatch || (difference <= MaxDifferent && second < 1 && second - difference >= MinMargin)) return best;
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
