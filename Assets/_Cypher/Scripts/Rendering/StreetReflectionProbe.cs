using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.Rendering
{
    /// <summary>
    /// One box-projected realtime reflection probe that is rendered once, on demand, so wet streets and
    /// glass reflect the sky and the city's own neon. The city is generated at runtime, so a baked probe
    /// could not contain it. There is no per-frame cost: after the capture the probe is a static cubemap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StreetReflectionProbe : MonoBehaviour
    {
        public const int DefaultResolution = 128;

        /// <summary>Frames to wait before the first capture, so freshly created textures have uploaded.</summary>
        public const int SettleFrames = 2;

        [SerializeField] Vector3 centre = new Vector3(0f, 6f, 0f);
        [SerializeField] Vector3 size = new Vector3(200f, 80f, 200f);
        [SerializeField, Range(16, 512)] int resolution = DefaultResolution;
        [SerializeField, Tooltip("Capture once after SettleFrames. Turn off to call Capture() yourself.")] bool captureOnStart = true;

        ReflectionProbe _probe;
        int _renderId = -1;

        public ReflectionProbe Probe => _probe;

        /// <summary>True once a capture has been requested and the GPU has finished it.</summary>
        public bool IsCaptured => _probe != null && _renderId >= 0 && _probe.IsFinishedRendering(_renderId);

        public int CaptureCount { get; private set; }

        public void Configure(Vector3 worldCentre, Vector3 boxSize, int cubemapResolution, bool captureAutomatically)
        {
            if (boxSize.x <= 0f || boxSize.y <= 0f || boxSize.z <= 0f) throw new ArgumentOutOfRangeException(nameof(boxSize), boxSize, "box must have positive extents");
            if (cubemapResolution < 16 || cubemapResolution > 512) throw new ArgumentOutOfRangeException(nameof(cubemapResolution), cubemapResolution, "resolution must be 16..512");
            centre = worldCentre;
            size = boxSize;
            resolution = cubemapResolution;
            captureOnStart = captureAutomatically;
            if (_probe != null) ApplySettings();
        }

        void Awake()
        {
            var go = new GameObject("StreetReflectionProbe");
            go.transform.SetParent(transform, false);
            _probe = go.AddComponent<ReflectionProbe>();
            ApplySettings();
        }

        IEnumerator Start()
        {
            if (!captureOnStart) yield break;
            for (int i = 0; i < SettleFrames; i++) yield return null;
            Capture();
        }

        void OnDestroy()
        {
            if (_probe != null) Destroy(_probe.gameObject);
        }

        /// <summary>Render the probe now. Returns without waiting; poll <see cref="IsCaptured"/>.</summary>
        public void Capture()
        {
            if (_probe == null) throw new InvalidOperationException("the probe does not exist yet; wait for Awake");
            if (!QualitySettings.realtimeReflectionProbes)
            {
                // The URP template ships with this off, and Unity then silently never renders realtime probes.
                throw new InvalidOperationException("Realtime Reflection Probes is disabled in Project Settings > Quality, so the street probe can never render.");
            }
            _renderId = _probe.RenderProbe();
            CaptureCount++;
        }

        void ApplySettings()
        {
            _probe.transform.position = centre;
            _probe.mode = ReflectionProbeMode.Realtime;
            _probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            _probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            _probe.resolution = resolution;
            _probe.hdr = true;
            _probe.boxProjection = true;
            _probe.size = size;
            _probe.center = Vector3.zero;
            _probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            _probe.nearClipPlane = 0.3f;
            _probe.farClipPlane = 900f;
            _probe.cullingMask = ~0;
            _probe.importance = 1;
            _probe.intensity = 1f;
            _probe.blendDistance = 1f;
        }
    }
}
