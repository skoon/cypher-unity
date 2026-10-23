using System;
using Cypher.City;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.Actors
{
    /// <summary>The materials the traffic is drawn with, made by the scene builder and saved in the scene.</summary>
    [Serializable]
    public sealed class TrafficMaterials
    {
        /// <summary>Painted bodies: vehicles and aircraft (<c>Cypher/Vehicle</c>, painted by each vehicle).</summary>
        public Material Bodies;

        /// <summary>Rubber for the wheels (<c>Cypher/Vehicle</c>, a fixed colour).</summary>
        public Material Wheels;

        /// <summary>The signal masts and housings (<c>Cypher/Vehicle</c>, a fixed colour, drawn where they are).</summary>
        public Material Masts;

        /// <summary>Lamps, under-glow, headlight cones and aircraft thrust (<c>Cypher/VehicleGlow</c>).</summary>
        public Material Glow;

        /// <summary>The soft shadow under each vehicle (<c>Cypher/VehicleShadow</c>).</summary>
        public Material Shadow;

        /// <summary>The signal lenses (<c>Cypher/Signal</c>).</summary>
        public Material Lenses;

        public void Validate()
        {
            if (Bodies == null || Wheels == null || Masts == null || Glow == null || Shadow == null || Lenses == null)
            {
                throw new InvalidOperationException("the traffic needs all six materials: bodies, wheels, masts, glow, shadow and lenses");
            }
        }
    }

    /// <summary>
    /// Draws the traffic: each frame the vehicles and aircraft in view are written into two structured buffers, sorted by type (see <see cref="TrafficFrame"/>), and one instanced draw per
    /// type, plus a draw each for the wheels, the glow, the shadows, the aircraft's glow and the signals' lenses, and one for the static signal masts, puts them all on screen:
    /// at most twelve draw calls however many vehicles there are. The vehicle shaders place every part from the buffer, so the CPU's work is the culling, the sort and 48 bytes a vehicle.
    /// Owns its meshes, buffers and property blocks; <see cref="Dispose"/> releases them.
    /// </summary>
    public sealed class TrafficRenderer : IDisposable
    {
        public static readonly int VehiclesId = Shader.PropertyToID("_Vehicles");
        public static readonly int BaseId = Shader.PropertyToID("_InstanceBase");
        public static readonly int KindId = Shader.PropertyToID("_Kind");
        public static readonly int AircraftId = Shader.PropertyToID("_Aircraft");
        public static readonly int HeadsId = Shader.PropertyToID("_Heads");
        public static readonly int SignalStateId = Shader.PropertyToID("_CypherSignalState");
        public static readonly int WalkYId = Shader.PropertyToID("_WalkY");

        // the Kind values of VehiclePose.hlsl
        const float KindBody = 0, KindWheel = 1, KindAircraft = 2, KindWorld = 3;

        /// <summary>Everything the city can contain: the draws' culling box, since the vehicles are culled on the CPU.</summary>
        static readonly Bounds WorldBounds = new Bounds(Vector3.zero, new Vector3(2400f, 600f, 2400f));

        readonly TrafficMaterials _materials;
        readonly Mesh[] _bodies = new Mesh[4];
        readonly Mesh[] _airBodies = new Mesh[2];
        readonly Mesh _wheel, _glow, _airGlow, _shadow, _lens, _masts;
        readonly MaterialPropertyBlock[] _props = new MaterialPropertyBlock[16];
        readonly TrafficFrame _frame = new TrafficFrame();
        readonly Frustum _frustum = new Frustum();
        readonly Plane[] _planes = new Plane[6];
        readonly int _headCount;
        readonly float _walkY;
        GraphicsBuffer _vehicles, _aircraft, _heads;
        int _nextProps;

        /// <summary>What was last prepared.</summary>
        public TrafficFrame Frame => _frame;

        /// <summary>The camera's frustum as of the last <see cref="UpdateFrustum"/>.</summary>
        public Frustum View => _frustum;

        /// <summary>Draw calls issued by the last <see cref="Render"/>.</summary>
        public int DrawCalls { get; private set; }

        public int SignalHeads => _headCount;

        public TrafficRenderer(TrafficMaterials materials, CityNav nav)
        {
            if (materials == null) throw new ArgumentNullException(nameof(materials));
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            materials.Validate();
            _materials = materials;

            for (int i = 0; i < _bodies.Length; i++) _bodies[i] = MeshConverter.ToMesh("Vehicle_" + VehicleCatalog.Ground[i].Key, VehicleMeshes.Body(VehicleCatalog.Ground[i]));
            for (int i = 0; i < _airBodies.Length; i++) _airBodies[i] = MeshConverter.ToMesh("Aircraft_" + VehicleCatalog.Aircraft[i].Key, VehicleMeshes.Body(VehicleCatalog.Aircraft[i]));
            _wheel = MeshConverter.ToMesh("VehicleWheel", VehicleMeshes.Wheel());
            _glow = MeshConverter.ToMesh("VehicleGlow", VehicleMeshes.GroundGlow());
            _airGlow = MeshConverter.ToMesh("AircraftGlow", VehicleMeshes.AircraftGlow());
            _shadow = MeshConverter.ToMesh("VehicleShadow", VehicleMeshes.Shadow());
            _lens = MeshConverter.ToMesh("SignalLens", VehicleMeshes.SignalLens());
            _masts = MeshConverter.ToMesh("SignalMasts", VehicleMeshes.SignalMasts(nav));
            for (int i = 0; i < _props.Length; i++) _props[i] = new MaterialPropertyBlock();

            _vehicles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, QualityTiers.MaxGroundVehicles, VehicleGpuData.Size) { name = "Vehicles" };
            _aircraft = new GraphicsBuffer(GraphicsBuffer.Target.Structured, QualityTiers.MaxAircraft, VehicleGpuData.Size) { name = "Aircraft" };

            var heads = VehicleMeshes.SignalHeads(nav);
            _headCount = heads.Count;
            var data = new Vector4[heads.Count];
            for (int i = 0; i < data.Length; i++) data[i] = new Vector4((float)heads[i].X, (float)heads[i].Z, (float)heads[i].Yaw, heads[i].Axis);
            _heads = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, 16) { name = "SignalHeads" };
            _heads.SetData(data);
            _walkY = (float)nav.WalkY;
        }

        /// <summary>Read the camera's frustum, for culling and for the simulation's "can this be seen" test.</summary>
        public void UpdateFrustum(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            for (int i = 0; i < 6; i++) _frustum.SetPlane(i, _planes[i].normal.x, _planes[i].normal.y, _planes[i].normal.z, _planes[i].distance);
        }

        /// <summary>Prepare this frame's vehicles and queue the draws.</summary>
        public void Render(TrafficSim sim, Camera camera, double alpha)
        {
            if (_vehicles == null) throw new ObjectDisposedException(nameof(TrafficRenderer));
            UpdateFrustum(camera);
            Vector3 eye = camera.transform.position;
            _frame.Build(sim, _frustum, eye.x, eye.z, alpha);
            Shader.SetGlobalVector(SignalStateId, new Vector4(sim.Signal.GreenAxis, sim.Signal.Amber ? 1f : 0f, 0f, 0f));

            DrawCalls = 0;
            _nextProps = 0;

            if (_frame.GroundTotal > 0)
            {
                _vehicles.SetData(_frame.Ground, 0, 0, _frame.GroundTotal);
                for (int type = 0; type < 4; type++)
                {
                    int count = _frame.GroundCount(type);
                    if (count > 0) Draw(_materials.Bodies, _bodies[type], count, _vehicles, _frame.GroundStart(type), KindBody);
                }
                Draw(_materials.Wheels, _wheel, _frame.GroundTotal * 4, _vehicles, 0, KindWheel);
                Draw(_materials.Glow, _glow, _frame.GroundTotal, _vehicles, 0, 0, aircraft: 0f);
                Draw(_materials.Shadow, _shadow, _frame.GroundTotal, _vehicles, 0, 0);
            }

            if (_frame.AircraftTotal > 0)
            {
                _aircraft.SetData(_frame.Aircraft, 0, 0, _frame.AircraftTotal);
                for (int type = 0; type < 2; type++)
                {
                    int count = _frame.AircraftCount(type);
                    if (count > 0) Draw(_materials.Bodies, _airBodies[type], count, _aircraft, _frame.AircraftStart(type), KindAircraft);
                }
                Draw(_materials.Glow, _airGlow, _frame.AircraftTotal, _aircraft, 0, 0, aircraft: 1f);
            }

            // the signals: lenses, three to a head, whose lit lamp the shader works out from the cycle's state; and the masts, a static mesh
            MaterialPropertyBlock lensProps = NextProps();
            lensProps.SetBuffer(HeadsId, _heads);
            lensProps.SetFloat(WalkYId, _walkY);
            Submit(_materials.Lenses, lensProps, _lens, _headCount * 3);

            MaterialPropertyBlock mastProps = NextProps();
            mastProps.SetFloat(KindId, KindWorld);
            mastProps.SetBuffer(VehiclesId, _vehicles);
            var parameters = new RenderParams(_materials.Masts) { worldBounds = WorldBounds, matProps = mastProps, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = 0 };
            Graphics.RenderMesh(parameters, _masts, 0, Matrix4x4.identity);
            DrawCalls++;
        }

        MaterialPropertyBlock NextProps()
        {
            if (_nextProps >= _props.Length) throw new InvalidOperationException("more draws than property blocks");
            return _props[_nextProps++];
        }

        void Draw(Material material, Mesh mesh, int instances, GraphicsBuffer buffer, int start, float kind, float aircraft = float.NaN)
        {
            MaterialPropertyBlock props = NextProps();
            props.SetBuffer(VehiclesId, buffer);
            props.SetFloat(BaseId, start);
            props.SetFloat(KindId, kind);
            if (!float.IsNaN(aircraft)) props.SetFloat(AircraftId, aircraft);
            Submit(material, props, mesh, instances);
        }

        void Submit(Material material, MaterialPropertyBlock props, Mesh mesh, int instances)
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
            _vehicles?.Release();
            _aircraft?.Release();
            _heads?.Release();
            _vehicles = _aircraft = _heads = null;
            foreach (Mesh m in _bodies) DestroyObject(m);
            foreach (Mesh m in _airBodies) DestroyObject(m);
            DestroyObject(_wheel); DestroyObject(_glow); DestroyObject(_airGlow); DestroyObject(_shadow); DestroyObject(_lens); DestroyObject(_masts);
        }

        static void DestroyObject(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
