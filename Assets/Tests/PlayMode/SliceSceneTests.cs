#if UNITY_EDITOR
using System.Collections;
using Cypher.City;
using Cypher.Core;
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

        static IEnumerator LoadSlice()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneBuildsTheCityAndTheSpawnedPlayerComesToRestOnThePlazaDeck()
        {
            yield return LoadSlice();

            CityBuilder city = Object.FindFirstObjectByType<CityBuilder>();
            FirstPersonController player = Object.FindFirstObjectByType<FirstPersonController>();
            Assert.IsNotNull(city, "no CityBuilder in the Slice scene");
            Assert.IsNotNull(player, "no player in the Slice scene");
            Assert.AreEqual(9, city.Slice.Chunks.Count);

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
        public IEnumerator SceneHasOneMainCameraAtEyeHeightUnderThePlayer()
        {
            yield return LoadSlice();

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
            Assert.AreEqual(1, probe.CaptureCount);
            Assert.IsTrue(probe.Probe.boxProjection);
            Assert.Greater(probe.Probe.size.x, 200f, "the box covers the 3x3 window");
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

            Camera cam = Camera.main;
            Assert.AreEqual(cam.transform.position.x, rain.transform.position.x, 1e-3f);
            Assert.AreEqual(cam.transform.position.z, rain.transform.position.z, 1e-3f);
        }

        [UnityTest]
        public IEnumerator PlayerStaysOnTheGroundForSeveralSeconds()
        {
            yield return LoadSlice();

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
