using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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
        public bool FixedLayout;    // a saved fixed-slot tab: every separate item area is one slot (can be 2x2)
        public int HoverDelay;
        public int CopyTimeout;

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
        public bool Aborted;
        public bool StashNotFound;
        public TabProfile Tab;         // recognised saved tab, or null
        public PixelBuffer Snapshot;   // the stash as captured before hovering (baseline for tab-change detection)
    }

    /// <summary>What to hover, decided from one clean screenshot.</summary>
    public class ScanPlan
    {
        public bool StashVisible;
        public TabProfile Tab;                // recognised saved tab, or null
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
        public double[,] Score;      // share of item background (shown in preview)
        public string[,] Texts;
        public int Priority;         // lower wins when probes of different groups overlap

        public ProbeGroup(int rows, int cols)
        {
            Rows = rows; Cols = cols;
            Rects = new Rectangle[rows, cols];
            Active = new bool[rows, cols];
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
            if (known != null)
                foreach (Rectangle slot in known)
                {
                    int kx = Math.Max(1, (int)Math.Round(slot.Width / cs)), ky = Math.Max(1, (int)Math.Round(slot.Height / cs));
                    for (int r = 0; r < ky; r++)
                        for (int c = 0; c < kx; c++)
                        {
                            Rectangle k = Cell(slot, kx, ky, r, c);
                            ProbeGroup g = new ProbeGroup(1, 1);
                            g.Rects[0, 0] = Offset(k, origin);
                            g.Score[0, 0] = SlotDetector.TintFraction(pb, k, cfg.TintSensitivity);
                            g.Active[0, 0] = Occupied(pb, k, cfg);
                            g.Priority = -1;
                            groups.Add(g);
                        }
                }
            List<Rectangle> blobs = SlotDetector.Detect(pb, cs, cfg.TintSensitivity);

            // Fixed-slot tabs place slots in regular rows and columns. The cleanly detected single items
            // reveal those lines; testing every row x column crossing catches items whose own area was
            // missed or merged with something else.
            ProbeGroup lattice = Lattice(pb, cfg, blobs, origin);
            if (lattice != null) groups.Add(lattice);

            foreach (Rectangle blob in blobs)
            {
                // Touching items form one area: split it into cells and test each one. A big slot (Fragments
                // has 2x2 ones) is split too; its cells all read the same item, which is merged afterwards.
                int nx = Math.Max(1, (int)Math.Round(blob.Width / cs));
                int ny = Math.Max(1, (int)Math.Round(blob.Height / cs));
                ProbeGroup g = new ProbeGroup(ny, nx);
                for (int r = 0; r < ny; r++)
                    for (int c = 0; c < nx; c++)
                    {
                        Rectangle sub = Cell(blob, nx, ny, r, c);
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
            StashLocator.Result loc = StashLocator.Locate(full);
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
            double difference;
            plan.FrameColor = loc.FrameColor;
            plan.Tab = profiles == null ? null : TabLibrary.Identify(plan.Snapshot, loc.FrameColor, profiles, out difference);
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

        string CopyHovered()
        {
            uint seq = Native.GetClipboardSequenceNumber();
            Native.SendCtrlC();
            int waited = 0;
            while (Native.GetClipboardSequenceNumber() == seq)
            {
                if (waited >= cfg.CopyTimeout) return null;   // nothing copied: empty slot
                Thread.Sleep(5);
                waited += 5;
            }
            Thread.Sleep(5);
            string txt = ReadClipboard();
            return ItemParser.LooksLikeItem(txt) ? txt : null;
        }

        public ScanResult Run(ICollection<TabProfile> profiles, Func<ParsedItem, PriceInfo> lookup, Action<int, int> progress)
        {
            ScanResult res = new ScanResult();

            // Don't start while the user still holds the hotkey / modifiers.
            for (int i = 0; i < 100 && (Native.IsKeyDown(Native.VK_F7) || Native.IsKeyDown(Native.VK_CONTROL)); i++) Thread.Sleep(10);

            Native.POINT orig;
            Native.GetCursorPos(out orig);
            ScanPlan plan = Prepare(cfg, profiles);
            res.Tab = plan.Tab;
            if (!plan.StashVisible)
            {
                Native.MoveMouse(orig.X, orig.Y);
                res.StashNotFound = true;
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
            ReadCountsFromScreen(res);
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
        static void MergeNearDuplicates(ScanResult res, double maxDist)
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
