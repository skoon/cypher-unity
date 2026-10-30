#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cypher.Combat;
using Cypher.Core;
using Cypher.Player;
using Cypher.Rpg;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>
    /// Gate C's pilot: the main job played to the corpo ending in the real Slice scene. The pilot is put at each place (walking is the street-route tests' job),
    /// presses E, answers through the conversation screen and picks the breach through its cells. Halfway it saves from the pause menu, goes to the title and
    /// Continues, and it fires the gun on the way.
    /// </summary>
    public class MainJobPlayTests
    {
        const string Job = "q_main_ghost";

        // the answers that lead to the Spire, as the core golden's main-corpo session prefers them; any other open answer moves the talk on
        static readonly string[] Prefer = { "w_o_cut", "w_o_sell", "k_o_accept", "k_o_eat" };

        MemorySaveStorage _saves;

        [SetUp]
        public void SetUp()
        {
            _saves = new MemorySaveStorage();
            SliceTestScene.Isolate(saves: _saves);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static RpgService Rpg => Object.FindAnyObjectByType<RpgService>();

        static UiRoot Ui => Object.FindAnyObjectByType<UiRoot>();

        static WeaponSystem Guns => Object.FindAnyObjectByType<WeaponSystem>();

        static FirstPersonController Player => Object.FindAnyObjectByType<FirstPersonController>();

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static string Stage => Rpg.Core.Quests.GetState(Job).Stage;

        static IEnumerator StandAt(string key)
        {
            RpgLocation loc = Rpg.Core.GetLocation(key);
            CityNav nav = CityNav.Build(Object.FindAnyObjectByType<Cypher.City.CityBuilder>().Slice.Plan);
            Player.Teleport(new Vector3((float)loc.X, (float)nav.WalkY + 0.05f, (float)loc.Z), 0f);
            Physics.SyncTransforms();
            yield return Frames(4);
        }

        static IEnumerator PressE(VirtualInput input)
        {
            input.Hold(Key.E);
            yield return Frames(2);
            input.Release(Key.E);
            yield return Frames(2);
        }

        /// <summary>Answer through the screen until the conversation is over and the screen has closed.</summary>
        static IEnumerator Talk()
        {
            Assert.AreEqual(UiScreen.Dialogue, Ui.Top, "E opened the conversation");
            var given = new HashSet<string>();
            for (int i = 0; i < 40 && Ui.Top == UiScreen.Dialogue; i++)
            {
                Ui.Conversation.Skip();
                yield return null;
                List<FocusControl> buttons = Ui.Conversation.Buttons.ToList();
                Assert.IsNotEmpty(buttons, "a line always offers something to press");
                if (!(buttons[0] is ChoiceButton))
                {
                    buttons[0].Activate();   // LEAVE after the last line
                    yield return Frames(2);
                    continue;
                }
                List<ChoiceButton> open = buttons.Cast<ChoiceButton>().Where(b => !b.Choice.Locked).ToList();
                Assert.IsNotEmpty(open, "an answer is open");
                ChoiceButton pick = open.FirstOrDefault(b => Prefer.Contains(b.Choice.Id))
                                    ?? open.FirstOrDefault(b => !given.Contains(b.Choice.Id)) ?? open[0];
                given.Add(pick.Choice.Id);
                pick.Activate();
                yield return Frames(2);
            }
            Assert.AreEqual(UiScreen.None, Ui.Top, "the conversation closed");
            Assert.IsFalse(Rpg.Core.Dialogue.Active);
        }

        /// <summary>Pick the breach's solution through the cells and wait for its screen to close.</summary>
        static IEnumerator Breach()
        {
            Assert.AreEqual(UiScreen.Breach, Ui.Top, "E opened the breach");
            foreach (GridCell p in Rpg.Core.Breach.S.SolutionPath.ToList())
            {
                if (Rpg.Core.Breach.Done) break;
                Ui.Breach.Cells[p.R][p.C].Activate();
                yield return null;
            }
            Assert.IsTrue(Rpg.Core.Breach.Done);
            StringAssert.StartsWith("BREACH COMPLETE", Ui.Breach.Outcome);
            yield return new WaitForSecondsRealtime((float)BreachScreen.OutcomeSeconds + 0.3f);
            Assert.AreEqual(UiScreen.None, Ui.Top);
        }

        static IEnumerator Burst()
        {
            int before = Guns.Rig.Current.Ammo;
            Player.Look.Set(Player.Look.Yaw, -0.5);
            for (int i = 0; i < 20; i++)
            {
                Guns.Step(new WeaponInput { Acting = true, FireHeld = true }, new WeaponBody { Grounded = true });
                yield return null;
            }
            Guns.Step(new WeaponInput { Acting = true }, new WeaponBody { Grounded = true });
            Assert.Less(Guns.Rig.Current.Ammo, before, "the gun fired");
            Player.Look.Set(Player.Look.Yaw, 0);
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator TheMainJobPlaysToTheSpireThroughTheScreensWithASaveAndContinueHalfway()
        {
            using (var input = new VirtualInput())
            {
                yield return SliceTestScene.LoadAndPlay();
                yield return new WaitForSeconds(0.6f);   // past the draw
                Assert.AreEqual("active", Rpg.Core.Quests.GetState(Job).State);
                Assert.AreEqual("s_meet", Stage);
                double xp = Rpg.Core.Stats.Xp;

                // Mama Kaz
                yield return StandAt("noodle");
                Assert.IsTrue(Rpg.Core.Quests.GetState(Job).IsDone("o_find_kaz"));
                Assert.AreEqual("dialogue:dlg_kaz", Rpg.Prompt);
                yield return PressE(input);
                yield return Talk();
                Assert.AreEqual("s_drop", Stage);

                // the dead drop gives the shard
                yield return Burst();
                yield return StandAt("drop");
                Assert.AreEqual(1, Rpg.Core.Inventory.Count("q_ghost_shard"), "the shard is in the bag");
                Assert.AreEqual("s_datacore", Stage);

                // save, title, Continue
                Assert.IsTrue(SliceTestScene.Flow.Pause());
                Assert.IsNull(SliceTestScene.Flow.SaveGame());
                Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle());
                yield return Frames(3);
                Assert.IsNull(Rpg.Core);
                Assert.IsTrue(SliceTestScene.Flow.Continue());
                yield return new WaitForSeconds(0.6f);
                Assert.AreEqual(FlowState.Playing, SliceTestScene.Flow.State);
                Assert.AreEqual("s_datacore", Stage, "the job came back where it was saved");
                Assert.AreEqual(1, Rpg.Core.Inventory.Count("q_ghost_shard"));
                yield return Frames(2);
                CollectionAssert.Contains(Ui.Hud.Tracker.StepTexts, "Get to the Sector Data Core", "the tracker shows the loaded stage");
                Assert.IsTrue(Guns.Drawn, "the gun is drawn in the loaded game");
                yield return Burst();

                // the core: breach first, then Wire
                yield return StandAt("datacore");
                Assert.AreEqual("breach:datacore", Rpg.Prompt, "the job's breach is what E does here");
                yield return PressE(input);
                yield return Breach();
                Assert.AreEqual("s_wire", Stage);
                yield return Frames(2);
                Assert.AreEqual("dialogue:dlg_wire", Rpg.Prompt);
                yield return PressE(input);
                yield return Talk();
                Assert.AreEqual(true, Rpg.Core.Flag("ghost_sold"));
                Assert.AreEqual("s_end_corpo", Stage);

                // the Spire ends it
                yield return StandAt("spire");
                QuestRuntime done = Rpg.Core.Quests.GetState(Job);
                Assert.AreEqual("completed", done.State);
                Assert.AreEqual("corpo", done.Outcome);
                Assert.Greater(Rpg.Core.Stats.Xp, xp, "the job paid XP");
                Assert.AreEqual(FlowState.Playing, SliceTestScene.Flow.State);
                Assert.IsTrue(Player.enabled, "the player has control at the end");
            }
        }
    }
}
#endif
