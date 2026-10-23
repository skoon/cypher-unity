using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class CrowdSimTests
    {
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate());
        const double Dt = 1.0 / 60;

        static CrowdSim Make(int pool = 60, int active = 60, double px = 0, double pz = 0, uint seed = CrowdSim.DefaultSeed) => CrowdSim.Create(Nav, pool, active, px, pz, seed);

        static void Run(CrowdSim sim, double seconds, double camX = 0, double camZ = 0, double playerX = 0, double playerZ = 0, ITrafficQuery traffic = null)
        {
            int steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++) sim.Step(Dt, camX, camZ, playerX, playerZ, traffic);
        }

        sealed class Busy : ITrafficQuery
        {
            public bool Answer;
            public int Asked;
            public double FromX, FromZ, ToX, ToZ, WalkSpeed;
            public Crossing Kind;

            public bool RoadBusy(double fromX, double fromZ, double toX, double toZ, Crossing crossing, double walkSpeed)
            {
                Asked++;
                FromX = fromX; FromZ = fromZ; ToX = toX; ToZ = toZ; Kind = crossing; WalkSpeed = walkSpeed;
                return Answer;
            }
        }

        /// <summary>A person standing still somewhere open, with a target far ahead along +Z, for testing one rule at a time.</summary>
        static Pedestrian Place(Pedestrian p, double x, double z, double heading, double tx, double tz)
        {
            p.X = x; p.Z = z; p.Y = Nav.WalkY; p.Heading = heading;
            p.Tx = tx; p.Tz = tz;
            p.PrevX = x; p.PrevZ = z; p.PrevY = p.Y; p.PrevHeading = heading;
            p.State = PedState.Walk; p.Timer = 100; p.WaitTimer = 0; p.Speed = p.Cruise;
            return p;
        }

        // ---------------------------------------------------------------- creation

        [Test]
        public void ThePoolIsBuiltAndAPrefixIsActive()
        {
            CrowdSim sim = Make(80, 30);
            Assert.AreEqual(80, sim.Pool.Count);
            Assert.AreEqual(30, sim.Active.Count);
            Assert.AreEqual(30, sim.TargetCount);
            for (int i = 0; i < 30; i++) Assert.AreSame(sim.Pool[i], sim.Active[i]);
            for (int i = 0; i < 80; i++) Assert.AreEqual(i, sim.Pool[i].Index);
            Assert.AreSame(Nav, sim.Nav);
        }

        [Test]
        public void EveryoneStartsOnAPavementNodeWithATargetOnTheGraph()
        {
            CrowdSim sim = Make(200, 200);
            foreach (Pedestrian p in sim.Pool)
            {
                Assert.AreEqual(Nav.WalkY, p.Y);
                Assert.That(p.PrevNode, Is.InRange(0, Nav.NodeCount - 1), "they have just come from a node");
                Assert.IsTrue(Nav.Neighbours(p.PrevNode).Contains(p.Node), $"person {p.Index} walks along an edge of the graph");
                // within the seeding scatter of the node they were placed at, then a step toward the next
                Assert.LessOrEqual(Math.Abs(p.Tx - Nav.NodeX(p.Node)), Nav.AsphaltHalf + 1e-9);
                Assert.AreEqual(p.Speed, p.State == PedState.Idle ? 0 : p.Cruise);
                Assert.AreEqual(p.X, p.PrevX);
                Assert.AreEqual(p.Heading, p.PrevHeading);
            }
        }

        [Test]
        public void TheSameSeedMakesTheSameCrowdAndAnotherSeedADifferentOne()
        {
            CrowdSim a = Make(60, 60), b = Make(60, 60), c = Make(60, 60, seed: 12345);
            Run(a, 10); Run(b, 10); Run(c, 10);
            for (int i = 0; i < 60; i++)
            {
                Assert.AreEqual(a.Active[i].X, b.Active[i].X);
                Assert.AreEqual(a.Active[i].Z, b.Active[i].Z);
                Assert.AreEqual(a.Active[i].Heading, b.Active[i].Heading);
            }
            Assert.IsTrue(Enumerable.Range(0, 60).Any(i => a.Active[i].X != c.Active[i].X));
        }

        [Test]
        public void AppearancesComeFromThePalettesAndRanges()
        {
            CrowdSim sim = Make(400, 400);
            foreach (Pedestrian p in sim.Pool)
            {
                PedestrianAppearance look = p.Appearance;
                Assert.That(look.Scale, Is.InRange(0.92, 1.09));
                Assert.That(look.Build, Is.InRange(0.88, 1.18));
                Assert.Contains(look.Skin, PedestrianFactory.Skin);
                Assert.IsTrue(PedestrianFactory.Coat.Contains(look.Coat) || PedestrianFactory.CoatAccent.Contains(look.Coat));
                Assert.Contains(look.Pants, PedestrianFactory.Pants);
                Assert.Contains(look.TrimColour, PedestrianFactory.Trim);
                Assert.That(look.TrimPower, Is.InRange(1.6, 4.2));
                Assert.IsFalse(look.Umbrella && look.Hood, "an umbrella and a hood never go together");
                Assert.That(p.Cruise, Is.InRange(1.12, 1.54));
            }
            Assert.That(sim.Pool.Count(p => p.Appearance.Umbrella), Is.InRange(70, 140), "about a quarter carry umbrellas");
            Assert.That(sim.Pool.Count(p => p.Appearance.LongCoat), Is.InRange(130, 210), "about 42% wear long coats");
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => CrowdSim.Create(null, 10, 5, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CrowdSim.Create(Nav, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CrowdSim.Create(Nav, 10, 11, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CrowdSim.Create(Nav, 10, -1, 0, 0));
            CrowdSim sim = Make(10, 10);
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Step(-0.01, 0, 0, 0, 0));
            Assert.Throws<ArgumentNullException>(() => sim.NearestToPlayer(0, 0, null));
            Assert.Throws<ArgumentNullException>(() => PedestrianFactory.Create(null, 0));
        }

        // ---------------------------------------------------------------- walking

        [Test]
        public void PeopleStayOnThePavementOrCrossAtACrosswalk()
        {
            CrowdSim sim = Make(120, 120);
            double worst = 0;
            for (int s = 0; s < 90; s++)
            {
                Run(sim, 1);
                foreach (Pedestrian p in sim.Active)
                {
                    if (p.Crossing == Crossing.None) worst = Math.Max(worst, Nav.KerbDistance(p.X, p.Z) - Nav.WalkOuter);
                }
            }
            Assert.Less(worst, 1.5, "off the crosswalks nobody strays more than a metre and a half past the kerb (the crowd can nudge someone off the edge)");
        }

        [Test]
        public void PeopleWalkAtSensibleSpeeds()
        {
            CrowdSim sim = Make(120, 120);
            double max = 0;
            for (int s = 0; s < 40; s++)
            {
                Run(sim, 1);
                foreach (Pedestrian p in sim.Active)
                {
                    Assert.GreaterOrEqual(p.Speed, 0);
                    max = Math.Max(max, p.Speed);
                }
            }
            Assert.LessOrEqual(max, 1.54 * 1.85 + 1e-9, "the fastest hurry is 1.85 times the fastest stroll");
            Assert.Greater(max, 1.1);
        }

        [Test]
        public void ThePhaseAdvancesWithSpeedAndWraps()
        {
            CrowdSim sim = Make(10, 10);
            Pedestrian p = sim.Active[0];
            Place(p, 0, 0, 0, 0, 50);
            p.Phase = 0; p.PrevPhase = 0; p.Speed = 1.2;
            sim.Step(Dt, 0, 0, 100, 100);
            Assert.Greater(p.Phase, 0);
            p.Phase = Math.PI * 2 - 1e-6; p.PrevPhase = p.Phase;
            sim.Step(Dt, 0, 0, 100, 100);
            Assert.Less(p.Phase, 1.0, "wrapped");
            Assert.Less(p.PrevPhase, 0, "and its previous phase wrapped with it, so the interpolation is smooth");
        }

        [Test]
        public void AFrozenStepMovesNobody()
        {
            CrowdSim sim = Make(60, 60);
            var before = sim.Active.Select(p => (p.X, p.Z, p.Heading)).ToList();
            sim.Step(0, 0, 0, 0, 0);
            for (int i = 0; i < 60; i++) Assert.AreEqual(before[i], (sim.Active[i].X, sim.Active[i].Z, sim.Active[i].Heading));
        }

        [Test]
        public void EachStepRemembersTheLastForInterpolation()
        {
            CrowdSim sim = Make(30, 30);
            var x = sim.Active.Select(p => p.X).ToArray();
            sim.Step(Dt, 0, 0, 0, 0);
            for (int i = 0; i < 30; i++) Assert.AreEqual(x[i], sim.Active[i].PrevX, "the position before the step");
        }

        // ---------------------------------------------------------------- rules, one at a time

        [Test]
        public void SomeoneAheadPushesThemAside()
        {
            CrowdSim sim = Make(2, 2);
            Pedestrian a = Place(sim.Active[0], 0, 0, 0, 0, 40);
            Place(sim.Active[1], 0.4, 1.0, 0, 0.4, 40);   // slightly to the right, a metre ahead
            sim.Step(Dt, 0, 0, 500, 500);
            Assert.Less(a.Heading, 0, "a turns away from them, to the left (negative heading is toward -X)");
        }

        [Test]
        public void SomeoneBehindDoesNotPushThemAnywhere()
        {
            CrowdSim sim = Make(2, 2);
            Pedestrian a = Place(sim.Active[0], 0, 0, 0, 0, 40);
            Place(sim.Active[1], 0.4, -1.0, 0, 0.4, 40);   // a metre behind
            sim.Step(Dt, 0, 0, 500, 500);
            Assert.AreEqual(0.0, a.Heading, 1e-12, "people yield only to those in the way ahead");
        }

        [Test]
        public void CrowdingSlowsPeopleDown()
        {
            CrowdSim alone = Make(1, 1), packed = Make(4, 4);
            Pedestrian a = Place(alone.Active[0], 0, 0, 0, 0, 40);
            Pedestrian b = Place(packed.Active[0], 0, 0, 0, 0, 40);
            for (int i = 1; i < 4; i++) Place(packed.Active[i], 0.3 * i, 0.8 + 0.2 * i, 0, 0, 40);
            for (int i = 0; i < 60; i++)
            {
                alone.Step(Dt, 0, 0, 500, 500);
                packed.Step(Dt, 0, 0, 500, 500);
            }
            Assert.Less(b.Speed, a.Speed, "the hemmed-in person has slowed");
        }

        [Test]
        public void ThePlayerIsSteppedAround()
        {
            CrowdSim sim = Make(1, 1);
            Pedestrian p = Place(sim.Active[0], 0.5, 2.0, Math.PI, 0.5, -40);   // walking toward the player, who is at the origin
            double heading = p.Heading;
            sim.Step(Dt, 0, 0, 0, 0);
            Assert.AreNotEqual(heading, p.Heading, "turns to go round");
        }

        [Test]
        public void NobodyEverEndsUpInsideThePlayer()
        {
            CrowdSim sim = Make(150, 150);
            Pedestrian first = sim.Active[0];
            double px = first.X, pz = first.Z;   // the player stands exactly where someone is
            for (int i = 0; i < 300; i++)
            {
                sim.Step(Dt, px, pz, px, pz);
                foreach (Pedestrian p in sim.Active)
                {
                    double d = Math.Sqrt((p.X - px) * (p.X - px) + (p.Z - pz) * (p.Z - pz));
                    if (d > 1e-4) Assert.GreaterOrEqual(d, CrowdSim.KeepOut - 1e-9, $"person {p.Index} at step {i}");
                }
            }
        }

        [TestCase(PedState.Idle, false, PedState.Walk)]
        [TestCase(PedState.Idle, true, PedState.Hurry)]
        public void AnIdlePersonSetsOffWhenTheirTimeIsUp(PedState from, bool hurried, PedState expected)
        {
            CrowdSim sim = Make(1, 1);
            Pedestrian p = Place(sim.Active[0], 0, 0, 0, 0, 40);
            p.State = from; p.Hurried = hurried; p.Timer = 0;
            sim.Step(Dt, 0, 0, 500, 500);
            Assert.AreEqual(expected, p.State);
            Assert.That(p.Timer, Is.InRange(6.0, 20.0), "they walk for 6 to 20 seconds before the next decision");
        }

        [Test]
        public void AWalkerWhoseTimeIsUpEitherStopsOrCarriesOn()
        {
            int idle = 0, walk = 0, hurry = 0;
            for (uint seed = 1; seed <= 300; seed++)
            {
                CrowdSim sim = Make(1, 1, seed: seed);
                Pedestrian p = Place(sim.Active[0], 0, 0, 0, 0, 40);
                p.State = PedState.Walk; p.Timer = 0;
                sim.Step(Dt, 0, 0, 500, 500);
                if (p.State == PedState.Idle) { idle++; Assert.That(p.Timer, Is.InRange(1.5, 6.0)); }
                else
                {
                    if (p.State == PedState.Hurry) hurry++; else walk++;
                    Assert.That(p.Timer, Is.InRange(5.0, 17.0));
                }
            }
            Assert.That(idle, Is.InRange(40, 95), "about 22% stop");
            Assert.That(hurry, Is.InRange(25, 90), "a quarter of the rest hurry");
            Assert.Greater(walk, 100);
        }

        [Test]
        public void AnIdlePersonStopsWalking()
        {
            CrowdSim sim = Make(1, 1);
            Pedestrian p = Place(sim.Active[0], 0, 0, 0, 0, 40);
            p.State = PedState.Idle; p.Timer = 50;
            for (int i = 0; i < 180; i++) sim.Step(Dt, 0, 0, 500, 500);
            Assert.Less(p.Speed, 0.05);
        }

        [Test]
        public void AHurryingPersonWalksFasterThanAStrollingOne()
        {
            CrowdSim sim = Make(2, 2);
            Pedestrian stroll = Place(sim.Active[0], 0, 0, 0, 0, 90), hurry = Place(sim.Active[1], 20, 0, 0, 20, 90);
            hurry.State = PedState.Hurry;
            hurry.Cruise = stroll.Cruise;
            stroll.Speed = hurry.Speed = 0;
            for (int i = 0; i < 120; i++) sim.Step(Dt, 0, 0, 500, 500);
            Assert.Greater(hurry.Speed, stroll.Speed * 1.5);
        }

        // ---------------------------------------------------------------- picking a way on, and kerbs

        /// <summary>Someone on a kerb-edge midpoint, facing the road, about to choose: returns the node and its crossing neighbour.</summary>
        static (int node, int across) AtAKerb(Pedestrian p)
        {
            int node = 1;   // the first block's south-edge midpoint... block 0 is on the grid's edge, so use an interior block
            int block = Nav.Blocks.ToList().FindIndex(b => b.Gx == 3 && b.Gz == 3);
            node = block * 8 + 3;   // east-edge midpoint: a crosswalk leads east
            int across = Enumerable.Range(0, Nav.Neighbours(node).Count).Where(e => Nav.NeighbourCrossings(node)[e] != Crossing.None).Select(e => Nav.Neighbours(node)[e]).Single();
            p.Node = node;
            p.PrevNode = block * 8 + 2;
            p.X = Nav.NodeX(node); p.Z = Nav.NodeZ(node); p.Y = Nav.WalkY;
            p.Heading = Math.Atan2(Nav.NodeX(across) - p.X, Nav.NodeZ(across) - p.Z);
            p.Tx = p.X; p.Tz = p.Z; p.Lat = 0; p.Crossing = Crossing.None;
            p.State = PedState.Walk; p.Timer = 100; p.WaitTimer = 0; p.Speed = p.Cruise;
            return (node, across);
        }

        [Test]
        public void ArrivingAtANodePicksTheNextAndNeverGoesStraightBack()
        {
            CrowdSim sim = Make(1, 1);
            Pedestrian p = sim.Active[0];
            for (int trial = 0; trial < 50; trial++)
            {
                (int node, _) = AtAKerb(p);
                int prev = p.PrevNode;
                sim.Step(Dt, 0, 0, 5000, 5000);
                Assert.AreEqual(node, p.PrevNode, "the node just left is remembered");
                Assert.AreNotEqual(prev, p.Node, "never straight back the way they came");
                Assert.IsTrue(Nav.Neighbours(node).Contains(p.Node));
            }
        }

        [Test]
        public void WithAClearRoadSomeoneFacingItCrossesAndWithABusyOneTheyWait()
        {
            int crossed = 0, waited = 0;
            for (uint seed = 1; seed <= 100; seed++)
            {
                var clear = new Busy { Answer = false };
                CrowdSim sim = Make(1, 1, seed: seed);
                Pedestrian p = sim.Active[0];
                (int node, int across) = AtAKerb(p);
                sim.Step(Dt, 0, 0, 5000, 5000, clear);
                if (p.Crossing == Crossing.None) continue;   // chose to carry on round the block this time
                crossed++;
                Assert.AreEqual(across, p.Node);
                Assert.AreEqual(Crossing.Z, p.Crossing, "an east-west crosswalk: the pedestrian walks along X over a road carrying Z traffic");
                Assert.AreEqual(1, clear.Asked, "asked once");

                // the same choice with the road busy: the same seed, so the same draws
                var busy = new Busy { Answer = true };
                CrowdSim sim2 = Make(1, 1, seed: seed);
                Pedestrian q = sim2.Active[0];
                AtAKerb(q);
                double speed = q.Speed;
                sim2.Step(Dt, 0, 0, 5000, 5000, busy);
                waited++;
                Assert.AreEqual(node, q.Node, "they stay where they were");
                Assert.AreEqual(Crossing.None, q.Crossing);
                Assert.That(q.WaitTimer, Is.InRange(0.4, 1.1), "and wait a moment, then look again");
                Assert.Less(q.Speed, speed, "slowing to a stop at the kerb");
            }
            Assert.Greater(crossed, 20, "facing the road, a crossing is a likely choice");
            Assert.AreEqual(crossed, waited);
        }

        [Test]
        public void AWaitingPersonStandsStillThenSetsOffAgain()
        {
            var busy = new Busy { Answer = true };
            CrowdSim sim = null;
            Pedestrian p = null;
            for (uint seed = 1; seed < 200; seed++)
            {
                sim = Make(1, 1, seed: seed);
                p = sim.Active[0];
                AtAKerb(p);
                sim.Step(Dt, 0, 0, 5000, 5000, busy);
                if (p.WaitTimer > 0) break;
            }
            Assert.Greater(p.WaitTimer, 0, "found a person who waits");
            int asked = busy.Asked;
            double x = p.X, z = p.Z;
            for (int i = 0; i < 20; i++) sim.Step(Dt, 0, 0, 5000, 5000, busy);
            Assert.AreEqual(asked, busy.Asked, "they are not asking while they wait");
            Assert.AreEqual(x, p.X, 0.15, "and come to a stop (the speed halves, then decays by a fifth a step)");
            Assert.AreEqual(z, p.Z, 0.15);
            busy.Answer = false;
            for (int i = 0; i < 150; i++) sim.Step(Dt, 0, 0, 5000, 5000, busy);
            Assert.LessOrEqual(p.WaitTimer, 0, "the wait is over");
            Assert.Greater(p.Speed, 0.5, "and they are on their way again");
        }

        // ---------------------------------------------------------------- recycling

        [Test]
        public void SomeoneBeyondTheCullRadiusIsRecycledIntoTheSpawnBand()
        {
            int inBand = 0;
            for (uint seed = 1; seed <= 30; seed++)
            {
                CrowdSim sim = Make(1, 1, seed: seed);
                Pedestrian p = sim.Active[0];
                Place(p, 500, 500, 0, 500, 540);   // far outside the city
                sim.Step(Dt, 0, 0, 0, 0);
                double d = Math.Sqrt(p.X * p.X + p.Z * p.Z);
                Assert.LessOrEqual(d, CrowdSim.SpawnMax + 1e-9, "never farther out than the band");
                if (d >= CrowdSim.SpawnMin) inBand++;
                Assert.AreEqual(Nav.WalkY, p.Y);
                Assert.AreEqual(p.X, p.PrevX, "no interpolation across the jump");
                Assert.AreEqual(0.0, p.WaitTimer);
                Assert.AreEqual(p.Cruise, p.Speed, "recycled people are up to speed");
            }
            Assert.GreaterOrEqual(inBand, 25, "almost always inside the band: there are 392 nodes to pick from");
        }

        [Test]
        public void ARecycledPersonFacesTheirNextNode()
        {
            CrowdSim sim = Make(1, 1);
            Pedestrian p = sim.Active[0];
            Place(p, 500, 500, 0, 500, 540);
            sim.Step(Dt, 0, 0, 0, 0);
            Assert.AreEqual(Math.Atan2(p.Tx - p.X, p.Tz - p.Z), p.Heading, 1e-12);
        }

        [Test]
        public void TheSpawnBandIsInsideTheCullRadius()
        {
            Assert.Less(CrowdSim.SpawnMax, CrowdSim.CullRadius, "or people would be recycled the step they appear");
            Assert.Less(CrowdSim.SpawnMin, CrowdSim.SpawnMax);
        }

        // ---------------------------------------------------------------- the pool

        [Test]
        public void LoweringTheTargetRetiresPeopleOutOfViewFromTheBack()
        {
            CrowdSim sim = Make(100, 100);
            sim.IsVisible = p => false;
            sim.TargetCount = 40;
            sim.Step(Dt, 5000, 5000, 5000, 5000);   // the camera is far from all of them (they will be recycled to its side, but the count settles first)
            Assert.AreEqual(40, sim.Active.Count);
            for (int i = 0; i < 40; i++) Assert.AreSame(sim.Pool[i], sim.Active[i], "the front of the pool stays");
        }

        [Test]
        public void PeopleInViewOrWithinTwelveMetresAreNotRetired()
        {
            CrowdSim sim = Make(100, 100);
            var keep = new HashSet<Pedestrian> { sim.Pool[99], sim.Pool[90] };
            sim.IsVisible = p => keep.Contains(p);
            // a person standing right beside the camera is also kept
            Pedestrian beside = sim.Pool[80];
            Place(beside, 3, 3, 0, 3, 40);
            sim.TargetCount = 40;
            sim.Step(Dt, 0, 0, 5000, 5000);
            Assert.IsTrue(sim.Active.Contains(sim.Pool[99]));
            Assert.IsTrue(sim.Active.Contains(sim.Pool[90]));
            Assert.IsTrue(sim.Active.Contains(beside));
            Assert.AreEqual(40, sim.Active.Count, "the count still reaches the target: the three who stay displace people from the front");
            Assert.IsTrue(sim.Active.Contains(sim.Pool[0]));
            Assert.IsFalse(sim.Active.Contains(sim.Pool[37]), "37 from the front and the three who were kept");
        }

        [Test]
        public void RaisingTheTargetBringsPoolMembersBackWhereTheyCannotBeSeen()
        {
            CrowdSim sim = Make(100, 30);
            sim.IsVisible = p => false;
            sim.TargetCount = 70;
            sim.Step(Dt, 0, 0, 0, 0);
            Assert.AreEqual(70, sim.Active.Count);
            Assert.AreEqual(70, sim.Active.Distinct().Count(), "nobody twice");
            var spawn = sim.Pool.Skip(30).Take(40).Select(p => Math.Sqrt(p.X * p.X + p.Z * p.Z)).ToList();
            Assert.GreaterOrEqual(spawn.Count(d => d >= CrowdSim.SpawnMin - 1), 30, "the newcomers appear out at the edge");
        }

        [Test]
        public void SomeoneWhoWouldAppearInViewIsSkipped()
        {
            CrowdSim sim = Make(40, 10);
            int calls = 0;
            sim.IsVisible = p => (calls++ % 2) == 0;   // every other candidate would be in view
            sim.TargetCount = 20;
            sim.Step(Dt, 0, 0, 0, 0);
            Assert.AreEqual(20, sim.Active.Count);
            Assert.Greater(calls, 10, "ten places to fill took more than ten candidates, because some would have been seen");
        }

        [Test]
        public void AnUnreachableTargetDoesNotLoopForever()
        {
            CrowdSim sim = Make(20, 10);
            sim.IsVisible = p => true;   // everyone would be seen: nobody can be introduced
            sim.TargetCount = 15;
            sim.Step(Dt, 0, 0, 0, 0);
            Assert.AreEqual(10, sim.Active.Count);
        }

        // ---------------------------------------------------------------- colliders

        [Test]
        public void TheNearestPeopleWithinRangeAreFoundNearestFirst()
        {
            CrowdSim sim = Make(10, 10);
            double[] dist = { 5.0, 1.0, 9.0, 3.0, 2.0, 4.5, 0.5, 5.9, 6.5, 30 };
            for (int i = 0; i < 10; i++) Place(sim.Active[i], dist[i], 0, 0, dist[i], 40);
            var into = new Pedestrian[8];
            int n = sim.NearestToPlayer(0, 0, into);
            Assert.AreEqual(7, n, "seven are within six metres");
            double[] expect = { 0.5, 1.0, 2.0, 3.0, 4.5, 5.0, 5.9 };
            for (int i = 0; i < n; i++) Assert.AreEqual(expect[i], into[i].X, 1e-12);
        }

        [Test]
        public void OnlyAsManyAsTheBufferHolds()
        {
            CrowdSim sim = Make(10, 10);
            for (int i = 0; i < 10; i++) Place(sim.Active[i], 1 + i * 0.4, 0, 0, 1 + i * 0.4, 40);
            var into = new Pedestrian[3];
            Assert.AreEqual(3, sim.NearestToPlayer(0, 0, into));
            Assert.AreEqual(1.0, into[0].X, 1e-12);
            Assert.AreEqual(1.8, into[2].X, 1e-12);
        }

        [Test]
        public void TiesKeepTheirOrder()
        {
            CrowdSim sim = Make(3, 3);
            Place(sim.Active[0], 2, 0, 0, 2, 40);
            Place(sim.Active[1], 0, 2, 0, 0, 42);
            Place(sim.Active[2], -2, 0, 0, -2, 40);
            var into = new Pedestrian[3];
            sim.NearestToPlayer(0, 0, into);
            Assert.AreSame(sim.Active[0], into[0]);
            Assert.AreSame(sim.Active[1], into[1]);
            Assert.AreSame(sim.Active[2], into[2]);
        }

        [Test]
        public void NobodyNearMeansNoColliders()
        {
            CrowdSim sim = Make(10, 10);
            for (int i = 0; i < 10; i++) Place(sim.Active[i], 50 + i, 50, 0, 50 + i, 90);
            Assert.AreEqual(0, sim.NearestToPlayer(0, 0, new Pedestrian[8]));
        }

        // ---------------------------------------------------------------- the maths

        [TestCase(0.0, 1.0, 1.0)]
        [TestCase(0.0, -1.0, -1.0)]
        [TestCase(3.0, -3.0, -6.0 + 2 * Math.PI)]
        [TestCase(-3.0, 3.0, 6.0 - 2 * Math.PI)]
        [TestCase(0.1, 0.1 + 2 * Math.PI, 0.0)]
        public void ShortestAngleWrapsToTheNearerWay(double a, double b, double expected) =>
            Assert.AreEqual(expected, CrowdSim.ShortestAngle(a, b), 1e-9);
    }
}
