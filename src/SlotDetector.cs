using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PoeStashPricer
{
    /// <summary>Copy of a bitmap's pixels (BGRA) for fast repeated reads.</summary>
    public class PixelBuffer
    {
        public readonly int Width, Height, Stride;
        public readonly byte[] Px;

        public PixelBuffer(Bitmap bmp)
        {
            Width = bmp.Width;
            Height = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                Stride = data.Stride;
                Px = new byte[Stride * Height];
                Marshal.Copy(data.Scan0, Px, 0, Px.Length);
            }
            finally { bmp.UnlockBits(data); }
        }

        PixelBuffer(int w, int h)
        {
            Width = w;
            Height = h;
            Stride = w * 4;
            Px = new byte[Stride * h];
        }

        public PixelBuffer Crop(Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, Width, Height));
            PixelBuffer c = new PixelBuffer(r.Width, r.Height);
            for (int y = 0; y < r.Height; y++)
                Buffer.BlockCopy(Px, (r.Y + y) * Stride + r.X * 4, c.Px, y * c.Stride, r.Width * 4);
            return c;
        }

        public Bitmap ToBitmap()
        {
            Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < Height; y++)
                    Marshal.Copy(Px, y * Stride, data.Scan0 + y * data.Stride, Width * 4);
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }
    }

    /// <summary>
    /// Finds items anywhere in the stash area, independent of the tab's layout.
    /// PoE2 paints a tinted background (navy; green/red for special states) behind every item,
    /// while empty slots, frames and the panel are neutral grey/brown. Each connected tinted
    /// area is one item (or, in grid tabs, a group of touching items), so special tabs
    /// (currency, fragments, essences...) need no grid alignment.
    /// </summary>
    public static class SlotDetector
    {
        static bool IsTinted(int r, int g, int b, int s)
        {
            // Navy item background is very dark with blue clearly on top (≈ 8,7,30). Some tab panels are
            // bluish stone (≈ 33,33,43), so red/green must stay low for a pixel to count.
            int rg = Math.Max(r, g);
            if (b - rg >= s + 4 && rg <= 22 && b >= 18) return true;
            // Very dark variant (Expedition's top row ≈ 9,5,17): red/green almost black, blue just above.
            if (b - rg >= 7 && rg <= 12 && b >= 14) return true;
            // Lighter, purplish variant under the glow of big smoky icons (Delirium ≈ 26,21,33 / 21,12,30).
            // Kept narrow: bluish panel stone (≈ 33,33,43) has more red/green and must not count.
            if (rg <= 28 && b >= 26 && b <= 40 && b - rg >= 5 && r >= g - 2) return true;
            if (b - rg >= s * 2 && b >= 60) return true;                               // bright blue icon pixels
            // Green item background is dark and pure (≈ 3,29,2); faded green placeholder icons are greyish.
            if (g - Math.Max(r, b) >= s + 7 && g >= 20 && Math.Max(r, b) <= 12) return true;
            if (r - Math.Max(g, b) >= s * 3 && r >= 50 && g < r * 0.6) return true;    // red: unusable
            return false;
        }

        /// <summary>Share of tinted pixels in the rectangle's inner area (0..1).</summary>
        public static double TintFraction(PixelBuffer pb, Rectangle rect, int sensitivity)
        {
            return TintFraction(pb, rect, sensitivity, 6);
        }

        /// <summary>
        /// Share of tinted pixels in the outer band of the rectangle. A big bright icon can cover most of a
        /// slot, but the item background still shows as a frame around it.
        /// </summary>
        public static double RingTintFraction(PixelBuffer pb, Rectangle rect, int sensitivity)
        {
            int bx = Math.Max(1, rect.Width / 7), by = Math.Max(1, rect.Height / 7);
            Rectangle inner = Rectangle.Inflate(rect, -bx, -by);
            int tinted = 0, n = 0;
            for (int y = Math.Max(0, rect.Top); y < Math.Min(pb.Height, rect.Bottom); y++)
                for (int x = Math.Max(0, rect.Left); x < Math.Min(pb.Width, rect.Right); x++)
                {
                    if (inner.Contains(x, y)) continue;
                    int o = y * pb.Stride + x * 4;
                    n++;
                    if (IsTinted(pb.Px[o + 2], pb.Px[o + 1], pb.Px[o], sensitivity)) tinted++;
                }
            return n == 0 ? 0 : (double)tinted / n;
        }

        /// <param name="marginDiv">Ignore rect.Width / marginDiv on each side (0 = whole rectangle).</param>
        public static double TintFraction(PixelBuffer pb, Rectangle rect, int sensitivity, int marginDiv)
        {
            int mx = marginDiv > 0 ? rect.Width / marginDiv : 0, my = marginDiv > 0 ? rect.Height / marginDiv : 0;
            int x0 = Math.Max(0, rect.Left + mx), x1 = Math.Min(pb.Width, rect.Right - mx);
            int y0 = Math.Max(0, rect.Top + my), y1 = Math.Min(pb.Height, rect.Bottom - my);
            int tinted = 0, n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int o = y * pb.Stride + x * 4;
                    n++;
                    if (IsTinted(pb.Px[o + 2], pb.Px[o + 1], pb.Px[o], sensitivity)) tinted++;
                }
            return n == 0 ? 0 : (double)tinted / n;
        }

        /// <param name="cellSize">Approximate size of one inventory cell in pixels (block size and noise filtering).</param>
        /// <param name="sensitivity">Minimum channel dominance for a pixel to count as tinted (default 8).</param>
        public static List<Rectangle> Detect(PixelBuffer pb, double cellSize, int sensitivity)
        {
            int bs = Math.Max(2, (int)Math.Round(cellSize / 12));
            int bw = pb.Width / bs, bh = pb.Height / bs;
            bool[,] on = new bool[bh, bw];

            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int tinted = 0, n = 0;
                    for (int y = by * bs; y < by * bs + bs; y++)
                        for (int x = bx * bs; x < bx * bs + bs; x++)
                        {
                            int o = y * pb.Stride + x * 4;
                            n++;
                            if (IsTinted(pb.Px[o + 2], pb.Px[o + 1], pb.Px[o], sensitivity)) tinted++;
                        }
                    on[by, bx] = tinted * 4 >= n;   // at least a quarter of the block
                }

            // Big icons can cover the whole slot, leaving only a thin, broken navy ring visible.
            // A morphological closing (dilate then erode) turns that ring back into one solid area.
            on = Erode(Dilate(on));

            // Connected components (4-neighbour, so diagonal touches between slots don't merge them).
            int[,] label = new int[bh, bw];
            List<Rectangle> result = new List<Rectangle>();
            int minSide = (int)(cellSize * 0.5);
            int next = 0;
            Queue<Point> q = new Queue<Point>();
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    if (!on[y, x] || label[y, x] != 0) continue;
                    next++;
                    int x0 = x, x1 = x, y0 = y, y1 = y, count = 0;
                    label[y, x] = next;
                    q.Enqueue(new Point(x, y));
                    while (q.Count > 0)
                    {
                        Point p = q.Dequeue();
                        count++;
                        x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X);
                        y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y);
                        Visit(on, label, q, next, p.X + 1, p.Y);
                        Visit(on, label, q, next, p.X - 1, p.Y);
                        Visit(on, label, q, next, p.X, p.Y + 1);
                        Visit(on, label, q, next, p.X, p.Y - 1);
                    }
                    Rectangle r = new Rectangle(x0 * bs, y0 * bs, (x1 - x0 + 1) * bs, (y1 - y0 + 1) * bs);
                    // Ignore specks and thin strips (text, frame highlights).
                    if (r.Width < minSide || r.Height < minSide) continue;
                    // An item's background is a solid rectangle; sparse blobs are noise.
                    if (count * bs * bs < r.Width * r.Height * 0.2) continue;
                    result.Add(r);
                }
            return result;
        }

        /// <summary>
        /// Share of "slot-like" pixels: near-black (empty slot) or item background. Panel stone and
        /// frames are lighter, so a crossing that falls on the panel scores low.
        /// </summary>
        public static double DarkFill(PixelBuffer pb, Rectangle rect, int sensitivity)
        {
            int mx = rect.Width / 8, my = rect.Height / 8;
            int x0 = Math.Max(0, rect.Left + mx), x1 = Math.Min(pb.Width, rect.Right - mx);
            int y0 = Math.Max(0, rect.Top + my), y1 = Math.Min(pb.Height, rect.Bottom - my);
            int hit = 0, n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int o = y * pb.Stride + x * 4;
                    int b = pb.Px[o], g = pb.Px[o + 1], r = pb.Px[o + 2];
                    n++;
                    if (Math.Max(r, Math.Max(g, b)) <= 26 || IsTinted(r, g, b, sensitivity)) hit++;
                }
            return n == 0 ? 0 : (double)hit / n;
        }
        static bool[,] Dilate(bool[,] m)
        {
            int h = m.GetLength(0), w = m.GetLength(1);
            bool[,] o = new bool[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    o[y, x] = m[y, x] || (x > 0 && m[y, x - 1]) || (x < w - 1 && m[y, x + 1]) || (y > 0 && m[y - 1, x]) || (y < h - 1 && m[y + 1, x]);
            return o;
        }

        static bool[,] Erode(bool[,] m)
        {
            int h = m.GetLength(0), w = m.GetLength(1);
            bool[,] o = new bool[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    o[y, x] = m[y, x] && (x == 0 || m[y, x - 1]) && (x == w - 1 || m[y, x + 1]) && (y == 0 || m[y - 1, x]) && (y == h - 1 || m[y + 1, x]);
            return o;
        }

        static void Visit(bool[,] on, int[,] label, Queue<Point> q, int id, int x, int y)
        {
            if (x < 0 || y < 0 || y >= on.GetLength(0) || x >= on.GetLength(1)) return;
            if (!on[y, x] || label[y, x] != 0) return;
            label[y, x] = id;
            q.Enqueue(new Point(x, y));
        }
    }
}
