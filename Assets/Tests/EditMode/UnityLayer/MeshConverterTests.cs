using System;
using System.Linq;
using Cypher.City;
using Cypher.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    public class MeshConverterTests
    {
        Mesh _mesh;

        [TearDown]
        public void TearDown()
        {
            if (_mesh) UnityEngine.Object.DestroyImmediate(_mesh);
        }

        static MeshData TwoQuads()
        {
            var mb = new MeshBuilder();
            mb.Slab(0, 0, 4, 2, 1, new Rgb(1.18f, 0.5f, 0.25f), 1);
            mb.Slab(10, 10, 12, 14, 3, new Rgb(0.1f, 0.2f, 0.3f), 1);
            return mb.Build();
        }

        [Test]
        public void EmptyDataGivesNullSoCallersCanSkipTheRenderer()
        {
            Assert.IsNull(MeshConverter.ToMesh("empty", new MeshBuilder().Build()));
        }

        [Test]
        public void CountsAndBoundsSurviveTheConversion()
        {
            MeshData data = TwoQuads();
            _mesh = MeshConverter.ToMesh("quads", data);

            Assert.AreEqual(data.VertexCount, _mesh.vertexCount);
            Assert.AreEqual(data.Indices.Length, (int)_mesh.GetIndexCount(0));
            Assert.AreEqual(1, _mesh.subMeshCount);
            Assert.AreEqual(new Vector3(6f, 2f, 7f), _mesh.bounds.center);
            Assert.AreEqual(new Vector3(12f, 2f, 14f), _mesh.bounds.size);
        }

        [Test]
        public void HdrVertexColourIsNotClamped()
        {
            _mesh = MeshConverter.ToMesh("quads", TwoQuads());
            Color[] colours = _mesh.colors;
            Assert.AreEqual(1.18f, colours[0].r, 1e-5f);
            Assert.AreEqual(0.25f, colours[0].b, 1e-5f);
        }

        [Test]
        public void NormalsUvsAndIndicesRoundTrip()
        {
            MeshData data = TwoQuads();
            _mesh = MeshConverter.ToMesh("quads", data);

            Vector3[] normals = _mesh.normals;
            Vector2[] uvs = _mesh.uv;
            int[] indices = _mesh.GetIndices(0);
            for (int i = 0; i < data.VertexCount; i++)
            {
                Assert.AreEqual(data.Normals[i * 3 + 1], normals[i].y, 1e-6f);
                Assert.AreEqual(data.Uvs[i * 2], uvs[i].x, 1e-6f);
                Assert.AreEqual(data.Uvs[i * 2 + 1], uvs[i].y, 1e-6f);
            }
            CollectionAssert.AreEqual(data.Indices, indices);
        }

        [Test]
        public void SecondUvSetRoundTripsAsFourFloats()
        {
            MeshData data = TwoQuads();
            data.Uv1 = new float[data.VertexCount * 4];
            for (int i = 0; i < data.Uv1.Length; i++) data.Uv1[i] = i * 0.5f;
            _mesh = MeshConverter.ToMesh("neon", data);

            var uv1 = new System.Collections.Generic.List<Vector4>();
            _mesh.GetUVs(1, uv1);
            Assert.AreEqual(data.VertexCount, uv1.Count);
            Assert.AreEqual(new Vector4(0f, 0.5f, 1f, 1.5f), uv1[0]);
            Assert.AreEqual(new Vector4(2f, 2.5f, 3f, 3.5f), uv1[1]);
        }

        [Test]
        public void MeshesWithoutASecondUvSetGetZeros()
        {
            _mesh = MeshConverter.ToMesh("plain", TwoQuads());
            var uv1 = new System.Collections.Generic.List<Vector4>();
            _mesh.GetUVs(1, uv1);
            Assert.IsTrue(uv1.All(v => v == Vector4.zero));
        }

        [Test]
        public void MismatchedSecondUvSetFailsLoudly()
        {
            MeshData data = TwoQuads();
            data.Uv1 = new float[5];
            Assert.Throws<ArgumentException>(() => MeshConverter.ToMesh("bad", data));
        }

        [Test]
        public void MismatchedAttributeArraysFailLoudly()
        {
            MeshData data = TwoQuads();
            data.Normals = new float[3];
            Assert.Throws<ArgumentException>(() => MeshConverter.ToMesh("bad", data));
            Assert.Throws<ArgumentNullException>(() => MeshConverter.ToMesh("null", null));
        }
    }
}
