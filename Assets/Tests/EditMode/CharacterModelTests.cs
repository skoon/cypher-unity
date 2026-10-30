using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The character sheet's view of the core: progress, attributes and their points, the real perk trees with reasons, and the chrome by slot.</summary>
    public class CharacterModelTests
    {
        static RpgCore Core()
        {
            var core = new RpgCore(null, 20771, new MemorySaveStorage(), () => 1700000000000) { AutosaveEnabled = false };
            core.GiveStartingKit();
            return core;
        }

        [Test]
        public void ANewCharacterHasFiveAttributesAndNoPointsToSpend()
        {
            RpgCore core = Core();
            CharacterModel.Sheet s = CharacterModel.Read(core);
            Assert.AreEqual(1, s.Level);
            Assert.AreEqual(RpgStats.LevelCap, s.LevelCap);
            CollectionAssert.AreEqual(RpgStats.Attributes, s.Attributes.Select(a => a.Key));
            Assert.IsTrue(s.Attributes.All(a => a.Label == a.Label.ToUpperInvariant() && a.Max == RpgStats.AttrMax));
            Assert.AreEqual(core.Stats.AttributePoints, s.AttributePoints);
            if (s.AttributePoints == 0) Assert.IsTrue(s.Attributes.All(a => a.Blocked == "NO POINTS"));
            Assert.That(s.XpShare, Is.InRange(0.0, 1.0));
        }

        [Test]
        public void LevellingUpGivesPointsThatRaiseAnAttribute()
        {
            RpgCore core = Core();
            core.AddXp(5000);
            CharacterModel.Sheet s = CharacterModel.Read(core);
            Assert.Greater(s.Level, 1);
            Assert.Greater(s.AttributePoints, 0);
            AttributeView body = s.Attributes.Single(a => a.Key == "body");
            Assert.IsTrue(body.CanRaise);
            Assert.IsTrue(core.SpendAttributePoint("body").Ok);
            Assert.AreEqual(body.Value + 1, CharacterModel.Read(core).Attributes.Single(a => a.Key == "body").Value);
        }

        [Test]
        public void EachAttributeHasItsRealPerkTreeInTierOrderWithReasons()
        {
            RpgCore core = Core();
            foreach (string a in RpgStats.Attributes)
            {
                var tree = CharacterModel.Tree(core, a);
                Assert.IsNotEmpty(tree, a);
                CollectionAssert.AreEqual(tree.Select(p => p.Tier).OrderBy(t => t), tree.Select(p => p.Tier), a);
                Assert.IsTrue(tree.All(p => p.Rank == 0), "a new character owns no perk");
                Assert.IsTrue(tree.All(p => (p.Blocked == null) == core.CanBuyPerk(p.Id).Ok), "the reason follows the core's rule");
                if (core.Stats.PerkPoints == 0) Assert.IsTrue(tree.All(p => p.State == "LOCKED"));
            }
            Assert.Throws<System.ArgumentOutOfRangeException>(() => CharacterModel.Tree(core, "luck"));
        }

        [Test]
        public void APerkThatCanBeBoughtSaysWhatItCostsAndBuyingItRanksItUp()
        {
            RpgCore core = Core();
            core.AddXp(20000);
            PerkView buyable = RpgStats.Attributes.SelectMany(a => CharacterModel.Tree(core, a)).FirstOrDefault(p => p.Blocked == null);
            Assert.IsNotNull(buyable, "with points, a first-tier perk is open");
            Assert.AreEqual("AVAILABLE", buyable.State);
            StringAssert.StartsWith("UNLOCK — ", CharacterModel.BuyLabel(buyable));
            Assert.IsTrue(core.BuyPerk(buyable.Id).Ok);
            PerkView after = RpgStats.Attributes.SelectMany(a => CharacterModel.Tree(core, a)).Single(p => p.Id == buyable.Id);
            Assert.AreEqual(1, after.Rank);
            Assert.IsTrue(after.Owned);
        }

        [Test]
        public void ChromeIsListedBySlotAgainstTheBodysCapacity()
        {
            RpgCore core = Core();
            (int used0, int cap, var slots0) = CharacterModel.Chrome(core);
            Assert.AreEqual(0, used0);
            Assert.Greater(cap, 0);
            Assert.IsNotEmpty(slots0);
            Assert.IsTrue(core.InstallCyberware("cw_kiroshi_mk1").Ok);
            (int used, _, var slots) = CharacterModel.Chrome(core);
            Assert.AreEqual(RpgCyberware.Get("cw_kiroshi_mk1").Capacity, used);
            ChromeSlotView ocular = slots.Single(s => s.Key == "ocular");
            Assert.AreEqual("cw_kiroshi_mk1", ocular.Installed.Single().Id);
        }
    
        [Test]
        public void APerksTextSaysWhatOneRankGives()
        {
            Assert.AreEqual("Thicker meat. +25 max health. (per rank)", CharacterModel.Describe(RpgPerks.Get("bulk")));
            Assert.AreEqual("Hands ahead of thought. +6% ranged damage. (per rank)", CharacterModel.Describe(RpgPerks.Get("quickdraw")));
            Assert.AreEqual("Rain never quite lands on you. +12 stamina, +4 stealth. (per rank)", CharacterModel.Describe(RpgPerks.Get("slippery")));
            foreach (Perk p in RpgPerks.All) StringAssert.DoesNotContain("{n}", CharacterModel.Describe(p), p.Id);
        }
}
}
