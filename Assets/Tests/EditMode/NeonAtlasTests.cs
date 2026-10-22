using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class NeonAtlasTests
    {
        static readonly NeonAtlasLayout Layout = NeonAtlas.Layout();
        static readonly byte[] Pixels = NeonAtlas.Render(Layout);

        /// <summary>Pixels brighter than a threshold in a cell, found through its texture rectangle (v = 0 is the bottom row of the bytes).</summary>
        static int Bright(AtlasCell c, int threshold)
        {
            var uv = c.Uv(NeonAtlas.Size);
            int x0 = (int)Math.Round(uv.u * NeonAtlas.Size), y0 = (int)Math.Round(uv.v * NeonAtlas.Size);
            int n = 0;
            for (int y = y0; y < y0 + c.H; y++)
            {
                for (int x = x0; x < x0 + c.W; x++)
                {
                    if (Pixels[y * NeonAtlas.Size + x] >= threshold) n++;
                }
            }
            return n;
        }

        [Test]
        public void TheLayoutHasEveryCellInItsGroup()
        {
            Assert.AreEqual(24 + 4 + 14 + 8 + 8 + 8, Layout.Cells.Count);
            Assert.AreEqual(28, Layout.Group(SignGroup.Word).Count, "24 words and 4 kana bands");
            Assert.AreEqual(14, Layout.Group(SignGroup.Kanji).Count);
            Assert.AreEqual(16, Layout.Group(SignGroup.Vertical).Count, "8 words and 8 kana columns");
            Assert.AreEqual(8, Layout.Group(SignGroup.Icon).Count);
            Assert.AreEqual(2048, Layout.Size);
        }

        [Test]
        public void CellsHaveTheTypeOfSizeTheirGroupNeeds()
        {
            foreach (AtlasCell c in Layout.Group(SignGroup.Word)) Assert.AreEqual((384, 112), (c.W, c.H), c.Name);
            foreach (AtlasCell c in Layout.Group(SignGroup.Kanji)) Assert.AreEqual((176, 176), (c.W, c.H), c.Name);
            foreach (AtlasCell c in Layout.Group(SignGroup.Vertical)) Assert.AreEqual((96, 384), (c.W, c.H), c.Name);
            foreach (AtlasCell c in Layout.Group(SignGroup.Icon)) Assert.AreEqual((80, 80), (c.W, c.H), c.Name);
            Assert.AreEqual(384.0 / 112, Layout["word0"].Aspect, 1e-9);
        }

        [Test]
        public void CellsAreInsideTheAtlasAndNeverOverlapEachOtherOrTheirPadding()
        {
            foreach (AtlasCell c in Layout.Cells)
            {
                Assert.GreaterOrEqual(c.X, NeonAtlas.Pad, c.Name);
                Assert.GreaterOrEqual(c.Y, NeonAtlas.Pad, c.Name);
                Assert.LessOrEqual(c.X + c.W + NeonAtlas.Pad, NeonAtlas.Size, c.Name);
                Assert.LessOrEqual(c.Y + c.H + NeonAtlas.Pad, NeonAtlas.Size, c.Name);
            }
            for (int i = 0; i < Layout.Cells.Count; i++)
            {
                for (int j = i + 1; j < Layout.Cells.Count; j++)
                {
                    AtlasCell a = Layout.Cells[i], b = Layout.Cells[j];
                    int gap = NeonAtlas.Pad * 2;
                    bool apart = a.X + a.W + gap <= b.X || b.X + b.W + gap <= a.X || a.Y + a.H + gap <= b.Y || b.Y + b.H + gap <= a.Y;
                    Assert.IsTrue(apart, $"{a.Name} and {b.Name} are too close");
                }
            }
        }

        [Test]
        public void CellNamesAreUniqueAndLookUpByName()
        {
            Assert.AreEqual(Layout.Cells.Count, Layout.Cells.Select(c => c.Name).Distinct().Count());
            Assert.AreEqual("word0", Layout["word0"].Name);
            Assert.AreEqual(SignGroup.Icon, Layout["icon3"].Group);
            Assert.Throws<ArgumentException>(() => { var x = Layout["nope"]; });
        }

        [Test]
        public void TextureRectanglesFlipTheRowsForUnity()
        {
            AtlasCell first = Layout.Cells[0];   // top-left of the picture
            var uv = first.Uv(NeonAtlas.Size);
            Assert.AreEqual((double)first.X / 2048, uv.u, 1e-12);
            Assert.AreEqual(1.0 - (double)(first.Y + first.H) / 2048, uv.v, 1e-12);
            Assert.AreEqual(1.0 - (double)NeonAtlas.Pad / 2048, uv.v + uv.dv, 1e-12, "the first row of cells is at the top: its upper edge is just below v = 1");
            AtlasCell last = Layout.Cells[Layout.Cells.Count - 1];
            Assert.Less(last.Uv(2048).v, first.Uv(2048).v, "later rows are lower in the picture, so lower in v");
        }

        [Test]
        public void EveryCellHasLetteringInIt()
        {
            foreach (AtlasCell c in Layout.Cells)
            {
                Assert.Greater(Bright(c, 220), c.W * c.H / 60, $"{c.Name} has a hard core");
                Assert.Less(Bright(c, 220), c.W * c.H / 2, $"{c.Name} is not just filled in");
            }
        }

        [Test]
        public void EveryCellHasAHaloAroundItsCore()
        {
            foreach (AtlasCell c in Layout.Cells)
            {
                int core = Bright(c, 220);
                int glow = Bright(c, 40);
                Assert.Greater(glow, core * 1.5, $"{c.Name}: the glow reaches well past the lines");
            }
        }

        [Test]
        public void TheBottomOfTheAtlasIsEmptyBecauseNothingIsPackedThere()
        {
            // rows are counted from the bottom of the bytes: the top of the picture is the last rows
            int lowestUsed = Layout.Cells.Max(c => c.Y + c.H + NeonAtlas.Pad);
            int emptyRows = NeonAtlas.Size - lowestUsed;
            Assert.Greater(emptyRows, 100);
            for (int y = 0; y < emptyRows - 1; y++)
            {
                for (int x = 0; x < NeonAtlas.Size; x += 7) Assert.AreEqual(0, Pixels[y * NeonAtlas.Size + x]);
            }
        }

        [Test]
        public void TheSameBytesComeOutEveryTime()
        {
            byte[] again = NeonAtlas.Render(NeonAtlas.Layout());
            Assert.AreEqual(Pixels.Length, again.Length);
            Assert.IsTrue(Pixels.SequenceEqual(again));
        }

        [Test]
        public void WordsAreCentredInTheirBanner()
        {
            AtlasCell c = Layout["word22"];   // XXX
            var uv = c.Uv(NeonAtlas.Size);
            int x0 = (int)Math.Round(uv.u * NeonAtlas.Size), y0 = (int)Math.Round(uv.v * NeonAtlas.Size);
            int left = int.MaxValue, right = -1;
            for (int y = y0; y < y0 + c.H; y++)
            {
                for (int x = x0; x < x0 + c.W; x++)
                {
                    if (Pixels[y * NeonAtlas.Size + x] < 230) continue;
                    left = Math.Min(left, x - x0); right = Math.Max(right, x - x0);
                }
            }
            Assert.AreEqual(c.W / 2.0, (left + right) / 2.0, 12);
        }

        [Test]
        public void AFramedBannerHasItsFrameAtTheEdges()
        {
            // at least one of the words is framed: it has bright pixels near the border of its cell that an unframed one does not
            int framed = 0;
            foreach (AtlasCell c in Layout.Group(SignGroup.Word).Take(24))
            {
                var uv = c.Uv(NeonAtlas.Size);
                int x0 = (int)Math.Round(uv.u * NeonAtlas.Size), y0 = (int)Math.Round(uv.v * NeonAtlas.Size);
                int edge = 0;
                for (int y = y0; y < y0 + c.H; y++)
                {
                    for (int x = x0; x < x0 + 14; x++)
                    {
                        if (Pixels[y * NeonAtlas.Size + x] >= 230) edge++;
                    }
                }
                if (edge > 40) framed++;
            }
            Assert.That(framed, Is.InRange(3, 20), "a few banners are framed, not all and not none");
        }

        [Test]
        public void EveryBladeHasItsLetteringDownItsLength()
        {
            AtlasCell c = Layout["vword2"];   // RAMEN stacked
            var uv = c.Uv(NeonAtlas.Size);
            int x0 = (int)Math.Round(uv.u * NeonAtlas.Size), y0 = (int)Math.Round(uv.v * NeonAtlas.Size);
            for (int band = 0; band < 5; band++)
            {
                int n = 0;
                int by0 = y0 + 25 + band * 68;
                for (int y = by0; y < by0 + 50; y++)
                {
                    for (int x = x0 + 20; x < x0 + c.W - 20; x++)
                    {
                        if (Pixels[y * NeonAtlas.Size + x] >= 230) n++;
                    }
                }
                Assert.Greater(n, 15, $"no lettering in band {band} of the RAMEN blade");
            }
        }

        [Test]
        public void ARenderNeedsALayout()
        {
            Assert.Throws<ArgumentNullException>(() => NeonAtlas.Render(null));
        }
    }
}
