using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class PropTexturesTests
    {
        const int Size = PropTextures.AtlasSize;

        static readonly byte[] Graffiti = PropTextures.Graffiti();
        static readonly byte[] Ground = PropTextures.Ground();

        static int Alpha(byte[] rgba, int width, int x, int y) => rgba[(y * width + x) * 4 + 3];

        /// <summary>Pixels with alpha above a threshold inside a rectangle.</summary>
        static int Count(byte[] rgba, int width, int x0, int y0, int w, int h, int minAlpha = 200)
        {
            int n = 0;
            for (int y = y0; y < y0 + h; y++)
            {
                for (int x = x0; x < x0 + w; x++)
                {
                    if (Alpha(rgba, width, x, y) >= minAlpha) n++;
                }
            }
            return n;
        }

        [Test]
        public void TheGrimeMapIsAnOpaqueMottledGrey()
        {
            byte[] g = PropTextures.Grime();
            Assert.AreEqual(PropTextures.GrimeSize * PropTextures.GrimeSize * 4, g.Length);
            double sum = 0;
            int min = 255, max = 0;
            for (int i = 0; i < g.Length; i += 4)
            {
                Assert.AreEqual(255, g[i + 3]);
                sum += g[i];
                min = Math.Min(min, g[i]);
                max = Math.Max(max, g[i]);
            }
            double mean = sum / (g.Length / 4);
            Assert.That(mean, Is.InRange(180.0, 235.0), "light grey on average, so it only dirties the colours it multiplies");
            Assert.Greater(max - min, 40, "and it has real variation");
            Assert.IsTrue(g.SequenceEqual(PropTextures.Grime()), "the same every time");
        }

        [Test]
        public void TheGrimeMapCanBeMadeAtAnotherSize()
        {
            Assert.AreEqual(64 * 64 * 4, PropTextures.Grime(64).Length);
            Assert.Throws<ArgumentOutOfRangeException>(() => PropTextures.Grime(4));
        }

        [Test]
        public void EveryGraffitiTileHasAnEmptyMarginAndSomethingDrawnInside()
        {
            int t = Size / 4;
            for (int ty = 0; ty < 4; ty++)
            {
                for (int tx = 0; tx < 4; tx++)
                {
                    int ox = tx * t, oy = ty * t;
                    // the outer 12 pixels of every tile are clipped away, so filtering never picks up a neighbour's paint
                    Assert.AreEqual(0, Count(Graffiti, Size, ox, oy, t, 12, 1), $"tile {ty * 4 + tx}: top margin");
                    Assert.AreEqual(0, Count(Graffiti, Size, ox, oy + t - 12, t, 12, 1), $"tile {ty * 4 + tx}: bottom margin");
                    Assert.AreEqual(0, Count(Graffiti, Size, ox, oy, 12, t, 1), $"tile {ty * 4 + tx}: left margin");
                    Assert.AreEqual(0, Count(Graffiti, Size, ox + t - 12, oy, 12, t, 1), $"tile {ty * 4 + tx}: right margin");
                    Assert.Greater(Count(Graffiti, Size, ox + 12, oy + 12, t - 24, t - 24), 1500, $"tile {ty * 4 + tx} is blank");
                }
            }
        }

        [Test]
        public void TheLastFiveGraffitiTilesAreFlyposters()
        {
            int t = Size / 4;
            // a flyposter has a big opaque grey sheet (0xc9ccd4); a tag does not
            int Paper(int idx)
            {
                int ox = (idx % 4) * t, oy = (idx / 4) * t, n = 0;
                for (int y = oy; y < oy + t; y++)
                {
                    for (int x = ox; x < ox + t; x++)
                    {
                        int i = (y * Size + x) * 4;
                        if (Graffiti[i + 3] > 200 && Math.Abs(Graffiti[i] - 0xc9) < 6 && Math.Abs(Graffiti[i + 1] - 0xcc) < 6 && Math.Abs(Graffiti[i + 2] - 0xd4) < 6) n++;
                    }
                }
                return n;
            }
            for (int idx = 0; idx < 11; idx++) Assert.Less(Paper(idx), 200, $"tile {idx} is a tag");
            for (int idx = 11; idx < 16; idx++) Assert.Greater(Paper(idx), 8000, $"tile {idx} is a flyposter");
        }

        [Test]
        public void TheTagsUseTheNeonPalette()
        {
            int t = Size / 4;
            int bright = 0;
            for (int y = 12; y < t - 12; y++)
            {
                for (int x = 12; x < t - 12; x++)
                {
                    int i = (y * Size + x) * 4;
                    if (Graffiti[i + 3] > 240 && Math.Max(Graffiti[i], Math.Max(Graffiti[i + 1], Graffiti[i + 2])) > 200) bright++;
                }
            }
            Assert.Greater(bright, 600, "the first tag has fat strokes in a saturated colour");
        }

        [Test]
        public void EveryGroundTileHasAnEmptyMarginAndSomethingDrawnInside()
        {
            int t = Size / 4;
            for (int ty = 0; ty < 4; ty++)
            {
                for (int tx = 0; tx < 4; tx++)
                {
                    int ox = tx * t, oy = ty * t;
                    Assert.AreEqual(0, Count(Ground, Size, ox, oy, t, 6, 1), $"tile {ty * 4 + tx}: top margin");
                    Assert.AreEqual(0, Count(Ground, Size, ox, oy + t - 6, t, 6, 1), $"tile {ty * 4 + tx}: bottom margin");
                    Assert.AreEqual(0, Count(Ground, Size, ox, oy, 6, t, 1), $"tile {ty * 4 + tx}: left margin");
                    Assert.AreEqual(0, Count(Ground, Size, ox + t - 6, oy, 6, t, 1), $"tile {ty * 4 + tx}: right margin");
                    Assert.Greater(Count(Ground, Size, ox + 6, oy + 6, t - 12, t - 12, 20), 300, $"tile {ty * 4 + tx} is blank");
                }
            }
        }

        [Test]
        public void TheOilSlicksAreDarkAndSoft()
        {
            int t = Size / 4;
            long darkWeight = 0;
            int partial = 0, total = 0;
            for (int y = 6; y < t - 6; y++)
            {
                for (int x = 6; x < t - 6; x++)
                {
                    int i = (y * Size + x) * 4;
                    if (Ground[i + 3] == 0) continue;
                    total++;
                    if (Ground[i + 3] < 200) partial++;
                    darkWeight += Ground[i];
                }
            }
            Assert.Greater(total, 5000);
            Assert.Less(darkWeight / (double)total, 60, "oil is near black");
            Assert.Greater(partial / (double)total, 0.5, "and fades softly at the edges");
        }

        [Test]
        public void TheAtlasesAreTheSameEveryTime()
        {
            Assert.IsTrue(Graffiti.SequenceEqual(PropTextures.Graffiti()));
            Assert.IsTrue(Ground.SequenceEqual(PropTextures.Ground()));
        }

        [Test]
        public void TheDecalAtlasStacksGraffitiOverGroundAndIsFlippedForUnity()
        {
            byte[] atlas = PropTextures.DecalAtlas();
            Assert.AreEqual(PropTextures.DecalAtlasWidth * PropTextures.DecalAtlasHeight * 4, atlas.Length);
            Assert.AreEqual(Size, PropTextures.DecalAtlasWidth);
            Assert.AreEqual(Size * 2, PropTextures.DecalAtlasHeight);

            // for every tile of both kinds, the pixels under DecalUv are the tile's pixels in the source atlas
            for (int kind = 0; kind < 2; kind++)
            {
                byte[] source = kind == 0 ? Graffiti : Ground;
                for (int tile = 0; tile < 16; tile++)
                {
                    var (u, v, du, dv) = PropTextures.DecalUv(kind == 0 ? DecalKind.Graffiti : DecalKind.Ground, tile);
                    int px0 = (int)Math.Round(u * PropTextures.DecalAtlasWidth), px1 = (int)Math.Round((u + du) * PropTextures.DecalAtlasWidth);
                    // v = 0 is the bottom row of the texture, which is the last row of the stacked picture
                    int py0 = (int)Math.Round((1 - (v + dv)) * PropTextures.DecalAtlasHeight), py1 = (int)Math.Round((1 - v) * PropTextures.DecalAtlasHeight);
                    int inAtlas = Count(atlas, PropTextures.DecalAtlasWidth, px0, PropTextures.DecalAtlasHeight - py1, px1 - px0, py1 - py0, 100);

                    int tx = tile & 3, ty = tile >> 2, t = Size / 4;
                    int inSource = Count(source, Size, tx * t + 5, ty * t + 5, t - 10, t - 10, 100);
                    Assert.Greater(inAtlas, 200, $"kind {kind} tile {tile}: nothing under its UV");
                    Assert.AreEqual(inSource, inAtlas, inSource * 0.05 + 50, $"kind {kind} tile {tile}: the UV looks at the wrong place");
                }
            }
        }

        [Test]
        public void DecalUvsKeepGraffitiInTheUpperHalfAndGroundInTheLower()
        {
            for (int tile = 0; tile < 16; tile++)
            {
                var g = PropTextures.DecalUv(DecalKind.Graffiti, tile);
                var o = PropTextures.DecalUv(DecalKind.Ground, tile);
                Assert.GreaterOrEqual(g.v, 0.5);
                Assert.LessOrEqual(g.v + g.dv, 1.0);
                Assert.GreaterOrEqual(o.v, 0.0);
                Assert.LessOrEqual(o.v + o.dv, 0.5);
                Assert.That(g.u + g.du, Is.LessThanOrEqualTo(1.0));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => PropTextures.DecalUv(DecalKind.Ground, 16));
            Assert.Throws<ArgumentOutOfRangeException>(() => PropTextures.DecalUv(DecalKind.Ground, -1));
        }

        [Test]
        public void TheFirstRowOfThePictureHasTheHighestV()
        {
            // tile 0 is the top-left of the picture: it must sit at the top of its half (highest v), left (lowest u)
            var first = PropTextures.DecalUv(DecalKind.Graffiti, 0);
            var last = PropTextures.DecalUv(DecalKind.Graffiti, 15);
            Assert.Greater(first.v, last.v);
            Assert.Less(first.u, last.u);
        }

        [Test]
        public void ThePuffIsBrightInTheMiddleAndClearAtTheEdge()
        {
            byte[] p = PropTextures.Puff();
            int n = PropTextures.PuffSize;
            Assert.AreEqual(n * n * 4, p.Length);
            // ragged: the centre may be eaten into by the erased blobs, so look at the strongest pixel near it
            int strongest = 0;
            for (int y = n / 2 - 12; y < n / 2 + 12; y++)
            {
                for (int x = n / 2 - 12; x < n / 2 + 12; x++) strongest = Math.Max(strongest, Alpha(p, n, x, y));
            }
            Assert.Greater(strongest, 150);
            Assert.AreEqual(0, Alpha(p, n, 0, 0));
            Assert.AreEqual(0, Alpha(p, n, n - 1, n - 1));
            Assert.LessOrEqual(Alpha(p, n, 2, n / 2), 3, "the gradient has all but faded out at the rim");
            Assert.IsTrue(p.SequenceEqual(PropTextures.Puff()), "the same every time");
        }

        [Test]
        public void FlippingTheRowsTwiceGivesTheOriginalBack()
        {
            byte[] a = new byte[4 * 3 * 4];
            for (int i = 0; i < a.Length; i++) a[i] = (byte)i;
            byte[] flipped = PropTextures.FlipRows(a, 4, 3);
            Assert.AreEqual(a[0], flipped[2 * 16]);
            Assert.AreEqual(a.Skip(32).Take(16), flipped.Take(16));
            Assert.AreEqual(a, PropTextures.FlipRows(flipped, 4, 3));
            Assert.Throws<ArgumentException>(() => PropTextures.FlipRows(a, 5, 3));
            Assert.Throws<ArgumentNullException>(() => PropTextures.FlipRows(null, 4, 3));
        }
    }
}
