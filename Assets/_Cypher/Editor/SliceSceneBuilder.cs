using System.IO;
using Cypher.Actors;
using Cypher.City;
using Cypher.Core;
using Cypher.Game;
using Cypher.Perf;
using Cypher.Player;
using Cypher.Rendering;
using Cypher.UI;
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
        public const string SteamShaderName = "Cypher/Steam";
        public const string BoardShaderName = "Cypher/NeonBoard";
        public const string PedestrianShaderName = "Cypher/Pedestrian";
        public const string PedestrianBlobShaderName = "Cypher/PedestrianBlob";
        public const string VehicleShaderName = "Cypher/Vehicle";
        public const string VehicleGlowShaderName = "Cypher/VehicleGlow";
        public const string VehicleShadowShaderName = "Cypher/VehicleShadow";
        public const string SignalShaderName = "Cypher/Signal";

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
            FirstPersonController playerController = camera.transform.parent.GetComponent<FirstPersonController>();

            // Where a new game starts; the player is parked here until the title screen is left.
            var spawn = new GameObject("Spawn");
            spawn.transform.position = camera.transform.parent.position;

            var cityGo = new GameObject("City");
            var cityBuilder = cityGo.AddComponent<CityBuilder>();
            cityBuilder.Configure(CityPlan.DefaultSeed, CityLayout.PlazaIndex, mats);   // the whole 7x7 city
            cityBuilder.ConfigureLoading(buildOverFrames: true, budgetMilliseconds: 24f);   // behind the loading bar; about 70 frames, not 200: on a busy machine each frame costs more than the work in it

            var post = new GameObject("Post FX");
            var volume = post.AddComponent<UnityEngine.Rendering.Volume>();   // not Cypher.Core.Volume, the building massing type
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            var rendering = new GameObject("Rendering");
            var lowRes = rendering.AddComponent<LowResController>();
            var night = rendering.AddComponent<NightLighting>();
            night.Configure(moon, camera.GetComponent<Camera>(), sky, (float)NightRig.DefaultTimeOfDay, (float)NightRig.DefaultRain, (float)NightRig.DefaultFog);
            night.Apply();   // bakes ambient, fog and the moon into the scene; shader globals are re-applied on enable

            // Camera-following rain; on by default, switch it off with RainController.Raining.
            var rain = new GameObject("Rain");
            var rainController = rain.AddComponent<RainController>();
            rainController.Configure(camera.transform, rainMaterial, (float)NightRig.DefaultRain,
                (float)RainModel.DefaultWind, RainController.DefaultMaxParticles);

            // A box-projected probe per district, each captured once after the whole city exists (GameFlow asks for the capture).
            var probeGo = new GameObject("Street Reflection");
            var street = probeGo.AddComponent<StreetReflectionProbe>();
            street.Configure(
                ProbeLayout.Build(CityWindow.AroundPlaza(CityLayout.PlazaIndex), ProbeLayout.DefaultPerAxis, WorldEdge.Apron),
                StreetReflectionProbe.DefaultResolution, captureAutomatically: false);

            // Applies the quality preset (resolution, MSAA, bloom, rain cap, probe size) and lowers it when frames miss the budget.
            var quality = new GameObject("Quality");
            quality.AddComponent<QualityDirector>().Configure(lowRes, rainController, street, volume);

            // Perf overlay (F3) and the --benchmark mode; the runner does nothing unless the flag is on the command line.
            var perf = new GameObject("Perf");
            var overlay = perf.AddComponent<PerfOverlay>();
            perf.AddComponent<BenchmarkRunner>();

            // The title flight, the settings, and the flow that ties loading, title, playing and pausing together.
            var titleRig = new GameObject("Title Camera");
            var titleCamera = titleRig.AddComponent<TitleCamera>();
            var game = new GameObject("Game");
            var settings = game.AddComponent<Cypher.Game.SettingsService>();   // not UnityEditor.SettingsService
            settings.Configure(quality.GetComponent<QualityDirector>(), camera.GetComponent<Camera>(), rainController, overlay, playerController);
            var flow = game.AddComponent<GameFlow>();
            flow.Configure(playerController, camera.transform, spawn.transform, titleCamera, quality.GetComponent<QualityDirector>(), cityBuilder, street);
            var uiRoot = game.AddComponent<UiRoot>();
            uiRoot.Configure(flow, settings);   // loading, title, pause, options, dialogs and the Esc key
            game.AddComponent<UiShots>().Configure(uiRoot, flow, settings);   // only does anything with --shots DIR

            // The people: created round the player when a game starts, gone again at the title.
            (Material peopleMaterial, Material peopleShadow) = EnsurePedestrianMaterials();
            var crowdGo = new GameObject("Crowd");
            var crowd = crowdGo.AddComponent<CrowdController>();
            crowd.Configure(flow, cityBuilder, playerController, camera.GetComponent<Camera>(), quality.GetComponent<QualityDirector>(), peopleMaterial, peopleShadow);

            // The traffic and the aircraft: likewise, and they and the people look out for each other.
            var trafficGo = new GameObject("Traffic");
            var traffic = trafficGo.AddComponent<TrafficController>();
            traffic.Configure(flow, cityBuilder, playerController, camera.GetComponent<Camera>(), quality.GetComponent<QualityDirector>(), crowd, EnsureTrafficMaterials());

            // The sound: the menu sounds from the first screen, the city's bed and the score from a new game, through the generated mixer.
            AudioMixerWriter.Write();
            var audioMixer = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>(AudioMixerWriter.Path);
            if (audioMixer == null) throw new FileNotFoundException($"The mixer {AudioMixerWriter.Path} did not import.");
            var audioGo = new GameObject("Audio");
            audioGo.AddComponent<Cypher.Audio.AudioEngine>().Configure(audioMixer, flow, settings, playerController, camera.transform, cityBuilder, crowd, traffic, rainController);

            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"[Cypher] Slice scene saved to {ScenePath}; camera '{camera.name}' at spawn.");
        }

        /// <summary>The pedestrians' body material and the contact-shadow material under them.</summary>
        public static (Material body, Material shadow) EnsurePedestrianMaterials()
        {
            Directory.CreateDirectory(MaterialsFolder);
            Shader body = Shader.Find(PedestrianShaderName);
            if (body == null) throw new FileNotFoundException($"Shader {PedestrianShaderName} not found; has it been imported?");
            Shader blob = Shader.Find(PedestrianBlobShaderName);
            if (blob == null) throw new FileNotFoundException($"Shader {PedestrianBlobShaderName} not found; has it been imported?");
            Material bodyMaterial = Ensure("CityPeople", body, _ => { });
            Material shadowMaterial = Ensure("CityPeopleShadow", blob, m =>
            {
                m.SetFloat("_Opacity", 0.42f);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            });
            AssetDatabase.SaveAssets();
            return (bodyMaterial, shadowMaterial);
        }

        /// <summary>The vehicles' paint and rubber, the signal masts, the glowing lamps, the shadow under each vehicle and the signal lenses.</summary>
        public static TrafficMaterials EnsureTrafficMaterials()
        {
            Directory.CreateDirectory(MaterialsFolder);
            Shader Find(string name)
            {
                Shader shader = Shader.Find(name);
                if (shader == null) throw new FileNotFoundException($"Shader {name} not found; has it been imported?");
                return shader;
            }
            Shader vehicle = Find(VehicleShaderName);
            var materials = new TrafficMaterials
            {
                // the prototype's materials: paint roughness 0.30 metalness 0.62; rubber 0x0d0e12 roughness 0.88 metalness 0.05; masts 0x14161c roughness 0.72 metalness 0.35
                Bodies = Ensure("CityVehicles", vehicle, m => { m.SetFloat("_Smoothness", 0.70f); m.SetFloat("_Metallic", 0.62f); m.SetFloat("_UsePaint", 1f); }),
                Wheels = Ensure("CityVehicleWheels", vehicle, m =>
                {
                    m.SetFloat("_Smoothness", 0.12f); m.SetFloat("_Metallic", 0.05f); m.SetFloat("_UsePaint", 0f);
                    m.SetColor("_FixedAlbedo", new Color32(0x0d, 0x0e, 0x12, 0xff));
                }),
                Masts = Ensure("CitySignalMasts", vehicle, m =>
                {
                    m.SetFloat("_Smoothness", 0.28f); m.SetFloat("_Metallic", 0.35f); m.SetFloat("_UsePaint", 0f);
                    m.SetColor("_FixedAlbedo", new Color32(0x14, 0x16, 0x1c, 0xff));
                }),
                Glow = Ensure("CityVehicleGlow", Find(VehicleGlowShaderName), m => m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent),
                Shadow = Ensure("CityVehicleShadow", Find(VehicleShadowShaderName), m =>
                {
                    m.SetFloat("_Opacity", 0.5f);
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }),
                Lenses = Ensure("CitySignalLenses", Find(SignalShaderName), _ => { }),
            };
            AssetDatabase.SaveAssets();
            return materials;
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

            // The street props: one CityLit material for every kit (per-vertex surface), one alpha-blended for the decals, one for the steam.
            var props = Ensure("CityProps", shader, m =>
            {
                m.SetFloat("_PropSurface", 1f);
                m.SetColor("_BaseColor", Color.white);   // the grime texture and the vertex colours carry everything
            });
            var decals = Ensure("CityDecals", shader, m =>
            {
                m.SetFloat("_PropSurface", 2f);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
                m.SetFloat("_OffsetFactor", -2f);   // a hair in front of the wall or the ground it lies on
                m.SetFloat("_OffsetUnits", -2f);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            });
            Shader steamShader = Shader.Find(SteamShaderName);
            if (steamShader == null) throw new FileNotFoundException($"Shader {SteamShaderName} not found; has it been imported?");
            var steam = Ensure("CitySteam", steamShader, m =>
            {
                m.SetFloat("_Wind", 0.7f);
                m.SetFloat("_FadeDistance", 60f);
            });

            // The LED hoardings (opaque) and the holograms (additive): one shader, told apart by _Holo.
            Shader boardShader = Shader.Find(BoardShaderName);
            if (boardShader == null) throw new FileNotFoundException($"Shader {BoardShaderName} not found; has it been imported?");
            var boards = Ensure("CityBoards", boardShader, m =>
            {
                m.SetFloat("_Holo", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            });
            var holos = Ensure("CityHolos", boardShader, m =>
            {
                m.SetFloat("_Holo", 1f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            });

            AssetDatabase.SaveAssets();
            return new CityMaterials { Walls = walls, Roofs = roofs, Sidewalk = sidewalk, Road = road, Paint = paint, Metal = metal, Neon = neon, Props = props, Decals = decals, Steam = steam, Boards = boards, Holos = holos };
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
            cam.farClipPlane = 520f;   // exp2 fog at its thinnest (density 0.0075) is opaque past ~350 m; the roadbed quad is ~1 km across but its far part is fog
            cam.clearFlags = CameraClearFlags.Skybox;   // set again by NightLighting
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            go.AddComponent<AudioListener>();

            var so = new SerializedObject(controller);
            so.FindProperty("cameraPivot").objectReferenceValue = go.transform;
            so.FindProperty("lockCursor").boolValue = false;   // GameFlow locks and frees the cursor
            so.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }
    }
}
