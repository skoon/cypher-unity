using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>A quest as a condition sees it.</summary>
    public readonly struct QuestStateView
    {
        public readonly string State, Stage, Outcome;

        public QuestStateView(string state, string stage, string outcome)
        {
            State = state; Stage = stage; Outcome = outcome;
        }
    }

    /// <summary>The game state a condition reads, kept small so the evaluator stays pure.</summary>
    public interface IConditionView
    {
        int Level { get; }
        int StreetCred { get; }
        double Money { get; }
        int Attribute(string name);
        bool HasPerk(string id, int rank);
        bool HasCyberware(string id);
        bool HasAbility(string tag);
        /// <summary>A world flag's value (bool, string or number), or null when unset.</summary>
        object Flag(string name);
        int CountItem(string templateId);
        QuestStateView QuestState(string id);
    }

    /// <summary>
    /// The one condition grammar of quest prerequisites, stage branches and dialogue gates: a port of <c>rpg/Conditions.js</c>, read straight from the condition's
    /// JSON (the prototype's tables are written as JSON-shaped objects, with fields that may be a string, a list or a map). Every present field must pass:
    /// <c>level, streetCred, attribute, perk, cyberware, ability, flag, notFlag, item, money, quest, all, any, not</c>.
    /// </summary>
    public static class RpgConditions
    {
        static readonly HashSet<string> KnownKeys = new HashSet<string>
        {
            "level", "streetCred", "attribute", "perk", "cyberware", "ability", "flag", "notFlag", "item", "money", "quest", "all", "any", "not",
        };

        public static JsonObject Assert(object cond, string where = "condition")
        {
            if (cond == null) return null;
            if (!(cond is JsonObject c)) throw new ArgumentException($"{where}: must be an object");
            foreach (string key in c.Keys)
            {
                if (!KnownKeys.Contains(key)) throw new ArgumentOutOfRangeException(nameof(cond), $"{where}: unknown condition key \"{key}\"");
            }
            if (c.TryGetValue("attribute", out object attrs) && attrs is JsonObject ao)
            {
                foreach (string attr in ao.Keys)
                {
                    if (!RpgStats.IsAttribute(attr)) throw new ArgumentOutOfRangeException(nameof(cond), $"{where}: unknown attribute \"{attr}\"");
                }
            }
            foreach (object sub in RpgJs.AsList(Get(c, "all"))) Assert(sub, where + ".sub");
            foreach (object sub in RpgJs.AsList(Get(c, "any"))) Assert(sub, where + ".sub");
            if (RpgJs.Truthy(Get(c, "not"))) Assert(c["not"], where + ".not");
            return c;
        }

        static object Get(JsonObject c, string key) => c.TryGetValue(key, out object v) ? v : null;

        static bool Has(JsonObject c, string key) => c.ContainsKey(key);

        public static (bool Ok, List<string> Missing) Check(object condition, IConditionView view)
        {
            var missing = new List<string>();
            if (condition == null) return (true, missing);
            JsonObject cond = Assert(condition);

            if (Has(cond, "level") && view.Level < RpgJs.Num(cond["level"], "level")) missing.Add($"LEVEL {RpgJs.Str((double)cond["level"])}");
            if (Has(cond, "streetCred") && view.StreetCred < RpgJs.Num(cond["streetCred"], "streetCred")) missing.Add($"STREET CRED {RpgJs.Str((double)cond["streetCred"])}");
            if (Has(cond, "money") && view.Money < RpgJs.Num(cond["money"], "money")) missing.Add($"{RpgJs.Str((double)cond["money"])}€$");

            if (Get(cond, "attribute") is JsonObject attrs)
            {
                foreach (KeyValuePair<string, object> kv in attrs)
                {
                    double min = RpgJs.Num(kv.Value, "attribute minimum");
                    if (view.Attribute(kv.Key) < min) missing.Add($"{RpgStats.AttributeLabelUpper(kv.Key)} {RpgJs.Str(min)}");
                }
            }

            if (Has(cond, "perk"))
            {
                object perk = cond["perk"];
                if (perk is JsonObject ranks)
                {
                    foreach (KeyValuePair<string, object> kv in ranks)
                    {
                        int rank = RpgJs.Int(kv.Value, "perk rank");
                        if (!view.HasPerk(kv.Key, rank)) missing.Add($"PERK {kv.Key} {rank}");
                    }
                }
                else
                {
                    foreach (object id in RpgJs.AsList(perk)) if (!view.HasPerk((string)id, 1)) missing.Add($"PERK {id}");
                }
            }

            foreach (object id in RpgJs.AsList(Get(cond, "cyberware"))) if (!view.HasCyberware((string)id)) missing.Add($"CYBERWARE {id}");
            foreach (object tag in RpgJs.AsList(Get(cond, "ability"))) if (!view.HasAbility((string)tag)) missing.Add($"ABILITY {tag}");

            if (Has(cond, "flag"))
            {
                object flag = cond["flag"];
                if (flag is JsonObject wants)
                {
                    foreach (KeyValuePair<string, object> kv in wants)
                    {
                        object have = view.Flag(kv.Key);
                        bool match = kv.Value is bool want ? RpgJs.Truthy(have) == want : RpgJs.StrictEquals(have, kv.Value);
                        if (!match) missing.Add($"FLAG {kv.Key}={JsString(kv.Value)}");
                    }
                }
                else
                {
                    foreach (object name in RpgJs.AsList(flag)) if (!RpgJs.Truthy(view.Flag((string)name))) missing.Add($"FLAG {name}");
                }
            }
            foreach (object name in RpgJs.AsList(Get(cond, "notFlag"))) if (RpgJs.Truthy(view.Flag((string)name))) missing.Add($"NOT FLAG {name}");

            if (Get(cond, "item") is JsonObject items)
            {
                foreach (KeyValuePair<string, object> kv in items)
                {
                    double qty = RpgJs.Num(kv.Value, "item quantity");
                    if (view.CountItem(kv.Key) < qty) missing.Add($"ITEM {kv.Key} x{RpgJs.Str(qty)}");
                }
            }

            foreach (object qo in RpgJs.AsList(Get(cond, "quest")))
            {
                var q = (JsonObject)qo;
                QuestStateView st = view.QuestState((string)q["id"]);
                string state = Get(q, "state") as string, stage = Get(q, "stage") as string, outcome = Get(q, "outcome") as string;
                if (!string.IsNullOrEmpty(state) && st.State != state) missing.Add($"QUEST {q["id"]} {state}");
                if (!string.IsNullOrEmpty(stage) && st.Stage != stage) missing.Add($"QUEST {q["id"]} @{stage}");
                if (!string.IsNullOrEmpty(outcome) && st.Outcome != outcome) missing.Add($"QUEST {q["id"]} -> {outcome}");
            }

            foreach (object sub in RpgJs.AsList(Get(cond, "all")))
            {
                (bool ok, List<string> subMissing) = Check(sub, view);
                if (!ok) missing.AddRange(subMissing);
            }
            if (Has(cond, "any"))
            {
                List<object> options = RpgJs.AsList(cond["any"]);
                if (options.Count > 0 && !options.Exists(sub => Check(sub, view).Ok)) missing.Add($"ANY OF {options.Count}");
            }
            if (Has(cond, "not") && Check(cond["not"], view).Ok) missing.Add("NOT");

            return (missing.Count == 0, missing);
        }

        /// <summary>The short label of a gate, as the dialogue prefixes an option with it: <c>INTELLIGENCE 6</c>, <c>5000€$</c>.</summary>
        public static string Describe(object condition)
        {
            if (!(condition is JsonObject cond)) return "";
            var parts = new List<string>();
            if (Get(cond, "attribute") is JsonObject attrs)
            {
                foreach (KeyValuePair<string, object> kv in attrs) parts.Add($"{RpgStats.AttributeLabelUpper(kv.Key)} {JsString(kv.Value)}");
            }
            if (Has(cond, "level")) parts.Add($"LEVEL {JsString(cond["level"])}");
            if (Has(cond, "streetCred")) parts.Add($"STREET CRED {JsString(cond["streetCred"])}");
            if (Has(cond, "money")) parts.Add($"{JsString(cond["money"])}€$");
            if (Has(cond, "perk"))
            {
                object perk = cond["perk"];
                if (perk is JsonObject ranks)
                {
                    foreach (KeyValuePair<string, object> kv in ranks) parts.Add($"PERK: {kv.Key} {JsString(kv.Value)}");
                }
                else
                {
                    foreach (object id in RpgJs.AsList(perk)) parts.Add($"PERK: {id}");
                }
            }
            foreach (object id in RpgJs.AsList(Get(cond, "cyberware"))) parts.Add($"CHROME: {id}");
            foreach (object tag in RpgJs.AsList(Get(cond, "ability"))) parts.Add(((string)tag).ToUpperInvariant().Replace('_', ' '));
            if (Get(cond, "item") is JsonObject items)
            {
                foreach (KeyValuePair<string, object> kv in items) parts.Add($"{kv.Key} x{JsString(kv.Value)}");
            }
            return string.Join(" + ", parts);
        }

        /// <summary>A JSON value as a JavaScript template literal writes it.</summary>
        static string JsString(object v)
        {
            switch (v)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case double d: return RpgJs.Str(d);
                default: return v.ToString();
            }
        }
    }
}
