// Fullscreen capture overlay: pick a region or window, adjust it, annotate it, confirm.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteCap
{
    class SelectOverlay : Form
    {
        enum State { Idle, Creating, Adjusting, Moving, Resizing }
        enum Hit { None, Move, N, S, E, W, NE, NW, SE, SW }
        enum Tool { None, Rect, Ellipse, Arrow, Pen, Marker, Text, Mosaic }

        class Btn
        {
            public string Id, Tip;
            public Rectangle R;
            public Btn(string id, Rectangle r, string tip) { Id = id; R = r; Tip = tip; }
        }

        static readonly Color[] Palette = {
            Color.FromArgb(239, 68, 68), Color.FromArgb(249, 115, 22), Color.FromArgb(250, 204, 21),
            Color.FromArgb(34, 197, 94), Color.FromArgb(14, 165, 233), Color.FromArgb(168, 85, 247),
            Color.White, Color.FromArgb(17, 24, 39) };
        static readonly float[] StrokeWidths = { 2f, 4f, 7f };
        static readonly float[] TextSizes = { 18f, 26f, 38f };
        static readonly float[] MosaicCells = { 7f, 12f, 20f };
        static readonly string[] ToolIds = { "Rect", "Ellipse", "Arrow", "Pen", "Marker", "Text", "Mosaic" };

        // Remembered across captures within a session.
        static int colorIdx = 0, sizeIdx = 1;

        readonly Bitmap shot, dimmed;
        readonly Rectangle vs, full;
        readonly List<Rectangle> windows = new List<Rectangle>();
        bool editable;
        readonly string hint;
        readonly Font labelFont, hintFont, btnFont, tipFont, magFont;
        readonly int handleSize, magCells = 15, magZoom;

        State state = State.Idle;
        bool pending;
        Point downPt, cursorPt;
        Rectangle sel = Rectangle.Empty, selAtDown, hover = Rectangle.Empty, hintRect = Rectangle.Empty;
        Hit grab = Hit.None;
        Rectangle lastActive = Rectangle.Empty;
        List<Rectangle> lastExtras = new List<Rectangle>();

        Tool tool = Tool.None;
        readonly List<Shape> shapes = new List<Shape>();
        Shape drawing;
        TextBox editor;
        Point editorPt;

        List<Btn> buttons = new List<Btn>();
        Rectangle mainBar = Rectangle.Empty, subBar = Rectangle.Empty;
        string hoverBtn;

        public Rectangle Result = Rectangle.Empty;
        public List<Shape> Shapes { get { return shapes; } }

        // True when the user asked for text recognition instead of an image.
        public bool OcrRequested;
        readonly bool ocrMode;

        public SelectOverlay(Bitmap shot, Rectangle vs, List<Rectangle> screenWindows, bool ocrMode, bool editable)
        {
            this.shot = shot;
            this.vs = vs;
            this.full = new Rectangle(0, 0, vs.Width, vs.Height);
            this.editable = editable;
            foreach (Rectangle r in screenWindows)
            {
                Rectangle local = new Rectangle(r.X - vs.X, r.Y - vs.Y, r.Width, r.Height);
                local.Intersect(full);
                if (local.Width > 0 && local.Height > 0) windows.Add(local);
            }
            this.ocrMode = ocrMode;
            if (ocrMode) this.editable = false;
            hint = L.T(ocrMode ? "hint_ocr" : "hint_region");

            handleSize = Math.Max(5, Ui.Px(7));
            magZoom = Math.Max(6, Ui.Px(8));
            labelFont = new Font("Segoe UI", Ui.Px(12), FontStyle.Bold, GraphicsUnit.Pixel);
            hintFont = Ui.F(14, FontStyle.Regular);
            btnFont = Ui.F(13, FontStyle.Bold);
            tipFont = Ui.F(12, FontStyle.Regular);
            magFont = new Font("Consolas", Ui.Px(12), FontStyle.Regular, GraphicsUnit.Pixel);

            dimmed = new Bitmap(shot);
            using (Graphics g = Graphics.FromImage(dimmed))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.FillRectangle(b, 0, 0, dimmed.Width, dimmed.Height);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = false;
            Cursor = Cursors.Cross;
            Bounds = vs;
        }

        protected override void WndProc(ref Message m)
        {
            // Ignore DPI-change resizing when spanning monitors with different scaling.
            if (m.Msg == Native.WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = vs;
            Activate();
            Focus();

            Screen scr = Screen.FromPoint(Cursor.Position);
            Size ts = TextRenderer.MeasureText(hint, hintFont);
            int w = ts.Width + Ui.Px(32), h = ts.Height + Ui.Px(16);
            hintRect = new Rectangle(scr.Bounds.X - vs.X + (scr.Bounds.Width - w) / 2, scr.Bounds.Y - vs.Y + Ui.Px(28), w, h);

            cursorPt = PointToClient(Cursor.Position);
            hover = FindWindowAt(cursorPt);
            Invalidate();
        }

        // ================= geometry =================

        Rectangle ActiveRect { get { return state == State.Idle ? hover : sel; } }
        bool ShowMagnifier { get { return state == State.Idle || state == State.Creating || state == State.Resizing; } }
        bool ShowHint { get { return state == State.Idle || state == State.Creating; } }
        bool ShowBars { get { return state == State.Adjusting && editable; } }

        Rectangle FindWindowAt(Point p)
        {
            foreach (Rectangle r in windows)
                if (r.Contains(p)) return r;
            return full;
        }

        static Rectangle FromPoints(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
        }

        static Point Clamp(Rectangle r, Point p)
        {
            return new Point(Math.Max(r.Left, Math.Min(r.Right - 1, p.X)), Math.Max(r.Top, Math.Min(r.Bottom - 1, p.Y)));
        }

        Rectangle MoveRect(Rectangle r, int dx, int dy)
        {
            int x = Math.Max(0, Math.Min(full.Width - r.Width, r.X + dx));
            int y = Math.Max(0, Math.Min(full.Height - r.Height, r.Y + dy));
            return new Rectangle(x, y, r.Width, r.Height);
        }

        Rectangle ResizeRect(Rectangle r, Hit h, int dx, int dy)
        {
            int l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
            if (h == Hit.W || h == Hit.NW || h == Hit.SW) l += dx;
            if (h == Hit.E || h == Hit.NE || h == Hit.SE) rt += dx;
            if (h == Hit.N || h == Hit.NE || h == Hit.NW) t += dy;
            if (h == Hit.S || h == Hit.SE || h == Hit.SW) b += dy;
            Rectangle n = Rectangle.FromLTRB(Math.Min(l, rt), Math.Min(t, b), Math.Max(l, rt), Math.Max(t, b));
            n.Intersect(full);
            if (n.Width < 1) n.Width = 1;
            if (n.Height < 1) n.Height = 1;
            return n;
        }

        Rectangle LabelRect(Rectangle a)
        {
            Size ts = TextRenderer.MeasureText(a.Width + " × " + a.Height, labelFont);
            int w = ts.Width + Ui.Px(12), h = ts.Height + Ui.Px(6);
            int y = a.Y - h - Ui.Px(6);
            if (y < 0) y = a.Y + Ui.Px(6);
            int x = Math.Max(0, Math.Min(a.X, full.Width - w));
            return new Rectangle(x, y, w, h);
        }

        Rectangle MagRect()
        {
            int m = magCells * magZoom;
            int w = m, h = m + Ui.Px(40);
            int x = cursorPt.X + Ui.Px(20), y = cursorPt.Y + Ui.Px(20);
            if (x + w > full.Width) x = cursorPt.X - Ui.Px(20) - w;
            if (y + h > full.Height) y = cursorPt.Y - Ui.Px(20) - h;
            return new Rectangle(x, y, w, h);
        }

        Rectangle[] Handles(Rectangle r)
        {
            int s = handleSize, hs = s / 2;
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            return new Rectangle[] {
                new Rectangle(r.Left - hs, r.Top - hs, s, s), new Rectangle(cx - hs, r.Top - hs, s, s), new Rectangle(r.Right - hs, r.Top - hs, s, s),
                new Rectangle(r.Left - hs, cy - hs, s, s), new Rectangle(r.Right - hs, cy - hs, s, s),
                new Rectangle(r.Left - hs, r.Bottom - hs, s, s), new Rectangle(cx - hs, r.Bottom - hs, s, s), new Rectangle(r.Right - hs, r.Bottom - hs, s, s)
            };
        }

        Hit HitTest(Point p)
        {
            if (state != State.Adjusting || sel.IsEmpty || tool != Tool.None) return Hit.None;
            int tol = Math.Max(handleSize, Ui.Px(6));
            bool nl = Math.Abs(p.X - sel.Left) <= tol, nr = Math.Abs(p.X - sel.Right) <= tol;
            bool nt = Math.Abs(p.Y - sel.Top) <= tol, nb = Math.Abs(p.Y - sel.Bottom) <= tol;
            bool inX = p.X >= sel.Left - tol && p.X <= sel.Right + tol;
            bool inY = p.Y >= sel.Top - tol && p.Y <= sel.Bottom + tol;
            if (nt && nl) return Hit.NW;
            if (nt && nr) return Hit.NE;
            if (nb && nl) return Hit.SW;
            if (nb && nr) return Hit.SE;
            if (nt && inX) return Hit.N;
            if (nb && inX) return Hit.S;
            if (nl && inY) return Hit.W;
            if (nr && inY) return Hit.E;
            if (sel.Contains(p)) return Hit.Move;
            return Hit.None;
        }

        static Cursor CursorFor(Hit h)
        {
            switch (h)
            {
                case Hit.N: case Hit.S: return Cursors.SizeNS;
                case Hit.E: case Hit.W: return Cursors.SizeWE;
                case Hit.NW: case Hit.SE: return Cursors.SizeNWSE;
                case Hit.NE: case Hit.SW: return Cursors.SizeNESW;
                case Hit.Move: return Cursors.SizeAll;
                default: return Cursors.Cross;
            }
        }

        // ================= toolbar layout =================

        void LayoutBars()
        {
            buttons = new List<Btn>();
            mainBar = subBar = Rectangle.Empty;
            if (!ShowBars) return;

            int bs = Ui.Px(34), gap = Ui.Px(2), pad = Ui.Px(6), sep = Ui.Px(13);
            int doneW = Math.Max(Ui.Px(84), TextRenderer.MeasureText(L.T("done"), btnFont).Width + Ui.Px(48));
            int H = bs + pad * 2;
            int W = pad + ToolIds.Length * bs + (ToolIds.Length - 1) * gap + sep + bs + gap + bs + sep + bs + gap * 2 + doneW + pad;

            bool showSub = tool != Tool.None;
            int dot = Ui.Px(30), sw = Ui.Px(24), swGap = Ui.Px(5), subH = Ui.Px(40);
            int subW = pad + 3 * dot + (tool == Tool.Mosaic ? 0 : sep + Palette.Length * sw + (Palette.Length - 1) * swGap) + pad + Ui.Px(4);
            int need = H + (showSub ? subH + Ui.Px(6) : 0);
            int margin = Ui.Px(10);

            int x = Math.Max(0, Math.Min(sel.Right - W, full.Width - W));
            int y;
            bool above = false;
            if (sel.Bottom + margin + need <= full.Height) y = sel.Bottom + margin;
            else if (sel.Top - margin - need >= 0) { y = sel.Top - margin - H; above = true; }
            else { y = sel.Bottom - margin - H; above = true; }
            mainBar = new Rectangle(x, y, W, H);
            if (showSub)
            {
                int sx = Math.Max(0, Math.Min(x, full.Width - subW));
                subBar = above ? new Rectangle(sx, y - Ui.Px(6) - subH, subW, subH)
                               : new Rectangle(sx, y + H + Ui.Px(6), subW, subH);
            }

            int bx = x + pad, by = y + pad;
            for (int i = 0; i < ToolIds.Length; i++)
            {
                buttons.Add(new Btn("tool:" + ToolIds[i], new Rectangle(bx, by, bs, bs), L.T("tip_" + ToolIds[i])));
                bx += bs + gap;
            }
            bx += sep - gap;
            buttons.Add(new Btn("undo", new Rectangle(bx, by, bs, bs), L.T("tip_undo")));
            bx += bs + gap;
            buttons.Add(new Btn("ocr", new Rectangle(bx, by, bs, bs), L.T("tip_ocr")));
            bx += bs + sep;
            buttons.Add(new Btn("cancel", new Rectangle(bx, by, bs, bs), L.T("tip_cancel")));
            bx += bs + gap * 2;
            buttons.Add(new Btn("done", new Rectangle(bx, by, doneW, bs), L.T("tip_done")));

            if (showSub)
            {
                int sx2 = subBar.X + pad;
                string prefix = tool == Tool.Text ? "tsize_" : tool == Tool.Mosaic ? "msize_" : "size_";
                for (int i = 0; i < 3; i++)
                {
                    buttons.Add(new Btn("size:" + i, new Rectangle(sx2, subBar.Y + (subH - dot) / 2, dot, dot), L.T(prefix + i)));
                    sx2 += dot;
                }
                if (tool != Tool.Mosaic)
                {
                    sx2 += sep;
                    for (int i = 0; i < Palette.Length; i++)
                    {
                        buttons.Add(new Btn("color:" + i, new Rectangle(sx2, subBar.Y + (subH - sw) / 2, sw, sw), null));
                        sx2 += sw + swGap;
                    }
                }
            }
        }

        Btn BtnAt(Point p)
        {
            foreach (Btn b in buttons)
                if (b.R.Contains(p)) return b;
            return null;
        }

        Rectangle TipRect(out string text)
        {
            text = null;
            if (hoverBtn == null) return Rectangle.Empty;
            Btn b = null;
            foreach (Btn cand in buttons) if (cand.Id == hoverBtn) b = cand;
            if (b == null || b.Tip == null) return Rectangle.Empty;
            text = b.Tip;
            Size ts = TextRenderer.MeasureText(text, tipFont);
            int w = ts.Width + Ui.Px(16), h = ts.Height + Ui.Px(8);
            Rectangle bar = mainBar.Contains(b.R) ? mainBar : subBar;
            int x = Math.Max(0, Math.Min(b.R.X + b.R.Width / 2 - w / 2, full.Width - w));
            int y = bar.Y - h - Ui.Px(6);
            if (y < 0 || (bar == mainBar && !subBar.IsEmpty && subBar.Y < mainBar.Y)) y = bar.Bottom + Ui.Px(6);
            return new Rectangle(x, y, w, h);
        }

        // ================= incremental repaint =================

        List<Rectangle> Extras()
        {
            List<Rectangle> list = new List<Rectangle>();
            Rectangle a = ActiveRect;
            if (!a.IsEmpty)
            {
                Rectangle l = LabelRect(a);
                l.Inflate(2, 2);
                list.Add(l);
            }
            if (ShowHint && !hintRect.IsEmpty) list.Add(hintRect);
            if (!mainBar.IsEmpty) { Rectangle r = mainBar; r.Inflate(Ui.Px(12), Ui.Px(12)); list.Add(r); }
            if (!subBar.IsEmpty) { Rectangle r = subBar; r.Inflate(Ui.Px(12), Ui.Px(12)); list.Add(r); }
            string tip;
            Rectangle t = TipRect(out tip);
            if (!t.IsEmpty) { t.Inflate(2, 2); list.Add(t); }
            if (ShowMagnifier)
            {
                Rectangle m = MagRect();
                m.Inflate(3, 3);
                list.Add(m);
            }
            return list;
        }

        Rectangle Grow(Rectangle r)
        {
            if (r.IsEmpty) return r;
            r.Inflate(handleSize + 3, handleSize + 3);
            return r;
        }

        void UpdateView()
        {
            LayoutBars();
            Rectangle a = ActiveRect;
            List<Rectangle> extras = Extras();
            using (Region dirty = new Region(Rectangle.Empty))
            {
                if (!a.IsEmpty) dirty.Union(Grow(a));
                if (!lastActive.IsEmpty) dirty.Union(Grow(lastActive));
                // Pixels and annotations are in absolute coordinates, so the shared interior
                // of the old and new selection is unchanged: skip repainting it.
                Rectangle inter = Rectangle.Intersect(a, lastActive);
                inter.Inflate(-(handleSize + 3), -(handleSize + 3));
                if (inter.Width > 0 && inter.Height > 0) dirty.Exclude(inter);
                foreach (Rectangle r in extras) dirty.Union(r);
                foreach (Rectangle r in lastExtras) dirty.Union(r);
                Invalidate(dirty);
            }
            lastActive = a;
            lastExtras = extras;
        }

        void InvalidateArea(Rectangle r)
        {
            r.Inflate(4, 4);
            Invalidate(r);
        }

        // ================= painting =================

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle clip = e.ClipRectangle;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(dimmed, clip, clip, GraphicsUnit.Pixel);

            Rectangle a = ActiveRect;
            if (!a.IsEmpty)
            {
                Rectangle c = Rectangle.Intersect(a, clip);
                if (c.Width > 0 && c.Height > 0) g.DrawImage(shot, c, c, GraphicsUnit.Pixel);
            }
            g.CompositingMode = CompositingMode.SourceOver;

            if (!a.IsEmpty && (shapes.Count > 0 || drawing != null))
            {
                GraphicsState st = g.Save();
                g.SetClip(a, CombineMode.Intersect);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                foreach (Shape s in shapes) s.Draw(g, shot);
                if (drawing != null) drawing.Draw(g, shot);
                g.Restore(st);
            }

            if (!a.IsEmpty)
            {
                using (Pen pen = new Pen(Ui.Select, Math.Max(1, Ui.Px(2))))
                {
                    pen.Alignment = PenAlignment.Inset;
                    g.DrawRectangle(pen, a.X, a.Y, a.Width, a.Height);
                }
                if (state != State.Idle && tool == Tool.None)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    foreach (Rectangle h in Handles(a))
                    {
                        g.FillEllipse(Brushes.White, h);
                        using (Pen p = new Pen(Ui.Select, 1.5f)) g.DrawEllipse(p, h);
                    }
                    g.SmoothingMode = SmoothingMode.None;
                }
                DrawTag(g, LabelRect(a), a.Width + " × " + a.Height, labelFont, Color.White, Ui.Px(5));
            }

            if (ShowHint && !hintRect.IsEmpty && hintRect.IntersectsWith(clip) && !hintRect.IntersectsWith(a))
                DrawTag(g, hintRect, hint, hintFont, Ui.TextMain, Ui.Px(10));

            if (ShowBars) DrawBars(g);
            if (ShowMagnifier) DrawMagnifier(g);
        }

        static void DrawTag(Graphics g, Rectangle r, string text, Font f, Color fore, int radius)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Ui.FillRound(g, r, radius, Color.FromArgb(230, Ui.Bg));
            g.SmoothingMode = old;
            TextRenderer.DrawText(g, text, f, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        void DrawBar(Graphics g, Rectangle r)
        {
            Ui.Shadow(g, r, Ui.Px(10));
            Ui.FillRound(g, r, Ui.Px(10), Color.FromArgb(248, Ui.Bg));
            Ui.StrokeRound(g, r, Ui.Px(10), Color.FromArgb(70, 255, 255, 255));
        }

        void DrawBars(Graphics g)
        {
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (!mainBar.IsEmpty) DrawBar(g, mainBar);
            if (!subBar.IsEmpty) DrawBar(g, subBar);

            int sepTop = mainBar.Y + Ui.Px(12), sepBottom = mainBar.Bottom - Ui.Px(12);
            foreach (Btn b in buttons)
            {
                bool isHover = b.Id == hoverBtn;
                if (b.Id.StartsWith("tool:"))
                {
                    bool active = "tool:" + tool.ToString() == b.Id;
                    if (active) Ui.FillRound(g, b.R, Ui.Px(7), Ui.AccentDeep);
                    else if (isHover) Ui.FillRound(g, b.R, Ui.Px(7), Ui.BgSoft);
                    Icons.Draw(g, b.Id.Substring(5), b.R, active ? Color.White : Ui.TextMain);
                }
                else if (b.Id == "undo")
                {
                    if (isHover) Ui.FillRound(g, b.R, Ui.Px(7), Ui.BgSoft);
                    bool can = shapes.Count > 0 || editor != null;
                    Icons.Draw(g, "Undo", b.R, can ? Ui.TextMain : Color.FromArgb(90, Ui.TextMain));
                    using (Pen sp = new Pen(Ui.Line))
                        g.DrawLine(sp, b.R.X - Ui.Px(7), sepTop, b.R.X - Ui.Px(7), sepBottom);
                }
                else if (b.Id == "ocr")
                {
                    if (isHover) Ui.FillRound(g, b.R, Ui.Px(7), Ui.BgSoft);
                    Icons.Draw(g, "ScanText", b.R, Ui.TextMain);
                }
                else if (b.Id == "cancel")
                {
                    if (isHover) Ui.FillRound(g, b.R, Ui.Px(7), Color.FromArgb(70, 239, 68, 68));
                    Icons.Draw(g, "Cancel", b.R, isHover ? Color.FromArgb(252, 165, 165) : Ui.TextMain);
                    using (Pen sp = new Pen(Ui.Line))
                        g.DrawLine(sp, b.R.X - Ui.Px(7), sepTop, b.R.X - Ui.Px(7), sepBottom);
                }
                else if (b.Id == "done")
                {
                    Ui.FillRound(g, b.R, Ui.Px(7), isHover ? Ui.AccentHover : Ui.AccentDeep);
                    Rectangle ic = new Rectangle(b.R.X + Ui.Px(6), b.R.Y, b.R.Height - Ui.Px(4), b.R.Height);
                    Icons.Draw(g, "Done", ic, Color.White);
                    Rectangle tr = new Rectangle(ic.Right - Ui.Px(4), b.R.Y, b.R.Right - ic.Right, b.R.Height);
                    TextRenderer.DrawText(g, L.T("done"), btnFont, tr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else if (b.Id.StartsWith("size:"))
                {
                    int i = int.Parse(b.Id.Substring(5));
                    if (i == sizeIdx) Ui.FillRound(g, b.R, Ui.Px(7), Ui.BgSoft);
                    else if (isHover) Ui.FillRound(g, b.R, Ui.Px(7), Color.FromArgb(120, Ui.BgSoft));
                    float d = Ui.Px(5 + i * 4);
                    Color dc = tool == Tool.Mosaic ? Ui.TextMain : Palette[colorIdx];
                    if (dc.GetBrightness() < 0.2f) dc = Ui.TextMain;
                    using (SolidBrush db = new SolidBrush(i == sizeIdx ? dc : Color.FromArgb(150, dc)))
                        g.FillEllipse(db, b.R.X + (b.R.Width - d) / 2f, b.R.Y + (b.R.Height - d) / 2f, d, d);
                    if (i == 2 && tool != Tool.Mosaic)
                        using (Pen sp = new Pen(Ui.Line))
                            g.DrawLine(sp, b.R.Right + Ui.Px(6), subBar.Y + Ui.Px(10), b.R.Right + Ui.Px(6), subBar.Bottom - Ui.Px(10));
                }
                else if (b.Id.StartsWith("color:"))
                {
                    int i = int.Parse(b.Id.Substring(6));
                    Rectangle r = b.R;
                    if (i == colorIdx)
                    {
                        Rectangle ring = r;
                        ring.Inflate(Ui.Px(2), Ui.Px(2));
                        using (Pen rp = new Pen(Color.White, Math.Max(1.5f, Ui.Px(2)))) g.DrawEllipse(rp, ring);
                    }
                    Rectangle dotR = r;
                    if (i != colorIdx && !isHover) dotR.Inflate(-Ui.Px(2), -Ui.Px(2));
                    using (SolidBrush cb = new SolidBrush(Palette[i])) g.FillEllipse(cb, dotR);
                    using (Pen op = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawEllipse(op, dotR);
                }
            }

            string tip;
            Rectangle t = TipRect(out tip);
            if (!t.IsEmpty) DrawTag(g, t, tip, tipFont, Ui.TextMain, Ui.Px(6));
            g.Restore(st);
        }

        void DrawMagnifier(Graphics g)
        {
            Rectangle m = MagRect();
            int size = magCells * magZoom, half = magCells / 2;
            Rectangle img = new Rectangle(m.X, m.Y, size, size);
            g.FillRectangle(Brushes.Black, img);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(shot, img, new Rectangle(cursorPt.X - half, cursorPt.Y - half, magCells, magCells), GraphicsUnit.Pixel);
            g.PixelOffsetMode = PixelOffsetMode.Default;

            using (SolidBrush cross = new SolidBrush(Color.FromArgb(100, Ui.Accent)))
            {
                g.FillRectangle(cross, img.X, img.Y + half * magZoom, size, magZoom);
                g.FillRectangle(cross, img.X + half * magZoom, img.Y, magZoom, size);
            }
            using (Pen p = new Pen(Color.White)) g.DrawRectangle(p, img.X + half * magZoom, img.Y + half * magZoom, magZoom - 1, magZoom - 1);

            Rectangle info = new Rectangle(m.X, m.Y + size, size, m.Height - size);
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(240, Ui.Bg))) g.FillRectangle(bg, info);
            string rgb = "";
            if (full.Contains(cursorPt))
            {
                Color c = shot.GetPixel(cursorPt.X, cursorPt.Y);
                rgb = string.Format("RGB {0},{1},{2}", c.R, c.G, c.B);
            }
            TextRenderer.DrawText(g, string.Format("X {0}  Y {1}\n{2}", cursorPt.X, cursorPt.Y, rgb), magFont, info,
                Ui.TextMain, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using (Pen p = new Pen(Color.FromArgb(90, 255, 255, 255))) g.DrawRectangle(p, m.X, m.Y, m.Width - 1, m.Height - 1);
        }

        // ================= annotation =================

        void SetTool(Tool t)
        {
            CommitText();
            tool = t;
            UpdateView();
            UpdateCursor(cursorPt);
        }

        Shape NewShape(Point p)
        {
            Color c = Palette[colorIdx];
            float w = Math.Max(1f, StrokeWidths[sizeIdx] * Ui.S);
            switch (tool)
            {
                case Tool.Rect: return new BoxShape(p, c, w, false);
                case Tool.Ellipse: return new BoxShape(p, c, w, true);
                case Tool.Arrow: return new ArrowShape(p, c, w);
                case Tool.Pen: return new StrokeShape(p, c, w);
                case Tool.Marker: return new StrokeShape(p, Color.FromArgb(105, c), w * 3 + Ui.Px(8));
                case Tool.Mosaic: return new MosaicShape(p, (int)(MosaicCells[sizeIdx] * Ui.S));
                default: return null;
            }
        }

        Font TextFont()
        {
            return Ui.F(TextSizes[sizeIdx], FontStyle.Bold);
        }

        Size EditorSize(string text)
        {
            Size sz = TextRenderer.MeasureText(text + "国", editor.Font);
            return new Size(Math.Max(sz.Width + Ui.Px(8), Ui.Px(60)), sz.Height + Ui.Px(4));
        }

        void StyleEditor()
        {
            Color c = Palette[colorIdx];
            editor.ForeColor = c;
            editor.BackColor = c.GetBrightness() > 0.7f ? Color.FromArgb(40, 44, 52) : Color.White;
        }

        void StartText(Point p)
        {
            editor = new TextBox();
            editor.Multiline = true;
            editor.BorderStyle = BorderStyle.None;
            editor.Font = TextFont();
            editor.WordWrap = false;
            editor.Location = p;
            StyleEditor();
            editor.Size = EditorSize("");
            editor.TextChanged += delegate { if (editor != null) editor.Size = EditorSize(editor.Text); };
            editorPt = p;
            Controls.Add(editor);
            editor.Focus();
            UpdateView();
        }

        void CommitText()
        {
            if (editor == null) return;
            TextBox ed = editor;
            editor = null;
            string t = ed.Text.TrimEnd(new char[] { ' ', '\t', '\r', '\n' });
            Rectangle area = ed.Bounds;
            if (t.Length > 0)
            {
                TextShape s = new TextShape(new Point(editorPt.X + 1, editorPt.Y + 1), t, (Font)ed.Font.Clone(), ed.ForeColor);
                shapes.Add(s);
                area = Rectangle.Union(area, s.Bounds);
            }
            Controls.Remove(ed);
            ed.Dispose();
            Focus();
            InvalidateArea(area);
            UpdateView();
        }

        void CancelText()
        {
            if (editor == null) return;
            Rectangle area = editor.Bounds;
            Controls.Remove(editor);
            editor.Dispose();
            editor = null;
            Focus();
            InvalidateArea(area);
            UpdateView();
        }

        void Undo()
        {
            if (editor != null) { CancelText(); return; }
            if (shapes.Count == 0) return;
            Shape s = shapes[shapes.Count - 1];
            shapes.RemoveAt(shapes.Count - 1);
            InvalidateArea(s.Bounds);
            UpdateView();
        }

        void OnButton(string id)
        {
            if (id.StartsWith("tool:"))
            {
                Tool t = (Tool)Enum.Parse(typeof(Tool), id.Substring(5));
                SetTool(tool == t ? Tool.None : t);
            }
            else if (id == "undo") Undo();
            else if (id == "ocr") { OcrRequested = true; Finish(sel); }
            else if (id == "cancel") CancelAll();
            else if (id == "done") Finish(sel);
            else if (id.StartsWith("size:")) SetSize(int.Parse(id.Substring(5)));
            else if (id.StartsWith("color:"))
            {
                colorIdx = int.Parse(id.Substring(6));
                if (editor != null) StyleEditor();
                UpdateView();
            }
        }

        void SetSize(int i)
        {
            sizeIdx = i;
            if (editor != null)
            {
                editor.Font = TextFont();
                editor.Size = EditorSize(editor.Text);
            }
            UpdateView();
        }

        void UpdateCursor(Point p)
        {
            Cursor want;
            if (state != State.Adjusting) want = Cursors.Cross;
            else if (BtnAt(p) != null) want = Cursors.Hand;
            else if (tool != Tool.None) want = sel.Contains(p) ? (tool == Tool.Text ? Cursors.IBeam : Cursors.Cross) : Cursors.Default;
            else want = CursorFor(HitTest(p));
            if (Cursor != want) Cursor = want;
        }

        // ================= input =================

        void Finish(Rectangle r)
        {
            CommitText();
            r.Intersect(full);
            if (r.Width < 2 || r.Height < 2) return;
            Result = r;
            if (ocrMode) OcrRequested = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        void CancelAll()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        void EnterAdjusting(Rectangle r)
        {
            sel = r;
            if (editable) state = State.Adjusting;
            else Finish(r);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Point p = e.Location;
            cursorPt = p;

            if (editor != null)
            {
                bool insideText = tool == Tool.Text && sel.Contains(p) && BtnAt(p) == null;
                CommitText();
                if (insideText) return;
            }

            if (e.Button == MouseButtons.Right)
            {
                if (tool != Tool.None) { SetTool(Tool.None); return; }
                if (state == State.Adjusting)
                {
                    if (shapes.Count > 0) return;   // keep annotations; use Esc to abandon
                    state = State.Idle;
                    sel = Rectangle.Empty;
                    hover = FindWindowAt(p);
                    UpdateCursor(p);
                    UpdateView();
                }
                else CancelAll();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            downPt = p;
            selAtDown = sel;
            if (state == State.Adjusting)
            {
                LayoutBars();
                Btn b = BtnAt(p);
                if (b != null) { OnButton(b.Id); return; }
                if (tool != Tool.None)
                {
                    if (!sel.Contains(p)) return;
                    if (tool == Tool.Text) { StartText(p); return; }
                    drawing = NewShape(p);
                    if (drawing != null) InvalidateArea(drawing.Bounds);
                    return;
                }
                Hit h = HitTest(p);
                if (h == Hit.Move) state = State.Moving;
                else if (h != Hit.None) { state = State.Resizing; grab = h; }
                else pending = true;
                UpdateView();
                return;
            }
            if (state == State.Idle) pending = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = e.Location;
            cursorPt = p;

            if (drawing != null)
            {
                Rectangle before = drawing.Bounds;
                drawing.Update(Clamp(sel, p));
                InvalidateArea(Rectangle.Union(before, drawing.Bounds));
                return;
            }

            if (pending && (Math.Abs(p.X - downPt.X) > 4 || Math.Abs(p.Y - downPt.Y) > 4))
            {
                pending = false;
                if (state == State.Adjusting && shapes.Count > 0) return;   // don't discard annotations by dragging outside
                state = State.Creating;
            }
            int dx = p.X - downPt.X, dy = p.Y - downPt.Y;
            switch (state)
            {
                case State.Creating: sel = FromPoints(downPt, p); break;
                case State.Moving: sel = MoveRect(selAtDown, dx, dy); break;
                case State.Resizing: sel = ResizeRect(selAtDown, grab, dx, dy); break;
                case State.Idle: if (!pending) hover = FindWindowAt(p); break;
                case State.Adjusting:
                    {
                        Btn b = BtnAt(p);
                        hoverBtn = b == null ? null : b.Id;
                        break;
                    }
            }
            UpdateCursor(p);
            UpdateView();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (drawing != null)
            {
                Shape s = drawing;
                drawing = null;
                if (s.Valid) shapes.Add(s);
                InvalidateArea(s.Bounds);
                UpdateView();
                return;
            }
            if (pending)
            {
                pending = false;
                if (state == State.Idle) { EnterAdjusting(FindWindowAt(e.Location)); if (IsDisposed) return; }
            }
            else if (state == State.Creating)
            {
                if (sel.Width < 3 || sel.Height < 3) { state = State.Idle; sel = Rectangle.Empty; }
                else { EnterAdjusting(sel); if (IsDisposed) return; }
            }
            else if (state == State.Moving || state == State.Resizing)
            {
                state = State.Adjusting;
            }
            UpdateCursor(e.Location);
            UpdateView();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && state == State.Adjusting && tool == Tool.None
                && sel.Contains(e.Location) && BtnAt(e.Location) == null)
                Finish(sel);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) != 0, shift = (keyData & Keys.Shift) != 0;

            if (editor != null)
            {
                if (k == Keys.Escape) { CancelText(); return true; }
                if (k == Keys.Enter && !shift) { CommitText(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            if (k == Keys.Escape)
            {
                if (tool != Tool.None) SetTool(Tool.None);
                else CancelAll();
                return true;
            }
            if (k == Keys.Enter || k == Keys.Space || (ctrl && k == Keys.C))
            {
                if (state == State.Adjusting) Finish(sel);
                else if (k == Keys.Enter) Finish(full);
                return true;
            }
            if (state != State.Adjusting) return base.ProcessCmdKey(ref msg, keyData);

            if (ctrl && k == Keys.Z) { Undo(); return true; }
            if (editable && !ctrl && k == Keys.O) { OcrRequested = true; Finish(sel); return true; }
            if (editable && !ctrl)
            {
                Tool t = Tool.None;
                switch (k)
                {
                    case Keys.R: t = Tool.Rect; break;
                    case Keys.E: t = Tool.Ellipse; break;
                    case Keys.A: t = Tool.Arrow; break;
                    case Keys.P: t = Tool.Pen; break;
                    case Keys.H: t = Tool.Marker; break;
                    case Keys.T: t = Tool.Text; break;
                    case Keys.M: t = Tool.Mosaic; break;
                }
                if (t != Tool.None) { SetTool(tool == t ? Tool.None : t); return true; }
                if (tool != Tool.None && (k == Keys.D1 || k == Keys.D2 || k == Keys.D3)) { SetSize(k - Keys.D1); return true; }
            }
            if (tool == Tool.None && (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down))
            {
                int step = ctrl ? 10 : 1;
                int dx = k == Keys.Left ? -step : k == Keys.Right ? step : 0;
                int dy = k == Keys.Up ? -step : k == Keys.Down ? step : 0;
                sel = shift ? ResizeRect(sel, Hit.SE, dx, dy) : MoveRect(sel, dx, dy);
                UpdateView();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dimmed.Dispose();
                labelFont.Dispose(); hintFont.Dispose(); btnFont.Dispose(); tipFont.Dispose(); magFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
