using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class CityChunkerTests
    {
        static CitySlice Slice(int radius = 1, uint seed = CityPlan.DefaultSeed) =>
            CityChunker.Build(CityPlan.Generate(seed), CityWindow.AroundPlaza(radius));

        static double MinY(MeshData m)
        {
            double min = double.PositiveInfinity;
            for (int i = 0; i < m.VertexCount; i++) min = Math.Min(min, m.Positions[i * 3 + 1]);
            return min;
        }

        [Test]
        public void ThreeByThreeWindowGivesNineChunksInsideTheWindow()
        {
            CitySlice s = Slice(1);
            Assert.AreEqual(9, s.Chunks.Count);
            Assert.IsTrue(s.Chunks.All(c => s.Window.Contains(c.Gx, c.Gz)));
            Assert.AreEqual(9, s.Chunks.Select(c => (c.Gx, c.Gz)).Distinct().Count());
        }

        [Test]
        public void PlazaChunkHasASidewalkAndNoBuildings()
        {
            CityChunk plaza = Slice(1).Chunks.Single(c => c.Gx == CityLayout.PlazaIndex && c.Gz == CityLayout.PlazaIndex);
            Assert.AreEqual(0, plaza.BuildingCount);
            Assert.AreEqual(0, plaza.Walls.TriangleCount);
            Assert.AreEqual(0, plaza.Roofs.TriangleCount);
            Assert.Greater(plaza.Sidewalk.TriangleCount, 0);
            Assert.AreEqual(1, plaza.Colliders.Count, "just the deck");
        }

        [Test]
        public void SurroundingCellsHaveBuildingsWithWallsAndRoofs()
        {
            CitySlice s = Slice(1);
            var built = s.Chunks.Where(c => c.BuildingCount > 0).ToList();
            Assert.AreEqual(8, built.Count, "every non-plaza cell has at least one building");
            foreach (CityChunk c in built)
            {
                Assert.Greater(c.Walls.TriangleCount, 0);
                Assert.Greater(c.Roofs.TriangleCount, 0);
            }
        }

        [Test]
        public void BuildingGeometryStandsOnTheSidewalkDeck()
        {
            foreach (CityChunk c in Slice(1).Chunks.Where(c => c.BuildingCount > 0))
            {
                Assert.GreaterOrEqual(MinY(c.Walls), CityLayout.SidewalkHeight - 1e-5, $"walls of ({c.Gx},{c.Gz}) sink below the deck");
                Assert.GreaterOrEqual(MinY(c.Roofs), CityLayout.SidewalkHeight - 1e-5);
            }
            double lowest = Slice(1).Chunks.Where(c => c.BuildingCount > 0).Min(c => MinY(c.Walls));
            Assert.AreEqual(CityLayout.SidewalkHeight, lowest, 1e-5, "street-level walls start exactly on the deck");
        }

        [Test]
        public void EveryVolumeHasACollider()
        {
            CitySlice s = Slice(1);
            foreach (CityChunk c in s.Chunks)
            {
                int volumes = s.Plan.Buildings
                    .Where(b => b.Lot.Gx == c.Gx && b.Lot.Gz == c.Gz)
                    .Sum(b => b.Volumes.Count);
                Assert.AreEqual(1 + volumes, c.Colliders.Count, $"cell ({c.Gx},{c.Gz})");
            }
        }

        [Test]
        public void ColliderTopsMatchChunkMaxYAndRoofMeshes()
        {
            foreach (CityChunk c in Slice(1).Chunks.Where(c => c.BuildingCount > 0))
            {
                Assert.AreEqual(c.Colliders.Max(b => b.Y1), c.MaxY, 1e-9);

                double roofTop = double.NegativeInfinity;
                for (int i = 0; i < c.Roofs.VertexCount; i++) roofTop = Math.Max(roofTop, c.Roofs.Positions[i * 3 + 1]);
                Assert.AreEqual(c.MaxY, roofTop, 1e-3, $"cell ({c.Gx},{c.Gz})");
            }
        }

        [Test]
        public void SameSeedGivesIdenticalGeometry()
        {
            CitySlice a = Slice(1), b = Slice(1);
            Assert.AreEqual(a.TriangleCount, b.TriangleCount);
            for (int i = 0; i < a.Chunks.Count; i++)
            {
                CollectionAssert.AreEqual(a.Chunks[i].Walls.Positions, b.Chunks[i].Walls.Positions);
            }
        }

        [Test]
        public void DifferentSeedGivesDifferentCity()
        {
            Assert.AreNotEqual(Slice(1).TriangleCount, Slice(1, 999).TriangleCount);
        }

        [Test]
        public void LargerWindowBuildsMoreChunks()
        {
            Assert.AreEqual(1, Slice(0).Chunks.Count);
            Assert.AreEqual(25, Slice(2).Chunks.Count);
        }

        [Test]
        public void SliceStaysInsideTheTriangleBudget()
        {
            // M1 aims at roughly one million triangles for the whole walkable slice;
            // the 3x3 window should use a small fraction of that.
            Assert.Less(Slice(1).TriangleCount, 250_000);
        }

        [Test]
        public void NullPlanFailsLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => CityChunker.Build(null, CityWindow.AroundPlaza(1)));
        }
    }
}
