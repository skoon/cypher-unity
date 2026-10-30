using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>An attribute as the character sheet lists it.</summary>
    public readonly struct AttributeView
    {
        public readonly string Key, Label, Blurb;
        public readonly int Value, Max;
        /// <summary>Null when a point can go into it now, otherwise why not.</summary>
        public readonly string Blocked;

        public AttributeView(string key, string label, string blurb, int value, int max, string blocked)
        {
            Key = key; Label = label; Blurb = blurb; Value = value; Max = max; Blocked = blocked;
        }

        public bool CanRaise => Blocked == null;
    }

    /// <summary>A perk as the tree shows it.</summary>
    public sealed class PerkView
    {
        public string Id, Name, Desc;
        public int Tier, Rank, MaxRank, Cost;
        /// <summary>Null when it can be bought now, otherwise why not, in words.</summary>
        public string Blocked;
        /// <summary>What it needs, as the core describes it (<c>Reflexes 6</c>, <c>Level 4</c>).</summary>
        public List<string> Requires = new List<string>();
        /// <summary>What is still missing of that.</summary>
        public List<string> Missing = new List<string>();

        public bool Owned => Rank > 0;
        public bool Maxed => Rank >= MaxRank;
        /// <summary>ACTIVE, MAXED, AVAILABLE or LOCKED: the detail's heading.</summary>
        public string State => Maxed ? "MAXED" : Blocked == null ? "AVAILABLE" : Owned ? "ACTIVE" : "LOCKED";
    }

    /// <summary>A chrome slot of the body with what is in it.</summary>
    public sealed class ChromeSlotView
    {
        public string Key, Label, Blurb;
        public int Capacity;
        public List<(string Id, string Name, string Corp, int Load, string Desc)> Installed = new List<(string, string, string, int, string)>();
    }

    /// <summary>
    /// The character sheet from the core: level, XP and street cred with their progress, the attributes and their points, each attribute's perk tree with what
    /// can be bought and why not (the prototype drew a placeholder tree), and the chrome in each slot against the body's capacity. Pure.
    /// </summary>
    public static class CharacterModel
    {
        public sealed class Sheet
        {
            public int Level, LevelCap, StreetCred, AttributePoints, PerkPoints;
            public double XpInto, XpToNext, CredInto, CredToNext;
            public List<AttributeView> Attributes = new List<AttributeView>();

            public double XpShare => double.IsInfinity(XpToNext) || XpToNext <= 0 ? 1 : Math.Max(0, Math.Min(1, XpInto / XpToNext));
            public double CredShare => double.IsInfinity(CredToNext) || CredToNext <= 0 ? 1 : Math.Max(0, Math.Min(1, CredInto / CredToNext));
        }

        public static Sheet Read(RpgCore core)
        {
            if (core == null) throw new ArgumentNullException(nameof(core));
            var sheet = new Sheet
            {
                Level = core.Stats.Level, LevelCap = RpgStats.LevelCap, StreetCred = core.Stats.StreetCred, AttributePoints = core.Stats.AttributePoints,
                PerkPoints = core.Stats.PerkPoints, XpInto = core.Stats.XpInto, XpToNext = core.Stats.XpToNext, CredInto = core.Stats.CredInto, CredToNext = core.Stats.CredToNext,
            };
            foreach (string a in RpgStats.Attributes)
            {
                int v = core.Stats.Attributes[a];
                (string label, string blurb) = RpgStats.AttributeMeta[a];
                string blocked = v >= RpgStats.AttrMax ? "AT MAXIMUM" : core.Stats.AttributePoints <= 0 ? "NO POINTS" : null;
                sheet.Attributes.Add(new AttributeView(a, label.ToUpperInvariant(), blurb, v, RpgStats.AttrMax, blocked));
            }
            return sheet;
        }

        /// <summary>An attribute's perk tree in tier order.</summary>
        public static List<PerkView> Tree(RpgCore core, string attribute)
        {
            JsonObject tree = core.GetPerkTrees().FirstOrDefault(t => (string)t["tree"] == attribute)
                ?? throw new ArgumentOutOfRangeException(nameof(attribute), attribute, "no perk tree for that attribute");
            var list = new List<PerkView>();
            foreach (JsonObject p in ((List<object>)tree["perks"]).Cast<JsonObject>())
            {
                list.Add(new PerkView
                {
                    Id = (string)p["id"], Name = ((string)p["name"]).ToUpperInvariant(), Desc = Describe(RpgPerks.Get((string)p["id"])), Tier = (int)(double)p["tier"], Rank = (int)(double)p["rank"],
                    MaxRank = (int)(double)p["maxRank"], Cost = (int)(double)p["cost"], Blocked = (bool)p["canBuy"] ? null : InventoryModel.Reason(p["reason"] as string),
                    Requires = ((List<object>)p["requires"]).Cast<string>().ToList(), Missing = ((List<object>)p["missing"]).Cast<string>().ToList(),
                });
            }
            return list;
        }

        /// <summary>
        /// A perk's text with each <c>{n}</c> filled in with what one rank gives, from its own modifiers in order (the prototype's tables say "{n} is substituted with
        /// the per-rank value", but no code of the prototype ever did it). A value written before <c>%</c> is a fraction shown as a percentage.
        /// </summary>
        public static string Describe(Perk perk)
        {
            if (perk == null) throw new ArgumentNullException(nameof(perk));
            StatModifier[] mods = perk.Mods(1);
            var sb = new System.Text.StringBuilder();
            int next = 0;
            for (int i = 0; i < perk.Desc.Length; i++)
            {
                if (string.CompareOrdinal(perk.Desc, i, "{n}", 0, 3) != 0)
                {
                    sb.Append(perk.Desc[i]);
                    continue;
                }
                if (mods.Length == 0) throw new InvalidOperationException($"perk \"{perk.Id}\" says {{n}} but gives nothing per rank");
                double v = mods[Math.Min(next++, mods.Length - 1)].Value;
                bool percent = i + 3 < perk.Desc.Length && perk.Desc[i + 3] == '%';
                double shown = percent && Math.Abs(v) <= 1 ? RpgJs.Round(v * 1000) / 10 : RpgJs.Round(v * 10) / 10;
                sb.Append(shown.ToString(System.Globalization.CultureInfo.InvariantCulture));
                i += 2;
            }
            return perk.MaxRank > 1 && perk.Desc.Contains("{n}") ? sb + " (per rank)" : sb.ToString();
        }

        /// <summary>The button under a perk: what pressing it does, or why it cannot.</summary>
        public static string BuyLabel(PerkView p)
        {
            if (p.Maxed) return "MAXED";
            if (p.Blocked != null) return p.Blocked;
            return p.Cost == 1 ? "UNLOCK — 1 PERK POINT" : $"UNLOCK — {p.Cost} PERK POINTS";
        }

        /// <summary>Capacity used and in total, and each slot's chrome.</summary>
        public static (int Used, int Capacity, List<ChromeSlotView> Slots) Chrome(RpgCore core)
        {
            JsonObject c = core.GetCyberware();
            var slots = new List<ChromeSlotView>();
            foreach (JsonObject s in ((List<object>)c["slots"]).Cast<JsonObject>())
            {
                var v = new ChromeSlotView { Key = (string)s["key"], Label = ((string)s["label"]).ToUpperInvariant(), Blurb = (string)s["blurb"], Capacity = (int)(double)s["capacity"] };
                foreach (JsonObject w in ((List<object>)s["installed"]).Cast<JsonObject>())
                {
                    v.Installed.Add(((string)w["id"], ((string)w["name"]).ToUpperInvariant(), ((string)w["corp"]).ToUpperInvariant(), (int)(double)w["capacity"], (string)w["desc"]));
                }
                slots.Add(v);
            }
            return ((int)(double)c["used"], (int)(double)c["capacity"], slots);
        }
    }
}
