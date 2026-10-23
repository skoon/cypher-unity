using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The navigation graph checked number for number against CityNav.js, run on the same city (fixture from tools/gen-golden.mjs).</summary>
    public class CityNavGoldenTests
    {
        static readonly string[] Lines = Golden.Lines("nav_golden_20771.txt");

        // The prototype probed the kerb by raycasts stepping 0.3 m from the block edge, and found its outer edge at 25.4 m; the C# build is given the same measurement.
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate(20771).Streets, new NavGeometry(62, 46, 25.4, 0.185, 0));

        [Test]
        public void TheMeasurementsMatch()
        {
            string[] m = Golden.Rows(Lines, "M").Single();
            Assert.AreEqual(Golden.D(m[1]), Nav.Cell, 1e-9);
            Assert.AreEqual(Golden.D(m[2]), Nav.BlockHalf, 1e-9);
            Assert.AreEqual(Golden.D(m[3]), Nav.WalkOuter, 1e-9);
            Assert.AreEqual(Golden.D(m[4]), Nav.WalkY, 1e-6);
            Assert.AreEqual(Golden.D(m[5]), Nav.RoadY, 1e-9);
            Assert.AreEqual(Golden.D(m[6]), Nav.AsphaltHalf, 1e-9);
            Assert.AreEqual(Golden.D(m[7]), Nav.RingRadius, 1e-9);
            Assert.AreEqual(Golden.D(m[8]), Nav.WalkBand, 1e-9);
            Assert.AreEqual(Golden.I(m[9]), Nav.NodeCount);
        }

        [Test]
        public void TheRoadCentrelinesMatch()
        {
            double[] x = Golden.Rows(Lines, "X").Single().Skip(1).Select(Golden.D).ToArray();
            double[] z = Golden.Rows(Lines, "Z").Single().Skip(1).Select(Golden.D).ToArray();
            Assert.AreEqual(x.Length, Nav.XLines.Count);
            Assert.AreEqual(z.Length, Nav.ZLines.Count);
            for (int i = 0; i < x.Length; i++)
            {
                Assert.AreEqual(x[i], Nav.XLines[i], 1e-9);
                Assert.AreEqual(z[i], Nav.ZLines[i], 1e-9);
            }
        }

        [Test]
        public void EveryNodeIsWhereTheGraphPutsItWithTheSameNeighboursAndCrossings()
        {
            List<string[]> nodes = Golden.Rows(Lines, "N");
            Assert.AreEqual(Nav.NodeCount, nodes.Count);
            foreach (string[] n in nodes)
            {
                int i = Golden.I(n[1]);
                // the prototype keeps node positions in a Float32Array
                Assert.AreEqual(Golden.D(n[2]), Nav.NodeX(i), 1e-4, $"x of node {i}");
                Assert.AreEqual(Golden.D(n[3]), Nav.NodeZ(i), 1e-4, $"z of node {i}");
                Assert.AreEqual(Golden.I(n[4]), Nav.NodeBlock(i), $"block of node {i}");

                string[] edges = n[5].Split(',');
                Assert.AreEqual(edges.Length, Nav.Neighbours(i).Count, $"degree of node {i}");
                for (int e = 0; e < edges.Length; e++)
                {
                    string[] pair = edges[e].Split(':');
                    Assert.AreEqual(Golden.I(pair[0]), Nav.Neighbours(i)[e], $"neighbour {e} of node {i}");
                    Assert.AreEqual(Golden.I(pair[1]), (int)Nav.NeighbourCrossings(i)[e], $"crossing {e} of node {i}");
                }
            }
        }

        [Test]
        public void GroundHeightAndKerbDistanceMatchAtTheProbedPoints()
        {
            List<string[]> rows = Golden.Rows(Lines, "G");
            Assert.GreaterOrEqual(rows.Count, 8);
            foreach (string[] g in rows)
            {
                double x = Golden.D(g[1]), z = Golden.D(g[2]);
                Assert.AreEqual(Golden.D(g[3]), Nav.KerbDistance(x, z), 1e-9, $"kerb distance at ({x}, {z})");
                Assert.AreEqual(Golden.D(g[4]), Nav.GroundY(x, z), 1e-6, $"ground at ({x}, {z})");
            }
        }

        [Test]
        public void TheBandSearchPicksTheSameNodesFromTheSameDraws()
        {
            var rng = new Rng(99);   // the generator's makeRng(99), consumed in the same order
            foreach (string[] b in Golden.Rows(Lines, "B"))
            {
                int picked = Nav.NodeInBand(rng, Golden.D(b[1]), Golden.D(b[2]), Golden.D(b[3]), Golden.D(b[4]));
                Assert.AreEqual(Golden.I(b[5]), picked, $"band ({b[1]}, {b[2]}) {b[3]}-{b[4]}");
            }
        }
    }
}
