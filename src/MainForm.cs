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
        const int HK_SCAN = 2, HK_OVERLAY = 3;
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

        // What the game shows right now (kept up to date by the watcher).
        IntPtr gameHwnd;
        Rectangle stashRegion;          // screen coordinates, empty until the stash was found once
        string currentTab;              // key of the tab on screen, UnknownTab, or null (stash closed / not recognised)
        bool stashVisible;
        DateTime nextDetect = DateTime.MinValue;
        DateTime settleUntil = DateTime.MinValue;   // after a change, keep checking every tick until then
        bool overlayWanted = true;      // the overlay key turns the price overlay off and on
        string overlayState = "";       // what the overlay shows, to avoid redrawing the same thing
        string viewKey;                 // tab shown in the item list when picked by hand; null = follow the game

        ComboBox cbLeague, cbCurrency;
        NumericUpDown nudDelay;
        Button btnRefresh, btnRename, btnDeleteTab, btnDeleteAll, btnPreview, btnScan, btnOverlay, btnScanKey, btnOverlayKey;
        Label lblGrand, lblGrandSub, lblView, lblStatus;
        DarkListView tabList, list;
        ThinProgress progress;
        DarkCheckBox chkHover;

        // Price on hover: the tab on screen, ready for the user's own mouse.
        HoverFrame frame;
        readonly System.Windows.Forms.Timer hoverTimer = new System.Windows.Forms.Timer { Interval = 40 };
        Point restPos;
        DateTime restSince = DateTime.MaxValue;
        Rectangle lastSlot;             // slot copied last; copied again only after the mouse was elsewhere
        bool copying, clickHeld;
        int recheckTries;
        DateTime recheckAt = DateTime.MaxValue;

        static readonly string[] CurrencyKeys = { "auto", "divine", "exalted", "chaos" };
        static readonly string[] CurrencyNames = { "Auto", "Divine", "Exalted", "Chaos" };

        public MainForm()
        {
            using (Graphics g = CreateGraphics()) dpi = g.DpiX / 96f;
            Text = "PoE2 Stash Pricer";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(S(960), S(660));
            MinimumSize = new Size(S(760), S(480));
            StartPosition = FormStartPosition.CenterScreen;
            MoveLearnedTabsToLayouts();
            BuildUi();
            RefreshAll();

            // Create the overlay window now so it is excluded from screen captures before it is ever shown.
            IntPtr overlayHandle = overlay.Handle;
            watchTimer.Tick += WatchTick;
            watchTimer.Start();
            hoverTimer.Tick += HoverTick;
            hoverTimer.Start();
        }

        int S(int px) { return (int)Math.Round(px * dpi); }

        /// <summary>
        /// Tabs learned on this PC that a built-in layout covers now: their last scan moves over to the built-in
        /// tab (the same panel, so item positions still fit) and the learned copy goes, so no tab is listed or
        /// counted twice. The last scan of a paged tab is dropped: it only ever showed one page.
        /// </summary>
        void MoveLearnedTabsToLayouts()
        {
            List<TabProfile> layouts = profiles.Values.Where(p => p.BuiltIn).ToList();
            bool moved = false;
            foreach (TabProfile p in profiles.Values.Where(p => !p.BuiltIn).ToList())
            {
                var ranked = layouts.Select(l => new { l, d = TabLibrary.Distance(p.Signature, p.ItemMask, l.Signature, l.ItemMask) })
                                    .OrderBy(x => x.d).ToList();
                if (ranked.Count == 0 || ranked[0].d > 0.15 || (ranked.Count > 1 && ranked[1].d - ranked[0].d < 0.05)) continue;
                TabProfile layout = ranked[0].l;
                TabResult tr;
                if (results.TryGetValue(p.Key, out tr))
                {
                    results.Remove(p.Key);
                    if (!layout.Paged && !results.ContainsKey(layout.Key)) { tr.Key = layout.Key; results[layout.Key] = tr; }
                }
                TabLibrary.Delete(p.Key);
                profiles.Remove(p.Key);
                Log.Write(string.Format("learned tab '{0}' ({1}) is now the built-in '{2}' (difference {3:0.00})", p.Name, p.Key, layout.Name, ranked[0].d));
                moved = true;
            }
            if (moved) ResultStore.Save(results);
        }

        // ---------------------------------------------------------------- UI

        FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0), BackColor = Theme.Bg };
        }

        Label L(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(S(6), S(7), S(4), 0) };
        }

        Button B(string text, EventHandler click)
        {
            return Theme.Button(text, click, dpi);
        }

        void BuildUi()
        {
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(S(14)), BackColor = Theme.Bg };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(108)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // Row 1: stash total (left) and league / currency (right)
            Card hero = new Card { Hero = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, S(12)), Padding = new Padding(S(18), S(12), S(18), S(12)) };
            TableLayoutPanel heroGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0) };
            heroGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heroGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            FlowLayoutPanel total = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) };
            Label totalCaption = Theme.Caption("Total stash value", dpi);
            totalCaption.Margin = new Padding(0);
            lblGrand = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 22f), ForeColor = Theme.Gold, BackColor = Color.Transparent, Text = "—", Margin = new Padding(0) };
            lblGrandSub = new Label { AutoSize = true, ForeColor = Theme.Muted, BackColor = Color.Transparent, Margin = new Padding(S(2), 0, 0, 0) };
            totalCaption.BackColor = Color.Transparent;
            total.Controls.AddRange(new Control[] { totalCaption, lblGrand, lblGrandSub });
            heroGrid.Controls.Add(total, 0, 0);

            TableLayoutPanel prefs = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 2, BackColor = Color.Transparent, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            cbLeague = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(200), Margin = new Padding(0, 0, S(8), 0) };
            Theme.Style(cbLeague);
            cbLeague.SelectedIndexChanged += delegate
            {
                if (cbLeague.SelectedItem == null || (string)cbLeague.SelectedItem == settings.League && table != null) return;
                settings.League = (string)cbLeague.SelectedItem;
                settings.Save();
                LoadPrices();
            };
            cbCurrency = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(110), Margin = new Padding(0, 0, S(8), 0) };
            Theme.Style(cbCurrency);
            cbCurrency.Items.AddRange(CurrencyNames);
            int ci = Array.IndexOf(CurrencyKeys, settings.DisplayCurrency);
            cbCurrency.SelectedIndex = ci < 0 ? 0 : ci;
            cbCurrency.SelectedIndexChanged += delegate
            {
                settings.DisplayCurrency = CurrencyKeys[Math.Max(0, cbCurrency.SelectedIndex)];
                settings.Save();
                RefreshAll();
            };
            btnRefresh = B("Refresh prices", delegate { LoadPrices(); });
            btnRefresh.Margin = new Padding(0);
            btnRefresh.Padding = new Padding(S(10), 0, S(10), 0);
            Label leagueCaption = Theme.Caption("League", dpi), showCaption = Theme.Caption("Show in", dpi);
            leagueCaption.BackColor = showCaption.BackColor = Color.Transparent;
            prefs.Controls.Add(leagueCaption, 0, 0);
            prefs.Controls.Add(showCaption, 1, 0);
            prefs.Controls.Add(cbLeague, 0, 1);
            prefs.Controls.Add(cbCurrency, 1, 1);
            prefs.Controls.Add(btnRefresh, 2, 1);
            heroGrid.Controls.Add(prefs, 1, 0);
            hero.Controls.Add(heroGrid);
            root.Controls.Add(hero);

            // Row 2: tabs (left) + items of one tab (right)
            TableLayoutPanel mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = Theme.Bg };
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(400)));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Card leftCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, S(12), 0), Padding = new Padding(S(12)) };
            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0), BackColor = Theme.Surface };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(Theme.Caption("Tabs", dpi));
            tabList = new DarkListView(dpi) { Dock = DockStyle.Fill, MultiSelect = false, Margin = new Padding(0) };
            tabList.Columns.Add("Tab", S(140));
            tabList.Columns.Add("Value", S(95), HorizontalAlignment.Right);
            tabList.Columns.Add("Status", S(130));
            tabList.ItemSelectionChanged += (s, e) =>
            {
                if (!e.IsSelected || refreshingTabs) return;
                viewKey = (string)e.Item.Tag;
                RefreshItems();
            };
            left.Controls.Add(tabList);
            FlowLayoutPanel tabButtons = Row();
            tabButtons.Margin = new Padding(0, S(10), 0, 0);
            tabButtons.BackColor = Theme.Surface;
            btnRename = B("Rename", delegate { RenameSelectedTab(); });
            btnDeleteTab = B("Delete", delegate { DeleteSelectedTab(); });
            btnDeleteAll = B("Delete all", delegate { DeleteAll(); });
            btnDeleteAll.ForeColor = Theme.Danger;
            btnDeleteAll.EnabledChanged += delegate { btnDeleteAll.ForeColor = btnDeleteAll.Enabled ? Theme.Danger : Theme.Faint; };
            tabButtons.Controls.AddRange(new Control[] { btnRename, btnDeleteTab, btnDeleteAll });
            left.Controls.Add(tabButtons);
            leftCard.Controls.Add(left);
            mid.Controls.Add(leftCard, 0, 0);

            Card rightCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(S(12)) };
            TableLayoutPanel right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0), BackColor = Theme.Surface };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.Controls.Add(Theme.Caption("Items", dpi));
            lblView = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, S(8)) };
            right.Controls.Add(lblView);
            list = new DarkListView(dpi) { Dock = DockStyle.Fill, Margin = new Padding(0) };
            list.Columns.Add("Item", S(200));
            list.Columns.Add("Qty", S(60), HorizontalAlignment.Right);
            list.Columns.Add("Unit price", S(95), HorizontalAlignment.Right);
            list.Columns.Add("Total", S(100), HorizontalAlignment.Right);
            list.Columns.Add("Category", S(105));
            right.Controls.Add(list);
            rightCard.Controls.Add(right);
            mid.Controls.Add(rightCard, 1, 0);
            root.Controls.Add(mid);

            // Row 3: actions (left) and settings (right)
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, S(12), 0, 0), BackColor = Theme.Bg };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            FlowLayoutPanel main = Row();
            main.WrapContents = false;
            btnScan = Theme.Button(ScanButtonText(), delegate { StartScan(false); }, dpi, true);
            btnOverlay = B("Overlay  (" + OverlayKeyName + ")", delegate { ToggleOverlay(); });
            btnPreview = B("Preview", delegate { Preview(); });
            chkHover = new DarkCheckBox { Text = "Price on hover", Font = Font, Checked = settings.HoverPrices, Margin = new Padding(S(8), S(8), 0, 0) };
            new ToolTip().SetToolTip(chkHover, "The app doesn't move the mouse: " + ScanKeyName + " (or opening a known tab) gets the tab ready,\n" +
                                               "then rest the mouse on an item and its price shows.");
            chkHover.CheckedChanged += delegate
            {
                settings.HoverPrices = chkHover.Checked;
                settings.Save();
                frame = null;
                btnScan.Text = ScanButtonText();
                SetStatus(settings.HoverPrices
                    ? "Price on hover: open a stash tab (press " + ScanKeyName + " on a tab the app doesn't know), then rest the mouse on an item."
                    : "Price on hover off: " + ScanKeyName + " scans the whole tab.");
                nextDetect = DateTime.MinValue;   // get the tab on screen ready right away
                settleUntil = DateTime.Now.AddMilliseconds(1500);
                UpdateOverlay(true);
            };
            main.Controls.AddRange(new Control[] { btnScan, btnOverlay, btnPreview, chkHover });
            actions.Controls.Add(main, 0, 0);

            FlowLayoutPanel extra = Row();
            extra.Dock = DockStyle.None;
            extra.Anchor = AnchorStyles.Right;
            extra.WrapContents = false;
            nudDelay = new NumericUpDown { Minimum = 10, Maximum = 500, Width = S(60), Margin = new Padding(0, S(4), S(12), 0), Value = Math.Max(10, Math.Min(500, settings.HoverDelay)) };
            Theme.Style(nudDelay);
            nudDelay.ValueChanged += delegate { settings.HoverDelay = (int)nudDelay.Value; settings.Save(); };
            btnScanKey = B("Scan key: " + ScanKeyName, delegate { ChangeHotkey(true); });
            btnOverlayKey = B("Overlay key: " + OverlayKeyName, delegate { ChangeHotkey(false); });
            btnOverlayKey.Margin = new Padding(0);
            extra.Controls.AddRange(new Control[] { L("Hover delay (ms)"), nudDelay, btnScanKey, btnOverlayKey });
            actions.Controls.Add(extra, 1, 0);
            root.Controls.Add(actions);

            // Row 4: status
            FlowLayoutPanel r5 = Row();
            r5.Margin = new Padding(0, S(10), 0, 0);
            r5.WrapContents = false;
            progress = new ThinProgress { Width = S(160), Height = S(16), Margin = new Padding(0, S(1), S(10), 0) };
            lblStatus = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, S(1), 0, 0) };
            r5.Controls.AddRange(new Control[] { progress, lblStatus });
            root.Controls.Add(r5);
        }

        void SetStatus(string s) { lblStatus.Text = s; }

        static string TabName(string key)
        {
            return key == UnknownTab ? "Unsaved tab" : TabLibrary.NameOf(key);
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
                lblGrand.Text = "—";
                lblGrandSub.Text = "No tab scanned yet. Open a tab in the game and press " + ScanKeyName + ".";
                return;
            }
            double sum = results.Values.Sum(r => ResultStore.Total(r, t));
            lblGrand.Text = t == null ? "loading prices..." : Fmt(sum);
            string alt = t != null && t.ExPerDiv > 0 ? string.Format("≈ {0} div / {1} ex · ", Num(sum), Num(sum * t.ExPerDiv)) : "";
            DateTime oldest = results.Values.Min(r => r.ScannedAt);
            lblGrandSub.Text = string.Format("{0}{1} tabs scanned · oldest scan {2:dd.MM HH:mm}", alt, results.Count, oldest);
        }

        bool refreshingTabs;

        void RefreshTabList()
        {
            PriceTable t = table;
            string selected = viewKey;
            refreshingTabs = true;
            tabList.BeginUpdate();
            tabList.Items.Clear();
            // The tabs learned so far (each is learned on its first scan), plus an unsaved tab just scanned.
            // Results without a saved tab (left by older versions) are listed too: they count in the total.
            // Built-in layouts are listed once they were scanned; learned tabs always.
            List<string> keys = profiles.Values.Where(p => !p.BuiltIn).Select(p => p.Key).Union(results.Keys).OrderBy(k => TabName(k)).ToList();
            if (unknownResult != null) keys.Add(UnknownTab);
            foreach (string key in keys)
            {
                TabResult r = ResultFor(key);
                ListViewItem li = new ListViewItem(TabName(key)) { Tag = key };
                li.SubItems.Add(r != null && t != null ? Fmt(ResultStore.Total(r, t)) : "");
                string status = r != null ? string.Format("scanned {0:dd.MM HH:mm}", r.ScannedAt) : "not scanned";
                if (key == UnknownTab) status = "not saved";
                if (key == currentTab) status = "▶ " + status;
                li.SubItems.Add(status);
                li.UseItemStyleForSubItems = false;
                li.ForeColor = key == UnknownTab ? Theme.Muted : key == currentTab ? Theme.GoldBright : Theme.Text;
                li.SubItems[1].ForeColor = Theme.Gold;
                li.SubItems[2].ForeColor = key == currentTab ? Theme.GoldBright : Theme.Muted;
                if (key == currentTab) li.Font = new Font(tabList.Font, FontStyle.Bold);
                if (key == selected) li.Selected = true;
                tabList.Items.Add(li);
            }
            tabList.EndUpdate();
            tabList.StretchLater();
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
                li.SubItems.Add(r.Priced ? Fmt(r.Total) : "no price");
                li.SubItems.Add(r.Category);
                li.UseItemStyleForSubItems = false;
                li.ForeColor = r.Priced ? Theme.Text : Theme.Faint;
                li.SubItems[1].ForeColor = r.Unread ? Theme.Danger : r.Priced ? Theme.Text : Theme.Faint;
                li.SubItems[2].ForeColor = r.Priced ? Theme.Muted : Theme.Faint;
                li.SubItems[3].ForeColor = r.Priced ? Theme.Gold : Theme.Faint;
                li.SubItems[4].ForeColor = Theme.Faint;
                list.Items.Add(li);
            }
            list.EndUpdate();
            list.StretchLater();

            string note = null;
            if (key == null) lblView.Text = "Open a scanned tab in the game or pick a tab on the left.";
            else if (tr == null) lblView.Text = TabName(key) + " · not scanned yet (" + ScanKeyName + ")";
            else
            {
                double sum = items.Sum(i => i.TotalDiv);
                lblView.Text = string.Format("{0} · {1} · scanned {2:dd.MM HH:mm}", TabName(key), Fmt(sum), tr.ScannedAt);
                note = PriceChangeNote(tr, sum);
                if (note != null) lblView.Text += "\n" + note;
            }
            lblView.ForeColor = note != null ? Theme.GoldBright : Theme.Text;
        }

        // ---------------------------------------------------------------- lifecycle / hotkeys

        public const string Version = "1.4.0";
        readonly List<string> hotkeyProblems = new List<string>();

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
            Log.Write("---- PoE2 Stash Pricer " + Version + " started | " + Environment.OSVersion + " | screen " + Screen.PrimaryScreen.Bounds.Size + " | dpi " + DeviceDpi);
            if (!Register(HK_SCAN, ScanKey)) hotkeyProblems.Add(ScanKeyName);
            if (!Register(HK_OVERLAY, OverlayKey)) hotkeyProblems.Add(OverlayKeyName);
            Log.Write(hotkeyProblems.Count == 0 ? "hotkeys " + ScanKeyName + " / " + OverlayKeyName + " registered" : "hotkeys NOT registered: " + string.Join(", ", hotkeyProblems.ToArray()));
        }

        // ---------------------------------------------------------------- hotkeys (can be changed by the user)

        Keys ScanKey { get { return settings.ScanKey != 0 ? (Keys)settings.ScanKey : Keys.F7; } }
        Keys OverlayKey { get { return settings.OverlayKey != 0 ? (Keys)settings.OverlayKey : Keys.F8; } }
        string ScanKeyName { get { return Hotkeys.Name(ScanKey); } }
        string OverlayKeyName { get { return Hotkeys.Name(OverlayKey); } }

        bool Register(int id, Keys key)
        {
            return Native.RegisterHotKey(Handle, id, Native.MOD_NOREPEAT | Hotkeys.Modifiers(key), Hotkeys.VirtualKey(key));
        }

        void UpdateHotkeyTexts()
        {
            if (!busy) btnScan.Text = ScanButtonText();
            btnOverlay.Text = "Overlay  (" + OverlayKeyName + ")";
            btnScanKey.Text = "Scan key: " + ScanKeyName;
            btnOverlayKey.Text = "Overlay key: " + OverlayKeyName;
        }

        /// <summary>Lets the user pick another key for scanning or for the overlay.</summary>
        void ChangeHotkey(bool scan)
        {
            Keys old = scan ? ScanKey : OverlayKey, other = scan ? OverlayKey : ScanKey;
            Keys chosen;
            using (KeyCaptureForm f = new KeyCaptureForm(scan ? "Scan" : "Show / hide overlay", old))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                chosen = f.Result;
            }
            if (chosen == old) return;
            if (chosen == other)
            {
                MessageBox.Show(this, Hotkeys.Name(chosen) + " is already used for " + (scan ? "the overlay" : "scanning") + ".", Text);
                return;
            }
            int id = scan ? HK_SCAN : HK_OVERLAY;
            Native.UnregisterHotKey(Handle, id);
            if (!Register(id, chosen))
            {
                Register(id, old);
                MessageBox.Show(this, Hotkeys.Name(chosen) + " is already used by another program. Pick another key.", Text,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (scan) settings.ScanKey = (int)chosen; else settings.OverlayKey = (int)chosen;
            settings.Save();
            Log.Write((scan ? "scan" : "overlay") + " hotkey changed to " + Hotkeys.Name(chosen));
            hotkeyProblems.Remove(Hotkeys.Name(old));
            UpdateHotkeyTexts();
            RefreshAll();
            SetStatus((scan ? "Scan" : "Overlay") + " hotkey is now " + Hotkeys.Name(chosen) + ".");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LoadLeagues();
            if (profiles.Count == 0)
                SetStatus("Open a stash tab in the game and press " + ScanKeyName + ". Each tab is learned on its first scan.");
            if (hotkeyProblems.Count > 0)
            {
                string keys = string.Join(", ", hotkeyProblems.ToArray());
                SetStatus("Hotkeys not available: " + keys);
                MessageBox.Show(this,
                    "The hotkey(s) " + keys + " could not be registered: another program is already using them " +
                    "(for example another PoE tool, an overlay or a recording app).\n\n" +
                    "Pick other keys with the \"Scan key\" / \"Overlay key\" buttons, or close that program and restart PoE2 Stash Pricer. " +
                    "Until then you can use the buttons in this window instead.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Something the user asked for from the game didn't work: tell them where they are looking.</summary>
        void Problem(string text, ScanConfig cfg)
        {
            Log.Write("problem: " + text);
            SetStatus(text);
            System.Media.SystemSounds.Exclamation.Play();
            if (cfg != null) ShowMessage(text, cfg);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (scanner != null) scanner.CancelRequested = true;
            Native.UnregisterHotKey(Handle, HK_SCAN);
            Native.UnregisterHotKey(Handle, HK_OVERLAY);
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
                    case HK_SCAN: StartScan(true); break;
                    case HK_OVERLAY: ToggleOverlay(); break;
                }
                return;
            }
            base.WndProc(ref m);
        }

        void ToggleOverlay()
        {
            // A message or preview on screen: the overlay key just dismisses it.
            if (overlay.Visible && !overlayState.StartsWith("tab:")) { overlay.HideOverlay(); overlayState = "hidden"; return; }
            overlayWanted = !overlayWanted;
            UpdateOverlay(true);
            SetStatus(overlayWanted ? "Price overlay on." : "Price overlay off (" + OverlayKeyName + " to turn on).");
        }

        // ---------------------------------------------------------------- following the game

        /// <summary>
        /// Keeps track of which tab is open: when the stash picture changes (and the cursor isn't over it,
        /// where item tooltips come and go) the stash is located and recognised again. Results are never
        /// thrown away; the overlay just follows the tab on screen.
        /// </summary>
        void WatchTick(object sender, EventArgs e)
        {
            AutoRefreshPrices();
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

            // Price on hover: a tab the app knows is made ready as soon as it is on screen.
            if (settings.HoverPrices && stashVisible && tab != null && (frame == null || frame.TabKey != tab.Key || frame.Region != stashRegion))
                Arm(gameHwnd, win, full, loc, tab);

            // A paged tab (Tablets...) shows one of several pages: like an unknown tab, its last scan is not kept.
            string key = stashVisible && tab != null && !tab.Paged ? tab.Key : null;
            if (FrameOnScreen(tab, loc)) key = frame.Key;   // hover prices of an unsaved or paged tab stay shown
            bool changed = key != currentTab;
            if (changed)
            {
                currentTab = key;
                viewKey = null;   // the item list follows the game again
                RefreshTabList();
                RefreshItems();
            }
            // Another tab replaces a preview or message.
            UpdateOverlay(changed);
        }

        /// <summary>Shows the saved prices of the tab on screen, or hides the overlay when there are none.</summary>
        void UpdateOverlay(bool force)
        {
            // Messages and previews stay until the tab changes or the overlay key.
            if (!force && (overlayState == "message" || overlayState == "preview") && overlay.Visible) return;

            bool gameFront = Native.IsGameWindow(Native.GetForegroundWindow());
            TabResult tr = ResultFor(currentTab);
            bool hoverHere = settings.HoverPrices && frame != null && frame.Key == currentTab;
            bool show = overlayWanted && gameFront && stashVisible && (tr != null || hoverHere) && !stashRegion.IsEmpty;
            string state = show ? string.Format("tab:{0}:{1:O}:{2}:{3}:{4}:{5}", currentTab, tr == null ? DateTime.MinValue : tr.ScannedAt, stashRegion,
                                                 table == null ? 0 : table.LoadedAt.Ticks, settings.DisplayCurrency, hoverHere) : "hidden";
            if (!force && state == overlayState) return;
            overlayState = state;
            if (!show) { overlay.HideOverlay(); return; }

            List<PricedItem> items = ResultStore.Price(tr, table, stashRegion);
            List<OverlayLabel> labels = new List<OverlayLabel>();
            foreach (PricedItem pi in items)
                if (pi.Price != null)
                    labels.Add(new OverlayLabel { Bounds = pi.Bounds, Text = Fmt(pi.TotalDiv), Color = ValueColor(pi.TotalDiv) });
            double sum = items.Sum(i => i.TotalDiv), grand = results.Values.Sum(r => ResultStore.Total(r, table));
            string header = hoverHere
                ? string.Format("{0}: {1}  ·  Stash total: {2}  ·  rest the mouse on an item to price it  ·  {3}: hide",
                                TabName(currentTab), Fmt(sum), Fmt(grand), OverlayKeyName)
                : string.Format("{0}: {1}  ·  scanned {2:HH:mm}  ·  Stash total: {3}  ·  {4}: rescan · {5}: hide",
                                TabName(currentTab), Fmt(sum), tr.ScannedAt, Fmt(grand), ScanKeyName, OverlayKeyName);
            overlay.ShowLabels(labels, header, stashRegion, hoverHere ? null : PriceChangeNote(tr, sum));
        }

        /// <summary>
        /// "Prices updated" line when poe.ninja prices changed the tab value since it was scanned (the values
        /// shown already use the new prices; a rescan also picks up items that were moved or used).
        /// </summary>
        string PriceChangeNote(TabResult tr, double now)
        {
            PriceTable t = table;
            if (t == null || tr == null) return null;
            if (tr.PricesAtScan == DateTime.MinValue)   // scanned by an older version: no value to compare
                return t.LoadedAt > tr.ScannedAt
                    ? string.Format("↻ Prices updated at {0:HH:mm} since this scan  ·  {1}: rescan", t.LoadedAt, ScanKeyName)
                    : null;
            if (t.LoadedAt <= tr.PricesAtScan || tr.ValueAtScan <= 0) return null;
            double change = (now - tr.ValueAtScan) / tr.ValueAtScan;
            if (Math.Abs(change) < 0.01) return null;
            return string.Format("↻ Prices updated at {0:HH:mm}: tab value {1} → {2} ({3}{4:0.#}%)  ·  {5}: rescan",
                                 t.LoadedAt, Fmt(tr.ValueAtScan), Fmt(now), change > 0 ? "+" : "−", Math.Abs(change) * 100, ScanKeyName);
        }

        // ---------------------------------------------------------------- price on hover

        // Instead of the app hovering every slot, the user moves the mouse: when it rests on a slot, that one item
        // is copied (Ctrl+C, as the scan does) and priced. The tab must be "ready" first, its slots known: a tab
        // the app knows is made ready when it is opened, any other one with the scan key.

        const int RestMs = 150;        // the mouse stays this long on a slot before its item is read
        const int HoverCopyWait = 300; // the game's answer to Ctrl+C; nothing by then = an empty slot

        class HoverFrame
        {
            public string Key;             // result key: the tab's key, or UnknownTab (not recognised, or paged)
            public string TabKey;          // the recognised tab, or null
            public Rectangle Region;       // the stash area on screen
            public List<Rectangle> Slots;  // on screen; null = not a known tab, every cell counts
            public bool Exact;             // Slots are a built-in layout: every slot is in the list
            public bool Fixed;             // one slot per item type
            public double CellSize;
            public double[] FrameColor;
            public PixelBuffer Snapshot;   // the stash as it looked when made ready (or last checked)
        }

        string ScanButtonText()
        {
            return (settings.HoverPrices ? "Get tab ready  (" : "Scan  (") + ScanKeyName + ")";
        }

        /// <summary>The ready tab is still the one on screen (same place, same tab or at least the same frame colour).</summary>
        bool FrameOnScreen(TabProfile tab, StashLocator.Result loc)
        {
            if (!settings.HoverPrices || frame == null || !stashVisible || frame.Region != stashRegion) return false;
            if (tab != null) return frame.TabKey == tab.Key;
            return frame.TabKey == null && frame.FrameColor != null && loc.FrameColor != null
                   && StashLocator.ColorDistance(frame.FrameColor, loc.FrameColor) <= 0.15;
        }

        /// <summary>Makes the tab in <paramref name="full"/> (a capture of <paramref name="win"/>) ready for hover prices.</summary>
        void Arm(IntPtr game, Rectangle win, PixelBuffer full, StashLocator.Result loc, TabProfile tab)
        {
            Rectangle region = new Rectangle(win.X + loc.Region.X, win.Y + loc.Region.Y, loc.Region.Width, loc.Region.Height);
            PixelBuffer pb = full.Crop(loc.Region);
            string key = tab != null && !tab.Paged ? tab.Key : UnknownTab;
            bool sameUnknown = frame != null && frame.Key == UnknownTab && key == UnknownTab && frame.Region == region
                               && frame.TabKey == (tab != null ? tab.Key : null);
            HoverFrame f = new HoverFrame
            {
                Key = key, TabKey = tab != null ? tab.Key : null, Region = region, CellSize = Grid.CellSizeFor(region.Width),
                FrameColor = loc.FrameColor, Snapshot = pb, Exact = tab != null && tab.BuiltIn, Fixed = tab != null && !tab.Paged
            };
            if (tab != null) f.Slots = tab.SlotsIn(region.Size).Select(s => { s.Offset(region.Location); return s; }).ToList();
            frame = f;
            lastSlot = Rectangle.Empty;
            if (key == UnknownTab && !sameUnknown) unknownResult = null;   // another unsaved tab or page: start afresh
            Log.Write(string.Format("hover prices: tab ready: {0} ({1})", tab != null ? tab.Key : "not recognised",
                                    f.Slots != null ? f.Slots.Count + " slots" : "every cell"));
            NoteStash(game, region, pb, key);
            RefreshAll();
        }

        /// <summary>The scan key with price on hover on: make the tab on screen ready, whatever it is. The mouse isn't moved.</summary>
        async void ArmFromGame(bool fromHotkey)
        {
            IntPtr game = await GetGame(fromHotkey);
            if (game == IntPtr.Zero)
            {
                if (fromHotkey) Problem(ScanKeyName + ": the active window is not Path of Exile 2. Press it while in the game.", null);
                else MessageBox.Show(this, NoGame, Text);
                return;
            }
            Log.Write("scan key " + ScanKeyName + " pressed (price on hover) | foreground: " + Native.ForegroundDescription());
            Rectangle win = Native.ClientRectOnScreen(game);
            PixelBuffer full;
            using (Bitmap bmp = Grid.Capture(win)) full = new PixelBuffer(bmp);
            StashLocator.Result loc = StashLocator.Locate(full);
            double d;
            TabProfile tab = TabLibrary.Identify(full.Crop(loc.Region), loc.StashVisible ? loc.FrameColor : null, profiles.Values, out d);
            if (!loc.StashVisible && tab != null && d <= 0.15) loc.EdgesFound = 4;   // a known tab whose frame was faint
            if (!loc.StashVisible) { Problem(NoStash, BuildConfig(game)); return; }
            Arm(game, win, full, loc, tab);
            SetStatus(string.Format("{0} is ready: rest the mouse on an item to price it.", TabName(frame.Key)));
        }

        async void HoverTick(object sender, EventArgs e)
        {
            if (!settings.HoverPrices || frame == null || busy || copying) return;
            if (!stashVisible || !Native.IsGameWindow(Native.GetForegroundWindow())) { lastSlot = Rectangle.Empty; return; }
            try
            {
                // A click can move or take items, or show another page: look at the slots again once it's done.
                if (Native.IsKeyDown(0x01) || Native.IsKeyDown(0x02)) { clickHeld = true; restSince = DateTime.MaxValue; return; }
                if (clickHeld) { clickHeld = false; recheckAt = DateTime.Now.AddMilliseconds(350); }
                if (DateTime.Now >= recheckAt) { recheckAt = DateTime.MaxValue; RecheckFrame(); }
                if (recheckAt != DateTime.MaxValue || frame == null) return;   // until then, which tab is on screen is not sure

                Point p = Cursor.Position;
                if (!frame.Region.Contains(p)) { lastSlot = Rectangle.Empty; return; }
                if (restSince == DateTime.MaxValue || Math.Abs(p.X - restPos.X) > 3 || Math.Abs(p.Y - restPos.Y) > 3)
                {
                    restPos = p;
                    restSince = DateTime.Now;
                    return;
                }
                if ((DateTime.Now - restSince).TotalMilliseconds < RestMs) return;
                Rectangle slot = SlotAt(p);
                if (slot.IsEmpty || slot == lastSlot) return;
                // Keys the user holds would turn our Ctrl+C into something else.
                if (Native.IsKeyDown(Native.VK_CONTROL) || Native.IsKeyDown(0x10) || Native.IsKeyDown(0x12)) return;

                lastSlot = slot;
                HoverFrame f = frame;
                string txt;
                copying = true;
                try { txt = await RunSta(() => Scanner.CopyItemUnderCursor(HoverCopyWait)); }
                finally { copying = false; }
                if (f == frame) ApplyHover(f, slot, txt);
            }
            catch (Exception ex) { Log.Write("hover prices: " + ex.Message); }
        }

        /// <summary>The slot under a screen point: a slot of the tab, or for a tab without a layout the stash cell there.</summary>
        Rectangle SlotAt(Point p)
        {
            HoverFrame f = frame;
            if (f.Slots != null)
            {
                foreach (Rectangle s in f.Slots) if (s.Contains(p)) return s;
                if (f.Exact) return Rectangle.Empty;   // a built-in layout has every slot: this is the panel between them
            }
            double cs = f.CellSize;
            int c = (int)((p.X - f.Region.X) / cs), r = (int)((p.Y - f.Region.Y) / cs);
            if (c < 0 || r < 0 || c >= 12 || r >= 12) return Rectangle.Empty;
            return new Rectangle(f.Region.X + (int)Math.Round(c * cs), f.Region.Y + (int)Math.Round(r * cs), (int)Math.Round(cs), (int)Math.Round(cs));
        }

        static Rectangle OnScreen(SavedItem s, Rectangle region)
        {
            return new Rectangle(region.X + (int)Math.Round(s.X * region.Width), region.Y + (int)Math.Round(s.Y * region.Height),
                                 (int)Math.Round(s.W * region.Width), (int)Math.Round(s.H * region.Height));
        }

        /// <summary>Puts what was copied from a slot into the tab's result, replacing what was there before.</summary>
        void ApplyHover(HoverFrame f, Rectangle slot, string txt)
        {
            ParsedItem it = txt != null ? ItemParser.Parse(txt) : null;
            TabResult tr = ResultFor(f.Key);
            if (tr == null)
            {
                if (it == null) return;
                tr = new TabResult { Key = f.Key };
                if (f.Key == UnknownTab) unknownResult = tr;
                else results[f.Key] = tr;
            }
            Rectangle region = f.Region;
            Point mid = new Point(slot.X + slot.Width / 2, slot.Y + slot.Height / 2);
            // Whatever was read here before goes: the item may have been moved, used or replaced.
            int removed = tr.Items.RemoveAll(s =>
            {
                Rectangle b = OnScreen(s, region);
                return b.Contains(mid) || slot.Contains(new Point(b.X + b.Width / 2, b.Y + b.Height / 2));
            });
            if (it == null && removed == 0) return;

            if (it != null)
            {
                SavedItem si = new SavedItem
                {
                    Text = txt,
                    X = (slot.X - region.X) / (double)region.Width, Y = (slot.Y - region.Y) / (double)region.Height,
                    W = slot.Width / (double)region.Width, H = slot.Height / (double)region.Height
                };
                // A big item (armour, a unique) answers on every cell it covers: hovering another of its cells
                // grows it instead of adding it twice.
                SavedItem same = !f.Fixed && it.IsMultiCellCandidate
                    ? tr.Items.FirstOrDefault(s => s.Text == txt && OnScreen(s, region).IntersectsWith(Rectangle.Inflate(slot, (int)(f.CellSize * 2.5), (int)(f.CellSize * 2.5))))
                    : null;
                if (same != null)
                {
                    Rectangle u = Rectangle.Union(OnScreen(same, region), slot);
                    same.X = (u.X - region.X) / (double)region.Width; same.Y = (u.Y - region.Y) / (double)region.Height;
                    same.W = u.Width / (double)region.Width; same.H = u.Height / (double)region.Height;
                }
                else
                {
                    if (it.NeedsCount)
                    {
                        // No stack size in the text: read the number on the icon, as a scan does.
                        Rectangle local = slot;
                        local.Offset(-region.X, -region.Y);
                        int n = DigitReader.Read(f.Snapshot, local, f.CellSize);
                        if (n > 0) si.Count = n; else si.CountUnread = true;
                    }
                    tr.Items.Add(si);
                }
            }

            PriceTable t = table;
            tr.ScannedAt = DateTime.Now;
            tr.ValueAtScan = ResultStore.Total(tr, t);
            tr.PricesAtScan = t != null ? t.LoadedAt : DateTime.MinValue;
            if (f.Key != UnknownTab) ResultStore.Save(results);
            PriceInfo price = it != null && t != null ? t.Lookup(it) : null;
            Log.Write(it == null ? "hover prices: " + TabName(f.Key) + ": slot now empty"
                                 : string.Format("hover prices: {0}: {1} x{2} = {3}", TabName(f.Key), it.DisplayName, it.Stack,
                                                 price != null ? Fmt(price.Div * it.Stack) : "no price"));
            RefreshAll();
        }

        /// <summary>
        /// After a click. Another tab opened: that one is made ready (the prices of the one before stay, its items
        /// are still in the stash). The same tab: items whose slot looks different now were moved, taken or are on
        /// another page; their prices go until they are hovered again.
        /// </summary>
        void RecheckFrame()
        {
            HoverFrame f = frame;
            if (f == null) return;
            Rectangle win = Native.ClientRectOnScreen(gameHwnd);
            if (win.Width <= 0 || win.Height <= 0) return;
            PixelBuffer full;
            using (Bitmap bmp = Grid.Capture(win)) full = new PixelBuffer(bmp);
            StashLocator.Result loc = StashLocator.Locate(full);
            double d;
            TabProfile tab = TabLibrary.Identify(full.Crop(loc.Region), loc.StashVisible ? loc.FrameColor : null, profiles.Values, out d);
            if (!loc.StashVisible && tab != null && d <= 0.15) loc.EdgesFound = 4;
            if (!loc.StashVisible) { recheckTries = 0; return; }   // stash closed: the watcher takes it from here
            if (tab != null && tab.Key != f.TabKey)
            {
                recheckTries = 0;
                Arm(gameHwnd, win, full, loc, tab);
                return;
            }
            if (tab == null && f.TabKey != null)
            {
                // Not recognised: an item tooltip may hide part of the tab (or it is still fading in). Only another
                // frame colour says for sure that another tab is open; otherwise it's still this one, and its
                // slots can't be compared with a tooltip over them, so the prices stay.
                bool otherColour = loc.FrameColor != null && f.FrameColor != null && StashLocator.ColorDistance(loc.FrameColor, f.FrameColor) > 0.15;
                if (!otherColour)
                {
                    if (++recheckTries < 4) recheckAt = DateTime.Now.AddMilliseconds(300);
                    else recheckTries = 0;
                    return;
                }
                recheckTries = 0;
                frame = null;
                currentTab = null;
                Log.Write("hover prices: the tab on screen is not one the app knows; " + ScanKeyName + " gets it ready");
                SetStatus("This tab isn't one the app knows: press " + ScanKeyName + " to get it ready for hover prices.");
                UpdateOverlay(true);
                return;
            }
            recheckTries = 0;
            Rectangle local = f.Region;
            local.Offset(-win.X, -win.Y);
            PixelBuffer now = full.Crop(local);
            TabResult tr = ResultFor(f.Key);
            int dropped = 0;
            if (tr != null)
                dropped = tr.Items.RemoveAll(s =>
                {
                    Rectangle b = OnScreen(s, f.Region);
                    b.Offset(-f.Region.X, -f.Region.Y);
                    return Scanner.MeanDifference(f.Snapshot, now, b) > 20;
                });
            f.Snapshot = now;
            lastSlot = Rectangle.Empty;   // the slot under the mouse may have changed too
            if (dropped == 0) return;
            Log.Write("hover prices: " + dropped + " items changed after a click, their prices were removed");
            tr.ScannedAt = DateTime.Now;
            tr.ValueAtScan = ResultStore.Total(tr, table);
            if (f.Key != UnknownTab) ResultStore.Save(results);
            RefreshAll();
        }

        // ---------------------------------------------------------------- prices

        // Prices are fetched at start and then every 15 minutes while the app is open. A failed or
        // rate-limited fetch is retried sooner (1, 2, 5, 10 minutes), or when poe.ninja's Retry-After says.
        static readonly TimeSpan PriceInterval = TimeSpan.FromMinutes(15);
        static readonly int[] RetryMinutes = { 1, 2, 5, 10, 15 };
        DateTime nextPriceLoad = DateTime.MaxValue;
        DateTime nextLeagueLoad = DateTime.MaxValue;
        int priceFailures, leagueFailures;

        /// <summary>Called by the watch timer: starts the fetches that are due.</summary>
        void AutoRefreshPrices()
        {
            if (loadingPrices || busy) return;
            DateTime now = DateTime.Now;
            if (now >= nextLeagueLoad) LoadLeagues();
            else if (now >= nextPriceLoad) LoadPrices(true);
        }

        DateTime RetryAt(int failures, TimeSpan retryAfter)
        {
            TimeSpan wait = TimeSpan.FromMinutes(RetryMinutes[Math.Min(failures, RetryMinutes.Length) - 1]);
            if (retryAfter > wait) wait = retryAfter;
            return DateTime.Now + wait;
        }

        async void LoadLeagues()
        {
            nextLeagueLoad = DateTime.MaxValue;
            SetStatus("Loading leagues...");
            List<string> leagues;
            try
            {
                leagues = await Task.Run(() => PriceService.GetLeagues());
                leagueFailures = 0;
            }
            catch (Exception ex)
            {
                leagueFailures++;
                nextLeagueLoad = RetryAt(leagueFailures, TimeSpan.Zero);
                SetStatus(string.Format("Could not reach poe.ninja: {0} Retrying at {1:HH:mm}.", ex.Message, nextLeagueLoad));
                Log.Write("leagues failed: " + ex.Message);
                leagues = new List<string>();
                if (!string.IsNullOrEmpty(settings.League)) leagues.Add(settings.League);
            }
            int idx = settings.League != null ? leagues.IndexOf(settings.League) : -1;
            if (idx < 0 && leagues.Count > 0) { idx = 0; settings.League = leagues[0]; settings.Save(); }
            if (leagues.Count > 0)
            {
                cbLeague.Items.Clear();
                foreach (string l in leagues) cbLeague.Items.Add(l);
                if (idx >= 0) cbLeague.SelectedIndex = idx;   // triggers LoadPrices via the handler when needed
            }
            if (table == null && settings.League != null) LoadPrices();   // no-op if the league change above started it
        }

        void LoadPrices() { LoadPrices(false); }

        async void LoadPrices(bool auto)
        {
            string league = settings.League;
            if (loadingPrices || string.IsNullOrEmpty(league)) return;
            loadingPrices = true;
            nextPriceLoad = DateTime.MaxValue;
            btnRefresh.Enabled = false;
            if (!auto) priceFailures = 0;
            try
            {
                // An automatic update runs quietly; only its result is shown.
                Action<string> report = auto ? null : (Action<string>)(s => BeginInvoke((Action)(() => SetStatus(s))));
                PriceTable old = table;
                PriceTable t = await Task.Run(() => PriceService.Load(league, report));
                t.FillFailedFrom(old);
                string when;
                if (t.Failed.Count == 0)
                {
                    priceFailures = 0;
                    nextPriceLoad = DateTime.Now + PriceInterval;
                    when = string.Format("next update {0:HH:mm}", nextPriceLoad);
                }
                else
                {
                    priceFailures++;
                    nextPriceLoad = RetryAt(priceFailures, t.RetryAfter);
                    when = string.Format("{0} {1:HH:mm}", t.RateLimited ? "poe.ninja asked to slow down, retrying at" : "retrying at", nextPriceLoad);
                    Log.Write("prices: failed " + string.Join(", ", t.Failed.ToArray()) + (t.RateLimited ? " (rate limited)" : "") + ", retry " + nextPriceLoad.ToString("HH:mm:ss"));
                }
                if (t.Count == 0)
                {
                    SetStatus(old != null
                        ? "Could not update prices, keeping the ones from " + old.LoadedAt.ToString("HH:mm") + " · " + when
                        : "Could not load prices. Check your internet connection · " + when);
                    return;
                }
                table = t;
                string msg = string.Format("{0} prices: {1} items · 1 div = {2:0} ex · updated {3:HH:mm} · {4}", league, t.Count, t.ExPerDiv, t.LoadedAt, when);
                if (t.Failed.Count > 0 && !t.RateLimited) msg += " · failed: " + string.Join(", ", t.Failed.ToArray());
                if (!auto || !busy) SetStatus(msg);
                RefreshAll();
            }
            catch (Exception ex)
            {
                priceFailures++;
                nextPriceLoad = RetryAt(priceFailures, TimeSpan.Zero);
                SetStatus(string.Format("Price loading error: {0} · retrying at {1:HH:mm}", ex.Message, nextPriceLoad));
            }
            finally
            {
                loadingPrices = false;
                btnRefresh.Enabled = true;
                // The league was changed while loading: load that one now.
                if (settings.League != league) nextPriceLoad = DateTime.Now;
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
                CopyTimeout = Math.Max(100, settings.CopyTimeout),
                HotkeyVk = (int)Hotkeys.VirtualKey(ScanKey)
            };
        }

        const string NoGame = "Path of Exile 2 window not found. Use the hotkeys while in the game.";
        const string NoStash = "Stash not visible. Open the stash, keep the mouse off it and try again.";
        bool fullscreenWarned;

        /// <summary>
        /// Exclusive Fullscreen: scanning may still work, but nothing can be drawn over the game, so the
        /// instructions and prices would stay invisible. Say so once per session.
        /// </summary>
        void CheckFullscreen()
        {
            int state;
            bool exclusive = Native.ExclusiveFullscreen(out state);
            Log.Write("display state " + state + (exclusive ? " = exclusive fullscreen" : ""));
            if (!exclusive || fullscreenWarned) return;
            fullscreenWarned = true;
            Problem("The game runs in Fullscreen mode: nothing can be shown over it. Set Display Mode to Windowed Fullscreen.", null);
            MessageBox.Show(this,
                "The game runs in exclusive Fullscreen mode. In this mode Windows doesn't let any other program draw over the game, " +
                "so the instructions and prices of PoE2 Stash Pricer stay invisible (saving and scanning may still work; " +
                "results appear in this window).\n\nIn the game open Options > Graphics and set Display Mode to \"Windowed Fullscreen\".",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// The capture of the game is black: it runs in exclusive fullscreen, where Windows can neither capture
        /// it nor draw the overlay over it. A message box is the one thing that still gets through.
        /// </summary>
        void BlackScreen(ScanConfig cfg)
        {
            Problem("The game picture is black: set Options > Graphics > Display Mode to Windowed Fullscreen.", null);
            MessageBox.Show(this,
                "PoE2 Stash Pricer can't see the game: the screenshot comes out black.\n\n" +
                "This happens when the game runs in exclusive Fullscreen. In the game open Options > Graphics and set " +
                "Display Mode to \"Windowed Fullscreen\" (or Windowed), then try again.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

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

        // ---------------------------------------------------------------- saved tabs

        /// <summary>
        /// The first scan of a tab teaches it: if the scanned tab isn't one of the saved ones and looks like a
        /// special (fixed-slot) tab, it is saved under a name guessed from its items. Returns that name, or null.
        /// </summary>
        /// <summary>Share of item types in common between a scan and a saved result (of the smaller of the two).</summary>
        static double SharedItems(ScanResult res, TabResult saved)
        {
            if (saved == null) return 0;
            HashSet<string> now = new HashSet<string>(res.Items.Where(i => i.Item != null && i.Item.Name != null).Select(i => i.Item.Name));
            HashSet<string> before = new HashSet<string>();
            foreach (SavedItem s in saved.Items)
            {
                ParsedItem it = ItemParser.Parse(s.Text);
                if (it != null && it.Name != null) before.Add(it.Name);
            }
            int smaller = Math.Min(now.Count, before.Count);
            if (smaller == 0) return 0;
            return (double)now.Count(n => before.Contains(n)) / smaller;
        }

        string LearnIfNew(ScanResult res, ScanConfig cfg)
        {
            if (res.Tab != null || res.Aborted || res.Snapshot == null) return null;
            string why;
            if (res.Candidate != null)
            {
                // The picture is similar to a saved tab but not the same. The items decide: the same tab still
                // holds mostly the same items; a look-alike sub-tab (Kalguuran Runes next to Runes) holds others.
                double shared = SharedItems(res, ResultFor(res.Candidate.Key));
                if (shared >= 0.5)
                {
                    Scanner.MergeNearDuplicates(res, double.MaxValue);   // a special tab: one slot per item type
                    res.Tab = res.Candidate;
                    TabLibrary.Refresh(res.Tab, res.Snapshot, res.FrameColor);
                    TabLibrary.AddSlots(res.Tab, res.Items.Select(i => new Rectangle(i.Bounds.X - cfg.Region.X, i.Bounds.Y - cfg.Region.Y, i.Bounds.Width, i.Bounds.Height)), cfg.Region.Size);
                    TabLibrary.Save(res.Tab);
                    Log.Write(string.Format("tab '{0}' recognised by its items ({1:0%} the same), its picture was updated", res.Tab.Name, shared));
                    return null;
                }
                Log.Write(string.Format("looked like '{0}' but holds other items ({1:0%} the same): a different tab", res.Candidate.Name, shared));
            }
            string guess = TabLibrary.GuessSpecialTab(res, cfg.Region, out why);
            if (guess == null)
            {
                Log.Write("tab not learned: " + why);
                return null;
            }

            // It's a special tab: every item type has one slot, so repeated reads of one item (the cells of a
            // big slot) are one item. The first scan didn't know that yet.
            Scanner.MergeNearDuplicates(res, double.MaxValue);

            string key, name;
            TabLibrary.NewKey(guess, profiles.Keys, out key, out name);
            List<Rectangle> itemSlots = res.Items.Select(i =>
            {
                Rectangle b = i.Bounds;
                b.Offset(-cfg.Region.X, -cfg.Region.Y);
                return b;
            }).ToList();
            TabProfile learned = TabLibrary.Learn(key, name, res.Snapshot, res.FrameColor, cfg, itemSlots);
            learned.Kind = guess;
            TabLibrary.Save(learned);
            profiles[key] = learned;
            res.Tab = learned;
            Log.Write("tab learned: " + key + " '" + name + "', " + learned.Slots.Count + " slots");
            return name;
        }

        /// <summary>The tab name is guessed from its items; this lets the user pick a better one.</summary>
        void RenameSelectedTab()
        {
            string key = SelectedTabKey();
            TabProfile p;
            if (key == null || !profiles.TryGetValue(key, out p)) { SetStatus("Pick a tab in the list first."); return; }
            if (p.BuiltIn) { SetStatus("'" + p.Name + "' is a built-in tab: its name comes with the app."); return; }
            string name = Microsoft.VisualBasic.Interaction.InputBox("New name for this tab:", Text, p.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            TabLibrary.Rename(p, name.Trim());
            RefreshAll();
        }

        /// <summary>Back to a fresh install: saved tabs, scan results and learned digits are removed.</summary>
        void DeleteAll()
        {
            if (busy) return;
            if (MessageBox.Show(this,
                    "Delete all saved tabs, scan results and learned digits?\n\nThe app starts over as if freshly installed; tabs are learned again on their first scan (" + ScanKeyName + "). Your league and currency choices are kept.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                TabLibrary.DeleteAll();
                Log.Write("everything deleted (tabs, results, digits)");
                ResultStore.DeleteAll();
                DigitReader.DeleteAll();
            }
            catch (Exception ex)
            {
                SetStatus("Could not delete everything: " + ex.Message);
            }
            profiles = TabLibrary.LoadAll();   // the built-in layouts stay
            results.Clear();
            unknownResult = null;
            currentTab = null;
            viewKey = null;
            watcher.Clear();
            overlay.ClearContent();
            overlayState = "hidden";
            RefreshAll();
            SetStatus("Everything deleted. Tabs are learned again on their first scan (" + ScanKeyName + ").");
        }

        void DeleteSelectedTab()
        {
            string key = SelectedTabKey();
            if (key == null || (!profiles.ContainsKey(key) && !results.ContainsKey(key))) return;
            bool builtIn = profiles.ContainsKey(key) && profiles[key].BuiltIn;
            string question = builtIn ? "'" + TabLibrary.NameOf(key) + "': delete its last scan?" : "'" + TabLibrary.NameOf(key) + "': delete the saved tab and its last scan?";
            if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Log.Write("tab deleted: " + key);
            if (!builtIn)   // a built-in layout stays; only its scan goes
            {
                TabLibrary.Delete(key);
                profiles.Remove(key);
            }
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
            CheckFullscreen();
            busy = true;
            try
            {
                overlay.HideOverlay();
                ScanConfig cfg = BuildConfig(game);
                List<TabProfile> known = profiles.Values.ToList();
                ScanPlan plan = await RunSta(() => Scanner.Prepare(cfg, known));
                if (plan.BlackScreen) { BlackScreen(cfg); return; }
                if (!plan.StashVisible) { Problem(NoStash, cfg); return; }
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
                string tab = plan.Tab != null ? "Tab: " + TabLibrary.NameOf(plan.Tab.Key) : "New tab (learned on its first " + ScanKeyName + " scan)";
                overlayState = "preview";
                overlay.ShowLabels(labels, string.Format("Preview · {0} · {1} item positions will be scanned.  {2}: hide", tab, full, OverlayKeyName), cfg.Region);
                SetStatus(string.Format("Preview: {0}, {1} positions. If items are missed, save the tab again.", tab, full));
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
            if (settings.HoverPrices) { ArmFromGame(fromHotkey); return; }
            PriceTable t = table;
            if (t == null) { SetStatus("Prices are not loaded yet, please wait a moment."); return; }
            IntPtr game = await GetGame(fromHotkey);
            if (game == IntPtr.Zero)
            {
                if (fromHotkey) Problem(ScanKeyName + ": the active window is not Path of Exile 2. Press it while in the game.", null);
                else MessageBox.Show(this, NoGame, Text);
                return;
            }
            if ((DateTime.Now - t.LoadedAt).TotalMinutes > 60) LoadPrices();
            Log.Write("scan key " + ScanKeyName + " pressed | foreground: " + Native.ForegroundDescription());
            CheckFullscreen();

            overlay.HideOverlay();
            overlayState = "hidden";
            ScanConfig cfg = BuildConfig(game);
            List<TabProfile> known = profiles.Values.ToList();
            busy = true;
            scanner = new Scanner(cfg, game);
            btnScan.Text = "Stop (" + ScanKeyName + "/Esc)";
            progress.Value = 0;
            SetStatus("Scanning... (Esc to stop, do not touch the mouse)");
            Scanner sc = scanner;
            try
            {
                ScanResult res = await RunSta(() => sc.Run(known, t.Lookup, (done, total) => BeginInvoke((Action)(() =>
                {
                    progress.Maximum = Math.Max(1, total);
                    progress.Value = Math.Min(done, progress.Maximum);
                }))));
                if (res.BlackScreen) { BlackScreen(cfg); return; }
                if (res.StashNotFound) { Problem(NoStash, cfg); return; }

                string learnedName = LearnIfNew(res, cfg);
                string key = res.Tab != null && !res.Tab.Paged ? res.Tab.Key : UnknownTab;
                Log.Write(string.Format("scan done: tab {0}, {1} positions tried, {2} items read ({3} on a second try of {4}), {5} items, aborted={6}",
                                        key, res.CellsTried, res.CellsCopied, res.CellsRecovered, res.CellsRetried, res.Items.Count, res.Aborted));
                if (res.Timing != null) Log.Write("  timing: " + res.Timing);
                // Only when the tab was recognised for sure: slots of a look-alike tab must not get mixed in.
                if (learnedName == null && res.Tab != null && !res.Tab.BuiltIn && !res.Aborted && res.TabDifference <= TabLibrary.SureMatch)
                {
                    // Items in slots that were empty when the tab was learned: remember those slots too.
                    Rectangle region = cfg.Region;
                    List<Rectangle> found = res.Items.Where(i => i.Bounds.Width <= cfg.CellSize * 2.3 && i.Bounds.Height <= cfg.CellSize * 2.3)   // at most a 2x2 slot
                                                     .Select(i => new Rectangle(i.Bounds.X - region.X, i.Bounds.Y - region.Y, i.Bounds.Width, i.Bounds.Height)).ToList();
                    int added = TabLibrary.AddSlots(res.Tab, found, region.Size);
                    if (added > 0)
                    {
                        TabLibrary.Save(res.Tab);
                        Log.Write("tab " + key + ": " + added + " new slots learned, " + res.Tab.Slots.Count + " in all");
                    }
                }
                TabResult tr = ResultStore.FromScan(key, res, cfg.Region);
                tr.ValueAtScan = ResultStore.Total(tr, t);
                tr.PricesAtScan = t.LoadedAt;
                if (res.Aborted && ResultFor(key) != null)
                {
                    SetStatus("Scan stopped; the previous result of this tab was kept.");
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
                    string status = string.Format("Scan done ({0}): {1} positions tried, {2} items read.", TabName(key), res.CellsTried, res.CellsCopied);
                    if (res.Aborted) status = "Scan stopped (partial result). " + status;
                    if (res.CellsTried > 0 && res.CellsCopied == 0)
                        status = "No item text could be copied from the game! If the game runs as administrator, run this app as administrator too, or increase the hover delay.";
                    else if (res.CellsTried == 0)
                        status = "No items found in this tab.";
                    if (learnedName != null) status += string.Format(" New tab learned as '{0}' (named after its items; use Rename to change it).", learnedName);
                    if (key == UnknownTab) status += " This looks like a normal tab, so it is not saved or added to the total stash value.";
                    int unread = res.Items.Count(i => i.CountUnread);
                    if (unread > 0)
                        status += string.Format(" The count of {0} items could not be read from the screen (shown with \"?\", counted as 1). Digits are learned while scanning (known: {1}); scan other tabs, then rescan this one.",
                                                unread, DigitReader.Known == "" ? "none" : DigitReader.Known);
                    SetStatus(status);
                }
            }
            catch (Exception ex)
            {
                Log.Write("scan error: " + ex);
                Problem("Scan error: " + ex.Message, null);
            }
            finally
            {
                busy = false;
                scanner = null;
                btnScan.Text = ScanButtonText();
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
