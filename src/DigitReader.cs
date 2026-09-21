using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;

namespace PoeStashPricer
{
    /// <summary>
    /// Reads the stack number the game prints in the top-left corner of an item.
    /// Some stackable items (Simulacrum, Shattered Triskelion...) copy without a "Stack Size" line, so the
    /// count only exists on screen. The digits are learned from the game itself: every scanned item whose
    /// text does have a stack size shows that same number on screen, which gives a sample of each digit in
    /// the game's own font at the current resolution.
    /// </summary>
    public static class DigitReader
    {
        const int GW = 8, GH = 12;           // glyphs are compared on this grid
        const int MaxSamples = 6;            // per digit
        const double MaxDistance = 18;       // above this a glyph is not considered read
        const double MinMargin = 4;          // the best digit must beat every other digit by this much

        public class Store
        {
            public Dictionary<string, List<double[]>> Glyphs { get; set; }
            public Store() { Glyphs = new Dictionary<string, List<double[]>>(); }
        }

        static Store store;
        static bool dirty;

        static string FilePath { get { return Path.Combine(AppSettings.Dir, "digits.json"); } }

        static Store Data
        {
            get
            {
                if (store == null)
                {
                    try
                    {
                        if (File.Exists(FilePath)) store = new JavaScriptSerializer().Deserialize<Store>(File.ReadAllText(FilePath));
                    }
                    catch { }
                    if (store == null || store.Glyphs == null) store = new Store();
                }
                return store;
            }
        }

        public static void Save()
        {
            if (!dirty) return;
            try
            {
                Directory.CreateDirectory(AppSettings.Dir);
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(Data));
                dirty = false;
            }
            catch { }
        }

        /// <summary>Digits already learned, e.g. "01234".</summary>
        public static string Known
        {
            get
            {
                string s = "";
                for (char d = '0'; d <= '9'; d++) if (Data.Glyphs.ContainsKey(d.ToString())) s += d;
                return s;
            }
        }

        /// <summary>Remembers how the digits of <paramref name="value"/> look in this slot.</summary>
        public static void Learn(PixelBuffer pb, Rectangle slot, double cellSize, int value)
        {
            string digits = value.ToString();
            List<double[]> glyphs = Glyphs(pb, slot, cellSize);
            if (glyphs.Count != digits.Length) return;   // e.g. "13.1K" style numbers, or covered corner
            for (int i = 0; i < digits.Length; i++)
            {
                string k = digits[i].ToString();
                List<double[]> list;
                if (!Data.Glyphs.TryGetValue(k, out list)) Data.Glyphs[k] = list = new List<double[]>();
                if (list.Count >= MaxSamples) list.RemoveAt(0);
                list.Add(glyphs[i]);
                dirty = true;
            }
        }

        /// <summary>The number in the corner of <paramref name="slot"/>, or 0 if it can't be read.</summary>
        public static int Read(PixelBuffer pb, Rectangle slot, double cellSize)
        {
            List<double[]> glyphs = Glyphs(pb, slot, cellSize);
            if (glyphs.Count == 0 || glyphs.Count > 6) return 0;
            string s = "";
            foreach (double[] g in glyphs)
            {
                // Closest digit, and how close the runner-up digit gets: a wrong count is worse than none,
                // so an ambiguous glyph means "can't read".
                string bestDigit = null;
                double best = double.MaxValue, runnerUp = double.MaxValue;
                foreach (KeyValuePair<string, List<double[]>> kv in Data.Glyphs)
                {
                    double d = double.MaxValue;
                    foreach (double[] t in kv.Value) d = Math.Min(d, Distance(g, t));
                    if (d < best) { runnerUp = best; best = d; bestDigit = kv.Key; }
                    else if (d < runnerUp) runnerUp = d;
                }
                if (bestDigit == null || best > MaxDistance || runnerUp - best < MinMargin) return 0;
                s += bestDigit;
            }
            int n;
            return int.TryParse(s, out n) ? n : 0;
        }

        static double Distance(double[] a, double[] b)
        {
            if (a.Length != b.Length) return double.MaxValue;
            double d = 0;
            for (int i = 0; i < a.Length - 1; i++) d += Math.Abs(a[i] - b[i]);
            return d + 10 * Math.Abs(a[a.Length - 1] - b[b.Length - 1]);   // last value: width / digit height
        }

        // ------------------------------------------------------------------ segmentation

