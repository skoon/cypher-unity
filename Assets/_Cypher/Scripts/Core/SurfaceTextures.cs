using System;

namespace Cypher.Core
{
    /// <summary>
    /// Procedural surface textures ported from City._buildMaterials: wet asphalt, sidewalk paving and
    /// concrete, plus the roughness fields that give the road its puddles. Everything is RGBA32 bytes.
    /// Albedo textures are sRGB; the smoothness maps are linear data (smoothness in R, G and B).
    ///
    /// The prototype generates each texture at a fixed size and tiles it by a fixed repeat count. These
    /// generators take any size and scale their pixel coordinates by (reference size / size), so the
    /// pattern covers the same UV area whatever resolution is chosen.
    /// </summary>
    public static class SurfaceTextures
    {
        public const int AsphaltReferenceSize = 1024;
        public const int PavingReferenceSize = 512;
        public const int ConcreteReferenceSize = 512;
        public const int WetRoughnessReferenceSize = 512;
        public const int ConcreteRoughnessReferenceSize = 256;

        const uint AsphaltCrackSeed = 311;
        const uint ConcreteStainSeed = 77;

        public static byte[] Asphalt(int size)
        {
            double k = Scale(AsphaltReferenceSize, size);
            var px = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double rx = x * k, ry = y * k;
                    double n = Noise.Fbm2(rx / 6, ry / 6, 4) * 0.5 + 0.5;
                    double patch = Noise.Fbm2(rx / 80, ry / 80, 3) * 0.5 + 0.5;
                    double v = 26 + n * 24 + patch * 20;
                    Put(px, (y * size + x) * 4, v, v * 1.03, v * 1.16);
                }
            }

