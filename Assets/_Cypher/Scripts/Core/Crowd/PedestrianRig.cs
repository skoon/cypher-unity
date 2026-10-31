using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>The body parts of the rig. A vertex carries the id of its part in <c>Uv1.x</c>; the shader chooses the joint chain and the colour from it.</summary>
    public enum RigPart
    {
        /// <summary>The cheap levels of detail: one rigid body, not articulated.</summary>
        Static = 0,
        TorsoShort = 1,
        TorsoLong = 2,
        Trim = 3,
        Head = 4,
        Hood = 5,
        ArmL = 6,
        ArmR = 7,
        Umbrella = 8,
        Pack = 9,
        ThighL = 10,
        ShinL = 11,
        ThighR = 12,
        ShinR = 13,

        /// <summary>The fighters' guns, in the right hand: drawn only for the one it is (<see cref="HeldWeapon"/>).</summary>
        GunSmg = 14,
        GunRevolver = 15,

        /// <summary>A glowing band across the eyes, in the trim colour: the gang look.</summary>
        Visor = 16,
    }

    public enum RigLod
    {
        /// <summary>The articulated rig, about 40 m out and nearer.</summary>
        Full = 0,

        /// <summary>One rigid four-part body, out to about 90 m.</summary>
        Simple = 1,

        /// <summary>A single box, beyond that.</summary>
        Silhouette = 2,
    }

    /// <summary>
    /// The pedestrian's body parts, a port of the geometry in Crowd.js, built from the same primitives as the props. Each part is modelled in its own joint's frame
    /// (a limb hangs down from its joint, the torso rises from the hips) so that <see cref="PedestrianPose"/>'s matrices, or the shader's version of them, place it.
    /// The full mesh holds every part a pedestrian might have (both coats, the hood, the pack, the umbrella, the trim); the shader collapses the ones this person
    /// lacks. Vertex normals are zero: the pedestrian shader derives faceted normals from screen-space derivatives, as the prototype's flat shading did.
    /// </summary>
    public static class PedestrianRig
    {
        /// <summary>The blob contact shadow's radius for a person of scale 1 (the shadow quad is scaled up from this by the shader).</summary>
        public const double ShadowRadius = 0.44;

        public static MeshData Build(RigLod lod)
        {
            switch (lod)
            {
                case RigLod.Full: return FullBody();
                case RigLod.Simple: return Simple();
                case RigLod.Silhouette: return Silhouette();
                default: throw new ArgumentOutOfRangeException(nameof(lod), lod, "unknown level of detail");
            }
        }

        static MeshData FullBody()
        {
            return Merge(
                Stamp(TorsoGeo(false), RigPart.TorsoShort),
                Stamp(TorsoGeo(true), RigPart.TorsoLong),
                Stamp(TrimGeo(), RigPart.Trim),
                Stamp(HeadGeo(), RigPart.Head),
                Stamp(HoodGeo(), RigPart.Hood),
                Stamp(ArmGeo(), RigPart.ArmL),
                Stamp(ArmGeo(), RigPart.ArmR),
                Stamp(UmbrellaGeo(), RigPart.Umbrella),
                Stamp(PackGeo(), RigPart.Pack),
                Stamp(LimbGeo(0.078, 0.058, PedestrianPose.ThighLength, 5), RigPart.ThighL),
                Stamp(ShinGeo(), RigPart.ShinL),
                Stamp(LimbGeo(0.078, 0.058, PedestrianPose.ThighLength, 5), RigPart.ThighR),
                Stamp(ShinGeo(), RigPart.ShinR),
                Stamp(SmgGeo(), RigPart.GunSmg),
                Stamp(RevolverGeo(), RigPart.GunRevolver),
                Stamp(VisorGeo(), RigPart.Visor));
        }

        /// <summary>A torso, a head and two legs as one rigid piece, standing on the ground (the prototype's "simple" rig).</summary>
        static MeshData Simple()
        {
            MeshData torso = Move(Cyl(0.19, 0.16, 0.72, 5), 0, 1.03, 0);
            MeshData head = Move(Box(0.20, 0.24, 0.20), 0, 1.52, 0);
            MeshData legL = Move(Box(0.13, 0.7, 0.15), -0.09, 0.37, 0);
            MeshData legR = Move(Box(0.13, 0.7, 0.15), 0.09, 0.37, 0);
            return Stamp(Merge(torso, head, legL, legR), RigPart.Static);
        }

        static MeshData Silhouette() => Stamp(Move(Box(0.34, 1.62, 0.25), 0, 0.81, 0), RigPart.Static);

        /// <summary>The soft disc under a person, lying on the ground, facing up, as a fan of ten triangles about the middle.</summary>
        public static MeshData ShadowDisc()
        {
            const int segments = 10;
            var p = new List<float> { 0, 0, 0 };
            for (int k = 0; k < segments; k++)
            {
                double a = (double)k / segments * 2 * Math.PI;
                p.Add((float)(ShadowRadius * Math.Cos(a))); p.Add(0f); p.Add((float)(ShadowRadius * Math.Sin(a)));
            }
            var i = new List<int>();
            for (int k = 0; k < segments; k++) { i.Add(0); i.Add(1 + (k + 1) % segments); i.Add(1 + k); }   // up is the front
            return Finish(p.ToArray(), i.ToArray(), RigPart.Static);
        }

        // ---------------------------------------------------------------- the parts (Crowd.js)

        /// <summary>A tapering limb hanging down from its joint at the origin.</summary>
        static MeshData LimbGeo(double rTop, double rBottom, double length, int seg) => Move(Cyl(rTop, rBottom, length, seg), 0, -length / 2, 0);

        static MeshData ArmGeo()
        {
            MeshData upper = LimbGeo(0.058, 0.046, 0.32, 5);
            // a few degrees of baked elbow keeps the arm from reading as a broom handle
            MeshData fore = Move(RotX(LimbGeo(0.046, 0.038, 0.28, 5), -0.22), 0, -0.31, 0.01);
            MeshData hand = Move(RotX(Box(0.062, 0.10, 0.05), -0.22), 0, -0.60, 0.055);
            return Merge(upper, fore, hand);
        }

        static MeshData ShinGeo()
        {
            MeshData calf = LimbGeo(0.058, 0.045, PedestrianPose.ShinLength, 5);
            MeshData foot = Move(Box(0.096, 0.062, 0.24), 0, -PedestrianPose.ShinLength + 0.031, 0.052);
            return Merge(calf, foot);
        }

        /// <summary>The torso; a long coat grows a hem past the hips, which is the key silhouette split.</summary>
        static MeshData TorsoGeo(bool longCoat)
        {
            var parts = new List<MeshData>
            {
                Move(Scale(Cyl(0.205, 0.158, 0.58, 6), 1, 1, 0.66), 0, 0.30, 0),   // chest
                Move(Box(0.43, 0.115, 0.245), 0, 0.545, 0),                         // yoke
                Move(Cyl(0.052, 0.068, 0.12, 5), 0, 0.60, 0),                       // neck
            };
            if (longCoat)
            {
                parts.Add(Move(Scale(Cyl(0.158, 0.215, 0.44, 6), 1, 1, 0.66), 0, -0.22, 0));   // hem
                parts.Add(Move(RotX(Box(0.20, 0.30, 0.03), 0.08), 0, 0.36, 0.125));            // lapel wedge so the coat front catches a highlight
            }
            else
            {
                parts.Add(Move(Scale(Cyl(0.152, 0.135, 0.16, 6), 1, 1, 0.72), 0, -0.06, 0));   // hips
            }
            return Merge(parts.ToArray());
        }

        static MeshData HeadGeo()
        {
            MeshData skull = Scale(Icosahedron(0.116), 0.92, 1.14, 1.0);
            MeshData jaw = Move(Box(0.13, 0.075, 0.10), 0, -0.075, 0.045);
            return Merge(skull, jaw);
        }

        static MeshData HoodGeo()
        {
            MeshData cowl = Move(Scale(PartialSphere(0.158, 8, 6, Math.PI * 0.62), 1.0, 1.08, 1.12), 0, 0.006, -0.028);
            MeshData peak = Move(RotX(Cyl(0.145, 0.11, 0.05, 8, open: true), 0.34), 0, 0.052, 0.052);
            return Merge(cowl, peak);
        }

        static MeshData PackGeo()
        {
            MeshData bag = Box(0.27, 0.34, 0.16);
            MeshData flap = Move(Box(0.24, 0.09, 0.175), 0, 0.12, 0.005);
            return Merge(bag, flap);
        }

        static MeshData UmbrellaGeo()
        {
            // the canopy is seen from underneath as often as from above, so both faces are drawn
            MeshData canopy = Move(Cyl(0, 0.53, 0.21, 9, open: true), 0, 0.70, 0);
            canopy = Merge(canopy, Flip(canopy));

            var ring = new List<Float3>();
            for (int k = 0; k <= 9; k++)
            {
                double a = (double)k / 9 * 2 * Math.PI;
                ring.Add(new Float3(0.52 * Math.Cos(a), 0.60, 0.52 * Math.Sin(a)));
            }
            var tube = new PropMeshBuilder();
            tube.Tube(ring, 0.012, 3, 0xffffff);
            MeshData rim = tube.Build();

            MeshData shaft = Move(Cyl(0.011, 0.011, 0.72, 4), 0, 0.36, 0);
            return Merge(canopy, rim, shaft);
        }

        // The held guns, three boxes each, in the gun's own frame: barrel along +Z, top +Y, the hand at the origin. The proportions are the viewmodels'
        // (WeaponModels' Wasp and Magnat) at a glance; at the distances a fighter is seen from, more would not show, and every person's full body carries them.

        static MeshData SmgGeo()
        {
            MeshData receiver = Move(Box(0.05, 0.075, 0.36), 0, 0.03, 0.10);   // the barrel shroud runs on from it
            MeshData mag = Move(RotX(Box(0.03, 0.12, 0.04), 0.15), 0, -0.05, 0.13);
            MeshData grip = Move(RotX(Box(0.035, 0.10, 0.04), -0.25), 0, -0.04, -0.01);
            return Merge(receiver, mag, grip);
        }

        static MeshData RevolverGeo()
        {
            MeshData frame = Move(Box(0.05, 0.065, 0.12), 0, 0.035, 0.05);      // the frame with the cylinder in it
            MeshData barrel = Move(Box(0.022, 0.026, 0.16), 0, 0.045, 0.18);
            MeshData grip = Move(RotX(Box(0.032, 0.10, 0.045), -0.3), 0, -0.035, -0.01);
            return Merge(frame, barrel, grip);
        }

        /// <summary>In the head's frame, across the front of the skull and just proud of it.</summary>
        static MeshData VisorGeo() => Move(Box(0.20, 0.038, 0.05), 0, 0.02, 0.095);

        /// <summary>Emissive trim worn just proud of the torso so it never fights it for the pixel.</summary>
        static MeshData TrimGeo()
        {
            MeshData band = Move(Scale(Cyl(0.212, 0.208, 0.032, 6), 1, 1, 0.68), 0, 0.335, 0);
            MeshData shoulder = Move(Box(0.45, 0.022, 0.255), 0, 0.545, 0);
            MeshData stripeL = Move(Box(0.022, 0.26, 0.02), -0.115, 0.40, 0.142);
            MeshData stripeR = Move(Box(0.022, 0.26, 0.02), 0.115, 0.40, 0.142);
            return Merge(band, shoulder, stripeL, stripeR);
        }

        // ---------------------------------------------------------------- primitives and the geometry operations three.js has

        static MeshData Cyl(double rTop, double rBottom, double height, int seg, bool open = false)
        {
            var b = new PropMeshBuilder();
            b.Cylinder(rTop, rBottom, height, seg, 0xffffff, new PartStyle(open: open));
            return b.Build();
        }

        static MeshData Box(double w, double h, double d)
        {
            var b = new PropMeshBuilder();
            b.Box(w, h, d, 0xffffff);
            return b.Build();
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
            return Finish(p, faces, RigPart.Static);
        }

        /// <summary>The cap of a sphere from the pole down to <paramref name="thetaLength"/> (three.js's SphereGeometry with a theta length), open at the bottom.</summary>
        static MeshData PartialSphere(double radius, int widthSegments, int heightSegments, double thetaLength)
        {
            var p = new List<float>();
            for (int iy = 0; iy <= heightSegments; iy++)
            {
                double v = (double)iy / heightSegments;
                for (int ix = 0; ix <= widthSegments; ix++)
                {
                    double u = (double)ix / widthSegments;
                    double theta = v * thetaLength;
                    p.Add((float)(-radius * Math.Cos(u * 2 * Math.PI) * Math.Sin(theta)));
                    p.Add((float)(radius * Math.Cos(theta)));
                    p.Add((float)(radius * Math.Sin(u * 2 * Math.PI) * Math.Sin(theta)));
                }
            }
            var idx = new List<int>();
            int row = widthSegments + 1;
            for (int iy = 0; iy < heightSegments; iy++)
            {
                for (int ix = 0; ix < widthSegments; ix++)
                {
                    int a = iy * row + ix + 1, b = iy * row + ix, c = (iy + 1) * row + ix, d = (iy + 1) * row + ix + 1;
                    if (iy != 0) { idx.Add(a); idx.Add(b); idx.Add(d); }
                    idx.Add(b); idx.Add(c); idx.Add(d);   // every row has this one: the cap stops short of the south pole, so the last ring is its open rim
                }
            }
            return Finish(p.ToArray(), idx.ToArray(), RigPart.Static);
        }

        static MeshData Move(MeshData g, double x, double y, double z) => g.Translated(x, y, z);

        static MeshData Scale(MeshData g, double sx, double sy, double sz)
        {
            var p = (float[])g.Positions.Clone();
            for (int k = 0; k < p.Length; k += 3) { p[k] = (float)(p[k] * sx); p[k + 1] = (float)(p[k + 1] * sy); p[k + 2] = (float)(p[k + 2] * sz); }
            return Finish(p, g.Indices, RigPart.Static);
        }

        /// <summary>Turn about the X axis through the origin (three.js's rotateX): +Y swings toward +Z.</summary>
        static MeshData RotX(MeshData g, double angle)
        {
            double cs = Math.Cos(angle), sn = Math.Sin(angle);
            var p = (float[])g.Positions.Clone();
            for (int k = 0; k < p.Length; k += 3)
            {
                double y = p[k + 1], z = p[k + 2];
                p[k + 1] = (float)(y * cs - z * sn);
                p[k + 2] = (float)(y * sn + z * cs);
            }
            return Finish(p, g.Indices, RigPart.Static);
        }

        /// <summary>The same triangles facing the other way.</summary>
        static MeshData Flip(MeshData g)
        {
            var i = (int[])g.Indices.Clone();
            for (int k = 0; k < i.Length; k += 3) { int t = i[k + 1]; i[k + 1] = i[k + 2]; i[k + 2] = t; }
            return Finish((float[])g.Positions.Clone(), i, RigPart.Static);
        }

        /// <summary>The parts as one mesh. Each keeps the part id it was stamped with.</summary>
        static MeshData Merge(params MeshData[] parts)
        {
            var p = new List<float>();
            var uv1 = new List<float>();
            var idx = new List<int>();
            foreach (MeshData g in parts)
            {
                int offset = p.Count / 3;
                p.AddRange(g.Positions);
                uv1.AddRange(g.Uv1);
                foreach (int i in g.Indices) idx.Add(offset + i);
            }
            int n = p.Count / 3;
            return new MeshData
            {
                Positions = p.ToArray(),
                Normals = new float[n * 3],
                Uvs = new float[n * 2],
                Colors = new float[n * 3],
                Uv1 = uv1.ToArray(),
                Indices = idx.ToArray(),
            };
        }

        /// <summary>Set every vertex's part id.</summary>
        static MeshData Stamp(MeshData g, RigPart part) => Finish(g.Positions, g.Indices, part);

        static MeshData Finish(float[] positions, int[] indices, RigPart part)
        {
            int n = positions.Length / 3;
            var uv1 = new float[n * 4];
            for (int k = 0; k < n; k++) uv1[k * 4] = (float)part;
            return new MeshData
            {
                Positions = positions,
                Normals = new float[n * 3],
                Uvs = new float[n * 2],
                Colors = new float[n * 3],
                Uv1 = uv1,
                Indices = indices,
            };
        }
    }
}
