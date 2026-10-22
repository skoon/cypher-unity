using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class BenchmarkPilotTests
    {
        const double Dt = 1.0 / 60.0;

        /// <summary>The pilot driving a motor with no collisions at all: just integrates the displacement.</summary>
        sealed class Walker
        {
            public readonly BenchmarkPilot Pilot;
            public readonly PlayerMotor Motor = new PlayerMotor();
            public readonly LookState Look = new LookState();
            public double X, Z, Travelled;
            public double MaxSpeed;

            public Walker(BenchmarkPilot pilot, double x = 0, double z = 0)
            {
                Pilot = pilot;
                X = x;
                Z = z;
            }

            public void Run(double seconds, double dt = Dt)
            {
                int steps = (int)Math.Round(seconds / dt);
                for (int i = 0; i < steps; i++)
                {
                    PlayerCommand cmd = Pilot.Next(X, Z, Look.Yaw, Look.Pitch, dt);
                    Look.Apply(cmd.LookX, cmd.LookY);
                    Vec3 move = Motor.Step(cmd, Look.Yaw, dt, false);
                    X += move.X;
                    Z += move.Z;
                    Travelled += Math.Sqrt(move.X * move.X + move.Z * move.Z);
                    MaxSpeed = Math.Max(MaxSpeed, Motor.PlanarSpeed);
                    Motor.ReportResult(true, false);   // flat ground
                }
            }
        }

        [Test]
        public void DefaultRouteRunsAlongTheRoadCentresOfTheSlice()
        {
            var route = BenchmarkPilot.DefaultRoute;
            Assert.AreEqual(5, route.Count);
            foreach (BenchmarkPilot.Waypoint w in route)
            {
                Assert.IsTrue(CityLayout.IsOnRoad(w.X, w.Z), $"({w.X}, {w.Z}) is not on a road");
            }
            Assert.AreEqual(-31.0, CityLayout.RoadCentre(CityLayout.PlazaIndex));
            Assert.AreEqual(31.0, CityLayout.RoadCentre(CityLayout.PlazaIndex + 1));
            Assert.AreEqual(-93.0, CityLayout.RoadCentre(CityLayout.PlazaIndex - 1));
        }

        [Test]
        public void DefaultRouteStaysInsideTheStreetsOfTheThreeByThreeWindow()
        {
            Rect2 extent = CityWindow.AroundPlaza(1).RoadExtent;
            foreach (BenchmarkPilot.Waypoint w in BenchmarkPilot.DefaultRoute)
            {
                Assert.That(w.X, Is.InRange(extent.X0, extent.X1));
                Assert.That(w.Z, Is.InRange(extent.Z0, extent.Z1));
            }
        }

        [Test]
        public void AFreeWalkerFollowsTheRouteInOrderAndLoops()
        {
            var walker = new Walker(new BenchmarkPilot(BenchmarkPilot.DefaultRoute));
            int lastTarget = walker.Pilot.Target;
            var order = new System.Collections.Generic.List<int> { lastTarget };
            for (int s = 0; s < 60 * 240 && walker.Pilot.Laps < 2; s++)
            {
                walker.Run(Dt);
                if (walker.Pilot.Target != lastTarget)
                {
                    lastTarget = walker.Pilot.Target;
                    order.Add(lastTarget);
                }
            }

            Assert.AreEqual(2, walker.Pilot.Laps, "two laps within four minutes");
            for (int i = 1; i < order.Count; i++) Assert.AreEqual((order[i - 1] + 1) % 5, order[i], "waypoints are visited in order");
        }

        [Test]
        public void ALapTakesAboutAMinuteAndAHalfAndSprintsOnTheLongLegs()
        {
            var walker = new Walker(new BenchmarkPilot(BenchmarkPilot.DefaultRoute));
            for (int s = 0; s < 60 * 200 && walker.Pilot.Laps < 1; s++) walker.Run(Dt);
            Assert.AreEqual(1, walker.Pilot.Laps);
            Assert.That(walker.Pilot.Elapsed, Is.InRange(60.0, 100.0), "one lap of the 372 m route");
            Assert.Greater(walker.MaxSpeed, PlayerMotor.WalkSpeed + 1, "sprints on the long legs");
            Assert.LessOrEqual(walker.MaxSpeed, PlayerMotor.SprintSpeed + 1e-9);
            Assert.That(walker.Travelled, Is.InRange(320.0, 380.0), "from the plaza through all five waypoints, corners cut a little");
        }

        [Test]
        public void ThePathDoesNotDependOnTheFrameRate()
        {
            var fast = new Walker(new BenchmarkPilot(BenchmarkPilot.DefaultRoute));
            var slow = new Walker(new BenchmarkPilot(BenchmarkPilot.DefaultRoute));
            fast.Run(40, 1.0 / 120.0);
            slow.Run(40, 1.0 / 20.0);
            Assert.AreEqual(fast.Pilot.Target, slow.Pilot.Target);
            Assert.AreEqual(fast.X, slow.X, 6.0, "within a few metres after 40 s");
            Assert.AreEqual(fast.Z, slow.Z, 6.0);
        }

        [Test]
        public void ItTurnsTowardTheTargetTheShortWay()
        {
            var route = new[] { new BenchmarkPilot.Waypoint(10, 0, false), new BenchmarkPilot.Waypoint(0, 0, false) };
            var pilot = new BenchmarkPilot(route);
            // Target is due +X (yaw +90 deg) and we face +Z: turn right.
            PlayerCommand right = pilot.Next(0, 0, 0, 0, Dt);
            Assert.Greater(right.LookX, 0);

            // Facing -X (yaw -90 deg) the same target is 180 deg away; either way is fine but it must turn.
            var pilot2 = new BenchmarkPilot(route);
            PlayerCommand around = pilot2.Next(0, 0, -Math.PI / 2, 0, Dt);
            Assert.AreNotEqual(0.0, around.LookX);

            // Past a full turn of accumulated yaw it still takes the short way.
            var pilot3 = new BenchmarkPilot(route);
            PlayerCommand wrapped = pilot3.Next(0, 0, 2 * Math.PI + 0.1, 0, Dt);
            Assert.Greater(wrapped.LookX, 0, "yaw 2pi + 0.1 is just left of +Z, so the +X target is to the right");
        }

        [Test]
        public void TurnRateIsLimitedAndScaledByTheSensitivity()
        {
            var route = new[] { new BenchmarkPilot.Waypoint(0, -50, false), new BenchmarkPilot.Waypoint(0, 50, false) };
            var coarse = new BenchmarkPilot(route, 0.0022);
            var fine = new BenchmarkPilot(route, 0.0011);
            PlayerCommand a = coarse.Next(0, 0, 0, 0, Dt);   // target straight behind: the largest possible turn
            PlayerCommand b = fine.Next(0, 0, 0, 0, Dt);
            Assert.AreEqual(BenchmarkPilot.MaxTurnRate * Dt, Math.Abs(a.LookX) * 0.0022, 1e-12);
            Assert.AreEqual(Math.Abs(a.LookX) * 2, Math.Abs(b.LookX), 1e-9);
        }

        [Test]
        public void ItWalksSlowlyWhileFacingTheWrongWayAndNeverSprintsThen()
        {
            var route = new[] { new BenchmarkPilot.Waypoint(0, -50, true), new BenchmarkPilot.Waypoint(0, 50, true) };
            var pilot = new BenchmarkPilot(route);
            PlayerCommand behind = pilot.Next(0, 0, 0, 0, Dt);
            Assert.Less(behind.Forward, 0.2);
            Assert.Greater(behind.Forward, 0.0);
            Assert.IsFalse(behind.Sprint);

            var ahead = new BenchmarkPilot(new[] { new BenchmarkPilot.Waypoint(0, 50, true), new BenchmarkPilot.Waypoint(0, -50, true) });
            PlayerCommand straight = ahead.Next(0, 0, 0, 0, Dt);
            Assert.AreEqual(1.0, straight.Forward, 1e-12);
            Assert.IsTrue(straight.Sprint);
        }

        [Test]
        public void ViewSweepsBetweenLookingUpAndDownAndStaysInRange()
        {
            var walker = new Walker(new BenchmarkPilot(BenchmarkPilot.DefaultRoute));
            double min = double.MaxValue, max = double.MinValue;
            for (int i = 0; i < 60 * 40; i++)
            {
                walker.Run(Dt);
                min = Math.Min(min, walker.Look.Pitch);
                max = Math.Max(max, walker.Look.Pitch);
            }
            Assert.Greater(max, 0.3, "looks up at the skyline");
            Assert.Less(min, -0.15, "looks down at the street");
            Assert.Less(max, LookState.PitchLimit);
            Assert.Greater(min, -LookState.PitchLimit);
        }

        [Test]
        public void ReachingAWaypointIsCounted()
        {
            var route = new[] { new BenchmarkPilot.Waypoint(1, 0, false), new BenchmarkPilot.Waypoint(50, 0, false) };
            var pilot = new BenchmarkPilot(route);
            pilot.Next(0, 0, 0, 0, Dt);   // already within the arrival radius of waypoint 0
            Assert.AreEqual(1, pilot.WaypointsReached);
            Assert.AreEqual(1, pilot.Target);
            Assert.AreEqual(0, pilot.Laps);
            Assert.AreEqual(Dt, pilot.Elapsed, 1e-12);
        }

        [Test]
        public void BadInputFailsLoudly()
        {
            var one = new[] { new BenchmarkPilot.Waypoint(0, 0, false) };
            Assert.Throws<ArgumentNullException>(() => new BenchmarkPilot(null));
            Assert.Throws<ArgumentException>(() => new BenchmarkPilot(one));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BenchmarkPilot(BenchmarkPilot.DefaultRoute, 0));

            var pilot = new BenchmarkPilot(BenchmarkPilot.DefaultRoute);
            Assert.Throws<ArgumentOutOfRangeException>(() => pilot.Next(0, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => pilot.Next(0, 0, 0, 0, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => pilot.Next(0, 0, 0, 0, double.PositiveInfinity));
        }

        [Test]
        public void TheRouteForANameIsTheRightOne()
        {
            Assert.AreSame(BenchmarkPilot.DefaultRoute, BenchmarkPilot.Route(BenchmarkRoute.Centre));
            Assert.AreSame(BenchmarkPilot.CityRoute, BenchmarkPilot.Route(BenchmarkRoute.City));
            Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkPilot.Route((BenchmarkRoute)99));
        }

        [Test]
        public void EveryLegOfTheCityRouteRunsAlongARoadCorridor()
        {
            var route = BenchmarkPilot.CityRoute;
            for (int i = 0; i < route.Count; i++)
            {
                BenchmarkPilot.Waypoint a = route[i], b = route[(i + 1) % route.Count];
                Assert.IsTrue(CityLayout.IsOnRoad(a.X, a.Z), $"waypoint {i} is not on a road");
                bool alongX = Math.Abs(a.Z - b.Z) < 1e-9, alongZ = Math.Abs(a.X - b.X) < 1e-9;
                Assert.IsTrue(alongX || alongZ, $"leg {i} is diagonal, so it would cut across a block");
                // the whole straight leg must stay in the corridor: sample it
                for (int s = 0; s <= 40; s++)
                {
                    double t = s / 40.0;
                    Assert.IsTrue(CityLayout.IsOnRoad(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t), $"leg {i} leaves the road at {t:0.00}");
                }
            }
        }

        [Test]
        public void TheCityRouteReachesEveryEdgeAndCornerOfTheWholeCity()
        {
            var route = BenchmarkPilot.CityRoute;
            double edge = WorldEdge.CityHalf - CityLayout.Road / 2;
            Assert.IsTrue(route.Any(w => Math.Abs(w.X - edge) < 1e-9 && Math.Abs(w.Z - edge) < 1e-9), "north-east corner");
            Assert.IsTrue(route.Any(w => Math.Abs(w.X - edge) < 1e-9 && Math.Abs(w.Z + edge) < 1e-9), "south-east corner");
            Assert.IsTrue(route.Any(w => Math.Abs(w.X + edge) < 1e-9 && Math.Abs(w.Z + edge) < 1e-9), "south-west corner");
            Assert.IsTrue(route.Any(w => Math.Abs(w.X + edge) < 1e-9 && Math.Abs(w.Z - edge) < 1e-9), "north-west corner");
            Assert.IsTrue(route.All(w => WorldEdge.IsInsidePlayArea(w.X, w.Z)));
        }

        [Test]
        public void AFreeWalkerCompletesTheCityRouteInAboutFiveMinutes()
        {
            var walker = new Walker(new BenchmarkPilot(BenchmarkPilot.CityRoute), CityLayout.RoadCentre(CityLayout.PlazaIndex), CityLayout.RoadCentre(CityLayout.PlazaIndex));
            for (int s = 0; s < 60 * 600 && walker.Pilot.Laps < 1; s++) walker.Run(Dt);
            Assert.AreEqual(1, walker.Pilot.Laps, "a whole lap within ten minutes");
            Assert.That(walker.Pilot.Elapsed, Is.InRange(230.0, 420.0));
            Assert.That(walker.Travelled, Is.InRange(1700.0, 2300.0), "from beside the plaza round every road at the edge and back to the plaza road");
        }
    }
}
