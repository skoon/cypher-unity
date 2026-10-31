using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The enemies' bodies following their brains: made and dropped with them, dressed in the gang look, raising the gun, kicking, flinching and falling.</summary>
    public class EnemyBodiesTests
    {
        const double Dt = 1.0 / 60;

        sealed class Open : IEnemyWorld
        {
            public bool Clear(V3 from, V3 to) => true;
            public bool Walkable(V3 from, V3 to) => true;
        }

        static readonly Open World = new Open();

        static PlayerView Player(double x, double z) => new PlayerView(new V3(x, 0, z), 1.7, 0, false);

        static (EnemySim Sim, Enemy E, EnemyBodies Bodies) OneGunner(EnemyArchetype kind = null)
        {
            var sim = new EnemySim(5);
            Enemy e = sim.Add(sim.AddSquad("jackal"), kind ?? EnemyTable.Gunner, new V3(0, 0, 0), 0);
            return (sim, e, new EnemyBodies(11));
        }

        static void Run(EnemySim sim, EnemyBodies bodies, PlayerView player, double seconds, Action<int> each = null)
        {
            for (int i = 0; i < (int)Math.Round(seconds / Dt); i++)
            {
                sim.Step(Dt, player, World);
                bodies.Step(Dt, sim, player.Chest);
                each?.Invoke(i);
            }
        }

        [Test]
        public void ABodyComesWithItsEnemyAndGoesWithIt()
        {
            var (sim, e, bodies) = OneGunner();
            bodies.Step(Dt, sim, Player(0, 100).Chest);
            Assert.AreEqual(1, bodies.All.Count);
            CombatBody b = bodies.Find(e.Id);
            Assert.IsNotNull(b);
            Assert.AreEqual(e.Position.X, b.PrevX);
            Assert.AreEqual(e.Position.Z, b.PrevZ, "it starts where it stands, not sliding in from the origin");
            sim.Remove(e.Squad);
            bodies.Step(Dt, sim, Player(0, 100).Chest);
            Assert.AreEqual(0, bodies.All.Count);
        }

        [Test]
        public void TooManyFightersIsABug()
        {
            var sim = new EnemySim(5);
            EnemySquad s = sim.AddSquad("jackal");
            for (int i = 0; i <= EnemyBodies.Max; i++) sim.Add(s, EnemyTable.Gunner, new V3(i, 0, 0), 0);
            Assert.Throws<InvalidOperationException>(() => new EnemyBodies(1).Step(Dt, sim, Player(0, 100).Chest));
        }

        [Test]
        public void TheGangLookIsTheSameEachTimeAndMarksThemOut()
        {
            var (sim, e, _) = OneGunner();
            Enemy heavy = sim.Add(e.Squad, EnemyTable.Heavy, new V3(3, 0, 0), 0);
            CombatBody a = EnemyLook.Dress(e, 11), again = EnemyLook.Dress(e, 11), h = EnemyLook.Dress(heavy, 11);
            Assert.AreEqual(a.Appearance.Coat, again.Appearance.Coat);
            Assert.AreEqual(a.Appearance.Scale, again.Appearance.Scale);
            foreach (CombatBody b in new[] { a, h })
            {
                Assert.IsTrue(b.Appearance.Trim);
                Assert.AreEqual(EnemyLook.GangColour, b.Appearance.TrimColour);
                Assert.IsTrue(b.Visor);
                Assert.IsFalse(b.Appearance.Umbrella);
                Assert.IsFalse(b.Appearance.Pack);
                CollectionAssert.Contains(EnemyLook.Coat, b.Appearance.Coat);
            }
            Assert.AreEqual(HeldWeapon.Smg, a.Weapon);
            Assert.AreEqual(HeldWeapon.Revolver, h.Weapon);
            Assert.Greater(h.Appearance.Build, 1.2, "the heavy is broad");
            Assert.IsFalse(h.Appearance.LongCoat);
            Assert.IsFalse(h.Appearance.Hood);
        }

        [Test]
        public void EveryArchetypeHasAGunInTheHand()
        {
            foreach (EnemyArchetype a in EnemyTable.All) Assert.AreNotEqual(HeldWeapon.None, EnemyLook.WeaponOf(a), a.Id);
        }

        [Test]
        public void TheGunComesUpWhenItEngagesAndAllTheWayWhenItFires()
        {
            var (sim, e, bodies) = OneGunner();
            CombatBody b = null;
            bool sawFiringAtFull = false;
            Run(sim, bodies, Player(0, 15), 6, i =>
            {
                b = bodies.Find(e.Id);
                if (e.State == EnemyState.Idle) Assert.AreEqual(0, b.Aim, 1e-9, "at ease it is down");
                if (e.Firing && b.Aim >= 1) sawFiringAtFull = true;
            });
            Assert.AreEqual(EnemyState.Engaged, e.State);
            Assert.IsTrue(sawFiringAtFull);
            Assert.Greater(b.Aim, 0.25, "fighting, it stays up");
        }

        [Test]
        public void EachShotKicksTheGun()
        {
            var (sim, e, bodies) = OneGunner();
            int shots = 0;
            Run(sim, bodies, Player(0, 15), 6, i =>
            {
                if (sim.Events.Any(v => v.Kind == EnemyEventKind.Shot && v.Enemy == e.Id))
                {
                    Assert.AreEqual(1, bodies.Find(e.Id).Recoil);
                    shots++;
                }
            });
            Assert.Greater(shots, 2);
        }

        [Test]
        public void ItPointsTheGunUpAtAPlayerAboveIt()
        {
            var (sim, e, bodies) = OneGunner();
            var player = new PlayerView(new V3(0, 6, 12), 1.7, 0, false);
            Run(sim, bodies, player, 1);
            CombatBody b = bodies.Find(e.Id);
            double flat = Math.Sqrt(b.X * b.X + (12 - b.Z) * (12 - b.Z));
            Assert.AreEqual(Math.Atan2(6 + 1.7 * 0.75 - EnemySim.EyeHeight * b.Appearance.Scale, flat), b.AimPitch, 1e-9);
            Assert.Greater(b.AimPitch, 0.2);
        }

        [Test]
        public void AHitMakesItFlinchAwayFromThePlayerAndItWearsOff()
        {
            var (sim, e, bodies) = OneGunner();
            PlayerView player = Player(5, 5);   // ahead and off to one side
            Run(sim, bodies, player, 0.1);
            sim.Hit(e, new EnemyHit(10, 10, false, false), player.Eye);
            bodies.Step(Dt, sim, player.Chest);
            CombatBody b = bodies.Find(e.Id);
            Assert.AreEqual(1, b.Flinch);
            Assert.AreEqual(Math.Sin(CrowdSim.ShortestAngle(e.Yaw, Math.Atan2(5 - e.Position.X, 5 - e.Position.Z))), b.Side, 1e-9);
            Assert.AreNotEqual(0, b.Side);
            for (int i = 0; i < 30; i++) bodies.Step(Dt, sim, player.Chest);
            Assert.Less(b.Flinch, 0.05);
        }

        [Test]
        public void ADeadFighterFallsOverAFallAndStaysDownWithTheGunLowered()
        {
            var (sim, e, bodies) = OneGunner();
            PlayerView player = Player(0, 15);
            Run(sim, bodies, player, 3);
            Assert.Greater(bodies.Find(e.Id).Aim, 0);
            Assert.IsTrue(sim.Harm(e, 1e6));
            double half = EnemyBodies.FallSeconds / 2;
            Run(sim, bodies, player, half);
            CombatBody b = bodies.Find(e.Id);
            Assert.AreEqual(0.5, b.Death, 2 * Dt / EnemyBodies.FallSeconds);
            Run(sim, bodies, player, 2);
            Assert.AreEqual(1, b.Death);
            Assert.AreEqual(0, b.Aim);
            Assert.AreEqual(0, b.Speed);
        }

        [Test]
        public void ItWalksAtItsEnemysPaceAndThePhaseStaysInOneTurn()
        {
            var sim = new EnemySim(5);
            var beat = new[] { new V3(0, 0, 0), new V3(0, 0, 30) };
            Enemy e = sim.Add(sim.AddSquad("jackal", hostile: false), EnemyTable.Gunner, beat[0], 0, beat);
            var bodies = new EnemyBodies(3);
            double maxPhase = 0;
            Run(sim, bodies, Player(80, 80), 8, i =>
            {
                CombatBody b = bodies.Find(e.Id);
                Assert.AreEqual(e.Speed, b.Speed);
                Assert.AreEqual(e.Position.Z, b.Z);
                Assert.LessOrEqual(b.Phase, Math.PI * 2);
                Assert.LessOrEqual(b.Phase - b.PrevPhase, 0.2, "no jump across the turn");
                maxPhase = Math.Max(maxPhase, b.Phase);
            });
            Assert.Greater(e.Position.Z, 5, "it walked");
            Assert.Greater(maxPhase, 5);
        }

        [Test]
        public void ThePoseInputCarriesEverything()
        {
            var (sim, e, bodies) = OneGunner(EnemyTable.Heavy);
            Run(sim, bodies, Player(0, 10), 2);
            CombatBody b = bodies.Find(e.Id);
            PoseInput p = b.Pose(0.5, 9);
            Assert.AreEqual(b.Aim, p.Aim);
            Assert.AreEqual(b.Recoil, p.Recoil);
            Assert.AreEqual(HeldWeapon.Revolver, p.Weapon);
            Assert.IsTrue(p.Visor);
            Assert.AreEqual(b.Appearance.Build, p.Build);
            Assert.AreEqual(0.5, p.Alpha);
            Assert.IsNotNull(PedestrianPose.Pose(p).Gun);
        }
    }
}
