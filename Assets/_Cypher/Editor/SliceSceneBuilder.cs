using System.IO;
using Cypher.City;
using Cypher.Core;
using Cypher.Player;
using Cypher.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Cypher.EditorTools
{
    /// <summary>
    /// Creates the city materials and the Slice scene. Idempotent: re-running updates the assets in
    /// place. Also callable from the command line: -executeMethod Cypher.EditorTools.SliceSceneBuilder.Build
    /// </summary>
    public static class SliceSceneBuilder
    {
        public const string MaterialsFolder = "Assets/_Cypher/Materials";
        public const string ScenePath = "Assets/Scenes/Slice.unity";
        public const string ShaderName = "Cypher/CityLit";
        public const string SkyShaderName = "Cypher/SkyGradient";
        public const string NeonShaderName = "Cypher/NeonAdd";
        public const string RainShaderName = "Cypher/Rain";

        public const string SettingsFolder = "Assets/_Cypher/Settings";
        public const string VolumeProfilePath = SettingsFolder + "/NightVolume.asset";

        [MenuItem("Cypher/Build Slice Scene")]
        public static void Build()
        {
            CityMaterials mats = EnsureMaterials();
            VolumeProfile profile = EnsureVolumeProfile();
            Material sky = EnsureSkyMaterial();
            Material rainMaterial = EnsureRainMaterial();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Light moon = CreateMoon();
            GameObject camera = CreatePlayer();

            var cityGo = new GameObject("City");
            cityGo.AddComponent<CityBuilder>().Configure(CityPlan.DefaultSeed, 1, mats);

            var post = new GameObject("Post FX");
            var volume = post.AddComponent<UnityEngine.Rendering.Volume>();   // not Cypher.Core.Volume, the building massing type
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            var rendering = new GameObject("Rendering");
            rendering.AddComponent<LowResController>();
            var night = rendering.AddComponent<NightLighting>();
            night.Configure(moon, camera.GetComponent<Camera>(), sky, (float)NightRig.DefaultTimeOfDay, (float)NightRig.DefaultRain, (float)NightRig.DefaultFog);
            night.Apply();   // bakes ambient, fog and the moon into the scene; shader globals are re-applied on enable

            // Camera-following rain; on by default, switch it off with RainController.Raining.
            var rain = new GameObject("Rain");
            rain.AddComponent<RainController>().Configure(camera.transform, rainMaterial, (float)NightRig.DefaultRain,
                (float)RainModel.DefaultWind, RainController.DefaultMaxParticles);

            // One box-projected probe over the slice, captured once after the city exists.
            Rect2 extent = CityWindow.AroundPlaza(1).RoadExtent;
            var probeGo = new GameObject("Street Reflection");
            probeGo.AddComponent<StreetReflectionProbe>().Configure(
                new Vector3((float)extent.CentreX, 6f, (float)extent.CentreZ),
                new Vector3((float)extent.Width + 40f, 80f, (float)extent.Depth + 40f),
                StreetReflectionProbe.DefaultResolution, captureAutomatically: true);

            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"[Cypher] Slice scene saved to {ScenePath}; camera '{camera.name}' at spawn.");
        }

        public static Material EnsureRainMaterial()
        {
            Directory.CreateDirectory(MaterialsFolder);
            Shader shader = Shader.Find(RainShaderName);
            if (shader == null) throw new FileNotFoundException($"Shader {RainShaderName} not found; has it been imported?");
            return Ensure("CityRain", shader, m => m.SetFloat("_Intensity", 1f));
        }

        public static Material EnsureSkyMaterial()
        {
            Directory.CreateDirectory(MaterialsFolder);
            Shader shader = Shader.Find(SkyShaderName);
            if (shader == null) throw new FileNotFoundException($"Shader {SkyShaderName} not found; has it been imported?");
            return Ensure("NightSky", shader, _ => { });   // colours are driven by NightLighting
        }

        /// <summary>
        /// Bloom, Neutral tone mapping and a restrained vignette. Starting values: the prototype's bloom
        /// threshold/intensity are in a different unit (postprocessing library) so these are tuned by eye.
        /// </summary>
        public static VolumeProfile EnsureVolumeProfile()
        {
            Directory.CreateDirectory(SettingsFolder);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(0.8f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(false);   // cheaper; the Vega 8 is the floor

            var tone = GetOrAdd<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);

            var vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.4f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T existing)) return existing;
            T added = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(added, profile);
            return added;
        }

        public static CityMaterials EnsureMaterials()
        {
            Directory.CreateDirectory(MaterialsFolder);
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) throw new FileNotFoundException($"Shader {ShaderName} not found; has it been imported?");

            var walls = Ensure("CityWalls", shader, m =>
            {
                m.SetColor("_BaseColor", Color.white);   // the concrete texture carries the colour; vertex colour tints it
                m.SetFloat("_Smoothness", 1f);           // scales the smoothness map, which already holds roughness 0.94
                m.SetFloat("_Metallic", 0.05f);
                m.SetFloat("_WindowMix", 1f);
                m.SetColor("_EmissionColor", new Color(2.6f, 2.6f, 2.6f, 1f));   // HDR, linear: the prototype's emissiveIntensity
                m.SetColor("_GlassColor", new Color(0.039f, 0.055f, 0.094f));    // prototype window base 0x0a0e18
                m.SetFloat("_GlassSmoothness", 0.84f);                           // roughness 0.16
                m.SetFloat("_GlassMetallic", 0.72f);
            });
            var roofs = Ensure("CityRoofs", shader, m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", 1f);
                m.SetFloat("_Metallic", 0.05f);
            });
            var sidewalk = Ensure("CitySidewalk", shader, m =>
            {
                m.SetColor("_BaseColor", Color.white);   // paving texture; vertex colour does the kerb nosing
                m.SetFloat("_Smoothness", 1f);           // map holds roughness 0.9 with wet patches
                m.SetFloat("_Metallic", 0.30f);
            });
            var road = Ensure("CityRoad", shader, m =>
            {
                m.SetColor("_BaseColor", new Color(0.600f, 0.639f, 0.714f));   // prototype street colour 0x99a3b6
                m.SetFloat("_Smoothness", 1f);                                  // map holds the puddles
                m.SetFloat("_Metallic", 0.50f);
            });
            var paint = Ensure("CityPaint", shader, m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", 0.30f);
                m.SetFloat("_Metallic", 0.10f);
                m.SetFloat("_VertexEmission", 0.06f);   // "slightly emissive so it catches the eye at night"
                m.SetFloat("_OffsetFactor", -2f);
                m.SetFloat("_OffsetUnits", -3f);
            });

            var metal = Ensure("CityMetal", shader, m =>
            {
                m.SetColor("_BaseColor", Color.white);   // housings are painted with vertex colour
                m.SetFloat("_Smoothness", 0.42f);        // prototype metal: roughness 0.58
                m.SetFloat("_Metallic", 0.78f);
            });

            Shader neonShader = Shader.Find(NeonShaderName);
            if (neonShader == null) throw new FileNotFoundException($"Shader {NeonShaderName} not found; has it been imported?");
            var neon = Ensure("CityNeon", neonShader, m => m.SetFloat("_Brightness", 1f));

            AssetDatabase.SaveAssets();
            return new CityMaterials { Walls = walls, Roofs = roofs, Sidewalk = sidewalk, Road = road, Paint = paint, Metal = metal, Neon = neon };
        }

        static Material Ensure(string name, Shader shader, System.Action<Material> configure)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            configure(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>The key light. Colour, intensity and direction are set by NightLighting.</summary>
        static Light CreateMoon()
        {
            var go = new GameObject("Moonlight");
            return go.AddComponent<Light>();
        }

        /// <summary>Player on the plaza deck, camera as its child. Returns the camera object.</summary>
        static GameObject CreatePlayer()
        {
            // A little above the deck: the controller drops to rest in the first frames.
            var player = new GameObject("Player") { tag = "Player" };
            player.transform.position = new Vector3(0f, (float)CityLayout.SidewalkHeight + 0.1f, 0f);
            var controller = player.AddComponent<FirstPersonController>();

            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetParent(player.transform, false);
            go.transform.localPosition = new Vector3(0f, (float)PlayerMotor.EyeStand, 0f);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1500f;   // the roadbed quad is ~1 km across
            cam.clearFlags = CameraClearFlags.Skybox;   // set again by NightLighting
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            go.AddComponent<AudioListener>();

            var so = new SerializedObject(controller);
            so.FindProperty("cameraPivot").objectReferenceValue = go.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }
    }
}
