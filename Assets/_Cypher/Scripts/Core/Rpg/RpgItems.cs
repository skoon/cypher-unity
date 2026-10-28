using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cypher.Core
{
    public sealed class Rarity
    {
        public readonly string Key, Label, Color;
        public readonly int Order, Affixes;
        public readonly double StatMul, ValueMul;

        public Rarity(string key, string label, int order, double statMul, double valueMul, int affixes, string color)
        {
            Key = key; Label = label; Order = order; StatMul = statMul; ValueMul = valueMul; Affixes = affixes; Color = color;
        }
    }

    public sealed class AffixDef
    {
        public readonly string Id, Label, Stat, Op;
        public readonly double Min, Max;
        public readonly IReadOnlyList<string> Pools;

        public AffixDef(string id, string label, string stat, string op, double min, double max, params string[] pools)
        {
            Id = id; Label = label; Stat = stat; Op = op; Min = min; Max = max; Pools = pools;
        }
    }

    /// <summary>An affix as rolled onto an item.</summary>
    public sealed class AffixRoll
    {
        public string Id, Label, Stat, Op;
        public double Value;

        public JsonObject ToJson() => new JsonObject { ["id"] = Id, ["label"] = Label, ["stat"] = Stat, ["op"] = Op, ["value"] = Value };

        public static AffixRoll FromJson(JsonObject o) => new AffixRoll
        {
            Id = o["id"] as string, Label = o["label"] as string, Stat = o["stat"] as string, Op = o["op"] as string, Value = RpgJs.Num(o["value"], "affix value"),
        };
    }

    /// <summary>A timed buff a consumable gives.</summary>
    public sealed class BuffSpec
    {
        public string Id;
        public double Duration;
        public IReadOnlyList<StatModifier> Mods;

        public JsonObject ToJson()
        {
            var mods = new List<object>();
            foreach (StatModifier m in Mods) mods.Add(m.ToJson());
            return new JsonObject { ["id"] = Id, ["duration"] = Duration, ["mods"] = mods };
        }
    }

    /// <summary>What using a consumable does (each part optional).</summary>
    public sealed class ItemUse
    {
        public double? Heal, HealOverTime, Duration, Ram, Stamina;
        public BuffSpec Buff;

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            if (Heal.HasValue) o["heal"] = Heal.Value;
            if (HealOverTime.HasValue) o["healOverTime"] = HealOverTime.Value;
            if (Duration.HasValue) o["duration"] = Duration.Value;
            if (Ram.HasValue) o["ram"] = Ram.Value;
            if (Stamina.HasValue) o["stamina"] = Stamina.Value;
            if (Buff != null) o["buff"] = Buff.ToJson();
            return o;
        }

        public static ItemUse FromJson(JsonObject o)
        {
            if (o == null) return null;
            var use = new ItemUse
            {
                Heal = Opt(o, "heal"), HealOverTime = Opt(o, "healOverTime"), Duration = Opt(o, "duration"), Ram = Opt(o, "ram"), Stamina = Opt(o, "stamina"),
            };
            if (o.TryGetValue("buff", out object b) && b is JsonObject bo)
            {
                var mods = new List<StatModifier>();
                foreach (object m in RpgJs.AsList(bo["mods"])) mods.Add(StatModifier.FromJson(m));
                use.Buff = new BuffSpec { Id = bo["id"] as string, Duration = RpgJs.Num(bo["duration"], "buff duration"), Mods = mods };
            }
            return use;
        }

        static double? Opt(JsonObject o, string key) => o.TryGetValue(key, out object v) && v != null ? RpgJs.Num(v, key) : (double?)null;
    }

    /// <summary>A quickhack's cost and effect.</summary>
    public sealed class HackSpec
    {
        public double Cost, Duration, Power;
        public double? Spread;
        public string Effect;

        public JsonObject ToJson()
        {
            var o = new JsonObject { ["cost"] = Cost, ["duration"] = Duration, ["effect"] = Effect, ["power"] = Power };
            if (Spread.HasValue) o["spread"] = Spread.Value;
            return o;
        }

        public static HackSpec FromJson(JsonObject o)
        {
            if (o == null) return null;
            return new HackSpec
            {
                Cost = RpgJs.Num(o["cost"], "hack cost"), Duration = RpgJs.Num(o["duration"], "hack duration"), Effect = o["effect"] as string,
                Power = RpgJs.Num(o["power"], "hack power"), Spread = o.TryGetValue("spread", out object s) && s != null ? RpgJs.Num(s, "spread") : (double?)null,
            };
        }
    }

    public sealed class ItemTemplate
    {
        public string Id, Type, Slot, AffixPool, Desc;
        public IReadOnlyList<string> BaseNames;
        public double Weight;
        public int Value;
        public int? MaxStack, MinLevel;
        /// <summary>Flavour stats rolled in the given ranges, in the order written.</summary>
        public IReadOnlyList<(string Key, double Min, double Max)> StatRanges = Array.Empty<(string, double, double)>();
        public ItemUse Use;
        public HackSpec Hack;
    }

    /// <summary>
    /// One item as carried. <see cref="Stats"/> are tooltip numbers (damage, rpm); <see cref="Mods"/> are the derived-stat modifiers that apply while it is equipped.
    /// Cyberware carried before it is installed has <see cref="CyberwareId"/> and <see cref="CyberSlot"/>.
    /// </summary>
    public sealed class Item
    {
        public string Uid, TemplateId, CyberwareId, Type, Slot, CyberSlot, Name, Rarity, Desc;
        public int Level, Value, Qty, MaxStack;
        public List<KeyValuePair<string, double>> Stats = new List<KeyValuePair<string, double>>();
        public List<AffixRoll> Affixes = new List<AffixRoll>();
        public List<StatModifier> Mods = new List<StatModifier>();
        public ItemUse Use;
        public HackSpec Hack;
        public double Weight;

        /// <summary>A shallow copy, as <c>{ ...item }</c>: the lists and the use and hack parts are shared.</summary>
        public Item Copy() => (Item)MemberwiseClone();

        public double Stat(string key)
        {
            foreach (KeyValuePair<string, double> kv in Stats) if (kv.Key == key) return kv.Value;
            return double.NaN;
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject { ["uid"] = Uid, ["templateId"] = TemplateId };
            if (CyberwareId != null) o["cyberwareId"] = CyberwareId;
            o["type"] = Type;
            o["slot"] = Slot;
            if (CyberwareId != null) o["cyberSlot"] = CyberSlot;
            o["name"] = Name;
            o["rarity"] = Rarity;
            o["level"] = (double)Level;
            var stats = new JsonObject();
            foreach (KeyValuePair<string, double> kv in Stats) stats[kv.Key] = kv.Value;
            o["stats"] = stats;
            var affixes = new List<object>();
            foreach (AffixRoll a in Affixes) affixes.Add(a.ToJson());
            o["affixes"] = affixes;
            var mods = new List<object>();
            foreach (StatModifier m in Mods) mods.Add(m.ToJson());
            o["mods"] = mods;
            o["use"] = Use?.ToJson();
            o["hack"] = Hack?.ToJson();
            o["weight"] = Weight;
            o["value"] = (double)Value;
            o["qty"] = (double)Qty;
            o["maxStack"] = (double)MaxStack;
            o["desc"] = Desc;
            return o;
        }

        /// <summary>An item from a save or a fixture. Missing fields stay unset, so the inventory's own checks reject a malformed one.</summary>
        public static Item FromJson(JsonObject o)
        {
            if (o == null) throw new ArgumentException("Inventory: item must be an object");
            var item = new Item
            {
                Uid = Str(o, "uid"), TemplateId = Str(o, "templateId"), CyberwareId = Str(o, "cyberwareId"), Type = Str(o, "type"), Slot = Str(o, "slot"),
                CyberSlot = Str(o, "cyberSlot"), Name = Str(o, "name"), Rarity = Str(o, "rarity"), Desc = Str(o, "desc"),
                Level = o.TryGetValue("level", out object lv) && lv != null ? RpgJs.Int(lv, "level") : 1,
                Value = o.TryGetValue("value", out object v) && v != null ? RpgJs.Int(v, "value") : 0,
                Qty = o.TryGetValue("qty", out object q) && q is double qd ? (qd == Math.Floor(qd) ? (int)qd : int.MinValue) : 0,
                MaxStack = o.TryGetValue("maxStack", out object ms) && ms != null ? RpgJs.Int(ms, "maxStack") : 1,
                Weight = o.TryGetValue("weight", out object w) && w is double wd ? wd : double.NaN,
                Use = ItemUse.FromJson(o.TryGetValue("use", out object u) ? u as JsonObject : null),
                Hack = HackSpec.FromJson(o.TryGetValue("hack", out object h) ? h as JsonObject : null),
            };
            if (o.TryGetValue("stats", out object st) && st is JsonObject so)
            {
                foreach (KeyValuePair<string, object> kv in so) item.Stats.Add(new KeyValuePair<string, double>(kv.Key, RpgJs.Num(kv.Value, "item stat")));
            }
            if (o.TryGetValue("affixes", out object af))
            {
                foreach (object a in RpgJs.AsList(af)) item.Affixes.Add(AffixRoll.FromJson((JsonObject)a));
            }
            if (o.TryGetValue("mods", out object md))
            {
                foreach (object m in RpgJs.AsList(md)) item.Mods.Add(StatModifier.FromJson(m));
            }
            return item;
        }

        static string Str(JsonObject o, string key) => o.TryGetValue(key, out object v) ? v as string : null;
    }

    /// <summary>
    /// Item templates, rarities, names and the seeded loot roller: a port of <c>rpg/Items.js</c>. Every roll draws from the prototype's mulberry32 stream in the
    /// prototype's order, so a seed gives the same loot, uids included.
    /// </summary>
    public static class RpgItems
    {
        public static readonly IReadOnlyList<string> ItemTypes = new[] { "weapon", "clothing", "cyberware", "consumable", "quickhack", "junk", "quest" };

        public static readonly IReadOnlyList<string> EquipSlots = new[] { "primary", "secondary", "melee", "head", "face", "torso", "legs", "feet" };

        public static readonly IReadOnlyList<Rarity> Rarities = new[]
        {
            new Rarity("common", "Common", 0, 1.0, 1, 0, "#8b95a6"),
            new Rarity("uncommon", "Uncommon", 1, 1.18, 2.2, 1, "#5ad07a"),
            new Rarity("rare", "Rare", 2, 1.42, 5, 2, "#4aa8ff"),
            new Rarity("epic", "Epic", 3, 1.75, 12, 3, "#c46bff"),
            new Rarity("legendary", "Legendary", 4, 2.2, 30, 4, "#ffb02e"),
        };

        static readonly (string, double)[] RarityBaseWeights = { ("common", 58), ("uncommon", 26), ("rare", 11), ("epic", 4), ("legendary", 1) };

        static readonly string[] Corps = { "Militech", "Arasaka", "Kang Tao", "Tsunami", "Budget Arms", "Malorian", "Rostović", "Zetatech", "Dynalar", "Nokota" };

        static readonly string[] Epithets =
        {
            "Ghostline", "Rainmaker", "Widow", "Static", "Nightjar", "Bloodhound", "Kilowatt", "Salt", "Cathedral", "Hex", "Nine", "Lullaby", "Tin Saint",
            "Blackout", "Choir", "Sundog", "Ash", "Mercy", "Longwire",
        };

        public static readonly IReadOnlyList<AffixDef> Affixes = new[]
        {
            new AffixDef("sharpened", "Sharpened", "critChance", "add", 0.01, 0.05, "weapon", "melee"),
            new AffixDef("butchers", "Butcher's", "critDamage", "add", 0.05, 0.25, "weapon", "melee"),
            new AffixDef("tuned", "Tuned", "rangedDamage", "add", 0.04, 0.18, "weapon"),
            new AffixDef("weighted", "Weighted", "meleeDamage", "add", 0.05, 0.22, "melee"),
            new AffixDef("plated", "Plated", "armor", "add", 3, 16, "clothing"),
            new AffixDef("padded", "Padded", "maxHealth", "add", 8, 42, "clothing"),
            new AffixDef("lined", "Lined", "carryCapacity", "add", 4, 18, "clothing"),
            new AffixDef("wired", "Wired", "maxRam", "add", 1, 2, "clothing", "weapon"),
            new AffixDef("muffled", "Muffled", "stealth", "add", 3, 14, "clothing", "weapon", "melee"),
            new AffixDef("breathable", "Breathable", "maxStamina", "add", 6, 26, "clothing"),
            new AffixDef("scavengers", "Scavenger's", "lootLuck", "add", 0.03, 0.16, "clothing", "weapon"),
            new AffixDef("shielded", "Shielded", "hackStrength", "add", 2, 12, "clothing", "weapon"),
        };

        static (string, double, double)[] R(params (string, double, double)[] r) => r;

        public static readonly IReadOnlyList<ItemTemplate> Templates = new[]
        {
            // weapons
            new ItemTemplate { Id = "w_pistol", Type = "weapon", Slot = "secondary", AffixPool = "weapon", MinLevel = 1, BaseNames = new[] { "Lexington", "Unity", "Nova", "Liberty" },
                Weight = 2.1, Value = 320, StatRanges = R(("damage", 22, 34), ("rpm", 180, 260), ("accuracy", 0.5, 0.72), ("handling", 0.7, 0.95)),
                Desc = "Sidearm. Loud, honest, and cheaper than a taxi out of the sector." },
            new ItemTemplate { Id = "w_smg", Type = "weapon", Slot = "primary", AffixPool = "weapon", MinLevel = 2, BaseNames = new[] { "Saburo", "Shingen", "Pulsar", "Gnat" },
                Weight = 3.4, Value = 640, StatRanges = R(("damage", 14, 22), ("rpm", 620, 900), ("accuracy", 0.34, 0.52), ("handling", 0.6, 0.86)),
                Desc = "Spray weapon for people who solve problems with volume." },
            new ItemTemplate { Id = "w_rifle", Type = "weapon", Slot = "primary", AffixPool = "weapon", MinLevel = 4, BaseNames = new[] { "Copperhead", "Ajax", "Masamune", "Grad" },
                Weight = 5.2, Value = 1150, StatRanges = R(("damage", 38, 56), ("rpm", 320, 480), ("accuracy", 0.62, 0.84), ("handling", 0.4, 0.7)),
                Desc = "Assault rifle. Militech surplus, serial number ground off with a file." },
            new ItemTemplate { Id = "w_shotgun", Type = "weapon", Slot = "primary", AffixPool = "weapon", MinLevel = 3, BaseNames = new[] { "Carnage", "Palica", "Testera", "Doorknock" },
                Weight = 6.1, Value = 980, StatRanges = R(("damage", 70, 110), ("rpm", 60, 110), ("accuracy", 0.2, 0.36), ("handling", 0.3, 0.6)),
                Desc = "Room clearance tool. Also a key, if the door is cheap enough." },
            new ItemTemplate { Id = "w_sniper", Type = "weapon", Slot = "primary", AffixPool = "weapon", MinLevel = 8, BaseNames = new[] { "Nekomata", "Overwatch", "Breakthrough" },
                Weight = 7.4, Value = 2200, StatRanges = R(("damage", 140, 220), ("rpm", 30, 55), ("accuracy", 0.9, 0.99), ("handling", 0.2, 0.42)),
                Desc = "Rail-assisted. Punches through two walls and most opinions." },
            new ItemTemplate { Id = "w_katana", Type = "weapon", Slot = "melee", AffixPool = "melee", MinLevel = 1, BaseNames = new[] { "Jinchu-Maru", "Errata", "Tanto", "Cutlass" },
                Weight = 2.8, Value = 720, StatRanges = R(("damage", 45, 70), ("swingRate", 70, 110), ("reach", 1.6, 2.1)),
                Desc = "Folded steel, monoedge coating. Silent, legal-ish, unforgiving." },
            new ItemTemplate { Id = "w_baton", Type = "weapon", Slot = "melee", AffixPool = "melee", MinLevel = 1, BaseNames = new[] { "Sledge", "Crowbar", "Ballpeen", "Stun Baton" },
                Weight = 3.6, Value = 180, StatRanges = R(("damage", 28, 44), ("swingRate", 55, 85), ("reach", 1.1, 1.5)),
                Desc = "Blunt. Reliable. Does not need ammunition or an explanation." },

            // clothing
            new ItemTemplate { Id = "a_visor", Type = "clothing", Slot = "face", AffixPool = "clothing", MinLevel = 1, BaseNames = new[] { "Respirator", "Half-Mask", "Smog Filter", "Bandana" },
                Weight = 0.4, Value = 220, StatRanges = R(("armorRating", 2, 8)), Desc = "The air out here has opinions. This one disagrees with them." },
            new ItemTemplate { Id = "a_jacket", Type = "clothing", Slot = "torso", AffixPool = "clothing", MinLevel = 1, BaseNames = new[] { "Bomber", "Trench", "Kevlar Vest", "Shell Jacket" },
                Weight = 2.2, Value = 480, StatRanges = R(("armorRating", 6, 20)), Desc = "Rain-shedding, mostly. Ballistic weave in the panels, mostly." },
            new ItemTemplate { Id = "a_pants", Type = "clothing", Slot = "legs", AffixPool = "clothing", MinLevel = 1, BaseNames = new[] { "Cargos", "Tacpants", "Denims", "Runner Leggings" },
                Weight = 1.1, Value = 260, StatRanges = R(("armorRating", 3, 12)), Desc = "Pockets, and a knee panel that will save you exactly once." },
            new ItemTemplate { Id = "a_boots", Type = "clothing", Slot = "feet", AffixPool = "clothing", MinLevel = 1, BaseNames = new[] { "Grip Boots", "Steeltoes", "Runners", "Deck Shoes" },
                Weight = 1.4, Value = 210, StatRanges = R(("armorRating", 2, 9)), Desc = "Soles rated for wet rebar. Nothing is rated for this rain." },
            new ItemTemplate { Id = "a_cap", Type = "clothing", Slot = "head", AffixPool = "clothing", MinLevel = 1, BaseNames = new[] { "Beanie", "Ballcap", "Hood", "Ear-Deck" },
                Weight = 0.3, Value = 140, StatRanges = R(("armorRating", 1, 6)), Desc = "Keeps the rain off the optics. That is the entire pitch." },

            // consumables
            new ItemTemplate { Id = "c_maxdoc", Type = "consumable", BaseNames = new[] { "MaxDoc Mk.1" }, Weight = 0.2, Value = 90, MaxStack = 10, MinLevel = 1,
                Use = new ItemUse { Heal = 55 }, Desc = "Foam and painkiller in an autoinjector. Tastes like copper for an hour." },
            new ItemTemplate { Id = "c_bounceback", Type = "consumable", BaseNames = new[] { "BounceBack Mk.2" }, Weight = 0.2, Value = 210, MaxStack = 10, MinLevel = 4,
                Use = new ItemUse { Heal = 40, HealOverTime = 90, Duration = 12 }, Desc = "Slow-release trauma gel. Keeps working while you keep moving." },
            new ItemTemplate { Id = "c_ram_shard", Type = "consumable", BaseNames = new[] { "RAM Shard" }, Weight = 0.1, Value = 160, MaxStack = 8, MinLevel = 2,
                Use = new ItemUse { Ram = 4 }, Desc = "One-shot memory dump. Burns out on contact with your deck." },
            new ItemTemplate { Id = "c_stim", Type = "consumable", BaseNames = new[] { "Stinger Stim" }, Weight = 0.1, Value = 120, MaxStack = 12, MinLevel = 1,
                Use = new ItemUse { Stamina = 60, Buff = new BuffSpec { Id = "stim", Duration = 20, Mods = new[] { StatModifier.Add("staminaRegen", 6) } } },
                Desc = "Two milligrams of very bad idea. Your legs will forgive you later." },
            new ItemTemplate { Id = "c_synthcoke", Type = "consumable", BaseNames = new[] { "Synthcoke" }, Weight = 0.05, Value = 240, MaxStack = 6, MinLevel = 3,
                Use = new ItemUse { Buff = new BuffSpec { Id = "synthcoke", Duration = 45, Mods = new[] { StatModifier.Add("critChance", 0.08), StatModifier.Add("critDamage", 0.2), StatModifier.Add("stealth", -6) } } },
                Desc = "Everything gets sharper. Including the things looking at you." },

            // quickhacks
            new ItemTemplate { Id = "qh_ping", Type = "quickhack", BaseNames = new[] { "PING" }, Weight = 0, Value = 400, MaxStack = 1, MinLevel = 1,
                Hack = new HackSpec { Cost = 1, Duration = 14, Effect = "reveal", Power = 1 }, Desc = "Walks the subnet and lights up everything wired to it." },
            new ItemTemplate { Id = "qh_reboot_optics", Type = "quickhack", BaseNames = new[] { "REBOOT OPTICS" }, Weight = 0, Value = 900, MaxStack = 1, MinLevel = 3,
                Hack = new HackSpec { Cost = 3, Duration = 8, Effect = "blind", Power = 1 }, Desc = "Their optics reboot. For eight seconds they are just a person in the dark." },
            new ItemTemplate { Id = "qh_overheat", Type = "quickhack", BaseNames = new[] { "OVERHEAT" }, Weight = 0, Value = 1200, MaxStack = 1, MinLevel = 4,
                Hack = new HackSpec { Cost = 4, Duration = 10, Effect = "burn", Power = 6 }, Desc = "Drives the coolant loop backwards. Cooks the chrome and the meat around it." },
            new ItemTemplate { Id = "qh_short_circuit", Type = "quickhack", BaseNames = new[] { "SHORT CIRCUIT" }, Weight = 0, Value = 1600, MaxStack = 1, MinLevel = 6,
                Hack = new HackSpec { Cost = 5, Duration = 1, Effect = "shock", Power = 40 }, Desc = "Dumps the capacitor bank through the nervous system. One spike, one drop." },
            new ItemTemplate { Id = "qh_contagion", Type = "quickhack", BaseNames = new[] { "CONTAGION" }, Weight = 0, Value = 2400, MaxStack = 1, MinLevel = 9,
                Hack = new HackSpec { Cost = 6, Duration = 16, Effect = "contagion", Power = 8, Spread = 3 }, Desc = "Biotoxin routed through the immune monitor. Jumps to whoever stands too close." },
            new ItemTemplate { Id = "qh_cyberpsychosis", Type = "quickhack", BaseNames = new[] { "CYBERPSYCHOSIS" }, Weight = 0, Value = 4800, MaxStack = 1, MinLevel = 14,
                Hack = new HackSpec { Cost = 9, Duration = 12, Effect = "berserk", Power = 1 }, Desc = "Removes the part that recognises faces. Everything left is a threat." },

            // junk
            new ItemTemplate { Id = "j_scrap", Type = "junk", BaseNames = new[] { "Scrap Alloy" }, Weight = 0.5, Value = 8, MaxStack = 99, MinLevel = 1, Desc = "Someone else's wall." },
            new ItemTemplate { Id = "j_wire", Type = "junk", BaseNames = new[] { "Copper Bundle" }, Weight = 0.3, Value = 12, MaxStack = 99, MinLevel = 1, Desc = "Stripped from a dead lamp post. Still warm." },
            new ItemTemplate { Id = "j_chipset", Type = "junk", BaseNames = new[] { "Burnt Chipset" }, Weight = 0.2, Value = 26, MaxStack = 99, MinLevel = 1, Desc = "Melted around the edges. The middle still talks." },
            new ItemTemplate { Id = "j_glass", Type = "junk", BaseNames = new[] { "Optic Glass" }, Weight = 0.2, Value = 34, MaxStack = 99, MinLevel = 3, Desc = "Lens blank. Ripperdocs pay for a clean one." },
            new ItemTemplate { Id = "j_syringe", Type = "junk", BaseNames = new[] { "Spent Injector" }, Weight = 0.1, Value = 6, MaxStack = 99, MinLevel = 1, Desc = "Recycling pays four eddies. The sidewalk pays nothing." },
            new ItemTemplate { Id = "j_datashard", Type = "junk", BaseNames = new[] { "Cracked Shard" }, Weight = 0.1, Value = 45, MaxStack = 99, MinLevel = 5, Desc = "Half a personality on a broken shard. Mostly adverts." },

            // quest items
            new ItemTemplate { Id = "q_ghost_shard", Type = "quest", BaseNames = new[] { "Unmarked Shard" }, Weight = 0.1, Value = 0, MaxStack = 1, MinLevel = 1,
                Desc = "No manufacturer mark. Warm to the touch, which shards should never be." },
            new ItemTemplate { Id = "q_ledger", Type = "quest", BaseNames = new[] { "Marlene's Ledger" }, Weight = 0.4, Value = 0, MaxStack = 1, MinLevel = 1,
                Desc = "Paper. Actual paper. Nobody can hack a notebook and that is the point." },
            new ItemTemplate { Id = "q_bloodpump", Type = "quest", BaseNames = new[] { "Sealed Perfusion Case" }, Weight = 3.2, Value = 0, MaxStack = 1, MinLevel = 1,
                Desc = "A heart in a box, ticking down. The box is worth more than the block." },
            new ItemTemplate { Id = "q_relay_key", Type = "quest", BaseNames = new[] { "Substation Master Key" }, Weight = 0.2, Value = 0, MaxStack = 1, MinLevel = 1,
                Desc = "Physical key for a grid that pretends it went digital in 2049." },
            new ItemTemplate { Id = "q_evidence_holo", Type = "quest", BaseNames = new[] { "Holo: Garage Feed" }, Weight = 0.1, Value = 0, MaxStack = 1, MinLevel = 1,
                Desc = "Nine seconds of footage worth more than nine years of your life." },
        };

        /// <summary>Type weights for a drop that does not name a type.</summary>
        public static readonly IReadOnlyList<(string, double)> DefaultTypeWeights = new[]
        {
            ("weapon", 20.0), ("clothing", 18.0), ("consumable", 22.0), ("junk", 26.0), ("quickhack", 6.0), ("cyberware", 8.0),
        };

        static readonly Dictionary<string, ItemTemplate> TemplateById = new Dictionary<string, ItemTemplate>();
        static readonly Dictionary<string, Rarity> RarityByKey = new Dictionary<string, Rarity>();

        static RpgItems()
        {
            foreach (Rarity r in Rarities) RarityByKey.Add(r.Key, r);
            foreach (ItemTemplate t in Templates)
            {
                if (TemplateById.ContainsKey(t.Id)) throw new InvalidOperationException($"Duplicate item template \"{t.Id}\"");
                TemplateById.Add(t.Id, t);
                if (!((IList<string>)ItemTypes).Contains(t.Type)) throw new InvalidOperationException($"Template \"{t.Id}\" has unknown type \"{t.Type}\"");
                if (t.Slot != null && !((IList<string>)EquipSlots).Contains(t.Slot)) throw new InvalidOperationException($"Template \"{t.Id}\" has unknown slot \"{t.Slot}\"");
                if (t.BaseNames == null || t.BaseNames.Count == 0) throw new InvalidOperationException($"Template \"{t.Id}\" has no base names");
            }
        }

        public static ItemTemplate Template(string id)
        {
            if (id == null || !TemplateById.TryGetValue(id, out ItemTemplate t)) throw new ArgumentOutOfRangeException(nameof(id), $"Unknown item template \"{id}\"");
            return t;
        }

        public static bool IsRarity(string key) => key != null && RarityByKey.ContainsKey(key);

        public static Rarity RarityOf(string key)
        {
            if (key == null || !RarityByKey.TryGetValue(key, out Rarity r)) throw new ArgumentOutOfRangeException(nameof(key), $"Unknown rarity \"{key}\"");
            return r;
        }

        public static bool IsEquipSlot(string slot) => slot != null && ((IList<string>)EquipSlots).Contains(slot);

        static T Pick<T>(Func<double> rng, IReadOnlyList<T> arr) => arr[Math.Min(arr.Count - 1, (int)Math.Floor(rng() * arr.Count))];

        static double RollRange(Func<double> rng, double min, double max) => min + rng() * (max - min);

        /// <summary>A weighted pick over keys in order.</summary>
        public static string WeightedPick(Func<double> rng, IReadOnlyList<(string Key, double Weight)> weights)
        {
            double total = 0;
            foreach ((string _, double w) in weights) total += w;
            if (!(total > 0)) throw new ArgumentOutOfRangeException(nameof(weights), "weightedPick: total weight must be > 0");
            double r = rng() * total;
            foreach ((string key, double w) in weights)
            {
                r -= w;
                if (r <= 0) return key;
            }
            return weights[weights.Count - 1].Key;
        }

        /// <summary>Rarity weights shifted towards the top by level, loot luck and the drop's tier (0 trash, 1 normal, 2 stash).</summary>
        public static IReadOnlyList<(string Key, double Weight)> RarityWeights(double level = 1, double luck = 0, double tier = 1)
        {
            double push = level * 0.05 + luck * 4 + tier * 0.55;
            return new[]
            {
                ("common", Math.Max(4, RarityBaseWeights[0].Item2 - push * 9)),
                ("uncommon", RarityBaseWeights[1].Item2 + push * 1.6),
                ("rare", RarityBaseWeights[2].Item2 + push * 3.4),
                ("epic", RarityBaseWeights[3].Item2 + push * 2.6),
                ("legendary", RarityBaseWeights[4].Item2 + push * 1.4),
            };
        }

        static double LevelScale(int level) => 1 + Math.Max(0, level - 1) * 0.075;

        static List<AffixRoll> RollAffixes(Func<double> rng, string pool, int count, double rarityMul, int level)
        {
            var chosen = new List<AffixRoll>();
            if (count <= 0 || pool == null) return chosen;
            var candidates = new List<AffixDef>();
            foreach (AffixDef a in Affixes) if (((IList<string>)a.Pools).Contains(pool)) candidates.Add(a);
            if (candidates.Count == 0) return chosen;
            var used = new HashSet<string>();
            for (int i = 0; i < count && used.Count < candidates.Count; i++)
            {
                AffixDef a = Pick(rng, candidates.FindAll(c => !used.Contains(c.Id)));
                used.Add(a.Id);
                double raw = RollRange(rng, a.Min, a.Max) * rarityMul * (1 + (level - 1) * 0.03);
                int dp = a.Stat == "critChance" || a.Stat == "lootLuck" ? 3 : 2;
                double value = a.Stat == "maxRam" ? Math.Max(1, RpgJs.Round(raw)) : RpgJs.RoundTo(raw, dp);
                chosen.Add(new AffixRoll { Id = a.Id, Label = a.Label, Stat = a.Stat, Op = a.Op, Value = value });
            }
            return chosen;
        }

        static string BuildName(Func<double> rng, ItemTemplate t, string rarity, List<AffixRoll> affixes)
        {
            string baseName = Pick(rng, t.BaseNames);
            if (t.Type == "junk" || t.Type == "quest" || t.Type == "quickhack") return baseName;
            string corp = Pick(rng, Corps);
            int order = RarityOf(rarity).Order;
            if (order >= 2) return $"{corp} {baseName} \"{Pick(rng, Epithets)}\"";
            if (order == 1 && affixes.Count > 0) return $"{affixes[0].Label} {baseName}";
            return $"{corp} {baseName}";
        }

        /// <summary>One item from a template, drawing its stats, affixes and name from <paramref name="rng"/>.</summary>
        public static Item MakeItem(Func<double> rng, string templateId, string rarity = "common", double level = 1, string uid = null, double qty = 1)
        {
            ItemTemplate t = Template(templateId);
            if (!IsRarity(rarity)) throw new ArgumentOutOfRangeException(nameof(rarity), $"Unknown rarity \"{rarity}\"");
            if (string.IsNullOrEmpty(uid)) throw new ArgumentException("makeItem: uid is required");
            Rarity r = RarityOf(rarity);
            int lvl = (int)Math.Max(1, RpgJs.Round(level));
            double scale = LevelScale(lvl) * r.StatMul;

            var stats = new List<KeyValuePair<string, double>>();
            foreach ((string key, double min, double max) in t.StatRanges)
            {
                double raw = RollRange(rng, min, max);
                bool isFactor = key == "accuracy" || key == "handling";
                double v = isFactor ? RpgJs.RoundTo(Math.Min(0.99, raw * (1 + (r.StatMul - 1) * 0.5)), 3) : RpgJs.RoundTo(raw * scale, 1);
                stats.Add(new KeyValuePair<string, double>(key, v));
            }

            List<AffixRoll> affixes = RollAffixes(rng, t.AffixPool, r.Affixes, r.StatMul, lvl);
            var mods = affixes.ConvertAll(a => new StatModifier(a.Stat, a.Op, a.Value));
            string name = BuildName(rng, t, rarity, affixes);

            return new Item
            {
                Uid = uid, TemplateId = t.Id, Type = t.Type, Slot = t.Slot, Name = name, Rarity = rarity, Level = lvl, Stats = stats, Affixes = affixes, Mods = mods,
                Use = t.Use, Hack = t.Hack, Weight = RpgJs.RoundTo(t.Weight, 2),
                Value = (int)Math.Max(1, RpgJs.Round(t.Value * r.ValueMul * LevelScale(lvl))),
                Qty = (int)Math.Max(1, RpgJs.Round(qty)), MaxStack = t.MaxStack ?? 1, Desc = t.Desc,
            };
        }

        /// <summary>A piece of cyberware as a carried item (bought or looted, then installed).</summary>
        public static Item MakeCyberwareItem(string cyberwareId, string uid)
        {
            CyberwareDef ware = RpgCyberware.Get(cyberwareId);
            if (string.IsNullOrEmpty(uid)) throw new ArgumentException("makeCyberwareItem: uid is required");
            string rarity = ware.Tier >= 3 ? "epic" : ware.Tier == 2 ? "rare" : "uncommon";
            return new Item
            {
                Uid = uid, TemplateId = "cw:" + ware.Id, CyberwareId = ware.Id, Type = "cyberware", Slot = null, CyberSlot = ware.Slot, Name = $"{ware.Corp} {ware.Name}",
                Rarity = rarity, Level = ware.Requires.Level.HasValue && ware.Requires.Level.Value != 0 ? ware.Requires.Level.Value : 1,
                Stats = new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>("capacity", ware.Capacity), new KeyValuePair<string, double>("tier", ware.Tier) },
                Weight = ware.Weight, Value = ware.Value, Qty = 1, MaxStack = 1, Desc = ware.Desc,
            };
        }

        /// <summary>Highest rarity first, then the dearest, then by name.</summary>
        public static int Compare(Item a, Item b)
        {
            int ro = RarityOf(b.Rarity).Order - RarityOf(a.Rarity).Order;
            if (ro != 0) return ro;
            if (b.Value != a.Value) return b.Value - a.Value;
            return string.Compare(a.Name, b.Name, CultureInfo.InvariantCulture, CompareOptions.None);
        }
    }

    /// <summary>Options for one roll; unset fields are chosen by the roll.</summary>
    public struct LootRoll
    {
        public double Level, Luck, Tier;
        public string Type, TemplateId, Rarity;

        public static LootRoll Default => new LootRoll { Level = 1, Luck = 0, Tier = 1 };
    }

    /// <summary>The seeded loot roller. Same seed in, same items out, uids included (<c>&lt;prefix&gt;&lt;seed base 36&gt;_&lt;n&gt;</c> unless a uid source is given).</summary>
    public sealed class LootGenerator
    {
        public readonly uint Seed;
        Rng _rng;
        readonly string _uidPrefix;
        int _uidSeq;
        readonly Func<string> _nextUid;

        public LootGenerator(uint seed = 1337, string uidPrefix = "it", Func<string> nextUid = null)
        {
            Seed = seed;
            _rng = new Rng(seed);
            _uidPrefix = uidPrefix;
            _nextUid = nextUid;
        }

        /// <summary>The generator's stream, for rolls made outside it (vendor chrome).</summary>
        public double Next() => _rng.Next();

        public LootGenerator Reset()
        {
            _rng = new Rng(Seed);
            _uidSeq = 0;
            return this;
        }

        public string Uid()
        {
            if (_nextUid != null) return _nextUid();
            _uidSeq += 1;
            return $"{_uidPrefix}{RpgJs.Base36(Seed)}_{_uidSeq}";
        }

        public List<ItemTemplate> Candidates(string type, int level)
        {
            var list = new List<ItemTemplate>();
            foreach (ItemTemplate t in RpgItems.Templates) if (t.Type == type && (t.MinLevel ?? 1) <= level) list.Add(t);
            if (list.Count == 0) throw new ArgumentOutOfRangeException(nameof(type), $"No templates of type \"{type}\" available at level {level}");
            return list;
        }

        public Item RollItem(LootRoll o)
        {
            int lvl = (int)Math.Max(1, RpgJs.Round(o.Level));
            string chosenTemplate = o.TemplateId;
            if (chosenTemplate == null)
            {
                string chosenType = o.Type ?? RpgItems.WeightedPick(Next, RpgItems.DefaultTypeWeights);
                if (chosenType == "cyberware")
                {
                    var pool = new List<CyberwareDef>();
                    foreach (CyberwareDef c in RpgCyberware.All) if ((c.Requires.Level ?? 1) <= lvl + 2) pool.Add(c);
                    IReadOnlyList<CyberwareDef> from = pool.Count > 0 ? (IReadOnlyList<CyberwareDef>)pool : RpgCyberware.All;
                    CyberwareDef ware = from[Math.Min(from.Count - 1, (int)Math.Floor(Next() * from.Count))];
                    return RpgItems.MakeCyberwareItem(ware.Id, Uid());
                }
                List<ItemTemplate> candidates = Candidates(chosenType, lvl);
                chosenTemplate = candidates[Math.Min(candidates.Count - 1, (int)Math.Floor(Next() * candidates.Count))].Id;
            }
            ItemTemplate t = RpgItems.Template(chosenTemplate);
            bool isFixed = t.Type == "junk" || t.Type == "quest" || t.Type == "quickhack";
            string rarity = o.Rarity ?? (isFixed ? "common" : RpgItems.WeightedPick(Next, RpgItems.RarityWeights(lvl, o.Luck, o.Tier)));
            double qty = t.Type == "junk" ? 1 + Math.Floor(Next() * 4) : t.Type == "consumable" ? 1 + Math.Floor(Next() * 2) : 1;
            return RpgItems.MakeItem(Next, t.Id, rarity, lvl, Uid(), qty);
        }

        /// <summary>A whole drop: some items and some eddies.</summary>
        public (List<Item> Items, int Money) RollDrop(double level = 1, double luck = 0, double tier = 1, int? count = null, double moneyBase = 40)
        {
            int n = count ?? (int)(1 + Math.Floor(Next() * (2 + tier)));
            var items = new List<Item>();
            for (int i = 0; i < n; i++) items.Add(RollItem(new LootRoll { Level = level, Luck = luck, Tier = tier }));
            int money = (int)RpgJs.Round(moneyBase * (0.5 + Next()) * (1 + level * 0.22) * (1 + tier * 0.4));
            return (items, money);
        }
    }
}
