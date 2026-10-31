using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class PedestrianRigTests
    {
        static readonly MeshData Full = PedestrianRig.Build(RigLod.Full);
        static readonly MeshData Simple = PedestrianRig.Build(RigLod.Simple);
        static readonly MeshData Silhouette = PedestrianRig.Build(RigLod.Silhouette);

        static IEnumerable<int> Vertices(MeshData m, RigPart part) =>
            Enumerable.Range(0, m.VertexCount).Where(i => (int)m.Uv1[i * 4] == (int)part);

        static IEnumerable<int> Triangles(MeshData m, RigPart part) =>
            Enumerable.Range(0, m.TriangleCount).Where(t => (int)m.Uv1[m.Indices[t * 3] * 4] == (int)part);

        static (double Min, double Max) Range(MeshData m, RigPart part, int axis)
        {
            var values = Vertices(m, part).Select(i => (double)m.Positions[i * 3 + axis]).ToList();
            return (values.Min(), values.Max());
        }

        static double[] TriangleNormal(MeshData m, int t)
        {
            int a = m.Indices[t * 3] * 3, b = m.Indices[t * 3 + 1] * 3, c = m.Indices[t * 3 + 2] * 3;
            double ux = m.Positions[b] - m.Positions[a], uy = m.Positions[b + 1] - m.Positions[a + 1], uz = m.Positions[b + 2] - m.Positions[a + 2];
            double vx = m.Positions[c] - m.Positions[a], vy = m.Positions[c + 1] - m.Positions[a + 1], vz = m.Positions[c + 2] - m.Positions[a + 2];
            return new[] { uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx };
        }

        static double[] Centroid(MeshData m, int t)
        {
            var c = new double[3];
            for (int k = 0; k < 3; k++)
            {
                for (int axis = 0; axis < 3; axis++) c[axis] += m.Positions[m.Indices[t * 3 + k] * 3 + axis] / 3.0;
            }
            return c;
        }

        // ---------------------------------------------------------------- structure

        [Test]
        public void EveryLevelOfDetailIsAWellFormedMesh()
        {
            foreach (MeshData m in new[] { Full, Simple, Silhouette })
            {
                int n = m.VertexCount;
                Assert.Greater(n, 0);
                Assert.AreEqual(n * 3, m.Normals.Length);
                Assert.AreEqual(n * 2, m.Uvs.Length);
                Assert.AreEqual(n * 3, m.Colors.Length);
                Assert.AreEqual(n * 4, m.Uv1.Length);
                Assert.AreEqual(0, m.Indices.Length % 3);
                Assert.IsTrue(m.Indices.All(i => i >= 0 && i < n), "every index is a vertex");
                Assert.IsTrue(m.Positions.All(v => !float.IsNaN(v) && !float.IsInfinity(v)));
            }
        }

        [Test]
        public void TheFullBodyHasEveryPartAndTheCheapOnesOnlyOneRigidPart()
        {
            var parts = Enumerable.Range(0, Full.VertexCount).Select(i => (int)Full.Uv1[i * 4]).Distinct().OrderBy(x => x).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(1, 16).ToList(), parts, "every part from the long torso to the visor, and not the static one");
            foreach (RigPart part in Enum.GetValues(typeof(RigPart)).Cast<RigPart>().Where(p => p != RigPart.Static))
            {
                Assert.Greater(Triangles(Full, part).Count(), 0, part.ToString());
            }
            Assert.IsTrue(Enumerable.Range(0, Simple.VertexCount).All(i => (int)Simple.Uv1[i * 4] == (int)RigPart.Static));
            Assert.IsTrue(Enumerable.Range(0, Silhouette.VertexCount).All(i => (int)Silhouette.Uv1[i * 4] == (int)RigPart.Static));
        }

        [Test]
        public void ThePartIdsAreTheOnesTheShaderIsToldAbout()
        {
            // the HLSL defines the same numbers (a Unity-side test reads the file): this pins the C# side
            Assert.AreEqual(0, (int)RigPart.Static);
            Assert.AreEqual(1, (int)RigPart.TorsoShort);
            Assert.AreEqual(2, (int)RigPart.TorsoLong);
            Assert.AreEqual(3, (int)RigPart.Trim);
            Assert.AreEqual(4, (int)RigPart.Head);
            Assert.AreEqual(5, (int)RigPart.Hood);
            Assert.AreEqual(6, (int)RigPart.ArmL);
            Assert.AreEqual(7, (int)RigPart.ArmR);
            Assert.AreEqual(8, (int)RigPart.Umbrella);
            Assert.AreEqual(9, (int)RigPart.Pack);
            Assert.AreEqual(10, (int)RigPart.ThighL);
            Assert.AreEqual(11, (int)RigPart.ShinL);
            Assert.AreEqual(12, (int)RigPart.ThighR);
            Assert.AreEqual(13, (int)RigPart.ShinR);
            Assert.AreEqual(14, (int)RigPart.GunSmg);
            Assert.AreEqual(15, (int)RigPart.GunRevolver);
            Assert.AreEqual(16, (int)RigPart.Visor);
        }

        [Test]
        public void EachLevelOfDetailIsCheaperThanTheLast()
        {
            Console.WriteLine($"RIG full {Full.TriangleCount} triangles / {Full.VertexCount} vertices, simple {Simple.TriangleCount} / {Simple.VertexCount}, silhouette {Silhouette.TriangleCount} / {Silhouette.VertexCount}");
            Assert.Greater(Full.TriangleCount, Simple.TriangleCount * 4);
            Assert.Greater(Simple.TriangleCount, Silhouette.TriangleCount * 2);
            Assert.AreEqual(12, Silhouette.TriangleCount, "a box");
            Assert.Less(Full.TriangleCount, 800, "the budget for 400 people: this many triangles each, collapsed accessories included");
        }

        [Test]
        public void ThePartsOfOneBodyAreWornTogetherSoTheLongAndShortCoatDifferByTheHem()
        {
            Assert.Greater(Triangles(Full, RigPart.TorsoLong).Count(), Triangles(Full, RigPart.TorsoShort).Count() - 12, "the long coat has the hem and lapel in place of the hips");
            Assert.Less(Range(Full, RigPart.TorsoLong, 1).Min, Range(Full, RigPart.TorsoShort, 1).Min - 0.1, "and reaches lower");
        }

        // ---------------------------------------------------------------- shape

        // (the torso and the trim are unions of overlapping pieces whose hidden faces point inward, so they do not suit a test from one middle)
        [TestCase(RigPart.Head)]
        [TestCase(RigPart.ThighL)]
        [TestCase(RigPart.ShinR)]
        [TestCase(RigPart.Pack)]
        public void TrianglesFaceOutwardFromTheMiddleOfAClosedPart(RigPart part)
        {
            double[] mid = new double[3];
            var verts = Vertices(Full, part).ToList();
            foreach (int v in verts) for (int a = 0; a < 3; a++) mid[a] += Full.Positions[v * 3 + a] / verts.Count;
            int outward = 0, total = 0;
            foreach (int t in Triangles(Full, part))
            {
                double[] n = TriangleNormal(Full, t), c = Centroid(Full, t);
                double len = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
                if (len < 1e-12) continue;   // the odd degenerate triangle
                total++;
                if (n[0] * (c[0] - mid[0]) + n[1] * (c[1] - mid[1]) + n[2] * (c[2] - mid[2]) > 0) outward++;
            }
            Assert.GreaterOrEqual(outward / (double)total, 0.9, $"{part}: {outward} of {total} triangles face out");
        }

        [Test]
        public void PartsAreModelledInTheirOwnJointsFrame()
        {
            // a limb hangs down from its joint at the origin
            Assert.That(Range(Full, RigPart.ThighL, 1).Max, Is.InRange(-0.01, 0.01));
            Assert.That(Range(Full, RigPart.ThighL, 1).Min, Is.InRange(-0.47, -0.43));
            Assert.That(Range(Full, RigPart.ShinL, 1).Max, Is.InRange(-0.01, 0.01));
            Assert.That(Range(Full, RigPart.ShinL, 1).Min, Is.InRange(-0.46, -0.43));
            Assert.That(Range(Full, RigPart.ArmL, 1).Max, Is.InRange(-0.01, 0.01));
            Assert.That(Range(Full, RigPart.ArmL, 1).Min, Is.InRange(-0.68, -0.58));
            // the torso rises from the hips to the neck
            Assert.That(Range(Full, RigPart.TorsoShort, 1).Min, Is.InRange(-0.15, -0.1));
            Assert.That(Range(Full, RigPart.TorsoShort, 1).Max, Is.InRange(0.64, 0.68));
            // the head is about its own middle
            Assert.That(Range(Full, RigPart.Head, 1).Min, Is.InRange(-0.15, -0.1));
            Assert.That(Range(Full, RigPart.Head, 1).Max, Is.InRange(0.10, 0.125), "the skull is an icosahedron scaled 1.14 tall: its highest vertex is 85% of the way up");
        }

        [Test]
        public void TheTwoLegsAndTwoArmsAreTheSameShapeTwice()
        {
            Assert.AreEqual(Triangles(Full, RigPart.ThighL).Count(), Triangles(Full, RigPart.ThighR).Count());
            Assert.AreEqual(Triangles(Full, RigPart.ShinL).Count(), Triangles(Full, RigPart.ShinR).Count());
            Assert.AreEqual(Triangles(Full, RigPart.ArmL).Count(), Triangles(Full, RigPart.ArmR).Count());
            Assert.AreEqual(Range(Full, RigPart.ArmL, 0), Range(Full, RigPart.ArmR, 0), "an arm is modelled the same either side: the pose puts it on the left or the right");
        }

        [Test]
        public void TheShinEndsInAFootThatPointsForward()
        {
            var foot = Vertices(Full, RigPart.ShinL).Where(i => Full.Positions[i * 3 + 1] < -0.40).ToList();
            double zMax = foot.Max(i => Full.Positions[i * 3 + 2]);
            Assert.Greater(zMax, 0.15, "toes ahead of the heel, +Z being the way a person faces");
        }

        // ---------------------------------------------------------------- posed

        static (double MinY, double MaxY, double MinX, double MaxX, double MinZ, double MaxZ) Extent(PoseInput input, params RigPart[] parts)
        {
            PedestrianPoseResult r = PedestrianPose.Pose(input);
            double minY = double.MaxValue, maxY = double.MinValue, minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (RigPart part in parts)
            {
                Mat4 m = Matrix(r, part);
                foreach (int i in Vertices(Full, part))
                {
                    var p = m.Transform(Full.Positions[i * 3], Full.Positions[i * 3 + 1], Full.Positions[i * 3 + 2]);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
                }
            }
            return (minY, maxY, minX, maxX, minZ, maxZ);
        }

        static Mat4 Matrix(PedestrianPoseResult r, RigPart part)
        {
            switch (part)
            {
                case RigPart.TorsoShort:
                case RigPart.TorsoLong: return r.Torso;
                case RigPart.Trim: return r.Trim;
                case RigPart.Head: return r.Head;
                case RigPart.Hood: return r.Hood;
                case RigPart.ArmL: return r.ArmL;
                case RigPart.ArmR: return r.ArmR;
                case RigPart.Umbrella: return r.Umbrella;
                case RigPart.Pack: return r.Pack;
                case RigPart.ThighL: return r.ThighL;
                case RigPart.ShinL: return r.ShinL;
                case RigPart.ThighR: return r.ThighR;
                case RigPart.ShinR: return r.ShinR;
                default: throw new ArgumentException(part.ToString());
            }
        }

        static PoseInput Standing(bool umbrella = false, double speed = 0, double phase = 0) => new PoseInput
        {
            Scale = 1, Build = 1, Speed = speed, Phase = phase, PrevPhase = phase, Alpha = 1, Umbrella = umbrella, Time = 0, LookSeed = 0,
        };

        static readonly RigPart[] WornAlways =
        {
            RigPart.TorsoShort, RigPart.Head, RigPart.ArmL, RigPart.ArmR, RigPart.ThighL, RigPart.ShinL, RigPart.ThighR, RigPart.ShinR,
        };

        [Test]
        public void StandingTheFigureIsAboutOnePointSevenTwoMetresTallWithItsFeetOnTheGround()
        {
            var e = Extent(Standing(), WornAlways);
            Assert.That(e.MaxY, Is.InRange(1.66, 1.78), "the top of the head");
            Assert.That(e.MinY, Is.InRange(-0.05, 0.01), "the soles (the root sits 2 cm below the ground height, so the shoes sink a touch)");
            Assert.That(e.MaxX - e.MinX, Is.InRange(0.4, 0.7), "shoulder to shoulder");
            Assert.That(e.MaxZ - e.MinZ, Is.InRange(0.2, 0.45), "front to back");
        }

        [Test]
        public void ScaleScalesTheWholeFigure()
        {
            PoseInput tall = Standing();
            tall.Scale = 1.09;
            var small = Extent(Standing(), WornAlways);
            var big = Extent(tall, WornAlways);
            Assert.AreEqual((small.MaxY + 0.02) * 1.09 - 0.02, big.MaxY, 0.01);
        }

        [Test]
        public void WalkingTheLegsSwingApartAndOneFootIsAheadOfTheOther()
        {
            // phase 0: cos = 1, so the left thigh swings one way and the right the other
            var stand = Extent(Standing(), RigPart.ShinL, RigPart.ShinR, RigPart.ThighL, RigPart.ThighR);
            var stride = Extent(Standing(speed: 1.4, phase: 0), RigPart.ShinL, RigPart.ShinR, RigPart.ThighL, RigPart.ThighR);
            Assert.Greater(stride.MaxZ - stride.MinZ, stand.MaxZ - stand.MinZ + 0.2, "a stride is longer than standing");
        }

        [Test]
        public void TheFeetStayNearTheGroundThroughTheWholeStep()
        {
            for (double phase = 0; phase < Math.PI * 2; phase += 0.4)
            {
                var e = Extent(Standing(speed: 1.4, phase: phase), RigPart.ShinL, RigPart.ShinR);
                Assert.That(e.MinY, Is.InRange(-0.12, 0.08), $"phase {phase:0.0}: the planted foot stays planted (the hips drop as the stance leg extends)");
            }
        }

        [Test]
        public void AnUmbrellaOpensAboveTheHead()
        {
            var e = Extent(Standing(umbrella: true), RigPart.Umbrella);
            Assert.Greater(e.MaxY, 2.0, "the canopy's top");
            Assert.Greater(e.MaxX - e.MinX, 0.9, "about a metre across");
            Assert.Greater(e.MinY, 1.3, "the handle is held at about elbow height; the canopy is far above");
        }

        [Test]
        public void ARaisedArmCarriesTheUmbrellaForward()
        {
            var down = Extent(Standing(), RigPart.ArmR);
            var carrying = Extent(Standing(umbrella: true), RigPart.ArmR);
            Assert.Greater(carrying.MaxZ, down.MaxZ + 0.2, "the arm is out in front");
            Assert.Greater(carrying.MinY, down.MinY + 0.3, "and no longer hangs to the thigh");
        }

        // ---------------------------------------------------------------- the others

        [Test]
        public void TheSimpleBodyStandsOnTheGroundAsTallAsTheRig()
        {
            double minY = Enumerable.Range(0, Simple.VertexCount).Min(i => Simple.Positions[i * 3 + 1]);
            double maxY = Enumerable.Range(0, Simple.VertexCount).Max(i => Simple.Positions[i * 3 + 1]);
            Assert.That(minY, Is.InRange(-0.01, 0.03));
            Assert.That(maxY, Is.InRange(1.6, 1.7));
        }

        [Test]
        public void TheSilhouetteIsAnUprightBoxOnTheGround()
        {
            double minY = Enumerable.Range(0, Silhouette.VertexCount).Min(i => Silhouette.Positions[i * 3 + 1]);
            double maxY = Enumerable.Range(0, Silhouette.VertexCount).Max(i => Silhouette.Positions[i * 3 + 1]);
            Assert.AreEqual(0.0, minY, 1e-6);
            Assert.AreEqual(1.62, maxY, 1e-6);
        }

        [Test]
        public void TheStaticBodyIsPlacedByHeadingScaleAndBuild()
        {
            Mat4 m = PedestrianPose.StaticBody(3, 0.2, -4, Math.PI / 2, 1.1, 1.2);
            // a point a metre ahead (+Z) of the body, turned a quarter toward +X, lands a metre along +X, with the build applied to the (local) x axis only
            var ahead = m.Transform(0, 0, 1);
            Assert.AreEqual(3 + 1.1, ahead.X, 1e-9);
            Assert.AreEqual(0.2, ahead.Y, 1e-9);
            Assert.AreEqual(-4, ahead.Z, 1e-9);
            var side = m.Transform(1, 0, 0);
            Assert.AreEqual(1.1 * 1.2, Math.Sqrt((side.X - 3) * (side.X - 3) + (side.Z + 4) * (side.Z + 4)), 1e-9);
        }

        [Test]
        public void TheShadowIsAFlatDiscFacingUp()
        {
            MeshData d = PedestrianRig.ShadowDisc();
            Assert.AreEqual(10, d.TriangleCount);
            Assert.AreEqual(11, d.VertexCount);
            for (int t = 0; t < d.TriangleCount; t++)
            {
                double[] n = TriangleNormal(d, t);
                Assert.Greater(n[1], 0, "up is the front");
                Assert.AreEqual(0.0, n[0], 1e-9);
                Assert.AreEqual(0.0, n[2], 1e-9);
            }
            Assert.IsTrue(Enumerable.Range(0, d.VertexCount).All(i => d.Positions[i * 3 + 1] == 0f), "on the ground");
            double r = Enumerable.Range(0, d.VertexCount).Max(i => Math.Sqrt(d.Positions[i * 3] * d.Positions[i * 3] + d.Positions[i * 3 + 2] * d.Positions[i * 3 + 2]));
            Assert.AreEqual(PedestrianRig.ShadowRadius, r, 1e-6);
        }

        [Test]
        public void AnUnknownLevelOfDetailFailsLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PedestrianRig.Build((RigLod)9));
        }
    }
}
