using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>A static box collider in a kit's own space: half extents, and the centre's offset from the kit origin (which sits at the base).</summary>
    public readonly struct PropCollider
    {
        public readonly double Hx, Hy, Hz, Cx, Cy, Cz;

        public PropCollider(double hx, double hy, double hz, double cy, double cx = 0, double cz = 0)
        {
            Hx = hx; Hy = hy; Hz = hz; Cx = cx; Cy = cy; Cz = cz;
        }
    }

    /// <summary>
    /// One kind of prop: its merged geometry (origin at the base, front face toward +Z; wall-mounted kits have their origin on the wall
    /// plane and grow toward +Z), the footprint the placement cursor uses, and an optional collider.
    /// </summary>
    public sealed class PropKit
    {
        public string Name;
        public MeshData Geometry;

        /// <summary>Footprint along and into the wall, in metres.</summary>
        public double Len, Dep;

        public bool CastsShadow;
        public PropCollider? Collider;
    }

    /// <summary>
    /// The prop library, ported from buildKits() in the prototype's Props.js: street furniture, urban decay, cyberpunk fixtures and
    /// rooftop gear. Each is a merge of boxes, cylinders and spheres. Left out on purpose: the pooled-light sockets (the city lights
    /// its streets with glow pools) and the four kickable "dynamic" props (cans, bottles, crates, bins).
    /// </summary>
    public static class PropKits
    {
        const double Pi = Math.PI;

        static readonly Dictionary<string, PropKit> Kits = Build();

        public static IReadOnlyDictionary<string, PropKit> All => Kits;

        public static PropKit Get(string name)
        {
            if (!Kits.TryGetValue(name, out PropKit kit)) throw new ArgumentException($"There is no prop kit called '{name}'.", nameof(name));
            return kit;
        }

        // ---------------------------------------------------------------- the library

        static Dictionary<string, PropKit> Build()
        {
            var kits = new Dictionary<string, PropKit>();

            void Def(string name, Action<PropMeshBuilder> parts, double len = 1, double dep = 0.8, bool cast = false, PropCollider? col = null)
            {
                var m = new PropMeshBuilder();
                parts(m);
                kits.Add(name, new PropKit { Name = name, Geometry = m.Build(), Len = len, Dep = dep, CastsShadow = cast, Collider = col });
            }

            // ---- street furniture ----

            Def("bollard", m =>
            {
                C(m, 0.10, 0.13, 1.02, 6, 0x565d68, y: 0.51, rough: 0.42, metal: 0.7);
                C(m, 0.135, 0.135, 0.05, 6, 0x9ae4ff, y: 0.86, emit: 2.6, rough: 0.4, metal: 0.0);
                C(m, 0.11, 0.11, 0.04, 6, 0x2c3138, y: 0.99);
            }, len: 0.4, dep: 0.35);

            Def("barrier", m =>
            {
                B(m, 1.72, 0.18, 0.54, 0xb8571f, y: 0.09);
                B(m, 1.5, 0.6, 0.36, 0xc4611f, y: 0.47);
                C(m, 0.065, 0.065, 1.62, 6, 0x474d57, y: 0.84, rz: Pi / 2, rough: 0.4, metal: 0.7);
                B(m, 0.16, 0.42, 0.38, 0xe6e9ee, x: -0.44, y: 0.47, emit: 0.55);
                B(m, 0.16, 0.42, 0.38, 0xe6e9ee, x: 0.44, y: 0.47, emit: 0.55);
                C(m, 0.05, 0.05, 0.09, 6, 0xff8a2a, x: 0.7, y: 0.9, rx: Pi / 2, emit: 4.0);
            }, len: 1.8, dep: 0.6, cast: true, col: new PropCollider(0.86, 0.42, 0.28, cy: 0.42));

            Def("guardrail", m =>
            {
                C(m, 0.055, 0.06, 1.0, 6, 0x545a64, x: -1.1, y: 0.5, rough: 0.45, metal: 0.75);
                C(m, 0.055, 0.06, 1.0, 6, 0x545a64, x: 1.1, y: 0.5, rough: 0.45, metal: 0.75);
                B(m, 2.4, 0.17, 0.05, 0x646b76, y: 0.88, rough: 0.35, metal: 0.8);
                B(m, 2.4, 0.1, 0.04, 0x545a64, y: 0.56, rough: 0.4, metal: 0.8);
            }, len: 2.4, dep: 0.25);

            Def("streetlight", m =>
            {
                C(m, 0.17, 0.24, 0.32, 8, 0x3d434c, y: 0.16);
                C(m, 0.085, 0.13, 7.1, 6, 0x4d545e, y: 3.7, rough: 0.4, metal: 0.75);
                B(m, 0.11, 0.11, 1.55, 0x4d545e, y: 7.2, z: 0.72, rough: 0.4, metal: 0.75);
                B(m, 0.4, 0.17, 0.92, 0x3a4048, y: 7.06, z: 1.38);
                B(m, 0.33, 0.055, 0.82, 0xffd9a0, y: 6.95, z: 1.38, emit: 8.0, rough: 0.3, metal: 0);
                B(m, 0.24, 0.3, 0.2, 0x33383f, y: 5.4, z: 0.16);
                C(m, 0.035, 0.035, 0.06, 5, 0xff3b30, y: 5.6, z: 0.16, rx: Pi / 2, emit: 4.0);
            }, len: 0.7, dep: 0.6, cast: true);

            Def("signal", m =>
            {
                C(m, 0.12, 0.16, 3.9, 6, 0x454b55, y: 1.95, rough: 0.42, metal: 0.7);
                B(m, 0.1, 0.1, 1.15, 0x454b55, y: 3.84, z: 0.52);
                B(m, 0.36, 1.02, 0.28, 0x22262d, y: 3.5, z: 1.05);
                B(m, 0.42, 0.1, 0.34, 0x1a1d22, y: 4.03, z: 1.05);
                C(m, 0.105, 0.105, 0.06, 6, 0xff3b30, y: 3.81, z: 1.2, rx: Pi / 2, emit: 1.0);
                C(m, 0.105, 0.105, 0.06, 6, 0xffb020, y: 3.5, z: 1.2, rx: Pi / 2, emit: 0.8);
                C(m, 0.105, 0.105, 0.06, 6, 0x39ff9e, y: 3.19, z: 1.2, rx: Pi / 2, emit: 6.5);
                B(m, 0.5, 0.22, 0.03, 0x2af0ff, y: 2.5, z: 0.1, emit: 2.2);
            }, len: 0.9, dep: 0.7, cast: true);

            Def("meter", m =>
            {
                C(m, 0.05, 0.07, 1.12, 6, 0x545a64, y: 0.56, rough: 0.45, metal: 0.7);
                B(m, 0.24, 0.36, 0.17, 0x363c44, y: 1.3);
                B(m, 0.26, 0.06, 0.19, 0x24282f, y: 1.49);
                B(m, 0.15, 0.1, 0.02, 0x59f0ff, y: 1.35, z: 0.092, emit: 3.4);
            }, len: 0.35, dep: 0.3);

            Def("shelter", m =>
            {
                B(m, 0.1, 2.5, 0.1, 0x434952, x: -2.0, y: 1.25, z: -0.55);
                B(m, 0.1, 2.5, 0.1, 0x434952, x: 2.0, y: 1.25, z: -0.55);
                B(m, 0.1, 2.5, 0.1, 0x434952, x: -2.0, y: 1.25, z: 0.55);
                B(m, 0.1, 2.5, 0.1, 0x434952, x: 2.0, y: 1.25, z: 0.55);
                B(m, 4.3, 0.14, 1.45, 0x33383f, y: 2.57);
                B(m, 4.0, 2.2, 0.05, 0x1a2028, y: 1.32, z: -0.58, rough: 0.12, metal: 0.55);
                B(m, 1.05, 1.95, 0.12, 0x101318, x: 1.5, y: 1.35, z: 0.55);
                B(m, 0.88, 1.72, 0.03, 0xff4fa3, x: 1.5, y: 1.35, z: 0.62, emit: 4.0);
                B(m, 2.2, 0.09, 0.4, 0x4a5058, x: -0.7, y: 0.48, z: -0.35);
                B(m, 0.08, 0.45, 0.4, 0x3a3f47, x: -1.7, y: 0.24, z: -0.35);
                B(m, 0.08, 0.45, 0.4, 0x3a3f47, x: 0.3, y: 0.24, z: -0.35);
                B(m, 3.9, 0.05, 0.26, 0xcfeaff, y: 2.46, z: 0.2, emit: 2.6);
            }, len: 4.4, dep: 1.35, cast: true, col: new PropCollider(2.05, 1.25, 0.1, cy: 1.25, cz: -0.58));

            Def("bench", m =>
            {
                B(m, 1.9, 0.09, 0.48, 0x6a563c, y: 0.46);
                B(m, 1.9, 0.44, 0.08, 0x6a563c, y: 0.72, z: -0.2);
                B(m, 0.09, 0.46, 0.46, 0x3f454e, x: -0.82, y: 0.23, rough: 0.4, metal: 0.7);
                B(m, 0.09, 0.46, 0.46, 0x3f454e, x: 0.82, y: 0.23, rough: 0.4, metal: 0.7);
            }, len: 2.0, dep: 0.55, col: new PropCollider(0.95, 0.27, 0.26, cy: 0.27));

            Def("kiosk", m =>
            {
                B(m, 1.15, 2.32, 0.72, 0x2d323b, y: 1.16);
                B(m, 0.94, 1.12, 0.05, 0x2af0ff, y: 1.56, z: 0.375, emit: 3.8);
                B(m, 0.94, 0.2, 0.06, 0xff4fa3, y: 2.18, z: 0.375, emit: 5.5);
                B(m, 1.24, 0.12, 0.84, 0x1d2129, y: 2.36);
                B(m, 0.4, 0.28, 0.04, 0x1a1d22, y: 1.0, z: 0.375);
                B(m, 0.1, 1.9, 0.04, 0x39ff9e, x: -0.6, y: 1.2, z: 0.375, emit: 4.4);
            }, len: 1.25, dep: 0.8, cast: true, col: new PropCollider(0.58, 1.16, 0.38, cy: 1.16));

            Def("bin", m =>
            {
                C(m, 0.3, 0.25, 0.9, 8, 0x424852, y: 0.45, rough: 0.45, metal: 0.6);
                C(m, 0.33, 0.33, 0.07, 8, 0x2c3138, y: 0.93);
                C(m, 0.17, 0.17, 0.05, 8, 0x14171c, y: 0.98);
                B(m, 0.12, 0.16, 0.02, 0x9be0ff, y: 0.62, z: 0.3, emit: 1.6);
            }, len: 0.72, dep: 0.7, col: new PropCollider(0.32, 0.5, 0.32, cy: 0.5));

            Def("hydrant", m =>
            {
                C(m, 0.16, 0.2, 0.6, 8, 0x9c3128, y: 0.3);
                S(m, 0.16, 8, 0x9c3128, y: 0.6);
                C(m, 0.06, 0.06, 0.18, 6, 0x7e2820, y: 0.42, z: 0.16, rx: Pi / 2);
                C(m, 0.06, 0.06, 0.18, 6, 0x7e2820, y: 0.42, x: 0.16, rz: Pi / 2);
            }, len: 0.45, dep: 0.42);

            Def("utilbox", m =>
            {
                B(m, 0.98, 1.24, 0.56, 0x4b515b, y: 0.62, rough: 0.55, metal: 0.5);
                B(m, 1.04, 0.1, 0.62, 0x2f343c, y: 1.27);
                B(m, 0.52, 0.12, 0.02, 0xffb020, y: 0.95, z: 0.29, emit: 1.4);
                B(m, 0.3, 0.5, 0.02, 0x363c44, x: -0.25, y: 0.55, z: 0.29);
                C(m, 0.045, 0.045, 0.6, 5, 0x3a3f47, x: 0.42, y: 1.5);
            }, len: 1.05, dep: 0.62, col: new PropCollider(0.5, 0.62, 0.3, cy: 0.62));

            Def("signpost", m =>
            {
                C(m, 0.045, 0.055, 2.7, 6, 0x545a64, y: 1.35, rough: 0.45, metal: 0.7);
                B(m, 1.05, 0.2, 0.03, 0x2e6f8c, x: 0.5, y: 2.56);
                B(m, 0.85, 0.18, 0.03, 0x7a3454, x: -0.4, y: 2.28, ry: Pi / 2);
                B(m, 0.34, 0.42, 0.03, 0xd8dee8, y: 1.86);
            }, len: 0.5, dep: 0.4);

            // ---- urban decay ----

            Def("dumpster", m =>
            {
                B(m, 2.1, 1.12, 1.02, 0x35604e, y: 0.66, rough: 0.6, metal: 0.45);
                B(m, 2.16, 0.1, 1.08, 0x2a4b3d, y: 1.26);
                B(m, 1.0, 0.09, 1.1, 0x2a4b3d, x: -0.53, y: 1.44, z: 0.14, rx: -0.42);
                C(m, 0.09, 0.09, 0.1, 6, 0x1a1d22, x: -0.9, y: 0.07, z: 0.42, rx: Pi / 2);
                C(m, 0.09, 0.09, 0.1, 6, 0x1a1d22, x: 0.9, y: 0.07, z: 0.42, rx: Pi / 2);
                B(m, 2.16, 0.12, 0.06, 0xffb020, y: 0.34, z: 0.53, emit: 0.5);
                S(m, 0.3, 5, 0x22262d, x: 0.45, y: 1.4, z: -0.1, sy: 0.8);
                S(m, 0.27, 5, 0x2b3037, x: 0.85, y: 1.38, z: 0.22, sy: 0.85);
                B(m, 0.48, 0.36, 0.4, 0x6b5334, x: 0.15, y: 1.48, z: 0.28, ry: 0.6, rz: 0.32);
                B(m, 0.3, 0.24, 0.28, 0x5a4630, x: -0.2, y: 1.4, z: -0.3, ry: -0.9, rz: -0.2);
            }, len: 2.25, dep: 1.15, cast: true, col: new PropCollider(1.08, 0.62, 0.53, cy: 0.62));

            Def("bag", m =>
            {
                S(m, 0.32, 6, 0x1e2229, y: 0.28, sy: 0.9, rough: 0.45, metal: 0.05);
                S(m, 0.14, 5, 0x1a1e24, y: 0.55, x: 0.05, sy: 1.3);
            }, len: 0.62, dep: 0.55);

            Def("cardbox", m =>
            {
                B(m, 0.56, 0.42, 0.44, 0x6f5738, y: 0.21);
                B(m, 0.58, 0.03, 0.2, 0x7d6440, y: 0.44, z: 0.14, rx: 0.5);
                B(m, 0.24, 0.14, 0.02, 0x9a8256, y: 0.24, z: 0.225);
            }, len: 0.62, dep: 0.5);

            Def("crate", m =>
            {
                B(m, 0.72, 0.66, 0.72, 0x5f4c33, y: 0.33);
                B(m, 0.77, 0.06, 0.77, 0x4e3e28, y: 0.66);
                B(m, 0.78, 0.08, 0.09, 0x3f3220, y: 0.34, z: 0.36);
                B(m, 0.09, 0.08, 0.78, 0x3f3220, y: 0.34, x: 0.36);
            }, len: 0.8, dep: 0.8);

            Def("pallet", m =>
            {
                B(m, 1.2, 0.055, 0.2, 0x705a3a, y: 0.145, z: -0.4);
                B(m, 1.2, 0.055, 0.2, 0x705a3a, y: 0.145);
                B(m, 1.2, 0.055, 0.2, 0x705a3a, y: 0.145, z: 0.4);
                B(m, 0.1, 0.1, 1.0, 0x5f4c33, x: -0.5, y: 0.05);
                B(m, 0.1, 0.1, 1.0, 0x5f4c33, x: 0.5, y: 0.05);
                B(m, 1.2, 0.05, 1.0, 0x60492e, y: 0.195);
            }, len: 1.3, dep: 1.05);

            Def("barrel", m =>
            {
                C(m, 0.29, 0.29, 0.86, 8, 0x44604f, y: 0.43, rough: 0.5, metal: 0.6);
                C(m, 0.31, 0.31, 0.05, 8, 0x2f463a, y: 0.62);
                C(m, 0.31, 0.31, 0.05, 8, 0x2f463a, y: 0.24);
                C(m, 0.26, 0.26, 0.03, 8, 0x5d7466, y: 0.87);
                B(m, 0.16, 0.2, 0.02, 0xffb020, y: 0.44, z: 0.295, emit: 0.9);
            }, len: 0.66, dep: 0.62);

            Def("cone", m =>
            {
                B(m, 0.36, 0.045, 0.36, 0xa8481c, y: 0.022);
                C(m, 0.032, 0.155, 0.62, 6, 0xc85c1e, y: 0.34);
                C(m, 0.105, 0.125, 0.09, 6, 0xeef0f2, y: 0.36, emit: 0.8);
            }, len: 0.42, dep: 0.4);

            Def("debris", m =>
            {
                B(m, 0.52, 0.09, 0.42, 0x3d4149, y: 0.045, ry: 0.4);
                B(m, 0.3, 0.14, 0.27, 0x484d55, x: 0.2, y: 0.1, ry: -0.7, rz: 0.2);
                B(m, 0.22, 0.2, 0.2, 0x32363d, x: -0.19, y: 0.09, ry: 1.1);
                C(m, 0.055, 0.055, 0.52, 5, 0x50483c, x: 0.06, y: 0.06, z: 0.22, rz: Pi / 2, ry: 0.5);
                B(m, 0.16, 0.02, 0.2, 0x9aa0a8, x: -0.22, y: 0.02, z: -0.18, ry: -0.5);
            }, len: 0.72, dep: 0.6);

            // ---- cyberpunk specifics ----

            Def("vending", m =>
            {
                B(m, 1.06, 2.04, 0.7, 0x262b33, y: 1.02);
                B(m, 0.92, 1.5, 0.04, 0xff2f8e, y: 1.16, z: 0.35, emit: 4.4, rough: 0.3, metal: 0);
                B(m, 0.94, 0.26, 0.05, 0x2af0ff, y: 1.98, z: 0.35, emit: 6.0, rough: 0.3, metal: 0);
                B(m, 0.52, 0.2, 0.06, 0xffb020, y: 0.4, z: 0.35, emit: 2.2);
                B(m, 1.1, 0.09, 0.76, 0x14171c, y: 2.08);
                B(m, 0.16, 1.5, 0.05, 0x14171c, x: 0.4, y: 1.16, z: 0.365);
                B(m, 0.24, 0.4, 0.06, 0x1a1e24, x: -0.34, y: 0.4, z: 0.36);
            }, len: 1.15, dep: 0.78, cast: true, col: new PropCollider(0.55, 1.02, 0.37, cy: 1.02));

            Def("stall", m =>
            {
                B(m, 2.7, 0.92, 0.95, 0x4f3a2c, y: 0.46);
                B(m, 2.9, 0.09, 1.1, 0x69503f, y: 0.96);
                B(m, 0.09, 2.3, 0.09, 0x3d434c, x: -1.35, y: 1.15, z: -0.36);
                B(m, 0.09, 2.3, 0.09, 0x3d434c, x: 1.35, y: 1.15, z: -0.36);
                B(m, 3.1, 0.1, 1.55, 0x7c2136, y: 2.3, z: 0.12);
                B(m, 3.1, 0.42, 0.06, 0xff3b5c, y: 2.03, z: 0.88, emit: 3.6, rough: 0.3, metal: 0);
                B(m, 0.5, 0.9, 0.05, 0xffb020, x: -1.55, y: 1.5, z: 0.1, ry: Pi / 2, emit: 3.0);
                S(m, 0.085, 6, 0xffcf8a, x: -0.85, y: 1.94, z: 0.72, emit: 10.0, rough: 0.3, metal: 0);
                S(m, 0.085, 6, 0xffcf8a, x: 0, y: 1.97, z: 0.72, emit: 10.0, rough: 0.3, metal: 0);
                S(m, 0.085, 6, 0xffcf8a, x: 0.85, y: 1.94, z: 0.72, emit: 10.0, rough: 0.3, metal: 0);
                C(m, 0.012, 0.012, 0.32, 4, 0x22262d, x: -0.85, y: 2.18, z: 0.72);
                C(m, 0.012, 0.012, 0.3, 4, 0x22262d, x: 0, y: 2.2, z: 0.72);
                C(m, 0.012, 0.012, 0.32, 4, 0x22262d, x: 0.85, y: 2.18, z: 0.72);
                C(m, 0.16, 0.14, 0.14, 8, 0x8d939c, x: 0.9, y: 1.06, z: -0.1, rough: 0.3, metal: 0.85);
                C(m, 0.16, 0.14, 0.14, 8, 0x8d939c, x: 1.2, y: 1.06, z: -0.1, rough: 0.3, metal: 0.85);
                C(m, 0.16, 0.16, 0.5, 6, 0x3d434c, x: -1.0, y: 0.25, z: 0.86);
                C(m, 0.16, 0.16, 0.5, 6, 0x3d434c, x: -0.3, y: 0.25, z: 0.86);
            }, len: 3.2, dep: 1.35, cast: true, col: new PropCollider(1.45, 0.5, 0.5, cy: 0.5));

            // wall-mounted: origin sits on the wall plane, the unit grows toward +Z
            Def("atm", m =>
            {
                B(m, 0.86, 1.42, 0.28, 0x2e333c, z: 0.14);
                B(m, 0.62, 0.42, 0.03, 0x39ff9e, y: 0.26, z: 0.295, emit: 3.4, rough: 0.3, metal: 0);
                B(m, 0.72, 0.09, 0.04, 0x2af0ff, y: 0.6, z: 0.295, emit: 4.5, rough: 0.3, metal: 0);
                B(m, 0.3, 0.06, 0.04, 0xffb020, y: -0.16, z: 0.295, emit: 2.2);
                B(m, 0.9, 0.09, 0.34, 0x1d2129, y: 0.75, z: 0.16);
            }, len: 0.95, dep: 0.32);

            Def("campole", m =>
            {
                C(m, 0.07, 0.1, 3.6, 6, 0x4b515b, y: 1.8, rough: 0.4, metal: 0.75);
                B(m, 0.07, 0.07, 0.8, 0x4b515b, y: 3.54, z: 0.38);
                B(m, 0.17, 0.16, 0.44, 0x22262d, y: 3.44, z: 0.76, rx: 0.3);
                C(m, 0.062, 0.062, 0.05, 6, 0x0e1013, y: 3.37, z: 0.97, rx: Pi / 2);
                S(m, 0.028, 5, 0xff3b30, y: 3.55, z: 0.94, emit: 7.0);
                B(m, 0.22, 0.3, 0.18, 0x363c44, y: 1.5, z: 0.05);
            }, len: 0.6, dep: 0.5);

            Def("camwall", m =>
            {
                B(m, 0.09, 0.09, 0.3, 0x3d434c, z: 0.15);
                B(m, 0.16, 0.15, 0.4, 0x22262d, z: 0.44, y: -0.03, rx: 0.32);
                C(m, 0.058, 0.058, 0.05, 6, 0x0e1013, z: 0.62, y: -0.08, rx: Pi / 2);
                S(m, 0.026, 5, 0xff3b30, z: 0.58, y: 0.08, emit: 7.0);
            }, len: 0.4, dep: 0.65);

            Def("dronepad", m =>
            {
                C(m, 1.7, 1.78, 0.1, 10, 0x2a2f37, y: 0.05);
                C(m, 1.5, 1.5, 0.03, 10, 0x1b1f26, y: 0.11, rough: 0.3, metal: 0.4);
                C(m, 1.63, 1.63, 0.07, 12, 0x2af0ff, y: 0.14, emit: 3.2, open: true, rough: 0.3, metal: 0);
                B(m, 0.7, 0.025, 0.14, 0x39ff9e, y: 0.13, z: 0.9, emit: 3.4);
                B(m, 0.7, 0.025, 0.14, 0x39ff9e, y: 0.13, z: -0.9, emit: 3.4);
                B(m, 0.14, 0.025, 0.7, 0x39ff9e, y: 0.13, x: 0.9, emit: 3.4);
                B(m, 0.14, 0.025, 0.7, 0x39ff9e, y: 0.13, x: -0.9, emit: 3.4);
            }, len: 3.6, dep: 3.6);

            Def("satdish", m =>
            {
                C(m, 0.11, 0.15, 0.55, 6, 0x434952, y: 0.27);
                C(m, 0.62, 0.14, 0.24, 10, 0x646b76, y: 0.72, rx: -0.75, rough: 0.5, metal: 0.4);
                C(m, 0.025, 0.025, 0.44, 4, 0x2f343c, y: 0.9, z: 0.22, rx: 0.9);
                B(m, 0.34, 0.05, 0.34, 0x363c44, y: 0.03);
            }, len: 1.4, dep: 1.4);

            Def("acunit", m =>
            {
                B(m, 0.84, 0.62, 0.5, 0x5a616b, z: 0.28, rough: 0.55, metal: 0.5);
                B(m, 0.68, 0.46, 0.03, 0x2c3138, z: 0.535);
                B(m, 0.9, 0.07, 0.56, 0x434952, z: 0.28, y: 0.34);
                B(m, 0.9, 0.07, 0.56, 0x434952, z: 0.28, y: -0.34);
                C(m, 0.045, 0.045, 0.55, 5, 0x3d434c, x: 0.3, y: -0.6, z: 0.1);
            }, len: 0.95, dep: 0.6);

            Def("roofunit", m =>
            {
                B(m, 1.5, 0.85, 1.1, 0x4d545e, y: 0.45, rough: 0.55, metal: 0.5);
                B(m, 1.2, 0.06, 0.86, 0x2f343c, y: 0.9);
                C(m, 0.42, 0.42, 0.14, 8, 0x363c44, y: 0.95);
                B(m, 1.56, 0.1, 1.16, 0x3d434c, y: 0.05);
                C(m, 0.11, 0.11, 0.9, 6, 0x545a64, x: 0.9, y: 0.45);
            }, len: 1.7, dep: 1.3);

            Def("ventstack", m =>
            {
                C(m, 0.3, 0.34, 1.7, 8, 0x4b515b, y: 0.85, rough: 0.55, metal: 0.55);
                C(m, 0.4, 0.4, 0.12, 8, 0x363c44, y: 1.76);
                C(m, 0.24, 0.3, 0.3, 8, 0x22262d, y: 1.94);
            }, len: 0.9, dep: 0.9);

            return kits;
        }

        // ---------------------------------------------------------------- authoring shorthand (the prototype's B, C and S)

        static void B(PropMeshBuilder m, double w, double h, double d, uint hex,
            double x = 0, double y = 0, double z = 0, double rx = 0, double ry = 0, double rz = 0,
            double sx = 1, double sy = 1, double sz = 1, double emit = 0, double rough = 0.72, double metal = 0.2, double uv = 1.3) =>
            m.Box(w, h, d, hex, new PartStyle(x, y, z, rx, ry, rz, sx, sy, sz, emit, rough, metal, uv));

        static void C(PropMeshBuilder m, double rt, double rb, double h, int seg, uint hex,
            double x = 0, double y = 0, double z = 0, double rx = 0, double ry = 0, double rz = 0,
            double sx = 1, double sy = 1, double sz = 1, double emit = 0, double rough = 0.72, double metal = 0.2, double uv = 1.3, bool open = false) =>
            m.Cylinder(rt, rb, h, seg, hex, new PartStyle(x, y, z, rx, ry, rz, sx, sy, sz, emit, rough, metal, uv, open));

        static void S(PropMeshBuilder m, double r, int seg, uint hex,
            double x = 0, double y = 0, double z = 0, double rx = 0, double ry = 0, double rz = 0,
            double sx = 1, double sy = 1, double sz = 1, double emit = 0, double rough = 0.72, double metal = 0.2, double uv = 1.3) =>
            m.Sphere(r, seg, hex, new PartStyle(x, y, z, rx, ry, rz, sx, sy, sz, emit, rough, metal, uv));
    }
}
