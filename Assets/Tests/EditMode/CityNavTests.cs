using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class CityNavTests
    {
        static readonly CityPlan Plan = CityPlan.Generate();
        static readonly CityNav Nav = CityNav.Build(Plan);

        static StreetCell Street(int gx, int gz, double cx, double cz) => new StreetCell { Gx = gx, Gz = gz, Cx = cx, Cz = cz };

        /// <summary>A grid of columns by rows, cells <paramref name="cell"/> apart, with the first block's centre at the origin.</summary>
        static List<StreetCell> Grid(int columns, int rows, double cell = 62)
        {
            var list = new List<StreetCell>();
            for (int gz = 0; gz < rows; gz++)
            {
                for (int gx = 0; gx < columns; gx++) list.Add(Street(gx, gz, gx * cell, gz * cell));
            }
            return list;
        }

        static readonly NavGeometry Geometry62 = new NavGeometry(62, 46, 25.5, 0.185, 0);

        // ---------------------------------------------------------------- the real city

        [Test]
        public void TheCityMeasuresWhatItsGeometryAndThePrototypeSay()
        {
            Assert.AreEqual(49, Nav.Blocks.Count);
            Assert.AreEqual(49 * 8, Nav.NodeCount);
            Assert.AreEqual(62.0, Nav.Cell);
            Assert.AreEqual(23.0, Nav.BlockHalf);
            Assert.AreEqual(25.5, Nav.WalkOuter);
            Assert.AreEqual(0.185, Nav.WalkY);
            Assert.AreEqual(0.0, Nav.RoadY);
            Assert.AreEqual(5.5, Nav.AsphaltHalf, 1e-12, "the 16 m road less a 2.5 m pavement on each side leaves 11 m of asphalt");
            Assert.AreEqual(24.25, Nav.RingRadius, 1e-12, "the middle of the band from 0.45 m past the block to 0.45 m inside the kerb");
            Assert.AreEqual(0.8, Nav.WalkBand, 1e-12);
        }

        [Test]
        public void TheRoadCentrelinesAreTheCitysRoadCentres()
        {
            Assert.AreEqual(CityLayout.Grid + 1, Nav.XLines.Count);
            Assert.AreEqual(CityLayout.Grid + 1, Nav.ZLines.Count);
            for (int i = 0; i <= CityLayout.Grid; i++)
            {
                Assert.AreEqual(CityLayout.RoadCentre(i), Nav.XLines[i], 1e-9, $"x line {i}");
                Assert.AreEqual(CityLayout.RoadCentre(i), Nav.ZLines[i], 1e-9, $"z line {i}");
            }
        }

        [Test]
        public void EveryRingNodeLiesOnThePavementBandOfItsBlock()
        {
            for (int n = 0; n < Nav.NodeCount; n++)
            {
                NavBlock b = Nav.Blocks[Nav.NodeBlock(n)];
                Assert.AreEqual(n / 8, Nav.NodeBlock(n));
                double dx = Math.Abs(Nav.NodeX(n) - b.Cx), dz = Math.Abs(Nav.NodeZ(n) - b.Cz);
                Assert.AreEqual(Nav.RingRadius, Math.Max(dx, dz), 1e-9, $"node {n} is on the ring");
                Assert.AreEqual(Nav.WalkY, Nav.GroundY(Nav.NodeX(n), Nav.NodeZ(n)), "and the ground there is the pavement");
                Assert.That(Nav.KerbDistance(Nav.NodeX(n), Nav.NodeZ(n)), Is.InRange(Nav.BlockHalf + 0.45, Nav.WalkOuter - 0.45), "inside the walkable band");
            }
        }

        [Test]
        public void TheEightNodesRoundABlockAreInCyclicOrder()
        {
            NavBlock b = Nav.Blocks[0];
            double r = Nav.RingRadius;
            (double, double)[] expect =
            {
                (-r, -r), (0, -r), (r, -r), (r, 0), (r, r), (0, r), (-r, r), (-r, 0),
            };
            for (int k = 0; k < 8; k++)
            {
                Assert.AreEqual(b.Cx + expect[k].Item1, Nav.NodeX(k), 1e-9, $"x of node {k}");
                Assert.AreEqual(b.Cz + expect[k].Item2, Nav.NodeZ(k), 1e-9, $"z of node {k}");
            }
        }

        [Test]
        public void EveryNodeStartsWithItsTwoRingNeighbours()
        {
            for (int n = 0; n < Nav.NodeCount; n++)
            {
                int b = n / 8, k = n % 8;
                IReadOnlyList<int> nb = Nav.Neighbours(n);
                Assert.GreaterOrEqual(nb.Count, 2);
                Assert.AreEqual(b * 8 + (k + 7) % 8, nb[0], $"node {n} previous");
                Assert.AreEqual(b * 8 + (k + 1) % 8, nb[1], $"node {n} next");
                Assert.AreEqual(Crossing.None, Nav.NeighbourCrossings(n)[0]);
                Assert.AreEqual(Crossing.None, Nav.NeighbourCrossings(n)[1]);
                Assert.AreEqual(nb.Count, Nav.NeighbourCrossings(n).Count);
            }
        }

        [Test]
        public void TheGraphIsSymmetricAndCrossingsAreTheSameBothWays()
        {
            for (int a = 0; a < Nav.NodeCount; a++)
            {
                for (int e = 0; e < Nav.Neighbours(a).Count; e++)
                {
                    int b = Nav.Neighbours(a)[e];
                    int back = Nav.Neighbours(b).ToList().IndexOf(a);
                    Assert.GreaterOrEqual(back, 0, $"{b} does not lead back to {a}");
                    Assert.AreEqual(Nav.NeighbourCrossings(a)[e], Nav.NeighbourCrossings(b)[back], $"edge {a}-{b}");
                }
            }
        }

        [Test]
        public void ThereIsACrosswalkBetweenEveryPairOfNeighbouringBlocks()
        {
            int x = 0, z = 0;
            for (int a = 0; a < Nav.NodeCount; a++)
            {
                for (int e = 0; e < Nav.Neighbours(a).Count; e++)
                {
                    if (Nav.NeighbourCrossings(a)[e] == Crossing.X) x++;
                    if (Nav.NeighbourCrossings(a)[e] == Crossing.Z) z++;
                }
            }
            Assert.AreEqual(2 * 7 * 6, x, "7 columns, 6 gaps between rows, both directions");
            Assert.AreEqual(2 * 6 * 7, z, "6 gaps between columns, 7 rows, both directions");
        }

        [Test]
        public void ACrosswalkRunsStraightAcrossTheRoadItNamesAndEndsOnTheOppositeKerb()
        {
            for (int a = 0; a < Nav.NodeCount; a++)
            {
                for (int e = 0; e < Nav.Neighbours(a).Count; e++)
                {
                    Crossing kind = Nav.NeighbourCrossings(a)[e];
                    if (kind == Crossing.None) continue;
                    int b = Nav.Neighbours(a)[e];
                    double dx = Nav.NodeX(b) - Nav.NodeX(a), dz = Nav.NodeZ(b) - Nav.NodeZ(a);
                    double gap = Nav.Cell - 2 * Nav.RingRadius;
                    if (kind == Crossing.X)
                    {
                        Assert.AreEqual(0.0, dx, 1e-9, "walks along Z");
                        Assert.AreEqual(gap, Math.Abs(dz), 1e-9);
                    }
                    else
                    {
                        Assert.AreEqual(0.0, dz, 1e-9, "walks along X");
                        Assert.AreEqual(gap, Math.Abs(dx), 1e-9);
                    }
                    double mx = (Nav.NodeX(a) + Nav.NodeX(b)) / 2, mz = (Nav.NodeZ(a) + Nav.NodeZ(b)) / 2;
                    Assert.IsTrue(CityLayout.IsOnRoad(mx, mz), "the middle of a crossing is in the road");
                    // the traffic it crosses runs perpendicular to the walk: along X for a walk along Z
                    double along = kind == Crossing.X ? mz : mx;
                    var lines = kind == Crossing.X ? Nav.ZLines : Nav.XLines;
                    Assert.IsTrue(lines.Any(l => Math.Abs(l - along) < 1e-9), "and it is on a road centreline");
                }
            }
        }

        [Test]
        public void EdgeBlocksHaveNoCrosswalkOffTheEdgeOfTheGrid()
        {
            // the first block (gx 0, gz 0) has no south neighbour and its west neighbour is the grid's edge: only its east and north blocks link to it
            var linked = new List<Crossing>();
            for (int k = 0; k < 8; k++) linked.AddRange(Nav.NeighbourCrossings(k).Skip(2));
            Assert.AreEqual(2, linked.Count(c => c != Crossing.None), "a corner block has two crosswalks");
        }

        [Test]
        public void KerbDistanceMatchesTheBruteForceDefinitionEverywhere()
        {
            var rng = new Rng(2026);
            for (int i = 0; i < 4000; i++)
            {
                double x = (rng.Next() - 0.5) * 600, z = (rng.Next() - 0.5) * 600;
                double brute = Nav.Blocks.Min(b => Math.Max(Math.Abs(x - b.Cx), Math.Abs(z - b.Cz)));
                Assert.AreEqual(brute, Nav.KerbDistance(x, z), 1e-9, $"({x:0.0}, {z:0.0})");
            }
        }

        [Test]
        public void GroundIsPavementInsideTheSlabAndAsphaltOutsideIt()
        {
            Assert.AreEqual(Nav.WalkY, Nav.GroundY(0, 0), "the plaza block");
            Assert.AreEqual(Nav.WalkY, Nav.GroundY(Nav.WalkOuter, 0), "right on the kerb");
            Assert.AreEqual(Nav.RoadY, Nav.GroundY(Nav.WalkOuter + 0.01, 0), "just off it");
            foreach (double line in Nav.XLines) Assert.AreEqual(Nav.RoadY, Nav.GroundY(line, Nav.ZLines[2]), "the middle of a road");
        }

        [Test]
        public void PavementHeightAgreesWithTheCitysOwnRoadTest()
        {
            var rng = new Rng(77);
            for (int i = 0; i < 3000; i++)
            {
                double x = CityLayout.Base + rng.Next() * CityLayout.Grid * CityLayout.Cell, z = CityLayout.Base + rng.Next() * CityLayout.Grid * CityLayout.Cell;
                bool block = !CityLayout.IsOnRoad(x, z);
                if (block) Assert.AreEqual(Nav.WalkY, Nav.GroundY(x, z), $"({x:0.0}, {z:0.0}) is in a block");
            }
        }

        // ---------------------------------------------------------------- nodeInBand

        [Test]
        public void NodeInBandFindsANodeInTheAnnulusWhenOneExists()
        {
            var rng = new Rng(5);
            for (int i = 0; i < 100; i++)
            {
                int n = Nav.NodeInBand(rng, 0, 0, 60, 120);
                double d = Math.Sqrt(Nav.NodeX(n) * Nav.NodeX(n) + Nav.NodeZ(n) * Nav.NodeZ(n));
                Assert.That(d, Is.InRange(60.0, 120.0));
            }
        }

        [Test]
        public void NodeInBandIsDeterministic()
        {
            var a = new Rng(11);
            var b = new Rng(11);
            for (int i = 0; i < 50; i++) Assert.AreEqual(Nav.NodeInBand(a, 10, -20, 30, 90), Nav.NodeInBand(b, 10, -20, 30, 90));
        }

        [Test]
        public void NodeInBandFallsBackWhenTheBandIsEmpty()
        {
            var rng = new Rng(1);
            int n = Nav.NodeInBand(rng, 5000, 5000, 0, 1);   // nothing is anywhere near
            Assert.That(n, Is.InRange(0, Nav.NodeCount - 1));
            int inside = Nav.NodeInBand(new Rng(3), 0, 0, 1000, 1001, tries: 1);   // nothing is that far, but plenty are closer
            Assert.That(inside, Is.InRange(0, Nav.NodeCount - 1));
        }

        [Test]
        public void TheFallbackIsInsideTheOuterRadiusWhenTheDrawsFindANodeThere()
        {
            // the band's inner edge is further out than any node, but nodes inside the outer radius abound: the fallback is one of them
            var rng = new Rng(9);
            int n = Nav.NodeInBand(rng, 0, 0, 250, 260, tries: 200);
            double d = Math.Sqrt(Nav.NodeX(n) * Nav.NodeX(n) + Nav.NodeZ(n) * Nav.NodeZ(n));
            Assert.LessOrEqual(d, 260 + 1e-9);
        }

        // ---------------------------------------------------------------- other layouts

        [Test]
        public void ASingleBlockHasARoadOnEachSide()
        {
            CityNav nav = CityNav.Build(Grid(1, 1), Geometry62);
            CollectionAssert.AreEqual(new[] { -31.0, 31.0 }, nav.XLines);
            CollectionAssert.AreEqual(new[] { -31.0, 31.0 }, nav.ZLines);
            Assert.AreEqual(8, nav.NodeCount);
            for (int n = 0; n < 8; n++) Assert.AreEqual(2, nav.Neighbours(n).Count, "no crosswalks with no neighbour");
        }

        [Test]
        public void ARectangularGridGetsTheRightNumberOfLinesAndCrossings()
        {
            CityNav nav = CityNav.Build(Grid(3, 2), Geometry62);
            Assert.AreEqual(4, nav.XLines.Count);
            Assert.AreEqual(3, nav.ZLines.Count);
            CollectionAssert.AreEqual(new[] { -31.0, 31.0, 93.0, 155.0 }, nav.XLines);
            CollectionAssert.AreEqual(new[] { -31.0, 31.0, 93.0 }, nav.ZLines);
            int crossings = 0;
            for (int n = 0; n < nav.NodeCount; n++) crossings += nav.NeighbourCrossings(n).Count(c => c != Crossing.None);
            Assert.AreEqual(2 * (3 * 1 + 2 * 2), crossings, "3 north-south pairs and 4 east-west pairs, both directions");
        }

        [Test]
        public void TheOrderOfTheBlocksDecidesTheNodeNumbering()
        {
            List<StreetCell> reversed = Grid(2, 2);
            reversed.Reverse();
            CityNav nav = CityNav.Build(reversed, Geometry62);
            Assert.AreEqual(1, nav.Blocks[0].Gx);
            Assert.AreEqual(1, nav.Blocks[0].Gz);
            Assert.AreEqual(62 + nav.RingRadius, nav.NodeX(2), 1e-9, "node 2 is the corner at +x, -z of the first block listed, which is at x = 62");
        }

        [Test]
        public void ASmallerGeometryChangesTheBand()
        {
            var g = new NavGeometry(40, 28, 16, 0.2, 0.05);
            CityNav nav = CityNav.Build(Grid(2, 2, 40), g);
            Assert.AreEqual(14.0, nav.BlockHalf);
            Assert.AreEqual(4.0, nav.AsphaltHalf, 1e-12, "20 - 16");
            Assert.AreEqual((14.45 + 15.55) / 2, nav.RingRadius, 1e-12);
            Assert.AreEqual(0.2, nav.GroundY(0, 0));
            Assert.AreEqual(0.05, nav.GroundY(20, 20));
        }

        [Test]
        public void AVeryNarrowRoadStillLeavesRoomForACar()
        {
            var g = new NavGeometry(62, 46, 30, 0.185, 0);   // only a metre of asphalt left
            CityNav nav = CityNav.Build(Grid(2, 2), g);
            Assert.AreEqual(2.6, nav.AsphaltHalf, "never narrower than 2.6 m");
        }

        // ---------------------------------------------------------------- bad input

        [Test]
        public void BadInputFailsLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => CityNav.Build((CityPlan)null));
            Assert.Throws<ArgumentNullException>(() => CityNav.Build((IReadOnlyList<StreetCell>)null, Geometry62));
            Assert.Throws<ArgumentException>(() => CityNav.Build(new List<StreetCell>(), Geometry62));

            List<StreetCell> twice = Grid(2, 1);
            twice.Add(Street(1, 0, 62, 0));
            Assert.Throws<ArgumentException>(() => CityNav.Build(twice, Geometry62), "two blocks in one cell");

            List<StreetCell> holey = Grid(2, 2);
            holey.RemoveAt(3);
            Assert.Throws<ArgumentException>(() => CityNav.Build(holey, Geometry62), "a missing block is not a full grid");

            Assert.Throws<ArgumentNullException>(() => Nav.NodeInBand(null, 0, 0, 1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => Nav.NodeInBand(new Rng(1), 0, 0, 5, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => Nav.NodeInBand(new Rng(1), 0, 0, -1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => Nav.NodeInBand(new Rng(1), 0, 0, 1, 2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Nav.NodeX(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Nav.NodeZ(Nav.NodeCount));
            Assert.Throws<ArgumentOutOfRangeException>(() => Nav.NodeBlock(Nav.NodeCount));
            Assert.Throws<ArgumentOutOfRangeException>(() => Nav.Neighbours(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Nav.NeighbourCrossings(Nav.NodeCount));
        }

        [Test]
        public void BadGeometryFailsLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NavGeometry(0, 46, 25, 0.2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NavGeometry(62, 62, 25, 0.2, 0), "the block fills the cell");
            Assert.Throws<ArgumentOutOfRangeException>(() => new NavGeometry(62, 46, 22, 0.2, 0), "the pavement is inside the block");
            Assert.Throws<ArgumentOutOfRangeException>(() => new NavGeometry(62, 46, 31, 0.2, 0), "the pavement runs past the road's middle");
        }
    }
}
