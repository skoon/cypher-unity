using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The markers at the named places: every name can be lettered, the atlas puts each where it says, the mesh is sound, and the tint follows the jobs.</summary>
    public class PlaceMarkerArtTests
    {
        static List<(string Key, string Name)> Places() =>
            RpgLocations.Defs.Select(d => ((string)d["key"], (string)d["name"])).ToList();

        [Test]
        public void EveryPlaceNameCanBeLettered()
        {
            foreach ((string key, string name) in Places())
            {
                foreach (char c in PlaceMarkerArt.Lettering(name)) Assert.IsTrue(StrokeFont.HasLatin(c), $"{key}: no glyph for '{c}'");
            }
        }

        [Test]
        public void TheAtlasHasEachNameInItsOwnRowAndIsTheSameEveryTime()
        {
            List<(string, string)> places = Places();
            (byte[] pixels, List<PlaceNameCell> cells) = PlaceMarkerArt.Render(places);
            Assert.AreEqual(PlaceMarkerArt.AtlasWidth * PlaceMarkerArt.AtlasHeight, pixels.Length);
            Assert.AreEqual(places.Count, cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                PlaceNameCell c = cells[i];
                Assert.AreEqual(places[i].Item1, c.Key);
                Assert.Less(c.V0, c.V1);
                Assert.Less(c.U0, c.U1);
                Assert.LessOrEqual(c.U1, 1.0);
                Assert.AreEqual(c.Aspect, (c.U1 - c.U0) * PlaceMarkerArt.AtlasWidth / ((c.V1 - c.V0) * PlaceMarkerArt.AtlasHeight), 1e-9);

                // something is lit inside the cell (rows are stored bottom up, like the texture)
                int x0 = (int)(c.U0 * PlaceMarkerArt.AtlasWidth), x1 = (int)(c.U1 * PlaceMarkerArt.AtlasWidth);
                int y0 = (int)Math.Round(c.V0 * PlaceMarkerArt.AtlasHeight), y1 = (int)Math.Round(c.V1 * PlaceMarkerArt.AtlasHeight);
                int lit = 0;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++) if (pixels[y * PlaceMarkerArt.AtlasWidth + x] > 200) lit++;
                }
                Assert.Greater(lit, 100, $"{c.Key} is lettered");
            }
            // nothing below the last row
            int used = (int)Math.Round(cells.Min(c => c.V0) * PlaceMarkerArt.AtlasHeight);
            Assert.IsTrue(pixels.Take(used * PlaceMarkerArt.AtlasWidth).All(p => p == 0));
            Assert.IsTrue(pixels.SequenceEqual(PlaceMarkerArt.Render(places).Pixels));
        }

        [Test]
        public void ALongNameIsShrunkToFitTheAtlas()
        {
            (_, List<PlaceNameCell> cells) = PlaceMarkerArt.Render(new[] { ("long", new string('W', 80)) });
            Assert.AreEqual(1.0, cells[0].U1, 1e-9);
            Assert.Throws<ArgumentException>(() => PlaceMarkerArt.Render(Enumerable.Range(0, 13).Select(i => ("k" + i, "X")).ToList()));
            Assert.Throws<ArgumentException>(() => PlaceMarkerArt.Render(new[] { ("bad", "NO ~ GLYPH") }));
        }

        [Test]
        public void TheMeshIsABeamANamePanelAndAPool()
        {
            var cell = new PlaceNameCell("noodle", 0.1, 0.5, 0.6, 0.6, 8);
            PlaceMarkerMesh m = PlaceMarkerArt.Build(cell, 0.25);
            Assert.AreEqual(0, m.Indices.Count % 3);
            Assert.IsTrue(m.Indices.All(i => i >= 0 && i < m.VertexCount));
            Assert.AreEqual(m.VertexCount, m.Uvs.Count);
            Assert.AreEqual(m.VertexCount, m.Signs.Count);
            Assert.AreEqual(m.VertexCount, m.Weights.Count);

            Assert.AreEqual(PlaceMarkerArt.BeamHeight, m.Positions.Max(p => p.Y), 1e-9, "the beam's top");
            Assert.AreEqual(0, m.Weights[m.Positions.FindIndex(p => p.Y == PlaceMarkerArt.BeamHeight)], "it fades out at the top");

            // the panel: shape 2, reading its atlas cell, as wide as the name over its height, the left of the name on the viewer's left (+X)
            List<int> plate = Enumerable.Range(0, m.VertexCount).Where(i => Math.Floor(m.Signs[i].ModeShape / 8) == 2).ToList();
            Assert.AreEqual(4, plate.Count);
            double width = plate.Max(i => m.Positions[i].X) - plate.Min(i => m.Positions[i].X);
            double height = plate.Max(i => m.Positions[i].Y) - plate.Min(i => m.Positions[i].Y);
            Assert.AreEqual(8, width / height, 1e-9);
            int left = plate.OrderByDescending(i => m.Positions[i].X).First();
            Assert.AreEqual(cell.U0, m.Uvs[left].U, 1e-9);
            Assert.AreEqual(4, plate.Select(i => m.Uvs[i]).Distinct().Count());

            // no part of the beam crosses the name
            double plateLow = plate.Min(i => m.Positions[i].Y), plateHigh = plate.Max(i => m.Positions[i].Y);
            for (int t = 0; t < m.Indices.Count; t += 3)
            {
                int[] tri = { m.Indices[t], m.Indices[t + 1], m.Indices[t + 2] };
                if (tri.Any(i => Math.Floor(m.Signs[i].ModeShape / 8) != 1)) continue;
                double lo = tri.Min(i => m.Positions[i].Y), hi = tri.Max(i => m.Positions[i].Y);
                Assert.IsTrue(hi <= plateLow || lo >= plateHigh, $"a beam triangle {lo:0.00}-{hi:0.00} crosses the name {plateLow:0.00}-{plateHigh:0.00}");
            }
            Assert.AreEqual(0.7 + (0.3 - 0.7) * (9.5 - 6) / 7, PlaceMarkerArt.BeamWeight(9.5), 1e-9);

            PlaceMarkerMesh wide =PlaceMarkerArt.Build(new PlaceNameCell("x", 0, 0, 1, 0.1, 40), 0);
            List<int> widePlate = Enumerable.Range(0, wide.VertexCount).Where(i => Math.Floor(wide.Signs[i].ModeShape / 8) == 2).ToList();
            Assert.AreEqual(PlaceMarkerArt.PlateMaxWidth, widePlate.Max(i => wide.Positions[i].X) * 2, 1e-9, "a long name is held to the widest panel");
        }

        [Test]
        public void TheTintFollowsTheJobs()
        {
            var markers = new List<JsonObject>
            {
                new JsonObject { ["key"] = "noodle", ["tracked"] = true },
                new JsonObject { ["key"] = "market", ["tracked"] = false },
            };
            Assert.AreEqual(PlaceMarkerArt.Tint.Tracked, PlaceMarkerArt.TintFor("noodle", markers));
            Assert.AreEqual(PlaceMarkerArt.Tint.Job, PlaceMarkerArt.TintFor("market", markers));
            Assert.AreEqual(PlaceMarkerArt.Tint.Place, PlaceMarkerArt.TintFor("clinic", markers));
        }
    }
}
