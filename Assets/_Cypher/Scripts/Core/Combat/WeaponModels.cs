using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>A surface of a gun: albedo (sRGB hex), metalness, roughness, emissive strength and glow channel (0 always, 1 ammo, 2 charge, 3 heat).</summary>
    public readonly struct GunSurface
    {
        public readonly uint Color;
        public readonly double Metal, Rough, Glow;
        public readonly int Channel;

        public GunSurface(uint color, double metal = 0.9, double rough = 0.4, double glow = 0, int channel = 0)
        {
            Color = color; Metal = metal; Rough = rough; Glow = glow; Channel = channel;
        }
    }

    /// <summary>One moving part of a gun: its geometry baked about its pivot (non-indexed triangles), and which part it hangs from.</summary>
    public sealed class GunPart
    {
        public string Name, Parent;
        public (double X, double Y, double Z) Pivot;
        /// <summary>Per vertex: position (3), normal (3), linear colour (3), metal/rough/emissive/channel (4).</summary>
        public float[] Positions, Normals, Colors, Mre;
        public int VertexCount => Positions.Length / 3;
    }

    /// <summary>A gun's model: name, parts in order, anchors (muzzle, eject, sight).</summary>
    public sealed class GunModel
    {
        public string Id, Name;
        public List<GunPart> Parts = new List<GunPart>();
        public Dictionary<string, (double X, double Y, double Z)> Anchors = new Dictionary<string, (double, double, double)>();

        public GunPart Part(string name)
        {
            foreach (GunPart p in Parts) if (p.Name == name) return p;
            throw new ArgumentOutOfRangeException(nameof(name), name, $"{Id} has no part of that name");
        }
    }

    /// <summary>
    /// The three guns' models, a port of the prototype's <c>player/WeaponModels.js</c> and the three.js primitives it builds from (box, cylinder, cone, torus,
    /// sphere, and a tube along a centripetal Catmull-Rom curve), reproduced exactly, Float32 rounding included, so the meshes match the prototype vertex for
    /// vertex (golden: <c>tools/gen-golden-weapons.mjs</c>). Weapon space: +X right, +Y up, -Z down the bore; origin at the top of the grip.
    /// </summary>
    public static class WeaponModels
    {
        // ------------------------------------------------------------------ surfaces (WeaponModels.js SURF and NEON)

        static readonly GunSurface Frame = new GunSurface(0x272b33, 0.94, 0.47), FrameLt = new GunSurface(0x3b424e, 0.92, 0.36), Steel = new GunSurface(0x848c98, 1.00, 0.22),
            Chrome = new GunSurface(0xc6ced9, 1.00, 0.07), Gunblue = new GunSurface(0x1b2230, 1.00, 0.26), Poly = new GunSurface(0x121418, 0.06, 0.86),
            Rubber = new GunSurface(0x0a0c0f, 0.02, 0.97), Brass = new GunSurface(0xb98c3c, 1.00, 0.31), Copper = new GunSurface(0x9d5a32, 1.00, 0.36),
            Cable = new GunSurface(0x0d0f13, 0.10, 0.74), Carbon = new GunSurface(0x191d24, 0.35, 0.55);

        const uint Cyan = 0x36f2ff, Ice = 0x9df6ff, Magenta = 0xff2f8e, Violet = 0xa060ff, Amber = 0xffa32c, Ember = 0xff5a1e;

        static GunSurface Glow(uint color, double strength = 3.0, int channel = 0) => new GunSurface(color, 0.0, 0.45, strength, channel);

        // ------------------------------------------------------------------ geometry: non-indexed triangles, positions and normals stored as Float32

        sealed class Geo
        {
            public readonly List<float> P = new List<float>(), N = new List<float>();
        }

        /// <summary>An indexed three.js geometry, made non-indexed (its index order) as <c>toNonIndexed</c> does.</summary>
        static Geo Expand(List<double> pos, List<double> nrm, List<int> index)
        {
            var g = new Geo();
            foreach (int i in index)
            {
                g.P.Add((float)pos[i * 3]); g.P.Add((float)pos[i * 3 + 1]); g.P.Add((float)pos[i * 3 + 2]);
                g.N.Add((float)nrm[i * 3]); g.N.Add((float)nrm[i * 3 + 1]); g.N.Add((float)nrm[i * 3 + 2]);
            }
            return g;
        }

        static Geo Box(double w, double h, double d)
        {
            var pos = new List<double>();
            var nrm = new List<double>();
            var idx = new List<int>();
            int n = 0;
            void Plane(int u, int v, int wa, double udir, double vdir, double width, double height, double depth)
            {
                double wh = width / 2, hh = height / 2, dh = depth / 2;
                for (int iy = 0; iy < 2; iy++)
                {
                    double y = iy * height - hh;
                    for (int ix = 0; ix < 2; ix++)
                    {
                        double x = ix * width - wh;
                        var vec = new double[3];
                        vec[u] = x * udir; vec[v] = y * vdir; vec[wa] = dh;
                        pos.AddRange(vec);
                        var nv = new double[3];
                        nv[wa] = depth > 0 ? 1 : -1;
                        nrm.AddRange(nv);
                    }
                }
                int a = n, b = n + 2, c = n + 3, d0 = n + 1;
                idx.AddRange(new[] { a, b, d0, b, c, d0 });
                n += 4;
            }
            const int X = 0, Y = 1, Z = 2;
            Plane(Z, Y, X, -1, -1, d, h, w);
            Plane(Z, Y, X, 1, -1, d, h, -w);
            Plane(X, Z, Y, 1, 1, w, d, h);
            Plane(X, Z, Y, 1, -1, w, d, -h);
            Plane(X, Y, Z, 1, -1, w, h, d);
            Plane(X, Y, Z, -1, -1, w, h, -d);
            return Expand(pos, nrm, idx);
        }

        static Geo Cylinder(double rTop, double rBottom, double height, int radial, bool openEnded)
        {
            var pos = new List<double>();
            var nrm = new List<double>();
            var idx = new List<int>();
            int index = 0;
            double half = height / 2, slope = (rBottom - rTop) / height;
            var rows = new List<int[]>();
            for (int y = 0; y <= 1; y++)
            {
                var row = new int[radial + 1];
                double v = y, radius = v * (rBottom - rTop) + rTop;
                for (int x = 0; x <= radial; x++)
                {
                    double u = (double)x / radial, theta = u * Math.PI * 2, s = Math.Sin(theta), c = Math.Cos(theta);
                    pos.Add(radius * s); pos.Add(-v * height + half); pos.Add(radius * c);
                    double len = Math.Sqrt(s * s + slope * slope + c * c);
                    nrm.Add(s / len); nrm.Add(slope / len); nrm.Add(c / len);
                    row[x] = index++;
                }
                rows.Add(row);
            }
            for (int x = 0; x < radial; x++)
            {
                int a = rows[0][x], b = rows[1][x], c = rows[1][x + 1], d = rows[0][x + 1];
                if (rTop > 0) idx.AddRange(new[] { a, b, d });
                if (rBottom > 0) idx.AddRange(new[] { b, c, d });
            }
            void Cap(bool top)
            {
                int centerStart = index;
                double radius = top ? rTop : rBottom, sign = top ? 1 : -1;
                for (int x = 1; x <= radial; x++)
                {
                    pos.Add(0); pos.Add(half * sign); pos.Add(0);
                    nrm.Add(0); nrm.Add(sign); nrm.Add(0);
                    index++;
                }
                int centerEnd = index;
                for (int x = 0; x <= radial; x++)
                {
                    double theta = (double)x / radial * Math.PI * 2;
                    pos.Add(radius * Math.Sin(theta)); pos.Add(half * sign); pos.Add(radius * Math.Cos(theta));
                    nrm.Add(0); nrm.Add(sign); nrm.Add(0);
                    index++;
                }
                for (int x = 0; x < radial; x++)
                {
                    int c = centerStart + x, i = centerEnd + x;
                    if (top) idx.AddRange(new[] { i, i + 1, c });
                    else idx.AddRange(new[] { i + 1, i, c });
                }
            }
            if (!openEnded)
            {
                if (rTop > 0) Cap(true);
                if (rBottom > 0) Cap(false);
            }
            return Expand(pos, nrm, idx);
        }

        static Geo Torus(double radius, double tube, int radialSegments, int tubularSegments)
        {
            var pos = new List<double>();
            var nrm = new List<double>();
            var idx = new List<int>();
            for (int j = 0; j <= radialSegments; j++)
            {
                double v = (double)j / radialSegments * Math.PI * 2;
                for (int i = 0; i <= tubularSegments; i++)
                {
                    double u = (double)i / tubularSegments * Math.PI * 2;
                    double x = (radius + tube * Math.Cos(v)) * Math.Cos(u), y = (radius + tube * Math.Cos(v)) * Math.Sin(u), z = tube * Math.Sin(v);
                    pos.Add(x); pos.Add(y); pos.Add(z);
                    double nx = x - radius * Math.Cos(u), ny = y - radius * Math.Sin(u), nz = z, len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    nrm.Add(nx / len); nrm.Add(ny / len); nrm.Add(nz / len);
                }
            }
            for (int j = 1; j <= radialSegments; j++)
            {
                for (int i = 1; i <= tubularSegments; i++)
                {
                    int a = (tubularSegments + 1) * j + i - 1, b = (tubularSegments + 1) * (j - 1) + i - 1, c = (tubularSegments + 1) * (j - 1) + i, d = (tubularSegments + 1) * j + i;
                    idx.AddRange(new[] { a, b, d, b, c, d });
                }
            }
            return Expand(pos, nrm, idx);
        }

        static Geo Sphere(double radius, int widthSegments, int heightSegments)
        {
            var pos = new List<double>();
            var nrm = new List<double>();
            var idx = new List<int>();
            var grid = new List<int[]>();
            int index = 0;
            for (int iy = 0; iy <= heightSegments; iy++)
            {
                var row = new int[widthSegments + 1];
                double v = (double)iy / heightSegments, theta = v * Math.PI, y = radius * Math.Cos(theta), ring = Math.Sqrt(radius * radius - y * y);
                for (int ix = 0; ix <= widthSegments; ix++)
                {
                    double phi = (double)ix / widthSegments * Math.PI * 2;
                    double x = -ring * Math.Cos(phi), z = ring * Math.Sin(phi), len = Math.Sqrt(x * x + y * y + z * z);
                    pos.Add(x); pos.Add(y); pos.Add(z);
                    if (len > 0) { nrm.Add(x / len); nrm.Add(y / len); nrm.Add(z / len); }
                    else { nrm.Add(0); nrm.Add(0); nrm.Add(0); }
                    row[ix] = index++;
                }
                grid.Add(row);
            }
            for (int iy = 0; iy < heightSegments; iy++)
            {
                for (int ix = 0; ix < widthSegments; ix++)
                {
                    int a = grid[iy][ix + 1], b = grid[iy][ix], c = grid[iy + 1][ix], d = grid[iy + 1][ix + 1];
                    if (iy != 0) idx.AddRange(new[] { a, b, d });
                    if (iy != heightSegments - 1) idx.AddRange(new[] { b, c, d });
                }
            }
            return Expand(pos, nrm, idx);
        }

        // --- the curve the cables follow: three's CatmullRomCurve3, centripetal, read at equal arc length; TubeGeometry with Frenet frames

        sealed class Curve
        {
            readonly double[][] _p;
            double[] _lengths;
            const int Divisions = 200;

            public Curve(double[][] points)
            {
                _p = points;
            }

            static void Init(double x0, double x1, double t0, double t1, out double c0, out double c1, out double c2, out double c3)
            {
                c0 = x0; c1 = t0; c2 = -3 * x0 + 3 * x1 - 2 * t0 - t1; c3 = 2 * x0 - 2 * x1 + t0 + t1;
            }

            static double Calc(double x0, double x1, double x2, double x3, double dt0, double dt1, double dt2, double t)
            {
                double t1 = (x1 - x0) / dt0 - (x2 - x0) / (dt0 + dt1) + (x2 - x1) / dt1;
                double t2 = (x2 - x1) / dt1 - (x3 - x1) / (dt1 + dt2) + (x3 - x2) / dt2;
                t1 *= dt1;
                t2 *= dt1;
                Init(x1, x2, t1, t2, out double c0, out double c1, out double c2, out double c3);
                double tt = t * t;
                return c0 + c1 * t + c2 * tt + c3 * tt * t;
            }

            static double D2(double[] a, double[] b) => (a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]);

            public double[] Point(double t)
            {
                int l = _p.Length;
                double p = (l - 1) * t;
                int i = (int)Math.Floor(p);
                double weight = p - i;
                if (weight == 0 && i == l - 1)
                {
                    i = l - 2;
                    weight = 1;
                }
                double[] p0 = i > 0 ? _p[(i - 1) % l] : new[] { _p[0][0] - _p[1][0] + _p[0][0], _p[0][1] - _p[1][1] + _p[0][1], _p[0][2] - _p[1][2] + _p[0][2] };
                double[] p1 = _p[i % l], p2 = _p[(i + 1) % l];
                double[] p3 = i + 2 < l ? _p[(i + 2) % l] : new[] { _p[l - 1][0] - _p[l - 2][0] + _p[l - 1][0], _p[l - 1][1] - _p[l - 2][1] + _p[l - 1][1], _p[l - 1][2] - _p[l - 2][2] + _p[l - 1][2] };
                double dt0 = Math.Pow(D2(p0, p1), 0.25), dt1 = Math.Pow(D2(p1, p2), 0.25), dt2 = Math.Pow(D2(p2, p3), 0.25);
                if (dt1 < 1e-4) dt1 = 1.0;
                if (dt0 < 1e-4) dt0 = dt1;
                if (dt2 < 1e-4) dt2 = dt1;
                return new[]
                {
                    Calc(p0[0], p1[0], p2[0], p3[0], dt0, dt1, dt2, weight), Calc(p0[1], p1[1], p2[1], p3[1], dt0, dt1, dt2, weight),
                    Calc(p0[2], p1[2], p2[2], p3[2], dt0, dt1, dt2, weight),
                };
            }

            double[] Lengths()
            {
                if (_lengths != null) return _lengths;
                var cache = new double[Divisions + 1];
                double[] last = Point(0);
                double sum = 0;
                for (int p = 1; p <= Divisions; p++)
                {
                    double[] cur = Point((double)p / Divisions);
                    sum += Math.Sqrt(D2(cur, last));
                    cache[p] = sum;
                    last = cur;
                }
                return _lengths = cache;
            }

            public double UtoT(double u)
            {
                double[] arc = Lengths();
                int il = arc.Length, low = 0, high = il - 1, i;
                double target = u * arc[il - 1];
                while (low <= high)
                {
                    i = (int)Math.Floor(low + (high - low) / 2.0);
                    double cmp = arc[i] - target;
                    if (cmp < 0) low = i + 1;
                    else if (cmp > 0) high = i - 1;
                    else
                    {
                        high = i;
                        break;
                    }
                }
                i = high;
                if (arc[i] == target) return (double)i / (il - 1);
                double before = arc[i], after = arc[i + 1];
                return (i + (target - before) / (after - before)) / (il - 1);
            }

            public double[] PointAt(double u) => Point(UtoT(u));

            public double[] TangentAt(double u)
            {
                double t = UtoT(u), t1 = Math.Max(0, t - 0.0001), t2 = Math.Min(1, t + 0.0001);
                double[] a = Point(t1), b = Point(t2);
                return Normalize(new[] { b[0] - a[0], b[1] - a[1], b[2] - a[2] });
            }
        }

        static double[] Normalize(double[] v)
        {
            double len = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            return len > 0 ? new[] { v[0] / len, v[1] / len, v[2] / len } : new[] { 0.0, 0, 0 };
        }

        static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        /// <summary>A vector turned by <paramref name="theta"/> about a unit axis (three's <c>makeRotationAxis</c> then <c>applyMatrix4</c>).</summary>
        static double[] RotateAxis(double[] v, double[] axis, double theta)
        {
            double c = Math.Cos(theta), s = Math.Sin(theta), t = 1 - c, x = axis[0], y = axis[1], z = axis[2], tx = t * x, ty = t * y;
            double m0 = tx * x + c, m4 = tx * y - s * z, m8 = tx * z + s * y;
            double m1 = tx * y + s * z, m5 = ty * y + c, m9 = ty * z - s * x;
            double m2 = tx * z - s * y, m6 = ty * z + s * x, m10 = t * z * z + c;
            return new[] { m0 * v[0] + m4 * v[1] + m8 * v[2], m1 * v[0] + m5 * v[1] + m9 * v[2], m2 * v[0] + m6 * v[1] + m10 * v[2] };
        }

        /// <summary>JavaScript's <c>Number.EPSILON</c>.</summary>
        const double JsEpsilon = 2.220446049250313e-16;

        static Geo Tube(double[][] points, double radius, int tubular)
        {
            const int radial = 5;
            var curve = new Curve(points);
            var tangents = new double[tubular + 1][];
            for (int i = 0; i <= tubular; i++) tangents[i] = curve.TangentAt((double)i / tubular);
            var normals = new double[tubular + 1][];
            var binormals = new double[tubular + 1][];
            double min = double.MaxValue;
            double[] seed = new double[3];
            double tx = Math.Abs(tangents[0][0]), ty = Math.Abs(tangents[0][1]), tz = Math.Abs(tangents[0][2]);
            if (tx <= min) { min = tx; seed = new[] { 1.0, 0, 0 }; }
            if (ty <= min) { min = ty; seed = new[] { 0.0, 1, 0 }; }
            if (tz <= min) seed = new[] { 0.0, 0, 1 };
            double[] vec = Normalize(Cross(tangents[0], seed));
            normals[0] = Cross(tangents[0], vec);
            binormals[0] = Cross(tangents[0], normals[0]);
            for (int i = 1; i <= tubular; i++)
            {
                normals[i] = (double[])normals[i - 1].Clone();
                vec = Cross(tangents[i - 1], tangents[i]);
                double len = Math.Sqrt(vec[0] * vec[0] + vec[1] * vec[1] + vec[2] * vec[2]);
                if (len > JsEpsilon)
                {
                    vec = Normalize(vec);
                    double dot = tangents[i - 1][0] * tangents[i][0] + tangents[i - 1][1] * tangents[i][1] + tangents[i - 1][2] * tangents[i][2];
                    normals[i] = RotateAxis(normals[i], vec, Math.Acos(Math.Max(-1, Math.Min(1, dot))));
                }
                binormals[i] = Cross(tangents[i], normals[i]);
            }

            var pos = new List<double>();
            var nrm = new List<double>();
            for (int i = 0; i <= tubular; i++)
            {
                double[] p = curve.PointAt((double)i / tubular), n = normals[i], b = binormals[i];
                for (int j = 0; j <= radial; j++)
                {
                    double v = (double)j / radial * Math.PI * 2, sin = Math.Sin(v), cos = -Math.Cos(v);
                    double[] nn = Normalize(new[] { cos * n[0] + sin * b[0], cos * n[1] + sin * b[1], cos * n[2] + sin * b[2] });
                    nrm.AddRange(nn);
                    pos.Add(p[0] + radius * nn[0]); pos.Add(p[1] + radius * nn[1]); pos.Add(p[2] + radius * nn[2]);
                }
            }
            var idx = new List<int>();
            for (int j = 1; j <= tubular; j++)
            {
                for (int i = 1; i <= radial; i++)
                {
                    int a = (radial + 1) * (j - 1) + (i - 1), bb = (radial + 1) * j + (i - 1), c = (radial + 1) * j + i, d = (radial + 1) * (j - 1) + i;
                    idx.AddRange(new[] { a, bb, d, bb, c, d });
                }
            }
            return Expand(pos, nrm, idx);
        }

        // the prototype's helpers
        static Geo BoxG(double w, double h, double d) => Box(w, h, d);
        /// <summary>A cylinder down the bore (Z).</summary>
        static Geo TubeZ(double r1, double r2, double len, int seg = 12) => Transformed(Cylinder(r1, r2, len, seg, false), Math.PI / 2, 0, 0);
        /// <summary>A cylinder along Y.</summary>
        static Geo Post(double r1, double r2, double len, int seg = 10) => Cylinder(r1, r2, len, seg, false);
        /// <summary>A torus in the XY plane.</summary>
        static Geo Ring(double r, double t, int seg = 18, int tseg = 6) => Torus(r, t, tseg, seg);
        static Geo Ball(double r, int seg = 10) => Sphere(r, seg, Math.Max(4, seg >> 1));
        static Geo CableG(double[][] points, double r = 0.004, int seg = 14) => Tube(points, r, seg);

        /// <summary>A geometry rotated about X (as <c>geometry.rotateX</c>: position and normal, stored as Float32 again).</summary>
        static Geo Transformed(Geo g, double rx, double ry, double rz) => Apply(g, Matrix(0, 0, 0, rx, ry, rz));

        /// <summary>three's <c>Matrix4.compose</c> from a position and an XYZ Euler (no scale is used by the guns): rows of the 3x3 and the translation.</summary>
        static double[] Matrix(double x, double y, double z, double rx, double ry, double rz)
        {
            // Euler XYZ to a quaternion, then to a matrix, as three does
            double c1 = Math.Cos(rx / 2), c2 = Math.Cos(ry / 2), c3 = Math.Cos(rz / 2), s1 = Math.Sin(rx / 2), s2 = Math.Sin(ry / 2), s3 = Math.Sin(rz / 2);
            double qx = s1 * c2 * c3 + c1 * s2 * s3, qy = c1 * s2 * c3 - s1 * c2 * s3, qz = c1 * c2 * s3 + s1 * s2 * c3, qw = c1 * c2 * c3 - s1 * s2 * s3;
            double x2 = qx + qx, y2 = qy + qy, z2 = qz + qz, xx = qx * x2, xy = qx * y2, xz = qx * z2, yy = qy * y2, yz = qy * z2, zz = qz * z2, wx = qw * x2, wy = qw * y2, wz = qw * z2;
            return new[]
            {
                1 - (yy + zz), xy - wz, xz + wy, x,
                xy + wz, 1 - (xx + zz), yz - wx, y,
                xz - wy, yz + wx, 1 - (xx + yy), z,
            };
        }

        static Geo Apply(Geo g, double[] m)
        {
            var o = new Geo();
            for (int i = 0; i < g.P.Count; i += 3)
            {
                double px = g.P[i], py = g.P[i + 1], pz = g.P[i + 2];
                o.P.Add((float)(m[0] * px + m[1] * py + m[2] * pz + m[3]));
                o.P.Add((float)(m[4] * px + m[5] * py + m[6] * pz + m[7]));
                o.P.Add((float)(m[8] * px + m[9] * py + m[10] * pz + m[11]));
                // a pure rotation: the normal matrix is the rotation itself; three normalises after
                double nx = g.N[i], ny = g.N[i + 1], nz = g.N[i + 2];
                double ax = m[0] * nx + m[1] * ny + m[2] * nz, ay = m[4] * nx + m[5] * ny + m[6] * nz, az = m[8] * nx + m[9] * ny + m[10] * nz;
                double len = Math.Sqrt(ax * ax + ay * ay + az * az);
                if (len > 0) { ax /= len; ay /= len; az /= len; }
                o.N.Add((float)ax); o.N.Add((float)ay); o.N.Add((float)az);
            }
            return o;
        }

        // ------------------------------------------------------------------ PartBuilder

        sealed class Builder
        {
            readonly List<float> _p = new List<float>(), _n = new List<float>(), _c = new List<float>(), _m = new List<float>();

            public bool Empty => _p.Count == 0;

            public Builder Add(Geo geo, GunSurface s, double x = 0, double y = 0, double z = 0, double rx = 0, double ry = 0, double rz = 0)
            {
                Geo g = Apply(geo, Matrix(x, y, z, rx, ry, rz));
                float r = (float)Srgb(((s.Color >> 16) & 0xff) / 255.0), gg = (float)Srgb(((s.Color >> 8) & 0xff) / 255.0), b = (float)Srgb((s.Color & 0xff) / 255.0);
                _p.AddRange(g.P);
                _n.AddRange(g.N);
                for (int i = 0; i < g.P.Count / 3; i++)
                {
                    _c.Add(r); _c.Add(gg); _c.Add(b);
                    _m.Add((float)s.Metal); _m.Add((float)s.Rough); _m.Add((float)s.Glow); _m.Add(s.Channel);
                }
                return this;
            }

            /// <summary>A primitive repeated round the Z axis.</summary>
            public Builder Radial(int count, double radius, Func<int, Geo> factory, GunSurface s, double x = 0, double y = 0, double z = 0, double phase = 0)
            {
                for (int i = 0; i < count; i++)
                {
                    double a = (double)i / count * Math.PI * 2 + phase;
                    Add(factory(i), s, x + Math.Cos(a) * radius, y + Math.Sin(a) * radius, z, 0, 0, a);
                }
                return this;
            }

            public GunPart Build(string name, double px, double py, double pz, string parent = null)
            {
                var part = new GunPart { Name = name, Parent = parent, Pivot = (px, py, pz) };
                part.Positions = new float[_p.Count];
                for (int i = 0; i < _p.Count; i += 3)
                {
                    part.Positions[i] = (float)(_p[i] - px);
                    part.Positions[i + 1] = (float)(_p[i + 1] - py);
                    part.Positions[i + 2] = (float)(_p[i + 2] - pz);
                }
                part.Normals = _n.ToArray();
                part.Colors = _c.ToArray();
                part.Mre = _m.ToArray();
                return part;
            }
        }

        /// <summary>three's sRGB to linear transfer.</summary>
        public static double Srgb(double c) => c < 0.04045 ? c * 0.0773993808 : Math.Pow(c * 0.9478672986 + 0.0521327014, 2.4);

        // ------------------------------------------------------------------ sub-assemblies

        static void Picatinny(Builder b, double x, double y, double z0, double z1, double w)
        {
            double len = Math.Abs(z1 - z0);
            b.Add(BoxG(w, 0.008, len), FrameLt, x, y, (z0 + z1) / 2);
            int slots = Math.Max(3, (int)Math.Round(len / 0.024, MidpointRounding.AwayFromZero));
            for (int i = 0; i < slots; i++)
            {
                double t = (i + 0.5) / slots;
                b.Add(BoxG(w * 0.92, 0.009, 0.011), Steel, x, y + 0.008, z0 + (z1 - z0) * t);
            }
        }

        static void GripRidges(Builder b, double x, double y, double z, double rx, int count, double w, double r, double span)
        {
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / (count - 1) - 0.5;
                b.Add(Post(r, r, w, 6), Rubber, x, y + t * span * Math.Cos(rx), z - t * span * Math.Sin(rx), rx, 0, Math.PI / 2);
            }
        }

        /// <summary>The chromed cyber-gauntlet, blocky and dark so it reads as an augmented hand.</summary>
        static void Gauntlet(Builder b, double x, double y, double z, uint accent, double roll = 0, double pitch = 0, double yaw = 0, int mirror = 1)
        {
            double Px(double v) => x + v * mirror;
            b.Add(Post(0.036, 0.045, 0.20, 10), Carbon, Px(0.012), y - 0.16, z + 0.10, -0.62, 0, roll);
            b.Add(Post(0.038, 0.038, 0.026, 10), Chrome, Px(0.008), y - 0.085, z + 0.048, -0.62);
            b.Add(BoxG(0.006, 0.11, 0.004), Glow(accent, 1.4, 0), Px(-0.02), y - 0.13, z + 0.088, -0.62);
            b.Add(Ball(0.032, 10), FrameLt, Px(0.004), y - 0.055, z + 0.030);
            b.Add(BoxG(0.030, 0.070, 0.052), Carbon, Px(0.020), y - 0.038, z + 0.006, pitch + 0.18, yaw, roll);
            b.Add(BoxG(0.022, 0.050, 0.010), Chrome, Px(0.034), y - 0.034, z + 0.006, pitch + 0.18);
            for (int i = 0; i < 4; i++)
            {
                double fy = y - 0.012 - i * 0.023, curl = 0.10 + i * 0.03;
                b.Add(BoxG(0.030, 0.018, 0.020), Carbon, Px(0.008), fy, z - 0.020, curl);
                b.Add(BoxG(0.026, 0.016, 0.018), FrameLt, Px(0.006), fy - 0.004, z - 0.038, curl + 0.5);
                if (i < 3) b.Add(BoxG(0.028, 0.004, 0.004), Glow(accent, 1.0, 0), Px(0.008), fy + 0.009, z - 0.026, curl);
            }
            b.Add(BoxG(0.016, 0.020, 0.044), Carbon, Px(0.026), y - 0.014, z - 0.010, 0.30, 0, roll - mirror * 0.35);
            b.Add(BoxG(0.014, 0.016, 0.028), FrameLt, Px(0.026), y + 0.002, z - 0.040, 0.75);
        }

        static double[][] Pts(params double[] xyz)
        {
            var o = new double[xyz.Length / 3][];
            for (int i = 0; i < o.Length; i++) o[i] = new[] { xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2] };
            return o;
        }

        // ------------------------------------------------------------------ the guns

        static GunModel Wasp()
        {
            Builder body = new Builder(), bolt = new Builder(), mag = new Builder(), hand = new Builder(), support = new Builder();
            uint acc = Cyan;

            body.Add(BoxG(0.060, 0.080, 0.255), Frame, 0, 0.006, -0.100);
            body.Add(BoxG(0.064, 0.020, 0.24), Gunblue, 0, 0.040, -0.100);
            body.Add(BoxG(0.050, 0.018, 0.235), FrameLt, 0, 0.052, -0.100);
            Picatinny(body, 0, 0.062, -0.005, -0.215, 0.046);
            for (int i = 0; i < 3; i++) body.Add(BoxG(0.066, 0.030, 0.034), Poly, 0, 0.004, -0.055 - i * 0.055);
            body.Add(BoxG(0.004, 0.008, 0.145), Glow(acc, 1.7, 0), -0.031, 0.024, -0.105);
            body.Add(BoxG(0.004, 0.008, 0.145), Glow(acc, 1.7, 0), 0.031, 0.024, -0.105);
            body.Add(BoxG(0.003, 0.024, 0.044), Glow(Ice, 2.2, 1), -0.032, -0.010, -0.045);
            body.Add(BoxG(0.005, 0.032, 0.052), Poly, -0.030, -0.010, -0.045);
            body.Add(BoxG(0.007, 0.030, 0.070), Poly, 0.030, 0.030, -0.062);

            body.Add(TubeZ(0.024, 0.024, 0.150, 14), Frame, 0, 0.018, -0.300);
            for (int i = 0; i < 6; i++) body.Add(Ring(0.0255, 0.0035, 14, 5), FrameLt, 0, 0.018, -0.238 - i * 0.024);
            for (int i = 0; i < 4; i++) body.Add(BoxG(0.056, 0.008, 0.016), Poly, 0, 0.018, -0.250 - i * 0.024, 0, 0, 0.4);
            body.Add(TubeZ(0.0105, 0.0105, 0.26, 10), Steel, 0, 0.018, -0.290);
            body.Add(BoxG(0.048, 0.014, 0.135), Frame, 0, -0.004, -0.300);
            body.Add(TubeZ(0.020, 0.017, 0.052, 12), FrameLt, 0, 0.018, -0.398);
            for (int i = 0; i < 3; i++) body.Add(BoxG(0.046, 0.006, 0.010), Poly, 0, 0.018, -0.386 - i * 0.014);
            body.Add(Ring(0.0165, 0.0028, 14, 5), Glow(Ember, 5.0, 3), 0, 0.018, -0.4245);

            body.Add(BoxG(0.026, 0.030, 0.010), FrameLt, 0, 0.076, -0.030);
            body.Add(Ring(0.0092, 0.0026, 14, 5), Steel, 0, 0.078, -0.026);
            body.Add(Ring(0.0062, 0.0013, 12, 4), Glow(acc, 2.0, 0), 0, 0.078, -0.0245);
            body.Add(BoxG(0.008, 0.034, 0.008), FrameLt, 0, 0.062, -0.330);
            body.Add(BoxG(0.0035, 0.0035, 0.0035), Glow(Ice, 3.4, 0), 0, 0.078, -0.330);

            body.Add(BoxG(0.052, 0.046, 0.120), Poly, 0, -0.028, -0.272);
            for (int i = 0; i < 5; i++) body.Add(BoxG(0.056, 0.006, 0.008), Carbon, 0, -0.028, -0.222 - i * 0.024);
            body.Add(BoxG(0.030, 0.062, 0.030), Rubber, 0, -0.076, -0.258, 0.20);
            GripRidges(support, 0, -0.076, -0.258, 0.20, 4, 0.032, 0.0035, 0.046);

            body.Add(BoxG(0.038, 0.118, 0.052), Poly, 0, -0.086, 0.016, 0.22);
            body.Add(BoxG(0.042, 0.026, 0.056), Frame, 0, -0.026, 0.006);
            GripRidges(body, 0, -0.090, 0.031, 0.22, 6, 0.040, 0.0042, 0.088);
            body.Add(Ring(0.026, 0.0048, 14, 5), Frame, 0, -0.040, -0.030, 0, Math.PI / 2);
            body.Add(BoxG(0.009, 0.028, 0.007), Steel, 0, -0.034, -0.032, -0.18);
            body.Add(BoxG(0.044, 0.030, 0.062), Frame, 0, -0.032, -0.074);

            body.Add(TubeZ(0.008, 0.008, 0.135, 8), Frame, 0.026, 0.028, 0.086);
            body.Add(TubeZ(0.008, 0.008, 0.135, 8), Frame, -0.026, 0.028, 0.086);
            body.Add(BoxG(0.056, 0.078, 0.020), Poly, 0, 0.018, 0.150);
            body.Add(BoxG(0.058, 0.016, 0.026), Rubber, 0, -0.018, 0.150);
            body.Add(BoxG(0.034, 0.018, 0.080), Poly, 0, 0.056, 0.082);
            body.Add(BoxG(0.010, 0.006, 0.070), Glow(acc, 1.2, 0), 0.018, 0.062, 0.082);
            body.Add(CableG(Pts(0.031, -0.008, 0.020, 0.048, 0.008, 0.052, 0.040, 0.030, 0.100, 0.014, 0.030, 0.146), 0.0045), Cable);
            body.Add(CableG(Pts(-0.031, -0.004, -0.010, -0.050, 0.014, 0.030, -0.038, 0.034, 0.090, -0.012, 0.028, 0.146), 0.0038), Cable);

            bolt.Add(BoxG(0.014, 0.014, 0.088), Steel, -0.034, 0.046, -0.060);
            bolt.Add(BoxG(0.030, 0.014, 0.026), FrameLt, -0.046, 0.046, -0.024);
            bolt.Add(BoxG(0.006, 0.006, 0.020), Glow(acc, 1.5, 0), -0.058, 0.046, -0.024);

            mag.Add(BoxG(0.032, 0.150, 0.060), Frame, 0, -0.120, -0.070, 0.05);
            mag.Add(BoxG(0.034, 0.008, 0.062), FrameLt, 0, -0.050, -0.071);
            mag.Add(BoxG(0.038, 0.014, 0.070), Poly, 0, -0.196, -0.066);
            mag.Add(BoxG(0.003, 0.104, 0.026), Glow(acc, 2.1, 1), -0.0165, -0.120, -0.070, 0.05);
            mag.Add(BoxG(0.003, 0.104, 0.026), Glow(acc, 2.1, 1), 0.0165, -0.120, -0.070, 0.05);
            for (int i = 0; i < 4; i++) mag.Add(BoxG(0.034, 0.005, 0.062), Carbon, 0, -0.076 - i * 0.030, -0.070, 0.05);

            Gauntlet(hand, 0.004, -0.030, 0.020, acc);
            Gauntlet(support, -0.004, -0.020, -0.252, acc, pitch: -0.25, mirror: -1);

            var m = new GunModel { Id = "wasp", Name = "WASP-9" };
            m.Parts.Add(body.Build("body", 0, 0, 0));
            m.Parts.Add(bolt.Build("bolt", 0, 0.046, -0.060));
            m.Parts.Add(mag.Build("mag", 0, -0.046, -0.070));
            m.Parts.Add(hand.Build("hand", 0, -0.030, 0.020));
            m.Parts.Add(support.Build("support", 0, -0.060, -0.255));
            m.Anchors["muzzle"] = (0, 0.018, -0.432);
            m.Anchors["eject"] = (0.042, 0.036, -0.062);
            m.Anchors["sight"] = (0, 0.078, -0.030);
            return m;
        }

        static GunModel Magnat()
        {
            Builder body = new Builder(), yoke = new Builder(), cyl = new Builder(), hammer = new Builder(), hand = new Builder();
            uint acc = Amber;
            const double cy = -0.070;

            body.Add(BoxG(0.036, 0.086, 0.150), Gunblue, 0, 0.012, -0.020);
            body.Add(BoxG(0.040, 0.040, 0.056), Gunblue, 0, 0.030, 0.036);
            body.Add(BoxG(0.052, 0.062, 0.040), Gunblue, 0, 0.012, -0.128);
            body.Add(BoxG(0.030, 0.020, 0.100), FrameLt, 0, 0.050, -0.040);
            body.Add(BoxG(0.003, 0.006, 0.100), Glow(acc, 1.5, 0), -0.019, 0.030, -0.020);
            body.Add(BoxG(0.003, 0.006, 0.100), Glow(acc, 1.5, 0), 0.019, 0.030, -0.020);

            body.Add(TubeZ(0.027, 0.027, 0.215, 8), Gunblue, 0, 0.020, -0.250, 0, 0, Math.PI / 8);
            body.Add(BoxG(0.026, 0.016, 0.215), Gunblue, 0, 0.046, -0.250);
            for (int i = 0; i < 5; i++) body.Add(BoxG(0.028, 0.010, 0.012), Poly, 0, 0.049, -0.190 - i * 0.030);
            body.Add(BoxG(0.036, 0.030, 0.170), Gunblue, 0, -0.010, -0.250);
            body.Add(TubeZ(0.0085, 0.0085, 0.160, 10), Steel, 0, -0.014, -0.250);
            body.Add(TubeZ(0.0125, 0.0125, 0.020, 10), FrameLt, 0, -0.014, -0.336);
            body.Add(TubeZ(0.0135, 0.0135, 0.230, 12), Poly, 0, 0.020, -0.250);
            body.Add(Ring(0.0245, 0.004, 16, 6), Chrome, 0, 0.020, -0.357);
            body.Add(Ring(0.0175, 0.0026, 14, 5), Glow(Ember, 5.0, 3), 0, 0.020, -0.358);

            body.Add(BoxG(0.026, 0.024, 0.012), FrameLt, 0, 0.066, 0.018);
            body.Add(BoxG(0.006, 0.012, 0.010), Poly, 0, 0.074, 0.014);
            body.Add(BoxG(0.0035, 0.010, 0.004), Glow(acc, 2.2, 0), -0.008, 0.074, 0.013);
            body.Add(BoxG(0.0035, 0.010, 0.004), Glow(acc, 2.2, 0), 0.008, 0.074, 0.013);
            body.Add(BoxG(0.007, 0.026, 0.008), FrameLt, 0, 0.066, -0.348);
            body.Add(BoxG(0.004, 0.005, 0.005), Glow(Ice, 3.4, 0), 0, 0.076, -0.348);

            body.Add(BoxG(0.044, 0.132, 0.062), Rubber, 0, -0.096, 0.044, 0.30);
            body.Add(BoxG(0.048, 0.030, 0.056), Frame, 0, -0.034, 0.026);
            body.Add(BoxG(0.014, 0.130, 0.014), Chrome, 0, -0.096, 0.074, 0.30);
            body.Add(BoxG(0.046, 0.020, 0.058), FrameLt, 0, -0.160, 0.062);
            for (int i = 0; i < 3; i++) body.Add(Post(0.010, 0.010, 0.046, 8), Rubber, 0, -0.062 - i * 0.036, 0.012 - i * 0.011, 0.30, 0, Math.PI / 2);
            body.Add(BoxG(0.020, 0.016, 0.018), Glow(acc, 1.6, 1), -0.023, -0.052, 0.048, 0, 0.2);

            body.Add(Ring(0.028, 0.0055, 16, 6), Gunblue, 0, -0.044, -0.030, 0, Math.PI / 2);
            body.Add(BoxG(0.010, 0.030, 0.008), Chrome, 0, -0.038, -0.032, -0.20);

            hammer.Add(BoxG(0.012, 0.030, 0.016), Chrome, 0, 0.062, 0.052);
            hammer.Add(BoxG(0.018, 0.014, 0.014), FrameLt, 0, 0.074, 0.058);
            hammer.Add(BoxG(0.010, 0.008, 0.006), Glow(acc, 1.4, 0), 0, 0.078, 0.058);

            yoke.Add(BoxG(0.012, 0.028, 0.070), FrameLt, -0.026, 0.014, cy);
            yoke.Add(Post(0.007, 0.007, 0.080, 8), Steel, -0.030, 0.014, cy);

            cyl.Add(TubeZ(0.038, 0.038, 0.078, 16), Gunblue, 0, 0.014, cy);
            cyl.Add(Ring(0.0385, 0.0035, 18, 6), FrameLt, 0, 0.014, cy - 0.036);
            cyl.Add(Ring(0.0385, 0.0035, 18, 6), FrameLt, 0, 0.014, cy + 0.036);
            cyl.Radial(6, 0.034, _ => BoxG(0.016, 0.010, 0.056), Poly, 0, 0.014, cy);
            cyl.Radial(6, 0.022, _ => TubeZ(0.0095, 0.0095, 0.082, 8), Poly, 0, 0.014, cy, 0.5);
            cyl.Radial(6, 0.022, _ => TubeZ(0.0105, 0.0105, 0.006, 8), Brass, 0, 0.014, cy + 0.040, 0.5);
            cyl.Radial(6, 0.022, _ => Ring(0.0068, 0.0016, 10, 4), Glow(acc, 2.4, 1), 0, 0.014, cy + 0.0435, 0.5);
            cyl.Add(Post(0.006, 0.006, 0.086, 8), Steel, 0, 0.014, cy, Math.PI / 2);
            cyl.Add(Ring(0.0392, 0.0022, 18, 5), Glow(Ember, 3.4, 3), 0, 0.014, cy - 0.040);

            Gauntlet(hand, 0.004, -0.040, 0.048, acc, pitch: 0.10);

            var m = new GunModel { Id = "magnat", Name = "MAGNAT .500" };
            m.Parts.Add(body.Build("body", 0, 0, 0));
            m.Parts.Add(hammer.Build("hammer", 0, 0.052, 0.058));
            m.Parts.Add(yoke.Build("yoke", -0.030, 0.014, cy));
            m.Parts.Add(cyl.Build("cyl", 0, 0.014, cy, parent: "yoke"));
            m.Parts.Add(hand.Build("hand", 0, -0.040, 0.048));
            m.Anchors["muzzle"] = (0, 0.020, -0.366);
            m.Anchors["eject"] = (0, 0.014, -0.030);
            m.Anchors["sight"] = (0, 0.074, 0.014);
            return m;
        }

        static GunModel Voltline()
        {
            Builder body = new Builder(), cell = new Builder(), coils = new Builder(), hand = new Builder(), support = new Builder();
            uint acc = Violet, hot = Magenta;

            body.Add(BoxG(0.070, 0.100, 0.300), Frame, 0, 0.014, -0.100);
            body.Add(BoxG(0.086, 0.028, 0.280), Carbon, 0, 0.056, -0.105);
            body.Add(BoxG(0.078, 0.016, 0.150), FrameLt, 0, -0.038, -0.150);
            body.Add(BoxG(0.062, 0.086, 0.030), Poly, 0, 0.030, 0.070);
            body.Add(BoxG(0.070, 0.020, 0.026), Rubber, 0, -0.012, 0.074);
            body.Add(BoxG(0.050, 0.060, 0.060), Frame, 0, 0.030, 0.036, -0.12);
            for (int i = 0; i < 7; i++) body.Add(BoxG(0.076, 0.026, 0.008), FrameLt, 0, 0.080, -0.020 - i * 0.024);
            body.Add(BoxG(0.024, 0.010, 0.170), Glow(acc, 1.5, 0), 0, 0.094, -0.100);
            for (int i = 0; i < 4; i++) body.Add(BoxG(0.076, 0.020, 0.012), Poly, 0, -0.006, -0.040 - i * 0.030, 0, 0, 0.35);
            body.Add(BoxG(0.003, 0.010, 0.180), Glow(acc, 1.6, 0), 0.036, 0.030, -0.100);
            body.Add(BoxG(0.003, 0.010, 0.180), Glow(acc, 1.6, 0), -0.036, 0.030, -0.100);

            body.Add(BoxG(0.014, 0.020, 0.300), Chrome, 0.026, 0.024, -0.330);
            body.Add(BoxG(0.014, 0.020, 0.300), Chrome, -0.026, 0.024, -0.330);
            body.Add(BoxG(0.008, 0.006, 0.290), Glow(hot, 2.0, 2), 0.026, 0.036, -0.330);
            body.Add(BoxG(0.008, 0.006, 0.290), Glow(hot, 2.0, 2), -0.026, 0.036, -0.330);
            body.Add(BoxG(0.070, 0.010, 0.290), Carbon, 0, 0.010, -0.330);
            body.Add(TubeZ(0.046, 0.038, 0.052, 10), FrameLt, 0, 0.024, -0.482);
            body.Add(Ring(0.034, 0.006, 14, 6), Chrome, 0, 0.024, -0.500);
            body.Add(Ring(0.026, 0.005, 14, 5), Glow(hot, 2.4, 2), 0, 0.024, -0.504);
            for (int i = 0; i < 3; i++)
            {
                double a = i / 3.0 * Math.PI * 2 + 0.5;
                body.Add(BoxG(0.012, 0.030, 0.050), FrameLt, Math.Cos(a) * 0.040, 0.024 + Math.Sin(a) * 0.040, -0.512, 0, 0, a);
            }
            body.Add(Ring(0.020, 0.004, 12, 5), Glow(Ice, 4.0, 3), 0, 0.024, -0.524);

            for (int i = 0; i < 4; i++)
            {
                double z = -0.230 - i * 0.070;
                coils.Add(Ring(0.038, 0.008, 16, 6), Copper, 0, 0.024, z);
                coils.Add(Ring(0.030, 0.0035, 14, 5), Glow(hot, 1.8, 2), 0, 0.024, z);
                coils.Radial(4, 0.038, _ => BoxG(0.010, 0.010, 0.014), FrameLt, 0, 0.024, z, 0.4);
            }

            body.Add(BoxG(0.044, 0.036, 0.070), Poly, 0, 0.104, -0.070);
            body.Add(Ring(0.016, 0.004, 14, 5), Chrome, 0, 0.104, -0.106);
            body.Add(TubeZ(0.014, 0.014, 0.004, 12), Glow(Ice, 2.0, 0), 0, 0.104, -0.104);
            body.Add(BoxG(0.006, 0.006, 0.004), Glow(hot, 3.2, 0), 0, 0.104, -0.108);
            body.Add(BoxG(0.030, 0.010, 0.010), FrameLt, 0, 0.120, -0.040);

            body.Add(BoxG(0.042, 0.126, 0.056), Poly, 0, -0.086, 0.030, 0.26);
            GripRidges(body, 0, -0.090, 0.044, 0.26, 6, 0.044, 0.0045, 0.092);
            body.Add(BoxG(0.048, 0.030, 0.060), Frame, 0, -0.026, 0.018);
            body.Add(Ring(0.028, 0.005, 14, 5), Frame, 0, -0.042, -0.024, 0, Math.PI / 2);
            body.Add(BoxG(0.010, 0.030, 0.008), Chrome, 0, -0.036, -0.026, -0.18);
            body.Add(BoxG(0.034, 0.070, 0.038), Rubber, 0, -0.078, -0.196, -0.14);
            body.Add(BoxG(0.014, 0.048, 0.008), Glow(acc, 1.4, 2), -0.019, -0.078, -0.196, -0.14);

            body.Add(CableG(Pts(-0.040, -0.030, 0.010, -0.062, -0.010, -0.040, -0.058, 0.014, -0.100, -0.036, 0.020, -0.150), 0.0055), Cable);
            body.Add(CableG(Pts(0.040, -0.028, 0.000, 0.064, 0.000, -0.050, 0.056, 0.030, -0.120, 0.034, 0.030, -0.170), 0.0048), Cable);

            cell.Add(BoxG(0.052, 0.084, 0.100), Frame, -0.046, -0.030, -0.030);
            cell.Add(BoxG(0.020, 0.070, 0.086), Glow(hot, 2.4, 1), -0.070, -0.030, -0.030);
            cell.Add(BoxG(0.056, 0.014, 0.104), FrameLt, -0.046, 0.014, -0.030);
            cell.Add(BoxG(0.056, 0.014, 0.104), FrameLt, -0.046, -0.074, -0.030);
            for (int i = 0; i < 3; i++) cell.Add(BoxG(0.058, 0.006, 0.010), Chrome, -0.046, -0.052 + i * 0.030, -0.030);
            cell.Add(Post(0.008, 0.008, 0.030, 8), Copper, -0.046, 0.026, -0.062);

            Gauntlet(hand, 0.004, -0.030, 0.034, acc);
            Gauntlet(support, -0.004, -0.020, -0.190, acc, pitch: -0.30, mirror: -1);

            var m = new GunModel { Id = "voltline", Name = "VOLTLINE RP" };
            m.Parts.Add(body.Build("body", 0, 0, 0));
            m.Parts.Add(coils.Build("coils", 0, 0.024, -0.335));
            m.Parts.Add(cell.Build("cell", -0.046, 0.020, -0.030));
            m.Parts.Add(hand.Build("hand", 0, -0.030, 0.034));
            m.Parts.Add(support.Build("support", 0, -0.062, -0.192));
            m.Anchors["muzzle"] = (0, 0.024, -0.536);
            m.Anchors["eject"] = (-0.070, 0.030, -0.030);
            m.Anchors["sight"] = (0, 0.104, -0.106);
            return m;
        }

        /// <summary>A gun's model by id.</summary>
        public static GunModel Build(string id)
        {
            switch (id)
            {
                case "wasp": return Wasp();
                case "magnat": return Magnat();
                case "voltline": return Voltline();
                default: throw new ArgumentOutOfRangeException(nameof(id), id, "no such gun");
            }
        }
    }
}
