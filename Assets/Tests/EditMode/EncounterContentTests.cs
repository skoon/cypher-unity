using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// Milestone 5's content: the prototype's jobs with the fights added, the set encounters put on the real city, the Data Core's breach waiting on its guards,
    /// the two gigs played through headless, and the encounter director's wanted, arriving and survivors-only rules.
    /// </summary>
    public class EncounterContentTests
    {
        static readonly CityPlan Plan = CityPlan.Generate(CityPlan.DefaultSeed);
        static readonly CityNav Nav = CityNav.Build(Plan);

        /// <summary>The game's core on the real city, as <c>RpgService</c> makes it.</summary>
        static RpgCore Game()
        {
            var core = new RpgCore(null, 20771, new MemorySaveStorage(), () => 1700000000000, RpgGameContent.Quests, RpgGameContent.Dialogues) { AutosaveEnabled = false };
            core.SetCity(RpgCityMeta.From(Plan));
            return core;
        }

        static List<EncounterDef> Encounters(RpgCore core)
        {
            PropLayout props = PropPlacer.Build(Plan, CityWindow.AroundPlaza(3));
            return EncounterContent.Build(RpgGameContent.EncountersJson, core.Locations, Nav, props.Boxes, Plan.Buildings.Select(b => (b.X, b.Z, b.Width, b.Depth)));
        }

        static void StandAt(RpgCore core, string place)
        {
            RpgLocation at = core.GetLocation(place);
            core.SetPlayerPosition(at.X, at.Y, at.Z);
            core.Tick(0.1);
        }

        static List<string> OptionIds(RpgCore core) => core.Dialogue.Options().Select(o => (string)o["id"]).ToList();

        static JsonObject Objective(JsonObject quest, string stage, string id) =>
            ((List<object>)((List<object>)quest["stages"]).Cast<JsonObject>().Single(s => (string)s["id"] == stage)["objectives"]).Cast<JsonObject>().Single(o => (string)o["id"] == id);

        static JsonObject QuestDef(IReadOnlyList<JsonObject> quests, string id) => quests.Single(q => (string)q["id"] == id);

        // ---------------------------------------------------------------- the content

        [Test]
        public void TheGameHasThePrototypesJobsWithTheFightsAddedAndThePrototypesAreUntouched()
        {
            Assert.AreEqual(QuestLog.Shipped.Count + 2, RpgGameContent.Quests.Count);
            CollectionAssert.IsSubsetOf(new[] { "q_gig_jackals", "q_gig_protection" }, RpgGameContent.Quests.Select(q => (string)q["id"]).ToList());

            JsonObject game = QuestDef(RpgGameContent.Quests, "q_main_ghost"), shipped = QuestDef(QuestLog.Shipped, "q_main_ghost");
            List<string> ids(JsonObject q) => ((List<object>)((List<object>)q["stages"]).Cast<JsonObject>().Single(s => (string)s["id"] == "s_datacore")["objectives"])
                .Cast<JsonObject>().Select(o => (string)o["id"]).ToList();
            CollectionAssert.AreEqual(new[] { "o_reach_core", "o_guards", "o_breach", "o_datamine" }, ids(game));
            CollectionAssert.AreEqual(new[] { "o_reach_core", "o_breach", "o_datamine" }, ids(shipped), "the goldens' content is the prototype's");
            CollectionAssert.AreEqual(new object[] { "o_guards" }, (List<object>)Objective(game, "s_datacore", "o_breach")["after"]);

            JsonObject kaz = DialogueRunner.Shipped.Single(g => (string)g["id"] == "dlg_kaz");
            Assert.IsFalse(((JsonObject)kaz["nodes"]).ContainsKey("k_jackals_pitch"));
            Assert.AreEqual(2, ((List<object>)kaz["entries"]).Count);
        }

        [Test]
        public void AnObjectiveCanOnlyWaitOnAnotherOfItsStage()
        {
            JsonObject quest = (JsonObject)Json.Parse(@"{ ""id"": ""q"", ""title"": ""Q"", ""stages"": [ { ""id"": ""s"", ""objectives"": [
                { ""id"": ""a"", ""trigger"": ""manual"" }, { ""id"": ""b"", ""trigger"": ""manual"", ""after"": [""c""] } ], ""complete"": ""done"" } ],
                ""outcomes"": { ""done"": { ""title"": ""D"" } } }");
            Assert.Throws<InvalidOperationException>(() => QuestLog.Validate(new[] { quest }));
            Objective(quest, "s", "b")["after"] = new List<object> { "b" };
            Assert.Throws<InvalidOperationException>(() => QuestLog.Validate(new[] { quest }), "not on itself");
            Objective(quest, "s", "b")["after"] = new List<object> { "a" };
            QuestLog.Validate(new[] { quest });
        }

        [Test]
        public void TheEncountersStandOnClearPavementRoundTheirPlacesInTheRealCity()
        {
            RpgCore core = Game();
            List<EncounterDef> defs = Encounters(core);
            CollectionAssert.AreEqual(new[] { "enc_garage_jackals", "enc_pawn_collectors", "enc_datacore_guards" }, defs.Select(d => d.Id).ToList());
            PropLayout props = PropPlacer.Build(Plan, CityWindow.AroundPlaza(3));
            var walls = Plan.Buildings.Select(b => (b.X, b.Z, b.Width, b.Depth)).ToList();
            foreach (EncounterDef d in defs)
            {
                RpgLocation place = core.Locations.Values.Single(l => Math.Abs(l.X - d.Anchor.X) < 1e-9 && Math.Abs(l.Z - d.Anchor.Z) < 1e-9);
                Console.WriteLine($"{d.Id} at {place.Key}: " + string.Join("  ", d.Members.Select(m => $"{m.Archetype} ({m.Dx:0.0}, {m.Dz:0.0})")));
                for (int i = 0; i < d.Members.Count; i++)
                {
                    EncounterMember m = d.Members[i];
                    double r = Math.Sqrt(m.Dx * m.Dx + m.Dz * m.Dz);
                    Assert.That(r, Is.InRange(EncounterContent.Nearest - 1e-9, 16 + 1e-9), $"{d.Id} {i}");
                    Assert.IsTrue(EncounterContent.OnPavement(d.Anchor.X + m.Dx, d.Anchor.Z + m.Dz, Nav, props.Boxes, walls), $"{d.Id} {i} on clear pavement");
                    Assert.AreEqual(Nav.WalkY, Nav.GroundY(d.Anchor.X + m.Dx, d.Anchor.Z + m.Dz), 1e-9);
                    for (int j = 0; j < i; j++)
                    {
                        double dx = m.Dx - d.Members[j].Dx, dz = m.Dz - d.Members[j].Dz;
                        Assert.GreaterOrEqual(Math.Sqrt(dx * dx + dz * dz), EncounterContent.Spacing - 1e-9, $"{d.Id} {i} and {j} apart");
                    }
                }
                Assert.AreEqual("heavy", d.Members[0].Archetype, "the heavy stands nearest the door");
            }
            Assert.AreEqual(40, defs.Single(d => d.Id == "enc_pawn_collectors").ArriveBeyond, "Marlene's collectors come while you are away");
            Assert.Less(40, EncounterDirector.SpawnWithin);
        }

        [Test]
        public void EveryJobsKillCountIsItsEncountersSizeAndEachEncounterWaitsOnARealStage()
        {
            List<EncounterDef> defs = Encounters(Game());
            var kills = new List<(string Quest, string Stage, string Tag, double Count)>();
            foreach (JsonObject q in RpgGameContent.Quests)
            {
                foreach (JsonObject s in ((List<object>)q["stages"]).Cast<JsonObject>())
                {
                    foreach (JsonObject o in ((List<object>)s["objectives"]).Cast<JsonObject>())
                    {
                        if ((string)o["trigger"] == "kill") kills.Add(((string)q["id"], (string)s["id"], (string)o["tag"], (double)o["count"]));
                    }
                }
            }
            Assert.AreEqual(3, kills.Count);
            foreach ((string quest, string stage, string tag, double count) in kills)
            {
                EncounterDef d = defs.Single(x => x.Tag == tag);
                Assert.AreEqual(count, d.Members.Count, tag);
                var when = (JsonObject)d.When["quest"];
                Assert.AreEqual(quest, when["id"], tag);
                Assert.AreEqual(stage, when["stage"], $"{tag}: there while its kills count");
            }
            Assert.AreEqual(defs.Count, defs.Select(d => d.Tag).Distinct().Count(), "a tag each, so kills count only for their own job");
            Assert.IsFalse(defs.Any(d => d.Tag == "street"), "patrol kills count for nothing");
        }

        [Test]
        public void BadEncounterContentFailsLoudly()
        {
            RpgCore core = Game();
            var walls = Array.Empty<(double, double, double, double)>();
            List<EncounterDef> Build(string json) => EncounterContent.Build(json, core.Locations, Nav, Array.Empty<PropBox>(), walls);
            Assert.Throws<ArgumentOutOfRangeException>(() => Build(@"[{ ""id"": ""x"", ""tag"": ""t"", ""place"": ""moon"", ""reach"": 10, ""members"": [""gunner""] }]"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Build(@"[{ ""id"": ""x"", ""tag"": ""t"", ""place"": ""pawn"", ""reach"": 10, ""members"": [""dragon""] }]"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Build(@"[{ ""id"": ""x"", ""tag"": ""t"", ""place"": ""pawn"", ""reach"": 10, ""members"": [""gunner""], ""colour"": 1 }]"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Build(@"[{ ""id"": ""x"", ""tag"": ""t"", ""place"": ""pawn"", ""reach"": 10, ""members"": [""gunner""], ""when"": { ""weather"": 1 } }]"));
            Assert.Throws<InvalidOperationException>(() => Build(@"[{ ""id"": ""x"", ""tag"": ""t"", ""place"": ""pawn"", ""reach"": 4, ""members"": [""gunner"", ""gunner"", ""gunner"", ""gunner"", ""gunner"", ""gunner""] }]"),
                "no room for six within 4 m");
        }

        // ---------------------------------------------------------------- the jobs, headless

        static void ToStage(RpgCore core, string questId, string stageId)
        {
            for (int guard = 0; guard < 20 && (string)core.Quests.Snapshot(questId)["stageId"] != stageId; guard++)
            {
                JsonObject snap = core.Quests.Snapshot(questId);
                JsonObject open = ((List<object>)snap["objectives"]).Cast<JsonObject>().First(o => !(bool)o["done"] && !(bool)o["optional"]);
                Assert.IsTrue(core.Quests.CompleteObjective(questId, (string)open["id"]).Ok);
            }
            Assert.AreEqual(stageId, core.Quests.Snapshot(questId)["stageId"]);
        }

        [Test]
        public void TheDataCoreCannotBeBreachedUntilItsFourGuardsAreDown()
        {
            RpgCore core = Game();
            Assert.IsTrue(core.Quests.IsActive("q_main_ghost"));
            ToStage(core, "q_main_ghost", "s_datacore");
            StandAt(core, "datacore");
            Assert.IsNull(core.GetNearbyInteractable(), "nothing to do at the core while its guards stand");
            for (int i = 0; i < 3; i++) core.CombatKill(tag: "corpsec");
            core.CombatKill(tag: "street");
            core.Tick(0.1);
            Assert.IsNull(core.GetNearbyInteractable(), "three of four, and a passer-by's patrol, is not enough");
            Assert.AreEqual(3.0, ((List<object>)core.Quests.Snapshot("q_main_ghost")["objectives"]).Cast<JsonObject>().Single(o => (string)o["id"] == "o_guards")["count"]);
            core.CombatKill(tag: "corpsec");
            JsonObject e = core.GetNearbyInteractable();
            Assert.IsNotNull(e);
            Assert.AreEqual("breach", e["kind"]);
            Assert.AreEqual("datacore", e["id"]);
            Assert.AreEqual("s_datacore", core.Quests.Snapshot("q_main_ghost")["stageId"], "the breach is still to do");
        }

        [Test]
        public void KazOffersJackalSeasonOnceTheMainJobIsTakenAndPaysForFiveJackals()
        {
            RpgCore core = Game();
            core.SetFlag("kaz_job_taken", true);
            StandAt(core, "noodle");
            core.StartDialogue("dlg_kaz");
            Assert.AreEqual("k_already", core.Dialogue.NodeId);
            JsonObject offer = core.Dialogue.Options().Single(o => (string)o["id"] == "k_o_jackals");
            Assert.IsTrue((bool)offer["locked"], "level 2 first");
            Assert.AreEqual("just passing.", ((string)core.Dialogue.Options().Last()["text"]).ToLowerInvariant(), "the way out stays last");
            Assert.IsTrue(core.ChooseDialogueOption("k_o_bye").Ok);

            core.AddXp(RpgStats.TotalXpForLevel(2));
            core.StartDialogue("dlg_kaz");
            Assert.IsTrue(core.ChooseDialogueOption("k_o_jackals").Ok);
            Assert.IsTrue(core.ChooseDialogueOption("k_o_jackals_how").Ok);
            Assert.IsTrue(core.ChooseDialogueOption("k_o_jackals_yes").Ok);
            Assert.IsTrue(core.Quests.IsActive("q_gig_jackals"));
            Assert.IsTrue(core.ChooseDialogueOption("k_o_jackals_end").Ok);
            core.Tick(0.1);
            Assert.AreEqual("s_clear", core.Quests.Snapshot("q_gig_jackals")["stageId"], "taken at the bar");

            core.StartDialogue("dlg_kaz");
            Assert.IsFalse(OptionIds(core).Contains("k_o_jackals"), "not offered twice");
            core.ChooseDialogueOption("k_o_bye");

            StandAt(core, "garage");
            for (int i = 0; i < 4; i++) core.CombatKill(tag: "jackals");
            core.CombatKill(tag: "collectors");
            Assert.AreEqual("s_clear", core.Quests.Snapshot("q_gig_jackals")["stageId"]);
            core.CombatKill(tag: "jackals");
            Assert.AreEqual("s_back", core.Quests.Snapshot("q_gig_jackals")["stageId"]);

            double money = core.Money;
            StandAt(core, "noodle");
            core.StartDialogue("dlg_kaz");
            Assert.AreEqual("k_jackals_back", core.Dialogue.NodeId);
            core.ChooseDialogueOption("k_o_jackals_paid");
            Assert.IsTrue(core.Quests.IsCompleted("q_gig_jackals"));
            Assert.AreEqual("cleared", core.Quests.Snapshot("q_gig_jackals")["outcome"]);
            Assert.AreEqual(money + 1800, core.Money);
        }

        [Test]
        public void MarleneOffersProtectionAndPaysInCounterPricesForThreeCollectors()
        {
            RpgCore core = Game();
            core.AddXp(RpgStats.TotalXpForLevel(3));
            StandAt(core, "pawn");
            core.StartDialogue("dlg_marlene");
            Assert.IsTrue(core.ChooseDialogueOption("m_o_protect").Ok);
            Assert.IsTrue(core.ChooseDialogueOption("m_o_protect_who").Ok);
            Assert.IsTrue(core.ChooseDialogueOption("m_o_protect_yes").Ok);
            Assert.IsTrue(core.ChooseDialogueOption("m_o_protect_end").Ok);
            core.Tick(0.1);
            Assert.AreEqual("s_collect", core.Quests.Snapshot("q_gig_protection")["stageId"]);
            Assert.IsNull(core.GetFlag("marlene_counter_prices"));
            for (int i = 0; i < 3; i++) core.CombatKill(tag: "collectors");
            Assert.AreEqual("s_back", core.Quests.Snapshot("q_gig_protection")["stageId"]);
            core.Tick(0.1);
            core.StartDialogue("dlg_marlene");
            Assert.AreEqual("m_protect_back", core.Dialogue.NodeId);
            core.ChooseDialogueOption("m_o_protect_paid");
            Assert.IsTrue(core.Quests.IsCompleted("q_gig_protection"));
            Assert.AreEqual(true, core.GetFlag("marlene_counter_prices"));
        }

        // ---------------------------------------------------------------- the director

        static EncounterDef Three(double arriveBeyond = 0) => new EncounterDef
        {
            Id = "enc", Tag = "t", Anchor = new V3(0, 0, 0), ArriveBeyond = arriveBeyond,
            Members = new[] { new EncounterMember { Archetype = "heavy", Dx = 3 }, new EncounterMember { Archetype = "gunner", Dx = -3 }, new EncounterMember { Archetype = "gunner", Dz = 3 } },
        };

        [Test]
        public void AnEncounterIsThereOnlyWhileItIsWanted()
        {
            var sim = new EnemySim(1);
            bool wanted = false;
            var dir = new EncounterDirector(sim, new[] { Three() }, id => false, wanted: d => wanted);
            dir.Step(new V3(20, 0, 0));
            Assert.IsEmpty(sim.Enemies, "not yet wanted");
            wanted = true;
            dir.Step(new V3(20, 0, 0));
            Assert.AreEqual(3, sim.Enemies.Count);
            wanted = false;
            dir.Step(new V3(20, 0, 0));
            Assert.IsEmpty(sim.Enemies, "unwanted and calm: gone");
            wanted = true;
            dir.Step(new V3(20, 0, 0));
            sim.ShotAt(sim.Enemies[0], new V3(20, 0, 0));
            wanted = false;
            dir.Step(new V3(20, 0, 0));
            Assert.AreEqual(3, sim.Enemies.Count, "not taken away in the middle of a fight");
        }

        [Test]
        public void AnArrivingEncounterWaitsForThePlayerToBeAway()
        {
            var sim = new EnemySim(1);
            var dir = new EncounterDirector(sim, new[] { Three(arriveBeyond: 40) }, id => false);
            dir.Step(new V3(5, 0, 0));
            Assert.IsEmpty(sim.Enemies, "not while you are in the shop");
            dir.Step(new V3(45, 0, 0));
            Assert.AreEqual(3, sim.Enemies.Count, "they came while you were away");
            dir.Step(new V3(5, 0, 0));
            Assert.AreEqual(3, sim.Enemies.Count, "and are there when you come back");
        }

        [Test]
        public void ThoseWhoWentDownStayDownAfterADespawnAResetAndALoad()
        {
            var sim = new EnemySim(1);
            var dir = new EncounterDirector(sim, new[] { Three() }, id => false);
            dir.Step(new V3(20, 0, 0));
            EnemySquad squad = dir.Live("enc");
            sim.Harm(squad.All[1], 1e6);
            dir.Step(new V3(20, 0, 0));
            CollectionAssert.AreEqual(new[] { ("enc", 1) }, dir.JustDown, "said, to be saved");
            dir.Step(new V3(20, 0, 0));
            Assert.IsEmpty(dir.JustDown, "said once");

            dir.Step(new V3(130, 0, 0));
            Assert.IsEmpty(sim.Enemies, "away: gone");
            dir.Step(new V3(20, 0, 0));
            CollectionAssert.AreEqual(new[] { "heavy", "gunner" }, dir.Live("enc").All.Select(e => e.Archetype.Id).ToList(), "back with the survivors only");

            sim.Harm(dir.Live("enc").All[0], 1e6);   // the heavy, member 0, falls just before the flatline
            dir.Reset();
            CollectionAssert.AreEqual(new[] { ("enc", 0) }, dir.JustDown, "a reset still notes who fell");
            Assert.IsEmpty(sim.Enemies);
            dir.Step(new V3(20, 0, 0));
            Assert.AreEqual(1, dir.Live("enc").All.Count, "one left after the Trauma Team, not three");
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, dir.Down("enc"));

            var loaded = new EncounterDirector(new EnemySim(1), new[] { Three() }, id => false, id => new[] { 0, 1 });
            loaded.Step(new V3(20, 0, 0));
            Assert.AreEqual(1, loaded.Live("enc").All.Count, "and the same after a load");
            Assert.AreEqual(EnemyTable.Gunner, loaded.Live("enc").All[0].Archetype);

            sim.Harm(dir.Live("enc").All[0], 1e6);
            dir.Step(new V3(20, 0, 0));
            CollectionAssert.AreEqual(new[] { "enc" }, dir.Cleared, "the last of them: cleared");

            var allDown = new EncounterDirector(new EnemySim(1), new[] { Three() }, id => false, id => new[] { 0, 1, 2 });
            allDown.Step(new V3(20, 0, 0));
            CollectionAssert.AreEqual(new[] { "enc" }, allDown.Cleared, "all down in the save is cleared");
            Assert.IsNull(allDown.Live("enc"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EncounterDirector(new EnemySim(1), new[] { Three() }, id => false, id => new[] { 3 }));
        }
    }
}
