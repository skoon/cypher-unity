using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class SkylineRingTests
    {
        static readonly IReadOnlyList<SkylineTower> Towers = SkylineRing.Plan();

        [Test]
        public void ThePlanIsTheSameEveryTimeAndDiffersBySeed()
        {
            IReadOnlyList<SkylineTower> again = SkylineRing.Plan();
            Assert.AreEqual(Towers.Count, again.Count);
            for (int i = 0; i < Towers.Count; i++)
            {
                Assert.AreEqual(Towers[i].Footprint.X0, again[i].Footprint.X0);
                Assert.AreEqual(Towers[i].Height, again[i].Height);
            }
            Assert.AreNotEqual(Towers.Sum(t => t.Height), SkylineRing.Plan(SkylineRing.DefaultSeed + 1).Sum(t => t.Height));
        }

        [Test]
        public void EveryTowerStandsOutsideTheWallAndInsideTheRoadbed()
        {
            double wallOuter = WorldEdge.PlayHalf + WorldEdge.WallThickness;
            double roadbedHalf = StreetMesher.RoadbedSize / 2;
            foreach (SkylineTower t in Towers)
            {
                Rect2 r = t.Footprint;
                double outward = Math.Max(MinAbs(r.X0, r.X1), MinAbs(r.Z0, r.Z1));
                Assert.Greater(outward, wallOuter + SkylineRing.Gap - SkylineRing.Jitter - 1e-6, $"tower at ({r.CentreX:0}, {r.CentreZ:0}) is too close to the wall");
                Assert.LessOrEqual(Math.Max(Math.Max(Math.Abs(r.X0), Math.Abs(r.X1)), Math.Max(Math.Abs(r.Z0), Math.Abs(r.Z1))), roadbedHalf - 8, "off the edge of the ground");
            }
        }

        /// <summary>Distance from zero of the nearer end of an interval, or zero when it straddles zero.</summary>
        static double MinAbs(double a, double b) => a * b <= 0 ? 0 : Math.Min(Math.Abs(a), Math.Abs(b));

        [Test]
        public void NoTwoTowersOverlap()
        {
            for (int i = 0; i < Towers.Count; i++)
            {
                for (int j = i + 1; j < Towers.Count; j++)
                {
                    Rect2 a = Towers[i].Footprint, b = Towers[j].Footprint;
                    bool overlap = a.X0 < b.X1 && b.X0 < a.X1 && a.Z0 < b.Z1 && b.Z0 < a.Z1;
                    Assert.IsFalse(overlap, $"towers {i} and {j} overlap");
                }
            }
        }

        [Test]
        public void TowersRunAllRoundAndRiseToDifferentHeights()
        {
            for (int side = 0; side < 4; side++) Assert.Greater(Towers.Count(t => t.Side == side), 20, $"side {side}");
            Assert.That(Towers.Min(t => t.Height), Is.GreaterThanOrEqualTo(SkylineRing.MinHeight - CityLayout.FloorHeight));
            Assert.That(Towers.Max(t => t.Height), Is.LessThanOrEqualTo(SkylineRing.MaxHeight + CityLayout.FloorHeight));
            Assert.Greater(Towers.Max(t => t.Height) - Towers.Min(t => t.Height), 100, "a skyline, not a flat wall");
            Assert.Greater(Towers.Where(t => t.Row == 1).Average(t => t.Height), Towers.Where(t => t.Row == 0).Average(t => t.Height), "the outer row is the taller");
        }

        [Test]
        public void TheMeshesAreAFewSegmentsWithFewTriangles()
        {
            IReadOnlyList<SkylineSegment> segments = SkylineRing.Build();
            Assert.That(segments.Count, Is.InRange(8, 64), "enough pieces to cull, not so many that they cost draw calls");
            Assert.AreEqual(Towers.Count, segments.Sum(s => s.TowerCount));
            Assert.That(segments.Sum(s => s.TriangleCount), Is.GreaterThanOrEqualTo(Towers.Count * 10).And.LessThan(12000));
            Assert.IsTrue(segments.All(s => s.Walls.TriangleCount > 0 && s.Roofs.TriangleCount > 0));
        }

        [Test]
        public void TheMeshesStartAtTheGroundAndReachTheTowerHeights()
        {
            IReadOnlyList<SkylineSegment> segments = SkylineRing.Build();
            float maxY = float.NegativeInfinity, minY = float.PositiveInfinity;
            foreach (SkylineSegment s in segments)
            {
                for (int i = 1; i < s.Walls.Positions.Length; i += 3)
                {
                    maxY = Math.Max(maxY, s.Walls.Positions[i]);
                    minY = Math.Min(minY, s.Walls.Positions[i]);
                }
            }
            Assert.AreEqual(0f, minY, 1e-4);
            Assert.AreEqual(Towers.Max(t => t.Height), maxY, 1e-3);
        }
    }
}
