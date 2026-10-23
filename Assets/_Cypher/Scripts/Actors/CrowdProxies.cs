using System;
using Cypher.Core;
using UnityEngine;

namespace Cypher.Actors
{
    /// <summary>
    /// Solid stand-ins for the people nearest the player, so the player cannot walk through them. Eight kinematic capsules are leased each physics step to whoever is
    /// closest within <see cref="CrowdSim.ProxyRange"/>; the rest wait far below the city. The prototype's size: radius 0.30 m, 1.64 m tall, standing on the person's feet.
    /// </summary>
    public sealed class CrowdProxies : IDisposable
    {
        public const int Count = 8;
        public const float Radius = 0.30f;
        public const float Height = 1.64f;
        public const float CentreHeight = 0.86f;

        readonly GameObject[] _objects = new GameObject[Count];
        readonly Rigidbody[] _bodies = new Rigidbody[Count];
        readonly Pedestrian[] _leased = new Pedestrian[Count];

        /// <summary>How many capsules are out with a person now.</summary>
        public int Leased { get; private set; }

        public CrowdProxies(Transform parent)
        {
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject($"PedestrianProxy_{i}");
                if (parent != null) go.transform.SetParent(parent, false);
                var body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = Radius;
                capsule.height = Height;
                capsule.direction = 1;   // along Y
                _objects[i] = go;
                _bodies[i] = body;
                body.position = ParkingSpot(i);
            }
        }

        public GameObject this[int index] => _objects[index];

        /// <summary>Lease the capsules to the people nearest the player; call from FixedUpdate.</summary>
        public void Update(CrowdSim sim, double playerX, double playerZ)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            Leased = sim.NearestToPlayer(playerX, playerZ, _leased);
            for (int i = 0; i < Count; i++)
            {
                if (i < Leased)
                {
                    Pedestrian p = _leased[i];
                    _bodies[i].position = new Vector3((float)p.X, (float)(p.Y + CentreHeight * p.Appearance.Scale), (float)p.Z);
                }
                else
                {
                    _bodies[i].position = ParkingSpot(i);
                }
            }
        }

        /// <summary>Send every capsule away (the crowd is gone).</summary>
        public void ParkAll()
        {
            Leased = 0;
            for (int i = 0; i < Count; i++) _bodies[i].position = ParkingSpot(i);
        }

        /// <summary>Where an unused capsule waits: far under the roadbed, each in its own spot.</summary>
        public static Vector3 ParkingSpot(int index) => new Vector3(0f, -600f - index, 0f);

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
