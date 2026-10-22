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
    /// <summary>The street props, decals, colliders and steam as the city builder makes them in the engine.</summary>
    public class CityPropsTests
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

        Material Make(Shader shader, string name)
        {
            var m = new Material(shader) { name = name };
            _cleanup.Add(m);
            return m;
        }

        CityMaterials Materials(bool withProps)
        {
            Shader city = Shader.Find("Cypher/CityLit");
            Shader neon = Shader.Find("Cypher/NeonAdd");
            Shader steam = Shader.Find("Cypher/Steam");
            Assert.IsNotNull(city); Assert.IsNotNull(neon); Assert.IsNotNull(steam);
            var mats = new CityMaterials
            {
                Walls = Make(city, "walls"), Roofs = Make(city, "roofs"), Sidewalk = Make(city, "sidewalk"), Road = Make(city, "road"),
                Paint = Make(city, "paint"), Metal = Make(city, "metal"), Neon = Make(neon, "neon"),
            };
            if (!withProps) return mats;

            mats.Props = Make(city, "props");
            mats.Props.SetFloat("_PropSurface", 1f);
            mats.Decals = Make(city, "decals");
            mats.Decals.SetFloat("_PropSurface", 2f);
            mats.Steam = Make(steam, "steam");
            return mats;
        }

        CityBuilder Build(int radius, bool withProps = true, QualityTier tier = null)
        {
            var go = new GameObject("CityPropsTest");
            _cleanup.Add(go);
            var builder = go.AddComponent<CityBuilder>();   // edit mode does not run Awake: build explicitly
            builder.Configure(CityPlan.DefaultSeed, radius, Materials(withProps));
            if (tier != null) builder.ApplyQuality(tier);
            builder.Build();
            Assert.IsTrue(builder.IsBuilt);
            return builder;
        }

        static IEnumerable<Transform> Chunks(CityBuilder b) => b.Root.Cast<Transform>().Where(t => t.name.StartsWith("Chunk_"));

        [Test]
        public void TheSteamShaderCompilesWithoutErrors()
        {
            Shader shader = Shader.Find("Cypher/Steam");
            Assert.IsNotNull(shader);
            var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity == ShaderCompilerMessageSeverity.Error).ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => $"{e.file}({e.line}): {e.message}")));
        }

        [Test]
        public void ADressedBlockHasAPropsMeshAndADecalsMeshWithTheirMaterials()
        {
            CityBuilder b = Build(1);
            Assert.IsNotNull(b.Dressing);
            Assert.IsNotNull(b.Props);
            Assert.AreEqual(9, b.Dressing.BlockCount, "a block for each of the 3x3 cells");
            foreach (Transform chunk in Chunks(b))
            {
                Transform props = chunk.Find(CityBuilder.PropsName), decals = chunk.Find(CityBuilder.DecalsName);
                Assert.IsNotNull(props, chunk.name); Assert.IsNotNull(decals, chunk.name);
                Assert.IsNotNull(props.GetComponent<MeshRenderer>(), $"{chunk.name}: props mesh");
                Assert.IsNotNull(decals.GetComponent<MeshRenderer>(), $"{chunk.name}: decals mesh");
                Assert.AreEqual(ShadowCastingModeOff, props.GetComponent<MeshRenderer>().shadowCastingMode);
            }
            Assert.Greater(b.Props.PropTriangles, 20000);
        }

        const UnityEngine.Rendering.ShadowCastingMode ShadowCastingModeOff = UnityEngine.Rendering.ShadowCastingMode.Off;

        [Test]
        public void TheBakedTexturesAreOnTheMaterialInstances()
        {
            CityBuilder b = Build(1);
            Transform chunk = Chunks(b).First();
            Material props = chunk.Find(CityBuilder.PropsName).GetComponent<MeshRenderer>().sharedMaterial;
            Material decals = chunk.Find(CityBuilder.DecalsName).GetComponent<MeshRenderer>().sharedMaterial;
            Material steam = b.Root.Find(CityBuilder.SteamName).GetComponent<MeshRenderer>().sharedMaterial;

            Texture grime = props.GetTexture("_BaseMap");
            Assert.IsNotNull(grime);
            Assert.AreEqual(PropTextures.GrimeSize, grime.width);
            Assert.AreEqual(TextureWrapMode.Repeat, grime.wrapMode);

            Texture atlas = decals.GetTexture("_BaseMap");
            Assert.IsNotNull(atlas);
            Assert.AreEqual((PropTextures.DecalAtlasWidth, PropTextures.DecalAtlasHeight), (atlas.width, atlas.height));
            Assert.AreEqual(TextureWrapMode.Clamp, atlas.wrapMode, "a decal's tile edge must never wrap round to the other side");

            Texture puff = steam.GetTexture("_BaseMap");
            Assert.IsNotNull(puff);
            Assert.AreEqual(PropTextures.PuffSize, puff.width);
            Assert.AreNotSame(Materials(true).Props, props, "instances with their own textures, not the project assets");
        }

        [Test]
        public void PropMeshesCarryTheSurfaceInTheSecondUvSetAndTheColourInTheFirstColour()
        {
            CityBuilder b = Build(1);
            Mesh mesh = Chunks(b).First().Find(CityBuilder.PropsName).GetComponent<MeshFilter>().sharedMesh;
            var uv1 = new List<Vector4>();
            mesh.GetUVs(1, uv1);
            Assert.AreEqual(mesh.vertexCount, uv1.Count);
            Assert.IsTrue(uv1.Any(v => v.x > 1f), "something glows");
            Assert.IsTrue(uv1.All(v => v.y >= 0f && v.y <= 1f && v.z >= 0f && v.z <= 1f), "roughness and metalness are 0 to 1");
            var colours = new List<Color>();
            mesh.GetColors(colours);
            Assert.AreEqual(mesh.vertexCount, colours.Count);
        }

        [Test]
        public void EverySolidPropGetsATurnedBoxColliderOnItsOwnObject()
        {
            CityBuilder b = Build(1);
            // props in the road just beyond a small window belong to a cell with no chunk to hang on; the whole city has no such cells
            CityWindow window = CityWindow.AroundPlaza(1);
            int inWindow = b.Props.Cells.Where(kv => window.Contains(kv.Key.Gx, kv.Key.Gz)).Sum(kv => kv.Value.Boxes.Count);
            Assert.AreEqual(inWindow, b.PropColliderCount, "one collider for each box in a cell that has a chunk");
            int counted = 0;
            foreach (Transform chunk in Chunks(b))
            {
                Transform holder = chunk.Find(CityBuilder.PropCollidersName);
                if (holder == null) continue;
                foreach (Transform t in holder)
                {
                    var col = t.GetComponent<BoxCollider>();
                    Assert.IsNotNull(col);
                    Assert.Greater(col.size.x, 0f); Assert.Greater(col.size.y, 0f); Assert.Greater(col.size.z, 0f);
                    counted++;
                }
            }
            Assert.AreEqual(b.PropColliderCount, counted);
            Assert.Greater(counted, 50);
        }

        [Test]
        public void InTheWholeCityNoColliderIsLost()
        {
            CityBuilder b = Build(3);
            Assert.AreEqual(b.Props.Layout.Boxes.Count, b.PropColliderCount);
            Assert.AreEqual(b.Props.Layout.Instances.Count, b.Props.Cells.Values.Sum(c => c.InstanceCount));
        }

        [Test]
        public void AColliderIsTurnedLikeItsPropAndSitsWhereItsBoxIs()
        {
            CityBuilder b = Build(1);
            PropBox box = b.Props.Cells.Values.SelectMany(c => c.Boxes).First(x => Math.Abs(x.Yaw) > 0.5);
            bool found = false;
            foreach (Transform chunk in Chunks(b))
            {
                Transform holder = chunk.Find(CityBuilder.PropCollidersName);
                if (holder == null) continue;
                foreach (Transform t in holder)
                {
                    if (Vector3.Distance(t.position, new Vector3((float)box.Cx, (float)box.Cy, (float)box.Cz)) > 0.01f) continue;
                    found = true;
                    Vector3 forward = t.rotation * Vector3.forward;
                    Assert.AreEqual(Math.Sin(box.Yaw), forward.x, 1e-3, "+Z swings toward +X for a positive yaw, as in the baked geometry");
                    Assert.AreEqual(Math.Cos(box.Yaw), forward.z, 1e-3);
                    Assert.AreEqual(box.Hx * 2, t.GetComponent<BoxCollider>().size.x, 1e-3);
                }
            }
            Assert.IsTrue(found, "the box has an object");
        }

        [Test]
        public void TheSteamIsOneRendererWithAllThePuffs()
        {
            CityBuilder b = Build(3);
            Transform steam = b.Root.Find(CityBuilder.SteamName);
            Assert.IsNotNull(steam);
            Mesh mesh = steam.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(96 * 4, mesh.vertexCount);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, steam.GetComponent<MeshRenderer>().shadowCastingMode);
            Assert.Greater(mesh.bounds.size.y, 12f, "room for the puffs to rise");
        }

        [Test]
        public void WithoutThePropMaterialsTheCityIsBare()
        {
            CityBuilder b = Build(1, withProps: false);
            Assert.IsNull(b.Dressing);
            Assert.IsNull(b.Props);
            Assert.IsFalse(b.BuildsProps);
            Assert.AreEqual(0, b.PropColliderCount);
            Assert.IsNull(b.Root.Find(CityBuilder.SteamName));
            Assert.IsNull(Chunks(b).First().Find(CityBuilder.PropsName));
        }

        [Test]
        public void PropsCanBeSwitchedOffForABuild()
        {
            var go = new GameObject("NoProps");
            _cleanup.Add(go);
            var b = go.AddComponent<CityBuilder>();
            b.Configure(CityPlan.DefaultSeed, 1, Materials(true));
            b.ConfigureProps(false);
            b.Build();
            Assert.IsNull(b.Dressing);
            Assert.IsNull(b.Root.Find(CityBuilder.SteamName));
        }

        [Test]
        public void EveryStageIncludingThePropsIsTimed()
        {
            CityBuilder b = Build(1);
            Assert.IsTrue(b.StageMilliseconds.ContainsKey(CityBuilder.StepProps));
            Assert.GreaterOrEqual(b.StageMilliseconds[CityBuilder.StepProps], 0.0);
        }

        // ---------------------------------------------------------------- distance and quality

        [Test]
        public void FarBlocksAreHiddenAndNearOnesShown()
        {
            CityBuilder b = Build(3);
            PropDressing d = b.Dressing;
            Assert.AreEqual(49, d.BlockCount);

            d.DrawDistance = 125f;
            d.Refresh(new Vector3(0f, 1.7f, 0f));
            Assert.That(d.VisibleBlocks, Is.InRange(5, 30), "the middle of the city sees its surroundings, not all of it");

            d.Refresh(new Vector3(5000f, 1.7f, 5000f));
            Assert.AreEqual(0, d.VisibleBlocks, "from far away nothing is drawn");

            d.DrawDistance = 10000f;
            d.Refresh(new Vector3(0f, 1.7f, 0f));
            Assert.AreEqual(49, d.VisibleBlocks, "with an enormous distance everything is");
        }

        [Test]
        public void HiddenBlocksReallyTurnTheirRenderersOff()
        {
            CityBuilder b = Build(3);
            b.Dressing.DrawDistance = 60f;
            b.Dressing.Refresh(new Vector3(0f, 1.7f, 0f));
            int on = 0, off = 0;
            foreach (Transform chunk in Chunks(b))
            {
                var r = chunk.Find(CityBuilder.PropsName).GetComponent<MeshRenderer>();
                if (r.enabled) on++; else off++;
            }
            Assert.Greater(on, 0);
            Assert.Greater(off, 20);
            Assert.AreEqual(b.Dressing.VisibleBlocks, on);
        }

        [Test]
        public void ABlockJustBeyondTheDistanceStaysOnceShownUntilTheMargin()
        {
            var go = new GameObject("DressingOnly");
            _cleanup.Add(go);
            var d = go.AddComponent<PropDressing>();
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);   // bounds 99.5 .. 100.5 along x
            _cleanup.Add(cube);
            cube.transform.position = new Vector3(100f, 0f, 0f);
            var renderer = cube.GetComponent<MeshRenderer>();
            d.Add(renderer.bounds, renderer);
            d.DrawDistance = 100f;

            d.Refresh(new Vector3(0f, 0f, 0f));     // 99.5 m away: shown
            Assert.IsTrue(renderer.enabled);
            d.Refresh(new Vector3(-1f, 0f, 0f));    // 100.5 m: beyond the distance but inside the margin, and it was shown
            Assert.IsTrue(renderer.enabled, "no flicker at the edge");
            d.Refresh(new Vector3(-20f, 0f, 0f));   // 119.5 m: beyond the margin too
            Assert.IsFalse(renderer.enabled);
            d.Refresh(new Vector3(-1f, 0f, 0f));    // back to 100.5 m: it was hidden, so it has to come properly inside to return
            Assert.IsFalse(renderer.enabled, "and it does not come back until it is inside the distance");
            d.Refresh(new Vector3(0f, 0f, 0f));
            Assert.IsTrue(renderer.enabled);
        }

        [Test]
        public void RegisteringNothingToSwitchIsHarmlessAndAMissingRendererFailsLoudly()
        {
            var go = new GameObject("DressingOnly");
            _cleanup.Add(go);
            var d = go.AddComponent<PropDressing>();
            d.Add(new Bounds(Vector3.zero, Vector3.one), new Renderer[] { null });
            Assert.AreEqual(0, d.BlockCount, "a block with no renderer is not a block");
            Assert.Throws<ArgumentNullException>(() => d.Add(new Bounds(Vector3.zero, Vector3.one), null));
            d.Refresh();   // no camera, no viewer: nothing happens
        }

        [Test]
        public void ABadDrawDistanceFailsLoudly()
        {
            CityBuilder b = Build(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Dressing.DrawDistance = -1f);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Dressing.DrawDistance = float.PositiveInfinity);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Dressing.DrawDistance = float.NaN);
        }

        [Test]
        public void AQualityTierSetsTheDrawDistanceAndTheSteam()
        {
            CityBuilder b = Build(1);
            MeshRenderer steam = b.Root.Find(CityBuilder.SteamName).GetComponent<MeshRenderer>();

            b.ApplyQuality(QualityTiers.For(QualityPreset.Performance));
            Assert.AreEqual(70f, b.Dressing.DrawDistance);
            Assert.IsFalse(steam.enabled, "no steam on the cheapest tier");
            Assert.IsFalse(b.Dressing.SteamVisible);

            b.ApplyQuality(QualityTiers.For(QualityPreset.Ultra));
            Assert.AreEqual(210f, b.Dressing.DrawDistance);
            Assert.IsTrue(steam.enabled);
        }

        [Test]
        public void ATierChosenBeforeTheCityIsBuiltIsRemembered()
        {
            CityBuilder b = Build(1, tier: QualityTiers.For(QualityPreset.Low));
            Assert.AreEqual(95f, b.Dressing.DrawDistance);
            Assert.IsTrue(b.Dressing.SteamVisible);
            Assert.Throws<ArgumentNullException>(() => b.ApplyQuality(null));
        }

        [Test]
        public void ClearingTheCityLeavesNoDressingBehind()
        {
            CityBuilder b = Build(1);
            b.Clear();
            Assert.IsNull(b.Dressing);
            Assert.IsNull(b.Props);
            Assert.AreEqual(0, b.PropColliderCount);
            Assert.IsNull(b.Root);
        }

        [Test]
        public void BuildingAgainReplacesTheDressing()
        {
            CityBuilder b = Build(1);
            PropDressing first = b.Dressing;
            b.Build();
            Assert.IsNotNull(b.Dressing);
            Assert.AreNotSame(first, b.Dressing);
            Assert.AreEqual(9, b.Dressing.BlockCount);
        }

        [Test]
        public void MaterialsWithoutPropsReportThatTheyHaveNone()
        {
            Assert.IsFalse(Materials(false).HasProps);
            Assert.IsTrue(Materials(true).HasProps);
            var mats = Materials(true);
            mats.Steam = null;
            Assert.IsFalse(mats.HasProps, "all three are needed");
            Assert.IsTrue(mats.IsComplete, "the original seven are enough for a city");
        }

        [Test]
        public void ThePropSurfaceShaderModeIsSetOnTheRightMaterials()
        {
            CityBuilder b = Build(1);
            Transform chunk = Chunks(b).First();
            Assert.AreEqual(1f, chunk.Find(CityBuilder.PropsName).GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_PropSurface"));
            Assert.AreEqual(2f, chunk.Find(CityBuilder.DecalsName).GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_PropSurface"));
            Assert.AreEqual(0f, chunk.Find("Walls").GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_PropSurface"), "the buildings are unchanged");
        }
    }
}
