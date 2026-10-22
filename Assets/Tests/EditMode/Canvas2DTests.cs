using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class Canvas2DTests
    {
        static readonly Color4 Red = new Color4(1, 0, 0);
        static readonly Color4 Blue = new Color4(0, 0, 1);

        [Test]
        public void AFilledRectangleIsOpaqueInsideAndEmptyOutside()
        {
            var c = new Canvas2D(16, 16);
            c.FillRect(4, 4, 8, 8, Red);
            Assert.AreEqual(1f, c.Pixel(8, 8).A, 1e-5f);
            Assert.AreEqual(1f, c.Pixel(8, 8).R, 1e-5f);
            Assert.AreEqual(0f, c.Pixel(2, 2).A);
            Assert.AreEqual(0f, c.Pixel(13, 8).A);
        }

        [Test]
        public void ARectangleEdgeIsAntialiased()
        {
            var c = new Canvas2D(16, 16);
            c.FillRect(0, 0, 8.5, 16, Red);
            Assert.AreEqual(1f, c.Pixel(7, 8).A, 1e-5f);
            Assert.AreEqual(0.5f, c.Pixel(8, 8).A, 0.02f, "the pixel the edge cuts through is half covered");
            Assert.AreEqual(0f, c.Pixel(9, 8).A);
        }

        [Test]
        public void ADiscIsRoundAndCentred()
        {
            var c = new Canvas2D(32, 32);
            c.FillCircle(16, 16, 8, Red);
            Assert.AreEqual(1f, c.Pixel(16, 16).A, 1e-5f);
            Assert.AreEqual(1f, c.Pixel(16, 9).A, 1e-3f);
            Assert.AreEqual(0f, c.Pixel(16, 6).A);
            Assert.AreEqual(0f, c.Pixel(23, 23).A, "the corner of the box is outside the circle");
        }

        [Test]
        public void ARotatedRectangleTurnsAboutItsCentre()
        {
            var c = new Canvas2D(40, 40);
            c.FillRotatedRect(20, 20, 30, 4, Math.PI / 2, Red);   // a long bar turned to stand upright
            Assert.AreEqual(1f, c.Pixel(20, 8).A, 1e-3f);
            Assert.AreEqual(1f, c.Pixel(20, 31).A, 1e-3f);
            Assert.AreEqual(0f, c.Pixel(8, 20).A);
        }

        [Test]
        public void ANewShapeIsCompositedOverWhatIsThere()
        {
            var c = new Canvas2D(8, 8);
            c.FillRect(0, 0, 8, 8, Red);
            c.FillRect(0, 0, 8, 8, Blue, 0.5);
            Color4 p = c.Pixel(4, 4);
            Assert.AreEqual(1f, p.A, 1e-5f);
            Assert.AreEqual(0.5f, p.R, 1e-5f);
            Assert.AreEqual(0.5f, p.B, 1e-5f);
        }

        [Test]
        public void TheClipRectangleProtectsEverythingOutsideIt()
        {
            var c = new Canvas2D(16, 16);
            c.SetClip(4, 4, 4, 4);
            c.FillRect(0, 0, 16, 16, Red);
            Assert.AreEqual(1f, c.Pixel(5, 5).A, 1e-5f);
            Assert.AreEqual(0f, c.Pixel(2, 2).A);
            Assert.AreEqual(0f, c.Pixel(9, 9).A);
            c.ClearClip();
            c.FillRect(0, 0, 16, 16, Blue);
            Assert.AreEqual(1f, c.Pixel(2, 2).A, 1e-5f);
        }

        [Test]
        public void AStrokeThatDoublesBackDoesNotDarkenWhereItOverlapsItself()
        {
            var once = new Canvas2D(40, 20);
            once.StrokePolyline(new List<(double, double)> { (5, 10), (35, 10) }, 6, Red, 0.5);
            var twice = new Canvas2D(40, 20);
            twice.StrokePolyline(new List<(double, double)> { (5, 10), (35, 10), (5, 10) }, 6, Red, 0.5);
            Assert.AreEqual(once.Pixel(20, 10).A, twice.Pixel(20, 10).A, 1e-5f, "one stroke is composited once");
            Assert.AreEqual(0.5f, twice.Pixel(20, 10).A, 0.01f);
        }

        [Test]
        public void AStrokeHasRoundEndsAndTheRightWidth()
        {
            var c = new Canvas2D(40, 20);
            c.StrokePolyline(new List<(double, double)> { (10, 10), (30, 10) }, 6, Red);
            Assert.AreEqual(1f, c.Pixel(20, 8).A, 1e-3f);
            Assert.AreEqual(1f, c.Pixel(20, 12).A, 1e-3f);
            Assert.AreEqual(0f, c.Pixel(20, 5).A);
            Assert.AreEqual(1f, c.Pixel(8, 10).A, 0.05f, "the round cap reaches past the end point by half the width");
            Assert.AreEqual(0f, c.Pixel(5, 10).A);
        }

        [Test]
        public void AOnePointStrokeIsADot()
        {
            var c = new Canvas2D(16, 16);
            c.StrokePolyline(new List<(double, double)> { (8, 8) }, 6, Red);
            Assert.AreEqual(1f, c.Pixel(8, 8).A, 1e-3f);
            Assert.AreEqual(0f, c.Pixel(14, 8).A);
        }

        [Test]
        public void AnArcSweepsClockwiseFromItsStart()
        {
            var c = new Canvas2D(40, 40);
            c.StrokeArc(20, 20, 12, 0, Math.PI / 2, 3, Red);   // from east round to south (y points down)
            Assert.AreEqual(1f, c.Pixel(32, 20).A, 0.05f, "east end");
            Assert.AreEqual(1f, c.Pixel(20, 32).A, 0.05f, "south end");
            Assert.AreEqual(0f, c.Pixel(8, 20).A, "nothing on the west");
            Assert.AreEqual(0f, c.Pixel(20, 8).A, "nothing on the north");
        }

        [Test]
        public void AnArcWhoseEndIsBeforeItsStartWrapsAroundTheLongWay()
        {
            var c = new Canvas2D(40, 40);
            c.StrokeArc(20, 20, 12, Math.PI / 2, 0, 3, Red);   // south, then west, north, east: three quarters
            Assert.AreEqual(1f, c.Pixel(8, 20).A, 0.05f, "west is on it");
            Assert.AreEqual(1f, c.Pixel(20, 8).A, 0.05f, "north is on it");
        }

        [Test]
        public void ARadialGradientRunsFromItsFirstStopToItsLast()
        {
            var c = new Canvas2D(40, 40);
            c.FillRadialGradient(20, 20, 16, new[]
            {
                new GradientStop(0, new Color4(1, 1, 1, 1f)),
                new GradientStop(1, new Color4(1, 1, 1, 0f)),
            });
            Assert.AreEqual(1f, c.Pixel(20, 20).A, 0.1f);
            Assert.AreEqual(0.5f, c.Pixel(28, 20).A, 0.1f, "halfway out is halfway faded");
            Assert.AreEqual(0f, c.Pixel(2, 20).A);
        }

        [Test]
        public void ErasingTakesAwayWhatIsUnderTheCircleOnly()
        {
            var c = new Canvas2D(32, 32);
            c.FillRect(0, 0, 32, 32, Red);
            c.EraseCircle(16, 16, 6);
            Assert.AreEqual(0f, c.Pixel(16, 16).A, 1e-5f);
            Assert.AreEqual(1f, c.Pixel(2, 2).A, 1e-5f);
        }

        [Test]
        public void ARadialEraseIsStrongestInTheMiddle()
        {
            var c = new Canvas2D(32, 32);
            c.FillRect(0, 0, 32, 32, Red);
            c.EraseRadial(16, 16, 10, 0.5);
            Assert.AreEqual(0.5f, c.Pixel(16, 16).A, 0.06f);
            Assert.Greater(c.Pixel(22, 16).A, c.Pixel(16, 16).A);
            Assert.AreEqual(1f, c.Pixel(2, 2).A, 1e-5f);
        }

        [Test]
        public void BytesAreStraightAlphaNotPremultiplied()
        {
            var c = new Canvas2D(2, 1);
            c.FillRect(0, 0, 1, 1, Red, 0.5);
            byte[] b = c.ToBytes();
            Assert.AreEqual(255, b[0], 1, "full red, not halved");
            Assert.AreEqual(0, b[1]);
            Assert.AreEqual(128, b[3], 1);
            Assert.AreEqual(0, b[7], "an untouched pixel is fully transparent");
        }

        [Test]
        public void ABezierCurveStartsAndEndsOnItsEndpoints()
        {
            var pts = new List<(double x, double y)>(Canvas2D.Bezier((0, 0), (0, 10), (10, 10), (10, 0), 24));
            Assert.AreEqual(25, pts.Count);
            Assert.AreEqual((0.0, 0.0), pts[0]);
            Assert.AreEqual(10.0, pts[24].x, 1e-9);
            Assert.AreEqual(0.0, pts[24].y, 1e-9);
            Assert.AreEqual(7.5, pts[12].y, 1e-9, "the middle of this curve is three quarters of the way up its control points");
        }

        [Test]
        public void ColoursConvertFromBytesAndHex()
        {
            Color4 c = Color4.Bytes(255, 128, 0, 0.5);
            Assert.AreEqual((1f, 128 / 255f, 0f, 0.5f), (c.R, c.G, c.B, c.A));
            Color4 h = Color4.Hex(0x2af0ff);
            Assert.AreEqual(0x2a / 255f, h.R, 1e-6f);
            Assert.AreEqual(0xff / 255f, h.B, 1e-6f);
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Canvas2D(0, 4));
            var c = new Canvas2D(4, 4);
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Pixel(4, 0));
            Assert.Throws<ArgumentException>(() => c.FillRadialGradient(2, 2, 2, new[] { new GradientStop(0, Red) }));
            Assert.Throws<ArgumentException>(() => c.StrokePolyline(new List<(double, double)>(), 2, Red));
        }

        [Test]
        public void DrawingOutsideTheCanvasIsHarmless()
        {
            var c = new Canvas2D(8, 8);
            c.FillCircle(-20, -20, 5, Red);
            c.FillRect(100, 100, 5, 5, Red);
            c.StrokePolyline(new List<(double, double)> { (-10, -10), (-5, -5) }, 4, Red);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++) Assert.AreEqual(0f, c.Pixel(x, y).A);
            }
        }
    }
}
