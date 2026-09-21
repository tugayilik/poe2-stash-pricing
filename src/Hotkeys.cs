using System;
using System.Drawing;
using System.Windows.Forms;

namespace PoeStashPricer
{
    /// <summary>
    /// Global hotkeys as a Keys value (key code plus Ctrl/Alt/Shift), e.g. Keys.F7 or Keys.Control | Keys.S.
    /// </summary>
    public static class Hotkeys
    {
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4;

        public static string Name(Keys k)
        {
            string s = "";
            if ((k & Keys.Control) != 0) s += "Ctrl+";
            if ((k & Keys.Alt) != 0) s += "Alt+";
            if ((k & Keys.Shift) != 0) s += "Shift+";
            Keys code = k & Keys.KeyCode;
            if (code >= Keys.D0 && code <= Keys.D9) return s + (char)('0' + (code - Keys.D0));
            if (code >= Keys.NumPad0 && code <= Keys.NumPad9) return s + "Num" + (char)('0' + (code - Keys.NumPad0));
            switch (code)
            {
                case Keys.Oemtilde: return s + "`";
                case Keys.OemMinus: return s + "-";
                case Keys.Oemplus: return s + "=";
                case Keys.OemOpenBrackets: return s + "[";
                case Keys.Oem6: return s + "]";
                case Keys.Oem5: return s + "\\";
                case Keys.Oem1: return s + ";";
                case Keys.Oem7: return s + "'";
                case Keys.Oemcomma: return s + ",";
                case Keys.OemPeriod: return s + ".";
                case Keys.OemQuestion: return s + "/";
                case Keys.Next: return s + "PageDown";
                case Keys.Prior: return s + "PageUp";
                default: return s + code;
            }
        }

        /// <summary>
        /// A hotkey takes its key away from every other program, the game included. Function keys are fine
        /// alone; anything else needs Ctrl, Alt or Shift, or typing that letter in the game chat would stop working.
        /// </summary>
        public static bool Allowed(Keys k, out string why)
        {
            Keys code = k & Keys.KeyCode;
            bool modifier = (k & (Keys.Control | Keys.Alt | Keys.Shift)) != 0;
            why = null;
            if (code == Keys.Escape) { why = "Esc stops a running scan, pick another key."; return false; }
            if (code == Keys.None || code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu) { why = "Press a key, not only Ctrl/Alt/Shift."; return false; }
            if (code >= Keys.F1 && code <= Keys.F24) return true;
            if (!modifier) { why = "Use a function key (F1-F24), or hold Ctrl, Alt or Shift with the key: a single " + Name(k) + " would stop working in the game."; return false; }
            return true;
        }

        public static uint Modifiers(Keys k)
        {
            uint m = 0;
            if ((k & Keys.Alt) != 0) m |= MOD_ALT;
            if ((k & Keys.Control) != 0) m |= MOD_CONTROL;
            if ((k & Keys.Shift) != 0) m |= MOD_SHIFT;
            return m;
        }

        public static uint VirtualKey(Keys k) { return (uint)(k & Keys.KeyCode); }
    }

    /// <summary>"Press the new key" dialog.</summary>
    public class KeyCaptureForm : Form
    {
        readonly Label prompt, error;
        public Keys Result { get; private set; }

        public KeyCaptureForm(string action, Keys current)
        {
            Text = "Change hotkey";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = new Font("Segoe UI", 10f);
            Padding = new Padding(16);
            KeyPreview = true;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;

            FlowLayoutPanel panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            prompt = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                Text = "Press the new key for \"" + action + "\" (now " + Hotkeys.Name(current) + ").\n\n" +
                       "Function keys (F1-F24) work alone; other keys need Ctrl, Alt or Shift.\nEsc cancels."
            };
            error = new Label { AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = Theme.Danger, Margin = new Padding(0, 10, 0, 0) };
            panel.Controls.Add(prompt);
            panel.Controls.Add(error);
            Controls.Add(panel);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        // ProcessCmdKey also sees F10, Alt combinations and arrow keys, which KeyDown would miss or act on.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu) return true;   // wait for the real key
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            string why;
            if (!Hotkeys.Allowed(keyData, out why)) { error.Text = why; return true; }
            Result = keyData;
            DialogResult = DialogResult.OK;
            return true;
        }
    }
}
