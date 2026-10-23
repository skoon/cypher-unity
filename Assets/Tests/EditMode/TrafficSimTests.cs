using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class TrafficSimTests
    {
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate());
        const double Dt = 1.0 / 60;

        static TrafficSim Make(int ground = 52, int aircraft = 20, double px = 0, double pz = 0, uint seed = TrafficSim.DefaultSeed, TrafficRules rules = null) =>
            TrafficSim.Create(Nav, px, pz, ground, aircraft, seed, rules ?? TrafficRules.City);

        static void Run(TrafficSim sim, double seconds, double px = 0, double pz = 0, IPedestrianQuery people = null)
        {
            int steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++) sim.Step(Dt, px, pz, px, pz, people);
        }

        sealed class Walkers : IPedestrianQuery
        {
            public readonly List<Pedestrian> List = new List<Pedestrian>();

            public IReadOnlyList<Pedestrian> OnRoad => List;
        }

        static double StopOffset => Nav.AsphaltHalf + 1.1;

        /// <summary>Run a clock on for some seconds in small steps (it moves on at most one phase per call).</summary>
        static void AdvanceClock(SignalClock clock, double seconds)
        {
            for (int i = 0; i < (int)Math.Round(seconds / 0.01); i++) clock.Advance(0.01);
        }

        /// <summary>A sim with one vehicle on the road, which the test then puts where it wants: travelling the way (dx, dz), approaching junction (ix, iz), with the junction's stop line <paramref name="toStopLine"/> metres ahead of its middle.</summary>
        static Vehicle Lone(TrafficSim sim, int typeIndex, int dx, int dz, int ix, int iz, double toStopLine, double speed, int slot = 0)
        {
            Vehicle v = sim.Vehicles[slot];
            v.Type = VehicleCatalog.Ground[typeIndex];
            v.TypeIndex = typeIndex;
            v.Cruise = v.Type.CruiseMax;
            double lane = sim.Lane * v.Type.LaneScale;
            double cx = Nav.XLines[ix], cz = Nav.ZLines[iz];
            // the stop line is StopOffset short of the junction's middle; the lane is on the right of the way of travel in the prototype's frame
            v.X = cx - dx * (StopOffset + toStopLine) + -dz * lane;
            v.Z = cz - dz * (StopOffset + toStopLine) + dx * lane;
            v.Y = Nav.RoadY;
            v.Dx = dx; v.Dz = dz; v.Ndx = dx; v.Ndz = dz;
            v.Heading = Math.Atan2(dx, dz);
            v.Speed = speed;
            v.Active = true;
            v.Ix = ix; v.Iz = iz;
            v.Leg = 0;
            v.Turn = Manoeuvre.Straight;
            v.WpX = cx - dx * StopOffset + -dz * lane;
            v.WpZ = cz - dz * StopOffset + dx * lane;
            v.PrevX = v.X; v.PrevZ = v.Z; v.PrevHeading = v.Heading;
            return v;
        }

        static double DistanceToStopLine(Vehicle v) => Math.Sqrt((v.WpX - v.X) * (v.WpX - v.X) + (v.WpZ - v.Z) * (v.WpZ - v.Z));

        // ---------------------------------------------------------------- creation

        [TestCase(18, 5)]
        [TestCase(52, 20)]
        [TestCase(96, 36)]
        [TestCase(0, 0)]
        public void ATierGetsItsBudgetOfVehiclesAndAircraftFromTheFullFleet(int ground, int aircraft)
        {
            TrafficSim sim = Make(ground, aircraft);
            Assert.AreEqual(96, sim.Fleet.Count);
            Assert.AreEqual(36, sim.AircraftFleet.Count);
            Assert.AreEqual(ground, sim.Vehicles.Count);
            Assert.AreEqual(aircraft, sim.Airborne.Count);
            CollectionAssert.AreEqual(FleetBudget.Distribute(ground, VehicleCatalog.GroundShares()), Enumerable.Range(0, 4).Select(ti => sim.Vehicles.Count(v => v.TypeIndex == ti)).ToArray());
        }

        [Test]
        public void ASmallerTierUsesTheFirstOfEachTypesRun()
        {
            TrafficSim sim = Make(18, 5);
            foreach (Vehicle v in sim.Vehicles) Assert.Less(v.Slot, FleetBudget.Distribute(18, VehicleCatalog.GroundShares())[v.TypeIndex]);
            Assert.AreSame(sim.Fleet[0], sim.Vehicles[0]);
        }

        [Test]
        public void TheFleetIsMadeOfTheTypesInOrderWithTheirPlaces()
        {
            TrafficSim sim = Make();
            for (int i = 0; i < sim.Fleet.Count; i++) Assert.AreEqual(i, sim.Fleet[i].Index);
            CollectionAssert.AreEqual(new[] { 38, 18, 13, 27 }, Enumerable.Range(0, 4).Select(ti => sim.Fleet.Count(v => v.TypeIndex == ti)).ToArray());
            foreach (Vehicle v in sim.Fleet)
            {
                Assert.AreSame(VehicleCatalog.Ground[v.TypeIndex], v.Type);
                Assert.That(v.Cruise, Is.InRange(v.Type.CruiseMin, v.Type.CruiseMax));
                Assert.That(v.LampTint, Is.InRange(0.9, 1.15));
                Assert.That(v.Glow, Is.InRange(0.0, 1.0));
                Assert.That(v.PaintIndex, Is.InRange(0, VehicleCatalog.Paint.Length - 1));
                Assert.That(v.NeonIndex, Is.InRange(0, VehicleCatalog.Neon.Length - 1));
            }
        }

        [Test]
        public void TheSameSeedMakesTheSameTrafficAndAnotherSeedADifferentOne()
        {
            TrafficSim a = Make(), b = Make(), c = Make(seed: 1234);
            Run(a, 20); Run(b, 20); Run(c, 20);
            for (int i = 0; i < a.Vehicles.Count; i++)
            {
                Assert.AreEqual(a.Vehicles[i].X, b.Vehicles[i].X, $"vehicle {i}");
                Assert.AreEqual(a.Vehicles[i].Z, b.Vehicles[i].Z);
            }
            Assert.IsTrue(Enumerable.Range(0, a.Vehicles.Count).Any(i => a.Vehicles[i].X != c.Vehicles[i].X), "another seed, another city's traffic");
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => TrafficSim.Create(null, 0, 0, 10, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => TrafficSim.Create(Nav, 0, 0, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TrafficSim.Create(Nav, 0, 0, 97, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TrafficSim.Create(Nav, 0, 0, 0, 37));
            TrafficSim sim = Make();
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Step(-0.01, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.SetBudget(97, 0));
            Assert.Throws<ArgumentNullException>(() => sim.NearestToPlayer(0, 0, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.RoadBusy(0, 0, 0, 10, Crossing.X, 0));
        }

        // ---------------------------------------------------------------- the road

        [Test]
        public void EveryoneStartsOnTheRoadInTheSpawnBandOrNotAtAll()
        {
            TrafficSim sim = Make(96, 36);
            int onRoad = 0;
            foreach (Vehicle v in sim.Vehicles)
            {
                if (!v.Active) continue;
                onRoad++;
                double d = Math.Sqrt(v.X * v.X + v.Z * v.Z);
                Assert.That(d, Is.InRange(8, 210), $"vehicle {v.Index}");
                Assert.AreEqual(Nav.RoadY, v.Y);
            }
            Assert.Greater(onRoad, 80, "nearly all of them find a place");
        }

        [Test]
        public void VehiclesKeepToTheirLaneBetweenJunctions()
        {
            TrafficSim sim = Make(96, 0);
            int samples = 0, outsideLane = 0;
            double worst = 0;
            string where = "";
            for (int s = 0; s < 300; s++)
            {
                Run(sim, 1);
                if (s < 10) continue;   // the first seconds settle the vehicles that were put down too fast to stop at a signal
                foreach (Vehicle v in sim.Vehicles)
                {
                    if (!v.Active || v.Leg != 0) continue;
                    double lane = sim.Lane * v.Type.LaneScale;
                    // on the legs between junctions, the lateral position is the lane's: z for a vehicle travelling along X, x for one along Z
                    IReadOnlyList<double> across = v.Dx != 0 ? Nav.ZLines : Nav.XLines;
                    IReadOnlyList<double> along = v.Dx != 0 ? Nav.XLines : Nav.ZLines;
                    double lateral = v.Dx != 0 ? v.Z : v.X, longitudinal = v.Dx != 0 ? v.X : v.Z;
                    if (along.Min(l => Math.Abs(l - longitudinal)) < 16) continue;   // near a junction the vehicle is still lining up or turning
                    double sign = v.Dx != 0 ? v.Dx : -v.Dz;
                    double deviation = across.Min(l => Math.Abs(lateral - (l + sign * lane)));
                    samples++;
                    if (deviation > 2.0) outsideLane++;
                    if (deviation > worst) { worst = deviation; where = $"vehicle {v.Index} at {v.X:0.0}, {v.Z:0.0} after {s} s"; }
                }
            }
            Assert.Greater(samples, 5000);
            Assert.Less(worst, Nav.AsphaltHalf, $"nobody leaves the asphalt (worst: {worst:0.0} m from their lane, {where})");
            Assert.Less(outsideLane / (double)samples, 0.03, "and nearly everyone is within 2 m of the lane's middle");
        }

        [Test]
        public void NoOneDrivesIntoTheBackOfTheVehicleAhead()
        {
            TrafficSim sim = Make(96, 0);
            double least = double.PositiveInfinity;
            string where = "";
            for (int s = 0; s < 180; s++)
            {
                Run(sim, 1);
                foreach (Vehicle v in sim.Vehicles)
                {
                    if (!v.Active) continue;
                    double fx = Math.Sin(v.Heading), fz = Math.Cos(v.Heading);
                    foreach (Vehicle o in sim.Vehicles)
                    {
                        if (ReferenceEquals(o, v) || !o.Active) continue;
                        double ox = o.X - v.X, oz = o.Z - v.Z;
                        double ahead = ox * fx + oz * fz, side = ox * fz - oz * fx;
                        if (ahead <= 0 || Math.Abs(side) > 1.2) continue;
                        if (Math.Sin(o.Heading) * fx + Math.Cos(o.Heading) * fz < 0.9) continue;   // going the same way
                        double bumper = ahead - v.HalfLength - o.HalfLength;
                        if (bumper < least) { least = bumper; where = $"vehicle {v.Index} behind {o.Index} after {s} s"; }
                    }
                }
            }
            Assert.Greater(least, 0.5, $"closest approach to the vehicle ahead in the same lane: {least:0.00} m ({where})");
        }

        [Test]
        public void EveryoneStaysInsideTheCityAndIsFiniteForTenMinutes()
        {
            TrafficSim sim = Make(96, 36);
            double reach = Nav.XLines[Nav.XLines.Count - 1] + 20;
            for (int s = 0; s < 600; s++)
            {
                Run(sim, 1);
                foreach (Vehicle v in sim.Vehicles)
                {
                    Assert.IsTrue(!double.IsNaN(v.X) && !double.IsNaN(v.Z) && !double.IsNaN(v.Heading) && !double.IsNaN(v.Speed), $"vehicle {v.Index} after {s} s");
                    if (!v.Active) continue;
                    Assert.LessOrEqual(Math.Abs(v.X), reach, $"vehicle {v.Index} after {s} s");
                    Assert.LessOrEqual(Math.Abs(v.Z), reach, $"vehicle {v.Index} after {s} s");
                    Assert.That(v.Speed, Is.InRange(0, v.Type.CruiseMax + 1e-9));
                }
            }
        }

        // ---------------------------------------------------------------- signals

        [Test]
        public void ARedSignalStopsAVehicleAtTheLineAndGreenLetsItGo()
        {
            TrafficSim sim = Make(1, 0);
            // travelling along Z: red for the first 15.1 s of the cycle (X has green, then amber)
            Vehicle v = Lone(sim, 0, 0, 1, 3, 3, 35, 10);
            Run(sim, 12);
            Assert.AreEqual(0, sim.Signal.GreenAxis);
            Assert.AreEqual(0, v.Speed, "stopped");
            Assert.That(DistanceToStopLine(v), Is.InRange(0.0, 1.5), "at the stop line, not past it");
            Assert.AreEqual(0, v.Leg, "and waiting");

            Run(sim, 8);   // 20 s: green for Z began at 15.1 s
            Assert.AreEqual(1, sim.Signal.GreenAxis);
            Assert.IsTrue(v.Leg == 1 || v.Iz > 3 || v.Z > Nav.ZLines[3], "it has gone through");
        }

        [Test]
        public void OnGreenAVehicleCarriesOnThroughTheJunction()
        {
            TrafficSim sim = Make(1, 0);
            Vehicle v = Lone(sim, 0, 1, 0, 4, 3, 20, 12);   // along X: green for the first 12.5 s
            double x0 = v.X;
            Run(sim, 5);
            Assert.Greater(v.Speed, 10, "no braking");
            Assert.Greater(v.X, x0 + 40, "and a long way along");
            Assert.Greater(v.X, Nav.XLines[4], "through the junction");
        }

        [Test]
        public void OnAmberAVehicleCloseToTheLineGoesAndOneFarFromItStops()
        {
            foreach ((double distance, bool goes) in new[] { (5.0, true), (45.0, false) })
            {
                TrafficSim sim = Make(1, 0);
                AdvanceClock(sim.Signal, 12.51);   // X is now on amber
                Assert.IsTrue(sim.Signal.Amber);
                Vehicle v = Lone(sim, 0, 1, 0, 4, 3, distance, 10);
                Run(sim, 2.0);
                Assert.AreEqual(goes, v.Leg == 1 || v.X > Nav.XLines[4], $"{distance} m from the line");
            }
        }

        [Test]
        public void VehiclesQueueBehindOneStoppedAtTheLine()
        {
            TrafficSim sim = Make(2, 0);
            Vehicle front = Lone(sim, 0, 0, 1, 3, 3, 3, 0);
            Vehicle rear = Lone(sim, 0, 0, 1, 3, 3, 40, 11, slot: 1);
            Run(sim, 12);
            Assert.AreEqual(0, front.Speed);
            Assert.AreEqual(0, rear.Speed, "stopped behind it");
            double bumper = front.Z - rear.Z - front.HalfLength - rear.HalfLength;
            Assert.That(bumper, Is.InRange(5.0, 9.0), "about 7.5 m between bumpers");
        }

        [Test]
        public void ATurnAcrossTheOncomingLaneGivesWayThenCommits()
        {
            TrafficSim sim = Make(2, 0);
            Vehicle turner = Lone(sim, 0, 1, 0, 4, 3, 12, 8);
            turner.Turn = Manoeuvre.CrossingTurn;
            turner.Ndx = 0; turner.Ndz = 1;
            // an oncoming vehicle already close to the junction on its approach
            Vehicle oncoming = Lone(sim, 0, -1, 0, 4, 3, 8, 8, slot: 1);
            Run(sim, 0.2);
            Assert.AreEqual(0, turner.Leg, "still approaching: the oncoming vehicle has the junction");
            oncoming.Active = false;
            sim.Vehicles[1].X = 9999;   // out of the way (and it will be put back where it can, far from the turner)
            Run(sim, 4);
            Assert.IsTrue(turner.Leg == 1 || turner.Dz == 1, "now it turns");
        }

        // ---------------------------------------------------------------- turn choice

        [Test]
        public void TurnChoiceFavoursGoingStraightOnAndNeverLeavesTheGrid()
        {
            TrafficSim sim = Make(96, 0);
            var seen = new Dictionary<Manoeuvre, int>();
            var last = new Dictionary<Vehicle, (int ix, int iz)>();
            for (int step = 0; step < 60 * 600; step++)
            {
                sim.Step(Dt, 0, 0, 0, 0);
                foreach (Vehicle v in sim.Vehicles)
                {
                    if (!v.Active) continue;
                    Assert.IsTrue(v.Ix >= 0 && v.Ix < Nav.XLines.Count && v.Iz >= 0 && v.Iz < Nav.ZLines.Count, "heading for a junction that exists");
                    if (last.TryGetValue(v, out var l) && l.ix == v.Ix && l.iz == v.Iz) continue;
                    last[v] = (v.Ix, v.Iz);
                    seen[v.Turn] = seen.TryGetValue(v.Turn, out int n) ? n + 1 : 1;
                }
            }
            int total = seen.Values.Sum();
            double straight = seen[Manoeuvre.Straight] / (double)total;
            Assert.Greater(total, 300, "plenty of choices were made");
            Assert.That(straight, Is.InRange(0.45, 0.72), "straight on is 3.0 against 1.05 and 0.95 where all three are open");
            Assert.Greater(seen[Manoeuvre.Turn], 0);
            Assert.Greater(seen[Manoeuvre.CrossingTurn], 0);
        }

        // ---------------------------------------------------------------- the prototype's faults, mended in the city rules

        [Test]
        public void AVehicleWaitingAtASignalDoesNotSwingRoundToFaceItsWaypoint()
        {
            double Swing(TrafficRules rules)
            {
                TrafficSim sim = Make(1, 0, rules: rules);
                // along Z on red, standing a few centimetres past its waypoint, where the prototype's keeps steering back for it
                Vehicle v = Lone(sim, 0, 0, 1, 3, 3, 0, 0);
                v.Z = v.WpZ + 0.2; v.X = v.WpX + 0.1;
                double h = v.Heading;
                Run(sim, 5);
                Assert.AreEqual(0, v.Speed);
                return Math.Abs(CrowdSim.ShortestAngle(h, v.Heading));
            }
            Assert.Less(Swing(TrafficRules.City), 1e-9, "it stays as it was");
            Assert.Greater(Swing(TrafficRules.Prototype), 1.0, "the prototype's swings right round to face the waypoint behind it");
        }

        [Test]
        public void NobodyDrivesOffTheRoadAcrossTheBlocks()
        {
            TrafficSim sim = Make(96, 0);
            var strays = new HashSet<int>();
            string first = "";
            for (int s = 0; s < 240; s++)
            {
                Run(sim, 1);
                foreach (Vehicle v in sim.Vehicles)
                {
                    if (!v.Active) continue;
                    IReadOnlyList<double> across = v.Dx != 0 ? Nav.ZLines : Nav.XLines;
                    IReadOnlyList<double> along = v.Dx != 0 ? Nav.XLines : Nav.ZLines;
                    double lateral = v.Dx != 0 ? v.Z : v.X, longitudinal = v.Dx != 0 ? v.X : v.Z;
                    if (along.Min(l => Math.Abs(l - longitudinal)) < 16) continue;
                    if (across.Min(l => Math.Abs(lateral - l)) > 8 && strays.Add(v.Index) && first == "") first = $"vehicle {v.Index} at {v.X:0.0}, {v.Z:0.0} after {s} s";
                }
            }
            Assert.AreEqual(0, strays.Count, $"vehicles seen off the road in 4 minutes ({first})");
        }

        [Test]
        public void ThePrototypesRulesAreTheDefaultAndTheCityRulesMendThree()
        {
            Assert.AreSame(TrafficRules.Prototype, TrafficSim.Create(Nav, 0, 0, 4, 0).Rules);
            Assert.IsFalse(TrafficRules.Prototype.HoldHeadingNearWaypoint);
            Assert.IsFalse(TrafficRules.Prototype.ProgressAlongTheRoad);
            Assert.IsFalse(TrafficRules.Prototype.StopForPlayerLikePeople);
            Assert.IsTrue(TrafficRules.City.HoldHeadingNearWaypoint);
            Assert.IsTrue(TrafficRules.City.ProgressAlongTheRoad);
            Assert.IsTrue(TrafficRules.City.StopForPlayerLikePeople);
        }

        // ---------------------------------------------------------------- the player

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ThePlayerStandingInTheRoadIsAHardStopForEveryKindOfVehicle(int typeIndex)
        {
            TrafficSim sim = Make(1, 0);
            Vehicle v = Lone(sim, typeIndex, 1, 0, 4, 3, 60, VehicleCatalog.Ground[typeIndex].CruiseMax);
            // stand in the lane, 25 m ahead of the vehicle's nose
            double px = v.X + v.HalfLength + 25, pz = v.Z;
            for (int i = 0; i < 60 * 12; i++)
            {
                sim.Step(Dt, px, pz, px, pz);
                Assert.Less(v.X + v.HalfLength, px - 0.5, $"never reaches them (step {i})");
            }
            Assert.AreEqual(0, v.Speed, "stopped");
            Assert.That(px - (v.X + v.HalfLength), Is.InRange(2.0, 3.5), "about 3 m short of them");
        }

        [Test]
        public void ThePrototypesPlayerRuleLetsTheFastestRunTheirNoseUpToThem()
        {
            TrafficSim sim = Make(1, 0, rules: TrafficRules.Prototype);
            Vehicle v = Lone(sim, 3, 1, 0, 4, 3, 60, VehicleCatalog.Ground[3].CruiseMax);
            double px = v.X + v.HalfLength + 25, pz = v.Z;
            double closest = double.PositiveInfinity;
            for (int i = 0; i < 60 * 12; i++)
            {
                sim.Step(Dt, px, pz, px, pz);
                closest = Math.Min(closest, px - (v.X + v.HalfLength));
            }
            Assert.Less(closest, 1.0, "the bike gets within a metre of them: it begins to brake only 14 m out");
        }

        // ---------------------------------------------------------------- people on the road

        [TestCase(0, 25.0)]
        [TestCase(1, 25.0)]
        [TestCase(2, 25.0)]
        [TestCase(3, 25.0)]
        [TestCase(0, 45.0)]
        [TestCase(3, 45.0)]
        public void EveryKindOfVehicleStopsShortOfSomeoneInItsLane(int typeIndex, double distance)
        {
            TrafficSim sim = Make(1, 0);
            Vehicle v = Lone(sim, typeIndex, 1, 0, 4, 3, 60, v_cruise(typeIndex));
            double nose = v.X + v.HalfLength;
            var people = new Walkers();
            people.List.Add(new Pedestrian { X = nose + distance, Z = v.Z, Y = 0 });
            double stoppedAt = double.NaN;
            for (int i = 0; i < 60 * 15; i++)
            {
                sim.Step(Dt, 0, 0, 0, 0, people);
                Assert.Less(v.X + v.HalfLength, people.List[0].X - 0.5, $"never reaches them (step {i})");
                if (v.Speed == 0) { stoppedAt = people.List[0].X - (v.X + v.HalfLength); break; }
            }
            Assert.IsFalse(double.IsNaN(stoppedAt), "it came to a stop");
            Assert.That(stoppedAt, Is.InRange(2.0, 3.5), "about 3 m short");
        }

        static double v_cruise(int typeIndex) => VehicleCatalog.Ground[typeIndex].CruiseMax;

        [Test]
        public void SomeoneToTheSideOrBehindDoesNotStopAVehicle()
        {
            foreach ((double ahead, double side) in new[] { (20.0, 4.5), (-8.0, 0.0), (20.0, -4.5) })
            {
                TrafficSim sim = Make(1, 0);
                Vehicle v = Lone(sim, 0, 1, 0, 4, 3, 60, 12);
                var people = new Walkers();
                people.List.Add(new Pedestrian { X = v.X + ahead, Z = v.Z + side });
                Run(sim, 1.5, 0, 0, people);
                Assert.Greater(v.Speed, 10, $"{ahead} m ahead, {side} m to the side");
            }
        }

        [Test]
        public void SomeoneFarAheadIsNotYetAReasonToSlow()
        {
            TrafficSim sim = Make(1, 0);
            Vehicle v = Lone(sim, 0, 1, 0, 4, 3, 60, 12);
            var people = new Walkers();
            people.List.Add(new Pedestrian { X = v.X + 50, Z = v.Z });
            Run(sim, 0.5, 0, 0, people);
            Assert.GreaterOrEqual(v.Speed, 12, "no braking: it is speeding up to its cruise");
        }

        [Test]
        public void WithNoPeopleTheQueryChangesNothing()
        {
            TrafficSim a = Make(96, 36), b = Make(96, 36);
            var none = new Walkers();
            for (int i = 0; i < 60 * 30; i++)
            {
                a.Step(Dt, 0, 0, 0, 0);
                b.Step(Dt, 0, 0, 0, 0, none);
            }
            for (int i = 0; i < a.Vehicles.Count; i++)
            {
                Assert.AreEqual(a.Vehicles[i].X, b.Vehicles[i].X, $"vehicle {i}");
                Assert.AreEqual(a.Vehicles[i].Heading, b.Vehicles[i].Heading);
            }
        }

        // ---------------------------------------------------------------- looking both ways

        /// <summary>The crossing of the road along X at row <paramref name="row"/>, mid-block at column <paramref name="column"/>: the walker goes along Z from one kerb to the other.</summary>
        static (double fromX, double fromZ, double toX, double toZ) CrossingOfRoadAlongX(int row, double x)
        {
            double centre = Nav.ZLines[row], half = Nav.AsphaltHalf + 1.2;
            return (x, centre - half, x, centre + half);
        }

        [Test]
        public void AnEmptyRoadIsNotBusy()
        {
            TrafficSim sim = Make(0, 0);
            (double fx, double fz, double tx, double tz) = CrossingOfRoadAlongX(3, 0);
            Assert.IsFalse(sim.RoadBusy(fx, fz, tx, tz, Crossing.X, 1.3));
        }

        [Test]
        public void ThereIsNothingToWaitForAtAnEdgeThatIsNotACrossing()
        {
            TrafficSim sim = Make(96, 0);
            Assert.IsFalse(sim.RoadBusy(0, 0, 10, 10, Crossing.None, 1.3));
        }

        [TestCase(60.0, 10.0, true, "arrives in 4 s: well inside the 12 s the walker needs")]
        [TestCase(150.0, 10.0, true, "speeding up from 10 to 16.5 m/s, it covers 150 m in 9.6 s: before they are across and clear")]
        [TestCase(300.0, 10.0, false, "it needs 18 s: they are across by then")]
        [TestCase(30.0, 3.0, true, "slow, but speeding up: under 4 s")]
        [TestCase(30.0, 0.0, true, "standing, but its light is green (the cycle has just begun): it sets off at once")]
        [TestCase(1.0, 0.0, true, "standing right on the line: in the way")]
        [TestCase(-25.0, 12.0, false, "already past and going away")]
        public void AMovingVehicleMakesTheRoadBusyIfItWouldArriveBeforeTheWalkerIsAcrossAndClear(double distanceBefore, double speed, bool busy, string why)
        {
            TrafficSim sim = Make(1, 0);
            double x0 = 0;
            (double fx, double fz, double tx, double tz) = CrossingOfRoadAlongX(3, x0);
            // along X, in its lane on the road at row 3, distanceBefore metres short of the walker's line
            Vehicle v = Lone(sim, 0, 1, 0, 4, 3, 60, speed);
            v.X = x0 - distanceBefore - v.HalfLength;
            Assert.AreEqual(busy, sim.RoadBusy(fx, fz, tx, tz, Crossing.X, 1.3), why);
        }

        [Test]
        public void AVehicleWaitingAtARedLightIsNoThreatUntilItsLightIsAboutToTurn()
        {
            TrafficSim sim = Make(1, 0);
            (double fx, double fz, double tx, double tz) = CrossingOfRoadAlongX(3, 0);
            Vehicle v = Lone(sim, 0, 1, 0, 4, 3, 60, 0);
            v.X = -40 - v.HalfLength;
            // traffic along X: red from 15.1 s to 30.2 s of the cycle
            AdvanceClock(sim.Signal, 16);
            Assert.AreEqual(1, sim.Signal.GreenAxis);
            Assert.AreEqual(14.2, sim.Signal.TimeUntilGreen(0), 0.05);
            Assert.IsFalse(sim.RoadBusy(fx, fz, tx, tz, Crossing.X, 1.3), "14 s to green, then 4 s to reach the line: after the walker is across");
            AdvanceClock(sim.Signal, 10);
            Assert.IsTrue(sim.RoadBusy(fx, fz, tx, tz, Crossing.X, 1.3), "4 s to green: it would be there as the walker is half across");
        }

        [Test]
        public void TheTimeUntilGreenFollowsTheCycle()
        {
            var clock = new SignalClock();
            Assert.AreEqual(0, clock.TimeUntilGreen(0));
            Assert.AreEqual(15.1, clock.TimeUntilGreen(1), 1e-9);
            AdvanceClock(clock, 13);   // X is on amber, 0.5 s in
            Assert.AreEqual(0, clock.TimeUntilGreen(0), "amber still counts as go for X");
            Assert.AreEqual(2.1, clock.TimeUntilGreen(1), 0.02);
            AdvanceClock(clock, 3);   // Z is green, 0.9 s in
            Assert.AreEqual(0, clock.TimeUntilGreen(1));
            Assert.AreEqual(12.5 - 0.9 + 2.6, clock.TimeUntilGreen(0), 0.05);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.TimeUntilGreen(2));
        }

        [Test]
        public void AVehicleComingTheOtherWayCountsToo()
        {
            TrafficSim sim = Make(1, 0);
            (double fx, double fz, double tx, double tz) = CrossingOfRoadAlongX(3, 0);
            Vehicle v = Lone(sim, 0, -1, 0, 3, 3, 60, 10);
            v.X = 80;
            Assert.IsTrue(sim.RoadBusy(fx, fz, tx, tz, Crossing.X, 1.3), "80 m away going toward it at 10 m/s");
            v.X = -30;
            Assert.IsFalse(sim.RoadBusy(fx, fz, tx, tz, Crossing.X, 1.3), "30 m past it, going away");
        }

        [Test]
        public void VehiclesOnOtherRoadsOrAcrossTheCrossingDoNotCount()
        {
            TrafficSim sim = Make(1, 0);
            (double fx, double fz, double tx, double tz) = CrossingOfRoadAlongX(3, 0);
            Vehicle v = Lone(sim, 0, 1, 0, 4, 3, 60, 10);
            v.X = -40; v.Z = Nav.ZLines[4] + sim.Lane;   // the next road along
            Assert.IsFalse(sim.RoadBusy(fx, fz, tx, tz, Crossing.X, 1.3), "another road");
            Lone(sim, 0, 0, 1, 3, 4, 60, 10);   // a vehicle on a road that runs across the walker's, travelling along Z
            sim.Vehicles[0].X = 0; sim.Vehicles[0].Z = fz - 20;
            Assert.IsFalse(sim.RoadBusy(fx, fz, tx, tz, Crossing.X, 1.3), "it is travelling along Z, not along X");
        }

        [Test]
        public void AFasterWalkerNeedsAShorterGap()
        {
            TrafficSim sim = Make(1, 0);
            (double fx, double fz, double tx, double tz) = CrossingOfRoadAlongX(3, 0);
            Vehicle v = Lone(sim, 0, 1, 0, 4, 3, 60, 10);
            v.X = -150;
            Assert.IsTrue(sim.RoadBusy(fx, fz, tx, tz, Crossing.X, 1.2), "a stroller needs 13 s; it covers 150 m in about 9 s");
            Assert.IsFalse(sim.RoadBusy(fx, fz, tx, tz, Crossing.X, 3.0), "someone running across needs 6.5 s");
        }

        [Test]
        public void TheOtherAxisIsHandledToo()
        {
            TrafficSim sim = Make(1, 0);
            double centre = Nav.XLines[3], half = Nav.AsphaltHalf + 1.2;
            Vehicle v = Lone(sim, 0, 0, 1, 3, 4, 60, 10);   // along Z on the road at column 3
            v.Z = -80;
            Assert.IsTrue(sim.RoadBusy(centre - half, 0, centre + half, 0, Crossing.Z, 1.3), "a vehicle along Z, 80 m short of a walker crossing along X");
            Assert.IsFalse(sim.RoadBusy(centre - half, 0, centre + half, 0, Crossing.X, 1.3), "but not for a walker crossing a road that carries X traffic");
        }

        // ---------------------------------------------------------------- pooling

        [Test]
        public void LoweringTheBudgetRetiresVehiclesOutOfViewAndRaisingItBringsThemBack()
        {
            TrafficSim sim = Make(96, 36);
            sim.SetBudget(18, 5);
            Run(sim, 0.5);
            Assert.AreEqual(18 + CountNearCamera(sim, 18), sim.Vehicles.Count, 3, "down to the budget, except anyone within 20 m of the camera");
            Assert.AreEqual(5, sim.Airborne.Count);

            sim.SetBudget(96, 36);
            Run(sim, 0.5);
            Assert.AreEqual(96, sim.Vehicles.Count);
            Assert.AreEqual(36, sim.Airborne.Count);
        }

        static int CountNearCamera(TrafficSim sim, int budget) => Math.Max(0, sim.Vehicles.Count(v => v.X * v.X + v.Z * v.Z < 400) - 0);

        [Test]
        public void NobodyInViewIsRetired()
        {
            TrafficSim sim = Make(96, 36);
            sim.InView = (x, y, z, r) => true;   // everything is in view
            sim.SetBudget(18, 5);
            Run(sim, 2);
            Assert.AreEqual(96, sim.Vehicles.Count, "all in view: none go");
            Assert.AreEqual(36, sim.Airborne.Count);
            sim.InView = null;
            Run(sim, 2);
            Assert.Less(sim.Vehicles.Count, 40, "out of view they go");
        }

        [Test]
        public void SomeoneWhoWouldAppearInViewIsNotIntroduced()
        {
            TrafficSim sim = Make(18, 5);
            sim.InView = (x, y, z, r) => true;
            sim.SetBudget(96, 36);
            Run(sim, 2);
            Assert.AreEqual(18, sim.Vehicles.Count, "every one would pop into view: none are introduced");
        }

        [Test]
        public void EachTypeKeepsItsShareWhenTheBudgetChanges()
        {
            TrafficSim sim = Make(96, 36);
            sim.SetBudget(30, 12);
            Run(sim, 3);
            int[] expected = FleetBudget.Distribute(30, VehicleCatalog.GroundShares());
            for (int ti = 0; ti < 4; ti++) Assert.AreEqual(expected[ti], sim.Vehicles.Count(v => v.TypeIndex == ti), 2, VehicleCatalog.Ground[ti].Key);
        }

        // ---------------------------------------------------------------- recycling

        [Test]
        public void VehiclesLeftBehindArePutBackRoundThePlayer()
        {
            TrafficSim sim = Make(96, 0, 0, 0);
            // the player moves to a far corner: vehicles more than 250 m from them are put back, 120 to 215 m from them; the rest stay where they are
            double px = 190, pz = 190;
            var left = sim.Vehicles.Where(v => v.Active && Math.Sqrt((v.X - px) * (v.X - px) + (v.Z - pz) * (v.Z - pz)) > TrafficSim.CullRadius).ToList();
            Assert.Greater(left.Count, 10, "plenty are left behind");
            sim.Step(0, px, pz, px, pz);
            foreach (Vehicle v in left)
            {
                if (!v.Active) continue;
                double d = Math.Sqrt((v.X - px) * (v.X - px) + (v.Z - pz) * (v.Z - pz));
                Assert.That(d, Is.InRange(TrafficSim.SpawnMin, TrafficSim.SpawnMax), $"vehicle {v.Index}");
            }
            Assert.Greater(left.Count(v => v.Active), left.Count / 2, "most find a place");
        }

        [Test]
        public void WithNoRoadNearThePlayerVehiclesWaitUnusedAndReturnWhenThereIsOne()
        {
            TrafficSim sim = Make(40, 0);
            for (int i = 0; i < 5; i++) sim.Step(Dt, 5000, 5000, 5000, 5000);
            Assert.AreEqual(0, sim.Vehicles.Count(v => v.Active), "none can be placed 5 km from any road");
            foreach (Vehicle v in sim.Vehicles) Assert.AreEqual(-900, v.Y, "parked far below");
            for (int i = 0; i < 20; i++) sim.Step(Dt, 0, 0, 0, 0);
            Assert.Greater(sim.Vehicles.Count(v => v.Active), 20, "back in the city they return");
        }

        // ---------------------------------------------------------------- colliders

        [Test]
        public void TheNearestVehiclesWithinRangeAreFoundNearestFirst()
        {
            TrafficSim sim = Make(5, 0);
            for (int i = 0; i < 5; i++)
            {
                Vehicle v = sim.Vehicles[i];
                v.Active = true; v.X = 1000 + 3 * (4 - i); v.Z = 0;
            }
            var into = new Vehicle[3];
            int n = sim.NearestToPlayer(1000, 0, into);
            Assert.AreEqual(3, n, "as many as the array holds");
            CollectionAssert.AreEqual(new[] { sim.Vehicles[4], sim.Vehicles[3], sim.Vehicles[2] }, into);
            Assert.AreEqual(0, sim.NearestToPlayer(0, 0, into), "nobody near");
            sim.Vehicles[4].X = 1000 + TrafficSim.ProxyRange + 0.5;
            Assert.AreEqual(4, sim.NearestToPlayer(1000, 0, new Vehicle[8]), "one is beyond the 20 m range");
        }

        [Test]
        public void TiesKeepTheirOrder()
        {
            TrafficSim sim = Make(3, 0);
            for (int i = 0; i < 3; i++) { sim.Vehicles[i].Active = true; sim.Vehicles[i].X = 2000; sim.Vehicles[i].Z = 5; }
            var into = new Vehicle[3];
            Assert.AreEqual(3, sim.NearestToPlayer(2000, 0, into));
            CollectionAssert.AreEqual(new[] { sim.Vehicles[0], sim.Vehicles[1], sim.Vehicles[2] }, into);
        }

        // ---------------------------------------------------------------- aircraft

        [Test]
        public void AircraftFlyInTheBandsAlongTheCanyonsOrFree()
        {
            TrafficSim sim = Make(0, 36);
            var bands = new[] { 24.0, 33, 45, 62, 88, 118 };
            foreach (Aircraft a in sim.Airborne)
            {
                Assert.IsTrue(bands.Any(b => a.Y >= b && a.Y < b + 6), $"aircraft {a.Index} at {a.Y}");
                Assert.That(a.Speed, Is.InRange(a.Type.SpeedMin, a.Type.SpeedMax));
                Assert.AreEqual(1.0, Math.Sqrt(a.DirX * a.DirX + a.DirZ * a.DirZ), 1e-9);
                if (a.Y < 70)
                {
                    // a canyon run: along a road, its line within a lane of the road's middle
                    bool alongX = a.DirX != 0;
                    Assert.AreEqual(0, alongX ? a.DirZ : a.DirX, "straight along an axis");
                    IReadOnlyList<double> lines = alongX ? Nav.ZLines : Nav.XLines;
                    double across = alongX ? a.Z : a.X;
                    Assert.LessOrEqual(lines.Min(l => Math.Abs(l - across)), Nav.AsphaltHalf * 0.5 + 1e-9, $"aircraft {a.Index} threads a road");
                }
            }
            Assert.IsTrue(sim.Airborne.Any(a => a.Y < 70) && sim.Airborne.Any(a => a.Y >= 70), "some low, some high");
        }

        [Test]
        public void AircraftFlyStraightAndAreSentRoundAgainWhenTheyLeave()
        {
            TrafficSim sim = Make(0, 36);
            Aircraft a = sim.Airborne[0];
            double x0 = a.X, z0 = a.Z, y0 = a.Y, speed = a.Speed;
            sim.Step(1.0, 0, 0, 0, 0);
            Assert.AreEqual(x0 + a.DirX * speed, a.X, 1e-9);
            Assert.AreEqual(z0 + a.DirZ * speed, a.Z, 1e-9);
            Assert.AreEqual(y0, a.Y, "level flight");
            Assert.AreEqual(Math.Atan2(a.DirX, a.DirZ), a.Heading, 1e-9);

            a.X = 1000; a.Z = 0;
            sim.Step(Dt, 0, 0, 0, 0);
            double d = Math.Sqrt(a.X * a.X + a.Z * a.Z);
            Assert.LessOrEqual(d, TrafficSim.AircraftReach + 100, "back round the player");
            Assert.AreEqual(a.X, a.PrevX, 1e-9, "and not interpolated across the city");
        }

        [Test]
        public void AircraftBankGentlyAndKeepTheirHeading()
        {
            TrafficSim sim = Make(0, 10);
            double maxBank = 0;
            for (int s = 0; s < 60; s++)
            {
                Run(sim, 1);
                foreach (Aircraft a in sim.Airborne) maxBank = Math.Max(maxBank, Math.Abs(a.Bank));
            }
            Assert.That(maxBank, Is.InRange(0.01, 0.1001));
        }

        // ---------------------------------------------------------------- interpolation

        [Test]
        public void EachStepRemembersTheLastForInterpolation()
        {
            TrafficSim sim = Make(20, 5);
            Run(sim, 1);
            double[] x = sim.Vehicles.Select(v => v.X).ToArray();
            sim.Step(Dt, 0, 0, 0, 0);
            for (int i = 0; i < x.Length; i++) Assert.AreEqual(x[i], sim.Vehicles[i].PrevX, 1e-12, $"vehicle {i}");
        }

        [Test]
        public void AFrozenStepMovesNobody()
        {
            TrafficSim sim = Make(40, 10);
            Run(sim, 1);
            double[] x = sim.Vehicles.Select(v => v.X).ToArray();
            sim.Step(0, 0, 0, 0, 0);
            for (int i = 0; i < x.Length; i++) Assert.AreEqual(x[i], sim.Vehicles[i].X, 1e-12, $"vehicle {i}");
        }
    }
}
