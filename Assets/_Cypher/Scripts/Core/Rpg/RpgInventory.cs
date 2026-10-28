using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// What is carried: stacking, weight against capacity, the equip slots and the quickhack deck. A port of <c>rpg/Inventory.js</c>. Weight counts everything,
    /// equipped items included; an add that would go over capacity is refused whole (half a stack is a trap).
    /// </summary>
    public sealed class Inventory
    {
        public double Capacity;
        public int DeckSize { get; private set; }
        public readonly List<Item> Items = new List<Item>();
        /// <summary>Equip slot → item uid (or null), every slot present, in <see cref="RpgItems.EquipSlots"/> order.</summary>
        public readonly Dictionary<string, string> Equipped = new Dictionary<string, string>();
        public string[] Deck { get; private set; }

        static string StackKey(Item i) => $"{i.TemplateId}|{i.Rarity}|{RpgJs.Str(i.Level)}";

        public Inventory(double capacity = 100, int deckSize = 4, IEnumerable<Item> items = null, IReadOnlyDictionary<string, string> equipped = null, IReadOnlyList<string> deck = null)
        {
            Capacity = capacity;
            DeckSize = deckSize;
            if (items != null)
            {
                foreach (Item it in items) Items.Add(AssertItem(it.Copy()));
            }
            foreach (string s in RpgItems.EquipSlots) Equipped[s] = equipped != null && equipped.TryGetValue(s, out string uid) ? uid : null;
            Deck = new string[deckSize];
            for (int i = 0; i < deckSize; i++) Deck[i] = deck != null && i < deck.Count ? deck[i] : null;
            ValidateReferences();
        }

        static Item AssertItem(Item item)
        {
            if (item == null) throw new ArgumentException("Inventory: item must be an object");
            if (item.Uid == null) throw new ArgumentException("Inventory: item is missing \"uid\"");
            if (item.TemplateId == null) throw new ArgumentException("Inventory: item is missing \"templateId\"");
            if (item.Type == null) throw new ArgumentException("Inventory: item is missing \"type\"");
            if (item.Name == null) throw new ArgumentException("Inventory: item is missing \"name\"");
            if (item.Rarity == null) throw new ArgumentException("Inventory: item is missing \"rarity\"");
            if (!RpgItems.IsRarity(item.Rarity)) throw new ArgumentOutOfRangeException(nameof(item), $"Inventory: item \"{item.Uid}\" has unknown rarity \"{item.Rarity}\"");
            if (double.IsNaN(item.Weight) || double.IsInfinity(item.Weight) || item.Weight < 0) throw new ArgumentException($"Inventory: item \"{item.Uid}\" has bad weight {item.Weight}");
            if (item.Qty < 1) throw new ArgumentException($"Inventory: item \"{item.Uid}\" has bad qty {item.Qty}");
            return item;
        }

        void ValidateReferences()
        {
            foreach (string slot in RpgItems.EquipSlots)
            {
                string uid = Equipped[slot];
                if (uid != null && Find(uid) == null) throw new InvalidOperationException($"Inventory: equipped slot \"{slot}\" references missing item \"{uid}\"");
            }
            foreach (string uid in Deck)
            {
                if (uid != null && Find(uid) == null) throw new InvalidOperationException($"Inventory: deck references missing item \"{uid}\"");
            }
        }

        // ------------------------------------------------------------ reads

        public Item Find(string uid) => Items.Find(i => i.Uid == uid);

        /// <summary>Units of a template held, across stacks.</summary>
        public int Count(string templateId)
        {
            int n = 0;
            foreach (Item i in Items) if (i.TemplateId == templateId) n += i.Qty;
            return n;
        }

        public bool Has(string templateId, int qty = 1) => Count(templateId) >= qty;

        public double Weight
        {
            get
            {
                double w = 0;
                foreach (Item i in Items) w += i.Weight * i.Qty;
                return RpgJs.Round(w * 100) / 100;
            }
        }

        public double Free => RpgJs.Round((Capacity - Weight) * 100) / 100;

        public bool Overweight => Weight > Capacity;

        /// <summary>Distinct stacks.</summary>
        public int Size => Items.Count;

        public List<Item> ByType(string type)
        {
            var list = Items.FindAll(i => i.Type == type);
            var sorted = new List<Item>(System.Linq.Enumerable.OrderBy(list, i => i, Comparer<Item>.Create(RpgItems.Compare)));
            return sorted;
        }

        public Item EquippedItem(string slot)
        {
            if (!RpgItems.IsEquipSlot(slot)) throw new ArgumentOutOfRangeException(nameof(slot), $"Unknown equip slot \"{slot}\"");
            string uid = Equipped[slot];
            return uid != null ? Find(uid) : null;
        }

        public bool IsEquipped(string uid) => Equipped.ContainsValue(uid) || Array.IndexOf(Deck, uid) >= 0;

        public Item[] DeckItems() => Array.ConvertAll(Deck, uid => uid != null ? Find(uid) : null);

        /// <summary>The modifiers of everything equipped, each with its source (<c>item:&lt;uid&gt;</c>).</summary>
        public List<(string Source, IReadOnlyList<StatModifier> Mods)> EquippedModifiers()
        {
            var out_ = new List<(string, IReadOnlyList<StatModifier>)>();
            foreach (string slot in RpgItems.EquipSlots)
            {
                Item item = EquippedItem(slot);
                if (item != null && item.Mods != null && item.Mods.Count > 0) out_.Add(("item:" + item.Uid, item.Mods));
            }
            return out_;
        }

        // ------------------------------------------------------------ writes

        public bool CanFit(Item item)
        {
            AssertItem(item);
            return Weight + item.Weight * item.Qty <= Capacity + 1e-6;
        }

        /// <summary>Add an item, filling existing stacks first where the template stacks.</summary>
        public RpgResult Add(Item item)
        {
            AssertItem(item);
            if (!CanFit(item)) return new RpgResult { Ok = false, Reason = "overweight", Item = item };
            if (item.MaxStack > 1)
            {
                string key = StackKey(item);
                int remaining = item.Qty;
                bool merged = false;
                foreach (Item existing in Items)
                {
                    if (remaining <= 0) break;
                    if (StackKey(existing) != key) continue;
                    int room = existing.MaxStack - existing.Qty;
                    if (room <= 0) continue;
                    int move = Math.Min(room, remaining);
                    existing.Qty += move;
                    remaining -= move;
                    merged = true;
                }
                if (remaining <= 0) return new RpgResult { Ok = true, Merged = merged, Item = item };
                // the rest starts new stacks, split at maxStack
                Item first = null;
                while (remaining > 0)
                {
                    int take = Math.Min(item.MaxStack, remaining);
                    Item copy = item.Copy();
                    copy.Uid = remaining == item.Qty ? item.Uid : $"{item.Uid}s{remaining}";
                    copy.Qty = take;
                    Items.Add(copy);
                    if (first == null) first = copy;
                    remaining -= take;
                }
                return new RpgResult { Ok = true, Merged = merged, Item = first };
            }
            Items.Add(item);
            return new RpgResult { Ok = true, Merged = false, Item = item };
        }

        public RpgResult Remove(string uid, int qty = 1)
        {
            if (qty < 1) throw new ArgumentOutOfRangeException(nameof(qty), qty, "remove: bad qty");
            int idx = Items.FindIndex(i => i.Uid == uid);
            if (idx < 0) return RpgResult.Fail("not-found");
            Item item = Items[idx];
            if (item.Qty < qty) return new RpgResult { Ok = false, Reason = "not-enough", Removed = 0 };
            item.Qty -= qty;
            if (item.Qty == 0)
            {
                Items.RemoveAt(idx);
                foreach (string slot in RpgItems.EquipSlots) if (Equipped[slot] == uid) Equipped[slot] = null;
                for (int i = 0; i < Deck.Length; i++) if (Deck[i] == uid) Deck[i] = null;
            }
            return new RpgResult { Ok = true, Removed = qty, Item = item };
        }

        /// <summary>Take units of a template across stacks.</summary>
        public RpgResult RemoveByTemplate(string templateId, int qty = 1)
        {
            if (Count(templateId) < qty) return RpgResult.Fail("not-enough");
            int left = qty;
            foreach (Item item in Items.ToArray())
            {
                if (left <= 0) break;
                if (item.TemplateId != templateId) continue;
                int take = Math.Min(item.Qty, left);
                Remove(item.Uid, take);
                left -= take;
            }
            return new RpgResult { Ok = true, Removed = qty };
        }

        public RpgResult Equip(string uid)
        {
            Item item = Find(uid);
            if (item == null) return RpgResult.Fail("not-found");
            if (item.Slot == null) return RpgResult.Fail("not-equippable");
            string prevUid = Equipped[item.Slot];
            Item replaced = prevUid != null ? Find(prevUid) : null;
            Equipped[item.Slot] = uid;
            return new RpgResult { Ok = true, Slot = item.Slot, Item = item, HasReplaced = true, Replaced = replaced };
        }

        public RpgResult Unequip(string slot)
        {
            if (!RpgItems.IsEquipSlot(slot)) throw new ArgumentOutOfRangeException(nameof(slot), $"Unknown equip slot \"{slot}\"");
            string uid = Equipped[slot];
            if (uid == null) return RpgResult.Fail("empty");
            Equipped[slot] = null;
            return new RpgResult { Ok = true, Slot = slot, Item = Find(uid) };
        }

        /// <summary>Put a quickhack in a deck slot; a hack sits in one slot only.</summary>
        public RpgResult EquipHack(string uid, int index)
        {
            Item item = Find(uid);
            if (item == null) return RpgResult.Fail("not-found");
            if (item.Type != "quickhack") return RpgResult.Fail("not-a-quickhack");
            if (index < 0 || index >= Deck.Length) return RpgResult.Fail("bad-slot");
            string replacedUid = Deck[index];
            for (int i = 0; i < Deck.Length; i++) if (Deck[i] == uid) Deck[i] = null;
            Deck[index] = uid;
            return new RpgResult { Ok = true, Index = index, Item = item, HasReplaced = true, Replaced = replacedUid != null ? Find(replacedUid) : null };
        }

        public RpgResult UnequipHack(int index)
        {
            if (index < 0 || index >= Deck.Length) return RpgResult.Fail("bad-slot");
            string uid = Deck[index];
            if (uid == null) return RpgResult.Fail("empty");
            Deck[index] = null;
            return new RpgResult { Ok = true, Index = index, Item = Find(uid) };
        }

        /// <summary>Grow or shrink the deck (chrome changes the RAM slots).</summary>
        public Inventory SetDeckSize(int n)
        {
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), n, "setDeckSize: bad size");
            var next = new string[n];
            for (int i = 0; i < n; i++) next[i] = i < Deck.Length ? Deck[i] : null;
            Deck = next;
            DeckSize = n;
            return this;
        }

        // ------------------------------------------------------------ saves

        public JsonObject Serialize()
        {
            var items = new List<object>();
            foreach (Item i in Items) items.Add(i.ToJson());
            var equipped = new JsonObject();
            foreach (string s in RpgItems.EquipSlots) equipped[s] = Equipped[s];
            return new JsonObject
            {
                ["capacity"] = Capacity, ["deckSize"] = (double)DeckSize, ["items"] = items, ["equipped"] = equipped, ["deck"] = new List<object>(Deck),
            };
        }

        public static Inventory Deserialize(JsonObject data)
        {
            if (data == null) return new Inventory();
            var items = new List<Item>();
            if (data.TryGetValue("items", out object its))
            {
                foreach (object o in RpgJs.AsList(its)) items.Add(Item.FromJson(o as JsonObject));
            }
            var equipped = new Dictionary<string, string>();
            if (data.TryGetValue("equipped", out object eq) && eq is JsonObject eo)
            {
                foreach (KeyValuePair<string, object> kv in eo) equipped[kv.Key] = kv.Value as string;
            }
            var deck = new List<string>();
            if (data.TryGetValue("deck", out object dk))
            {
                foreach (object o in RpgJs.AsList(dk)) deck.Add(o as string);
            }
            return new Inventory(
                data.TryGetValue("capacity", out object c) && c != null ? RpgJs.Num(c, "capacity") : 100,
                data.TryGetValue("deckSize", out object ds) && ds != null ? RpgJs.Int(ds, "deckSize") : 4,
                items, equipped, deck);
        }
    }
}
