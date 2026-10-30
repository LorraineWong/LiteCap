// Custom-drawn light controls used by the settings window.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteCap
{
    abstract class PaintedControl : Control
    {
        protected bool hover, down;

        protected PaintedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Focus(); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected Graphics Begin(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            return g;
        }
    }

    // Rounded button: Primary = filled blue; otherwise white with a hairline border.
    class FlatButton : PaintedControl
    {
        public bool Primary;
        bool selected;

        public FlatButton(string text, bool primary)
        {
            Text = text;
            Primary = primary;
        }

        // Ghost buttons can act as a segmented choice.
        public bool Selected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; Invalidate(); } }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            int rad = Ui.Px(7);
            Color text;
            if (!Enabled)
            {
                Ui.FillRound(g, r, rad, Primary ? Color.FromArgb(191, 207, 245) : Ui.Divider);
                text = Primary ? Color.White : Ui.InkSoft;
            }
            else if (Primary)
            {
                Ui.FillRound(g, r, rad, down ? Color.FromArgb(30, 64, 175) : hover ? Ui.BlueHover : Ui.Blue);
                text = Color.White;
            }
            else
            {
                Color fill = selected ? Ui.BlueSoft : down ? Ui.Divider : hover ? Ui.FieldBg : Ui.Surface;
                Ui.FillRound(g, r, rad, fill);
                Ui.StrokeRound(g, r, rad, selected ? Color.FromArgb(147, 177, 247) : hover ? Color.FromArgb(203, 210, 222) : Ui.Border);
                text = selected ? Ui.Blue : Ui.Ink;
            }
            if (Focused && !hover && !Primary) Ui.StrokeRound(g, r, rad, Ui.Blue);
            TextRenderer.DrawText(g, Text, Font, r, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Sidebar navigation entry.
    class NavItem : PaintedControl
    {
        public readonly string IconId;
        bool selected;

        public NavItem(string text, string icon)
        {
            Text = text;
            IconId = icon;
            TabStop = false;
        }

        public bool Selected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; Invalidate(); } }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            if (selected)
            {
                Ui.FillRound(g, r, Ui.Px(8), Ui.Surface);
                Ui.StrokeRound(g, r, Ui.Px(8), Ui.Border);
                Rectangle bar = new Rectangle(Ui.Px(1), Height / 2 - Ui.Px(8), Ui.Px(3), Ui.Px(16));
                Ui.FillRound(g, bar, Ui.Px(2), Ui.Blue);
            }
            else if (hover) Ui.FillRound(g, r, Ui.Px(8), Color.FromArgb(226, 232, 240));
            int ic = Ui.Px(30);
            Icons.Draw(g, IconId, new Rectangle(Ui.Px(8), (Height - ic) / 2, ic, ic), selected ? Ui.Blue : Ui.IconInk);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Ui.Px(42), 0, Width - Ui.Px(42), Height),
                selected ? Ui.Ink : Color.FromArgb(55, 65, 81), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    // On/off switch.
    class Toggle : PaintedControl
    {
        bool on;
        public event EventHandler Changed;

        public bool Checked
        {
            get { return on; }
            set { if (on != value) { on = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); } }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) Checked = !Checked;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = on ? (hover ? Ui.BlueHover : Ui.Blue) : (hover ? Color.FromArgb(190, 197, 208) : Color.FromArgb(209, 214, 223));
            Ui.FillRound(g, track, Height / 2, fill);
            int d = Height - Ui.Px(6);
            int x = on ? Width - d - Ui.Px(3) - 1 : Ui.Px(3);
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                g.FillEllipse(shadow, x, Ui.Px(3) + 1, d, d);
            g.FillEllipse(Brushes.White, x, Ui.Px(3), d, d);
            if (Focused) Ui.StrokeRound(g, new Rectangle(0, 0, Width, Height), Height / 2, Color.FromArgb(147, 177, 247));
        }
    }

    // Hotkey recorder that shows the combination as keycaps.
    class KeyBox : PaintedControl
    {
        Hotkey value = Hotkey.Empty;
        string transient;

        public Hotkey Value
        {
            get { return value; }
            set { this.value = value; transient = null; Invalidate(); }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            transient = L.T("kb_press");
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            transient = null;
            base.OnLostFocus(e);
        }

        protected override bool IsInputKey(Keys keyData) { return (keyData & Keys.KeyCode) != Keys.Tab; }

        static uint ModsOf(Keys keyData)
        {
            uint m = 0;
            if ((keyData & Keys.Control) != 0) m |= Native.MOD_CONTROL;
            if ((keyData & Keys.Alt) != 0) m |= Native.MOD_ALT;
            if ((keyData & Keys.Shift) != 0) m |= Native.MOD_SHIFT;
            if (Native.WinKeyDown()) m |= Native.MOD_WIN;
            return m;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!Focused) return base.ProcessCmdKey(ref msg, keyData);
            Keys k = keyData & Keys.KeyCode;
            uint mods = ModsOf(keyData);
            if (k == Keys.Tab && mods == 0) return base.ProcessCmdKey(ref msg, keyData);
            if (mods == 0 && (k == Keys.Back || k == Keys.Delete)) { Value = Hotkey.Empty; Invalidate(); return true; }
            if (mods == 0 && k == Keys.Escape) { transient = null; Parent.SelectNextControl(this, true, true, true, true); return true; }
            if (Hotkey.IsModifierKey(k))
            {
                transient = new Hotkey(mods, Keys.None).Display + " + …";
                Invalidate();
                return true;
            }
            Accept(mods, k);
            return true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            // PrintScreen only arrives as key-up.
            if (e.KeyCode == Keys.PrintScreen) Accept(ModsOf(e.KeyData), Keys.PrintScreen);
            else if (transient != null && transient.EndsWith("…") && Control.ModifierKeys == Keys.None && !Native.WinKeyDown())
            {
                transient = L.T("kb_press");
                Invalidate();
            }
            base.OnKeyUp(e);
        }

        void Accept(uint mods, Keys k)
        {
            if (mods == 0 && !Hotkey.CanStandAlone(k))
            {
                transient = L.T("kb_needmod");
                Invalidate();
                return;
            }
            Value = new Hotkey(mods, k);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            int rad = Ui.Px(8);
            if (Focused)
            {
                Ui.FillRound(g, r, rad, Ui.BlueSoft);
                Ui.StrokeRound(g, r, rad, Ui.Blue);
            }
            else
            {
                Ui.FillRound(g, r, rad, hover ? Ui.Surface : Ui.FieldBg);
                Ui.StrokeRound(g, r, rad, hover ? Color.FromArgb(203, 210, 222) : Ui.Border);
            }

            if (transient != null)
            {
                TextRenderer.DrawText(g, transient, Font, r, Ui.Blue, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            if (value.IsEmpty)
            {
                TextRenderer.DrawText(g, L.T("unset"), Font, r, Ui.InkSoft, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            string[] parts = value.Display.Split(new string[] { " + " }, StringSplitOptions.None);
            int capH = Height - Ui.Px(14), gap = Ui.Px(5), padX = Ui.Px(9);
            List<int> widths = new List<int>();
            int total = 0;
            foreach (string p in parts)
            {
                int w = Math.Max(capH, TextRenderer.MeasureText(p, Font).Width + padX * 2 - Ui.Px(6));
                widths.Add(w);
                total += w;
            }
            total += gap * (parts.Length - 1);
            int x = (Width - total) / 2, y = (Height - capH) / 2 - Ui.Px(1);
            for (int i = 0; i < parts.Length; i++)
            {
                Rectangle cap = new Rectangle(x, y, widths[i], capH);
                Rectangle under = cap;
                under.Offset(0, Ui.Px(2));
                Ui.FillRound(g, under, Ui.Px(5), Color.FromArgb(209, 214, 223));
                Ui.FillRound(g, cap, Ui.Px(5), Ui.Surface);
                Ui.StrokeRound(g, cap, Ui.Px(5), Color.FromArgb(209, 214, 223));
                TextRenderer.DrawText(g, parts[i], Font, cap, Ui.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += widths[i] + gap;
            }
        }
    }

    // White rounded card; place child controls with BackColor = Ui.Surface.
    class Card : Panel
    {
        public readonly List<int> Dividers = new List<int>();   // y positions of row separators

        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Page;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            Ui.FillRound(g, r, Ui.Px(10), Ui.Surface);
            Ui.StrokeRound(g, r, Ui.Px(10), Ui.Border);
            using (Pen p = new Pen(Ui.Divider))
                foreach (int y in Dividers) g.DrawLine(p, Ui.Px(18), y, Width - Ui.Px(18), y);
        }
    }

    // Single-line text field drawn as a rounded box around a borderless TextBox.
    class FieldBox : Panel
    {
        public readonly TextBox Box = new TextBox();

        public FieldBox(Font font)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Surface;
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Ui.FieldBg;
            Box.ForeColor = Ui.Ink;
            Box.Font = font;
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Controls.Add(Box);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Box.Bounds = new Rectangle(Ui.Px(10), (Height - Box.PreferredHeight) / 2, Width - Ui.Px(20), Box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            Ui.FillRound(g, r, Ui.Px(7), Ui.FieldBg);
            Ui.StrokeRound(g, r, Ui.Px(7), Box.Focused ? Ui.Blue : Ui.Border);
        }
    }
}
