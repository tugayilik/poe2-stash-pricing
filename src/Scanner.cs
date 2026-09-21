using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PoeStashPricer
{
    public class ScanConfig
    {
        public Rectangle Window;    // game client area, screen coordinates
        public int TintSensitivity;
        public double Threshold;
        public bool FixedLayout;    // a saved fixed-slot tab: every item type has exactly one slot (some are 2x2)
        public int HoverDelay;
        public int CopyTimeout;
        public int HotkeyVk;        // the scan hotkey; the scan waits until it is released

        // Filled in by Scanner.Prepare from the screenshot:
        public Rectangle Region;    // stash contents, screen coordinates
        public double CellSize;     // one stash cell in pixels (normal or quad tab)
    }

    public class ScanItem
    {
        public string Text;        // raw clipboard text
        public ParsedItem Item;
        public PriceInfo Price;
        public int Qty;
        public bool CountUnread;   // count had to come from the screen and couldn't be read (Qty is 1)
        public double TotalDiv;
        public Rectangle Bounds;   // screen coordinates
    }

    public class ScanResult
    {
        public List<ScanItem> Items = new List<ScanItem>();
        public int CellsTried, CellsCopied;
        public int CellsRetried, CellsRecovered;   // second, slower tries of spots that copied nothing
        public string Timing;          // where the time went, for the log
        public bool Aborted;
        public bool StashNotFound;
        public bool BlackScreen;       // the game picture can't be captured (exclusive fullscreen)
        public TabProfile Tab;         // recognised saved tab, or null
        public double TabDifference = 1;   // how far the picture was from that tab (0 = identical)
        public double[] FrameColor;    // colour of the tab's frame (for learning a new tab), or null
        public PixelBuffer Snapshot;   // the stash as captured before hovering (baseline for tab-change detection)
    }

    /// <summary>What to hover, decided from one clean screenshot.</summary>
    public class ScanPlan
    {
        public bool StashVisible;
        public bool BlackScreen;              // the game picture can't be captured (exclusive fullscreen)
        public TabProfile Tab;                // recognised saved tab, or null
        public double Difference = 1;         // picture difference to the closest saved tab
        public double[] FrameColor;           // colour of the tab's frame, or null
        public PixelBuffer Snapshot;          // the stash region only
        public List<ProbeGroup> Groups = new List<ProbeGroup>();
    }

    /// <summary>
    /// A small grid of hover points: one per detected item area (split into cells when it covers several
    /// touching items), plus one for the slot lattice of fixed-layout tabs.
    /// </summary>
    public class ProbeGroup
    {
        public int Rows, Cols;
        public Rectangle[,] Rects;   // screen coordinates
        public bool[,] Active;       // false = considered empty, not hovered
        public bool[,] Likely;       // looked occupied in the screenshot (a saved slot is hovered even when not)
        public double[,] Score;      // share of item background (shown in preview)
        public string[,] Texts;
        public int Priority;         // lower wins when probes of different groups overlap

        public ProbeGroup(int rows, int cols)
        {
            Rows = rows; Cols = cols;
            Rects = new Rectangle[rows, cols];
            Active = new bool[rows, cols];
            Likely = new bool[rows, cols];
            Score = new double[rows, cols];
            Texts = new string[rows, cols];
        }
    }

    public static class Grid
    {
        public static Rectangle Cell(Rectangle region, int cols, int rows, int r, int c)
        {
            int x0 = region.X + (int)Math.Round(region.Width * (double)c / cols);
            int x1 = region.X + (int)Math.Round(region.Width * (double)(c + 1) / cols);
            int y0 = region.Y + (int)Math.Round(region.Height * (double)r / rows);
            int y1 = region.Y + (int)Math.Round(region.Height * (double)(r + 1) / rows);
            return new Rectangle(x0, y0, x1 - x0, y1 - y0);
        }

        public static Bitmap Capture(Rectangle r)
        {
            Bitmap bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
                g.CopyFromScreen(r.Location, Point.Empty, r.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        /// <summary>Standard deviation of luminance in the cell's inner area: empty cells are flat, icons are detailed.</summary>
        public static double Busyness(PixelBuffer pb, Rectangle cell)
        {
            int mx = Math.Max(1, cell.Width / 5), my = Math.Max(1, cell.Height / 5);
            double sum = 0, sq = 0; int n = 0;
            for (int y = Math.Max(0, cell.Top + my); y < Math.Min(pb.Height, cell.Bottom - my); y++)
                for (int x = Math.Max(0, cell.Left + mx); x < Math.Min(pb.Width, cell.Right - mx); x++)
                {
                    int o = y * pb.Stride + x * 4;
                    double l = 0.114 * pb.Px[o] + 0.587 * pb.Px[o + 1] + 0.299 * pb.Px[o + 2];
                    sum += l; sq += l * l; n++;
                }
            if (n == 0) return 0;
            double mean = sum / n;
            return Math.Sqrt(Math.Max(0, sq / n - mean * mean));
        }

        static Rectangle Offset(Rectangle r, Point by) { r.Offset(by); return r; }

        /// <summary>
        /// Splits an item area into slots of about <paramref name="slot"/> pixels. Touching or nearly touching
        /// items form one area, and slots can have gaps between them (Currency: 75 px slots, 90 px apart), so an
        /// even split by cell size lands on the edges. Slots in a row are evenly spaced: the first one starts at
        /// the area's left edge, the last one ends at its right edge, the others are spread between. How many
        /// slots there are is not simply length / slot (five slots with gaps look like six without), so each
        /// plausible count is tried and the one whose slot borders fall on lines without item background wins.
        /// </summary>
        public static Rectangle[,] Split(PixelBuffer pb, Rectangle blob, double slot, int sensitivity)
        {
            List<int[]> xs = Spans(pb, blob, slot, sensitivity, true), ys = Spans(pb, blob, slot, sensitivity, false);
            Rectangle[,] cells = new Rectangle[ys.Count, xs.Count];
            for (int r = 0; r < ys.Count; r++)
                for (int c = 0; c < xs.Count; c++)
                    cells[r, c] = new Rectangle(xs[c][0], ys[r][0], xs[c][1] - xs[c][0], ys[r][1] - ys[r][0]);
            return cells;
        }

        static List<int[]> Spans(PixelBuffer pb, Rectangle blob, double slot, int sensitivity, bool alongX)
        {
            int start = alongX ? blob.Left : blob.Top, len = alongX ? blob.Width : blob.Height;
            int guess = Math.Max(1, (int)Math.Round(len / slot));
            List<int[]> best = null;
            double bestScore = double.MaxValue;
            for (int n = Math.Max(1, (int)(len / (slot * 1.4))); n <= Math.Max(1, (int)Math.Ceiling(len / (slot * 0.9))); n++)
            {
                int size = n == 1 ? len : (int)Math.Round(Math.Min(slot, len / (double)n));
                if (n > 1)
                {
                    double gap = (len - n * size) / (double)(n - 1);
                    if (size < slot * 0.85 || gap > slot * 0.4) continue;   // slots this small or this far apart: not this count
                }
                List<int[]> spans = new List<int[]>();
                double pitch = n == 1 ? 0 : (len - size) / (double)(n - 1);
                for (int i = 0; i < n; i++)
                {
                    int a = start + (int)Math.Round(i * pitch);
                    spans.Add(new[] { a, a + size });
                }
                // Item background on the borders between neighbouring slots (little is good); a count close to
                // length / slot is preferred when the picture can't tell.
                double score = Math.Abs(n - guess) * 0.05;
                if (n > 1 && pb != null)
                {
                    double border = 0;
                    for (int i = 0; i + 1 < n; i++)
                    {
                        int mid = (spans[i][1] + spans[i + 1][0]) / 2;
                        Rectangle band = alongX ? new Rectangle(mid - 1, blob.Top, 3, blob.Height) : new Rectangle(blob.Left, mid - 1, blob.Width, 3);
                        border += SlotDetector.TintFraction(pb, band, sensitivity, 0);
                    }
                    score += border / (n - 1);
                }
                else if (n == 1 && len > slot * 1.4) score += 2;   // an area this long holds several slots
                if (score < bestScore) { bestScore = score; best = spans; }
            }
            if (best == null)
            {
                best = new List<int[]>();
                for (int i = 0; i < guess; i++)
                    best.Add(new[] { start + (int)Math.Round(len * (double)i / guess), start + (int)Math.Round(len * (double)(i + 1) / guess) });
            }
            return best;
        }

        /// <summary>
        /// Size of one slot as drawn: the median size of the areas that hold exactly one item, or a bit more
        /// than a cell (slots of the fixed-layout tabs are a little bigger than inventory cells).
        /// </summary>
        public static double SlotSize(List<Rectangle> blobs, double cs)
        {
            List<double> sizes = new List<double>();
            foreach (Rectangle b in blobs)
                if (b.Width >= cs * 0.8 && b.Width <= cs * 1.3 && b.Height >= cs * 0.8 && b.Height <= cs * 1.3)
                    sizes.Add(Math.Max(b.Width, b.Height));
            if (sizes.Count < 2) return cs;
            sizes.Sort();
            return sizes[sizes.Count / 2];
        }

        /// <summary>
        /// Size of one slot. The stash area is 12 normal cells wide and the supported fixed-layout tabs
        /// use normal-sized slots. (Quad tabs would be 24 wide; small faded placeholder icons made guessing
        /// that from the picture unreliable, so it is not attempted.)
        /// </summary>
        public static double CellSizeFor(int stashWidth)
        {
            return stashWidth / 12.0;
        }

        /// <summary>
        /// Decides where to hover. <paramref name="pb"/> is a capture of <c>cfg.Region</c>;
        /// <paramref name="known"/> are the slots of the recognised tab (area coordinates), or null.
        /// </summary>
        public static List<ProbeGroup> Plan(PixelBuffer pb, ScanConfig cfg, List<Rectangle> known)
        {
            List<ProbeGroup> groups = new List<ProbeGroup>();
            Point origin = cfg.Region.Location;
            double cs = cfg.CellSize;

            // Slots learned from the user's screenshot of this tab: exact positions, checked for an item now.
            // A learned slot bigger than one cell (a 2x2 slot, or touching slots saved by version 1.2.0) is
            // checked cell by cell; repeated reads of one item are merged afterwards.
            List<Rectangle> blobs = SlotDetector.Detect(pb, cs, cfg.TintSensitivity);
            double slotSize = SlotSize(blobs, cs);
            // Item areas seen in this picture, one slot in size: where an item really sits right now.
            // Touching items form one area; its cells stand for them.
            List<Rectangle> single = new List<Rectangle>();
            foreach (Rectangle b in blobs)
            {
                if (b.Width < cs * 0.6 || b.Height < cs * 0.6 || b.Width * b.Height > cs * cs * 20) continue;
                foreach (Rectangle cell in Split(pb, b, slotSize, cfg.TintSensitivity)) single.Add(cell);
            }

            if (known != null)
                foreach (Rectangle slot in known)
                {
                    int kx = Math.Max(1, (int)Math.Round(slot.Width / cs)), ky = Math.Max(1, (int)Math.Round(slot.Height / cs));
                    for (int r = 0; r < ky; r++)
                        for (int c = 0; c < kx; c++)
                        {
                            Rectangle k = Cell(slot, kx, ky, r, c);
                            // A saved position can be a little off (learned by an older version, or from an
                            // item area that touched its neighbour). When an item area overlaps it, hover
                            // that area's middle instead of the saved one, which may be on the edge.
                            Rectangle snap = Rectangle.Empty;
                            double best = 0;
                            foreach (Rectangle b in single)
                            {
                                Rectangle x = Rectangle.Intersect(b, k);
                                double share = (double)x.Width * x.Height / Math.Max(1, Math.Min(b.Width * b.Height, k.Width * k.Height));
                                if (share > 0.3 && share > best) { best = share; snap = b; }
                            }
                            if (!snap.IsEmpty) k = snap;
                            ProbeGroup g = new ProbeGroup(1, 1);
                            g.Rects[0, 0] = Offset(k, origin);
                            g.Score[0, 0] = SlotDetector.TintFraction(pb, k, cfg.TintSensitivity);
                            // Every saved slot is hovered: an icon can hide the item background, and an empty
                            // slot copies nothing, so hovering it costs only a moment.
                            g.Likely[0, 0] = Occupied(pb, k, cfg);
                            g.Active[0, 0] = true;
                            g.Priority = -1;
                            groups.Add(g);
                        }
                }

            // Fixed-slot tabs place slots in regular rows and columns. The cleanly detected single items
            // reveal those lines; testing every row x column crossing catches items whose own area was
            // missed or merged with something else.
            ProbeGroup lattice = Lattice(pb, cfg, blobs, origin);
            if (lattice != null) groups.Add(lattice);

            foreach (Rectangle blob in blobs)
            {
                // Touching items form one area: split it into cells and test each one. A big slot (Fragments
                // has 2x2 ones) is split too; its cells all read the same item, which is merged afterwards.
                Rectangle[,] parts = Split(pb, blob, slotSize, cfg.TintSensitivity);
                int ny = parts.GetLength(0), nx = parts.GetLength(1);
                ProbeGroup g = new ProbeGroup(ny, nx);
                for (int r = 0; r < ny; r++)
                    for (int c = 0; c < nx; c++)
                    {
                        Rectangle sub = parts[r, c];
                        g.Rects[r, c] = Offset(sub, origin);
                        g.Score[r, c] = SlotDetector.TintFraction(pb, sub, cfg.TintSensitivity);
                        // A lone area must be solidly item background, or at least framed by it when a big
                        // icon covers the middle. Faded placeholder icons in empty slots have a few navy-like
                        // pixels too, but only as a sparse speck.
                        g.Active[r, c] = nx * ny == 1
                            ? SlotDetector.TintFraction(pb, sub, cfg.TintSensitivity, 0) >= 0.2
                              || SlotDetector.RingTintFraction(pb, sub, cfg.TintSensitivity) >= 0.35
                            : Occupied(pb, sub, cfg);
                    }
                // A large unsplit area may be several slots merged with the panel; let the exact slots
                // (learned ones and the lattice) win over it when they overlap.
                bool large = blob.Width > cs * 1.5 || blob.Height > cs * 1.5;
                g.Priority = nx * ny > 1 ? 2 : large ? 3 : 0;
                groups.Add(g);
            }

            // Areas can overlap; never hover the same spot twice (a stackable item would be counted twice).
            // Single-item areas are the most precise, then the slot lattice, then split multi-item areas.
            groups.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            List<Rectangle> taken = new List<Rectangle>();
            foreach (ProbeGroup g in groups)
                for (int r = 0; r < g.Rows; r++)
                    for (int c = 0; c < g.Cols; c++)
                    {
                        if (!g.Active[r, c]) continue;
                        Rectangle rc = g.Rects[r, c];
                        bool dup = false;
                        foreach (Rectangle t in taken)
                        {
                            // Overlapping more than a quarter of either rectangle = same spot.
                            Rectangle x = Rectangle.Intersect(t, rc);
                            double area = (double)x.Width * x.Height;
                            if (area > 0.25 * Math.Min(t.Width * t.Height, rc.Width * rc.Height)) { dup = true; break; }
                        }
                        if (dup) g.Active[r, c] = false;
                        else taken.Add(rc);
                        if (g.Priority != -1) g.Likely[r, c] = g.Active[r, c];   // found by looking, so it looked occupied
                    }
            return groups;
        }

        /// <summary>
        /// Large icons hide most of the tint, so a detailed cell also counts as occupied,
        /// but only if some item background shows (textured panel stone has none).
        /// </summary>
        static bool Occupied(PixelBuffer pb, Rectangle rect, ScanConfig cfg)
        {
            if (SlotDetector.RingTintFraction(pb, rect, cfg.TintSensitivity) >= 0.35) return true;
            if (SlotDetector.TintFraction(pb, rect, cfg.TintSensitivity, 0) < 0.06) return false;
            return SlotDetector.TintFraction(pb, rect, cfg.TintSensitivity) >= 0.25 || Busyness(pb, rect) >= cfg.Threshold;
        }

        static List<double> Cluster(List<double> values, double tol)
        {
            values.Sort();
            List<double> centers = new List<double>();
            List<double> cur = new List<double>();
            foreach (double v in values)
            {
                if (cur.Count > 0 && v - cur[cur.Count - 1] > tol)
                {
                    centers.Add(Average(cur));
                    cur.Clear();
                }
                cur.Add(v);
            }
            if (cur.Count > 0) centers.Add(Average(cur));
            return centers;
        }

        static double Average(List<double> l)
        {
            double s = 0;
            foreach (double v in l) s += v;
            return s / l.Count;
        }

        static ProbeGroup Lattice(PixelBuffer pb, ScanConfig cfg, List<Rectangle> blobs, Point origin)
        {
            double cs = cfg.CellSize;
            List<double> xs = new List<double>(), ys = new List<double>(), sizes = new List<double>();
            foreach (Rectangle b in blobs)
            {
                if (b.Width < cs * 0.6 || b.Width > cs * 1.5 || b.Height < cs * 0.6 || b.Height > cs * 1.5) continue;
                xs.Add(b.X + b.Width / 2.0);
                ys.Add(b.Y + b.Height / 2.0);
                sizes.Add((b.Width + b.Height) / 2.0);
            }
            if (sizes.Count < 3) return null;
            sizes.Sort();
            double slot = sizes[sizes.Count / 2];
            List<double> cols = Cluster(xs, slot * 0.35), rows = Cluster(ys, slot * 0.35);

            ProbeGroup g = new ProbeGroup(rows.Count, cols.Count);
            g.Priority = 1;
            int half = (int)(slot / 2);
            Rectangle bounds = new Rectangle(0, 0, pb.Width, pb.Height);
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < cols.Count; c++)
                {
                    Rectangle rc = Rectangle.Intersect(bounds, new Rectangle((int)cols[c] - half, (int)rows[r] - half, 2 * half, 2 * half));
                    g.Rects[r, c] = Offset(rc, origin);
                    g.Score[r, c] = SlotDetector.TintFraction(pb, rc, cfg.TintSensitivity);
                    g.Active[r, c] = rc.Width > slot * 0.6 && rc.Height > slot * 0.6 && Occupied(pb, rc, cfg);
                }
            return g;
        }
    }

    /// <summary>Hovers each detected item, presses Ctrl+C and reads what the game copied. Must run on an STA thread.</summary>
    public class Scanner
    {
        readonly ScanConfig cfg;
        readonly IntPtr game;
        public volatile bool CancelRequested;

        public Scanner(ScanConfig cfg, IntPtr gameWindow)
        {
            this.cfg = cfg;
            this.game = gameWindow;
        }

        /// <summary>
        /// Moves the cursor onto the tab header row (so no item tooltip covers the stash), screenshots the game
        /// window, finds the stash panel and decides where the items are. Fills cfg.Region and cfg.CellSize.
        /// </summary>
        public static ScanPlan Prepare(ScanConfig cfg, ICollection<TabProfile> profiles)
        {
            ScanPlan plan = new ScanPlan();
            Rectangle win = cfg.Window;
            Rectangle guess = StashLocator.Predict(win.Size);
            Native.MoveMouse(win.X + guess.X + guess.Width / 2, win.Y + Math.Max(0, guess.Y - (int)(win.Height * 0.03)));
            Thread.Sleep(150);

            PixelBuffer full = CaptureStable(win, guess);
            if (IsBlack(full, guess))
            {
                // Exclusive fullscreen: Windows can't capture the game, the picture is black.
                Log.Write("capture of the game window " + win + " is black (exclusive fullscreen?)");
                plan.BlackScreen = true;
                return plan;
            }
            StashLocator.Result loc = StashLocator.Locate(full);
            Log.Write(string.Format("stash search in window {0}: {1}/4 frame edges, region {2}, frame colour {3}",
                win, loc.EdgesFound, loc.Region, loc.FrameColor == null ? "none" : string.Join(",", Array.ConvertAll(loc.FrameColor, c => ((int)c).ToString()))));
            if (!loc.StashVisible && profiles != null && profiles.Count > 0)
            {
                // Faint frames (grey ones, at low resolution) can escape the frame search; if the predicted
                // area looks exactly like a saved tab, the stash is open all the same.
                double d;
                if (TabLibrary.Identify(full.Crop(loc.Region), null, profiles, out d) != null && d <= 0.15) loc.EdgesFound = 4;
            }
            plan.StashVisible = loc.StashVisible;
            if (!plan.StashVisible) return plan;

            plan.Snapshot = full.Crop(loc.Region);
            cfg.Region = new Rectangle(win.X + loc.Region.X, win.Y + loc.Region.Y, loc.Region.Width, loc.Region.Height);
            double difference = 1;
            plan.FrameColor = loc.FrameColor;
            plan.Tab = profiles == null ? null : TabLibrary.Identify(plan.Snapshot, loc.FrameColor, profiles, out difference);
            plan.Difference = difference;
            if (profiles != null) Log.Write("tab recognised: " + (plan.Tab != null ? plan.Tab.Key : "none") + " (difference " + difference.ToString("0.00") + ")");
            Size area = new Size(plan.Snapshot.Width, plan.Snapshot.Height);
            cfg.CellSize = Grid.CellSizeFor(area.Width);
            cfg.FixedLayout = plan.Tab != null;
            plan.Groups = Grid.Plan(plan.Snapshot, cfg, plan.Tab != null ? plan.Tab.SlotsIn(area) : null);
            return plan;
        }

        /// <summary>
        /// The game fades a tab in when it is opened; a picture taken during the fade is darker or washed
        /// out. Take pictures until the stash area (<paramref name="watch"/>, window coordinates) stops
        /// changing, at most about a second.
        /// </summary>
        static PixelBuffer CaptureStable(Rectangle win, Rectangle watch)
        {
            PixelBuffer prev;
            using (Bitmap bmp = Grid.Capture(win)) prev = new PixelBuffer(bmp);
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(100);
                PixelBuffer cur;
                using (Bitmap bmp = Grid.Capture(win)) cur = new PixelBuffer(bmp);
                if (MeanDifference(prev, cur, watch) < 1.0) return cur;
                prev = cur;
            }
            return prev;
        }

        /// <summary>True when the area is (nearly) uniformly black: what a capture of an exclusive-fullscreen game gives.</summary>
        static bool IsBlack(PixelBuffer pb, Rectangle area)
        {
            area.Intersect(new Rectangle(0, 0, pb.Width, pb.Height));
            long bright = 0; int n = 0;
            for (int y = area.Top; y < area.Bottom; y += 5)
                for (int x = area.Left; x < area.Right; x += 5)
                {
                    int o = y * pb.Stride + x * 4;
                    if (Math.Max(pb.Px[o], Math.Max(pb.Px[o + 1], pb.Px[o + 2])) > 8) bright++;
                    n++;
                }
            return n > 0 && bright < n / 100;
        }

        static double MeanDifference(PixelBuffer a, PixelBuffer b, Rectangle area)
        {
            area.Intersect(new Rectangle(0, 0, Math.Min(a.Width, b.Width), Math.Min(a.Height, b.Height)));
            long sum = 0; int n = 0;
            for (int y = area.Top; y < area.Bottom; y += 7)
                for (int x = area.Left; x < area.Right; x += 7)
                {
                    int oa = y * a.Stride + x * 4, ob = y * b.Stride + x * 4;
                    sum += Math.Abs(a.Px[oa] - b.Px[ob]) + Math.Abs(a.Px[oa + 1] - b.Px[ob + 1]) + Math.Abs(a.Px[oa + 2] - b.Px[ob + 2]);
                    n++;
                }
            return n == 0 ? 0 : sum / (3.0 * n);
        }

        bool ShouldAbort()
        {
            if (CancelRequested || Native.IsKeyDown(Native.VK_ESCAPE)) return true;
            return game != IntPtr.Zero && Native.GetForegroundWindow() != game;
        }

        static string ReadClipboard()
        {
            for (int i = 0; i < 5; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch (ExternalException) { Thread.Sleep(10); }
            }
            return null;
        }

        // How long the game took to answer Ctrl+C on recent items (milliseconds). Kept across scans, so a tab
        // with one item still knows how long an empty slot is worth waiting for.
        static readonly List<double> copyLatency = new List<double>();

        /// <summary>
        /// Waiting for an empty slot is most of a scan's time. Once a few items showed how fast the game
        /// answers, wait a few times that long instead of the full configured timeout.
        /// </summary>
        int EmptySlotTimeout()
        {
            if (copyLatency.Count < 4) return cfg.CopyTimeout;
            List<double> l = new List<double>(copyLatency);
            l.Sort();
            double p90 = l[Math.Min(l.Count - 1, (int)(l.Count * 0.9))];
            return (int)Math.Max(60, Math.Min(cfg.CopyTimeout, p90 * 3 + 30));
        }

        string CopyHovered() { return CopyHovered(EmptySlotTimeout()); }

        string CopyHovered(int timeout)
        {
            uint seq = Native.GetClipboardSequenceNumber();
            Native.SendCtrlC();
            // Measure real time: Sleep(1) lasts up to a timer tick (about 15 ms), not 1 ms.
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            while (Native.GetClipboardSequenceNumber() == seq)
            {
                if (sw.ElapsedMilliseconds >= timeout) return null;   // nothing copied: empty slot
                Thread.Sleep(1);
            }
            lock (copyLatency)
            {
                copyLatency.Add(sw.Elapsed.TotalMilliseconds);
                if (copyLatency.Count > 60) copyLatency.RemoveAt(0);
            }
            Thread.Sleep(5);
            string txt = ReadClipboard();
            return ItemParser.LooksLikeItem(txt) ? txt : null;
        }

        /// <summary>Median and slowest answer to Ctrl+C in this scan, for the log.</summary>
        public string LatencySummary()
        {
            if (copyLatency.Count == 0) return "no copies";
            List<double> l = new List<double>(copyLatency);
            l.Sort();
            return string.Format("copy answer median {0:0} ms, max {1:0} ms, empty-slot wait {2} ms", l[l.Count / 2], l[l.Count - 1], EmptySlotTimeout());
        }

        public ScanResult Run(ICollection<TabProfile> profiles, Func<ParsedItem, PriceInfo> lookup, Action<int, int> progress)
        {
            ScanResult res = new ScanResult();

            // Don't start while the user still holds the hotkey / modifiers.
            for (int i = 0; i < 100 && (Native.IsKeyDown(cfg.HotkeyVk) || Native.IsKeyDown(Native.VK_CONTROL) || Native.IsKeyDown(0x10) || Native.IsKeyDown(0x12)); i++) Thread.Sleep(10);

            Native.POINT orig;
            Native.GetCursorPos(out orig);
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            ScanPlan plan = Prepare(cfg, profiles);
            long planMs = clock.ElapsedMilliseconds, firstMs = 0;
            res.Tab = plan.Tab;
            res.TabDifference = plan.Difference;
            res.FrameColor = plan.FrameColor;
            if (!plan.StashVisible)
            {
                Native.MoveMouse(orig.X, orig.Y);
                res.StashNotFound = true;
                res.BlackScreen = plan.BlackScreen;
                return res;
            }
            res.Snapshot = plan.Snapshot;
            List<ProbeGroup> groups = plan.Groups;

            int total = 0;
            foreach (ProbeGroup g in groups)
                foreach (bool a in g.Active) if (a) total++;

            string savedClipboard = ReadClipboard();
            string last = null;
            int done = 0;
            try
            {
                foreach (ProbeGroup g in groups)
                {
                    for (int r = 0; r < g.Rows && !res.Aborted; r++)
                        for (int c = 0; c < g.Cols; c++)
                        {
                            if (!g.Active[r, c]) continue;
                            if (ShouldAbort()) { res.Aborted = true; break; }
                            Rectangle cell = g.Rects[r, c];
                            int cx = cell.X + cell.Width / 2, cy = cell.Y + cell.Height / 2;
                            Native.MoveMouse(cx + 1, cy + 1);
                            Native.MoveMouse(cx, cy);
                            Thread.Sleep(cfg.HoverDelay);
                            string txt = CopyHovered();
                            if (txt != null && txt == last)
                            {
                                // Same text as the previous cell: either a genuine duplicate or the game hadn't
                                // updated the hover yet. Wait and read again to be sure.
                                Thread.Sleep(cfg.HoverDelay * 2);
                                txt = CopyHovered();
                            }
                            g.Texts[r, c] = txt;
                            last = txt;
                            res.CellsTried++;
                            if (txt != null) res.CellsCopied++;
                            if (progress != null) progress(++done, total);
                        }
                    if (res.Aborted) break;
                }

                firstMs = clock.ElapsedMilliseconds - planMs;
                // Second chance for spots that copied nothing: the game can be late to update the tooltip (a busy
                // frame, the mouse arriving from far away). Spots that looked occupied get a longer look; saved
                // slots that look empty are tried again only while the game's answer time is still unknown.
                foreach (ProbeGroup g in groups)
                    for (int r = 0; r < g.Rows && !res.Aborted; r++)
                        for (int c = 0; c < g.Cols; c++)
                        {
                            if (!g.Active[r, c] || g.Texts[r, c] != null) continue;
                            bool likely = g.Likely[r, c], measured = copyLatency.Count >= 4;
                            // Once the game's answer time is known, the first wait was already several times
                            // the slowest answer: only spots that looked occupied are worth another look.
                            if (!likely && measured) continue;
                            if (ShouldAbort()) { res.Aborted = true; break; }
                            Rectangle cell = g.Rects[r, c];
                            int cx = cell.X + cell.Width / 2, cy = cell.Y + cell.Height / 2;
                            Native.MoveMouse(cx + 2, cy + 2);
                            Thread.Sleep(30);
                            Native.MoveMouse(cx, cy);
                            Thread.Sleep(likely ? Math.Max(90, cfg.HoverDelay * 2) : cfg.HoverDelay);
                            string txt = CopyHovered(measured ? Math.Max(120, EmptySlotTimeout() * 2) : likely ? Math.Max(250, cfg.CopyTimeout) : cfg.CopyTimeout);
                            res.CellsRetried++;
                            if (txt == null) continue;
                            g.Texts[r, c] = txt;
                            res.CellsCopied++;
                            res.CellsRecovered++;
                        }
            }
            finally
            {
                Native.MoveMouse(orig.X, orig.Y);
                if (savedClipboard != null)
                {
                    try { Clipboard.SetText(savedClipboard); } catch { }
                }
            }

            foreach (ProbeGroup g in groups) BuildItems(res, g, lookup);
            // In a saved fixed-slot tab every item type has exactly one slot, so the same text read at several
            // points is one item (a big slot, or two probes on one slot). Elsewhere two identical stacks can
            // sit side by side, so only reads closer than a cell are merged there.
            MergeNearDuplicates(res, cfg.FixedLayout ? double.MaxValue : cfg.CellSize * 0.85);
            long hoverMs = clock.ElapsedMilliseconds - planMs;
            ReadCountsFromScreen(res);
            res.Timing = string.Format("screenshot and plan {0} ms, hovering {1} ms, second tries {2} ms, counts {3} ms; {4}",
                                       planMs, firstMs, Math.Max(0, hoverMs - firstMs), clock.ElapsedMilliseconds - planMs - hoverMs, LatencySummary());
            return res;
        }

        /// <summary>
        /// Items whose text has a stack size teach the digit reader what the numbers look like; items that are
        /// stackable but have no stack size in their text get their count read from the screen.
        /// </summary>
        void ReadCountsFromScreen(ScanResult res)
        {
            if (res.Snapshot == null) return;
            Point origin = cfg.Region.Location;
            foreach (ScanItem si in res.Items)
                if (si.Item.IsStackable && si.Qty > 0)
                    DigitReader.Learn(res.Snapshot, Local(si.Bounds, origin), cfg.CellSize, si.Qty);
            foreach (ScanItem si in res.Items)
            {
                if (!si.Item.NeedsCount) continue;
                int n = DigitReader.Read(res.Snapshot, Local(si.Bounds, origin), cfg.CellSize);
                if (n > 0)
                {
                    si.Qty = n;
                    si.TotalDiv = si.Price != null ? si.Price.Div * n : 0;
                }
                else si.CountUnread = true;
            }
            DigitReader.Save();
        }

        static Rectangle Local(Rectangle screen, Point origin)
        {
            screen.Offset(-origin.X, -origin.Y);
            return screen;
        }

        /// <summary>
        /// Two probes can land on the same slot (e.g. one of them only on its edge). Identical text from
        /// points closer than one cell is one item; genuine twin stacks sit a full cell apart.
        /// </summary>
        public static void MergeNearDuplicates(ScanResult res, double maxDist)
        {
            List<ScanItem> kept = new List<ScanItem>();
            foreach (ScanItem si in res.Items)
            {
                bool twin = false;
                foreach (ScanItem k in kept)
                {
                    if (k.Text != si.Text) continue;
                    double dx = (k.Bounds.X + k.Bounds.Width / 2.0) - (si.Bounds.X + si.Bounds.Width / 2.0);
                    double dy = (k.Bounds.Y + k.Bounds.Height / 2.0) - (si.Bounds.Y + si.Bounds.Height / 2.0);
                    if (Math.Sqrt(dx * dx + dy * dy) < maxDist)
                    {
                        k.Bounds = Rectangle.Union(k.Bounds, si.Bounds);   // e.g. all cells of a 2x2 slot
                        twin = true;
                        break;
                    }
                }
                if (!twin) kept.Add(si);
            }
            res.Items = kept;
        }

        static void BuildItems(ScanResult res, ProbeGroup g, Func<ParsedItem, PriceInfo> lookup)
        {
            int rows = g.Rows, cols = g.Cols;
            string[,] texts = g.Texts;
            bool[,] seen = new bool[rows, cols];

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    string txt = texts[r, c];
                    if (txt == null || seen[r, c]) continue;
                    ParsedItem it = ItemParser.Parse(txt);
                    if (it == null) continue;
                    seen[r, c] = true;
                    Rectangle bounds = g.Rects[r, c];

                    if (it.IsMultiCellCandidate)
                    {
                        // Big items (e.g. 2x3 armour) answer on every cell they cover: flood-fill identical neighbours.
                        Queue<Point> q = new Queue<Point>();
                        q.Enqueue(new Point(c, r));
                        while (q.Count > 0)
                        {
                            Point p = q.Dequeue();
                            Point[] nb = { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) };
                            foreach (Point n in nb)
                            {
                                if (n.X < 0 || n.Y < 0 || n.X >= cols || n.Y >= rows || seen[n.Y, n.X]) continue;
                                if (texts[n.Y, n.X] != txt) continue;
                                seen[n.Y, n.X] = true;
                                bounds = Rectangle.Union(bounds, g.Rects[n.Y, n.X]);
                                q.Enqueue(n);
                            }
                        }
                    }

                    ScanItem si = new ScanItem();
                    si.Text = txt;
                    si.Item = it;
                    si.Qty = it.Stack;
                    si.Price = lookup(it);
                    si.TotalDiv = si.Price != null ? si.Price.Div * si.Qty : 0;
                    si.Bounds = bounds;
                    res.Items.Add(si);
                }
        }
    }
}
