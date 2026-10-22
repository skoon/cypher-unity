using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Cypher.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Cypher.City
{
    [Serializable]
    public struct CityMaterials
    {
        public Material Walls;
        public Material Roofs;
        public Material Sidewalk;
        public Material Road;
        public Material Paint;

        /// <summary>Dark metal for the housings behind signs (Cypher/CityLit).</summary>
        public Material Metal;

        /// <summary>Additive emissive signage (Cypher/NeonAdd).</summary>
        public Material Neon;

        public bool IsComplete => Walls && Roofs && Sidewalk && Road && Paint && Metal && Neon;

        /// <summary>CityLit with <c>_PropSurface</c> 1: every prop kit and the cables, one material.</summary>
        public Material Props;

        /// <summary>CityLit with <c>_PropSurface</c> 2 and alpha blending: the wall and ground decals.</summary>
        public Material Decals;

        /// <summary>Cypher/Steam: the animated puffs.</summary>
        public Material Steam;

        /// <summary>Cypher/NeonBoard with <c>_Holo</c> 0: the opaque LED hoardings.</summary>
        public Material Boards;

        /// <summary>Cypher/NeonBoard with <c>_Holo</c> 1, additive: the floating holograms.</summary>
        public Material Holos;

        /// <summary>Whether the LED hoardings and holograms can be drawn. They are optional: without these two materials the city has none.</summary>
        public bool HasBoards => Boards && Holos;

        /// <summary>Whether the street props can be built. They are optional: a city without these three materials is simply bare.</summary>
        public bool HasProps => Props && Decals && Steam;
    }

    /// <summary>
    /// Generates the city at runtime from a seed: roadbed, road paint, and one static chunk per 62 m grid
    /// cell (sidewalk, building walls, building roofs, signage) with box colliders. When the window is the
    /// whole grid it also closes the world: an invisible wall round the play area and the skyline ring
    /// beyond it. All geometry decisions live in Cypher.Core; this component only turns that data into
    /// GameObjects. Cell-sized chunks are the unit of Unity's frustum culling.
    /// <para>
    /// <see cref="Build"/> does everything at once. <see cref="BuildOverFrames"/> does the same in pieces,
    /// stopping for the frame after about <see cref="FrameBudgetMilliseconds"/>, and reports
    /// <see cref="Progress"/> for the loading bar.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBuilder : MonoBehaviour
    {
        public const string RootName = "City";
        public const string EdgeName = "WorldEdge";
        public const string SkylineName = "Skyline";

        public const string StepPlan = "plan";
        public const string StepSignage = "signage";
        public const string StepTextures = "textures";
        public const string StepGround = "ground";
        public const string StepChunks = "chunks";
        public const string StepProps = "props";
        public const string StepEdge = "edge";
        public const string PropsName = "Props";
        public const string DecalsName = "Decals";
        public const string SteamName = "Steam";
        public const string PropCollidersName = "PropColliders";
        public const string BoardsName = "Boards";
        public const string HologramsName = "Holos";

        [SerializeField] uint seed = CityPlan.DefaultSeed;
        [SerializeField, Range(0, 3), Tooltip("3 is the whole 7x7 city.")] int windowRadius = 3;
        [SerializeField] int atlasSize = WindowAtlas.DefaultSize;
        [SerializeField] bool buildOnAwake = true;
        [SerializeField, Tooltip("Generate the neon signage, housings and glow pools.")] bool neon = true;
        [SerializeField, Tooltip("Dress the streets with props, decals, cables and steam (needs the three prop materials).")] bool props = true;
        [SerializeField, Tooltip("Build over several frames (for the loading bar) instead of all in Awake.")] bool incremental;
        [SerializeField, Min(0f), Tooltip("When building over frames, stop for the frame after this long.")] float frameBudgetMilliseconds = 24f;
        [SerializeField] CityMaterials materials;

        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        GameObject _root;
        CityMaterials _surfaces;   // instances with generated textures; the serialized assets stay untouched
        LoadTracker _progress;
        Coroutine _running;
        readonly Stopwatch _work = new Stopwatch();
        readonly Dictionary<string, double> _stageMilliseconds = new Dictionary<string, double>();
        readonly Dictionary<(int Gx, int Gz), GameObject> _chunkObjects = new Dictionary<(int, int), GameObject>();
        double _propDistance = 125;
        bool _steamOn = true;

        /// <summary>The meshes that were built, or null before Build() / after Clear().</summary>
        public CitySlice Slice { get; private set; }

        public Transform Root => _root ? _root.transform : null;

        /// <summary>The skyline ring's meshes, or null when the window is not the whole city (or before the build finishes).</summary>
        public IReadOnlyList<SkylineSegment> Skyline { get; private set; }

        /// <summary>The block-by-block visibility of the street props, or null when the city has none (or is not built).</summary>
        public PropDressing Dressing { get; private set; }

        /// <summary>The placement and meshes the props were built from, or null without props.</summary>
        public PropBake Props { get; private set; }

        /// <summary>Prop colliders added to the physics world.</summary>
        public int PropColliderCount { get; private set; }

        /// <summary>Whether this builder dresses the streets: it is switched on and has the three materials.</summary>
        public bool BuildsProps => props && materials.HasProps;

        /// <summary>CPU time the build took in total, wherever it stopped for a frame: planning, textures, meshes and colliders.</summary>
        public double BuildMilliseconds { get; private set; }

        /// <summary>Frames the last build was spread over (1 for <see cref="Build"/>).</summary>
        public int BuildFrames { get; private set; }

        /// <summary>True while <see cref="BuildOverFrames"/> has not finished.</summary>
        public bool IsBuilding => _running != null;

        /// <summary>True once the city exists: <see cref="Slice"/> is set only then.</summary>
        public bool IsBuilt => Slice != null;

        /// <summary>0 to 1 across the build; 1 once <see cref="IsBuilt"/>.</summary>
        public double Progress => IsBuilt ? 1.0 : (_progress == null ? 0.0 : _progress.Progress);

        public string Status => IsBuilt ? "Ready" : (_progress == null ? "Waiting" : _progress.Status);

        public float FrameBudgetMilliseconds => frameBudgetMilliseconds;

        /// <summary>CPU time of each stage of the last build, by step name.</summary>
        public IReadOnlyDictionary<string, double> StageMilliseconds => _stageMilliseconds;

        /// <summary>Whether the world edge and skyline ring go with this window: only the whole city has an edge.</summary>
        public bool HasWorldEdge => windowRadius >= CityLayout.PlazaIndex;

        public void Configure(uint newSeed, int newWindowRadius, CityMaterials newMaterials)
        {
            seed = newSeed;
            windowRadius = newWindowRadius;
            materials = newMaterials;
        }

        /// <summary>Turn the street dressing on or off for the next build.</summary>
        public void ConfigureProps(bool dress) => props = dress;

        /// <summary>
        /// Follow a quality tier: how far out the props are drawn and whether the steam shows. Safe to call before the city is built; it is
        /// remembered and applied when the props exist.
        /// </summary>
        public void ApplyQuality(QualityTier tier)
        {
            if (tier == null) throw new ArgumentNullException(nameof(tier));
            _propDistance = tier.PropDistance;
            _steamOn = tier.Steam;
            if (Dressing == null) return;
            Dressing.DrawDistance = (float)_propDistance;
            Dressing.SteamVisible = _steamOn;
        }

        /// <summary>Choose between <see cref="Build"/> in Awake (false) and <see cref="BuildOverFrames"/> from Start (true).</summary>
        public void ConfigureLoading(bool buildOverFrames, float budgetMilliseconds)
        {
            if (budgetMilliseconds < 0f) throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds), budgetMilliseconds, "the budget cannot be negative");
            incremental = buildOverFrames;
            frameBudgetMilliseconds = budgetMilliseconds;
        }

        void Awake()
        {
            if (buildOnAwake && !incremental) Build();
        }

        void Start()
        {
            if (buildOnAwake && incremental && !IsBuilt && !IsBuilding) BuildOverFrames();
        }

        void OnDestroy() => Clear();

        /// <summary>Build the whole city now.</summary>
        public void Build()
        {
            Clear();
            IEnumerator steps = Steps();
            while (steps.MoveNext()) { }
            BuildFrames = 1;
        }

        /// <summary>Start building the city across frames; poll <see cref="IsBuilt"/> and <see cref="Progress"/>.</summary>
        public Coroutine BuildOverFrames()
        {
            Clear();
            _running = StartCoroutine(Run());
            return _running;
        }

        IEnumerator Run()
        {
            IEnumerator steps = Steps();
            var frame = Stopwatch.StartNew();
            int frames = 1;
            while (steps.MoveNext())
            {
                if (frame.Elapsed.TotalMilliseconds < frameBudgetMilliseconds) continue;
                yield return null;
                frames++;
                frame.Restart();
            }
            BuildFrames = frames;
            _running = null;
        }

        /// <summary>The build as a series of small pieces. Everything that takes time is timed into <see cref="StageMilliseconds"/>.</summary>
        IEnumerator Steps()
        {
            if (!materials.IsComplete)
            {
                throw new InvalidOperationException("CityBuilder needs all seven materials assigned (walls, roofs, sidewalk, road, paint, metal, neon).");
            }

            _stageMilliseconds.Clear();
            _work.Reset();
            _progress = new LoadTracker(new[]
            {
                // Weights are the stages' measured times in the Development player (ms / 100), so the bar moves at an even pace.
                new LoadStep(StepPlan, "Planning the city", 0.3),
                new LoadStep(StepSignage, "Wiring the signs", neon ? 1.7 : 0.01),
                new LoadStep(StepTextures, "Making the surfaces", 7.5),
                new LoadStep(StepGround, "Laying the roads", 0.2),
                new LoadStep(StepChunks, "Raising the blocks", 1.4),
                new LoadStep(StepProps, "Dressing the streets", BuildsProps ? 2.0 : 0.01),
                new LoadStep(StepEdge, "Closing the edge", 1.0),
            });
            var stage = Stopwatch.StartNew();
            void EndStage(string name)
            {
                _stageMilliseconds[name] = (_stageMilliseconds.TryGetValue(name, out double before) ? before : 0) + stage.Elapsed.TotalMilliseconds;
                stage.Restart();
            }

            _work.Start();
            CityPlan plan = CityPlan.Generate(seed);
            CityWindow window = CityWindow.AroundPlaza(windowRadius);
            var job = new CityChunkJob(plan, window);

            // The props and the sign atlases are pure computation that nothing changes: workers bake them while this thread builds the blocks.
            Task<PropBake> bake = BuildsProps ? Task.Run(() => PropBake.Run(plan, window)) : null;
            NeonAtlasLayout atlas = neon ? NeonAtlas.Layout() : null;
            Task<SignBake> signBake = neon ? Task.Run(() => SignBake.Run(atlas)) : null;
            _progress.Complete(StepPlan);
            EndStage(StepPlan);
            _work.Stop(); yield return null; _work.Start(); stage.Restart();

            Dictionary<(int Gx, int Gz), NeonCellMeshes> signs = null;
            if (neon)
            {
                NeonLayout layout = job.PlanNeon(atlas);
                _progress.Set(StepSignage, 0.5);
                EndStage(StepSignage);
                _work.Stop(); yield return null; _work.Start(); stage.Restart();
                signs = job.MeshNeon(layout, atlas);
            }
            _progress.Complete(StepSignage);
            EndStage(StepSignage);
            _work.Stop(); yield return null; _work.Start(); stage.Restart();

            _root = new GameObject(RootName);
            _root.transform.SetParent(transform, false);
            _root.isStatic = true;

            var surfaces = new SurfaceMaterials.Result();
            IEnumerator textures = SurfaceMaterials.Steps(materials, atlasSize, _owned, surfaces);
            int made = 0;
            while (textures.MoveNext())
            {
                _progress.Set(StepTextures, Math.Min(0.99, ++made / 6.0));
                EndStage(StepTextures);
                _work.Stop(); yield return null; _work.Start(); stage.Restart();
            }
            _surfaces = surfaces.Materials;
            if (signBake != null)
            {
                // usually long finished by now; if not, the bar waits here
                while (!signBake.IsCompleted)
                {
                    _work.Stop(); yield return null; _work.Start(); stage.Restart();
                }
                if (signBake.IsFaulted) ExceptionDispatchInfo.Capture(signBake.Exception.GetBaseException()).Throw();
                SignBake baked = signBake.Result;
                Texture2D lettering = SurfaceMaterials.MakeMask("SignAtlas", baked.SignAtlas, NeonAtlas.Size, aniso: 4);
                _owned.Add(lettering);
                EndStage(StepTextures);
                _work.Stop(); yield return null; _work.Start(); stage.Restart();
                Texture2D art = SurfaceMaterials.MakeTexture("BoardAtlas", baked.BoardAtlas, BoardArt.Size, BoardArt.Size, srgb: true, aniso: 8, TextureWrapMode.Clamp);
                _owned.Add(art);
                (_surfaces.Neon, _surfaces.Boards, _surfaces.Holos) = SurfaceMaterials.CreateSigns(materials, lettering, art, _owned);
                Debug.Log($"[Cypher] signs: sign atlas {baked.SignMilliseconds:0} ms, board art {baked.BoardMilliseconds:0} ms on the worker");
            }
            _progress.Complete(StepTextures);
            EndStage(StepTextures);

            AddRoadbed();
            MeshData markings = job.BuildMarkings();
            AddMesh(_root.transform, "RoadMarkings", markings, _surfaces.Paint);
            _progress.Complete(StepGround);
            EndStage(StepGround);
            _work.Stop(); yield return null; _work.Start(); stage.Restart();

            var chunks = new List<CityChunk>(job.Cells.Count);
            for (int i = 0; i < job.Cells.Count; i++)
            {
                CityChunk chunk = job.BuildChunk(job.Cells[i]);
                if (signs != null) job.AttachNeon(new[] { chunk }, signs);
                _chunkObjects[(chunk.Gx, chunk.Gz)] = AddChunk(chunk);
                chunks.Add(chunk);
                _progress.Set(StepChunks, (i + 1) / (double)job.Cells.Count * 0.999);
                EndStage(StepChunks);
                _work.Stop(); yield return null; _work.Start(); stage.Restart();
            }
            _progress.Complete(StepChunks);

            if (bake != null)
            {
                // usually long finished by now; if not, the bar waits here
                var waiting = Stopwatch.StartNew();
                while (!bake.IsCompleted)
                {
                    _work.Stop(); yield return null; _work.Start(); stage.Restart();
                }
                double waited = waiting.Elapsed.TotalMilliseconds;
                if (bake.IsFaulted) ExceptionDispatchInfo.Capture(bake.Exception.GetBaseException()).Throw();
                PropBake baked = bake.Result;
                IEnumerator dressing = DressTheStreets(baked);
                int blocks = 0;
                while (dressing.MoveNext())
                {
                    _progress.Set(StepProps, Math.Min(0.99, ++blocks / 50.0));
                    EndStage(StepProps);
                    _work.Stop(); yield return null; _work.Start(); stage.Restart();
                }
                Debug.Log($"[Cypher] props: {baked.Layout.Instances.Count:N0} props ({baked.PropTriangles:N0} triangles), {baked.Layout.Decals.Count:N0} decals, {PropColliderCount:N0} colliders; " +
                          $"worker: layout {baked.LayoutMilliseconds:0} ms, meshes {baked.MeshMilliseconds:0} ms, textures {baked.TextureMilliseconds:0} ms; the main thread waited {waited:0} ms for it");
            }
            _progress.Complete(StepProps);
            EndStage(StepProps);

            IReadOnlyList<SkylineSegment> skyline = null;
            if (HasWorldEdge)
            {
                AddWorldEdge();
                skyline = SkylineRing.Build();
                var ring = new GameObject(SkylineName);
                ring.transform.SetParent(_root.transform, false);
                ring.isStatic = true;
                for (int i = 0; i < skyline.Count; i++)
                {
                    var segment = new GameObject($"Segment_{i}");
                    segment.transform.SetParent(ring.transform, false);
                    segment.isStatic = true;
                    AddMesh(segment.transform, "Walls", skyline[i].Walls, _surfaces.Walls, castShadows: false);
                    AddMesh(segment.transform, "Roofs", skyline[i].Roofs, _surfaces.Roofs, castShadows: false);
                }
            }
            _progress.Complete(StepEdge);
            EndStage(StepEdge);
            _work.Stop();

            var slice = new CitySlice { Plan = plan, Window = window, Markings = markings };
            slice.Chunks.AddRange(chunks);
            Skyline = skyline;
            BuildMilliseconds = _work.Elapsed.TotalMilliseconds;
            Slice = slice;
            Debug.Log($"[Cypher] city built: {chunks.Count} chunks, {slice.TriangleCount:N0} triangles, {BuildMilliseconds:0} ms of work ({StageSummary()})");
        }

        string StageSummary()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, double> kv in _stageMilliseconds) parts.Add($"{kv.Key} {kv.Value:0}");
            return string.Join(", ", parts);
        }

        public void Clear()
        {
            if (_running != null)
            {
                StopCoroutine(_running);
                _running = null;
            }
            if (_root) DestroyObject(_root);
            _root = null;
            _chunkObjects.Clear();
            Dressing = null;
            Props = null;
            PropColliderCount = 0;
            foreach (UnityEngine.Object o in _owned)
            {
                if (o) DestroyObject(o);
            }
            _owned.Clear();
            Slice = null;
            Skyline = null;
            _progress = null;
            BuildMilliseconds = 0;
            BuildFrames = 0;
        }

        /// <summary>
        /// Turns the baked props into objects, a block at a time (it yields after each): the textures and materials, a props mesh and a decals
        /// mesh per block, a box collider for each solid prop, the steam, and the component that hides far blocks.
        /// </summary>
        IEnumerator DressTheStreets(PropBake bake)
        {
            (Material props, Material decals, Material steam) surfaces = SurfaceMaterials.CreateProps(materials, bake, _owned);
            yield return null;

            PropDressing dressing = _root.AddComponent<PropDressing>();
            foreach (KeyValuePair<(int Gx, int Gz), PropCell> kv in bake.Cells)
            {
                PropCell cell = kv.Value;
                if (!_chunkObjects.TryGetValue(kv.Key, out GameObject chunk)) continue;   // a block outside the window has no chunk to hang on

                GameObject propsGo = AddMesh(chunk.transform, PropsName, cell.Props, surfaces.props, castShadows: false);
                GameObject decalsGo = AddMesh(chunk.transform, DecalsName, cell.Decals, surfaces.decals, castShadows: false);
                MeshRenderer propsRenderer = propsGo.GetComponent<MeshRenderer>(), decalsRenderer = decalsGo.GetComponent<MeshRenderer>();
                if (decalsRenderer != null) decalsRenderer.receiveShadows = false;

                var bounds = new Bounds();
                bool any = false;
                foreach (MeshRenderer r in new[] { propsRenderer, decalsRenderer })
                {
                    if (r == null) continue;
                    if (any) bounds.Encapsulate(r.bounds);
                    else { bounds = r.bounds; any = true; }
                }
                if (any) dressing.Add(bounds, propsRenderer, decalsRenderer);

                AddPropColliders(chunk.transform, cell.Boxes);
                yield return null;
            }

            var steamGo = new GameObject(SteamName);
            steamGo.transform.SetParent(_root.transform, false);
            Mesh steamMesh = MeshConverter.ToMesh(SteamName, bake.Steam);
            if (steamMesh != null)
            {
                // the shader moves the puffs after the mesh is made: leave room for the rise and the drift
                Bounds b = steamMesh.bounds;
                b.Expand(new Vector3(8f, 12f, 8f));
                steamMesh.bounds = b;
                _owned.Add(steamMesh);
                steamGo.AddComponent<MeshFilter>().sharedMesh = steamMesh;
                var steamRenderer = steamGo.AddComponent<MeshRenderer>();
                steamRenderer.sharedMaterial = surfaces.steam;
                steamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                steamRenderer.receiveShadows = false;
                dressing.SetSteam(steamRenderer);
            }

            dressing.DrawDistance = (float)_propDistance;
            dressing.SteamVisible = _steamOn;
            Dressing = dressing;
            Props = bake;
        }

        /// <summary>One box collider per solid prop, each on its own small object because a box cannot turn inside a shared one.</summary>
        void AddPropColliders(Transform chunk, IReadOnlyList<PropBox> boxes)
        {
            if (boxes.Count == 0) return;
            var holder = new GameObject(PropCollidersName);
            holder.transform.SetParent(chunk, false);
            holder.isStatic = true;
            foreach (PropBox b in boxes)
            {
                var go = new GameObject("Prop");
                go.transform.SetParent(holder.transform, false);
                go.isStatic = true;
                go.transform.localPosition = new Vector3((float)b.Cx, (float)b.Cy, (float)b.Cz);
                go.transform.localRotation = Quaternion.Euler(0f, (float)(b.Yaw * 180.0 / Math.PI), 0f);   // +Z toward +X for a positive yaw, as in Core
                go.AddComponent<BoxCollider>().size = new Vector3((float)(b.Hx * 2), (float)(b.Hy * 2), (float)(b.Hz * 2));
                PropColliderCount++;
            }
        }

        /// <summary>The invisible wall round the play area: four tall, thick boxes.</summary>
        void AddWorldEdge()
        {
            var go = new GameObject(EdgeName);
            go.transform.SetParent(_root.transform, false);
            go.isStatic = true;
            foreach (Aabb b in WorldEdge.Walls())
            {
                var col = go.AddComponent<BoxCollider>();
                col.center = new Vector3((float)b.CentreX, (float)b.CentreY, (float)b.CentreZ);
                col.size = new Vector3((float)b.SizeX, (float)b.SizeY, (float)b.SizeZ);
            }
        }

        void AddRoadbed()
        {
            var mb = new MeshBuilder();
            StreetMesher.AddRoadbed(mb);
            GameObject go = AddMesh(_root.transform, "Roadbed", mb.Build(), _surfaces.Road);

            // Thick enough that a fast fall cannot tunnel through.
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3((float)StreetMesher.RoadbedCentre, -0.5f, (float)StreetMesher.RoadbedCentre);
            box.size = new Vector3((float)StreetMesher.RoadbedSize, 1f, (float)StreetMesher.RoadbedSize);
        }

        GameObject AddChunk(CityChunk chunk)
        {
            var go = new GameObject($"Chunk_{chunk.Gx}_{chunk.Gz}");
            go.transform.SetParent(_root.transform, false);
            go.isStatic = true;

            AddMesh(go.transform, "Sidewalk", chunk.Sidewalk, _surfaces.Sidewalk);
            AddMesh(go.transform, "Walls", chunk.Walls, _surfaces.Walls);
            AddMesh(go.transform, "Roofs", chunk.Roofs, _surfaces.Roofs);
            AddMesh(go.transform, "Housings", chunk.Housings, _surfaces.Metal);
            AddMesh(go.transform, "Neon", chunk.Neon, _surfaces.Neon, castShadows: false);
            if (_surfaces.HasBoards)
            {
                AddMesh(go.transform, BoardsName, chunk.Boards, _surfaces.Boards, castShadows: false);
                AddMesh(go.transform, HologramsName, chunk.Holos, _surfaces.Holos, castShadows: false);
            }

            foreach (Aabb b in chunk.Colliders)
            {
                var col = go.AddComponent<BoxCollider>();
                col.center = new Vector3((float)b.CentreX, (float)b.CentreY, (float)b.CentreZ);
                col.size = new Vector3((float)b.SizeX, (float)b.SizeY, (float)b.SizeZ);
            }
            return go;
        }

        GameObject AddMesh(Transform parent, string name, MeshData data, Material material, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.isStatic = true;

            Mesh mesh = MeshConverter.ToMesh(name, data);
            if (mesh == null) return go;   // nothing to draw, e.g. the plaza has no walls

            _owned.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (!castShadows)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return go;
        }

        static void DestroyObject(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
