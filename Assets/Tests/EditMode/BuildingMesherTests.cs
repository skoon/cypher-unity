using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class BuildingMesherTests
    {
        static Building SingleBox(double x0, double z0, double x1, double z1, double y1)
        {
            var b = new Building { Tint = new[] { 0.5, 0.5, 0.5 }, Floors = 3 };
            b.Volumes.Add(new Volume
            {
                Poly = new[] { new Point2(x0, z0), new Point2(x1, z0), new Point2(x1, z1), new Point2(x0, z1) },
                Y0 = 0,
                Y1 = y1,
            });
            return b;
        }

        static void Normal(MeshData m, int tri, out double x, out double y, out double z)
        {
            int ia = m.Indices[tri * 3] * 3, ib = m.Indices[tri * 3 + 1] * 3, ic = m.Indices[tri * 3 + 2] * 3;
            double ax = m.Positions[ib] - m.Positions[ia], ay = m.Positions[ib + 1] - m.Positions[ia + 1], az = m.Positions[ib + 2] - m.Positions[ia + 2];
            double bx = m.Positions[ic] - m.Positions[ia], by = m.Positions[ic + 1] - m.Positions[ia + 1], bz = m.Positions[ic + 2] - m.Positions[ia + 2];
            x = ay * bz - az * by; y = az * bx - ax * bz; z = ax * by - ay * bx;
            double len = Math.Sqrt(x * x + y * y + z * z);
            x /= len; y /= len; z /= len;
        }

        [Test]
        public void BoxBuildingHasFourWallsAndOneRoof()
        {
            var walls = new MeshBuilder();
            var roofs = new MeshBuilder();
            new BuildingMesher(walls, roofs).AddBuilding(SingleBox(-5, -5, 5, 5, 10.65));
            Assert.AreEqual(8, walls.TriangleCount);
            Assert.AreEqual(2, roofs.TriangleCount);
        }

        [Test]
        public void WallsFaceAwayFromTheBuildingCentre()
        {
            var walls = new MeshBuilder();
            new BuildingMesher(walls, new MeshBuilder()).AddBuilding(SingleBox(10, 20, 14, 28, 10));
            MeshData m = walls.Build();
            for (int t = 0; t < m.TriangleCount; t++)
            {
                Normal(m, t, out double nx, out double ny, out double nz);
                int vi = m.Indices[t * 3] * 3;
                double ox = m.Positions[vi] - 12, oz = m.Positions[vi + 2] - 24;
                Assert.AreEqual(0.0, ny, 1e-6, "walls are vertical");
                Assert.Greater(nx * ox + nz * oz, 0, $"wall triangle {t} faces inward");
                Assert.AreEqual(1.0, nx * m.Normals[vi] + ny * m.Normals[vi + 1] + nz * m.Normals[vi + 2], 1e-5);
            }
        }

        [Test]
        public void RoofFacesUpRegardlessOfPolygonWinding()
        {
            var b = SingleBox(0, 0, 4, 4, 8);
            var reversed = SingleBox(0, 0, 4, 4, 8);
            Array.Reverse(reversed.Volumes[0].Poly);
            foreach (Building bld in new[] { b, reversed })
            {
                var roofs = new MeshBuilder();
                new BuildingMesher(new MeshBuilder(), roofs).AddBuilding(bld);
                MeshData m = roofs.Build();
                for (int t = 0; t < m.TriangleCount; t++)
                {
                    Normal(m, t, out _, out double ny, out _);
                    Assert.AreEqual(1.0, ny, 1e-9);
                }
                Assert.AreEqual(1f, m.Normals[1], 1e-6);
            }
        }

        [Test]
        public void WallsHiddenInsideASiblingVolumeAreSkipped()
        {
            // A podium (0..7.1 m) with a tower (0..30 m) rising from inside it. The
            // tower's walls are buried in the podium up to 7.1 m and visible above it.
            var b = SingleBox(0, 0, 20, 20, 7.1);
            b.Volumes.Add(new Volume
            {
                Poly = new[] { new Point2(5, 5), new Point2(15, 5), new Point2(15, 15), new Point2(5, 15) },
                Y0 = 0,
                Y1 = 30,
            });
            var walls = new MeshBuilder();
            new BuildingMesher(walls, new MeshBuilder()).AddBuilding(b);
            MeshData m = walls.Build();
            Assert.AreEqual(16, m.TriangleCount);   // 4 podium walls + 4 tower walls, still one quad each
            for (int v = 0; v < m.VertexCount; v++)
            {
                double x = m.Positions[v * 3], y = m.Positions[v * 3 + 1], z = m.Positions[v * 3 + 2];
                bool onTowerWall = (Math.Abs(x - 5) < 1e-4 || Math.Abs(x - 15) < 1e-4 || Math.Abs(z - 5) < 1e-4 || Math.Abs(z - 15) < 1e-4)
                    && x >= 5 - 1e-4 && x <= 15 + 1e-4 && z >= 5 - 1e-4 && z <= 15 + 1e-4;
                if (onTowerWall) Assert.GreaterOrEqual(y, 7.1 - 1e-4, "tower wall vertex below podium roof");
            }
        }

        [Test]
        public void WallFullyBuriedInsideALargerSiblingIsSkipped()
        {
            var b = SingleBox(0, 0, 20, 20, 30);
            b.Volumes.Add(new Volume
            {
                Poly = new[] { new Point2(5, 5), new Point2(15, 5), new Point2(15, 15), new Point2(5, 15) },
                Y0 = 0,
                Y1 = 10,
            });
            var walls = new MeshBuilder();
            new BuildingMesher(walls, new MeshBuilder()).AddBuilding(b);
            Assert.AreEqual(8, walls.TriangleCount);   // only the big volume's 4 walls
        }

        [Test]
        public void ZeroHeightVolumeFailsLoudly()
        {
            var b = SingleBox(0, 0, 4, 4, 0);
            Assert.Throws<InvalidOperationException>(() =>
                new BuildingMesher(new MeshBuilder(), new MeshBuilder()).AddBuilding(b));
        }

        [Test]
        public void ShadeDarkensStreetLevelAndSaturatesWithHeight()
        {
            Assert.AreEqual(0.40f, BuildingMesher.Shade(0), 1e-6);
            Assert.Greater(BuildingMesher.Shade(30), BuildingMesher.Shade(3));
            Assert.Less(BuildingMesher.Shade(1000), 1.0001f);
            Assert.AreEqual(0.40f, BuildingMesher.Shade(-5), 1e-6);
        }

        [Test]
        public void WallFrameNormalPointsAwayFromCentroid()
        {
            WallFrame w = WallFrame.Create(0, 0, 10, 0, 5, 5);   // centroid is at +z, so normal is -z
            Assert.AreEqual(0.0, w.Nx, 1e-9);
            Assert.AreEqual(-1.0, w.Nz, 1e-9);
            Assert.AreEqual(10.0, w.Length, 1e-9);
        }

        [Test]
        public void FullCityMeshesWithinTriangleBudget()
        {
            CityPlan plan = CityPlan.Generate(CityPlan.DefaultSeed);
            var walls = new MeshBuilder();
            var roofs = new MeshBuilder();
            var mesher = new BuildingMesher(walls, roofs);
            foreach (Building b in plan.Buildings) mesher.AddBuilding(b);
            int tris = walls.TriangleCount + roofs.TriangleCount;
            Assert.Greater(tris, 1000);
            // The prototype measured ~1M visible triangles as the number worth aiming at;
            // massing alone for the whole 7x7 city must be a small fraction of it.
            Assert.Less(tris, 40000, $"massing is {tris} triangles");
        }

        [Test]
        public void MeshingIsDeterministic()
        {
            int Count()
            {
                CityPlan plan = CityPlan.Generate(CityPlan.DefaultSeed);
                var walls = new MeshBuilder();
                var mesher = new BuildingMesher(walls, new MeshBuilder());
                foreach (Building b in plan.Buildings) mesher.AddBuilding(b);
                return walls.TriangleCount;
            }
            Assert.AreEqual(Count(), Count());
        }
    }
}
