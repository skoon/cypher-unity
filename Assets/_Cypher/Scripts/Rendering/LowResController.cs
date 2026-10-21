using System;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cypher.Rendering
{
    /// <summary>
    /// Renders the 3D scene at a fixed internal height (default 540p) and upscales it with point
    /// filtering, so the picture stays chunky at any window size while overlay UI stays sharp.
    /// URP only exposes render scale on the pipeline asset, so this installs a runtime clone of the
    /// active asset and leaves the project's own asset untouched; disabling restores the original.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LowResController : MonoBehaviour
    {
        public const int DefaultTargetHeight = 540;

        [SerializeField, Min(1)] int targetHeight = DefaultTargetHeight;

        RenderPipelineAsset _original;
        UniversalRenderPipelineAsset _clone;
        int _lastScreenHeight;

        public int TargetHeight
        {
            get => targetHeight;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), value, "target height must be positive");
                targetHeight = value;
                if (_clone != null) Apply(_lastScreenHeight);
            }
        }

        /// <summary>Render scale currently set on the installed asset; 1 before the controller is enabled.</summary>
        public float CurrentScale { get; private set; } = 1f;

        /// <summary>The runtime asset in use, or null while disabled.</summary>
        public UniversalRenderPipelineAsset ActiveAsset => _clone;

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset active))
            {
                throw new InvalidOperationException("LowResController needs the Universal Render Pipeline to be the active render pipeline.");
            }

            _original = QualitySettings.renderPipeline;   // null when the quality level inherits the graphics default
            _clone = Instantiate(active);
            _clone.name = active.name + " (low-res runtime)";
            _clone.upscalingFilter = UpscalingFilterSelection.Point;
            QualitySettings.renderPipeline = _clone;
            Apply(Screen.height);
        }

        void OnDisable()
        {
            if (_clone == null) return;
            QualitySettings.renderPipeline = _original;
            Destroy(_clone);
            _clone = null;
            _original = null;
            CurrentScale = 1f;
        }

        void Update()
        {
            if (_clone != null && Screen.height != _lastScreenHeight) Apply(Screen.height);
        }

        /// <summary>Set the render scale for a display of the given height. Public so tests can simulate window sizes.</summary>
        public void Apply(int screenHeight)
        {
            if (_clone == null) throw new InvalidOperationException("LowResController is not active.");
            CurrentScale = ResolutionScaler.RenderScale(targetHeight, screenHeight);
            _clone.renderScale = CurrentScale;
            _lastScreenHeight = screenHeight;
        }
    }
}
