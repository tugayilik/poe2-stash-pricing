using System;
using System.Drawing;

namespace PoeStashPricer
{
    /// <summary>
    /// Finds the stash panel in a screenshot of the game window, at any resolution.
    /// PoE2 scales its UI with the window height and draws the stash contents inside a thin frame in the
    /// colour of the current tab (orange, green, ...). We predict the frame from the height, then look for
    /// that coloured line near each predicted edge.
    /// </summary>
    public static class StashLocator
    {
        // Frame line positions measured on a 2560x1440 client, as fractions of the height.
        const double FrameLeft = 20.0 / 1440, FrameTop = 166.0 / 1440, FrameSize = 847.0 / 1440;
        const double SearchRange = 0.035;   // ± fraction of the height searched around each predicted edge

        public class Result
        {
            public Rectangle Region;     // stash contents, in the coordinates of the given buffer
            public int EdgesFound;       // 0..4 frame lines confirmed in the image
            public double[] FrameColor;  // r, g, b of the frame line (each tab has its own colour), or null
            public bool StashVisible { get { return EdgesFound >= 3; } }
        }

        /// <summary>Region predicted purely from the window size (no image check).</summary>
        public static Rectangle Predict(Size client)
        {
            double h = client.Height;
            int size = (int)Math.Round(FrameSize * h);
            return new Rectangle((int)Math.Round(FrameLeft * h) + 2, (int)Math.Round(FrameTop * h) + 2, size - 3, size - 3);
        }

        /// <param name="pb">Screenshot of the game window's client area.</param>
        public static Result Locate(PixelBuffer pb)
        {
            double h = pb.Height;
            int left = (int)Math.Round(FrameLeft * h), top = (int)Math.Round(FrameTop * h);
            int size = (int)Math.Round(FrameSize * h);
            int range = Math.Max(6, (int)(SearchRange * h));
            int thick = Math.Max(3, (int)Math.Round(3 * h / 1440));   // line thickness at this resolution

            Result res = new Result();
            int l, r, t, b;
            // Most tabs have a coloured frame. Some (Delirium) have a grey one: if the coloured search
            // doesn't find the panel, look for a thin line that is brighter than both its sides instead.
            res.EdgesFound = FindEdges(pb, left, top, size, range, thick, false, out l, out r, out t, out b);
            if (res.EdgesFound < 3)
            {
                int gl, gr, gt, gb;
                int grey = FindEdges(pb, left, top, size, range, thick, true, out gl, out gr, out gt, out gb);
                if (grey > res.EdgesFound) { res.EdgesFound = grey; l = gl; r = gr; t = gt; b = gb; }
            }
            // Opposite edges must be one panel size apart. Otherwise one of them is another line nearby
            // (e.g. the tab header's border): keep the one closer to where the frame is expected.
            int tol = Math.Max(4, size / 50);
            if (l >= 0 && r >= 0 && Math.Abs(r - l - size) > tol)
            {
                if (Math.Abs(l - left) <= Math.Abs(r - (left + size))) r = -1; else l = -1;
                res.EdgesFound--;
            }
            if (t >= 0 && b >= 0 && Math.Abs(b - t - size) > tol)
            {
                if (Math.Abs(t - top) <= Math.Abs(b - (top + size))) b = -1; else t = -1;
                res.EdgesFound--;
            }
            res.FrameColor = LineColor(pb, l >= 0 ? l : r, top + size / 10, top + size * 9 / 10, thick);
            // Missing edges (hidden by a tooltip, say) are derived from the opposite one: the panel is square.
            if (l < 0) l = r >= 0 ? r - size : left;
            if (r < 0) r = l + size;
            if (t < 0) t = b >= 0 ? b - size : top;
            if (b < 0) b = t + size;
            int x0 = l + thick, y0 = t + thick;
            res.Region = Rectangle.Intersect(new Rectangle(0, 0, pb.Width, pb.Height), Rectangle.FromLTRB(x0, y0, r, b));
            return res;
        }

        static int FindEdges(PixelBuffer pb, int left, int top, int size, int range, int thick, bool grey,
                             out int l, out int r, out int t, out int b)
        {
            // Sample the middle 80% of each edge, so tab headers and corners don't interfere.
            int spanA = top + size / 10, spanB = top + size * 9 / 10;
            l = FindVertical(pb, left, range, spanA, spanB, thick, grey, 1);
            r = FindVertical(pb, left + size, range, spanA, spanB, thick, grey, -1);
            spanA = left + size / 10; spanB = left + size * 9 / 10;
            t = FindHorizontal(pb, top, range, spanA, spanB, thick, grey, 1);
            b = FindHorizontal(pb, top + size, range, spanA, spanB, thick, grey, -1);
            return (l >= 0 ? 1 : 0) + (r >= 0 ? 1 : 0) + (t >= 0 ? 1 : 0) + (b >= 0 ? 1 : 0);
        }

