using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.Combat
{
    /// <summary>
    /// Draws the gunfire effects (<see cref="ImpactSim"/>) the way the prototype's <c>combat/Impacts.js</c> does, in a handful of draws: one dynamic mesh per pool,
    /// rebuilt each frame (decals, sparks, smoke, tracers, debris, brass), and four pooled point lights for the muzzle flashes. The prototype's point sprites
    /// become quads facing the camera, as wide as the sprite would be (its size times tan of half the field of view); its tracer cylinders become ribbons.
    /// </summary>
    public sealed class GunFxRenderer : IDisposable
    {
        sealed class Batch
        {
            public readonly Mesh Mesh;
            public readonly Material Material;
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<int> T = new List<int>();
            readonly bool _normals;

            public Batch(string name, Material material, bool normals)
            {
                Mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                Mesh.MarkDynamic();
                Material = material;
                _normals = normals;
            }

            public void Clear()
            {
                V.Clear(); C.Clear(); U.Clear(); N.Clear(); T.Clear();
            }

            public void Upload()
            {
                Mesh.Clear();
                if (V.Count == 0) return;
                Mesh.SetVertices(V);
                Mesh.SetColors(C);
                if (U.Count == V.Count) Mesh.SetUVs(0, U);
                if (_normals) Mesh.SetNormals(N);
                Mesh.SetTriangles(T, 0);
                Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                int i = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                C.Add(color); C.Add(color); C.Add(color); C.Add(color);
                U.Add(new Vector2(0, 0)); U.Add(new Vector2(1, 0)); U.Add(new Vector2(1, 1)); U.Add(new Vector2(0, 1));
                T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
            }
        }

        static readonly int ModeId = Shader.PropertyToID("_Mode"), TexId = Shader.PropertyToID("_MainTex"), FogId = Shader.PropertyToID("_Fog"),
            OffsetFactorId = Shader.PropertyToID("_OffsetFactor"), OffsetUnitsId = Shader.PropertyToID("_OffsetUnits"),
            SmoothnessId = Shader.PropertyToID("_Smoothness"), MetallicId = Shader.PropertyToID("_Metallic"), CullId = Shader.PropertyToID("_Cull");

        readonly Batch _decals, _sparks, _smoke, _tracers, _debris, _shells;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly Light[] _lights = new Light[ImpactSim.MaxLights];
        readonly Transform _root;
        readonly Vector3[] _tetra, _shellVerts, _shellNormals;
        readonly int[] _tetraTris, _shellTris;

        static readonly Color Brass = ViewmodelRig.Hex(0xb98c3c);

        public GunFxRenderer(GunMaterials materials)
        {
            if (materials == null || !materials.IsComplete) throw new ArgumentException("the effects need their materials", nameof(materials));
            _root = new GameObject("Gunfire effects").transform;

            Material decal = Own(new Material(materials.Alpha) { name = "Bullet holes" });
            decal.SetFloat(ModeId, 1);
            decal.SetFloat(FogId, 1);
            decal.SetFloat(OffsetFactorId, -4);
            decal.SetFloat(OffsetUnitsId, -6);
            decal.SetTexture(TexId, Own(ViewmodelRig.Texture(GunFxArt.BulletHole(), "Bullet hole")));
            decal.renderQueue = (int)RenderQueue.Transparent + 2;
            Material smoke = Own(new Material(materials.Alpha) { name = "Gun smoke" });
            smoke.SetFloat(ModeId, 0);
            smoke.renderQueue = (int)RenderQueue.Transparent + 3;
            Material spark = Own(new Material(materials.Additive) { name = "Sparks" });
            spark.SetFloat(ModeId, 0);
            spark.renderQueue = (int)RenderQueue.Transparent + 4;
            Material tracer = Own(new Material(materials.Additive) { name = "Tracers" });
            tracer.SetFloat(ModeId, 1);
            tracer.renderQueue = (int)RenderQueue.Transparent + 5;
            Material debris = Own(new Material(materials.Lit) { name = "Debris" });
            debris.SetFloat(SmoothnessId, 0.15f);
            debris.SetFloat(MetallicId, 0.1f);
            debris.SetFloat(CullId, 0);   // the chunks and cases are built without minding the winding
            Material brass = Own(new Material(materials.Lit) { name = "Brass" });
            brass.SetFloat(SmoothnessId, 0.7f);
            brass.SetFloat(MetallicId, 1f);
            brass.SetFloat(CullId, 0);

            _decals = Own(new Batch("Bullet holes", decal, false));
            _sparks = Own(new Batch("Sparks", spark, false));
            _smoke = Own(new Batch("Gun smoke", smoke, false));
            _tracers = Own(new Batch("Tracers", tracer, false));
            _debris = Own(new Batch("Debris", debris, true));
            _shells = Own(new Batch("Brass", brass, true));

            for (int i = 0; i < _lights.Length; i++)
            {
                var go = new GameObject($"Muzzle light {i}");
                go.transform.SetParent(_root, false);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                l.enabled = false;
                _lights[i] = l;
            }

            // three's TetrahedronGeometry(0.5): four corners of a cube, flat shaded
            float t = 0.5f / Mathf.Sqrt(3);
            Vector3 p0 = new Vector3(t, t, t), p1 = new Vector3(-t, -t, t), p2 = new Vector3(-t, t, -t), p3 = new Vector3(t, -t, -t);
            _tetra = new[] { p2, p1, p0, p0, p3, p2, p1, p3, p0, p2, p3, p1 };
            _tetraTris = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            (_shellVerts, _shellNormals, _shellTris) = ShellShape();
        }

        Batch Own(Batch b)
        {
            _owned.Add(b.Mesh);
            return b;
        }

        T Own<T>(T o) where T : UnityEngine.Object
        {
            _owned.Add(o);
            return o;
        }

        /// <summary>A case: a body 20 mm long, 4.6 mm in radius, and its rim, eight sides, along Z.</summary>
        static (Vector3[] V, Vector3[] N, int[] T) ShellShape()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tr = new List<int>();
            void Cylinder(float r, float z0, float z1)
            {
                const int seg = 8;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0), d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0);
                    int b = v.Count;
                    v.Add(d0 * r + Vector3.forward * z0); v.Add(d1 * r + Vector3.forward * z0); v.Add(d1 * r + Vector3.forward * z1); v.Add(d0 * r + Vector3.forward * z1);
                    n.Add(d0); n.Add(d1); n.Add(d1); n.Add(d0);
                    tr.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                    int c = v.Count;
                    v.Add(Vector3.forward * z1); v.Add(d0 * r + Vector3.forward * z1); v.Add(d1 * r + Vector3.forward * z1);
                    n.Add(Vector3.forward); n.Add(Vector3.forward); n.Add(Vector3.forward);
                    tr.AddRange(new[] { c, c + 1, c + 2 });
                    int e = v.Count;
                    v.Add(Vector3.forward * z0); v.Add(d1 * r + Vector3.forward * z0); v.Add(d0 * r + Vector3.forward * z0);
                    n.Add(Vector3.back); n.Add(Vector3.back); n.Add(Vector3.back);
                    tr.AddRange(new[] { e, e + 1, e + 2 });
                }
            }
            Cylinder(0.0046f, -0.010f, 0.010f);
            Cylinder(0.0056f, 0.009f, 0.012f);
            return (v.ToArray(), n.ToArray(), tr.ToArray());
        }

        static Vector3 W(V3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        /// <summary>Build this frame's meshes from the simulation and draw them.</summary>
        public void Draw(ImpactSim sim, Camera camera)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (camera == null) return;
            Vector3 right = camera.transform.right, up = camera.transform.up, eye = camera.transform.position;
            float sprite = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            _decals.Clear();
            foreach (Decal d in sim.Decals)
            {
                if (!d.Live) continue;
                Vector3 n = W(d.Normal), p = W(d.Pos);
                Quaternion q = Quaternion.FromToRotation(Vector3.forward, n) * Quaternion.AngleAxis((float)(d.Roll * Mathf.Rad2Deg), Vector3.forward);
                Vector3 a = q * Vector3.right * (float)(d.Size / 2), b = q * Vector3.up * (float)(d.Size / 2);
                _decals.Quad(p - a - b, p + a - b, p + a + b, p - a + b, ViewmodelRig.Hex(d.Tint));
            }

            _sparks.Clear();
            foreach (Spark s in sim.Sparks)
            {
                if (s.Life <= 0) continue;
                Billboard(_sparks, W(s.Pos), (float)s.DrawSize * sprite, right, up, ViewmodelRig.Hex(s.Color), (float)s.Alpha);
            }
            _smoke.Clear();
            foreach (Smoke s in sim.SmokePuffs)
            {
                if (s.Life <= 0) continue;
                Billboard(_smoke, W(s.Pos), (float)s.DrawSize * sprite, right, up, ViewmodelRig.Hex(s.Color), (float)s.Alpha);
            }

            _tracers.Clear();
            foreach (Tracer t in sim.Tracers)
            {
                if (t.Life <= 0) continue;
                Vector3 tail = W(t.Tail), head = W(t.Head), along = head - tail;
                if (along.sqrMagnitude < 1e-8f) continue;
                Vector3 mid = (tail + head) * 0.5f;
                Vector3 side = Vector3.Cross(along, eye - mid).normalized * (float)t.DrawWidth;
                Color c = ViewmodelRig.Hex(t.Color) * (float)t.Fade;
                c.a = 1;
                _tracers.Quad(tail - side, head - side, head + side, tail + side, c);
            }

            _debris.Clear();
            foreach (Debris d in sim.Chunks)
            {
                if (d.Life <= 0) continue;
                Solid(_debris, _tetra, null, _tetraTris, W(d.Pos), new Quaternion((float)d.QX, (float)d.QY, (float)d.QZ, (float)d.QW), (float)d.DrawSize, ViewmodelRig.Hex(d.Color));
            }
            _shells.Clear();
            foreach (Shell s in sim.Shells)
            {
                if (s.Life <= 0) continue;
                var q = s.Resting ? Quaternion.Euler(0, 90, 0) : new Quaternion((float)s.QX, (float)s.QY, (float)s.QZ, (float)s.QW);
                Solid(_shells, _shellVerts, _shellNormals, _shellTris, W(s.Pos), q, (float)s.DrawScale, Brass);
            }

            foreach (Batch b in new[] { _decals, _smoke, _sparks, _tracers, _debris, _shells })
            {
                b.Upload();
                if (b.V.Count == 0) continue;
                var rp = new RenderParams(b.Material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = b.Mesh.bounds };
                Graphics.RenderMesh(rp, b.Mesh, 0, Matrix4x4.identity);
            }

            for (int i = 0; i < _lights.Length; i++)
            {
                FlashLight e = sim.Lights[i];
                Light l = _lights[i];
                bool on = e.Life > 0 && e.Intensity > 0;
                if (l.enabled != on) l.enabled = on;
                if (!on) continue;
                l.transform.position = W(e.Pos);
                l.color = ViewmodelRig.Hex(e.Color);
                // three's physically based point light (candela) against Unity's: the prototype's numbers read about this bright in the city's units
                l.intensity = (float)(e.Intensity * 0.12);
                l.range = (float)e.Range;
            }
        }

        static void Billboard(Batch b, Vector3 p, float size, Vector3 right, Vector3 up, Color color, float alpha)
        {
            Vector3 r = right * (size / 2), u = up * (size / 2);
            color.a = alpha;
            b.Quad(p - r - u, p + r - u, p + r + u, p - r + u, color);
        }

        static void Solid(Batch b, Vector3[] verts, Vector3[] normals, int[] tris, Vector3 p, Quaternion q, float scale, Color color)
        {
            int start = b.V.Count;
            for (int i = 0; i < verts.Length; i++)
            {
                b.V.Add(p + q * (verts[i] * scale));
                b.C.Add(color);
            }
            if (normals != null)
            {
                foreach (Vector3 n in normals) b.N.Add(q * n);
            }
            else
            {
                for (int i = 0; i < verts.Length; i += 3)
                {
                    Vector3 n = q * Vector3.Cross(verts[i + 1] - verts[i], verts[i + 2] - verts[i]).normalized;
                    b.N.Add(n); b.N.Add(n); b.N.Add(n);
                }
            }
            foreach (int t in tris) b.T.Add(start + t);
        }

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            foreach (UnityEngine.Object o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
        }
    }
}
