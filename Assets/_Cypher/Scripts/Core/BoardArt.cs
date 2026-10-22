using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// The artwork the LED hoardings and holograms play: four ad panels (a corporate logo, a soft drink, a face, a data readout) in a
    /// 2x2 atlas, each with a ticker band along the bottom that the shader scrolls. A port of the prototype's drawPanel* functions, with
    /// the stroke font standing in for the OS fonts. Panel coordinates are canvas ones (y down); the atlas bytes are in Unity's order,
    /// the first row at the bottom of the picture.
    /// </summary>
    public static class BoardArt
    {
        public const int PanelCount = 4;
        public const int PanelSize = 512;
        public const int Columns = 2;
        public const int Size = PanelSize * Columns;

        /// <summary>Texels kept clear of the panel's edge so bilinear and mip taps never reach the neighbouring panel.</summary>
        public const int Inset = 8;

        /// <summary>The inset as a share of one panel, for the shader.</summary>
        public const double InsetFraction = (double)Inset / PanelSize;

        /// <summary>The bottom share of every panel that is a ticker the shader scrolls on its own.</summary>
        public const double TickerBand = 0.135;

        public static readonly IReadOnlyList<string> PanelNames = new[] { "corpo", "drink", "face", "data" };

        /// <summary>Where a point of a panel is in the atlas: (<paramref name="lu"/>, <paramref name="lv"/>) run 0 to 1 from the panel's left and bottom.</summary>
        public static (double U, double V) AtlasUv(int panel, double lu, double lv)
        {
            if (panel < 0 || panel >= PanelCount) throw new ArgumentOutOfRangeException(nameof(panel), panel, "there are four panels");
            int col = panel % Columns, row = panel / Columns;
            double span = PanelSize - 2 * Inset;
            double x = col * PanelSize + Inset + lu * span;
            double y = row * PanelSize + Inset + (1.0 - lv) * span;   // canvas rows count down
            return (x / Size, 1.0 - y / Size);
        }

        /// <summary>The atlas as RGBA bytes (straight alpha, sRGB colours), <see cref="Size"/> squared, first row at the bottom. The same bytes every time.</summary>
        public static byte[] Render()
        {
            var bytes = new byte[Size * Size * 4];
            for (int i = 0; i < PanelCount; i++)
            {
                var c = new Canvas2D(PanelSize, PanelSize);
                var rng = new Rng((uint)(9100 + i * 37));
                switch (i)
                {
                    case 0: DrawCorpo(c, rng); break;
                    case 1: DrawDrink(c, rng); break;
                    case 2: DrawFace(c, rng); break;
                    default: DrawData(c, rng); break;
                }
                byte[] px = c.ToBytes();
                int x0 = (i % Columns) * PanelSize, rowTop = (i / Columns) * PanelSize;
                for (int y = 0; y < PanelSize; y++)
                {
                    int dst = ((Size - 1 - (rowTop + y)) * Size + x0) * 4;   // the canvas row y is picture row rowTop + y; the bytes start at the bottom
                    Buffer.BlockCopy(px, y * PanelSize * 4, bytes, dst, PanelSize * 4);
                }
            }
            return bytes;
        }

        // ---------------------------------------------------------------- panels

        static readonly Color4 White = new Color4(1, 1, 1);
        static readonly Color4 Black = new Color4(0, 0, 0);

        static void Backdrop(Canvas2D c, uint top, uint bottom)
        {
            double w = c.Width, h = c.Height;
            c.FillLinearGradientRect(0, 0, w, h, 0, 0, w * 0.4, h, new[]
            {
                new GradientStop(0, Color4.Hex(top)),
                new GradientStop(1, Color4.Hex(bottom)),
            });
        }

        static void DrawCorpo(Canvas2D c, Rng rng)
        {
            double w = c.Width, h = c.Height;
            Backdrop(c, 0x04121c, 0x000308);

            // concentric diamond mark
            double cx = w * 0.30, cy = h * 0.40;
            Color4 ring = Color4.Hex(0x5fe9ff);
            for (int i = 4; i >= 1; i--)
            {
                double r = w * 0.055 * i;
                var diamond = new List<(double, double)> { (cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy), (cx, cy - r) };
                c.StrokePolyline(diamond, Math.Max(1.5, w * 0.010), ring, 0.35 + i * 0.16);
            }
            double k = w * 0.028;
            c.FillPolygon(new List<(double, double)> { (cx, cy - k), (cx + k, cy), (cx, cy + k), (cx - k, cy) }, Color4.Hex(0xeaffff));

            Line(c, "KENZAI", w * 0.10, h * 0.72, h * 0.14 * 0.72, GlyphPainter.Heavy, TextAlign.Left, Color4.Hex(0xd9f6ff), w * 0.80);
            Line(c, "BIOMECHANICAL SYSTEMS // TOKYO-3", w * 0.105, h * 0.79, h * 0.052 * 0.72, GlyphPainter.Light, TextAlign.Left, Color4.Hex(0x2ad8ff), w * 0.84);

            KanaColumn(c, w * 0.80, h * 0.10, h * 0.10, 6, rng, Color4.Hex(0xff2a86));
            Ticker(c, "   KENZAI HOLDINGS +4.2%   SECTOR 7 CURFEW 23:00   ", Color4.Hex(0x7ff0ff));
        }

        static void DrawDrink(Canvas2D c, Rng rng)
        {
            double w = c.Width, h = c.Height;
            Backdrop(c, 0x1a0320, 0x05000a);

            // bottle silhouette with a hot rim light
            double ox = w * 0.24, oy = h * 0.14, bw = w * 0.13;
            var path = new List<(double, double)> { (bw * 0.35, 0), (bw * 0.65, 0), (bw * 0.65, h * 0.16) };
            Quad(path, (bw * 0.65, h * 0.16), (bw, h * 0.24), (bw, h * 0.34));
            path.Add((bw, h * 0.62));
            Quad(path, (bw, h * 0.62), (bw, h * 0.66), (bw * 0.85, h * 0.66));
            path.Add((bw * 0.15, h * 0.66));
            Quad(path, (bw * 0.15, h * 0.66), (0, h * 0.66), (0, h * 0.62));
            path.Add((0, h * 0.34));
            Quad(path, (0, h * 0.34), (0, h * 0.24), (bw * 0.35, h * 0.16));
            path.Add((bw * 0.35, 0));
            var shifted = new List<(double, double)>(path.Count);
            foreach ((double x, double y) p in path) shifted.Add((p.x + ox, p.y + oy));
            c.FillPolygonLinearGradient(shifted, ox, 0, ox + bw, 0, new[]
            {
                new GradientStop(0, Color4.Hex(0xff2a86)),
                new GradientStop(0.45, Color4.Hex(0x3a0620)),
                new GradientStop(1, Color4.Hex(0xffd24a)),
            });
            c.StrokePolyline(shifted, Math.Max(1.2, w * 0.005), Color4.Hex(0xffe9f6));

            Line(c, "SYNTH-COLA", w * 0.42, h * 0.34, h * 0.13 * 0.72, GlyphPainter.Heavy, TextAlign.Left, White, w * 0.43);
            Line(c, "TASTE THE FUTURE", w * 0.42, h * 0.46, h * 0.085 * 0.72, GlyphPainter.Normal, TextAlign.Left, Color4.Hex(0xff54a8), w * 0.43);
            Line(c, "¥ 480", w * 0.42, h * 0.70, h * 0.20 * 0.72, GlyphPainter.Heavy, TextAlign.Left, Color4.Hex(0xffd24a), w * 0.40);
            KanaColumn(c, w * 0.86, h * 0.08, h * 0.095, 6, rng, Color4.Hex(0x22e0ff));
            Ticker(c, "   NEW FORMULA   NOW WITH REAL SUGAR ANALOGUE   ", Color4.Hex(0xffb0d8));
        }

        static void DrawFace(Canvas2D c, Rng rng)
        {
            double w = c.Width, h = c.Height;
            Backdrop(c, 0x12001a, 0x000000);

            // a stylised face: arcs and blocks, not a portrait, so it reads at sixty metres
            double cx = w * 0.36, cy = h * 0.48, r = h * 0.34;
            Color4 pink = Color4.Hex(0xff2a86), cyan = Color4.Hex(0x22e0ff);
            c.StrokeEllipse(cx, cy, r * 0.74, r, 0, 2 * Math.PI, w * 0.012, pink);
            c.StrokeEllipse(cx - r * 0.30, cy - r * 0.16, r * 0.20, r * 0.10, 0, 2 * Math.PI, w * 0.008, White);
            c.StrokeEllipse(cx + r * 0.30, cy - r * 0.16, r * 0.20, r * 0.10, 0, 2 * Math.PI, w * 0.008, White);
            c.FillCircle(cx - r * 0.30, cy - r * 0.16, r * 0.075, cyan);
            c.FillCircle(cx + r * 0.30, cy - r * 0.16, r * 0.075, cyan);
            c.StrokeArc(cx, cy + r * 0.16, r * 0.34, 0.25, Math.PI - 0.25, w * 0.016, pink);
            for (int i = 0; i < 5; i++)   // interface bars across one cheek
            {
                c.FillRect(cx + r * 0.44, cy - r * 0.5 + i * r * 0.14, r * (0.12 + rng.Next() * 0.3), r * 0.06, cyan, 0.75);
            }

            double right = w * 0.94;
            Line(c, "OFF-WORLD", right, h * 0.40, h * 0.12 * 0.72, GlyphPainter.Normal, TextAlign.Right, White, w * 0.70);
            Line(c, "A NEW LIFE AWAITS YOU", right, h * 0.50, h * 0.062 * 0.72, GlyphPainter.Light, TextAlign.Right, pink, w * 0.70);
            Line(c, "IN THE OFF-WORLD COLONIES", right, h * 0.57, h * 0.062 * 0.72, GlyphPainter.Light, TextAlign.Right, pink, w * 0.70);
            KanaColumn(c, w * 0.05, h * 0.10, h * 0.10, 6, rng, Color4.Hex(0x8a2bff));
            Ticker(c, "   EMIGRATE NOW   CHANCE TO BEGIN AGAIN   ", Color4.Hex(0xff7fc0));
        }

        static void DrawData(Canvas2D c, Rng rng)
        {
            double w = c.Width, h = c.Height;
            Backdrop(c, 0x001410, 0x000403);

            Color4 green = Color4.Hex(0x8dff2a);
            for (int i = 0; i < 24; i++) c.FillRect(Math.Round(i * w / 24), 0, 1, h, green, 0.16);
            for (int i = 0; i < 14; i++) c.FillRect(0, Math.Round(i * h / 14), w, 1, green, 0.16);

            var wave = new List<(double, double)>(91);
            for (int i = 0; i <= 90; i++)
            {
                double x = i / 90.0 * w * 0.88 + w * 0.06;
                double y = h * 0.42 + Math.Sin(i * 0.42) * h * 0.10 * (0.4 + rng.Next() * 0.8);
                wave.Add((x, y));
            }
            c.StrokePolyline(wave, Math.Max(1.5, w * 0.006), green);

            Line(c, "NET//SEC  ADVISORY", w * 0.06, h * 0.20, h * 0.105 * 0.72, GlyphPainter.Normal, TextAlign.Left, Color4.Hex(0xd8ffd0), w * 0.88);
            string[] lines =
            {
                "ICE INTEGRITY .......... 98.2%",
                "BLACKWALL DRIFT ........ NOMINAL",
                "SECTOR 7 CURFEW ........ 23:00",
                "UNREGISTERED IMPLANTS .. 14,882",
                "AIR QUALITY ............ HAZARD",
            };
            for (int i = 0; i < lines.Length; i++)
            {
                Line(c, lines[i], w * 0.06, h * 0.56 + i * h * 0.052, h * 0.048 * 0.72, GlyphPainter.Light, TextAlign.Left, green, w * 0.62);
            }
            Line(c, "88", w * 0.94, h * 0.60, h * 0.22 * 0.72, GlyphPainter.Heavy, TextAlign.Right, Color4.Hex(0xff2f52), w * 0.30);
            Ticker(c, "   TRAFFIC CONTROL ONLINE   DO NOT EXIT VEHICLE   ", green);
        }

        // ---------------------------------------------------------------- pieces

        /// <summary>One line of text, shrunk when it would be wider than <paramref name="maxWidth"/>.</summary>
        static void Line(Canvas2D c, string text, double x, double baseline, double cap, double weight, TextAlign align, Color4 colour, double maxWidth)
        {
            double width = GlyphPainter.Width(text, cap);
            if (width > maxWidth) cap *= maxWidth / width;
            GlyphPainter.Text(c, text, x, baseline, cap, weight, align, colour);
        }

        /// <summary>
        /// The ticker along the bottom: the text is repeated a whole number of times across the panel, and the size is chosen so the
        /// repeats fill it exactly, so the shader can scroll it with a wrapping coordinate and show no seam.
        /// </summary>
        static void Ticker(Canvas2D c, string text, Color4 colour)
        {
            double w = c.Width, h = c.Height;
            double bandH = h * TickerBand, y = h - bandH;
            c.FillRect(0, y, w, bandH, Black, 0.9);

            double unit = StrokeFont.Measure(text) + StrokeFont.Tracking;   // in cap heights, with the gap to the next repeat
            double natural = bandH * 0.40;
            int repeats = Math.Max(1, (int)Math.Floor(w / (unit * natural) + 0.5));
            double cap = Math.Min(bandH * 0.62, w / (repeats * unit));
            double period = w / repeats;
            for (int i = 0; i < repeats; i++) GlyphPainter.Text(c, text, i * period, y + bandH * 0.5 + cap / 2, cap, GlyphPainter.Light, TextAlign.Left, colour);
            c.FillRect(0, y - 1, w, 1, White, 0.22);
        }

        static void KanaColumn(Canvas2D c, double x, double y, double size, int n, Rng rng, Color4 colour)
        {
            for (int i = 0; i < n; i++)
            {
                IReadOnlyList<string> keys = rng.Next() > 0.5 ? StrokeFont.KanjiKeys : StrokeFont.KanaKeys;
                string key = keys[(int)(rng.Next() * keys.Count)];
                GlyphPainter.Symbol(c, key, x, y + i * size * 1.16, size, Math.Max(1.5, size * 0.10), colour);
            }
        }

        /// <summary>Append a quadratic curve from <paramref name="p0"/> (already in the path) through a control point to <paramref name="p2"/>.</summary>
        static void Quad(List<(double, double)> path, (double x, double y) p0, (double x, double y) control, (double x, double y) p2)
        {
            (double, double) c1 = (p0.x + 2.0 / 3 * (control.x - p0.x), p0.y + 2.0 / 3 * (control.y - p0.y));
            (double, double) c2 = (p2.x + 2.0 / 3 * (control.x - p2.x), p2.y + 2.0 / 3 * (control.y - p2.y));
            bool first = true;
            foreach ((double, double) p in Canvas2D.Bezier(p0, c1, c2, p2, 12))
            {
                if (first) { first = false; continue; }   // the start is already in the path
                path.Add(p);
            }
        }
    }
}
