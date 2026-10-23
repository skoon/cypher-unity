using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The crowd's rules around roads: the prototype's preset, the game's crossing bias, and the list of people on the asphalt the traffic stops for.</summary>
    public class CrowdRulesTests
    {
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate());
        const double Dt = 1.0 / 60;

        sealed class Busy : ITrafficQuery
        {
            public int Asked;
            public double FromX, FromZ, ToX, ToZ, WalkSpeed;
            public Crossing Kind;

            public bool RoadBusy(double fromX, double fromZ, double toX, double toZ, Crossing crossing, double walkSpeed)
            {
                Asked++;
                FromX = fromX; FromZ = fromZ; ToX = toX; ToZ = toZ; Kind = crossing; WalkSpeed = walkSpeed;
                return false;
            }
        }

        // ---------------------------------------------------------------- the rules themselves

        [Test]
        public void ThePresetsHoldTheValuesTheyAreNamedFor()
        {
            Assert.AreEqual(0.45, CrowdRules.Prototype.CrossingWeight, "the prototype's weight on a crossing");
            Assert.IsFalse(CrowdRules.Prototype.NoIdleOnRoad);
            Assert.IsFalse(CrowdRules.Prototype.LookBeforeStepping);
            Assert.AreEqual(2.0, CrowdRules.City.CrossingWeight);
            Assert.IsTrue(CrowdRules.City.NoIdleOnRoad);
            Assert.IsTrue(CrowdRules.City.LookBeforeStepping);
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void ABadWeightFailsLoudly(double weight)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CrowdRules(weight, false));
        }

        [Test]
        public void ACrowdPlaysByThePrototypesRulesUnlessToldOtherwise()
        {
            Assert.AreSame(CrowdRules.Prototype, CrowdSim.Create(Nav, 4, 4, 0, 0).Rules);
            Assert.AreSame(CrowdRules.City, CrowdSim.Create(Nav, 4, 4, 0, 0, rules: CrowdRules.City).Rules);
        }

        // ---------------------------------------------------------------- how often people cross

        /// <summary>Everyone stood on a kerb-edge midpoint of one of the nine blocks round the plaza, having just walked along the pavement, about to choose the way on.</summary>
        static void ArriveAlongThePavement(CrowdSim sim)
        {
            List<int> near = Enumerable.Range(0, Nav.Blocks.Count).Where(bi => Math.Abs(Nav.Blocks[bi].Cx) < 100 && Math.Abs(Nav.Blocks[bi].Cz) < 100).ToList();
            int[] midpoints = { 1, 3, 5, 7 };
            for (int n = 0; n < sim.Active.Count; n++)
            {
                Pedestrian p = sim.Active[n];
                int bi = near[n % near.Count], k = midpoints[n % 4];
                int node = bi * 8 + k, next = bi * 8 + (k + 1) % 8;
                p.Node = node;
                p.PrevNode = bi * 8 + (k + 7) % 8;
                p.X = Nav.NodeX(node); p.Z = Nav.NodeZ(node); p.Y = Nav.WalkY;
                p.Heading = Math.Atan2(Nav.NodeX(next) - p.X, Nav.NodeZ(next) - p.Z);
                p.Tx = p.X; p.Tz = p.Z; p.Lat = 0; p.Crossing = Crossing.None;
                p.State = PedState.Walk; p.Timer = 100; p.WaitTimer = 0; p.Speed = p.Cruise;
            }
        }

        static double ShareThatCross(CrowdRules rules, int rounds)
        {
            CrowdSim sim = CrowdSim.Create(Nav, 400, 400, 0, 0, rules: rules);
            int crossed = 0, arrivals = 0;
            for (int r = 0; r < rounds; r++)
            {
                ArriveAlongThePavement(sim);
                sim.Step(Dt, 0, 0, 5000, 5000);
                foreach (Pedestrian p in sim.Active)
                {
                    arrivals++;
                    if (p.Crossing != Crossing.None) crossed++;
                }
            }
            return crossed / (double)arrivals;
        }

        [Test]
        public void WithThePrototypesWeightWalkingOnAlwaysBeatsACrossing()
        {
            Assert.AreEqual(0.0, ShareThatCross(CrowdRules.Prototype, 10), "4,000 arrivals and not one crossing: 0.196 at best against 0.55 at least");
        }

        [TestCase(2.0, 0.08, 0.13)]
        [TestCase(3.0, 0.34, 0.44)]
        [TestCase(4.0, 0.62, 0.74)]
        public void TheCrossingWeightSetsHowOftenAnArrivalCrosses(double weight, double least, double most)
        {
            double share = ShareThatCross(new CrowdRules(weight, false), 10);
            Assert.That(share, Is.InRange(least, most), $"weight {weight}");
        }

        [Test]
        public void TheCityRulesPutAFewPeopleOnTheRoadAndThePrototypesAlmostNone()
        {
            double Average(CrowdRules rules)
            {
                CrowdSim sim = CrowdSim.Create(Nav, 110, 110, 0, 0, rules: rules);
                long onRoad = 0;
                const int seconds = 300;
                for (int s = 0; s < seconds; s++)
                {
                    for (int i = 0; i < 60; i++) sim.Step(Dt, 0, 0, 0, 0);
                    onRoad += sim.OnRoad.Count(p => p.Crossing != Crossing.None);
                }
                return onRoad / (double)seconds;
            }

            double city = Average(CrowdRules.City), prototype = Average(CrowdRules.Prototype);
            Console.WriteLine($"CROWDROAD on the road, on average, of 110: city rules {city:0.00}, prototype's {prototype:0.00}");
            Assert.That(city, Is.InRange(1.0, 8.0), "a few of 110 at any moment");
            Assert.Less(prototype, 1.5, "the prototype's crowd crosses only when someone has just been put down facing a road");
            Assert.Greater(city, prototype * 3, "the bias is plain to see");
        }

        [Test]
        public void UnderTheCityRulesPeopleStillKeepToThePavementOrACrosswalk()
        {
            CrowdSim sim = CrowdSim.Create(Nav, 120, 120, 0, 0, rules: CrowdRules.City);
            double worst = 0;
            for (int s = 0; s < 90; s++)
            {
                for (int i = 0; i < 60; i++) sim.Step(Dt, 0, 0, 0, 0);
                foreach (Pedestrian p in sim.Active)
                {
                    if (p.Crossing == Crossing.None) worst = Math.Max(worst, Nav.KerbDistance(p.X, p.Z) - Nav.WalkOuter);
                }
            }
            Assert.Less(worst, 1.5, "off the crosswalks nobody strays more than a metre and a half past the kerb");
        }

        [Test]
        public void TheTrafficIsToldWhereTheCrossingGoesAndHowFastTheyWalk()
        {
            var traffic = new Busy();
            // a weight so high that the crossing is chosen every time
            CrowdSim sim = CrowdSim.Create(Nav, 1, 1, 0, 0, rules: new CrowdRules(50, false));
            Pedestrian p = sim.Active[0];
            int block = Nav.Blocks.ToList().FindIndex(b => b.Gx == 3 && b.Gz == 3);
            int node = block * 8 + 3;   // east-edge midpoint: a crosswalk leads east
            int next = block * 8 + 4;
            p.Node = node;
            p.PrevNode = block * 8 + 2;
            p.X = Nav.NodeX(node); p.Z = Nav.NodeZ(node); p.Y = Nav.WalkY;
            p.Heading = Math.Atan2(Nav.NodeX(next) - p.X, Nav.NodeZ(next) - p.Z);
            p.Tx = p.X; p.Tz = p.Z; p.Lat = 0; p.Crossing = Crossing.None;
            p.State = PedState.Walk; p.Timer = 100; p.WaitTimer = 0; p.Speed = p.Cruise;
            double fromX = p.X, fromZ = p.Z;

            sim.Step(Dt, 0, 0, 5000, 5000, traffic);

            Assert.AreEqual(1, traffic.Asked);
            Assert.AreEqual(fromX, traffic.FromX, "from where they stand");
            Assert.AreEqual(fromZ, traffic.FromZ);
            Assert.AreEqual(p.Tx, traffic.ToX, "to where they would land");
            Assert.AreEqual(p.Tz, traffic.ToZ);
            Assert.Greater(traffic.ToX - fromX, 5, "across the road, which runs 11 m or so");
            Assert.AreEqual(Crossing.Z, traffic.Kind, "walking along X over a road carrying Z traffic");
            Assert.AreEqual(p.Cruise, traffic.WalkSpeed);
        }

        // ---------------------------------------------------------------- the road

        static Pedestrian Place(Pedestrian p, double x, double z, double heading, double tx, double tz)
        {
            p.X = x; p.Z = z; p.Y = Nav.WalkY; p.Heading = heading;
            p.Tx = tx; p.Tz = tz;
            p.PrevX = x; p.PrevZ = z; p.PrevY = p.Y; p.PrevHeading = heading;
            p.State = PedState.Walk; p.Timer = 100; p.WaitTimer = 0; p.Speed = p.Cruise;
            return p;
        }

        [Test]
        public void OnRoadListsExactlyThePeopleOnTheAsphalt()
        {
            CrowdSim sim = CrowdSim.Create(Nav, 6, 6, 0, 0);
            double road = Nav.XLines[Nav.XLines.Count / 2];
            var onAsphalt = new List<Pedestrian>();
            for (int i = 0; i < 3; i++)
            {
                onAsphalt.Add(Place(sim.Active[i], road, -20 + 10 * i, 0, road, 40));
                Place(sim.Active[3 + i], 24, -20 + 10 * i, 0, 24, 40);   // 24 m from a block's middle: on the kerb slab
            }
            Assert.Greater(Nav.KerbDistance(road, 0), Nav.WalkOuter, "the test's road is asphalt");
            Assert.LessOrEqual(Nav.KerbDistance(24, 0), Nav.WalkOuter, "and its pavement is pavement");

            sim.Step(0, 0, 0, 5000, 5000);

            CollectionAssert.AreEquivalent(onAsphalt, sim.OnRoad);
        }

        [Test]
        public void SomeoneWhoStepsOffTheRoadLeavesTheList()
        {
            CrowdSim sim = CrowdSim.Create(Nav, 1, 1, 0, 0);
            Pedestrian p = Place(sim.Active[0], Nav.XLines[Nav.XLines.Count / 2], 0, Math.PI / 2, 60, 0);
            p.Cruise = 1.4;
            sim.Step(0, 0, 0, 5000, 5000);
            Assert.AreEqual(1, sim.OnRoad.Count);
            for (int i = 0; i < 60 * 20; i++) sim.Step(Dt, 0, 0, 5000, 5000);
            Assert.AreEqual(0, sim.OnRoad.Count, "the walk to the far pavement is over");
        }

        [Test]
        public void UnderTheCityRulesNobodyStopsToIdleOnTheRoad()
        {
            int city = 0, prototype = 0;
            double road = Nav.XLines[Nav.XLines.Count / 2];
            for (uint seed = 1; seed <= 300; seed++)
            {
                foreach (CrowdRules rules in new[] { CrowdRules.City, CrowdRules.Prototype })
                {
                    CrowdSim sim = CrowdSim.Create(Nav, 1, 1, 0, 0, seed, rules);
                    Pedestrian p = Place(sim.Active[0], road, 0, Math.PI / 2, road + 8, 0);
                    p.Timer = 0;   // their time is up: they either stop or carry on
                    sim.Step(Dt, 0, 0, 5000, 5000);
                    if (p.State == PedState.Idle)
                    {
                        if (ReferenceEquals(rules, CrowdRules.City)) city++;
                        else prototype++;
                    }
                }
            }
            Assert.AreEqual(0, city);
            Assert.Greater(prototype, 30, "the prototype's people stop in the road about one time in five");
        }

        [Test]
        public void OnThePavementTheCityRulesStillLetPeopleIdle()
        {
            int idle = 0;
            for (uint seed = 1; seed <= 300; seed++)
            {
                CrowdSim sim = CrowdSim.Create(Nav, 1, 1, 0, 0, seed, CrowdRules.City);
                Pedestrian p = Place(sim.Active[0], 24, 0, 0, 24, 40);
                p.Timer = 0;
                sim.Step(Dt, 0, 0, 5000, 5000);
                if (p.State == PedState.Idle) idle++;
            }
            Assert.Greater(idle, 30);
        }
    }
}
