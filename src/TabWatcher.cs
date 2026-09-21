using System;
using System.Drawing;
using System.Windows.Forms;

namespace PoeStashPricer
{
    /// <summary>
    /// Remembers what the stash looked like when it was priced and reports when that picture changes
    /// (another tab opened, stash closed, items moved), so stale prices can be cleared.
    /// </summary>
    public class TabWatcher
    {
        const int ColorTolerance = 14;   // per-channel mean difference for a cell to count as changed
        const int MinChangedCells = 2;   // one odd cell is noise; a different tab changes many

        float[,,] baseline;
        Rectangle region;
        int cols, rows;

        public bool Active { get { return baseline != null; } }

        public void SetBaseline(PixelBuffer pb, Rectangle region, int cols, int rows)
        {
            this.region = region;
            this.cols = Math.Max(1, cols);
            this.rows = Math.Max(1, rows);
            baseline = Signature(pb, this.cols, this.rows);
        }

        public void Clear() { baseline = null; }

        /// <summary>
        /// Call periodically. True when the stash no longer matches the baseline. An item tooltip counts as a
        /// change too; the caller decides whether it was a real tab change.
        /// </summary>
        public bool HasChanged()
        {
            if (baseline == null) return false;
            float[,,] now;
            using (Bitmap bmp = Grid.Capture(region))
                now = Signature(new PixelBuffer(bmp), cols, rows);

            int changed = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    for (int ch = 0; ch < 3; ch++)
                        if (Math.Abs(now[r, c, ch] - baseline[r, c, ch]) > ColorTolerance) { changed++; break; }
            return changed >= MinChangedCells;
        }

        /// <summary>Mean colour of each cell's inner area (sub-sampled).</summary>
        static float[,,] Signature(PixelBuffer pb, int cols, int rows)
        {
            float[,,] sig = new float[rows, cols, 3];
            Rectangle all = new Rectangle(0, 0, pb.Width, pb.Height);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    Rectangle cell = Grid.Cell(all, cols, rows, r, c);
                    int mx = cell.Width / 6, my = cell.Height / 6;
                    long sb = 0, sg = 0, sr = 0; int n = 0;
                    for (int y = cell.Top + my; y < cell.Bottom - my; y += 2)
                        for (int x = cell.Left + mx; x < cell.Right - mx; x += 2)
                        {
                            int o = y * pb.Stride + x * 4;
                            sb += pb.Px[o]; sg += pb.Px[o + 1]; sr += pb.Px[o + 2]; n++;
                        }
                    if (n == 0) continue;
                    sig[r, c, 0] = sr / (float)n; sig[r, c, 1] = sg / (float)n; sig[r, c, 2] = sb / (float)n;
                }
            return sig;
        }
    }
}
