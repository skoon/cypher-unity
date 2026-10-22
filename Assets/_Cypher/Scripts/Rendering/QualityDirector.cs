using System;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Cypher.Rendering
{
    /// <summary>
    /// Applies a quality preset to the running scene and, when adaptive quality is on, lowers it by itself when frames miss
    /// the budget. What a preset means is Core's <see cref="QualityTiers"/>; when to step down is Core's
    /// <see cref="QualityGovernor"/>. This only turns those decisions into settings on the pieces that exist: the low-res
    /// controller's height, MSAA on its runtime copy of the pipeline asset, bloom on the post volume's own copy of its
    /// profile, the rain cap and the reflection probe size. The project's assets are never touched.
    /// The player's choice (<see cref="Apply"/>) is the ceiling; the governor only goes down from it and never back up.
    /// <see cref="Changed"/> fires for every change so the caller can save a lowered preset and show its notice.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]   // before other components' Start, so they see the preset already applied
    public sealed class QualityDirector : MonoBehaviour
    {
        /// <summary>The governor aims this far above the 30 FPS floor so a machine holds it with room to spare.</summary>
        public const double HeadroomFps = 5;

        [SerializeField] LowResController lowRes;
        [SerializeField] RainController rain;
        [SerializeField] StreetReflectionProbe probe;
        [SerializeField, Tooltip("The global post-processing volume that holds Bloom.")] UnityEngine.Rendering.Volume postVolume;   // not Cypher.Core.Volume, the building massing type

        GraphicsOptions _options = new GraphicsOptions();
        readonly QualityGovernor _governor = new QualityGovernor(SettingsRanges.QualityDefault, QualityGovernor.TargetFps + HeadroomFps);
        Bloom _bloom;
        bool _started;
        bool _suspended;

        public event Action<QualityChange> Changed;

        /// <summary>Fires every time a tier is applied to the scene (at start, when the player chooses, when the governor steps down), for the parts that follow it.</summary>
        public event Action<QualityTier> TierApplied;

        /// <summary>The tier in force right now.</summary>
        public QualityTier Tier => QualityTiers.For(Preset);

        /// <summary>The preset in force right now: the player's choice, or a lower one the governor chose.</summary>
        public QualityPreset Preset => _governor.Quality;

        public bool Adaptive => _options.AdaptiveQuality;

        /// <summary>MSAA sample count actually set on the running pipeline asset, 0 before the scene has started.</summary>
        public int MsaaSamples => _started && lowRes.ActiveAsset != null ? lowRes.ActiveAsset.msaaSampleCount : 0;

        /// <summary>Median frame rate the governor has measured, 0 while it is still settling.</summary>
        public double Fps => _governor.Fps;

        public bool Settling => _governor.Settling;

        /// <summary>A copy of the options in force, with <see cref="GraphicsOptions.Quality"/> as currently applied.</summary>
        public GraphicsOptions Options => _options.Clone();

        /// <summary>While true no frames are judged (pause, menus); switching it either way re-arms the warm-up.</summary>
        public bool Suspended
        {
            get => _suspended;
            set
            {
                if (_suspended == value) return;
                _suspended = value;
                _governor.Suspend();
            }
        }

        public void Configure(LowResController lowResController, RainController rainController, StreetReflectionProbe reflectionProbe, UnityEngine.Rendering.Volume volume)
        {
            lowRes = lowResController;
            rain = rainController;
            probe = reflectionProbe;
            postVolume = volume;
        }

        void Start()
        {
            if (lowRes == null || rain == null || probe == null || postVolume == null)
            {
                throw new InvalidOperationException("QualityDirector needs the LowResController, RainController, StreetReflectionProbe and post Volume; call Configure.");
            }
            if (!postVolume.profile.TryGet(out _bloom)) throw new InvalidOperationException("The post volume has no Bloom override for QualityDirector to drive.");
            _started = true;
            ApplyTier(_options.Quality);
        }

        void Update() => Sample(Time.unscaledDeltaTime);

        /// <summary>Feed one frame time in seconds. Public so tests and scripted runs can drive it.</summary>
        public void Sample(double dt)
        {
            if (!_started || _suspended) return;
            QualityPreset? next = _governor.Sample(dt);
            if (!next.HasValue) return;

            QualityPreset from = _options.Quality;
            _options.Quality = next.Value;
            ApplyTier(next.Value);
            Changed?.Invoke(new QualityChange(from, next.Value, _governor.Fps, QualityChangeReason.Adaptive));
        }

        /// <summary>
        /// Adopt the player's graphics options. The preset becomes the new ceiling and the governor starts again from it,
        /// with a full warm-up, so history from the old preset cannot overrule the choice.
        /// </summary>
        public void Apply(GraphicsOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            QualityPreset from = _options.Quality;
            _options = options.Clone();
            _governor.Enabled = _options.AdaptiveQuality;
            _governor.Reset(_options.Quality);
            if (!_started) return;   // Start applies it once the scene's pieces are alive

            ApplyTier(_options.Quality);
            if (from != _options.Quality) Changed?.Invoke(new QualityChange(from, _options.Quality, 0, QualityChangeReason.Player));
        }

        void ApplyTier(QualityPreset preset)
        {
            QualityTier tier = QualityTiers.For(preset);
            UniversalRenderPipelineAsset asset = lowRes.ActiveAsset
                ?? throw new InvalidOperationException("The LowResController is not active, so there is no render pipeline asset to adjust.");

            lowRes.TargetHeight = QualityTiers.EffectiveInternalHeight(preset, _options.InternalHeight);
            asset.msaaSampleCount = tier.MsaaSamples;
            ApplyBloom(tier.Bloom);
            rain.MaxParticles = tier.RainParticleCap;
            probe.SetResolution(tier.ProbeResolution);
            TierApplied?.Invoke(tier);
        }

        void ApplyBloom(BloomQuality quality)
        {
            switch (quality)
            {
                case BloomQuality.Fast:
                    _bloom.downscale.Override(BloomDownscaleMode.Quarter);
                    _bloom.maxIterations.Override(4);
                    _bloom.highQualityFiltering.Override(false);
                    break;
                case BloomQuality.Standard:
                    _bloom.downscale.Override(BloomDownscaleMode.Half);
                    _bloom.maxIterations.Override(6);
                    _bloom.highQualityFiltering.Override(false);
                    break;
                case BloomQuality.High:
                    _bloom.downscale.Override(BloomDownscaleMode.Half);
                    _bloom.maxIterations.Override(6);
                    _bloom.highQualityFiltering.Override(true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(quality), quality, "unknown bloom quality");
            }
        }
    }
}
