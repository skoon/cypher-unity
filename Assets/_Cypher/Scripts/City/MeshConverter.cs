using System;
using System.Runtime.InteropServices;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.City
{
    /// <summary>Converts Core's engine-free MeshData into a UnityEngine.Mesh.</summary>
    public static class MeshConverter
    {
        // Vertex colour is stored as Float32 so HDR values (the prototype's kerb nosing is 1.18) survive;
        // the default 8-bit colour format would clamp them.
        [StructLayout(LayoutKind.Sequential)]
        struct Vertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector4 Color;
            public Vector2 Uv;
            public Vector4 Uv1;   // zero unless the mesh carries a second UV set (neon quad parameters)
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
        };

        /// <summary>Returns null for empty data so callers can skip creating a renderer.</summary>
        public static Mesh ToMesh(string name, MeshData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            int count = data.VertexCount;
            if (count == 0 || data.Indices.Length == 0) return null;
            if (data.Normals.Length != count * 3 || data.Uvs.Length != count * 2 || data.Colors.Length != count * 3)
            {
                throw new ArgumentException($"mesh '{name}' has attribute arrays that do not match {count} vertices", nameof(data));
            }
            bool hasUv1 = data.Uv1.Length != 0;
            if (hasUv1 && data.Uv1.Length != count * 4)
            {
                throw new ArgumentException($"mesh '{name}' has a second UV set that does not match {count} vertices", nameof(data));
            }

            var vertices = new Vertex[count];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vertex
                {
                    Position = new Vector3(data.Positions[i * 3], data.Positions[i * 3 + 1], data.Positions[i * 3 + 2]),
                    Normal = new Vector3(data.Normals[i * 3], data.Normals[i * 3 + 1], data.Normals[i * 3 + 2]),
                    Color = new Vector4(data.Colors[i * 3], data.Colors[i * 3 + 1], data.Colors[i * 3 + 2], 1f),
                    Uv = new Vector2(data.Uvs[i * 2], data.Uvs[i * 2 + 1]),
                    Uv1 = hasUv1 ? new Vector4(data.Uv1[i * 4], data.Uv1[i * 4 + 1], data.Uv1[i * 4 + 2], data.Uv1[i * 4 + 3]) : Vector4.zero,
                };
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertexBufferParams(count, Layout);
            mesh.SetVertexBufferData(vertices, 0, 0, count);
            mesh.SetIndexBufferParams(data.Indices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(data.Indices, 0, 0, data.Indices.Length);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, data.Indices.Length));
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
