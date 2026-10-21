using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine;

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
    }

    /// <summary>
    /// Generates the walkable slice of the city at runtime from a seed: roadbed, road paint, and one
    /// static chunk per 62 m grid cell (sidewalk, building walls, building roofs) with box colliders.
    /// All geometry decisions live in Cypher.Core; this component only turns that data into
    /// GameObjects. Cell-sized chunks are the unit of Unity's frustum culling.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBuilder : MonoBehaviour
    {
        public const string RootName = "City";

        [SerializeField] uint seed = CityPlan.DefaultSeed;
        [SerializeField, Range(0, 3)] int windowRadius = 1;
        [SerializeField] int atlasSize = WindowAtlas.DefaultSize;
        [SerializeField] bool buildOnAwake = true;
        [SerializeField, Tooltip("Generate the neon signage, housings and glow pools.")] bool neon = true;
        [SerializeField] CityMaterials materials;

        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        GameObject _root;
        CityMaterials _surfaces;   // instances with generated textures; the serialized assets stay untouched

        /// <summary>The meshes that were built, or null before Build() / after Clear().</summary>
        public CitySlice Slice { get; private set; }

        public Transform Root => _root ? _root.transform : null;

        /// <summary>Wall-clock time of the last Build() after planning: textures, materials and meshes.</summary>
        public double BuildMilliseconds { get; private set; }

        public void Configure(uint newSeed, int newWindowRadius, CityMaterials newMaterials)
        {
            seed = newSeed;
            windowRadius = newWindowRadius;
            materials = newMaterials;
        }

        void Awake()
        {
            if (buildOnAwake) Build();
        }

        void OnDestroy() => Clear();

        public void Build()
        {
            if (!materials.IsComplete)
            {
                throw new InvalidOperationException("CityBuilder needs all seven materials assigned (walls, roofs, sidewalk, road, paint, metal, neon).");
            }

            Clear();

            CityPlan plan = CityPlan.Generate(seed);
            CityWindow window = CityWindow.AroundPlaza(windowRadius);
            CitySlice slice = CityChunker.Build(plan, window, neon);

            _root = new GameObject(RootName);
            _root.transform.SetParent(transform, false);
            _root.isStatic = true;

            var timer = System.Diagnostics.Stopwatch.StartNew();
            _surfaces = SurfaceMaterials.Create(materials, atlasSize, _owned);

            AddRoadbed();
            AddMesh(_root.transform, "RoadMarkings", slice.Markings, _surfaces.Paint);
            foreach (CityChunk chunk in slice.Chunks)
            {
                AddChunk(chunk);
            }

            Slice = slice;
            BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
        }

        public void Clear()
        {
            if (_root) DestroyObject(_root);
            _root = null;
            foreach (UnityEngine.Object o in _owned)
            {
                if (o) DestroyObject(o);
            }
            _owned.Clear();
            Slice = null;
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

        void AddChunk(CityChunk chunk)
        {
            var go = new GameObject($"Chunk_{chunk.Gx}_{chunk.Gz}");
            go.transform.SetParent(_root.transform, false);
            go.isStatic = true;

            AddMesh(go.transform, "Sidewalk", chunk.Sidewalk, _surfaces.Sidewalk);
            AddMesh(go.transform, "Walls", chunk.Walls, _surfaces.Walls);
            AddMesh(go.transform, "Roofs", chunk.Roofs, _surfaces.Roofs);
            AddMesh(go.transform, "Housings", chunk.Housings, _surfaces.Metal);
            AddMesh(go.transform, "Neon", chunk.Neon, _surfaces.Neon, castShadows: false);

            foreach (Aabb b in chunk.Colliders)
            {
                var col = go.AddComponent<BoxCollider>();
                col.center = new Vector3((float)b.CentreX, (float)b.CentreY, (float)b.CentreZ);
                col.size = new Vector3((float)b.SizeX, (float)b.SizeY, (float)b.SizeZ);
            }
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
