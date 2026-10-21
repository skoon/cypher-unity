using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class WindowAtlasTests
    {
        const int Size = 512;   // 16 px per pane: enough resolution to measure the glass rectangle
        static readonly int Step = Size / WindowAtlas.Panes;

        static int Idx(int x, int y) => (y * Size + x) * 4;

        [Test]
        public void ProducesRgbaForTheRequestedSize()
        {
            Assert.AreEqual(Size * Size * 4, WindowAtlas.Generate(Size).Length);
        }

        [Test]
        public void SizeMustBeAMultipleOfThePaneCount()
        {
            Assert.Throws<ArgumentException>(() => WindowAtlas.Generate(100));
            Assert.Throws<ArgumentException>(() => WindowAtlas.Generate(0));
            Assert.Throws<ArgumentException>(() => WindowAtlas.Generate(-32));
        }

        [Test]
        public void SameSeedIsDeterministicAndDifferentSeedsDiffer()
        {
            CollectionAssert.AreEqual(WindowAtlas.Generate(Size, 1), WindowAtlas.Generate(Size, 1));
            CollectionAssert.AreNotEqual(WindowAtlas.Generate(Size, 1), WindowAtlas.Generate(Size, 2));
        }

        [Test]
        public void EveryPaneHasAMullionMarginAndAGlassCentre()
        {
            byte[] px = WindowAtlas.Generate(Size);
            for (int row = 0; row < WindowAtlas.Panes; row++)
            {
                for (int col = 0; col < WindowAtlas.Panes; col++)
                {
                    int x = col * Step, y = row * Step;
                    Assert.AreEqual(0, px[Idx(x, y) + 3], $"pane ({col},{row}) corner should be mullion");
                    Assert.AreEqual(255, px[Idx(x + Step / 2, y + Step / 2) + 3], $"pane ({col},{row}) centre should be glass");
                }
            }
        }

        [Test]
        public void MullionMarginsStayDark()
        {
            byte[] px = WindowAtlas.Generate(Size);
            for (int row = 0; row < WindowAtlas.Panes; row++)
            {
                for (int col = 0; col < WindowAtlas.Panes; col++)
                {
                    int i = Idx(col * Step, row * Step);
                    Assert.LessOrEqual(px[i] + px[i + 1] + px[i + 2], 30);
                }
            }
        }

        [Test]
        public void GlassFillsTheFacadesProportionsNotTheWholeCell()
        {
            // Measured at the real atlas size: at 16 px per cell the rectangle rounds to whole pixels too coarsely.
            byte[] px = WindowAtlas.Generate();
            long glass = 0;
            for (int i = 3; i < px.Length; i += 4)
            {
                if (px[i] == 255) glass++;
            }
            double share = glass / ((double)WindowAtlas.DefaultSize * WindowAtlas.DefaultSize);
            double expected = (1 - 2 * WindowAtlas.GlassInsetX) * (1 - 2 * WindowAtlas.GlassInsetY);
            Assert.AreEqual(expected, share, 0.02, "68% x 52% of each pane cell is glass");
            Assert.Less(share, 0.40, "well under the old 64%, so concrete shows between panes");
        }

        [Test]
        public void GlassIsCentredInItsCell()
        {
            byte[] px = WindowAtlas.Generate(Size);
            int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
            for (int y = 0; y < Step; y++)
            {
                for (int x = 0; x < Step; x++)
                {
                    if (px[Idx(x, y) + 3] != 255) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
            }
            Assert.AreEqual(Step - 1 - right, left, 1, "equal margin left and right");
            Assert.AreEqual(Step - 1 - bottom, top, 1, "equal margin top and bottom");
            Assert.Greater(top, left, "taller margins than side margins: panes are wider than they are tall");
        }

        [Test]
        public void ARoughlyMixedShareOfPanesIsLit()
        {
            byte[] px = WindowAtlas.Generate(Size);
            int lit = 0;
            for (int row = 0; row < WindowAtlas.Panes; row++)
            {
                for (int col = 0; col < WindowAtlas.Panes; col++)
                {
                    // A pane is lit if any glass pixel glows; blinds and silhouettes only cover part of it.
                    int brightest = 0;
                    for (int y = 0; y < Step; y++)
                    {
                        for (int x = 0; x < Step; x++)
                        {
                            int i = Idx(col * Step + x, row * Step + y);
                            if (px[i + 3] != 255) continue;
                            brightest = Math.Max(brightest, Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])));
                        }
                    }
                    if (brightest > 60) lit++;
                }
            }
            double share = lit / (double)(WindowAtlas.Panes * WindowAtlas.Panes);
            Assert.That(share, Is.InRange(0.2, 0.8), $"lit share {share:0.00}");
        }
    }
}