            // Cracks: 60 random-walk polylines, 1-3 px wide, 50% black.
            var canvas = new Canvas(px, size, k);
            var rng = new Rng(AsphaltCrackSeed);
            for (int c = 0; c < 60; c++)
            {
                double width = 1 + rng.Next() * 2;
                double x = rng.Next() * AsphaltReferenceSize, y = rng.Next() * AsphaltReferenceSize;
                for (int seg = 0; seg < 5; seg++)
                {
                    double nx = x + (rng.Next() - 0.5) * 90, ny = y + (rng.Next() - 0.5) * 90;
                    canvas.Line(x, y, nx, ny, width, 0, 0, 0, 0.5);
                    x = nx;
                    y = ny;
                }
            }
            return px;
        }

        public static byte[] Paving(int size)
        {
            double k = Scale(PavingReferenceSize, size);
            var px = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double rx = x * k, ry = y * k;
                    double n = Noise.Fbm2(rx / 16, ry / 16, 4) * 0.5 + 0.5;
                    double stain = Math.Max(0, Noise.Fbm2(rx / 55, ry / 55, 3));
                    double v = 74 + n * 40 - stain * 30;
                    Put(px, (y * size + x) * 4, v * 0.98, v, v * 1.08);
                }
            }

            // Slab joints: a 4 x 4 grid of 3 px lines at 55% black.
            var canvas = new Canvas(px, size, k);
            const int slabs = 4;
            for (int i = 0; i <= slabs; i++)
            {
                double p = (double)i * PavingReferenceSize / slabs;
                canvas.Line(p, 0, p, PavingReferenceSize, 3, 0, 0, 0, 0.55);
                canvas.Line(0, p, PavingReferenceSize, p, 3, 0, 0, 0, 0.55);
            }
            return px;
        }

        public static byte[] Concrete(int size)
        {
            double k = Scale(ConcreteReferenceSize, size);
            var px = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double rx = x * k, ry = y * k;
                    double n = Noise.Fbm2(rx / 24, ry / 24, 5) * 0.5 + 0.5;
                    double fine = Noise.Fbm2(rx / 3.5, ry / 3.5, 2) * 0.5 + 0.5;
                    double streak = Noise.Fbm2(rx / 5, ry / 160, 3) * 0.5 + 0.5;
                    double v = 108 + n * 54 + fine * 26 - streak * 34;
                    Put(px, (y * size + x) * 4, v * 0.97, v, v * 1.06);
                }
            }

            // Precast panel seams every 128 px, then a scatter of stains and patch repairs.
            var canvas = new Canvas(px, size, k);
            for (int i = 0; i <= ConcreteReferenceSize; i += 128)
            {
                canvas.Line(i, 0, i, ConcreteReferenceSize, 2, 0, 0, 0, 0.40);
                canvas.Line(0, i, ConcreteReferenceSize, i, 2, 0, 0, 0, 0.40);
            }
            var rng = new Rng(ConcreteStainSeed);
            for (int s = 0; s < 90; s++)
            {
                double alpha = 0.05 + rng.Next() * 0.16;
                bool black = rng.Next() > 0.5;
                double w = 8 + rng.Next() * 90;
                double x = rng.Next() * ConcreteReferenceSize, y = rng.Next() * ConcreteReferenceSize;
                double h = w * (0.15 + rng.Next() * 1.4);
                if (black) canvas.Rect(x, y, w, h, 0, 0, 0, alpha);
                else canvas.Rect(x, y, w, h, 0x5a, 0x53, 0x48, alpha);
            }
            return px;
        }

        /// <summary>
        /// Roughness (0..255) of wet asphalt: dry grain broken by smooth puddles. One byte per pixel,
        /// the prototype's "asphalt-rough" data texture. Turn it into a material map with
        /// <see cref="SmoothnessFromRoughness"/>.
        /// </summary>
        public static byte[] WetRoughness(int size)
        {
            double k = Scale(WetRoughnessReferenceSize, size);
            var rough = new byte[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double rx = x * k, ry = y * k;
                    double puddle = Noise.Fbm2(rx / 50, ry / 50, 4) * 0.5 + 0.5;
                    double grain = Noise.Fbm2(rx / 5, ry / 5, 3) * 0.5 + 0.5;
                    double wet = Math.Pow(Math.Max(0, puddle - 0.42) / 0.58, 0.65);
                    double r = (70 + grain * 95) + (8 - (70 + grain * 95)) * wet;
                    rough[y * size + x] = Trunc8(r);
                }
            }
            return rough;
        }

        /// <summary>Roughness (0..255) of the concrete, the prototype's "conc-rough" texture.</summary>
        public static byte[] ConcreteRoughness(int size)
        {
            double k = Scale(ConcreteRoughnessReferenceSize, size);
            var rough = new byte[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double n = Noise.Fbm2(x * k / 18, y * k / 18, 4) * 0.5 + 0.5;
                    rough[y * size + x] = Trunc8(165 + n * 78);
                }
            }
            return rough;
        }

        /// <summary>
        /// RGBA smoothness map (linear data, value in R, G and B) from a roughness field. The prototype's
        /// materials multiply the map by a base roughness (0.94 concrete, 0.9 paving, 1.0 road), which is
        /// baked in here because a Unity smoothness map cannot be rescaled that way at runtime.
        /// </summary>
        public static byte[] SmoothnessFromRoughness(byte[] roughness, double roughnessScale)
        {
            if (roughness == null) throw new ArgumentNullException(nameof(roughness));
            if (roughnessScale < 0 || roughnessScale > 1) throw new ArgumentOutOfRangeException(nameof(roughnessScale), roughnessScale, "scale must be 0..1");
            var px = new byte[roughness.Length * 4];
            for (int i = 0; i < roughness.Length; i++)
            {
                byte s = Clamp8(255 * (1 - roughnessScale * roughness[i] / 255.0));
                px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = s;
                px[i * 4 + 3] = 255;
            }
            return px;
        }

        static double Scale(int referenceSize, int size)
        {
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size), size, "size must be positive");
            return (double)referenceSize / size;
        }

        /// <summary>Canvas pixel writes round to nearest.</summary>
        static byte Clamp8(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v)));

        /// <summary>The prototype's data textures store through a Uint8Array, which truncates.</summary>
        static byte Trunc8(double v) => (byte)Math.Max(0, Math.Min(255, Math.Floor(v)));

        static void Put(byte[] px, int i, double r, double g, double b)
        {
            px[i] = Clamp8(r);
            px[i + 1] = Clamp8(g);
            px[i + 2] = Clamp8(b);
            px[i + 3] = 255;
        }

        /// <summary>
        /// Minimal alpha-blending 2D painter over an RGBA buffer, in reference-pixel coordinates (the
        /// prototype's canvas units); k converts them to buffer pixels.
        /// </summary>
        sealed class Canvas
        {
            readonly byte[] _px;
            readonly int _size;
            readonly double _invK;

            public Canvas(byte[] px, int size, double k)
            {
                _px = px;
                _size = size;
                _invK = 1 / k;
            }

            public void Line(double x0, double y0, double x1, double y1, double width, double r, double g, double b, double alpha)
            {
                x0 *= _invK; y0 *= _invK; x1 *= _invK; y1 *= _invK;
                double half = Math.Max(0.5, width * _invK / 2);
                int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - half - 1));
                int maxX = Math.Min(_size - 1, (int)Math.Ceiling(Math.Max(x0, x1) + half + 1));
                int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - half - 1));
                int maxY = Math.Min(_size - 1, (int)Math.Ceiling(Math.Max(y0, y1) + half + 1));
                double dx = x1 - x0, dy = y1 - y0;
                double len2 = dx * dx + dy * dy;
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        double px = x + 0.5, py = y + 0.5;
                        double t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((px - x0) * dx + (py - y0) * dy) / len2));
                        double ex = px - (x0 + dx * t), ey = py - (y0 + dy * t);
                        double dist = Math.Sqrt(ex * ex + ey * ey);
                        double coverage = Math.Max(0, Math.Min(1, half + 0.5 - dist));
                        if (coverage > 0) Blend(x, y, r, g, b, alpha * coverage);
                    }
                }
            }

            public void Rect(double x, double y, double w, double h, double r, double g, double b, double alpha)
            {
                int x0 = Math.Max(0, (int)Math.Round(x * _invK)), x1 = Math.Min(_size, (int)Math.Round((x + w) * _invK));
                int y0 = Math.Max(0, (int)Math.Round(y * _invK)), y1 = Math.Min(_size, (int)Math.Round((y + h) * _invK));
                for (int yy = y0; yy < y1; yy++)
                {
                    for (int xx = x0; xx < x1; xx++) Blend(xx, yy, r, g, b, alpha);
                }
            }

            void Blend(int x, int y, double r, double g, double b, double alpha)
            {
                int i = (y * _size + x) * 4;
                _px[i] = Clamp8(_px[i] * (1 - alpha) + r * alpha);
                _px[i + 1] = Clamp8(_px[i + 1] * (1 - alpha) + g * alpha);
                _px[i + 2] = Clamp8(_px[i + 2] * (1 - alpha) + b * alpha);
            }
        }
    }
}
