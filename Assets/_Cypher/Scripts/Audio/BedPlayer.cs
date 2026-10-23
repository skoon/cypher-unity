using System;
using Cypher.Core;
using UnityEngine;

namespace Cypher.Audio
{
    /// <summary>
    /// The city's continuous sound playing: the bed, and the two sounds that stand somewhere (the neon tube and the air-conditioning), each a live synthesis graph on its own source. The weather
    /// reaches them as a snapshot that each graph picks up on the audio thread between blocks. The emitters move to the nearest real tube and the nearest rooftop machine as the player walks,
    /// gliding there so nothing clicks.
    /// </summary>
    public sealed class BedPlayer : IDisposable
    {
        sealed class Snapshot
        {
            public BedState State;
            public int Stamp;
        }

        // where the prototype stood these two when there is nothing near, relative to the listener
        static readonly Vector3 NeonOffset = new Vector3(6f, 4.2f, -5f), MachineOffset = new Vector3(-9f, 6.5f, 7f);
        const float NearestRange = 70f;
        const float GlideSeconds = 0.8f;

        readonly Bed _bed;
        readonly SynthSource _main, _neon, _machine;
        volatile Snapshot _snapshot;
        int _stamp;
        int _mainSeen, _neonSeen, _machineSeen;
        Vector3 _neonTarget, _machineTarget;
        System.Collections.Generic.IReadOnlyList<Vector3> _neonIndexed, _machineIndexed;
        PointGrid _neonGrid, _machineGrid;
        bool _placed;

        public Bed Bed => _bed;
        public SynthSource Main => _main;
        public SynthSource Neon => _neon;
        public SynthSource Machine => _machine;

        /// <summary>Milliseconds the audio thread spends on the bed and its two emitters per device block.</summary>
        public double MillisecondsPerBlock => _main.MillisecondsPerCallback + _neon.MillisecondsPerCallback + _machine.MillisecondsPerCallback;

        public double WorstMilliseconds => Math.Max(_main.WorstMilliseconds, Math.Max(_neon.WorstMilliseconds, _machine.WorstMilliseconds));

        public Vector3 NeonPosition => _neon.transform.position;
        public Vector3 MachinePosition => _machine.transform.position;

        public BedPlayer(Transform parent, MixerLevels mixer, AudioNoiseBank noise, uint seed)
        {
            _bed = new Bed(noise, new Rng(seed), 0, Bed.EmitterMode.Separate);
            // slow modulators and slow weather: the filters need not be remade every frame
            _bed.Main.CoefficientInterval = 32;
            _bed.Neon.CoefficientInterval = 32;
            _bed.AirConditioning.CoefficientInterval = 32;

            var group = mixer.Group(AudioBus.Ambience, 0);
            _main = SynthSource.Create("Bed", parent, _bed.Main, 2, group, spatial: false);
            _neon = SynthSource.Create("Neon buzz", parent, _bed.Neon, 1, group, spatial: true, maxDistance: RolloffCurves.MaxDistance);
            _machine = SynthSource.Create("Air conditioning", parent, _bed.AirConditioning, 1, group, spatial: true, maxDistance: RolloffCurves.MaxDistance);
            foreach (SynthSource emitter in new[] { _neon, _machine })
            {
                emitter.Source.rolloffMode = AudioRolloffMode.Custom;
                emitter.Source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, RolloffCurves.For(3.5, 1.4));
            }

            _snapshot = new Snapshot { State = new BedState(0, 0, 0, 1), Stamp = 0 };
            _main.BeforeBlock = g =>
            {
                Snapshot s = _snapshot;
                if (s.Stamp == _mainSeen) return;
                _mainSeen = s.Stamp;
                _bed.Advance(g.Time, s.State);
            };
            _neon.BeforeBlock = g =>
            {
                Snapshot s = _snapshot;
                if (s.Stamp == _neonSeen) return;
                _neonSeen = s.Stamp;
                _bed.AdvanceNeon(g.Time, s.State);
            };
            _machine.BeforeBlock = g =>
            {
                Snapshot s = _snapshot;
                if (s.Stamp == _machineSeen) return;
                _machineSeen = s.Stamp;
                _bed.AdvanceAirConditioning(g.Time, s.State);
            };
            _mainSeen = _neonSeen = _machineSeen = -1;
            _main.Begin();
            _neon.Begin();
            _machine.Begin();
        }

        /// <summary>What the bed was last told to follow.</summary>
        public BedState State => _snapshot.State;

        /// <summary>How many times the weather or the room has changed what the bed is moving toward.</summary>
        public int StateChanges => _stamp;

        /// <summary>The weather and the room the bed should follow; the graphs move toward it over about a second.</summary>
        public void SetState(BedState state)
        {
            // told every tenth of a second, but a graph moving toward what it already moves toward has nothing to do
            if (state.SameAs(_snapshot.State)) return;
            _snapshot = new Snapshot { State = state, Stamp = ++_stamp };
        }

        /// <summary>Move the two emitters toward the nearest tube and machine to <paramref name="listener"/> (or to where the prototype stood them, if none is near).</summary>
        public void Place(Vector3 listener, System.Collections.Generic.IReadOnlyList<Vector3> neonSources, System.Collections.Generic.IReadOnlyList<Vector3> machineSources, float dt)
        {
            _neonTarget = Nearest(listener, neonSources, ref _neonIndexed, ref _neonGrid) ?? listener + NeonOffset;
            _machineTarget = Nearest(listener, machineSources, ref _machineIndexed, ref _machineGrid) ?? listener + MachineOffset;
            if (!_placed)
            {
                _neon.transform.position = _neonTarget;
                _machine.transform.position = _machineTarget;
                _placed = true;
                return;
            }
            float k = 1f - Mathf.Exp(-dt / GlideSeconds);
            _neon.transform.position = Vector3.Lerp(_neon.transform.position, _neonTarget, k);
            _machine.transform.position = Vector3.Lerp(_machine.transform.position, _machineTarget, k);
        }

        /// <summary>The city has thousands of tubes and vents: they are put in a grid the first time (and again if the city is rebuilt) and the nearest is found in a few cells.</summary>
        static Vector3? Nearest(Vector3 from, System.Collections.Generic.IReadOnlyList<Vector3> points, ref System.Collections.Generic.IReadOnlyList<Vector3> indexed, ref PointGrid grid)
        {
            if (points == null || points.Count == 0) return null;
            if (!ReferenceEquals(points, indexed))
            {
                var tuples = new (double, double, double)[points.Count];
                for (int i = 0; i < tuples.Length; i++) tuples[i] = (points[i].x, points[i].y, points[i].z);
                grid = new PointGrid(tuples, NearestRange);
                indexed = points;
            }
            int found = grid.Nearest(from.x, from.y, from.z, NearestRange);
            return found < 0 ? (Vector3?)null : points[found];
        }

        /// <summary>The first exception any of the graphs threw on the audio thread, or null.</summary>
        public Exception Failure => _main.Failure ?? _neon.Failure ?? _machine.Failure;

        public void Dispose()
        {
            foreach (SynthSource s in new[] { _main, _neon, _machine })
            {
                if (s) UnityEngine.Object.Destroy(s.gameObject);
            }
        }
    }
}
