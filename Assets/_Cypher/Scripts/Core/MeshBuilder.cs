using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    public readonly struct Rgb
    {
        public readonly float R, G, B;

        public Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public Rgb Scale(float k) => new Rgb(R * k, G * k, B * k);
    }

    public readonly struct Float3
    {
        public readonly float X, Y, Z;

        public Float3(double x, double y, double z)
        {
            X = (float)x;
            Y = (float)y;
            Z = (float)z;
        }
    }

    /// <summary>Raw arrays for one mesh. Converted to a UnityEngine.Mesh outside Core.</summary>
    public sealed class MeshData
    {
        public float[] Positions = Array.Empty<float>();   // xyz per vertex
        public float[] Normals = Array.Empty<float>();     // xyz per vertex
        public float[] Uvs = Array.Empty<float>();         // uv per vertex
        public float[] Colors = Array.Empty<float>();      // rgb per vertex
        public int[] Indices = Array.Empty<int>();

        /// <summary>
        /// Optional second UV set, four floats per vertex, empty when unused. The neon shader reads
        /// per-quad parameters (seed, flicker mode and shape, extra, gain) from it.
        /// </summary>
        public float[] Uv1 = Array.Empty<float>();

        public int VertexCount => Positions.Length / 3;
        public int TriangleCount => Indices.Length / 3;

        /// <summary>Copy shifted by (dx,dy,dz). Normals, UVs, colours and indices are shared by value.</summary>
        public MeshData Translated(double dx, double dy, double dz)
        {
            var p = (float[])Positions.Clone();
            for (int i = 0; i < p.Length; i += 3)
            {
                p[i] += (float)dx;
                p[i + 1] += (float)dy;
                p[i + 2] += (float)dz;
            }
            return new MeshData
            {
                Positions = p,
                Normals = (float[])Normals.Clone(),
                Uvs = (float[])Uvs.Clone(),
                Colors = (float[])Colors.Clone(),
                Indices = (int[])Indices.Clone(),
                Uv1 = (float[])Uv1.Clone(),
            };
        }
    }

    /// <summary>
    /// Accumulates quads and boxes for one material, then bakes a single MeshData
    /// carrying vertex colour so one merged mesh reads as many surfaces. A port of
    /// the prototype's MeshBuilder (src/world/City.js); the call sites translate
    /// one-to-one. Quad vertices are given in the prototype's order, counter-clockwise
    /// seen from the front; the stored normal is cross(b-a, d-a), and that same
    /// direction is the front face in Unity (see CityLayout).
    /// </summary>
    public sealed class MeshBuilder
    {
        public const double ConcreteUvScale = 4.5;   // metres per concrete texture repeat

        readonly List<float> _p = new List<float>();
        readonly List<float> _n = new List<float>();
        readonly List<float> _t = new List<float>();
        readonly List<float> _c = new List<float>();
        readonly List<int> _i = new List<int>();

        public bool IsEmpty => _i.Count == 0;
        public int TriangleCount => _i.Count / 3;
        public int VertexCount => _p.Count / 3;

        /// <summary>
        /// a,b,c,d counter-clockwise from the front. uv is 8 numbers. colA colours
        /// vertices a,b; colB colours c,d (a free vertical grime gradient on walls).
        /// </summary>
        public void Quad(Float3 a, Float3 b, Float3 c, Float3 d, float[] uv, Rgb colA, Rgb colB)
        {
            if (uv.Length != 8) throw new ArgumentException("quad uv needs 8 numbers", nameof(uv));
            int baseIndex = VertexCount;
            Normal(a, b, d, out float nx, out float ny, out float nz);
            Float3[] vs = { a, b, c, d };
            Rgb[] cs = { colA, colA, colB, colB };
            for (int k = 0; k < 4; k++)
            {
                AddVertex(vs[k], nx, ny, nz, uv[k * 2], uv[k * 2 + 1], cs[k]);
            }
            _i.Add(baseIndex); _i.Add(baseIndex + 1); _i.Add(baseIndex + 2);
            _i.Add(baseIndex); _i.Add(baseIndex + 2); _i.Add(baseIndex + 3);
        }

        public void Quad(Float3 a, Float3 b, Float3 c, Float3 d, float[] uv, Rgb col) => Quad(a, b, c, d, uv, col, col);

        /// <summary>Convex polygon face, fan-triangulated. uv is 2 numbers per point.</summary>
        public void Face(IReadOnlyList<Float3> pts, float[] uv, Rgb col)
        {
            if (pts.Count < 3) throw new ArgumentException("face needs at least 3 points", nameof(pts));
            if (uv.Length != pts.Count * 2) throw new ArgumentException("face needs 2 uv numbers per point", nameof(uv));
            int baseIndex = VertexCount;
            Normal(pts[0], pts[1], pts[2], out float nx, out float ny, out float nz);
            for (int k = 0; k < pts.Count; k++)
            {
                AddVertex(pts[k], nx, ny, nz, uv[k * 2], uv[k * 2 + 1], col);
            }
            for (int k = 1; k < pts.Count - 1; k++)
            {
                _i.Add(baseIndex); _i.Add(baseIndex + k); _i.Add(baseIndex + k + 1);
            }
        }

        /// <summary>Axis-aligned box by centre + half extents.</summary>
        public void Box(double cx, double cy, double cz, double hx, double hy, double hz,
            Rgb col, double s = ConcreteUvScale, Rgb? colTop = null)
        {
            Rgb top = colTop ?? col;
            double x0 = cx - hx, x1 = cx + hx;
            double y0 = cy - hy, y1 = cy + hy;
            double z0 = cz - hz, z1 = cz + hz;
            float U(double v) => (float)(v / s);

            // +X / -X
            Quad(new Float3(x1, y0, z1), new Float3(x1, y0, z0), new Float3(x1, y1, z0), new Float3(x1, y1, z1),
                new[] { U(z1), U(y0), U(z0), U(y0), U(z0), U(y1), U(z1), U(y1) }, col, top);
            Quad(new Float3(x0, y0, z0), new Float3(x0, y0, z1), new Float3(x0, y1, z1), new Float3(x0, y1, z0),
                new[] { U(z0), U(y0), U(z1), U(y0), U(z1), U(y1), U(z0), U(y1) }, col, top);
            // +Z / -Z
            Quad(new Float3(x0, y0, z1), new Float3(x1, y0, z1), new Float3(x1, y1, z1), new Float3(x0, y1, z1),
                new[] { U(x0), U(y0), U(x1), U(y0), U(x1), U(y1), U(x0), U(y1) }, col, top);
            Quad(new Float3(x1, y0, z0), new Float3(x0, y0, z0), new Float3(x0, y1, z0), new Float3(x1, y1, z0),
                new[] { U(x1), U(y0), U(x0), U(y0), U(x0), U(y1), U(x1), U(y1) }, col, top);
            // +Y / -Y
            Quad(new Float3(x0, y1, z1), new Float3(x1, y1, z1), new Float3(x1, y1, z0), new Float3(x0, y1, z0),
                new[] { U(x0), U(z1), U(x1), U(z1), U(x1), U(z0), U(x0), U(z0) }, top);
            Quad(new Float3(x0, y0, z0), new Float3(x1, y0, z0), new Float3(x1, y0, z1), new Float3(x0, y0, z1),
                new[] { U(x0), U(z0), U(x1), U(z0), U(x1), U(z1), U(x0), U(z1) }, col);
        }

        /// <summary>Flat horizontal quad (facing +Y) from an XZ rect.</summary>
        public void Slab(double x0, double z0, double x1, double z1, double y, Rgb col, double s = ConcreteUvScale)
        {
            Quad(new Float3(x0, y, z1), new Float3(x1, y, z1), new Float3(x1, y, z0), new Float3(x0, y, z0),
                new[]
                {
                    (float)(x0 / s), (float)(z1 / s), (float)(x1 / s), (float)(z1 / s),
                    (float)(x1 / s), (float)(z0 / s), (float)(x0 / s), (float)(z0 / s),
                }, col);
        }

        public MeshData Build()
        {
            return new MeshData
            {
                Positions = _p.ToArray(),
                Normals = _n.ToArray(),
                Uvs = _t.ToArray(),
                Colors = _c.ToArray(),
                Indices = _i.ToArray(),
            };
        }

        void AddVertex(Float3 p, float nx, float ny, float nz, float u, float v, Rgb c)
        {
            _p.Add(p.X); _p.Add(p.Y); _p.Add(p.Z);
            _n.Add(nx); _n.Add(ny); _n.Add(nz);
            _t.Add(u); _t.Add(v);
            _c.Add(c.R); _c.Add(c.G); _c.Add(c.B);
        }

        static void Normal(Float3 a, Float3 b, Float3 c, out float nx, out float ny, out float nz)
        {
            double ax = b.X - a.X, ay = b.Y - a.Y, az = b.Z - a.Z;
            double bx = c.X - a.X, by = c.Y - a.Y, bz = c.Z - a.Z;
            double x = ay * bz - az * by, y = az * bx - ax * bz, z = ax * by - ay * bx;
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (len < 1e-12) len = 1;
            nx = (float)(x / len);
            ny = (float)(y / len);
            nz = (float)(z / len);
        }
    }
}
