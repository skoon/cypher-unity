using Cypher.Core;
using UnityEngine;

namespace Cypher.Audio
{
    /// <summary>
    /// What the listener's surroundings do to sound, found with rays into the physics world: which reverb space they are in (twelve rays round and one up, every half second) and whether a wall
    /// stands between them and a sound. The decisions are Core's <see cref="SpaceProbe"/> and <see cref="Occlusion"/>; this casts the rays.
    /// </summary>
    public sealed class AudioEnvironment
    {
        readonly SpaceProbe _probe = new SpaceProbe();
        readonly double[] _distance = new double[SpaceProbe.Rays];
        readonly bool[] _hit = new bool[SpaceProbe.Rays];
        int _raysThisFrame;
        int _frame = -1;

        public int LayerMask { get; set; } = Physics.DefaultRaycastLayers;

        public SpaceProbe Probe => _probe;

        public ReverbSpace Space => _probe.Current;

        /// <summary>0 outside to 1 shut in a small room.</summary>
        public double Indoor => _probe.Indoor;

        /// <summary>Probe the surroundings if one is due. Returns the new space if it changed.</summary>
        public ReverbSpace? Update(Vector3 listener, double now)
        {
            if (!_probe.Due(now)) return null;
            Vector3 origin = new Vector3(listener.x, Mathf.Max(0.6f, listener.y), listener.z);
            for (int i = 0; i < SpaceProbe.Rays; i++)
            {
                SpaceProbe.Direction(i, out double x, out double z);
                bool hit = Physics.Raycast(origin, new Vector3((float)x, 0f, (float)z), out RaycastHit h, (float)SpaceProbe.Range, LayerMask, QueryTriggerInteraction.Ignore);
                _hit[i] = hit;
                _distance[i] = hit ? h.distance : SpaceProbe.Range;
            }
            double ceiling = SpaceProbe.Range;
            if (Physics.Raycast(origin, Vector3.up, out RaycastHit up, (float)SpaceProbe.CeilingRange, LayerMask, QueryTriggerInteraction.Ignore)) ceiling = up.distance;
            return _probe.Observe(now, _distance, _hit, ceiling);
        }

        /// <summary>
        /// How much a wall muffles a sound at <paramref name="source"/>: one ray from the listener toward it. Rays are capped per frame so a burst of sounds cannot spike the physics;
        /// past the cap a sound is heard clear.
        /// </summary>
        public double OcclusionTo(Vector3 listener, Vector3 source)
        {
            if (Time.frameCount != _frame)
            {
                _frame = Time.frameCount;
                _raysThisFrame = 0;
            }
            Vector3 to = source - listener;
            double distance = to.magnitude;
            if (!Occlusion.WorthARay(distance) || _raysThisFrame >= Occlusion.RaysPerFrame) return 0;
            _raysThisFrame++;
            bool hit = Physics.Raycast(listener, to / (float)distance, out RaycastHit h, (float)(distance - 0.5), LayerMask, QueryTriggerInteraction.Ignore);
            return Occlusion.Level(distance, hit, hit ? h.distance : 0);
        }
    }
}
