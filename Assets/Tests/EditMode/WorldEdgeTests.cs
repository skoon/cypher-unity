using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class WorldEdgeTests
    {
        static readonly CitySlice Whole = CityChunker.Build(CityPlan.Generate(), CityWindow.AroundPlaza(3), includeNeon: false);

        static bool InsideAnyWall(double x, double y, double z) =>
            WorldEdge.Walls().Any(w => x >= w.X0 && x <= w.X1 && y >= w.Y0 && y <= w.Y1 && z >= w.Z0 && z <= w.Z1);

        [Test]
        public void TheCityHalfIsHalfTheOuterRoadToOuterRoadWidth()
        {
            Rect2 e = CityWindow.AroundPlaza(3).RoadExtent;
            Assert.AreEqual(e.X1, WorldEdge.CityHalf, 1e-9);
            Assert.AreEqual(e.Z1, WorldEdge.CityHalf, 1e-9);
            Assert.AreEqual(e.X0, -WorldEdge.CityHalf, 1e-9);
            Assert.AreEqual(e.Z0, -WorldEdge.CityHalf, 1e-9);
        }

        [Test]
        public void ThePlayAreaLeavesAnApronBeyondTheOuterRoads()
        {
            Assert.AreEqual(WorldEdge.CityHalf + WorldEdge.Apron, WorldEdge.PlayHalf, 1e-9);
            Assert.Greater(WorldEdge.Apron, 16, "wider than a road, so the outermost road is not a cliff edge");
        }

        [Test]
        public void EveryColliderOfTheCityIsInsideThePlayArea()
        {
            foreach (CityChunk c in Whole.Chunks)
            {
                foreach (Aabb b in c.Colliders)
                {
                    Assert.LessOrEqual(Math.Max(Math.Abs(b.X0), Math.Abs(b.X1)), WorldEdge.PlayHalf, $"chunk ({c.Gx},{c.Gz})");
                    Assert.LessOrEqual(Math.Max(Math.Abs(b.Z0), Math.Abs(b.Z1)), WorldEdge.PlayHalf, $"chunk ({c.Gx},{c.Gz})");
                }
            }
        }

        [Test]
        public void IsInsidePlayAreaFollowsTheSquare()
        {
            Assert.IsTrue(WorldEdge.IsInsidePlayArea(0, 0));
            Assert.IsTrue(WorldEdge.IsInsidePlayArea(WorldEdge.PlayHalf, -WorldEdge.PlayHalf));
            Assert.IsFalse(WorldEdge.IsInsidePlayArea(WorldEdge.PlayHalf + 0.01, 0));
            Assert.IsFalse(WorldEdge.IsInsidePlayArea(0, -WorldEdge.PlayHalf - 0.01));
        }

        [Test]
        public void TheWallsStandJustOutsideThePlayAreaOnAllFourSides()
        {
            Assert.AreEqual(4, WorldEdge.Walls().Count);
            double near = WorldEdge.PlayHalf + 0.5;
            foreach ((double x, double z) in new[] { (near, 0.0), (-near, 0.0), (0.0, near), (0.0, -near) })
            {
                Assert.IsTrue(InsideAnyWall(x, 1, z), $"no wall at ({x}, {z})");
            }
            Assert.IsTrue(InsideAnyWall(near, 1, near), "the corner is closed");
            Assert.IsTrue(InsideAnyWall(-near, 1, -near), "and the opposite one");
        }

        [Test]
        public void NothingInsideThePlayAreaTouchesAWall()
        {
            double inside = WorldEdge.PlayHalf - 0.01;
            foreach (double x in new[] { -inside, 0, inside })
            {
                foreach (double z in new[] { -inside, 0, inside })
                {
                    Assert.IsFalse(InsideAnyWall(x, 1, z), $"({x}, {z}) is inside a wall");
                }
            }
        }

        [Test]
        public void TheWallsAreTallAndThickEnoughToStopAFastFallOrAJump()
        {
            foreach (Aabb w in WorldEdge.Walls())
            {
                Assert.GreaterOrEqual(w.SizeY, 300, "nobody jumps over");
                Assert.GreaterOrEqual(Math.Min(w.SizeX, w.SizeZ), WorldEdge.WallThickness - 1e-9, "nobody tunnels through");
            }
        }
    }
}
