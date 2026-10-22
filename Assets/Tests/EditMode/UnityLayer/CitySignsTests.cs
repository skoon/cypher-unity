using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.City;
using Cypher.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    /// <summary>The sign lettering and the LED hoardings and holograms, as the builder puts them in the scene.</summary>
    public class CitySignsTests
    {
        readonly List<UnityEngine.Object> _cleanup = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in _cleanup)
            {
                if (o) UnityEngine.Object.DestroyImmediate(o);
            }
            _cleanup.Clear();
        }

        Material Make(string shaderName, string name)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.IsNotNull(shader, $"{shaderName} not found");
            var m = new Material(shader) { name = name };
            _cleanup.Add(m);
            return m;
        }

        CityMaterials Materials(bool withBoards)
        {
            var mats = new CityMaterials
            {
                Walls = Make("Cypher/CityLit", "walls"), Roofs = Make("Cypher/CityLit", "roofs"), Sidewalk = Make("Cypher/CityLit", "sidewalk"),
                Road = Make("Cypher/CityLit", "road"), Paint = Make("Cypher/CityLit", "paint"), Metal = Make("Cypher/CityLit", "metal"),
                Neon = Make("Cypher/NeonAdd", "neon"),
            };
            if (!withBoards) return mats;
            mats.Boards = Make("Cypher/NeonBoard", "boards");
            mats.Holos = Make("Cypher/NeonBoard", "holos");
            mats.Holos.SetFloat("_Holo", 1f);
            return mats;
        }

        CityBuilder Build(int radius, bool withBoards, CityMaterials? given = null)
        {
            var go = new GameObject("CitySignsTest");
            _cleanup.Add(go);
            var builder = go.AddComponent<CityBuilder>();   // edit mode does not run Awake: build explicitly
            builder.Configure(CityPlan.DefaultSeed, radius, given ?? Materials(withBoards));
            builder.Build();
            Assert.IsTrue(builder.IsBuilt);
            return builder;
        }

        static Transform Chunk(CityBuilder b, CityChunk c) => b.Root.Find($"Chunk_{c.Gx}_{c.Gz}");

        [Test]
        public void TheSignMaterialGetsTheLetteringAtlas()
        {
            CityMaterials assets = Materials(false);
            CityBuilder b = Build(1, false, assets);
            MeshRenderer neon = Chunk(b, b.Slice.Chunks.First(c => c.Neon.TriangleCount > 0)).Find("Neon").GetComponent<MeshRenderer>();
            var atlas = neon.sharedMaterial.GetTexture("_SignAtlas") as Texture2D;
            Assert.IsNotNull(atlas);
            Assert.AreEqual(NeonAtlas.Size, atlas.width);
            Assert.AreEqual(TextureFormat.R8, atlas.format);
            Assert.Greater(atlas.mipmapCount, 8, "a full mip chain, so far signs do not shimmer");
            Assert.AreEqual(TextureWrapMode.Clamp, atlas.wrapMode);
            Assert.IsNull(assets.Neon.GetTexture("_SignAtlas"), "the project's own material is left alone; the city draws with an instance");
            Assert.AreNotSame(assets.Neon, neon.sharedMaterial);
        }

        [Test]
        public void ChunksWithBoardsGetABoardsRendererAndAHologramsRendererWhenTheMaterialsAreThere()
        {
            CityBuilder b = Build(3, true);
            int withBoards = 0, withHolos = 0;
            foreach (CityChunk c in b.Slice.Chunks)
            {
                Transform chunk = Chunk(b, c);
                Transform boards = chunk.Find(CityBuilder.BoardsName), holos = chunk.Find(CityBuilder.HologramsName);
                Assert.IsNotNull(boards, chunk.name);
                Assert.IsNotNull(holos, chunk.name);
                Assert.AreEqual(c.Boards.TriangleCount > 0, boards.GetComponent<MeshRenderer>() != null, $"boards renderer {chunk.name}");
                Assert.AreEqual(c.Holos.TriangleCount > 0, holos.GetComponent<MeshRenderer>() != null, $"holos renderer {chunk.name}");
                if (boards.GetComponent<MeshRenderer>() != null) withBoards++;
                if (holos.GetComponent<MeshRenderer>() != null) withHolos++;
            }
            Assert.GreaterOrEqual(withBoards, 15, "hoardings are all over the city");
            Assert.GreaterOrEqual(withHolos, 4);
            Assert.GreaterOrEqual(b.Slice.Chunks.Sum(c => c.Boards.TriangleCount), 2 * 20, "two triangles for each hoarding");
        }

        [Test]
        public void TheBoardMaterialsGetTheArtAndTheInset()
        {
            CityMaterials assets = Materials(true);
            CityBuilder b = Build(3, true, assets);
            CityChunk withBoard = b.Slice.Chunks.First(c => c.Boards.TriangleCount > 0);
            CityChunk withHolo = b.Slice.Chunks.First(c => c.Holos.TriangleCount > 0);
            Material board = Chunk(b, withBoard).Find(CityBuilder.BoardsName).GetComponent<MeshRenderer>().sharedMaterial;
            Material holo = Chunk(b, withHolo).Find(CityBuilder.HologramsName).GetComponent<MeshRenderer>().sharedMaterial;

            var art = board.GetTexture("_BoardAtlas") as Texture2D;
            Assert.IsNotNull(art);
            Assert.AreEqual(BoardArt.Size, art.width);
            Assert.AreEqual(TextureFormat.RGBA32, art.format);
            Assert.IsTrue(art.isDataSRGB, "the panels' colours are sRGB, as the prototype's canvas ones");
            Assert.AreSame(art, holo.GetTexture("_BoardAtlas"), "both share one texture");
            Assert.AreEqual((float)BoardArt.InsetFraction, board.GetFloat("_PanelInset"), 1e-9f);
            Assert.AreEqual((float)BoardArt.InsetFraction, holo.GetFloat("_PanelInset"), 1e-9f);
            Assert.AreEqual(0f, board.GetFloat("_Holo"));
            Assert.AreEqual(1f, holo.GetFloat("_Holo"));
            Assert.IsNull(assets.Boards.GetTexture("_BoardAtlas"), "the project's own material is left alone");
        }

        [Test]
        public void BoardsAndHologramsCastNoShadowsAndReceiveNone()
        {
            CityBuilder b = Build(3, true);
            foreach (CityChunk c in b.Slice.Chunks)
            {
                foreach (string name in new[] { CityBuilder.BoardsName, CityBuilder.HologramsName })
                {
                    MeshRenderer r = Chunk(b, c).Find(name).GetComponent<MeshRenderer>();
                    if (r == null) continue;
                    Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, name);
                    Assert.IsFalse(r.receiveShadows, name);
                }
            }
        }

        [Test]
        public void WithoutTheBoardMaterialsTheCityHasNoBoardsButStillHasLettering()
        {
            CityBuilder b = Build(1, false);
            foreach (CityChunk c in b.Slice.Chunks)
            {
                Assert.IsNull(Chunk(b, c).Find(CityBuilder.BoardsName), "no boards object");
                Assert.IsNull(Chunk(b, c).Find(CityBuilder.HologramsName), "no holos object");
            }
            Assert.Greater(b.Slice.Chunks.Sum(c => c.Neon.TriangleCount), 0);
        }

        [Test]
        public void ACityWithNeonSwitchedOffBakesNoSignTextures()
        {
            var go = new GameObject("NoNeon");
            _cleanup.Add(go);
            var builder = go.AddComponent<CityBuilder>();
            typeof(CityBuilder).GetField("neon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(builder, false);
            builder.Configure(CityPlan.DefaultSeed, 1, Materials(true));
            int before = Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t.name == "SignAtlas" || t.name == "BoardAtlas");
            builder.Build();
            Assert.IsTrue(builder.IsBuilt);
            Assert.AreEqual(0, builder.Slice.Chunks.Sum(c => c.Neon.TriangleCount + c.Boards.TriangleCount + c.Holos.TriangleCount));
            Assert.AreEqual(before, Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t.name == "SignAtlas" || t.name == "BoardAtlas"));
        }

        [Test]
        public void MakeMaskChecksItsBytes()
        {
            Assert.Throws<ArgumentNullException>(() => SurfaceMaterials.MakeMask("m", null, 4, 1));
            Assert.Throws<ArgumentException>(() => SurfaceMaterials.MakeMask("m", new byte[15], 4, 1));
            Texture2D t = SurfaceMaterials.MakeMask("m", new byte[16], 4, 1);
            _cleanup.Add(t);
            Assert.AreEqual(TextureFormat.R8, t.format);
        }

        [Test]
        public void CreateSignsChecksItsInputs()
        {
            var owned = new List<UnityEngine.Object>();
            var texture = new Texture2D(4, 4);
            _cleanup.Add(texture);
            Assert.Throws<ArgumentException>(() => SurfaceMaterials.CreateSigns(default, texture, texture, owned));
            Assert.Throws<ArgumentNullException>(() => SurfaceMaterials.CreateSigns(Materials(false), null, texture, owned));
            Assert.Throws<ArgumentNullException>(() => SurfaceMaterials.CreateSigns(Materials(false), texture, null, owned));
            Assert.Throws<ArgumentNullException>(() => SurfaceMaterials.CreateSigns(Materials(false), texture, texture, null));
        }

        [Test]
        public void CreateSignsMakesInstancesWithoutBoardsWhenThereAreNoBoardMaterials()
        {
            var owned = new List<UnityEngine.Object>();
            var texture = new Texture2D(4, 4);
            _cleanup.Add(texture);
            (Material neon, Material boards, Material holos) = SurfaceMaterials.CreateSigns(Materials(false), texture, texture, owned);
            _cleanup.AddRange(owned);
            Assert.IsNotNull(neon);
            Assert.IsNull(boards);
            Assert.IsNull(holos);
            Assert.AreEqual(1, owned.Count);
        }
    }
}
