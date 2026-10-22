using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The lettering on the signs, the LED hoardings and the floating holograms the planner adds.</summary>
    public class NeonSignageTests
    {
        static readonly CityPlan Plan = CityPlan.Generate();
        static readonly NeonAtlasLayout Atlas = NeonAtlas.Layout();
        static readonly CityWindow Near = CityWindow.AroundPlaza(1);
        static readonly CityWindow Whole = CityWindow.AroundPlaza(3);
        static readonly NeonLayout NearLayout = NeonPlanner.Plan(Plan, Near);
        static readonly NeonLayout WholeLayout = NeonPlanner.Plan(Plan, Whole);

        static IEnumerable<NeonQuad> Panels(NeonLayout l) => l.Quads.Where(q => q.Shape == NeonShape.Panel);

        // ---------------------------------------------------------------- lettering

        [Test]
        public void EveryPanelNamesACellThatExists()
        {
            Assert.Greater(Panels(NearLayout).Count(), 100);
            foreach (NeonQuad q in Panels(NearLayout)) Assert.DoesNotThrow(() => { var c = Atlas[q.Cell]; }, $"cell '{q.Cell}'");
        }

        [Test]
        public void NoOtherShapeNamesACell()
        {
            foreach (NeonQuad q in NearLayout.Quads.Where(q => q.Shape != NeonShape.Panel)) Assert.IsNull(q.Cell);
        }

        [Test]
        public void LetteringSuitsTheShapeOfTheSign()
        {
            foreach (NeonQuad q in Panels(NearLayout))
            {
                AtlasCell cell = Atlas[q.Cell];
                double aspect = q.Width / q.Height;
                switch (cell.Group)
                {
                    case SignGroup.Word: Assert.GreaterOrEqual(aspect, 2.0, $"{cell.Name} on a {aspect:0.00} sign"); break;
                    case SignGroup.Kanji: Assert.AreEqual(1.0, aspect, 0.01, cell.Name); break;
                    case SignGroup.Icon: Assert.AreEqual(1.0, aspect, 0.01, cell.Name); Assert.AreEqual(0.65, q.Width, 0.01, cell.Name); break;
                    case SignGroup.Vertical: Assert.Less(aspect, 1.1, $"{cell.Name} on a {aspect:0.00} sign"); break;
                }
            }
        }

        [Test]
        public void EveryKindOfLetteringIsUsed()
        {
            var groups = Panels(WholeLayout).Select(q => Atlas[q.Cell].Group).Distinct().ToList();
            CollectionAssert.AreEquivalent(new[] { SignGroup.Word, SignGroup.Kanji, SignGroup.Vertical, SignGroup.Icon }, groups);
        }

        [Test]
        public void ManyDifferentCellsAreUsed()
        {
            int distinct = Panels(WholeLayout).Select(q => q.Cell).Distinct().Count();
            Assert.GreaterOrEqual(distinct, 40, "the signs do not all say the same thing");
        }

        [Test]
        public void TheTwoFacesOfABladeCarryTheSameLettering()
        {
            var blades = Panels(NearLayout).Where(q => Atlas[q.Cell].Group == SignGroup.Vertical).GroupBy(q => (q.Gx, q.Gz, q.Seed)).Where(g => g.Count() > 1).ToList();
            Assert.Greater(blades.Count, 5);
            foreach (var g in blades) Assert.AreEqual(1, g.Select(q => q.Cell).Distinct().Count(), "both faces of one blade");
        }

        [Test]
        public void ChoosingLetteringNeverMovesAShape()
        {
            // an atlas with one cell per group: every choice is forced, yet nothing else about the signage may change
            var small = new NeonAtlasLayout(2048, new[]
            {
                new AtlasCell { Name = "a", Group = SignGroup.Word, X = 8, Y = 8, W = 384, H = 112 },
                new AtlasCell { Name = "b", Group = SignGroup.Kanji, X = 8, Y = 200, W = 176, H = 176 },
                new AtlasCell { Name = "c", Group = SignGroup.Vertical, X = 8, Y = 400, W = 96, H = 384 },
                new AtlasCell { Name = "d", Group = SignGroup.Icon, X = 8, Y = 900, W = 80, H = 80 },
            });
            NeonLayout other = NeonPlanner.Plan(Plan, Near, NeonPlanner.DefaultSeed, small);
            Assert.AreEqual(NearLayout.Quads.Count, other.Quads.Count);
            for (int i = 0; i < NearLayout.Quads.Count; i++)
            {
                NeonQuad a = NearLayout.Quads[i], b = other.Quads[i];
                b.Cell = a.Cell;
                Assert.AreEqual(a, b, $"quad {i}");
            }
            Assert.AreEqual(NearLayout.Boxes.Count, other.Boxes.Count);
            Assert.AreEqual(NearLayout.Boards.Count, other.Boards.Count);
        }

        [Test]
        public void TheLetteringStreamHasItsOwnSeed()
        {
            NeonLayout other = NeonPlanner.Plan(Plan, Near, NeonPlanner.DefaultSeed + 1);
            Assert.AreNotEqual(Panels(NearLayout).Select(q => q.Cell).ToList(), Panels(other).Select(q => q.Cell).ToList());
        }

        [Test]
        public void ThePlanIsTheSameEveryTime()
        {
            NeonLayout again = NeonPlanner.Plan(Plan, Near);
            Assert.AreEqual(NearLayout.Quads.Count, again.Quads.Count);
            Assert.IsTrue(NearLayout.Quads.SequenceEqual(again.Quads));
            Assert.IsTrue(NearLayout.Boards.SequenceEqual(again.Boards));
            Assert.IsTrue(NearLayout.Holograms.SequenceEqual(again.Holograms));
        }

        // ---------------------------------------------------------------- LED hoardings

        [Test]
        public void ThereAreHoardingsAndTheyAreAllOnTheStreet()
        {
            Assert.GreaterOrEqual(WholeLayout.Boards.Count, 20, "a city this size has plenty");
            Assert.Greater(NearLayout.Boards.Count, 0);
            foreach (NeonBoard b in WholeLayout.Boards)
            {
                Assert.AreEqual(1.0, Math.Sqrt(b.Nx * b.Nx + b.Nz * b.Nz), 1e-9, "faces horizontally");
                Assert.IsTrue(Whole.Contains(b.Gx, b.Gz));
            }
        }

        [Test]
        public void HoardingsHaveTheSizesOfThePrototype()
        {
            foreach (NeonBoard b in WholeLayout.Boards)
            {
                Assert.That(b.Width, Is.InRange(2.0, 20.0));
                Assert.GreaterOrEqual(b.Height, 3.5);
                Assert.LessOrEqual(b.Height / b.Width, 0.86 + 1e-9, "a landscape screen");
                Assert.GreaterOrEqual(b.Y - b.Height / 2, CityLayout.SidewalkHeight + 2.5 - 1e-6, "clear of the shopfronts' end of the wall");
            }
        }

        [Test]
        public void HoardingParametersStayInRange()
        {
            foreach (NeonBoard b in WholeLayout.Boards)
            {
                Assert.That(b.Panel, Is.InRange(0, BoardArt.PanelCount - 1));
                Assert.That(b.Seed, Is.InRange(0.0, 10.0));
                Assert.That(b.Glitch, Is.InRange(0.6, 1.4));
                Assert.That(b.TickerSpeed, Is.InRange(0.05, 0.14));
                Assert.That(b.Gain, Is.InRange(2.2, 3.8));
                foreach (float c in new[] { b.Colour.R, b.Colour.G, b.Colour.B }) Assert.That(c, Is.InRange(0f, 1.0001f), "a screen leans toward a hue from white, never beyond");
            }
        }

        [Test]
        public void EveryPanelOfArtIsShown()
        {
            CollectionAssert.AreEquivalent(Enumerable.Range(0, BoardArt.PanelCount), WholeLayout.Boards.Select(b => b.Panel).Distinct());
        }

        [Test]
        public void EachHoardingSitsInADarkFrame()
        {
            foreach (NeonBoard b in WholeLayout.Boards)
            {
                // the housing box is centred 0.29 m behind the screen, half a metre larger each way
                bool framed = WholeLayout.Boxes.Any(x => x.Gx == b.Gx && x.Gz == b.Gz
                    && Math.Abs(x.X - (b.X - b.Nx * 0.29)) < 1e-6 && Math.Abs(x.Z - (b.Z - b.Nz * 0.29)) < 1e-6 && Math.Abs(x.Y - b.Y) < 1e-6
                    && Math.Abs(x.SizeX - (b.Width + 0.5)) < 1e-6 && Math.Abs(x.SizeY - (b.Height + 0.5)) < 1e-6);
                Assert.IsTrue(framed, $"board at ({b.X:0.0}, {b.Y:0.0}, {b.Z:0.0})");
            }
        }

        [Test]
        public void EachHoardingIsRingedByFourTubesAndWashesTheAirWithLight()
        {
            foreach (NeonBoard b in WholeLayout.Boards.Take(30))
            {
                var near = WholeLayout.Quads.Where(q => q.Gx == b.Gx && q.Gz == b.Gz && Math.Abs(q.Gain - 2.4) < 1e-9 && q.Shape == NeonShape.Tube
                    && Math.Abs(q.X - b.X) < b.Width && Math.Abs(q.Z - b.Z) < b.Width && Math.Abs(q.Y - b.Y) < b.Height).ToList();
                Assert.GreaterOrEqual(near.Count(q => Math.Abs(q.Roll) < 1e-9), 2, "a tube above and one below");
                Assert.GreaterOrEqual(near.Count(q => Math.Abs(q.Roll - Math.PI / 2) < 1e-9), 2, "a tube each side");
                Assert.IsTrue(WholeLayout.Quads.Any(q => q.Shape == NeonShape.Radial && q.Gx == b.Gx && q.Gz == b.Gz && !q.FacingUp && Math.Abs(q.Gain - 0.30) < 1e-9
                    && Math.Abs(q.Y - b.Y) < 1e-9 && Math.Abs(q.X - (b.X + b.Nx * 1.16)) < 1e-6 && Math.Abs(q.Z - (b.Z + b.Nz * 1.16)) < 1e-6), "a glow in front");
            }
        }

        // ---------------------------------------------------------------- holograms

        [Test]
        public void ThereAreUpToSixteenHolograms()
        {
            Assert.That(WholeLayout.Holograms.Count, Is.InRange(8, NeonPlanner.HologramAttempts));
        }

        [Test]
        public void HologramsHangInOpenAirBesideTallBuildings()
        {
            var tall = Plan.Buildings.Where(b => b.Height > NeonPlanner.HologramMinHeight).ToList();
            foreach (NeonBoard h in WholeLayout.Holograms)
            {
                Assert.IsFalse(Plan.IsBlocked(h.X, h.Z), "not inside another lot");
                Assert.AreEqual(1.0, Math.Abs(h.Nx) + Math.Abs(h.Nz), 1e-9, "faces along an axis");
                Assert.That(h.Y, Is.InRange(14.0, 38.0));
                Assert.That(h.Width, Is.InRange(7.0, 16.0));
                Assert.That(h.Height / h.Width, Is.InRange(0.7, 1.4));
                Assert.That(h.Gain, Is.InRange(1.1, 1.9));
                Assert.That(h.Glitch, Is.InRange(1.1, 2.0));

                bool beside = tall.Any(b =>
                {
                    double half = h.Nx != 0 ? b.Width / 2 : b.Depth / 2;
                    double along = h.Nx != 0 ? Math.Abs(h.X - b.X) : Math.Abs(h.Z - b.Z);
                    double across = h.Nx != 0 ? Math.Abs(h.Z - b.Z) : Math.Abs(h.X - b.X);
                    return across < 1e-6 && along >= half + 5 - 1e-6 && along <= half + 10 + 1e-6 && b.Lot.Gx == h.Gx && b.Lot.Gz == h.Gz;
                });
                Assert.IsTrue(beside, $"hologram at ({h.X:0.0}, {h.Z:0.0})");
            }
        }

        [Test]
        public void EachHologramHasAGlowOfItsOwn()
        {
            foreach (NeonBoard h in WholeLayout.Holograms)
            {
                Assert.IsTrue(WholeLayout.Quads.Any(q => q.Shape == NeonShape.Radial && Math.Abs(q.Gain - 0.22) < 1e-9 && Math.Abs(q.X - h.X) < 1e-9 && Math.Abs(q.Y - h.Y) < 1e-9 && Math.Abs(q.Z - h.Z) < 1e-9));
            }
        }

        [Test]
        public void OnlyHologramsInTheWindowAreMade()
        {
            foreach (NeonBoard h in NearLayout.Holograms) Assert.IsTrue(Near.Contains(h.Gx, h.Gz));
        }

        [Test]
        public void ThePlazaAloneHasNoBuildingsToHangAnythingOn()
        {
            NeonLayout plaza = NeonPlanner.Plan(Plan, CityWindow.AroundPlaza(0));
            Assert.AreEqual(0, plaza.Holograms.Count);
            Assert.AreEqual(0, plaza.Boards.Count);
        }

        [Test]
        public void NullPlanFailsLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => NeonPlanner.Plan(null, Near));
        }
    }
}
