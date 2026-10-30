using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>A job in the journal's list.</summary>
    public readonly struct JournalEntry
    {
        public readonly string Id, Title, Type, Giver, Status, Progress;
        public readonly int Level;
        public readonly bool Tracked;
        /// <summary>ACTIVE, AVAILABLE or FINISHED: the list's section.</summary>
        public readonly string Section;

        public JournalEntry(string id, string title, string type, string giver, string status, string progress, int level, bool tracked, string section)
        {
            Id = id; Title = title; Type = type; Giver = giver; Status = status; Progress = progress; Level = level; Tracked = tracked; Section = section;
        }
    }

    /// <summary>A job as the journal's right-hand pane shows it.</summary>
    public sealed class JournalDetail
    {
        public string Id, Title, Type, Giver, Status, Summary, StageTitle, StageText, OutcomeTitle, OutcomeSummary, TimeLeft;
        public int Level;
        public bool Tracked;
        /// <summary>The job can be tracked now (it is active and not the tracked one).</summary>
        public bool CanTrack;
        public List<(string Text, bool Done, bool Optional, bool Active, string Distance)> Objectives = new List<(string, bool, bool, bool, string)>();
        /// <summary>What a finished job paid, from its ending; empty while it is open (the pay depends on how it ends).</summary>
        public List<string> Rewards = new List<string>();
        /// <summary>For a job not yet taken: who to see about it.</summary>
        public string Hint = "";
    }

    /// <summary>
    /// The journal from the core's quest log (the prototype's journal showed made-up rewards; this one shows only what the log says): the jobs in three sections,
    /// active (the tracked one first), available and finished, and the selected job's stage, objectives with distances, time left, ending and pay. Pure.
    /// </summary>
    public static class JournalModel
    {
        public static List<JournalEntry> Entries(JsonObject quests)
        {
            if (quests == null) throw new ArgumentNullException(nameof(quests));
            var list = new List<JournalEntry>();
            List<JsonObject> active = Objs(quests["active"]).OrderByDescending(q => (bool)q["tracked"]).ToList();
            foreach (JsonObject q in active)
            {
                int open = Objs(q["objectives"]).Count(o => !(bool)o["done"] && (bool)o["visible"] && !(bool)o["optional"]);
                list.Add(Entry(q, "ACTIVE", "ACTIVE", open == 1 ? "1 OBJECTIVE" : $"{open} OBJECTIVES", (bool)q["tracked"]));
            }
            foreach (JsonObject q in Objs(quests["available"])) list.Add(Entry(q, "AVAILABLE", "AVAILABLE", "NOT TAKEN", false));
            foreach (JsonObject q in Objs(quests["finished"]))
            {
                string status = (string)q["state"] == "failed" ? "FAILED" : "COMPLETE";
                list.Add(Entry(q, "FINISHED", status, ((q["outcomeTitle"] as string) ?? status).ToUpperInvariant(), false));
            }
            return list;
        }

        static JournalEntry Entry(JsonObject q, string section, string status, string progress, bool tracked) =>
            new JournalEntry((string)q["id"], ((string)q["title"]).ToUpperInvariant(), HudModel.JobType(q["type"] as string), ((q["giver"] as string) ?? "").ToUpperInvariant(), status, progress,
                q.TryGetValue("recommendedLevel", out object l) && l is double d ? (int)d : 0, tracked, section);

        /// <summary>The detail of a job: a snapshot (active or finished) or an available job's short entry, with its definition for the pay.</summary>
        public static JournalDetail Detail(JsonObject quest, JsonObject def, double playerX, double playerZ)
        {
            if (quest == null) throw new ArgumentNullException(nameof(quest));
            string state = quest.TryGetValue("state", out object s) ? s as string : null;
            var d = new JournalDetail
            {
                Id = (string)quest["id"], Title = ((string)quest["title"]).ToUpperInvariant(), Type = HudModel.JobType(quest["type"] as string),
                Giver = ((quest["giver"] as string) ?? "").ToUpperInvariant(), Summary = (quest["summary"] as string) ?? "",
                Level = def != null && def.TryGetValue("recommendedLevel", out object l) && l is double lv ? (int)lv : 0,
            };
            switch (state)
            {
                case null:
                case "inactive":
                    d.Status = "AVAILABLE";
                    d.Hint = d.Giver.Length > 0 ? $"TALK TO {d.Giver} TO TAKE THIS JOB" : "";
                    return d;
                case "active":
                    d.Status = "ACTIVE";
                    break;
                case "failed":
                    d.Status = "FAILED";
                    break;
                default:
                    d.Status = "COMPLETE";
                    break;
            }
            d.Tracked = (bool)quest["tracked"];
            d.CanTrack = state == "active" && !d.Tracked;
            d.StageTitle = ((quest["stageTitle"] as string) ?? "").ToUpperInvariant();
            d.StageText = (quest["journal"] as string) ?? "";
            if (quest["timeLeft"] is double t) d.TimeLeft = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(t))).ToString(@"m\:ss", CultureInfo.InvariantCulture);

            List<TrackerStep> open = HudModel.Steps(quest, playerX, playerZ, int.MaxValue);
            foreach (JsonObject o in Objs(quest["objectives"]).Where(o => (bool)o["visible"]))
            {
                bool done = (bool)o["done"];
                TrackerStep? step = done ? (TrackerStep?)null : open.FirstOrDefault(x => x.Text == (string)o["text"]);
                string dist = step?.Distance is double m && state == "active" ? HudModel.Distance(m) : "";
                d.Objectives.Add(((string)o["text"], done, (bool)o["optional"], step?.Active ?? false, dist));
            }

            if (state == "completed" || state == "failed")
            {
                d.OutcomeTitle = ((quest["outcomeTitle"] as string) ?? "").ToUpperInvariant();
                d.OutcomeSummary = (quest["outcomeSummary"] as string) ?? "";
                string outcomeId = quest["outcome"] as string;
                if (outcomeId != null && def?["outcomes"] is JsonObject outcomes && outcomes.TryGetValue(outcomeId, out object oo) && oo is JsonObject outcome
                    && outcome.TryGetValue("rewards", out object r) && r is JsonObject rewards) d.Rewards = Rewards(rewards);
            }
            return d;
        }

        /// <summary>A reward table as lines: XP, eddies, street cred, points, and the items and chrome by name.</summary>
        public static List<string> Rewards(JsonObject rewards)
        {
            var lines = new List<string>();
            double Num(string key) => rewards.TryGetValue(key, out object v) && v is double x ? x : 0;
            if (Num("xp") > 0) lines.Add($"{Num("xp").ToString("#,0", CultureInfo.InvariantCulture)} XP");
            if (Num("money") > 0) lines.Add(HudModel.Money(Num("money")));
            if (Num("cred") > 0) lines.Add($"{Num("cred"):0} STREET CRED");
            if (Num("perkPoints") > 0) lines.Add(Num("perkPoints") == 1 ? "1 PERK POINT" : $"{Num("perkPoints"):0} PERK POINTS");
            if (Num("attributePoints") > 0) lines.Add(Num("attributePoints") == 1 ? "1 ATTRIBUTE POINT" : $"{Num("attributePoints"):0} ATTRIBUTE POINTS");
            if (rewards.TryGetValue("items", out object items) && items is List<object> list)
            {
                foreach (JsonObject it in list.Cast<JsonObject>())
                {
                    if (it.TryGetValue("cyberware", out object cw) && cw is string cid) lines.Add(RpgCyberware.Get(cid).Name.ToUpperInvariant());
                    else if (it.TryGetValue("templateId", out object tid) && tid is string id)
                    {
                        string rarity = it.TryGetValue("rarity", out object ra) && ra is string rs ? rs.ToUpperInvariant() + " " : "";
                        lines.Add(rarity + RpgItems.Template(id).BaseNames[0].ToUpperInvariant());
                    }
                }
            }
            return lines;
        }

        static List<JsonObject> Objs(object o) => o is List<object> l ? l.Cast<JsonObject>().ToList() : new List<JsonObject>();
    }
}
