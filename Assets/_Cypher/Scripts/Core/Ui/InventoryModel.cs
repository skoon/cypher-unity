using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cypher.Core
{
    /// <summary>Something the inventory can do with an item.</summary>
    public enum ItemAction { Equip, Unequip, Use, Install, Deck, Undeck, Drop }

    /// <summary>An action button: what it says, and why it cannot be pressed (null when it can).</summary>
    public readonly struct ItemActionView
    {
        public readonly ItemAction Action;
        public readonly string Label, Blocked;

        public ItemActionView(ItemAction action, string label, string blocked)
        {
            Action = action; Label = label; Blocked = blocked;
        }

        public bool Enabled => Blocked == null;
    }

    /// <summary>An item as the inventory shows it.</summary>
    public sealed class ItemView
    {
        public string Uid, Name, Kind, Rarity, RarityLabel, RarityColor, Desc;
        public int Qty, Level, Value;
        public double Weight;
        public bool Equipped;
        public List<(string Label, string Value)> Stats = new List<(string, string)>();
        public List<string> Mods = new List<string>();
        public List<ItemActionView> Actions = new List<ItemActionView>();
        /// <summary>Against what is worn in the same slot: label, signed difference as written, and whether it is an improvement.</summary>
        public List<(string Label, string Delta, bool Better)> Compare = new List<(string, string, bool)>();
        /// <summary>The name of what it would replace, or null.</summary>
        public string Replaces;
    }

    /// <summary>
    /// The inventory from the core's items, with the prototype's categories (all, weapons, cyberware, quickhacks, apparel, consumables, junk and quest) and, unlike
    /// the prototype's (whose buttons did nothing), the actions that work: equip and unequip, use, install chrome, load or unload the quickhack deck, drop; and the
    /// comparison with what is worn in the same slot. Pure.
    /// </summary>
    public static class InventoryModel
    {
        public static readonly IReadOnlyList<(string Id, string Label, string[] Types)> Categories = new[]
        {
            ("all", "ALL", (string[])null),
            ("weapon", "WEAPONS", new[] { "weapon" }),
            ("cyber", "CYBERWARE", new[] { "cyberware" }),
            ("hack", "QUICKHACKS", new[] { "quickhack" }),
            ("gear", "APPAREL", new[] { "clothing" }),
            ("use", "CONSUMABLES", new[] { "consumable" }),
            ("junk", "JUNK / QUEST", new[] { "junk", "quest" }),
        };

        public static bool InCategory(Item item, string category)
        {
            var c = Categories.FirstOrDefault(x => x.Id == category);
            if (c.Id == null) throw new ArgumentOutOfRangeException(nameof(category), category, "unknown inventory category");
            return c.Types == null || c.Types.Contains(item.Type);
        }

        /// <summary>The items of a category, in the inventory's order.</summary>
        public static List<Item> Items(RpgCore core, string category) => core.Inventory.Items.Where(i => InCategory(i, category)).ToList();

        public static int Count(RpgCore core, string category) => core.Inventory.Items.Count(i => InCategory(i, category));

        /// <summary>A stat key as a label: <c>fireRate</c> is <c>FIRE RATE</c>.</summary>
        public static string Label(string key)
        {
            var sb = new StringBuilder();
            foreach (char ch in key)
            {
                if (char.IsUpper(ch) && sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>A stat's value as the prototype writes it: a fraction as a percentage, otherwise to one decimal.</summary>
        public static string Value(double v) => v > 0 && v < 1
            ? RpgJs.Round(v * 100).ToString(CultureInfo.InvariantCulture) + "%"
            : (RpgJs.Round(v * 10) / 10).ToString(CultureInfo.InvariantCulture);

        static string Signed(double d, bool fraction)
        {
            string body = fraction ? RpgJs.Round(Math.Abs(d) * 100).ToString(CultureInfo.InvariantCulture) + "%" : (RpgJs.Round(Math.Abs(d) * 10) / 10).ToString(CultureInfo.InvariantCulture);
            return (d >= 0 ? "+" : "−") + body;
        }

        public static ItemView View(RpgCore core, Item item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            Rarity rarity = RpgItems.RarityOf(item.Rarity);
            bool equipped = core.Inventory.IsEquipped(item.Uid);
            var v = new ItemView
            {
                Uid = item.Uid, Name = item.Name.ToUpperInvariant(), Kind = ((item.Slot ?? item.CyberSlot ?? item.Type) ?? "").Replace('_', ' ').Replace('-', ' ').ToUpperInvariant(),
                Rarity = item.Rarity, RarityLabel = rarity.Label.ToUpperInvariant(), RarityColor = rarity.Color, Desc = item.Desc ?? "", Qty = item.Qty, Level = item.Level,
                Value = item.Value, Weight = item.Weight, Equipped = equipped,
            };
            foreach (KeyValuePair<string, double> s in item.Stats) v.Stats.Add((Label(s.Key), Value(s.Value)));
            foreach (AffixRoll a in item.Affixes) v.Mods.Add(a.Label != null ? a.Label.ToUpperInvariant() : $"{Label(a.Stat)} {(a.Op == "mul" ? "×" : "+")}{a.Value}");
            v.Actions = Actions(core, item, equipped);

            if (item.Slot != null && !equipped && core.Inventory.EquippedItem(item.Slot) is Item worn)
            {
                v.Replaces = worn.Name.ToUpperInvariant();
                var keys = item.Stats.Select(s => s.Key).Concat(worn.Stats.Select(s => s.Key)).Distinct();
                foreach (string k in keys)
                {
                    double a = item.Stat(k), b = worn.Stat(k), d = a - b;
                    if (Math.Abs(d) < 1e-9) continue;
                    bool fraction = Math.Max(Math.Abs(a), Math.Abs(b)) < 1;
                    v.Compare.Add((Label(k), Signed(d, fraction), d > 0));
                }
            }
            return v;
        }

        static List<ItemActionView> Actions(RpgCore core, Item item, bool equipped)
        {
            var list = new List<ItemActionView>();
            if (item.Slot != null)
            {
                if (equipped) list.Add(new ItemActionView(ItemAction.Unequip, "UNEQUIP", null));
                else list.Add(new ItemActionView(ItemAction.Equip, "EQUIP", null));
            }
            if (item.Type == "consumable") list.Add(new ItemActionView(ItemAction.Use, "USE", item.Use == null ? "NOTHING TO USE" : null));
            if (item.Type == "quickhack")
            {
                if (equipped) list.Add(new ItemActionView(ItemAction.Undeck, "REMOVE FROM DECK", null));
                else list.Add(new ItemActionView(ItemAction.Deck, "LOAD INTO DECK", FreeDeckSlot(core) < 0 ? "DECK FULL" : null));
            }
            if (item.Type == "cyberware" && item.CyberwareId != null)
            {
                RpgResult can = core.CanInstallCyberware(item.CyberwareId);
                list.Add(new ItemActionView(ItemAction.Install, "INSTALL", can.Ok ? null : Reason(can.Reason)));
            }
            string dropBlocked = item.Type == "quest" ? "JOB ITEM" : equipped ? "UNEQUIP FIRST" : null;
            list.Add(new ItemActionView(ItemAction.Drop, item.Qty > 1 ? "DROP ONE" : "DROP", dropBlocked));
            return list;
        }

        /// <summary>The first empty deck slot, or -1.</summary>
        public static int FreeDeckSlot(RpgCore core) => Array.IndexOf(core.Inventory.Deck, null);

        /// <summary>A rule's refusal (the core's reason codes) in words.</summary>
        public static string Reason(string reason)
        {
            switch (reason)
            {
                case null: return null;
                case "slot-full": return "SLOT FULL";
                case "no-capacity": return "NOT ENOUGH CAPACITY";
                case "requirements": return "REQUIREMENTS NOT MET";
                case "already-installed": return "ALREADY INSTALLED";
                case "max-rank": return "MAXED";
                case "no-points": return "NO POINTS";
                case "at-max": return "AT MAXIMUM";
                case "cannot-afford":
                case "insufficient-funds": return "NOT ENOUGH €$";
                case "overweight": return "TOO HEAVY TO CARRY";
                case "quest-item": return "JOB ITEM";
                case "not-in-stock": return "SOLD OUT";
                case "refused": return "THEY WON'T DEAL";
                default: return reason.Replace('-', ' ').ToUpperInvariant();
            }
        }

        /// <summary>Do an action; the core's result.</summary>
        public static RpgResult Apply(RpgCore core, Item item, ItemAction action)
        {
            switch (action)
            {
                case ItemAction.Equip: return core.EquipItem(item.Uid);
                case ItemAction.Unequip: return core.UnequipSlot(item.Slot);
                case ItemAction.Use: return core.UseItem(item.Uid);
                case ItemAction.Install: return core.InstallCyberware(item.CyberwareId);
                case ItemAction.Deck:
                {
                    int slot = FreeDeckSlot(core);
                    return slot < 0 ? RpgResult.Fail("deck-full") : core.EquipHack(item.Uid, slot);
                }
                case ItemAction.Undeck: return core.UnequipHack(Array.IndexOf(core.Inventory.Deck, item.Uid));
                case ItemAction.Drop: return core.RemoveItem(item.Uid, 1);
                default: throw new ArgumentOutOfRangeException(nameof(action), action, "unknown item action");
            }
        }
    }
}
