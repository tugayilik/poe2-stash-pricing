using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PoeStashPricer
{
    public class OverlayLabel
    {
        public Rectangle Bounds;   // screen coordinates
        public string Text;
        public Color Color;
        public bool Outline;       // draw only a frame (used by preview)
    }

    /// <summary>Click-through, always-on-top window drawn over the game to show prices on the stash.</summary>
    public class OverlayForm : Form
    {
        static readonly Color Key = Color.FromArgb(255, 0, 255);
        readonly Rectangle virt;
        List<OverlayLabel> labels = new List<OverlayLabel>();
        string header;
        Rectangle headerAnchor;

        public OverlayForm()
        {
            virt = SystemInformation.VirtualScreen;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virt;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Key;
            TransparencyKey = Key;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Our own screenshots (scan, tab-change check) must see the game, not these labels.
            ExcludedFromCapture = Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
        }

        public bool ExcludedFromCapture { get; private set; }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000;  // LAYERED | TRANSPARENT | TOOLWINDOW | NOACTIVATE
                return cp;
            }
        }

        public void ShowLabels(List<OverlayLabel> items, string headerText, Rectangle region)
        {
            labels = items;
            header = headerText;
            headerAnchor = region;
            Bounds = virt;
            if (!Visible) Show();
            TopMost = true;
            Invalidate();
        }

        public void HideOverlay()
        {
            if (Visible) Hide();
        }

        /// <summary>Hides and forgets the labels, so F8 can't bring stale prices back.</summary>
        public void ClearContent()
        {
            labels = new List<OverlayLabel>();
            header = null;
            HideOverlay();
        }

        public bool HasContent { get { return labels.Count > 0 || header != null; } }

        Rectangle ToClient(Rectangle r)
        {
            return new Rectangle(r.X - virt.X, r.Y - virt.Y, r.Width, r.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // Anti-aliasing would blend into the transparency key and leave pink fringes.
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            using (Font f = new Font("Segoe UI", 8f, FontStyle.Bold))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(18, 18, 22)))
            {
                foreach (OverlayLabel ol in labels)
                {
                    OverlayLabel l = new OverlayLabel { Text = ol.Text, Color = ol.Color, Outline = ol.Outline, Bounds = ToClient(ol.Bounds) };
                    using (Pen pen = new Pen(l.Color, l.Outline ? 1 : 2))
                    {
                        if (l.Outline) { g.DrawRectangle(pen, l.Bounds.X + 1, l.Bounds.Y + 1, l.Bounds.Width - 3, l.Bounds.Height - 3); }
                    }
                    if (string.IsNullOrEmpty(l.Text)) continue;
                    Size sz = TextRenderer.MeasureText(g, l.Text, f, Size.Empty, TextFormatFlags.NoPadding);
                    int w = sz.Width + 4, h = sz.Height + 2;
                    int x = l.Bounds.X + (l.Bounds.Width - w) / 2;
                    int y = l.Outline ? l.Bounds.Y + (l.Bounds.Height - h) / 2 : l.Bounds.Bottom - h - 1;
                    g.FillRectangle(bg, x, y, w, h);
                    TextRenderer.DrawText(g, l.Text, f, new Point(x + 2, y + 1), l.Color, TextFormatFlags.NoPadding);
                }
            }

            if (header != null)
            {
                using (Font f = new Font("Segoe UI", 11f, FontStyle.Bold))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(18, 18, 22)))
                using (Pen border = new Pen(Color.FromArgb(200, 160, 60), 2))
                {
                    Size sz = TextRenderer.MeasureText(g, header, f);
                    Rectangle anchor = ToClient(headerAnchor);
                    Rectangle box = new Rectangle(anchor.X, anchor.Bottom + 6, sz.Width + 16, sz.Height + 10);
                    if (box.Bottom > ClientSize.Height) box.Y = anchor.Top - box.Height - 6;
                    g.FillRectangle(bg, box);
                    g.DrawRectangle(border, box);
                    TextRenderer.DrawText(g, header, f, new Point(box.X + 8, box.Y + 5), Color.Gold);
                }
            }
        }
    }
}
