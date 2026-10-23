using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// The traffic checked number for number against Traffic.js, run headless in node on the same city (fixture from tools/gen-golden-traffic.mjs). Three scenarios with the player
    /// and camera standing still and nobody walking, so the prototype's behaviour is all there is: the Medium tier at the plaza, the Ultra fleet elsewhere, and the Ultra fleet with
    /// the player standing in a lane so vehicles have to stop for them. The fleet table (kinds, speeds, paint and under-glow) and the aircraft are checked too.
    /// </summary>
    public class TrafficSimGoldenTests
    {
        static readonly string[] Lines = Golden.Lines("traffic_golden_20771.txt");

        // The same measurement the navigation golden uses: the prototype's kerb probe reads the pavement edge as 25.4 m.
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate(20771).Streets, new NavGeometry(62, 46, 25.4, 0.185, 0));

        // The traffic has no single-precision data, so only the last digits of the maths library differ.
        const double Tolerance = 1e-6;

        sealed class Scenario
        {
            public int Ground, Aircraft;
            public double PlayerX, PlayerZ;
            public int[] Checkpoints;
        }

        static readonly Dictionary<string, Scenario> Scenarios = new Dictionary<string, Scenario>
        {
            ["medium"] = new Scenario { Ground = 52, Aircraft = 20, PlayerX = 0, PlayerZ = 0, Checkpoints = new[] { 0, 60, 300, 900, 1800, 3600 } },
            ["ultra"] = new Scenario { Ground = 96, Aircraft = 36, PlayerX = 97, PlayerZ = -64, Checkpoints = new[] { 0, 600, 1800, 2700 } },
            ["inroad"] = new Scenario { Ground = 96, Aircraft = 36, PlayerX = InRoadX(), PlayerZ = 0, Checkpoints = new[] { 0, 900, 1800 } },
        };

        /// <summary>The scenario's player stands in the lane of +Z traffic on the fourth north-south road; the fixture says where.</summary>
        static double InRoadX()
        {
            string header = Lines.First(l => l.StartsWith("# inroad:", StringComparison.Ordinal));
            string[] words = header.Split(' ');
            return Golden.D(words[words.ToList().IndexOf("player") + 1]);
        }

        sealed class Snapshot
        {
            public readonly List<Vehicle> Vehicles = new List<Vehicle>();
            public readonly List<Aircraft> Aircraft = new List<Aircraft>();
            public int SignalPhase;
            public double SignalTime;
        }

        static T Copy<T>(T value) where T : class =>
            (T)typeof(T).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(value, null);

        static int _stoppedForThePlayer;

        static Dictionary<int, Snapshot> Run(string label)
        {
            Scenario sc = Scenarios[label];
            TrafficSim sim = TrafficSim.Create(Nav, sc.PlayerX, sc.PlayerZ, sc.Ground, sc.Aircraft);
            var snapshots = new Dictionary<int, Snapshot>();
            int step = 0;
            foreach (int target in sc.Checkpoints)
            {
                while (step < target)
                {
                    sim.Step(1.0 / 60, sc.PlayerX, sc.PlayerZ, sc.PlayerX, sc.PlayerZ);
                    step++;
                    if (label == "inroad")
                    {
                        foreach (Vehicle v in sim.Vehicles)
                        {
                            double rel = (sc.PlayerX - v.X) * Math.Sin(v.Heading) + (sc.PlayerZ - v.Z) * Math.Cos(v.Heading);
                            double lat = (sc.PlayerX - v.X) * Math.Cos(v.Heading) - (sc.PlayerZ - v.Z) * Math.Sin(v.Heading);
                            if (v.Active && v.Speed < 0.5 && rel > 0 && rel < 14 && Math.Abs(lat) < 2.6) _stoppedForThePlayer++;
                        }
                    }
                }
                var snap = new Snapshot { SignalPhase = sim.Signal.Phase, SignalTime = sim.Signal.PhaseTime };
                foreach (Vehicle v in sim.Vehicles) snap.Vehicles.Add(Copy(v));
                foreach (Aircraft a in sim.Airborne) snap.Aircraft.Add(Copy(a));
                snapshots[step] = snap;
            }
            return snapshots;
        }

        static readonly Dictionary<string, Dictionary<int, Snapshot>> Runs = Scenarios.Keys.ToDictionary(k => k, Run);

        // ---------------------------------------------------------------- the fleet

        [Test]
        public void TheFleetIsBuiltAsInThePrototype()
        {
            TrafficSim sim = TrafficSim.Create(Nav, 0, 0, 52, 20);
            List<string[]> rows = Golden.Rows(Lines, "A").Where(r => r[1] == "medium").ToList();
            Assert.AreEqual(96, rows.Count);
            Assert.AreEqual(96, sim.Fleet.Count);
            foreach (string[] a in rows)
            {
                Vehicle v = sim.Fleet[Golden.I(a[2])];
                Assert.AreEqual(Golden.I(a[3]), v.TypeIndex, $"type of {v.Index}");
                Assert.AreEqual(Golden.I(a[4]), v.Slot, $"slot of {v.Index}");
                Assert.AreEqual(Golden.D(a[5]), v.Cruise, 1e-12, $"cruise of {v.Index}");
                Assert.AreEqual(Golden.D(a[6]), v.Spin, 1e-12, $"spin of {v.Index}");
                Assert.AreEqual(Golden.D(a[7]), v.Glow, 1e-12, $"glow of {v.Index}");
                Assert.AreEqual(Golden.D(a[8]), v.LampTint, 1e-12, $"lamp tint of {v.Index}");
                Assert.AreEqual(Golden.I(a[9]), v.PaintIndex, $"paint of {v.Index}");
                Assert.AreEqual(Golden.I(a[10]), v.NeonIndex, $"under-glow of {v.Index}");
            }
        }

        [Test]
        public void TheAircraftAreBuiltAsInThePrototype()
        {
            TrafficSim sim = TrafficSim.Create(Nav, 0, 0, 52, 20);
            List<string[]> rows = Golden.Rows(Lines, "B").Where(r => r[1] == "medium").ToList();
            Assert.AreEqual(36, rows.Count);
            Assert.AreEqual(36, sim.AircraftFleet.Count);
            foreach (string[] b in rows)
            {
                Aircraft a = sim.AircraftFleet[Golden.I(b[2])];
                Assert.AreEqual(Golden.I(b[3]), a.TypeIndex, $"type of {a.Index}");
                Assert.AreEqual(Golden.I(b[4]), a.Slot, $"slot of {a.Index}");
                Assert.AreEqual(Golden.I(b[5]), a.PaintIndex, $"paint of {a.Index}");
            }
        }

        // ---------------------------------------------------------------- stepping

        static void Compare(string label, int step)
        {
            Snapshot mine = Runs[label][step];
            string[] signal = Golden.Rows(Lines, "S").Single(r => r[1] == label && Golden.I(r[2]) == step);
            Assert.AreEqual(Golden.I(signal[3]), mine.SignalPhase, $"{label} at step {step}: signal phase");
            Assert.AreEqual(Golden.D(signal[4]), mine.SignalTime, 1e-9, $"{label} at step {step}: signal time");

            List<string[]> rows = Golden.Rows(Lines, "V").Where(r => r[1] == label && Golden.I(r[2]) == step).ToList();
            Assert.AreEqual(rows.Count, mine.Vehicles.Count, $"{label} at step {step}: how many vehicles");
            double worst = 0;
            string worstWhere = "";
            foreach (string[] r in rows)
            {
                int index = Golden.I(r[3]);
                Vehicle v = mine.Vehicles.SingleOrDefault(o => o.Index == index);
                Assert.IsNotNull(v, $"vehicle {index} is not in use in the port at step {step} ({label})");
                void Near(string name, double expected, double actual)
                {
                    double e = Math.Abs(expected - actual);
                    if (e > worst) { worst = e; worstWhere = $"{name} of vehicle {index}: prototype {expected}, port {actual}"; }
                }
                string where = $"vehicle {index} at step {step} ({label})";
                Assert.AreEqual(r[4] == "1", v.Active, $"active of {where}");
                if (!v.Active) continue;
                Near("x", Golden.D(r[5]), v.X);
                Near("z", Golden.D(r[6]), v.Z);
                Near("heading", Golden.D(r[7]), v.Heading);
                Near("speed", Golden.D(r[8]), v.Speed);
                Near("wpX", Golden.D(r[15]), v.WpX);
                Near("wpZ", Golden.D(r[16]), v.WpZ);
                Near("spin", Golden.D(r[17]), v.Spin);
                Assert.AreEqual(Golden.I(r[9]), v.Ix, $"ix of {where}");
                Assert.AreEqual(Golden.I(r[10]), v.Iz, $"iz of {where}");
                Assert.AreEqual(Golden.I(r[11]), v.Dx, $"dx of {where}");
                Assert.AreEqual(Golden.I(r[12]), v.Dz, $"dz of {where}");
                Assert.AreEqual(Golden.I(r[13]), v.Leg, $"leg of {where}");
                Assert.AreEqual(Golden.I(r[14]), (int)v.Turn, $"turn of {where}");
            }
            Assert.LessOrEqual(worst, Tolerance, $"{label} at step {step}: the largest difference is {worstWhere}");

            List<string[]> flyers = Golden.Rows(Lines, "F").Where(r => r[1] == label && Golden.I(r[2]) == step).ToList();
            Assert.AreEqual(flyers.Count, mine.Aircraft.Count, $"{label} at step {step}: how many aircraft");
            worst = 0;
            foreach (string[] r in flyers)
            {
                int index = Golden.I(r[3]);
                Aircraft a = mine.Aircraft.SingleOrDefault(o => o.Index == index);
                Assert.IsNotNull(a, $"aircraft {index} is not in use in the port at step {step} ({label})");
                void Near(string name, double expected, double actual)
                {
                    double e = Math.Abs(expected - actual);
                    if (e > worst) { worst = e; worstWhere = $"{name} of aircraft {index}: prototype {expected}, port {actual}"; }
                }
                Near("x", Golden.D(r[4]), a.X);
                Near("y", Golden.D(r[5]), a.Y);
                Near("z", Golden.D(r[6]), a.Z);
                Near("heading", Golden.D(r[7]), a.Heading);
                Near("speed", Golden.D(r[8]), a.Speed);
                Near("dirX", Golden.D(r[9]), a.DirX);
                Near("dirZ", Golden.D(r[10]), a.DirZ);
                Near("bob", Golden.D(r[11]), a.Bob);
                Near("bank", Golden.D(r[12]), a.Bank);
            }
            Assert.LessOrEqual(worst, Tolerance, $"{label} at step {step}: the largest difference is {worstWhere}");
        }

        [TestCase(0)]
        [TestCase(60)]
        [TestCase(300)]
        [TestCase(900)]
        [TestCase(1800)]
        [TestCase(3600)]
        public void TheMediumTrafficIsWhereThePrototypeHasItAfterNSteps(int step) => Compare("medium", step);

        // Not longer than 45 s: a vehicle waiting to turn across traffic coasts past its waypoint and then steers for a point exactly behind it, where the direction
        // of the turn is decided by the last bit of atan2. At 48 s in this run that happens and the port and V8 round differently.
        [TestCase(0)]
        [TestCase(600)]
        [TestCase(1800)]
        [TestCase(2700)]
        public void TheFullFleetIsWhereThePrototypeHasItAfterNSteps(int step) => Compare("ultra", step);

        [TestCase(0)]
        [TestCase(900)]
        [TestCase(1800)]
        public void TrafficStoppingForAPlayerInTheRoadIsAsInThePrototype(int step) => Compare("inroad", step);

        // ---------------------------------------------------------------- the scenarios do what they claim

        [Test]
        public void TheScenariosAreBusyEnoughToMeanSomething()
        {
            Snapshot late = Runs["ultra"][2700];
            Assert.AreEqual(96, late.Vehicles.Count);
            Assert.AreEqual(36, late.Aircraft.Count);
            Assert.Greater(late.Vehicles.Count(v => v.Speed > 5), 10, "vehicles are moving");
            Assert.Greater(late.Vehicles.Count(v => v.Speed < 0.5), 3, "and some are queued");
            Assert.Greater(late.Vehicles.Select(v => v.TypeIndex).Distinct().Count(), 3, "all four kinds");
            Assert.AreEqual(2, late.Vehicles.Select(v => v.Leg).Distinct().Count(), "some in the junctions, some not");
            Assert.Greater(late.Vehicles.Count(v => v.Turn != Manoeuvre.Straight), 5, "some turning");
        }

        [Test]
        public void SomeoneStandingInALaneStopsTheVehiclesComingAtThem()
        {
            Assert.Greater(_stoppedForThePlayer, 0, "a vehicle came to a halt in front of the player, in their lane and within 14 m");
        }
    }
}
