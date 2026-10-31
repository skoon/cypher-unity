using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// Step 3 of M5: cover made from the city, routes round the blocks, the gunner fighting from cover, street patrols that mind their own business until provoked,
    /// and the directors that bring encounters and patrols into the world and take them out again.
    /// </summary>
    public class EnemyCoverTests
    {
        const double Dt = 1.0 / 60;

        /// <summary>A world of upright boxes (turned about Y like a prop) with tops: a line is blocked where it passes through one below its top.</summary>
        sealed class Boxes : IEnemyWorld
        {
            public readonly List<PropBox> List = new List<PropBox>();

            public bool Clear(V3 from, V3 to)
            {
                foreach (PropBox b in List) if (Through(b, from, to)) return false;
                return true;
            }

            public bool Walkable(V3 from, V3 to) => Clear(new V3(from.X, 0.5, from.Z), new V3(to.X, 0.5, to.Z));

            /// <summary>The segment against the box, in the box's own frame: a slab test on x and z, then the height where it is inside.</summary>
            static bool Through(PropBox b, V3 from, V3 to)
            {
                double c = Math.Cos(b.Yaw), s = Math.Sin(b.Yaw);
                (double X, double Z) Local(V3 p)
                {
                    double dx = p.X - b.Cx, dz = p.Z - b.Cz;
                    return (dx * c - dz * s, dx * s + dz * c);
                }
                var a = Local(from);
                var e = Local(to);
                double t0 = 0, t1 = 1;
                if (!Slab(a.X, e.X - a.X, b.Hx, ref t0, ref t1) || !Slab(a.Z, e.Z - a.Z, b.Hz, ref t0, ref t1)) return false;
                double y0 = from.Y + (to.Y - from.Y) * t0, y1 = from.Y + (to.Y - from.Y) * t1;
                return Math.Min(y0, y1) < b.Cy + b.Hy && Math.Max(y0, y1) > b.Cy - b.Hy;
            }

            static bool Slab(double p, double d, double h, ref double t0, ref double t1)
            {
                if (Math.Abs(d) < 1e-12) return Math.Abs(p) <= h;
                double ta = (-h - p) / d, tb = (h - p) / d;
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = Math.Max(t0, ta);
                t1 = Math.Min(t1, tb);
                return t0 <= t1;
            }
        }

        static double Flat0(double x, double z) => 0;

        static PlayerView At(double x, double z, bool crouched = false) => new PlayerView(new V3(x, 0, z), crouched ? 1.0 : 1.6, 0, crouched);

        // ------------------------------------------------------------ the cover map

        [Test]
        public void ACrateGivesLowCoverOnItsFacesAndATallBoxGivesPointsToStepOutFrom()
        {
            // a crate 3.2 m by 1 m by 1 m, and a tall box 1.2 m by 2 m tall
            var crate = new PropBox(0, 0.5, 0, 1.6, 0.5, 0.5, 0);
            var tall = new PropBox(20, 1.0, 0, 0.6, 1.0, 0.6, 0);
            var tiny = new PropBox(40, 0.5, 0, 0.2, 0.5, 0.2, 0);   // narrower than a person
            var flat = new PropBox(60, 0.2, 0, 2, 0.2, 2, 0);       // too low to hide behind
            CoverMap map = CoverMap.Build(new[] { crate, tall, tiny, flat }, Array.Empty<(double, double, double, double)>(), Flat0);

            List<CoverPoint> crateCover = map.Points.Where(p => Math.Abs(p.Hide.X) < 5).ToList();
            Assert.IsTrue(crateCover.All(p => p.Low));
            Assert.IsTrue(crateCover.All(p => p.Hide.X == p.Peek.X && p.Hide.Z == p.Peek.Z), "low cover: stand up where you hide");
            Assert.AreEqual(2, crateCover.Count(p => p.NormalZ > 0.9), "the long face is wide enough for one point every 1.6 m");
            Assert.AreEqual(1, crateCover.Count(p => p.NormalX > 0.9), "the short face, one");
            Assert.AreEqual(1.6 + CoverMap.Standoff, crateCover.First(p => p.NormalX > 0.9).Hide.X, 1e-9);

            List<CoverPoint> tallCover = map.Points.Where(p => Math.Abs(p.Hide.X - 20) < 5).ToList();
            Assert.AreEqual(8, tallCover.Count, "two ends of each of four faces");
            Assert.IsTrue(tallCover.All(p => !p.Low));
            foreach (CoverPoint p in tallCover)
            {
                double along = Math.Abs((p.Peek.X - 20) * p.NormalZ - p.Peek.Z * p.NormalX);   // how far along the face the peek is
                Assert.Greater(along, 0.6, "stepping out goes past the end of the face");
            }
            Assert.IsFalse(map.Points.Any(p => Math.Abs(p.Hide.X - 40) < 5), "nothing behind a post");
            Assert.IsFalse(map.Points.Any(p => Math.Abs(p.Hide.X - 60) < 5), "nothing behind a kerb");
        }

        [Test]
        public void ATurnedBoxHasItsCoverTurnedWithIt()
        {
            var turned = new PropBox(0, 1.0, 0, 1.0, 1.0, 0.4, Math.PI / 2);
            CoverMap map = CoverMap.Build(new[] { turned }, Array.Empty<(double, double, double, double)>(), Flat0);
            var world = new Boxes();
            world.List.Add(turned);
            foreach (CoverPoint p in map.Points)
            {
                Assert.IsTrue(world.Walkable(p.Hide, p.Hide), "the hiding spot is outside the box");
                V3 behind = new V3(p.Hide.X - p.NormalX * 6, 1.6, p.Hide.Z - p.NormalZ * 6);
                Assert.IsFalse(world.Clear(new V3(p.Hide.X, 1.55, p.Hide.Z), behind), $"cover {p.Id} hides from straight behind the box");
            }
        }

        [Test]
        public void TheWholeCityHasCoverEverywhereAndItIsQuickToMake()
        {
            CityPlan plan = CityPlan.Generate(CityPlan.DefaultSeed);
            CityNav nav = CityNav.Build(plan);
            PropLayout props = PropPlacer.Build(plan, CityWindow.AroundPlaza(3));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            CoverMap map = CoverMap.Build(props.Boxes, plan.Buildings.Select(b => (b.X, b.Z, b.Width, b.Depth)), nav.GroundY);
            clock.Stop();
            Console.WriteLine($"cover: {map.Points.Count} points ({map.Points.Count(p => p.Low)} low) from {props.Boxes.Count} prop boxes and {plan.Buildings.Count} buildings in {clock.ElapsedMilliseconds} ms");
            Assert.Greater(map.Points.Count, 1000);
            Assert.Less(clock.ElapsedMilliseconds, 500, "made while the city loads, so it must be cheap");
            // every pavement node has some cover within a short run
            int bare = 0;
            for (int i = 0; i < nav.NodeCount; i++) if (map.Near(new V3(nav.NodeX(i), 0, nav.NodeZ(i)), 15).Count == 0) bare++;
            Assert.Less(bare, nav.NodeCount / 10, "at most one corner in ten with nothing to hide behind within 15 m");
        }

        [Test]
        public void ABuildingsCornersGiveCoverEitherSide()
        {
            CoverMap map = CoverMap.Build(Array.Empty<PropBox>(), new[] { (0.0, 0.0, 10.0, 6.0) }, Flat0);
            Assert.AreEqual(8, map.Points.Count);
            Assert.IsTrue(map.Points.All(p => !p.Low));
            Assert.IsTrue(map.Points.All(p => Math.Abs(p.Hide.X) >= 5 + CoverMap.Standoff - 1e-9 || Math.Abs(p.Hide.Z) >= 3 + CoverMap.Standoff - 1e-9), "outside the walls");
        }

        [Test]
        public void FindPicksCoverThatHidesFromTheDangerAndLetsItShootAtAboutItsRange()
        {
            var crate = new PropBox(0, 0.5, 0, 1.0, 0.5, 0.5, 0);
            var world = new Boxes();
            world.List.Add(crate);
            CoverMap map = CoverMap.Build(world.List, Array.Empty<(double, double, double, double)>(), Flat0);
            var threat = new V3(0, 1.6, 16);   // north of the crate
            CoverPoint c = map.Find(new V3(0, 0, -6), threat, 16, world);
            Assert.IsNotNull(c);
            Assert.Less(c.NormalZ, -0.9, "the south face: the crate between it and the danger");
            Assert.IsTrue(c.Faces(threat));

            map.Take(c, 1);
            CoverPoint other = map.Find(new V3(0, 0, -6), threat, 16, world);
            Assert.AreNotSame(c, other, "a taken spot is not offered");
            Assert.Throws<InvalidOperationException>(() => map.Take(c, 2));
            map.Release(c, 1);
            Assert.AreSame(c, map.Find(new V3(0, 0, -6), threat, 16, world));

            Assert.IsNull(map.Find(new V3(0, 0, -6), new V3(40, 1.6, 0), 16, world), "a danger off to the side: nothing here hides from it at that range");
        }

        // ------------------------------------------------------------ routes

        [Test]
        public void RoutesGoRoundTheBlocksAlongThePavementGraph()
        {
            CityNav nav = CityNav.Build(CityPlan.Generate(CityPlan.DefaultSeed));
            var paths = new EnemyPaths(nav);
            int a = 0, b = nav.NodeCount - 1;
            List<int> route = paths.Route(a, b);
            Assert.AreEqual(a, route[0]);
            Assert.AreEqual(b, route[^1]);
            for (int i = 1; i < route.Count; i++) CollectionAssert.Contains(nav.Neighbours(route[i - 1]), route[i], "each step is an edge");
            CollectionAssert.AreEqual(new[] { 5 }, paths.Route(5, 5));
            Assert.AreEqual(route, paths.Route(a, b), "the same route every time");

            List<V3> points = paths.Waypoints(new V3(nav.NodeX(a), 0, nav.NodeZ(a)), new V3(nav.NodeX(b) + 1, 0, nav.NodeZ(b)));
            Assert.AreEqual(nav.NodeX(b) + 1, points[^1].X, "it ends at the place, not the node");
            Assert.Throws<ArgumentOutOfRangeException>(() => paths.Route(-1, 0));
        }

        // ------------------------------------------------------------ fighting from cover

        static (EnemySim Sim, Enemy E, Boxes World) GunnerBehindACrate(uint seed = 4)
        {
            var world = new Boxes();
            world.List.Add(new PropBox(0, 0.5, 0, 1.0, 0.5, 0.5, 0));
            world.List.Add(new PropBox(6, 0.5, -3, 1.0, 0.5, 0.5, 0));
            CoverMap map = CoverMap.Build(world.List, Array.Empty<(double, double, double, double)>(), Flat0);
            var sim = new EnemySim(seed, map);
            Enemy e = sim.Add(sim.AddSquad("jackal"), EnemyTable.Gunner, new V3(2, 0, -5), 0);
            return (sim, e, world);
        }

        [Test]
        public void TheGunnerTakesCoverHidesBetweenBurstsAndStandsUpToShoot()
        {
            var (sim, e, world) = GunnerBehindACrate();
            PlayerView player = At(0, 16);
            bool sawHidden = false, sawExposedShot = false;
            for (int i = 0; i < 60 * 20; i++)
            {
                sim.Step(Dt, player, world);
                if (e.Cover == null) continue;
                foreach (EnemyEvent v in sim.Events.Where(v => v.Kind == EnemyEventKind.Shot))
                {
                    Assert.IsFalse(e.Crouched, "it stands to shoot");
                    Assert.IsTrue(world.Clear(e.Eye, player.Eye), "and only shoots with a clear line");
                    sawExposedShot = true;
                }
                if (e.Crouched)
                {
                    Assert.IsFalse(e.Firing);
                    Assert.IsFalse(world.Clear(e.Eye, player.Eye), "crouched behind the crate it cannot be seen");
                    sawHidden = true;
                }
            }
            Assert.IsNotNull(e.Cover);
            Assert.IsTrue(e.Cover.Low);
            Assert.IsTrue(sawHidden && sawExposedShot);
        }

        [Test]
        public void ItsBodyCrouchesBehindTheCrateAndStandsWithTheGunUpToShoot()
        {
            var (sim, e, world) = GunnerBehindACrate();
            var bodies = new EnemyBodies(2);
            PlayerView player = At(0, 16);
            bool sawCrouched = false, sawStandingShot = false;
            int crouchedFor = 0;
            for (int i = 0; i < 60 * 20; i++)
            {
                sim.Step(Dt, player, world);
                bodies.Step(Dt, sim, player.Chest);
                CombatBody b = bodies.Find(e.Id);
                crouchedFor = e.Crouched ? crouchedFor + 1 : 0;
                if (crouchedFor > 30)
                {
                    Assert.AreEqual(1, b.Crouch, 1e-9);
                    sawCrouched = true;
                }
                if (sim.Events.Any(v => v.Kind == EnemyEventKind.Shot) && e.Cover != null)
                {
                    Assert.Less(b.Crouch, 0.5, "up over the crate");
                    Assert.Greater(b.Aim, 0.5);
                    sawStandingShot = true;
                }
            }
            Assert.IsTrue(sawCrouched && sawStandingShot);
        }

        [Test]
        public void ItMovesOnAfterAFewBurstsAndWhenThePlayerFlanksIt()
        {
            var (sim, e, world) = GunnerBehindACrate();
            var covers = new List<int>();
            for (int i = 0; i < 60 * 40; i++)
            {
                sim.Step(Dt, At(3, 16), world);
                if (e.Cover != null && (covers.Count == 0 || covers[^1] != e.Cover.Id)) covers.Add(e.Cover.Id);
            }
            Assert.GreaterOrEqual(covers.Count, 2, "it changed cover after its bursts");

            (sim, e, world) = GunnerBehindACrate(9);
            for (int i = 0; i < 60 * 6; i++) sim.Step(Dt, At(0, 16), world);
            CoverPoint before = e.Cover;
            Assert.IsNotNull(before);
            // the player runs round to the side the cover no longer hides and opens up from there
            sim.ShotAt(e, new V3(-16, 0, -6));
            for (int i = 0; i < 60 * 3; i++) sim.Step(Dt, At(-16, -6), world);
            Assert.AreNotSame(before, e.Cover, "flanked, it gave that cover up");
            Assert.IsTrue(e.Cover == null || e.Cover.Faces(new V3(-16, 1.6, -6)));
        }

        [Test]
        public void TheHeavyDoesNotHide()
        {
            var world = new Boxes();
            world.List.Add(new PropBox(0, 0.5, 0, 1.0, 0.5, 0.5, 0));
            var sim = new EnemySim(2, CoverMap.Build(world.List, Array.Empty<(double, double, double, double)>(), Flat0));
            Enemy h = sim.Add(sim.AddSquad("jackal"), EnemyTable.Heavy, new V3(2, 0, -5), 0);
            for (int i = 0; i < 60 * 10; i++) sim.Step(Dt, At(0, 16), world);
            Assert.IsNull(h.Cover);
            Assert.IsFalse(h.Crouched);
        }

        [Test]
        public void ItWalksRoundABlockInsteadOfThroughIt()
        {
            CityPlan plan = CityPlan.Generate(CityPlan.DefaultSeed);
            CityNav nav = CityNav.Build(plan);
            var world = new Boxes();
            foreach (Building b in plan.Buildings) world.List.Add(new PropBox(b.X, 20, b.Z, b.Width / 2, 20, b.Depth / 2, 0));
            var sim = new EnemySim(5, null, new EnemyPaths(nav));
            // from one side of a built-up block to the far side
            NavBlock blk = nav.Blocks.First(k => !world.Walkable(new V3(k.Cx - nav.RingRadius, 0, k.Cz), new V3(k.Cx + nav.RingRadius, 0, k.Cz)));
            var from = new V3(blk.Cx - nav.RingRadius, 0, blk.Cz);
            var to = new V3(blk.Cx + nav.RingRadius, 0, blk.Cz);
            Enemy e = sim.Add(sim.AddSquad("x"), EnemyTable.Gunner, from, 0, new[] { to });
            Assert.IsFalse(world.Walkable(from, to), "the block is in the way");
            V3 last = e.Position;
            for (int i = 0; i < 60 * 120 && Math.Sqrt((e.Position.X - to.X) * (e.Position.X - to.X) + (e.Position.Z - to.Z) * (e.Position.Z - to.Z)) > 0.6; i++)
            {
                sim.Step(Dt, At(9999, 9999), world);
                Assert.IsTrue(world.Walkable(last, e.Position), "never through a wall");
                last = e.Position;
            }
            Assert.AreEqual(to.X, e.Position.X, 0.7, "it got there");
        }

        // ------------------------------------------------------------ patrols

        static (EnemySim Sim, EnemySquad Squad, Enemy E) Patrol()
        {
            var sim = new EnemySim(8);
            EnemySquad squad = sim.AddSquad("patrol", hostile: false);
            Enemy e = sim.Add(squad, EnemyTable.Gunner, new V3(0, 0, 0), 0, new[] { new V3(0, 0, 10), new V3(0, 0, 0) });
            sim.Add(squad, EnemyTable.Gunner, new V3(1, 0, 0), 0, new[] { new V3(1, 0, 10), new V3(1, 0, 0) });
            return (sim, squad, e);
        }

        [Test]
        public void APatrolSeesYouAndLetsYouBe()
        {
            var (sim, squad, e) = Patrol();
            var log = new List<EnemyEvent>();
            for (int i = 0; i < 60 * 10; i++)
            {
                sim.Step(Dt, At(6, 5), new Boxes());   // in plain view, off to the side of its beat
                log.AddRange(sim.Events);
            }
            Assert.IsFalse(squad.Hostile);
            Assert.AreEqual(EnemyState.Idle, e.State);
            Assert.IsFalse(log.Any(v => v.Kind == EnemyEventKind.Shot));
            Assert.Greater(e.Position.Z, 0.5, "and walks its beat");
        }

        [Test]
        public void StandingInItsFaceAimingAtItOrShootingNearItProvokesTheWholePatrol()
        {
            var (sim, squad, e) = Patrol();
            for (int i = 0; i < 60; i++) sim.Step(Dt, new PlayerView(e.Position + new V3(0, 0, 1), 1.6, 0, false), new Boxes());
            Assert.IsFalse(squad.Hostile, "a second close by is fine");
            for (int i = 0; i < 60; i++) sim.Step(Dt, new PlayerView(e.Position + new V3(0, 0, 1), 1.6, 0, false), new Boxes());
            Assert.IsTrue(squad.Hostile, "two is not");
            Assert.IsTrue(squad.All.All(m => m.State == EnemyState.Engaged));

            (sim, squad, e) = Patrol();
            sim.AimedAt(e, new V3(0, 0, 20), 0.5);
            Assert.IsFalse(squad.Hostile);
            sim.AimedAt(e, new V3(0, 0, 20), 0.6);
            Assert.IsTrue(squad.Hostile, "held in the sights for a second");

            (sim, squad, e) = Patrol();
            sim.AimedAt(e, new V3(0, 0, 50), 5);
            Assert.IsFalse(squad.Hostile, "too far to notice the aim");

            (sim, squad, e) = Patrol();
            sim.Hear(new V3(0, 0, 40));
            Assert.IsFalse(squad.Hostile, "a shot far off is none of its business");
            sim.Hear(new V3(0, 0, 20));
            Assert.IsTrue(squad.Hostile, "one close by is");
            Assert.AreEqual(squad.All.Count, sim.Events.Count(v => v.Kind == EnemyEventKind.Provoked));

            (sim, squad, e) = Patrol();
            sim.Hit(e, new EnemyHit(10, 10, false, false), new V3(0, 0, 30));
            Assert.IsTrue(squad.Hostile, "and so does being shot");
        }

        // ------------------------------------------------------------ the directors

        static EncounterDef Garage => new EncounterDef
        {
            Id = "enc_test", Tag = "jackal", Anchor = new V3(100, 0, 0),
            Members = new[]
            {
                new EncounterMember { Archetype = "gunner", Dx = 0, Dz = 0, Yaw = 0 },
                new EncounterMember { Archetype = "heavy", Dx = 2, Dz = 0, Yaw = 0, Beat = new[] { (2.0, 6.0), (2.0, 0.0) } },
            },
        };

        [Test]
        public void AnEncounterComesWhenYouComeNearGoesWhenYouLeaveAndOnceClearedStaysGone()
        {
            var sim = new EnemySim(1);
            var dir = new EncounterDirector(sim, new[] { Garage }, id => false);
            dir.Step(new V3(0, 0, 0));
            Assert.IsEmpty(sim.Enemies, "100 m off: not yet");
            dir.Step(new V3(30, 0, 0));
            Assert.AreEqual(2, sim.Enemies.Count);
            Assert.AreEqual(102, sim.Enemies[1].Position.X, 1e-9);
            Assert.AreEqual(2, sim.Enemies[1].Patrol.Count);

            dir.Step(new V3(-30, 0, 0));
            Assert.IsEmpty(sim.Enemies, "130 m off and not fighting: gone");
            dir.Step(new V3(30, 0, 0));
            Assert.AreEqual(2, sim.Enemies.Count, "back, fresh");
            Assert.IsTrue(sim.Enemies.All(x => x.Vitals.Health == x.Archetype.MaxHealth));

            foreach (Enemy x in sim.Enemies.ToList()) sim.Hit(x, new EnemyHit(999, 999, false, false), new V3(30, 0, 0));
            dir.Step(new V3(30, 0, 0));
            CollectionAssert.AreEqual(new[] { "enc_test" }, dir.Cleared);
            dir.Step(new V3(30, 0, 0));
            Assert.IsEmpty(dir.Cleared, "said once");
            dir.Step(new V3(-30, 0, 0));
            dir.Step(new V3(30, 0, 0));
            Assert.IsEmpty(sim.Enemies, "cleared stays cleared");

            var saved = new EncounterDirector(new EnemySim(1), new[] { Garage }, id => id == "enc_test");
            saved.Step(new V3(100, 0, 0));
            Assert.IsNull(saved.Live("enc_test"), "cleared in the save");
        }

        [Test]
        public void AnEncounterInAFightDoesNotVanishBehindYou()
        {
            var sim = new EnemySim(1);
            var dir = new EncounterDirector(sim, new[] { Garage }, id => false);
            dir.Step(new V3(30, 0, 0));
            sim.ShotAt(sim.Enemies[0], new V3(30, 0, 0));
            dir.Step(new V3(-30, 0, 0));
            Assert.AreEqual(2, sim.Enemies.Count);
        }

        [Test]
        public void BadEncountersFailWhenTheDirectorIsMade()
        {
            var sim = new EnemySim(1);
            Assert.Throws<ArgumentException>(() => new EncounterDirector(sim, new[] { Garage, Garage }, id => false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EncounterDirector(sim, new[]
            {
                new EncounterDef { Id = "x", Tag = "t", Members = new[] { new EncounterMember { Archetype = "dragon" } } },
            }, id => false));
            Assert.Throws<ArgumentException>(() => new EncounterDirector(sim, new[] { new EncounterDef { Id = "y", Tag = "t", Members = new EncounterMember[0] } }, id => false));
        }

        sealed class Blind : IEnemyWorld
        {
            public bool Clear(V3 from, V3 to) => false;

            public bool Walkable(V3 from, V3 to) => true;
        }

        [Test]
        public void PatrolsComeOutOfSightAtADistanceAndLeaveWhenFarAndNotFighting()
        {
            CityNav nav = CityNav.Build(CityPlan.Generate(CityPlan.DefaultSeed));
            var sim = new EnemySim(3, null, new EnemyPaths(nav));
            var dir = new PatrolDirector(sim, new EnemyPaths(nav), 77, "patrol");
            var me = new V3(0, 1.6, 0);
            for (int i = 0; i < 60 * 10; i++)
            {
                dir.Step(Dt, me, new Blind());
                sim.Step(Dt, new PlayerView(new V3(0, 0, 0), 1.6, 0, false), new Blind());
            }
            Assert.AreEqual(PatrolDirector.Squads, dir.Count);
            foreach (EnemySquad s in dir.All)
            {
                Assert.IsFalse(s.Hostile);
                Assert.That(s.All.Count, Is.InRange(2, 3));
                foreach (Enemy e in s.All)
                {
                    double d = Math.Sqrt(e.Position.X * e.Position.X + e.Position.Z * e.Position.Z);
                    Assert.That(d, Is.InRange(PatrolDirector.SpawnMin - 15, PatrolDirector.SpawnMax + 15), "about where they came out");
                }
            }

            // the player goes far away: they leave, and the next ones wait their turn
            var far = new V3(5000, 1.6, 0);
            dir.Step(Dt, far, new Blind());
            Assert.AreEqual(0, dir.Count);
            Assert.IsEmpty(sim.Enemies);
            dir.Step(Dt, me, new Blind());
            Assert.AreEqual(0, dir.Count, "not straight back");
        }

        [Test]
        public void PatrolsDoNotAppearInFrontOfYou()
        {
            CityNav nav = CityNav.Build(CityPlan.Generate(CityPlan.DefaultSeed));
            var sim = new EnemySim(3);
            var dir = new PatrolDirector(sim, new EnemyPaths(nav), 77, "patrol");
            var seesAll = new Boxes();   // nothing in the way: every node is in view
            for (int i = 0; i < 60 * 10; i++) dir.Step(Dt, new V3(0, 1.6, 0), seesAll);
            Assert.AreEqual(0, dir.Count);
        }

        [Test]
        public void RemovingASquadFreesItsCoverAndKeepsTheOthersIds()
        {
            var (sim, e, world) = GunnerBehindACrate();
            EnemySquad other = sim.AddSquad("other");
            Enemy keep = sim.Add(other, EnemyTable.Heavy, new V3(50, 0, 50), 0);
            for (int i = 0; i < 60 * 3; i++) sim.Step(Dt, At(0, 16), world);
            CoverPoint held = e.Cover;
            Assert.IsNotNull(held);
            sim.Remove(e.Squad);
            Assert.AreEqual(-1, held.TakenBy);
            Assert.AreSame(keep, sim.Find(keep.Id));
            Assert.IsNull(sim.Find(e.Id));
            Assert.Throws<ArgumentException>(() => sim.Remove(e.Squad));
        }
    }
}
