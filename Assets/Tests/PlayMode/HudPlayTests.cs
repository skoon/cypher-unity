#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using Cypher.Rpg;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>
    /// The HUD in the real Slice scene: shown only while playing; the biomonitor, the tracker and the waypoint follow the RPG; the RPG's events become notices; the
    /// E prompt names the place and the key as it is bound now.
    /// </summary>
    public class HudPlayTests
    {
        [SetUp]
        public void SetUp() => SliceTestScene.Isolate(saves: new MemorySaveStorage());

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static RpgService Rpg => Object.FindAnyObjectByType<RpgService>();

        static Hud Hud => Object.FindAnyObjectByType<UiRoot>().Hud;

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
        public IEnumerator TheHudShowsOnlyWhilePlaying()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            yield return Frames(2);
            Assert.IsNotNull(Hud, "the scene's UI has the HUD");
            Assert.IsFalse(Hud.Shown, "no HUD behind the title");

            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Frames(3);
            Assert.IsTrue(Hud.Shown, "the HUD while playing");

            Assert.IsTrue(SliceTestScene.Flow.Pause());
            yield return Frames(2);
            Assert.IsFalse(Hud.Shown, "the pause menu hides it");
            Assert.IsTrue(SliceTestScene.Flow.Resume());
            yield return Frames(2);
            Assert.IsTrue(Hud.Shown);
        }

        [UnityTest]
        public IEnumerator TheBiomonitorAndTrackerReadTheRpg()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(3);
            RpgCore core = Rpg.Core;
            Assert.AreEqual($"LV {core.Stats.Level}", Hud.Bio.LevelText);
            Assert.AreEqual(HudModel.Money(core.Economy.Money), Hud.Bio.MoneyText);
            Assert.AreEqual((int)core.Derived["maxRam"], Hud.Bio.RamChips, "a chip per unit of RAM");

            core.Damage(20);
            yield return Frames(2);
            StringAssert.StartsWith($"{System.Math.Ceiling(core.Health):000}/", Hud.Bio.HealthText);

            Assert.AreEqual("q_main_ghost", core.Quests.Tracked);
            Assert.IsTrue(Hud.Tracker.Shown);
            Assert.AreEqual(((string)core.Quests.Snapshot("q_main_ghost")["title"]).ToUpperInvariant(), Hud.Tracker.Title);
            Assert.IsNotEmpty(Hud.Tracker.StepTexts);
            Assert.IsTrue(Hud.Waypoint.Shown, "the waypoint points at the tracked step's place");
        }

        [UnityTest]
        public IEnumerator EventsBecomeNotices()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(3);
            Rpg.Core.AddMoney(250, "found");
            Rpg.Core.AddXp(40, "test");
            yield return Frames(2);
            Assert.IsTrue(Hud.Notices.Any(n => n.Kind == "cred" && n.Text == "+" + HudModel.Money(250)), "money");
            Assert.IsTrue(Hud.Notices.Any(n => n.Kind == "xp" && n.Text == "+40 XP"), "XP");
        }

        [UnityTest]
        public IEnumerator ThePromptNamesThePlaceAndTheKeyAsBound()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Assert.IsFalse(Hud.PromptShown, "nothing to do at the spawn");
            StandAt(Rpg.Core.GetLocation("noodle"));
            yield return Frames(4);
            Assert.IsTrue(Hud.PromptShown);
            Assert.AreEqual("[E]  TALK TO MAMA KAZ", Hud.PromptText);

            SettingsService settings = Object.FindAnyObjectByType<SettingsService>();
            Assert.AreEqual(AssignOutcome.Applied, settings.Bindings.Assign(GameAction.Interact, BindingSlot.Primary, "<Keyboard>/g").Outcome);
            yield return Frames(2);
            Assert.AreEqual("[G]  TALK TO MAMA KAZ", Hud.PromptText);
        }
    }
}
#endif
