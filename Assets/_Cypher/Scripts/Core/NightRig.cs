using System;

namespace Cypher.Core
{
    public static class Srgb
    {
        public static float ToLinear(double c)
        {
            if (c < 0 || c > 1) throw new ArgumentOutOfRangeException(nameof(c), c, "sRGB channel must be in 0..1");
            return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }

        /// <summary>0xRRGGBB (sRGB) to a linear-light colour.</summary>
        public static Rgb HexToLinear(uint hex) => new Rgb(
            ToLinear(((hex >> 16) & 0xff) / 255.0),
            ToLinear(((hex >> 8) & 0xff) / 255.0),
            ToLinear((hex & 0xff) / 255.0));
    }

    /// <summary>A light of the night rig: linear colour, the prototype's intensity, and the unit vector toward it.</summary>
    public readonly struct RigLight
    {
        public readonly Rgb Colour;
        public readonly double Intensity;
        public readonly Vec3 ToLight;

        public RigLight(Rgb colour, double intensity, Vec3 toLight)
        {
            Colour = colour;
            Intensity = intensity;
            ToLight = toLight;
        }

        /// <summary>
        /// Radiance in Unity's convention. three.js (physical lights) divides diffuse by pi where
        /// Unity does not, so the same look needs intensity / pi.
        /// </summary>
        public Rgb Radiance => Colour.Scale((float)(Intensity / Math.PI));
    }

    /// <summary>
    /// The prototype's night lighting rig (Sky._applyState) evaluated for a time of day and weather:
    /// moon key, hemisphere ambient, two unshadowed "light pollution" fills, a street uplight and fog.
    /// Weather is the prototype's rain and fog amounts, each 0..1. Positions and directions use the
    /// prototype's coordinates, like the rest of the port.
    /// </summary>
    public sealed class NightRigState
    {
        public double Night;
        public RigLight Moon;
        public RigLight FillWarm;
        public RigLight FillCool;
        public RigLight Uplight;
        public Rgb HemiSky;
        public Rgb HemiGround;
        public double HemiIntensity;
        public Rgb FogColour;
        public double FogDensity;
        public Rgb SkyZenith;
        public Rgb SkyHorizon;

        /// <summary>Hemisphere radiance (Unity convention): colour scaled by intensity / pi.</summary>
        public Rgb HemiSkyRadiance => HemiSky.Scale((float)(HemiIntensity / Math.PI));
        public Rgb HemiGroundRadiance => HemiGround.Scale((float)(HemiIntensity / Math.PI));
    }

    public static class NightRig
    {
        public const double DefaultTimeOfDay = 21.5;
        public const double DefaultRain = 0.45;
        public const double DefaultFog = 0.6;

        // Palette poles, 0xRRGGBB sRGB, from Sky.js NIGHT / DUSK.
        const uint NightZenith = 0x0a1226, DuskZenith = 0x1b2a52;
        const uint NightHorizon = 0x453055, DuskHorizon = 0x8a5a5c;
        const uint NightFog = 0x2a2740, DuskFog = 0x4a4560;
        const uint NightMoon = 0x9dc0ff, DuskMoon = 0xc6d4ff;
        const uint NightHemiSky = 0x415f9c, DuskHemiSky = 0x6d86bd;
        const uint NightHemiGround = 0x4a2440, DuskHemiGround = 0x6a4048;

        const uint FillWarmColour = 0xff4f8f, FillCoolColour = 0x2fd0ff, UplightColour = 0xff9ad2;

        static readonly Vec3 FillWarmPosition = new Vec3(60, 18, -40);
        static readonly Vec3 FillCoolPosition = new Vec3(-55, 14, 55);
        static readonly Vec3 UplightPosition = new Vec3(0, -30, 0);

        public static NightRigState Compute(double timeOfDay = DefaultTimeOfDay, double rain = DefaultRain, double fog = DefaultFog)
        {
            rain = Clamp(rain, 0, 1);
            fog = Clamp(fog, 0, 1);

            // Sun elevation on a 24 h clock (noon at 12). night: 1 = full night, 0 = daylight.
            double sunEl = Math.Sin((timeOfDay - 6) / 24 * Math.PI * 2);
            double night = Clamp(-sunEl * 2.6 + 0.55, 0, 1);
            double cloud = Clamp(0.24 + fog * 0.4 + rain * 0.42, 0, 0.95);

            // The moon rides opposite the sun.
            double moonEl = Clamp(-sunEl, 0.12, 1);
            double moonAz = timeOfDay / 24 * Math.PI * 2 + Math.PI * 0.35;
            double horizontal = Math.Sqrt(1 - moonEl * moonEl);
            Vec3 moonDir = Normalize(new Vec3(Math.Cos(moonAz) * horizontal, moonEl, Math.Sin(moonAz) * horizontal));

            return new NightRigState
            {
                Night = night,
                Moon = new RigLight(Mix(DuskMoon, NightMoon, night), (0.35 + night * 0.95) * (1 - cloud * 0.45), moonDir),
                FillWarm = new RigLight(Srgb.HexToLinear(FillWarmColour), 0.42 + night * 0.28, Normalize(FillWarmPosition)),
                FillCool = new RigLight(Srgb.HexToLinear(FillCoolColour), 0.34 + night * 0.22, Normalize(FillCoolPosition)),
                Uplight = new RigLight(Srgb.HexToLinear(UplightColour), (0.14 + rain * 0.16) * (0.4 + night * 0.7), Normalize(UplightPosition)),
                HemiSky = Mix(DuskHemiSky, NightHemiSky, night),
                HemiGround = Mix(DuskHemiGround, NightHemiGround, night),
                HemiIntensity = 1.05 + fog * 0.65 + rain * 0.2,
                FogColour = Mix(DuskFog, NightFog, night),
                FogDensity = 0.0042 + fog * 0.0055 + rain * 0.0016,
                SkyZenith = Mix(DuskZenith, NightZenith, night),
                SkyHorizon = Mix(DuskHorizon, NightHorizon, night),
            };
        }

        /// <summary>Blend two sRGB hex colours in linear space, as the prototype's mixHex does.</summary>
        static Rgb Mix(uint a, uint b, double t)
        {
            Rgb ca = Srgb.HexToLinear(a), cb = Srgb.HexToLinear(b);
            float k = (float)t;
            return new Rgb(ca.R + (cb.R - ca.R) * k, ca.G + (cb.G - ca.G) * k, ca.B + (cb.B - ca.B) * k);
        }

        static Vec3 Normalize(Vec3 v)
        {
            double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (len < 1e-12) throw new ArgumentException("cannot normalise a zero vector");
            return new Vec3(v.X / len, v.Y / len, v.Z / len);
        }

        static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));
    }
}
