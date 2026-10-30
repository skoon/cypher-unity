using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The vendor screen's view of the core's economy: the shelf and the bag at their prices, what cannot be traded and why, and a buy and a sell.</summary>
    public class VendorModelTests
    {
        const string Kaz = "v_kaz";

        static RpgCore Core()
        {
            var core = new RpgCore(null, 20771, new MemorySaveStorage(), () => 1700000000000) { AutosaveEnabled = false };
            core.GiveStartingKit();
            Assert.IsTrue(core.OpenVendor(Kaz).Ok);
            return core;
        }

        [Test]
        public void TheShopListsTheShelfAtTheBuyPriceAndTheBagAtTheSellPrice()
        {
            RpgCore core = Core();
            VendorModel.Shop shop = VendorModel.Read(core, Kaz);
            Assert.IsNotEmpty(shop.Name);
            Assert.AreEqual((int)core.Money, shop.Money);
            Assert.IsNotEmpty(shop.Shelf);
            foreach (TradeLine l in shop.Shelf)
            {
                Item item = core.Economy.State(Kaz).Stock.First(i => i.Uid == l.Uid);
                Assert.AreEqual(core.Economy.BuyPrice(Kaz, item), l.Price);
                if (l.Price > core.Money) Assert.AreEqual("NOT ENOUGH €$", l.Blocked);
                else Assert.IsTrue(l.Blocked == null || l.Blocked == "TOO HEAVY TO CARRY", l.Blocked);
            }
            CollectionAssert.AreEquivalent(core.Inventory.Items.Select(i => i.Uid), shop.Bag.Select(l => l.Uid));
            TradeLine pistol = shop.Bag.Single(l => l.Uid == core.Inventory.EquippedItem("secondary").Uid);
            Assert.IsTrue(pistol.Equipped);
            Assert.AreEqual("UNEQUIP FIRST", pistol.Blocked);
            TradeLine doc = shop.Bag.First(l => core.Inventory.Find(l.Uid).TemplateId == "c_maxdoc");
            Assert.IsTrue(doc.Enabled);
            Assert.AreEqual(core.Economy.SellPrice(Kaz, core.Inventory.Find(doc.Uid)), doc.Price);
        }

        [Test]
        public void BuyingTakesTheMoneyAndGivesTheItem()
        {
            RpgCore core = Core();
            core.AddMoney(100000);
            TradeLine line = VendorModel.Read(core, Kaz).Shelf.First(l => l.Enabled);
            double before = core.Money;
            Assert.IsTrue(VendorModel.Buy(core, Kaz, line.Uid).Ok);
            Assert.AreEqual(before - line.Price, core.Money);
            Assert.IsNotNull(core.Inventory.Find(line.Uid));
            Assert.IsFalse(VendorModel.Read(core, Kaz).Shelf.Any(l => l.Uid == line.Uid), "off the shelf");
        }

        [Test]
        public void WhatCannotBeAffordedIsMarked()
        {
            RpgCore core = Core();
            core.AddMoney(-core.Money);
            Assert.IsTrue(VendorModel.Read(core, Kaz).Shelf.Where(l => l.Price > 0).All(l => l.Blocked == "NOT ENOUGH €$"));
        }

        [Test]
        public void SellingAStackSellsOneAndAWornThingIsRefused()
        {
            RpgCore core = Core();
            Item doc = core.Inventory.Items.First(i => i.TemplateId == "c_maxdoc");
            int price = core.Economy.SellPrice(Kaz, doc);
            double before = core.Money;
            Assert.IsTrue(VendorModel.Sell(core, Kaz, doc.Uid).Ok);
            Assert.AreEqual(2, core.Inventory.Count("c_maxdoc"));
            Assert.AreEqual(before + price, core.Money);

            string worn = core.Inventory.EquippedItem("secondary").Uid;
            RpgResult refused = VendorModel.Sell(core, Kaz, worn);
            Assert.IsFalse(refused.Ok);
            Assert.AreEqual("equipped", refused.Reason);
            Assert.IsNotNull(core.Inventory.Find(worn), "still carried");
        }

        [Test]
        public void TheDiscountIsWrittenOnlyWhenThereIsOne()
        {
            Assert.AreEqual("", VendorModel.DiscountText(0));
            Assert.AreEqual("−12% STREET CRED DISCOUNT", VendorModel.DiscountText(0.12));
        }
    }
}
