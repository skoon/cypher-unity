using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class NeonMesherTests
    {
        static NeonQuad Wall(double nx, double nz, NeonShape shape = NeonShape.Tube, double roll = 0, double w = 4, double h = 1) => new NeonQuad
        {
            Gx = 2, Gz = 3, X = 10, Y = 5, Z = 20, Nx = nx, Nz = nz, Width = w, Height = h, Roll = roll,
            Shape = shape, Colour = new Rgb(1f, 0.5f, 0.25f), Gain = 2.5, FlickerMode = 3, Seed = 7.5, Chase = 0,
        };

        static MeshData Mesh(NeonQuad q)
        {
            var layout = new NeonLayout();
            layout.Quads.Add(q);
            return NeonMesher.Build(layout)[(q.Gx, q.Gz)].Emissive;
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

        static (double Min, double Max) Range(MeshData m, int axis)
        {
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            for (int i = 0; i < m.VertexCount; i++)
            {
                double v = m.Positions[i * 3 + axis];
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
            return (min, max);
        }

        // ---- emissive quads ----

        [TestCase(0.0, 1.0)]
        [TestCase(1.0, 0.0)]
        [TestCase(0.0, -1.0)]
        [TestCase(-1.0, 0.0)]
        [TestCase(0.6, 0.8)]
        public void QuadsAreWoundToFaceTheirNormal(double nx, double nz)
        {
            foreach (double roll in new[] { 0.0, Math.PI / 2 })
            {
                MeshData m = Mesh(Wall(nx, nz, roll: roll));
                Assert.AreEqual(2, m.TriangleCount);
                for (int t = 0; t < 2; t++)
                {
                    Normal(m, t, out double gx, out double gy, out double gz);
                    Assert.AreEqual(nx, gx, 1e-6, $"facing x, roll {roll}");
                    Assert.AreEqual(0.0, gy, 1e-6);
                    Assert.AreEqual(nz, gz, 1e-6, $"facing z, roll {roll}");
                }
                for (int v = 0; v < 4; v++)
                {
                    Assert.AreEqual(nx, m.Normals[v * 3], 1e-6, "stored normal matches");
                    Assert.AreEqual(nz, m.Normals[v * 3 + 2], 1e-6);
                }
            }
        }

        [Test]
        public void WidthRunsAlongTheWallAndHeightUpAtRollZero()
        {
            MeshData m = Mesh(Wall(0, 1, w: 6, h: 2));   // facing +Z, so the wall runs along X
            var x = Range(m, 0); var y = Range(m, 1); var z = Range(m, 2);
            Assert.AreEqual(6.0, x.Max - x.Min, 1e-5);
            Assert.AreEqual(2.0, y.Max - y.Min, 1e-5);
            Assert.AreEqual(0.0, z.Max - z.Min, 1e-5, "flat against the wall plane");
            Assert.AreEqual(10.0, (x.Min + x.Max) / 2, 1e-5);
            Assert.AreEqual(5.0, (y.Min + y.Max) / 2, 1e-5);
            Assert.AreEqual(20.0, z.Min, 1e-5);
        }

        [Test]
        public void AQuarterTurnRollMakesTheWidthVertical()
        {
            MeshData m = Mesh(Wall(0, 1, roll: Math.PI / 2, w: 6, h: 0.2));
            var x = Range(m, 0); var y = Range(m, 1);
            Assert.AreEqual(6.0, y.Max - y.Min, 1e-5, "a vertical tube's length runs up");
            Assert.AreEqual(0.2, x.Max - x.Min, 1e-5);
        }

        [Test]
        public void UvRunsZeroToOneAcrossTheQuad()
        {
            MeshData m = Mesh(Wall(0, 1));
            CollectionAssert.AreEqual(new float[] { 0, 0, 1, 0, 1, 1, 0, 1 }, m.Uvs);
        }

        [Test]
        public void ColourIsTheTintAndTheParametersArePackedPerVertex()
        {
            MeshData m = Mesh(Wall(0, 1, NeonShape.Tube));
            Assert.AreEqual(4 * 4, m.Uv1.Length);
            for (int v = 0; v < 4; v++)
            {
                Assert.AreEqual(1f, m.Colors[v * 3]);
                Assert.AreEqual(0.5f, m.Colors[v * 3 + 1]);
                Assert.AreEqual(0.25f, m.Colors[v * 3 + 2]);
                Assert.AreEqual(7.5f, m.Uv1[v * 4], "seed");
                Assert.AreEqual(3 + NeonMesher.ShapeStride * (int)NeonShape.Tube, m.Uv1[v * 4 + 1], "mode + 8 * shape");
                Assert.AreEqual(2.5f, m.Uv1[v * 4 + 3], "gain");
            }
        }

        [Test]
        public void ShapeAndModeRoundTripThroughThePackedChannel()
        {
            foreach (NeonShape shape in Enum.GetValues(typeof(NeonShape)))
            {
                for (int mode = 0; mode <= 4; mode++)
                {
                    NeonQuad q = Wall(0, 1, shape);
                    q.FlickerMode = mode;
                    float packed = Mesh(q).Uv1[1];
                    int s = (int)Math.Floor(packed / NeonMesher.ShapeStride);
                    Assert.AreEqual((int)shape, s);
                    Assert.AreEqual(mode, (int)(packed - s * NeonMesher.ShapeStride));
                }
            }
        }

        [Test]
        public void ExtraChannelCarriesAspectBulbCountOrChase()
        {
            Assert.AreEqual(3.0f, Mesh(Wall(0, 1, NeonShape.Panel, w: 6, h: 2)).Uv1[2], 1e-6f, "panel aspect");

            NeonQuad bulbs = Wall(0, 1, NeonShape.Bulbs);
            bulbs.Chase = 12.4;
            Assert.AreEqual(12f, Mesh(bulbs).Uv1[2], "bulb count, rounded");
            bulbs.Chase = 0;
            Assert.AreEqual(1f, Mesh(bulbs).Uv1[2], "at least one bulb");

            NeonQuad tube = Wall(0, 1, NeonShape.Tube);
            tube.Chase = 9;
            Assert.AreEqual(9f, Mesh(tube).Uv1[2], "chase segments");

            Assert.AreEqual(0f, Mesh(Wall(0, 1, NeonShape.Radial)).Uv1[2]);
        }

        [Test]
        public void PoolsLieFlatFacingUp()
        {
            var pool = new NeonQuad
            {
                Gx = 1, Gz = 1, X = 5, Y = NeonPlanner.PoolY, Z = 9, FacingUp = true, Width = 4, Height = 4,
                Shape = NeonShape.Radial, Colour = new Rgb(1f, 0f, 1f), Gain = 0.5,
            };
            MeshData m = Mesh(pool);
            for (int t = 0; t < 2; t++)
            {
                Normal(m, t, out double nx, out double ny, out double nz);
                Assert.AreEqual(1.0, ny, 1e-6);
            }
            var x = Range(m, 0); var y = Range(m, 1); var z = Range(m, 2);
            Assert.AreEqual(4.0, x.Max - x.Min, 1e-6);
            Assert.AreEqual(4.0, z.Max - z.Min, 1e-6);
            Assert.AreEqual(NeonPlanner.PoolY, y.Min, 1e-6);
            Assert.AreEqual(y.Min, y.Max, 1e-9);
        }

        [Test]
        public void QuadsAreGroupedByCell()
        {
            var layout = new NeonLayout();
            NeonQuad a = Wall(0, 1); a.Gx = 1; a.Gz = 1;
            NeonQuad b = Wall(0, 1); b.Gx = 1; b.Gz = 1;
            NeonQuad c = Wall(0, 1); c.Gx = 2; c.Gz = 4;
            layout.Quads.AddRange(new[] { a, b, c });
            var result = NeonMesher.Build(layout);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(4, result[(1, 1)].Emissive.TriangleCount);
            Assert.AreEqual(2, result[(2, 4)].Emissive.TriangleCount);
            Assert.AreEqual(0, result[(1, 1)].Housing.TriangleCount);
        }

        [Test]
        public void EmptyLayoutMakesNoMeshes()
        {
            Assert.AreEqual(0, NeonMesher.Build(new NeonLayout()).Count);
        }

        // ---- housing boxes ----

        static MeshData BoxMesh(NeonBox b)
        {
            var mb = new MeshBuilder();
            NeonMesher.AddBox(mb, b, NeonMesher.HousingColour);
            return mb.Build();
        }

        [TestCase(0.0, 1.0)]
        [TestCase(1.0, 0.0)]
        [TestCase(0.6, -0.8)]
        public void BoxesAreSolidWithOutwardFacesAndTheRightExtents(double dx, double dz)
        {
            var box = new NeonBox { X = 3, Y = 7, Z = -2, DirX = dx, DirZ = dz, SizeX = 4, SizeY = 2, SizeZ = 0.5 };
            MeshData m = BoxMesh(box);
            Assert.AreEqual(12, m.TriangleCount, "six faces");

            for (int t = 0; t < m.TriangleCount; t++)
            {
                Normal(m, t, out double nx, out double ny, out double nz);
                int vi = m.Indices[t * 3] * 3;
                double ox = m.Positions[vi] - box.X, oy = m.Positions[vi + 1] - box.Y, oz = m.Positions[vi + 2] - box.Z;
                Assert.Greater(nx * ox + ny * oy + nz * oz, 0, $"triangle {t} faces into the box");
            }

            var y = Range(m, 1);
            Assert.AreEqual(2.0, y.Max - y.Min, 1e-5, "height");
            Assert.AreEqual(7.0, (y.Min + y.Max) / 2, 1e-5);

            // Extent along the dir axis (local Z) is SizeZ; across it (local X) is SizeX.
            double minAlong = double.PositiveInfinity, maxAlong = double.NegativeInfinity, minAcross = double.PositiveInfinity, maxAcross = double.NegativeInfinity;
            for (int i = 0; i < m.VertexCount; i++)
            {
                double px = m.Positions[i * 3] - box.X, pz = m.Positions[i * 3 + 2] - box.Z;
                double along = px * dx + pz * dz, across = px * dz - pz * dx;
                minAlong = Math.Min(minAlong, along); maxAlong = Math.Max(maxAlong, along);
                minAcross = Math.Min(minAcross, across); maxAcross = Math.Max(maxAcross, across);
            }
            Assert.AreEqual(0.5, maxAlong - minAlong, 1e-5, "depth along the direction");
            Assert.AreEqual(4.0, maxAcross - minAcross, 1e-5, "width across it");
        }

        [Test]
        public void BoxesCarryTheHousingColour()
        {
            MeshData m = BoxMesh(new NeonBox { DirX = 0, DirZ = 1, SizeX = 1, SizeY = 1, SizeZ = 1 });
            for (int v = 0; v < m.VertexCount; v++)
            {
                Assert.AreEqual(NeonMesher.HousingColour.R, m.Colors[v * 3]);
            }
        }

        [Test]
        public void BoxesAreGroupedWithTheirCell()
        {
            var layout = new NeonLayout();
            layout.Boxes.Add(new NeonBox { Gx = 4, Gz = 2, DirX = 0, DirZ = 1, SizeX = 1, SizeY = 1, SizeZ = 1 });
            var result = NeonMesher.Build(layout);
            Assert.AreEqual(12, result[(4, 2)].Housing.TriangleCount);
            Assert.AreEqual(0, result[(4, 2)].Emissive.TriangleCount);
        }

        // ---- failure modes ----

        [Test]
        public void BadInputFailsLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => NeonMesher.Build(null));
            Assert.Throws<ArgumentNullException>(() => NeonMesher.AddBox(null, default, NeonMesher.HousingColour));
            Assert.Throws<ArgumentException>(() => NeonMesher.AddBox(new MeshBuilder(), new NeonBox { SizeX = 1, SizeY = 1, SizeZ = 1 }, NeonMesher.HousingColour), "zero direction");

            var layout = new NeonLayout();
            layout.Quads.Add(Wall(0, 1, w: 0));
            Assert.Throws<ArgumentException>(() => NeonMesher.Build(layout), "zero width");

            layout = new NeonLayout();
            layout.Quads.Add(Wall(0, 0));
            Assert.Throws<ArgumentException>(() => NeonMesher.Build(layout), "faces nowhere");
        }

        // ---- the whole pipeline ----

        [Test]
        public void RealLayoutMeshesCleanly()
        {
            NeonLayout layout = NeonPlanner.Plan(CityPlan.Generate(), CityWindow.AroundPlaza(1));
            var cells = NeonMesher.Build(layout);
            Assert.AreEqual(layout.Quads.Count * 2, cells.Values.Sum(c => c.Emissive.TriangleCount));
            Assert.AreEqual(layout.Boxes.Count * 12, cells.Values.Sum(c => c.Housing.TriangleCount));
            foreach (NeonCellMeshes c in cells.Values)
            {
                Assert.AreEqual(c.Emissive.VertexCount * 4, c.Emissive.Uv1.Length);
                Assert.IsTrue(c.Emissive.Indices.All(i => i >= 0 && i < c.Emissive.VertexCount));
            }
        }
    }
}
