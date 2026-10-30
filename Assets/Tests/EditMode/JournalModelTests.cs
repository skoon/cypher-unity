using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The journal's view of the quest log: sections, the tracked job first, the detail of an open, an available and a finished job, and the pay by name.</summary>
    public class JournalModelTests
    {
        static RpgCore Core()
        {
            var core = new RpgCore(null, 20771, new MemorySaveStorage(), () => 1700000000000) { AutosaveEnabled = false };
            core.SetCity(RpgCityMeta.From(CityPlan.Generate(20771)));
            core.GiveStartingKit();
            core.AddXp(5000);   // level 3: the side job and the gig that need levels are on offer
            Assert.IsNotEmpty(core.Quests.Available());
            return core;
        }

        [Test]
        public void ANewGameHasTheMainJobActiveAndTrackedFirstAndTheOthersAvailableOrHidden()
        {
            RpgCore core = Core();
            List<JournalEntry> entries = JournalModel.Entries(core.GetQuests());
            JournalEntry first = entries[0];
            Assert.AreEqual("q_main_ghost", first.Id);
            Assert.AreEqual("ACTIVE", first.Section);
            Assert.IsTrue(first.Tracked);
            Assert.AreEqual("MAIN JOB", first.Type);
            StringAssert.EndsWith("OBJECTIVE", first.Progress.TrimEnd('S'));
            CollectionAssert.AreEqual(core.Quests.Available().Select(q => (string)q["id"]), entries.Where(e => e.Section == "AVAILABLE").Select(e => e.Id));
            Assert.IsTrue(entries.Where(e => e.Section == "AVAILABLE").All(e => e.Status == "AVAILABLE" && !e.Tracked));
            Assert.IsEmpty(entries.Where(e => e.Section == "FINISHED"));
        }

        [Test]
        public void TheTrackedJobComesFirstAmongTheActiveOnes()
        {
            RpgCore core = Core();
            string other = core.Quests.Available().Select(q => (string)q["id"]).First();
            Assert.IsTrue(core.StartQuest(other).Ok);
            core.TrackQuest(other);
            List<JournalEntry> entries = JournalModel.Entries(core.GetQuests());
            Assert.AreEqual(other, entries[0].Id);
            Assert.AreEqual("q_main_ghost", entries[1].Id);
            Assert.IsFalse(entries[1].Tracked);
        }

        [Test]
        public void AnOpenJobShowsItsStageAndObjectivesWithDistancesAndCanBeTrackedWhenItIsNot()
        {
            RpgCore core = Core();
            RpgLocation noodle = core.GetLocation("noodle");
            JournalDetail d = JournalModel.Detail(core.GetQuest("q_main_ghost"), core.Quests.Def("q_main_ghost"), noodle.X + 30, noodle.Z + 40);
            Assert.AreEqual("ACTIVE", d.Status);
            Assert.IsTrue(d.Tracked);
            Assert.IsFalse(d.CanTrack, "already tracked");
            Assert.IsNotEmpty(d.Summary);
            Assert.IsNotEmpty(d.Objectives);
            var first = d.Objectives[0];
            Assert.IsTrue(first.Active);
            Assert.IsFalse(first.Done);
            Assert.AreEqual("50M", first.Distance, "the noodle bar is 50 m off");
            Assert.IsEmpty(d.Rewards, "the pay depends on the ending");
            Assert.AreEqual("", d.Hint);

            string other = core.Quests.Available().Select(q => (string)q["id"]).First();
            core.StartQuest(other);
            Assert.IsTrue(JournalModel.Detail(core.GetQuest(other), core.Quests.Def(other), 0, 0).CanTrack);
        }

        [Test]
        public void AnAvailableJobSaysWhoToSee()
        {
            RpgCore core = Core();
            JsonObject available = core.Quests.Available().First();
            JournalDetail d = JournalModel.Detail(available, core.Quests.Def((string)available["id"]), 0, 0);
            Assert.AreEqual("AVAILABLE", d.Status);
            Assert.IsFalse(d.CanTrack);
            StringAssert.StartsWith("TALK TO " + ((string)available["giver"]).ToUpperInvariant(), d.Hint);
            Assert.IsEmpty(d.Objectives);
        }

        [Test]
        public void AFinishedJobShowsItsEndingAndWhatItPaidByName()
        {
            RpgCore core = Core();
            JsonObject def = core.Quests.Def("q_main_ghost");
            string outcome = ((JsonObject)def["outcomes"]).Keys.First();
            Assert.IsTrue(core.FailQuest("q_main_ghost", outcome).Ok || core.Quests.GetState("q_main_ghost").State != "active");
            List<JournalEntry> entries = JournalModel.Entries(core.GetQuests());
            JournalEntry e = entries.Single(x => x.Id == "q_main_ghost");
            Assert.AreEqual("FINISHED", e.Section);

            JournalDetail d = JournalModel.Detail(core.GetQuest("q_main_ghost"), def, 0, 0);
            Assert.IsTrue(d.Status == "COMPLETE" || d.Status == "FAILED");
            Assert.IsNotEmpty(d.OutcomeTitle);
            Assert.IsFalse(d.CanTrack);
            Assert.IsTrue(d.Objectives.All(o => o.Distance == ""), "no distances on a closed job");
        }

        [Test]
        public void RewardsAreListedWithItemAndChromeNames()
        {
            var rewards = (JsonObject)Json.Parse("{\"xp\":1200,\"cred\":30,\"money\":9000,\"perkPoints\":1,\"items\":[{\"templateId\":\"w_pistol\",\"rarity\":\"epic\"},{\"cyberware\":\"cw_bioconductor\"}]}");
            List<string> lines = JournalModel.Rewards(rewards);
            Assert.AreEqual("1,200 XP", lines[0]);
            Assert.AreEqual("9,000 €$", lines[1]);
            Assert.AreEqual("30 STREET CRED", lines[2]);
            Assert.AreEqual("1 PERK POINT", lines[3]);
            StringAssert.StartsWith("EPIC ", lines[4]);
            Assert.AreEqual(RpgCyberware.Get("cw_bioconductor").Name.ToUpperInvariant(), lines[5]);
            Assert.IsEmpty(JournalModel.Rewards(new JsonObject()));
        }
    }
}
