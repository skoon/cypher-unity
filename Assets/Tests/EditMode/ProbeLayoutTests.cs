using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class ProbeLayoutTests
    {
        static readonly CityWindow Whole = CityWindow.AroundPlaza(3);

        [Test]
        public void ThreeByThreeProbesCoverTheWholeCityWithRoomToSpare()
        {
            IReadOnlyList<ProbeRegion> probes = ProbeLayout.Build(Whole);
            Assert.AreEqual(9, probes.Count);
            Rect2 e = Whole.RoadExtent;
            for (double x = e.X0; x <= e.X1; x += 5)
            {
                for (double z = e.Z0; z <= e.Z1; z += 5)
                {
                    Assert.IsTrue(probes.Any(p => p.ContainsXz(x, z)), $"nothing covers ({x}, {z})");
                }
            }
        }

        [Test]
        public void EachProbeIsCapturedFromTheMiddleOfARoadJunction()
        {
            foreach (ProbeRegion p in ProbeLayout.Build(Whole))
            {
                Assert.IsTrue(CityLayout.IsOnRoad(p.CaptureX, p.CaptureZ), "on a road");
                double lx = ((p.CaptureX - CityLayout.Base) % CityLayout.Cell + CityLayout.Cell) % CityLayout.Cell;
                double lz = ((p.CaptureZ - CityLayout.Base) % CityLayout.Cell + CityLayout.Cell) % CityLayout.Cell;
                Assert.AreEqual(CityLayout.Road / 2, lx, 1e-6, "in the middle of the corridor across");
                Assert.AreEqual(CityLayout.Road / 2, lz, 1e-6, "and along: a junction, with nothing around it but the open crossing");
                Assert.That(p.CaptureY, Is.GreaterThan(2).And.LessThan(15), "above the street, below the roofs");
            }
        }

        [Test]
        public void EachCaptureSitsInsideItsOwnBox()
        {
            foreach (ProbeRegion p in ProbeLayout.Build(Whole))
            {
                Assert.IsTrue(p.ContainsXz(p.CaptureX, p.CaptureZ));
                Assert.That(p.CaptureY, Is.InRange(p.BoxCentreY - p.SizeY / 2, p.BoxCentreY + p.SizeY / 2));
            }
        }

        [Test]
        public void NeighbouringBoxesOverlapSoTheyCanBlend()
        {
            IReadOnlyList<ProbeRegion> probes = ProbeLayout.Build(Whole);
            ProbeRegion left = probes[0], middle = probes[1], right = probes[2];
            double overlapLM = (left.BoxCentreX + left.SizeX / 2) - (middle.BoxCentreX - middle.SizeX / 2);
            double overlapMR = (middle.BoxCentreX + middle.SizeX / 2) - (right.BoxCentreX - right.SizeX / 2);
            Assert.AreEqual(2 * ProbeLayout.Overlap, overlapLM, 1e-6);
            Assert.AreEqual(2 * ProbeLayout.Overlap, overlapMR, 1e-6);
        }

        [Test]
        public void OneProbePerAxisIsASingleBoxOverTheWindow()
        {
            ProbeRegion only = ProbeLayout.Build(CityWindow.AroundPlaza(1), 1).Single();
            Rect2 e = CityWindow.AroundPlaza(1).RoadExtent;
            Assert.AreEqual(e.CentreX, only.BoxCentreX, 1e-9);
            Assert.AreEqual(e.Width + 2 * ProbeLayout.Overlap, only.SizeX, 1e-9);
            Assert.IsTrue(CityLayout.IsOnRoad(only.CaptureX, only.CaptureZ));
        }

        [Test]
        public void AnOuterMarginCoversTheApronToo()
        {
            IReadOnlyList<ProbeRegion> probes = ProbeLayout.Build(Whole, 3, WorldEdge.Apron);
            for (double x = -WorldEdge.PlayHalf; x <= WorldEdge.PlayHalf; x += 5)
            {
                foreach (double z in new[] { -WorldEdge.PlayHalf, 0, WorldEdge.PlayHalf })
                {
                    Assert.IsTrue(probes.Any(p => p.ContainsXz(x, z)), $"nothing covers ({x}, {z})");
                    Assert.IsTrue(probes.Any(p => p.ContainsXz(z, x)), $"nothing covers ({z}, {x})");
                }
            }
            Assert.IsTrue(probes.All(p => CityLayout.IsOnRoad(p.CaptureX, p.CaptureZ)), "capture points are unchanged by the margin");
            Assert.Throws<ArgumentOutOfRangeException>(() => ProbeLayout.Build(Whole, 3, -1));
        }

        [Test]
        public void AProbeNeedsAtLeastOnePerAxis()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ProbeLayout.Build(Whole, 0));
        }
    }
}
