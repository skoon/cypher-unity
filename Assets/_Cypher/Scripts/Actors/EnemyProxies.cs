using System;
using System.Collections.Generic;
using Cypher.City;
using Cypher.Core;
using UnityEngine;

namespace Cypher.Actors
{
    /// <summary>
    /// What a shot can hit on the enemies: for every living fighter (at any distance, not just the nearest few as the crowd has), a capsule from the legs to the shoulders
    /// and a sphere on the head, posed as the body is (<see cref="PedestrianPose.HitShape"/>: crouched behind cover, flinching), each a kinematic body marked as an
    /// <see cref="ShotTarget.Enemy"/> with an <see cref="EnemyHitbox"/> naming the enemy. The dead are not targets: their volumes go back to wait far below the city.
    /// They are on their own layer (<see cref="LayerName"/>), which collides with nothing: shots and sight lines are rays and still find them, but the player walks
    /// through them as they walk through the player (the sim moves them, not physics).
    /// </summary>
    public sealed class EnemyProxies : IDisposable
    {
        public const int Count = EnemyBodies.Max;

        /// <summary>The project's layer for the volumes (ProjectSettings/TagManager.asset).</summary>
        public const string LayerName = "Enemy";

        /// <summary>The volumes' layer; throws when the project does not have it.</summary>
        public static int Layer
        {
            get
            {
                int layer = LayerMask.NameToLayer(LayerName);
                if (layer < 0) throw new InvalidOperationException($"the project has no '{LayerName}' layer (ProjectSettings/TagManager.asset)");
                return layer;
            }
        }

        readonly GameObject[] _objects = new GameObject[Count * 2];
        readonly Rigidbody[] _bodies = new Rigidbody[Count * 2];
        readonly EnemyHitbox[] _hits = new EnemyHitbox[Count * 2];
        readonly CapsuleCollider[] _capsules = new CapsuleCollider[Count];
        readonly SphereCollider[] _spheres = new SphereCollider[Count];

        /// <summary>How many fighters have volumes now.</summary>
        public int Leased { get; private set; }

        public EnemyProxies(Transform parent)
        {
            int layer = Layer;
            for (int other = 0; other < 32; other++) Physics.IgnoreLayerCollision(layer, other, true);
            for (int i = 0; i < Count; i++)
            {
                _capsules[i] = Make(i * 2, $"EnemyBody_{i}", parent, false).AddComponent<CapsuleCollider>();
                _capsules[i].direction = 1;   // along the body's own Y, turned to lie along the spine
                _spheres[i] = Make(i * 2 + 1, $"EnemyHead_{i}", parent, true).AddComponent<SphereCollider>();
            }
        }

        GameObject Make(int slot, string name, Transform parent, bool head)
        {
            var go = new GameObject(name) { layer = Layer };
            if (parent != null) go.transform.SetParent(parent, false);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            go.AddComponent<ShotSurface>().Target = ShotTarget.Enemy;
            EnemyHitbox hit = go.AddComponent<EnemyHitbox>();
            hit.Head = head;
            _objects[slot] = go;
            _bodies[slot] = body;
            _hits[slot] = hit;
            body.position = ParkingSpot(slot);
            return go;
        }

        /// <summary>The body capsule (even slots) and head sphere (odd) of fighter slot <paramref name="index"/> / 2.</summary>
        public GameObject this[int index] => _objects[index];

        public CapsuleCollider Capsule(int index) => _capsules[index];

        public SphereCollider Sphere(int index) => _spheres[index];

        /// <summary>Pose the volumes on the living fighters, as they stand <paramref name="alpha"/> of the way into the step; call from FixedUpdate.</summary>
        public void Update(IReadOnlyList<CombatBody> fighters, double alpha, double time)
        {
            if (fighters == null) throw new ArgumentNullException(nameof(fighters));
            int n = 0;
            for (int i = 0; i < fighters.Count; i++)
            {
                CombatBody f = fighters[i];
                if (f.Death > 0) continue;
                if (n >= Count) throw new InvalidOperationException($"more than {Count} living fighters");
                PoseHitShape h = PedestrianPose.HitShape(f.Pose(alpha, time));
                Place(n, f.Id, h);
                n++;
            }
            Leased = n;
            for (int i = n; i < Count; i++) Park(i);
        }

        void Place(int i, int enemy, PoseHitShape h)
        {
            var a = new Vector3((float)h.BodyA.X, (float)h.BodyA.Y, (float)h.BodyA.Z);
            var b = new Vector3((float)h.BodyB.X, (float)h.BodyB.Y, (float)h.BodyB.Z);
            Vector3 spine = b - a;
            float length = spine.magnitude;
            CapsuleCollider c = _capsules[i];
            c.radius = (float)h.BodyRadius;
            c.height = length + 2 * (float)h.BodyRadius;
            Rigidbody body = _bodies[i * 2];
            body.position = (a + b) * 0.5f;
            body.rotation = length > 1e-4f ? Quaternion.FromToRotation(Vector3.up, spine / length) : Quaternion.identity;
            _hits[i * 2].Enemy = enemy;

            _spheres[i].radius = (float)h.HeadRadius;
            Rigidbody head = _bodies[i * 2 + 1];
            head.position = new Vector3((float)h.Head.X, (float)h.Head.Y, (float)h.Head.Z);
            _hits[i * 2 + 1].Enemy = enemy;
        }

        void Park(int i)
        {
            for (int k = i * 2; k <= i * 2 + 1; k++)
            {
                _bodies[k].position = ParkingSpot(k);
                _bodies[k].rotation = Quaternion.identity;
                _hits[k].Enemy = -1;
            }
        }

        /// <summary>Send every volume away (the fight is gone).</summary>
        public void ParkAll()
        {
            Leased = 0;
            for (int i = 0; i < Count; i++) Park(i);
        }

        /// <summary>Where an unused volume waits: far under the roadbed, each in its own spot (and clear of the crowd's).</summary>
        public static Vector3 ParkingSpot(int index) => new Vector3(40f, -700f - index, 0f);

        public void Dispose()
        {
            foreach (GameObject go in _objects)
            {
                if (go == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(go);
                else UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
