using System;
using System.Collections;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.Rendering
{
    /// <summary>
    /// The city's box-projected realtime reflection probes, each rendered once, on demand, so wet streets and glass reflect the
    /// sky and the city's own neon. The city is generated at runtime, so a baked probe could not contain it. A probe per district
    /// (see <see cref="ProbeLayout"/>) keeps the reflections about the neon that is actually nearby; the renderer blends the probes
    /// where their boxes overlap. There is no per-frame cost: after the capture the probes are static cubemaps.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StreetReflectionProbe : MonoBehaviour
    {
        public const int DefaultResolution = 128;

        /// <summary>Frames to wait before the first capture, so freshly created textures have uploaded.</summary>
        public const int SettleFrames = 2;

        [Serializable]
        struct Region
        {
            public Vector3 capture;   // where the cubemap is rendered from
            public Vector3 centre;    // middle of the box it is projected onto
            public Vector3 size;
        }

        [SerializeField] Region[] regions = { new Region { capture = new Vector3(0f, 6f, 0f), centre = new Vector3(0f, 6f, 0f), size = new Vector3(200f, 80f, 200f) } };
        [SerializeField, Range(16, 512)] int resolution = DefaultResolution;
        [SerializeField, Tooltip("Capture once after SettleFrames. Turn off to call Capture() yourself.")] bool captureOnStart = true;

        readonly List<ReflectionProbe> _probes = new List<ReflectionProbe>();
        int[] _renderIds = Array.Empty<int>();

        /// <summary>The first probe; with a single region, the only one.</summary>
        public ReflectionProbe Probe => _probes.Count > 0 ? _probes[0] : null;

        public IReadOnlyList<ReflectionProbe> Probes => _probes;

        public int Count => regions.Length;

        /// <summary>True once a capture has been requested and the GPU has finished all of the probes.</summary>
        public bool IsCaptured
        {
            get
            {
                if (_probes.Count == 0 || CaptureCount == 0) return false;
                for (int i = 0; i < _probes.Count; i++)
                {
                    if (_renderIds[i] < 0 || !_probes[i].IsFinishedRendering(_renderIds[i])) return false;
                }
                return true;
            }
        }

        /// <summary>How many times all of the probes have been rendered.</summary>
        public int CaptureCount { get; private set; }

        /// <summary>One probe whose box is centred on, and rendered from, <paramref name="worldCentre"/>.</summary>
        public void Configure(Vector3 worldCentre, Vector3 boxSize, int cubemapResolution, bool captureAutomatically)
        {
            Validate(boxSize, cubemapResolution);
            Apply(new[] { new Region { capture = worldCentre, centre = worldCentre, size = boxSize } }, cubemapResolution, captureAutomatically);
        }

        /// <summary>One probe per region, each rendered from its capture point and projected onto its box.</summary>
        public void Configure(IReadOnlyList<ProbeRegion> layout, int cubemapResolution, bool captureAutomatically)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (layout.Count == 0) throw new ArgumentException("a layout needs at least one probe", nameof(layout));
            var list = new Region[layout.Count];
            for (int i = 0; i < list.Length; i++)
            {
                ProbeRegion r = layout[i];
                list[i] = new Region
                {
                    capture = new Vector3((float)r.CaptureX, (float)r.CaptureY, (float)r.CaptureZ),
                    centre = new Vector3((float)r.BoxCentreX, (float)r.BoxCentreY, (float)r.BoxCentreZ),
                    size = new Vector3((float)r.SizeX, (float)r.SizeY, (float)r.SizeZ),
                };
                Validate(list[i].size, cubemapResolution);
            }
            Apply(list, cubemapResolution, captureAutomatically);
        }

        static void Validate(Vector3 boxSize, int cubemapResolution)
        {
            if (boxSize.x <= 0f || boxSize.y <= 0f || boxSize.z <= 0f) throw new ArgumentOutOfRangeException(nameof(boxSize), boxSize, "box must have positive extents");
            if (cubemapResolution < 16 || cubemapResolution > 512) throw new ArgumentOutOfRangeException(nameof(cubemapResolution), cubemapResolution, "resolution must be 16..512");
        }

        void Apply(Region[] newRegions, int cubemapResolution, bool captureAutomatically)
        {
            bool recreate = newRegions.Length != regions.Length;
            regions = newRegions;
            resolution = cubemapResolution;
            captureOnStart = captureAutomatically;
            if (_probes.Count == 0) return;   // not awake yet: Awake builds them from these settings
            if (recreate) CreateProbes();
            else ApplySettings();
        }

        public int Resolution => resolution;

        /// <summary>
        /// Change the cube face size, for a quality preset. If the probes have already been captured they are captured
        /// again at the new size, so the street never loses its reflections; until then the new size just applies.
        /// </summary>
        public void SetResolution(int cubemapResolution)
        {
            if (cubemapResolution < 16 || cubemapResolution > 512 || (cubemapResolution & (cubemapResolution - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cubemapResolution), cubemapResolution, "resolution must be a power of two from 16 to 512");
            }
            if (cubemapResolution == resolution) return;
            resolution = cubemapResolution;
            if (_probes.Count == 0) return;
            foreach (ReflectionProbe p in _probes) p.resolution = resolution;
            if (CaptureCount > 0) Capture();
        }

        void Awake() => CreateProbes();

        IEnumerator Start()
        {
            if (!captureOnStart) yield break;
            for (int i = 0; i < SettleFrames; i++) yield return null;
            Capture();
        }

        void OnDestroy() => DestroyProbes();

        /// <summary>Render every probe now. Returns without waiting; poll <see cref="IsCaptured"/>.</summary>
        public void Capture()
        {
            if (_probes.Count == 0) throw new InvalidOperationException("the probes do not exist yet; wait for Awake");
            if (!QualitySettings.realtimeReflectionProbes)
            {
                // The URP template ships with this off, and Unity then silently never renders realtime probes.
                throw new InvalidOperationException("Realtime Reflection Probes is disabled in Project Settings > Quality, so the street probe can never render.");
            }
            for (int i = 0; i < _probes.Count; i++) _renderIds[i] = _probes[i].RenderProbe();
            CaptureCount++;
        }

        void CreateProbes()
        {
            DestroyProbes();
            _renderIds = new int[regions.Length];
            for (int i = 0; i < regions.Length; i++)
            {
                var go = new GameObject(regions.Length == 1 ? "StreetReflectionProbe" : $"StreetReflectionProbe_{i}");
                go.transform.SetParent(transform, false);
                _probes.Add(go.AddComponent<ReflectionProbe>());
                _renderIds[i] = -1;
            }
            CaptureCount = 0;
            ApplySettings();
        }

        void DestroyProbes()
        {
            foreach (ReflectionProbe p in _probes)
            {
                if (p != null) Destroy(p.gameObject);
            }
            _probes.Clear();
        }

        void ApplySettings()
        {
            for (int i = 0; i < _probes.Count; i++)
            {
                Region r = regions[i];
                ReflectionProbe p = _probes[i];
                p.transform.position = r.capture;
                p.mode = ReflectionProbeMode.Realtime;
                p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                p.resolution = resolution;
                p.hdr = true;
                p.boxProjection = true;
                p.size = r.size;
                p.center = r.centre - r.capture;   // the box is centred where the district is, not where the camera stood
                p.clearFlags = ReflectionProbeClearFlags.Skybox;
                p.nearClipPlane = 0.3f;
                p.farClipPlane = 900f;
                p.cullingMask = ~0;
                p.importance = 1;
                p.intensity = 1f;
                p.blendDistance = ProbeBlendDistance;
            }
        }

        /// <summary>How far inside its box a probe starts to fade out, so neighbours blend; matches the overlap in <see cref="ProbeLayout"/>.</summary>
        const float ProbeBlendDistance = (float)ProbeLayout.Overlap;
    }
}
