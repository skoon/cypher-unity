using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.Combat
{
    /// <summary>The materials the guns draw with, made by the scene builder (so their shaders are in the build).</summary>
    [Serializable]
    public sealed class GunMaterials
    {
        /// <summary>Cypher/Viewmodel.</summary>
        public Material Viewmodel;
        /// <summary>Cypher/GunFxAdditive: the muzzle flash (instanced per use: tint, mode and depth squash set in code).</summary>
        public Material Additive;
        /// <summary>Cypher/GunFxAlpha: smoke and decals (instanced per use).</summary>
        public Material Alpha;
        /// <summary>Cypher/CityLit with vertex colour: the debris chunks and the brass.</summary>
        public Material Lit;

        public bool IsComplete => Viewmodel && Additive && Alpha && Lit;
    }

    /// <summary>
    /// The guns in the player's hands: one object tree per gun (a root, then a node per moving part at its pivot, with the part's mesh), under a holder that
    /// squeezes X and Y for the narrower viewmodel field of view, under the camera; and the muzzle flash (core, petals and glow) at the muzzle. The models are
    /// built in the prototype's weapon space (-Z down the bore); Unity's camera looks down +Z, so positions and meshes are mirrored in Z on the way in, and the
    /// rotations about X and Y change sign.
    /// </summary>
    public sealed class ViewmodelRig : IDisposable
    {
        sealed class Gun
        {
            public GunModel Model;
            public Transform Root;
            public readonly Dictionary<string, Transform> Nodes = new Dictionary<string, Transform>();
            public readonly Dictionary<string, Vector3> Rest = new Dictionary<string, Vector3>();
            public readonly List<MeshRenderer> Renderers = new List<MeshRenderer>();
            public Transform Muzzle, Eject;
        }

        static readonly int GlowChanId = Shader.PropertyToID("_GlowChan"), TintId = Shader.PropertyToID("_Tint"), ModeId = Shader.PropertyToID("_Mode"),
            SquashId = Shader.PropertyToID("_DepthSquash"), TexId = Shader.PropertyToID("_MainTex");

        readonly Transform _holder;
        readonly Dictionary<string, Gun> _guns = new Dictionary<string, Gun>();
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly Transform _flash;
        readonly MeshRenderer _flashCore, _flashPetals, _flashGlow;
        readonly Material _solid, _glow;
        Gun _shown;

        /// <summary>The holder under the camera (scaled for the viewmodel field of view).</summary>
        public Transform Holder => _holder;

        public bool Visible { get; private set; }

        public ViewmodelRig(Transform camera, GunMaterials materials)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (materials == null || !materials.IsComplete) throw new ArgumentException("the guns need all their materials", nameof(materials));
            _holder = new GameObject("Viewmodel").transform;
            _holder.SetParent(camera, false);
            foreach (WeaponConfig cfg in WeaponTable.All) _guns[cfg.Id] = Build(WeaponModels.Build(cfg.Id), materials.Viewmodel);

            _solid = Own(new Material(materials.Additive) { name = "Muzzle flash" });
            _solid.SetFloat(ModeId, 3);
            _solid.SetFloat(SquashId, 0.05f);
            _glow = Own(new Material(materials.Additive) { name = "Muzzle glow" });
            _glow.SetFloat(ModeId, 2);
            _glow.SetFloat(SquashId, 0.05f);
            _glow.SetTexture(TexId, Own(Texture(GunFxArt.FlashGlow(), "Muzzle glow")));
            _flash = new GameObject("Muzzle flash").transform;
            _flash.SetParent(_holder, false);
            _flashCore = Part(_flash, "core", Octahedron(0.05f), _solid);
            _flashPetals = Part(_flash, "petals", Petals(), _solid);
            _flashGlow = Part(_flash, "glow", Quad(0.42f, -0.02f), _glow);
            _flash.gameObject.SetActive(false);
            SetVisible(false);
        }

        T Own<T>(T o) where T : UnityEngine.Object
        {
            _owned.Add(o);
            return o;
        }

        /// <summary>A texture from the art's straight RGBA bytes (row 0 at the top).</summary>
        public static Texture2D Texture(byte[] rgba, string name)
        {
            int size = GunFxArt.Size;
            var flipped = new byte[rgba.Length];
            for (int y = 0; y < size; y++) Buffer.BlockCopy(rgba, y * size * 4, flipped, (size - 1 - y) * size * 4, size * 4);
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
            t.SetPixelData(flipped, 0);
            t.Apply(true, true);
            return t;
        }

        Gun Build(GunModel model, Material material)
        {
            var gun = new Gun { Model = model, Root = new GameObject("Gun " + model.Id).transform };
            gun.Root.SetParent(_holder, false);
            foreach (GunPart part in model.Parts)
            {
                Transform parent = part.Parent != null ? gun.Nodes[part.Parent] : gun.Root;
                (double X, double Y, double Z) parentPivot = part.Parent != null ? model.Part(part.Parent).Pivot : (0, 0, 0);
                var node = new GameObject(part.Name).transform;
                node.SetParent(parent, false);
                Vector3 rest = Mirror(part.Pivot.X - parentPivot.X, part.Pivot.Y - parentPivot.Y, part.Pivot.Z - parentPivot.Z);
                node.localPosition = rest;
                gun.Nodes[part.Name] = node;
                gun.Rest[part.Name] = rest;
                Mesh mesh = Own(PartMesh(part, model.Id));
                gun.Renderers.Add(Part(node, part.Name + " mesh", mesh, material));
            }
            gun.Muzzle = Anchor(gun, "muzzle");
            gun.Eject = Anchor(gun, "eject");
            return gun;
        }

        Transform Anchor(Gun gun, string name)
        {
            (double x, double y, double z) = gun.Model.Anchors[name];
            var a = new GameObject(name).transform;
            a.SetParent(gun.Root, false);
            a.localPosition = Mirror(x, y, z);
            return a;
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            r.allowOcclusionWhenDynamic = false;
            return r;
        }

        static Vector3 Mirror(double x, double y, double z) => new Vector3((float)x, (float)y, (float)-z);

        /// <summary>A part's mesh in Unity's space: Z mirrored (and so the winding flipped), the surface values in UV1.</summary>
        static Mesh PartMesh(GunPart part, string gun)
        {
            int n = part.VertexCount;
            var pos = new Vector3[n];
            var nrm = new Vector3[n];
            var col = new Color[n];
            var mre = new List<Vector4>(n);
            for (int i = 0; i < n; i++)
            {
                pos[i] = new Vector3(part.Positions[i * 3], part.Positions[i * 3 + 1], -part.Positions[i * 3 + 2]);
                nrm[i] = new Vector3(part.Normals[i * 3], part.Normals[i * 3 + 1], -part.Normals[i * 3 + 2]);
                col[i] = new Color(part.Colors[i * 3], part.Colors[i * 3 + 1], part.Colors[i * 3 + 2], 1);
                mre.Add(new Vector4(part.Mre[i * 4], part.Mre[i * 4 + 1], part.Mre[i * 4 + 2], part.Mre[i * 4 + 3]));
            }
            var tris = new int[n];
            for (int i = 0; i < n; i += 3)
            {
                tris[i] = i;
                tris[i + 1] = i + 2;
                tris[i + 2] = i + 1;
            }
            var mesh = new Mesh { name = $"{gun} {part.Name}", indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = pos;
            mesh.normals = nrm;
            mesh.colors = col;
            mesh.SetUVs(1, mre);
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        // the flash's shapes, in Unity's space (+Z out of the muzzle): an octahedron, an open cone of six petals, a glow quad
        Mesh Octahedron(float r)
        {
            var v = new[] { new Vector3(r, 0, 0), new Vector3(-r, 0, 0), new Vector3(0, r, 0), new Vector3(0, -r, 0), new Vector3(0, 0, r), new Vector3(0, 0, -r) };
            var t = new[] { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            return Own(Simple("flash core", v, t, null));
        }

        Mesh Petals()
        {
            const int seg = 6;
            const float radius = 0.07f, len = 0.16f;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            // three's ConeGeometry turned down the bore (rotateX(-90 degrees)) and moved 5 cm out: the tip 13 cm in front of the muzzle, the open base 3 cm behind
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                int b = v.Count;
                v.Add(new Vector3(0, 0, len / 2 + 0.05f));
                v.Add(new Vector3(Mathf.Sin(a0) * radius, Mathf.Cos(a0) * radius, -len / 2 + 0.05f));
                v.Add(new Vector3(Mathf.Sin(a1) * radius, Mathf.Cos(a1) * radius, -len / 2 + 0.05f));
                uv.Add(new Vector2(0.5f, 0.5f)); uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
                t.AddRange(new[] { b, b + 1, b + 2 });
            }
            return Own(Simple("flash petals", v.ToArray(), t.ToArray(), uv.ToArray()));
        }

        Mesh Quad(float size, float z)
        {
            float h = size / 2;
            var v = new[] { new Vector3(-h, -h, -z), new Vector3(h, -h, -z), new Vector3(h, h, -z), new Vector3(-h, h, -z) };
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            return Own(Simple("flash glow", v, new[] { 0, 2, 1, 0, 3, 2 }, uv));
        }

        static Mesh Simple(string name, Vector3[] v, int[] t, Vector2[] uv)
        {
            var m = new Mesh { name = name, vertices = v, triangles = t };
            var c = new Color[v.Length];
            for (int i = 0; i < c.Length; i++) c[i] = Color.white;
            m.colors = c;
            if (uv != null) m.uv = uv;
            m.RecalculateBounds();
            return m;
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            _holder.gameObject.SetActive(visible);
        }

        /// <summary>Show the gun in hand, posed, with its glow channels; and the flash.</summary>
        public void Apply(WeaponRig rig, ViewmodelPose pose, Camera camera)
        {
            Gun gun = _guns[rig.Current.Config.Id];
            if (gun != _shown)
            {
                foreach (Gun g in _guns.Values) g.Root.gameObject.SetActive(g == gun);
                _flash.SetParent(gun.Muzzle, false);
                _shown = gun;
            }

            // the viewmodel field of view: squeeze X and Y by the ratio of the two projections, as the prototype does
            float r = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan((float)pose.VmFov * 0.5f * Mathf.Deg2Rad);
            _holder.localScale = new Vector3(r, r, 1);

            gun.Root.localPosition = Mirror(pose.X, pose.Y, pose.Z);
            gun.Root.localRotation = EulerYXZ(-pose.RX, -pose.RY, pose.RZ);
            foreach (KeyValuePair<string, Transform> kv in gun.Nodes)
            {
                Transform node = kv.Value;
                if (pose.Parts.TryGetValue(kv.Key, out PartPose p))
                {
                    node.localPosition = gun.Rest[kv.Key] + new Vector3((float)p.DX, (float)p.DY, (float)-p.DZ);
                    node.localRotation = EulerXYZ(-p.RX, -p.RY, p.RZ);
                    if (node.gameObject.activeSelf != p.Visible) node.gameObject.SetActive(p.Visible);
                }
                else
                {
                    node.localPosition = gun.Rest[kv.Key];
                    node.localRotation = Quaternion.identity;
                    if (!node.gameObject.activeSelf) node.gameObject.SetActive(true);
                }
            }

            _block.Clear();
            _block.SetVector(GlowChanId, new Vector4((float)pose.Glow0, (float)pose.Glow1, (float)pose.Glow2, (float)pose.Glow3));
            foreach (MeshRenderer m in gun.Renderers) m.SetPropertyBlock(_block);

            UpdateFlash(rig);
        }

        void UpdateFlash(WeaponRig rig)
        {
            WeaponConfig cfg = rig.Current.Config;
            if (rig.FlashTimer <= 0)
            {
                if (_flash.gameObject.activeSelf) _flash.gameObject.SetActive(false);
                return;
            }
            _flash.gameObject.SetActive(true);
            float t = (float)(rig.FlashTimer / (cfg.Flash.Life * 1.5));
            float s = (float)cfg.Flash.Scale * (0.55f + t * 0.75f);
            float jitter = 0.85f + UnityEngine.Random.value * 0.3f;
            _flashCore.transform.localScale = new Vector3(0.7f * s * jitter, 0.7f * s * jitter, 2.2f * s);
            _flashPetals.transform.localScale = new Vector3(s * jitter, s * jitter, s * (0.8f + UnityEngine.Random.value * 0.5f));
            _flashPetals.transform.localRotation = Quaternion.Euler(0, 0, UnityEngine.Random.value * 360f);
            _flashGlow.transform.localScale = Vector3.one * (s * (0.9f + (1 - t) * 0.8f));
            _flashGlow.transform.localRotation = Quaternion.Euler(0, 0, UnityEngine.Random.value * 360f);
            float k = 1.4f + t * 3.2f;
            Color col = Hex(cfg.Flash.Color);
            _block.Clear();
            _block.SetColor(TintId, col * k);
            _flashCore.SetPropertyBlock(_block);
            _flashPetals.SetPropertyBlock(_block);
            _block.SetColor(TintId, col * (k * 0.8f));
            _flashGlow.SetPropertyBlock(_block);
        }

        /// <summary>A linear colour from an sRGB hex, as three's <c>setHex</c> gives.</summary>
        public static Color Hex(uint rgb) => new Color((float)WeaponModels.Srgb(((rgb >> 16) & 0xff) / 255.0), (float)WeaponModels.Srgb(((rgb >> 8) & 0xff) / 255.0),
            (float)WeaponModels.Srgb((rgb & 0xff) / 255.0), 1);

        /// <summary>three's Euler 'YXZ' (the gun root): Y, then X, then Z, which is what Unity's <c>Quaternion.Euler</c> does.</summary>
        static Quaternion EulerYXZ(double rx, double ry, double rz) => Quaternion.Euler((float)(rx * Mathf.Rad2Deg), (float)(ry * Mathf.Rad2Deg), (float)(rz * Mathf.Rad2Deg));

        /// <summary>three's default Euler 'XYZ' (the parts): X, then Y, then Z.</summary>
        static Quaternion EulerXYZ(double rx, double ry, double rz) =>
            Quaternion.AngleAxis((float)(rx * Mathf.Rad2Deg), Vector3.right) * Quaternion.AngleAxis((float)(ry * Mathf.Rad2Deg), Vector3.up) * Quaternion.AngleAxis((float)(rz * Mathf.Rad2Deg), Vector3.forward);

        /// <summary>Where an anchor of the gun in hand really is in the world: its drawn position with the field-of-view squeeze taken back out.</summary>
        public Vector3 AnchorWorld(bool muzzle, Transform camera)
        {
            Transform a = muzzle ? _shown.Muzzle : _shown.Eject;
            Vector3 local = camera.InverseTransformPoint(a.position);
            Vector3 s = _holder.localScale;
            return camera.TransformPoint(new Vector3(local.x / s.x, local.y / s.y, local.z));
        }

        public void Dispose()
        {
            if (_holder != null) UnityEngine.Object.Destroy(_holder.gameObject);
            foreach (UnityEngine.Object o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
        }
    }
}
