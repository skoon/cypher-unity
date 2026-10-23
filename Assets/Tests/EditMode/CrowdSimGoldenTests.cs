using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// The crowd checked number for number against Crowd.js, run headless in node on the same city (fixture from tools/gen-golden-crowd.mjs).
    /// Three scenarios: the crowd as seeded with no traffic (90 s); everyone stood on a kerb-edge midpoint facing the road, with no traffic and with a stub traffic query
    /// that holds people at some kerbs (15 s). In the seeded crowd people almost never cross a road, so the second pair is what exercises crossings and the wait.
    /// </summary>
    public class CrowdSimGoldenTests
    {
        static readonly string[] Lines = Golden.Lines("crowd_golden_20771.txt");

        // The same measurement the navigation golden uses: the prototype's kerb probe reads the pavement edge as 25.4 m.
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate(20771).Streets, new NavGeometry(62, 46, 25.4, 0.185, 0));

        // The prototype keeps its node positions in a Float32Array, so its people walk toward points that are off by up to about 1e-5 m from the exact ones
        // the port uses (doubles), and the gait phase integrates that over hundreds of steps. Everything discrete (nodes, states, crossings) must match exactly;
        // the numbers to within half a millimetre.
        const double Tolerance = 5e-4;

        sealed class BusyTraffic : ITrafficQuery
        {
            public int Asked, Held;

            public bool RoadBusy(double fromX, double fromZ, double x, double z, Crossing crossing, double walkSpeed)
            {
                bool busy = ((int)Math.Floor(x * 0.1) + (int)Math.Floor(z * 0.1) + (int)crossing) % 3 == 0;
                Asked++;
                if (busy) Held++;
                return busy;
            }
        }

        static readonly Dictionary<string, int[]> Checkpoints = new Dictionary<string, int[]>
        {
            ["free"] = new[] { 0, 30, 120, 360, 900, 2400, 5400 },
            ["kerbfree"] = new[] { 0, 5, 30, 120, 360, 900 },
            ["kerbbusy"] = new[] { 0, 5, 30, 120, 360, 900 },
        };

        /// <summary>Stand each active person on a kerb-edge midpoint of one of the blocks near the plaza, facing the road, just arrived and about to choose a way on (the harness does the same).</summary>
        static void StartAtKerbs(CrowdSim sim)
        {
            List<int> near = Enumerable.Range(0, Nav.Blocks.Count).Where(bi => Math.Abs(Nav.Blocks[bi].Cx) < 100 && Math.Abs(Nav.Blocks[bi].Cz) < 100).ToList();
            int[] midpoints = { 1, 3, 5, 7 };
            for (int n = 0; n < sim.Active.Count; n++)
            {
                Pedestrian p = sim.Active[n];
                int bi = near[n % near.Count], k = midpoints[n % 4];
                int node = bi * 8 + k;
                int nextRing = bi * 8 + (k + 1) % 8;
                p.Node = node;
                p.PrevNode = bi * 8 + (k + 7) % 8;
                p.X = Nav.NodeX(node); p.Z = Nav.NodeZ(node); p.Y = Nav.WalkY;
                int across = nextRing;
                for (int e = 0; e < Nav.Neighbours(node).Count; e++)
                {
                    if (Nav.NeighbourCrossings(node)[e] != Crossing.None) { across = Nav.Neighbours(node)[e]; break; }
                }
                p.Heading = Math.Atan2(Nav.NodeX(across) - Nav.NodeX(node), Nav.NodeZ(across) - Nav.NodeZ(node));
                p.Tx = p.X; p.Tz = p.Z; p.Lat = 0; p.Crossing = Crossing.None;
                p.State = PedState.Walk; p.Timer = 5; p.WaitTimer = 0; p.Speed = p.Cruise;
                p.PrevX = p.X; p.PrevZ = p.Z; p.PrevY = p.Y; p.PrevHeading = p.Heading; p.PrevPhase = p.Phase;
            }
        }

        static Pedestrian Copy(Pedestrian p) =>
            (Pedestrian)typeof(Pedestrian).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(p, null);

        /// <summary>Snapshots of every active person at each checkpoint, keyed by (step, person index).</summary>
        static Dictionary<(int, int), Pedestrian> Run(string scenario, ITrafficQuery traffic)
        {
            CrowdSim sim = CrowdSim.Create(Nav, 400, 110, 0, 0);
            if (scenario != "free") StartAtKerbs(sim);
            var snapshots = new Dictionary<(int, int), Pedestrian>();
            int step = 0;
            foreach (int target in Checkpoints[scenario])
            {
                while (step < target)
                {
                    sim.Step(1.0 / 60, 0, 0, 0, 0, traffic);
                    step++;
                }
                foreach (Pedestrian p in sim.Active) snapshots[(step, p.Index)] = Copy(p);
            }
            return snapshots;
        }

        static readonly BusyTraffic Stub = new BusyTraffic();
        static readonly Dictionary<(int, int), Pedestrian> Free = Run("free", null);
        static readonly Dictionary<(int, int), Pedestrian> KerbFree = Run("kerbfree", null);
        static readonly Dictionary<(int, int), Pedestrian> KerbBusy = Run("kerbbusy", Stub);

        // ---------------------------------------------------------------- the pool

        [Test]
        public void EveryPersonInThePoolLooksAndStartsAsInThePrototype()
        {
            CrowdSim sim = CrowdSim.Create(Nav, 400, 110, 0, 0);
            List<string[]> rows = Golden.Rows(Lines, "A");
            Assert.AreEqual(400, rows.Count);
            Assert.AreEqual(400, sim.Pool.Count);
            foreach (string[] a in rows)
            {
                Pedestrian p = sim.Pool[Golden.I(a[1])];
                PedestrianAppearance look = p.Appearance;
                Assert.AreEqual(Golden.D(a[2]), look.Scale, 1e-12, $"scale of {p.Index}");
                Assert.AreEqual(Golden.D(a[3]), look.Build, 1e-12, $"build of {p.Index}");
                Assert.AreEqual(a[4] == "1", look.LongCoat, $"long coat of {p.Index}");
                Assert.AreEqual(a[5] == "1", look.Umbrella, $"umbrella of {p.Index}");
                Assert.AreEqual(a[6] == "1", look.Hood, $"hood of {p.Index}");
                Assert.AreEqual(a[7] == "1", look.Pack, $"pack of {p.Index}");
                Assert.AreEqual(a[8] == "1", look.Trim, $"trim of {p.Index}");
                Assert.AreEqual(uint.Parse(a[9]), look.Skin, $"skin of {p.Index}");
                Assert.AreEqual(uint.Parse(a[10]), look.Coat, $"coat of {p.Index}");
                Assert.AreEqual(uint.Parse(a[11]), look.Pants, $"pants of {p.Index}");
                Assert.AreEqual(uint.Parse(a[12]), look.TrimColour, $"trim colour of {p.Index}");
                Assert.AreEqual(Golden.D(a[13]), look.TrimPower, 1e-12, $"trim power of {p.Index}");
                Assert.AreEqual(uint.Parse(a[14]), look.UmbrellaColour, $"umbrella colour of {p.Index}");
                Assert.AreEqual(Golden.D(a[15]), p.Cruise, 1e-12, $"cruise of {p.Index}");
                Assert.AreEqual(a[16] == "1", p.Hurried, $"hurried {p.Index}");
                Assert.AreEqual(Golden.D(a[17]), p.LookSeed, 1e-9, $"look seed of {p.Index}");
            }
        }

        // ---------------------------------------------------------------- stepping

        static void Compare(string scenario, Dictionary<(int, int), Pedestrian> mine, int step)
        {
            List<string[]> rows = Golden.Rows(Lines, "T").Where(r => r[1] == scenario && Golden.I(r[2]) == step).ToList();
            Assert.AreEqual(110, rows.Count, $"{scenario} at step {step}");
            double worst = 0;
            string worstWhere = "";
            foreach (string[] r in rows)
            {
                int index = Golden.I(r[3]);
                Assert.IsTrue(mine.TryGetValue((step, index), out Pedestrian p), $"person {index} is not active in the port");
                void Near(string name, double expected, double actual)
                {
                    double e = Math.Abs(expected - actual);
                    if (e > worst) { worst = e; worstWhere = $"{name} of person {index}: prototype {expected}, port {actual}"; }
                }
                Near("x", Golden.D(r[4]), p.X);
                Near("z", Golden.D(r[5]), p.Z);
                Near("y", Golden.D(r[6]), p.Y);
                Near("heading", Golden.D(r[7]), p.Heading);
                Near("phase", Golden.D(r[8]), p.Phase);
                Near("speed", Golden.D(r[9]), p.Speed);
                Near("timer", Golden.D(r[14]), p.Timer);
                Near("waitTimer", Golden.D(r[15]), p.WaitTimer);
                Near("tx", Golden.D(r[16]), p.Tx);
                Near("tz", Golden.D(r[17]), p.Tz);
                Near("lat", Golden.D(r[18]), p.Lat);
                Assert.AreEqual(Golden.I(r[10]), p.Node, $"node of person {index} at step {step} ({scenario})");
                Assert.AreEqual(Golden.I(r[11]), p.PrevNode, $"previous node of person {index} at step {step} ({scenario})");
                Assert.AreEqual(Golden.I(r[12]), (int)p.State, $"state of person {index} at step {step} ({scenario})");
                Assert.AreEqual(Golden.I(r[13]), (int)p.Crossing, $"crossing of person {index} at step {step} ({scenario})");
            }
            Assert.LessOrEqual(worst, Tolerance, $"{scenario} at step {step}: the largest difference is {worstWhere}");
        }

        [TestCase(0)]
        [TestCase(30)]
        [TestCase(120)]
        [TestCase(360)]
        [TestCase(900)]
        [TestCase(2400)]
        [TestCase(5400)]
        public void TheSeededCrowdIsWhereThePrototypeHasItAfterNSteps(int step) => Compare("free", Free, step);

        [TestCase(0)]
        [TestCase(5)]
        [TestCase(30)]
        [TestCase(120)]
        [TestCase(360)]
        [TestCase(900)]
        public void PeopleAtTheKerbChooseAndWalkAsInThePrototypeWithNoTraffic(int step) => Compare("kerbfree", KerbFree, step);

        [TestCase(0)]
        [TestCase(5)]
        [TestCase(30)]
        [TestCase(120)]
        [TestCase(360)]
        [TestCase(900)]
        public void PeopleAtTheKerbWaitAndGoAsInThePrototypeWhenTheRoadIsBusy(int step) => Compare("kerbbusy", KerbBusy, step);

        // ---------------------------------------------------------------- the scenarios do what they claim

        [Test]
        public void TheBusyStubWasAskedAndHeldPeopleBack()
        {
            Assert.Greater(Stub.Asked, 100, "people reached kerbs and asked");
            Assert.Greater(Stub.Held, 50, "and many were told to wait");
            Assert.Less(Stub.Held, Stub.Asked, "but not all of the time");
        }

        [Test]
        public void SomeoneWaitsAtAKerbAndSomeoneCrossesTheRoad()
        {
            Assert.IsTrue(KerbBusy.Where(kv => kv.Key.Item1 == 5).Any(kv => kv.Value.WaitTimer > 0), "someone is waiting");
            Assert.IsTrue(KerbBusy.Where(kv => kv.Key.Item1 == 30).Any(kv => kv.Value.Crossing != Crossing.None), "someone is on a crossing");
            Assert.IsTrue(KerbFree.Where(kv => kv.Key.Item1 == 30).Any(kv => kv.Value.Crossing != Crossing.None));
        }

        [Test]
        public void TheBusyKerbsChangeWhereThePeopleAre()
        {
            int differ = KerbFree.Where(kv => kv.Key.Item1 == 120).Count(kv => Math.Abs(kv.Value.X - KerbBusy[kv.Key].X) > 0.01 || Math.Abs(kv.Value.Z - KerbBusy[kv.Key].Z) > 0.01);
            Assert.Greater(differ, 20);
        }

        [Test]
        public void InTheSeededCrowdCrossingsAreRareBecauseWalkingAlongTheRingBeatsTurningAcrossIt()
        {
            // a property of the prototype's choice weights that the port keeps: a crossing is perpendicular to the way someone is walking (alignment 0), so it
            // scores at most 0.196 against at least 0.55 for carrying on round the block; only a spawn or a recycle that happens to face the road starts one
            int onCrossings = Free.Count(kv => kv.Value.Crossing != Crossing.None);
            Assert.Less(onCrossings, Free.Count / 10, $"{onCrossings} of {Free.Count} snapshots are on a crossing");
        }
    }
}
