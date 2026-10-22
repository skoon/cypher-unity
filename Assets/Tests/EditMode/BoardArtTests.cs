using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class BoardArtTests
    {
        static readonly byte[] Pixels = BoardArt.Render();

        /// <summary>The colour of a point of a panel (lu and lv run 0 to 1 from its left and bottom).</summary>
        static (int R, int G, int B, int A) At(int panel, double lu, double lv)
        {
            (double u, double v) = BoardArt.AtlasUv(panel, lu, lv);
            int x = Math.Min(BoardArt.Size - 1, (int)(u * BoardArt.Size)), y = Math.Min(BoardArt.Size - 1, (int)(v * BoardArt.Size));
            int i = (y * BoardArt.Size + x) * 4;
            return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
        }

        /// <summary>How many pixels of a rectangle of a panel are close to a colour.</summary>
        static int Count(int panel, double lu0, double lv0, double lu1, double lv1, uint hex, int tolerance = 60)
        {
            int r = (int)((hex >> 16) & 0xff), g = (int)((hex >> 8) & 0xff), b = (int)(hex & 0xff);
            int n = 0;
            const int steps = 120;
            for (int i = 0; i < steps; i++)
            {
                for (int j = 0; j < steps; j++)
                {
                    var p = At(panel, lu0 + (lu1 - lu0) * i / (steps - 1), lv0 + (lv1 - lv0) * j / (steps - 1));
                    if (Math.Abs(p.R - r) + Math.Abs(p.G - g) + Math.Abs(p.B - b) < tolerance) n++;
                }
            }
            return n;
        }

        [Test]
        public void TheAtlasIsFourPanelsInATwoByTwoGrid()
        {
            Assert.AreEqual(4, BoardArt.PanelCount);
            Assert.AreEqual(1024, BoardArt.Size);
            Assert.AreEqual(BoardArt.Size * BoardArt.Size * 4, Pixels.Length);
            Assert.AreEqual(4, BoardArt.PanelNames.Count);
        }

        [Test]
        public void ThePanelsOccupyDistinctQuartersAndKeepClearOfTheirEdges()
        {
            for (int p = 0; p < 4; p++)
            {
                var lo = BoardArt.AtlasUv(p, 0, 0);
                var hi = BoardArt.AtlasUv(p, 1, 1);
                Assert.Greater(hi.U, lo.U);
                Assert.Greater(hi.V, lo.V);
                double quarterU = (p % 2) * 0.5, quarterV = 0.5 - (p / 2) * 0.5;   // panel 0 is at the top left of the picture
                Assert.GreaterOrEqual(lo.U, quarterU + BoardArt.Inset / (double)BoardArt.Size - 1e-12);
                Assert.LessOrEqual(hi.U, quarterU + 0.5 - BoardArt.Inset / (double)BoardArt.Size + 1e-12);
                Assert.GreaterOrEqual(lo.V, quarterV - 1e-12 + BoardArt.Inset / (double)BoardArt.Size);
                Assert.LessOrEqual(hi.V, quarterV + 0.5 - BoardArt.Inset / (double)BoardArt.Size + 1e-12);
            }
        }

        [Test]
        public void TheInsetFractionIsTheInsetAsAShareOfOnePanel()
        {
            Assert.AreEqual(BoardArt.Inset / (double)BoardArt.PanelSize, BoardArt.InsetFraction, 1e-15);
            // the shader finds the panel from floor(uv * 2) and its local coordinate from frac(uv * 2) with this inset
            var p = BoardArt.AtlasUv(3, 0.25, 0.75);
            double fx = p.U * 2 - Math.Floor(p.U * 2), fy = p.V * 2 - Math.Floor(p.V * 2);
            Assert.AreEqual(1.0, Math.Floor(p.U * 2), "right column");
            Assert.AreEqual(0.0, Math.Floor(p.V * 2), "bottom row");
            Assert.AreEqual(0.25, (fx - BoardArt.InsetFraction) / (1 - 2 * BoardArt.InsetFraction), 1e-9);
            Assert.AreEqual(0.75, (fy - BoardArt.InsetFraction) / (1 - 2 * BoardArt.InsetFraction), 1e-9);
        }

        [Test]
        public void ABadPanelFailsLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BoardArt.AtlasUv(-1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => BoardArt.AtlasUv(4, 0, 0));
        }

        [Test]
        public void EveryPixelIsOpaque()
        {
            for (int i = 3; i < Pixels.Length; i += 4) Assert.AreEqual(255, Pixels[i]);
        }

        [Test]
        public void TheSameBytesComeOutEveryTime()
        {
            Assert.IsTrue(Pixels.SequenceEqual(BoardArt.Render()));
        }

        [Test]
        public void EachPanelHasABrightPictureOnADarkBackdrop()
        {
            for (int p = 0; p < 4; p++)
            {
                int bright = 0, dark = 0;
                for (int i = 0; i < 200; i++)
                {
                    for (int j = 0; j < 200; j++)
                    {
                        var c = At(p, i / 199.0, j / 199.0);
                        int sum = c.R + c.G + c.B;
                        if (sum > 400) bright++;
                        if (sum < 120) dark++;
                    }
                }
                Assert.Greater(bright, 150, $"panel {p} has something to look at");
                Assert.Greater(dark, 200 * 200 / 2, $"panel {p} is mostly dark, like a screen");
            }
        }

        [Test]
        public void ThePicturesAreTheRightWayUp()
        {
            Assert.Greater(Count(0, 0.08, 0.20, 0.45, 0.36, 0xd9f6ff), 40, "KENZAI, in the lower left of the first panel");
            Assert.AreEqual(0, Count(0, 0.08, 0.80, 0.45, 0.95, 0xd9f6ff), "and not at the top");
            Assert.Greater(Count(3, 0.60, 0.25, 0.95, 0.50, 0xff2f52), 60, "the red 88, to the right of the data panel's middle");
            Assert.Greater(Count(2, 0.05, 0.60, 0.25, 0.95, 0x8a2bff, 90), 20, "the violet kana column down the face panel's left");
        }

        [Test]
        public void EachPanelHasATickerAlongItsBottom()
        {
            uint[] colours = { 0x7ff0ff, 0xffb0d8, 0xff7fc0, 0x8dff2a };
            for (int p = 0; p < 4; p++)
            {
                Assert.Greater(Count(p, 0.0, 0.01, 1.0, BoardArt.TickerBand - 0.01, colours[p], 90), 100, $"panel {p} ticker text");
                Assert.Less(Count(p, 0.0, 0.30, 1.0, 0.70, colours[p], 30), 3000, $"panel {p}: and the ticker's colour is not all over the picture");
            }
        }

        [Test]
        public void NoLetterOfATickerIsCutByTheEdgeSoItCanWrapWithoutASeam()
        {
            for (int p = 0; p < 4; p++)
            {
                for (double lv = 0.01; lv < BoardArt.TickerBand - 0.01; lv += 0.004)
                {
                    foreach (double lu in new[] { 0.0, 0.002, 0.004, 0.996, 1.0 })
                    {
                        var c = At(p, lu, lv);
                        Assert.Less(c.R + c.G + c.B, 250, $"panel {p} has ticker ink at the edge ({lu}, {lv:0.000})");
                    }
                }
            }
        }

        [Test]
        public void TheTickerRepeatsExactlyAcrossThePanel()
        {
            // repeated a whole number of times: shifting by the width / n lines the ink up again, for some n
            for (int p = 0; p < 4; p++)
            {
                bool periodic = false;
                for (int n = 1; n <= 4 && !periodic; n++)
                {
                    int mismatches = 0, ink = 0;
                    for (int i = 0; i < 400; i++)
                    {
                        for (int j = 0; j < 12; j++)
                        {
                            double lu = i / 400.0 / n, lv = 0.02 + j * 0.008;
                            var a = At(p, lu, lv);
                            bool aOn = a.R + a.G + a.B > 300;
                            if (aOn) ink++;
                            for (int k = 1; k < n; k++)
                            {
                                var b = At(p, lu + k / (double)n, lv);
                                if ((b.R + b.G + b.B > 300) != aOn) mismatches++;
                            }
                        }
                    }
                    if (ink > 30 && mismatches < ink / 10 + 5) periodic = true;
                }
                Assert.IsTrue(periodic, $"panel {p}'s ticker does not repeat evenly");
            }
        }

        [Test]
        public void DrawingThePanelsTakesAFractionOfASecond()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BoardArt.Render();
            Assert.Less(sw.Elapsed.TotalMilliseconds, 4000);
        }
    }
}
