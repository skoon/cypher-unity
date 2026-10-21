using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class MeshBuilderTests
    {
        static readonly Rgb White = new Rgb(1, 1, 1);

        /// <summary>Unity front face for a triangle is the direction of cross(b-a, c-a).</summary>
        static void TriangleNormal(MeshData m, int tri, out double x, out double y, out double z)
        {
            int ia = m.Indices[tri * 3] * 3, ib = m.Indices[tri * 3 + 1] * 3, ic = m.Indices[tri * 3 + 2] * 3;
            double ax = m.Positions[ib] - m.Positions[ia], ay = m.Positions[ib + 1] - m.Positions[ia + 1], az = m.Positions[ib + 2] - m.Positions[ia + 2];
            double bx = m.Positions[ic] - m.Positions[ia], by = m.Positions[ic + 1] - m.Positions[ia + 1], bz = m.Positions[ic + 2] - m.Positions[ia + 2];
            x = ay * bz - az * by;
            y = az * bx - ax * bz;
            z = ax * by - ay * bx;
            double len = Math.Sqrt(x * x + y * y + z * z);
            x /= len; y /= len; z /= len;
        }

        [Test]
        public void BoxHasSixQuads()
        {
            var mb = new MeshBuilder();
            mb.Box(0, 0, 0, 1, 1, 1, White);
            MeshData m = mb.Build();
            Assert.AreEqual(24, m.VertexCount);
            Assert.AreEqual(12, m.TriangleCount);
            Assert.AreEqual(m.VertexCount * 2, m.Uvs.Length);
            Assert.AreEqual(m.VertexCount * 3, m.Colors.Length);
        }

        [Test]
        public void BoxTrianglesFaceOutwardAndMatchStoredNormals()
        {
            var mb = new MeshBuilder();
            mb.Box(5, 2, -3, 1, 2, 3, White);
            MeshData m = mb.Build();
            for (int t = 0; t < m.TriangleCount; t++)
            {
                TriangleNormal(m, t, out double nx, out double ny, out double nz);
                int vi = m.Indices[t * 3] * 3;
                // Outward = pointing away from the box centre (5,2,-3).
                double ox = m.Positions[vi] - 5, oy = m.Positions[vi + 1] - 2, oz = m.Positions[vi + 2] + 3;
                Assert.Greater(nx * ox + ny * oy + nz * oz, 0, $"triangle {t} faces inward");
                Assert.AreEqual(1.0, nx * m.Normals[vi] + ny * m.Normals[vi + 1] + nz * m.Normals[vi + 2], 1e-5, $"triangle {t} stored normal disagrees with winding");
            }
        }

        [Test]
        public void SlabFacesUp()
        {
            var mb = new MeshBuilder();
            mb.Slab(-5, -5, 5, 5, 0.2, White);
            MeshData m = mb.Build();
            Assert.AreEqual(2, m.TriangleCount);
            for (int t = 0; t < 2; t++)
            {
                TriangleNormal(m, t, out _, out double ny, out _);
                Assert.AreEqual(1.0, ny, 1e-9);
            }
            Assert.AreEqual(1f, m.Normals[1], 1e-6);
        }

        [Test]
        public void FaceFanTriangulatesConvexPolygon()
        {
            var mb = new MeshBuilder();
            var pts = new[] { new Float3(0, 0, 0), new Float3(1, 0, 0), new Float3(1, 0, 1), new Float3(0.5, 0, 1.5), new Float3(0, 0, 1) };
            mb.Face(pts, new float[10], White);
            Assert.AreEqual(5, mb.VertexCount);
            Assert.AreEqual(3, mb.TriangleCount);
        }

        [Test]
        public void QuadColoursSplitBetweenBottomAndTopEdges()
        {
            var mb = new MeshBuilder();
            var lo = new Rgb(0.1f, 0.2f, 0.3f);
            var hi = new Rgb(0.9f, 0.8f, 0.7f);
            mb.Quad(new Float3(0, 0, 0), new Float3(1, 0, 0), new Float3(1, 1, 0), new Float3(0, 1, 0), new float[8], lo, hi);
            MeshData m = mb.Build();
            Assert.AreEqual(0.1f, m.Colors[0]);   // a
            Assert.AreEqual(0.1f, m.Colors[3]);   // b
            Assert.AreEqual(0.9f, m.Colors[6]);   // c
            Assert.AreEqual(0.9f, m.Colors[9]);   // d
        }

        [Test]
        public void BadUvLengthThrows()
        {
            var mb = new MeshBuilder();
            Assert.Throws<ArgumentException>(() => mb.Quad(new Float3(0, 0, 0), new Float3(1, 0, 0), new Float3(1, 1, 0), new Float3(0, 1, 0), new float[6], White));
            Assert.Throws<ArgumentException>(() => mb.Face(new[] { new Float3(0, 0, 0), new Float3(1, 0, 0), new Float3(1, 0, 1) }, new float[4], White));
            Assert.Throws<ArgumentException>(() => mb.Face(new[] { new Float3(0, 0, 0), new Float3(1, 0, 0) }, new float[4], White));
        }

        [Test]
        public void EmptyBuilderIsEmptyAndBuildsEmptyData()
        {
            var mb = new MeshBuilder();
            Assert.IsTrue(mb.IsEmpty);
            MeshData m = mb.Build();
            Assert.AreEqual(0, m.VertexCount);
            Assert.AreEqual(0, m.TriangleCount);
        }

        [Test]
        public void IndicesAreValidAndOffsetAcrossPrimitives()
        {
            var mb = new MeshBuilder();
            mb.Box(0, 0, 0, 1, 1, 1, White);
            mb.Box(10, 0, 0, 1, 1, 1, White);
            MeshData m = mb.Build();
            Assert.AreEqual(48, m.VertexCount);
            foreach (int i in m.Indices)
            {
                Assert.GreaterOrEqual(i, 0);
                Assert.Less(i, m.VertexCount);
            }
        }
    }
}
