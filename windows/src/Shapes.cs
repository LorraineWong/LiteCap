// Annotation shapes. Coordinates are in the frozen screen bitmap's pixel space,
// so the same objects draw on the overlay and on the final cropped image.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LiteCap
{
    abstract class Shape
    {
        public Color Color;
        public float Width;

        public abstract void Update(Point p);
        public abstract Rectangle Bounds { get; }
        public abstract void Draw(Graphics g, Bitmap src);
        public virtual bool Valid { get { return true; } }

        protected Rectangle Pad(Rectangle r, float extra)
        {
            int e = (int)Math.Ceiling(Width / 2 + extra + 2);
            r.Inflate(e, e);
            return r;
        }

        protected static Rectangle Norm(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        protected Pen MakePen()
        {
            Pen p = new Pen(Color, Width);
            p.StartCap = p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            return p;
        }
    }

    class BoxShape : Shape
    {
        Point a, b;
        readonly bool ellipse;

        public BoxShape(Point p, Color c, float w, bool ellipse)
        {
            a = b = p; Color = c; Width = w; this.ellipse = ellipse;
        }

        public override void Update(Point p) { b = p; }
        public override Rectangle Bounds { get { return Pad(Norm(a, b), 0); } }
        public override bool Valid { get { Rectangle r = Norm(a, b); return r.Width > 3 && r.Height > 3; } }

        public override void Draw(Graphics g, Bitmap src)
        {
            Rectangle r = Norm(a, b);
            using (Pen p = MakePen())
            {
                if (ellipse) g.DrawEllipse(p, r);
                else g.DrawRectangle(p, r);
            }
        }
    }

    class ArrowShape : Shape
    {
        Point a, b;

        public ArrowShape(Point p, Color c, float w) { a = b = p; Color = c; Width = w; }

        float Head { get { return Math.Max(Width * 3.4f, 12f); } }
        public override void Update(Point p) { b = p; }
        public override Rectangle Bounds { get { return Pad(Norm(a, b), Head); } }

        public override bool Valid
        {
            get { int dx = b.X - a.X, dy = b.Y - a.Y; return dx * dx + dy * dy > 64; }
        }

        public override void Draw(Graphics g, Bitmap src)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            float ux = dx / len, uy = dy / len;
            float head = Math.Min(Head, len * 0.6f);
            PointF bas = new PointF(b.X - ux * head * 0.85f, b.Y - uy * head * 0.85f);
            float half = head * 0.55f;
            using (Pen p = MakePen())
                g.DrawLine(p, a, bas);
            using (SolidBrush br = new SolidBrush(Color))
                g.FillPolygon(br, new PointF[] {
                    b,
                    new PointF(b.X - ux * head - uy * half, b.Y - uy * head + ux * half),
                    new PointF(b.X - ux * head + uy * half, b.Y - uy * head - ux * half) });
        }
    }

    class StrokeShape : Shape
    {
        readonly List<Point> pts = new List<Point>();

        public StrokeShape(Point p, Color c, float w) { pts.Add(p); Color = c; Width = w; }

        public override void Update(Point p)
        {
            Point last = pts[pts.Count - 1];
            if (Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) >= 2) pts.Add(p);
        }

        public override Rectangle Bounds
        {
            get
            {
                int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
                foreach (Point p in pts)
                {
                    l = Math.Min(l, p.X); t = Math.Min(t, p.Y); r = Math.Max(r, p.X); b = Math.Max(b, p.Y);
                }
                return Pad(Rectangle.FromLTRB(l, t, r, b), 0);
            }
        }

        public override void Draw(Graphics g, Bitmap src)
        {
            if (pts.Count == 1)
            {
                using (SolidBrush br = new SolidBrush(Color))
                    g.FillEllipse(br, pts[0].X - Width / 2, pts[0].Y - Width / 2, Width, Width);
                return;
            }
            using (Pen p = MakePen()) g.DrawLines(p, pts.ToArray());
        }
    }

    class MosaicShape : Shape
    {
        Point a, b;
        readonly int cell;
        Bitmap cache;
        Rectangle cacheRect;

        public MosaicShape(Point p, int cell) { a = b = p; this.cell = Math.Max(4, cell); Width = 0; }

        public override void Update(Point p) { b = p; }
        public override Rectangle Bounds { get { return Pad(Norm(a, b), 0); } }
        public override bool Valid { get { Rectangle r = Norm(a, b); return r.Width > 4 && r.Height > 4; } }

        public override void Draw(Graphics g, Bitmap src)
        {
            Rectangle r = Norm(a, b);
            r.Intersect(new Rectangle(0, 0, src.Width, src.Height));
            if (r.Width < 1 || r.Height < 1) return;
            if (cache == null || cacheRect != r)
            {
                if (cache != null) cache.Dispose();
                cache = new Bitmap(Math.Max(1, r.Width / cell), Math.Max(1, r.Height / cell));
                using (Graphics cg = Graphics.FromImage(cache))
                {
                    cg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    cg.DrawImage(src, new Rectangle(0, 0, cache.Width, cache.Height), r, GraphicsUnit.Pixel);
                }
                cacheRect = r;
            }
            GraphicsState st = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(cache, r);
            g.Restore(st);
        }
    }

    class TextShape : Shape
    {
        readonly Point p;
        readonly string text;
        readonly Font font;
        readonly Size size;

        public TextShape(Point p, string text, Font font, Color c)
        {
            this.p = p; this.text = text; this.font = font; Color = c; Width = 0;
            using (Bitmap tmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(tmp))
                size = Size.Ceiling(g.MeasureString(text, font, new PointF(0, 0), StringFormat.GenericTypographic));
        }

        public override void Update(Point q) { }
        public override Rectangle Bounds { get { return Pad(new Rectangle(p, size), 2); } }

        public override void Draw(Graphics g, Bitmap src)
        {
            GraphicsState st = g.Save();
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (SolidBrush br = new SolidBrush(Color))
                g.DrawString(text, font, br, p, StringFormat.GenericTypographic);
            g.Restore(st);
        }
    }
}
