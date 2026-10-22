using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// How one part of a prop is placed and painted, as the prototype's part() options: it is scaled, then tipped about Z, then X,
    /// then swung about Y, then moved. <see cref="Emit"/>, <see cref="Rough"/> and <see cref="Metal"/> become the per-vertex surface
    /// (<c>Uv1</c> = emit, roughness, metalness), which is how one material covers rusted steel, wet plastic and a blown-out neon panel.
    /// </summary>
    public readonly struct PartStyle
    {
        public readonly double X, Y, Z, Rx, Ry, Rz, Sx, Sy, Sz, Emit, Rough, Metal, Uv;
        public readonly bool Open;

        public PartStyle(double x = 0, double y = 0, double z = 0, double rx = 0, double ry = 0, double rz = 0,
            double sx = 1, double sy = 1, double sz = 1, double emit = 0, double rough = 0.72, double metal = 0.2, double uv = 1.3, bool open = false)
        {
            X = x; Y = y; Z = z; Rx = rx; Ry = ry; Rz = rz; Sx = sx; Sy = sy; Sz = sz;
            Emit = emit; Rough = rough; Metal = metal; Uv = uv; Open = open;
        }
    }

    /// <summary>
    /// Accumulates prop geometry. Every vertex carries a colour (linear albedo) and a surface (emit, rough, metal) in <c>Uv1</c>, and
    /// UVs scaled so the shared grime map tiles at a roughly constant size. Triangles wind so that cross(b-a, c-a) points along the
    /// outward normal, like <see cref="MeshBuilder"/>.
    /// </summary>
    public sealed class PropMeshBuilder
    {
        readonly List<float> _p = new List<float>();
        readonly List<float> _n = new List<float>();
        readonly List<float> _t = new List<float>();
        readonly List<float> _c = new List<float>();
        readonly List<float> _s = new List<float>();
        readonly List<int> _i = new List<int>();

        public int VertexCount => _p.Count / 3;
        public int TriangleCount => _i.Count / 3;
        public bool IsEmpty => _i.Count == 0;

        public MeshData Build() => new MeshData
        {
            Positions = _p.ToArray(),
            Normals = _n.ToArray(),
            Uvs = _t.ToArray(),
            Colors = _c.ToArray(),
            Uv1 = _s.ToArray(),
            Indices = _i.ToArray(),
        };

        // ------------------------------------------------------------------ parts

        /// <summary>Box w x h x d about its own centre.</summary>
        public void Box(double w, double h, double d, uint hex, PartStyle o = default)
        {
            o = Normalise(o);
            double hx = w / 2, hy = h / 2, hz = d / 2;
            // (normal, four corners counter-clockwise seen from outside)
            var faces = new (double nx, double ny, double nz, double[] c)[]
            {
                (1, 0, 0, new[] { hx, -hy, hz, hx, -hy, -hz, hx, hy, -hz, hx, hy, hz }),
                (-1, 0, 0, new[] { -hx, -hy, -hz, -hx, -hy, hz, -hx, hy, hz, -hx, hy, -hz }),
                (0, 1, 0, new[] { -hx, hy, hz, hx, hy, hz, hx, hy, -hz, -hx, hy, -hz }),
                (0, -1, 0, new[] { -hx, -hy, -hz, hx, -hy, -hz, hx, -hy, hz, -hx, -hy, hz }),
                (0, 0, 1, new[] { -hx, -hy, hz, hx, -hy, hz, hx, hy, hz, -hx, hy, hz }),
                (0, 0, -1, new[] { hx, -hy, -hz, -hx, -hy, -hz, -hx, hy, -hz, hx, hy, -hz }),
            };
            Rgb col = Srgb.HexToLinear(hex);
            foreach (var f in faces)
            {
                int b = VertexCount;
                double[] uvs = { 0, 0, 1, 0, 1, 1, 0, 1 };
                for (int k = 0; k < 4; k++)
                {
                    Vertex(f.c[k * 3], f.c[k * 3 + 1], f.c[k * 3 + 2], f.nx, f.ny, f.nz, uvs[k * 2], uvs[k * 2 + 1], col, o);
                }
                Tri(b, b + 1, b + 2);
                Tri(b, b + 2, b + 3);
            }
        }

        /// <summary>Cylinder (or cone) along Y about its centre, <paramref name="seg"/> sides, closed unless <see cref="PartStyle.Open"/>.</summary>
        public void Cylinder(double radiusTop, double radiusBottom, double height, int seg, uint hex, PartStyle o = default)
        {
            o = Normalise(o);
            if (seg < 3) throw new ArgumentOutOfRangeException(nameof(seg), seg, "a cylinder needs at least 3 sides");
            Rgb col = Srgb.HexToLinear(hex);
            double half = height / 2;
            double slope = (radiusBottom - radiusTop) / height;

            // Side: two rings, each ring point shared by its two triangles; the seam gets its own pair so the UV can wrap.
            int sideBase = VertexCount;
            for (int ring = 0; ring < 2; ring++)
            {
                double y = ring == 0 ? half : -half;
                double r = ring == 0 ? radiusTop : radiusBottom;
                for (int ix = 0; ix <= seg; ix++)
                {
                    double u = (double)ix / seg;
                    double theta = u * 2 * Math.PI;
                    double sn = Math.Sin(theta), cs = Math.Cos(theta);
                    double nl = Math.Sqrt(1 + slope * slope);
                    Vertex(r * sn, y, r * cs, sn / nl, slope / nl, cs / nl, u, ring == 0 ? 1 : 0, col, o);
                }
            }
            int row = seg + 1;
            for (int ix = 0; ix < seg; ix++)
            {
                int a = sideBase + ix, b = sideBase + row + ix, c = sideBase + row + ix + 1, d = sideBase + ix + 1;
                // a top, b bottom, c bottom next, d top next: outward is counter-clockwise a, b, c / a, c, d seen from outside
                Tri(a, b, c);
                Tri(a, c, d);
            }

            if (o.Open) return;
            for (int cap = 0; cap < 2; cap++)
            {
                double r = cap == 0 ? radiusTop : radiusBottom;
                if (r <= 0) continue;
                double y = cap == 0 ? half : -half;
                double ny = cap == 0 ? 1 : -1;
                int centre = VertexCount;
                Vertex(0, y, 0, 0, ny, 0, 0.5, 0.5, col, o);
                for (int ix = 0; ix <= seg; ix++)
                {
                    double theta = (double)ix / seg * 2 * Math.PI;
                    double sn = Math.Sin(theta), cs = Math.Cos(theta);
                    Vertex(r * sn, y, r * cs, 0, ny, 0, sn * 0.5 + 0.5, cs * 0.5 * ny + 0.5, col, o);
                }
                for (int ix = 0; ix < seg; ix++)
                {
                    if (cap == 0) Tri(centre, centre + 1 + ix, centre + 2 + ix);
                    else Tri(centre, centre + 2 + ix, centre + 1 + ix);
                }
            }
        }

        /// <summary>Sphere with <paramref name="seg"/> segments round and half as many (at least two) from pole to pole.</summary>
        public void Sphere(double radius, int seg, uint hex, PartStyle o = default)
        {
            o = Normalise(o);
            if (seg < 3) throw new ArgumentOutOfRangeException(nameof(seg), seg, "a sphere needs at least 3 segments");
            Rgb col = Srgb.HexToLinear(hex);
            int rings = Math.Max(2, seg >> 1);
            int b0 = VertexCount;
            for (int iy = 0; iy <= rings; iy++)
            {
                double v = (double)iy / rings;
                for (int ix = 0; ix <= seg; ix++)
                {
                    double u = (double)ix / seg;
                    double nx = -Math.Cos(u * 2 * Math.PI) * Math.Sin(v * Math.PI);
                    double ny = Math.Cos(v * Math.PI);
                    double nz = Math.Sin(u * 2 * Math.PI) * Math.Sin(v * Math.PI);
                    Vertex(radius * nx, radius * ny, radius * nz, nx, ny, nz, u, 1 - v, col, o);
                }
            }
            int row = seg + 1;
            for (int iy = 0; iy < rings; iy++)
            {
                for (int ix = 0; ix < seg; ix++)
                {
                    int a = b0 + iy * row + ix + 1, b = b0 + iy * row + ix, c = b0 + (iy + 1) * row + ix, d = b0 + (iy + 1) * row + ix + 1;
                    if (iy != 0) Tri(a, b, d);
                    if (iy != rings - 1) Tri(b, c, d);
                }
            }
        }

        /// <summary>
        /// A thin tube along a polyline, <paramref name="radial"/> sides (3 is a triangular prism: thin cables are sub-pixel at any
        /// distance). Used for the cables strung across the street. No end caps.
        /// </summary>
        public void Tube(IReadOnlyList<Float3> path, double radius, int radial, uint hex, PartStyle o = default)
        {
            o = Normalise(o);
            if (path.Count < 2) throw new ArgumentException("a tube needs at least two points", nameof(path));
            if (radial < 3) throw new ArgumentOutOfRangeException(nameof(radial), radial, "a tube needs at least 3 sides");
            Rgb col = Srgb.HexToLinear(hex);
            int b0 = VertexCount;
            for (int k = 0; k < path.Count; k++)
            {
                Float3 p = path[k];
                Float3 prev = path[Math.Max(0, k - 1)], next = path[Math.Min(path.Count - 1, k + 1)];
                double tx = next.X - prev.X, ty = next.Y - prev.Y, tz = next.Z - prev.Z;
                double tl = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                if (tl < 1e-9) throw new ArgumentException("a tube cannot have two identical neighbouring points", nameof(path));
                tx /= tl; ty /= tl; tz /= tl;
                // any vector not parallel to the tangent gives a frame
                double ax = Math.Abs(ty) < 0.9 ? 0 : 1, ay = Math.Abs(ty) < 0.9 ? 1 : 0;
                double ux = ay * tz, uy = -ax * tz, uz = ax * ty - ay * tx;       // a x t
                double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz);
                ux /= ul; uy /= ul; uz /= ul;
                double vx = ty * uz - tz * uy, vy = tz * ux - tx * uz, vz = tx * uy - ty * ux;   // t x u
                for (int j = 0; j <= radial; j++)
                {
                    double theta = (double)j / radial * 2 * Math.PI;
                    double cs = Math.Cos(theta), sn = Math.Sin(theta);
                    double nx = cs * ux + sn * vx, ny = cs * uy + sn * vy, nz = cs * uz + sn * vz;
                    Vertex(p.X + radius * nx, p.Y + radius * ny, p.Z + radius * nz, nx, ny, nz, (double)k / (path.Count - 1), (double)j / radial, col, o);
                }
            }
            int row = radial + 1;
            for (int k = 0; k < path.Count - 1; k++)
            {
                for (int j = 0; j < radial; j++)
                {
                    int a = b0 + k * row + j, b = b0 + (k + 1) * row + j, c = b0 + (k + 1) * row + j + 1, d = b0 + k * row + j + 1;
                    Tri(a, b, d);
                    Tri(b, c, d);
                }
            }
            FixTubeWinding(b0);
        }

        /// <summary>
        /// One quad with explicit attributes: corners a, b, c, d counter-clockwise from the front, normal (nx, ny, nz), UVs for the four corners
        /// (8 numbers), a colour and the surface (emit, rough, metal). For decals, whose UVs point into an atlas.
        /// </summary>
        public void Quad(Float3 a, Float3 b, Float3 c, Float3 d, double nx, double ny, double nz, double[] uv, Rgb colour, double emit, double rough, double metal)
        {
            if (uv == null || uv.Length != 8) throw new ArgumentException("a quad needs 8 uv numbers", nameof(uv));
            int b0 = VertexCount;
            Float3[] corners = { a, b, c, d };
            for (int k = 0; k < 4; k++)
            {
                _p.Add(corners[k].X); _p.Add(corners[k].Y); _p.Add(corners[k].Z);
                _n.Add((float)nx); _n.Add((float)ny); _n.Add((float)nz);
                _t.Add((float)uv[k * 2]); _t.Add((float)uv[k * 2 + 1]);
                _c.Add(colour.R); _c.Add(colour.G); _c.Add(colour.B);
                _s.Add((float)emit); _s.Add((float)rough); _s.Add((float)metal); _s.Add(0f);
            }
            Tri(b0, b0 + 1, b0 + 2);
            Tri(b0, b0 + 2, b0 + 3);
        }

        // ------------------------------------------------------------------ instancing

        /// <summary>
        /// Append <paramref name="geometry"/> placed at (x, y, z), turned by <paramref name="ry"/> about Y (three.js's convention: +Z
        /// swings toward +X) and scaled uniformly by <paramref name="scale"/>.
        /// </summary>
        public void AddInstance(MeshData geometry, double x, double y, double z, double ry, double scale = 1)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            double cs = Math.Cos(ry), sn = Math.Sin(ry);
            int b = VertexCount;
            int n = geometry.VertexCount;
            for (int k = 0; k < n; k++)
            {
                double px = geometry.Positions[k * 3] * scale, py = geometry.Positions[k * 3 + 1] * scale, pz = geometry.Positions[k * 3 + 2] * scale;
                _p.Add((float)(px * cs + pz * sn + x)); _p.Add((float)(py + y)); _p.Add((float)(-px * sn + pz * cs + z));
                double nx = geometry.Normals[k * 3], nz = geometry.Normals[k * 3 + 2];
                _n.Add((float)(nx * cs + nz * sn)); _n.Add(geometry.Normals[k * 3 + 1]); _n.Add((float)(-nx * sn + nz * cs));
                _t.Add(geometry.Uvs[k * 2]); _t.Add(geometry.Uvs[k * 2 + 1]);
                _c.Add(geometry.Colors[k * 3]); _c.Add(geometry.Colors[k * 3 + 1]); _c.Add(geometry.Colors[k * 3 + 2]);
                _s.Add(geometry.Uv1[k * 4]); _s.Add(geometry.Uv1[k * 4 + 1]); _s.Add(geometry.Uv1[k * 4 + 2]); _s.Add(geometry.Uv1[k * 4 + 3]);
            }
            foreach (int index in geometry.Indices) _i.Add(b + index);
        }

        // ------------------------------------------------------------------ internals

        static PartStyle Normalise(PartStyle o) =>
            // default(PartStyle) is all zeros, which would collapse the part; zero scale and zero roughness are never wanted
            new PartStyle(o.X, o.Y, o.Z, o.Rx, o.Ry, o.Rz,
                o.Sx == 0 ? 1 : o.Sx, o.Sy == 0 ? 1 : o.Sy, o.Sz == 0 ? 1 : o.Sz,
                o.Emit, o.Rough == 0 && o.Metal == 0 && o.Uv == 0 ? 0.72 : o.Rough, o.Rough == 0 && o.Metal == 0 && o.Uv == 0 ? 0.2 : o.Metal,
                o.Uv == 0 ? 1.3 : o.Uv, o.Open);

        void Vertex(double x, double y, double z, double nx, double ny, double nz, double u, double v, Rgb col, PartStyle o)
        {
            // scale, then Z, X, Y rotations, then the move: the prototype's part()
            double px = x * o.Sx, py = y * o.Sy, pz = z * o.Sz;
            double qx = nx / o.Sx, qy = ny / o.Sy, qz = nz / o.Sz;
            double ql = Math.Sqrt(qx * qx + qy * qy + qz * qz);
            if (ql > 0) { qx /= ql; qy /= ql; qz /= ql; }
            Rotate(ref px, ref py, ref pz, o);
            Rotate(ref qx, ref qy, ref qz, o);
            _p.Add((float)(px + o.X)); _p.Add((float)(py + o.Y)); _p.Add((float)(pz + o.Z));
            _n.Add((float)qx); _n.Add((float)qy); _n.Add((float)qz);
            _t.Add((float)(u * o.Uv)); _t.Add((float)(v * o.Uv));
            _c.Add(col.R); _c.Add(col.G); _c.Add(col.B);
            _s.Add((float)o.Emit); _s.Add((float)o.Rough); _s.Add((float)o.Metal); _s.Add(0f);
        }

        static void Rotate(ref double x, ref double y, ref double z, PartStyle o)
        {
            double t;
            if (o.Rz != 0)
            {
                double cs = Math.Cos(o.Rz), sn = Math.Sin(o.Rz);
                t = x * cs - y * sn; y = x * sn + y * cs; x = t;
            }
            if (o.Rx != 0)
            {
                double cs = Math.Cos(o.Rx), sn = Math.Sin(o.Rx);
                t = y * cs - z * sn; z = y * sn + z * cs; y = t;
            }
            if (o.Ry != 0)
            {
                double cs = Math.Cos(o.Ry), sn = Math.Sin(o.Ry);
                t = x * cs + z * sn; z = -x * sn + z * cs; x = t;
            }
        }

        void Tri(int a, int b, int c) { _i.Add(a); _i.Add(b); _i.Add(c); }

        /// <summary>Make every triangle of a tube face away from its axis, whichever way the frame happened to turn.</summary>
        void FixTubeWinding(int firstVertex)
        {
            for (int t = _i.Count - 3; t >= 0; t -= 3)
            {
                if (_i[t] < firstVertex) break;
                int a = _i[t], b = _i[t + 1], c = _i[t + 2];
                double ax = _p[a * 3], ay = _p[a * 3 + 1], az = _p[a * 3 + 2];
                double ux = _p[b * 3] - ax, uy = _p[b * 3 + 1] - ay, uz = _p[b * 3 + 2] - az;
                double vx = _p[c * 3] - ax, vy = _p[c * 3 + 1] - ay, vz = _p[c * 3 + 2] - az;
                double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                double dot = cx * _n[a * 3] + cy * _n[a * 3 + 1] + cz * _n[a * 3 + 2];
                if (dot < 0) { _i[t + 1] = c; _i[t + 2] = b; }
            }
        }
    }
}
