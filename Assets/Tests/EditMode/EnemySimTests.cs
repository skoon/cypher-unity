using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The enemies' minds in scripted fights: what they see and hear, how they come to fight, how they shoot and take turns, how they lose the player and give up.</summary>
    public class EnemySimTests
    {
        const double Dt = 1.0 / 60;

        /// <summary>A world of walls: vertical rectangles on the ground (x0, z0)-(x1, z1), tall enough to block any eye line.</summary>
        sealed class Walls : IEnemyWorld
        {
            public readonly List<(double X0, double Z0, double X1, double Z1)> List = new List<(double, double, double, double)>();

            public bool Clear(V3 from, V3 to)
            {
                foreach (var w in List)
                {
                    if (Crosses(from.X, from.Z, to.X, to.Z, w.X0, w.Z0, w.X1, w.Z1)) return false;
                }
                return true;
            }

            public bool Walkable(V3 from, V3 to) => Clear(from, to);

            static double Cross(double ax, double az, double bx, double bz, double cx, double cz) => (bx - ax) * (cz - az) - (bz - az) * (cx - ax);

            static bool Crosses(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz)
            {
                double d1 = Cross(cx, cz, dx, dz, ax, az), d2 = Cross(cx, cz, dx, dz, bx, bz), d3 = Cross(ax, az, bx, bz, cx, cz), d4 = Cross(ax, az, bx, bz, dx, dz);
                return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
            }
        }

        static PlayerView At(double x, double z, bool crouched = false, double speed = 0, bool alive = true) => new PlayerView(new V3(x, 0, z), crouched ? 1.0 : 1.6, speed, crouched, alive);

        /// <summary>Step for some seconds; every event, with the time it happened.</summary>
        static List<(double T, EnemyEvent E)> Run(EnemySim sim, double seconds, Func<double, PlayerView> player, IEnemyWorld world, double start = 0)
        {
            var log = new List<(double, EnemyEvent)>();
            int steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                double t = start + i * Dt;
                sim.Step(Dt, player(t), world);
                foreach (EnemyEvent e in sim.Events) log.Add((t, e));
            }
            return log;
        }

        static List<(double T, EnemyEvent E)> Run(EnemySim sim, double seconds, PlayerView player, IEnemyWorld world) => Run(sim, seconds, t => player, world);

        static (EnemySim Sim, Enemy Enemy) One(EnemyArchetype a = null, double yaw = 0, uint seed = 7)
        {
            var sim = new EnemySim(seed);
            EnemySquad squad = sim.AddSquad("jackal");
            Enemy e = sim.Add(squad, a ?? EnemyTable.Gunner, new V3(0, 0, 0), yaw);
            return (sim, e);
        }

        static double TimeTo(EnemySim sim, Enemy e, EnemyState state, PlayerView player, IEnemyWorld world, double limit = 30)
        {
            for (double t = 0; t < limit; t += Dt)
            {
                sim.Step(Dt, player, world);
                if (e.State == state) return t;
            }
            return double.PositiveInfinity;
        }

        // ------------------------------------------------------------ seeing

        [Test]
        public void ItSeesInFrontButNotBehindExceptRightUpClose()
        {
            var open = new Walls();
            var (sim, e) = One();
            Assert.Less(TimeTo(sim, e, EnemyState.Engaged, At(0, 20), open), 2, "in front, 20 m away");

            (sim, e) = One();
            Assert.AreEqual(double.PositiveInfinity, TimeTo(sim, e, EnemyState.Suspicious, At(0, -20), open, 10), "behind it, nothing");

            (sim, e) = One();
            Assert.Less(TimeTo(sim, e, EnemyState.Engaged, At(0, -2), open), 1, "but right behind it, it feels you");

            (sim, e) = One();
            Assert.AreEqual(double.PositiveInfinity, TimeTo(sim, e, EnemyState.Suspicious, At(0, 60), open, 10), "out of sight range");
        }

        [Test]
        public void FarAwayOrCrouchedItTakesLongerToBeSure()
        {
            var open = new Walls();
            var (simFar, far) = One();
            double tFar = TimeTo(simFar, far, EnemyState.Engaged, At(0, 35), open);
            var (simLow, low) = One();
            double tLow = TimeTo(simLow, low, EnemyState.Engaged, At(0, 35, crouched: true), open);
            var (simNear, n) = One();
            double tNear = TimeTo(simNear, n, EnemyState.Engaged, At(0, 5), open);
            Assert.Less(tNear, tFar);
            Assert.Less(tFar, tLow, "crouching hides you for longer");
            Assert.Less(tNear, 0.6, "close in: almost at once");
            Assert.That(tFar, Is.InRange(0.6, 3.0), "at 35 m: a second or two");
        }

        [Test]
        public void AWallHidesYou()
        {
            var walls = new Walls();
            walls.List.Add((-5, 10, 5, 10));
            var (sim, e) = One();
            Assert.AreEqual(double.PositiveInfinity, TimeTo(sim, e, EnemyState.Suspicious, At(0, 20), walls, 10));
            Assert.IsFalse(e.SeesPlayer);
        }

        [Test]
        public void AGlimpseMakesItSuspiciousAndItGivesUpWhenNothingMoreComes()
        {
            var open = new Walls();
            var walls = new Walls();
            walls.List.Add((-50, 30, 50, 30));
            var (sim, e) = One();
            // a fifth of a second in view at 40 m, then behind a wall
            Run(sim, 0.2, At(0, 40), open);
            Assert.AreEqual(EnemyState.Suspicious, e.State);
            Assert.Less(e.Awareness, 1);
            Run(sim, 2, At(0, 40), walls);
            Assert.AreEqual(EnemyState.Suspicious, e.State);
            Assert.Greater(e.Position.Z, 0.5, "it walks over to look");
            Run(sim, EnemySim.SuspiciousFor + 4, At(0, 40), walls);
            Assert.AreEqual(EnemyState.Idle, e.State, "and gives up");
            Run(sim, 30, At(0, 40), walls);
            Assert.AreEqual(0, e.Position.Z, 0.31, "back at its post");
        }

        // ------------------------------------------------------------ hearing and being hit

        [Test]
        public void AShotInEarshotTurnsItToLookAndOneOutOfEarshotDoesNot()
        {
            var walls = new Walls();
            walls.List.Add((-100, -5, 100, -5));
            var (sim, e) = One();
            sim.Hear(new V3(30, 0, 0));
            Assert.AreEqual(EnemyState.Suspicious, e.State);
            Run(sim, 1, At(0, -50), walls);
            Assert.AreEqual(Math.PI / 2, e.Yaw, 0.05, "it turned to the sound");

            var (sim2, e2) = One();
            sim2.Hear(new V3(0, 0, EnemyTable.Gunner.HearingRange + 1));
            Assert.AreEqual(EnemyState.Idle, e2.State);
        }

        [Test]
        public void BeingHitStartsTheFightAtOnceAndTheKillingHitEndsIt()
        {
            var (sim, e) = One();
            var from = new V3(10, 0, -10);
            Assert.IsFalse(sim.Hit(e, new EnemyHit(30, 30, false, false), from));
            Assert.AreEqual(EnemyState.Engaged, e.State);
            Assert.AreEqual(from.X, e.Target.X);
            Assert.IsTrue(sim.Events.Any(v => v.Kind == EnemyEventKind.Hurt && v.Damage == 30));
            Assert.IsTrue(sim.Hit(e, new EnemyHit(200, 200, false, true), from));
            Assert.AreEqual(EnemyState.Down, e.State);
            Assert.IsTrue(sim.Events.Any(v => v.Kind == EnemyEventKind.Died));
            Assert.IsFalse(sim.Hit(e, new EnemyHit(200, 200, false, true), from), "the dead are not killed twice");
            var log = Run(sim, 5, At(0, 10), new Walls());
            Assert.IsEmpty(log, "the dead do nothing");
            Assert.IsTrue(sim.Harm(One().Enemy, 0) == false);
        }

        // ------------------------------------------------------------ shooting

        [Test]
        public void ItWaitsItsReactionTimeThenFiresInBurstsOfItsSize()
        {
            var (sim, e) = One();
            var log = Run(sim, 30, At(0, 15), new Walls());
            double engaged = log.First(x => x.E.Kind == EnemyEventKind.Engaged).T;
            List<double> shots = log.Where(x => x.E.Kind == EnemyEventKind.Shot).Select(x => x.T).ToList();
            Assert.GreaterOrEqual(shots[0] - engaged, EnemyTable.Gunner.ReactionTime - Dt, "no shot before its reaction time");

            // shots closer together than half a pause are one burst
            var bursts = new List<List<double>>();
            foreach (double t in shots)
            {
                if (bursts.Count == 0 || t - bursts[^1][^1] > EnemyTable.Gunner.BurstPause * 0.5) bursts.Add(new List<double>());
                bursts[^1].Add(t);
            }
            Assert.Greater(bursts.Count, 8);
            foreach (List<double> b in bursts.Take(bursts.Count - 1))
            {
                Assert.That(b.Count, Is.InRange(EnemyTable.Gunner.BurstMin, EnemyTable.Gunner.BurstMax));
                for (int i = 1; i < b.Count; i++) Assert.That(b[i] - b[i - 1], Is.InRange(EnemyTable.Gunner.ShotInterval - 1e-9, EnemyTable.Gunner.ShotInterval + Dt + 1e-9));
            }
            Assert.AreEqual(log.Count(x => x.E.Kind == EnemyEventKind.BurstStart), bursts.Count);
        }

        [Test]
        public void OverAFightItHitsAboutAsOftenAsTheDamageModelSays()
        {
            var (sim, e) = One(seed: 99);
            var log = Run(sim, 400, At(0, 15), new Walls());
            var shots = log.Where(x => x.E.Kind == EnemyEventKind.Shot).Select(x => x.E).ToList();
            double rate = shots.Count(s => s.Hit) / (double)shots.Count;
            // the burst's settling makes the average lie between the first shot's chance and a settled one
            double first = DamageModel.EnemyHitChance(EnemyTable.Gunner, 15, 0, false, 0), settled = DamageModel.EnemyHitChance(EnemyTable.Gunner, 15, 0, false, 1);
            Assert.That(rate, Is.InRange(first, settled + 0.04));
            Assert.IsTrue(shots.Where(s => s.Hit).All(s => s.Damage == EnemyTable.Gunner.ShotDamage));
            Assert.IsTrue(shots.Where(s => !s.Hit).All(s => s.Damage == 0));
            double dps = shots.Sum(s => s.Damage) / 400;
            Assert.That(dps, Is.InRange(3.0, 8.0), "about what step 1's estimate gave (5.9 before armour)");
        }

        [Test]
        public void AFastOrCrouchedTargetIsHitLessOften()
        {
            double Rate(PlayerView p, uint seed)
            {
                var (sim, _) = One(seed: seed);
                var shots = Run(sim, 300, p, new Walls()).Where(x => x.E.Kind == EnemyEventKind.Shot).ToList();
                return shots.Count(x => x.E.Hit) / (double)shots.Count;
            }
            double still = Rate(At(0, 15), 5), running = Rate(At(0, 15, speed: 6.5), 5), crouched = Rate(At(0, 15, crouched: true), 5);
            Assert.Less(running, still * 0.7);
            Assert.Less(crouched, still * 0.95);
        }

        [Test]
        public void ASquadTakesTurnsSoNeverMoreThanHalfAreInABurst()
        {
            var sim = new EnemySim(3);
            EnemySquad squad = sim.AddSquad("jackal");
            for (int i = 0; i < 4; i++) sim.Add(squad, EnemyTable.Gunner, new V3(i * 2 - 3, 0, 0), 0);
            Assert.AreEqual(2, squad.MaxFiring);
            int most = 0;
            for (int i = 0; i < 60 * 20; i++)
            {
                sim.Step(Dt, At(0, 16), new Walls());
                int firing = squad.All.Count(m => m.Firing);
                Assert.LessOrEqual(firing, squad.MaxFiring);
                most = Math.Max(most, firing);
            }
            Assert.AreEqual(2, most, "and they do fight two at a time");

            sim.Hit(squad.All[0], new EnemyHit(999, 999, false, false), new V3(0, 0, 16));
            sim.Hit(squad.All[1], new EnemyHit(999, 999, false, false), new V3(0, 0, 16));
            Assert.AreEqual(1, squad.MaxFiring, "two left: one at a time");
        }

        // ------------------------------------------------------------ the squad

        [Test]
        public void OneThatEngagesCallsInTheRestOfItsSquadNearby()
        {
            var walls = new Walls();
            walls.List.Add((5, -50, 5, 50));   // the player, to the east, is hidden from all but the first
            var sim = new EnemySim(11);
            EnemySquad squad = sim.AddSquad("jackal");
            Enemy spotter = sim.Add(squad, EnemyTable.Gunner, new V3(10, 0, 0), Math.PI / 2);
            Enemy mate = sim.Add(squad, EnemyTable.Heavy, new V3(0, 0, 0), Math.PI);
            Enemy far = sim.Add(squad, EnemyTable.Gunner, new V3(-50, 0, 0), Math.PI);
            EnemySquad others = sim.AddSquad("other");
            Enemy stranger = sim.Add(others, EnemyTable.Gunner, new V3(0, 0, 2), Math.PI);

            var log = Run(sim, 3, At(25, 0), walls);
            double spotted = log.First(x => x.E.Kind == EnemyEventKind.Engaged && x.E.Enemy == spotter.Id).T;
            double joined = log.First(x => x.E.Kind == EnemyEventKind.Engaged && x.E.Enemy == mate.Id).T;
            Assert.That(joined - spotted, Is.InRange(EnemySim.CalloutDelayMin - Dt, EnemySim.CalloutDelayMax + Dt), "the call takes a moment");
            Assert.AreEqual(25, mate.Target.X, 1e-9, "and says where");
            Assert.AreEqual(EnemyState.Idle, far.State, "out of earshot of the call");
            Assert.AreEqual(EnemyState.Idle, stranger.State, "another squad is not told");
        }

        // ------------------------------------------------------------ keeping range, losing the player

        [Test]
        public void TheHeavyPushesInAndTheGunnerKeepsItsDistance()
        {
            var (simH, heavy) = One(EnemyTable.Heavy);
            Run(simH, 12, At(0, 30), new Walls());
            Assert.LessOrEqual(30 - heavy.Position.Z, EnemyTable.Heavy.PreferredRange * 1.25 + 0.1, "the heavy came in close");

            var (simG, gunner) = One(EnemyTable.Gunner);
            Run(simG, 12, At(0, 30), new Walls());
            double d = 30 - gunner.Position.Z;
            Assert.That(d, Is.InRange(EnemyTable.Gunner.PreferredRange * 0.6, EnemyTable.Gunner.PreferredRange * 1.25 + 0.1), "the gunner came to its range and stopped");

            var (simB, backer) = One(EnemyTable.Gunner);
            Run(simB, 6, At(0, 4), new Walls());
            Assert.Less(backer.Position.Z, 0, "too close: it backs off");
        }

        [Test]
        public void LosingThePlayerSendsItSearchingThenHomeAndADeadPlayerIsNotShot()
        {
            // seen at (0, 15), the player slips away east behind a long wall; the enemy goes to where it saw them and finds nobody
            var walls = new Walls();
            var (sim, e) = One();
            Run(sim, 3, At(0, 15), walls);
            Assert.AreEqual(EnemyState.Engaged, e.State);
            walls.List.Add((20, -100, 20, 100));
            var log = Run(sim, EnemySim.LoseAfter + 1, At(40, 15), walls);
            Assert.AreEqual(EnemyState.Searching, e.State);
            Assert.IsFalse(log.Any(x => x.E.Kind == EnemyEventKind.Shot && x.T > 0.2), "no shots through a wall");
            Assert.AreEqual(15, e.Target.Z, 1e-9, "searching where it last saw them");
            Run(sim, EnemySim.SearchFor + 1, At(40, 15), walls);
            Assert.AreEqual(EnemyState.Idle, e.State);

            var (sim2, e2) = One();
            Run(sim2, 3, At(0, 15), new Walls());
            var after = Run(sim2, 3, At(0, 15, alive: false), new Walls());
            Assert.AreEqual(EnemyState.Searching, e2.State);
            Assert.IsFalse(after.Any(x => x.E.Kind == EnemyEventKind.Shot));
        }

        // ------------------------------------------------------------ the same fight twice

        static string Fight(uint seed)
        {
            var sim = new EnemySim(seed);
            EnemySquad squad = sim.AddSquad("jackal");
            sim.Add(squad, EnemyTable.Gunner, new V3(-4, 0, 0), 0, new[] { new V3(-4, 0, 6), new V3(4, 0, 6) });
            sim.Add(squad, EnemyTable.Heavy, new V3(4, 0, 0), 0);
            var log = Run(sim, 40, t => At(Math.Sin(t * 0.3) * 10, 25 - t * 0.2, speed: 1.5), new Walls());
            return string.Join("\n", log.Select(x => $"{x.T:F4} {x.E.Kind} {x.E.Enemy} {x.E.Hit} {x.E.Damage}"))
                + "\n" + string.Join(" ", sim.Enemies.Select(e => $"{e.Position.X:F6},{e.Position.Z:F6},{e.Yaw:F6},{e.State}"));
        }

        [Test]
        public void TheSameSeedPlaysTheSameFight()
        {
            Assert.AreEqual(Fight(42), Fight(42));
            Assert.AreNotEqual(Fight(42), Fight(43), "and another seed another");
        }

        [Test]
        public void BadInputsFailLoudly()
        {
            var (sim, _) = One();
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Step(0, At(0, 1), new Walls()));
            Assert.Throws<ArgumentNullException>(() => sim.Step(Dt, At(0, 1), null));
            Assert.Throws<ArgumentException>(() => sim.Add(new EnemySim(1).AddSquad("x"), EnemyTable.Gunner, default, 0));
            Assert.Throws<ArgumentNullException>(() => sim.Add(sim.Squads[0], null, default, 0));
        }
    }
}
