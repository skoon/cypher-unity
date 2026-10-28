#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using Cypher.Game;
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
    /// The RPG in the real Slice scene: a new game starts the jobs with the starting kit and the places on the city; walking to a place and pressing E do what they
    /// should; time stands still when paused; the pause menu saves and the title's Continue puts the game back where the player stood; the title has no RPG.
    /// </summary>
    public class RpgServicePlayTests
    {
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

        static FirstPersonController Player => Object.FindAnyObjectByType<FirstPersonController>();

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static void StandAt(RpgLocation loc)
        {
            CityNav nav = CityNav.Build(Object.FindAnyObjectByType<Cypher.City.CityBuilder>().Slice.Plan);
            Player.Teleport(new Vector3((float)loc.X, (float)nav.WalkY + 0.05f, (float)loc.Z), 0f);
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator ANewGameStartsTheJobsWithTheStartingKitAndThePlacesOnTheCity()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            Assert.IsNull(Rpg.Core, "no RPG behind the title");
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Frames(2);

            RpgCore core = Rpg.Core;
            Assert.IsNotNull(core);
            Assert.AreEqual("active", core.Quests.GetState("q_main_ghost").State, "the main job opens at once");
            Assert.AreEqual("w_pistol", core.Inventory.EquippedItem("secondary").TemplateId, "the starting kit is worn");
            Assert.AreEqual("qh_ping", core.Inventory.DeckItems()[0].TemplateId);
            Assert.AreEqual(3, core.Inventory.Count("c_maxdoc"));

            // the places are on the real city, as the core puts them on its plan
            List<RpgLocation> expected = RpgLocations.Build(RpgCityMeta.From(CityPlan.Generate(RpgService.Seed)), RpgService.Seed ^ 0x5f37);
            Assert.AreEqual(12, core.GetLocationList().Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].Key, core.GetLocationList()[i].Key);
                Assert.AreEqual(expected[i].X, core.GetLocationList()[i].X, 1e-9);
                Assert.AreEqual(expected[i].Z, core.GetLocationList()[i].Z, 1e-9);
            }
            foreach (RpgLocation l in core.GetLocationList())
            {
                Assert.LessOrEqual(Mathf.Abs((float)l.X), (float)(CityLayout.Grid * CityLayout.Cell / 2), $"{l.Key} is inside the city");
                Assert.LessOrEqual(Mathf.Abs((float)l.Z), (float)(CityLayout.Grid * CityLayout.Cell / 2), $"{l.Key} is inside the city");
            }
        }

        [UnityTest]
        public IEnumerator WalkingToTheNoodleBarMeetsTheFirstObjectiveAndOffersTalkingToKaz()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            var events = new List<(string Type, JsonObject Payload)>();
            Rpg.Event += (t, p) => events.Add((t, p));

            StandAt(Rpg.Core.GetLocation("noodle"));
            yield return Frames(4);

            QuestRuntime st = Rpg.Core.Quests.GetState("q_main_ghost");
            Assert.IsTrue(st.IsDone("o_find_kaz"), "the reach objective is met by standing there");
            Assert.IsTrue(events.Any(e => e.Type == "rpg:locationReached" && (string)e.Payload["key"] == "noodle"));
            Assert.AreEqual("dialogue:dlg_kaz", Rpg.Prompt);
            Assert.IsTrue(events.Any(e => e.Type == "rpg:promptChanged" && (string)e.Payload["text"] == "Talk to Mama Kaz"), "the HUD is told what E would do");
        }

        [UnityTest]
        public IEnumerator PressingInteractTalksAndARebindIsHonoured()
        {
            using (var input = new VirtualInput())
            {
                yield return SliceTestScene.LoadAndPlay();
                yield return Frames(2);
                StandAt(Rpg.Core.GetLocation("noodle"));
                yield return Frames(3);
                Assert.IsFalse(Rpg.Core.Dialogue.Active);

                input.Hold(Key.E);
                yield return Frames(2);
                input.Release(Key.E);
                yield return null;
                Assert.IsTrue(Rpg.Core.Dialogue.Active, "E starts the conversation");
                Assert.AreEqual("dlg_kaz", Rpg.Core.Dialogue.GraphId);
                Rpg.Core.EndDialogue();

                SettingsService settings = Object.FindAnyObjectByType<SettingsService>();
                Assert.AreEqual(AssignOutcome.Applied, settings.Bindings.Assign(GameAction.Interact, BindingSlot.Primary, "<Keyboard>/g").Outcome);
                yield return null;
                input.Hold(Key.E);
                yield return Frames(2);
                input.Release(Key.E);
                yield return null;
                Assert.IsFalse(Rpg.Core.Dialogue.Active, "E no longer interacts");
                input.Hold(Key.G);
                yield return Frames(2);
                input.Release(Key.G);
                yield return null;
                Assert.IsTrue(Rpg.Core.Dialogue.Active, "G does");
            }
        }

        [UnityTest]
        public IEnumerator TimeStandsStillForTheRpgWhilePaused()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(10);
            Assert.Greater(Rpg.Core.GameTime, 0);
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            yield return null;
            double t = Rpg.Core.GameTime;
            yield return Frames(10);
            Assert.AreEqual(t, Rpg.Core.GameTime, "no time passes while paused");
            Assert.IsTrue(SliceTestScene.Flow.Resume());
            yield return Frames(10);
            Assert.Greater(Rpg.Core.GameTime, t);
        }

        [UnityTest]
        public IEnumerator SaveFromThePauseMenuAndContinueFromTheTitleBringBackTheGameAndWhereThePlayerStood()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            Assert.IsFalse(SliceTestScene.Flow.CanContinue, "nothing to continue yet");
            Assert.IsFalse(SliceTestScene.Flow.Continue());
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Frames(2);

            RpgCore core = Rpg.Core;
            core.AddXp(3000);
            core.SetFlag("kaz_honest", true);
            RpgLocation drop = core.GetLocation("market");
            StandAt(drop);
            Player.Teleport(Player.transform.position, 135f);
            yield return Frames(3);
            Vector3 stood = Player.transform.position;
            double xp = core.Stats.Xp;

            Assert.IsTrue(SliceTestScene.Flow.Pause());
            Assert.IsNull(SliceTestScene.Flow.SaveGame(), "saving works");
            Assert.IsNotNull(_saves.GetItem(RpgSave.KeyPrefix + RpgService.Slot));
            Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle());
            yield return Frames(2);
            Assert.IsNull(Rpg.Core, "the title has no RPG");
            Assert.IsTrue(SliceTestScene.Flow.CanContinue);

            Assert.IsTrue(SliceTestScene.Flow.Continue());
            yield return Frames(3);
            Assert.AreNotSame(core, Rpg.Core, "a new core, loaded from the save");
            Assert.AreEqual(xp, Rpg.Core.Stats.Xp);
            Assert.AreEqual(true, Rpg.Core.Flag("kaz_honest"));
            Assert.AreEqual(stood.x, Player.transform.position.x, 0.05f, "back where the player stood");
            Assert.AreEqual(stood.z, Player.transform.position.z, 0.05f);
            Assert.AreEqual(135f, Player.Look.Yaw * Mathf.Rad2Deg, 0.1f, "facing the same way");
        }

        [UnityTest]
        public IEnumerator TheTitlesContinueButtonIsOfferedOnlyWithASave()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            yield return Frames(2);
            TitleScreen title = Object.FindAnyObjectByType<UiRoot>().Title;
            Assert.IsFalse(title.Continue.enabledSelf, "greyed out with no save");

            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Frames(2);
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            Assert.IsNull(SliceTestScene.Flow.SaveGame());
            Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle());
            yield return Frames(2);
            Assert.IsTrue(title.Continue.enabledSelf, "offered once there is a save");
        }

        [UnityTest]
        public IEnumerator SavingWithNoGameSaysSo()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            Assert.IsNotNull(SliceTestScene.Flow.SaveGame(), "nothing to save at the title");
            Assert.IsNull(_saves.GetItem(RpgSave.KeyPrefix + RpgService.Slot));
        }
    }
}
#endif
