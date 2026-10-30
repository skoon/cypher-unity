using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// The RPG core one behaviour at a time: the prototype's own tests (<c>tests/rpg.test.mjs</c>) for jobs, conversations, breaches, quickhacks, vendors, places,
    /// saves and the core as a whole, ported.
    /// </summary>
    public class RpgWorldTests
    {
        sealed class Core
        {
            public readonly RpgCore C;
            public readonly List<(string Type, JsonObject Payload)> Events = new List<(string, JsonObject)>();

            public Core(uint seed = 20771)
            {
                C = new RpgCore((t, p) => Events.Add((t, p)), seed, new MemorySaveStorage(), () => 1700000000000) { AutosaveEnabled = false };
            }

            public int Count(string type) => Events.Count(e => e.Type == type);
        }

        /// <summary>The test city of the prototype's tests: a 7 x 7 grid, the plaza in the middle, one building per block, taller to the south-east.</summary>
        static RpgCityMeta FakeCity(int grid = 7, double block = 46, double road = 16, double heightBias = 0)
        {
            double cell = block + road, half = grid * cell / 2;
            int mid = grid >> 1;
            var meta = new RpgCityMeta { BlockSize = block, Cell = cell, Grid = grid };
            for (int gz = 0; gz < grid; gz++)
            {
                for (int gx = 0; gx < grid; gx++)
                {
                    double cx = -half + gx * cell + cell / 2, cz = -half + gz * cell + cell / 2;
                    bool plaza = gx == mid && gz == mid;
                    meta.Streets.Add((gx, gz, cx, cz, plaza));
                    if (!plaza) meta.Buildings.Add((cx, cz, 40, 40, 20 + gx * 3 + gz * 2 + heightBias));
                }
            }
            return meta;
        }

        static void Boost(RpgCore core, string attr, int to)
        {
            while (core.Stats.Attributes[attr] < to)
            {
                core.Stats.AttributePoints += 1;
                Assert.IsTrue(core.SpendAttributePoint(attr).Ok, $"could not raise {attr}");
            }
        }

        static JsonObject Obj(string json) => (JsonObject)Json.Parse(json);

        // ---------------------------------------------------------------- the core and the stats

        [Test]
        public void PerksChromeAndEquippedGearAllFeedTheSameDerivedStats()
        {
            var core = new Core().C;
            double b = core.Derived["maxHealth"];
            core.BuyPerk("bulk");
            Assert.AreEqual(b + 25, core.Derived["maxHealth"], "Bulk rank 1 is +25 health");

            Boost(core, "body", 6);
            core.InstallCyberware("cw_subdermal");
            Assert.Greater(core.Derived["armor"], 4 + 4 * 2.2, "subdermal armour raises armour");

            Item jacket = RpgItems.MakeItem(core.Gen.Next, "a_jacket", "rare", 5, "test_jacket");
            jacket.Mods = new List<StatModifier> { StatModifier.Add("maxHealth", 40) };
            core.AddItem(jacket);
            double before = core.Derived["maxHealth"];
            core.EquipItem("test_jacket");
            Assert.AreEqual(before + 40, core.Derived["maxHealth"]);
            core.UnequipSlot("torso");
            Assert.AreEqual(before, core.Derived["maxHealth"]);
        }

        [Test]
        public void StatsChangedFiresWhenDerivedStatsMove()
        {
            var core = new Core();
            int before = core.Count("rpg:statsChanged");
            core.C.BuyPerk("bulk");
            Assert.Greater(core.Count("rpg:statsChanged"), before);
        }

        [Test]
        public void RespecThroughTheCoreRefundsAndRestoresTheBaseStats()
        {
            var core = new Core().C;
            core.Stats.PerkPoints = 6;
            core.BuyPerk("bulk");
            core.BuyPerk("bulk");
            int before = core.Stats.PerkPoints;
            Assert.AreEqual(2, core.RespecPerks());
            Assert.AreEqual(before + 2, core.Stats.PerkPoints);
            Assert.AreEqual(0, core.Perks.RankOf("bulk"));
            Assert.AreEqual(RpgStats.BaseDerived(core.Stats.Attributes, 1)["maxHealth"], core.Derived["maxHealth"]);
        }

        [Test]
        public void InstallingThenRemovingChromeIsSymmetricAndTheChromeComesBack()
        {
            var core = new Core().C;
            string before = Json.Write(core.Derived.ToJson(), false);
            core.InstallCyberware("cw_kiroshi_mk1");
            Assert.AreNotEqual(before, Json.Write(core.Derived.ToJson(), false));
            Assert.AreEqual(RpgCyberware.Get("cw_kiroshi_mk1").Capacity, core.Cyber.UsedCapacity());
            core.RemoveCyberware("cw_kiroshi_mk1");
            Assert.AreEqual(before, Json.Write(core.Derived.ToJson(), false));
            Assert.AreEqual(0, core.Cyber.UsedCapacity());
            Assert.IsTrue(core.Inventory.Items.Any(i => i.CyberwareId == "cw_kiroshi_mk1"), "the removed chrome comes back as an item");
        }

        [Test]
        public void ConditionsReadTheCoresAttributesFlagsItemsMoneyAndQuests()
        {
            var core = new Core().C;
            Boost(core, "intelligence", 7);
            core.SetFlag("kaz_honest", true);
            core.GiveItem("j_scrap", qty: 2);
            Assert.IsTrue(core.Check(Obj("{\"attribute\":{\"intelligence\":6}}")).Ok);
            Assert.IsFalse(core.Check(Obj("{\"attribute\":{\"intelligence\":12}}")).Ok);
            Assert.IsTrue(core.Check(Obj("{\"flag\":\"kaz_honest\"}")).Ok);
            Assert.IsFalse(core.Check(Obj("{\"notFlag\":\"kaz_honest\"}")).Ok);
            Assert.IsTrue(core.Check(Obj("{\"item\":{\"j_scrap\":2}}")).Ok);
            Assert.IsFalse(core.Check(Obj("{\"item\":{\"j_scrap\":9}}")).Ok);
            Assert.IsFalse(core.Check(Obj("{\"money\":999999}")).Ok);
            Assert.IsTrue(core.Check(Obj("{\"quest\":{\"id\":\"q_main_ghost\",\"state\":\"active\"}}")).Ok);
            Assert.IsTrue(core.Check(Obj("{\"any\":[{\"level\":99},{\"flag\":\"kaz_honest\"}]}")).Ok);
            Assert.IsTrue(core.Check(Obj("{\"not\":{\"level\":99}}")).Ok);
        }

        // ---------------------------------------------------------------- quests

        sealed class QuestHost : IQuestHost, IConditionView
        {
            public readonly List<(string Type, JsonObject Payload)> Emitted = new List<(string, JsonObject)>();
            public readonly List<JsonObject> Rewards = new List<JsonObject>();
            public readonly Dictionary<string, object> Flags = new Dictionary<string, object>();
            public QuestLog Log;

            public void Emit(string type, JsonObject payload) => Emitted.Add((type, payload));
            public void ApplyEffects(List<object> effects, JsonObject context) { }
            public JsonObject GrantRewards(JsonObject rewards, JsonObject meta)
            {
                Rewards.Add(rewards);
                return rewards;
            }
            public (bool Ok, List<string> Missing) Check(object condition) => RpgConditions.Check(condition, this);
            public IReadOnlyDictionary<string, RpgLocation> Locations { get; } = new Dictionary<string, RpgLocation>();
            public int CountItem(string templateId) => 0;
            public object GetFlag(string flag) => Flags.TryGetValue(flag, out object v) ? v : null;
            public int Level => 1;
            public int StreetCred => 1;
            public double Money => 0;
            public int Attribute(string name) => 4;
            public bool HasPerk(string id, int rank) => false;
            public bool HasCyberware(string id) => false;
            public bool HasAbility(string tag) => false;
            public object Flag(string name) => GetFlag(name);
            QuestStateView IConditionView.QuestState(string id)
            {
                QuestRuntime st = Log.GetState(id);
                return new QuestStateView(st.State, st.Stage, st.Outcome);
            }
        }

        const string TestQuest = @"{
            ""id"": ""tq"", ""title"": ""Test Job"", ""type"": ""gig"", ""giver"": ""Nobody"", ""summary"": ""A test."", ""requires"": { ""level"": 1 },
            ""stages"": [
                { ""id"": ""s1"", ""title"": ""One"", ""journal"": ""..."",
                  ""objectives"": [ { ""id"": ""o_main"", ""text"": ""Do the thing"", ""trigger"": ""manual"" },
                                    { ""id"": ""o_opt"", ""text"": ""Optional extra"", ""optional"": true, ""trigger"": ""manual"" } ],
                  ""next"": ""s2"" },
                { ""id"": ""s2"", ""title"": ""Two"", ""journal"": ""..."",
                  ""objectives"": [ { ""id"": ""o_flag"", ""text"": ""Wait for the flag"", ""trigger"": ""flag"", ""flag"": ""tq_flag"" } ],
                  ""branches"": [ { ""when"": { ""flag"": ""tq_good"" }, ""to"": ""s_good"" } ],
                  ""complete"": ""bad"" },
                { ""id"": ""s_good"", ""title"": ""Good"", ""journal"": ""..."", ""objectives"": [ { ""id"": ""o_g"", ""text"": ""Finish well"", ""trigger"": ""manual"" } ], ""complete"": ""good"" }
            ],
            ""outcomes"": {
                ""good"": { ""title"": ""Good end"", ""summary"": ""..."", ""rewards"": { ""xp"": 100 } },
                ""bad"": { ""title"": ""Bad end"", ""summary"": ""..."", ""rewards"": { ""xp"": 10 } }
            }
        }";

        static (QuestHost Host, QuestLog Log) NewLog(string questJson = TestQuest)
        {
            var host = new QuestHost();
            var defs = new List<JsonObject> { Obj(questJson) };
            QuestLog.Validate(defs);
            var log = new QuestLog(host, defs);
            host.Log = log;
            return (host, log);
        }

        [Test]
        public void AStageAdvancesOnlyWhenEveryRequiredObjectiveIsDone()
        {
            (QuestHost _, QuestLog log) = NewLog();
            Assert.IsTrue(log.Start("tq").Ok);
            Assert.AreEqual("s1", log.GetState("tq").Stage);
            log.CompleteObjective("tq", "o_opt");
            Assert.AreEqual("s1", log.GetState("tq").Stage, "an optional objective does not advance the stage");
            log.CompleteObjective("tq", "o_main");
            Assert.AreEqual("s2", log.GetState("tq").Stage);
        }

        [Test]
        public void FlagObjectivesResolveWhenTheStateChanges()
        {
            (QuestHost host, QuestLog log) = NewLog();
            log.Start("tq");
            log.CompleteObjective("tq", "o_main");
            host.Flags["tq_flag"] = true;
            log.NotifyStateChanged();
            Assert.AreEqual("completed", log.GetState("tq").State);
            Assert.AreEqual("bad", log.GetState("tq").Outcome, "no branch flag: the default outcome");
        }

        [Test]
        public void BranchesPickTheFirstPassingCondition()
        {
            (QuestHost host, QuestLog log) = NewLog();
            host.Flags["tq_good"] = true;
            log.Start("tq");
            log.CompleteObjective("tq", "o_main");
            host.Flags["tq_flag"] = true;
            log.NotifyStateChanged();
            Assert.AreEqual("s_good", log.GetState("tq").Stage);
            log.CompleteObjective("tq", "o_g");
            Assert.AreEqual("good", log.GetState("tq").Outcome);
            Assert.AreEqual(100.0, host.Rewards.Last()["xp"]);
        }

        [Test]
        public void AStageTimeLimitEndsTheQuestInItsTimeoutOutcome()
        {
            JsonObject timed = Obj(TestQuest);
            timed["id"] = "tq_timed";
            timed["stages"] = new List<object>
            {
                Obj("{\"id\":\"s1\",\"title\":\"Run\",\"journal\":\"...\",\"timeLimit\":5,\"onTimeout\":{\"fail\":\"bad\"},\"objectives\":[{\"id\":\"o\",\"text\":\"Be quick\",\"trigger\":\"manual\"}],\"complete\":\"good\"}"),
            };
            (QuestHost _, QuestLog log) = NewLog(Json.Write(timed, false));
            log.Start("tq_timed");
            log.Tick(3);
            Assert.AreEqual("active", log.GetState("tq_timed").State);
            log.Tick(3);
            Assert.AreEqual("completed", log.GetState("tq_timed").State, "the bad outcome is not marked failed");
            Assert.AreEqual("bad", log.GetState("tq_timed").Outcome);
        }

        [Test]
        public void QuestPrerequisitesGateAvailability()
        {
            var core = new Core().C;
            Assert.IsFalse(core.Quests.Available().Any(q => (string)q["id"] == "q_gig_ledger"), "the ledger gig needs Chrome Tax finished first");
            RpgResult res = core.StartQuest("q_gig_ledger");
            Assert.IsFalse(res.Ok);
            Assert.AreEqual("requirements", res.Reason);
            Assert.IsTrue(res.Missing.Any(m => m.Contains("q_side_chrometax")));
        }

        [Test]
        public void TheMainQuestWalksItsWholeCorpoBranch()
        {
            var core = new Core().C;
            core.SetCity(FakeCity());
            JsonObject Q() => core.GetQuest("q_main_ghost");
            Assert.AreEqual("active", Q()["state"]);
            Assert.AreEqual("s_meet", Q()["stageId"]);
            core.CompleteObjective("q_main_ghost", "o_find_kaz");
            core.CompleteObjective("q_main_ghost", "o_talk_kaz");
            Assert.AreEqual("s_drop", Q()["stageId"]);
            core.CompleteObjective("q_main_ghost", "o_drop");
            Assert.AreEqual(1, core.Inventory.Count("q_ghost_shard"), "reaching the drop gives the shard, which meets the item objective");
            Assert.AreEqual("s_datacore", Q()["stageId"]);
            core.CompleteObjective("q_main_ghost", "o_reach_core");
            core.CompleteObjective("q_main_ghost", "o_breach");
            Assert.AreEqual("s_wire", Q()["stageId"]);
            core.SetFlag("ghost_sold", true);
            core.CompleteObjective("q_main_ghost", "o_talk_wire");
            Assert.AreEqual("s_end_corpo", Q()["stageId"]);
            int money = core.Economy.Money;
            core.CompleteObjective("q_main_ghost", "o_spire");
            Assert.AreEqual("completed", Q()["state"]);
            Assert.AreEqual("corpo", Q()["outcome"]);
            Assert.AreEqual(money + 9000, core.Economy.Money);
            Assert.AreEqual(true, core.Flag("sector_corpo_favour"));
        }

        [Test]
        public void ObjectivesHaveRealWorldCoordinatesFromTheCity()
        {
            var core = new Core().C;
            core.SetCity(FakeCity());
            var markers = (List<object>)core.GetQuests()["markers"];
            Assert.IsNotEmpty(markers);
            foreach (JsonObject m in markers.Cast<JsonObject>())
            {
                Assert.IsFalse(double.IsNaN((double)m["x"]) || double.IsInfinity((double)m["x"]));
                Assert.IsFalse(double.IsNaN((double)m["z"]) || double.IsInfinity((double)m["z"]));
            }
        }

        [Test]
        public void WalkingIntoAnObjectivesPlaceCompletesIt()
        {
            var core = new Core();
            core.C.SetCity(FakeCity());
            RpgLocation noodle = core.C.GetLocation("noodle");
            core.C.SetPlayerPosition(noodle.X, 0, noodle.Z);
            core.C.Tick(1 / 60.0);
            var objective = ((List<object>)core.C.GetQuest("q_main_ghost")["objectives"]).Cast<JsonObject>().First(o => (string)o["id"] == "o_find_kaz");
            Assert.AreEqual(true, objective["done"]);
            Assert.GreaterOrEqual(core.Count("rpg:locationReached"), 1);
        }

        [Test]
        public void TheQuestLogSavesAndRestoresMidQuest()
        {
            (QuestHost _, QuestLog log) = NewLog();
            log.Start("tq");
            log.CompleteObjective("tq", "o_opt");
            var data = (JsonObject)Json.Parse(Json.Write(log.Serialize(), false));
            (QuestHost _, QuestLog log2) = NewLog();
            log2.Deserialize(data);
            QuestRuntime st = log2.GetState("tq");
            Assert.AreEqual("s1", st.Stage);
            Assert.IsTrue(st.IsDone("o_opt"));
            Assert.IsFalse(st.IsDone("o_main"));
        }

        [Test]
        public void ASaveNamingAnUnknownQuestIsAHardError()
        {
            (QuestHost _, QuestLog log) = NewLog();
            Assert.Throws<ArgumentOutOfRangeException>(() => log.Deserialize(Obj("{\"states\":[{\"id\":\"q_deleted\",\"state\":\"active\"}]}")));
        }

        [Test]
        public void TheShippedQuestsHaveMainSideAndGigContentWithRealWriting()
        {
            var types = new HashSet<string>(QuestLog.Shipped.Select(q => (string)q["type"]));
            Assert.IsTrue(types.SetEquals(new[] { "main", "side", "gig" }));
            Assert.GreaterOrEqual(QuestLog.Shipped.Count, 4);
            foreach (JsonObject q in QuestLog.Shipped)
            {
                Assert.Greater(((string)q["summary"]).Length, 60, $"{q["id"]} needs a real summary");
                foreach (JsonObject s in ((List<object>)q["stages"]).Cast<JsonObject>()) Assert.Greater(((string)s["journal"]).Length, 20, $"{q["id"]}.{s["id"]} needs a journal entry");
            }
        }

        [Test]
        public void ABrokenQuestTableIsRefusedWhenItIsRead()
        {
            JsonObject bad = Obj(TestQuest);
            ((JsonObject)((List<object>)bad["stages"])[0])["next"] = "s_missing";
            Assert.Throws<InvalidOperationException>(() => QuestLog.Validate(new[] { bad }));
            JsonObject badTrigger = Obj(TestQuest);
            ((JsonObject)((List<object>)((JsonObject)((List<object>)badTrigger["stages"])[0])["objectives"])[0])["trigger"] = "teleport";
            Assert.Throws<InvalidOperationException>(() => QuestLog.Validate(new[] { badTrigger }));
        }

        // ---------------------------------------------------------------- dialogue

        static JsonObject Option(RpgCore core, string id) => ((List<object>)core.GetDialogueNode()["options"]).Cast<JsonObject>().FirstOrDefault(o => (string)o["id"] == id);

        [Test]
        public void GatedOptionsAreShownButLockedUntilTheStatIsMet()
        {
            var core = new Core().C;
            core.StartDialogue("dlg_kaz");
            Assert.AreEqual(true, Option(core, "k_o_read")["locked"]);
            Assert.AreEqual("INTELLIGENCE 6", Option(core, "k_o_read")["gateLabel"]);
            StringAssert.StartsWith("[INTELLIGENCE 6]", (string)Option(core, "k_o_read")["label"]);
            RpgResult refused = core.ChooseDialogueOption("k_o_read");
            Assert.IsFalse(refused.Ok);
            Assert.AreEqual("locked", refused.Reason);

            core.EndDialogue();
            Boost(core, "intelligence", 6);
            core.StartDialogue("dlg_kaz");
            Assert.AreEqual(false, Option(core, "k_o_read")["locked"]);
            Assert.IsTrue(core.ChooseDialogueOption("k_o_read").Ok);
            Assert.AreEqual(true, core.Flag("kaz_honest"), "the node's effects ran on entry");
        }

        [Test]
        public void ConditionalOptionsAreHiddenNotLocked()
        {
            var core = new Core().C;
            core.SetFlag("kaz_job_taken", true);
            core.StartDialogue("dlg_kaz");
            Assert.AreEqual("k_already", core.GetDialogueNode()["nodeId"], "the entries pick the opening node");
            Assert.IsNull(Option(core, "k_o_more"));
            core.EndDialogue();
            core.SetFlag("kaz_honest", true);
            core.StartDialogue("dlg_kaz");
            Assert.IsNotNull(Option(core, "k_o_more"));
        }

        [Test]
        public void OnceOptionsBurnAfterUse()
        {
            var core = new Core().C;
            core.SetFlag("kaz_job_taken", true);
            core.SetFlag("kaz_honest", true);
            core.StartDialogue("dlg_kaz");
            core.ChooseDialogueOption("k_o_more");
            core.EndDialogue();
            core.StartDialogue("dlg_kaz");
            Assert.IsNull(Option(core, "k_o_more"));
        }

        [Test]
        public void DialogueEffectsMoveMoneyXpFlagsAndQuests()
        {
            var core = new Core();
            int money = core.C.Economy.Money;
            Boost(core.C, "cool", 7);
            core.C.StartDialogue("dlg_kaz");
            core.C.ChooseDialogueOption("k_o_now");
            core.C.ChooseDialogueOption("k_o_deal");
            core.C.ChooseDialogueOption("k_o_haggle");
            Assert.AreEqual(money + 500, core.C.Economy.Money);
            core.C.ChooseDialogueOption("k_o_haggle_yes");
            Assert.AreEqual(true, core.C.Flag("kaz_job_taken"));
            Assert.GreaterOrEqual(core.Count("rpg:dialogueChoice"), 3);
        }

        [Test]
        public void EndingAConversationMeetsATalkObjective()
        {
            var core = new Core().C;
            core.SetCity(FakeCity());
            core.CompleteObjective("q_main_ghost", "o_find_kaz");
            core.StartDialogue("dlg_kaz");
            core.ChooseDialogueOption("k_o_leave");
            Assert.IsFalse(core.Dialogue.Active);
            Assert.AreEqual("s_drop", core.GetQuest("q_main_ghost")["stageId"], "the talk objective closed the stage");
        }

        [Test]
        public void AnUnknownDialogueThrowsAndASecondStartIsRefused()
        {
            var core = new Core().C;
            Assert.Throws<ArgumentOutOfRangeException>(() => core.StartDialogue("dlg_nope"));
            core.StartDialogue("dlg_vey");
            Assert.AreEqual("already-active", core.StartDialogue("dlg_kaz").Reason);
        }

        [Test]
        public void EveryShippedConversationHasRealWritingAndRealGates()
        {
            Assert.GreaterOrEqual(DialogueRunner.Shipped.Count, 3);
            int gated = 0;
            foreach (JsonObject g in DialogueRunner.Shipped)
            {
                var nodes = ((JsonObject)g["nodes"]).Select(kv => (JsonObject)kv.Value).ToList();
                Assert.GreaterOrEqual(nodes.Count, 6, $"{g["id"]} is too short to be a conversation");
                Assert.Greater(nodes.Sum(n => ((string)n["text"]).Length), 900, $"{g["id"]} has too little writing");
                foreach (JsonObject n in nodes)
                {
                    if (n.TryGetValue("options", out object opts)) gated += ((List<object>)opts).Cast<JsonObject>().Count(o => o.ContainsKey("gate"));
                }
            }
            Assert.GreaterOrEqual(gated, 8);
        }

        // ---------------------------------------------------------------- breach and quickhacks

        [Test]
        public void ContainsSequenceFindsContiguousRunsOnly()
        {
            Assert.IsTrue(RpgHacking.ContainsSequence(new[] { "1C", "55", "BD" }, new[] { "55", "BD" }));
            Assert.IsFalse(RpgHacking.ContainsSequence(new[] { "1C", "55", "BD" }, new[] { "1C", "BD" }));
            Assert.IsFalse(RpgHacking.ContainsSequence(new[] { "1C" }, new[] { "1C", "55" }));
        }

        [Test]
        public void EveryGeneratedBreachIsSolvedByWalkingItsOwnSolution()
        {
            for (uint seed = 1; seed <= 40; seed++)
            {
                BreachState state = RpgHacking.Generate(seed, 6, 7, 60, 2);
                var session = new BreachSession(state);
                int guard = 0;
                while (!session.Done && guard++ < 32)
                {
                    GridCell cell = state.SolutionPath[state.Buffer.Count];
                    Assert.IsTrue(session.LegalCells().Any(c => c.R == cell.R && c.C == cell.C), $"seed {seed} step {state.Buffer.Count}: the solution's cell is not legal");
                    session.Select(cell.R, cell.C);
                }
                BreachResult result = session.Result();
                Assert.IsTrue(result.Success, $"seed {seed} made an unsolvable matrix");
                Assert.IsEmpty(result.Missed, $"seed {seed} left daemons unsolved");
            }
        }

        [Test]
        public void BreachGenerationIsDeterministicAndUsesOnlyKnownCodes()
        {
            BreachState a = RpgHacking.Generate(555, 5, 6), b = RpgHacking.Generate(555, 5, 6);
            Assert.AreEqual(Json.Write(a.ToJson(), false), Json.Write(b.ToJson(), false));
            Assert.IsTrue(a.Matrix.All(row => row.All(c => RpgHacking.Codes.Contains(c))));
            Assert.Throws<ArgumentOutOfRangeException>(() => RpgHacking.Generate(1, 99));
        }

        [Test]
        public void SelectionAlternatesRowAndColumnAndNeverReusesACell()
        {
            BreachState state = RpgHacking.Generate(42, 5, 6, 99);
            var session = new BreachSession(state);
            Assert.AreEqual("row", state.Axis);
            Assert.IsTrue(session.LegalCells().All(c => c.R == 0), "the first pick is in row 0");
            var first = session.LegalCells()[0];
            session.Select(first.R, first.C);
            Assert.AreEqual("col", state.Axis);
            Assert.IsTrue(session.LegalCells().All(c => c.C == first.C), "the next is in that column");
            Assert.AreEqual("illegal-cell", session.Select(first.R, first.C).Reason, "a used cell cannot be picked again");
        }

        [Test]
        public void TheBreachTimerEndsTheSession()
        {
            BreachState state = RpgHacking.Generate(7, 5, 6, 4);
            var session = new BreachSession(state);
            session.Tick(2);
            Assert.IsFalse(session.Done);
            session.Tick(3);
            Assert.IsTrue(session.Done);
            Assert.AreEqual("timeout", state.Reason);
        }

        [Test]
        public void SolvingADaemonPaysOutThroughTheCore()
        {
            var core = new Core();
            core.C.SetCity(FakeCity());
            int money = core.C.Economy.Money;
            core.C.StartBreach("market_terminal");
            BreachState state = core.C.Breach.S;
            int guard = 0;
            while (!core.C.Breach.Done && guard++ < 32)
            {
                string want = state.Solution[state.Buffer.Count];
                var legal = core.C.Breach.LegalCells();
                var cell = legal.FirstOrDefault(c => c.Code == want);
                if (cell.Code == null) cell = legal[0];
                core.C.BreachSelect(cell.R, cell.C);
            }
            Assert.IsTrue(core.C.Breach.Result().Success);
            Assert.GreaterOrEqual(core.Count("rpg:daemonUploaded"), 1);
            Assert.IsTrue(core.C.Economy.Money > money || core.Count("rpg:lootDropped") >= 1);
            Assert.IsTrue(core.C.StartBreach("market_terminal").Ok, "a finished breach can be started again");
        }

        [Test]
        public void QuickhacksCostRamRefuseWhenBrokeAndExpireOnTheTick()
        {
            var core = new Core();
            RpgResult res = core.C.GiveItem("qh_overheat");
            core.C.EquipHack(res.Item.Uid, 0);
            double ram = core.C.Deck.Ram;
            RpgResult upload = core.C.UploadQuickhack(0, "npc_7");
            Assert.IsTrue(upload.Ok);
            Assert.AreEqual(ram - (double)upload.Extra["cost"], core.C.Deck.Ram);
            core.C.Deck.Ram = 0;
            Assert.AreEqual("no-ram", core.C.UploadQuickhack(0, "npc_7").Reason);
            Assert.AreEqual("empty-slot", core.C.UploadQuickhack(3, "npc_7").Reason);
            for (int i = 0; i < 60 * 30; i++) core.C.Tick(1 / 60.0);
            Assert.AreEqual(0, core.C.Deck.Active.Count);
            Assert.GreaterOrEqual(core.Count("rpg:quickhackExpired"), 1);
            Assert.Greater(core.C.Deck.Ram, 0, "RAM regenerates");
        }

        // ---------------------------------------------------------------- vendors

        static List<JsonObject> Stock(JsonObject snap) => ((List<object>)snap["stock"]).Cast<JsonObject>().ToList();

        [Test]
        public void VendorPricesRespondToCoolAndTechnical()
        {
            var poor = new Core().C;
            var smooth = new Core().C;
            Boost(smooth, "cool", 12);
            Boost(smooth, "technical", 10);
            JsonObject a = poor.GetVendor("v_marlene"), b = smooth.GetVendor("v_marlene");
            Assert.Greater((double)b["discount"], (double)a["discount"]);
            Assert.Less((double)Stock(b)[0]["price"], (double)Stock(a)[0]["price"], "and pays less for the same shelf");
        }

        [Test]
        public void RelationshipFlagsStackOnTheDerivedDiscount()
        {
            var core = new Core().C;
            double before = (double)core.GetVendor("v_marlene")["discount"];
            core.SetFlag("marlene_counter_prices", true);
            Assert.Greater((double)core.GetVendor("v_marlene")["discount"], before);
        }

        [Test]
        public void BuyingDebitsAddsTheItemAndRefusesWhenBroke()
        {
            var core = new Core().C;
            JsonObject cheapest = Stock(core.GetVendor("v_market")).OrderBy(s => (double)s["price"]).First();
            string uid = ((Item)cheapest["item"]).Uid;
            int money = core.Economy.Money;
            RpgResult res = core.BuyFrom("v_market", uid);
            Assert.IsTrue(res.Ok);
            Assert.AreEqual(money - res.Price, core.Economy.Money);
            Assert.IsNotNull(core.Inventory.Find(uid));
            core.Economy.Money = 0;
            string next = ((Item)Stock(core.GetVendor("v_market"))[0]["item"]).Uid;
            Assert.AreEqual("cannot-afford", core.BuyFrom("v_market", next).Reason);
            Assert.AreEqual("not-in-stock", core.BuyFrom("v_market", "ghost-uid").Reason);
        }

        [Test]
        public void SellingPaysTheBuyRateAndQuestItemsNeverSell()
        {
            var core = new Core().C;
            RpgResult junk = core.GiveItem("j_chipset");
            int money = core.Economy.Money;
            RpgResult res = core.SellTo("v_marlene", junk.Item.Uid);
            Assert.IsTrue(res.Ok);
            Assert.AreEqual(money + res.Price, core.Economy.Money);
            RpgResult quest = core.GiveItem("q_ghost_shard");
            Assert.AreEqual("quest-item", core.SellTo("v_marlene", quest.Item.Uid).Reason);
        }

        [Test]
        public void AVendorCanRefuseToTrade()
        {
            var core = new Core().C;
            core.SetFlag("marlene_enemy", true);
            RpgResult res = core.OpenVendor("v_marlene");
            Assert.IsFalse(res.Ok);
            Assert.AreEqual("refused", res.Reason);
        }

        [Test]
        public void VendorStockIsTheSameForTheSameRestock()
        {
            var a = new Core().C;
            var b = new Core().C;
            CollectionAssert.AreEqual(Stock(a.GetVendor("v_static")).Select(s => ((Item)s["item"]).Name), Stock(b.GetVendor("v_static")).Select(s => ((Item)s["item"]).Name));
        }

        [Test]
        public void MoneyCannotGoNegative()
        {
            RpgResult res = new Core().C.AddMoney(-99999, "test");
            Assert.IsFalse(res.Ok);
            Assert.AreEqual("insufficient-funds", res.Reason);
        }

        // ---------------------------------------------------------------- places

        [Test]
        public void PlacesComeFromTheCityNotFromCoordinates()
        {
            List<RpgLocation> a = RpgLocations.Build(FakeCity(), 1234), b = RpgLocations.Build(FakeCity(block: 60, road: 20), 1234);
            Assert.AreEqual(RpgLocations.Defs.Count, a.Count);
            CollectionAssert.AreNotEqual(a.Select(l => (l.X, l.Z)), b.Select(l => (l.X, l.Z)), "another city moves every place");
        }

        [Test]
        public void PlacesAreStableForASeedAndACity()
        {
            string A() => Json.Write(RpgLocations.Build(FakeCity(), 99).Select(l => (object)l.ToJson()).ToList(), false);
            Assert.AreEqual(A(), A());
        }

        [Test]
        public void TheSpireStandsNextToTheTallestBuilding()
        {
            RpgCityMeta city = FakeCity();
            var tallest = city.Buildings.Aggregate((best, x) => x.H > best.H ? x : best);
            RpgLocation spire = RpgLocations.Build(city, 5).First(l => l.Key == "spire");
            Assert.Less(Math.Abs(spire.Z - tallest.Z), 1);
            Assert.Less(Math.Abs(spire.X - tallest.X), tallest.W);
        }

        [Test]
        public void EveryLandmarkHasABlockOfItsOwn()
        {
            var locs = RpgLocations.Build(FakeCity(), 7).Where(l => l.Key != "plaza" && l.Key != "spire").ToList();
            Assert.AreEqual(locs.Count, locs.Select(l => $"{l.Block["cx"]},{l.Block["cz"]}").Distinct().Count());
        }

        [Test]
        public void BadCityMetadataThrows()
        {
            Assert.Throws<ArgumentException>(() => RpgLocations.Build(null));
            Assert.Throws<ArgumentException>(() => RpgLocations.Build(new RpgCityMeta { BlockSize = 1, Grid = 1, Cell = 1 }));
        }

        // ---------------------------------------------------------------- saves

        [Test]
        public void ASaveRoundTripsTheWholeGame()
        {
            var core = new Core().C;
            core.SetCity(FakeCity());
            Boost(core, "body", 7);
            core.Stats.PerkPoints += 4;
            core.BuyPerk("bulk");
            core.BuyPerk("bulk");
            core.InstallCyberware("cw_subdermal");
            core.GiveItem("c_maxdoc", qty: 4);
            RpgResult gun = core.GiveItem("w_rifle", "rare", 6);
            core.EquipItem(gun.Item.Uid);
            core.SetFlag("kaz_honest", true);
            core.AddXp(3000);
            core.AddCred(400);
            core.CompleteObjective("q_main_ghost", "o_find_kaz");
            core.Economy.AddMoney(-200, "test");

            JsonObject before = core.Serialize();
            var loaded = new Core().C;
            loaded.SetCity(FakeCity());
            Assert.AreEqual(RpgSave.Version, loaded.Saves.Write("slot1", before).Version);
            loaded.Deserialize(loaded.Saves.Read("slot1"));
            Assert.AreEqual(Json.Write(before, false), Json.Write(loaded.Serialize(), false));
            Assert.AreEqual(core.Stats.Level, loaded.Stats.Level);
            Assert.AreEqual(core.Derived["maxHealth"], loaded.Derived["maxHealth"]);
            Assert.AreEqual(2, loaded.Perks.RankOf("bulk"));
            Assert.IsTrue(loaded.Cyber.Has("cw_subdermal"));
            Assert.AreEqual(4, loaded.Inventory.Count("c_maxdoc"));
            Assert.AreEqual(gun.Item.Uid, loaded.Inventory.EquippedItem("primary").Uid);
            Assert.AreEqual(true, loaded.Flag("kaz_honest"));
            Assert.AreEqual(loaded.Economy.Money, core.Economy.Money);
        }

        [Test]
        public void LoadRestoresALiveCore()
        {
            var core = new Core();
            core.C.AddXp(5000);
            core.C.Save("manual");
            int level = core.C.Stats.Level;
            core.C.AddXp(90000);
            Assert.AreNotEqual(level, core.C.Stats.Level);
            Assert.IsTrue(core.C.Load("manual").Ok);
            Assert.AreEqual(level, core.C.Stats.Level);
            Assert.AreEqual("empty-slot", core.C.Load("empty-slot-name").Reason);
            Assert.GreaterOrEqual(core.Count("rpg:loaded"), 1);
            Assert.GreaterOrEqual(core.Count("rpg:saved"), 1);
        }

        [Test]
        public void DeserializeRefusesAnUnmigratedPayload()
        {
            var core = new Core().C;
            StringAssert.Contains("migrate first", Assert.Throws<InvalidOperationException>(() => core.Deserialize(new JsonObject { ["version"] = 1.0 })).Message);
            Assert.Throws<ArgumentException>(() => core.Deserialize(null));
        }

        [Test]
        public void TheCoreListsItsSavesAndDeletesThem()
        {
            var core = new Core().C;
            core.Save("a");
            core.Save("b");
            Assert.AreEqual(2, core.ListSaves().Count);
            Assert.IsTrue(core.ListSaves().All(s => s.Version == RpgSave.Version));
            core.DeleteSave("a");
            Assert.AreEqual(1, core.ListSaves().Count);
        }

        [Test]
        public void TheCoreAutosavesOnItsOwnClock()
        {
            var core = new Core();
            core.C.AutosaveEnabled = true;
            for (int i = 0; i < 60 * 119; i++) core.C.Tick(1 / 60.0);
            Assert.IsFalse(core.C.HasSave("auto"));
            for (int i = 0; i < 61; i++) core.C.Tick(1 / 60.0);
            Assert.IsTrue(core.C.HasSave("auto"), "an autosave every 120 s");
        }

        // ---------------------------------------------------------------- the core as a whole

        [Test]
        public void TheSnapshotHasEverythingTheHudPaintsFromNothing()
        {
            var core = new Core().C;
            core.SetCity(FakeCity());
            JsonObject snap = core.GetSnapshot();
            foreach (string key in new[] { "stats", "vitals", "inventory", "cyberware", "quests", "perkTrees", "flags", "locations", "money" }) Assert.IsTrue(snap.ContainsKey(key), key);
            Assert.AreEqual(4.0, ((JsonObject)((JsonObject)snap["stats"])["attributes"])["body"]);
            Assert.AreEqual(RpgStats.Attributes.Count, ((List<object>)snap["perkTrees"]).Count);
            Assert.AreEqual(RpgLocations.Defs.Count, ((List<object>)snap["locations"]).Count);
        }

        [Test]
        public void DamageIsMitigatedByArmourAndDeathIsAnnounced()
        {
            var core = new Core();
            double armour = core.C.Derived["armor"];
            core.C.Damage(100);
            Assert.AreEqual(core.C.Derived["maxHealth"] - 100 * (100 / (100 + armour)), core.C.Health, 0.001);
            core.C.Damage(99999);
            Assert.AreEqual(0, core.C.Health);
            Assert.AreEqual(1, core.Count("rpg:playerDied"));
            Assert.Throws<ArgumentOutOfRangeException>(() => core.C.Damage(-5));
        }

        [Test]
        public void TheTickRegeneratesVitalsWithoutPassingTheMaximum()
        {
            var core = new Core().C;
            core.Damage(50);
            core.Stamina = 0;
            for (int i = 0; i < 60 * 120; i++) core.Tick(1 / 60.0);
            Assert.AreEqual(core.Derived["maxHealth"], core.Health);
            Assert.AreEqual(core.Derived["maxStamina"], core.Stamina);
            Assert.Throws<ArgumentOutOfRangeException>(() => core.Tick(0));
        }

        [Test]
        public void EffectListsAreCheckedBeforeTheyRun()
        {
            var core = new Core().C;
            Assert.Throws<ArgumentOutOfRangeException>(() => core.ApplyEffects(new List<object> { Obj("{\"type\":\"nonsense\"}") }, null));
            Assert.Throws<ArgumentException>(() => core.ApplyEffects(new List<object> { Obj("{\"type\":\"setFlag\"}") }, null));
            core.ApplyEffects(new List<object> { Obj("{\"type\":\"setFlag\",\"flag\":\"ok\",\"value\":true}") }, null);
            Assert.AreEqual(true, core.Flag("ok"));
        }

        [Test]
        public void InteractionReportsTheNearestThingWorthPressingEOn()
        {
            var core = new Core().C;
            core.SetCity(FakeCity());
            RpgLocation pawn = core.GetLocation("pawn");
            core.SetPlayerPosition(pawn.X, 0, pawn.Z);
            JsonObject near = core.GetNearbyInteractable();
            Assert.AreEqual("pawn", near["locationKey"]);
            Assert.AreEqual("dialogue", near["kind"]);
            Assert.AreEqual("dlg_marlene", near["id"]);
            core.SetPlayerPosition(9999, 0, 9999);
            Assert.IsNull(core.GetNearbyInteractable());
            Assert.AreEqual("nothing-nearby", core.Interact().Reason);
        }

        [Test]
        public void ABreachAJobIsWaitingOnComesBeforeThePlacesConversation()
        {
            var core = new Core().C;
            core.SetCity(FakeCity());
            RpgLocation datacore = core.GetLocation("datacore");
            core.SetPlayerPosition(datacore.X, 0, datacore.Z);
            Assert.AreEqual("dlg_wire", core.GetNearbyInteractable()["id"], "with no job waiting, the core is Wire's");

            if (!core.Quests.IsActive("q_main_ghost")) Assert.IsTrue(core.StartQuest("q_main_ghost").Ok);
            foreach (string o in new[] { "o_find_kaz", "o_talk_kaz", "o_drop", "o_shard" }) core.CompleteObjective("q_main_ghost", o);
            Assert.AreEqual("s_datacore", core.Quests.GetState("q_main_ghost").Stage);
            core.CompleteObjective("q_main_ghost", "o_reach_core");

            JsonObject near = core.GetNearbyInteractable();
            Assert.AreEqual("breach", near["kind"], "the job's breach is offered at the core");
            Assert.AreEqual("datacore", near["id"]);
            Assert.AreEqual(("breach", (string)near["prompt"]), core.InteractionAt("datacore"), "the scanner says the same");
            Assert.IsTrue(core.Interact().Ok);
            Assert.AreEqual("datacore", core.Breach.S.TargetId);

            foreach (GridCell p in core.Breach.S.SolutionPath.ToList())
            {
                if (core.Breach.Done) break;
                core.BreachSelect(p.R, p.C);
            }
            Assert.AreEqual("s_wire", core.Quests.GetState("q_main_ghost").Stage);
            Assert.AreEqual("dlg_wire", core.GetNearbyInteractable()["id"], "once breached, Wire answers");
        }

        [Test]
        public void TheViewsForTheScreensAreBuiltOnceAndRebuiltAfterAnyChange()
        {
            var core = new Core().C;
            core.SetCity(FakeCity());
            if (!core.Quests.IsActive("q_main_ghost")) Assert.IsTrue(core.StartQuest("q_main_ghost").Ok);
            IReadOnlyList<JsonObject> markers = core.JobMarkers;
            JsonObject tracked = core.TrackedJob;
            Assert.AreSame(markers, core.JobMarkers, "asked again with nothing changed, the same list");
            Assert.AreSame(tracked, core.TrackedJob);
            CollectionAssert.AreEqual(core.Quests.Markers().Select(m => Json.Write(m)), markers.Select(m => Json.Write(m)), "the same markers the quest log gives");
            Assert.AreEqual(Json.Write(core.Quests.Snapshot("q_main_ghost")), Json.Write(tracked));

            core.Quests.CompleteObjective("q_main_ghost", "o_find_kaz", silent: true);   // a change with no event
            Assert.AreNotSame(markers, core.JobMarkers, "a change to the jobs rebuilds them");
            Assert.AreNotSame(tracked, core.TrackedJob);
            Assert.IsTrue(core.JobMarkers.All(m => (string)m["objectiveId"] != "o_find_kaz"), "the met objective's marker is gone");

            markers = core.JobMarkers;
            core.SetFlag("anything", true);   // a change elsewhere can show a hidden objective: rebuild too
            Assert.AreNotSame(markers, core.JobMarkers);

            core.SetPlayerPosition(1, 0, 1);
            markers = core.JobMarkers;
            core.SetPlayerPosition(2, 0, 2);
            Assert.AreSame(markers, core.JobMarkers, "moving changes nothing the views hold");
        }

        [Test]
        public void NearbyActionIsWhatGetNearbyInteractableSays()
        {
            var core = new Core().C;
            core.SetCity(FakeCity());
            Assert.IsNull(core.NearbyAction(), "nothing at the origin");
            foreach (RpgLocation l in core.GetLocationList())
            {
                core.SetPlayerPosition(l.X, 0, l.Z);
                JsonObject near = core.GetNearbyInteractable();
                var action = core.NearbyAction();
                Assert.AreEqual(near == null, action == null, l.Key);
                if (near == null) continue;
                Assert.AreEqual(((string)near["kind"], (string)near["id"], (string)near["prompt"]), action.Value, l.Key);
            }
        }

        [Test]
        public void AKillRoutedThroughTheCorePaysXpAndLoot()
        {
            var core = new Core();
            double xp = core.C.Stats.Xp;
            core.C.AddXp(45, "combat");
            core.C.DropLoot(tier: 1, count: 2, source: "kill");
            Assert.Greater(core.C.Stats.Xp, xp);
            Assert.AreEqual(1, core.Count("rpg:lootDropped"));
            Assert.GreaterOrEqual(core.Count("rpg:itemAdded"), 1);
        }
    }
}
