using System.Linq;
using Cypher.Core;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.Tests
{
    public class NightLightingTests
    {
        GameObject _go;
        Light _moon;
        Camera _camera;
        Material _skyMat;
        Material _savedSkybox;

        // Apply() writes global scene state, so put it back afterwards.
        AmbientMode _ambientMode;
        Color _sky, _equator, _ground, _fogColor;
        bool _fog;
        FogMode _fogMode;
        float _fogDensity;

        [SetUp]
        public void SetUp()
        {
            _ambientMode = RenderSettings.ambientMode;
            _sky = RenderSettings.ambientSkyColor;
            _equator = RenderSettings.ambientEquatorColor;
            _ground = RenderSettings.ambientGroundColor;
            _fog = RenderSettings.fog;
            _fogMode = RenderSettings.fogMode;
            _fogColor = RenderSettings.fogColor;
            _fogDensity = RenderSettings.fogDensity;

            _savedSkybox = RenderSettings.skybox;
            Shader skyShader = Shader.Find("Cypher/SkyGradient");
            Assert.IsNotNull(skyShader, "Cypher/SkyGradient shader not found");
            _skyMat = new Material(skyShader);

            _go = new GameObject("NightLightingTest");
            _moon = new GameObject("Moon").AddComponent<Light>();
            _moon.transform.SetParent(_go.transform);
            _camera = new GameObject("Cam").AddComponent<Camera>();
            _camera.transform.SetParent(_go.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_skyMat);
            RenderSettings.skybox = _savedSkybox;
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientSkyColor = _sky;
            RenderSettings.ambientEquatorColor = _equator;
            RenderSettings.ambientGroundColor = _ground;
            RenderSettings.fog = _fog;
            RenderSettings.fogMode = _fogMode;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogDensity = _fogDensity;
            foreach (string id in new[] { "_CypherFillDir0", "_CypherFillDir1", "_CypherFillDir2", "_CypherFillColor0", "_CypherFillColor1", "_CypherFillColor2" })
            {
                Shader.SetGlobalVector(id, Vector4.zero);
            }
        }

        NightLighting Make(float tod = (float)NightRig.DefaultTimeOfDay, float rain = (float)NightRig.DefaultRain, float fog = (float)NightRig.DefaultFog)
        {
            var nl = _go.AddComponent<NightLighting>();
            nl.Configure(_moon, _camera, _skyMat, tod, rain, fog);
            nl.Apply();
            return nl;
        }

        [Test]
        public void FogMatchesThePrototypeAtBootState()
        {
            Make();
            Assert.IsTrue(RenderSettings.fog);
            Assert.AreEqual(FogMode.ExponentialSquared, RenderSettings.fogMode);
            Assert.AreEqual(0.00822f, RenderSettings.fogDensity, 1e-6f);
        }

        [Test]
        public void FogColourIsThePrototypesNightFog()
        {
            Make();
            // 0x2a2740 survives the linear round trip through Unity's gamma colour fields.
            Color expected = new Color(0x2a / 255f, 0x27 / 255f, 0x40 / 255f);
            Assert.AreEqual(expected.r, RenderSettings.fogColor.r, 0.01f);
            Assert.AreEqual(expected.g, RenderSettings.fogColor.g, 0.01f);
            Assert.AreEqual(expected.b, RenderSettings.fogColor.b, 0.01f);
        }

        [Test]
        public void SkyMaterialGetsTheNightPaletteAndBecomesTheSkybox()
        {
            Make();
            Assert.AreSame(_skyMat, RenderSettings.skybox);
            Color zenith = _skyMat.GetColor("_Zenith"), horizon = _skyMat.GetColor("_Horizon"), ground = _skyMat.GetColor("_Ground");
            Assert.AreEqual(0x0a / 255f, zenith.r, 0.01f);
            Assert.AreEqual(0x12 / 255f, zenith.g, 0.01f);
            Assert.AreEqual(0x26 / 255f, zenith.b, 0.01f);
            Assert.AreEqual(0x45 / 255f, horizon.r, 0.01f);
            Assert.AreEqual(0x30 / 255f, horizon.g, 0.01f);
            Assert.AreEqual(0x55 / 255f, horizon.b, 0.01f);
            Assert.AreEqual(RenderSettings.fogColor.r, ground.r, 0.005f, "below the horizon the sky is the fog colour");
            Assert.AreEqual(CameraClearFlags.Skybox, _camera.clearFlags);
        }

        [Test]
        public void WithoutASkyTheCameraClearsToTheFogColour()
        {
            var nl = _go.AddComponent<NightLighting>();
            nl.Configure(_moon, _camera, null, 21.5f, 0.45f, 0.6f);
            nl.Apply();
            Assert.IsNull(RenderSettings.skybox);
            Assert.AreEqual(CameraClearFlags.SolidColor, _camera.clearFlags);
            Assert.AreEqual(RenderSettings.fogColor, _camera.backgroundColor);
        }

        [Test]
        public void SkyShaderCompilesWithoutErrors()
        {
            var errors = UnityEditor.ShaderUtil.GetShaderMessages(_skyMat.shader)
                .Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => $"{e.file}({e.line}): {e.message}")));
        }

        [Test]
        public void AmbientIsATrilightWithTheEquatorBetweenSkyAndGround()
        {
            Make();
            Assert.AreEqual(AmbientMode.Trilight, RenderSettings.ambientMode);
            float eq = RenderSettings.ambientEquatorColor.linear.g;
            float lo = Mathf.Min(RenderSettings.ambientSkyColor.linear.g, RenderSettings.ambientGroundColor.linear.g);
            float hi = Mathf.Max(RenderSettings.ambientSkyColor.linear.g, RenderSettings.ambientGroundColor.linear.g);
            Assert.That(eq, Is.InRange(lo, hi));
        }

        [Test]
        public void MoonIsAnUnshadowedDirectionalLightShiningAwayFromTheMoon()
        {
            NightLighting nl = Make();
            Vec3 to = nl.State.Moon.ToLight;
            Assert.AreEqual(LightType.Directional, _moon.type);
            Assert.AreEqual(LightShadows.None, _moon.shadows);
            Vector3 travel = _moon.transform.forward;
            Assert.AreEqual(-(float)to.X, travel.x, 1e-4f);
            Assert.AreEqual(-(float)to.Y, travel.y, 1e-4f);
            Assert.AreEqual(-(float)to.Z, travel.z, 1e-4f);
            Assert.AreEqual((float)(nl.State.Moon.Intensity / System.Math.PI), _moon.intensity, 1e-5f);
        }

        [Test]
        public void ShaderGlobalsCarryTheFillDirectionsAndRadiance()
        {
            NightLighting nl = Make();
            Vector4 dir0 = Shader.GetGlobalVector("_CypherFillDir0");
            Vector4 col0 = Shader.GetGlobalVector("_CypherFillColor0");
            Assert.AreEqual((float)nl.State.FillWarm.ToLight.X, dir0.x, 1e-5f);
            Assert.AreEqual((float)nl.State.FillWarm.ToLight.Y, dir0.y, 1e-5f);
            Assert.AreEqual(nl.State.FillWarm.Radiance.R, col0.x, 1e-5f);
            Assert.AreEqual(nl.State.FillWarm.Radiance.B, col0.z, 1e-5f);

            Vector4 dir2 = Shader.GetGlobalVector("_CypherFillDir2");
            Assert.AreEqual(-1f, dir2.y, 1e-5f, "uplight comes from below");
            Assert.Greater(Shader.GetGlobalVector("_CypherFillColor1").z, Shader.GetGlobalVector("_CypherFillColor1").x, "cool fill is cyan: more blue than red");
        }

        [Test]
        public void ChangingTimeOfDayAndReapplyingMovesTheMoon()
        {
            NightLighting night = Make(21.5f);
            Vector3 before = _moon.transform.forward;

            night.Configure(_moon, _camera, _skyMat, 3f, 0f, 0f);
            night.Apply();
            Assert.That(_moon.transform.forward.x, Is.Not.EqualTo(before.x).Within(1e-3f), "the moon moved");
            Assert.AreEqual(0.0042f, RenderSettings.fogDensity, 1e-6f, "weather cleared");
        }

        [Test]
        public void WorksWithoutAMoonOrCamera()
        {
            var nl = _go.AddComponent<NightLighting>();
            nl.Configure(null, null, null, 21.5f, 0f, 0f);
            Assert.DoesNotThrow(() => nl.Apply());
        }
    }
}
