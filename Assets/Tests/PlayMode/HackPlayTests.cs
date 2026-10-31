#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Cypher.Tests
{
    /// <summary>Quickhacks on enemies in the game: the scanner picks the target, Fire uploads, and each hack does what it says on the HUD and in the fight.</summary>
    public class HackPlayTests
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
        static UiRoot Ui => Object.FindFirstObjectByType<UiRoot>();

        static IEnumerator Ready()
        {
            yield return SliceTestScene.LoadAndPlay();
            for (int i = 0; i < 300 && !Fight.IsActive; i++) yield return null;
            Assert.IsTrue(Fight.IsActive, "the fights begin with the game");
            Fight.PatrolsOn = false;
            Fight.Patrols.Reset();
        }

        /// <summary>The deck holding these hacks from slot 0, and RAM for all of them.</summary>
        static void Deck(params string[] templates)
        {
            RpgCore core = Rpg.Core;
            for (int i = 0; i < core.Inventory.DeckSize; i++) core.Inventory.UnequipHack(i);
            for (int i = 0; i < templates.Length; i++)
            {
                RpgResult given = core.GiveItem(templates[i]);
                Assert.IsTrue(given.Ok, $"given {templates[i]}: {given.Reason}");
                Item item = given.Item ?? core.Inventory.Items.Last(it => it.TemplateId == templates[i]);
                Assert.IsTrue(core.Inventory.EquipHack(item.Uid, i).Ok);
            }
            FillRam();
        }

        static void FillRam()
        {
            Rpg.Core.Deck.SetMaxRam(40);
            Rpg.Core.Deck.Fill();
        }

        static Vector3 Ahead(float metres, float side = 0)
        {
            Transform p = Player.transform;
            Vector3 at = p.position + p.forward * metres + p.right * side;
            at.y = (float)Crowd.Sim.Nav.GroundY(at.x, at.z);
            return at;
        }

        static Vector3 Chest(Enemy e) => new Vector3((float)e.Position.X, (float)e.Position.Y + EnemyController.ChestHeight, (float)e.Position.Z);

        static void LookAt(Vector3 target)
        {
            Vector3 eye = Camera.main.transform.position, d = target - eye;
            Player.Look.Set(Math.Atan2(d.x, d.z), Math.Atan2(d.y, Math.Sqrt(d.x * d.x + d.z * d.z)));
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator Click(VirtualInput input)
        {
            input.PressMouse(MouseButton.Left, true);
            yield return null;
            input.PressMouse(MouseButton.Left, false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HoldingScanLowersTheGunTargetsTheEnemyAndFireUploadsAShortCircuitThatKillsAndPays()
        {
            using (var input = new VirtualInput())
            {
                yield return Ready();
                Enemy e = Fight.Spawn("test", new[] { (EnemyTable.Gunner, Ahead(8)) }, yaw: Player.Look.Yaw, hostile: false).All[0];
                Deck("qh_short_circuit");
                LookAt(Chest(e));
                input.Hold(Key.F);
                yield return Frames(30);
                Assert.IsTrue(Fight.Hacking);
                Assert.IsTrue(Guns.Suppressed, "the gun is down while scanning");
                Assert.AreSame(e, Fight.HackTarget);
                Assert.AreEqual(0, Fight.HackSlot);
                Assert.IsTrue(Ui.Hud.Fight.DeckShown);
                StringAssert.Contains("GUNNER", Ui.Hud.Fight.DeckTarget);
                var rows = Ui.Hud.Fight.DeckRows.ToList();
                Assert.AreEqual(1, rows.Count);
                Assert.IsTrue(rows[0].Picked && rows[0].Affordable);
                StringAssert.StartsWith("SHORT CIRCUIT", rows[0].Text);
                Assert.IsTrue(Ui.Scanner.Tagged.Any(t => t.Contact.Kind == ScanKind.Enemy && t.Contact.Action.StartsWith("[FIRE] SHORT CIRCUIT")), "its scanner card says what Fire does");

                Guns.TakeShots();
                double ram = Rpg.Core.Deck.Ram, scale = HackEffects.Scale(Rpg.Core.Derived["hackStrength"]);
                yield return Click(input);
                Assert.IsEmpty(Guns.TakeShots(), "Fire uploaded, it did not shoot");
                Assert.AreEqual(EnemyTable.Gunner.MaxHealth - 40 * scale, e.Vitals.Health, 1e-6);
                Assert.AreEqual(ram - 5, Rpg.Core.Deck.Ram, 0.1, "five RAM");
                Assert.IsTrue(e.Squad.Hostile, "a Short Circuit provokes");

                double xp = Rpg.Core.Stats.Xp;
                e.Vitals.Take(e.Vitals.Health - 1);
                FillRam();
                yield return Click(input);
                Assert.AreEqual(EnemyState.Down, e.State);
                Assert.Greater(Rpg.Core.Stats.Xp, xp, "the kill pays");

                input.Release(Key.F);
                yield return Frames(3);
                Assert.IsFalse(Fight.Hacking);
                Assert.IsFalse(Guns.Suppressed);
                Assert.IsFalse(Ui.Hud.Fight.DeckShown);
            }
        }

        [UnityTest]
        public IEnumerator AHackWithoutRamOrTargetIsRefusedAndTheWheelSkipsEmptySlots()
        {
            using (var input = new VirtualInput())
            {
                yield return Ready();
                Enemy e = Fight.Spawn("test", new[] { (EnemyTable.Gunner, Ahead(8)) }, hostile: false).All[0];
                Deck("qh_ping", "qh_overheat");
                Rpg.Core.Inventory.UnequipHack(1);
                Assert.IsTrue(Rpg.Core.Inventory.EquipHack(Rpg.Core.Inventory.Items.First(i => i.TemplateId == "qh_overheat").Uid, 2).Ok);   // slots: ping, empty, overheat
                LookAt(Chest(e));
                input.Hold(Key.F);
                yield return Frames(5);
                var refused = new List<string>();
                Fight.HackRefused += refused.Add;
                Assert.AreEqual(0, Fight.HackSlot);
                Fight.SelectHack(1);
                Assert.AreEqual(2, Fight.HackSlot, "past the empty slot");
                Fight.SelectHack(1);
                Assert.AreEqual(0, Fight.HackSlot, "and round");
                Fight.SelectHack(-1);
                Assert.AreEqual(2, Fight.HackSlot);

                Rpg.Core.Deck.Ram = 1;
                yield return Click(input);
                CollectionAssert.AreEqual(new[] { "no-ram" }, refused);
                Assert.AreEqual(EnemyTable.Gunner.MaxHealth, e.Vitals.Health);
                Assert.IsFalse(Ui.Hud.Fight.DeckRows.Single(r => r.Text.StartsWith("OVERHEAT")).Affordable, "greyed out");

                FillRam();
                LookAt(Camera.main.transform.position + Vector3.up * 10);
                yield return Frames(2);
                Assert.IsNull(Fight.HackTarget);
                Assert.AreEqual("NO TARGET IN RANGE", Ui.Hud.Fight.DeckTarget);
                yield return Click(input);
                CollectionAssert.AreEqual(new[] { "no-ram", "no-target" }, refused);
            }
        }

        [UnityTest]
        public IEnumerator PingMarksTheSquadOnTheHudAndLetsThemBeHackedThroughAWall()
        {
            using (var input = new VirtualInput())
            {
                yield return Ready();
                EnemySquad squad = Fight.Spawn("test", new[] { (EnemyTable.Gunner, Ahead(10)), (EnemyTable.Heavy, Ahead(10, 3)) }, hostile: false);
                Enemy e = squad.All[0], mate = squad.All[1];
                Deck("qh_ping");
                LookAt(Chest(e));
                input.Hold(Key.F);
                yield return Frames(5);
                yield return Click(input);
                yield return Frames(3);
                Assert.IsTrue(e.Revealed && mate.Revealed, "the squad is pinged");
                Assert.IsFalse(squad.Hostile, "quietly");
                Assert.AreEqual(2, Ui.Hud.Fight.PingsShown);

                // a wall between the player and the mate: still the target, being pinged
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    Vector3 eye = Camera.main.transform.position, chest = Chest(mate);
                    wall.transform.position = Vector3.Lerp(eye, chest, 0.5f);
                    wall.transform.rotation = Quaternion.LookRotation(chest - eye);
                    wall.transform.localScale = new Vector3(4, 4, 0.3f);
                    Physics.SyncTransforms();
                    LookAt(chest);
                    yield return Frames(3);
                    Assert.AreSame(mate, Fight.HackTarget, "pinged: hackable through the wall");

                    Rpg.Core.Deck.Active.Clear();   // the ping wears off
                    // the marks change on the fight's fixed step, which can come after the HUD in a frame: wait for it, then a frame for the HUD
                    for (int i = 0; i < 300 && mate.Revealed; i++) yield return null;
                    yield return Frames(2);
                    Assert.IsFalse(mate.Revealed);
                    Assert.IsNull(Fight.HackTarget, "not pinged, not seen");
                    Assert.AreEqual(0, Ui.Hud.Fight.PingsShown);
                }
                finally
                {
                    Object.Destroy(wall);
                }
            }
        }

        [UnityTest]
        public IEnumerator RebootOpticsBlindsAHostileAndTheHealthBarSaysSo()
        {
            using (var input = new VirtualInput())
            {
                yield return Ready();
                Enemy e = Fight.Spawn("test", new[] { (EnemyTable.Gunner, Ahead(12)) }, yaw: Player.Look.Yaw + Math.PI, hostile: true).All[0];
                Deck("qh_reboot_optics");
                LookAt(Chest(e));
                input.Hold(Key.F);
                yield return Frames(3);
                yield return Click(input);
                input.Release(Key.F);
                Assert.IsTrue(e.Blind);
                int hits = 0;
                Fight.PlayerHit += (from, d) => hits++;
                for (int i = 0; i < 60 * 5; i++) Fight.StepOnce();
                Assert.AreEqual(0, hits, "it cannot see to shoot");
                Assert.IsFalse(e.SeesPlayer);
                yield return Frames(3);
                Assert.AreSame(e, Fight.Aimed);
                Assert.AreEqual("BLIND", Ui.Hud.Fight.BarHacks);
            }
        }

        [UnityTest]
        public IEnumerator ContagionJumpsAlongASquadAndBurns()
        {
            using (var input = new VirtualInput())
            {
                yield return Ready();
                EnemySquad squad = Fight.Spawn("test", new[] { (EnemyTable.Heavy, Ahead(10)), (EnemyTable.Heavy, Ahead(10, 2)), (EnemyTable.Heavy, Ahead(10, 4)) }, hostile: false);
                Deck("qh_contagion");
                LookAt(Chest(squad.All[0]));
                input.Hold(Key.F);
                yield return Frames(3);
                Assert.AreSame(squad.All[0], Fight.HackTarget);
                yield return Click(input);
                input.Release(Key.F);
                for (int i = 0; i < 60 * 3; i++) Fight.StepOnce();
                foreach (Enemy e in squad.All)
                {
                    Assert.IsTrue(Rpg.Core.Deck.Active.Any(a => a.Effect == "contagion" && a.TargetId == HackEffects.TargetOf(e)), $"{e.Id} caught it");
                    Assert.Less(e.Vitals.Health, EnemyTable.Heavy.MaxHealth, $"{e.Id} burns");
                }
                Assert.IsTrue(squad.Hostile);
            }
        }

        [UnityTest]
        public IEnumerator CyberpsychosisTurnsASquadOnItselfAndAFlatlineClearsTheHacks()
        {
            using (var input = new VirtualInput())
            {
                yield return Ready();
                EnemySquad squad = Fight.Spawn("test", new[] { (EnemyTable.Gunner, Ahead(14)), (EnemyTable.Gunner, Ahead(20, 4)) }, yaw: Player.Look.Yaw, hostile: false);
                Enemy mad = squad.All[0], mate = squad.All[1];
                Deck("qh_cyberpsychosis");
                LookAt(Chest(mad));
                input.Hold(Key.F);
                yield return Frames(3);
                yield return Click(input);
                input.Release(Key.F);
                Assert.IsTrue(mad.Berserk);
                Assert.IsTrue(squad.Hostile);
                int playerHits = 0;
                Fight.PlayerHit += (from, d) => playerHits++;
                for (int i = 0; i < 60 * 8 && mad.Vitals.Health == EnemyTable.Gunner.MaxHealth && mate.Vitals.Health == EnemyTable.Gunner.MaxHealth; i++) Fight.StepOnce();
                Assert.IsTrue(mad.Vitals.Health < EnemyTable.Gunner.MaxHealth || mate.Vitals.Health < EnemyTable.Gunner.MaxHealth, "they shot each other");
                Assert.AreEqual(0, playerHits, "and not the player");

                Rpg.HurtPlayer(1e6, "test");
                yield return new WaitForSeconds(EnemyController.DownSeconds + 0.5f);
                Assert.IsFalse(Fight.Downed);
                Assert.IsFalse(Rpg.Core.Deck.Active.Any(a => HackEffects.EnemyId(a.TargetId) >= 0), "the flatline cleared the hacks");
            }
        }
    }
}
#endif
