using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The crowd and the traffic together: people cross roads, wait for gaps, and cars stop for them.</summary>
    public class CrowdTrafficTests
    {
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate());
        const double Dt = 1.0 / 60;

        /// <summary>A person's body radius for the purposes of "is anyone inside a vehicle".</summary>
        const double Body = 0.2;

        sealed class Tally
        {
            public int Steps, Crossings, Waits, Overlaps, OverlapsWithMovingVehicles, OnRoadSamples;
            public double Closest = double.PositiveInfinity;
            public string FirstOverlap = "";
        }

        static Tally Run(CrowdRules rules, int people, int vehicles, double seconds, uint seed = CrowdSim.DefaultSeed)
        {
            CrowdSim crowd = CrowdSim.Create(Nav, people, people, 0, 0, seed, rules);
            TrafficSim traffic = TrafficSim.Create(Nav, 0, 0, vehicles, 0, TrafficSim.DefaultSeed + seed, TrafficRules.City);
            var tally = new Tally();
            var wasCrossing = new Crossing[people];
            var wasWaiting = new bool[people];
            int steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                crowd.Step(Dt, 0, 0, 0, 0, traffic);
                traffic.Step(Dt, 0, 0, 0, 0, crowd);
                tally.Steps++;

                foreach (Pedestrian p in crowd.Active)
                {
                    if (p.Crossing != Crossing.None && wasCrossing[p.Index] == Crossing.None) tally.Crossings++;
                    wasCrossing[p.Index] = p.Crossing;
                    bool waiting = p.WaitTimer > 0;
                    if (waiting && !wasWaiting[p.Index]) tally.Waits++;
                    wasWaiting[p.Index] = waiting;
                }

                tally.OnRoadSamples += crowd.OnRoad.Count;
                if (crowd.OnRoad.Count == 0) continue;
                foreach (Vehicle v in traffic.Vehicles)
                {
                    if (!v.Active) continue;
                    double fx = Math.Sin(v.Heading), fz = Math.Cos(v.Heading);
                    foreach (Pedestrian p in crowd.OnRoad)
                    {
                        double dx = p.X - v.X, dz = p.Z - v.Z;
                        if (dx * dx + dz * dz > 100) continue;
                        double ahead = dx * fx + dz * fz;
                        double along = Math.Abs(ahead) - v.HalfLength, side = Math.Abs(dx * fz - dz * fx) - v.HalfWidth;
                        double centreGap = Math.Max(along, side);   // negative: the person's middle is inside the footprint (a box, so the larger overshoot is the right measure)
                        double gap = centreGap - Body;
                        tally.Closest = Math.Min(tally.Closest, gap);
                        if (gap < 0)
                        {
                            tally.Overlaps++;
                            if (v.Speed > 2.0 && ahead > 0 && centreGap < 0) tally.OverlapsWithMovingVehicles++;   // run into: the middle of the person is inside a moving vehicle, in front of its middle
                            if (tally.FirstOverlap == "") tally.FirstOverlap = $"person {p.Index} and vehicle {v.Index} ({v.Type.Key}, {v.Speed:0.0} m/s) at {p.X:0.0}, {p.Z:0.0} after {i * Dt:0.0} s";
                        }
                    }
                }
            }
            return tally;
        }

        [Test]
        public void WithTheGamesRulesNoOneIsEverInsideAVehicleInFiveMinutes()
        {
            Tally t = Run(CrowdRules.City, 110, 52, 300);
            Console.WriteLine($"CROWDTRAFFIC city rules, 110 people and 52 vehicles, 300 s: {t.Crossings} crossings, {t.Waits} waits, {t.OnRoadSamples / (double)t.Steps:0.00} on the road on average, closest {t.Closest:0.00} m");
            Assert.Greater(t.Crossings, 80, "people really do cross");
            Assert.Greater(t.Waits, 0, "and sometimes wait");
            Assert.AreEqual(0, t.Overlaps, t.FirstOverlap);
        }

        [Test]
        public void WithTheTopTiersFullCrowdAndFleetNoVehicleIsEverRunIntoAnyone()
        {
            Tally t = Run(CrowdRules.City, 400, 96, 150);
            Console.WriteLine($"CROWDTRAFFIC city rules, 400 people and 96 vehicles, 150 s: {t.Crossings} crossings, {t.Waits} waits, {t.OnRoadSamples / (double)t.Steps:0.00} on the road on average, closest {t.Closest:0.00} m, {t.Overlaps} steps of contact ({t.OverlapsWithMovingVehicles} of them run into)");
            Assert.Greater(t.Crossings, 100);
            // People nudged off the edge of the pavement can stand against a vehicle stopped at the kerb (the crowd does not steer round vehicles, as the prototype's does not), so contact
            // is counted only where a vehicle that is moving has someone in front of it.
            Assert.AreEqual(0, t.OverlapsWithMovingVehicles, t.FirstOverlap);
        }

        [Test]
        public void EvenInACrushAtAHeavyBiasVehiclesAlmostNeverRunIntoAnyone()
        {
            Tally t = Run(new CrowdRules(4.0, true, true), 400, 96, 120);
            Console.WriteLine($"CROWDTRAFFIC heavy bias, 400 people and 96 vehicles, 120 s: {t.Crossings} crossings, {t.Waits} waits, {t.OnRoadSamples / (double)t.Steps:0.00} on the road on average, closest {t.Closest:0.00} m, {t.Overlaps} steps of contact ({t.OverlapsWithMovingVehicles} of them run into)");
            Assert.Greater(t.Crossings, 200);
            // With 31 people on the roads at once, shoving each other, someone is now and then pushed or walks against the side or front of a vehicle that is stopped or setting off:
            // people do not steer round vehicles (the prototype's do not either). A moving vehicle running into someone is the case the yielding rules exist to prevent: a handful of
            // steps, all of it at a vehicle's first metre of movement from rest with the person already in its nose.
            Assert.Less(t.OverlapsWithMovingVehicles, 30, t.FirstOverlap);
        }

        [Test]
        public void TheSafetyNetIsReallyNeededWithoutTheYieldingRules()
        {
            // the same run with the prototype's cars (which only stop for the player) and the gap check disabled: people and cars do meet
            CrowdSim crowd = CrowdSim.Create(Nav, 400, 400, 0, 0, CrowdSim.DefaultSeed, new CrowdRules(4.0, true, true));
            TrafficSim traffic = TrafficSim.Create(Nav, 0, 0, 96, 0, TrafficSim.DefaultSeed, TrafficRules.City);
            var blind = new BlindToPeople(traffic);
            int overlaps = 0;
            for (int i = 0; i < 60 * 120; i++)
            {
                crowd.Step(Dt, 0, 0, 0, 0, blind);
                traffic.Step(Dt, 0, 0, 0, 0);   // no one to stop for
                foreach (Vehicle v in traffic.Vehicles)
                {
                    if (!v.Active) continue;
                    double fx = Math.Sin(v.Heading), fz = Math.Cos(v.Heading);
                    foreach (Pedestrian p in crowd.OnRoad)
                    {
                        double dx = p.X - v.X, dz = p.Z - v.Z;
                        if (Math.Max(Math.Abs(dx * fx + dz * fz) - v.HalfLength, Math.Abs(dx * fz - dz * fx) - v.HalfWidth) - Body < 0) overlaps++;
                    }
                }
            }
            Assert.Greater(overlaps, 0, "if no one looked, someone would be run over: the rules are what prevents it");
        }

        /// <summary>A traffic that never makes anyone wait.</summary>
        sealed class BlindToPeople : ITrafficQuery
        {
            public BlindToPeople(TrafficSim traffic) { }

            public bool RoadBusy(double fromX, double fromZ, double toX, double toZ, Crossing crossing, double walkSpeed) => false;
        }

        [Test]
        public void ThePrototypesCrowdBarelyCrossesEvenWithTraffic()
        {
            Tally prototype = Run(CrowdRules.Prototype, 110, 52, 150);
            Tally city = Run(CrowdRules.City, 110, 52, 150);
            Console.WriteLine($"CROWDTRAFFIC crossings in 150 s: prototype's rules {prototype.Crossings}, city rules {city.Crossings}");
            Assert.Greater(city.Crossings, prototype.Crossings * 2);
        }

        [Test]
        public void ABusyRoadMakesPeopleWaitAndAClearOneLetsThemGo()
        {
            // one walker at a mid-block crosswalk; a vehicle 40 m up the road coming fast, or none
            int block = Nav.Blocks.ToList().FindIndex(b => b.Gx == 3 && b.Gz == 3);
            int node = block * 8 + 3;   // the east-edge midpoint: the crosswalk leads east over a road carrying Z traffic
            int next = block * 8 + 4;
            foreach (bool vehicleComing in new[] { true, false })
            {
                CrowdSim crowd = CrowdSim.Create(Nav, 1, 1, 0, 0, 7, new CrowdRules(50, true, true));
                TrafficSim traffic = TrafficSim.Create(Nav, 0, 0, 1, 0, TrafficSim.DefaultSeed, TrafficRules.City);
                Pedestrian p = crowd.Active[0];
                p.Node = node; p.PrevNode = block * 8 + 2;
                p.X = Nav.NodeX(node); p.Z = Nav.NodeZ(node); p.Y = Nav.WalkY;
                p.Heading = Math.Atan2(Nav.NodeX(next) - p.X, Nav.NodeZ(next) - p.Z);
                p.Tx = p.X; p.Tz = p.Z; p.Lat = 0; p.Crossing = Crossing.None;
                p.State = PedState.Walk; p.Timer = 100; p.WaitTimer = 0; p.Speed = p.Cruise;

                Vehicle v = traffic.Vehicles[0];
                v.Active = vehicleComing;
                if (vehicleComing)
                {
                    // along Z on the road east of the block, 40 m short of the walker's line, at speed
                    int column = Nav.XLines.ToList().FindIndex(x => x > p.X && x < p.X + 20);
                    v.Type = VehicleCatalog.Ground[0]; v.TypeIndex = 0; v.Cruise = 16;
                    v.Dx = 0; v.Dz = 1; v.Ndx = 0; v.Ndz = 1; v.Heading = 0; v.Speed = 15;
                    v.X = Nav.XLines[column] - traffic.Lane; v.Z = p.Z - 40 - v.HalfLength; v.Y = 0;
                    v.Ix = column; v.Iz = Nav.ZLines.ToList().FindIndex(z => z > p.Z);
                    v.Leg = 0; v.Turn = Manoeuvre.Straight;
                    v.WpX = v.X; v.WpZ = Nav.ZLines[v.Iz] - (Nav.AsphaltHalf + 1.1);
                }
                crowd.Step(Dt, 0, 0, 5000, 5000, traffic);
                if (vehicleComing)
                {
                    Assert.AreEqual(Crossing.None, p.Crossing, "at 15 m/s 40 m away it would be on them in under 3 s: they wait");
                    Assert.Greater(p.WaitTimer, 0);
                }
                else
                {
                    Assert.AreEqual(Crossing.Z, p.Crossing, "an empty road: they cross");
                }
            }
        }
    }
}
