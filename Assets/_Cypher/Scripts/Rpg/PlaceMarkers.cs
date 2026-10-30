using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using UnityEngine;

namespace Cypher.Rpg
{
    /// <summary>
    /// A neon marker at each named place while a game is on: a beam of light, the place's name in neon lettering and a glow on the ground, drawn with the city's
    /// sign shader and a names atlas lettered at the start (see <see cref="PlaceMarkerArt"/>). Each turns to face the camera; it is amber where the tracked job's
    /// next step is, magenta where another job's is, cyan otherwise; and it fades out as the player walks into it.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]   // after the RPG has ticked this frame
    public sealed class PlaceMarkers : MonoBehaviour
    {
        /// <summary>Within this distance a marker fades out, gone at <see cref="NearHidden"/> metres.</summary>
        public const float NearFade = 9, NearHidden = 3;

        static readonly Color PlaceTint = new Color(0.18f, 0.95f, 1f), JobTint = new Color(1f, 0.18f, 0.58f), TrackedTint = new Color(1f, 0.69f, 0.23f);
        static readonly int BrightnessId = Shader.PropertyToID("_Brightness"), AtlasId = Shader.PropertyToID("_SignAtlas");

        [SerializeField] RpgService rpg;
        [SerializeField] Camera gameCamera;
        [SerializeField] Material neon;

        sealed class Marker
        {
            public string Key;
            public Transform Transform;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public PlaceMarkerMesh Data;
            public PlaceMarkerArt.Tint? Tint;
        }

        readonly List<Marker> _markers = new List<Marker>();
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        MaterialPropertyBlock _block;
        Material _material;
        IReadOnlyList<JsonObject> _tintedFrom;
        Texture2D _atlas;

        public IReadOnlyList<string> Keys => _markers.Select(m => m.Key).ToList();

        public void Configure(RpgService rpgService, Camera camera, Material neonMaterial)
        {
            rpg = rpgService;
            gameCamera = camera;
            neon = neonMaterial;
        }

        void Start()
        {
            if (rpg == null || gameCamera == null || neon == null) throw new InvalidOperationException("PlaceMarkers needs the RPG, the camera and the city's neon material; call Configure.");
            _block = new MaterialPropertyBlock();
            rpg.CoreChanged += OnCoreChanged;
            if (rpg.Core != null) OnCoreChanged(rpg.Core);
        }

        void OnDestroy()
        {
            if (rpg != null) rpg.CoreChanged -= OnCoreChanged;
            Clear();
            foreach (UnityEngine.Object o in _owned) Destroy(o);
        }

        void OnCoreChanged(RpgCore core)
        {
            Clear();
            if (core == null) return;
            IReadOnlyList<RpgLocation> places = core.GetLocationList();
            (byte[] pixels, List<PlaceNameCell> cells) = PlaceMarkerArt.Render(places.Select(p => (p.Key, p.Name)).ToList());
            if (_material == null)
            {
                _atlas = new Texture2D(PlaceMarkerArt.AtlasWidth, PlaceMarkerArt.AtlasHeight, TextureFormat.R8, mipChain: true, linear: true)
                {
                    name = "Place names", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4,
                };
                _material = new Material(neon) { name = "Place markers" };
                _owned.Add(_atlas);
                _owned.Add(_material);
            }
            // the places, and so their names, are the same every game, but a save could come from another run: letter them each time
            _atlas.SetPixelData(pixels, 0);
            _atlas.Apply(updateMipmaps: true);
            _material.SetTexture(AtlasId, _atlas);

            for (int i = 0; i < places.Count; i++)
            {
                RpgLocation p = places[i];
                PlaceMarkerMesh data = PlaceMarkerArt.Build(cells[i], (i + 1) * 0.137);
                var go = new GameObject("Place marker " + p.Key);
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
                var mesh = new Mesh { name = "Place marker " + p.Key };
                mesh.SetVertices(data.Positions.Select(v => new Vector3((float)v.X, (float)v.Y, (float)v.Z)).ToList());
                mesh.SetUVs(0, data.Uvs.Select(v => new Vector2((float)v.U, (float)v.V)).ToList());
                mesh.SetUVs(1, data.Signs.Select(s => new Vector4((float)s.Seed, (float)s.ModeShape, (float)s.Extra, (float)s.Gain)).ToList());
                mesh.SetTriangles(data.Indices, 0);
                mesh.bounds = new Bounds(new Vector3(0, (float)PlaceMarkerArt.BeamHeight / 2, 0), new Vector3(14, (float)PlaceMarkerArt.BeamHeight + 1, 14));
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _markers.Add(new Marker { Key = p.Key, Transform = go.transform, Mesh = mesh, Renderer = r, Data = data });
            }
            LateUpdate();
        }

        void Clear()
        {
            foreach (Marker m in _markers)
            {
                Destroy(m.Mesh);
                Destroy(m.Transform.gameObject);
            }
            _markers.Clear();
        }

        void LateUpdate()
        {
            RpgCore core = rpg.Core;
            if (core == null || _markers.Count == 0) return;
            // the core keeps its marker list until the jobs change: the tints are worked out again only then
            IReadOnlyList<JsonObject> jobs = core.JobMarkers;
            bool retint = !ReferenceEquals(jobs, _tintedFrom);
            _tintedFrom = jobs;
            Vector3 eye = gameCamera.transform.position;
            float brightness = _material.GetFloat(BrightnessId);
            foreach (Marker m in _markers)
            {
                PlaceMarkerArt.Tint tint = retint ? PlaceMarkerArt.TintFor(m.Key, jobs) : m.Tint ?? PlaceMarkerArt.TintFor(m.Key, jobs);
                if (tint != m.Tint)
                {
                    m.Tint = tint;
                    Color c = (tint == PlaceMarkerArt.Tint.Tracked ? TrackedTint : tint == PlaceMarkerArt.Tint.Job ? JobTint : PlaceTint).linear;
                    m.Mesh.SetColors(m.Data.Weights.Select(w => c * (float)w).ToList());
                }

                Vector3 to = eye - m.Transform.position;
                to.y = 0;
                float d = to.magnitude;
                if (d > 1e-3f) m.Transform.rotation = Quaternion.LookRotation(to / d, Vector3.up);   // local +Z towards the viewer
                float fade = Mathf.Clamp01((d - NearHidden) / (NearFade - NearHidden));
                m.Renderer.enabled = fade > 0;
                _block.SetFloat(BrightnessId, brightness * fade);
                m.Renderer.SetPropertyBlock(_block);
            }
        }

        /// <summary>A marker's state, for tests: where it stands, how it is lit and whether it is drawn.</summary>
        public (Vector3 Position, PlaceMarkerArt.Tint Tint, bool Drawn, Vector3 Facing) Describe(string key)
        {
            Marker m = _markers.FirstOrDefault(x => x.Key == key) ?? throw new ArgumentException($"no marker for '{key}'", nameof(key));
            return (m.Transform.position, m.Tint ?? PlaceMarkerArt.Tint.Place, m.Renderer.enabled, m.Transform.forward);
        }
    }
}
