using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>A colour with straight (not premultiplied) channels in 0..1.</summary>
    public readonly struct Color4
    {
        public readonly float R, G, B, A;

        public Color4(float r, float g, float b, float a = 1f)
        {
            R = r; G = g; B = b; A = a;
        }

        /// <summary>From an 8-bit-per-channel value (0..255), as the prototype's rgba() strings.</summary>
        public static Color4 Bytes(double r, double g, double b, double a = 1) =>
            new Color4((float)(r / 255.0), (float)(g / 255.0), (float)(b / 255.0), (float)a);

        /// <summary>From "#rrggbb".</summary>
        public static Color4 Hex(uint rgb, double a = 1) =>
            Bytes((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff, a);

        public Color4 WithAlpha(double a) => new Color4(R, G, B, (float)a);
    }

    /// <summary>A stop of a radial gradient: how far out (0 centre, 1 edge) and the colour there.</summary>
    public readonly struct GradientStop
    {
        public readonly double Offset;
        public readonly Color4 Colour;

        public GradientStop(double offset, Color4 colour)
        {
            Offset = offset;
            Colour = colour;
        }
    }

    /// <summary>
    /// Just enough of a 2D canvas to draw the prop decals and the steam puff without image files or an OS dependency: filled and
    /// rotated rectangles, discs, ellipses, thick polylines with round ends, arcs, radial gradients, and erasing, all antialiased, with
    /// a clip rectangle. The y axis points down, as in the browser's canvas the prototype drew on. Pixels are kept premultiplied;
    /// <see cref="ToBytes"/> gives straight RGBA.
    /// </summary>
    public sealed class Canvas2D
    {
        readonly float[] _px;   // premultiplied rgba
        double _clipX0, _clipY0, _clipX1, _clipY1;

        public int Width { get; }
        public int Height { get; }

        public Canvas2D(int width, int height)
        {
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "a canvas needs a positive size");
            Width = width;
            Height = height;
            _px = new float[width * height * 4];
            ClearClip();
        }

        public void Clear()
        {
            Array.Clear(_px, 0, _px.Length);
        }

        /// <summary>Only pixels inside this rectangle change until <see cref="ClearClip"/>.</summary>
        public void SetClip(double x, double y, double w, double h)
        {
            _clipX0 = x; _clipY0 = y; _clipX1 = x + w; _clipY1 = y + h;
        }

        public void ClearClip()
        {
            _clipX0 = 0; _clipY0 = 0; _clipX1 = Width; _clipY1 = Height;
        }

        // ------------------------------------------------------------------ shapes

        public void FillRect(double x, double y, double w, double h, Color4 colour, double alpha = 1) =>
            FillRotatedRect(x + w / 2, y + h / 2, w, h, 0, colour, alpha);

        /// <summary>A rectangle about its centre, turned clockwise (y down) by <paramref name="angle"/> radians.</summary>
        public void FillRotatedRect(double cx, double cy, double w, double h, double angle, Color4 colour, double alpha = 1)
        {
            double cs = Math.Cos(angle), sn = Math.Sin(angle);
            double hx = w / 2, hy = h / 2;
            double reach = Math.Abs(hx * cs) + Math.Abs(hy * sn), reachY = Math.Abs(hx * sn) + Math.Abs(hy * cs);
            Paint(cx - reach - 1, cy - reachY - 1, cx + reach + 1, cy + reachY + 1, (px, py) =>
            {
                double dx = px - cx, dy = py - cy;
                double lx = dx * cs + dy * sn, ly = -dx * sn + dy * cs;
                return Coverage(Math.Max(Math.Abs(lx) - hx, Math.Abs(ly) - hy));
            }, colour, alpha);
        }

        public void FillCircle(double cx, double cy, double r, Color4 colour, double alpha = 1) =>
            Paint(cx - r - 1, cy - r - 1, cx + r + 1, cy + r + 1, (px, py) => Coverage(Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - r), colour, alpha);

        /// <summary>An ellipse with radii rx, ry along its own axes, turned by <paramref name="angle"/>.</summary>
        public void FillEllipse(double cx, double cy, double rx, double ry, double angle, Color4 colour, double alpha = 1)
        {
            double cs = Math.Cos(angle), sn = Math.Sin(angle);
            double reach = Math.Max(rx, ry) + 1;
            Paint(cx - reach, cy - reach, cx + reach, cy + reach, (px, py) =>
            {
                double dx = px - cx, dy = py - cy;
                double lx = (dx * cs + dy * sn) / rx, ly = (-dx * sn + dy * cs) / ry;
                double d = Math.Sqrt(lx * lx + ly * ly);
                return Coverage((d - 1) * Math.Min(rx, ry));   // approximate pixel distance from the rim
            }, colour, alpha);
        }

        /// <summary>A disc filled from the centre outward by gradient stops (straight colours, interpolated, then composited).</summary>
        public void FillRadialGradient(double cx, double cy, double r, IReadOnlyList<GradientStop> stops, double alpha = 1)
        {
            if (stops == null || stops.Count < 2) throw new ArgumentException("a gradient needs at least two stops", nameof(stops));
            PaintColour(cx - r - 1, cy - r - 1, cx + r + 1, cy + r + 1, (px, py) =>
            {
                double d = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                double cover = Coverage(d - r);
                if (cover <= 0) return (default(Color4), 0.0);
                return (Sample(stops, Math.Min(1.0, d / r)), cover);
            }, alpha);
        }

        /// <summary>A thick line through the points with round ends and joins, composited once (overlaps do not darken).</summary>
        public void StrokePolyline(IReadOnlyList<(double x, double y)> points, double width, Color4 colour, double alpha = 1)
        {
            if (points == null || points.Count < 1) throw new ArgumentException("a stroke needs at least one point", nameof(points));
            double half = width / 2;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach ((double x, double y) p in points)
            {
                x0 = Math.Min(x0, p.x); x1 = Math.Max(x1, p.x); y0 = Math.Min(y0, p.y); y1 = Math.Max(y1, p.y);
            }
            // Coverage is the best of every segment's capsule, found one segment's own box at a time (a stroke's overall box is mostly
            // empty), then composited once so overlaps do not double up.
            int bx0 = (int)Math.Max(Math.Floor(Math.Max(x0 - half - 1, _clipX0)), 0), bx1 = (int)Math.Min(Math.Ceiling(Math.Min(x1 + half + 1, _clipX1)), Width);
            int by0 = (int)Math.Max(Math.Floor(Math.Max(y0 - half - 1, _clipY0)), 0), by1 = (int)Math.Min(Math.Ceiling(Math.Min(y1 + half + 1, _clipY1)), Height);
            int bw = bx1 - bx0, bh = by1 - by0;
            if (bw <= 0 || bh <= 0) return;
            var cover = new float[bw * bh];

            int segments = Math.Max(1, points.Count - 1);
            for (int s = 0; s < segments; s++)
            {
                (double x, double y) a = points[s], b = points[Math.Min(s + 1, points.Count - 1)];
                int sx0 = Math.Max(bx0, (int)Math.Floor(Math.Min(a.x, b.x) - half - 1)), sx1 = Math.Min(bx1, (int)Math.Ceiling(Math.Max(a.x, b.x) + half + 1));
                int sy0 = Math.Max(by0, (int)Math.Floor(Math.Min(a.y, b.y) - half - 1)), sy1 = Math.Min(by1, (int)Math.Ceiling(Math.Max(a.y, b.y) + half + 1));
                for (int y = sy0; y < sy1; y++)
                {
                    for (int x = sx0; x < sx1; x++)
                    {
                        float c = (float)Coverage(SegmentDistance(x + 0.5, y + 0.5, a, b) - half);
                        int k = (y - by0) * bw + (x - bx0);
                        if (c > cover[k]) cover[k] = c;
                    }
                }
            }

            for (int y = by0; y < by1; y++)
            {
                for (int x = bx0; x < bx1; x++)
                {
                    float c = cover[(y - by0) * bw + (x - bx0)];
                    if (c > 0) Composite(x, y, c * ClipCoverage(x + 0.5, y + 0.5) * alpha * colour.A, colour);
                }
            }
        }

        /// <summary>A filled polygon (even-odd), antialiased. <paramref name="shade"/> gives each pixel's colour; null paints <paramref name="colour"/>.</summary>
        public void FillPolygon(IReadOnlyList<(double x, double y)> points, Color4 colour, double alpha = 1, Func<double, double, Color4> shade = null)
        {
            if (points == null || points.Count < 3) throw new ArgumentException("a polygon needs at least three points", nameof(points));
            const int Sub = 4;   // sub-rows per pixel row; coverage along a row is exact
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach ((double x, double y) p in points)
            {
                x0 = Math.Min(x0, p.x); x1 = Math.Max(x1, p.x); y0 = Math.Min(y0, p.y); y1 = Math.Max(y1, p.y);
            }
            int ix0 = (int)Math.Max(Math.Floor(Math.Max(x0, _clipX0)), 0), ix1 = (int)Math.Min(Math.Ceiling(Math.Min(x1, _clipX1)), Width);
            int iy0 = (int)Math.Max(Math.Floor(Math.Max(y0, _clipY0)), 0), iy1 = (int)Math.Min(Math.Ceiling(Math.Min(y1, _clipY1)), Height);
            if (ix1 <= ix0 || iy1 <= iy0) return;

            var row = new float[ix1 - ix0];
            var crossings = new List<double>();
            for (int y = iy0; y < iy1; y++)
            {
                Array.Clear(row, 0, row.Length);
                bool any = false;
                for (int s = 0; s < Sub; s++)
                {
                    double sy = y + (s + 0.5) / Sub;
                    crossings.Clear();
                    for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                    {
                        (double x, double y) a = points[j], b = points[i];
                        if ((a.y > sy) == (b.y > sy)) continue;
                        crossings.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
                    }
                    crossings.Sort();
                    for (int k = 0; k + 1 < crossings.Count; k += 2)
                    {
                        double sx0 = Math.Max(crossings[k], ix0), sx1 = Math.Min(crossings[k + 1], ix1);
                        if (sx1 <= sx0) continue;
                        any = true;
                        for (int x = (int)Math.Floor(sx0); x < Math.Ceiling(sx1) && x < ix1; x++)
                        {
                            double lo = Math.Max(sx0, x), hi = Math.Min(sx1, x + 1);
                            if (hi > lo) row[x - ix0] += (float)((hi - lo) / Sub);
                        }
                    }
                }
                if (!any) continue;
                for (int x = ix0; x < ix1; x++)
                {
                    float cover = row[x - ix0];
                    if (cover <= 0f) continue;
                    Color4 c = shade != null ? shade(x + 0.5, y + 0.5) : colour;
                    Composite(x, y, Math.Min(1.0, cover) * ClipCoverage(x + 0.5, y + 0.5) * alpha * c.A, c);
                }
            }
        }

        /// <summary>A rectangle filled with a linear gradient from (<paramref name="gx0"/>, <paramref name="gy0"/>) to (<paramref name="gx1"/>, <paramref name="gy1"/>), as the prototype's createLinearGradient.</summary>
        public void FillLinearGradientRect(double x, double y, double w, double h, double gx0, double gy0, double gx1, double gy1, IReadOnlyList<GradientStop> stops, double alpha = 1)
        {
            if (stops == null || stops.Count < 2) throw new ArgumentException("a gradient needs at least two stops", nameof(stops));
            double dx = gx1 - gx0, dy = gy1 - gy0;
            double len2 = dx * dx + dy * dy;
            Func<double, double, Color4> shade = (px, py) => Sample(stops, len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((px - gx0) * dx + (py - gy0) * dy) / len2)));
            FillPolygon(new[] { (x, y), (x + w, y), (x + w, y + h), (x, y + h) }, default, alpha, shade);
        }

        /// <summary>The same gradient painted inside any polygon.</summary>
        public void FillPolygonLinearGradient(IReadOnlyList<(double x, double y)> points, double gx0, double gy0, double gx1, double gy1, IReadOnlyList<GradientStop> stops, double alpha = 1)
        {
            if (stops == null || stops.Count < 2) throw new ArgumentException("a gradient needs at least two stops", nameof(stops));
            double dx = gx1 - gx0, dy = gy1 - gy0;
            double len2 = dx * dx + dy * dy;
            FillPolygon(points, default, alpha, (px, py) => Sample(stops, len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((px - gx0) * dx + (py - gy0) * dy) / len2))));
        }

        /// <summary>An ellipse outline (or part of one, clockwise from <paramref name="start"/> to <paramref name="end"/>), drawn as a stroke.</summary>
        public void StrokeEllipse(double cx, double cy, double rx, double ry, double start, double end, double width, Color4 colour, double alpha = 1)
        {
            double sweep = end - start;
            if (sweep >= 2 * Math.PI) sweep = 2 * Math.PI;
            else
            {
                sweep %= 2 * Math.PI;
                if (sweep < 0) sweep += 2 * Math.PI;
            }
            int steps = Math.Max(8, (int)Math.Ceiling(sweep * Math.Max(rx, ry) / 2.0));
            var pts = new List<(double, double)>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                double a = start + sweep * i / steps;
                pts.Add((cx + Math.Cos(a) * rx, cy + Math.Sin(a) * ry));
            }
            StrokePolyline(pts, width, colour, alpha);
        }

        /// <summary>A circular arc from <paramref name="start"/> clockwise (y down) to <paramref name="end"/>, drawn as a stroke.</summary>
        public void StrokeArc(double cx, double cy, double r, double start, double end, double width, Color4 colour, double alpha = 1)
        {
            double sweep = end - start;
            if (sweep >= 2 * Math.PI) sweep = 2 * Math.PI;
            else
            {
                sweep %= 2 * Math.PI;
                if (sweep < 0) sweep += 2 * Math.PI;
            }
            int steps = Math.Max(4, (int)Math.Ceiling(sweep * r / 2.0));
            var pts = new List<(double, double)>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                double a = start + sweep * i / steps;
                pts.Add((cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
            }
            StrokePolyline(pts, width, colour, alpha);
        }

        /// <summary>Points along a cubic Bezier curve, endpoints included (the prototype's bezierCurveTo).</summary>
        public static IEnumerable<(double x, double y)> Bezier((double x, double y) p0, (double x, double y) p1, (double x, double y) p2, (double x, double y) p3, int steps = 24)
        {
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps, u = 1 - t;
                double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
                yield return (a * p0.x + b * p1.x + c * p2.x + d * p3.x, a * p0.y + b * p1.y + c * p2.y + d * p3.y);
            }
        }

        // ------------------------------------------------------------------ erasing (the canvas's destination-out)

        /// <summary>Remove <paramref name="alpha"/> of whatever is inside the disc.</summary>
        public void EraseCircle(double cx, double cy, double r, double alpha = 1) =>
            Erase(cx - r - 1, cy - r - 1, cx + r + 1, cy + r + 1, (px, py) => Coverage(Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - r), alpha);

        /// <summary>Remove from inside the disc by a falloff that is <paramref name="centreAlpha"/> at the middle and zero at the rim.</summary>
        public void EraseRadial(double cx, double cy, double r, double centreAlpha)
        {
            Erase(cx - r - 1, cy - r - 1, cx + r + 1, cy + r + 1, (px, py) =>
            {
                double d = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                return d >= r ? 0 : (1 - d / r) * centreAlpha;
            }, 1);
        }

        // ------------------------------------------------------------------ reading

        /// <summary>Straight (non-premultiplied) RGBA bytes, row 0 at the top of the canvas.</summary>
        public byte[] ToBytes()
        {
            var bytes = new byte[Width * Height * 4];
            for (int i = 0; i < Width * Height; i++)
            {
                float a = _px[i * 4 + 3];
                if (a <= 0f) continue;
                float inv = 1f / a;
                bytes[i * 4] = ToByte(_px[i * 4] * inv);
                bytes[i * 4 + 1] = ToByte(_px[i * 4 + 1] * inv);
                bytes[i * 4 + 2] = ToByte(_px[i * 4 + 2] * inv);
                bytes[i * 4 + 3] = ToByte(a);
            }
            return bytes;
        }

        /// <summary>The opacity of every pixel, 0 to 1, row 0 at the top: the canvas as a single-channel mask.</summary>
        public float[] ToAlpha()
        {
            var alpha = new float[Width * Height];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = _px[i * 4 + 3];
            return alpha;
        }

        /// <summary>The straight colour of one pixel.</summary>
        public Color4 Pixel(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside the canvas");
            int i = (y * Width + x) * 4;
            float a = _px[i + 3];
            return a <= 0f ? default : new Color4(_px[i] / a, _px[i + 1] / a, _px[i + 2] / a, a);
        }

        // ------------------------------------------------------------------ internals

        /// <summary>Pixel coverage from a signed distance to the shape's edge in pixels (negative inside): a one-pixel soft edge.</summary>
        static double Coverage(double signedDistance)
        {
            double c = 0.5 - signedDistance;
            return c <= 0 ? 0 : c >= 1 ? 1 : c;
        }

        static double SegmentDistance(double px, double py, (double x, double y) a, (double x, double y) b)
        {
            double abx = b.x - a.x, aby = b.y - a.y;
            double len2 = abx * abx + aby * aby;
            double t = len2 < 1e-12 ? 0 : ((px - a.x) * abx + (py - a.y) * aby) / len2;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            double dx = px - (a.x + abx * t), dy = py - (a.y + aby * t);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        static Color4 Sample(IReadOnlyList<GradientStop> stops, double t)
        {
            if (t <= stops[0].Offset) return stops[0].Colour;
            for (int i = 1; i < stops.Count; i++)
            {
                if (t > stops[i].Offset) continue;
                GradientStop a = stops[i - 1], b = stops[i];
                double k = b.Offset - a.Offset < 1e-9 ? 1 : (t - a.Offset) / (b.Offset - a.Offset);
                return new Color4(
                    (float)(a.Colour.R + (b.Colour.R - a.Colour.R) * k), (float)(a.Colour.G + (b.Colour.G - a.Colour.G) * k),
                    (float)(a.Colour.B + (b.Colour.B - a.Colour.B) * k), (float)(a.Colour.A + (b.Colour.A - a.Colour.A) * k));
            }
            return stops[stops.Count - 1].Colour;
        }

        static byte ToByte(float v) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255f)));

        /// <summary>Visit the pixels of a box that fall inside the clip, passing each pixel centre to the coverage function.</summary>
        void Paint(double x0, double y0, double x1, double y1, Func<double, double, double> coverage, Color4 colour, double alpha) =>
            PaintColour(x0, y0, x1, y1, (px, py) => (colour, coverage(px, py)), alpha);

        void PaintColour(double x0, double y0, double x1, double y1, Func<double, double, (Color4 colour, double cover)> shade, double alpha)
        {
            int ix0 = (int)Math.Max(Math.Floor(Math.Max(x0, _clipX0)), 0), ix1 = (int)Math.Min(Math.Ceiling(Math.Min(x1, _clipX1)), Width);
            int iy0 = (int)Math.Max(Math.Floor(Math.Max(y0, _clipY0)), 0), iy1 = (int)Math.Min(Math.Ceiling(Math.Min(y1, _clipY1)), Height);
            for (int y = iy0; y < iy1; y++)
            {
                for (int x = ix0; x < ix1; x++)
                {
                    double clip = ClipCoverage(x + 0.5, y + 0.5);
                    if (clip <= 0) continue;
                    (Color4 c, double cover) = shade(x + 0.5, y + 0.5);
                    Composite(x, y, cover * clip * alpha * c.A, c);
                }
            }
        }

        /// <summary>Source-over of one colour at the given opacity.</summary>
        void Composite(int x, int y, double a, Color4 c)
        {
            if (a <= 0) return;
            int i = (y * Width + x) * 4;
            float inv = 1f - (float)a;
            _px[i] = c.R * (float)a + _px[i] * inv;
            _px[i + 1] = c.G * (float)a + _px[i + 1] * inv;
            _px[i + 2] = c.B * (float)a + _px[i + 2] * inv;
            _px[i + 3] = (float)a + _px[i + 3] * inv;
        }

        void Erase(double x0, double y0, double x1, double y1, Func<double, double, double> amount, double alpha)
        {
            int ix0 = (int)Math.Max(Math.Floor(Math.Max(x0, _clipX0)), 0), ix1 = (int)Math.Min(Math.Ceiling(Math.Min(x1, _clipX1)), Width);
            int iy0 = (int)Math.Max(Math.Floor(Math.Max(y0, _clipY0)), 0), iy1 = (int)Math.Min(Math.Ceiling(Math.Min(y1, _clipY1)), Height);
            for (int y = iy0; y < iy1; y++)
            {
                for (int x = ix0; x < ix1; x++)
                {
                    double clip = ClipCoverage(x + 0.5, y + 0.5);
                    if (clip <= 0) continue;
                    double a = amount(x + 0.5, y + 0.5) * clip * alpha;
                    if (a <= 0) continue;
                    int i = (y * Width + x) * 4;
                    float keep = 1f - (float)Math.Min(1.0, a);
                    _px[i] *= keep; _px[i + 1] *= keep; _px[i + 2] *= keep; _px[i + 3] *= keep;
                }
            }
        }

        double ClipCoverage(double px, double py) => px >= _clipX0 && px < _clipX1 && py >= _clipY0 && py < _clipY1 ? 1 : 0;
    }
}
