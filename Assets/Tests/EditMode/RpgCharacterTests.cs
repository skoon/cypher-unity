using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// The RPG's character rules, one behaviour at a time: the prototype's own tests (<c>tests/rpg.test.mjs</c>) for stats, perks, chrome, inventory, loot, conditions
    /// and saves, ported. (The golden tests compare whole scripted sessions; these say what each rule is for.)
    /// </summary>
    public class RpgCharacterTests
    {
        static Dictionary<string, int> Attrs(int body = 4, int reflexes = 4, int intelligence = 4, int technical = 4, int cool = 4) => new Dictionary<string, int>
        {
            ["body"] = body, ["reflexes"] = reflexes, ["intelligence"] = intelligence, ["technical"] = technical, ["cool"] = cool,
        };

        // ---------------------------------------------------------------- XP and levelling

        [Test]
        public void TheXpCurveIsStrictlyIncreasingAndFiniteBelowTheCap()
        {
            for (int l = 1; l < RpgStats.LevelCap - 1; l++)
            {
                Assert.Less(RpgStats.XpToNext(l), RpgStats.XpToNext(l + 1), $"level {l}");
                Assert.IsFalse(double.IsInfinity(RpgStats.XpToNext(l)));
            }
            Assert.AreEqual(double.PositiveInfinity, RpgStats.XpToNext(RpgStats.LevelCap));
            Assert.Throws<ArgumentOutOfRangeException>(() => RpgStats.XpToNext(0));
        }

        [Test]
        public void LevelFromXpInvertsTotalXpForLevelExactly()
        {
            for (int l = 1; l <= 20; l++)
            {
                double total = RpgStats.TotalXpForLevel(l);
                (int level, double into, double _) = RpgStats.LevelFromXp(total);
                Assert.AreEqual(l, level);
                Assert.AreEqual(0, into);
                if (l > 1) Assert.AreEqual(l - 1, RpgStats.LevelFromXp(total - 1).Level, "one point short is still the previous level");
            }
        }

        [Test]
        public void LevelUpsAwardPointsOnTheDocumentedCadence()
        {
            Assert.AreEqual((1, 1), RpgStats.LevelUpAwards(2));
            Assert.AreEqual((2, 1), RpgStats.LevelUpAwards(3));
            Assert.AreEqual((1, 2), RpgStats.LevelUpAwards(5));
            Assert.AreEqual((2, 2), RpgStats.LevelUpAwards(15));
        }

        [Test]
        public void AddXpLevelsTheCharacterAndBanksThePoints()
        {
            var stats = new StatBlock();
            int attr = stats.AttributePoints, perk = stats.PerkPoints;
            XpGain res = stats.AddXp(RpgStats.TotalXpForLevel(4));
            Assert.AreEqual(4, res.Level);
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, res.Levels);
            Assert.AreEqual(attr + new[] { 2, 3, 4 }.Sum(l => RpgStats.LevelUpAwards(l).AttributePoints), stats.AttributePoints);
            Assert.AreEqual(perk + new[] { 2, 3, 4 }.Sum(l => RpgStats.LevelUpAwards(l).PerkPoints), stats.PerkPoints);
        }

        [Test]
        public void XpStopsAtTheLevelCapAndNegativeXpIsANoOp()
        {
            var stats = new StatBlock();
            stats.AddXp(RpgStats.TotalXpForLevel(RpgStats.LevelCap) * 3);
            Assert.AreEqual(RpgStats.LevelCap, stats.Level);
            double before = stats.Xp;
            Assert.AreEqual(0, stats.AddXp(-500).Gained);
            Assert.AreEqual(before, stats.Xp);
            Assert.Throws<ArgumentException>(() => stats.AddXp(double.NaN));
        }

        [Test]
        public void XpRateModifiersScaleIncomingXp()
        {
            var stats = new StatBlock();
            stats.Modifiers.Set("test", new[] { StatModifier.Add("xpRate", 0.5) });
            stats.Invalidate();
            Assert.AreEqual(150, stats.AddXp(100).Gained);
        }

        [Test]
        public void StreetCredIsASeparateSlowerTrack()
        {
            var stats = new StatBlock();
            Assert.AreEqual(1, stats.StreetCred);
            stats.AddCredXp(190);
            Assert.AreEqual(2, stats.StreetCred);
            Assert.AreEqual(1, RpgStats.CredFromXp(0).Level);
        }

        // ---------------------------------------------------------------- derived stats and modifiers

        [Test]
        public void BaseDerivedReadsEveryAttributeAndRejectsAMissingOne()
        {
            DerivedStats d = RpgStats.BaseDerived(Attrs(6, 5, 8, 7, 4), 3);
            Assert.AreEqual(100 + 6 * 12 + 3 * 4, d["maxHealth"]);
            Assert.AreEqual(4 + 8 / 2, d["maxRam"]);
            Assert.AreEqual(6 + 3 / 2 + 7, d["cyberCapacity"]);
            Assert.Throws<ArgumentException>(() => RpgStats.BaseDerived(new Dictionary<string, int> { ["body"] = 1 }, 1));
        }

        [Test]
        public void ModifiersAreBasePlusAddsTimesOnePlusMulsThenClampedAndRounded()
        {
            DerivedStats b = RpgStats.BaseDerived(Attrs(), 1);
            DerivedStats o = RpgStats.ApplyModifiers(b, new[] { StatModifier.Add("maxHealth", 50), StatModifier.Mul("maxHealth", 0.5) });
            Assert.AreEqual(RpgJs.Round((b["maxHealth"] + 50) * 1.5), o["maxHealth"]);
            Assert.AreEqual(Math.Floor(o["maxHealth"]), o["maxHealth"], "integer stats are rounded");
            Assert.AreEqual(0.85, RpgStats.ApplyModifiers(b, new[] { StatModifier.Add("critChance", 5) })["critChance"], "capped");
            Assert.AreEqual(0, RpgStats.ApplyModifiers(b, new[] { StatModifier.Add("armor", -9999) })["armor"], "floored");
        }

        [Test]
        public void AModifierNamingAnUnknownStatOrOpThrows()
        {
            var set = new ModifierSet();
            Assert.Throws<ArgumentOutOfRangeException>(() => set.Set("bad", new[] { new StatModifier("nonsense", "add", 1) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => set.Set("bad", new[] { new StatModifier("armor", "divide", 1) }));
            Assert.Throws<ArgumentException>(() => set.Set("bad", new[] { new StatModifier("armor", "add", double.NaN) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => set.ForStat("nope"));
        }

        [Test]
        public void ModifierSourcesReplaceRatherThanAccumulate()
        {
            var set = new ModifierSet();
            set.Set("perk:x", new[] { StatModifier.Add("armor", 10) });
            set.Set("perk:x", new[] { StatModifier.Add("armor", 4) });
            Assert.AreEqual(1, set.ForStat("armor").Count);
            Assert.AreEqual(4, set.ForStat("armor")[0].Value);
            set.Remove("perk:x");
            Assert.AreEqual(0, set.All().Count);
        }

        // ---------------------------------------------------------------- perks

        [Test]
        public void PerkPrerequisitesBlockAPurchaseAndReportWhatIsMissing()
        {
            var tree = new PerkTree();
            var stats = new PerkBuyer { Attributes = Attrs(), Level = 1, PerkPoints = 5 };
            RpgResult jug = tree.CanBuy("juggernaut", stats);
            Assert.IsFalse(jug.Ok);
            Assert.AreEqual("requirements", jug.Reason);
            CollectionAssert.AreEqual(new[] { "BODY 12", "LEVEL 12", "Iron Lung 2" }, jug.Missing);
            Assert.IsTrue(tree.CanBuy("bulk", stats).Ok);
        }

        [Test]
        public void APerkThatNeedsRanksInAnotherUnlocksOnceThoseRanksExist()
        {
            var tree = new PerkTree();
            var stats = new PerkBuyer { Attributes = Attrs(body: 8), Level = 5, PerkPoints = 9 };
            CollectionAssert.AreEqual(new[] { "Bulk 2" }, tree.CanBuy("iron_lung", stats).Missing);
            tree.Buy("bulk", stats);
            CollectionAssert.AreEqual(new[] { "Bulk 2" }, tree.CanBuy("iron_lung", stats).Missing);
            tree.Buy("bulk", stats);
            Assert.IsTrue(tree.CanBuy("iron_lung", stats).Ok);
        }

        [Test]
        public void BuyingAPerkSpendsPointsAndStopsAtMaxRank()
        {
            var tree = new PerkTree();
            var stats = new PerkBuyer { Attributes = Attrs(body: 6), Level = 1, PerkPoints = 4 };
            for (int i = 0; i < RpgPerks.Get("bulk").MaxRank; i++) Assert.IsTrue(tree.Buy("bulk", stats).Ok);
            Assert.AreEqual(3, tree.RankOf("bulk"));
            Assert.AreEqual(1, stats.PerkPoints);
            Assert.AreEqual("max-rank", tree.Buy("bulk", stats).Reason);
        }

        [Test]
        public void RunningOutOfPerkPointsIsReportedNotThrown()
        {
            RpgResult r = new PerkTree().CanBuy("bulk", new PerkBuyer { Attributes = Attrs(body: 6), Level = 1, PerkPoints = 0 });
            Assert.IsFalse(r.Ok);
            Assert.AreEqual("no-points", r.Reason);
            Assert.IsEmpty(r.Missing);
            Assert.AreEqual(1, r.Cost);
        }

        [Test]
        public void AnUnknownPerkIdThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PerkTree().CanBuy("not_a_perk", new PerkBuyer { Attributes = Attrs(), PerkPoints = 9 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PerkTree(new[] { new KeyValuePair<string, int>("bulk", 99) }));
        }

        [Test]
        public void RespecRefundsEveryPointSpentAndPerksFeedTheModifiers()
        {
            var tree = new PerkTree();
            var stats = new PerkBuyer { Attributes = Attrs(body: 6), Level = 1, PerkPoints = 6 };
            tree.Buy("bulk", stats);
            tree.Buy("bulk", stats);
            var set = tree.ApplyTo(new ModifierSet());
            Assert.AreEqual(50, set.ForStat("maxHealth").Sum(m => m.Value), "Bulk 2 is +50 health");
            Assert.AreEqual(2, tree.Respec(stats));
            Assert.AreEqual(6, stats.PerkPoints);
            Assert.AreEqual(0, tree.RankOf("bulk"));
            Assert.AreEqual(0, tree.ApplyTo(set).ForStat("maxHealth").Count, "the old perk modifiers are taken out");
        }

        [Test]
        public void EveryShippedPerkHasLegalModifiersAtEveryRank()
        {
            foreach (Perk perk in RpgPerks.All)
            {
                for (int rank = 1; rank <= perk.MaxRank; rank++)
                {
                    foreach (StatModifier m in perk.Mods(rank)) Assert.IsTrue(RpgStats.IsDerived(m.Stat), $"{perk.Id} targets unknown stat {m.Stat}");
                }
            }
            Assert.AreEqual(25, RpgPerks.All.Count);
            Assert.AreEqual(5, RpgPerks.Trees().Count);
        }

        // ---------------------------------------------------------------- cyberware

        static CyberStats Maxed(int capacity) => new CyberStats { Attributes = Attrs(20, 20, 20, 20, 20), Level = 30, StreetCred = 10, CyberCapacity = capacity };

        [Test]
        public void ChromeIsLimitedBySlotCountAndByTheCapacityBudget()
        {
            var rig = new CyberwareRig();
            CyberStats stats = Maxed(8);
            Assert.IsTrue(rig.Install("cw_kiroshi_mk1", stats).Ok);
            Assert.IsTrue(rig.Install("cw_nightvision", stats).Ok);
            Assert.AreEqual(5, rig.UsedCapacity());
            Assert.AreEqual("slot-full", rig.CanInstall("cw_kiroshi_mk3", stats).Reason, "both ocular slots are full");
            Assert.AreEqual("no-capacity", rig.CanInstall("cw_netdriver", stats).Reason, "and only 3 of the budget is left");
            Assert.AreEqual("no-capacity", rig.CanInstall("cw_kerenzikov", stats).Reason);
        }

        [Test]
        public void ChromeRequirementFailuresListTheMissingGates()
        {
            var rig = new CyberwareRig();
            var stats = new CyberStats { Attributes = Attrs(), Level = 1, StreetCred = 1, CyberCapacity = 99 };
            RpgResult r = rig.CanInstall("cw_netdriver", stats);
            Assert.IsFalse(r.Ok);
            CollectionAssert.AreEqual(new[] { "INTELLIGENCE 12", "TECHNICAL 6", "LEVEL 14", "STREET CRED 4" }, r.Missing);
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.CanInstall("cw_nope", stats));
        }

        [Test]
        public void SlotCapacitiesAreRespectedPerBodySystemAndChromeNeverFeedsTheBudget()
        {
            Assert.AreEqual(1, RpgCyberware.Slot("arms").Capacity);
            var rig = new CyberwareRig();
            Assert.IsTrue(rig.Install("cw_gorilla_arms", Maxed(200)).Ok);
            Assert.AreEqual("slot-full", rig.CanInstall("cw_mantis_blades", Maxed(200)).Reason);
            Assert.IsTrue(RpgCyberware.All.All(c => c.Mods.All(m => m.Stat != "cyberCapacity")));
        }

        // ---------------------------------------------------------------- inventory

        static Item Stackable(string uid, int qty = 1, string templateId = "c_maxdoc")
        {
            ItemTemplate t = RpgItems.Template(templateId);
            return new Item
            {
                Uid = uid, TemplateId = templateId, Type = t.Type, Name = t.BaseNames[0], Rarity = "common", Level = 1, Weight = t.Weight, Value = t.Value, Qty = qty,
                MaxStack = t.MaxStack ?? 1, Desc = t.Desc,
            };
        }

        [Test]
        public void StackableItemsMergeIntoExistingStacks()
        {
            var inv = new Inventory(100);
            inv.Add(Stackable("a", 4));
            inv.Add(Stackable("b", 3));
            Assert.AreEqual(1, inv.Size, "both adds landed in one stack");
            Assert.AreEqual(7, inv.Count("c_maxdoc"));
        }

        [Test]
        public void StacksSplitAtMaxStack()
        {
            var inv = new Inventory(1000);
            inv.Add(Stackable("a", 25));
            Assert.AreEqual(25, inv.Count("c_maxdoc"));
            Assert.AreEqual(3, inv.Size);
            Assert.IsTrue(inv.Items.All(i => i.Qty <= 10));
        }

        [Test]
        public void AnAddThatWouldExceedCapacityIsRefusedWhole()
        {
            var inv = new Inventory(1);
            RpgResult r = inv.Add(Stackable("a", 20));
            Assert.IsFalse(r.Ok);
            Assert.AreEqual("overweight", r.Reason);
            Assert.AreEqual(0, inv.Size, "nothing partial was added");
        }

        [Test]
        public void WeightAndFreeSpaceAreTracked()
        {
            var inv = new Inventory(10);
            inv.Add(Stackable("a", 5));
            Assert.AreEqual(1, inv.Weight);
            Assert.AreEqual(9, inv.Free);
            Assert.IsFalse(inv.Overweight);
        }

        [Test]
        public void RemovingMoreThanYouHoldFailsWithoutChangingAnything()
        {
            var inv = new Inventory(100);
            inv.Add(Stackable("a", 2));
            Assert.AreEqual("not-enough", inv.Remove("a", 5).Reason);
            Assert.AreEqual(2, inv.Count("c_maxdoc"));
            Assert.AreEqual("not-found", inv.Remove("nope", 1).Reason);
            Assert.Throws<ArgumentOutOfRangeException>(() => inv.Remove("a", 0));
        }

        [Test]
        public void RemoveByTemplateDrainsAcrossStacks()
        {
            var inv = new Inventory(1000);
            inv.Add(Stackable("a", 25));
            Assert.IsTrue(inv.RemoveByTemplate("c_maxdoc", 12).Ok);
            Assert.AreEqual(13, inv.Count("c_maxdoc"));
            Assert.IsFalse(inv.RemoveByTemplate("c_maxdoc", 99).Ok);
        }

        [Test]
        public void EquippingSwapsTheSlotAndUnequippingClearsIt()
        {
            var inv = new Inventory(100);
            inv.Add(RpgItems.MakeItem(() => 0.5, "w_pistol", "common", 1, "g1"));
            inv.Add(RpgItems.MakeItem(() => 0.5, "w_pistol", "rare", 1, "g2"));
            Assert.AreEqual("secondary", inv.Equip("g1").Slot);
            Assert.AreEqual("g1", inv.Equip("g2").Replaced.Uid);
            Assert.AreEqual("g2", inv.EquippedItem("secondary").Uid);
            Assert.IsTrue(inv.Unequip("secondary").Ok);
            Assert.IsNull(inv.EquippedItem("secondary"));
            Assert.Throws<ArgumentOutOfRangeException>(() => inv.Unequip("nonsense"));
        }

        [Test]
        public void OnlyQuickhacksGoInTheDeckAndOnlyInOneSlotAtATime()
        {
            var inv = new Inventory(100, 4);
            inv.Add(RpgItems.MakeItem(() => 0.5, "qh_ping", "common", 1, "h1"));
            inv.Add(Stackable("j1", 1, "j_scrap"));
            Assert.AreEqual("not-a-quickhack", inv.EquipHack("j1", 0).Reason);
            Assert.AreEqual("bad-slot", inv.EquipHack("h1", 9).Reason);
            Assert.IsTrue(inv.EquipHack("h1", 0).Ok);
            inv.EquipHack("h1", 2);
            Assert.IsNull(inv.Deck[0]);
            Assert.AreEqual("h1", inv.Deck[2]);
        }

        [Test]
        public void MalformedItemsAreRejectedLoudly()
        {
            var inv = new Inventory(100);
            Assert.Throws<ArgumentException>(() => inv.Add(new Item { Uid = "x" }));
            Item mythic = Stackable("x");
            mythic.Rarity = "mythic";
            Assert.Throws<ArgumentOutOfRangeException>(() => inv.Add(mythic));
            Item none = Stackable("y");
            none.Qty = 0;
            Assert.Throws<ArgumentException>(() => inv.Add(none));
        }

        [Test]
        public void ASaveReferencingAMissingEquippedItemIsRejected()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => new Inventory(items: new Item[0], equipped: new Dictionary<string, string> { ["primary"] = "ghost" }));
            StringAssert.Contains("missing item", ex.Message);
        }

        // ---------------------------------------------------------------- loot

        [Test]
        public void LootIsDeterministicForAFixedSeed()
        {
            string Drop(uint seed) => Json.Write(new LootGenerator(seed).RollDrop(12, 0, 2, 5).Items.Select(i => (object)i.ToJson()).ToList(), false);
            Assert.AreEqual(Drop(20771), Drop(20771));
            Assert.AreNotEqual(Drop(20771), Drop(20772));
        }

        [Test]
        public void ResetReplaysTheSameStream()
        {
            var gen = new LootGenerator(4242);
            string first = Json.Write(gen.RollItem(new LootRoll { Level = 5, Tier = 1 }).ToJson(), false);
            gen.Reset();
            Assert.AreEqual(first, Json.Write(gen.RollItem(new LootRoll { Level = 5, Tier = 1 }).ToJson(), false));
        }

        [Test]
        public void RarityDrivesAffixCountStatScaleAndPrice()
        {
            var gen = new LootGenerator(777);
            Item common = gen.RollItem(new LootRoll { TemplateId = "w_rifle", Rarity = "common", Level = 10, Tier = 1 });
            Item legendary = gen.RollItem(new LootRoll { TemplateId = "w_rifle", Rarity = "legendary", Level = 10, Tier = 1 });
            Assert.AreEqual(0, common.Affixes.Count);
            Assert.AreEqual(4, legendary.Affixes.Count);
            Assert.Greater(legendary.Value, common.Value * 10);
            Assert.IsTrue(legendary.Affixes.All(a => RpgStats.IsDerived(a.Stat)), "affixes modify real derived stats");
        }

        [Test]
        public void AffixesBecomeEquipModifiers()
        {
            Item item = new LootGenerator(31337).RollItem(new LootRoll { TemplateId = "a_jacket", Rarity = "epic", Level = 8, Tier = 1 });
            Assert.AreEqual(item.Affixes.Count, item.Mods.Count);
            CollectionAssert.AreEquivalent(item.Affixes.Select(a => a.Stat), item.Mods.Select(m => m.Stat));
        }

        [Test]
        public void HigherLevelAndLuckPushRarityUpward()
        {
            var low = new LootGenerator(11);
            var high = new LootGenerator(11);
            int lowSum = 0, highSum = 0;
            for (int i = 0; i < 200; i++)
            {
                lowSum += RpgItems.RarityOf(low.RollItem(new LootRoll { Type = "weapon", Level = 1, Luck = 0, Tier = 0 }).Rarity).Order;
                highSum += RpgItems.RarityOf(high.RollItem(new LootRoll { Type = "weapon", Level = 40, Luck = 1, Tier = 3 }).Rarity).Order;
            }
            Assert.Greater(highSum, lowSum);
        }

        [Test]
        public void GeneratedItemsKeepTheirTemplatesStackingRules()
        {
            var gen = new LootGenerator(9);
            for (int i = 0; i < 100; i++)
            {
                Item item = gen.RollItem(new LootRoll { Level = 10, Tier = 1 });
                Assert.LessOrEqual(item.Qty, item.MaxStack, item.Name);
                Assert.GreaterOrEqual(item.Value, 1);
                Assert.GreaterOrEqual(item.Weight, 0);
            }
        }

        [Test]
        public void MakeItemRefusesAnUnknownTemplateRarityOrMissingUid()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RpgItems.MakeItem(() => 0.5, "nope", uid: "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => RpgItems.MakeItem(() => 0.5, "w_pistol", "mythic", uid: "x"));
            Assert.Throws<ArgumentException>(() => RpgItems.MakeItem(() => 0.5, "w_pistol"));
        }

        // ---------------------------------------------------------------- conditions

        [Test]
        public void UnknownConditionKeysAndAttributesThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RpgConditions.Check(new JsonObject { ["nonsense"] = 1.0 }, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => RpgConditions.Check(new JsonObject { ["attribute"] = new JsonObject { ["charisma"] = 4.0 } }, null));
        }

        [Test]
        public void DescribeConditionGivesTheGateLabelTheHudShows()
        {
            Assert.AreEqual("INTELLIGENCE 6", RpgConditions.Describe(new JsonObject { ["attribute"] = new JsonObject { ["intelligence"] = 6.0 } }));
            Assert.AreEqual("5000€$", RpgConditions.Describe(new JsonObject { ["money"] = 5000.0 }));
            Assert.AreEqual("STREET CRED 3", RpgConditions.Describe(new JsonObject { ["streetCred"] = 3.0 }));
        }

        [Test]
        public void EffectsAreCheckedBeforeTheyRun()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RpgEffects.Assert(new JsonObject { ["type"] = "teleport" }));
            Assert.Throws<ArgumentException>(() => RpgEffects.Assert(new JsonObject { ["type"] = "addXp" }));
            Assert.Throws<ArgumentException>(() => RpgEffects.AssertAll(new JsonObject()));
            Assert.AreEqual(0, RpgEffects.AssertAll(null).Count);
            RpgEffects.Assert(new JsonObject { ["type"] = "endDialogue" });
        }

        // ---------------------------------------------------------------- saves

        [Test]
        public void MigrationUpgradesAnOldSaveAndRefusesANewerOne()
        {
            var v1 = (JsonObject)Json.Parse("{\"version\":1,\"money\":4321,\"stats\":{\"attributes\":{\"body\":5},\"xp\":900}}");
            JsonObject o = RpgSave.Migrate(v1);
            Assert.AreEqual((double)RpgSave.Version, o["version"]);
            Assert.AreEqual(4321.0, ((JsonObject)o["economy"])["money"]);
            Assert.IsFalse(o.ContainsKey("money"));
            Assert.AreEqual(0.0, ((JsonObject)o["stats"])["credXp"]);
            StringAssert.Contains("newer than this build", Assert.Throws<InvalidOperationException>(() => RpgSave.Migrate(new JsonObject { ["version"] = 3.0 })).Message);
            StringAssert.Contains("invalid version", Assert.Throws<InvalidOperationException>(() => RpgSave.Migrate(new JsonObject { ["version"] = "one" })).Message);
            Assert.Throws<ArgumentException>(() => RpgSave.Migrate(null));
        }

        [Test]
        public void ACorruptSlotThrowsInsteadOfReadingAsEmpty()
        {
            var storage = new MemorySaveStorage();
            var saves = new SaveManager(storage, () => 0);
            storage.SetItem(saves.Key("bad"), "{not json");
            StringAssert.Contains("corrupt", Assert.Throws<InvalidOperationException>(() => saves.Read("bad")).Message);
            Assert.IsNull(saves.Read("never-written"));
            Assert.Throws<ArgumentNullException>(() => new SaveManager(null, () => 0));
        }

        [Test]
        public void TheSaveManagerListsSlotsNewestFirst()
        {
            double now = 1000;
            var saves = new SaveManager(new MemorySaveStorage(), () => now);
            saves.Write("a", new JsonObject());
            now = 2000;
            saves.Write("b", new JsonObject());
            List<SaveSlotInfo> list = saves.List();
            CollectionAssert.AreEqual(new[] { "b", "a" }, list.Select(s => s.Slot));
            Assert.IsTrue(list.All(s => s.Version == RpgSave.Version));
            saves.Remove("a");
            Assert.AreEqual(1, saves.List().Count);
        }
    }
}
