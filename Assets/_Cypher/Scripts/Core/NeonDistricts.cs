using System;

namespace Cypher.Core
{
    /// <summary>
    /// A neon colour district. The prototype clusters hue by district so the city does not read as
    /// rainbow soup: each has a dominant colour and weighted accents.
    /// </summary>
    public sealed class NeonDistrict
    {
        public string Name { get; }
        public Rgb[] Colours { get; }
        public double[] Weights { get; }
        public double TotalWeight { get; }

        public NeonDistrict(string name, params (uint Hex, double Weight)[] hues)
        {
            if (hues == null || hues.Length == 0) throw new ArgumentException("a district needs at least one hue", nameof(hues));
            Name = name;
            Colours = new Rgb[hues.Length];
            Weights = new double[hues.Length];
            for (int i = 0; i < hues.Length; i++)
            {
                if (!(hues[i].Weight > 0)) throw new ArgumentException("hue weights must be positive", nameof(hues));
                Colours[i] = Srgb.HexToLinear(hues[i].Hex);
                Weights[i] = hues[i].Weight;
                TotalWeight += hues[i].Weight;
            }
        }

        /// <summary>Weighted random hue; consumes exactly one draw.</summary>
        public Rgb PickHue(Rng rng)
        {
            double r = rng.Next() * TotalWeight;
            for (int i = 0; i < Weights.Length; i++)
            {
                r -= Weights[i];
                if (r <= 0) return Colours[i];
            }
            return Colours[0];
        }
    }

    /// <summary>The prototype's four districts and the hash that assigns a world position to one (Neon.js).</summary>
    public static class NeonDistricts
    {
        /// <summary>Hot pink over cold cyan.</summary>
        public static readonly NeonDistrict Kabuki = new NeonDistrict("kabuki",
            (0xff2a86, 5), (0x18e4ff, 3), (0xffffff, 1), (0xffa32e, 1.2), (0x9b5cff, 1.0));

        /// <summary>Clinical cyan, white, electric violet.</summary>
        public static readonly NeonDistrict Corpo = new NeonDistrict("corpo",
            (0x2ad8ff, 5), (0xd8f4ff, 2), (0x7a5cff, 2), (0x36ffd0, 1.2), (0xff3c6e, 0.6));

        /// <summary>Amber, sodium, toxic green.</summary>
        public static readonly NeonDistrict Market = new NeonDistrict("market",
            (0xff9a12, 5), (0xffd24a, 2.2), (0x8dff2a, 1.8), (0xff2f52, 1.4), (0x22e0ff, 1.0));

        /// <summary>Deep red, magenta, a bruise of violet.</summary>
        public static readonly NeonDistrict Redline = new NeonDistrict("redline",
            (0xff1440, 5), (0xff3fbc, 3), (0x8a2bff, 1.6), (0x18e4ff, 0.8), (0xffb347, 0.7));

        public static readonly NeonDistrict[] All = { Kabuki, Corpo, Market, Redline };

        /// <summary>Size in metres of one district tile.</summary>
        public const double TileSize = 118;

        public static NeonDistrict At(double x, double z)
        {
            unchecked
            {
                int gx = (int)Math.Floor((x + 6000) / TileSize);
                int gz = (int)Math.Floor((z + 6000) / TileSize);
                // Math.imul semantics: 32-bit wrapping multiply, then XOR with the golden-ratio constant.
                uint h = (uint)((gx * 374761393) ^ (gz * 668265263) ^ (int)0x9e3779b9);
                return All[(int)(h % (uint)All.Length)];
            }
        }
    }
}
