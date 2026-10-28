using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>What the quest log reads from and does to the rest of the game.</summary>
    public interface IQuestHost
    {
        void Emit(string type, JsonObject payload);
        void ApplyEffects(List<object> effects, JsonObject context);
        JsonObject GrantRewards(JsonObject rewards, JsonObject meta);
        (bool Ok, List<string> Missing) Check(object condition);
        IReadOnlyDictionary<string, RpgLocation> Locations { get; }
        int CountItem(string templateId);
        object GetFlag(string flag);
    }

    /// <summary>Where a quest is: inactive, active (at a stage, with its objectives), completed or failed (with an outcome).</summary>
    public sealed class QuestRuntime
    {
        public string Id, State, Stage, Outcome;
        /// <summary>Seconds left on the stage's time limit, or null.</summary>
        public double? TimeLeft;
        /// <summary>The current stage's objectives, in the order written.</summary>
        public List<(string Id, bool Done, double Count)> Objectives = new List<(string, bool, double)>();

        public int IndexOf(string objectiveId) => Objectives.FindIndex(o => o.Id == objectiveId);

        public bool IsDone(string objectiveId)
        {
            int i = IndexOf(objectiveId);
            return i >= 0 && Objectives[i].Done;
        }

        public JsonObject ObjectivesJson()
        {
            var o = new JsonObject();
            foreach ((string id, bool done, double count) in Objectives) o[id] = new JsonObject { ["done"] = done, ["count"] = count };
            return o;
        }
    }

    /// <summary>
    /// The quest log: a port of <c>rpg/Quests.js</c>. A quest is a list of stages; a stage is a list of objectives, and when every one that is not optional is done the
    /// stage resolves, to the first branch whose condition passes, to an outcome that ends the quest, or to the next stage. A stage may have a time limit. Objectives
    /// are completed by reaching a place, holding an item, a flag, kills, a breach, a finished conversation, or by script. The quests are read from
    /// <see cref="RpgContent.QuestsJson"/> and checked once.
    /// </summary>
    public sealed class QuestLog
    {
        public static readonly IReadOnlyList<string> States = new[] { "inactive", "active", "completed", "failed" };
        static readonly HashSet<string> Triggers = new HashSet<string> { "reach", "item", "flag", "kill", "hack", "talk", "manual" };

        static List<JsonObject> _shipped;

        /// <summary>The shipped quests, checked.</summary>
        public static IReadOnlyList<JsonObject> Shipped
        {
            get
            {
                if (_shipped == null)
                {
                    var list = ((List<object>)Json.Parse(RpgContent.QuestsJson)).Cast<JsonObject>().ToList();
                    Validate(list);
                    _shipped = list;
                }
                return _shipped;
            }
        }

        static object Get(JsonObject o, string key) => o.TryGetValue(key, out object v) ? v : null;
        static string Str(JsonObject o, string key) => Get(o, key) as string;
        static List<JsonObject> Objs(object v) => RpgJs.AsList(v).Cast<JsonObject>().ToList();

        /// <summary>Check a quest table: a broken reference is a hard error when the table is read, never a quest that silently stalls.</summary>
        public static void Validate(IReadOnlyList<JsonObject> quests)
        {
            var seen = new HashSet<string>();
            foreach (JsonObject q in quests)
            {
                string qid = Str(q, "id");
                if (!seen.Add(qid)) throw new InvalidOperationException($"Duplicate quest id \"{qid}\"");
                RpgConditions.Assert(Get(q, "requires"), $"{qid}.requires");
                List<JsonObject> stages = Objs(Get(q, "stages"));
                if (stages.Count == 0) throw new InvalidOperationException($"Quest \"{qid}\" has no stages");
                var stageIds = new HashSet<string>(stages.Select(s => Str(s, "id")));
                var outcomes = Get(q, "outcomes") as JsonObject;
                var outcomeIds = new HashSet<string>(outcomes?.Keys ?? new List<string>());
                if (outcomeIds.Count == 0) throw new InvalidOperationException($"Quest \"{qid}\" has no outcomes");
                foreach (JsonObject s in stages)
                {
                    string sid = Str(s, "id");
                    RpgEffects.AssertAll(Get(s, "onEnter"), $"{qid}.{sid}.onEnter");
                    List<JsonObject> objectives = Objs(Get(s, "objectives"));
                    if (objectives.Count == 0) throw new InvalidOperationException($"Stage \"{qid}.{sid}\" has no objectives");
                    var objIds = new HashSet<string>();
                    foreach (JsonObject o in objectives)
                    {
                        string oid = Str(o, "id");
                        if (!objIds.Add(oid)) throw new InvalidOperationException($"Duplicate objective \"{oid}\" in \"{qid}.{sid}\"");
                        string trigger = Str(o, "trigger") ?? "manual";
                        if (!Triggers.Contains(trigger)) throw new InvalidOperationException($"Objective \"{qid}.{sid}.{oid}\" has unknown trigger \"{trigger}\"");
                        if (trigger == "reach") RpgLocations.Def(Str(o, "location"));
                        if (trigger == "item" && !RpgJs.Truthy(Get(o, "item"))) throw new InvalidOperationException($"Objective \"{oid}\" needs an item spec");
                        if (trigger == "flag" && !RpgJs.Truthy(Get(o, "flag"))) throw new InvalidOperationException($"Objective \"{oid}\" needs a flag name");
                        if (trigger == "kill" && !RpgJs.Truthy(Get(o, "tag"))) throw new InvalidOperationException($"Objective \"{oid}\" needs a kill tag");
                        if (trigger == "hack" && !RpgJs.Truthy(Get(o, "target"))) throw new InvalidOperationException($"Objective \"{oid}\" needs a hack target");
                        if (trigger == "talk" && !RpgJs.Truthy(Get(o, "dialogue"))) throw new InvalidOperationException($"Objective \"{oid}\" needs a dialogue id");
                        if (RpgJs.Truthy(Get(o, "hidden"))) RpgConditions.Assert(o["hidden"], $"{qid}.{sid}.{oid}.hidden");
                        RpgEffects.AssertAll(Get(o, "onComplete"), $"{qid}.{sid}.{oid}.onComplete");
                    }
                    foreach (JsonObject b in Objs(Get(s, "branches")))
                    {
                        RpgConditions.Assert(Get(b, "when"), $"{qid}.{sid}.branch");
                        if (!stageIds.Contains(Str(b, "to"))) throw new InvalidOperationException($"Branch in \"{qid}.{sid}\" points at missing stage \"{Str(b, "to")}\"");
                    }
                    string next = Str(s, "next"), complete = Str(s, "complete");
                    if (!string.IsNullOrEmpty(next) && !stageIds.Contains(next)) throw new InvalidOperationException($"Stage \"{qid}.{sid}\" points at missing stage \"{next}\"");
                    if (!string.IsNullOrEmpty(complete) && !outcomeIds.Contains(complete))
                    {
                        throw new InvalidOperationException($"Stage \"{qid}.{sid}\" completes into unknown outcome \"{complete}\"");
                    }
                    if (Get(s, "onTimeout") is JsonObject timeout)
                    {
                        string target = Str(timeout, "fail") ?? Str(timeout, "to");
                        if (!outcomeIds.Contains(target) && !stageIds.Contains(target)) throw new InvalidOperationException($"Stage \"{qid}.{sid}\" times out into unknown target \"{target}\"");
                    }
                    if (string.IsNullOrEmpty(next) && string.IsNullOrEmpty(complete) && Objs(Get(s, "branches")).Count == 0)
                    {
                        throw new InvalidOperationException($"Stage \"{qid}.{sid}\" is a dead end (no next, branches or complete)");
                    }
                }
            }
        }

        readonly IQuestHost _host;
        public readonly IReadOnlyList<JsonObject> Defs;
        readonly Dictionary<string, JsonObject> _byId = new Dictionary<string, JsonObject>();
        readonly List<string> _order = new List<string>();
        readonly Dictionary<string, QuestRuntime> _states = new Dictionary<string, QuestRuntime>();

        /// <summary>The quest the HUD tracks, or null.</summary>
        public string Tracked { get; private set; }

        public QuestLog(IQuestHost host, IReadOnlyList<JsonObject> defs = null)
        {
            _host = host ?? throw new ArgumentException("QuestLog: host is required");
            Defs = defs ?? Shipped;
            foreach (JsonObject q in Defs) _byId[Str(q, "id")] = q;
        }

        public JsonObject Def(string id)
        {
            if (id == null || !_byId.TryGetValue(id, out JsonObject q)) throw new ArgumentOutOfRangeException(nameof(id), $"Unknown quest id \"{id}\"");
            return q;
        }

        static JsonObject StageOf(JsonObject def, string stageId) => stageId == null ? null : Objs(def["stages"]).FirstOrDefault(s => Str(s, "id") == stageId);

        static JsonObject ObjectiveOf(JsonObject stage, string objectiveId) => Objs(stage["objectives"]).FirstOrDefault(o => Str(o, "id") == objectiveId);

        // ------------------------------------------------------------ reads

        /// <summary>A quest's state; an inactive one when it has never started.</summary>
        public QuestRuntime GetState(string id)
        {
            Def(id);
            return _states.TryGetValue(id, out QuestRuntime st) ? st : new QuestRuntime { Id = id, State = "inactive" };
        }

        IEnumerable<QuestRuntime> StatesInOrder() => _order.Select(id => _states[id]).ToList();

        public bool IsActive(string id) => GetState(id).State == "active";

        public bool IsCompleted(string id) => GetState(id).State == "completed";

        public List<JsonObject> Active() => StatesInOrder().Where(s => s.State == "active").Select(s => Snapshot(s.Id)).ToList();

        public List<JsonObject> Finished() => StatesInOrder().Where(s => s.State == "completed" || s.State == "failed").Select(s => Snapshot(s.Id)).ToList();

        /// <summary>Quests not yet started whose prerequisites pass now.</summary>
        public List<JsonObject> Available() => Defs
            .Where(q => GetState(Str(q, "id")).State == "inactive" && _host.Check(Get(q, "requires")).Ok)
            .Select(q => new JsonObject { ["id"] = q["id"], ["title"] = Get(q, "title"), ["type"] = Get(q, "type"), ["giver"] = Get(q, "giver"), ["summary"] = Get(q, "summary") })
            .ToList();

        /// <summary>A quest as the journal shows it.</summary>
        public JsonObject Snapshot(string id)
        {
            JsonObject def = Def(id);
            QuestRuntime st = GetState(id);
            JsonObject stage = StageOf(def, st.Stage);
            IReadOnlyDictionary<string, RpgLocation> locations = _host.Locations;
            var objectives = new List<object>();
            foreach (JsonObject o in stage != null ? Objs(stage["objectives"]) : new List<JsonObject>())
            {
                string oid = Str(o, "id");
                int i = st.IndexOf(oid);
                bool done = i >= 0 && st.Objectives[i].Done;
                double count = i >= 0 ? st.Objectives[i].Count : 0;
                string locKey = Str(o, "location");
                RpgLocation loc = locKey != null && locations.TryGetValue(locKey, out RpgLocation l) ? l : null;
                objectives.Add(new JsonObject
                {
                    ["id"] = oid, ["text"] = Get(o, "text"), ["optional"] = RpgJs.Truthy(Get(o, "optional")), ["done"] = done, ["count"] = count,
                    ["need"] = Get(o, "count") ?? 1.0, ["trigger"] = Str(o, "trigger") ?? "manual", ["locationKey"] = locKey, ["location"] = loc?.ToJson(),
                    ["visible"] = !RpgJs.Truthy(Get(o, "hidden")) || _host.Check(o["hidden"]).Ok,
                });
            }
            var outcomes = Get(def, "outcomes") as JsonObject;
            JsonObject outcome = st.Outcome != null ? (JsonObject)outcomes[st.Outcome] : null;
            List<JsonObject> stages = Objs(def["stages"]);
            return new JsonObject
            {
                ["id"] = def["id"], ["title"] = Get(def, "title"), ["type"] = Get(def, "type"), ["giver"] = Get(def, "giver"), ["summary"] = Get(def, "summary"),
                ["recommendedLevel"] = Get(def, "recommendedLevel") ?? 1.0, ["state"] = st.State, ["stageId"] = st.Stage, ["stageTitle"] = stage != null ? Get(stage, "title") : null,
                ["journal"] = stage != null ? Get(stage, "journal") : null, ["stageIndex"] = (double)(stage != null ? stages.FindIndex(s => Str(s, "id") == st.Stage) : -1),
                ["stageCount"] = (double)stages.Count, ["timeLeft"] = st.TimeLeft.HasValue ? (object)st.TimeLeft.Value : null, ["outcome"] = st.Outcome,
                ["outcomeTitle"] = outcome != null ? Get(outcome, "title") : null, ["outcomeSummary"] = outcome != null ? Get(outcome, "summary") : null,
                ["tracked"] = Tracked == Str(def, "id"), ["objectives"] = objectives,
            };
        }

        /// <summary>Every objective with a place, across the active quests, that is not yet done and is visible.</summary>
        public List<JsonObject> Markers()
        {
            var out_ = new List<JsonObject>();
            foreach (JsonObject snap in Active())
            {
                foreach (JsonObject o in ((List<object>)snap["objectives"]).Cast<JsonObject>())
                {
                    if (o["location"] is JsonObject loc && !(bool)o["done"] && (bool)o["visible"])
                    {
                        var m = new JsonObject { ["questId"] = snap["id"], ["questTitle"] = snap["title"], ["objectiveId"] = o["id"], ["text"] = o["text"] };
                        foreach (KeyValuePair<string, object> kv in loc) m[kv.Key] = kv.Value;
                        m["tracked"] = snap["tracked"];
                        out_.Add(m);
                    }
                }
            }
            return out_;
        }

        // ------------------------------------------------------------ writes

        public RpgResult Start(string id, bool force = false)
        {
            JsonObject def = Def(id);
            QuestRuntime st = GetState(id);
            if (st.State != "inactive") return RpgResult.Fail("already-" + st.State);
            if (!force)
            {
                (bool ok, List<string> missing) = _host.Check(Get(def, "requires"));
                if (!ok) return new RpgResult { Ok = false, Reason = "requirements", Missing = missing };
            }
            if (!_states.ContainsKey(id)) _order.Add(id);
            _states[id] = new QuestRuntime { Id = id, State = "active" };
            if (Tracked == null) SetTracked(id);
            _host.Emit("rpg:questStarted", new JsonObject { ["quest"] = Snapshot(id) });
            EnterStage(id, Str(Objs(def["stages"])[0], "id"));
            return new RpgResult { Ok = true, Extra = new JsonObject { ["quest"] = Snapshot(id) } };
        }

        public string SetTracked(string id)
        {
            if (id != null) Def(id);
            Tracked = id;
            _host.Emit("rpg:questTracked", new JsonObject { ["questId"] = id });
            return id;
        }

        void EnterStage(string questId, string stageId)
        {
            JsonObject def = Def(questId);
            JsonObject stage = StageOf(def, stageId) ?? throw new ArgumentOutOfRangeException(nameof(stageId), $"Quest \"{questId}\" has no stage \"{stageId}\"");
            QuestRuntime st = _states[questId];
            st.Stage = stageId;
            st.Objectives = Objs(stage["objectives"]).Select(o => (Str(o, "id"), false, 0.0)).ToList();
            st.TimeLeft = Get(stage, "timeLimit") is double limit ? limit : (double?)null;
            _host.ApplyEffects(RpgJs.AsList(Get(stage, "onEnter")), new JsonObject { ["questId"] = questId, ["stageId"] = stageId });
            _host.Emit("rpg:questUpdated", new JsonObject { ["quest"] = Snapshot(questId), ["stage"] = stageId });
            // a stage whose objectives are already met (the item was already carried) must not stall
            Evaluate(questId);
        }

        /// <summary>Mark an objective done (a no-op result if it already is).</summary>
        public RpgResult CompleteObjective(string questId, string objectiveId, bool silent = false)
        {
            if (questId == null || !_states.TryGetValue(questId, out QuestRuntime st) || st.State != "active") return RpgResult.Fail("not-active");
            JsonObject stage = StageOf(Def(questId), st.Stage);
            JsonObject objDef = ObjectiveOf(stage, objectiveId);
            if (objDef == null) return RpgResult.Fail("no-such-objective");
            int i = st.IndexOf(objectiveId);
            if (st.Objectives[i].Done) return RpgResult.Fail("already-done");
            st.Objectives[i] = (objectiveId, true, Get(objDef, "count") is double c ? c : 1);
            _host.ApplyEffects(RpgJs.AsList(Get(objDef, "onComplete")), new JsonObject { ["questId"] = questId, ["objectiveId"] = objectiveId });
            if (!silent)
            {
                _host.Emit("rpg:objectiveCompleted", new JsonObject
                {
                    ["questId"] = questId, ["objectiveId"] = objectiveId, ["text"] = Get(objDef, "text"), ["optional"] = RpgJs.Truthy(Get(objDef, "optional")),
                    ["quest"] = Snapshot(questId),
                });
            }
            Evaluate(questId);
            return RpgResult.Success();
        }

        /// <summary>Count towards an objective (kills, pickups).</summary>
        public RpgResult AdvanceObjective(string questId, string objectiveId, double amount = 1)
        {
            if (questId == null || !_states.TryGetValue(questId, out QuestRuntime st) || st.State != "active") return RpgResult.Fail("not-active");
            JsonObject stage = StageOf(Def(questId), st.Stage);
            JsonObject objDef = ObjectiveOf(stage, objectiveId);
            if (objDef == null) return RpgResult.Fail("no-such-objective");
            int i = st.IndexOf(objectiveId);
            if (st.Objectives[i].Done) return RpgResult.Fail("already-done");
            double count = st.Objectives[i].Count + amount;
            st.Objectives[i] = (objectiveId, false, count);
            double need = Get(objDef, "count") is double n ? n : 1;
            if (count >= need) return CompleteObjective(questId, objectiveId);
            _host.Emit("rpg:questUpdated", new JsonObject { ["quest"] = Snapshot(questId), ["stage"] = st.Stage, ["objective"] = objectiveId });
            return new RpgResult { Ok = true, Extra = new JsonObject { ["count"] = count, ["need"] = need } };
        }

        /// <summary>Re-check the state-driven objectives (item, flag) and resolve the stage when it is done.</summary>
        public void Evaluate(string questId)
        {
            if (questId == null || !_states.TryGetValue(questId, out QuestRuntime st) || st.State != "active") return;
            JsonObject stage = StageOf(Def(questId), st.Stage);
            if (stage == null) return;

            foreach (JsonObject o in Objs(stage["objectives"]))
            {
                string oid = Str(o, "id");
                if (st.IsDone(oid)) continue;
                string trigger = Str(o, "trigger");
                if (trigger == "item")
                {
                    bool satisfied = ((JsonObject)o["item"]).All(kv => _host.CountItem(kv.Key) >= (double)kv.Value);
                    if (satisfied)
                    {
                        CompleteObjective(questId, oid);
                        return;
                    }
                }
                else if (trigger == "flag")
                {
                    if (RpgJs.Truthy(_host.GetFlag(Str(o, "flag"))))
                    {
                        CompleteObjective(questId, oid);
                        return;
                    }
                }
            }

            bool allDone = Objs(stage["objectives"]).Where(o => !RpgJs.Truthy(Get(o, "optional"))).All(o => st.IsDone(Str(o, "id")));
            if (!allDone) return;
            ResolveStage(questId, stage);
        }

        void ResolveStage(string questId, JsonObject stage)
        {
            foreach (JsonObject b in Objs(Get(stage, "branches")))
            {
                if (_host.Check(Get(b, "when")).Ok)
                {
                    EnterStage(questId, Str(b, "to"));
                    return;
                }
            }
            string complete = Str(stage, "complete"), next = Str(stage, "next");
            if (!string.IsNullOrEmpty(complete))
            {
                Complete(questId, complete);
                return;
            }
            if (!string.IsNullOrEmpty(next))
            {
                EnterStage(questId, next);
                return;
            }
            throw new InvalidOperationException($"Quest \"{questId}\" stage \"{Str(stage, "id")}\" resolved with nowhere to go");
        }

        public RpgResult Complete(string questId, string outcomeId)
        {
            JsonObject def = Def(questId);
            if (!_states.TryGetValue(questId, out QuestRuntime st) || st.State != "active") return RpgResult.Fail("not-active");
            var outcomes = (JsonObject)def["outcomes"];
            if (outcomeId == null || !outcomes.TryGetValue(outcomeId, out object oo)) throw new ArgumentOutOfRangeException(nameof(outcomeId), $"Quest \"{questId}\" has no outcome \"{outcomeId}\"");
            var outcome = (JsonObject)oo;
            bool failed = RpgJs.Truthy(Get(outcome, "failed"));
            st.State = failed ? "failed" : "completed";
            st.Outcome = outcomeId;
            st.TimeLeft = null;
            JsonObject granted = _host.GrantRewards(Get(outcome, "rewards") as JsonObject ?? new JsonObject(), new JsonObject { ["questId"] = questId, ["outcomeId"] = outcomeId });
            if (Tracked == questId)
            {
                QuestRuntime nextTracked = StatesInOrder().FirstOrDefault(s => s.State == "active");
                SetTracked(nextTracked?.Id);
            }
            JsonObject snapshot = Snapshot(questId);
            _host.Emit(failed ? "rpg:questFailed" : "rpg:questCompleted", new JsonObject
            {
                ["quest"] = snapshot, ["outcome"] = outcomeId, ["outcomeTitle"] = Get(outcome, "title"), ["summary"] = Get(outcome, "summary"), ["rewards"] = granted,
            });
            return new RpgResult { Ok = true, Extra = new JsonObject { ["outcome"] = outcomeId, ["rewards"] = granted, ["quest"] = snapshot } };
        }

        public RpgResult Fail(string questId, string outcomeId)
        {
            JsonObject def = Def(questId);
            if (outcomeId == null || !((JsonObject)def["outcomes"]).ContainsKey(outcomeId)) throw new ArgumentOutOfRangeException(nameof(outcomeId), $"Quest \"{questId}\" has no outcome \"{outcomeId}\"");
            return Complete(questId, outcomeId);
        }

        // ------------------------------------------------------------ triggers

        /// <summary>Visit the open objectives of every active quest; the visitor returns true to stop with that quest.</summary>
        void ForEachActiveObjective(Func<string, JsonObject, bool> fn)
        {
            foreach (QuestRuntime st in StatesInOrder())
            {
                if (st.State != "active") continue;
                JsonObject stage = StageOf(Def(st.Id), st.Stage);
                if (stage == null) continue;
                foreach (JsonObject o in Objs(stage["objectives"]))
                {
                    if (st.IsDone(Str(o, "id"))) continue;
                    if (fn(st.Id, o)) break;
                }
            }
        }

        public void NotifyTalk(string dialogueId) => ForEachActiveObjective((questId, o) =>
        {
            if (Str(o, "trigger") == "talk" && Str(o, "dialogue") == dialogueId)
            {
                CompleteObjective(questId, Str(o, "id"));
                return true;
            }
            return false;
        });

        public void NotifyHack(string target) => ForEachActiveObjective((questId, o) =>
        {
            if (Str(o, "trigger") == "hack" && Str(o, "target") == target)
            {
                CompleteObjective(questId, Str(o, "id"));
                return true;
            }
            return false;
        });

        public void NotifyKill(string tag, double amount = 1) => ForEachActiveObjective((questId, o) =>
        {
            if (Str(o, "trigger") == "kill" && Str(o, "tag") == tag)
            {
                AdvanceObjective(questId, Str(o, "id"), amount);
                return true;
            }
            return false;
        });

        /// <summary>The inventory or the flags changed: re-check every active quest.</summary>
        public void NotifyStateChanged()
        {
            foreach (QuestRuntime st in StatesInOrder())
            {
                if (st.State == "active") Evaluate(st.Id);
            }
        }

        /// <summary>Complete every open "reach" objective whose place the player stands in.</summary>
        public void CheckProximity(double x, double z)
        {
            IReadOnlyDictionary<string, RpgLocation> locations = _host.Locations;
            if (locations.Count == 0) return;
            var hits = new List<(string QuestId, string ObjectiveId, RpgLocation Loc)>();
            ForEachActiveObjective((questId, o) =>
            {
                if (Str(o, "trigger") != "reach") return false;
                if (!locations.TryGetValue(Str(o, "location") ?? "", out RpgLocation loc)) return false;
                double dx = loc.X - x, dz = loc.Z - z;
                if (dx * dx + dz * dz <= loc.Radius * loc.Radius) hits.Add((questId, Str(o, "id"), loc));
                return false;
            });
            foreach ((string questId, string objectiveId, RpgLocation loc) in hits)
            {
                _host.Emit("rpg:locationReached", new JsonObject { ["key"] = loc.Key, ["name"] = loc.Name, ["questId"] = questId });
                CompleteObjective(questId, objectiveId);
            }
        }

        /// <summary>The fixed step: runs the stages' time limits.</summary>
        public void Tick(double dt)
        {
            foreach (QuestRuntime st in StatesInOrder())
            {
                if (st.State != "active" || !st.TimeLeft.HasValue) continue;
                st.TimeLeft = Math.Max(0, st.TimeLeft.Value - dt);
                if (st.TimeLeft.Value > 0) continue;
                JsonObject def = Def(st.Id);
                JsonObject stage = StageOf(def, st.Stage);
                st.TimeLeft = null;
                var timeout = Get(stage, "onTimeout") as JsonObject;
                string target = timeout != null ? Str(timeout, "fail") ?? Str(timeout, "to") : null;
                if (string.IsNullOrEmpty(target)) continue;
                if (((JsonObject)def["outcomes"]).ContainsKey(target)) Complete(st.Id, target);
                else EnterStage(st.Id, target);
            }
        }

        // ------------------------------------------------------------ saves

        public JsonObject Serialize() => new JsonObject
        {
            ["tracked"] = Tracked,
            ["states"] = StatesInOrder().Select(s => (object)new JsonObject
            {
                ["id"] = s.Id, ["state"] = s.State, ["stage"] = s.Stage, ["outcome"] = s.Outcome, ["timeLeft"] = s.TimeLeft.HasValue ? (object)s.TimeLeft.Value : null,
                ["objectives"] = s.ObjectivesJson(),
            }).ToList(),
        };

        public QuestLog Deserialize(JsonObject data)
        {
            _states.Clear();
            _order.Clear();
            Tracked = null;
            if (data == null) return this;
            foreach (JsonObject s in Objs(Get(data, "states")))
            {
                string id = Str(s, "id");
                Def(id);   // a save naming a quest we no longer ship is corrupt, not fine
                string state = Str(s, "state");
                if (!States.Contains(state)) throw new InvalidOperationException($"Save has unknown quest state \"{state}\"");
                var rt = new QuestRuntime
                {
                    Id = id, State = state, Stage = Str(s, "stage"), Outcome = Str(s, "outcome"), TimeLeft = Get(s, "timeLeft") is double t ? t : (double?)null,
                };
                if (Get(s, "objectives") is JsonObject objs)
                {
                    foreach (KeyValuePair<string, object> kv in objs)
                    {
                        var v = kv.Value as JsonObject ?? new JsonObject();
                        rt.Objectives.Add((kv.Key, RpgJs.Truthy(Get(v, "done")), Get(v, "count") is double c ? c : 0));
                    }
                }
                if (!_states.ContainsKey(id)) _order.Add(id);
                _states[id] = rt;
            }
            string tracked = Str(data, "tracked");
            if (!string.IsNullOrEmpty(tracked))
            {
                Def(tracked);
                Tracked = tracked;
            }
            return this;
        }
    }
}
