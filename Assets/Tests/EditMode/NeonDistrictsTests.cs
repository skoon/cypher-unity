using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class NeonDistrictsTests
    {
        // Goldens from a Node evaluation of districtAt() in src/world/Neon.js.
        [TestCase(0.0, 0.0, "corpo")]
        [TestCase(100.0, -200.0, "corpo")]
        [TestCase(-6000.0, 6000.0, "market")]
        [TestCase(1234.5, -987.6, "market")]
        [TestCase(-217.0, 217.0, "kabuki")]
        [TestCase(60.0, 18.0, "redline")]
        [TestCase(-55.0, 55.0, "market")]
        [TestCase(3000.0, 3000.0, "corpo")]
        [TestCase(-8.5, -8.5, "corpo")]
        [TestCase(-5999.9, -5999.9, "corpo")]
        [TestCase(40.0, -40.0, "kabuki")]
        [TestCase(-300.0, 250.0, "corpo")]
        public void DistrictHashMatchesThePrototype(double x, double z, string expected)
        {
            Assert.AreEqual(expected, NeonDistricts.At(x, z).Name);
        }

        [Test]
        public void SameTileSameDistrict()
        {
            Assert.AreSame(NeonDistricts.At(10, 10), NeonDistricts.At(10.5, 15), "both inside the 118 m tile [5900, 6018) in z");
        }

        [Test]
        public void AllFourDistrictsOccurAcrossTheCity()
        {
            var seen = new HashSet<string>();
            for (double x = -300; x <= 300; x += 59)
            {
                for (double z = -300; z <= 300; z += 59) seen.Add(NeonDistricts.At(x, z).Name);
            }
            CollectionAssert.AreEquivalent(new[] { "kabuki", "corpo", "market", "redline" }, seen);
        }

        [Test]
        public void PickHueUsesExactlyOneDrawAndOnlyDistrictColours()
        {
            var rng = new Rng(5);
            var reference = new Rng(5);
            reference.Next();

            Rgb c = NeonDistricts.Kabuki.PickHue(rng);
            Assert.AreEqual(reference.Next(), rng.Next(), "one draw consumed");
            Assert.IsTrue(NeonDistricts.Kabuki.Colours.Any(k => k.R == c.R && k.G == c.G && k.B == c.B));
        }

        [Test]
        public void HuesFollowTheirWeights()
        {
            NeonDistrict d = NeonDistricts.Kabuki;
            var rng = new Rng(99);
            var counts = new int[d.Colours.Length];
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                Rgb c = d.PickHue(rng);
                counts[Array.FindIndex(d.Colours, k => k.R == c.R && k.G == c.G && k.B == c.B)]++;
            }
            for (int i = 0; i < counts.Length; i++)
            {
                Assert.AreEqual(d.Weights[i] / d.TotalWeight, counts[i] / (double)n, 0.02, $"hue {i}");
            }
            Assert.Greater(counts[0], counts[1], "the first colour dominates");
        }

        [Test]
        public void KabukiLeadsWithHotPinkAndRedlineWithRed()
        {
            AssertRgbEqual(Srgb.HexToLinear(0xff2a86), NeonDistricts.Kabuki.Colours[0]);
            AssertRgbEqual(Srgb.HexToLinear(0xff1440), NeonDistricts.Redline.Colours[0]);
            AssertRgbEqual(Srgb.HexToLinear(0x2ad8ff), NeonDistricts.Corpo.Colours[0]);
            AssertRgbEqual(Srgb.HexToLinear(0xff9a12), NeonDistricts.Market.Colours[0]);
        }

        [Test]
        public void BadDistrictsFailLoudly()
        {
            Assert.Throws<ArgumentException>(() => new NeonDistrict("empty"));
            Assert.Throws<ArgumentException>(() => new NeonDistrict("zero", (0xffffff, 0.0)));
        }

        static void AssertRgbEqual(Rgb expected, Rgb actual)
        {
            Assert.AreEqual(expected.R, actual.R, 1e-6f);
            Assert.AreEqual(expected.G, actual.G, 1e-6f);
            Assert.AreEqual(expected.B, actual.B, 1e-6f);
        }
    }
}
