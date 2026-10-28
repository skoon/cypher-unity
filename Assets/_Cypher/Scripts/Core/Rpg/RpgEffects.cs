using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// The effect grammar of quest stages, objective completions, rewards and dialogue options (<c>{ type, …args }</c>): a port of <c>rpg/Effects.js</c>. An unknown
    /// type or a missing argument throws when the table is checked, so a typo in a quest never silently does nothing. The RPG core runs them.
    /// </summary>
    public static class RpgEffects
    {
        public static readonly IReadOnlyDictionary<string, string[]> Types = new Dictionary<string, string[]>
        {
            ["setFlag"] = new[] { "flag" },
            ["clearFlag"] = new[] { "flag" },
            ["addXp"] = new[] { "amount" },
            ["addCred"] = new[] { "amount" },
            ["addMoney"] = new[] { "amount" },
            ["giveItem"] = new[] { "templateId" },
            ["giveCyberware"] = new[] { "cyberwareId" },
            ["removeItem"] = new[] { "templateId" },
            ["startQuest"] = new[] { "questId" },
            ["completeObjective"] = new[] { "questId", "objectiveId" },
            ["advanceQuest"] = new[] { "questId" },
            ["failQuest"] = new[] { "questId", "outcome" },
            ["trackQuest"] = new[] { "questId" },
            ["notify"] = new[] { "text" },
            ["startHack"] = new[] { "targetId" },
            ["openVendor"] = new[] { "vendorId" },
            ["giveQuickhack"] = new[] { "templateId" },
            ["endDialogue"] = Array.Empty<string>(),
        };

        public static JsonObject Assert(object effect, string where = "effect")
        {
            if (!(effect is JsonObject e)) throw new ArgumentException($"{where}: must be an object");
            string type = e.TryGetValue("type", out object t) ? t as string : null;
            if (type == null || !Types.TryGetValue(type, out string[] spec)) throw new ArgumentOutOfRangeException(nameof(effect), $"{where}: unknown effect type \"{type}\"");
            foreach (string field in spec)
            {
                if (!e.ContainsKey(field)) throw new ArgumentException($"{where}: effect \"{type}\" is missing \"{field}\"");
            }
            return e;
        }

        public static List<object> AssertAll(object list, string where = "effects")
        {
            if (list == null) return new List<object>();
            if (!(list is List<object> l)) throw new ArgumentException($"{where}: must be an array");
            for (int i = 0; i < l.Count; i++) Assert(l[i], $"{where}[{i}]");
            return l;
        }
    }
}