        /// <summary>Average colour of the most saturated pixel across a vertical frame line.</summary>
        static double[] LineColor(PixelBuffer pb, int x, int y0, int y1, int thick)
        {
            if (x < 0) return null;
            double r = 0, g = 0, b = 0; int n = 0;
            for (int y = Math.Max(0, y0); y < Math.Min(pb.Height, y1); y += 4)
            {
                int bestX = -1, bestSat = 49;
                for (int xx = x; xx < Math.Min(pb.Width, x + thick + 1); xx++)
                {
                    int s = Sat(pb, xx, y);
                    if (s > bestSat) { bestSat = s; bestX = xx; }
                }
                if (bestX < 0) continue;   // covered here (tooltip, item)
                int o = y * pb.Stride + bestX * 4;
                b += pb.Px[o]; g += pb.Px[o + 1]; r += pb.Px[o + 2]; n++;
            }
            return n == 0 ? null : new[] { r / n, g / n, b / n };
        }

        /// <summary>
        /// Difference in hue between two frame colours, ignoring brightness (0 = same hue). Orange, green,
        /// purple and blue frames are far apart (&gt; 0.3); the same tab stays well below 0.1.
        /// </summary>
        public static double ColorDistance(double[] a, double[] b)
        {
            double sa = a[0] + a[1] + a[2], sb = b[0] + b[1] + b[2];
            if (sa <= 0 || sb <= 0) return 1;
            double d = 0;
            for (int i = 0; i < 3; i++) d += Math.Abs(a[i] / sa - b[i] / sb);
            return d;
        }

        static int Sat(PixelBuffer pb, int x, int y)
        {
            int o = y * pb.Stride + x * 4;
            int b = pb.Px[o], g = pb.Px[o + 1], r = pb.Px[o + 2];
            return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
        }

        static int Bright(PixelBuffer pb, int x, int y)
        {
            int o = y * pb.Stride + x * 4;
            return Math.Max(pb.Px[o + 2], Math.Max(pb.Px[o + 1], pb.Px[o]));
        }

        // A coloured frame pixel is clearly more saturated than the pixels a few steps away on both sides.
        // A grey frame is only compared with the inside of the panel (dx, dy point inwards): outside it
        // there can be brighter UI, like the tab header strip above the top edge.
        static bool Ridge(PixelBuffer pb, int x, int y, int dx, int dy, bool grey)
        {
            if (grey)
            {
                int c = Bright(pb, x, y);
                return c >= 25 && c - Bright(pb, x + dx, y + dy) >= 12;
            }
            int s = Sat(pb, x, y);
            return s >= 50 && s - Sat(pb, x - dx, y - dy) >= 35 && s - Sat(pb, x + dx, y + dy) >= 35;
        }

        static double ColScore(PixelBuffer pb, int x, int y0, int y1, int d, bool grey)
        {
            int hit = 0, n = 0;
            for (int y = Math.Max(0, y0); y < Math.Min(pb.Height, y1); y += 4)
            {
                n++;
                if (Ridge(pb, x, y, d, 0, grey)) hit++;
            }
            return n == 0 ? 0 : (double)hit / n;
        }

        static double RowScore(PixelBuffer pb, int y, int x0, int x1, int d, bool grey)
        {
            int hit = 0, n = 0;
            for (int x = Math.Max(0, x0); x < Math.Min(pb.Width, x1); x += 4)
            {
                n++;
                if (Ridge(pb, x, y, 0, d, grey)) hit++;
            }
            return n == 0 ? 0 : (double)hit / n;
        }

        /// <summary>Column of the vertical frame line nearest <paramref name="guess"/>, or -1.</summary>
        static int FindVertical(PixelBuffer pb, int guess, int range, int y0, int y1, int thick, bool grey, int inward)
        {
            int d = thick + 2, lo = Math.Max(d, guess - range), hi = Math.Min(pb.Width - 1 - d, guess + range);
            return Best(lo, hi, guess, thick, x => ColScore(pb, x, y0, y1, d * inward, grey));
        }

        static int FindHorizontal(PixelBuffer pb, int guess, int range, int x0, int x1, int thick, bool grey, int inward)
        {
            int d = thick + 2, lo = Math.Max(d, guess - range), hi = Math.Min(pb.Height - 1 - d, guess + range);
            return Best(lo, hi, guess, thick, y => RowScore(pb, y, x0, x1, d * inward, grey));
        }

        /// <summary>
        /// The line nearest to <paramref name="guess"/> among positions where at least half the edge looks like
        /// a line (the rest may be covered by a tooltip). Nearest rather than strongest: other straight lines
        /// nearby (tab header border) can be stronger than a dim grey frame. The frame is a few pixels thick,
        /// so the result is its first pixel.
        /// </summary>
        static int Best(int lo, int hi, int guess, int maxThick, Func<int, double> score)
        {
            if (hi < lo) return -1;   // search window outside the picture
            double[] s = new double[hi - lo + 1];
            for (int i = lo; i <= hi; i++) s[i - lo] = score(i);
            int best = -1;
            for (int i = lo; i <= hi; i++)
                if (s[i - lo] >= 0.5 && (best < 0 || Math.Abs(i - guess) < Math.Abs(best - guess))) best = i;
            if (best < 0) return -1;
            // Move to the strongest pixel of this line, then back to where the line starts.
            while (best < hi && s[best + 1 - lo] > s[best - lo]) best++;
            while (best > lo && s[best - 1 - lo] > s[best - lo]) best--;
            double peak = s[best - lo];
            for (int k = 0; k < maxThick && best > lo && s[best - 1 - lo] >= peak * 0.6; k++) best--;
            return best;
        }    }
}
