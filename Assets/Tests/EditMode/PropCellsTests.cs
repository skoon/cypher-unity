using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class PropCellsTests
    {
        static readonly CityPlan Plan = CityPlan.Generate();
        static readonly PropLayout Layout = PropPlacer.Build(Plan, CityWindow.AroundPlaza(3));
        static readonly Dictionary<(int Gx, int Gz), PropCell> Cells = PropCellMesher.Build(Layout);

        static int KitVertices(PropLayout l, (int, int) cell) =>
            l.Instances.Where(i => (Math.Max(0, Math.Min(6, CityLayout.CellIndex(i.X))), Math.Max(0, Math.Min(6, CityLayout.CellIndex(i.Z)))) == cell)
                .Sum(i => PropKits.Get(i.Kit).Geometry.VertexCount);

        [Test]
        public void EveryInstanceDecalAndColliderLandsInExactlyOneCell()
        {
            Assert.AreEqual(Layout.Instances.Count, Cells.Values.Sum(c => c.InstanceCount));
            Assert.AreEqual(Layout.Decals.Count, Cells.Values.Sum(c => c.DecalCount));
            Assert.AreEqual(Layout.Boxes.Count, Cells.Values.Sum(c => c.Boxes.Count));
            foreach (var kv in Cells)
            {
                Assert.That(kv.Key.Gx, Is.InRange(0, 6));
                Assert.That(kv.Key.Gz, Is.InRange(0, 6));
                Assert.AreEqual(kv.Key, (kv.Value.Gx, kv.Value.Gz));
            }
            Assert.AreEqual(49, Cells.Count, "every cell, the plaza included, has props");
        }

        [Test]
        public void ACellsPropMeshHoldsItsPropsAndItsCables()
        {
            foreach (var kv in Cells)
            {
                int cableVertices = Layout.CellParts.TryGetValue(kv.Key, out PropMeshBuilder b) ? b.VertexCount : 0;
                Assert.AreEqual(KitVertices(Layout, kv.Key) + cableVertices, kv.Value.Props.VertexCount, $"cell {kv.Key}");
            }
        }

        [Test]
        public void MeshArraysAgreeWithTheVertexCount()
        {
            foreach (PropCell c in Cells.Values)
            {
                foreach (MeshData m in new[] { c.Props, c.Decals })
                {
                    if (m.VertexCount == 0) continue;
                    Assert.AreEqual(m.VertexCount * 3, m.Normals.Length);
                    Assert.AreEqual(m.VertexCount * 2, m.Uvs.Length);
                    Assert.AreEqual(m.VertexCount * 3, m.Colors.Length);
                    Assert.AreEqual(m.VertexCount * 4, m.Uv1.Length);
                    Assert.IsTrue(m.Indices.All(i => i >= 0 && i < m.VertexCount));
                }
            }
        }

        [Test]
        public void PropsStayNearTheCellTheyWerePlacedIn()
        {
            foreach (PropCell c in Cells.Values)
            {
                // a cable is filed under the cell of its middle and spans up to 46 m, so its ends may be 23 m outside
                double x0 = CityLayout.Base + c.Gx * CityLayout.Cell - 26, x1 = CityLayout.Base + (c.Gx + 1) * CityLayout.Cell + 26;
                double z0 = CityLayout.Base + c.Gz * CityLayout.Cell - 26, z1 = CityLayout.Base + (c.Gz + 1) * CityLayout.Cell + 26;
                MeshData m = c.Props;
                for (int i = 0; i < m.VertexCount; i += 7)
                {
                    Assert.That(m.Positions[i * 3], Is.InRange(x0, x1), $"cell {c.Gx},{c.Gz}");
                    Assert.That(m.Positions[i * 3 + 2], Is.InRange(z0, z1), $"cell {c.Gx},{c.Gz}");
                }
            }
        }

        [Test]
        public void ACellOfPropsIsAFewThousandTrianglesAndTheWholeCityIsALot()
        {
            Assert.That(Cells.Values.Max(c => c.Props.TriangleCount), Is.LessThan(40000), "no cell is a monster");
            Assert.That(Cells.Values.Sum(c => c.Props.TriangleCount), Is.InRange(400000, 900000));
            Assert.That(Cells.Values.Max(c => c.MaxY), Is.GreaterThan(30), "tall aerials and cables on the towers");
        }

        [Test]
        public void GroundDecalsLieFlatFacingUpAndWallDecalsFaceAlongTheirWall()
        {
            foreach (PropCell c in Cells.Values)
            {
                MeshData m = c.Decals;
                for (int t = 0; t < m.Indices.Length; t += 3)
                {
                    int a = m.Indices[t], b = m.Indices[t + 1], d = m.Indices[t + 2];
                    double nx = m.Normals[a * 3], ny = m.Normals[a * 3 + 1], nz = m.Normals[a * 3 + 2];
                    Assert.AreEqual(1.0, Math.Sqrt(nx * nx + ny * ny + nz * nz), 1e-5);
                    // winding agrees with the normal
                    double ux = m.Positions[b * 3] - m.Positions[a * 3], uy = m.Positions[b * 3 + 1] - m.Positions[a * 3 + 1], uz = m.Positions[b * 3 + 2] - m.Positions[a * 3 + 2];
                    double vx = m.Positions[d * 3] - m.Positions[a * 3], vy = m.Positions[d * 3 + 1] - m.Positions[a * 3 + 1], vz = m.Positions[d * 3 + 2] - m.Positions[a * 3 + 2];
                    double dot = (uy * vz - uz * vy) * nx + (uz * vx - ux * vz) * ny + (ux * vy - uy * vx) * nz;
                    Assert.Greater(dot, 0, "a decal faces the way its normal says");
                    bool ground = m.Positions[a * 3 + 1] < 0.3;
                    if (ground) Assert.AreEqual(1.0, ny, 1e-5, "ground decals face up");
                    else Assert.AreEqual(0.0, ny, 1e-5, "wall decals stand upright");
                }
            }
        }

        [Test]
        public void DecalUvsPointIntoTheRightHalfOfTheAtlas()
        {
            foreach (PropCell c in Cells.Values)
            {
                MeshData m = c.Decals;
                for (int i = 0; i < m.VertexCount; i++)
                {
                    float u = m.Uvs[i * 2], v = m.Uvs[i * 2 + 1];
                    Assert.That(u, Is.InRange(0f, 1f));
                    Assert.That(v, Is.InRange(0f, 1f));
                    bool graffiti = m.Uv1[i * 4] > 0.1f;
                    if (graffiti) Assert.GreaterOrEqual(v, 0.5f, "graffiti is in the top half");
                    else Assert.LessOrEqual(v, 0.5f, "ground decals are in the bottom half");
                    Assert.AreEqual(PropCellMesher.DecalRough, m.Uv1[i * 4 + 1], 1e-6);
                    Assert.AreEqual(0f, m.Uv1[i * 4 + 2]);
                }
            }
        }

        [Test]
        public void OnlyGraffitiGlows()
        {
            int graffitiQuads = 0, groundQuads = 0;
            foreach (PropCell c in Cells.Values)
            {
                for (int i = 0; i < c.Decals.VertexCount; i += 4)
                {
                    float emit = c.Decals.Uv1[i * 4];
                    if (emit > 0) { Assert.AreEqual(PropCellMesher.GraffitiEmit, emit, 1e-6); graffitiQuads++; }
                    else groundQuads++;
                }
            }
            Assert.AreEqual(Layout.Decals.Count(d => d.Kind == DecalKind.Graffiti), graffitiQuads);
            Assert.AreEqual(Layout.Decals.Count(d => d.Kind == DecalKind.Ground), groundQuads);
        }

        [Test]
        public void BuildingTwiceGivesTheSameThingBecauseTheLayoutIsNotUsedUp()
        {
            Dictionary<(int Gx, int Gz), PropCell> again = PropCellMesher.Build(Layout);
            foreach (var kv in Cells)
            {
                Assert.AreEqual(kv.Value.Props.VertexCount, again[kv.Key].Props.VertexCount, $"cell {kv.Key}");
                Assert.AreEqual(kv.Value.Decals.VertexCount, again[kv.Key].Decals.VertexCount);
            }
        }

        [Test]
        public void ANullLayoutFailsLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => PropCellMesher.Build(null));
            Assert.Throws<ArgumentNullException>(() => SteamMesh.Build(null));
        }

        // ---------------------------------------------------------------- steam

        [Test]
        public void TheSteamMeshIsAQuadPerPuffCarryingItsParameters()
        {
            MeshData m = SteamMesh.Build(Layout.Puffs);
            Assert.AreEqual(96 * 4, m.VertexCount);
            Assert.AreEqual(96 * 2, m.TriangleCount);
            Assert.IsTrue(m.Indices.All(i => i >= 0 && i < m.VertexCount));
            SteamPuff p = Layout.Puffs[10];
            for (int k = 0; k < 4; k++)
            {
                int v = 10 * 4 + k;
                Assert.AreEqual((float)p.X, m.Positions[v * 3], 1e-4f);
                Assert.AreEqual((float)p.Y, m.Positions[v * 3 + 1], 1e-4f);
                Assert.AreEqual((float)p.Drift, m.Colors[v * 3], 1e-6f);
                Assert.AreEqual((float)p.Phase, m.Uv1[v * 4], 1e-6f);
                Assert.AreEqual((float)p.Speed, m.Uv1[v * 4 + 1], 1e-6f);
                Assert.AreEqual((float)p.Size, m.Uv1[v * 4 + 2], 1e-6f);
                Assert.AreEqual((float)p.Rise, m.Uv1[v * 4 + 3], 1e-6f);
            }
            Assert.AreEqual(new[] { 0f, 1f, 1f, 0f }, new[] { m.Uvs[0], m.Uvs[2], m.Uvs[4], m.Uvs[6] }, "the four corners");
            Assert.AreEqual(0, SteamMesh.Build(new List<SteamPuff>()).VertexCount);
        }

        [Test]
        public void APuffRisesGrowsAndFadesInAndOutOverItsCycle()
        {
            var p = new SteamPuff(10, 2, 20, phase: 0, speed: 0.2, size: 1.0, rise: 2.0, drift: 0.4);
            var start = SteamMesh.Pose(p, 0, 0.7, 0);
            Assert.AreEqual(2.0, start.y, 1e-9);
            Assert.AreEqual(0.35, start.size, 1e-9);
            Assert.AreEqual(0.0, start.alpha, 1e-9, "invisible when it is born");

            var mid = SteamMesh.Pose(p, 2.5, 0.7, 0);   // half way through a 5 s cycle
            Assert.AreEqual(3.0, mid.y, 1e-9);
            Assert.AreEqual(0.35 + 0.75, mid.size, 1e-9);
            Assert.AreEqual(0.16, mid.alpha, 1e-9, "brightest in the middle");
            Assert.AreEqual(10 + 0.4 * 0.5 * 0.7, mid.x, 1e-9);
            Assert.AreEqual(20 + 0.4 * 0.6 * 0.5 * 0.7, mid.z, 1e-9);

            var wrapped = SteamMesh.Pose(p, 5.0, 0.7, 0);
            Assert.AreEqual(start.y, wrapped.y, 1e-9, "and it starts again");
        }

        [Test]
        public void APuffFadesWithDistanceFromTheCameraAndIsGoneAtSixtyMetres()
        {
            var p = new SteamPuff(0, 0, 0, 0.5, 0.2, 1, 1, 0);
            double near = SteamMesh.Pose(p, 0, 0.7, 0).alpha;
            double mid = SteamMesh.Pose(p, 0, 0.7, 30 * 30).alpha;
            double far = SteamMesh.Pose(p, 0, 0.7, 60 * 60 + 1).alpha;
            Assert.Greater(near, mid);
            Assert.Greater(mid, 0);
            Assert.AreEqual(0.0, far);
            Assert.AreEqual(near * 0.75, mid, 1e-9, "linear in squared distance: 900 of 3600 leaves three quarters");
        }
    }
}
