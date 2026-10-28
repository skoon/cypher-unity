using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>A derived stat's label and how it is clamped and rounded.</summary>
    public sealed class DerivedMeta
    {
        public readonly string Key, Label;
        public readonly bool Int;
        public readonly double? Min, Max;

        public DerivedMeta(string key, string label, bool isInt = false, double? min = null, double? max = null)
        {
            Key = key; Label = label; Int = isInt; Min = min; Max = max;
        }
    }

    /// <summary>One modifier on a derived stat: <c>value = (base + Σadd) × (1 + Σmul)</c>. <see cref="Source"/> is set only when it is listed from a <see cref="ModifierSet"/>.</summary>
    public readonly struct StatModifier
    {
        public readonly string Stat, Op;
        public readonly double Value;
        public readonly string Source;

        public StatModifier(string stat, string op, double value, string source = null)
        {
            Stat = stat; Op = op; Value = value; Source = source;
        }

        public static StatModifier Add(string stat, double value) => new StatModifier(stat, "add", value);

        public static StatModifier Mul(string stat, double value) => new StatModifier(stat, "mul", value);

        public JsonObject ToJson()
        {
            var o = new JsonObject { ["stat"] = Stat, ["op"] = Op, ["value"] = Value };
            if (Source != null) o["source"] = Source;
            return o;
        }

        public static StatModifier FromJson(object v)
        {
            if (!(v is JsonObject o)) throw new ArgumentException("a modifier must be an object");
            return new StatModifier(o.TryGetValue("stat", out object s) ? s as string : null, o.TryGetValue("op", out object op) ? op as string : null,
                o.TryGetValue("value", out object val) && val is double d ? d : double.NaN);
        }
    }

    /// <summary>
    /// Attributes, derived stats, the XP curves and the modifier pipeline: a port of the prototype's <c>rpg/Stats.js</c>. Derived stats are
    /// <c>(base + Σadd) × (1 + Σmul)</c>, clamped, and rounded (to a whole number for integer stats, to 1/1000 otherwise).
    /// </summary>
    public static class RpgStats
    {
        public static readonly IReadOnlyList<string> Attributes = new[] { "body", "reflexes", "intelligence", "technical", "cool" };

        public static readonly IReadOnlyDictionary<string, (string Label, string Blurb)> AttributeMeta = new Dictionary<string, (string, string)>
        {
            ["body"] = ("Body", "Meat and bone. Health, carry weight, melee."),
            ["reflexes"] = ("Reflexes", "Twitch speed. Crit chance, stamina, gunplay."),
            ["intelligence"] = ("Intelligence", "Net presence. RAM, hack strength, breach buffer."),
            ["technical"] = ("Technical", "Hands and schematics. Armour, crafting, cyber-capacity."),
            ["cool"] = ("Cool", "Composure. Crit damage, stealth, how hard vendors squeeze you."),
        };

        public const int AttrMin = 3, AttrMax = 20, LevelCap = 50, CredCap = 20;

        public static readonly IReadOnlyList<DerivedMeta> Derived = new[]
        {
            new DerivedMeta("maxHealth", "Health", true, 1),
            new DerivedMeta("healthRegen", "Health Regen/s", min: 0),
            new DerivedMeta("maxStamina", "Stamina", true, 1),
            new DerivedMeta("staminaRegen", "Stamina Regen/s", min: 0),
            new DerivedMeta("carryCapacity", "Carry Capacity", true, 1),
            new DerivedMeta("critChance", "Crit Chance", min: 0, max: 0.85),
            new DerivedMeta("critDamage", "Crit Damage", min: 1),
            new DerivedMeta("meleeDamage", "Melee Damage", min: 0.1),
            new DerivedMeta("rangedDamage", "Ranged Damage", min: 0.1),
            new DerivedMeta("armor", "Armour", min: 0),
            new DerivedMeta("maxRam", "RAM", true, 1, 32),
            new DerivedMeta("ramRegen", "RAM Regen/s", min: 0),
            new DerivedMeta("hackStrength", "Hack Strength", min: 0),
            new DerivedMeta("hackDuration", "Hack Duration", min: 0.25),
            new DerivedMeta("hackRange", "Hack Range", min: 1),
            new DerivedMeta("breachBuffer", "Breach Buffer", true, 4, 12),
            new DerivedMeta("breachTime", "Breach Timer", min: 4),
            new DerivedMeta("stealth", "Stealth", min: 0),
            new DerivedMeta("vendorDiscount", "Vendor Discount", min: 0, max: 0.45),
            new DerivedMeta("sellBonus", "Sell Bonus", min: 0, max: 0.5),
            new DerivedMeta("craftQuality", "Craft Quality", min: 0),
            new DerivedMeta("cyberCapacity", "Cyber Capacity", true, 0),
            new DerivedMeta("xpRate", "XP Rate", min: 0.1),
            new DerivedMeta("lootLuck", "Loot Luck", min: 0),
        };

        static readonly Dictionary<string, DerivedMeta> MetaByKey = new Dictionary<string, DerivedMeta>();

        static RpgStats()
        {
            foreach (DerivedMeta m in Derived) MetaByKey.Add(m.Key, m);
        }

        public static bool IsDerived(string key) => key != null && MetaByKey.ContainsKey(key);

        public static bool IsAttribute(string key) => key != null && AttributeMeta.ContainsKey(key);

        public static string AttributeLabelUpper(string attr) => AttributeMeta[attr].Label.ToUpperInvariant();

        /// <summary>XP to go from <paramref name="level"/> to the next; infinity at the cap.</summary>
        public static double XpToNext(int level)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level), level, "xpToNext: level must be an integer >= 1");
            if (level >= LevelCap) return double.PositiveInfinity;
            return RpgJs.Round(180 + 95 * Math.Pow(level, 1.42));
        }

        /// <summary>Cumulative XP needed to reach <paramref name="level"/> from level 1.</summary>
        public static double TotalXpForLevel(int level)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level), level, "totalXpForLevel: level must be an integer >= 1");
            double sum = 0;
            for (int i = 1; i < Math.Min(level, LevelCap); i++) sum += XpToNext(i);
            return sum;
        }

        public static (int Level, double Into, double ToNext) LevelFromXp(double xp)
        {
            if (double.IsNaN(xp) || double.IsInfinity(xp) || xp < 0) throw new ArgumentOutOfRangeException(nameof(xp), xp, "levelFromXp: bad xp");
            int level = 1;
            double rest = xp;
            while (level < LevelCap)
            {
                double need = XpToNext(level);
                if (rest < need) break;
                rest -= need;
                level++;
            }
            return (level, rest, XpToNext(level));
        }

        public static double CredToNext(int credLevel)
        {
            if (credLevel < 1) throw new ArgumentOutOfRangeException(nameof(credLevel), credLevel, "credToNext: bad cred level");
            if (credLevel >= CredCap) return double.PositiveInfinity;
            return RpgJs.Round(120 + 70 * Math.Pow(credLevel, 1.3));
        }

        public static (int Level, double Into, double ToNext) CredFromXp(double xp)
        {
            if (double.IsNaN(xp) || double.IsInfinity(xp) || xp < 0) throw new ArgumentOutOfRangeException(nameof(xp), xp, "credFromXp: bad xp");
            int level = 1;
            double rest = xp;
            while (level < CredCap)
            {
                double need = CredToNext(level);
                if (rest < need) break;
                rest -= need;
                level++;
            }
            return (level, rest, CredToNext(level));
        }

        /// <summary>Points awarded on reaching <paramref name="level"/>.</summary>
        public static (int AttributePoints, int PerkPoints) LevelUpAwards(int level) =>
            (1 + (level % 3 == 0 ? 1 : 0), 1 + (level % 5 == 0 ? 1 : 0));

        /// <summary>The derived stats from attributes and level alone, before any modifier.</summary>
        public static DerivedStats BaseDerived(IReadOnlyDictionary<string, int> attr, int level = 1)
        {
            foreach (string key in Attributes)
            {
                if (attr == null || !attr.ContainsKey(key)) throw new ArgumentException($"baseDerived: missing attribute \"{key}\"");
            }
            double body = attr["body"], reflexes = attr["reflexes"], intelligence = attr["intelligence"], technical = attr["technical"], cool = attr["cool"];
            int intel = attr["intelligence"], tech = attr["technical"];
            var d = new DerivedStats();
            d["maxHealth"] = 100 + body * 12 + level * 4;
            d["healthRegen"] = 0.8 + body * 0.12;
            d["maxStamina"] = 70 + body * 3 + reflexes * 5;
            d["staminaRegen"] = 8 + reflexes * 0.7;
            d["carryCapacity"] = 45 + body * 7;
            d["critChance"] = 0.05 + reflexes * 0.012 + cool * 0.006;
            d["critDamage"] = 1.5 + cool * 0.035;
            d["meleeDamage"] = 1 + body * 0.06;
            d["rangedDamage"] = 1 + reflexes * 0.05;
            d["armor"] = 4 + technical * 2.2;
            d["maxRam"] = 4 + Math.Floor(intel / 2.0);
            d["ramRegen"] = 0.35 + intelligence * 0.03;
            d["hackStrength"] = intelligence * 1.6 + technical * 0.6;
            d["hackDuration"] = 1 + intelligence * 0.02;
            d["hackRange"] = 12 + intelligence * 0.8;
            d["breachBuffer"] = 4 + Math.Floor(intel / 4.0) + Math.Floor(tech / 6.0);
            d["breachTime"] = 12 + intelligence * 0.6;
            d["stealth"] = cool * 2.2;
            d["vendorDiscount"] = cool * 0.012 + technical * 0.008;
            d["sellBonus"] = cool * 0.015 + technical * 0.005;
            d["craftQuality"] = technical * 1.4;
            d["cyberCapacity"] = 6 + Math.Floor(level / 2.0) + technical;
            d["xpRate"] = 1;
            d["lootLuck"] = 0;
            return d;
        }

        /// <summary>Throws on an unknown stat or op or a non-finite value: a typo in a perk table is a bug, not a no-op.</summary>
        public static void AssertModifier(StatModifier mod, string source = "?")
        {
            if (!IsDerived(mod.Stat)) throw new ArgumentOutOfRangeException(nameof(mod), $"modifier from \"{source}\" targets unknown stat \"{mod.Stat}\"");
            if (mod.Op != "add" && mod.Op != "mul") throw new ArgumentOutOfRangeException(nameof(mod), $"modifier from \"{source}\" has unknown op \"{mod.Op}\"");
            if (double.IsNaN(mod.Value) || double.IsInfinity(mod.Value)) throw new ArgumentException($"modifier from \"{source}\" has non-finite value");
        }

        /// <summary>Apply modifiers to base stats, then clamp and round each.</summary>
        public static DerivedStats ApplyModifiers(DerivedStats baseStats, IEnumerable<StatModifier> modifiers)
        {
            var adds = new Dictionary<string, double>();
            var muls = new Dictionary<string, double>();
            foreach (StatModifier m in modifiers)
            {
                AssertModifier(m, m.Source ?? "runtime");
                Dictionary<string, double> into = m.Op == "add" ? adds : muls;
                into[m.Stat] = (into.TryGetValue(m.Stat, out double v) ? v : 0) + m.Value;
            }
            var out_ = new DerivedStats();
            foreach (DerivedMeta meta in Derived)
            {
                double v = baseStats.Get(meta.Key) + (adds.TryGetValue(meta.Key, out double a) ? a : 0);
                v *= 1 + (muls.TryGetValue(meta.Key, out double mu) ? mu : 0);
                if (meta.Min.HasValue) v = Math.Max(meta.Min.Value, v);
                if (meta.Max.HasValue) v = Math.Min(meta.Max.Value, v);
                out_[meta.Key] = meta.Int ? RpgJs.Round(v) : RpgJs.Round(v * 1000) / 1000;
            }
            return out_;
        }
    }

    /// <summary>The derived stats by key, in the catalogue's order.</summary>
    public sealed class DerivedStats
    {
        readonly Dictionary<string, double> _values = new Dictionary<string, double>();

        public double this[string key]
        {
            get => _values.TryGetValue(key, out double v) ? v : throw new KeyNotFoundException($"no derived stat \"{key}\"");
            set
            {
                if (!RpgStats.IsDerived(key)) throw new ArgumentOutOfRangeException(nameof(key), key, "unknown derived stat");
                _values[key] = value;
            }
        }

        /// <summary>The value, or 0 for a stat that is not set (as <c>base[key] ?? 0</c>).</summary>
        public double Get(string key) => _values.TryGetValue(key, out double v) ? v : 0;

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            foreach (DerivedMeta m in RpgStats.Derived)
            {
                if (_values.TryGetValue(m.Key, out double v)) o[m.Key] = v;
            }
            return o;
        }
    }

    /// <summary>Modifier lists keyed by source (<c>perk:x</c>, <c>cyber:y</c>, <c>item:z</c>, <c>buff:w</c>), so removing a source is one delete. Keeps the insertion order of a JavaScript <c>Map</c>.</summary>
    public sealed class ModifierSet
    {
        readonly List<string> _order = new List<string>();
        readonly Dictionary<string, StatModifier[]> _bySource = new Dictionary<string, StatModifier[]>();

        public ModifierSet Set(string source, IReadOnlyList<StatModifier> modifiers)
        {
            if (string.IsNullOrEmpty(source)) throw new ArgumentException("ModifierSet.set: source must be a non-empty string");
            var list = new List<StatModifier>();
            if (modifiers != null)
            {
                foreach (StatModifier m in modifiers)
                {
                    RpgStats.AssertModifier(m, source);
                    list.Add(new StatModifier(m.Stat, m.Op, m.Value));
                }
            }
            if (list.Count == 0)
            {
                Remove(source);
            }
            else
            {
                if (!_bySource.ContainsKey(source)) _order.Add(source);
                _bySource[source] = list.ToArray();
            }
            return this;
        }

        public bool Remove(string source)
        {
            if (!_bySource.Remove(source)) return false;
            _order.Remove(source);
            return true;
        }

        public bool Has(string source) => _bySource.ContainsKey(source);

        public void Clear()
        {
            _bySource.Clear();
            _order.Clear();
        }

        public IReadOnlyList<string> Sources() => _order.ToArray();

        /// <summary>Every active modifier, each tagged with its source.</summary>
        public List<StatModifier> All()
        {
            var out_ = new List<StatModifier>();
            foreach (string source in _order)
            {
                foreach (StatModifier m in _bySource[source]) out_.Add(new StatModifier(m.Stat, m.Op, m.Value, source));
            }
            return out_;
        }

        public List<StatModifier> ForStat(string stat)
        {
            if (!RpgStats.IsDerived(stat)) throw new ArgumentOutOfRangeException(nameof(stat), $"forStat: unknown stat \"{stat}\"");
            return All().FindAll(m => m.Stat == stat);
        }
    }

    public readonly struct XpGain
    {
        public readonly double Gained;
        public readonly IReadOnlyList<int> Levels;
        public readonly int Level;
        public readonly double Total;

        public XpGain(double gained, IReadOnlyList<int> levels, int level, double total)
        {
            Gained = gained; Levels = levels; Level = level; Total = total;
        }
    }

    /// <summary>Attributes, level, XP and spendable points. Raises no events: the RPG core reads the results and announces them.</summary>
    public sealed class StatBlock
    {
        public readonly Dictionary<string, int> Attributes = new Dictionary<string, int>();
        public double Xp, CredXp;
        public int AttributePoints, PerkPoints;
        public readonly ModifierSet Modifiers = new ModifierSet();
        DerivedStats _derived;

        public StatBlock(IReadOnlyDictionary<string, int> attributes = null, double xp = 0, double credXp = 0, int attributePoints = 7, int perkPoints = 1)
        {
            foreach (string key in RpgStats.Attributes)
            {
                int v = attributes != null && attributes.TryGetValue(key, out int a) ? a : RpgStats.AttrMin;
                if (v < RpgStats.AttrMin || v > RpgStats.AttrMax)
                {
                    throw new ArgumentOutOfRangeException(nameof(attributes), v, $"StatBlock: attribute {key} must be an integer {RpgStats.AttrMin}-{RpgStats.AttrMax}");
                }
                Attributes[key] = v;
            }
            Xp = xp;
            CredXp = credXp;
            AttributePoints = attributePoints;
            PerkPoints = perkPoints;
        }

        public int Level => RpgStats.LevelFromXp(Xp).Level;
        public double XpInto => RpgStats.LevelFromXp(Xp).Into;
        public double XpToNext => RpgStats.LevelFromXp(Xp).ToNext;
        public int StreetCred => RpgStats.CredFromXp(CredXp).Level;
        public double CredInto => RpgStats.CredFromXp(CredXp).Into;
        public double CredToNext => RpgStats.CredFromXp(CredXp).ToNext;

        /// <summary>Drop the cached derived stats; call after any modifier or attribute change.</summary>
        public StatBlock Invalidate()
        {
            _derived = null;
            return this;
        }

        public DerivedStats Derived => _derived ?? (_derived = RpgStats.ApplyModifiers(RpgStats.BaseDerived(Attributes, Level), Modifiers.All()));

        public XpGain AddXp(double amount)
        {
            if (double.IsNaN(amount) || double.IsInfinity(amount)) throw new ArgumentException($"addXp: bad amount {amount}");
            if (amount <= 0) return new XpGain(0, Array.Empty<int>(), Level, Xp);
            double scaled = RpgJs.Round(amount * Derived["xpRate"]);
            int before = Level;
            Xp += scaled;
            int after = Level;
            var levels = new List<int>();
            for (int l = before + 1; l <= after; l++)
            {
                (int attributePoints, int perkPoints) = RpgStats.LevelUpAwards(l);
                AttributePoints += attributePoints;
                PerkPoints += perkPoints;
                levels.Add(l);
            }
            if (levels.Count > 0) Invalidate();
            return new XpGain(scaled, levels, after, Xp);
        }

        public XpGain AddCredXp(double amount)
        {
            if (double.IsNaN(amount) || double.IsInfinity(amount)) throw new ArgumentException($"addCredXp: bad amount {amount}");
            if (amount <= 0) return new XpGain(0, Array.Empty<int>(), StreetCred, double.NaN);
            int before = StreetCred;
            CredXp += RpgJs.Round(amount);
            int after = StreetCred;
            var levels = new List<int>();
            for (int l = before + 1; l <= after; l++) levels.Add(l);
            return new XpGain(RpgJs.Round(amount), levels, after, double.NaN);
        }

        public RpgResult SpendAttributePoint(string attr)
        {
            if (!RpgStats.IsAttribute(attr)) throw new ArgumentOutOfRangeException(nameof(attr), $"spendAttributePoint: unknown attribute \"{attr}\"");
            if (AttributePoints <= 0) return RpgResult.Fail("no-points");
            if (Attributes[attr] >= RpgStats.AttrMax) return RpgResult.Fail("at-max");
            Attributes[attr] += 1;
            AttributePoints -= 1;
            Invalidate();
            return new RpgResult { Ok = true, Value = Attributes[attr], Remaining = AttributePoints };
        }

        public JsonObject Serialize()
        {
            var attrs = new JsonObject();
            foreach (string key in RpgStats.Attributes) attrs[key] = (double)Attributes[key];
            return new JsonObject
            {
                ["attributes"] = attrs, ["xp"] = Xp, ["credXp"] = CredXp, ["attributePoints"] = (double)AttributePoints, ["perkPoints"] = (double)PerkPoints,
            };
        }

        public static StatBlock Deserialize(JsonObject data)
        {
            if (data == null) return new StatBlock();
            Dictionary<string, int> attrs = null;
            if (data.TryGetValue("attributes", out object a) && a is JsonObject ao)
            {
                attrs = new Dictionary<string, int>();
                foreach (KeyValuePair<string, object> kv in ao) attrs[kv.Key] = RpgJs.Int(kv.Value, $"attribute {kv.Key}");
            }
            return new StatBlock(attrs,
                data.TryGetValue("xp", out object xp) && xp != null ? RpgJs.Num(xp, "xp") : 0,
                data.TryGetValue("credXp", out object cx) && cx != null ? RpgJs.Num(cx, "credXp") : 0,
                data.TryGetValue("attributePoints", out object ap) && ap != null ? RpgJs.Int(ap, "attributePoints") : 7,
                data.TryGetValue("perkPoints", out object pp) && pp != null ? RpgJs.Int(pp, "perkPoints") : 1);
        }
    }
}
