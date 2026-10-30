using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>Where a place's name is in the names atlas, in texture coordinates (v up), and its width over its height.</summary>
    public readonly struct PlaceNameCell
    {
        public readonly string Key;
        public readonly double U0, V0, U1, V1, Aspect;

        public PlaceNameCell(string key, double u0, double v0, double u1, double v1, double aspect)
        {
            Key = key; U0 = u0; V0 = v0; U1 = u1; V1 = v1; Aspect = aspect;
        }
    }

    /// <summary>
    /// One place's marker as mesh data for the NeonAdd shader (see <c>NeonAdd.shader</c>): a soft tube of light standing up from the place and fading as it rises,
    /// the place's name in neon lettering on a panel at head height above the street, and a glow pool on the ground. Built in the marker's own space, which the
    /// game turns about the vertical to face the camera: +Z is towards the viewer, so the viewer's right is -X. <see cref="Weights"/> scale the tint per vertex.
    /// </summary>
    public sealed class PlaceMarkerMesh
    {
        public readonly List<(double X, double Y, double Z)> Positions = new List<(double, double, double)>();
        public readonly List<(double U, double V)> Uvs = new List<(double, double)>();
        /// <summary>The shader's TEXCOORD1: seed, flicker mode + 8 * shape, extra, gain.</summary>
        public readonly List<(double Seed, double ModeShape, double Extra, double Gain)> Signs = new List<(double, double, double, double)>();
        public readonly List<double> Weights = new List<double>();
        public readonly List<int> Indices = new List<int>();

        public int VertexCount => Positions.Count;

        internal int Vertex(double x, double y, double z, double u, double v, (double, double, double, double) sign, double weight)
        {
            Positions.Add((x, y, z));
            Uvs.Add((u, v));
            Signs.Add(sign);
            Weights.Add(weight);
            return Positions.Count - 1;
        }
    }

    /// <summary>
    /// The neon markers that stand at the named places (the prototype marks them only on its HUD; the world marker was the user's choice for the port): the names
    /// atlas, lettered with the city's stroke font, and each marker's mesh. Pure, so it is tested without Unity; the same pixels every time.
    /// </summary>
    public static class PlaceMarkerArt
    {
        public const int AtlasWidth = 1024, AtlasHeight = 1024, RowHeight = 80;
        public const double CapHeight = 34, Margin = 30;

        public const double BeamHeight = 22, BeamWidth = 0.9;
        public const double PlateY = 5.2, PlateHeight = 1.1, PlateMaxWidth = 12, PlateGap = 0.25;
        public const double GlowSize = 6;

        // the shader's shapes and flicker modes
        const int ShapeSoftTube = 1, ShapePanel = 2, ShapeGlow = 4;
        const int ModeHum = 0, ModeBreathe = 2;

        /// <summary>The fading of the beam: heights and the tint's weight there.</summary>
        static readonly (double Y, double Weight)[] BeamStops = { (0, 1), (6, 0.7), (13, 0.3), (BeamHeight, 0) };

        /// <summary>A place's name as its marker spells it.</summary>
        public static string Lettering(string name) => (name ?? throw new ArgumentNullException(nameof(name))).ToUpperInvariant();

        /// <summary>
        /// The names atlas: one row per place from the top, each name lit as the city's signs are (core, glow and halo), as single-channel bytes with the first row
        /// at the bottom of the picture (Unity's order); and where each name is.
        /// </summary>
        public static (byte[] Pixels, List<PlaceNameCell> Cells) Render(IReadOnlyList<(string Key, string Name)> places)
        {
            if (places == null) throw new ArgumentNullException(nameof(places));
            if (places.Count * RowHeight > AtlasHeight) throw new ArgumentException($"{places.Count} names do not fit the atlas ({AtlasHeight / RowHeight} rows).", nameof(places));
            var atlas = new float[AtlasWidth * AtlasHeight];
            var cells = new List<PlaceNameCell>();
            var white = new Color4(1, 1, 1);
            for (int i = 0; i < places.Count; i++)
            {
                string text = Lettering(places[i].Name);
                double cap = CapHeight;
                double width = GlyphPainter.Width(text, cap);
                double maxText = AtlasWidth - Margin * 2;
                if (width > maxText)
                {
                    cap *= maxText / width;
                    width = maxText;
                }
                int w = (int)Math.Min(AtlasWidth, Math.Ceiling(width + Margin * 2));
                int top = i * RowHeight;
                var art = new Canvas2D(w, RowHeight);
                GlyphPainter.Text(art, text, w / 2.0, RowHeight / 2.0 + cap / 2, cap, GlyphPainter.Normal, TextAlign.Centre, white);
                Composite(atlas, 0, top, art);
                cells.Add(new PlaceNameCell(places[i].Key, 0, 1 - (double)(top + RowHeight) / AtlasHeight, (double)w / AtlasWidth, 1 - (double)top / AtlasHeight, (double)w / RowHeight));
            }

            var bytes = new byte[AtlasWidth * AtlasHeight];
            for (int y = 0; y < AtlasHeight; y++)
            {
                int dst = (AtlasHeight - 1 - y) * AtlasWidth;
                for (int x = 0; x < AtlasWidth; x++) bytes[dst + x] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(atlas[y * AtlasWidth + x] * 255f)));
            }
            return (bytes, cells);
        }

        /// <summary>The core strokes plus a glow and a halo, as the city's lettering is lit (<see cref="NeonAtlas"/>), kept inside the cell.</summary>
        static void Composite(float[] atlas, int x0, int y0, Canvas2D art, double halo = 9, double mid = 3.2, double haloAlpha = 0.55, double midAlpha = 0.6)
        {
            int w = art.Width, h = art.Height;
            float[] core = art.ToAlpha();
            float[] Blurred(double sigma)
            {
                var b = (float[])core.Clone();
                AlphaBlur.Apply(b, w, h, sigma);
                return b;
            }
            float[] haloBuffer = Blurred(halo), midBuffer = Blurred(mid);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float v = core[i] + haloBuffer[i] * (float)haloAlpha + midBuffer[i] * (float)midAlpha;
                    int a = (y0 + y) * AtlasWidth + x0 + x;
                    atlas[a] = Math.Min(1f, atlas[a] + v);
                }
            }
        }

        /// <summary>A marker's mesh: the beam, the name panel (from its atlas cell) and the glow pool. <paramref name="seed"/> sets apart how each one breathes.</summary>
        public static PlaceMarkerMesh Build(PlaceNameCell name, double seed)
        {
            var m = new PlaceMarkerMesh();

            // the beam: a soft tube along U (up), across V; drawn from both sides, with a gap where the name is so it never crosses the lettering
            var beam = (seed, (double)(ModeBreathe + 8 * ShapeSoftTube), 0.0, 1.2);
            double hw = BeamWidth / 2;
            double gapLow = PlateY - PlateHeight / 2 - PlateGap, gapHigh = PlateY + PlateHeight / 2 + PlateGap;
            var stops = BeamStops.Where(s => s.Y < gapLow || s.Y > gapHigh).ToList();
            stops.Add((gapLow, BeamWeight(gapLow)));
            stops.Add((gapHigh, BeamWeight(gapHigh)));
            stops.Sort((a, b) => a.Y.CompareTo(b.Y));
            for (int s = 0; s + 1 < stops.Count; s++)
            {
                (double y0, double w0) = stops[s];
                (double y1, double w1) = stops[s + 1];
                if (y0 == gapLow && y1 == gapHigh) continue;
                double u0 = y0 / BeamHeight, u1 = y1 / BeamHeight;
                int a = m.Vertex(hw, y0, 0, u0, 0, beam, w0), b = m.Vertex(hw, y1, 0, u1, 0, beam, w1);
                int c = m.Vertex(-hw, y1, 0, u1, 1, beam, w1), d = m.Vertex(-hw, y0, 0, u0, 1, beam, w0);
                TwoSided(m, a, b, c, d);
            }

            // the name: the viewer reads it left to right, which is +X to -X here; front face only, so it is never seen mirrored
            double ph = PlateHeight, pw = Math.Min(PlateMaxWidth, name.Aspect * ph);
            if (pw < name.Aspect * ph) ph = pw / name.Aspect;
            var plate = (seed, (double)(ModeHum + 8 * ShapePanel), name.Aspect, 1.0);
            double py0 = PlateY - ph / 2, py1 = PlateY + ph / 2;
            int p0 = m.Vertex(pw / 2, py0, 0.05, name.U0, name.V0, plate, 1);
            int p1 = m.Vertex(pw / 2, py1, 0.05, name.U0, name.V1, plate, 1);
            int p2 = m.Vertex(-pw / 2, py1, 0.05, name.U1, name.V1, plate, 1);
            int p3 = m.Vertex(-pw / 2, py0, 0.05, name.U1, name.V0, plate, 1);
            m.Indices.AddRange(new[] { p0, p1, p2, p0, p2, p3 });

            // the pool on the ground, seen from above
            var glow = (seed, (double)(ModeHum + 8 * ShapeGlow), 0.0, 1.0);
            double g = GlowSize / 2;
            int g0 = m.Vertex(-g, 0.04, -g, 0, 0, glow, 0.6), g1 = m.Vertex(-g, 0.04, g, 0, 1, glow, 0.6);
            int g2 = m.Vertex(g, 0.04, g, 1, 1, glow, 0.6), g3 = m.Vertex(g, 0.04, -g, 1, 0, glow, 0.6);
            TwoSided(m, g0, g1, g2, g3);
            return m;
        }

        /// <summary>The beam's tint weight at a height, between its stops.</summary>
        public static double BeamWeight(double y)
        {
            for (int i = 0; i + 1 < BeamStops.Length; i++)
            {
                (double y0, double w0) = BeamStops[i];
                (double y1, double w1) = BeamStops[i + 1];
                if (y <= y1) return w0 + (w1 - w0) * Math.Max(0, (y - y0) / (y1 - y0));
            }
            return 0;
        }

        static void TwoSided(PlaceMarkerMesh m, int a, int b, int c, int d) => m.Indices.AddRange(new[] { a, b, c, a, c, d, a, c, b, a, d, c });

        public enum Tint { Place, Job, Tracked }

        /// <summary>How a place's marker is lit: amber when the tracked job's next step is there, magenta when another job's is, cyan otherwise.</summary>
        public static Tint TintFor(string key, IEnumerable<JsonObject> jobMarkers)
        {
            var here = jobMarkers.Where(j => j.TryGetValue("key", out object k) && k as string == key).ToList();
            if (here.Any(j => j["tracked"] is bool t && t)) return Tint.Tracked;
            return here.Count > 0 ? Tint.Job : Tint.Place;
        }
    }
}
