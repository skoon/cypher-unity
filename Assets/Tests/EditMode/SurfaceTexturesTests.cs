using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class SurfaceTexturesTests
    {
        static double Luma(byte[] px, int size, int x, int y)
        {
            int i = (y * size + x) * 4;
            return (px[i] + px[i + 1] + px[i + 2]) / 3.0;
        }

        static double Mean(byte[] px, int size)
        {
            double sum = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++) sum += Luma(px, size, x, y);
            }
            return sum / (size * size);
        }

        static void AssertOpaque(byte[] px)
        {
            for (int i = 3; i < px.Length; i += 4) Assert.AreEqual(255, px[i], $"alpha at byte {i}");
        }

        // ---- prototype goldens (data textures, so byte-exact) ----

        [Test]
        public void WetRoughnessMatchesThePrototypeByteForByte()
        {
            byte[] rough = SurfaceTextures.WetRoughness(SurfaceTextures.WetRoughnessReferenceSize);
            int n = 0;
            foreach (string[] r in Golden.Rows(Golden.Lines("noise_golden.txt"), "W"))
            {
                int x = Golden.I(r[1]), y = Golden.I(r[2]);
                Assert.AreEqual(Golden.I(r[3]), rough[y * SurfaceTextures.WetRoughnessReferenceSize + x], $"wet roughness at ({x},{y})");
                n++;
            }
            Assert.AreEqual(8, n);
        }

        [Test]
        public void ConcreteRoughnessMatchesThePrototypeByteForByte()
        {
            byte[] rough = SurfaceTextures.ConcreteRoughness(SurfaceTextures.ConcreteRoughnessReferenceSize);
            int n = 0;
            foreach (string[] r in Golden.Rows(Golden.Lines("noise_golden.txt"), "C"))
            {
                int x = Golden.I(r[1]), y = Golden.I(r[2]);
                Assert.AreEqual(Golden.I(r[3]), rough[y * SurfaceTextures.ConcreteRoughnessReferenceSize + x], $"concrete roughness at ({x},{y})");
                n++;
            }
            Assert.AreEqual(6, n);
        }

        // ---- roughness fields ----

        [Test]
        public void WetRoughnessIsMostlyDampWithSlickPatchesAndDryGrain()
        {
            // Measured on the prototype's formula over a 512 px field: roughness 28..150, median 86,
            // ~30% under 75 (slick), ~9% over 120 (dry).
            byte[] rough = SurfaceTextures.WetRoughness(256);
            int slick = 0, dry = 0;
            byte min = 255, max = 0;
            foreach (byte r in rough)
            {
                if (r < 75) slick++;
                if (r > 120) dry++;
                min = Math.Min(min, r);
                max = Math.Max(max, r);
            }
            Assert.LessOrEqual(min, 40, "has slick, near-mirror patches");
            Assert.GreaterOrEqual(max, 130, "has rough dry grain");
            Assert.That(slick / (double)rough.Length, Is.InRange(0.20, 0.45));
            Assert.That(dry / (double)rough.Length, Is.InRange(0.04, 0.20));
        }

        [Test]
        public void ConcreteRoughnessStaysInTheProtoypesBand()
        {
            foreach (byte r in SurfaceTextures.ConcreteRoughness(64))
            {
                Assert.That(r, Is.InRange(165, 243));
            }
        }

        [Test]
        public void SmoothnessIsOneMinusScaledRoughness()
        {
            byte[] px = SurfaceTextures.SmoothnessFromRoughness(new byte[] { 0, 255, 100 }, 1.0);
            Assert.AreEqual(12, px.Length);
            Assert.AreEqual(255, px[0]);
            Assert.AreEqual(0, px[4]);
            Assert.AreEqual(Math.Round(255 * (1 - 100 / 255.0)), px[8]);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(px[i * 4], px[i * 4 + 1]);
                Assert.AreEqual(px[i * 4], px[i * 4 + 2]);
                Assert.AreEqual(255, px[i * 4 + 3]);
            }

            byte[] scaled = SurfaceTextures.SmoothnessFromRoughness(new byte[] { 255 }, 0.9);
            Assert.AreEqual(Math.Round(255 * (1 - 0.9)), scaled[0], "the prototype's base roughness multiplier is baked in");
        }

        [Test]
        public void SlickPatchesAreSmootherThanDryAsphaltInTheMap()
        {
            byte[] smooth = SurfaceTextures.SmoothnessFromRoughness(SurfaceTextures.WetRoughness(128), 1.0);
            byte min = 255, max = 0;
            for (int i = 0; i < smooth.Length; i += 4)
            {
                min = Math.Min(min, smooth[i]);
                max = Math.Max(max, smooth[i]);
            }
            Assert.Greater(max, 210, "slick patches are glossy");
            Assert.Less(min, 140, "dry asphalt stays rough");
        }

        // ---- albedo textures ----

        [Test]
        public void AsphaltIsDarkBluishAndOpaque()
        {
            const int size = 256;
            byte[] px = SurfaceTextures.Asphalt(size);
            Assert.AreEqual(size * size * 4, px.Length);
            AssertOpaque(px);
            double mean = Mean(px, size);
            Assert.That(mean, Is.InRange(35, 75), $"mean luma {mean:0.0}");
            long r = 0, b = 0;
            for (int i = 0; i < px.Length; i += 4) { r += px[i]; b += px[i + 2]; }
            Assert.Greater(b, r, "asphalt leans blue");
        }

        [Test]
        public void AsphaltHasCracksDarkerThanTheSurroundingSurface()
        {
            byte[] with = SurfaceTextures.Asphalt(256);
            int dark = 0;
            for (int i = 0; i < with.Length; i += 4)
            {
                if (with[i] < 20) dark++;   // the base never goes below ~26, only black crack strokes do
            }
            Assert.Greater(dark, 100, "cracks are painted");
        }

        [Test]
        public void PavingHasJointsOnA4x4Grid()
        {
            const int size = 512;
            byte[] px = SurfaceTextures.Paving(size);
            AssertOpaque(px);

            double joint = 0, away = 0;
            for (int y = 0; y < size; y++)
            {
                joint += Luma(px, size, 128, y);   // a vertical joint at 128 (a quarter of 512)
                away += Luma(px, size, 190, y);
            }
            Assert.Less(joint / size, away / size * 0.7, "the joint column is clearly darker");
        }

        [Test]
        public void ConcreteHasPanelSeamsEvery128Pixels()
        {
            const int size = 512;
            byte[] px = SurfaceTextures.Concrete(size);
            AssertOpaque(px);
            double seam = 0, away = 0;
            for (int y = 0; y < size; y++)
            {
                seam += Luma(px, size, 256, y);
                away += Luma(px, size, 200, y);
            }
            Assert.Less(seam / size, away / size - 8, "the seam column is darker than concrete between seams");
        }

        [Test]
        public void ConcreteIsMidToneLikeThePrototype()
        {
            const int size = 128;
            double mean = Mean(SurfaceTextures.Concrete(size), size);
            Assert.That(mean, Is.InRange(95, 160), $"mean luma {mean:0.0}");
        }

        // ---- general ----

        [Test]
        public void GeneratorsAreDeterministic()
        {
            CollectionAssert.AreEqual(SurfaceTextures.Asphalt(64), SurfaceTextures.Asphalt(64));
            CollectionAssert.AreEqual(SurfaceTextures.Paving(64), SurfaceTextures.Paving(64));
            CollectionAssert.AreEqual(SurfaceTextures.Concrete(64), SurfaceTextures.Concrete(64));
            CollectionAssert.AreEqual(SurfaceTextures.WetRoughness(64), SurfaceTextures.WetRoughness(64));
        }

        [Test]
        public void PatternCoversTheSameUvAreaAtAnySize()
        {
            // Mean tone must not depend on resolution: the content is rescaled, not cropped or repeated.
            double small = Mean(SurfaceTextures.Concrete(64), 64);
            double large = Mean(SurfaceTextures.Concrete(256), 256);
            Assert.AreEqual(large, small, 6.0);
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceTextures.Asphalt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceTextures.WetRoughness(-4));
            Assert.Throws<ArgumentNullException>(() => SurfaceTextures.SmoothnessFromRoughness(null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceTextures.SmoothnessFromRoughness(new byte[1], 1.5));
        }
    }
}
