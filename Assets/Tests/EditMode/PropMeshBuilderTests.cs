using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class PropMeshBuilderTests
    {
        static MeshData One(Action<PropMeshBuilder> make)
        {
            var b = new PropMeshBuilder();
            make(b);
            return b.Build();
        }

        /// <summary>Triangles whose winding disagrees with the vertex normals (skipping zero-area ones, which sit at sphere poles).</summary>
        static int WrongWayTriangles(MeshData m)
        {
            int wrong = 0;
            for (int t = 0; t < m.Indices.Length; t += 3)
            {
                int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
                double ux = m.Positions[b * 3] - m.Positions[a * 3], uy = m.Positions[b * 3 + 1] - m.Positions[a * 3 + 1], uz = m.Positions[b * 3 + 2] - m.Positions[a * 3 + 2];
                double vx = m.Positions[c * 3] - m.Positions[a * 3], vy = m.Positions[c * 3 + 1] - m.Positions[a * 3 + 1], vz = m.Positions[c * 3 + 2] - m.Positions[a * 3 + 2];
                double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                if (Math.Sqrt(cx * cx + cy * cy + cz * cz) < 1e-9) continue;
                double nx = (m.Normals[a * 3] + m.Normals[b * 3] + m.Normals[c * 3]) / 3;
                double ny = (m.Normals[a * 3 + 1] + m.Normals[b * 3 + 1] + m.Normals[c * 3 + 1]) / 3;
                double nz = (m.Normals[a * 3 + 2] + m.Normals[b * 3 + 2] + m.Normals[c * 3 + 2]) / 3;
                if (cx * nx + cy * ny + cz * nz <= 0) wrong++;
            }
            return wrong;
        }

        [Test]
        public void ABoxIsSixQuadsWithOutwardWinding()
        {
            MeshData m = One(b => b.Box(2, 4, 6, 0x808080));
            Assert.AreEqual(24, m.VertexCount);
            Assert.AreEqual(12, m.TriangleCount);
            Assert.AreEqual(0, WrongWayTriangles(m));
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < m.VertexCount; i++)
            {
                minX = Math.Min(minX, m.Positions[i * 3]); maxX = Math.Max(maxX, m.Positions[i * 3]);
                minY = Math.Min(minY, m.Positions[i * 3 + 1]); maxY = Math.Max(maxY, m.Positions[i * 3 + 1]);
                minZ = Math.Min(minZ, m.Positions[i * 3 + 2]); maxZ = Math.Max(maxZ, m.Positions[i * 3 + 2]);
            }
            Assert.AreEqual((-1f, 1f, -2f, 2f, -3f, 3f), (minX, maxX, minY, maxY, minZ, maxZ));
        }

        [Test]
        public void ACylinderHasASideAndTwoCapsUnlessOpen()
        {
            MeshData closed = One(b => b.Cylinder(0.5, 0.5, 2, 8, 0x808080));
            MeshData open = One(b => b.Cylinder(0.5, 0.5, 2, 8, 0x808080, new PartStyle(open: true)));
            Assert.AreEqual(8 * 2 + 2 * 8, closed.TriangleCount, "16 side triangles and a fan of 8 on each end");
            Assert.AreEqual(8 * 2, open.TriangleCount);
            Assert.AreEqual(0, WrongWayTriangles(closed));
            Assert.AreEqual(0, WrongWayTriangles(open));
        }

        [Test]
        public void ACylinderThatEndsInAPointHasOnlyTheCapWithAnArea()
        {
            MeshData cone = One(b => b.Cylinder(0, 0.5, 1, 6, 0x808080));
            Assert.AreEqual(6 * 2 + 6, cone.TriangleCount, "the side plus one base cap, no cap at the tip");
            Assert.AreEqual(0, WrongWayTriangles(cone));
        }

        [Test]
        public void ASphereHasUnitNormalsAndOutwardWinding()
        {
            MeshData s = One(b => b.Sphere(0.5, 8, 0x808080));
            Assert.AreEqual(0, WrongWayTriangles(s));
            for (int i = 0; i < s.VertexCount; i++)
            {
                double len = Math.Sqrt(s.Normals[i * 3] * s.Normals[i * 3] + s.Normals[i * 3 + 1] * s.Normals[i * 3 + 1] + s.Normals[i * 3 + 2] * s.Normals[i * 3 + 2]);
                Assert.AreEqual(1.0, len, 1e-5);
                double r = Math.Sqrt(s.Positions[i * 3] * s.Positions[i * 3] + s.Positions[i * 3 + 1] * s.Positions[i * 3 + 1] + s.Positions[i * 3 + 2] * s.Positions[i * 3 + 2]);
                Assert.AreEqual(0.5, r, 1e-5);
            }
        }

        [Test]
        public void ARotatedAndScaledPartKeepsUnitNormalsAndOutwardWinding()
        {
            MeshData m = One(b =>
            {
                b.Box(1, 1, 1, 0x808080, new PartStyle(x: 1, y: 2, z: 3, rx: 0.4, ry: -0.9, rz: 0.3, sx: 2, sy: 0.5, sz: 1.5));
                b.Sphere(0.3, 6, 0x808080, new PartStyle(sy: 0.8, ry: 1.1, rz: 0.4));
            });
            Assert.AreEqual(0, WrongWayTriangles(m));
            for (int i = 0; i < m.VertexCount; i++)
            {
                double len = Math.Sqrt(m.Normals[i * 3] * m.Normals[i * 3] + m.Normals[i * 3 + 1] * m.Normals[i * 3 + 1] + m.Normals[i * 3 + 2] * m.Normals[i * 3 + 2]);
                Assert.AreEqual(1.0, len, 1e-5);
            }
        }

        [Test]
        public void PartsAreScaledThenTippedAboutZThenXThenSwungAboutYThenMoved()
        {
            // A point at (1,0,0): scaled x2 -> (2,0,0); about Z by 90 deg -> (0,2,0); about X by 90 deg -> (0,0,2); about Y by 90 deg -> (2,0,0); moved by (0,0,5).
            MeshData m = One(b => b.Box(2, 0.0001, 0.0001, 0x808080, new PartStyle(rx: Math.PI / 2, ry: Math.PI / 2, rz: Math.PI / 2, sx: 2, z: 5)));
            float maxX = float.MinValue;
            for (int i = 0; i < m.VertexCount; i++) maxX = Math.Max(maxX, m.Positions[i * 3]);
            Assert.AreEqual(2f, maxX, 1e-3);
            Assert.AreEqual(5f, m.Positions[2], 1e-2, "moved last");
        }

        [Test]
        public void EachVertexCarriesItsLinearColourAndSurface()
        {
            MeshData m = One(b => b.Box(1, 1, 1, 0x2af0ff, new PartStyle(emit: 3.8, rough: 0.3, metal: 0.1)));
            Rgb expected = Srgb.HexToLinear(0x2af0ff);
            for (int i = 0; i < m.VertexCount; i++)
            {
                Assert.AreEqual(expected.R, m.Colors[i * 3], 1e-6f);
                Assert.AreEqual(expected.G, m.Colors[i * 3 + 1], 1e-6f);
                Assert.AreEqual(expected.B, m.Colors[i * 3 + 2], 1e-6f);
                Assert.AreEqual((3.8f, 0.3f, 0.1f, 0f), (m.Uv1[i * 4], m.Uv1[i * 4 + 1], m.Uv1[i * 4 + 2], m.Uv1[i * 4 + 3]));
            }
        }

        [Test]
        public void ThePlainStyleIsTheFlatDefaultSurface()
        {
            MeshData m = One(b => b.Box(1, 1, 1, 0x808080));
            Assert.AreEqual((0f, 0.72f, 0.2f), (m.Uv1[0], m.Uv1[1], m.Uv1[2]));
            Assert.AreEqual(1.3f, m.Uvs[2], 1e-6f, "UVs span 1.3 grime tiles a face by default");
        }

        [Test]
        public void AnInstanceIsScaledThenSwungAboutYThenMoved()
        {
            MeshData unit = One(b => b.Box(2, 2, 2, 0x808080));
            var b2 = new PropMeshBuilder();
            b2.AddInstance(unit, 10, 1, 20, Math.PI / 2, 3);
            MeshData m = b2.Build();

            // the (1,1,1) corner -> scaled (3,3,3) -> swung 90 deg about Y (x' = x cos + z sin, z' = -x sin + z cos) -> (3,3,-3) -> moved (13,4,17)
            bool found = false;
            for (int i = 0; i < m.VertexCount; i++)
            {
                if (Math.Abs(m.Positions[i * 3] - 13) < 1e-4 && Math.Abs(m.Positions[i * 3 + 1] - 4) < 1e-4 && Math.Abs(m.Positions[i * 3 + 2] - 17) < 1e-4) found = true;
            }
            Assert.IsTrue(found, "the corner lands where the prototype rotation puts it");
        }

        [Test]
        public void AnInstanceTurnsItsNormalsWithIt()
        {
            MeshData unit = One(b => b.Box(2, 2, 2, 0x808080));
            var b2 = new PropMeshBuilder();
            b2.AddInstance(unit, 0, 0, 0, Math.PI / 2);
            MeshData m = b2.Build();
            // a vertex whose normal was +Z must now have normal +X
            bool found = false;
            for (int i = 0; i < m.VertexCount; i++)
            {
                if (Math.Abs(m.Normals[i * 3] - 1) < 1e-5 && Math.Abs(m.Normals[i * 3 + 1]) < 1e-5 && Math.Abs(m.Normals[i * 3 + 2]) < 1e-5) found = true;
            }
            Assert.IsTrue(found);
            Assert.AreEqual(0, WrongWayTriangles(m));
        }

        [Test]
        public void InstancesAccumulateIntoOneMeshWithOffsetIndices()
        {
            MeshData unit = One(b => b.Box(1, 1, 1, 0x808080));
            var builder = new PropMeshBuilder();
            builder.AddInstance(unit, 0, 0, 0, 0);
            builder.AddInstance(unit, 5, 0, 0, 0);
            MeshData m = builder.Build();
            Assert.AreEqual(48, m.VertexCount);
            Assert.AreEqual(24, m.TriangleCount);
            Assert.AreEqual(m.Uv1.Length, m.VertexCount * 4);
            foreach (int index in m.Indices) Assert.That(index, Is.InRange(0, 47));
        }

        [Test]
        public void ATubeFollowsItsPathWithOutwardWinding()
        {
            var path = new[] { new Float3(0, 5, 0), new Float3(1, 4.5, 0), new Float3(2, 4.2, 0), new Float3(3, 4.5, 0), new Float3(4, 5, 0) };
            MeshData t = One(b => b.Tube(path, 0.03, 3, 0x15171b));
            Assert.AreEqual(4 * 3 * 2, t.TriangleCount);
            Assert.AreEqual(0, WrongWayTriangles(t));
            float maxDistance = 0;
            for (int i = 0; i < t.VertexCount; i++)
            {
                // every vertex lies within the radius of the path: its distance from the nearest path point stays small
                double best = double.MaxValue;
                foreach (Float3 p in path)
                {
                    double d = Math.Sqrt(Math.Pow(t.Positions[i * 3] - p.X, 2) + Math.Pow(t.Positions[i * 3 + 1] - p.Y, 2) + Math.Pow(t.Positions[i * 3 + 2] - p.Z, 2));
                    best = Math.Min(best, d);
                }
                maxDistance = Math.Max(maxDistance, (float)best);
            }
            Assert.Less(maxDistance, 0.6f, "the tube hugs its path");
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            var b = new PropMeshBuilder();
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Cylinder(1, 1, 1, 2, 0x808080));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Sphere(1, 2, 0x808080));
            Assert.Throws<ArgumentException>(() => b.Tube(new[] { new Float3(0, 0, 0) }, 0.03, 3, 0x808080));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Tube(new[] { new Float3(0, 0, 0), new Float3(1, 0, 0) }, 0.03, 2, 0x808080));
            Assert.Throws<ArgumentException>(() => b.Tube(new[] { new Float3(0, 0, 0), new Float3(0, 0, 0) }, 0.03, 3, 0x808080));
            Assert.Throws<ArgumentNullException>(() => b.AddInstance(null, 0, 0, 0, 0));
        }
    }
}
