using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class NightRigTests
    {
        const double Tol = 1e-9;

        // Goldens come from a Node re-implementation of Sky._applyState (see the 2026-10-20 session).

        [Test]
        public void DefaultsMatchThePrototypesBootState()
        {
            NightRigState r = NightRig.Compute();
            Assert.AreEqual(1.0, r.Night, Tol);
            Assert.AreEqual(0.5494591075030639, r.Moon.ToLight.X, Tol);
            Assert.AreEqual(0.7933533402912349, r.Moon.ToLight.Y, Tol);
            Assert.AreEqual(0.2620785504971293, r.Moon.ToLight.Z, Tol);
            Assert.AreEqual(0.908635, r.Moon.Intensity, Tol);
            Assert.AreEqual(0.00822, r.FogDensity, Tol);
            Assert.AreEqual(1.53, r.HemiIntensity, Tol);
            Assert.AreEqual(0.7, r.FillWarm.Intensity, Tol);
            Assert.AreEqual(0.56, r.FillCool.Intensity, Tol);
            Assert.AreEqual(0.2332, r.Uplight.Intensity, Tol);
        }

        [Test]
        public void DuskAtSixPmBlendsHalfwayAndKeepsAFloorOnTheMoon()
        {
            NightRigState r = NightRig.Compute(18, 0, 0);
            Assert.AreEqual(0.55, r.Night, 1e-9);
            Assert.AreEqual(0.8845680145301043, r.Moon.ToLight.X, Tol);
            Assert.AreEqual(0.12, r.Moon.ToLight.Y, Tol, "moon elevation never drops below 0.12");
            Assert.AreEqual(-0.450709915211846, r.Moon.ToLight.Z, Tol);
            Assert.AreEqual(0.77827, r.Moon.Intensity, Tol);
            Assert.AreEqual(0.0042, r.FogDensity, Tol);
            Assert.AreEqual(0.574, r.FillWarm.Intensity, Tol);
            Assert.AreEqual(0.461, r.FillCool.Intensity, Tol);
            Assert.AreEqual(0.1099, r.Uplight.Intensity, Tol);
        }

        [Test]
        public void SmallHoursWithLightWeather()
        {
            NightRigState r = NightRig.Compute(3, 0.2, 0.1);
            Assert.AreEqual(1.0, r.Night, Tol);
            Assert.AreEqual(-0.2185080122244105, r.Moon.ToLight.X, Tol);
            Assert.AreEqual(0.7071067811865475, r.Moon.ToLight.Y, Tol);
            Assert.AreEqual(0.6724985119639574, r.Moon.ToLight.Z, Tol);
            Assert.AreEqual(1.08706, r.Moon.Intensity, Tol);
            Assert.AreEqual(0.00507, r.FogDensity, Tol);
            Assert.AreEqual(1.155, r.HemiIntensity, Tol);
            Assert.AreEqual(0.1892, r.Uplight.Intensity, Tol);
        }

        [Test]
        public void NoonIsDaylight()
        {
            NightRigState r = NightRig.Compute(12, 0, 0);
            Assert.AreEqual(0.0, r.Night, Tol);
            Assert.AreEqual(0.3122, r.Moon.Intensity, Tol);
            Assert.AreEqual(0.42, r.FillWarm.Intensity, Tol);
            Assert.AreEqual(0.056, r.Uplight.Intensity, Tol);
        }

        [Test]
        public void AllDirectionsAreUnitLength()
        {
            foreach (double tod in new[] { 0.0, 6, 12, 18, 21.5, 23.9 })
            {
                NightRigState r = NightRig.Compute(tod);
                foreach (RigLight l in new[] { r.Moon, r.FillWarm, r.FillCool, r.Uplight })
                {
                    Vec3 d = l.ToLight;
                    Assert.AreEqual(1.0, Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z), 1e-12, $"tod {tod}");
                }
            }
        }

        [Test]
        public void FillDirectionsPointTowardTheirPrototypePositions()
        {
            NightRigState r = NightRig.Compute();
            double warm = Math.Sqrt(60 * 60 + 18 * 18 + 40 * 40);
            Assert.AreEqual(60 / warm, r.FillWarm.ToLight.X, Tol);
            Assert.AreEqual(18 / warm, r.FillWarm.ToLight.Y, Tol);
            Assert.AreEqual(-40 / warm, r.FillWarm.ToLight.Z, Tol);
            Assert.AreEqual(-1.0, r.Uplight.ToLight.Y, Tol, "the uplight shines up from below");
            Assert.Less(r.FillCool.ToLight.X, 0, "cool fill comes from -X, opposite the warm one");
        }

        [Test]
        public void NightPaletteIsThePrototypesAtFullNight()
        {
            NightRigState r = NightRig.Compute(21.5);
            AssertRgb(Srgb.HexToLinear(0x2a2740), r.FogColour);
            AssertRgb(Srgb.HexToLinear(0x9dc0ff), r.Moon.Colour);
            AssertRgb(Srgb.HexToLinear(0x415f9c), r.HemiSky);
            AssertRgb(Srgb.HexToLinear(0x4a2440), r.HemiGround);
            AssertRgb(Srgb.HexToLinear(0x0a1226), r.SkyZenith);
            AssertRgb(Srgb.HexToLinear(0x453055), r.SkyHorizon);
        }

        [Test]
        public void DaylightPaletteIsTheDuskPole()
        {
            NightRigState r = NightRig.Compute(12, 0, 0);
            AssertRgb(Srgb.HexToLinear(0x4a4560), r.FogColour);
            AssertRgb(Srgb.HexToLinear(0xc6d4ff), r.Moon.Colour);
            AssertRgb(Srgb.HexToLinear(0x1b2a52), r.SkyZenith);
            AssertRgb(Srgb.HexToLinear(0x8a5a5c), r.SkyHorizon);
        }

        [Test]
        public void SkyHorizonIsAlwaysBrighterThanTheZenith()
        {
            foreach (double tod in new[] { 0.0, 3, 6, 12, 18, 21.5 })
            {
                NightRigState r = NightRig.Compute(tod);
                float horizon = r.SkyHorizon.R + r.SkyHorizon.G + r.SkyHorizon.B;
                float zenith = r.SkyZenith.R + r.SkyZenith.G + r.SkyZenith.B;
                Assert.Greater(horizon, zenith, $"tod {tod}");
            }
        }

        [Test]
        public void RadianceDividesIntensityByPi()
        {
            RigLight l = new RigLight(new Rgb(1f, 0.5f, 0.25f), Math.PI, new Vec3(0, 1, 0));
            Assert.AreEqual(1.0, l.Radiance.R, 1e-6);
            Assert.AreEqual(0.5, l.Radiance.G, 1e-6);
            Assert.AreEqual(0.25, l.Radiance.B, 1e-6);
        }

        [Test]
        public void HemisphereRadianceUsesTheSameConvention()
        {
            NightRigState r = NightRig.Compute();
            Assert.AreEqual(r.HemiSky.R * r.HemiIntensity / Math.PI, r.HemiSkyRadiance.R, 1e-6);
            Assert.AreEqual(r.HemiGround.B * r.HemiIntensity / Math.PI, r.HemiGroundRadiance.B, 1e-6);
        }

        [Test]
        public void WeatherIsClampedToItsRange()
        {
            NightRigState wild = NightRig.Compute(21.5, 5, -3);
            NightRigState edge = NightRig.Compute(21.5, 1, 0);
            Assert.AreEqual(edge.FogDensity, wild.FogDensity, Tol);
            Assert.AreEqual(edge.Moon.Intensity, wild.Moon.Intensity, Tol);
        }

        [Test]
        public void SrgbConversionMatchesTheStandardCurve()
        {
            Assert.AreEqual(0f, Srgb.ToLinear(0));
            Assert.AreEqual(1f, Srgb.ToLinear(1), 1e-6f);
            Assert.AreEqual(0.2140411f, Srgb.ToLinear(0.5), 1e-5f);
            Assert.AreEqual(0.04045 / 12.92, Srgb.ToLinear(0.04045), 1e-7, "linear segment below the knee");
            Rgb white = Srgb.HexToLinear(0xffffff);
            Assert.AreEqual(1f, white.R, 1e-6f);
            Assert.Throws<ArgumentOutOfRangeException>(() => Srgb.ToLinear(1.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => Srgb.ToLinear(-0.01));
        }

        static void AssertRgb(Rgb expected, Rgb actual)
        {
            Assert.AreEqual(expected.R, actual.R, 1e-6f);
            Assert.AreEqual(expected.G, actual.G, 1e-6f);
            Assert.AreEqual(expected.B, actual.B, 1e-6f);
        }
    }
}
