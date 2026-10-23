using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The rig's joint matrices checked element for element against Crowd._pose (60 synthetic states from tools/gen-golden-crowd.mjs).</summary>
    public class PedestrianPoseGoldenTests
    {
        static readonly string[] Lines = Golden.Lines("pose_golden_20771.txt");
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate(20771).Streets, new NavGeometry(62, 46, 25.4, 0.185, 0));
        static readonly CrowdSim Sim = CrowdSim.Create(Nav, 400, 110, 0, 0);

        // The prototype's matrices are Float32Arrays, accurate to about 6e-8 of their size (translations here reach 10).
        const double Tolerance = 2e-6;

        const double Alpha = 0.37;

        static PedestrianPoseResult Compute(string[] p, out PoseInput input)
        {
            Pedestrian ped = Sim.Pool[Golden.I(p[2])];
            PedestrianAppearance a = ped.Appearance;
            input = new PoseInput
            {
                PrevX = Golden.D(p[3]), PrevZ = Golden.D(p[4]), PrevY = Golden.D(p[5]),
                X = Golden.D(p[6]), Z = Golden.D(p[7]), Y = Golden.D(p[8]),
                PrevHeading = Golden.D(p[9]), Heading = Golden.D(p[10]),
                PrevPhase = Golden.D(p[11]), Phase = Golden.D(p[12]),
                Speed = Golden.D(p[13]), LookYaw = Golden.D(p[14]), Near = p[15] == "1",
                Time = Golden.D(p[16]), CamX = Golden.D(p[17]), CamZ = Golden.D(p[18]), Alpha = Alpha,
                Scale = a.Scale, Build = a.Build, LongCoat = a.LongCoat, Hood = a.Hood, Pack = a.Pack, Umbrella = a.Umbrella, Trim = a.Trim, LookSeed = ped.LookSeed,
            };
            return PedestrianPose.Pose(input);
        }

        static Mat4 Part(PedestrianPoseResult r, string name)
        {
            switch (name)
            {
                case "torso": return r.Torso;
                case "trim": return r.Trim;
                case "head": return r.Head;
                case "hood": return r.Hood;
                case "armL": return r.ArmL;
                case "armR": return r.ArmR;
                case "umbrella": return r.Umbrella;
                case "pack": return r.Pack;
                case "thighL": return r.ThighL;
                case "shinL": return r.ShinL;
                case "thighR": return r.ThighR;
                case "shinR": return r.ShinR;
                default: throw new ArgumentException(name);
            }
        }

        [Test]
        public void EveryJointMatrixOfEveryStateMatchesThePrototype()
        {
            var states = Golden.Rows(Lines, "P");
            Assert.AreEqual(60, states.Count);
            int compared = 0;
            double worst = 0;
            foreach (string[] p in states)
            {
                string k = p[1];
                PedestrianPoseResult r = Compute(p, out _);
                foreach (string[] m in Golden.Rows(Lines, "M").Where(row => row[1] == k))
                {
                    Mat4 mine = Part(r, m[2]);
                    Assert.IsNotNull(mine, $"state {k}: the port has no {m[2]}");
                    for (int e = 0; e < 16; e++)
                    {
                        double diff = Math.Abs(Golden.D(m[3 + e]) - mine.E[e]);
                        worst = Math.Max(worst, diff);
                        Assert.LessOrEqual(diff, Tolerance, $"state {k}, {m[2]}, element {e}: prototype {m[3 + e]}, port {mine.E[e]}");
                    }
                    compared++;
                }
            }
            Assert.AreEqual(Golden.Rows(Lines, "M").Count, compared);
            Console.WriteLine($"POSEGOLDEN {compared} matrices, worst difference {worst:0.0e+0}");
        }

        [Test]
        public void TheHeadsNewTurnMatches()
        {
            foreach (string[] p in Golden.Rows(Lines, "P"))
            {
                PedestrianPoseResult r = Compute(p, out _);
                string[] l = Golden.Rows(Lines, "L").Single(row => row[1] == p[1]);
                Assert.AreEqual(Golden.D(l[2]), r.LookYaw, 1e-12, $"state {p[1]}");
            }
        }

        [Test]
        public void PartsAreMissingExactlyWhenThePersonLacksThem()
        {
            foreach (string[] p in Golden.Rows(Lines, "P"))
            {
                PedestrianPoseResult r = Compute(p, out _);
                PedestrianAppearance a = Sim.Pool[Golden.I(p[2])].Appearance;
                Assert.AreEqual(a.Trim, r.Trim != null);
                Assert.AreEqual(a.Hood, r.Hood != null);
                Assert.AreEqual(a.Pack, r.Pack != null);
                Assert.AreEqual(a.Umbrella, r.Umbrella != null);
            }
        }

        [Test]
        public void TheStatesCoverTheGaitAndTheGlance()
        {
            var speeds = Golden.Rows(Lines, "P").Select(p => Golden.D(p[13])).Distinct().ToList();
            Assert.GreaterOrEqual(speeds.Count, 5, "standing, strolling, hurrying");
            Assert.IsTrue(Golden.Rows(Lines, "P").Any(p => p[15] == "1"), "some are near the camera");
            Assert.IsTrue(Sim.Pool.Take(60).Any(p => p.Appearance.Umbrella) && Sim.Pool.Take(60).Any(p => p.Appearance.Hood) && Sim.Pool.Take(60).Any(p => p.Appearance.Pack));
        }
    }
}
