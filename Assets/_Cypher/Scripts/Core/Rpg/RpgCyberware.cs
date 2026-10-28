using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    public sealed class CyberwareSlot
    {
        public readonly string Key, Label, Blurb;
        public readonly int Capacity;

        public CyberwareSlot(string key, string label, int capacity, string blurb)
        {
            Key = key; Label = label; Capacity = capacity; Blurb = blurb;
        }
    }

    public sealed class CyberwareDef
    {
        public readonly string Id, Name, Corp, Slot, Desc;
        public readonly int Tier, Capacity, Value;
        public readonly double Weight;
        public readonly Requirements Requires;
        public readonly IReadOnlyList<StatModifier> Mods;
        public readonly IReadOnlyList<string> Abilities;

        public CyberwareDef(string id, string name, string corp, string slot, int tier, int capacity, int value, double weight, Requirements requires,
            StatModifier[] mods, string[] abilities, string desc)
        {
            Id = id; Name = name; Corp = corp; Slot = slot; Tier = tier; Capacity = capacity; Value = value; Weight = weight; Requires = requires;
            Mods = mods; Abilities = abilities ?? Array.Empty<string>(); Desc = desc;
        }

        public JsonObject ToJson()
        {
            var mods = new List<object>();
            foreach (StatModifier m in Mods) mods.Add(m.ToJson());
            var o = new JsonObject
            {
                ["id"] = Id, ["name"] = Name, ["corp"] = Corp, ["slot"] = Slot, ["tier"] = (double)Tier, ["capacity"] = (double)Capacity, ["value"] = (double)Value,
                ["weight"] = Weight, ["requires"] = Requires.ToJson(), ["mods"] = mods,
            };
            if (Abilities.Count > 0) o["abilities"] = new List<object>(Abilities);
            o["desc"] = Desc;
            return o;
        }
    }

    /// <summary>What installing chrome reads.</summary>
    public sealed class CyberStats
    {
        public IReadOnlyDictionary<string, int> Attributes;
        public int Level = 1, StreetCred = 1, CyberCapacity;
    }

    /// <summary>
    /// Cyberware across five body systems, limited twice: slots per system and a global capacity budget (<c>cyberCapacity</c>). A port of
    /// <c>rpg/Cyberware.js</c>; chrome may not modify <c>cyberCapacity</c> itself (a feedback loop), which the table check refuses.
    /// </summary>
    public static class RpgCyberware
    {
        public static readonly IReadOnlyList<CyberwareSlot> Slots = new[]
        {
            new CyberwareSlot("ocular", "Ocular", 2, "Eyes, optics, overlays."),
            new CyberwareSlot("nervous", "Nervous System", 2, "Spine, cortex, reflex arcs."),
            new CyberwareSlot("arms", "Arms", 1, "One pair. Choose carefully."),
            new CyberwareSlot("skeleton", "Skeleton", 3, "Frame, plating, joints."),
            new CyberwareSlot("circulatory", "Circulatory", 3, "Blood, pumps, filters."),
        };

        static StatModifier A(string stat, double v) => StatModifier.Add(stat, v);
        static StatModifier M(string stat, double v) => StatModifier.Mul(stat, v);
        static (string, int)[] Attr(params (string, int)[] a) => a;

        public static readonly IReadOnlyList<CyberwareDef> All = new[]
        {
            new CyberwareDef("cw_kiroshi_mk1", "Kiroshi Mk.1 Optics", "Kiroshi", "ocular", 1, 2, 1400, 0.4, Requirements.None,
                new[] { A("hackRange", 4), A("critChance", 0.01) }, new[] { "scanner" },
                "Basic overlay optics. Tags hostiles, reads barcodes, tells you the noodles are 40% soy."),
            new CyberwareDef("cw_kiroshi_mk3", "Kiroshi Mk.3 \"Threadcount\"", "Kiroshi", "ocular", 3, 6, 12800, 0.5, new Requirements(Attr(("intelligence", 9)), level: 10),
                new[] { A("hackRange", 12), A("hackStrength", 8), A("critChance", 0.04) }, new[] { "scanner", "threat_analysis" },
                "Sees the RAM in a man before it sees the man. Runs hot behind the eyes."),
            new CyberwareDef("cw_nightvision", "Owl-Eye Photoamp", "Rostović", "ocular", 2, 3, 4200, 0.3, new Requirements(Attr(("cool", 6))),
                new[] { A("stealth", 9), A("critChance", 0.02) }, new[] { "lowlight" },
                "Turns a dark alley into a grey one. Colour is a luxury anyway."),

            new CyberwareDef("cw_kerenzikov", "Kerenzikov Booster", "Dynalar", "nervous", 2, 5, 8600, 0.6, new Requirements(Attr(("reflexes", 9)), level: 8),
                new[] { A("critChance", 0.05), A("staminaRegen", 2.5), A("maxHealth", -15) }, new[] { "reflex_dilation" },
                "Time thins out when you flinch. Costs you fifteen points of meat to run."),
            new CyberwareDef("cw_cortex_dampener", "Kang Tao Cortex Dampener", "Kang Tao", "nervous", 1, 2, 2100, 0.2, Requirements.None,
                new[] { A("stealth", 6), A("critDamage", 0.08) }, null,
                "Flattens the spike when the adrenaline hits. You stop shaking. You stop a lot of things."),
            new CyberwareDef("cw_netdriver", "Netdriver Mk.4", "Zetatech", "nervous", 3, 7, 16400, 0.7,
                new Requirements(Attr(("intelligence", 12), ("technical", 6)), level: 14, streetCred: 4),
                new[] { A("maxRam", 4), A("ramRegen", 0.4), A("breachBuffer", 1), A("hackStrength", 14) }, new[] { "remote_breach" },
                "A second spine made of copper and bad ideas. You will dream in hex."),

            new CyberwareDef("cw_gorilla_arms", "Gorilla Arms", "Arasaka", "arms", 2, 5, 9400, 4.5, new Requirements(Attr(("body", 10)), level: 8),
                new[] { M("meleeDamage", 0.45), A("carryCapacity", 25), A("stealth", -8) }, new[] { "pry_doors" },
                "Hydraulic forearms. You will never open a door gently again."),
            new CyberwareDef("cw_mantis_blades", "Mantis Blades", "Arasaka", "arms", 3, 6, 15200, 3.2, new Requirements(Attr(("reflexes", 11)), level: 12, streetCred: 3),
                new[] { M("meleeDamage", 0.7), A("critChance", 0.06), A("maxHealth", -10) }, new[] { "leap_strike" },
                "Monofilament in the forearm. Quiet on the way out. Never on the way in."),
            new CyberwareDef("cw_monowire", "Monowire \"Silktether\"", "Tsunami", "arms", 3, 6, 14800, 1.1,
                new Requirements(Attr(("technical", 9), ("intelligence", 8)), level: 12),
                new[] { M("meleeDamage", 0.3), A("hackStrength", 10), A("maxRam", 2) }, new[] { "wire_upload" },
                "A whip one molecule thick that also happens to be a data cable."),

            new CyberwareDef("cw_subdermal", "Subdermal Armour", "Militech", "skeleton", 1, 3, 3200, 2.4, new Requirements(Attr(("body", 6))),
                new[] { A("armor", 18), A("stealth", -3) }, null,
                "Woven plate under the skin. Reads as a bad rash on a cheap scanner."),
            new CyberwareDef("cw_titanium_bones", "Titanium Bones", "Militech", "skeleton", 2, 4, 7600, 6.0, new Requirements(Attr(("body", 9)), level: 6),
                new[] { A("maxHealth", 45), A("carryCapacity", 35), A("staminaRegen", -1.5) }, null,
                "Heavier than you were. Harder than you were. Slower to catch your breath."),
            new CyberwareDef("cw_reinforced_tendons", "Reinforced Tendons", "Dynalar", "skeleton", 2, 3, 5400, 1.2, new Requirements(Attr(("reflexes", 8))),
                new[] { A("maxStamina", 30), A("staminaRegen", 2) }, new[] { "double_jump" },
                "Sprung ankles. Rooftops become a shortcut instead of a hazard."),
            new CyberwareDef("cw_dense_marrow", "Dense Marrow", "Biotechnica", "skeleton", 3, 5, 11200, 3.8, new Requirements(Attr(("body", 13)), level: 14),
                new[] { M("maxHealth", 0.18), A("meleeDamage", 0.35), A("maxStamina", -20) }, null,
                "Your bones make their own blood now. It is a lot of blood."),

            new CyberwareDef("cw_blood_pump", "Second Heart", "Biotechnica", "circulatory", 3, 6, 18000, 1.4,
                new Requirements(Attr(("body", 11), ("technical", 7)), level: 15, streetCred: 5),
                new[] { A("maxHealth", 60), A("healthRegen", 2.4) }, new[] { "revive" },
                "Beats once for every three of yours. Restarts you exactly once per day."),
            new CyberwareDef("cw_bioconductor", "Bioconductor", "Zetatech", "circulatory", 2, 3, 5800, 0.6, new Requirements(Attr(("technical", 6))),
                new[] { A("ramRegen", 0.5), A("healthRegen", 0.8) }, null,
                "Regulates the current so your chrome stops browning out mid-fight."),
            new CyberwareDef("cw_detox", "Detoxifier", "Biotechnica", "circulatory", 1, 2, 1900, 0.4, Requirements.None,
                new[] { A("healthRegen", 1.1), A("maxStamina", 14) }, null,
                "Scrubs the cheap stimulants out before they take your kidneys with them."),
            new CyberwareDef("cw_synaptic_accel", "Synaptic Accelerator", "Dynalar", "circulatory", 3, 5, 13400, 0.5,
                new Requirements(Attr(("reflexes", 12), ("cool", 8)), level: 13),
                new[] { A("critChance", 0.07), A("critDamage", 0.15), A("healthRegen", -0.6) }, new[] { "threat_sense" },
                "Floods the synapse when someone decides to kill you. Before they move."),
        };

        static readonly Dictionary<string, CyberwareDef> ById = new Dictionary<string, CyberwareDef>();
        static readonly Dictionary<string, CyberwareSlot> SlotByKey = new Dictionary<string, CyberwareSlot>();

        static RpgCyberware()
        {
            foreach (CyberwareSlot s in Slots) SlotByKey.Add(s.Key, s);
            foreach (CyberwareDef c in All)
            {
                if (ById.ContainsKey(c.Id)) throw new InvalidOperationException($"Duplicate cyberware id \"{c.Id}\"");
                ById.Add(c.Id, c);
                if (!SlotByKey.ContainsKey(c.Slot)) throw new InvalidOperationException($"Cyberware \"{c.Id}\" has unknown slot \"{c.Slot}\"");
                if (c.Capacity <= 0) throw new InvalidOperationException($"Cyberware \"{c.Id}\" needs a positive capacity cost");
                foreach ((string attr, int _) in c.Requires.Attribute)
                {
                    if (!RpgStats.IsAttribute(attr)) throw new InvalidOperationException($"Cyberware \"{c.Id}\" requires unknown attribute \"{attr}\"");
                }
                foreach (StatModifier m in c.Mods)
                {
                    if (m.Stat == "cyberCapacity") throw new InvalidOperationException($"Cyberware \"{c.Id}\" may not modify cyberCapacity (feedback loop)");
                }
            }
        }

        public static CyberwareDef Get(string id)
        {
            if (id == null || !ById.TryGetValue(id, out CyberwareDef c)) throw new ArgumentOutOfRangeException(nameof(id), $"Unknown cyberware id \"{id}\"");
            return c;
        }

        public static CyberwareSlot Slot(string key)
        {
            if (key == null || !SlotByKey.TryGetValue(key, out CyberwareSlot s)) throw new ArgumentOutOfRangeException(nameof(key), $"Unknown cyberware slot \"{key}\"");
            return s;
        }

        public static List<string> DescribeRequires(CyberwareDef ware)
        {
            var out_ = new List<string>();
            foreach ((string attr, int min) in ware.Requires.Attribute) out_.Add($"{RpgStats.AttributeLabelUpper(attr)} {min}");
            if (ware.Requires.Level.HasValue && ware.Requires.Level.Value != 0) out_.Add($"LEVEL {ware.Requires.Level}");
            if (ware.Requires.StreetCred.HasValue && ware.Requires.StreetCred.Value != 0) out_.Add($"STREET CRED {ware.Requires.StreetCred}");
            return out_;
        }
    }

    /// <summary>The installed rig, in install order: per-slot counts and the capacity budget enforced.</summary>
    public sealed class CyberwareRig
    {
        readonly List<string> _installed = new List<string>();

        public CyberwareRig(IEnumerable<string> installed = null)
        {
            if (installed == null) return;
            foreach (string id in installed)
            {
                RpgCyberware.Get(id);
                if (_installed.Contains(id)) throw new InvalidOperationException($"Cyberware \"{id}\" installed twice");
                _installed.Add(id);
            }
        }

        public IReadOnlyList<string> Installed => _installed;

        public List<CyberwareDef> List() => _installed.ConvertAll(RpgCyberware.Get);

        public bool Has(string id)
        {
            RpgCyberware.Get(id);
            return _installed.Contains(id);
        }

        /// <summary>Slot → installed ids, every slot present.</summary>
        public JsonObject BySlot()
        {
            var o = new JsonObject();
            foreach (CyberwareSlot s in RpgCyberware.Slots) o[s.Key] = new List<object>();
            foreach (string id in _installed) ((List<object>)o[RpgCyberware.Get(id).Slot]).Add(id);
            return o;
        }

        public int UsedCapacity()
        {
            int sum = 0;
            foreach (string id in _installed) sum += RpgCyberware.Get(id).Capacity;
            return sum;
        }

        public int SlotUsage(string slot)
        {
            RpgCyberware.Slot(slot);
            int n = 0;
            foreach (string id in _installed) if (RpgCyberware.Get(id).Slot == slot) n++;
            return n;
        }

        public RpgResult CanInstall(string id, CyberStats stats)
        {
            CyberwareDef ware = RpgCyberware.Get(id);
            if (Has(id)) return RpgResult.Fail("already-installed");

            var missing = new List<string>();
            foreach ((string attr, int min) in ware.Requires.Attribute)
            {
                int have = stats.Attributes != null && stats.Attributes.TryGetValue(attr, out int v) ? v : 0;
                if (have < min) missing.Add($"{RpgStats.AttributeLabelUpper(attr)} {min}");
            }
            if (ware.Requires.Level.HasValue && ware.Requires.Level.Value != 0 && stats.Level < ware.Requires.Level.Value) missing.Add($"LEVEL {ware.Requires.Level}");
            if (ware.Requires.StreetCred.HasValue && ware.Requires.StreetCred.Value != 0 && stats.StreetCred < ware.Requires.StreetCred.Value)
            {
                missing.Add($"STREET CRED {ware.Requires.StreetCred}");
            }
            if (missing.Count > 0) return new RpgResult { Ok = false, Reason = "requirements", Missing = missing };

            if (SlotUsage(ware.Slot) >= RpgCyberware.Slot(ware.Slot).Capacity) return new RpgResult { Ok = false, Reason = "slot-full", Free = 0 };
            int free = stats.CyberCapacity - UsedCapacity();
            if (free < ware.Capacity) return new RpgResult { Ok = false, Reason = "no-capacity", Need = ware.Capacity, Free = free };
            return new RpgResult { Ok = true, Need = ware.Capacity, Free = free };
        }

        public RpgResult Install(string id, CyberStats stats)
        {
            RpgResult check = CanInstall(id, stats);
            if (!check.Ok) return check;
            _installed.Add(id);
            return new RpgResult { Ok = true, Ware = RpgCyberware.Get(id) };
        }

        public RpgResult Remove(string id)
        {
            CyberwareDef ware = RpgCyberware.Get(id);
            if (!_installed.Remove(id)) return RpgResult.Fail("not-installed");
            return new RpgResult { Ok = true, Ware = ware };
        }

        /// <summary>Put every installed piece's modifiers in the set, keyed <c>cyber:&lt;id&gt;</c>, replacing the old ones.</summary>
        public ModifierSet ApplyTo(ModifierSet set)
        {
            foreach (string source in set.Sources())
            {
                if (source.StartsWith("cyber:", StringComparison.Ordinal)) set.Remove(source);
            }
            foreach (string id in _installed) set.Set("cyber:" + id, RpgCyberware.Get(id).Mods);
            return set;
        }

        public List<string> Abilities()
        {
            var out_ = new List<string>();
            foreach (string id in _installed)
            {
                foreach (string a in RpgCyberware.Get(id).Abilities) if (!out_.Contains(a)) out_.Add(a);
            }
            return out_;
        }

        public List<object> Serialize() => new List<object>(_installed);

        public static CyberwareRig Deserialize(List<object> data)
        {
            var ids = new List<string>();
            if (data != null)
            {
                foreach (object o in data) ids.Add(o as string ?? throw new ArgumentException("a cyberware id must be a string"));
            }
            return new CyberwareRig(ids);
        }
    }
}
