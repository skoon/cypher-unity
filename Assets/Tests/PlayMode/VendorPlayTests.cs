#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Cypher.Core;
using Cypher.Player;
using Cypher.Rpg;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>
    /// The vendor screen in the real Slice scene: asking Kaz what she sells hands the conversation over to her shop; the shop stops the world; a line buys and sells
    /// through the core; Esc closes the shop in the core too.
    /// </summary>
    public class VendorPlayTests
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

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator AskingKazWhatSheSellsOpensHerShopWhenTheConversationEnds()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Rpg.Core.SetFlag("kaz_job_taken", true);
            Assert.IsTrue(Rpg.Core.StartDialogue("dlg_kaz").Ok);
            yield return null;
            Assert.AreEqual(UiScreen.Dialogue, Ui.Top);
            Ui.Conversation.Buttons.Cast<ChoiceButton>().Single(b => b.Choice.Id == "k_o_stock").Activate();
            yield return Frames(2);
            Assert.IsFalse(Rpg.Core.Dialogue.Active);
            Assert.AreEqual(UiScreen.Vendor, Ui.Top, "the shop took over from the conversation");
            Assert.AreEqual("v_kaz", Ui.Vendor.VendorId);
            Assert.AreEqual(FlowModal.Menu, SliceTestScene.Flow.Modal);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsNotEmpty(Ui.Vendor.ShelfRows);
        }

        [UnityTest]
        public IEnumerator ALineBuysAndSellsOneThroughTheCore()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Rpg.Core.AddMoney(100000);
            Assert.IsTrue(Rpg.Core.OpenVendor("v_kaz").Ok);
            yield return null;
            TradeLine buy = Ui.Vendor.Shop.Shelf.First(l => l.Enabled);
            double money = Rpg.Core.Money;
            Ui.Vendor.ShelfRows.First(r => r.Id == buy.Uid).Activate();
            yield return null;
            Assert.AreEqual(money - buy.Price, Rpg.Core.Money);
            Assert.IsNotNull(Rpg.Core.Inventory.Find(buy.Uid));

            TradeLine sell = Ui.Vendor.Shop.Bag.First(l => Rpg.Core.Inventory.Find(l.Uid).TemplateId == "c_maxdoc");
            money = Rpg.Core.Money;
            Ui.Vendor.BagRows.First(r => r.Id == sell.Uid).Activate();
            yield return null;
            Assert.AreEqual(money + sell.Price, Rpg.Core.Money);
            Assert.AreEqual(2, Rpg.Core.Inventory.Count("c_maxdoc"));

            string worn = Rpg.Core.Inventory.EquippedItem("secondary").Uid;
            Ui.Vendor.BagRows.First(r => r.Id == worn).Activate();
            yield return null;
            Assert.IsNotNull(Rpg.Core.Inventory.Find(worn), "a worn gun is not sold");
        }

        [UnityTest]
        public IEnumerator EscClosesTheShopInTheCoreToo()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Assert.IsTrue(Rpg.Core.OpenVendor("v_kaz").Ok);
            yield return null;
            Ui.OnBack(fromBButton: false);
            yield return null;
            Assert.IsNull(Rpg.Core.Economy.OpenVendorId);
            Assert.AreEqual(UiScreen.None, Ui.Top);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(Object.FindAnyObjectByType<FirstPersonController>().enabled);
        }
    }
}
#endif
