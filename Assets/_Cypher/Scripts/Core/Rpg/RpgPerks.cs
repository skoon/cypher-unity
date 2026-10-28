using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>What a perk or a piece of chrome needs: attribute minimums (in the order written), a level, ranks in other perks, street cred.</summary>
    public sealed class Requirements
    {
        public readonly IReadOnlyList<(string Attr, int Min)> Attribute;
        public readonly int? Level, StreetCred;
        public readonly IReadOnlyList<(string Id, int Rank)> Perks;

        public Requirements((string, int)[] attribute = null, int? level = null, (string, int)[] perks = null, int? streetCred = null)
        {
            Attribute = attribute ?? Array.Empty<(string, int)>();
            Level = level;
            Perks = perks ?? Array.Empty<(string, int)>();
            StreetCred = streetCred;
        }

        public static readonly Requirements None = new Requirements();

        /// <summary>As the prototype's table writes it: only the parts given.</summary>
        public JsonObject ToJson()
        {
            var o = new JsonObject();
            if (Attribute.Count > 0)
            {
                var a = new JsonObject();
                foreach ((string attr, int min) in Attribute) a[attr] = (double)min;
                o["attribute"] = a;
            }
            if (Level.HasValue) o["level"] = (double)Level.Value;
            if (Perks.Count > 0)
            {
                var p = new JsonObject();
                foreach ((string id, int rank) in Perks) p[id] = (double)rank;
                o["perks"] = p;
            }
            if (StreetCred.HasValue) o["streetCred"] = (double)StreetCred.Value;
            return o;
        }
    }

    /// <summary>One perk: a tree (an attribute), a tier, ranks, a cost per rank, requirements and the modifiers a rank gives.</summary>
    public sealed class Perk
    {
        public readonly string Id, Name, Tree, Desc;
        public readonly int Tier, MaxRank, Cost;
        public readonly Requirements Requires;
        public readonly Func<int, StatModifier[]> Mods;
        public readonly IReadOnlyList<string> Abilities;

        public Perk(string id, string name, string tree, int tier, int maxRank, int cost, string desc, Requirements requires, Func<int, StatModifier[]> mods, string[] abilities = null)
        {
            Id = id; Name = name; Tree = tree; Tier = tier; MaxRank = maxRank; Cost = cost; Desc = desc; Requires = requires; Mods = mods;
            Abilities = abilities ?? Array.Empty<string>();
        }

        /// <summary>The perk as the prototype's table serialises (its modifiers are a function, so they are not written).</summary>
        public JsonObject ToJson()
        {
            var o = new JsonObject
            {
                ["id"] = Id, ["name"] = Name, ["tree"] = Tree, ["tier"] = (double)Tier, ["maxRank"] = (double)MaxRank, ["cost"] = (double)Cost, ["desc"] = Desc,
                ["requires"] = Requires.ToJson(),
            };
            if (Abilities.Count > 0) o["abilities"] = new List<object>(Abilities);
            return o;
        }
    }

    /// <summary>What buying a perk reads and spends.</summary>
    public sealed class PerkBuyer
    {
        public IReadOnlyDictionary<string, int> Attributes;
        public int Level = 1;
        public int PerkPoints;
    }

    /// <summary>
    /// The perk trees (five, one per attribute, three tiers) and the owned ranks: a port of <c>rpg/Perks.js</c>. A locked perk is a result with the missing
    /// requirements; an unknown id throws.
    /// </summary>
    public static class RpgPerks
    {
        static StatModifier A(string stat, double v) => StatModifier.Add(stat, v);
        static StatModifier M(string stat, double v) => StatModifier.Mul(stat, v);
        static (string, int)[] Attr(params (string, int)[] a) => a;

        public static readonly IReadOnlyList<Perk> All = new[]
        {
            // body
            new Perk("bulk", "Bulk", "body", 1, 3, 1, "Thicker meat. +{n} max health.", new Requirements(Attr(("body", 4))), r => new[] { A("maxHealth", 25 * r) }),
            new Perk("pack_mule", "Pack Mule", "body", 1, 2, 1, "You have never left a room without taking something. +{n} carry capacity.",
                new Requirements(Attr(("body", 5))), r => new[] { A("carryCapacity", 22 * r) }),
            new Perk("iron_lung", "Iron Lung", "body", 2, 3, 1, "Rebuilt alveoli. +{n} stamina and faster recovery.",
                new Requirements(Attr(("body", 7)), perks: new[] { ("bulk", 2) }), r => new[] { A("maxStamina", 18 * r), A("staminaRegen", 1.2 * r) }),
            new Perk("adrenaline_rush", "Adrenaline Rush", "body", 2, 2, 1, "Pain is a suggestion. +{n} health regen.",
                new Requirements(Attr(("body", 8)), level: 6), r => new[] { A("healthRegen", 0.9 * r) }),
            new Perk("juggernaut", "Juggernaut", "body", 3, 1, 2, "+15% max health, +12 armour. Doors are a formality.",
                new Requirements(Attr(("body", 12)), level: 12, perks: new[] { ("iron_lung", 2) }), r => new[] { M("maxHealth", 0.15), A("armor", 12) }, new[] { "breach_doors" }),

            // reflexes
            new Perk("quickdraw", "Quickdraw", "reflexes", 1, 3, 1, "Hands ahead of thought. +{n}% ranged damage.",
                new Requirements(Attr(("reflexes", 4))), r => new[] { A("rangedDamage", 0.06 * r) }),
            new Perk("slippery", "Slippery", "reflexes", 1, 2, 1, "Rain never quite lands on you. +{n} stamina, +{n} stealth.",
                new Requirements(Attr(("reflexes", 5))), r => new[] { A("maxStamina", 12 * r), A("stealth", 4 * r) }),
            new Perk("cat_burglar", "Cat Burglar", "reflexes", 2, 1, 1, "Windows are doors if you are patient. +10 stealth.",
                new Requirements(Attr(("reflexes", 7), ("cool", 6)), perks: new[] { ("slippery", 1) }), r => new[] { A("stealth", 10) }, new[] { "silent_landing" }),
            new Perk("edge_of_death", "Edge of Death", "reflexes", 2, 3, 1, "You read the flinch before it happens. +{n}% crit chance.",
                new Requirements(Attr(("reflexes", 8)), perks: new[] { ("quickdraw", 2) }), r => new[] { A("critChance", 0.03 * r) }),
            new Perk("bullet_time", "Kerenzikov Sync", "reflexes", 3, 1, 2, "The world thickens on demand. +25% crit damage, unlocks reflex dilation.",
                new Requirements(Attr(("reflexes", 14)), level: 16, perks: new[] { ("edge_of_death", 2) }), r => new[] { A("critDamage", 0.25) }, new[] { "reflex_dilation" }),

            // intelligence
            new Perk("ram_reserves", "RAM Reserves", "intelligence", 1, 3, 1, "Scavenged memory sticks, taped in. +{n} RAM.",
                new Requirements(Attr(("intelligence", 4))), r => new[] { A("maxRam", r) }),
            new Perk("buffer_optimizer", "Buffer Optimiser", "intelligence", 1, 2, 1, "Longer breach buffer. +{n} slot.",
                new Requirements(Attr(("intelligence", 5))), r => new[] { A("breachBuffer", r) }),
            new Perk("signal_boost", "Signal Boost", "intelligence", 2, 2, 1, "Reach past the rain. +{n}m hack range, longer uploads stick.",
                new Requirements(Attr(("intelligence", 7))), r => new[] { A("hackRange", 6 * r), A("hackDuration", 0.15 * r) }),
            new Perk("daemon_hunter", "Daemon Hunter", "intelligence", 2, 2, 1, "You see the shape of a sequence before you walk it. +{n}s breach timer, +hack strength.",
                new Requirements(Attr(("intelligence", 8)), perks: new[] { ("buffer_optimizer", 1) }), r => new[] { A("breachTime", 4 * r), A("hackStrength", 6 * r) }),
            new Perk("overclock", "Overclock", "intelligence", 3, 1, 2, "+3 RAM, +50% RAM regen. Your nose bleeds. Worth it.",
                new Requirements(Attr(("intelligence", 15)), level: 18, perks: new[] { ("ram_reserves", 3), ("daemon_hunter", 1) }),
                r => new[] { A("maxRam", 3), M("ramRegen", 0.5) }, new[] { "overclock" }),

            // technical
            new Perk("field_tech", "Field Tech", "technical", 1, 3, 1, "Plate scavenged off a delivery drone. +{n} armour.",
                new Requirements(Attr(("technical", 4))), r => new[] { A("armor", 6 * r) }),
            new Perk("scavenger", "Scavenger", "technical", 1, 2, 1, "You know which corpses are worth the search. +{n} loot luck.",
                new Requirements(Attr(("technical", 5))), r => new[] { A("lootLuck", 0.12 * r) }),
            new Perk("mechanic", "Mechanic", "technical", 2, 2, 1, "You can price a part by the sound it makes. +craft quality, +{n}% discount.",
                new Requirements(Attr(("technical", 7))), r => new[] { A("craftQuality", 8 * r), A("vendorDiscount", 0.03 * r) }),
            new Perk("chip_surgeon", "Chip Surgeon", "technical", 2, 2, 1, "You install your own chrome in a bathroom mirror. +{n} cyber capacity.",
                new Requirements(Attr(("technical", 8)), perks: new[] { ("field_tech", 2) }), r => new[] { A("cyberCapacity", 3 * r) }),
            new Perk("ghost_wiring", "Ghost Wiring", "technical", 3, 1, 2, "Parallel bus down the spine. +6 cyber capacity, +2 RAM.",
                new Requirements(Attr(("technical", 14), ("intelligence", 8)), level: 15, perks: new[] { ("chip_surgeon", 2) }),
                r => new[] { A("cyberCapacity", 6), A("maxRam", 2) }, new[] { "self_install" }),

            // cool
            new Perk("cold_blood", "Cold Blood", "cool", 1, 3, 1, "Your pulse does not change when theirs stops. +{n}% crit damage.",
                new Requirements(Attr(("cool", 4))), r => new[] { A("critDamage", 0.1 * r) }),
            new Perk("haggler", "Haggler", "cool", 1, 2, 1, "You let the silence do the negotiating. +{n}% off, +{n}% resale.",
                new Requirements(Attr(("cool", 5))), r => new[] { A("vendorDiscount", 0.05 * r), A("sellBonus", 0.05 * r) }),
            new Perk("ghost", "Ghost", "cool", 2, 3, 1, "Cameras log a smear. +{n} stealth.",
                new Requirements(Attr(("cool", 8))), r => new[] { A("stealth", 8 * r) }),
            new Perk("street_face", "Street Face", "cool", 2, 2, 1, "Fixers return your calls. +{n}% resale, +{n}% XP.",
                new Requirements(Attr(("cool", 7)), perks: new[] { ("haggler", 1) }), r => new[] { A("sellBonus", 0.06 * r), A("xpRate", 0.04 * r) }),
            new Perk("legend", "Local Legend", "cool", 3, 1, 2, "They name a drink after you in three bars. +15% XP, +5% crit, +loot luck.",
                new Requirements(Attr(("cool", 15)), level: 20, perks: new[] { ("street_face", 2), ("ghost", 2) }),
                r => new[] { M("xpRate", 0.15), A("critChance", 0.05), A("lootLuck", 0.25) }, new[] { "fixer_network" }),
        };

        static readonly Dictionary<string, Perk> ById = new Dictionary<string, Perk>();

        static RpgPerks()
        {
            foreach (Perk p in All)
            {
                if (ById.ContainsKey(p.Id)) throw new InvalidOperationException($"Duplicate perk id \"{p.Id}\"");
                ById.Add(p.Id, p);
            }
            foreach (Perk p in All)
            {
                if (!RpgStats.IsAttribute(p.Tree)) throw new InvalidOperationException($"Perk \"{p.Id}\" has unknown tree \"{p.Tree}\"");
                foreach ((string attr, int _) in p.Requires.Attribute)
                {
                    if (!RpgStats.IsAttribute(attr)) throw new InvalidOperationException($"Perk \"{p.Id}\" requires unknown attribute \"{attr}\"");
                }
                foreach ((string dep, int rank) in p.Requires.Perks)
                {
                    if (!ById.TryGetValue(dep, out Perk parent)) throw new InvalidOperationException($"Perk \"{p.Id}\" requires unknown perk \"{dep}\"");
                    if (rank > parent.MaxRank) throw new InvalidOperationException($"Perk \"{p.Id}\" requires rank {rank} of \"{dep}\" (max {parent.MaxRank})");
                }
            }
        }

        public static Perk Get(string id)
        {
            if (id == null || !ById.TryGetValue(id, out Perk p)) throw new ArgumentOutOfRangeException(nameof(id), $"Unknown perk id \"{id}\"");
            return p;
        }

        public static bool Exists(string id) => id != null && ById.ContainsKey(id);

        /// <summary>The trees in attribute order, each sorted by tier (stable, as the prototype's sort).</summary>
        public static IReadOnlyList<(string Tree, string Label, IReadOnlyList<Perk> Perks)> Trees()
        {
            var out_ = new List<(string, string, IReadOnlyList<Perk>)>();
            foreach (string tree in RpgStats.Attributes)
            {
                var list = new List<Perk>();
                foreach (Perk p in All) if (p.Tree == tree) list.Add(p);
                var sorted = new List<Perk>(System.Linq.Enumerable.OrderBy(list, p => p.Tier));
                out_.Add((tree, RpgStats.AttributeMeta[tree].Label, sorted));
            }
            return out_;
        }

        /// <summary>Requirement lines for a tooltip, e.g. <c>BODY 12</c>, <c>LEVEL 12</c>, <c>Iron Lung 2</c>.</summary>
        public static List<string> DescribeRequires(Perk perk)
        {
            var out_ = new List<string>();
            foreach ((string attr, int min) in perk.Requires.Attribute) out_.Add($"{RpgStats.AttributeLabelUpper(attr)} {min}");
            if (perk.Requires.Level.HasValue && perk.Requires.Level.Value != 0) out_.Add($"LEVEL {perk.Requires.Level}");
            foreach ((string id, int rank) in perk.Requires.Perks) out_.Add($"{Get(id).Name} {rank}");
            return out_;
        }
    }

    /// <summary>The ranks owned, with the prerequisites enforced. Keeps the insertion order of a JavaScript <c>Map</c>.</summary>
    public sealed class PerkTree
    {
        readonly List<string> _order = new List<string>();
        readonly Dictionary<string, int> _ranks = new Dictionary<string, int>();

        public PerkTree(IEnumerable<KeyValuePair<string, int>> ranks = null)
        {
            if (ranks == null) return;
            foreach (KeyValuePair<string, int> kv in ranks)
            {
                Perk perk = RpgPerks.Get(kv.Key);
                if (kv.Value < 1 || kv.Value > perk.MaxRank) throw new ArgumentOutOfRangeException(nameof(ranks), $"PerkTree: bad rank {kv.Value} for \"{kv.Key}\"");
                SetRank(kv.Key, kv.Value);
            }
        }

        void SetRank(string id, int rank)
        {
            if (!_ranks.ContainsKey(id)) _order.Add(id);
            _ranks[id] = rank;
        }

        public int RankOf(string id)
        {
            RpgPerks.Get(id);
            return _ranks.TryGetValue(id, out int r) ? r : 0;
        }

        public bool Has(string id, int minRank = 1) => RankOf(id) >= minRank;

        /// <summary>The owned ranks, in the order they were first bought.</summary>
        public IReadOnlyList<KeyValuePair<string, int>> Owned()
        {
            var out_ = new List<KeyValuePair<string, int>>();
            foreach (string id in _order) out_.Add(new KeyValuePair<string, int>(id, _ranks[id]));
            return out_;
        }

        public RpgResult CanBuy(string id, PerkBuyer stats)
        {
            Perk perk = RpgPerks.Get(id);
            int rank = RankOf(id);
            if (rank >= perk.MaxRank) return new RpgResult { Ok = false, Reason = "max-rank", Missing = new List<string>() };

            var missing = new List<string>();
            foreach ((string attr, int min) in perk.Requires.Attribute)
            {
                int have = stats.Attributes != null && stats.Attributes.TryGetValue(attr, out int v) ? v : 0;
                if (have < min) missing.Add($"{RpgStats.AttributeLabelUpper(attr)} {min}");
            }
            if (perk.Requires.Level.HasValue && perk.Requires.Level.Value != 0 && stats.Level < perk.Requires.Level.Value) missing.Add($"LEVEL {perk.Requires.Level}");
            foreach ((string dep, int needRank) in perk.Requires.Perks)
            {
                if (RankOf(dep) < needRank) missing.Add($"{RpgPerks.Get(dep).Name} {needRank}");
            }
            if (missing.Count > 0) return new RpgResult { Ok = false, Reason = "requirements", Missing = missing };
            if (stats.PerkPoints < perk.Cost) return new RpgResult { Ok = false, Reason = "no-points", Missing = new List<string>(), Cost = perk.Cost };
            return new RpgResult { Ok = true, Cost = perk.Cost };
        }

        /// <summary>Buy one rank; spends <see cref="PerkBuyer.PerkPoints"/> on success.</summary>
        public RpgResult Buy(string id, PerkBuyer stats)
        {
            RpgResult check = CanBuy(id, stats);
            if (!check.Ok) return check;
            Perk perk = RpgPerks.Get(id);
            int rank = RankOf(id) + 1;
            SetRank(id, rank);
            stats.PerkPoints -= perk.Cost;
            return new RpgResult { Ok = true, Rank = rank, Perk = perk };
        }

        /// <summary>Refund every rank of every perk; returns the points handed back.</summary>
        public int Respec(PerkBuyer stats)
        {
            int refund = 0;
            foreach (string id in _order) refund += RpgPerks.Get(id).Cost * _ranks[id];
            _ranks.Clear();
            _order.Clear();
            stats.PerkPoints += refund;
            return refund;
        }

        /// <summary>Put every owned perk's modifiers in the set, keyed <c>perk:&lt;id&gt;</c>, replacing the old ones.</summary>
        public ModifierSet ApplyTo(ModifierSet set)
        {
            foreach (string source in set.Sources())
            {
                if (source.StartsWith("perk:", StringComparison.Ordinal)) set.Remove(source);
            }
            foreach (string id in _order) set.Set("perk:" + id, RpgPerks.Get(id).Mods(_ranks[id]));
            return set;
        }

        public List<string> Abilities()
        {
            var out_ = new List<string>();
            foreach (string id in _order)
            {
                foreach (string a in RpgPerks.Get(id).Abilities) if (!out_.Contains(a)) out_.Add(a);
            }
            return out_;
        }

        public JsonObject Serialize()
        {
            var o = new JsonObject();
            foreach (string id in _order) o[id] = (double)_ranks[id];
            return o;
        }

        public static PerkTree Deserialize(JsonObject data)
        {
            if (data == null) return new PerkTree();
            var ranks = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, object> kv in data) ranks.Add(new KeyValuePair<string, int>(kv.Key, RpgJs.Int(kv.Value, $"perk rank {kv.Key}")));
            return new PerkTree(ranks);
        }
    }
}
