using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class PointGridTests
    {
        static List<(double, double, double)> RandomPoints(int n, uint seed, double extent)
        {
            var rng = new Rng(seed);
            var points = new List<(double, double, double)>();
            for (int i = 0; i < n; i++) points.Add(((rng.Next() - 0.5) * 2 * extent, rng.Next() * 20, (rng.Next() - 0.5) * 2 * extent));
            return points;
        }

        static int BruteForce(IReadOnlyList<(double x, double y, double z)> points, double x, double y, double z, double range)
        {
            double best = range * range;
            int found = -1;
            for (int i = 0; i < points.Count; i++)
            {
                double dx = points[i].x - x, dy = points[i].y - y, dz = points[i].z - z;
                double d = dx * dx + dy * dy + dz * dz;
                if (d < best)
                {
                    best = d;
                    found = i;
                }
            }
            return found;
        }

        [Test]
        public void ItFindsTheSamePointAsLookingAtThemAllWhereverYouStandAndHoweverFarYouLook()
        {
            var points = RandomPoints(3000, 5, 220);
            var grid = new PointGrid(points, 70);
            var rng = new Rng(9);
            foreach (double range in new[] { 5.0, 30.0, 70.0, 400.0, 1e6 })
            {
                for (int q = 0; q < 200; q++)
                {
                    double x = (rng.Next() - 0.5) * 600, y = rng.Next() * 20, z = (rng.Next() - 0.5) * 600;
                    Assert.AreEqual(BruteForce(points, x, y, z, range), grid.Nearest(x, y, z, range), $"({x:0.0}, {y:0.0}, {z:0.0}) within {range}");
                }
            }
        }

        [Test]
        public void NothingInRangeIsMinusOneAndAnEmptyGridHasNothing()
        {
            var grid = new PointGrid(new List<(double, double, double)> { (100, 0, 100) }, 70);
            Assert.AreEqual(-1, grid.Nearest(0, 0, 0, 50));
            Assert.AreEqual(0, grid.Nearest(90, 0, 90, 50));
            var empty = new PointGrid(new List<(double, double, double)>(), 70);
            Assert.AreEqual(0, empty.Count);
            Assert.AreEqual(-1, empty.Nearest(0, 0, 0, 1000));
        }

        [Test]
        public void AnExactlyRangeAwayPointIsOutAndHeightCounts()
        {
            var grid = new PointGrid(new List<(double, double, double)> { (10, 0, 0), (0, 3, 0) }, 8);
            Assert.AreEqual(-1, grid.Nearest(0, 0, 0, 3), "3 m up is not less than 3 m");
            Assert.AreEqual(1, grid.Nearest(0, 0, 0, 3.001), "but a hair more is");
            Assert.AreEqual(1, grid.Nearest(0, 0, 0, 11), "and 3 m is nearer than 10");
            Assert.AreEqual(-1, grid.Nearest(0, 0, 0, 2.9));
        }

        [Test]
        public void TheEarlierOfTwoEquallyNearPointsWinsAcrossCells()
        {
            // 8 m cells: the two are in different cells, both 4 m from the origin
            var grid = new PointGrid(new List<(double, double, double)> { (4, 0, 0), (-4, 0, 0), (0, 0, 4) }, 8);
            Assert.AreEqual(0, grid.Nearest(0, 0, 0, 10));
            var reversed = new PointGrid(new List<(double, double, double)> { (0, 0, 4), (-4, 0, 0), (4, 0, 0) }, 8);
            Assert.AreEqual(0, reversed.Nearest(0, 0, 0, 10));
        }

        [Test]
        public void PointsOnEitherSideOfZeroAndOnCellEdgesAreFound()
        {
            var points = new List<(double, double, double)> { (-0.0001, 0, -0.0001), (-70, 0, -70), (70, 0, 70), (-140.5, 2, 33) };
            var grid = new PointGrid(points, 70);
            for (int i = 0; i < points.Count; i++) Assert.AreEqual(i, grid.Nearest(points[i].Item1 + 0.01, points[i].Item2, points[i].Item3 - 0.01, 1), "point " + i);
            Assert.AreEqual((-140.5, 2.0, 33.0), grid.Point(3));
        }

        [Test]
        public void BadInputFailsLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => new PointGrid(null, 70));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PointGrid(new List<(double, double, double)>(), 0));
            Assert.Throws<ArgumentException>(() => new PointGrid(new List<(double, double, double)> { (double.NaN, 0, 0) }, 70));
            var grid = new PointGrid(new List<(double, double, double)> { (1, 1, 1) }, 70);
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.Nearest(0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.Point(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.Point(-1));
        }
    }
}
