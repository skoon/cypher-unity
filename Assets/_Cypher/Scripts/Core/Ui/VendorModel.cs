using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>A line of the shop: an item on the shelf or in the player's bag, its price for one, and why it cannot be traded (null when it can).</summary>
    public readonly struct TradeLine
    {
        public readonly string Uid, Name, Kind, Rarity, RarityColor;
        public readonly int Price, Qty;
        public readonly bool Equipped;
        public readonly string Blocked;

        public TradeLine(string uid, string name, string kind, string rarity, string rarityColor, int price, int qty, bool equipped, string blocked)
        {
            Uid = uid; Name = name; Kind = kind; Rarity = rarity; RarityColor = rarityColor; Price = price; Qty = qty; Equipped = equipped; Blocked = blocked;
        }

        public bool Enabled => Blocked == null;
    }

    /// <summary>
    /// The vendor screen from the core's economy (the prototype had the trading rules but no screen): who is selling, their greeting and the player's discount,
    /// the shelf at the buy price with what cannot be afforded or carried marked, and the player's things at the sell price (job items cannot be sold; worn things
    /// must be taken off first). Pure.
    /// </summary>
    public static class VendorModel
    {
        public sealed class Shop
        {
            public string Id, Name, Kind, Greeting;
            public double Discount;
            public int Money;
            public List<TradeLine> Shelf = new List<TradeLine>();
            public List<TradeLine> Bag = new List<TradeLine>();
        }

        public static Shop Read(RpgCore core, string vendorId)
        {
            if (core == null) throw new ArgumentNullException(nameof(core));
            JsonObject snap = core.Economy.Snapshot(vendorId);
            var shop = new Shop
            {
                Id = vendorId, Name = ((string)snap["name"]).ToUpperInvariant(), Kind = ((snap["kind"] as string) ?? "").ToUpperInvariant(),
                Greeting = (snap["greeting"] as string) ?? "", Discount = (double)snap["discount"], Money = (int)core.Money,
            };
            foreach (JsonObject s in ((List<object>)snap["stock"]).Cast<JsonObject>())
            {
                var item = (Item)s["item"];
                int price = (int)(double)s["price"];
                string blocked = core.Money < price ? InventoryModel.Reason("cannot-afford") : !core.Inventory.CanFit(item) ? InventoryModel.Reason("overweight") : null;
                shop.Shelf.Add(Line(item, price, false, blocked));
            }
            foreach (Item item in core.Inventory.Items)
            {
                bool worn = core.Inventory.IsEquipped(item.Uid);
                string blocked = item.Type == "quest" ? InventoryModel.Reason("quest-item") : worn ? "UNEQUIP FIRST" : null;
                shop.Bag.Add(Line(item, core.Economy.SellPrice(vendorId, item), worn, blocked));
            }
            return shop;
        }

        static TradeLine Line(Item item, int price, bool worn, string blocked)
        {
            Rarity r = RpgItems.RarityOf(item.Rarity);
            return new TradeLine(item.Uid, item.Name.ToUpperInvariant(), ((item.Slot ?? item.CyberSlot ?? item.Type) ?? "").Replace('_', ' ').ToUpperInvariant(), item.Rarity, r.Color,
                price, item.Qty, worn, blocked);
        }

        /// <summary>The discount as the shop's heading writes it, or empty.</summary>
        public static string DiscountText(double discount) => discount > 0 ? $"−{RpgJs.Round(discount * 100)}% STREET CRED DISCOUNT" : "";

        /// <summary>Buy one off the shelf.</summary>
        public static RpgResult Buy(RpgCore core, string vendorId, string uid) => core.BuyFrom(vendorId, uid);

        /// <summary>Sell one of the player's (a stack sells one at a time). A worn item is refused here, before the core would take it off the player.</summary>
        public static RpgResult Sell(RpgCore core, string vendorId, string uid)
        {
            if (core.Inventory.IsEquipped(uid)) return RpgResult.Fail("equipped");
            return core.SellTo(vendorId, uid, 1);
        }
    }
}
