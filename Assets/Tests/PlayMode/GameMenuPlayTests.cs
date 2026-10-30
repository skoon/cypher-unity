#if UNITY_EDITOR
using System.Collections;
using System.Linq;
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
    /// The game menu in the real Slice scene: Tab, K and J open it on their tabs and stop the world, the same key closes it, another key switches; and each page's
    /// controls change the real RPG (unequip, raise an attribute, buy a perk, track a job).
    /// </summary>
    public class GameMenuPlayTests
    {
        [SetUp]
        public void SetUp() => SliceTestScene.Isolate(saves: new MemorySaveStorage());

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static RpgService Rpg => Object.FindAnyObjectByType<RpgService>();

        static UiRoot Ui => Object.FindAnyObjectByType<UiRoot>();

        static FirstPersonController Player => Object.FindAnyObjectByType<FirstPersonController>();

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator Press(VirtualInput input, Key key)
        {
            input.Hold(key);
            yield return Frames(2);
            input.Release(key);
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator TheMenuKeysOpenSwitchAndCloseTheMenuAndItStopsTheWorld()
        {
            using (var input = new VirtualInput())
            {
                yield return SliceTestScene.LoadAndPlay();
                yield return Frames(2);
                yield return Press(input, Key.Tab);
                Assert.AreEqual(UiScreen.GameMenu, Ui.Top);
                Assert.AreEqual(MenuTab.Inventory, Ui.Menu.Tab);
                Assert.IsTrue(Ui.Menu.IsShown);
                Assert.AreEqual(FlowModal.Menu, SliceTestScene.Flow.Modal);
                Assert.AreEqual(0f, Time.timeScale, "the world stops behind the menu");
                Assert.IsFalse(Player.enabled);
                Assert.IsFalse(Ui.Hud.Shown, "no HUD under the menu");

                yield return Press(input, Key.J);
                Assert.AreEqual(UiScreen.GameMenu, Ui.Top);
                Assert.AreEqual(MenuTab.Journal, Ui.Menu.Tab, "another tab's key switches");
                yield return Press(input, Key.K);
                Assert.AreEqual(MenuTab.Character, Ui.Menu.Tab);
                yield return Press(input, Key.K);
                Assert.AreEqual(UiScreen.None, Ui.Top, "the tab's own key closes it");
                Assert.AreEqual(1f, Time.timeScale);
                Assert.IsTrue(Player.enabled);
                Assert.IsTrue(Ui.Hud.Shown);
            }
        }

        [UnityTest]
        public IEnumerator EscClosesTheMenuAndAPauseKeepsIt()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Ui.ToggleMenu(MenuTab.Journal);
            yield return null;
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            yield return null;
            Assert.IsTrue(SliceTestScene.Flow.Resume());
            yield return null;
            Assert.AreEqual(UiScreen.GameMenu, Ui.Top, "back to the menu after a pause");
            Assert.AreEqual(0f, Time.timeScale, "and the world is still stopped");
            Ui.OnBack(fromBButton: false);
            yield return null;
            Assert.AreEqual(UiScreen.None, Ui.Top);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator TheInventoryUnequipsTheWornGunThroughItsButton()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Ui.ToggleMenu(MenuTab.Inventory);
            yield return null;
            var page = (InventoryPage)Ui.Menu.Page(MenuTab.Inventory);
            Item pistol = Rpg.Core.Inventory.EquippedItem("secondary");
            page.Tiles.First(t => t.Id == pistol.Uid).Focus();
            yield return null;
            Assert.AreEqual(pistol.Uid, page.Selected);
            MenuButton unequip = page.ActionButtons.First(b => b.Text == "UNEQUIP");
            unequip.Activate();
            yield return null;
            Assert.IsNull(Rpg.Core.Inventory.EquippedItem("secondary"), "the core took it off");
            Assert.IsTrue(page.ActionButtons.Any(b => b.Text == "EQUIP"), "the page shows the new state");
        }

        [UnityTest]
        public IEnumerator TheCharacterSheetRaisesAnAttributeAndBuysAPerk()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Rpg.Core.AddXp(20000);
            Ui.ToggleMenu(MenuTab.Character);
            yield return null;
            var page = (CharacterPage)Ui.Menu.Page(MenuTab.Character);
            int body = Rpg.Core.Stats.Attributes["body"];
            page.PlusButtons[0].Activate();
            yield return null;
            Assert.AreEqual(body + 1, Rpg.Core.Stats.Attributes["body"]);

            string open = RpgStats.Attributes.SelectMany(a => CharacterModel.Tree(Rpg.Core, a).Select(p => (a, p))).First(x => x.p.Blocked == null).p.Id;
            string tree = RpgStats.Attributes.First(a => CharacterModel.Tree(Rpg.Core, a).Any(p => p.Id == open));
            page.Show(tree);
            page.MiddleRows.First(r => r.Id == open).Activate();
            yield return null;
            Assert.AreEqual(1, Rpg.Core.Perks.RankOf(open), "Submit on an open perk buys it");
        }

        [UnityTest]
        public IEnumerator TheJournalTracksAnotherJob()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Rpg.Core.AddXp(5000);
            string other = Rpg.Core.Quests.Available().Select(q => (string)q["id"]).First();
            Assert.IsTrue(Rpg.Core.StartQuest(other).Ok);
            Ui.ToggleMenu(MenuTab.Journal);
            yield return null;
            var page = (JournalPage)Ui.Menu.Page(MenuTab.Journal);
            Assert.AreEqual("q_main_ghost", page.Rows[0].Id, "the tracked job first");
            page.Rows.First(r => r.Id == other).Activate();
            yield return null;
            Assert.AreEqual(other, Rpg.Core.Quests.Tracked);
            Assert.AreEqual(other, page.Rows[0].Id, "now it is first");
        }
    }
}
#endif
