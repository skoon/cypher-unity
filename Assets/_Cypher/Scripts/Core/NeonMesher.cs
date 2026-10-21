using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>The meshes for one grid cell's signage.</summary>
    public sealed class NeonCellMeshes
    {
        /// <summary>Additive emissive quads. Colour is the linear tint; Uv1 = (seed, mode + 8 * shape, extra, gain).</summary>
        public MeshData Emissive = new MeshData();

        /// <summary>Dark housings behind signs and fittings, plain vertex-coloured boxes.</summary>
        public MeshData Housing = new MeshData();

        public int TriangleCount => Emissive.TriangleCount + Housing.TriangleCount;
    }

    /// <summary>Turns a <see cref="NeonLayout"/> into per-cell mesh data.</summary>
    public static class NeonMesher
    {
        /// <summary>Housing boxes are painted this dark metal colour (vertex colour).</summary>
        public static readonly Rgb HousingColour = new Rgb(0.12f, 0.12f, 0.14f);

        /// <summary>Shapes are packed into the flicker-mode channel as mode + ShapeStride * shape.</summary>
        public const int ShapeStride = 8;

        public static Dictionary<(int Gx, int Gz), NeonCellMeshes> Build(NeonLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));

            var quads = new Dictionary<(int, int), QuadBuilder>();
            foreach (NeonQuad q in layout.Quads)
            {
                (int, int) key = (q.Gx, q.Gz);
                if (!quads.TryGetValue(key, out QuadBuilder qb)) quads.Add(key, qb = new QuadBuilder());
                qb.Add(q);
            }

            var boxes = new Dictionary<(int, int), MeshBuilder>();
            foreach (NeonBox b in layout.Boxes)
            {
                (int, int) key = (b.Gx, b.Gz);
                if (!boxes.TryGetValue(key, out MeshBuilder mb)) boxes.Add(key, mb = new MeshBuilder());
                AddBox(mb, b, HousingColour);
            }

            var result = new Dictionary<(int Gx, int Gz), NeonCellMeshes>();
            foreach (KeyValuePair<(int, int), QuadBuilder> kv in quads)
            {
                result[kv.Key] = new NeonCellMeshes { Emissive = kv.Value.Build() };
            }
            foreach (KeyValuePair<(int, int), MeshBuilder> kv in boxes)
            {
                if (!result.TryGetValue(kv.Key, out NeonCellMeshes cell)) result[kv.Key] = cell = new NeonCellMeshes();
                cell.Housing = kv.Value.Build();
            }
            return result;
        }

        /// <summary>
        /// Oriented box: local Z along (DirX, 0, DirZ), local X that turned a quarter turn, local Y up, sized
        /// (SizeX, SizeY, SizeZ) about its centre. All six faces, wound outward.
        /// </summary>
        public static void AddBox(MeshBuilder mb, NeonBox b, Rgb colour)
        {
            if (mb == null) throw new ArgumentNullException(nameof(mb));
            double dl = Math.Sqrt(b.DirX * b.DirX + b.DirZ * b.DirZ);
            if (dl < 1e-9) throw new ArgumentException("box direction is zero", nameof(b));
            double dx = b.DirX / dl, dz = b.DirZ / dl;

            // Full-size axis vectors. X = (dz, 0, -dx), Y = up, Z = (dx, 0, dz); X x Y = Z.
            var a0 = new V3(dz * b.SizeX, 0, -dx * b.SizeX);
            var a1 = new V3(0, b.SizeY, 0);
            var a2 = new V3(dx * b.SizeZ, 0, dz * b.SizeZ);
            var baseCorner = new V3(b.X - (a0.X + a1.X + a2.X) / 2, b.Y - (a0.Y + a1.Y + a2.Y) / 2, b.Z - (a0.Z + a1.Z + a2.Z) / 2);

            // For each face the two edge vectors (u, v) satisfy u x v = outward normal.
            Face(mb, baseCorner + a0, a1, a2, colour);   // +X
            Face(mb, baseCorner, a2, a1, colour);        // -X
            Face(mb, baseCorner + a1, a2, a0, colour);   // +Y
            Face(mb, baseCorner, a0, a2, colour);        // -Y
            Face(mb, baseCorner + a2, a0, a1, colour);   // +Z
            Face(mb, baseCorner, a1, a0, colour);        // -Z
        }

        static void Face(MeshBuilder mb, V3 origin, V3 u, V3 v, Rgb colour)
        {
            V3 a = origin, b = origin + u, c = origin + u + v, d = origin + v;
            mb.Quad(a.F(), b.F(), c.F(), d.F(), new float[8], colour);
        }

        readonly struct V3
        {
            public readonly double X, Y, Z;

            public V3(double x, double y, double z)
            {
                X = x; Y = y; Z = z;
            }

            public static V3 operator +(V3 p, V3 q) => new V3(p.X + q.X, p.Y + q.Y, p.Z + q.Z);

            public Float3 F() => new Float3(X, Y, Z);
        }

        /// <summary>Accumulates emissive quads with the extra per-vertex parameter channel.</summary>
        sealed class QuadBuilder
        {
            readonly List<float> _p = new List<float>();
            readonly List<float> _n = new List<float>();
            readonly List<float> _t = new List<float>();
            readonly List<float> _c = new List<float>();
            readonly List<float> _u1 = new List<float>();
            readonly List<int> _i = new List<int>();

            public void Add(NeonQuad q)
            {
                if (!(q.Width > 0) || !(q.Height > 0)) throw new ArgumentException("neon quad needs a positive size");
                double hw = q.Width / 2, hh = q.Height / 2;
                V3 a, b, c, d;
                double nx, ny, nz;

                if (q.FacingUp)
                {
                    a = new V3(q.X - hw, q.Y, q.Z + hh);
                    b = new V3(q.X + hw, q.Y, q.Z + hh);
                    c = new V3(q.X + hw, q.Y, q.Z - hh);
                    d = new V3(q.X - hw, q.Y, q.Z - hh);
                    nx = 0; ny = 1; nz = 0;
                }
                else
                {
                    double len = Math.Sqrt(q.Nx * q.Nx + q.Nz * q.Nz);
                    if (len < 1e-9) throw new ArgumentException("neon quad faces nowhere");
                    nx = q.Nx / len; nz = q.Nz / len; ny = 0;

                    // Right = (nz, 0, -nx), up = Y; a roll turns both about the facing axis.
                    double rx = nz, rz = -nx;
                    double cr = Math.Cos(q.Roll), sr = Math.Sin(q.Roll);
                    var xAxis = new V3(rx * cr, sr, rz * cr);       // cos * right + sin * up
                    var yAxis = new V3(-rx * sr, cr, -rz * sr);     // -sin * right + cos * up
                    var centre = new V3(q.X, q.Y, q.Z);
                    a = centre + Scale(xAxis, -hw) + Scale(yAxis, -hh);
                    b = centre + Scale(xAxis, hw) + Scale(yAxis, -hh);
                    c = centre + Scale(xAxis, hw) + Scale(yAxis, hh);
                    d = centre + Scale(xAxis, -hw) + Scale(yAxis, hh);
                }

                float extra = ExtraFor(q);
                float modeAndShape = q.FlickerMode + ShapeStride * (int)q.Shape;
                int baseIndex = _p.Count / 3;
                V3[] vs = { a, b, c, d };
                float[] uv = { 0, 0, 1, 0, 1, 1, 0, 1 };
                for (int k = 0; k < 4; k++)
                {
                    _p.Add((float)vs[k].X); _p.Add((float)vs[k].Y); _p.Add((float)vs[k].Z);
                    _n.Add((float)nx); _n.Add((float)ny); _n.Add((float)nz);
                    _t.Add(uv[k * 2]); _t.Add(uv[k * 2 + 1]);
                    _c.Add(q.Colour.R); _c.Add(q.Colour.G); _c.Add(q.Colour.B);
                    _u1.Add((float)q.Seed); _u1.Add(modeAndShape); _u1.Add(extra); _u1.Add((float)q.Gain);
                }
                _i.Add(baseIndex); _i.Add(baseIndex + 1); _i.Add(baseIndex + 2);
                _i.Add(baseIndex); _i.Add(baseIndex + 2); _i.Add(baseIndex + 3);
            }

            /// <summary>Tubes carry their chase segments, panels their aspect ratio, bulbs their count.</summary>
            static float ExtraFor(NeonQuad q)
            {
                switch (q.Shape)
                {
                    case NeonShape.Panel: return (float)(q.Width / q.Height);
                    case NeonShape.Bulbs: return (float)Math.Max(1, Math.Round(q.Chase));
                    case NeonShape.Radial: return 0f;
                    default: return (float)q.Chase;
                }
            }

            static V3 Scale(V3 v, double k) => new V3(v.X * k, v.Y * k, v.Z * k);

            public MeshData Build() => new MeshData
            {
                Positions = _p.ToArray(),
                Normals = _n.ToArray(),
                Uvs = _t.ToArray(),
                Colors = _c.ToArray(),
                Uv1 = _u1.ToArray(),
                Indices = _i.ToArray(),
            };
        }
    }
}
