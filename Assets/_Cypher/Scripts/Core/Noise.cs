using System;

namespace Cypher.Core
{
    /// <summary>
    /// Classic 2D Perlin noise and fractal Brownian motion, ported from src/engine/Assets.js with the
    /// same permutation table (a Fisher-Yates shuffle driven by mulberry32 seed 9001), so surface
    /// textures come out of the same noise field as the web prototype.
    /// </summary>
    public static class Noise
    {
        const uint PermutationSeed = 9001;

        static readonly byte[] Perm = BuildPermutation();

        static byte[] BuildPermutation()
        {
            var rng = new Rng(PermutationSeed);
            var perm = new byte[256];
            for (int i = 0; i < 256; i++) perm[i] = (byte)i;
            for (int i = 255; i > 0; i--)
            {
                int j = (int)Math.Floor(rng.Next() * (i + 1));
                byte tmp = perm[i];
                perm[i] = perm[j];
                perm[j] = tmp;
            }
            var doubled = new byte[512];
            for (int i = 0; i < 512; i++) doubled[i] = perm[i & 255];
            return doubled;
        }

        static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

        static double Lerp(double a, double b, double t) => a + (b - a) * t;

        static double Grad(int hash, double x, double y)
        {
            switch (hash & 3)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                default: return -x - y;
            }
        }

        /// <summary>Perlin noise, output roughly in [-1, 1]; zero at lattice points.</summary>
        public static double Perlin2(double x, double y)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y);
            int xi = (int)fx & 255;
            int yi = (int)fy & 255;
            double xf = x - fx;
            double yf = y - fy;
            double u = Fade(xf);
            double v = Fade(yf);
            int aa = Perm[Perm[xi] + yi];
            int ab = Perm[Perm[xi] + yi + 1];
            int ba = Perm[Perm[xi + 1] + yi];
            int bb = Perm[Perm[xi + 1] + yi + 1];
            return Lerp(
                Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u),
                Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u),
                v);
        }

        /// <summary>Fractal sum of Perlin octaves, normalised by total amplitude. Roughly [-1, 1].</summary>
        public static double Fbm2(double x, double y, int octaves = 5, double lacunarity = 2.0, double gain = 0.5)
        {
            if (octaves < 1) throw new ArgumentOutOfRangeException(nameof(octaves), octaves, "need at least one octave");
            double amp = 1, freq = 1, sum = 0, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Perlin2(x * freq, y * freq);
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return sum / norm;
        }
    }
}
