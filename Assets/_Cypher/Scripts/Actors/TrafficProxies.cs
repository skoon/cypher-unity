using System;
using Cypher.City;
using Cypher.Core;
using UnityEngine;

namespace Cypher.Actors
{
    /// <summary>
    /// Solid stand-ins for the vehicles nearest the player, so the player cannot walk through them. Four kinematic boxes are leased each physics step to whichever vehicles are
    /// closest within <see cref="TrafficSim.ProxyRange"/>; the rest wait far below the city. Each box is the size of the vehicle it is leased to (the prototype's was one size for all).
    /// </summary>
    public sealed class TrafficProxies : IDisposable
    {
        public const int Count = 4;

        readonly GameObject[] _objects = new GameObject[Count];
        readonly Rigidbody[] _bodies = new Rigidbody[Count];
        readonly BoxCollider[] _boxes = new BoxCollider[Count];
        readonly Vehicle[] _leased = new Vehicle[Count];

        /// <summary>How many boxes are out with a vehicle now.</summary>
        public int Leased { get; private set; }

        public TrafficProxies(Transform parent)
        {
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject($"VehicleProxy_{i}");
                if (parent != null) go.transform.SetParent(parent, false);
                var body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                go.AddComponent<ShotSurface>().Target = ShotTarget.Vehicle;   // a shot that hits it is marked as a hit on a vehicle
                _boxes[i] = go.AddComponent<BoxCollider>();
                _objects[i] = go;
                _bodies[i] = body;
                body.position = ParkingSpot(i);
            }
        }

        public GameObject this[int index] => _objects[index];

        /// <summary>Lease the boxes to the vehicles nearest the player; call from FixedUpdate.</summary>
        public void Update(TrafficSim sim, double playerX, double playerZ)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            Leased = sim.NearestToPlayer(playerX, playerZ, _leased);
            for (int i = 0; i < Count; i++)
            {
                if (i >= Leased)
                {
                    _bodies[i].position = ParkingSpot(i);
                    continue;
                }
                Vehicle v = _leased[i];
                GroundVehicleType t = v.Type;
                _boxes[i].size = new Vector3((float)t.Width, (float)t.Height, (float)t.Length);
                _boxes[i].center = new Vector3(0f, (float)t.Height * 0.5f, 0f);
                _bodies[i].position = new Vector3((float)v.X, (float)v.Y, (float)v.Z);
                _bodies[i].rotation = Quaternion.AngleAxis((float)(v.Heading * Mathf.Rad2Deg), Vector3.up);
            }
        }

        /// <summary>Send every box away (the traffic is gone).</summary>
        public void ParkAll()
        {
            Leased = 0;
            for (int i = 0; i < Count; i++) _bodies[i].position = ParkingSpot(i);
        }

        /// <summary>Where an unused box waits: far under the roadbed, each in its own spot.</summary>
        public static Vector3 ParkingSpot(int index) => new Vector3(0f, -700f - index * 10f, 0f);

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
