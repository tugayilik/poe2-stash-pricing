using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PoeStashPricer
{
    /// <summary>Dark theme with PoE-style gold accents, plus the few custom-drawn controls it needs.</summary>
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(14, 15, 19);
        public static readonly Color Surface = Color.FromArgb(22, 24, 30);
        public static readonly Color Surface2 = Color.FromArgb(31, 34, 42);
        public static readonly Color Hover = Color.FromArgb(42, 46, 57);
        public static readonly Color Border = Color.FromArgb(44, 48, 58);
        public static readonly Color Text = Color.FromArgb(232, 228, 218);
        public static readonly Color Muted = Color.FromArgb(138, 143, 154);
        public static readonly Color Faint = Color.FromArgb(96, 100, 110);
        public static readonly Color Gold = Color.FromArgb(214, 176, 104);
        public static readonly Color GoldBright = Color.FromArgb(240, 204, 128);
        public static readonly Color GoldDark = Color.FromArgb(150, 116, 58);
        public static readonly Color Selection = Color.FromArgb(58, 49, 32);
        public static readonly Color Danger = Color.FromArgb(226, 110, 92);

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        /// <summary>Dark title bar (Windows 10 20H1+) coloured like the window (Windows 11).</summary>
        public static void DarkTitleBar(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
                int caption = ColorTranslator.ToWin32(Bg), border = ColorTranslator.ToWin32(Border);
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(f.Handle, 34, ref border, 4);
            }
            catch { }
        }

        /// <summary>Dark scroll bars for lists.</summary>
        public static void DarkScrollBars(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }

        public static Button Button(string text, EventHandler click, float dpi, bool primary = false)
        {
            Button b = new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                BackColor = primary ? Gold : Surface2,
                ForeColor = primary ? Bg : Text,
                Padding = new Padding((int)(10 * dpi), (int)(4 * dpi), (int)(10 * dpi), (int)(4 * dpi)),
                Margin = new Padding(0, 0, (int)(6 * dpi), 0)
            };
            b.FlatAppearance.BorderColor = primary ? Gold : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? GoldBright : Hover;
            b.FlatAppearance.MouseDownBackColor = primary ? GoldDark : Border;
            if (primary) b.Font = new Font(b.Font, FontStyle.Bold);
            b.EnabledChanged += delegate { b.ForeColor = b.Enabled ? (primary ? Bg : Text) : Faint; };
            if (click != null) b.Click += click;
            return b;
        }

        public static void Style(ComboBox cb)
        {
            cb.FlatStyle = FlatStyle.Flat;
            cb.BackColor = Surface2;
            cb.ForeColor = Text;
            // Drawn by hand: the system draws the chosen entry in blue whenever the box has the focus.
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.DrawItem += (s, e) =>
            {
                bool list = (e.State & DrawItemState.ComboBoxEdit) == 0;
                bool hot = list && (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush br = new SolidBrush(hot ? Selection : Surface2)) e.Graphics.FillRectangle(br, e.Bounds);
                if (e.Index < 0) return;
                TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, Rectangle.Inflate(e.Bounds, -3, 0),
                                      hot ? GoldBright : Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }

        public static void Style(NumericUpDown n)
        {
            n.BorderStyle = BorderStyle.FixedSingle;
            n.BackColor = Surface2;
            n.ForeColor = Text;
        }

        /// <summary>Small upper-case section caption.</summary>
        public static Label Caption(string text, float dpi)
        {
            return new Label
            {
                Text = text.ToUpperInvariant(),
                AutoSize = true,
                ForeColor = Muted,
                Font = new Font("Segoe UI Semibold", 8f),
                Margin = new Padding(0, 0, 0, (int)(6 * dpi))
            };
        }
    }

    /// <summary>A panel with a 1px border; the hero variant has a gold accent line and a warm gradient.</summary>
    public class Card : Panel
    {
        public bool Hero;

        public Card()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle r = ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            if (Hero)
            {
                using (LinearGradientBrush br = new LinearGradientBrush(r, Color.FromArgb(40, 33, 20), Theme.Surface, LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(br, r);
            }
            else
            {
                using (SolidBrush br = new SolidBrush(BackColor)) e.Graphics.FillRectangle(br, r);
            }
            using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            if (Hero)
            {
                using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, r.Width, 3), Theme.Gold, Color.FromArgb(0, Theme.Gold), LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(br, 0, 0, r.Width, 2);
            }
        }
    }

    /// <summary>A thin gold progress bar (the Maximum/Value API of ProgressBar).</summary>
    public class ThinProgress : Control
    {
        int max = 100, val;
        public int Maximum { get { return max; } set { max = Math.Max(1, value); Invalidate(); } }
        public int Value { get { return val; } set { val = Math.Max(0, Math.Min(max, value)); Invalidate(); } }

        public ThinProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg);
            int h = Math.Max(3, Height / 3), y = (Height - h) / 2;
            using (SolidBrush br = new SolidBrush(Theme.Surface2)) e.Graphics.FillRectangle(br, 0, y, Width, h);
            int w = (int)((long)Width * val / max);
            if (w > 0)
                using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, y, Math.Max(1, w), h), Theme.GoldDark, Theme.GoldBright, LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(br, 0, y, w, h);
        }
    }

    /// <summary>
    /// Details ListView drawn in the theme: dark header, roomy rows, gold selection. One column stretches so the
    /// header never shows the white system area to the right of the last column.
    /// </summary>
    public class DarkListView : ListView
    {
        public int StretchColumn;

        public DarkListView(float dpi)
        {
            DoubleBuffered = true;
            OwnerDraw = true;
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            // Row height comes from the small image list.
            SmallImageList = new ImageList { ImageSize = new Size(1, (int)(28 * dpi)) };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkScrollBars(this);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Stretch();
        }

        /// <summary>After the rows changed: the vertical scroll bar appears only after this message is handled.</summary>
        public void StretchLater()
        {
            Stretch();
            if (IsHandleCreated) BeginInvoke((Action)Stretch);
        }

        public void Stretch()
        {
            if (Columns.Count == 0 || StretchColumn >= Columns.Count) return;
            int others = 0;
            for (int i = 0; i < Columns.Count; i++) if (i != StretchColumn) others += Columns[i].Width;
            int w = ClientSize.Width - others;
            // Rows that don't fit bring a vertical scroll bar, which may not be there yet: leave room for it.
            bool vscroll = (GetWindowLong(Handle, -16) & 0x00200000) != 0;   // GWL_STYLE, WS_VSCROLL
            if (!vscroll && Items.Count > 0 && Items[0].Bounds.Height * Items.Count > ClientSize.Height - HeaderBottom())
                w -= SystemInformation.VerticalScrollBarWidth;
            if (w > 40 && Columns[StretchColumn].Width != w) Columns[StretchColumn].Width = w;
            if (IsHandleCreated) ShowScrollBar(Handle, 0, false);   // SB_HORZ: the list keeps it after the columns shrank
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush br = new SolidBrush(Theme.Surface2)) e.Graphics.FillRectangle(br, e.Bounds);
            using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            Rectangle r = Rectangle.Inflate(e.Bounds, -8, 0);
            using (Font f = new Font("Segoe UI Semibold", 8f))
                TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), f, r, Theme.Muted, Flags(e.Header.TextAlign));
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e) { }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            Color back = e.Item.Selected ? Theme.Selection : (e.ItemIndex % 2 == 1 ? Color.FromArgb(26, 28, 35) : Theme.Surface);
            using (SolidBrush br = new SolidBrush(back)) e.Graphics.FillRectangle(br, e.Bounds);
            if (e.Item.Selected && e.ColumnIndex == 0)
                using (SolidBrush br = new SolidBrush(Theme.Gold)) e.Graphics.FillRectangle(br, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);
            Color fore = e.Item.UseItemStyleForSubItems || e.ColumnIndex == 0 ? e.Item.ForeColor : e.SubItem.ForeColor;
            Font font = e.Item.UseItemStyleForSubItems || e.ColumnIndex == 0 ? e.Item.Font : e.SubItem.Font;
            Rectangle r = Rectangle.Inflate(e.Bounds, -8, 0);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, font, r, fore, Flags(Columns[e.ColumnIndex].TextAlign) | TextFormatFlags.EndEllipsis);
        }

        // Below the last row the list draws column lines of its own: paint that area over.
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x000F) return;   // WM_PAINT
            int top = Items.Count > 0 ? Items[Items.Count - 1].Bounds.Bottom : HeaderBottom();
            if (top >= ClientSize.Height) return;
            using (Graphics g = CreateGraphics())
            using (SolidBrush br = new SolidBrush(BackColor))
                g.FillRectangle(br, 0, Math.Max(0, top), ClientSize.Width, ClientSize.Height - Math.Max(0, top));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowScrollBar(IntPtr h, int bar, bool show);
        struct RECT { public int Left, Top, Right, Bottom; }

        int HeaderBottom()
        {
            IntPtr header = SendMessage(Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);   // LVM_GETHEADER
            RECT r;
            if (header == IntPtr.Zero || !GetWindowRect(header, out r)) return 0;
            return PointToClient(new Point(r.Left, r.Bottom)).Y;
        }

        // Owner-drawn rows lose their first column when the mouse moves over them; repaint the row.
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ListViewItem it = GetItemAt(e.X, e.Y);
            if (it != null) Invalidate(it.Bounds);
        }

        static TextFormatFlags Flags(HorizontalAlignment a)
        {
            TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (a == HorizontalAlignment.Right) f |= TextFormatFlags.Right;
            else if (a == HorizontalAlignment.Center) f |= TextFormatFlags.HorizontalCenter;
            return f;
        }
    }
}
