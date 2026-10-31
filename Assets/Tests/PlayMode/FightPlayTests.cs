#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Cypher.Actors;
using Cypher.Combat;
using Cypher.Core;
using Cypher.Fight;
using Cypher.Player;
using Cypher.Rpg;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Cypher.Tests
{
    /// <summary>The fight in the game: enemies in the world and on screen, the player's shots hurting and killing them, theirs hurting the player, and the Trauma Team.</summary>
    public class FightPlayTests
    {
        [SetUp]
        public void SetUp() => SliceTestScene.Isolate();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static EnemyController Fight => Object.FindFirstObjectByType<EnemyController>();
        static RpgService Rpg => Object.FindAnyObjectByType<RpgService>();
        static FirstPersonController Player => Object.FindAnyObjectByType<FirstPersonController>();
        static WeaponSystem Guns => Object.FindAnyObjectByType<WeaponSystem>();
        static CrowdController Crowd => Object.FindFirstObjectByType<CrowdController>();
        static Hud Hud => Object.FindFirstObjectByType<UiRoot>().Hud;

        /// <summary>A game with the fights running and no patrols: the street to the test.</summary>
        static IEnumerator Ready()
        {
            yield return SliceTestScene.LoadAndPlay();
            for (int i = 0; i < 300 && !Fight.IsActive; i++) yield return null;
            Assert.IsTrue(Fight.IsActive, "the fights begin with the game");
            Fight.PatrolsOn = false;
            Fight.Patrols.Reset();
        }

        /// <summary>A point on the ground <paramref name="metres"/> ahead of the player.</summary>
        static Vector3 Ahead(float metres)
        {
            Transform p = Player.transform;
            Vector3 at = p.position + p.forward * metres;
            at.y = (float)Crowd.Sim.Nav.GroundY(at.x, at.z);
            return at;
        }

        /// <summary>Turn the player to look at a point.</summary>
        static void LookAt(Vector3 target)
        {
            Vector3 eye = Camera.main.transform.position, d = target - eye;
            double yaw = Math.Atan2(d.x, d.z), pitch = Math.Atan2(d.y, Math.Sqrt(d.x * d.x + d.z * d.z));
            Player.Look.Set(yaw, pitch);
        }

        [UnityTest]
        public IEnumerator AShotInTheStreetScattersThePeopleNearAndLeavesTheFarAlone()
        {
            yield return Ready();
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(Crowd.Sim.Active.Any(p => p.Fleeing), "a calm street");
            Vector3 feet = Player.transform.position;
            float From(Pedestrian p) => Vector2.Distance(new Vector2((float)p.X, (float)p.Z), new Vector2(feet.x, feet.z));
            Guns.TakeShots();
            for (int i = 0; i < 120 && Guns.TakeShots().Count == 0; i++)
            {
                Guns.Step(new WeaponInput { Acting = true, FireHeld = true, FireClicked = true }, new WeaponBody { Grounded = true });   // the gun is drawn first
            }
            Assert.IsTrue(Crowd.Sim.Active.Any(p => p.Fleeing), "someone heard it");
            foreach (Pedestrian p in Crowd.Sim.Active) Assert.AreEqual(From(p) <= EnemyController.ScatterRadius, p.Fleeing, $"person {p.Index} at {From(p):0.0} m");
            var near = Crowd.Sim.Active.Where(p => p.Fleeing).ToDictionary(p => p, From);
            yield return new WaitForSeconds(2f);
            var still = near.Keys.Where(p => Crowd.Sim.Active.Contains(p)).ToList();
            Assert.Greater(still.Count, 0);
            Assert.Greater(still.Average(p => p.Speed / p.Cruise), 1.8, "running, not walking");
            Assert.IsTrue(still.All(p => From(p) > near[p] - 1), "and none of them closer (the pavements round the plaza run across the way away, so the distance grows slowly)");
        }

        [UnityTest]
        public IEnumerator AnEnemyIsDrawnWithTheCrowdAndCanBeShotAnywhere()
        {
            yield return Ready();
            Enemy e = Fight.Spawn("test", new[] { (EnemyTable.Gunner, Ahead(30)) }, hostile: false).All[0];
            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, Fight.Bodies.All.Count);
            Assert.AreEqual(1, Fight.Proxies.Leased, "thirty metres out, beyond the crowd's eight, it still has volumes");
            Assert.AreEqual(e.Id, Fight.Proxies[0].GetComponent<Cypher.City.EnemyHitbox>().Enemy);
            yield return null;
            Assert.IsTrue(Crowd.Renderer.Frame.Data.Take(Crowd.Renderer.Frame.Total).Any(d => d.Weapon != 0), "drawn in the crowd's frame");
        }

        [UnityTest]
        public IEnumerator ShootingAnEnemyHurtsItAndAKillPaysThroughTheRpg()
        {
            yield return Ready();
            Enemy e = Fight.Spawn("test", new[] { (EnemyTable.Gunner, Ahead(8)) }, yaw: Player.Look.Yaw + Math.PI, hostile: false).All[0];
            double xp = Rpg.Core.Stats.Xp;
            bool hurt = false, killed = false;
            Fight.EnemyHurt += (who, dead, hit) =>
            {
                if (who != e) return;
                hurt = true;
                killed |= dead;
            };
            for (int i = 0; i < 600 && !killed; i++)
            {
                LookAt(new Vector3((float)e.Position.X, (float)e.Position.Y + 1.1f, (float)e.Position.Z));
                if (i % 3 == 0) yield return new WaitForFixedUpdate();   // the volumes follow it in the physics step
                yield return null;
                Guns.Step(new WeaponInput { Acting = true, FireHeld = true, FireClicked = true }, new WeaponBody { Grounded = true });
            }
            Assert.IsTrue(hurt, "the shots reached it");
            Assert.IsTrue(killed, "and killed it");
            Assert.AreEqual(EnemyState.Down, e.State);
            Assert.Greater(Rpg.Core.Stats.Xp, xp, "XP for the kill");
            Assert.IsTrue(e.Squad.Hostile, "the first hit provoked its squad");
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(1, Fight.Bodies.Find(e.Id).Death, 1e-9, "it has fallen");
            Assert.AreEqual(0, Fight.Proxies.Leased, "and is no longer a target");
        }

        [UnityTest]
        public IEnumerator AnEnemyInTheSightsGetsANameAndHealthBar()
        {
            yield return Ready();
            Enemy e = Fight.Spawn("test", new[] { (EnemyTable.Heavy, Ahead(10)) }, yaw: Player.Look.Yaw + Math.PI, hostile: false).All[0];
            for (int i = 0; i < 20 && Fight.Aimed != e; i++)
            {
                LookAt(new Vector3((float)e.Position.X, (float)e.Position.Y + 1.1f, (float)e.Position.Z));
                yield return new WaitForFixedUpdate();
                yield return null;
            }
            Assert.AreSame(e, Fight.Aimed);
            yield return null;
            Assert.IsTrue(Hud.Fight.BarShown);
            Assert.AreEqual(EnemyTable.Heavy.Name.ToUpperInvariant(), Hud.Fight.BarName);
            Assert.AreEqual(1f, Hud.Fight.BarFill, 1e-6f);
            LookAt(Camera.main.transform.position + Vector3.up * 10);
            yield return null;
            yield return null;
            Assert.IsNull(Fight.Aimed);
            Assert.IsFalse(Hud.Fight.BarShown);
        }

        [UnityTest]
        public IEnumerator AnEnemyShootsThePlayerWithArcsOnTheHudAndAFlatlineBringsTheTraumaTeam()
        {
            yield return Ready();
            Vector3 at = Ahead(12);
            Enemy e = Fight.Spawn("test", new[] { (EnemyTable.Gunner, at) }, yaw: Player.Look.Yaw + Math.PI).All[0];
            double health = Rpg.Core.Health;
            int hits = 0;
            Fight.PlayerHit += (from, damage) => hits++;
            for (int i = 0; i < 60 * 30 && hits == 0; i++) Fight.StepOnce();
            Assert.Greater(hits, 0, "it found the player and hit them");
            Assert.Less(Rpg.Core.Health, health);
            yield return null;
            Assert.Greater(Hud.Fight.Arcs.Count, 0, "an arc points at the shooter");
            (double rel, double alpha) = Hud.Fight.Arcs.Get(0, Player.Look.Yaw);
            Assert.Less(Math.Abs(rel), 0.6, "ahead, where it stands");
            Assert.Greater(alpha, 0);

            double money = Rpg.Core.Money;
            RpgLocation clinic = Rpg.Core.GetLocation(RpgCore.TraumaTeamLocation);
            Rpg.HurtPlayer(1e6, "test");
            Assert.IsTrue(Fight.Downed);
            Assert.IsTrue(Guns.Suppressed, "no shooting while flatlined");
            yield return null;
            Assert.IsTrue(Hud.Fight.FlatlineShown);
            yield return new WaitForSeconds(EnemyController.DownSeconds + 0.5f);
            Assert.IsFalse(Fight.Downed);
            Assert.IsFalse(Guns.Suppressed);
            Assert.IsFalse(Hud.Fight.FlatlineShown);
            Assert.AreEqual(Rpg.Core.Derived["maxHealth"], Rpg.Core.Health);
            Assert.AreEqual(money - Math.Floor(money * RpgCore.TraumaTeamShare), Rpg.Core.Money);
            Vector3 feet = Player.transform.position;
            Assert.Less(Vector2.Distance(new Vector2(feet.x, feet.z), new Vector2((float)clinic.X, (float)clinic.Z)), 3f, "at Doc Vey's");
            Assert.AreEqual(0, Fight.Sim.Enemies.Count, "the fight was cleared away");
            Assert.AreEqual(0, Hud.Fight.Arcs.Count);
        }
    }
}
#endif
