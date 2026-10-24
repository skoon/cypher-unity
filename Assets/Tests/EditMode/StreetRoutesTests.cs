using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class StreetRoutesTests
    {
        static readonly CityPlan Plan = CityPlan.Generate();
        static readonly CityNav Nav = CityNav.Build(Plan);
        static readonly IReadOnlyList<BenchmarkPilot.Waypoint> Ring = StreetRoutes.PlazaRing(Nav);
        static readonly IReadOnlyList<BenchmarkPilot.Waypoint> Junction = StreetRoutes.JunctionLoop(Nav);

        static int NodeOf(BenchmarkPilot.Waypoint w)
        {
            for (int i = 0; i < Nav.NodeCount; i++)
            {
                if (Math.Abs(Nav.NodeX(i) - w.X) < 1e-9 && Math.Abs(Nav.NodeZ(i) - w.Z) < 1e-9) return i;
            }
            return -1;
        }

        static double Length(IReadOnlyList<BenchmarkPilot.Waypoint> route, bool closed = true)
        {
            double sum = 0;
            for (int i = 0; i < route.Count; i++)
            {
                if (i == route.Count - 1 && !closed) break;
                BenchmarkPilot.Waypoint a = route[i], b = route[(i + 1) % route.Count];
                sum += Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
            }
            return sum;
        }

        /// <summary>Whether the nav graph has an edge between two nodes, and whether that edge is a crosswalk.</summary>
        static bool Joined(int a, int b, out bool crosswalk)
        {
            IReadOnlyList<int> n = Nav.Neighbours(a);
            for (int i = 0; i < n.Count; i++)
            {
                if (n[i] == b)
                {
                    crosswalk = Nav.NeighbourCrossings(a)[i] != Crossing.None;
                    return true;
                }
            }
            crosswalk = false;
            return false;
        }

        static void AssertWalkable(string name, IReadOnlyList<BenchmarkPilot.Waypoint> route)
        {
            for (int i = 0; i < route.Count; i++)
            {
                int a = NodeOf(route[i]), b = NodeOf(route[(i + 1) % route.Count]);
                Assert.GreaterOrEqual(a, 0, $"{name} waypoint {i} is a node");
                Assert.IsTrue(Joined(a, b, out _), $"{name}: leg {i} follows an edge, the closing one included");
            }
        }

        [Test]
        public void EveryWaypointOfThePlazaRingIsANodeOfThePedestrianGraphAndEveryLegIsAnEdgeOfIt() => AssertWalkable("plaza ring", Ring);

        [Test]
        public void EveryWaypointOfTheJunctionLoopIsANodeOfThePedestrianGraphAndEveryLegIsAnEdgeOfIt() => AssertWalkable("junction", Junction);

        [Test]
        public void AllWaypointsStandOnTheRingOfPavementRoundTheirBlock()
        {
            foreach (BenchmarkPilot.Waypoint w in Ring.Concat(Junction))
            {
                int node = NodeOf(w);
                NavBlock b = Nav.Blocks[Nav.NodeBlock(node)];
                double chebyshev = Math.Max(Math.Abs(w.X - b.Cx), Math.Abs(w.Z - b.Cz));
                Assert.AreEqual(Nav.RingRadius, chebyshev, 1e-9, "on the middle of the walkable band");
            }
        }

        [Test]
        public void ThePlazaRingCrossesEightRoadsAndIsAboutSevenHundredMetres()
        {
            int crossings = 0;
            for (int i = 0; i < Ring.Count; i++)
            {
                Joined(NodeOf(Ring[i]), NodeOf(Ring[(i + 1) % Ring.Count]), out bool crosswalk);
                if (crosswalk) crossings++;
            }
            Assert.AreEqual(8, crossings);
            // four blocks are crossed side to side (97 m of pavement), four turned round a corner (48.5 m), and eight roads crossed (13.5 m)
            double crossing = Nav.Cell - 2 * Nav.RingRadius;
            Assert.AreEqual(4 * 4 * Nav.RingRadius + 4 * 2 * Nav.RingRadius + 8 * crossing, Length(Ring), 1e-6);
            Assert.That(Length(Ring), Is.InRange(680, 700));
        }

        [Test]
        public void TheJunctionLoopCrossesFourRoadsAndIsAboutTwoHundredAndFiftyMetres()
        {
            int crossings = 0;
            for (int i = 0; i < Junction.Count; i++)
            {
                Joined(NodeOf(Junction[i]), NodeOf(Junction[(i + 1) % Junction.Count]), out bool crosswalk);
                if (crosswalk) crossings++;
            }
            Assert.AreEqual(4, crossings);
            Assert.That(Length(Junction), Is.InRange(245, 255));
        }

        [Test]
        public void BothRoutesStartInTheBlockTheCharacterStandsIn()
        {
            foreach (IReadOnlyList<BenchmarkPilot.Waypoint> route in new[] { Ring, Junction })
            {
                int block = Nav.NodeBlock(NodeOf(route[0]));
                Assert.AreEqual(CityLayout.PlazaIndex, Nav.Blocks[block].Gx);
                Assert.AreEqual(CityLayout.PlazaIndex, Nav.Blocks[block].Gz);
                Assert.Less(Math.Sqrt(route[0].X * route[0].X + route[0].Z * route[0].Z), Nav.RingRadius + 0.01, "at most a short walk across the open plaza from the spawn");
            }
        }

        [Test]
        public void ThePlazaRingStaysInTheMiddleOfTheCity()
        {
            foreach (BenchmarkPilot.Waypoint w in Ring)
            {
                int node = NodeOf(w);
                NavBlock b = Nav.Blocks[Nav.NodeBlock(node)];
                Assert.That(b.Gx, Is.InRange(CityLayout.PlazaIndex, CityLayout.PlazaIndex + 2));
                Assert.That(b.Gz, Is.InRange(CityLayout.PlazaIndex, CityLayout.PlazaIndex + 2));
            }
        }

        [Test]
        public void AboutAThirdOfTheWalkingIsRunAndCrossingsAreAlwaysWalked()
        {
            double run = 0, pavement = 0;
            for (int i = 0; i < Ring.Count; i++)
            {
                BenchmarkPilot.Waypoint from = Ring[i], to = Ring[(i + 1) % Ring.Count];
                Joined(NodeOf(from), NodeOf(to), out bool crosswalk);
                double leg = Math.Sqrt((from.X - to.X) * (from.X - to.X) + (from.Z - to.Z) * (from.Z - to.Z));
                if (crosswalk)
                {
                    Assert.IsFalse(to.Sprint, "a crossing is walked");
                    continue;
                }
                pavement += leg;
                if (to.Sprint) run += leg;
            }
            Assert.That(run / pavement, Is.InRange(0.25, 0.4));
        }

        [Test]
        public void TheJunctionLoopIsWalkedNotRun()
        {
            Assert.IsFalse(Junction.Any(w => w.Sprint));
        }

        [Test]
        public void ARunningStrideOfZeroNeverRunsAndEveryOtherLegRunsHalf()
        {
            var cycle = new[] { (3, 3), (4, 3), (4, 4), (3, 4) };
            Assert.IsFalse(StreetRoutes.Loop(Nav, cycle, 0).Any(w => w.Sprint));
            int ringLegs = StreetRoutes.Loop(Nav, cycle, 1).Count(w => w.Sprint);
            Assert.AreEqual(8, ringLegs, "every pavement leg: two a block");
            Assert.AreEqual(4, StreetRoutes.Loop(Nav, cycle, 2).Count(w => w.Sprint));
        }

        [Test]
        public void CornersAndTheEndsOfCrosswalksWantToBeHitSquarelyAndTheMiddleOfAPavementDoesNot()
        {
            for (int i = 0; i < Ring.Count; i++)
            {
                BenchmarkPilot.Waypoint w = Ring[i];
                int node = NodeOf(w);
                bool corner = node % CityNav.RingNodes % 2 == 0;
                int previous = NodeOf(Ring[(i + Ring.Count - 1) % Ring.Count]), next = NodeOf(Ring[(i + 1) % Ring.Count]);
                Joined(previous, node, out bool arrivesByCrossing);
                Joined(node, next, out bool leavesByCrossing);
                if (corner || arrivesByCrossing || leavesByCrossing) Assert.AreEqual(StreetRoutes.TurnRadius, w.Radius, $"waypoint {i}");
                else Assert.AreEqual(StreetRoutes.PassRadius, w.Radius, $"waypoint {i}");
            }
        }

        [Test]
        public void TheSameRouteComesOutEveryTime()
        {
            IReadOnlyList<BenchmarkPilot.Waypoint> again = StreetRoutes.PlazaRing(CityNav.Build(CityPlan.Generate()));
            Assert.AreEqual(Ring.Count, again.Count);
            for (int i = 0; i < Ring.Count; i++)
            {
                Assert.AreEqual(Ring[i].X, again[i].X, 0);
                Assert.AreEqual(Ring[i].Z, again[i].Z, 0);
                Assert.AreEqual(Ring[i].Sprint, again[i].Sprint);
            }
        }

        [Test]
        public void AWalkThatIsNotAChainOfNeighboursFailsLoudly()
        {
            Assert.Throws<ArgumentException>(() => StreetRoutes.Loop(Nav, new[] { (3, 3), (5, 3), (5, 5), (3, 5) }, 0), "two blocks apart");
            Assert.Throws<ArgumentException>(() => StreetRoutes.Loop(Nav, new[] { (3, 3), (4, 4), (3, 5), (2, 4) }, 0), "diagonal");
            Assert.Throws<ArgumentException>(() => StreetRoutes.Loop(Nav, new[] { (3, 3), (4, 3), (4, 4) }, 0), "too short");
            Assert.Throws<ArgumentException>(() => StreetRoutes.Loop(Nav, new[] { (6, 6), (7, 6), (7, 7), (6, 7) }, 0), "past the edge of the city");
            Assert.Throws<ArgumentOutOfRangeException>(() => StreetRoutes.Loop(Nav, new[] { (3, 3), (4, 3), (4, 4), (3, 4) }, -1));
            Assert.Throws<ArgumentNullException>(() => StreetRoutes.Loop(null, new[] { (3, 3), (4, 3), (4, 4), (3, 4) }, 0));
            Assert.Throws<ArgumentNullException>(() => StreetRoutes.Loop(Nav, null, 0));
        }

        [Test]
        public void TheRoutesForTheStreetAndJunctionNamesAreTheseAndNeedTheGraph()
        {
            Assert.AreEqual(Ring.Count, BenchmarkPilot.Route(BenchmarkRoute.Street, Nav).Count);
            Assert.AreEqual(Junction.Count, BenchmarkPilot.Route(BenchmarkRoute.Junction, Nav).Count);
            Assert.Throws<ArgumentNullException>(() => BenchmarkPilot.Route(BenchmarkRoute.Street));
            Assert.Throws<ArgumentNullException>(() => BenchmarkPilot.Route(BenchmarkRoute.Junction, null));
            Assert.AreSame(BenchmarkPilot.DefaultRoute, BenchmarkPilot.Route(BenchmarkRoute.Centre, Nav), "the others do not mind");
            Assert.AreSame(BenchmarkPilot.CityRoute, BenchmarkPilot.Route(BenchmarkRoute.City));
        }
    }
}
