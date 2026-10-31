using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The quickhacks on enemies: each table's effect, the squads they provoke, what they leave on the deck, contagion's jumps, and berserk's fight.</summary>
    public class HackEffectsTests
    {
        const double Dt = 1.0 / 60;

        sealed class Open : IEnemyWorld
        {
            public bool Clear(V3 from, V3 to) => true;
            public bool Walkable(V3 from, V3 to) => true;
        }

        static readonly V3 Player = new V3(0, 0, -20);
        static PlayerView View => new PlayerView(Player, 1.6, 0, false);

        static ActiveHack Hack(string templateId, Enemy target, double timeLeft = -1)
        {
            HackSpec spec = RpgItems.Template(templateId).Hack;
            double t = timeLeft >= 0 ? timeLeft : spec.Duration;
            return new ActiveHack
            {
                Uid = templateId + ":" + HackEffects.TargetOf(target), HackId = templateId, Name = templateId, TargetId = HackEffects.TargetOf(target), Effect = spec.Effect,
                Power = spec.Power, Duration = spec.Duration, TimeLeft = t,
            };
        }

        /// <summary>Step the sim and the hacks together, as the game does, the deck counting the hacks down.</summary>
        static void Run(EnemySim sim, HackEffects fx, List<ActiveHack> deck, double seconds, List<Enemy> killed, double strength = 0, PlayerView? player = null)
        {
            for (double t = 0; t < seconds - 1e-9; t += Dt)
            {
                for (int i = deck.Count - 1; i >= 0; i--)
                {
                    deck[i].TimeLeft -= Dt;
                    if (deck[i].TimeLeft <= 0) deck.RemoveAt(i);
                }
                fx.Step(Dt, deck, strength, killed);
                sim.Step(Dt, player ?? View, new Open());
            }
        }

        [Test]
        public void TargetsNameTheEnemyAndOthersAreNotEnemies()
        {
            var sim = new EnemySim(1);
            Enemy e = sim.Add(sim.AddSquad("x"), EnemyTable.Gunner, new V3(0, 0, 0), 0);
            Assert.AreEqual(e.Id, HackEffects.EnemyId(HackEffects.TargetOf(e)));
            Assert.AreEqual(-1, HackEffects.EnemyId("npc_marlene"));
            Assert.AreEqual(-1, HackEffects.EnemyId(null));
            Assert.Throws<FormatException>(() => HackEffects.EnemyId("enemy:x"));
            Assert.AreEqual(1.2, HackEffects.Scale(20), 1e-12);
        }

        [Test]
        public void ShortCircuitStrikesAtOnceScaledByHackStrengthAndProvokesThePatrol()
        {
            var sim = new EnemySim(1);
            EnemySquad patrol = sim.AddSquad("street", hostile: false);
            Enemy e = sim.Add(patrol, EnemyTable.Gunner, new V3(0, 0, 0), 0);
            Enemy mate = sim.Add(patrol, EnemyTable.Gunner, new V3(3, 0, 0), 0);
            var fx = new HackEffects(sim);
            ActiveHack h = Hack("qh_short_circuit", e);
            Assert.IsFalse(fx.Applied(h, Player, 25));
            Assert.AreEqual(EnemyTable.Gunner.MaxHealth - 40 * 1.25, e.Vitals.Health, 1e-9);
            Assert.IsTrue(patrol.Hostile, "a loud hack provokes the squad");
            Assert.AreEqual(EnemyState.Engaged, e.State);
            Assert.AreEqual(EnemyState.Engaged, mate.State);

            e.Vitals.Take(e.Vitals.Health - 10);
            Assert.IsTrue(fx.Applied(Hack("qh_short_circuit", e), Player, 0), "a shock can kill");
            Assert.AreEqual(EnemyState.Down, e.State);
        }

        [Test]
        public void PingAndRebootOpticsAreQuiet()
        {
            var sim = new EnemySim(1);
            EnemySquad patrol = sim.AddSquad("street", hostile: false);
            Enemy e = sim.Add(patrol, EnemyTable.Gunner, new V3(0, 0, 0), 0);
            var fx = new HackEffects(sim);
            fx.Applied(Hack("qh_ping", e), Player, 0);
            fx.Applied(Hack("qh_reboot_optics", e), Player, 0);
            Assert.IsFalse(patrol.Hostile);
            Assert.AreEqual(EnemyState.Idle, e.State);
            Assert.AreEqual(EnemyTable.Gunner.MaxHealth, e.Vitals.Health);
        }

        [Test]
        public void PingMarksTheWholeSquadForItsTimeAndNoOneElse()
        {
            var sim = new EnemySim(1);
            EnemySquad squad = sim.AddSquad("a");
            Enemy e = sim.Add(squad, EnemyTable.Gunner, new V3(0, 0, 0), 0);
            Enemy mate = sim.Add(squad, EnemyTable.Heavy, new V3(30, 0, 0), 0);
            Enemy other = sim.Add(sim.AddSquad("b"), EnemyTable.Gunner, new V3(1, 0, 0), 0);
            var fx = new HackEffects(sim);
            var deck = new List<ActiveHack> { Hack("qh_ping", e, timeLeft: 1) };
            var killed = new List<Enemy>();
            Run(sim, fx, deck, 0.5, killed);
            Assert.IsTrue(e.Revealed && mate.Revealed);
            Assert.IsFalse(other.Revealed);
            Run(sim, fx, deck, 0.6, killed);
            Assert.IsFalse(e.Revealed || mate.Revealed, "gone when it runs out");
        }

        [Test]
        public void RebootOpticsBlindsItToThePlayerInFrontOfIt()
        {
            var sim = new EnemySim(1);
            Enemy e = sim.Add(sim.AddSquad("a"), EnemyTable.Gunner, new V3(0, 0, 0), Math.PI);   // facing the player at -Z
            var fx = new HackEffects(sim);
            var deck = new List<ActiveHack> { Hack("qh_reboot_optics", e) };
            var killed = new List<Enemy>();
            Run(sim, fx, deck, 5, killed);
            Assert.IsTrue(e.Blind);
            Assert.IsFalse(e.SeesPlayer);
            Assert.AreEqual(0, e.Awareness, 1e-9, "never saw them");
            Assert.AreEqual(EnemyState.Idle, e.State);
            Run(sim, fx, deck, 4, killed);
            Assert.IsFalse(e.Blind);
            Assert.IsTrue(e.SeesPlayer, "optics back");
        }

        [Test]
        public void OverheatBurnsItsPowerASecondAndAKillIsReported()
        {
            var sim = new EnemySim(1);
            Enemy e = sim.Add(sim.AddSquad("a"), EnemyTable.Gunner, new V3(0, 0, 0), 0);
            var fx = new HackEffects(sim);
            var deck = new List<ActiveHack> { Hack("qh_overheat", e) };
            var killed = new List<Enemy>();
            Run(sim, fx, deck, 4 - 2 * Dt, killed);   // eight ticks: one as it lands, then every half second
            Assert.AreEqual(EnemyTable.Gunner.MaxHealth - 6 * 4, e.Vitals.Health, 1e-6, "6 a second");
            Run(sim, fx, deck, 7, killed);
            Assert.AreEqual(EnemyTable.Gunner.MaxHealth - 6 * 10, e.Vitals.Health, 1e-6, "for its ten seconds and no more");
            Assert.IsEmpty(killed);

            Enemy weak = sim.Add(sim.AddSquad("b"), EnemyTable.Gunner, new V3(5, 0, 0), 0);
            weak.Vitals.Take(weak.Vitals.Health - 5);
            deck.Add(Hack("qh_overheat", weak));
            Run(sim, fx, deck, 2, killed);
            CollectionAssert.AreEqual(new[] { weak }, killed);
            Assert.IsFalse(deck.Any(h => h.TargetId == HackEffects.TargetOf(weak)), "a hack on the dead comes off the deck");
        }

        [Test]
        public void ARefreshedBurnStartsItsClockAgainAndHackStrengthScalesEachTick()
        {
            var sim = new EnemySim(1);
            Enemy e = sim.Add(sim.AddSquad("a"), EnemyTable.Heavy, new V3(0, 0, 0), 0);
            var fx = new HackEffects(sim);
            var deck = new List<ActiveHack> { Hack("qh_overheat", e) };
            var killed = new List<Enemy>();
            Run(sim, fx, deck, 8 - 2 * Dt, killed, strength: 50);   // sixteen ticks
            Assert.AreEqual(EnemyTable.Heavy.MaxHealth - 6 * 8 * 1.5, e.Vitals.Health, 1e-6);
            deck[0] = Hack("qh_overheat", e);   // uploaded again: the deck replaces the entry
            Run(sim, fx, deck, 11, killed, strength: 50);
            Assert.AreEqual(EnemyTable.Heavy.MaxHealth - 6 * 18 * 1.5, e.Vitals.Health, 1e-6, "eight seconds, then ten more");
        }

        [Test]
        public void AHackOnOneWhoIsGoneComesOffTheDeckAndOthersAreLeftAlone()
        {
            var sim = new EnemySim(1);
            EnemySquad squad = sim.AddSquad("a");
            Enemy e = sim.Add(squad, EnemyTable.Gunner, new V3(0, 0, 0), 0);
            var fx = new HackEffects(sim);
            var npc = new ActiveHack { Uid = "u", HackId = "qh_ping", TargetId = "npc_marlene", Effect = "reveal", Power = 1, Duration = 5, TimeLeft = 5 };
            var deck = new List<ActiveHack> { Hack("qh_ping", e), npc };
            sim.Remove(squad);
            fx.Step(Dt, deck, 0, new List<Enemy>());
            CollectionAssert.AreEqual(new[] { npc }, deck);
            fx.Clear(deck);
            CollectionAssert.AreEqual(new[] { npc }, deck, "clearing takes only the enemies' hacks");
        }

        [Test]
        public void ContagionJumpsToThoseCloseByOnceEachCarryingItsTimeLeft()
        {
            var sim = new EnemySim(1);
            EnemySquad squad = sim.AddSquad("a");
            Enemy e = sim.Add(squad, EnemyTable.Heavy, new V3(0, 0, 0), 0);
            Enemy near = sim.Add(squad, EnemyTable.Heavy, new V3(2.5, 0, 0), 0);
            Enemy nextOn = sim.Add(squad, EnemyTable.Heavy, new V3(5, 0, 0), 0);    // in reach of the second, not the first
            Enemy far = sim.Add(squad, EnemyTable.Heavy, new V3(20, 0, 0), 0);
            var fx = new HackEffects(sim);
            var deck = new List<ActiveHack> { Hack("qh_contagion", e) };
            var killed = new List<Enemy>();
            Run(sim, fx, deck, 1.0 + Dt, killed);
            Assert.AreEqual(2, deck.Count, "jumped to the one within three metres");
            ActiveHack jumped = deck.Single(h => h.TargetId == HackEffects.TargetOf(near));
            Assert.AreEqual(deck[0].TimeLeft, jumped.TimeLeft, 0.05, "with the time it had left");
            Run(sim, fx, deck, 1.0, killed);
            Assert.AreEqual(3, deck.Count, "and on from there");
            Assert.IsFalse(deck.Any(h => h.TargetId == HackEffects.TargetOf(far)));
            Assert.Less(near.Vitals.Health, EnemyTable.Heavy.MaxHealth, "each copy burns");
            Run(sim, fx, deck, 3, killed);
            Assert.AreEqual(3, deck.Count, "never twice on one");
        }

        [Test]
        public void ABerserkTurnsOnItsNearestSquadmateAndTheyShootBack()
        {
            var sim = new EnemySim(4);
            EnemySquad squad = sim.AddSquad("a");
            Enemy mad = sim.Add(squad, EnemyTable.Gunner, new V3(0, 0, 0), 0);
            Enemy near = sim.Add(squad, EnemyTable.Gunner, new V3(0, 0, 10), Math.PI);
            Enemy far = sim.Add(squad, EnemyTable.Gunner, new V3(0, 0, 25), Math.PI);
            var fx = new HackEffects(sim);
            var deck = new List<ActiveHack> { Hack("qh_cyberpsychosis", mad) };
            fx.Applied(deck[0], Player, 0);
            var shots = new List<EnemyEvent>();
            var killed = new List<Enemy>();
            var farPlayer = new PlayerView(new V3(0, 0, -200), 1.6, 0, false);   // out of everyone's sight
            for (int i = 0; i < 60 * 10; i++)
            {
                Run(sim, fx, deck, Dt, killed, player: farPlayer);
                shots.AddRange(sim.Events.Where(ev => ev.Kind == EnemyEventKind.Shot));
            }
            Assert.IsTrue(shots.Any(s => s.Enemy == mad.Id && s.Victim == near.Id), "the berserk fired on its nearest");
            Assert.IsTrue(shots.Any(s => s.Enemy != mad.Id && s.Victim == mad.Id), "and they fired back");
            Assert.IsFalse(shots.Any(s => s.Victim == -1), "nobody shot at the player, out of sight");
            Assert.IsTrue(mad.Vitals.Health < EnemyTable.Gunner.MaxHealth || near.Vitals.Health < EnemyTable.Gunner.MaxHealth, "hits landed on enemies");
        }

        [Test]
        public void WhenBerserkEndsTheSquadIsBackOnThePlayer()
        {
            var sim = new EnemySim(4);
            EnemySquad squad = sim.AddSquad("a");
            Enemy mad = sim.Add(squad, EnemyTable.Heavy, new V3(0, 0, 0), Math.PI);
            Enemy mate = sim.Add(squad, EnemyTable.Heavy, new V3(4, 0, 0), Math.PI);
            var fx = new HackEffects(sim);
            var deck = new List<ActiveHack> { Hack("qh_cyberpsychosis", mad, timeLeft: 1) };
            fx.Applied(deck[0], Player, 0);
            var killed = new List<Enemy>();
            Run(sim, fx, deck, 1.5, killed);
            Assert.IsFalse(mad.Berserk);
            var shots = new List<EnemyEvent>();
            for (int i = 0; i < 60 * 8; i++)
            {
                Run(sim, fx, deck, Dt, killed);
                shots.AddRange(sim.Events.Where(ev => ev.Kind == EnemyEventKind.Shot));
            }
            Assert.IsTrue(shots.Count > 0 && shots.All(s => s.Victim == -1), "all at the player again");
        }
    }
}
