using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>The meshes for one grid cell's signage.</summary>
    public sealed class NeonCellMeshes
    {
        /// <summary>
        /// Additive emissive quads. Colour is the linear tint; Uv1 = (seed, mode + 8 * shape, extra, gain). Panel quads carry the texture
        /// coordinates of their lettering in the sign atlas; every other shape has 0 to 1 across the quad.
        /// </summary>
        public MeshData Emissive = new MeshData();

        /// <summary>Dark housings behind signs and fittings, plain vertex-coloured boxes.</summary>
        public MeshData Housing = new MeshData();

        /// <summary>
        /// LED hoardings, one quad each. Colour is the linear tint; Uv1 = (seed, glitch, ticker speed, gain); the texture coordinates
        /// are those of the panel's art in the board atlas (see <see cref="BoardArt.AtlasUv"/>).
        /// </summary>
        public MeshData Boards = new MeshData();

        /// <summary>Floating projections, built the same way as <see cref="Boards"/> and drawn additively.</summary>
        public MeshData Holos = new MeshData();

        public int TriangleCount => Emissive.TriangleCount + Housing.TriangleCount + Boards.TriangleCount + Holos.TriangleCount;
    }

    /// <summary>Turns a <see cref="NeonLayout"/> into per-cell mesh data.</summary>
    public static class NeonMesher
    {
        /// <summary>Housing boxes are painted this dark metal colour (vertex colour).</summary>
        public static readonly Rgb HousingColour = new Rgb(0.12f, 0.12f, 0.14f);

        /// <summary>Shapes are packed into the flicker-mode channel as mode + ShapeStride * shape.</summary>
        public const int ShapeStride = 8;

        /// <param name="atlas">Where the lettering is, for the panel quads. Defaults to the standard atlas.</param>
        public static Dictionary<(int Gx, int Gz), NeonCellMeshes> Build(NeonLayout layout, NeonAtlasLayout atlas = null)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            atlas = atlas ?? NeonAtlas.Layout();

            var quads = new Dictionary<(int, int), QuadBuilder>();
            foreach (NeonQuad q in layout.Quads)
            {
                (int, int) key = (q.Gx, q.Gz);
                if (!quads.TryGetValue(key, out QuadBuilder qb)) quads.Add(key, qb = new QuadBuilder(atlas));
                qb.Add(q);
            }

            var boards = new Dictionary<(int, int), BoardBuilder>();
            var holos = new Dictionary<(int, int), BoardBuilder>();
            AddBoards(layout.Boards, boards);
            AddBoards(layout.Holograms, holos);

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
                Cell(result, kv.Key).Housing = kv.Value.Build();
            }
            foreach (KeyValuePair<(int, int), BoardBuilder> kv in boards) Cell(result, kv.Key).Boards = kv.Value.Build();
            foreach (KeyValuePair<(int, int), BoardBuilder> kv in holos) Cell(result, kv.Key).Holos = kv.Value.Build();
            return result;
        }

        static NeonCellMeshes Cell(Dictionary<(int Gx, int Gz), NeonCellMeshes> result, (int, int) key)
        {
            if (!result.TryGetValue(key, out NeonCellMeshes cell)) result[key] = cell = new NeonCellMeshes();
            return cell;
        }

        static void AddBoards(List<NeonBoard> boards, Dictionary<(int, int), BoardBuilder> into)
        {
            foreach (NeonBoard b in boards)
            {
                (int, int) key = (b.Gx, b.Gz);
                if (!into.TryGetValue(key, out BoardBuilder bb)) into.Add(key, bb = new BoardBuilder());
                bb.Add(b);
            }
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
            readonly NeonAtlasLayout _atlas;
            readonly List<float> _p = new List<float>();
            readonly List<float> _n = new List<float>();
            readonly List<float> _t = new List<float>();
            readonly List<float> _c = new List<float>();
            readonly List<float> _u1 = new List<float>();
            readonly List<int> _i = new List<int>();

            public QuadBuilder(NeonAtlasLayout atlas)
            {
                _atlas = atlas;
            }

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
                float[] uv = q.Shape == NeonShape.Panel ? LetteringUv(q) : new float[] { 0, 0, 1, 0, 1, 1, 0, 1 };
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

            /// <summary>
            /// The corners' texture coordinates in the atlas cell. The quad's right-hand edge runs the way a viewer in front of the face sees
            /// its left (Unity's axes are the mirror of the prototype's, which the city keeps), so the lettering is read from the right.
            /// </summary>
            float[] LetteringUv(NeonQuad q)
            {
                if (q.Cell == null) throw new ArgumentException("a sign panel needs an atlas cell");
                (double u, double v, double du, double dv) = _atlas[q.Cell].Uv(_atlas.Size);
                return new[]
                {
                    (float)(u + du), (float)v,
                    (float)u, (float)v,
                    (float)u, (float)(v + dv),
                    (float)(u + du), (float)(v + dv),
                };
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

        /// <summary>Accumulates screens, one quad each, wound like the emissive quads.</summary>
        sealed class BoardBuilder
        {
            readonly List<float> _p = new List<float>();
            readonly List<float> _n = new List<float>();
            readonly List<float> _t = new List<float>();
            readonly List<float> _c = new List<float>();
            readonly List<float> _u1 = new List<float>();
            readonly List<int> _i = new List<int>();

            public void Add(NeonBoard b)
            {
                if (!(b.Width > 0) || !(b.Height > 0)) throw new ArgumentException("a board needs a positive size");
                if (b.Panel < 0 || b.Panel >= BoardArt.PanelCount) throw new ArgumentException($"there is no board panel {b.Panel}");
                double len = Math.Sqrt(b.Nx * b.Nx + b.Nz * b.Nz);
                if (len < 1e-9) throw new ArgumentException("a board faces nowhere");
                double nx = b.Nx / len, nz = b.Nz / len;
                double hw = b.Width / 2, hh = b.Height / 2;
                double rx = nz, rz = -nx;   // right = (nz, 0, -nx), as for the emissive quads

                V3[] corners =
                {
                    new V3(b.X - rx * hw, b.Y - hh, b.Z - rz * hw),
                    new V3(b.X + rx * hw, b.Y - hh, b.Z + rz * hw),
                    new V3(b.X + rx * hw, b.Y + hh, b.Z + rz * hw),
                    new V3(b.X - rx * hw, b.Y + hh, b.Z - rz * hw),
                };
                // the picture is read from the right of that axis, as for the lettering
                double[] lu = { 1, 0, 0, 1 }, lv = { 0, 0, 1, 1 };
                int baseIndex = _p.Count / 3;
                for (int k = 0; k < 4; k++)
                {
                    (double u, double v) = BoardArt.AtlasUv(b.Panel, lu[k], lv[k]);
                    _p.Add((float)corners[k].X); _p.Add((float)corners[k].Y); _p.Add((float)corners[k].Z);
                    _n.Add((float)nx); _n.Add(0f); _n.Add((float)nz);
                    _t.Add((float)u); _t.Add((float)v);
                    _c.Add(b.Colour.R); _c.Add(b.Colour.G); _c.Add(b.Colour.B);
                    _u1.Add((float)b.Seed); _u1.Add((float)b.Glitch); _u1.Add((float)b.TickerSpeed); _u1.Add((float)b.Gain);
                }
                _i.Add(baseIndex); _i.Add(baseIndex + 1); _i.Add(baseIndex + 2);
                _i.Add(baseIndex); _i.Add(baseIndex + 2); _i.Add(baseIndex + 3);
            }

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
