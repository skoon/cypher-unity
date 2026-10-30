using System;
using System.Collections.Generic;
using System.Globalization;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// The three guns' models against the prototype's own <c>player/WeaponModels.js</c> run headless (<c>tools/gen-golden-weapons.mjs</c>): every part's
    /// vertices, in order, with position, normal, colour and the metal/rough/emissive/channel values, and the anchors.
    /// </summary>
    public class WeaponModelsGoldenTests
    {
        static readonly string[] Lines = Golden.Lines("weapons_golden.txt");

        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        [TestCase("wasp")]
        [TestCase("magnat")]
        [TestCase("voltline")]
        public void TheGunMatchesThePrototypeVertexForVertex(string id)
        {
            GunModel model = WeaponModels.Build(id);
            int i = Array.FindIndex(Lines, l => l.StartsWith("gun " + id + " ", StringComparison.Ordinal));
            Assert.GreaterOrEqual(i, 0);
            Assert.AreEqual(model.Name.Replace(' ', '_'), Lines[i].Split(' ')[2]);
            i++;
            var anchors = new Dictionary<string, (double, double, double)>();
            while (Lines[i].StartsWith("anchor ", StringComparison.Ordinal))
            {
                string[] a = Lines[i].Split(' ');
                anchors[a[1]] = (D(a[2]), D(a[3]), D(a[4]));
                i++;
            }
            CollectionAssert.AreEquivalent(anchors, model.Anchors);

            int partIndex = 0;
            double worst = 0;
            while (i < Lines.Length && Lines[i].StartsWith("part ", StringComparison.Ordinal))
            {
                string[] h = Lines[i].Split(' ');
                GunPart part = model.Parts[partIndex++];
                Assert.AreEqual(h[1], part.Name);
                Assert.AreEqual(h[2] == "-" ? null : h[2], part.Parent, part.Name);
                Assert.AreEqual(D(h[3]), part.Pivot.X, 1e-9);
                Assert.AreEqual(D(h[4]), part.Pivot.Y, 1e-9);
                Assert.AreEqual(D(h[5]), part.Pivot.Z, 1e-9);
                int count = int.Parse(h[6], CultureInfo.InvariantCulture);
                Assert.AreEqual(count, part.VertexCount, $"{id} {part.Name}: vertex count");
                i++;
                for (int v = 0; v < count; v++, i++)
                {
                    string[] f = Lines[i].Split(' ');
                    float[][] mine = { part.Positions, part.Normals, part.Colors };
                    for (int k = 0; k < 9; k++)
                    {
                        double got = mine[k / 3][v * 3 + k % 3], want = D(f[k]);
                        double err = Math.Abs(got - want);
                        worst = Math.Max(worst, err);
                        if (err > 2e-6) Assert.Fail($"{id} {part.Name} vertex {v} value {k}: {got} against {want}");
                    }
                    for (int k = 0; k < 4; k++) Assert.AreEqual(D(f[9 + k]), part.Mre[v * 4 + k], 2e-6, $"{id} {part.Name} vertex {v} mre {k}");
                }
            }
            Assert.AreEqual(model.Parts.Count, partIndex, "every part is in the golden");
            TestContext.Progress?.WriteLine($"{id}: worst difference {worst:E2}");
        }
    }
}
