using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace PoeStashPricer
{
    /// <summary>
    /// Finds the slots of a special stash tab from a screenshot by their frames: every slot, filled or empty, has
    /// a bronze frame around a dark (empty) or navy (filled) square. Writes an annotated picture for review and
    /// the slots as fractions of the stash area.
    /// </summary>
    static class LayoutTool
    {
        static void Main(string[] args)
        {
            if (args.Length == 4) { Batch(args[0], args[1], args[2], args[3]); return; }
            string shot = args[0], outPng = args[1];
            PixelBuffer full;
            using (Bitmap b = new Bitmap(shot)) full = new PixelBuffer(b);
            StashLocator.Result loc = StashLocator.Locate(full);
            PixelBuffer pb = full.Crop(loc.Region);
            List<Rectangle> slots = FindSlots(pb);
            Console.WriteLine("region " + loc.Region + ", " + slots.Count + " slots");
            foreach (Rectangle s in slots.OrderBy(s => s.Y).ThenBy(s => s.X)) Console.WriteLine("  " + s.X + "," + s.Y + " " + s.Width + "x" + s.Height);
            // Where the slots line up: their left and top edges, grouped when within a few pixels.
            Console.WriteLine("columns (left edge: count): " + string.Join("  ", Clusters(slots.Select(s => s.X))));
            Console.WriteLine("rows (top edge: count):     " + string.Join("  ", Clusters(slots.Select(s => s.Y))));
            Console.WriteLine("sizes (w x h: count):       " + string.Join("  ", slots.GroupBy(s => s.Width / 2 * 2 + "x" + s.Height / 2 * 2).OrderByDescending(g => g.Count()).Take(6).Select(g => g.Key + ":" + g.Count())));

            using (Bitmap bmp = new Bitmap(shot))
            using (Bitmap crop = bmp.Clone(loc.Region, bmp.PixelFormat))
            {
                using (Graphics g = Graphics.FromImage(crop))
                using (Pen pen = new Pen(Color.Lime, 2))
                    foreach (Rectangle s in slots) g.DrawRectangle(pen, s);
                crop.Save(outPng);
            }
        }

        /// <summary>
        /// Builds the built-in layouts: for every spec file (tools/layouts/NAME.txt) takes the screenshot
        /// NAME.png, finds the slots, applies the spec's corrections, draws them for review (outDir/NAME.png, with a
        /// ruler) and writes all layouts to one JSON file. Spec lines, in pixels of the stash area:
        ///   name Currency          shown name          kind Currency   what it holds
        ///   remove X Y             drop the found slot covering that point
        ///   add X Y W H            add a slot
        ///   grid X Y COLS ROWS PITCHX PITCHY W H    add a block of slots
        ///   clear                  drop everything found so far (for plain cell grids)
        ///   paged                  the tab shows one of several pages at a time (not added to the stash total)
        /// </summary>
        static void Batch(string shotDir, string specDir, string outDir, string jsonPath)
        {
            Directory.CreateDirectory(outDir);
            List<Dictionary<string, object>> layouts = new List<Dictionary<string, object>>();
            foreach (string spec in Directory.GetFiles(specDir, "*.txt").OrderBy(f => f))
            {
                string key = Path.GetFileNameWithoutExtension(spec), shot = Path.Combine(shotDir, key + ".png");
                if (!File.Exists(shot)) { Console.WriteLine(key + ": no screenshot " + shot); continue; }
                PixelBuffer full;
                using (Bitmap b = new Bitmap(shot)) full = new PixelBuffer(b);
                StashLocator.Result loc = StashLocator.Locate(full);
                PixelBuffer pb = full.Crop(loc.Region);
                List<Rectangle> slots = FindSlots(pb);
                string name = key, kind = key;
                bool paged = false;
                foreach (string raw in File.ReadAllLines(spec))
                {
                    string line = raw.Split('#')[0].Trim();
                    if (line.Length == 0) continue;
                    string[] p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    Func<int, int> n = i => int.Parse(p[i]);
                    switch (p[0])
                    {
                        case "name": name = line.Substring(5).Trim(); break;
                        case "kind": kind = line.Substring(5).Trim(); break;
                        case "clear": slots.Clear(); break;
                        case "paged": paged = true; break;
                        case "remove": slots.RemoveAll(s => s.Contains(n(1), n(2))); break;
                        case "add": slots.Add(new Rectangle(n(1), n(2), n(3), n(4))); break;
                        case "grid":
                            for (int r = 0; r < n(4); r++)
                                for (int c = 0; c < n(3); c++)
                                    slots.Add(new Rectangle(n(1) + (int)Math.Round(c * double.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture)),
                                                            n(2) + (int)Math.Round(r * double.Parse(p[6], System.Globalization.CultureInfo.InvariantCulture)), n(7), n(8)));
                            break;
                        default: throw new FormatException(spec + ": " + raw);
                    }
                }
                slots = slots.OrderBy(s => s.Y / 20).ThenBy(s => s.X).ToList();
                Console.WriteLine(string.Format("{0,-12} {1,3} slots  ({2})", key, slots.Count, name));

                using (Bitmap bmp = new Bitmap(shot))
                using (Bitmap crop = bmp.Clone(loc.Region, bmp.PixelFormat))
                {
                    using (Graphics g = Graphics.FromImage(crop))
                    using (Pen pen = new Pen(Color.Lime, 2))
                    using (Pen tick = new Pen(Color.Red, 1))
                    using (Font f = new Font("Arial", 8))
                    {
                        for (int i = 0; i < slots.Count; i++)
                        {
                            g.DrawRectangle(pen, slots[i]);
                            g.DrawString(i.ToString(), f, Brushes.Yellow, slots[i].X + 2, slots[i].Bottom - 14);
                        }
                        for (int v = 0; v < crop.Width; v += 10)
                        {
                            int len = v % 50 == 0 ? 10 : 4;
                            g.DrawLine(tick, v, 0, v, len); g.DrawLine(tick, 0, v, len, v);
                            if (v % 100 == 0) { g.DrawString(v.ToString(), f, Brushes.Red, v + 1, 10); g.DrawString(v.ToString(), f, Brushes.Red, 10, v + 1); }
                        }
                    }
                    crop.Save(Path.Combine(outDir, key + ".png"));
                }

                double W = pb.Width, H = pb.Height;
                layouts.Add(new Dictionary<string, object>
                {
                    { "Key", key }, { "Name", name }, { "Kind", kind }, { "Paged", paged },
                    { "Slots", slots.Select(s => new[] { Math.Round(s.X / W, 5), Math.Round(s.Y / H, 5), Math.Round(s.Width / W, 5), Math.Round(s.Height / H, 5) }).ToList() },
                    { "Signature", TabLibrary.Signature(pb).Select(v => Math.Round(v, 1)).ToList() },
                    { "ItemMask", TabLibrary.ItemMask(pb) }
                });
            }
            File.WriteAllText(jsonPath, new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(layouts));
            Console.WriteLine("wrote " + layouts.Count + " layouts to " + jsonPath);
        }

        static bool Bronze(byte r, byte g, byte b)
        {
            // Every slot frame has a thin light line outside a black one, measured in Currency from (87,78,64) on
            // grey-framed slots to (208,172,117) on bronze ones. The panel stone around never gets brighter than
            // about 47 (grey, 25-47), so a warm line brighter than 60 is frame.
            int lum = (r * 3 + g * 6 + b) / 10;
            return r >= g && g >= b && r - b >= 12 && r - b <= 120 && lum >= 60;
        }

        /// <summary>
        /// Slots from two sources: filled ones from their navy item background (the scanner's item areas, split
        /// at the slot borders), empty ones from their frames. A frame wins where both found the same slot.
        /// </summary>
        public static List<Rectangle> FindSlots(PixelBuffer pb)
        {
            // Size of one slot, from the item areas (as the scanner measures it).
            double cs = Grid.CellSizeFor(pb.Width);
            List<Rectangle> blobs = SlotDetector.Detect(pb, cs, 8);
            double slot = Grid.SlotSize(blobs, cs);
            List<Rectangle> slots = FramedSlots(pb, slot);
            // Item areas split at their slot borders (the scanner's split, without its lattice guesses).
            foreach (Rectangle blob in blobs)
                foreach (Rectangle cell in Grid.Split(pb, blob, slot, 8))
                {
                    bool item = SlotDetector.TintFraction(pb, cell, 8, 0) >= 0.2 || SlotDetector.RingTintFraction(pb, cell, 8) >= 0.35;
                    if (item && !slots.Any(s => Overlap(s, cell) > 0.3)) slots.Add(cell);
                }
            return slots;
        }

        static IEnumerable<string> Clusters(IEnumerable<int> values)
        {
            List<int> v = values.OrderBy(x => x).ToList();
            List<List<int>> groups = new List<List<int>>();
            foreach (int x in v)
            {
                if (groups.Count == 0 || x - groups[groups.Count - 1].Last() > 6) groups.Add(new List<int>());
                groups[groups.Count - 1].Add(x);
            }
            return groups.Select(g => (int)Math.Round(g.Average()) + ":" + g.Count);
        }

        static double Overlap(Rectangle a, Rectangle b)
        {
            Rectangle x = Rectangle.Intersect(a, b);
            return (double)x.Width * x.Height / Math.Max(1, Math.Min(a.Width * a.Height, b.Width * b.Height));
        }

        static List<Rectangle> FramedSlots(PixelBuffer pb, double slot)
        {
            // A slot frame is a light line with a black line right inside it: measured across frames in Currency
            // (bronze and grey) and Fragments (grey): "... 18 35 88 32 0 25 8 ..." (brightness). The panel stone's
            // lighter veins have no black next to them. So: bright (>= 60) with near-black (<= 12) within 3 px.
            int w = pb.Width, h = pb.Height;
            int[,] lum = new int[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = y * pb.Stride + x * 4;
                    lum[y, x] = (pb.Px[o + 2] * 3 + pb.Px[o + 1] * 6 + pb.Px[o]) / 10;
                }
            // Bronze frames (empty slots in most tabs, dark inside rather than black) are found by their colour.
            bool[,] m = new bool[h, w];
            for (int y = 3; y < h - 3; y++)
                for (int x = 3; x < w - 3; x++)
                {
                    int o = y * pb.Stride + x * 4;
                    if (Bronze(pb.Px[o + 2], pb.Px[o + 1], pb.Px[o])) { m[y, x] = true; continue; }
                    if (lum[y, x] < 60) continue;
                    for (int k = 1; k <= 3 && !m[y, x]; k++)
                        m[y, x] = lum[y, x + k] <= 12 || lum[y, x - k] <= 12 || lum[y + k, x] <= 12 || lum[y - k, x] <= 12;
                }
            // Close one-pixel breaks in the thin frame lines.
            bool[,] d = new bool[h, w];
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    d[y, x] = m[y, x] || m[y - 1, x] || m[y + 1, x] || m[y, x - 1] || m[y, x + 1];
            m = d;

            double cell = w / 12.0;
            List<Rectangle> res = new List<Rectangle>();
            int[,] label = new int[h, w];
            int next = 0;
            Stack<Point> stack = new Stack<Point>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!m[y, x] || label[y, x] != 0) continue;
                    next++;
                    int minX = x, maxX = x, minY = y, maxY = y, count = 0;
                    stack.Push(new Point(x, y));
                    label[y, x] = next;
                    while (stack.Count > 0)
                    {
                        Point p = stack.Pop();
                        count++;
                        if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                        if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = p.X + dx, ny = p.Y + dy;
                                if (nx < 0 || ny < 0 || nx >= w || ny >= h || !m[ny, nx] || label[ny, nx] != 0) continue;
                                label[ny, nx] = next;
                                stack.Push(new Point(nx, ny));
                            }
                    }
                    Rectangle box = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
                    if (Environment.GetEnvironmentVariable("LAYOUT_DEBUG") != null && box.Width >= cell * 0.5)
                    {
                        Console.WriteLine("component " + box + " pixels " + count);
                        foreach (bool ax in new[] { true, false }) Console.WriteLine("   cuts " + (ax ? "x" : "y") + ": " + string.Join(",", Cuts(m, box, ax, cell)));
                    }
                    if (box.Width < cell * 0.7 || box.Height < cell * 0.7) continue;
                    if (box.Width > cell * 9 || box.Height > cell * 9) continue;
                    foreach (Rectangle r in SplitFrames(m, box, cell, slot)) res.Add(r);
                }
            return res;
        }

        /// <summary>A frame component may hold several touching slots: cut it at the frame lines inside.</summary>
        static List<Rectangle> SplitFrames(bool[,] m, Rectangle box, double cell, double slot)
        {
            List<int> xs = Cuts(m, box, true, cell), ys = Cuts(m, box, false, cell);
            List<Rectangle> res = new List<Rectangle>();
            for (int i = 0; i + 1 < ys.Count; i++)
                for (int j = 0; j + 1 < xs.Count; j++)
                {
                    Rectangle r = Rectangle.FromLTRB(xs[j], ys[i], xs[j + 1], ys[i + 1]);
                    if (r.Width < slot * 0.88 || r.Height < cell * 0.5) continue;   // a sliver, not a slot
                    int sides = FramedSides(m, r);
                    if (sides == 15 && r.Height >= slot * 0.88) { res.Add(r); continue; }
                    // The bottom line of some frames is in shadow, darker than the rest: with the top, left and
                    // right framed, complete the square (slots are square, 1x1 or 2x2), but only for a width that
                    // is one or two slots; anything else is a piece cut wrongly.
                    bool oneOrTwo = (r.Width >= slot * 0.88 && r.Width <= slot * 1.15) || (r.Width >= slot * 1.76 && r.Width <= slot * 2.3);
                    if ((sides & 7) == 7 && r.Height < r.Width && oneOrTwo) res.Add(new Rectangle(r.X, r.Y, r.Width, r.Width));
                }
            return res;
        }

        /// <summary>Positions of frame lines across the box (its edges and the lines between touching slots).</summary>
        static List<int> Cuts(bool[,] m, Rectangle box, bool alongX, double cell)
        {
            int len = alongX ? box.Width : box.Height, across = alongX ? box.Height : box.Width;
            double[] share = new double[len];
            for (int i = 0; i < len; i++)
            {
                int n = 0;
                for (int k = 0; k < across; k++)
                {
                    int x = alongX ? box.X + i : box.X + k, y = alongX ? box.Y + k : box.Y + i;
                    if (m[y, x]) n++;
                }
                share[i] = (double)n / across;
            }
            // Peaks of frame colour, at least most of a cell apart.
            List<int> cuts = new List<int>();
            int start = alongX ? box.X : box.Y;
            for (int i = 0; i < len; i++)
            {
                if (share[i] < 0.35) continue;
                int j = i;
                while (j + 1 < len && share[j + 1] >= 0.35) j++;
                int mid = start + (i + j) / 2;
                if (cuts.Count == 0 || mid - cuts[cuts.Count - 1] >= cell * 0.5) cuts.Add(mid);
                i = j;
            }
            if (cuts.Count == 0 || cuts[0] - start > cell * 0.3) cuts.Insert(0, start);
            if (start + len - 1 - cuts[cuts.Count - 1] > cell * 0.3) cuts.Add(start + len - 1);
            return cuts;
        }

        /// <summary>
        /// Which sides of the rectangle run along a frame line (1 top, 2 left, 4 right, 8 bottom): for most
        /// positions along the side there is frame colour within a few pixels of it. A piece cut out between two
        /// slots misses a side.
        /// </summary>
        static int FramedSides(bool[,] m, Rectangle r)
        {
            int band = Math.Max(3, Math.Min(r.Width, r.Height) / 12);
            double top = Side(m, r, band, true, r.Top), left = Side(m, r, band, false, r.Left);
            double right = Side(m, r, band, false, r.Right - 1), bottom = Side(m, r, band, true, r.Bottom - 1);
            if (Environment.GetEnvironmentVariable("LAYOUT_DEBUG") != null)
                Console.WriteLine(string.Format("   rect {0}: top {1:0.00} bottom {2:0.00} left {3:0.00} right {4:0.00}", r, top, bottom, left, right));
            return (top >= 0.6 ? 1 : 0) | (left >= 0.6 ? 2 : 0) | (right >= 0.6 ? 4 : 0) | (bottom >= 0.6 ? 8 : 0);
        }

        static double Side(bool[,] m, Rectangle r, int band, bool horizontal, int at)
        {
            int h = m.GetLength(0), w = m.GetLength(1);
            int from = horizontal ? r.Left + band : r.Top + band, to = horizontal ? r.Right - band : r.Bottom - band;
            int n = 0, hit = 0;
            for (int i = from; i < to; i++)
            {
                n++;
                for (int k = -band; k <= band; k++)
                {
                    int x = horizontal ? i : at + k, y = horizontal ? at + k : i;
                    if (x >= 0 && y >= 0 && x < w && y < h && m[y, x]) { hit++; break; }
                }
            }
            return n == 0 ? 0 : (double)hit / n;
        }
    }
}