        /// <summary>
        /// Finds the glyphs of the stack number in the slot's top-left corner and turns each into a GW x GH grid
        /// of ink coverage (plus its width relative to the digit height).
        /// </summary>
        static List<double[]> Glyphs(PixelBuffer pb, Rectangle slot, double cs)
        {
            List<double[]> result = new List<double[]>();
            Rectangle area = Rectangle.Intersect(new Rectangle(0, 0, pb.Width, pb.Height),
                new Rectangle(slot.X, slot.Y, (int)Math.Min(slot.Width, cs * 1.1), (int)(cs * 0.36)));
            if (area.Width < 6 || area.Height < 6) return result;

            // The digits are white with soft (anti-aliased) edges and a dark outline. Bright neutral pixels are
            // "sure" ink; paler neutral pixels count only as part of a shape that has sure ink (hysteresis), so
            // whole strokes are kept while the dark outline keeps them apart from the icon.
            bool[,] strong = new bool[area.Height, area.Width], weak = new bool[area.Height, area.Width];
            for (int y = 0; y < area.Height; y++)
                for (int x = 0; x < area.Width; x++)
                {
                    int o = (area.Y + y) * pb.Stride + (area.X + x) * 4;
                    int b = pb.Px[o], g = pb.Px[o + 1], r = pb.Px[o + 2];
                    int mn = Math.Min(r, Math.Min(g, b)), mx = Math.Max(r, Math.Max(g, b));
                    bool neutral = mx - mn <= 60;
                    strong[y, x] = neutral && mn >= 150;
                    weak[y, x] = neutral && mn >= 95;
                }

            // All digits of this font are the same height (≈19% of a cell). Find the top of the number and keep
            // exactly one digit height: whatever of the icon touches the digits from below is cut off.
            int digitH = (int)Math.Round(cs * 0.19);
            int top = -1;
            for (int y = (int)(cs * 0.03); y < Math.Min(area.Height, (int)(cs * 0.22)) && top < 0; y++)
                for (int x = 0; x < Math.Min(area.Width, (int)(cs * 0.4)); x++)
                    if (strong[y, x]) { top = y; break; }
            if (top < 0) return result;
            for (int y = 0; y < area.Height; y++)
                if (y < top || y >= top + digitH)
                    for (int x = 0; x < area.Width; x++) { weak[y, x] = false; strong[y, x] = false; }

            int[,] label;
            List<Rectangle> comps = Components(weak, out label);
            List<int> ids = new List<int>();
            for (int i = 0; i < comps.Count; i++)
            {
                Rectangle c = comps[i];
                bool sure = false;
                for (int y = c.Top; y < c.Bottom && !sure; y++)
                    for (int x = c.Left; x < c.Right && !sure; x++) sure = strong[y, x];
                // Full digit height and narrower than tall (icon fragments are often short or wide).
                if (sure && c.Height >= digitH * 0.75 && c.Width <= c.Height) ids.Add(i);
            }
            ids.Sort((p, q) => comps[p].X.CompareTo(comps[q].X));

            // The number starts near the left edge; following digits are close by.
            List<int> line = new List<int>();
            foreach (int i in ids)
            {
                Rectangle d = comps[i];
                if (line.Count == 0)
                {
                    // Must start right at the corner: otherwise a missed first digit would turn "12" into "2".
                    if (d.Left <= cs * 0.2) line.Add(i);
                    continue;
                }
                if (d.Left - comps[line[line.Count - 1]].Right <= cs * 0.08) line.Add(i);
                else break;
            }

            // Each digit is compared in a fixed window of one digit height (not stretched to its own box):
            // the font never changes, so its pixels line up, and a narrow "1" stays narrow.
            int winW = (int)Math.Round(digitH * 0.8);
            foreach (int i in line)
            {
                Rectangle d = comps[i];
                double[] f = new double[GW * GH + 1];
                for (int gy = 0; gy < GH; gy++)
                    for (int gx = 0; gx < GW; gx++)
                    {
                        int x0 = d.X + gx * winW / GW, x1 = d.X + Math.Max(gx * winW / GW + 1, (gx + 1) * winW / GW);
                        int y0 = top + gy * digitH / GH, y1 = top + Math.Max(gy * digitH / GH + 1, (gy + 1) * digitH / GH);
                        int on = 0, n = 0;
                        for (int y = y0; y < y1; y++)
                            for (int x = x0; x < x1; x++)
                            {
                                n++;
                                if (x < area.Width && y < area.Height && label[y, x] == i + 1) on++;
                            }
                        f[gy * GW + gx] = n == 0 ? 0 : (double)on / n;
                    }
                f[GW * GH] = (double)d.Width / digitH;
                result.Add(f);
            }
            return result;
        }

        /// <summary>
        /// Connected shapes (4-neighbour: a diagonal touch through soft edge pixels would glue a digit to the
        /// icon); <paramref name="label"/> holds shape index + 1 per pixel.
        /// </summary>
        static List<Rectangle> Components(bool[,] m, out int[,] label)
        {
            int h = m.GetLength(0), w = m.GetLength(1);
            label = new int[h, w];
            List<Rectangle> res = new List<Rectangle>();
            Queue<Point> q = new Queue<Point>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!m[y, x] || label[y, x] != 0) continue;
                    int id = res.Count + 1;
                    int x0 = x, x1 = x, y0 = y, y1 = y;
                    label[y, x] = id;
                    q.Enqueue(new Point(x, y));
                    while (q.Count > 0)
                    {
                        Point p = q.Dequeue();
                        x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X);
                        y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y);
                        for (int k = 0; k < 4; k++)
                            {
                                int nx = p.X + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = p.Y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                                if (nx < 0 || ny < 0 || nx >= w || ny >= h || label[ny, nx] != 0 || !m[ny, nx]) continue;
                                label[ny, nx] = id;
                                q.Enqueue(new Point(nx, ny));
                            }
                    }
                    res.Add(Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1));
                }
            return res;
        }
    }
}
