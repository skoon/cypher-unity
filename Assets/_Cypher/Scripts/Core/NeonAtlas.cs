using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>The kind of sheet a sign uses, which decides which cells it can show.</summary>
    public enum SignGroup
    {
        /// <summary>Horizontal banners: a word, or a few kana.</summary>
        Word,

        /// <summary>Square plaques: a kanji-like block, sometimes with a kana, sometimes boxed.</summary>
        Kanji,

        /// <summary>Tall blade art: a word stacked letter by letter, or a column of kana.</summary>
        Vertical,

        /// <summary>Small pictograms.</summary>
        Icon,
    }

    /// <summary>One rectangle of the sign atlas, in pixels from the top-left of the picture.</summary>
    public sealed class AtlasCell
    {
        public string Name;
        public SignGroup Group;
        public int X, Y, W, H;

        public double Aspect => (double)W / H;

        /// <summary>The cell as a texture rectangle (u, v, width, height): v = 0 is the bottom row, the way Unity reads a texture.</summary>
        public (double u, double v, double du, double dv) Uv(int atlasSize) =>
            ((double)X / atlasSize, 1.0 - (double)(Y + H) / atlasSize, (double)W / atlasSize, (double)H / atlasSize);
    }

    /// <summary>Where each cell sits in the atlas, and which cells belong to which group.</summary>
    public sealed class NeonAtlasLayout
    {
        public int Size { get; }
        public IReadOnlyList<AtlasCell> Cells { get; }
        readonly Dictionary<string, AtlasCell> _byName = new Dictionary<string, AtlasCell>();
        readonly Dictionary<SignGroup, List<AtlasCell>> _groups = new Dictionary<SignGroup, List<AtlasCell>>();

        public NeonAtlasLayout(int size, IReadOnlyList<AtlasCell> cells)
        {
            Size = size;
            Cells = cells;
            foreach (SignGroup g in Enum.GetValues(typeof(SignGroup))) _groups[g] = new List<AtlasCell>();
            foreach (AtlasCell c in cells)
            {
                _byName.Add(c.Name, c);
                _groups[c.Group].Add(c);
            }
        }

        public AtlasCell this[string name]
        {
            get
            {
                if (!_byName.TryGetValue(name, out AtlasCell c)) throw new ArgumentException($"There is no atlas cell called '{name}'.", nameof(name));
                return c;
            }
        }

        public IReadOnlyList<AtlasCell> Group(SignGroup group) => _groups[group];
    }

    /// <summary>
    /// The sheet of sign lettering: horizontal word banners, kanji plaques, vertical blade signs and icons, drawn from the stroke font and given
    /// the glow of a neon tube (a wide dim halo, a mid glow, the hard core), in one single-channel 2048 x 2048 texture the sign shader samples.
    /// The layout (<see cref="Layout"/>) is a pure and cheap function of the cell sizes, so the planner can name cells without the pixels; the
    /// pixels (<see cref="Render"/>) take longer and are made on a worker. Ported from Neon._buildAtlases, with the stroke font where the
    /// prototype typeset text in system fonts.
    /// </summary>
    public static class NeonAtlas
    {
        public const int Size = 2048;
        public const int Pad = 8;
        public const int WordW = 384, WordH = 112;
        public const int KanjiSize = 176;
        public const int VerticalW = 96, VerticalH = 384;
        public const int IconSize = 80;
        public const uint Seed = 4711;

        public enum WordStyle
        {
            Heavy,
            Sans,
            Mono,
        }

        /// <summary>The words that appear on horizontal signs.</summary>
        public static readonly IReadOnlyList<(string Text, WordStyle Style)> Words = new[]
        {
            ("RAMEN", WordStyle.Heavy), ("SUSHI", WordStyle.Heavy), ("NOODLE BAR", WordStyle.Sans),
            ("CYBERWARE", WordStyle.Sans), ("PAWN", WordStyle.Heavy), ("OPEN 24H", WordStyle.Mono),
            ("CLINIC", WordStyle.Sans), ("HOTEL", WordStyle.Heavy), ("LIQUOR", WordStyle.Sans),
            ("PACHINKO", WordStyle.Sans), ("MASSAGE", WordStyle.Sans), ("TATTOO", WordStyle.Heavy),
            ("ARMS", WordStyle.Heavy), ("CHIP SHOP", WordStyle.Sans), ("NEURAL", WordStyle.Mono),
            ("KARAOKE", WordStyle.Sans), ("SYNTH", WordStyle.Heavy), ("BIOTECH", WordStyle.Mono),
            ("DRINKS", WordStyle.Sans), ("ROOMS", WordStyle.Sans), ("FUEL CELL", WordStyle.Mono),
            ("DATA HAUS", WordStyle.Sans), ("XXX", WordStyle.Heavy), ("GIRLS", WordStyle.Heavy),
        };

        /// <summary>The words that run down a blade sign.</summary>
        public static readonly IReadOnlyList<string> VerticalWords = new[] { "HOTEL", "BAR", "RAMEN", "SAUNA", "LIVE", "CLUB", "PAWN", "CLINIC" };

        public const int KanaBands = 4, KanjiPlaques = 14, KanaColumns = 8;

        // ---------------------------------------------------------------- layout

        public static NeonAtlasLayout Layout()
        {
            var cells = new List<AtlasCell>();
            var shelf = new Shelf(Size);

            void Add(string name, SignGroup group, int w, int h)
            {
                (int x, int y) = shelf.Add(w, h);
                cells.Add(new AtlasCell { Name = name, Group = group, X = x, Y = y, W = w, H = h });
            }

            for (int i = 0; i < Words.Count; i++) Add($"word{i}", SignGroup.Word, WordW, WordH);
            for (int i = 0; i < KanaBands; i++) Add($"kanaband{i}", SignGroup.Word, WordW, WordH);
            for (int i = 0; i < KanjiPlaques; i++) Add($"kanji{i}", SignGroup.Kanji, KanjiSize, KanjiSize);
            for (int i = 0; i < VerticalWords.Count; i++) Add($"vword{i}", SignGroup.Vertical, VerticalW, VerticalH);
            for (int i = 0; i < KanaColumns; i++) Add($"vkana{i}", SignGroup.Vertical, VerticalW, VerticalH);
            for (int i = 0; i < StrokeFont.IconKeys.Count; i++) Add($"icon{i}", SignGroup.Icon, IconSize, IconSize);
            return new NeonAtlasLayout(Size, cells);
        }

        /// <summary>The prototype's shelf packer: cells go left to right in rows, each with <see cref="Pad"/> of room around it.</summary>
        sealed class Shelf
        {
            readonly int _size;
            int _x, _y, _rowHeight;

            public Shelf(int size)
            {
                _size = size;
            }

            public (int x, int y) Add(int w, int h)
            {
                int aw = w + Pad * 2, ah = h + Pad * 2;
                if (_x + aw > _size)
                {
                    _x = 0;
                    _y += _rowHeight;
                    _rowHeight = 0;
                }
                if (_y + ah > _size) throw new InvalidOperationException("The neon atlas is full.");
                var at = (_x + Pad, _y + Pad);
                _x += aw;
                if (ah > _rowHeight) _rowHeight = ah;
                return at;
            }
        }

        // ---------------------------------------------------------------- pixels

        /// <summary>
        /// The atlas as single-channel bytes, <see cref="Size"/> squared, with the first row at the <i>bottom</i> of the picture (Unity's order).
        /// The same bytes every time.
        /// </summary>
        public static byte[] Render(NeonAtlasLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var atlas = new float[Size * Size];
            var rng = new Rng(Seed);
            var white = new Color4(1, 1, 1);

            for (int i = 0; i < Words.Count; i++)
            {
                bool framed = rng.Next() > 0.62;
                AtlasCell cell = layout[$"word{i}"];
                (string text, WordStyle style) = Words[i];
                Composite(atlas, cell, DrawWord(cell, text, style, framed, white));
            }

            for (int i = 0; i < KanaBands; i++)
            {
                int n = 3 + (int)(rng.Next() * 2);
                var keys = new List<string>();
                for (int k = 0; k < n; k++) keys.Add(StrokeFont.KanaKeys[(int)(rng.Next() * StrokeFont.KanaKeys.Count)]);
                AtlasCell cell = layout[$"kanaband{i}"];
                var c = new Canvas2D(cell.W, cell.H);
                double gw = Math.Min(cell.H * 0.78, cell.W * 0.86 / n);
                double x0 = (cell.W - gw * n) / 2;
                for (int k = 0; k < n; k++) GlyphPainter.Symbol(c, keys[k], x0 + k * gw, (cell.H - gw) / 2, gw, Math.Max(2.6, gw * 0.085), white);
                Composite(atlas, cell, c);
            }

            for (int i = 0; i < KanjiPlaques; i++)
            {
                string a = StrokeFont.KanjiKeys[(int)(rng.Next() * StrokeFont.KanjiKeys.Count)];
                string b = rng.Next() > 0.45 ? StrokeFont.KanaKeys[(int)(rng.Next() * StrokeFont.KanaKeys.Count)] : null;
                bool box = rng.Next() > 0.5;
                AtlasCell cell = layout[$"kanji{i}"];
                var c = new Canvas2D(cell.W, cell.H);
                if (box) StrokeRect(c, 10, 10, cell.W - 20, cell.H - 20, 3.4, white);
                if (b != null)
                {
                    double g = cell.W * 0.44;
                    GlyphPainter.Symbol(c, a, cell.W * 0.06, cell.H * 0.06, g, g * 0.09, white);
                    GlyphPainter.Symbol(c, b, cell.W * 0.50, cell.H * 0.50, g, g * 0.09, white);
                }
                else
                {
                    double g = cell.W * 0.66;
                    GlyphPainter.Symbol(c, a, (cell.W - g) / 2, (cell.H - g) / 2, g, g * 0.085, white);
                }
                Composite(atlas, cell, c);
            }

            for (int i = 0; i < VerticalWords.Count; i++)
            {
                AtlasCell cell = layout[$"vword{i}"];
                Composite(atlas, cell, DrawVerticalWord(cell, VerticalWords[i], white));
            }

            for (int i = 0; i < KanaColumns; i++)
            {
                int n = 4 + (int)(rng.Next() * 3);
                var keys = new List<string>();
                for (int k = 0; k < n; k++)
                {
                    IReadOnlyList<string> set = rng.Next() > 0.5 ? StrokeFont.KanjiKeys : StrokeFont.KanaKeys;
                    keys.Add(set[(int)(rng.Next() * set.Count)]);
                }
                AtlasCell cell = layout[$"vkana{i}"];
                var c = new Canvas2D(cell.W, cell.H);
                double step = cell.H / (n + 0.4);
                double g = Math.Min(cell.W * 0.72, step * 0.86);
                for (int k = 0; k < n; k++) GlyphPainter.Symbol(c, keys[k], (cell.W - g) / 2, step * (k + 0.35), g, Math.Max(2.6, g * 0.09), white);
                Composite(atlas, cell, c);
            }

            for (int i = 0; i < StrokeFont.IconKeys.Count; i++)
            {
                AtlasCell cell = layout[$"icon{i}"];
                var c = new Canvas2D(cell.W, cell.H);
                GlyphPainter.SymbolInRect(c, StrokeFont.IconKeys[i], 0, 0, cell.W, cell.H, 6, white);
                Composite(atlas, cell, c, halo: 7, mid: 2.6);
            }

            // bytes, flipped so that row 0 is the bottom of the picture
            var bytes = new byte[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                int dst = (Size - 1 - y) * Size;
                for (int x = 0; x < Size; x++) bytes[dst + x] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(atlas[y * Size + x] * 255f)));
            }
            return bytes;
        }

        static Canvas2D DrawWord(AtlasCell cell, string text, WordStyle style, bool framed, Color4 white)
        {
            var c = new Canvas2D(cell.W, cell.H);
            double weight = style == WordStyle.Heavy ? GlyphPainter.Heavy : style == WordStyle.Mono ? GlyphPainter.Light : GlyphPainter.Normal;
            double cap = cell.H * (framed ? 0.50 : 0.60) * 0.72;   // the prototype's font size, as a cap height
            double maxW = cell.W * (framed ? 0.76 : 0.88);
            double width = GlyphPainter.Width(text, cap);
            if (width > maxW) cap *= maxW / width;
            GlyphPainter.Text(c, text, cell.W / 2.0, cell.H / 2.0 + cap / 2 + 1, cap, weight, TextAlign.Centre, white);
            if (framed) StrokeRect(c, 9, 9, cell.W - 18, cell.H - 18, 3.2, white);
            return c;
        }

        static Canvas2D DrawVerticalWord(AtlasCell cell, string text, Color4 white)
        {
            var c = new Canvas2D(cell.W, cell.H);
            double step = cell.H / (text.Length + 0.5);
            double cap = Math.Min(Math.Min(cell.W * 0.74, step * 0.90) * 0.72, cell.W * 0.74 / 0.88);   // the widest letter still fits the blade
            for (int k = 0; k < text.Length; k++)
            {
                double mid = step * (k + 0.75);
                GlyphPainter.Text(c, text[k].ToString(), cell.W / 2.0, mid + cap / 2, cap, GlyphPainter.Normal, TextAlign.Centre, white);
            }
            StrokeRect(c, 7, 7, cell.W - 14, cell.H - 14, 2.6, white);
            return c;
        }

        static void StrokeRect(Canvas2D c, double x, double y, double w, double h, double lineWidth, Color4 colour) =>
            c.StrokePolyline(new List<(double, double)> { (x, y), (x + w, y), (x + w, y + h), (x, y + h), (x, y) }, lineWidth, colour);

        /// <summary>
        /// Add a cell's line art to the atlas three times: a wide dim halo, a mid glow and the hard core, clipped to the cell and its padding
        /// (the prototype did the same with canvas blur filters and additive compositing).
        /// </summary>
        static void Composite(float[] atlas, AtlasCell cell, Canvas2D art, double halo = 13, double mid = 4.5, double haloAlpha = 0.55, double midAlpha = 0.6)
        {
            int margin = (int)Math.Ceiling(halo * 2.5);
            int bw = cell.W + margin * 2, bh = cell.H + margin * 2;
            float[] core = art.ToAlpha();

            float[] Padded(double sigma)
            {
                var buffer = new float[bw * bh];
                for (int y = 0; y < cell.H; y++) Array.Copy(core, y * cell.W, buffer, (y + margin) * bw + margin, cell.W);
                AlphaBlur.Apply(buffer, bw, bh, sigma);
                return buffer;
            }

            float[] haloBuffer = Padded(halo), midBuffer = Padded(mid);
            for (int y = -Pad; y < cell.H + Pad; y++)
            {
                for (int x = -Pad; x < cell.W + Pad; x++)
                {
                    int ax = cell.X + x, ay = cell.Y + y;
                    if (ax < 0 || ay < 0 || ax >= Size || ay >= Size) continue;
                    int bi = (y + margin) * bw + x + margin;
                    float v = haloBuffer[bi] * (float)haloAlpha + midBuffer[bi] * (float)midAlpha;
                    if (x >= 0 && y >= 0 && x < cell.W && y < cell.H) v += core[y * cell.W + x];
                    float at = atlas[ay * Size + ax] + v;
                    atlas[ay * Size + ax] = at > 1f ? 1f : at;
                }
            }
        }
    }
}
