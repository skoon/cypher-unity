using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class CityWindowTests
    {
        [Test]
        public void AroundPlazaRadiusOneIsTheThreeByThreeBlock()
        {
            CityWindow w = CityWindow.AroundPlaza(1);
            Assert.AreEqual((2, 2, 4, 4), (w.Gx0, w.Gz0, w.Gx1, w.Gz1));
        }

        [Test]
        public void ContainsIsInclusiveOnAllEdges()
        {
            CityWindow w = CityWindow.AroundPlaza(1);
            Assert.IsTrue(w.Contains(2, 2));
            Assert.IsTrue(w.Contains(4, 4));
            Assert.IsTrue(w.Contains(3, 3));
            Assert.IsFalse(w.Contains(1, 3));
            Assert.IsFalse(w.Contains(3, 5));
        }

        [Test]
        public void RadiusZeroIsJustThePlazaCell()
        {
            CityWindow w = CityWindow.AroundPlaza(0);
            Assert.IsTrue(w.Contains(3, 3));
            Assert.IsFalse(w.Contains(2, 3));
        }

        [Test]
        public void RoadExtentIncludesTheHighSideRoad()
        {
            Rect2 e = CityWindow.AroundPlaza(0).RoadExtent;
            Assert.AreEqual(CityLayout.Base + 3 * CityLayout.Cell, e.X0, 1e-9);
            Assert.AreEqual(CityLayout.Base + 4 * CityLayout.Cell + CityLayout.Road, e.X1, 1e-9);
            Assert.AreEqual(e.X0, e.Z0, 1e-9, "grid is square");
        }

        [Test]
        public void WindowsOutsideTheGridFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CityWindow.AroundPlaza(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => CityWindow.AroundPlaza(-1));
            Assert.Throws<ArgumentException>(() => new CityWindow(3, 3, 2, 3));
        }
    }
}
