using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class NoiseTests
    {
        [Test]
        public void MatchesThePrototypeAtEverySampledPoint()
        {
            string[] lines = Golden.Lines("noise_golden.txt");
            int perlin = 0, fbm = 0;
            foreach (string[] r in Golden.Rows(lines, "P"))
            {
                double x = Golden.D(r[1]), y = Golden.D(r[2]), expected = Golden.D(r[3]);
                Assert.AreEqual(expected, Noise.Perlin2(x, y), 1e-12, $"perlin2({x}, {y})");
                perlin++;
            }
            foreach (string[] r in Golden.Rows(lines, "F"))
            {
                double x = Golden.D(r[1]), y = Golden.D(r[2]), expected = Golden.D(r[4]);
                int octaves = Golden.I(r[3]);
                Assert.AreEqual(expected, Noise.Fbm2(x, y, octaves), 1e-12, $"fbm2({x}, {y}, {octaves})");
                fbm++;
            }
            Assert.AreEqual(12, perlin);
            Assert.AreEqual(36, fbm);
        }

        [Test]
        public void PerlinIsZeroAtLatticePoints()
        {
            foreach (int i in new[] { -7, 0, 1, 42, 255, 256, 1000 })
            {
                Assert.AreEqual(0.0, Noise.Perlin2(i, i + 3), 1e-12);
            }
        }

        [Test]
        public void PerlinStaysInRangeAndActuallyVaries()
        {
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            for (int i = 0; i < 4000; i++)
            {
                double v = Noise.Perlin2(i * 0.173, i * 0.311);
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
            Assert.GreaterOrEqual(min, -1.0);
            Assert.LessOrEqual(max, 1.0);
            Assert.Less(min, -0.4);
            Assert.Greater(max, 0.4);
        }

        [Test]
        public void IsPeriodicEvery256Cells()
        {
            Assert.AreEqual(Noise.Perlin2(3.3, 4.4), Noise.Perlin2(3.3 + 256, 4.4), 1e-9);
            Assert.AreEqual(Noise.Perlin2(3.3, 4.4), Noise.Perlin2(3.3, 4.4 + 256), 1e-9);
        }

        [Test]
        public void FbmWithOneOctaveIsPerlin()
        {
            Assert.AreEqual(Noise.Perlin2(1.7, 2.9), Noise.Fbm2(1.7, 2.9, 1), 1e-12);
        }

        [Test]
        public void FbmIsDeterministic()
        {
            Assert.AreEqual(Noise.Fbm2(12.5, 7.25, 5), Noise.Fbm2(12.5, 7.25, 5));
        }

        [Test]
        public void ZeroOctavesFailsLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Noise.Fbm2(1, 1, 0));
        }
    }
}
