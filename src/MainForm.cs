using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PoeStashPricer
{
    public class MainForm : Form
    {
        const int HK_CAPTURE = 1, HK_SCAN = 2, HK_OVERLAY = 3, HK_SKIP = 4;
        const string UnknownTab = "_unknown";   // a scanned tab that isn't one of the saved ones

        readonly AppSettings settings = AppSettings.Load();
        readonly OverlayForm overlay = new OverlayForm();
        readonly TabWatcher watcher = new TabWatcher();
        readonly System.Windows.Forms.Timer watchTimer = new System.Windows.Forms.Timer { Interval = 500 };
        readonly float dpi;
        Dictionary<string, TabProfile> profiles = TabLibrary.LoadAll();
        Dictionary<string, TabResult> results = ResultStore.Load();
        TabResult unknownResult;
        volatile PriceTable table;
        bool loadingPrices, busy;
        Scanner scanner;
        Queue<string> wizard;   // tabs still to be saved in "save in order" mode, or null

        // What the game shows right now (kept up to date by the watcher).
        IntPtr gameHwnd;
        Rectangle stashRegion;          // screen coordinates, empty until the stash was found once
        string currentTab;              // key of the tab on screen, UnknownTab, or null (stash closed / not recognised)
        bool stashVisible;
        DateTime nextDetect = DateTime.MinValue;
        DateTime settleUntil = DateTime.MinValue;   // after a change, keep checking every tick until then
        bool overlayWanted = true;      // F8 turns the price overlay off and on
        string overlayState = "";       // what the overlay shows, to avoid redrawing the same thing
        string viewKey;                 // tab shown in the item list when picked by hand; null = follow the game

        ComboBox cbLeague, cbCurrency;
        NumericUpDown nudDelay;
        Button btnRefresh, btnWizard, btnCaptureOne, btnDeleteTab, btnPreview, btnScan, btnOverlay;
        Label lblGrand, lblGrandSub, lblView, lblStatus;
        ListView tabList, list;
        ProgressBar progress;

        static readonly string[] CurrencyKeys = { "auto", "divine", "exalted", "chaos" };
        static readonly string[] CurrencyNames = { "Otomatik", "Divine", "Exalted", "Chaos" };

        public MainForm()
        {
            using (Graphics g = CreateGraphics()) dpi = g.DpiX / 96f;
            Text = "PoE2 Stash Pricer";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(S(960), S(660));
            MinimumSize = new Size(S(760), S(480));
            StartPosition = FormStartPosition.CenterScreen;
            BuildUi();
            RefreshAll();

            // Create the overlay window now so it is excluded from screen captures before it is ever shown.
            IntPtr overlayHandle = overlay.Handle;
            watchTimer.Tick += WatchTick;
            watchTimer.Start();
        }

        int S(int px) { return (int)Math.Round(px * dpi); }

        // ---------------------------------------------------------------- UI

        FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 2, 0, 2) };
        }

        Label L(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(S(6), S(7), S(2), 0) };
        }

        Button B(string text, EventHandler click)
        {
            Button b = new Button { Text = text, AutoSize = true, Padding = new Padding(S(4), 0, S(4), 0) };
            b.Click += click;
            return b;
        }

        void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(S(8)) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // Row 1: stash total
            FlowLayoutPanel r0 = Row();
            lblGrand = new Label { AutoSize = true, Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Color.DarkGoldenrod, Text = "Toplam stash değeri: —" };
            lblGrandSub = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(S(10), S(10), 0, 0) };
            r0.Controls.AddRange(new Control[] { lblGrand, lblGrandSub });
            root.Controls.Add(r0);

            // Row 2: league / currency
            FlowLayoutPanel r1 = Row();
            cbLeague = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(200) };
            cbLeague.SelectedIndexChanged += delegate
            {
                if (cbLeague.SelectedItem == null || (string)cbLeague.SelectedItem == settings.League && table != null) return;
                settings.League = (string)cbLeague.SelectedItem;
                settings.Save();
                LoadPrices();
            };
            cbCurrency = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(110) };
            cbCurrency.Items.AddRange(CurrencyNames);
            int ci = Array.IndexOf(CurrencyKeys, settings.DisplayCurrency);
            cbCurrency.SelectedIndex = ci < 0 ? 0 : ci;
            cbCurrency.SelectedIndexChanged += delegate
            {
                settings.DisplayCurrency = CurrencyKeys[Math.Max(0, cbCurrency.SelectedIndex)];
                settings.Save();
                RefreshAll();
            };
            btnRefresh = B("Fiyatları yenile", delegate { LoadPrices(); });
            r1.Controls.AddRange(new Control[] { L("Lig:"), cbLeague, L("Göster:"), cbCurrency, btnRefresh });
            root.Controls.Add(r1);

            // Row 3: tabs (left) + items of one tab (right)
            TableLayoutPanel mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(390)));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0, 0, S(8), 0) };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(new Label { Text = "Sekmeler", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, S(4), 0, S(4)) });
            tabList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            tabList.Columns.Add("Sekme", S(160));
            tabList.Columns.Add("Değer", S(80), HorizontalAlignment.Right);
            tabList.Columns.Add("Durum", S(125));
            tabList.ItemSelectionChanged += (s, e) =>
            {
                if (!e.IsSelected || refreshingTabs) return;
                viewKey = (string)e.Item.Tag;
                RefreshItems();
            };
            left.Controls.Add(tabList);
            FlowLayoutPanel tabButtons = Row();
            btnWizard = B("Sırayla kaydet", delegate { StartWizard(); });
            btnCaptureOne = B("Seçileni kaydet", delegate { StartSingleCapture(); });
            btnDeleteTab = B("Sil", delegate { DeleteSelectedTab(); });
            tabButtons.Controls.AddRange(new Control[] { btnWizard, btnCaptureOne, btnDeleteTab });
            left.Controls.Add(tabButtons);
            mid.Controls.Add(left, 0, 0);

            TableLayoutPanel right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0) };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            lblView = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, S(4), 0, S(4)) };
            right.Controls.Add(lblView);
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
            list.Columns.Add("Eşya", S(200));
            list.Columns.Add("Adet", S(55), HorizontalAlignment.Right);
            list.Columns.Add("Birim fiyat", S(85), HorizontalAlignment.Right);
            list.Columns.Add("Toplam", S(85), HorizontalAlignment.Right);
            list.Columns.Add("Kategori", S(90));
            right.Controls.Add(list);
            mid.Controls.Add(right, 1, 0);
            root.Controls.Add(mid);

            // Row 4: actions
            FlowLayoutPanel r3 = Row();
            btnPreview = B("Önizle", delegate { Preview(); });
            btnScan = B("Tara  (F7)", delegate { StartScan(false); });
            btnScan.Font = new Font(Font, FontStyle.Bold);
            btnOverlay = B("Katman  (F8)", delegate { ToggleOverlay(); });
            nudDelay = new NumericUpDown { Minimum = 10, Maximum = 500, Width = S(55), Margin = new Padding(0, S(4), S(4), 0), Value = Math.Max(10, Math.Min(500, settings.HoverDelay)) };
            nudDelay.ValueChanged += delegate { settings.HoverDelay = (int)nudDelay.Value; settings.Save(); };
            r3.Controls.AddRange(new Control[] { btnPreview, btnScan, btnOverlay, L("Gecikme (ms):"), nudDelay });
            root.Controls.Add(r3);

            // Row 5: status
            FlowLayoutPanel r5 = Row();
            progress = new ProgressBar { Width = S(160), Height = S(16), Margin = new Padding(0, S(4), S(6), 0) };
            lblStatus = new Label { AutoSize = true, Margin = new Padding(0, S(4), 0, 0) };
            r5.Controls.AddRange(new Control[] { progress, lblStatus });
            root.Controls.Add(r5);
        }

        void SetStatus(string s) { lblStatus.Text = s; }

        static string TabName(string key)
        {
            return key == UnknownTab ? "Kayıtlı olmayan sekme" : TabLibrary.NameOf(key);
        }

        TabResult ResultFor(string key)
        {
            if (key == null) return null;
            if (key == UnknownTab) return unknownResult;
            TabResult tr;
            return results.TryGetValue(key, out tr) ? tr : null;
        }

        /// <summary>Redraws everything that depends on results or prices.</summary>
        void RefreshAll()
        {
            RefreshGrandTotal();
            RefreshTabList();
            RefreshItems();
            UpdateOverlay(true);
        }

        void RefreshGrandTotal()
        {
            PriceTable t = table;
            if (results.Count == 0)
            {
                lblGrand.Text = "Toplam stash değeri: —";
                lblGrandSub.Text = "Henüz taranmış sekme yok. Oyunda bir sekme açıp F7'ye basın.";
                return;
            }
            double sum = results.Values.Sum(r => ResultStore.Total(r, t));
            lblGrand.Text = "Toplam stash değeri: " + (t == null ? "fiyatlar yükleniyor..." : Fmt(sum));
            string alt = t != null && t.ExPerDiv > 0 ? string.Format("≈ {0} div / {1} ex · ", Num(sum), Num(sum * t.ExPerDiv)) : "";
            DateTime oldest = results.Values.Min(r => r.ScannedAt);
            lblGrandSub.Text = string.Format("{0}{1} sekme taranmış · en eski tarama {2:dd.MM HH:mm}", alt, results.Count, oldest);
        }

        bool refreshingTabs;

        void RefreshTabList()
        {
            PriceTable t = table;
            string selected = viewKey;
            refreshingTabs = true;
            tabList.BeginUpdate();
            tabList.Items.Clear();
            foreach (TabDef d in TabLibrary.Tabs)
            {
                TabProfile p;
                bool saved = profiles.TryGetValue(d.Key, out p);
                TabResult r = ResultFor(d.Key);
                ListViewItem li = new ListViewItem(d.Name) { Tag = d.Key };
                li.SubItems.Add(r != null && t != null ? Fmt(ResultStore.Total(r, t)) : "");
                string status = !saved ? "kaydedilmedi" : r != null ? string.Format("tarandı {0:dd.MM HH:mm}", r.ScannedAt) : "kayıtlı, taranmadı";
                if (d.Key == currentTab) status = "▶ " + status;
                li.SubItems.Add(status);
                if (!saved) li.ForeColor = Color.Gray;
                if (d.Key == currentTab) li.Font = new Font(tabList.Font, FontStyle.Bold);
                if (wizard != null && wizard.Count > 0 && wizard.Peek() == d.Key) li.BackColor = Color.LightGoldenrodYellow;
                if (d.Key == selected) li.Selected = true;
                tabList.Items.Add(li);
            }
            tabList.EndUpdate();
            refreshingTabs = false;
        }

        string SelectedTabKey()
        {
            return tabList.SelectedItems.Count > 0 ? (string)tabList.SelectedItems[0].Tag : null;
        }

        class ResultRow
        {
            public string Name, Category;
            public int Qty;
            public double Unit, Total;
            public bool Priced, Unread;
        }

        /// <summary>Item list for the tab picked in the list, or else the one open in the game.</summary>
        void RefreshItems()
        {
            string key = viewKey ?? currentTab;
            TabResult tr = ResultFor(key);
            List<PricedItem> items = ResultStore.Price(tr, table, Rectangle.Empty);

            Dictionary<string, ResultRow> rows = new Dictionary<string, ResultRow>();
            foreach (PricedItem pi in items)
            {
                string name = pi.Price != null ? pi.Price.Name : pi.Item.DisplayName;
                ResultRow row;
                if (!rows.TryGetValue(name, out row))
                {
                    row = new ResultRow { Name = name, Priced = pi.Price != null };
                    row.Unit = pi.Price != null ? pi.Price.Div : 0;
                    row.Category = pi.Price != null ? pi.Price.Category : (pi.Item.Rarity ?? pi.Item.ItemClass ?? "");
                    rows[name] = row;
                }
                row.Qty += pi.Qty;
                row.Total += pi.TotalDiv;
                row.Unread |= pi.CountUnread;
            }

            list.BeginUpdate();
            list.Items.Clear();
            foreach (ResultRow r in rows.Values.OrderByDescending(r => r.Priced).ThenByDescending(r => r.Total).ThenBy(r => r.Name))
            {
                ListViewItem li = new ListViewItem(r.Name);
                li.SubItems.Add(r.Qty.ToString("#,0") + (r.Unread ? "?" : ""));
                li.SubItems.Add(r.Priced ? Fmt(r.Unit) : "—");
                li.SubItems.Add(r.Priced ? Fmt(r.Total) : "fiyat yok");
                li.SubItems.Add(r.Category);
                if (!r.Priced) li.ForeColor = Color.Gray;
                list.Items.Add(li);
            }
            list.EndUpdate();

            if (key == null) lblView.Text = "Oyunda taranmış bir sekme açın veya soldan bir sekme seçin.";
            else if (tr == null) lblView.Text = TabName(key) + " · henüz taranmadı (F7)";
            else lblView.Text = string.Format("{0} · {1} · {2:dd.MM HH:mm} taraması", TabName(key), Fmt(items.Sum(i => i.TotalDiv)), tr.ScannedAt);
        }

        // ---------------------------------------------------------------- lifecycle / hotkeys

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            bool ok = Native.RegisterHotKey(Handle, HK_CAPTURE, Native.MOD_NOREPEAT, Native.VK_F6);
            ok &= Native.RegisterHotKey(Handle, HK_SCAN, Native.MOD_NOREPEAT, Native.VK_F7);
            ok &= Native.RegisterHotKey(Handle, HK_OVERLAY, Native.MOD_NOREPEAT, Native.VK_F8);
            if (!ok) SetStatus("Uyarı: F6/F7/F8 kısayolları kaydedilemedi (başka bir uygulama kullanıyor olabilir).");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LoadLeagues();
            if (profiles.Count == 0)
                SetStatus("Başlamak için 'Sırayla kaydet' ile sekmelerinizin görüntüsünü kaydedin.");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (scanner != null) scanner.CancelRequested = true;
            Native.UnregisterHotKey(Handle, HK_CAPTURE);
            Native.UnregisterHotKey(Handle, HK_SCAN);
            Native.UnregisterHotKey(Handle, HK_OVERLAY);
            Native.UnregisterHotKey(Handle, HK_SKIP);
            settings.Save();
            overlay.Close();
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                switch (m.WParam.ToInt32())
                {
                    case HK_CAPTURE: CaptureTab(true); break;
                    case HK_SCAN: StartScan(true); break;
                    case HK_OVERLAY: ToggleOverlay(); break;
                    case HK_SKIP: SkipWizardTab(); break;
                }
                return;
            }
            base.WndProc(ref m);
        }

        void ToggleOverlay()
        {
            // A message or preview on screen: F8 just dismisses it.
            if (overlay.Visible && !overlayState.StartsWith("tab:")) { overlay.HideOverlay(); overlayState = "hidden"; return; }
            overlayWanted = !overlayWanted;
            UpdateOverlay(true);
            SetStatus(overlayWanted ? "Fiyat katmanı açık." : "Fiyat katmanı kapalı (F8 ile açın).");
        }

        // ---------------------------------------------------------------- following the game

        /// <summary>
        /// Keeps track of which tab is open: when the stash picture changes (and the cursor isn't over it,
        /// where item tooltips come and go) the stash is located and recognised again. Results are never
        /// thrown away; the overlay just follows the tab on screen.
        /// </summary>
        void WatchTick(object sender, EventArgs e)
        {
            if (busy) return;
            try
            {
                UpdateOverlay(false);   // e.g. the game lost or regained focus

                IntPtr fg = Native.GetForegroundWindow();
                if (!Native.IsGameWindow(fg)) return;
                gameHwnd = fg;
                if (profiles.Count == 0 && results.Count == 0) return;

                bool due = DateTime.Now >= nextDetect;
                bool cursorOver = false;
                if (!stashRegion.IsEmpty)
                {
                    Rectangle guard = stashRegion;
                    guard.Inflate(8, 8);
                    cursorOver = guard.Contains(Cursor.Position);
                    if (!stashVisible)
                    {
                        // Stash closed: the game world moves all the time, so only look now and then.
                        if (!due) return;
                    }
                    else if (DateTime.Now > settleUntil)
                    {
                        // Look again when the picture changed. With the cursor over the stash item tooltips change
                        // it constantly, so then only at the slower pace.
                        if (!watcher.Active || !watcher.HasChanged()) return;
                        if (cursorOver && !due) return;
                        settleUntil = DateTime.Now.AddMilliseconds(1500);
                    }
                    // Within settleUntil: keep looking every tick, so a tab that is still fading in (or was
                    // captured before the game redrew it) is caught once it is fully shown.
                }
                else if (!due) return;

                Detect(cursorOver);
            }
            catch { }
        }

        /// <param name="cursorOver">
        /// The cursor is over the stash, so an item tooltip may hide part of it: only clear evidence counts
        /// (another saved tab recognised, a different frame colour, or the stash gone).
        /// </param>
        void Detect(bool cursorOver)
        {
            Rectangle win = Native.ClientRectOnScreen(gameHwnd);
            if (win.Width <= 0 || win.Height <= 0) return;
            PixelBuffer full;
            using (Bitmap bmp = Grid.Capture(win)) full = new PixelBuffer(bmp);

            StashLocator.Result loc = StashLocator.Locate(full);
            TabProfile tab = null;
            double diff;
            if (loc.StashVisible || profiles.Count > 0)
            {
                tab = TabLibrary.Identify(full.Crop(loc.Region), loc.StashVisible ? loc.FrameColor : null, profiles.Values, out diff);
                if (!loc.StashVisible && tab != null && diff > 0.15) tab = null;   // without a frame, only a sure match counts
                if (tab != null) loc.EdgesFound = 4;   // a saved tab is on screen even if its frame was faint
            }

            if (cursorOver && stashVisible)
            {
                TabProfile shown = currentTab != null && profiles.ContainsKey(currentTab) ? profiles[currentTab] : null;
                bool otherTab = tab != null && tab.Key != currentTab;
                bool otherColour = tab == null && loc.FrameColor != null && shown != null && shown.FrameColor != null
                                   && StashLocator.ColorDistance(loc.FrameColor, shown.FrameColor) > 0.15;
                bool closed = loc.EdgesFound <= 1;   // a tooltip hides one or two edges at most
                if (!otherTab && !otherColour && !closed)
                {
                    nextDetect = DateTime.Now.AddMilliseconds(1000);
                    return;   // probably just a tooltip: keep showing what we have
                }
            }

            stashVisible = loc.StashVisible;
            // While the stash is closed the game world moves all the time: look again only now and then.
            nextDetect = DateTime.Now.AddMilliseconds(stashVisible ? 500 : 1500);
            if (stashVisible)
            {
                stashRegion = new Rectangle(win.X + loc.Region.X, win.Y + loc.Region.Y, loc.Region.Width, loc.Region.Height);
                watcher.SetBaseline(full.Crop(loc.Region), stashRegion, 12, 12);
            }
            else if (!stashRegion.IsEmpty)
            {
                Rectangle local = stashRegion;
                local.Offset(-win.X, -win.Y);
                watcher.SetBaseline(full.Crop(local), stashRegion, 12, 12);
            }

            string key = stashVisible && tab != null ? tab.Key : null;
            bool changed = key != currentTab;
            if (changed)
            {
                currentTab = key;
                viewKey = null;   // the item list follows the game again
                RefreshTabList();
                RefreshItems();
            }
            // Another tab replaces a preview, but the save-in-order instructions stay until done.
            UpdateOverlay(changed && wizard == null);
        }

        /// <summary>Shows the saved prices of the tab on screen, or hides the overlay when there are none.</summary>
        void UpdateOverlay(bool force)
        {
            // Messages and previews stay until the tab changes or F8.
            if (!force && (overlayState == "message" || overlayState == "preview") && overlay.Visible) return;

            bool gameFront = Native.IsGameWindow(Native.GetForegroundWindow());
            TabResult tr = ResultFor(currentTab);
            bool show = overlayWanted && gameFront && stashVisible && tr != null && !stashRegion.IsEmpty;
            string state = show ? string.Format("tab:{0}:{1:O}:{2}:{3}:{4}", currentTab, tr.ScannedAt, stashRegion, table == null ? 0 : table.LoadedAt.Ticks, settings.DisplayCurrency) : "hidden";
            if (!force && state == overlayState) return;
            overlayState = state;
            if (!show) { overlay.HideOverlay(); return; }

            List<PricedItem> items = ResultStore.Price(tr, table, stashRegion);
            List<OverlayLabel> labels = new List<OverlayLabel>();
            foreach (PricedItem pi in items)
                if (pi.Price != null)
                    labels.Add(new OverlayLabel { Bounds = pi.Bounds, Text = Fmt(pi.TotalDiv), Color = ValueColor(pi.TotalDiv) });
            double sum = items.Sum(i => i.TotalDiv), grand = results.Values.Sum(r => ResultStore.Total(r, table));
            string header = string.Format("{0}: {1}  ·  {2:HH:mm} taraması  ·  Stash toplamı: {3}  ·  F7: yeniden tara · F8: gizle",
                                          TabName(currentTab), Fmt(sum), tr.ScannedAt, Fmt(grand));
            overlay.ShowLabels(labels, header, stashRegion);
        }

        // ---------------------------------------------------------------- prices

        async void LoadLeagues()
        {
            SetStatus("Ligler alınıyor...");
            List<string> leagues;
            try { leagues = await Task.Run(() => PriceService.GetLeagues()); }
            catch (Exception ex)
            {
                SetStatus("poe.ninja'ya erişilemedi: " + ex.Message);
                leagues = new List<string>();
                if (!string.IsNullOrEmpty(settings.League)) leagues.Add(settings.League);
            }
            int idx = settings.League != null ? leagues.IndexOf(settings.League) : -1;
            if (idx < 0 && leagues.Count > 0) { idx = 0; settings.League = leagues[0]; settings.Save(); }
            cbLeague.Items.Clear();
            foreach (string l in leagues) cbLeague.Items.Add(l);
            if (idx >= 0) cbLeague.SelectedIndex = idx;   // triggers LoadPrices via the handler when needed
            if (table == null && settings.League != null) LoadPrices();
        }

        async void LoadPrices()
        {
            string league = settings.League;
            if (loadingPrices || string.IsNullOrEmpty(league)) return;
            loadingPrices = true;
            btnRefresh.Enabled = false;
            try
            {
                Action<string> report = s => BeginInvoke((Action)(() => SetStatus(s)));
                PriceTable t = await Task.Run(() => PriceService.Load(league, report));
                if (t.Count == 0) { SetStatus("Fiyat alınamadı. İnternet bağlantısını kontrol edip tekrar deneyin."); return; }
                table = t;
                string msg = string.Format("{0} fiyatları: {1} eşya · 1 div = {2:0} ex · {3:HH:mm}", league, t.Count, t.ExPerDiv, t.LoadedAt);
                if (t.Failed.Count > 0) msg += " · alınamayan: " + string.Join(", ", t.Failed.ToArray());
                SetStatus(msg);
                RefreshAll();
            }
            catch (Exception ex) { SetStatus("Fiyat yükleme hatası: " + ex.Message); }
            finally
            {
                loadingPrices = false;
                btnRefresh.Enabled = true;
            }
        }

        // ---------------------------------------------------------------- game window helpers

        static Task<T> RunSta<T>(Func<T> fn)
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
            Thread th = new Thread(() =>
            {
                try { tcs.SetResult(fn()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
            return tcs.Task;
        }

        /// <summary>
        /// The game window to work on: the foreground window when triggered by a hotkey (the user is in the
        /// game), otherwise the game is found and brought to the front. Zero if not possible.
        /// </summary>
        async Task<IntPtr> GetGame(bool fromHotkey)
        {
            if (fromHotkey)
            {
                IntPtr fg = Native.GetForegroundWindow();
                return Native.IsGameWindow(fg) ? fg : IntPtr.Zero;
            }
            IntPtr game = Native.FindGameWindow();
            if (game == IntPtr.Zero) return IntPtr.Zero;
            if (Native.IsIconic(game)) Native.ShowWindow(game, 9);
            Native.SetForegroundWindow(game);
            await Task.Delay(400);
            return Native.GetForegroundWindow() == game ? game : IntPtr.Zero;
        }

        ScanConfig BuildConfig(IntPtr game)
        {
            return new ScanConfig
            {
                Window = Native.ClientRectOnScreen(game),
                TintSensitivity = Math.Max(1, settings.TintSensitivity),
                Threshold = settings.Threshold > 0 ? settings.Threshold : 12,
                HoverDelay = settings.HoverDelay,
                CopyTimeout = Math.Max(100, settings.CopyTimeout)
            };
        }

        const string NoGame = "Path of Exile 2 penceresi bulunamadı. Oyundayken kısayol tuşlarını kullanın.";
        const string NoStash = "Stash görünmüyor. Stash'i açın, farenin stash'in üstünde olmadığından emin olun ve tekrar deneyin.";

        /// <summary>Where the stash would be for this game window (for placing messages before it is found).</summary>
        static Rectangle PredictedStash(ScanConfig cfg)
        {
            Rectangle r = StashLocator.Predict(cfg.Window.Size);
            r.Offset(cfg.Window.Location);
            return r;
        }

        void ShowMessage(string text, ScanConfig cfg)
        {
            overlayState = "message";
            overlay.ShowLabels(new List<OverlayLabel>(), text, PredictedStash(cfg));
        }

        /// <summary>Remembers where the stash is after a scan, preview or save, so the watcher can follow it.</summary>
        void NoteStash(IntPtr game, Rectangle region, PixelBuffer snapshot, string key)
        {
            gameHwnd = game;
            stashRegion = region;
            stashVisible = true;
            if (snapshot != null) watcher.SetBaseline(snapshot, region, 12, 12);
            if (key != currentTab)
            {
                currentTab = key;
                viewKey = null;
            }
        }

        // ---------------------------------------------------------------- saving tabs

        void StartWizard()
        {
            // Continue with the tabs not saved yet; once all are saved, go through all of them again.
            List<string> missing = TabLibrary.Tabs.Select(t => t.Key).Where(k => !profiles.ContainsKey(k)).ToList();
            wizard = new Queue<string>(missing.Count > 0 ? missing : TabLibrary.Tabs.Select(t => t.Key).ToList());
            Native.RegisterHotKey(Handle, HK_SKIP, Native.MOD_NOREPEAT, Native.VK_F9);
            RefreshTabList();
            PromptWizard(null);
        }

        void StartSingleCapture()
        {
            string key = SelectedTabKey();
            if (key == null) { SetStatus("Önce listeden bir sekme seçin."); return; }
            wizard = new Queue<string>(new[] { key });
            Native.RegisterHotKey(Handle, HK_SKIP, Native.MOD_NOREPEAT, Native.VK_F9);
            RefreshTabList();
            PromptWizard(null);
        }

        void EndWizard(string message)
        {
            wizard = null;
            Native.UnregisterHotKey(Handle, HK_SKIP);
            RefreshTabList();
            SetStatus(message);
        }

        /// <summary>Tells the user (in the app and over the game) which tab to open next.</summary>
        void PromptWizard(string previous)
        {
            RefreshTabList();
            if (wizard == null || wizard.Count == 0)
            {
                EndWizard((previous != null ? previous + " " : "") + "Kayıt tamamlandı. Artık bir sekme açıp F7 ile tarayabilirsiniz.");
                IntPtr g = Native.FindGameWindow();
                if (g != IntPtr.Zero) ShowMessage((previous != null ? previous + "  " : "") + "Kayıt tamamlandı. F7: tara · F8: bu mesajı gizle", BuildConfig(g));
                return;
            }
            string next = TabLibrary.NameOf(wizard.Peek());
            string text = string.Format("{0}Oyunda '{1}' sekmesini açın ve F6'ya basın.  F9: bu sekmeyi atla", previous != null ? previous + "  " : "", next);
            SetStatus(text);
            IntPtr game = Native.FindGameWindow();
            if (game != IntPtr.Zero) ShowMessage(text, BuildConfig(game));
        }

        void SkipWizardTab()
        {
            if (wizard == null || wizard.Count == 0 || busy) return;
            string skipped = TabLibrary.NameOf(wizard.Dequeue());
            PromptWizard(skipped + " atlandı.");
        }

        async void CaptureTab(bool fromHotkey)
        {
            if (busy) return;
            string key = wizard != null && wizard.Count > 0 ? wizard.Peek() : SelectedTabKey();
            if (key == null)
            {
                SetStatus("F6: hangi sekmeyi kaydedeceğimi bilmiyorum. 'Sırayla kaydet'e basın ya da listeden bir sekme seçip 'Seçileni kaydet' deyin.");
                return;
            }
            IntPtr game = await GetGame(fromHotkey);
            if (game == IntPtr.Zero) { SetStatus(NoGame); return; }

            busy = true;
            try
            {
                overlay.HideOverlay();
                ScanConfig cfg = BuildConfig(game);
                ScanPlan plan = await RunSta(() => Scanner.Prepare(cfg, null));
                if (!plan.StashVisible) { ShowMessage(NoStash, cfg); SetStatus(NoStash); return; }

                // Catch the easy mistake of saving the wrong tab (forgot to switch).
                TabProfile other = TabLibrary.IdentifyExact(plan.Snapshot, plan.FrameColor, profiles.Values.Where(p => p.Key != key).ToList());
                if (other != null)
                {
                    string warn = string.Format("Bu ekran zaten '{0}' olarak kayıtlı görünüyor. '{1}' sekmesini açıp tekrar F6'ya basın.  F9: atla",
                                                TabLibrary.NameOf(other.Key), TabLibrary.NameOf(key));
                    ShowMessage(warn, cfg);
                    SetStatus(warn);
                    return;
                }

                TabProfile learned = TabLibrary.Learn(key, plan.Snapshot, plan.FrameColor, cfg);
                TabLibrary.Save(learned, plan.Snapshot);
                profiles[key] = learned;
                NoteStash(game, cfg.Region, plan.Snapshot, key);

                // Show what was learned on top of the game.
                List<OverlayLabel> labels = new List<OverlayLabel>();
                foreach (Rectangle r in learned.SlotsIn(cfg.Region.Size))
                {
                    Rectangle s = r;
                    s.Offset(cfg.Region.Location);
                    labels.Add(new OverlayLabel { Bounds = s, Color = Color.Magenta, Outline = true });
                }
                string done = string.Format("'{0}' kaydedildi ({1} yuva).", TabLibrary.NameOf(key), learned.Slots.Count);
                if (wizard != null && wizard.Count > 0 && wizard.Peek() == key) wizard.Dequeue();
                PromptWizard(done);
                string header = wizard != null && wizard.Count > 0
                    ? string.Format("{0}  Sıradaki: '{1}' → açıp F6 · F9: atla", done, TabLibrary.NameOf(wizard.Peek()))
                    : done + "  F7: tara · F8: gizle";
                overlayState = "message";
                overlay.ShowLabels(labels, header, cfg.Region);
            }
            catch (Exception ex) { SetStatus("Kayıt hatası: " + ex.Message); }
            finally { busy = false; }
        }

        void DeleteSelectedTab()
        {
            string key = SelectedTabKey();
            if (key == null || (!profiles.ContainsKey(key) && !results.ContainsKey(key))) return;
            if (MessageBox.Show(this, "'" + TabLibrary.NameOf(key) + "' kaydı ve son tarama sonucu silinsin mi?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            TabLibrary.Delete(key);
            profiles.Remove(key);
            if (results.Remove(key)) ResultStore.Save(results);
            if (currentTab == key) currentTab = null;
            RefreshAll();
        }

        // ---------------------------------------------------------------- preview / scan

        async void Preview()
        {
            if (busy) return;
            IntPtr game = await GetGame(false);
            if (game == IntPtr.Zero) { MessageBox.Show(this, NoGame, Text); return; }
            busy = true;
            try
            {
                overlay.HideOverlay();
                ScanConfig cfg = BuildConfig(game);
                List<TabProfile> known = profiles.Values.ToList();
                ScanPlan plan = await RunSta(() => Scanner.Prepare(cfg, known));
                if (!plan.StashVisible) { ShowMessage(NoStash, cfg); SetStatus(NoStash); return; }
                NoteStash(game, cfg.Region, plan.Snapshot, plan.Tab != null ? plan.Tab.Key : null);

                List<OverlayLabel> labels = new List<OverlayLabel>();
                int full = 0;
                foreach (ProbeGroup g in plan.Groups)
                    for (int r = 0; r < g.Rows; r++)
                        for (int c = 0; c < g.Cols; c++)
                        {
                            if (!g.Active[r, c]) continue;
                            full++;
                            labels.Add(new OverlayLabel { Bounds = g.Rects[r, c], Color = Color.LimeGreen, Outline = true });
                        }
                labels.Add(new OverlayLabel { Bounds = cfg.Region, Color = Color.OrangeRed, Outline = true });
                string tab = plan.Tab != null ? "Sekme: " + TabLibrary.NameOf(plan.Tab.Key) : "Kayıtlı olmayan sekme";
                overlayState = "preview";
                overlay.ShowLabels(labels, string.Format("Önizleme · {0} · {1} eşya konumu taranacak.  F8: gizle", tab, full), cfg.Region);
                SetStatus(string.Format("Önizleme: {0}, {1} konum. Kaçan eşya varsa sekmeyi yeniden kaydedin.", tab, full));
                RefreshTabList();
                RefreshItems();
            }
            finally { busy = false; }
        }

        async void StartScan(bool fromHotkey)
        {
            if (busy)
            {
                if (scanner != null) scanner.CancelRequested = true;
                return;
            }
            PriceTable t = table;
            if (t == null) { SetStatus("Fiyatlar henüz yüklenmedi, biraz bekleyin."); return; }
            IntPtr game = await GetGame(fromHotkey);
            if (game == IntPtr.Zero)
            {
                if (fromHotkey) SetStatus("F7: aktif pencere Path of Exile 2 değil. Oyundayken basın.");
                else MessageBox.Show(this, NoGame, Text);
                return;
            }
            if ((DateTime.Now - t.LoadedAt).TotalMinutes > 60) LoadPrices();

            overlay.HideOverlay();
            overlayState = "hidden";
            ScanConfig cfg = BuildConfig(game);
            List<TabProfile> known = profiles.Values.ToList();
            busy = true;
            scanner = new Scanner(cfg, game);
            btnScan.Text = "Durdur (F7/Esc)";
            progress.Value = 0;
            SetStatus("Taranıyor... (Esc ile durdurabilirsiniz, fareye dokunmayın)");
            Scanner sc = scanner;
            try
            {
                ScanResult res = await RunSta(() => sc.Run(known, t.Lookup, (done, total) => BeginInvoke((Action)(() =>
                {
                    progress.Maximum = Math.Max(1, total);
                    progress.Value = Math.Min(done, progress.Maximum);
                }))));
                if (res.StashNotFound) { ShowMessage(NoStash, cfg); SetStatus(NoStash); return; }

                string key = res.Tab != null ? res.Tab.Key : UnknownTab;
                TabResult tr = ResultStore.FromScan(key, res, cfg.Region);
                if (res.Aborted && ResultFor(key) != null)
                {
                    SetStatus("Tarama durduruldu; bu sekmenin önceki sonucu korundu.");
                }
                else
                {
                    if (key == UnknownTab) unknownResult = tr;
                    else { results[key] = tr; ResultStore.Save(results); }
                }
                NoteStash(game, cfg.Region, res.Snapshot, key);
                RefreshAll();

                if (!res.Aborted || ResultFor(key) == tr)
                {
                    string status = string.Format("Tarama bitti ({0}): {1} konum denendi, {2} eşya okundu.", TabName(key), res.CellsTried, res.CellsCopied);
                    if (res.Aborted) status = "Tarama durduruldu (kısmi sonuç). " + status;
                    if (res.CellsTried > 0 && res.CellsCopied == 0)
                        status = "Oyundan hiç eşya metni alınamadı! Oyun yönetici olarak çalışıyorsa bu uygulamayı da yönetici olarak açın, ya da gecikmeyi artırın.";
                    else if (res.CellsTried == 0)
                        status = "Bu sekmede eşya bulunamadı.";
                    if (key == UnknownTab) status += " Bu sekme kayıtlı değil: toplam stash değerine eklenmez.";
                    int unread = res.Items.Count(i => i.CountUnread);
                    if (unread > 0)
                        status += string.Format(" {0} eşyanın adedi ekrandan okunamadı (listede \"?\"; 1 sayıldı). Rakamlar taramalarda öğreniliyor (bilinen: {1}); başka sekmeleri tarayıp bu sekmeyi yeniden tarayın.",
                                                unread, DigitReader.Known == "" ? "yok" : DigitReader.Known);
                    SetStatus(status);
                }
            }
            catch (Exception ex) { SetStatus("Tarama hatası: " + ex.Message); }
            finally
            {
                busy = false;
                scanner = null;
                btnScan.Text = "Tara  (F7)";
            }
        }

        // ---------------------------------------------------------------- formatting

        static string Num(double v)
        {
            if (v >= 1000) return v.ToString("#,0");
            if (v >= 100) return v.ToString("0");
            if (v >= 10) return v.ToString("0.#");
            if (v >= 1) return v.ToString("0.##");
            return v.ToString("0.###");
        }

        string Fmt(double div)
        {
            PriceTable t = table;
            double ex = t != null ? t.ExPerDiv : 0, ch = t != null ? t.ChaosPerDiv : 0;
            string mode = settings.DisplayCurrency ?? "auto";
            if (mode == "auto") mode = div >= 1 || ex <= 0 ? "divine" : "exalted";
            if (mode == "exalted" && ex > 0) return Num(div * ex) + " ex";
            if (mode == "chaos" && ch > 0) return Num(div * ch) + " c";
            return Num(div) + " div";
        }

        Color ValueColor(double div)
        {
            PriceTable t = table;
            double ex = t != null && t.ExPerDiv > 0 ? div * t.ExPerDiv : div * 500;
            if (div >= 1) return Color.Gold;
            if (ex >= 10) return Color.White;
            return Color.Silver;
        }
    }
}
