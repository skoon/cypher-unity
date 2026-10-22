#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Cypher.City;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>Loads the saved Slice scene and checks the pieces work together, not just in isolation.</summary>
    public class SliceSceneTests
    {
        const string ScenePath = "Assets/Scenes/Slice.unity";

        [SetUp]
        public void SetUp()
        {
            // Never read or write the player's real settings file, and never inherit switches from the test runner's command line.
            SettingsService.StorageFactory = () => new MemorySettingsStorage();
            LaunchOptions.Override = new FlowOptions();
        }

        [TearDown]
        public void TearDown()
        {
            SettingsService.StorageFactory = null;
            LaunchOptions.Override = null;
            Time.timeScale = 1f;
        }

        static IEnumerator LoadSlice()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
        }

        /// <summary>Load the scene, wait out loading, and press New Game on the title screen.</summary>
        static IEnumerator LoadSlicePlaying()
        {
            yield return LoadSlice();
            var flow = Object.FindFirstObjectByType<GameFlow>();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (flow.State != FlowState.Title && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(FlowState.Title, flow.State, "loading never finished");
            Assert.IsTrue(flow.NewGame());
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneBuildsTheCityAndTheSpawnedPlayerComesToRestOnThePlazaDeck()
        {
            yield return LoadSlicePlaying();

            CityBuilder city = Object.FindFirstObjectByType<CityBuilder>();
            FirstPersonController player = Object.FindFirstObjectByType<FirstPersonController>();
            Assert.IsNotNull(city, "no CityBuilder in the Slice scene");
            Assert.IsNotNull(player, "no player in the Slice scene");
            Assert.AreEqual(49, city.Slice.Chunks.Count, "the whole 7x7 city");
            Assert.IsTrue(city.IsBuilt);

            float deadline = Time.time + 5f;
            while (!player.Motor.Grounded && Time.time < deadline) yield return null;
            yield return new WaitForSeconds(0.3f);

            Assert.IsTrue(player.Motor.Grounded, "player never touched down");
            Vector3 p = player.transform.position;
            Assert.AreEqual(0f, p.x, 0.05f);
            Assert.AreEqual(0f, p.z, 0.05f);
            Assert.That(p.y, Is.InRange((float)CityLayout.SidewalkHeight - 0.01f, (float)CityLayout.SidewalkHeight + 0.05f));
        }

        [UnityTest]
        public IEnumerator TheLoadingBarMovesWhileTheCityIsBuiltAndNeverRunsBackwards()
        {
            yield return LoadSlice();
            var flow = Object.FindFirstObjectByType<GameFlow>();
            var city = Object.FindFirstObjectByType<CityBuilder>();
            Assert.IsFalse(city.IsBuilt || flow.State == FlowState.Title, "loading is still going after the first frame");

            var seen = new System.Collections.Generic.List<double>();
            float deadline = Time.realtimeSinceStartup + 40f;
            while (flow.State == FlowState.Loading && Time.realtimeSinceStartup < deadline)
            {
                seen.Add(flow.LoadProgress);
                yield return null;
            }

            Assert.AreEqual(FlowState.Title, flow.State, "loading never finished");
            Assert.GreaterOrEqual(seen.Count, 20, "the bar had many frames to move in");
            for (int i = 1; i < seen.Count; i++) Assert.GreaterOrEqual(seen[i], seen[i - 1], $"the bar ran backwards at sample {i}");
            Assert.Less(seen.Max(), 1.0, "the bar is full only once everything is ready");
            Assert.GreaterOrEqual(seen.Distinct().Count(), 10, "it moved in many steps, not one jump");
            Assert.AreEqual(1.0, flow.LoadProgress);
            Assert.IsTrue(city.IsBuilt);
        }

        [UnityTest]
        public IEnumerator SceneHasOneMainCameraAtEyeHeightUnderThePlayer()
        {
            yield return LoadSlicePlaying();

            FirstPersonController player = Object.FindFirstObjectByType<FirstPersonController>();
            Camera cam = Camera.main;
            Assert.IsNotNull(cam, "no camera tagged MainCamera");
            Assert.IsTrue(cam.transform.IsChildOf(player.transform));
            Assert.AreEqual(1, Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length);
            Assert.AreEqual((float)PlayerMotor.EyeStand, cam.transform.localPosition.y, 0.01f);
        }

        [UnityTest]
        public IEnumerator SceneIsLowResWithNightLightingAndPostProcessing()
        {
            yield return LoadSlice();

            Assert.IsNotNull(Object.FindFirstObjectByType<LowResController>(), "no LowResController");
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.IsNotNull(asset);
            Assert.AreEqual(UpscalingFilterSelection.Point, asset.upscalingFilter);
            Assert.AreEqual(ResolutionScaler.RenderScale(LowResController.DefaultTargetHeight, Screen.height), asset.renderScale, 1e-5f);

            Assert.IsTrue(RenderSettings.fog);
            Assert.AreEqual(0.00822f, RenderSettings.fogDensity, 1e-6f);
            Assert.IsNotNull(RenderSettings.skybox, "no skybox");
            Assert.AreEqual("Cypher/SkyGradient", RenderSettings.skybox.shader.name);
            Assert.AreEqual(CameraClearFlags.Skybox, Camera.main.clearFlags);
            Assert.Greater(Shader.GetGlobalVector("_CypherFillColor0").x, 0f, "shader fill globals were not applied on enable");

            UnityEngine.Rendering.Volume volume = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            Assert.IsNotNull(volume);
            Assert.IsTrue(volume.isGlobal);
            Assert.IsTrue(volume.sharedProfile.TryGet(out Bloom bloom) && bloom.intensity.overrideState, "bloom");
            Assert.IsTrue(volume.sharedProfile.TryGet(out Tonemapping tone));
            Assert.AreEqual(TonemappingMode.Neutral, tone.mode.value);
            Assert.IsTrue(volume.sharedProfile.TryGet(out Vignette _), "vignette");
            Assert.IsTrue(Camera.main.GetUniversalAdditionalCameraData().renderPostProcessing);
        }

        [UnityTest]
        public IEnumerator SceneCapturesItsReflectionProbeAfterTheCityIsBuilt()
        {
            Assume.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null), "reflection probes need a GPU (run without -nographics)");
            yield return LoadSlice();

            var probe = Object.FindFirstObjectByType<StreetReflectionProbe>();
            Assert.IsNotNull(probe, "no StreetReflectionProbe in the Slice scene");
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!probe.IsCaptured && Time.realtimeSinceStartup < deadline) yield return null;

            Assert.IsTrue(probe.IsCaptured);
            Assert.AreEqual(1, probe.CaptureCount, "captured once, after the city was built");
            Assert.AreEqual(ProbeLayout.DefaultPerAxis * ProbeLayout.DefaultPerAxis, probe.Probes.Count, "a probe per district");
            foreach (ReflectionProbe p in probe.Probes)
            {
                Assert.IsTrue(p.boxProjection);
                Assert.Greater(p.size.x, 150f, "each box covers a district");
            }
        }

        [UnityTest]
        public IEnumerator SceneIsRainingByDefaultAndTheRainFollowsThePlayersCamera()
        {
            yield return LoadSlice();
            for (int i = 0; i < 3; i++) yield return null;

            var rain = Object.FindFirstObjectByType<RainController>();
            Assert.IsNotNull(rain, "no RainController in the Slice scene");
            Assert.IsTrue(rain.Raining, "rain is on by default");
            Assert.Greater(rain.System.particleCount, 100, "streaks are falling");
            Assert.LessOrEqual(rain.System.particleCount, RainController.DefaultMaxParticles);

            // The title camera moves in Update and the rain catches up in LateUpdate, so they differ by a frame of travel (about 3 m/s).
            Camera cam = Camera.main;
            Assert.AreEqual(cam.transform.position.x, rain.transform.position.x, 0.5f);
            Assert.AreEqual(cam.transform.position.z, rain.transform.position.z, 0.5f);
            Assert.Greater(new Vector2(rain.transform.position.x, rain.transform.position.z).magnitude, 5f, "it is around the camera, not parked at the origin");
        }

        [UnityTest]
        public IEnumerator SceneAppliesTheMediumPresetThroughTheQualityDirector()
        {
            yield return LoadSlice();
            yield return null;

            var director = Object.FindFirstObjectByType<QualityDirector>();
            Assert.IsNotNull(director, "no QualityDirector in the Slice scene");
            Assert.AreEqual(QualityPreset.Medium, director.Preset);
            Assert.IsTrue(director.Adaptive);

            // Medium is the Milestone 1 look, so applying it must leave the scene as it was built.
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.AreEqual(1, asset.msaaSampleCount);
            Assert.AreEqual(ResolutionScaler.RenderScale(540, Screen.height), asset.renderScale, 1e-5f);
            Assert.AreEqual(RainController.DefaultMaxParticles, Object.FindFirstObjectByType<RainController>().MaxParticles);
            Assert.AreEqual(StreetReflectionProbe.DefaultResolution, Object.FindFirstObjectByType<StreetReflectionProbe>().Resolution);
        }

        [UnityTest]
        public IEnumerator ChangingThePresetInTheSceneDoesNotTouchTheSavedVolumeProfile()
        {
            yield return LoadSlice();
            yield return null;

            var director = Object.FindFirstObjectByType<QualityDirector>();
            GraphicsOptions options = director.Options;
            options.Quality = QualityPreset.Ultra;
            director.Apply(options);

            UnityEngine.Rendering.Volume volume = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            Assert.IsTrue(volume.profile.TryGet(out Bloom live) && live.highQualityFiltering.value, "the live bloom is high quality");
            Assert.IsTrue(volume.sharedProfile.TryGet(out Bloom saved));
            Assert.IsFalse(saved.highQualityFiltering.value, "the saved profile asset still has standard filtering");
        }

        [UnityTest]
        public IEnumerator SceneHasThePerfOverlayOnAndTheBenchmarkRunnerIdle()
        {
            yield return LoadSlice();
            yield return null;

            var overlay = Object.FindFirstObjectByType<Cypher.Perf.PerfOverlay>();
            Assert.IsNotNull(overlay, "no PerfOverlay in the Slice scene");
            Assert.IsTrue(overlay.Visible, "the overlay is on by default");
            StringAssert.Contains("FPS", overlay.Text);

            var runner = Object.FindFirstObjectByType<Cypher.Perf.BenchmarkRunner>();
            Assert.IsNotNull(runner, "no BenchmarkRunner in the Slice scene");
            Assert.IsFalse(runner.enabled, "the benchmark only runs with --benchmark");
            Assert.IsNotInstanceOf<Cypher.Perf.PilotCommandSource>(Object.FindFirstObjectByType<FirstPersonController>().CommandSource);
        }

        [UnityTest]
        public IEnumerator PlayerStaysOnTheGroundForSeveralSeconds()
        {
            yield return LoadSlicePlaying();

            FirstPersonController player = Object.FindFirstObjectByType<FirstPersonController>();
            float end = Time.time + 3f;
            while (Time.time < end)
            {
                Assert.Greater(player.transform.position.y, -0.1f, "fell through the world");
                yield return null;
            }
        }
    }
}
#endif
