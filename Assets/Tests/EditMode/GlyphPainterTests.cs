using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class GlyphPainterTests
    {
        static readonly Color4 White = new Color4(1, 1, 1);

        static (int x0, int x1, int y0, int y1, int count) Extent(Canvas2D c, float minAlpha = 0.5f)
        {
            int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1, n = 0;
            float[] a = c.ToAlpha();
            for (int y = 0; y < c.Height; y++)
            {
                for (int x = 0; x < c.Width; x++)
                {
                    if (a[y * c.Width + x] < minAlpha) continue;
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); n++;
                }
            }
            return (x0, x1, y0, y1, n);
        }

        [Test]
        public void TextSitsOnItsBaselineAndRisesByItsCapHeight()
        {
            var c = new Canvas2D(200, 80);
            GlyphPainter.Text(c, "HI", 20, 60, 40, GlyphPainter.Normal, TextAlign.Left, White);
            var e = Extent(c);
            Assert.AreEqual(20, e.y0, 4, "the top is a cap height above the baseline");
            Assert.AreEqual(60, e.y1, 4, "the bottom is on the baseline");
            Assert.AreEqual(20, e.x0, 4, "starts at x");
        }

        [Test]
        public void AlignmentPlacesTheTextAtItsLeftCentreOrRightEdge()
        {
            double width = GlyphPainter.Width("OK", 30);
            var left = new Canvas2D(300, 60); GlyphPainter.Text(left, "OK", 100, 45, 30, GlyphPainter.Normal, TextAlign.Left, White);
            var centre = new Canvas2D(300, 60); GlyphPainter.Text(centre, "OK", 150, 45, 30, GlyphPainter.Normal, TextAlign.Centre, White);
            var right = new Canvas2D(300, 60); GlyphPainter.Text(right, "OK", 200, 45, 30, GlyphPainter.Normal, TextAlign.Right, White);
            Assert.AreEqual(100, Extent(left).x0, 4);
            Assert.AreEqual(150 - width / 2, Extent(centre).x0, 4);
            Assert.AreEqual(200, Extent(right).x1, 4);
            Assert.AreEqual(200 - width, Extent(right).x0, 4);
        }

        [Test]
        public void HeavierWeightsDrawFatterLetters()
        {
            var light = new Canvas2D(100, 60); GlyphPainter.Text(light, "I", 40, 50, 40, GlyphPainter.Light, TextAlign.Left, White);
            var heavy = new Canvas2D(100, 60); GlyphPainter.Text(heavy, "I", 40, 50, 40, GlyphPainter.Heavy, TextAlign.Left, White);
            Assert.Greater(Extent(heavy).count, Extent(light).count * 1.8);
        }

        [Test]
        public void ASpaceAdvancesThePenAndDrawsNothing()
        {
            var one = new Canvas2D(200, 60); GlyphPainter.Text(one, "AB", 10, 50, 30, GlyphPainter.Normal, TextAlign.Left, White);
            var spaced = new Canvas2D(200, 60); GlyphPainter.Text(spaced, "A B", 10, 50, 30, GlyphPainter.Normal, TextAlign.Left, White);
            Assert.Greater(Extent(spaced).x1, Extent(one).x1 + 8);
        }

        [Test]
        public void ASymbolFillsItsSquare()
        {
            var c = new Canvas2D(120, 120);
            GlyphPainter.Symbol(c, "kYama", 20, 20, 80, 6, White);
            var e = Extent(c);
            Assert.That(e.x0, Is.InRange(20, 40));
            Assert.That(e.x1, Is.InRange(80, 100));
            Assert.That(e.y1, Is.InRange(80, 100));
        }

        [Test]
        public void ASymbolInARectangleIsStretchedToIt()
        {
            var c = new Canvas2D(200, 100);
            GlyphPainter.SymbolInRect(c, "arrow", 0, 0, 200, 100, 6, White);
            Assert.Greater(Extent(c).x1, 150, "wide, because the rectangle is");
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            var c = new Canvas2D(10, 10);
            Assert.Throws<ArgumentNullException>(() => GlyphPainter.Text(null, "A", 0, 0, 10, 0.1, TextAlign.Left, White));
            Assert.Throws<ArgumentNullException>(() => GlyphPainter.Text(c, null, 0, 0, 10, 0.1, TextAlign.Left, White));
            Assert.Throws<ArgumentOutOfRangeException>(() => GlyphPainter.Text(c, "A", 0, 0, 0, 0.1, TextAlign.Left, White));
            Assert.Throws<ArgumentException>(() => GlyphPainter.Text(c, "@", 0, 0, 5, 0.1, TextAlign.Left, White));
            Assert.Throws<ArgumentException>(() => GlyphPainter.Symbol(c, "nope", 0, 0, 5, 1, White));
        }

        // ---------------------------------------------------------------- blur

        [Test]
        public void ABlurSpreadsAPointAndKeepsMostOfItsEnergy()
        {
            const int n = 101;
            var data = new float[n * n];
            data[50 * n + 50] = 100f;
            AlphaBlur.Apply(data, n, n, 6);
            Assert.Less(data[50 * n + 50], 100f);
            Assert.Greater(data[50 * n + 56], 0f, "it has spread sideways");
            Assert.AreEqual(100.0, data.Sum(), 1.0, "nothing is lost while the point is far from the edge");
        }

        [Test]
        public void ABiggerSigmaSpreadsFurtherAndFlatter()
        {
            const int n = 201;
            float[] Blurred(double sigma)
            {
                var d = new float[n * n];
                d[100 * n + 100] = 1f;
                AlphaBlur.Apply(d, n, n, sigma);
                return d;
            }
            float[] small = Blurred(3), big = Blurred(12);
            Assert.Greater(small[100 * n + 100], big[100 * n + 100]);
            Assert.Greater(big[100 * n + 125], small[100 * n + 125], "far out, the wide blur has more");
        }

        [Test]
        public void ABlurIsSymmetric()
        {
            const int n = 101;
            var d = new float[n * n];
            d[50 * n + 50] = 1f;
            AlphaBlur.Apply(d, n, n, 5);
            Assert.AreEqual(d[50 * n + 55], d[50 * n + 45], 1e-6f);
            Assert.AreEqual(d[55 * n + 50], d[45 * n + 50], 1e-6f);
            Assert.AreEqual(d[50 * n + 55], d[55 * n + 50], 1e-6f, "and the same both ways");
        }

        [Test]
        public void ZeroSigmaLeavesTheImageAlone()
        {
            var d = new float[16];
            d[5] = 1f;
            AlphaBlur.Apply(d, 4, 4, 0);
            Assert.AreEqual(1f, d[5]);
            Assert.AreEqual(0f, d[6]);
        }

        [Test]
        public void ABlurNeverBrightensBeyondTheMaximum()
        {
            var d = new float[40 * 40];
            for (int i = 0; i < d.Length; i++) d[i] = (i % 3 == 0) ? 1f : 0f;
            AlphaBlur.Apply(d, 40, 40, 4);
            Assert.LessOrEqual(d.Max(), 1.0001f);
            Assert.GreaterOrEqual(d.Min(), 0f);
        }

        [Test]
        public void BlurRefusesAMismatchedBuffer()
        {
            Assert.Throws<ArgumentException>(() => AlphaBlur.Apply(new float[10], 4, 4, 2));
            Assert.Throws<ArgumentNullException>(() => AlphaBlur.Apply(null, 4, 4, 2));
        }

        // ---------------------------------------------------------------- canvas additions

        [Test]
        public void APolygonFillHasTheAreaOfThePolygon()
        {
            var c = new Canvas2D(60, 60);
            c.FillPolygon(new List<(double, double)> { (10, 10), (40, 10), (10, 40) }, White);   // a right triangle, legs 30: area 450
            Assert.AreEqual(450.0, c.ToAlpha().Sum(), 6.0);
            Assert.AreEqual(1f, c.Pixel(14, 14).A, 0.01f);
            Assert.AreEqual(0f, c.Pixel(35, 35).A);
        }

        [Test]
        public void APolygonWithAHoleIsEvenOdd()
        {
            var c = new Canvas2D(60, 60);
            var outer = new List<(double, double)> { (5, 5), (55, 5), (55, 55), (5, 55), (5, 5), (20, 20), (40, 20), (40, 40), (20, 40), (20, 20) };
            c.FillPolygon(outer, White);
            Assert.AreEqual(1f, c.Pixel(10, 30).A, 0.01f, "in the ring");
            Assert.AreEqual(0f, c.Pixel(30, 30).A, 0.01f, "in the hole");
        }

        [Test]
        public void ALinearGradientRunsFromItsFirstStopToItsLast()
        {
            var c = new Canvas2D(100, 20);
            c.FillLinearGradientRect(0, 0, 100, 20, 0, 0, 100, 0, new[]
            {
                new GradientStop(0, new Color4(1, 0, 0)),
                new GradientStop(1, new Color4(0, 0, 1)),
            });
            Assert.AreEqual(1f, c.Pixel(1, 10).R, 0.05f);
            Assert.AreEqual(1f, c.Pixel(98, 10).B, 0.05f);
            Assert.AreEqual(0.5f, c.Pixel(50, 10).R, 0.05f);
        }

        [Test]
        public void AGradientCanFillAPolygon()
        {
            var c = new Canvas2D(60, 60);
            c.FillPolygonLinearGradient(new List<(double, double)> { (10, 10), (50, 10), (50, 50), (10, 50) }, 10, 0, 50, 0, new[]
            {
                new GradientStop(0, new Color4(1, 1, 1, 0)),
                new GradientStop(1, new Color4(1, 1, 1, 1)),
            });
            Assert.Less(c.Pixel(12, 30).A, 0.15f);
            Assert.Greater(c.Pixel(48, 30).A, 0.85f);
            Assert.AreEqual(0f, c.Pixel(5, 30).A);
        }

        [Test]
        public void AnEllipseOutlineIsAnOutline()
        {
            var c = new Canvas2D(100, 60);
            c.StrokeEllipse(50, 30, 40, 20, 0, 2 * Math.PI, 3, White);
            Assert.AreEqual(1f, c.Pixel(90, 30).A, 0.1f);
            Assert.AreEqual(1f, c.Pixel(50, 50).A, 0.1f);
            Assert.AreEqual(0f, c.Pixel(50, 30).A, "the middle is empty");
        }

        [Test]
        public void PolygonAndGradientBadArgumentsFailLoudly()
        {
            var c = new Canvas2D(10, 10);
            Assert.Throws<ArgumentException>(() => c.FillPolygon(new List<(double, double)> { (0, 0), (1, 1) }, White));
            Assert.Throws<ArgumentException>(() => c.FillLinearGradientRect(0, 0, 5, 5, 0, 0, 5, 0, new[] { new GradientStop(0, White) }));
        }
    }
}
