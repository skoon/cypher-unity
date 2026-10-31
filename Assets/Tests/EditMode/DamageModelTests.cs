using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class DamageModelTests
    {
        static readonly ShooterStats Plain = new ShooterStats(1, 0, 1.5);

        /// <summary>A new character, with the numbers the RPG derives for its starting attributes.</summary>
        static RpgCore NewCharacter() => new RpgCore(seed: 20771, storage: new MemorySaveStorage(), clock: () => 1700000000000) { AutosaveEnabled = false };

        static ShooterStats StatsOf(RpgCore core) => new ShooterStats(core.Derived["rangedDamage"], core.Derived["critChance"], core.Derived["critDamage"]);

        [Test]
        public void TheTwoKindsAreFoundByIdAndAreWhatTheirNamesSay()
        {
            CollectionAssert.AreEqual(new[] { "gunner", "heavy" }, EnemyTable.All.Select(a => a.Id));
            Assert.AreSame(EnemyTable.Heavy, EnemyTable.Get("heavy"));
            Assert.Throws<ArgumentOutOfRangeException>(() => EnemyTable.Get("netrunner"));
            foreach (EnemyArchetype a in EnemyTable.All)
            {
                Assert.Greater(a.MaxHealth, 0, a.Id);
                Assert.GreaterOrEqual(a.BurstMax, a.BurstMin, a.Id);
                Assert.Greater(a.BurstMin, 0, a.Id);
                Assert.That(a.Accuracy, Is.InRange(0.0, 1.0), a.Id);
                Assert.DoesNotThrow(() => WeaponTable.Get(a.WeaponId), a.Id);
            }
            Assert.Greater(EnemyTable.Heavy.Armor, EnemyTable.Gunner.Armor, "the heavy is armoured");
            Assert.Greater(EnemyTable.Heavy.HeadMultiplier, EnemyTable.Gunner.HeadMultiplier, "and its head is the weak spot");
            Assert.Less(EnemyTable.Heavy.PreferredRange, EnemyTable.Gunner.PreferredRange, "it pushes in; the gunner keeps back");
        }

        [Test]
        public void ArmourIsTheRpgsRule()
        {
            Assert.AreEqual(100, DamageModel.Mitigate(100, 0), 1e-12);
            Assert.AreEqual(50, DamageModel.Mitigate(100, 100), 1e-12);
            RpgCore core = NewCharacter();
            double before = core.Health;
            core.Damage(30);
            Assert.AreEqual(before - DamageModel.Mitigate(30, core.Derived["armor"]), core.Health, 1e-9, "the same rule the player's armour uses");
            Assert.Throws<ArgumentOutOfRangeException>(() => DamageModel.Mitigate(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => DamageModel.Mitigate(1, double.NaN));
        }

        [Test]
        public void AShotScalesByTheGunTheChargeRangedDamageACritAndTheHeadLessArmour()
        {
            WeaponConfig wasp = WeaponTable.Get("wasp");
            var shooter = new ShooterStats(1.25, 0.1, 1.5);
            EnemyHit body = DamageModel.PlayerShot(wasp, 1, shooter, EnemyTable.Gunner, head: false, critRoll: 0.5);
            Assert.AreEqual(13 * 1.25, body.Raw, 1e-12);
            Assert.AreEqual(13 * 1.25 * 100 / 110, body.Damage, 1e-12);
            Assert.IsFalse(body.Critical);

            EnemyHit crit = DamageModel.PlayerShot(wasp, 1, shooter, EnemyTable.Gunner, head: false, critRoll: 0.05);
            Assert.IsTrue(crit.Critical, "a roll under the crit chance");
            Assert.AreEqual(body.Damage * 1.5, crit.Damage, 1e-12);

            EnemyHit head = DamageModel.PlayerShot(wasp, 1, shooter, EnemyTable.Heavy, head: true, critRoll: 0.5);
            Assert.AreEqual(13 * 1.25 * 3 * 100 / 160, head.Damage, 1e-12);
            Assert.IsTrue(head.Head);

            WeaponConfig rail = WeaponTable.Get("voltline");
            Assert.AreEqual(DamageModel.PlayerShot(rail, 1, Plain, EnemyTable.Heavy, false, 0.5).Damage / 2,
                DamageModel.PlayerShot(rail, 0.5, Plain, EnemyTable.Heavy, false, 0.5).Damage, 1e-12, "half a charge, half the damage");

            Assert.Throws<ArgumentOutOfRangeException>(() => DamageModel.PlayerShot(wasp, 1, Plain, EnemyTable.Gunner, false, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => DamageModel.PlayerShot(wasp, -0.1, Plain, EnemyTable.Gunner, false, 0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShooterStats(1, 1.2, 1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShooterStats(1, 0.1, 0.9));
        }

        [Test]
        public void AnEnemysAimFallsOffWithDistanceMovementCrouchingAndAnUnsettledFirstShot()
        {
            EnemyArchetype g = EnemyTable.Gunner;
            double close = DamageModel.EnemyHitChance(g, 0, 0, false, 1);
            Assert.AreEqual(g.Accuracy, close, 1e-12, "point blank, still, settled: its accuracy");
            Assert.AreEqual(close / 2, DamageModel.EnemyHitChance(g, g.HalfAccuracyRange, 0, false, 1), 1e-12, "half at the half-accuracy range");
            double previous = close;
            for (double d = 1; d < 120; d += 1)
            {
                double p = DamageModel.EnemyHitChance(g, d, 0, false, 1);
                Assert.Less(p, previous, "further is harder");
                previous = p;
            }
            Assert.AreEqual(close * DamageModel.MovingTargetFactor, DamageModel.EnemyHitChance(g, 0, 6.5, false, 1), 1e-12, "a sprinting target");
            Assert.AreEqual(close * DamageModel.MovingTargetFactor, DamageModel.EnemyHitChance(g, 0, 20, false, 1), 1e-12, "no less than that, however fast");
            Assert.AreEqual(close * DamageModel.CrouchFactor, DamageModel.EnemyHitChance(g, 0, 0, true, 1), 1e-12);
            Assert.AreEqual(close * DamageModel.UnsettledAim, DamageModel.EnemyHitChance(g, 0, 0, false, 0), 1e-12, "the first shot before the aim settles");
            Assert.AreEqual(close, DamageModel.EnemyHitChance(g, 0, 0, false, 5), 1e-12, "settled is settled");
            Assert.Throws<ArgumentOutOfRangeException>(() => DamageModel.EnemyHitChance(g, -1, 0, false, 1));
        }

        [Test]
        public void TheKillingHitIsReportedOnceAndTheDeadTakeNoMore()
        {
            var v = new EnemyVitals(EnemyTable.Gunner);
            Assert.AreEqual(110, v.Health);
            Assert.IsFalse(v.Take(new EnemyHit(100, 100, false, false)));
            Assert.IsTrue(v.Take(new EnemyHit(30, 30, false, false)), "the hit that empties it");
            Assert.AreEqual(0, v.Health, "never below zero");
            Assert.IsFalse(v.Take(new EnemyHit(30, 30, false, false)), "a corpse is not killed twice");
            Assert.IsFalse(v.Take(5.0));
            var h = new EnemyVitals(EnemyTable.Heavy);
            Assert.IsTrue(h.Take(400.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyVitals(EnemyTable.Gunner).Take(-1.0));
        }

        // The balance as first guessed, pinned so a change to the table is a decision, not an accident. A new character, no crits.

        static int HitsToKill(string gun, EnemyArchetype target, bool head, ShooterStats shooter)
        {
            double each = DamageModel.PlayerShot(WeaponTable.Get(gun), 1, new ShooterStats(shooter.RangedDamage, 0, shooter.CritDamage), target, head, 0.5).Damage;
            return (int)Math.Ceiling(target.MaxHealth / each);
        }

        [Test]
        public void ANewCharacterPutsThemDownInAReasonableNumberOfHits()
        {
            ShooterStats me = StatsOf(NewCharacter());
            Assert.That(HitsToKill("wasp", EnemyTable.Gunner, false, me), Is.InRange(6, 12), "a gunner: a short burst of the SMG");
            Assert.That(HitsToKill("magnat", EnemyTable.Gunner, false, me), Is.InRange(1, 3), "or a couple of revolver rounds");
            Assert.That(HitsToKill("wasp", EnemyTable.Heavy, false, me), Is.GreaterThan(25), "a heavy shrugs off the SMG to the body");
            Assert.That(HitsToKill("wasp", EnemyTable.Heavy, true, me), Is.LessThanOrEqualTo(15), "but not to the head");
            Assert.That(HitsToKill("magnat", EnemyTable.Heavy, false, me), Is.InRange(4, 8), "the revolver is the answer to it");
        }

        /// <summary>Seconds an enemy takes to put down a new character standing still at a distance, on average: its bursts, its chance per shot, its damage after armour.</summary>
        static double SecondsToKillThePlayer(EnemyArchetype e, double distance)
        {
            RpgCore me = NewCharacter();
            double shots = (e.BurstMin + e.BurstMax) / 2.0;
            double cycle = (shots - 1) * e.ShotInterval + e.BurstPause;
            double hits = 0;
            for (int i = 0; i < (int)Math.Round(shots); i++)
            {
                double aimed = i * e.ShotInterval / e.AimTime;   // the aim settles through the burst
                hits += DamageModel.EnemyHitChance(e, distance, 0, false, aimed);
            }
            double dps = hits * DamageModel.Mitigate(e.ShotDamage, me.Derived["armor"]) / cycle;
            return me.Health / dps;
        }

        [Test]
        public void OneEnemyAloneTakesAWhileToPutANewCharacterDown()
        {
            Assert.That(SecondsToKillThePlayer(EnemyTable.Gunner, 15), Is.InRange(15.0, 60.0), "a gunner at its range: a fight, not an ambush");
            Assert.That(SecondsToKillThePlayer(EnemyTable.Heavy, 8), Is.InRange(10.0, 40.0), "a heavy up close is the bigger threat");
            Assert.Less(SecondsToKillThePlayer(EnemyTable.Heavy, 8), SecondsToKillThePlayer(EnemyTable.Gunner, 15));
        }
    }
}
