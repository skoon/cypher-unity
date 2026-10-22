using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    public enum TextAlign
    {
        Left,
        Centre,
        Right,
    }

    /// <summary>
    /// Draws text and symbols from the <see cref="StrokeFont"/> onto a <see cref="Canvas2D"/>: every glyph is a handful of thick round-ended
    /// lines, which is what a neon tube bent into a letter looks like. Sizes are in pixels; a Latin text's size is its cap height.
    /// </summary>
    public static class GlyphPainter
    {
        /// <summary>Stroke widths as a share of the cap height: a thin sans, a normal sign, a heavy one.</summary>
        public const double Light = 0.085, Normal = 0.12, Heavy = 0.19;

        /// <summary>How wide a text is at a cap height, in pixels.</summary>
        public static double Width(string text, double capHeight) => StrokeFont.Measure(text) * capHeight;

        /// <summary>
        /// Draw <paramref name="text"/> with its baseline at <paramref name="baselineY"/>. Characters the font lacks fail loudly.
        /// <paramref name="x"/> is the left, centre or right edge according to <paramref name="align"/>.
        /// </summary>
        public static void Text(Canvas2D canvas, string text, double x, double baselineY, double capHeight, double weight, TextAlign align, Color4 colour, double alpha = 1)
        {
            if (canvas == null) throw new ArgumentNullException(nameof(canvas));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (!(capHeight > 0)) throw new ArgumentOutOfRangeException(nameof(capHeight), capHeight, "text needs a positive height");

            double width = Width(text, capHeight);
            double pen = align == TextAlign.Left ? x : align == TextAlign.Centre ? x - width / 2 : x - width;
            double lineWidth = Math.Max(1.0, capHeight * weight);
            double top = baselineY - capHeight;
            foreach (char ch in text)
            {
                Glyph g = StrokeFont.LatinGlyph(ch);
                DrawStrokes(canvas, g, pen, top, capHeight, capHeight, lineWidth, colour, alpha);
                pen += (g.Advance + StrokeFont.Tracking) * capHeight;
            }
        }

        /// <summary>
        /// Draw a kana, kanji-like block or icon in the square whose top-left corner is (<paramref name="x"/>, <paramref name="y"/>) and whose
        /// side is <paramref name="size"/>.
        /// </summary>
        public static void Symbol(Canvas2D canvas, string key, double x, double y, double size, double lineWidth, Color4 colour, double alpha = 1)
        {
            if (canvas == null) throw new ArgumentNullException(nameof(canvas));
            DrawStrokes(canvas, StrokeFont.Symbol(key), x, y, size, size, lineWidth, colour, alpha);
        }

        /// <summary>A kana, kanji or icon drawn into a rectangle, stretched to it (the prototype's strokePolys).</summary>
        public static void SymbolInRect(Canvas2D canvas, string key, double x, double y, double w, double h, double lineWidth, Color4 colour, double alpha = 1)
        {
            if (canvas == null) throw new ArgumentNullException(nameof(canvas));
            DrawStrokes(canvas, StrokeFont.Symbol(key), x, y, w, h, lineWidth, colour, alpha);
        }

        static void DrawStrokes(Canvas2D canvas, Glyph glyph, double x, double y, double scaleX, double scaleY, double lineWidth, Color4 colour, double alpha)
        {
            foreach (GlyphStroke s in glyph.Strokes)
            {
                var pts = new List<(double x, double y)>(s.Points.Length);
                foreach ((double px, double py) p in s.Points) pts.Add((x + p.px * scaleX, y + p.py * scaleY));
                canvas.StrokePolyline(pts, lineWidth, colour, alpha);
            }
        }
    }

    /// <summary>Gaussian-like blur of a single-channel image, made of three box blurs so its cost does not grow with the radius.</summary>
    public static class AlphaBlur
    {
        /// <summary>Blur <paramref name="data"/> (width x height floats) in place. A sigma of 0 or less leaves it alone.</summary>
        public static void Apply(float[] data, int width, int height, double sigma)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length != width * height) throw new ArgumentException("the buffer does not match the size", nameof(data));
            if (!(sigma > 0)) return;

            // three box passes of this width approximate a gaussian of the given sigma
            int boxWidth = Math.Max(1, (int)Math.Round(Math.Sqrt(12 * sigma * sigma / 3 + 1)));
            int radius = boxWidth / 2;
            if (radius < 1) return;

            var tmp = new float[Math.Max(width, height)];
            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y < height; y++) BlurLine(data, y * width, 1, width, radius, tmp);
                for (int x = 0; x < width; x++) BlurLine(data, x, width, height, radius, tmp);
            }
        }

        /// <summary>One line of a box blur, with a running sum; what is off the edge counts as empty.</summary>
        static void BlurLine(float[] data, int start, int stride, int count, int radius, float[] tmp)
        {
            float inv = 1f / (2 * radius + 1);
            double sum = 0;
            for (int i = 0; i <= radius && i < count; i++) sum += data[start + i * stride];
            for (int i = 0; i < count; i++)
            {
                tmp[i] = (float)(sum * inv);
                int add = i + radius + 1, drop = i - radius;
                if (add < count) sum += data[start + add * stride];
                if (drop >= 0) sum -= data[start + drop * stride];
            }
            for (int i = 0; i < count; i++) data[start + i * stride] = tmp[i];
        }
    }
}
