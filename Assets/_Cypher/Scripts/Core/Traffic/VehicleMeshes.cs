using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>Which of the ground glow's three kinds of geometry a vertex belongs to, stored in <c>Uv1.x</c>; the shader sizes, places and tints each differently.</summary>
    public enum GlowPart
    {
        /// <summary>Headlamps, tail lamps, indicator strips and the roof markers, in the unit box of the vehicle (x and z within -0.5 to 0.5, y within 0 and 1).</summary>
        Lamps = 0,

        /// <summary>The pulsing pool of light on the road under the vehicle, in a unit square about its middle.</summary>
        Underglow = 1,

        /// <summary>The additive headlight cones, apex at the nose, opening along +Z.</summary>
        Beams = 2,
    }

    /// <summary>
    /// The vehicles' geometry, a port of the builders in Traffic.js: hulls extruded from side profiles, wheels, the rider, wings, the glowing lamp rigs and cones, the contact shadow,
    /// and the signals. Plain vertex and triangle arrays like <see cref="PedestrianRig"/>; the normals are zero because the vehicle shader derives faceted normals from screen-space
    /// derivatives, as the prototype's flat shading did. The glowing meshes carry colour already multiplied by the prototype's vertex alpha (they are added to the picture, so
    /// colour times alpha is all there is to it), and a few carry a part id in <c>Uv1.x</c>.
    /// </summary>
    public static class VehicleMeshes
    {
        // ---------------------------------------------------------------- hulls

        /// <summary>
        /// A closed side profile extruded across X. The half-width tapers toward the roof and the extreme nose and tail, which is what turns a slab into a vehicle.
        /// The profile runs counter-clockwise in the (z, y) plane; the triangles are the prototype's, in its order.
        /// </summary>
        public static MeshData Hull(HullShape shape)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            ProfilePoint[] profile = shape.Profile;
            int n = profile.Length;
            if (n < 3) throw new ArgumentException("a hull needs at least three profile points", nameof(shape));

            double maxY = 0, maxZ = 0;
            foreach (ProfilePoint p in profile)
            {
                if (p.Y > maxY) maxY = p.Y;
                if (Math.Abs(p.Z) > maxZ) maxZ = Math.Abs(p.Z);
            }
            var halfWidth = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = Math.Max(0, (profile[i].Y / maxY - shape.RoofY) / (1 - shape.RoofY));
                double zt = Math.Abs(profile[i].Z) / maxZ;
                halfWidth[i] = shape.HalfWidth * (1 - shape.RoofTaper * t * t) * (1 - shape.NoseTaper * zt * zt * zt);
            }

            var positions = new float[n * 6];
            for (int side = 0; side < 2; side++)
            {
                double sign = side == 0 ? -1 : 1;
                for (int i = 0; i < n; i++)
                {
                    int o = (side * n + i) * 3;
                    positions[o] = (float)(sign * halfWidth[i]);
                    positions[o + 1] = (float)profile[i].Y;
                    positions[o + 2] = (float)profile[i].Z;
                }
            }

            var idx = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                idx.AddRange(new[] { i, n + j, j, i, n + i, n + j });
            }
            for (int i = 1; i < n - 1; i++)
            {
                idx.AddRange(new[] { 0, i, i + 1 });
                idx.AddRange(new[] { n, n + i + 1, n + i });
            }
            return Plain(positions, idx.ToArray());
        }

        /// <summary>A ground vehicle's body: its hull, and for the motorbike the rider sitting on it.</summary>
        public static MeshData Body(GroundVehicleType type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            MeshData hull = Hull(type.Hull);
            return type.Rider ? Merge(hull, Rider()) : hull;
        }

        /// <summary>An aircraft's body: its hull and the wing across it.</summary>
        public static MeshData Body(AircraftType type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return Merge(Hull(type.Hull), Move(Box(type.WingSpan, type.WingThickness, type.WingChord), 0, 0, type.WingZ));
        }

        /// <summary>The motorbike's rider, crouched forward over the tank.</summary>
        public static MeshData Rider()
        {
            MeshData torso = Move(RotX(Scale(Cylinder(0.15, 0.19, 0.52, 6), 1, 1, 0.7), 0.34), 0, 1.16, -0.12);
            MeshData helmet = Move(Icosahedron(0.135), 0, 1.48, 0.02);
            MeshData legL = Move(RotX(Cylinder(0.062, 0.055, 0.42, 5), 0.9), -0.17, 0.72, 0.12);
            MeshData legR = Move(legL, 0.34, 0, 0);
            MeshData armL = Move(RotX(Cylinder(0.05, 0.045, 0.46, 5), 1.15), -0.19, 1.24, 0.20);
            MeshData armR = Move(armL, 0.38, 0, 0);
            return Merge(torso, helmet, legL, legR, armL, armR);
        }

        /// <summary>One wheel: a unit cylinder (radius 1, width 1) whose axis is X. The vehicle's wheel is this scaled to its width and radius.</summary>
        public static MeshData Wheel() => RotZ(Cylinder(1, 1, 1, 9), Math.PI / 2);

        /// <summary>The soft shadow under a vehicle: a unit disc lying on the ground, facing up, as a fan of twelve triangles.</summary>
        public static MeshData Shadow()
        {
            const int segments = 12;
            var positions = new List<float> { 0, 0, 0 };
            for (int s = 0; s <= segments; s++)
            {
                double a = (double)s / segments * 2 * Math.PI;
                positions.Add((float)Math.Cos(a)); positions.Add(0f); positions.Add((float)-Math.Sin(a));
            }
            var idx = new List<int>();
            for (int i = 1; i <= segments; i++) { idx.Add(i); idx.Add(i + 1); idx.Add(0); }
            return Plain(positions.ToArray(), idx.ToArray());
        }

        // ---------------------------------------------------------------- glowing rigs

        /// <summary>The ground vehicle's lamps, under-glow and headlight cones as one additive mesh; <c>Uv1.x</c> is the <see cref="GlowPart"/>.</summary>
        public static MeshData GroundGlow() => Merge(Stamp(Lamps(), GlowPart.Lamps), Stamp(Underglow(), GlowPart.Underglow), Stamp(Beams(), GlowPart.Beams));

        /// <summary>The aircraft's thrusters, navigation lamps, belly strip and exhaust streaks, in a unit box like <see cref="GlowPart.Lamps"/>.</summary>
        public static MeshData AircraftGlow()
        {
            var q = new GlowBuilder();
            double[] thrust = { 0.45, 0.85, 1.0 };
            double[] hot = { 1.0, 0.62, 0.28 };
            q.Quad(-0.26, 0.34, -0.5, 0.20, 0.13, 'z', thrust, 4.0);
            q.Quad(0.26, 0.34, -0.5, 0.20, 0.13, 'z', thrust, 4.0);
            q.Quad(-0.26, 0.34, -0.505, 0.42, 0.34, 'z', hot, 0.7);
            q.Quad(0.26, 0.34, -0.505, 0.42, 0.34, 'z', hot, 0.7);
            // port and starboard navigation lamps
            q.Quad(-0.5, 0.42, 0.05, 0.10, 0.07, 'x', new[] { 1.0, 0.08, 0.08 }, 3.2);
            q.Quad(0.5, 0.42, 0.05, 0.10, 0.07, 'x', new[] { 0.10, 1.0, 0.25 }, 3.2);
            // belly strip and forward landing lamps
            q.Quad(0, 0.02, 0, 0.55, 0.85, 'y', new[] { 0.6, 0.75, 1.0 }, 0.5);
            q.Quad(0, 0.30, 0.5, 0.34, 0.05, 'z', new[] { 0.8, 0.9, 1.1 }, 2.6);
            // exhaust streaks
            q.Taper(-0.26, 0.34, -0.52, 0.16, 0.34, 3.2, thrust, 0.75, 0);
            q.Taper(0.26, 0.34, -0.52, 0.16, 0.34, 3.2, thrust, 0.75, 0);
            return q.Build();
        }

        /// <summary>The ground vehicle's lamps, in the unit box of the vehicle: x and z within -0.5 to 0.5, y within 0 and 1.</summary>
        public static MeshData Lamps()
        {
            var q = new GlowBuilder();
            double[] head = { 0.66, 0.80, 1.05 };
            double[] tail = { 1.0, 0.07, 0.05 };
            double[] amber = { 1.0, 0.52, 0.10 };
            // headlamps and a dimmer wide halo round each
            q.Quad(-0.33, 0.34, 0.5, 0.15, 0.075, 'z', head, 4.4);
            q.Quad(0.33, 0.34, 0.5, 0.15, 0.075, 'z', head, 4.4);
            q.Quad(-0.33, 0.34, 0.502, 0.30, 0.20, 'z', head, 0.55);
            q.Quad(0.33, 0.34, 0.502, 0.30, 0.20, 'z', head, 0.55);
            // tail lamps and a full-width strip
            q.Quad(-0.34, 0.40, -0.5, 0.14, 0.065, 'z', tail, 3.2);
            q.Quad(0.34, 0.40, -0.5, 0.14, 0.065, 'z', tail, 3.2);
            q.Quad(0, 0.46, -0.5, 0.74, 0.020, 'z', tail, 1.7);
            q.Quad(0, 0.40, -0.502, 0.86, 0.24, 'z', tail, 0.28);
            // indicator strips down both flanks
            q.Quad(-0.5, 0.30, 0.02, 0.60, 0.022, 'x', amber, 1.5);
            q.Quad(0.5, 0.30, 0.02, 0.60, 0.022, 'x', amber, 1.5);
            // roof marker cluster
            q.Quad(0, 0.995, 0.06, 0.30, 0.05, 'y', amber, 1.1);
            return q.Build();
        }

        /// <summary>Two nested pools of light on the road, lying flat about the origin.</summary>
        public static MeshData Underglow()
        {
            var q = new GlowBuilder();
            q.Quad(0, 0, 0, 1.5, 1.25, 'y', new[] { 1.0, 1.0, 1.0 }, 0.55);
            q.Quad(0, 0, 0, 1.05, 0.9, 'y', new[] { 1.0, 1.0, 1.0 }, 0.7);
            return q.Build();
        }

        /// <summary>Four additive headlight cones, apex at the origin, opening along +Z and fading to nothing at the far end.</summary>
        public static MeshData Beams()
        {
            const int segments = 9;
            // x offset, y offset, length, radius at the apex, radius at the far end, colour, alpha at the apex
            var beams = new (double ox, double oy, double len, double r0, double r1, double[] colour, double alpha)[]
            {
                (-0.62, 0.0, 15.5, 0.16, 2.35, new[] { 0.62, 0.76, 1.0 }, 0.30),
                (0.62, 0.0, 15.5, 0.16, 2.35, new[] { 0.62, 0.76, 1.0 }, 0.30),
                (-0.62, 0.0, 8.5, 0.09, 0.85, new[] { 0.85, 0.92, 1.0 }, 0.34),
                (0.62, 0.0, 8.5, 0.09, 0.85, new[] { 0.85, 0.92, 1.0 }, 0.34),
            };
            var positions = new List<float>();
            var colours = new List<float>();
            var idx = new List<int>();
            foreach (var b in beams)
            {
                int baseIndex = positions.Count / 3;
                for (int ring = 0; ring < 2; ring++)
                {
                    double z = ring == 0 ? 0 : b.len;
                    double r = ring == 0 ? b.r0 : b.r1;
                    double alpha = ring == 0 ? b.alpha : 0;
                    for (int i = 0; i < segments; i++)
                    {
                        double th = (double)i / segments * Math.PI * 2;
                        positions.Add((float)(b.ox + Math.Cos(th) * r));
                        positions.Add((float)(b.oy + Math.Sin(th) * r * 0.55 - z * 0.022));
                        positions.Add((float)z);
                        colours.Add((float)(b.colour[0] * alpha)); colours.Add((float)(b.colour[1] * alpha)); colours.Add((float)(b.colour[2] * alpha));
                    }
                }
                for (int i = 0; i < segments; i++)
                {
                    int j = (i + 1) % segments;
                    idx.AddRange(new[] { baseIndex + i, baseIndex + segments + i, baseIndex + segments + j, baseIndex + i, baseIndex + segments + j, baseIndex + j });
                }
            }
            return Coloured(positions.ToArray(), colours.ToArray(), idx.ToArray());
        }

        // ---------------------------------------------------------------- signals

        /// <summary>One signal head: a mast with a housing of three lamps, at a corner of a junction, facing the traffic that approaches along its axis.</summary>
        public readonly struct SignalHead
        {
            /// <summary>Where the mast stands, and which way the housing faces (yaw about Y).</summary>
            public readonly double X, Z, Yaw;

            /// <summary>0 if it governs traffic travelling along X, 1 for Z.</summary>
            public readonly int Axis;

            public SignalHead(double x, double z, double yaw, int axis)
            {
                X = x; Z = z; Yaw = yaw; Axis = axis;
            }
        }

        /// <summary>The height of the housing's middle above the pavement, and the lamps' spacing and depth.</summary>
        public const double SignalHousingHeight = 5.32, SignalLampSpacing = 0.28, SignalLampInset = 0.16;

        /// <summary>Four heads at every junction, one at the far right corner of each approach.</summary>
        public static IReadOnlyList<SignalHead> SignalHeads(CityNav nav)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            double off = nav.AsphaltHalf + 0.95;
            var heads = new List<SignalHead>();
            int[] dxs = { 1, -1, 0, 0 }, dzs = { 0, 0, 1, -1 };
            foreach (double cx in nav.XLines)
            {
                foreach (double cz in nav.ZLines)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        int dx = dxs[k], dz = dzs[k];
                        // the far-right corner relative to the approaching lane
                        int rx = -dz, rz = dx;
                        double px = cx + dx * off + rx * off, pz = cz + dz * off + rz * off;
                        heads.Add(new SignalHead(px, pz, Math.Atan2(-dx, -dz), dx != 0 ? 0 : 1));
                    }
                }
            }
            return heads;
        }

        /// <summary>All the signal masts and housings as one static mesh, standing on the pavement.</summary>
        public static MeshData SignalMasts(CityNav nav)
        {
            IReadOnlyList<SignalHead> heads = SignalHeads(nav);
            var parts = new List<MeshData>(heads.Count * 2);
            foreach (SignalHead h in heads)
            {
                parts.Add(Move(Cylinder(0.075, 0.10, 5.1, 6), h.X, nav.WalkY + 2.55, h.Z));
                parts.Add(Move(RotY(Box(0.36, 0.94, 0.26), h.Yaw), h.X, nav.WalkY + SignalHousingHeight, h.Z));
            }
            return Merge(parts.ToArray());
        }

        /// <summary>One lamp lens: a small flat box, centred on its own origin.</summary>
        public static MeshData SignalLens() => Box(0.20, 0.20, 0.06);

        // ---------------------------------------------------------------- the geometry operations three.js has

        static MeshData Box(double w, double h, double d)
        {
            var b = new PropMeshBuilder();
            b.Box(w, h, d, 0xffffff);
            MeshData g = b.Build();
            return Plain(g.Positions, g.Indices);
        }

        static MeshData Cylinder(double rTop, double rBottom, double height, int seg)
        {
            var b = new PropMeshBuilder();
            b.Cylinder(rTop, rBottom, height, seg, 0xffffff);
            MeshData g = b.Build();
            return Plain(g.Positions, g.Indices);
        }

        /// <summary>A 20-sided polyhedron (three.js's IcosahedronGeometry with no detail).</summary>
        static MeshData Icosahedron(double radius)
        {
            double t = (1 + Math.Sqrt(5)) / 2;
            double[] v =
            {
                -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, 0,
                0, -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t,
                t, 0, -1, t, 0, 1, -t, 0, -1, -t, 0, 1,
            };
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var p = new float[v.Length];
            for (int k = 0; k < v.Length; k += 3)
            {
                double len = Math.Sqrt(v[k] * v[k] + v[k + 1] * v[k + 1] + v[k + 2] * v[k + 2]);
                p[k] = (float)(v[k] / len * radius); p[k + 1] = (float)(v[k + 1] / len * radius); p[k + 2] = (float)(v[k + 2] / len * radius);
            }
            return Plain(p, faces);
        }

        static MeshData Move(MeshData g, double x, double y, double z) => Plain(Map(g.Positions, (px, py, pz) => (px + x, py + y, pz + z)), g.Indices, g);

        static MeshData Scale(MeshData g, double sx, double sy, double sz) => Plain(Map(g.Positions, (px, py, pz) => (px * sx, py * sy, pz * sz)), g.Indices, g);

        /// <summary>Turn about the X axis through the origin (three.js's rotateX): +Y swings toward +Z.</summary>
        static MeshData RotX(MeshData g, double a)
        {
            double cs = Math.Cos(a), sn = Math.Sin(a);
            return Plain(Map(g.Positions, (px, py, pz) => (px, py * cs - pz * sn, py * sn + pz * cs)), g.Indices, g);
        }

        /// <summary>Turn about the Y axis (three.js's rotateY): +Z swings toward +X.</summary>
        static MeshData RotY(MeshData g, double a)
        {
            double cs = Math.Cos(a), sn = Math.Sin(a);
            return Plain(Map(g.Positions, (px, py, pz) => (px * cs + pz * sn, py, -px * sn + pz * cs)), g.Indices, g);
        }

        /// <summary>Turn about the Z axis (three.js's rotateZ): +X swings toward +Y.</summary>
        static MeshData RotZ(MeshData g, double a)
        {
            double cs = Math.Cos(a), sn = Math.Sin(a);
            return Plain(Map(g.Positions, (px, py, pz) => (px * cs - py * sn, px * sn + py * cs, pz)), g.Indices, g);
        }

        static float[] Map(float[] positions, Func<double, double, double, (double, double, double)> f)
        {
            var p = new float[positions.Length];
            for (int k = 0; k < p.Length; k += 3)
            {
                (double x, double y, double z) = f(positions[k], positions[k + 1], positions[k + 2]);
                p[k] = (float)x; p[k + 1] = (float)y; p[k + 2] = (float)z;
            }
            return p;
        }

        /// <summary>The parts as one mesh, each keeping its colours and part ids.</summary>
        static MeshData Merge(params MeshData[] parts)
        {
            var positions = new List<float>();
            var colours = new List<float>();
            var uv1 = new List<float>();
            var idx = new List<int>();
            bool stamped = false;
            foreach (MeshData g in parts)
            {
                if (g.Uv1.Length != 0) stamped = true;
            }
            foreach (MeshData g in parts)
            {
                int offset = positions.Count / 3;
                positions.AddRange(g.Positions);
                colours.AddRange(g.Colors);
                if (stamped) uv1.AddRange(g.Uv1.Length != 0 ? g.Uv1 : new float[g.VertexCount * 4]);
                foreach (int i in g.Indices) idx.Add(offset + i);
            }
            int n = positions.Count / 3;
            return new MeshData
            {
                Positions = positions.ToArray(),
                Normals = new float[n * 3],
                Uvs = new float[n * 2],
                Colors = colours.ToArray(),
                Uv1 = stamped ? uv1.ToArray() : Array.Empty<float>(),
                Indices = idx.ToArray(),
            };
        }

        static MeshData Stamp(MeshData g, GlowPart part)
        {
            int n = g.VertexCount;
            var uv1 = new float[n * 4];
            for (int k = 0; k < n; k++) uv1[k * 4] = (float)part;
            return new MeshData { Positions = g.Positions, Normals = g.Normals, Uvs = g.Uvs, Colors = g.Colors, Indices = g.Indices, Uv1 = uv1 };
        }

        static MeshData Plain(float[] positions, int[] indices, MeshData from = null)
        {
            int n = positions.Length / 3;
            return new MeshData
            {
                Positions = positions,
                Normals = new float[n * 3],
                Uvs = new float[n * 2],
                Colors = from != null && from.Colors.Length == n * 3 ? from.Colors : new float[n * 3],
                Uv1 = from != null && from.Uv1.Length == n * 4 ? from.Uv1 : Array.Empty<float>(),
                Indices = indices,
            };
        }

        static MeshData Coloured(float[] positions, float[] colours, int[] indices)
        {
            int n = positions.Length / 3;
            return new MeshData { Positions = positions, Normals = new float[n * 3], Uvs = new float[n * 2], Colors = colours, Indices = indices };
        }

        /// <summary>Quads and tapering streaks of one colour times an alpha, for the glowing rigs (the prototype's quadSet).</summary>
        sealed class GlowBuilder
        {
            readonly List<float> _positions = new List<float>();
            readonly List<float> _colours = new List<float>();
            readonly List<int> _indices = new List<int>();

            /// <summary>A flat quad centred on (cx, cy, cz): axis 'z' faces +/-Z, 'y' lies flat, 'x' faces +/-X.</summary>
            public void Quad(double cx, double cy, double cz, double w, double h, char axis, double[] colour, double alpha)
            {
                int baseIndex = _positions.Count / 3;
                double hw = w / 2, hh = h / 2;
                double[][] corners = axis == 'z'
                    ? new[] { new[] { -hw, -hh, 0 }, new[] { hw, -hh, 0 }, new[] { hw, hh, 0 }, new[] { -hw, hh, 0 } }
                    : axis == 'y'
                        ? new[] { new[] { -hw, 0, -hh }, new[] { hw, 0, -hh }, new[] { hw, 0, hh }, new[] { -hw, 0, hh } }
                        : new[] { new[] { 0, -hh, -hw }, new[] { 0, -hh, hw }, new[] { 0, hh, hw }, new[] { 0, hh, -hw } };
                foreach (double[] d in corners)
                {
                    _positions.Add((float)(cx + d[0])); _positions.Add((float)(cy + d[1])); _positions.Add((float)(cz + d[2]));
                    AddColour(colour, alpha);
                }
                _indices.AddRange(new[] { baseIndex, baseIndex + 1, baseIndex + 2, baseIndex, baseIndex + 2, baseIndex + 3 });
            }

            /// <summary>A streak running back along -Z from (cx, cy, cz), narrowing or widening, fading from alpha a0 to a1.</summary>
            public void Taper(double cx, double cy, double cz, double w0, double w1, double len, double[] colour, double a0, double a1)
            {
                int baseIndex = _positions.Count / 3;
                double[][] at =
                {
                    new[] { cx - w0 / 2, cy, cz, a0 }, new[] { cx + w0 / 2, cy, cz, a0 },
                    new[] { cx + w1 / 2, cy, cz - len, a1 }, new[] { cx - w1 / 2, cy, cz - len, a1 },
                };
                foreach (double[] p in at)
                {
                    _positions.Add((float)p[0]); _positions.Add((float)p[1]); _positions.Add((float)p[2]);
                    AddColour(colour, p[3]);
                }
                _indices.AddRange(new[] { baseIndex, baseIndex + 1, baseIndex + 2, baseIndex, baseIndex + 2, baseIndex + 3 });
            }

            void AddColour(double[] colour, double alpha)
            {
                _colours.Add((float)(colour[0] * alpha)); _colours.Add((float)(colour[1] * alpha)); _colours.Add((float)(colour[2] * alpha));
            }

            public MeshData Build() => Coloured(_positions.ToArray(), _colours.ToArray(), _indices.ToArray());
        }
    }
}
