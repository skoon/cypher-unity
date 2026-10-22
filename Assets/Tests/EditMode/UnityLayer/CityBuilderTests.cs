using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.City;
using Cypher.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Cypher.Tests
{
    public class CityBuilderTests
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

        static Shader CityShader()
        {
            Shader shader = Shader.Find("Cypher/CityLit");
            Assert.IsNotNull(shader, "Cypher/CityLit shader not found");
            return shader;
        }

        CityMaterials NewMaterials()
        {
            Material M(string n)
            {
                var m = new Material(CityShader()) { name = n };
                _cleanup.Add(m);
                return m;
            }
            Shader neonShader = Shader.Find("Cypher/NeonAdd");
            Assert.IsNotNull(neonShader, "Cypher/NeonAdd shader not found");
            var neon = new Material(neonShader) { name = "neon" };
            _cleanup.Add(neon);
            Shader boardShader = Shader.Find("Cypher/NeonBoard");
            Assert.IsNotNull(boardShader, "Cypher/NeonBoard shader not found");
            var boards = new Material(boardShader) { name = "boards" };
            var holos = new Material(boardShader) { name = "holos" };
            holos.SetFloat("_Holo", 1f);
            _cleanup.Add(boards);
            _cleanup.Add(holos);
            return new CityMaterials { Walls = M("walls"), Roofs = M("roofs"), Sidewalk = M("sidewalk"), Road = M("road"), Paint = M("paint"), Metal = M("metal"), Neon = neon, Boards = boards, Holos = holos };
        }

        CityBuilder NewBuilder(int radius = 1, uint seed = CityPlan.DefaultSeed)
        {
            var go = new GameObject("CityBuilderTest");
            _cleanup.Add(go);
            var builder = go.AddComponent<CityBuilder>();
            builder.Configure(seed, radius, NewMaterials());
            return builder;
        }

        [Test]
        public void ShaderCompilesWithoutErrors()
        {
            Shader shader = CityShader();
            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            var errors = messages.Where(m => m.severity == ShaderCompilerMessageSeverity.Error).ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => $"{e.file}({e.line}): {e.message}")));
        }

        [Test]
        public void NeonShaderCompilesWithoutErrors()
        {
            Shader shader = Shader.Find("Cypher/NeonAdd");
            Assert.IsNotNull(shader);
            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            var errors = messages.Where(m => m.severity == ShaderCompilerMessageSeverity.Error).ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => $"{e.file}({e.line}): {e.message}")));
        }

        [Test]
        public void BuiltChunksCarryNeonAndHousingRenderers()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();

            int withNeon = 0;
            foreach (CityChunk c in b.Slice.Chunks)
            {
                Transform chunk = b.Root.Find($"Chunk_{c.Gx}_{c.Gz}");
                bool neon = chunk.Find("Neon").GetComponent<MeshRenderer>() != null;
                bool housings = chunk.Find("Housings").GetComponent<MeshRenderer>() != null;
                Assert.AreEqual(c.Neon.TriangleCount > 0, neon, $"neon renderer ({c.Gx},{c.Gz})");
                Assert.AreEqual(c.Housings.TriangleCount > 0, housings, $"housing renderer ({c.Gx},{c.Gz})");
                if (neon) withNeon++;
            }
            Assert.AreEqual(8, withNeon, "every built cell has signage; the plaza has none");
        }

        [Test]
        public void NeonRenderersCastNoShadowsAndCarryTheirParameterChannel()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();

            MeshRenderer neon = b.Root.GetComponentsInChildren<MeshRenderer>().First(r => r.name == "Neon");
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, neon.shadowCastingMode);
            Assert.IsFalse(neon.receiveShadows);

            var uv1 = new System.Collections.Generic.List<Vector4>();
            neon.GetComponent<MeshFilter>().sharedMesh.GetUVs(1, uv1);
            Assert.IsNotEmpty(uv1, "quad parameters reach the mesh");
            Assert.IsTrue(uv1.Any(v => v.w > 0f), "gain is carried in w");

            MeshRenderer housing = b.Root.GetComponentsInChildren<MeshRenderer>().First(r => r.name == "Housings");
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, housing.shadowCastingMode, "solid housings cast shadows");
        }

        [Test]
        public void NeonCanBeSwitchedOff()
        {
            var go = new GameObject("NoNeon");
            _cleanup.Add(go);
            var builder = go.AddComponent<CityBuilder>();
            builder.Configure(CityPlan.DefaultSeed, 1, NewMaterials());
            var so = new SerializedObject(builder);
            so.FindProperty("neon").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            builder.Build();

            Assert.IsTrue(builder.Slice.Chunks.All(c => c.Neon.TriangleCount == 0 && c.Housings.TriangleCount == 0));
            Assert.IsEmpty(builder.Root.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "Neon" || r.name == "Housings"));
        }

        [Test]
        public void BuildCreatesRoadbedMarkingsAndOneChunkPerCell()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();

            Assert.AreEqual(9, b.Slice.Chunks.Count);
            Assert.IsNotNull(b.Root.Find("Roadbed"));
            Assert.IsNotNull(b.Root.Find("RoadMarkings"));
            foreach (CityChunk c in b.Slice.Chunks)
            {
                Transform chunk = b.Root.Find($"Chunk_{c.Gx}_{c.Gz}");
                Assert.IsNotNull(chunk, $"chunk ({c.Gx},{c.Gz})");
                Assert.IsNotNull(chunk.Find("Sidewalk").GetComponent<MeshRenderer>());
            }
        }

        [Test]
        public void ChunksWithBuildingsHaveWallAndRoofRenderersAndThePlazaDoesNot()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();

            foreach (CityChunk c in b.Slice.Chunks)
            {
                Transform chunk = b.Root.Find($"Chunk_{c.Gx}_{c.Gz}");
                bool hasWalls = chunk.Find("Walls").GetComponent<MeshRenderer>() != null;
                bool hasRoofs = chunk.Find("Roofs").GetComponent<MeshRenderer>() != null;
                Assert.AreEqual(c.BuildingCount > 0, hasWalls, $"walls ({c.Gx},{c.Gz})");
                Assert.AreEqual(c.BuildingCount > 0, hasRoofs, $"roofs ({c.Gx},{c.Gz})");
            }
        }

        [Test]
        public void RenderedTrianglesMatchTheSlicePlusRoadbed()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();

            int tris = b.Root.GetComponentsInChildren<MeshFilter>().Sum(f => (int)f.sharedMesh.GetIndexCount(0) / 3);
            Assert.AreEqual(b.Slice.TriangleCount + 2, tris);
        }

        [Test]
        public void EveryChunkColliderBecomesABoxCollider()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();

            foreach (CityChunk c in b.Slice.Chunks)
            {
                Transform chunk = b.Root.Find($"Chunk_{c.Gx}_{c.Gz}");
                Assert.AreEqual(c.Colliders.Count, chunk.GetComponents<BoxCollider>().Length, $"cell ({c.Gx},{c.Gz})");
            }
        }

        [Test]
        public void RoadbedHasAGroundColliderUnderTheSpawn()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();

            var box = b.Root.Find("Roadbed").GetComponent<BoxCollider>();
            Assert.IsNotNull(box);
            Assert.IsTrue(box.bounds.Contains(new Vector3(0f, -0.1f, 0f)), "origin (the plaza spawn) is over the ground collider");
            Assert.AreEqual(0f, box.bounds.max.y, 1e-4f, "collider top is at street level");
        }

        [Test]
        public void PlazaDeckHoldsThePlayerSpawnAtKerbHeight()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();

            Transform plaza = b.Root.Find($"Chunk_{CityLayout.PlazaIndex}_{CityLayout.PlazaIndex}");
            Bounds deck = plaza.GetComponent<BoxCollider>().bounds;
            Assert.IsTrue(deck.Contains(new Vector3(0f, 0.1f, 0f)));
            Assert.AreEqual((float)CityLayout.SidewalkHeight, deck.max.y, 1e-5f);
        }

        [Test]
        public void WallMaterialIsAnInstanceCarryingTheWindowAtlas()
        {
            CityBuilder b = NewBuilder(1);
            CityMaterials mats = NewMaterials();
            b.Configure(CityPlan.DefaultSeed, 1, mats);
            b.Build();

            Transform walls = b.Root.GetComponentsInChildren<Transform>().First(t => t.name == "Walls" && t.GetComponent<MeshRenderer>());
            Material used = walls.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreNotSame(mats.Walls, used, "shared asset must not be mutated");
            var atlas = used.GetTexture("_EmissionMap") as Texture2D;
            Assert.IsNotNull(atlas);
            Assert.AreEqual(WindowAtlas.DefaultSize, atlas.width);
            Assert.IsNull(mats.Walls.GetTexture("_EmissionMap"));
        }

        static Material Used(CityBuilder b, string childName)
        {
            if (childName == "Roadbed") return b.Root.Find("Roadbed").GetComponent<MeshRenderer>().sharedMaterial;
            Transform t = b.Root.GetComponentsInChildren<Transform>().First(x => x.name == childName && x.GetComponent<MeshRenderer>());
            return t.GetComponent<MeshRenderer>().sharedMaterial;
        }

        [Test]
        public void RoadGetsAsphaltAndAPuddleSmoothnessMapOnAnInstance()
        {
            CityBuilder b = NewBuilder(1);
            CityMaterials mats = NewMaterials();
            b.Configure(CityPlan.DefaultSeed, 1, mats);
            b.Build();

            Material road = Used(b, "Roadbed");
            Assert.AreNotSame(mats.Road, road);
            var albedo = road.GetTexture("_BaseMap") as Texture2D;
            var smooth = road.GetTexture("_MaskMap") as Texture2D;
            Assert.IsNotNull(albedo);
            Assert.IsNotNull(smooth);
            Assert.AreEqual(SurfaceMaterials.AsphaltSize, albedo.width);
            Assert.IsTrue(albedo.isDataSRGB, "asphalt colour is sRGB");
            Assert.IsFalse(smooth.isDataSRGB, "the smoothness map is linear data");
            Assert.AreEqual(SurfaceMaterials.RoadMaskTiling, road.GetTextureScale("_MaskMap").x, 1e-6f);
            Assert.IsNull(mats.Road.GetTexture("_BaseMap"), "the shared asset stays untouched");
            Assert.AreEqual(0f, road.GetFloat("_WorldUV"));
        }

        [Test]
        public void SidewalkGetsPavingWithAWiderPuddleTile()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();

            Material walk = Used(b, "Sidewalk");
            Assert.IsNotNull(walk.GetTexture("_BaseMap"));
            Assert.IsNotNull(walk.GetTexture("_MaskMap"));
            Assert.AreEqual(SurfaceMaterials.SidewalkMaskTiling, walk.GetTextureScale("_MaskMap").x, 1e-6f);
            Assert.Less(SurfaceMaterials.SidewalkMaskTiling, 1f / 3f, "puddles are bigger than the 3 m paving tile");
        }

        [Test]
        public void WallsAndRoofsShareConcreteAndUseWorldSpaceUvs()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();

            Material walls = Used(b, "Walls");
            Material roofs = Used(b, "Roofs");
            Assert.AreEqual(1f, walls.GetFloat("_WorldUV"), "wall mesh UVs belong to the window atlas");
            Assert.AreEqual(1f, roofs.GetFloat("_WorldUV"));
            Assert.AreSame(walls.GetTexture("_BaseMap"), roofs.GetTexture("_BaseMap"), "one concrete texture, shared");
            Assert.AreEqual(SurfaceMaterials.ConcreteTiling, walls.GetTextureScale("_BaseMap").x, 1e-6f);
            Assert.AreEqual(1f / 4.5f, SurfaceMaterials.ConcreteTiling, 1e-6f, "4.5 m per repeat, as in the prototype");
            Assert.IsNotNull(walls.GetTexture("_EmissionMap"), "windows still get their atlas");
        }

        [Test]
        public void GeneratingTheTexturesFitsAStartupBudget()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();
            // Not a performance gate (that is step 10), just a guard against an accidental 10x regression.
            Assert.That(b.BuildMilliseconds, Is.GreaterThan(0).And.LessThan(15000), $"build took {b.BuildMilliseconds:0} ms");
            Debug.Log($"[Cypher] city build (textures, materials, meshes): {b.BuildMilliseconds:0} ms, {b.Slice.TriangleCount} triangles in {b.Root.GetComponentsInChildren<MeshRenderer>().Length} renderers");
        }

        [Test]
        public void MakeTextureRejectsMismatchedData()
        {
            Assert.Throws<ArgumentException>(() => SurfaceMaterials.MakeTexture("bad", new byte[10], 4, true, 1));
            Assert.Throws<ArgumentNullException>(() => SurfaceMaterials.MakeTexture("null", null, 4, true, 1));
        }

        [Test]
        public void RebuildingReplacesTheCityInsteadOfStackingIt()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();
            b.Build();

            Assert.AreEqual(1, b.transform.Cast<Transform>().Count(t => t.name == CityBuilder.RootName));
        }

        [Test]
        public void ClearRemovesEverythingAndSliceBecomesNull()
        {
            CityBuilder b = NewBuilder(1);
            b.Build();
            b.Clear();

            Assert.AreEqual(0, b.transform.childCount);
            Assert.IsNull(b.Slice);
            Assert.IsNull(b.Root);
        }

        [Test]
        public void BiggerRadiusBuildsMoreChunks()
        {
            CityBuilder b = NewBuilder(2);
            b.Build();
            Assert.AreEqual(25, b.Slice.Chunks.Count);
        }

        [Test]
        public void MissingMaterialsFailLoudly()
        {
            var go = new GameObject("CityBuilderTest");
            _cleanup.Add(go);
            var builder = go.AddComponent<CityBuilder>();
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }
    }
}
