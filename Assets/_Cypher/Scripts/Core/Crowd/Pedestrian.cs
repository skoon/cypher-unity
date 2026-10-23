using System;

namespace Cypher.Core
{
    public enum PedState
    {
        Idle = 0,
        Walk = 1,
        Hurry = 2,
    }

    /// <summary>What one pedestrian looks like. Fixed for the life of the slot.</summary>
    public readonly struct PedestrianAppearance
    {
        /// <summary>Height as a multiple of the 1.72 m rig.</summary>
        public readonly double Scale;

        /// <summary>Shoulder and hip width as a multiple of the rig's.</summary>
        public readonly double Build;

        /// <summary>A long coat that hangs below the hips (the key silhouette split).</summary>
        public readonly bool LongCoat;

        public readonly bool Umbrella, Hood, Pack, Trim;

        /// <summary>sRGB hex colours.</summary>
        public readonly uint Skin, Coat, Pants, TrimColour, UmbrellaColour;

        /// <summary>How hot the trim glows (a multiplier on its colour).</summary>
        public readonly double TrimPower;

        public PedestrianAppearance(double scale, double build, bool longCoat, bool umbrella, bool hood, bool pack, bool trim, uint skin, uint coat, uint pants,
            uint trimColour, double trimPower, uint umbrellaColour)
        {
            Scale = scale; Build = build; LongCoat = longCoat; Umbrella = umbrella; Hood = hood; Pack = pack; Trim = trim;
            Skin = skin; Coat = coat; Pants = pants; TrimColour = trimColour; TrimPower = trimPower; UmbrellaColour = umbrellaColour;
        }
    }

    /// <summary>One person: where they are, where they are going, and what they look like. The simulation owns the state; the renderer reads it.</summary>
    public sealed class Pedestrian
    {
        public int Index;

        /// <summary>Position now, and at the previous step (for interpolating between steps).</summary>
        public double X, Z, Y, PrevX, PrevZ, PrevY;

        public double Heading, PrevHeading;

        /// <summary>Gait phase in radians, 0 to 2 pi.</summary>
        public double Phase, PrevPhase;

        public double Speed;

        /// <summary>Walking speed when unhurried, metres a second.</summary>
        public double Cruise;

        /// <summary>Some people always hurry between stops.</summary>
        public bool Hurried;

        public int Node, PrevNode;

        /// <summary>The point being walked to: the next node, offset sideways by <see cref="Lat"/>.</summary>
        public double Tx, Tz;

        /// <summary>Sideways offset from the line between nodes, so people do not file single-lane.</summary>
        public double Lat;

        public Crossing Crossing;
        public PedState State;
        public double Timer, WaitTimer;

        /// <summary>How far the head is turned, smoothed by the renderer.</summary>
        public double LookYaw;

        public double LookSeed;

        public PedestrianAppearance Appearance;
    }

    /// <summary>The palettes and the seeded stream that draws a pedestrian, in the prototype's order (Crowd._buildPeds).</summary>
    public static class PedestrianFactory
    {
        public static readonly uint[] Skin = { 0xd8ac8c, 0xb07d55, 0x7a5236, 0x4d3323, 0xe8c4a2, 0x93603c };

        public static readonly uint[] Coat =
        {
            0x1c2030, 0x14161c, 0x2b2434, 0x30201e, 0x1a2b2a, 0x232a38, 0x3a2030, 0x101820, 0x2e2a20, 0x1f1524,
        };

        public static readonly uint[] CoatAccent = { 0x2a4d6e, 0x6e2440, 0x1f5a54, 0x5c3a12 };
        public static readonly uint[] Pants = { 0x14161d, 0x1b1d26, 0x22201c, 0x101318, 0x252833 };
        public static readonly uint[] Trim = { 0x39f6ff, 0xff2f8e, 0xffb03a, 0x7bff5c, 0xb47bff, 0xff5a3c };

        /// <summary>
        /// Draw one pedestrian from <paramref name="rng"/>. The draws are consumed exactly as the prototype's object literal consumes them (and the hood
        /// draw is skipped when there is an umbrella), so a seed gives the same crowd.
        /// </summary>
        public static Pedestrian Create(Rng rng, int index)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            double scale = 0.92 + rng.Next() * 0.17;
            double build = 0.88 + rng.Next() * 0.30;
            bool longCoat = rng.Next() < 0.42;
            bool umbrella = rng.Next() < 0.26;
            bool hood = !umbrella && rng.Next() < 0.34;
            bool pack = rng.Next() < 0.24;
            bool trim = rng.Next() < 0.40;
            bool accent = rng.Next() < 0.18;

            var p = new Pedestrian { Index = index, PrevNode = -1 };
            p.Phase = rng.Next() * Math.PI * 2;
            p.Cruise = 1.12 + rng.Next() * 0.42;
            p.Hurried = rng.Next() < 0.22;
            p.Lat = (rng.Next() * 2 - 1) * 0.85;
            p.State = PedState.Walk;
            p.Timer = rng.Next() * 6;
            p.LookSeed = rng.Next() * 100;

            uint skin = Pick(Skin, rng);
            uint coat = accent ? Pick(CoatAccent, rng) : Pick(Coat, rng);
            uint pants = Pick(Pants, rng);
            uint trimColour = Pick(Trim, rng);
            double trimPower = 1.6 + rng.Next() * 2.6;
            uint umbrellaColour = rng.Next() < 0.2 ? 0x2a2f3cu : 0x0d0f14u;
            p.Appearance = new PedestrianAppearance(scale, build, longCoat, umbrella, hood, pack, trim, skin, coat, pants, trimColour, trimPower, umbrellaColour);
            return p;
        }

        static uint Pick(uint[] palette, Rng rng) => palette[(int)(rng.Next() * palette.Length)];
    }
}
