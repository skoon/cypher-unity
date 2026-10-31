using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The fight's small rules: the hit direction arcs, where a missed shot goes, the Trauma Team, and the directors clearing out after a flatline.</summary>
    public class FightRulesTests
    {
        // ---------------------------------------------------------------- arcs

        [Test]
        public void AnArcPointsAtTheShooterRelativeToWhereThePlayerFaces()
        {
            var arcs = new DamageArcs();
            arcs.Add(DamageArcs.BearingTo(0, 0, 10, 0), 1);   // the shooter is off along +X
            Assert.AreEqual(1, arcs.Count);
            Assert.AreEqual(Math.PI / 2, arcs.Get(0, 0).Relative, 1e-9, "facing +Z, a shooter at +X is to the right");
            Assert.AreEqual(0, arcs.Get(0, Math.PI / 2).Relative, 1e-9, "and dead ahead once the player turns to it");
            Assert.AreEqual(1, arcs.Get(0, 0).Alpha, 1e-9);
        }

        [Test]
        public void ArcsFadeAndGo()
        {
            var arcs = new DamageArcs();
            arcs.Add(0, 1);
            arcs.Update(DamageArcs.Life / 2);
            Assert.AreEqual(0.5, arcs.Get(0, 0).Alpha, 1e-9);
            arcs.Update(DamageArcs.Life / 2 + 0.01);
            Assert.AreEqual(0, arcs.Count);
        }

        [Test]
        public void HitsFromTheSameWayFeedOneArcAndFromOtherWaysMakeMore()
        {
            var arcs = new DamageArcs();
            arcs.Add(0, 0.5);
            arcs.Update(0.5);
            double faded = arcs.Get(0, 0).Alpha;
            arcs.Add(0.2, 0.3);
            Assert.AreEqual(1, arcs.Count);
            Assert.AreEqual(0.2, arcs.Get(0, 0).Relative, 1e-9, "turned to the latest hit");
            Assert.Greater(arcs.Get(0, 0).Alpha, faded, "and stronger than it had faded to");
            arcs.Add(Math.PI, 0.5);
            arcs.Add(-Math.PI / 2, 0.5);
            Assert.AreEqual(3, arcs.Count);
        }

        [Test]
        public void ThereAreNeverMoreThanTheMostAndTheFaintestMakesWay()
        {
            var arcs = new DamageArcs();
            for (int i = 0; i < DamageArcs.Max; i++)
            {
                arcs.Add(i * 1.2, 1);
                arcs.Update(0.1);
            }
            arcs.Add(-1.4, 1);
            Assert.AreEqual(DamageArcs.Max, arcs.Count);
            var relatives = Enumerable.Range(0, arcs.Count).Select(i => arcs.Get(i, 0).Relative).ToList();
            Assert.IsFalse(relatives.Any(r => Math.Abs(r) < 1e-9), "the oldest (at 0) went");
            Assert.IsTrue(relatives.Any(r => Math.Abs(r + 1.4) < 1e-9));
            Assert.Throws<ArgumentOutOfRangeException>(() => arcs.Get(DamageArcs.Max, 0));
        }

        // ---------------------------------------------------------------- misses

        [Test]
        public void AMissPassesBesideThePlayerNeverThroughThem()
        {
            var rng = new Rng(3);
            var from = new V3(0, 1.5, 0);
            var chest = new V3(0, 1.2, 20);
            bool left = false, right = false;
            for (int i = 0; i < 200; i++)
            {
                V3 m = EnemyShots.MissPoint(from, chest, rng);
                double side = m.X - chest.X;
                Assert.That(Math.Abs(side), Is.InRange(EnemyShots.MissMin - 1e-9, EnemyShots.MissMax + 1e-9));
                Assert.AreEqual(chest.Z, m.Z, 1e-9, "across the line of fire");
                Assert.That(m.Y - chest.Y, Is.InRange(-0.35, 0.6));
                left |= side < 0;
                right |= side > 0;
            }
            Assert.IsTrue(left && right);
        }

        [Test]
        public void AShotFliesOnPastWhereItPassed()
        {
            V3 end = EnemyShots.Onward(new V3(0, 0, 0), new V3(0, 0, 10), 60);
            Assert.AreEqual(60, end.Z, 1e-9);
        }

        // ---------------------------------------------------------------- the Trauma Team

        static RpgCore NewCore(System.Collections.Generic.List<string> seen = null)
        {
            var core = new RpgCore((type, p) => seen?.Add(type), 20771, new MemorySaveStorage(), () => 1700000000000) { AutosaveEnabled = false };
            core.SetCity(RpgCityMeta.From(CityPlan.Generate(CityPlan.DefaultSeed)));
            core.GiveStartingKit();
            return core;
        }

        [Test]
        public void TheTraumaTeamBillsATenthAndPatchesThePlayerUp()
        {
            var seen = new System.Collections.Generic.List<string>();
            RpgCore core = NewCore(seen);
            core.AddMoney(2345 - core.Money);
            core.PlayerDamage(1e6, "test");
            Assert.AreEqual(0, core.Health);
            Assert.Contains("rpg:playerDied", seen);
            int fee = core.TraumaTeam();
            Assert.AreEqual(234, fee);
            Assert.AreEqual(2345 - 234, core.Money);
            Assert.AreEqual(core.Derived["maxHealth"], core.Health);
            Assert.Contains("rpg:traumaTeam", seen);
            Assert.IsNotNull(core.GetLocation(RpgCore.TraumaTeamLocation), "Doc Vey's is a place");
        }

        [Test]
        public void TheTraumaTeamTakesNothingFromThePennilessAndOnlyComesForTheFlatlined()
        {
            RpgCore core = NewCore();
            Assert.Throws<InvalidOperationException>(() => core.TraumaTeam(), "alive");
            core.AddMoney(5 - core.Money);
            core.PlayerDamage(1e6, "test");
            Assert.AreEqual(0, core.TraumaTeam());
            Assert.AreEqual(5, core.Money);
        }

        // ---------------------------------------------------------------- after a flatline

        sealed class Open : IEnemyWorld
        {
            public bool Clear(V3 from, V3 to) => false;   // nobody sees anybody: only the directors are being tested
            public bool Walkable(V3 from, V3 to) => true;
        }

        [Test]
        public void AResetTakesEveryoneAwayAndTheUnclearedEncountersComeBackWithTheirSurvivors()
        {
            var sim = new EnemySim(2);
            var def = new EncounterDef
            {
                Id = "garage", Tag = "sixth", Anchor = new V3(0, 0, 0),
                Members = new[] { new EncounterMember { Archetype = "gunner", Dx = 1 }, new EncounterMember { Archetype = "heavy", Dx = -1 } },
            };
            var encounters = new EncounterDirector(sim, new[] { def }, id => false);
            encounters.Step(new V3(10, 0, 0));
            EnemySquad first = encounters.Live("garage");
            Assert.IsNotNull(first);
            sim.Harm(first.All[0], 1e6);
            encounters.Reset();
            Assert.IsNull(encounters.Live("garage"));
            Assert.AreEqual(0, sim.Enemies.Count);
            encounters.Step(new V3(10, 0, 0));
            EnemySquad again = encounters.Live("garage");
            Assert.IsNotNull(again);
            Assert.AreEqual(1, again.All.Count, "the heavy back alone: the gunner stayed down");
            Assert.AreEqual(EnemyTable.Heavy, again.All[0].Archetype);
            Assert.AreEqual(EnemyState.Idle, again.All[0].State);

            CityNav nav = CityNav.Build(CityPlan.Generate());
            var patrols = new PatrolDirector(sim, new EnemyPaths(nav), 5, "street");
            for (int i = 0; i < 600 && patrols.Count < PatrolDirector.Squads; i++) patrols.Step(1.0 / 60, new V3(0, 1.6, 0), new Open());
            Assert.AreEqual(PatrolDirector.Squads, patrols.Count);
            patrols.Reset();
            Assert.AreEqual(0, patrols.Count);
            for (int i = 0; i < 60 * (PatrolDirector.RespawnDelay - 1); i++) patrols.Step(1.0 / 60, new V3(0, 1.6, 0), new Open());
            Assert.AreEqual(0, patrols.Count, "the street stays quiet for a while");
        }
    }
}
