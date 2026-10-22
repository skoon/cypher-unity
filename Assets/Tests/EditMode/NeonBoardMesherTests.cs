using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The lettering coordinates of sign panels and the board and hologram meshes.</summary>
    public class NeonBoardMesherTests
    {
        static readonly NeonAtlasLayout Atlas = NeonAtlas.Layout();

        static NeonQuad Sign(string cell, double nx = 0, double nz = 1) => new NeonQuad
        {
            Gx = 1, Gz = 2, X = 10, Y = 5, Z = 20, Nx = nx, Nz = nz, Width = 4, Height = 1.2, Shape = NeonShape.Panel,
            Colour = new Rgb(1f, 0.5f, 0.25f), Gain = 3, FlickerMode = 0, Seed = 2, Cell = cell,
        };

        static NeonBoard Board(int panel = 2, double nx = 0, double nz = 1) => new NeonBoard
        {
            Gx = 4, Gz = 5, X = 30, Y = 25, Z = 40, Nx = nx, Nz = nz, Width = 12, Height = 6, Panel = panel,
            Colour = new Rgb(0.9f, 0.8f, 1f), Seed = 7.25, Glitch = 1.2, TickerSpeed = 0.09, Gain = 3.1,
        };

        static NeonCellMeshes Mesh(NeonQuad q)
        {
            var layout = new NeonLayout();
            layout.Quads.Add(q);
            return NeonMesher.Build(layout, Atlas)[(q.Gx, q.Gz)];
        }

        static NeonCellMeshes MeshBoard(NeonBoard b, bool hologram)
        {
            var layout = new NeonLayout();
            (hologram ? layout.Holograms : layout.Boards).Add(b);
            return NeonMesher.Build(layout, Atlas)[(b.Gx, b.Gz)];
        }

        static (double X, double Y, double Z) Vertex(MeshData m, int i) => (m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);

        // ---------------------------------------------------------------- lettering

        [Test]
        public void ASignsTextureCoordinatesCoverItsAtlasCell()
        {
            AtlasCell cell = Atlas["word5"];
            MeshData m = Mesh(Sign("word5")).Emissive;
            (double u, double v, double du, double dv) = cell.Uv(Atlas.Size);
            var us = Enumerable.Range(0, 4).Select(i => (double)m.Uvs[i * 2]).ToList();
            var vs = Enumerable.Range(0, 4).Select(i => (double)m.Uvs[i * 2 + 1]).ToList();
            Assert.AreEqual(u, us.Min(), 1e-6);
            Assert.AreEqual(u + du, us.Max(), 1e-6);
            Assert.AreEqual(v, vs.Min(), 1e-6);
            Assert.AreEqual(v + dv, vs.Max(), 1e-6);
        }

        [Test]
        public void TheLetteringIsNotMirroredForAViewerInFrontOfTheSign()
        {
            // a sign facing +Z has +X as its right-hand axis; a viewer in front looks along -Z and, in Unity's axes, sees +X on their left,
            // so the line has to start at the +X side and end at the -X side
            AtlasCell cell = Atlas["word5"];
            MeshData m = Mesh(Sign("word5", 0, 1)).Emissive;
            (double u, _, double du, _) = cell.Uv(Atlas.Size);
            int plusX = Enumerable.Range(0, 4).First(i => Vertex(m, i).X > 10), minusX = Enumerable.Range(0, 4).First(i => Vertex(m, i).X < 10);
            Assert.AreEqual(u, m.Uvs[plusX * 2], 1e-6, "the start of the line");
            Assert.AreEqual(u + du, m.Uvs[minusX * 2], 1e-6, "the end of the line");
        }

        [Test]
        public void TheTopOfTheLetteringIsTheTopOfTheSign()
        {
            AtlasCell cell = Atlas["word5"];
            MeshData m = Mesh(Sign("word5")).Emissive;
            (_, double v, _, double dv) = cell.Uv(Atlas.Size);
            for (int i = 0; i < 4; i++)
            {
                bool top = Vertex(m, i).Y > 5;
                Assert.AreEqual(top ? v + dv : v, m.Uvs[i * 2 + 1], 1e-6);
            }
        }

        [Test]
        public void DifferentCellsGiveDifferentCoordinates()
        {
            MeshData a = Mesh(Sign("word0")).Emissive, b = Mesh(Sign("kanji3")).Emissive;
            Assert.AreNotEqual(a.Uvs[0], b.Uvs[0]);
        }

        [Test]
        public void ASignWithoutACellFailsLoudly()
        {
            Assert.Throws<ArgumentException>(() => Mesh(Sign(null)));
            Assert.Throws<ArgumentException>(() => Mesh(Sign("no-such-cell")));
        }

        [Test]
        public void OtherShapesKeepZeroToOneCoordinates()
        {
            NeonQuad tube = Sign(null);
            tube.Shape = NeonShape.Tube;
            MeshData m = Mesh(tube).Emissive;
            CollectionAssert.AreEqual(new float[] { 0, 0, 1, 0, 1, 1, 0, 1 }, m.Uvs);
        }

        // ---------------------------------------------------------------- boards

        [Test]
        public void ABoardIsOneQuadFacingItsNormal()
        {
            MeshData m = MeshBoard(Board(), false).Boards;
            Assert.AreEqual(4, m.VertexCount);
            Assert.AreEqual(2, m.TriangleCount);
            var xs = Enumerable.Range(0, 4).Select(i => Vertex(m, i).X).ToList();
            var ys = Enumerable.Range(0, 4).Select(i => Vertex(m, i).Y).ToList();
            Assert.AreEqual(24.0, xs.Min(), 1e-5);
            Assert.AreEqual(36.0, xs.Max(), 1e-5);
            Assert.AreEqual(22.0, ys.Min(), 1e-5);
            Assert.AreEqual(28.0, ys.Max(), 1e-5);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(40.0, Vertex(m, i).Z, 1e-5, "flat in the wall plane");
                Assert.AreEqual(1f, m.Normals[i * 3 + 2], 1e-6f);
            }
        }

        [TestCase(0.0, 1.0)]
        [TestCase(1.0, 0.0)]
        [TestCase(0.0, -1.0)]
        [TestCase(-1.0, 0.0)]
        [TestCase(0.6, 0.8)]
        public void ABoardIsWoundToFaceItsNormal(double nx, double nz)
        {
            MeshData m = MeshBoard(Board(nx: nx, nz: nz), false).Boards;
            for (int t = 0; t < 2; t++)
            {
                int ia = m.Indices[t * 3] * 3, ib = m.Indices[t * 3 + 1] * 3, ic = m.Indices[t * 3 + 2] * 3;
                double ax = m.Positions[ib] - m.Positions[ia], ay = m.Positions[ib + 1] - m.Positions[ia + 1], az = m.Positions[ib + 2] - m.Positions[ia + 2];
                double bx = m.Positions[ic] - m.Positions[ia], by = m.Positions[ic + 1] - m.Positions[ia + 1], bz = m.Positions[ic + 2] - m.Positions[ia + 2];
                double cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
                double len = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                Assert.AreEqual(nx, cx / len, 1e-6);
                Assert.AreEqual(0.0, cy / len, 1e-6);
                Assert.AreEqual(nz, cz / len, 1e-6);
            }
        }

        [Test]
        public void ABoardsCoordinatesCoverItsPanelAndNoOther()
        {
            for (int panel = 0; panel < BoardArt.PanelCount; panel++)
            {
                MeshData m = MeshBoard(Board(panel), false).Boards;
                var lo = BoardArt.AtlasUv(panel, 0, 0);
                var hi = BoardArt.AtlasUv(panel, 1, 1);
                var us = Enumerable.Range(0, 4).Select(i => (double)m.Uvs[i * 2]).ToList();
                var vs = Enumerable.Range(0, 4).Select(i => (double)m.Uvs[i * 2 + 1]).ToList();
                Assert.AreEqual(lo.U, us.Min(), 1e-6, $"panel {panel}");
                Assert.AreEqual(hi.U, us.Max(), 1e-6);
                Assert.AreEqual(lo.V, vs.Min(), 1e-6);
                Assert.AreEqual(hi.V, vs.Max(), 1e-6);
            }
        }

        [Test]
        public void TheArtIsNotMirroredAndNotUpsideDown()
        {
            MeshData m = MeshBoard(Board(0, 0, 1), false).Boards;
            var lo = BoardArt.AtlasUv(0, 0, 0);
            var hi = BoardArt.AtlasUv(0, 1, 1);
            for (int i = 0; i < 4; i++)
            {
                var p = Vertex(m, i);
                bool plusX = p.X > 30;   // facing +Z: the picture starts at the +X side, which a viewer in front sees on their left
                Assert.AreEqual(plusX ? lo.U : hi.U, m.Uvs[i * 2], 1e-6);
                Assert.AreEqual(p.Y > 25 ? hi.V : lo.V, m.Uvs[i * 2 + 1], 1e-6);
            }
        }

        [Test]
        public void BoardsCarryTheirTintAndParameters()
        {
            MeshData m = MeshBoard(Board(), false).Boards;
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(0.9f, m.Colors[i * 3], 1e-6f);
                Assert.AreEqual(0.8f, m.Colors[i * 3 + 1], 1e-6f);
                Assert.AreEqual(1f, m.Colors[i * 3 + 2], 1e-6f);
                CollectionAssert.AreEqual(new[] { 7.25f, 1.2f, 0.09f, 3.1f }, new[] { m.Uv1[i * 4], m.Uv1[i * 4 + 1], m.Uv1[i * 4 + 2], m.Uv1[i * 4 + 3] });
            }
        }

        [Test]
        public void HologramsAreMeshedSeparatelyFromBoards()
        {
            NeonCellMeshes holo = MeshBoard(Board(), true);
            Assert.AreEqual(2, holo.Holos.TriangleCount);
            Assert.AreEqual(0, holo.Boards.TriangleCount);
            NeonCellMeshes board = MeshBoard(Board(), false);
            Assert.AreEqual(2, board.Boards.TriangleCount);
            Assert.AreEqual(0, board.Holos.TriangleCount);
            Assert.AreEqual(2, board.TriangleCount, "the cell's count includes them");
        }

        [Test]
        public void BoardsAreGroupedByCellAndMakeTheirOwnCell()
        {
            var layout = new NeonLayout();
            layout.Boards.Add(Board());
            NeonBoard other = Board();
            other.Gx = 9; other.Gz = 9;
            layout.Boards.Add(other);
            layout.Boards.Add(Board(1));
            var cells = NeonMesher.Build(layout, Atlas);
            Assert.AreEqual(2, cells.Count);
            Assert.AreEqual(4, cells[(4, 5)].Boards.TriangleCount);
            Assert.AreEqual(2, cells[(9, 9)].Boards.TriangleCount);
            Assert.AreEqual(0, cells[(4, 5)].Emissive.VertexCount);
        }

        [Test]
        public void BadBoardsFailLoudly()
        {
            NeonBoard b = Board();
            b.Panel = 4;
            Assert.Throws<ArgumentException>(() => MeshBoard(b, false));
            b = Board(); b.Width = 0;
            Assert.Throws<ArgumentException>(() => MeshBoard(b, false));
            b = Board(); b.Nx = 0; b.Nz = 0;
            Assert.Throws<ArgumentException>(() => MeshBoard(b, true));
        }

        [Test]
        public void TheDefaultAtlasIsUsedWhenNoneIsGiven()
        {
            var layout = new NeonLayout();
            layout.Quads.Add(Sign("word0"));
            Assert.DoesNotThrow(() => NeonMesher.Build(layout));
        }
    }
}
