using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The inventory's view of the core: categories, an item's detail and actions (each doing what it says through the core), and the comparison with what is worn.</summary>
    public class InventoryModelTests
    {
        static RpgCore Core()
        {
            var core = new RpgCore(null, 20771, new MemorySaveStorage(), () => 1700000000000) { AutosaveEnabled = false };
            core.GiveStartingKit();
            return core;
        }

        static Item ByTemplate(RpgCore core, string id) => core.Inventory.Items.First(i => i.TemplateId == id);

        static ItemActionView ActionOf(ItemView v, ItemAction a) => v.Actions.Single(x => x.Action == a);

        [Test]
        public void CategoriesSplitTheKitByType()
        {
            RpgCore core = Core();
            Assert.AreEqual(core.Inventory.Items.Count, InventoryModel.Count(core, "all"));
            Assert.AreEqual(InventoryModel.Count(core, "all"), InventoryModel.Categories.Skip(1).Sum(c => InventoryModel.Count(core, c.Id)), "every item is in exactly one category");
            Assert.IsTrue(InventoryModel.Items(core, "weapon").Any(i => i.TemplateId == "w_pistol"));
            Assert.IsTrue(InventoryModel.Items(core, "use").Any(i => i.TemplateId == "c_maxdoc"));
            Assert.IsTrue(InventoryModel.Items(core, "hack").Any(i => i.TemplateId == "qh_ping"));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => InventoryModel.Items(core, "nope"));
        }

        [Test]
        public void AWornWeaponCanBeTakenOffButNotDroppedWhileWorn()
        {
            RpgCore core = Core();
            Item pistol = ByTemplate(core, "w_pistol");
            ItemView v = InventoryModel.View(core, pistol);
            Assert.IsTrue(v.Equipped);
            Assert.AreEqual("SECONDARY", v.Kind);
            Assert.AreEqual(pistol.Name.ToUpperInvariant(), v.Name);
            Assert.AreEqual("UNCOMMON", v.RarityLabel);
            Assert.IsNotEmpty(v.Stats);
            Assert.IsTrue(ActionOf(v, ItemAction.Unequip).Enabled);
            Assert.AreEqual("UNEQUIP FIRST", ActionOf(v, ItemAction.Drop).Blocked);
            Assert.IsEmpty(v.Compare, "nothing to compare a worn item with");

            Assert.IsTrue(InventoryModel.Apply(core, pistol, ItemAction.Unequip).Ok);
            Assert.IsNull(core.Inventory.EquippedItem("secondary"));
            ItemView after = InventoryModel.View(core, pistol);
            Assert.IsTrue(ActionOf(after, ItemAction.Equip).Enabled);
            Assert.IsTrue(ActionOf(after, ItemAction.Drop).Enabled);
        }

        [Test]
        public void ASecondGunIsComparedWithTheWornOneAndEquippingSwapsThem()
        {
            RpgCore core = Core();
            Item worn = ByTemplate(core, "w_pistol");
            Assert.IsTrue(core.GiveItem("w_pistol", "legendary").Ok);
            Item other = core.Inventory.Items.Last(i => i.TemplateId == "w_pistol");
            ItemView v = InventoryModel.View(core, other);
            Assert.AreEqual(worn.Name.ToUpperInvariant(), v.Replaces);
            Assert.IsNotEmpty(v.Compare);
            foreach (var c in v.Compare) Assert.IsTrue(c.Delta.StartsWith("+") == c.Better, c.Label);

            Assert.IsTrue(InventoryModel.Apply(core, other, ItemAction.Equip).Ok);
            Assert.AreEqual(other.Uid, core.Inventory.EquippedItem("secondary").Uid);
            Assert.IsFalse(core.Inventory.IsEquipped(worn.Uid));
        }

        [Test]
        public void AConsumableIsUsedOneAtATime()
        {
            RpgCore core = Core();
            Item doc = ByTemplate(core, "c_maxdoc");
            core.Damage(40);
            double hurt = core.Health;
            ItemView v = InventoryModel.View(core, doc);
            Assert.AreEqual("DROP ONE", ActionOf(v, ItemAction.Drop).Label);
            Assert.IsTrue(ActionOf(v, ItemAction.Use).Enabled);
            Assert.IsTrue(InventoryModel.Apply(core, doc, ItemAction.Use).Ok);
            Assert.AreEqual(2, core.Inventory.Count("c_maxdoc"));
            Assert.Greater(core.Health, hurt);
            Assert.IsTrue(InventoryModel.Apply(core, doc, ItemAction.Drop).Ok);
            Assert.AreEqual(1, core.Inventory.Count("c_maxdoc"));
        }

        [Test]
        public void AQuickhackGoesInAndOutOfTheDeck()
        {
            RpgCore core = Core();
            Item ping = ByTemplate(core, "qh_ping");
            ItemView v = InventoryModel.View(core, ping);
            Assert.IsTrue(v.Equipped, "the kit slots it");
            Assert.IsTrue(InventoryModel.Apply(core, ping, ItemAction.Undeck).Ok);
            Assert.IsFalse(core.Inventory.IsEquipped(ping.Uid));
            Assert.IsTrue(ActionOf(InventoryModel.View(core, ping), ItemAction.Deck).Enabled);
            Assert.IsTrue(InventoryModel.Apply(core, ping, ItemAction.Deck).Ok);
            Assert.AreEqual(ping.Uid, core.Inventory.Deck[0]);
        }

        [Test]
        public void ChromeCanBeInstalledFromTheInventoryWhenTheRulesAllow()
        {
            RpgCore core = Core();
            Assert.IsTrue(core.GiveCyberware("cw_kiroshi_mk1").Ok);
            Item ware = core.Inventory.Items.First(i => i.CyberwareId == "cw_kiroshi_mk1");
            ItemView v = InventoryModel.View(core, ware);
            Assert.IsTrue(ActionOf(v, ItemAction.Install).Enabled);
            Assert.IsTrue(InventoryModel.Apply(core, ware, ItemAction.Install).Ok);
            Assert.IsTrue(core.HasCyberware("cw_kiroshi_mk1"));
            Assert.IsNull(core.Inventory.Items.FirstOrDefault(i => i.CyberwareId == "cw_kiroshi_mk1"), "the carried chrome is used up");

            Assert.IsTrue(core.GiveCyberware("cw_kiroshi_mk3").Ok);
            Item locked = core.Inventory.Items.First(i => i.CyberwareId == "cw_kiroshi_mk3");
            Assert.AreEqual("REQUIREMENTS NOT MET", ActionOf(InventoryModel.View(core, locked), ItemAction.Install).Blocked);
        }

        [Test]
        public void AJobItemCannotBeDropped()
        {
            RpgCore core = Core();
            Assert.IsTrue(core.GiveItem("q_ghost_shard").Ok);
            ItemView v = InventoryModel.View(core, ByTemplate(core, "q_ghost_shard"));
            Assert.AreEqual("JOB ITEM", ActionOf(v, ItemAction.Drop).Blocked);
        }

        [Test]
        public void StatsAreLabelledAndWrittenAsThePrototypeWritesThem()
        {
            Assert.AreEqual("FIRE RATE", InventoryModel.Label("fireRate"));
            Assert.AreEqual("DAMAGE", InventoryModel.Label("damage"));
            Assert.AreEqual("72%", InventoryModel.Value(0.72));
            Assert.AreEqual("28.4", InventoryModel.Value(28.44));
            Assert.AreEqual("240", InventoryModel.Value(240));
            Assert.AreEqual("NOT ENOUGH €$", InventoryModel.Reason("cannot-afford"));
            Assert.AreEqual("SOMETHING NEW", InventoryModel.Reason("something-new"));
        }
    }
}
