// Line icons designed on a 24-unit grid (Lucide-style), rendered at 4x and downsampled
// so they stay smooth at small sizes. Results are cached per id/size/colour.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LiteCap
{
    static class Icons
    {
        const float Stroke = 1.75f;   // grid units
        const int Super = 4;
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        // Draws icon `id` centred in `box` at roughly 55% of the box's short side.
        public static void Draw(Graphics g, string id, Rectangle box, Color c)
        {
            int px = Math.Max(12, (int)Math.Round(Math.Min(box.Width, box.Height) * 0.55));
            Bitmap b = Get(id, px, c);
            int x = box.X + (box.Width - px) / 2, y = box.Y + (box.Height - px) / 2;
            GraphicsState st = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(b, new Rectangle(x, y, px, px), 0, 0, px, px, GraphicsUnit.Pixel);
            g.Restore(st);
        }

        // Icon bitmap of exactly `size` pixels (for menus and picture boxes). Caller owns the copy.
        public static Bitmap Make(string id, int size, Color c)
        {
            return new Bitmap(Get(id, size, c));
        }

        static Bitmap Get(string id, int px, Color c)
        {
            string key = id + "|" + px + "|" + c.ToArgb();
            Bitmap b;
            if (cache.TryGetValue(key, out b)) return b;

            int big = px * Super;
            using (Bitmap hi = new Bitmap(big, big))
            {
                using (Graphics g = Graphics.FromImage(hi))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float k = big / 24f;
                    g.ScaleTransform(k, k);
                    using (Pen p = new Pen(c, Stroke))
                    using (SolidBrush br = new SolidBrush(c))
                    {
                        p.StartCap = p.EndCap = LineCap.Round;
                        p.LineJoin = LineJoin.Round;
                        Paint(g, id, p, br, c);
                    }
                }
                b = new Bitmap(px, px);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(hi, new Rectangle(0, 0, px, px));
                }
            }
            cache[key] = b;
            return b;
        }

        static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            GraphicsPath gp = new GraphicsPath();
            float d = r * 2;
            gp.AddArc(x, y, d, d, 180, 90);
            gp.AddArc(x + w - d, y, d, d, 270, 90);
            gp.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            gp.AddArc(x, y + h - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        static void Lines(Graphics g, Pen p, params float[] xy)
        {
            PointF[] pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
            g.DrawLines(p, pts);
        }

        static void Dot(Graphics g, Brush b, float x, float y, float r)
        {
            g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
        }

        // The capture glyph shared with the app logo: four corner brackets and a centre dot.
        static void Capture(Graphics g, Pen p, Brush b)
        {
            Lines(g, p, 3.5f, 8.5f, 3.5f, 3.5f, 8.5f, 3.5f);
            Lines(g, p, 15.5f, 3.5f, 20.5f, 3.5f, 20.5f, 8.5f);
            Lines(g, p, 20.5f, 15.5f, 20.5f, 20.5f, 15.5f, 20.5f);
            Lines(g, p, 8.5f, 20.5f, 3.5f, 20.5f, 3.5f, 15.5f);
            Dot(g, b, 12, 12, 2.6f);
        }

        static void Paint(Graphics g, string id, Pen p, Brush b, Color c)
        {
            switch (id)
            {
                case "Region":
                    Capture(g, p, b);
                    break;
                case "Rect":
                    using (GraphicsPath gp = RoundRect(3.5f, 5.5f, 17, 13, 2.5f)) g.DrawPath(p, gp);
                    break;
                case "Ellipse":
                    g.DrawEllipse(p, 3, 5, 18, 14);
                    break;
                case "Arrow":
                    g.DrawLine(p, 5.5f, 18.5f, 18, 6);
                    Lines(g, p, 9.5f, 5.5f, 18.5f, 5.5f, 18.5f, 14.5f);
                    break;
                case "Pen":
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        gp.AddLines(new PointF[] { new PointF(15.2f, 5.3f), new PointF(18.7f, 8.8f), new PointF(8.5f, 19f),
                                                   new PointF(4f, 20f), new PointF(5f, 15.5f) });
                        gp.CloseFigure();
                        g.DrawPath(p, gp);
                    }
                    g.DrawLine(p, 15.2f, 5.3f, 16.4f, 4.1f);
                    g.DrawLine(p, 16.4f, 4.1f, 17.6f, 3.9f);
                    g.DrawLine(p, 18.7f, 8.8f, 19.9f, 7.6f);
                    g.DrawLine(p, 19.9f, 7.6f, 20.1f, 6.4f);
                    g.DrawLine(p, 17.6f, 3.9f, 20.1f, 6.4f);
                    break;
                case "Marker":
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        gp.AddLines(new PointF[] { new PointF(13f, 4.5f), new PointF(19.5f, 11f), new PointF(12.5f, 18f),
                                                   new PointF(6f, 11.5f) });
                        gp.CloseFigure();
                        g.DrawPath(p, gp);
                    }
                    Lines(g, p, 6f, 11.5f, 3.5f, 17.5f, 6.5f, 20.5f, 12.5f, 18f);
                    using (SolidBrush soft = new SolidBrush(Color.FromArgb(90, c)))
                        g.FillRectangle(soft, 14f, 19.2f, 7f, 1.8f);
                    break;
                case "Text":
                    Lines(g, p, 5, 7.5f, 5, 4.5f, 19, 4.5f, 19, 7.5f);
                    g.DrawLine(p, 12, 4.5f, 12, 19.5f);
                    g.DrawLine(p, 9, 19.5f, 15, 19.5f);
                    break;
                case "Mosaic":
                    using (SolidBrush soft = new SolidBrush(Color.FromArgb(85, c)))
                        for (int i = 0; i < 3; i++)
                            for (int j = 0; j < 3; j++)
                                using (GraphicsPath gp = RoundRect(3.5f + i * 6, 3.5f + j * 6, 5, 5, 1.2f))
                                    g.FillPath((i + j) % 2 == 0 ? b : soft, gp);
                    break;
                case "Undo":
                    Lines(g, p, 8.5f, 14.5f, 3.5f, 9.5f, 8.5f, 4.5f);
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        gp.AddLine(3.5f, 9.5f, 14.5f, 9.5f);
                        gp.AddArc(9f, 9.5f, 11f, 11f, 270, 180);
                        gp.AddLine(14.5f, 20.5f, 11f, 20.5f);
                        g.DrawPath(p, gp);
                    }
                    break;
                case "Cancel":
                    g.DrawLine(p, 6.5f, 6.5f, 17.5f, 17.5f);
                    g.DrawLine(p, 17.5f, 6.5f, 6.5f, 17.5f);
                    break;
                case "Done":
                    Lines(g, p, 4.5f, 12.5f, 9.5f, 17.5f, 19.5f, 7f);
                    break;
                case "Window":
                    using (GraphicsPath gp = RoundRect(3, 4.5f, 18, 15, 2.5f)) g.DrawPath(p, gp);
                    g.DrawLine(p, 3, 9, 21, 9);
                    Dot(g, b, 6.2f, 6.8f, 0.95f);
                    Dot(g, b, 9f, 6.8f, 0.95f);
                    break;
                case "Full":
                    using (GraphicsPath gp = RoundRect(2.5f, 3.5f, 19, 13, 2.2f)) g.DrawPath(p, gp);
                    g.DrawLine(p, 12, 16.5f, 12, 20.5f);
                    g.DrawLine(p, 8, 20.5f, 16, 20.5f);
                    break;
                case "Folder":
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        gp.AddLines(new PointF[] { new PointF(5, 20), new PointF(19.5f, 20), new PointF(21.5f, 18), new PointF(21.5f, 9),
                            new PointF(19.5f, 7), new PointF(12f, 7), new PointF(10f, 4.5f), new PointF(4.5f, 4.5f),
                            new PointF(2.5f, 6.5f), new PointF(2.5f, 18) });
                        gp.CloseFigure();
                        g.DrawPath(p, gp);
                    }
                    break;
                case "Gear":
                    {
                        using (GraphicsPath gp = new GraphicsPath())
                        {
                            List<PointF> pts = new List<PointF>();
                            for (int i = 0; i < 8; i++)
                            {
                                double a0 = Math.PI * 2 * i / 8;
                                double[] off = { -0.2, -0.12, 0.12, 0.2 };
                                double[] rad = { 7.0, 9.4, 9.4, 7.0 };
                                for (int k = 0; k < 4; k++)
                                {
                                    double a = a0 + off[k];
                                    pts.Add(new PointF(12 + (float)(Math.Cos(a) * rad[k]), 12 + (float)(Math.Sin(a) * rad[k])));
                                }
                            }
                            gp.AddPolygon(pts.ToArray());
                            g.DrawPath(p, gp);
                        }
                        g.DrawEllipse(p, 9, 9, 6, 6);
                        break;
                    }
                case "Exit":
                    g.DrawArc(p, 4, 5, 16, 16, -50, 280);
                    g.DrawLine(p, 12, 2.5f, 12, 11.5f);
                    break;
                case "Keyboard":
                    using (GraphicsPath gp = RoundRect(2.5f, 5.5f, 19, 13, 2.5f)) g.DrawPath(p, gp);
                    foreach (float x in new float[] { 6.5f, 10f, 13.5f, 17f }) Dot(g, b, x, 9.5f, 1f);
                    foreach (float x in new float[] { 8.2f, 11.8f, 15.4f }) Dot(g, b, x, 12.6f, 1f);
                    g.DrawLine(p, 8, 15.5f, 16, 15.5f);
                    break;
                case "Sliders":
                    g.DrawLine(p, 4, 6, 10, 6); g.DrawLine(p, 14, 6, 20, 6);
                    g.DrawLine(p, 4, 12, 6, 12); g.DrawLine(p, 10, 12, 20, 12);
                    g.DrawLine(p, 4, 18, 14, 18); g.DrawLine(p, 18, 18, 20, 18);
                    g.DrawEllipse(p, 10, 4, 4, 4);
                    g.DrawEllipse(p, 6, 10, 4, 4);
                    g.DrawEllipse(p, 14, 16, 4, 4);
                    break;
                case "ScanText":
                    Lines(g, p, 3.5f, 8f, 3.5f, 3.5f, 8f, 3.5f);
                    Lines(g, p, 16f, 3.5f, 20.5f, 3.5f, 20.5f, 8f);
                    Lines(g, p, 20.5f, 16f, 20.5f, 20.5f, 16f, 20.5f);
                    Lines(g, p, 8f, 20.5f, 3.5f, 20.5f, 3.5f, 16f);
                    g.DrawLine(p, 8f, 9f, 16f, 9f);
                    g.DrawLine(p, 8f, 12f, 16f, 12f);
                    g.DrawLine(p, 8f, 15f, 13f, 15f);
                    break;
                case "Timer":
                    g.DrawEllipse(p, 4, 5, 16, 16);
                    Lines(g, p, 12, 9, 12, 13, 14.8f, 15.2f);
                    g.DrawLine(p, 9.5f, 2.5f, 14.5f, 2.5f);
                    break;
                case "Info":
                    g.DrawEllipse(p, 2.5f, 2.5f, 19, 19);
                    g.DrawLine(p, 12, 11, 12, 16.5f);
                    Dot(g, b, 12, 7.6f, 1.2f);
                    break;
                case "Globe":
                    g.DrawEllipse(p, 2.5f, 2.5f, 19, 19);
                    g.DrawEllipse(p, 8, 2.5f, 8, 19);
                    g.DrawLine(p, 2.5f, 12, 21.5f, 12);
                    break;
            }
        }
    }
}
