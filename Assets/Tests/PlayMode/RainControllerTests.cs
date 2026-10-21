using System;
using System.Collections;
using System.Text.RegularExpressions;
using Cypher.Core;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    public class RainControllerTests
    {
        GameObject _root;
        Transform _camera;
        Material _material;

        [SetUp]
        public void SetUp()
        {
            Shader shader = Shader.Find("Cypher/Rain");
            Assert.IsNotNull(shader, "Cypher/Rain shader not found");
            _material = new Material(shader);
            _camera = new GameObject("RainTestCamera").transform;
            _camera.position = new Vector3(5f, 2f, 7f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root) UnityEngine.Object.DestroyImmediate(_root);
            if (_camera) UnityEngine.Object.DestroyImmediate(_camera.gameObject);
            if (_material) UnityEngine.Object.DestroyImmediate(_material);
        }

        RainController Make(float intensity = 0.5f, float wind = 0.35f, int cap = 2000)
        {
            _root = new GameObject("RainTest");
            _root.SetActive(false);   // Configure before Awake builds the particle system
            var rain = _root.AddComponent<RainController>();
            rain.Configure(_camera, _material, intensity, wind, cap);
            _root.SetActive(true);
            return rain;
        }

        [Test]
        public void ParticleSystemIsConfiguredAsStretchedWorldSpaceStreaks()
        {
            RainController rain = Make();
            ParticleSystem.MainModule main = rain.System.main;
            Assert.AreEqual(ParticleSystemSimulationSpace.World, main.simulationSpace);
            Assert.IsTrue(main.prewarm);
            Assert.IsTrue(main.loop);
            Assert.AreEqual(2000, main.maxParticles);
            Assert.AreEqual((float)RainModel.Lifetime(RainModel.DefaultHeight, RainModel.DefaultFallSpeed), main.startLifetime.constant, 1e-5f);

            ParticleSystemRenderer r = rain.ParticleRenderer;
            Assert.AreEqual(ParticleSystemRenderMode.Stretch, r.renderMode);
            Assert.AreEqual((float)RainModel.StretchVelocityScale(RainModel.DefaultStreakLength, RainModel.DefaultFallSpeed), r.velocityScale, 1e-6f);
            Assert.AreSame(_material, r.sharedMaterial);
            Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode);
            Assert.IsFalse(r.receiveShadows);
            Assert.IsFalse(rain.System.collision.enabled);
        }

        [Test]
        public void StreaksFallAtTheModelSpeedWithAWindSlant()
        {
            RainController rain = Make(wind: 0.5f);
            ParticleSystem.VelocityOverLifetimeModule v = rain.System.velocityOverLifetime;
            Assert.IsTrue(v.enabled);
            Assert.AreEqual(-(float)RainModel.DefaultFallSpeed, v.y.constant, 1e-5f);
            Assert.AreEqual((float)RainModel.WindDrift(0.5), v.x.constant, 1e-5f);
            Assert.Greater(v.z.constant, 0f);
        }

        [Test]
        public void SteadyStateDensityTracksIntensityAndNeverExceedsTheCap()
        {
            foreach (float intensity in new[] { 0.25f, 0.5f, 1f })
            {
                RainController rain = Make(intensity: intensity, cap: 2000);
                rain.System.Simulate(6f, true, true);
                int count = rain.System.particleCount;
                Assert.That(count, Is.EqualTo(2000 * intensity).Within(2000 * intensity * 0.2 + 30), $"intensity {intensity}");
                Assert.LessOrEqual(count, 2000);
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void IntensityZeroMeansNoStreaks()
        {
            RainController rain = Make(intensity: 0f);
            rain.System.Simulate(4f, true, true);
            Assert.AreEqual(0, rain.System.particleCount);
        }

        [Test]
        public void ChangingIntensityChangesTheEmissionRate()
        {
            RainController rain = Make(intensity: 0.2f);
            float low = rain.System.emission.rateOverTime.constant;
            rain.Intensity = 0.8f;
            Assert.AreEqual(low * 4f, rain.System.emission.rateOverTime.constant, low * 0.01f);
        }

        [Test]
        public void SwitchingRainOffStopsEmissionAndTheLastStreaksClear()
        {
            RainController rain = Make(intensity: 1f);
            rain.System.Simulate(3f, true, true);
            Assert.Greater(rain.System.particleCount, 0);

            rain.Raining = false;
            Assert.IsFalse(rain.System.emission.enabled);
            rain.System.Simulate(3f, true, false);   // continue past one lifetime
            Assert.AreEqual(0, rain.System.particleCount);

            rain.Raining = true;
            Assert.IsTrue(rain.System.emission.enabled);
            Assert.Greater(rain.System.emission.rateOverTime.constant, 0f);
        }

        [UnityTest]
        public IEnumerator VolumeFollowsTheCameraAndSpawnsAboveIt()
        {
            RainController rain = Make();
            yield return null;
            _camera.position = new Vector3(-30f, 3f, 12f);
            yield return null;

            Assert.AreEqual(_camera.position, rain.transform.position);
            Vector3 spawn = rain.System.transform.position;
            Assert.AreEqual(_camera.position.x, spawn.x, 1e-4f);
            Assert.AreEqual(_camera.position.z, spawn.z, 1e-4f);
            Assert.AreEqual(_camera.position.y + (float)RainModel.SpawnHeightAboveCamera(RainModel.DefaultHeight), spawn.y, 1e-4f);
        }

        [Test]
        public void ParticlesAreBornInsideTheFootprintAroundTheCamera()
        {
            RainController rain = Make(intensity: 1f);
            rain.System.Simulate(0.3f, true, true);
            var particles = new ParticleSystem.Particle[rain.System.particleCount];
            int n = rain.System.GetParticles(particles);
            Assert.Greater(n, 50);
            for (int i = 0; i < n; i++)
            {
                // Wind drifts streaks sideways while they fall, so allow for 0.3 s of it plus the half footprint.
                Assert.That(Mathf.Abs(particles[i].position.x - _camera.position.x), Is.LessThan(22f + 3f));
                Assert.That(Mathf.Abs(particles[i].position.z - _camera.position.z), Is.LessThan(22f + 3f));
            }
        }

        [Test]
        public void BadConfigurationFailsLoudly()
        {
            RainController rain = Make();
            Assert.Throws<ArgumentOutOfRangeException>(() => rain.Configure(_camera, _material, 1.5f, 0.3f, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => rain.Configure(_camera, _material, 0.5f, -0.1f, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => rain.Configure(_camera, _material, 0.5f, 0.3f, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => rain.Intensity = -0.5f);
        }

        [Test]
        public void MissingMaterialFailsLoudlyOnAwake()
        {
            LogAssert.Expect(LogType.Exception, new Regex("RainController needs a material"));
            _root = new GameObject("NoMaterial");
            _root.AddComponent<RainController>();
        }
    }
}
