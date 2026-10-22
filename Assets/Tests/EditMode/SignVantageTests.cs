using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class SignVantageTests
    {
        static readonly CityPlan Plan = CityPlan.Generate();
        static readonly NeonAtlasLayout Atlas = NeonAtlas.Layout();
        static readonly NeonLayout Layout = NeonPlanner.Plan(Plan, CityWindow.AroundPlaza(3), NeonPlanner.DefaultSeed, Atlas);
        static readonly System.Collections.Generic.IReadOnlyList<SignShot> Shots = SignVantage.Pick(Layout, Atlas, Plan.IsBlocked);

        [Test]
        public void EveryKindOfSignHasAShot()
        {
            CollectionAssert.AreEquivalent(
                new[] { "sign_board", "sign_board_close", "sign_hologram", "sign_shop", "sign_plaque", "sign_blade" },
                Shots.Select(s => s.Name));
        }

        [Test]
        public void TheCameraStandsInOpenAirAtStreetLevelOrBelowTheSign()
        {
            foreach (SignShot s in Shots)
            {
                (double x, double y, double z) = s.CameraPosition(1.7);
                Assert.IsFalse(Plan.IsBlocked(x, z), $"{s.Name} would put the camera inside a lot");
                Assert.LessOrEqual(y, 1.7 + 1e-9);
                Assert.LessOrEqual(y, s.Y + 1e-9);
                Assert.AreEqual(1.0, Math.Sqrt(s.Nx * s.Nx + s.Nz * s.Nz), 1e-9);
                Assert.GreaterOrEqual(s.Distance, 2.0);
            }
        }

        [Test]
        public void TheDistanceIsNeverMoreThanAskedFor()
        {
            double[] asked = { 24, 11, 34, 5.5, 3.2, 6 };
            string[] names = { "sign_board", "sign_board_close", "sign_hologram", "sign_shop", "sign_plaque", "sign_blade" };
            for (int i = 0; i < names.Length; i++) Assert.LessOrEqual(Shots.First(s => s.Name == names[i]).Distance, asked[i] + 1e-9, names[i]);
        }

        [Test]
        public void TheHoardingChosenIsTheNearestToThePlaza()
        {
            SignShot shot = Shots.First(s => s.Name == "sign_board");
            double chosen = shot.X * shot.X + shot.Z * shot.Z;
            Assert.AreEqual(Layout.Boards.Min(b => b.X * b.X + b.Z * b.Z), chosen, 1e-9);
        }

        [Test]
        public void TheShopSignChosenIsAWordBannerAndTheBladeIsVertical()
        {
            SignShot shop = Shots.First(s => s.Name == "sign_shop"), blade = Shots.First(s => s.Name == "sign_blade");
            NeonQuad shopQuad = Layout.Quads.First(q => q.Shape == NeonShape.Panel && q.X == shop.X && q.Y == shop.Y && q.Z == shop.Z);
            NeonQuad bladeQuad = Layout.Quads.First(q => q.Shape == NeonShape.Panel && q.X == blade.X && q.Y == blade.Y && q.Z == blade.Z);
            Assert.AreEqual(SignGroup.Word, Atlas[shopQuad.Cell].Group);
            Assert.AreEqual(SignGroup.Vertical, Atlas[bladeQuad.Cell].Group);
        }

        [Test]
        public void ALayoutWithoutBoardsGivesNoBoardShots()
        {
            var empty = new NeonLayout();
            Assert.IsEmpty(SignVantage.Pick(empty, Atlas, (x, z) => false));
        }

        [Test]
        public void StandingStopsBeforeABlockedPoint()
        {
            var layout = new NeonLayout();
            layout.Boards.Add(new NeonBoard { X = 0, Y = 10, Z = 0, Nx = 0, Nz = 1, Width = 8, Height = 4 });
            SignShot s = SignVantage.Pick(layout, Atlas, (x, z) => z >= 7.5).First(shot => shot.Name == "sign_board");
            Assert.AreEqual(7.0, s.Distance, 1e-9, "the last whole metre before the wall");
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => SignVantage.Pick(null, Atlas, Plan.IsBlocked));
            Assert.Throws<ArgumentNullException>(() => SignVantage.Pick(Layout, null, Plan.IsBlocked));
            Assert.Throws<ArgumentNullException>(() => SignVantage.Pick(Layout, Atlas, null));
            var layout = new NeonLayout();
            layout.Boards.Add(new NeonBoard { Nx = 0, Nz = 0, Width = 1, Height = 1 });
            Assert.Throws<ArgumentException>(() => SignVantage.Pick(layout, Atlas, (x, z) => false));
        }
    }
}
