using System;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.Rendering
{
    /// <summary>
    /// A single camera-following particle system of stretched rain streaks. Streaks live in world space and
    /// are born in a box above the camera, so the rain stays put as you walk through it while the box
    /// travels with you. One draw call, a capped particle count, and no collision or sub-emitters.
    /// All the arithmetic (lifetime, emission rate, stretch) lives in <see cref="RainModel"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RainController : MonoBehaviour
    {
        public const int DefaultMaxParticles = 3000;

        [SerializeField, Tooltip("What the volume follows, normally the main camera.")] Transform follow;
        [SerializeField, Tooltip("Cypher/Rain material.")] Material material;
        [SerializeField, Range(0f, 1f), Tooltip("Rain amount; scales how many streaks are alive at once.")] float intensity = (float)NightRig.DefaultRain;
        [SerializeField, Range(0f, 1f)] float wind = (float)RainModel.DefaultWind;
        [SerializeField, Min(100)] int maxParticles = DefaultMaxParticles;
        [SerializeField] Vector2 footprint = new Vector2(44f, 44f);
        [SerializeField] bool raining = true;

        ParticleSystem _system;
        ParticleSystemRenderer _renderer;
        float _lifetime;

        public ParticleSystem System => _system;
        public ParticleSystemRenderer ParticleRenderer => _renderer;

        public float Intensity
        {
            get => intensity;
            set
            {
                if (!(value >= 0f && value <= 1f)) throw new ArgumentOutOfRangeException(nameof(value), value, "intensity must be 0..1");
                intensity = value;
                if (_system != null) ApplyEmission();
            }
        }

        /// <summary>Most streaks alive at once at full intensity; the quality preset sets it.</summary>
        public int MaxParticles
        {
            get => maxParticles;
            set
            {
                if (value < 100) throw new ArgumentOutOfRangeException(nameof(value), value, "cap must be at least 100");
                maxParticles = value;
                if (_system == null) return;
                ParticleSystem.MainModule main = _system.main;
                main.maxParticles = value;
                ApplyEmission();
            }
        }

        /// <summary>Master switch. Off stops new streaks; the ones already falling finish and clear.</summary>
        public bool Raining
        {
            get => raining;
            set
            {
                raining = value;
                if (_system != null) ApplyEmission();
            }
        }

        public void Configure(Transform followTarget, Material rainMaterial, float rainIntensity, float windAmount, int particleCap)
        {
            if (!(rainIntensity >= 0f && rainIntensity <= 1f)) throw new ArgumentOutOfRangeException(nameof(rainIntensity), rainIntensity, "intensity must be 0..1");
            if (!(windAmount >= 0f && windAmount <= 1f)) throw new ArgumentOutOfRangeException(nameof(windAmount), windAmount, "wind must be 0..1");
            if (particleCap < 100) throw new ArgumentOutOfRangeException(nameof(particleCap), particleCap, "cap must be at least 100");
            follow = followTarget;
            material = rainMaterial;
            intensity = rainIntensity;
            wind = windAmount;
            maxParticles = particleCap;
        }

        void Awake()
        {
            if (material == null) throw new InvalidOperationException("RainController needs a material (Cypher/Rain).");

            var go = new GameObject("RainParticles");
            go.transform.SetParent(transform, false);
            _system = go.AddComponent<ParticleSystem>();
            _renderer = go.GetComponent<ParticleSystemRenderer>();
            _lifetime = (float)RainModel.Lifetime(RainModel.DefaultHeight, RainModel.DefaultFallSpeed);
            ConfigureSystem();
            ApplyEmission();
            Follow();   // prewarm fills the volume on Play(), so it must already be around the camera
            _system.Play();
        }

        void LateUpdate() => Follow();

        void Follow()
        {
            if (follow == null) return;
            transform.position = follow.position;
            _system.transform.position = follow.position + Vector3.up * (float)RainModel.SpawnHeightAboveCamera(RainModel.DefaultHeight);
        }

        void OnDestroy()
        {
            if (_system != null) Destroy(_system.gameObject);
        }

        void ConfigureSystem()
        {
            // Stop first: configuring a playing system is an error, and the Play() in Awake restarts it.
            _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = _system.main;
            main.loop = true;
            main.prewarm = true;   // the volume is already full on the first frame
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = _lifetime;
            main.startSpeed = 0f;   // velocity comes from the velocity module so wind can slant the streaks
            main.startSize = 0.05f;
            main.startColor = new Color(0.62f, 0.76f, 1f, 0.55f);
            main.gravityModifier = 0f;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.VelocityOverLifetimeModule velocity = _system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            float drift = (float)RainModel.WindDrift(wind);
            velocity.x = drift;
            velocity.y = -(float)RainModel.DefaultFallSpeed;
            velocity.z = drift * 0.3f;

            ParticleSystem.ShapeModule shape = _system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(footprint.x, 0.1f, footprint.y);

            ParticleSystem.CollisionModule collision = _system.collision;
            collision.enabled = false;

            _renderer.renderMode = ParticleSystemRenderMode.Stretch;
            _renderer.lengthScale = 0f;
            _renderer.velocityScale = (float)RainModel.StretchVelocityScale(RainModel.DefaultStreakLength, RainModel.DefaultFallSpeed);
            _renderer.cameraVelocityScale = 0f;
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        void ApplyEmission()
        {
            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = raining;
            emission.rateOverTime = raining ? (float)RainModel.EmissionRate(maxParticles, intensity, _lifetime) : 0f;
        }
    }
}
