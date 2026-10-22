using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class NeonPlannerTests
    {
        static readonly CityPlan Plan = CityPlan.Generate();
        static readonly CityWindow Window = CityWindow.AroundPlaza(1);
        static readonly NeonLayout Layout = NeonPlanner.Plan(Plan, Window);

        [Test]
        public void PlacesAGoodQuantityOfSignageInTheSurroundingCells()
        {
            // Measured for the default seed: ~1,800 quads, ~600 housing boxes.
            Assert.That(Layout.Quads.Count, Is.InRange(800, 6000));
            Assert.That(Layout.Boxes.Count, Is.InRange(200, 3000));
            Assert.IsTrue(Layout.Quads.All(q => Window.Contains(q.Gx, q.Gz)));
            Assert.IsFalse(Layout.Quads.Any(q => q.Gx == CityLayout.PlazaIndex && q.Gz == CityLayout.PlazaIndex), "the plaza owns no signage");
            Assert.AreEqual(8, Layout.Quads.Select(q => (q.Gx, q.Gz)).Distinct().Count(), "every built cell gets some");
        }

        [Test]
        public void EveryShapeIsUsed()
        {
            foreach (NeonShape shape in Enum.GetValues(typeof(NeonShape)))
            {
                Assert.IsTrue(Layout.Quads.Any(q => q.Shape == shape), $"no {shape} quads were placed");
            }
        }

        [Test]
        public void IsDeterministicAndSeedSensitive()
        {
            NeonLayout again = NeonPlanner.Plan(Plan, Window);
            Assert.AreEqual(Layout.Quads.Count, again.Quads.Count);
            Assert.AreEqual(Layout.Boxes.Count, again.Boxes.Count);
            for (int i = 0; i < Layout.Quads.Count; i++)
            {
                Assert.AreEqual(Layout.Quads[i].X, again.Quads[i].X);
                Assert.AreEqual(Layout.Quads[i].Seed, again.Quads[i].Seed);
            }

            NeonLayout other = NeonPlanner.Plan(Plan, Window, 12345);
            Assert.AreNotEqual(Layout.Quads.Count, other.Quads.Count);
        }

        [Test]
        public void WallQuadsFaceHorizontallyAndPoolsLieFlatOnTheDeck()
        {
            foreach (NeonQuad q in Layout.Quads)
            {
                if (q.FacingUp)
                {
                    Assert.AreEqual(NeonPlanner.PoolY, q.Y, 1e-9);
                    Assert.AreEqual(NeonShape.Radial, q.Shape);
                    Assert.AreEqual(q.Width, q.Height, 1e-9, "pools are square quads");
                }
                else
                {
                    Assert.AreEqual(1.0, Math.Sqrt(q.Nx * q.Nx + q.Nz * q.Nz), 1e-9, "unit horizontal facing");
                    Assert.Greater(q.Y, CityLayout.SidewalkHeight, "wall fittings sit above the kerb");
                }
            }
        }

        [Test]
        public void ParametersStayInRange()
        {
            foreach (NeonQuad q in Layout.Quads)
            {
                Assert.Greater(q.Width, 0);
                Assert.Greater(q.Height, 0);
                Assert.That(q.Gain, Is.InRange(0.2, 6.0));
                Assert.That(q.FlickerMode, Is.InRange(0, 4));
                Assert.That(q.Seed, Is.InRange(0.0, 10.0));
                Assert.GreaterOrEqual(q.Chase, 0);
                Assert.That(q.Colour.R + q.Colour.G + q.Colour.B, Is.GreaterThan(0.1f), "colours are bright neon, never black");
            }
        }

        [Test]
        public void ColoursComeFromTheDistrictPalettes()
        {
            var palette = NeonDistricts.All.SelectMany(d => d.Colours).ToList();
            foreach (NeonQuad q in Layout.Quads)
            {
                // the glow washes round the LED screens are white leaned toward a district hue
                Assert.IsTrue(palette.Any(c => (c.R == q.Colour.R && c.G == q.Colour.G && c.B == q.Colour.B) || LeansToward(q.Colour, c)), "unknown neon colour");
            }
            Assert.GreaterOrEqual(Layout.Quads.Select(q => (q.Colour.R, q.Colour.G, q.Colour.B)).Distinct().Count(), 6, "a varied palette");
        }

        /// <summary>Whether a colour is white moved some share (not zero, not all) of the way to a hue.</summary>
        static bool LeansToward(Rgb colour, Rgb hue)
        {
            double share = double.NaN;
            foreach ((float c, float h) in new[] { (colour.R, hue.R), (colour.G, hue.G), (colour.B, hue.B) })
            {
                if (Math.Abs(h - 1f) < 0.02f) { if (Math.Abs(c - 1f) > 0.002f) return false; continue; }
                double s = (c - 1.0) / (h - 1.0);
                if (s < 0.2 || s > 0.8) return false;
                if (!double.IsNaN(share) && Math.Abs(s - share) > 0.002) return false;
                share = s;
            }
            return !double.IsNaN(share);
        }

        [Test]
        public void ChaseOnlyAppearsOnTubesAndBulbs()
        {
            foreach (NeonQuad q in Layout.Quads.Where(q => q.Chase > 0))
            {
                Assert.That(q.Shape, Is.EqualTo(NeonShape.Tube).Or.EqualTo(NeonShape.TubeSoft).Or.EqualTo(NeonShape.Bulbs));
            }
            Assert.IsTrue(Layout.Quads.Any(q => q.Chase > 0), "some tubes chase");
        }

        [Test]
        public void SignageSpansGroundFloorToTowerTops()
        {
            Assert.IsTrue(Layout.Quads.Any(q => !q.FacingUp && q.Y < 6), "shopfront level");
            Assert.IsTrue(Layout.Quads.Any(q => q.Y > 15), "upper-floor bands, edge tubes and signs");
        }

        [Test]
        public void VerticalTubesAreRolledAHalfTurn()
        {
            var vertical = Layout.Quads.Where(q => Math.Abs(q.Roll - Math.PI / 2) < 1e-9).ToList();
            Assert.IsNotEmpty(vertical);
            Assert.IsTrue(vertical.All(q => q.Shape == NeonShape.Tube));
            Assert.IsTrue(Layout.Quads.Where(q => q.Roll != 0).All(q => Math.Abs(q.Roll - Math.PI / 2) < 1e-9), "only quarter-turn rolls exist");
        }

        [Test]
        public void HousingBoxesHavePositiveSizeAndAUnitDirection()
        {
            foreach (NeonBox b in Layout.Boxes)
            {
                Assert.Greater(b.SizeX, 0);
                Assert.Greater(b.SizeY, 0);
                Assert.Greater(b.SizeZ, 0);
                Assert.AreEqual(1.0, Math.Sqrt(b.DirX * b.DirX + b.DirZ * b.DirZ), 1e-9);
                Assert.Greater(b.Y, CityLayout.SidewalkHeight);
            }
        }

        [Test]
        public void BiggerWindowGivesMoreSignage()
        {
            Assert.Greater(NeonPlanner.Plan(Plan, CityWindow.AroundPlaza(2)).Quads.Count, Layout.Quads.Count);
            Assert.AreEqual(0, NeonPlanner.Plan(Plan, CityWindow.AroundPlaza(0)).Quads.Count);
        }

        [Test]
        public void NullPlanFailsLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => NeonPlanner.Plan(null, Window));
        }
    }
}
