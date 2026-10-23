using System;
using Cypher.City;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.Actors
{
    /// <summary>
    /// Draws the crowd: each frame the people in view are written into one structured buffer, sorted by level of detail (see <see cref="CrowdFrame"/>), and one instanced draw
    /// per level plus one for the contact shadows puts them all on screen. The pedestrian shader poses every vertex from the buffer, so the CPU's work is the culling, the sort
    /// and 80 bytes a person. Owns its meshes, buffer and property blocks; <see cref="Dispose"/> releases them.
    /// </summary>
    public sealed class CrowdRenderer : IDisposable
    {
        public static readonly int PedsId = Shader.PropertyToID("_Peds");
        public static readonly int BaseId = Shader.PropertyToID("_InstanceBase");

        /// <summary>Everything the city can contain: the draw's culling box, since the people are culled on the CPU.</summary>
        static readonly Bounds WorldBounds = new Bounds(Vector3.zero, new Vector3(2400f, 600f, 2400f));

        readonly Mesh[] _bodies = new Mesh[3];
        readonly Mesh _shadow;
        readonly Material _bodyMaterial;
        readonly Material _shadowMaterial;
        readonly MaterialPropertyBlock[] _bodyProps = { new MaterialPropertyBlock(), new MaterialPropertyBlock(), new MaterialPropertyBlock() };
        readonly MaterialPropertyBlock _shadowProps = new MaterialPropertyBlock();
        readonly CrowdFrame _frame = new CrowdFrame();
        readonly Frustum _frustum = new Frustum();
        readonly Plane[] _planes = new Plane[6];
        GraphicsBuffer _buffer;

        /// <summary>What was last prepared: how many people, and how many at each level of detail.</summary>
        public CrowdFrame Frame => _frame;

        /// <summary>The camera's frustum as of the last <see cref="UpdateFrustum"/>.</summary>
        public Frustum View => _frustum;

        /// <summary>Draw calls issued by the last <see cref="Render"/>.</summary>
        public int DrawCalls { get; private set; }

        public CrowdRenderer(Material body, Material shadow, int capacity = QualityTiers.MaxPedestrians)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (shadow == null) throw new ArgumentNullException(nameof(shadow));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "the buffer needs room for at least one person");
            _bodyMaterial = body;
            _shadowMaterial = shadow;
            _bodies[0] = MeshConverter.ToMesh("PedestrianFull", PedestrianRig.Build(RigLod.Full));
            _bodies[1] = MeshConverter.ToMesh("PedestrianSimple", PedestrianRig.Build(RigLod.Simple));
            _bodies[2] = MeshConverter.ToMesh("PedestrianSilhouette", PedestrianRig.Build(RigLod.Silhouette));
            _shadow = MeshConverter.ToMesh("PedestrianShadow", PedestrianRig.ShadowDisc());
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, PedestrianGpuData.Size) { name = "Pedestrians" };
        }

        public int Capacity => _buffer.count;

        /// <summary>Read the camera's frustum, for culling and for the simulation's "can this person be seen" test.</summary>
        public void UpdateFrustum(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            for (int i = 0; i < 6; i++) _frustum.SetPlane(i, _planes[i].normal.x, _planes[i].normal.y, _planes[i].normal.z, _planes[i].distance);
        }

        /// <summary>Prepare this frame's people and queue the draws. The camera decides who is in view and which body each person gets.</summary>
        public void Render(CrowdSim sim, Camera camera, double alpha, double time, double dt, double near, double far)
        {
            if (_buffer == null) throw new ObjectDisposedException(nameof(CrowdRenderer));
            if (sim.Pool.Count > _buffer.count) throw new InvalidOperationException($"the crowd has {sim.Pool.Count} people but the buffer holds {_buffer.count}");
            UpdateFrustum(camera);
            Vector3 eye = camera.transform.position;
            _frame.Build(sim, _frustum, eye.x, eye.z, alpha, time, dt, near, far);

            DrawCalls = 0;
            if (_frame.Total == 0) return;
            _buffer.SetData(_frame.Data, 0, 0, _frame.Total);

            for (int lod = 0; lod < 3; lod++)
            {
                int count = _frame.Count((RigLod)lod);
                if (count == 0) continue;
                MaterialPropertyBlock props = _bodyProps[lod];
                props.SetBuffer(PedsId, _buffer);
                props.SetFloat(BaseId, _frame.Start((RigLod)lod));
                Draw(_bodyMaterial, props, _bodies[lod], count);
            }

            // the near and middle runs lie first in the buffer, so the shadows are one run from its start
            int shadows = _frame.Count(RigLod.Full) + _frame.Count(RigLod.Simple);
            if (shadows > 0)
            {
                _shadowProps.SetBuffer(PedsId, _buffer);
                _shadowProps.SetFloat(BaseId, 0f);
                Draw(_shadowMaterial, _shadowProps, _shadow, shadows);
            }
        }

        void Draw(Material material, MaterialPropertyBlock props, Mesh mesh, int instances)
        {
            var parameters = new RenderParams(material)
            {
                worldBounds = WorldBounds,
                matProps = props,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = 0,
            };
            Graphics.RenderMeshPrimitives(parameters, mesh, 0, instances);
            DrawCalls++;
        }

        public void Dispose()
        {
            _buffer?.Release();
            _buffer = null;
            foreach (Mesh m in _bodies) DestroyObject(m);
            DestroyObject(_shadow);
        }

        static void DestroyObject(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
