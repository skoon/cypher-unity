using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Hands a few sound voices to the nearest of many things (cars, people) and keeps them there: a thing that already has a voice keeps it while it stays within a margin of the cutoff, so
    /// two cars passing at about the same distance do not swap voices every frame with a click each time.
    /// </summary>
    public sealed class NearestVoices
    {
        readonly int[] _slot;
        readonly List<(int id, double distance)> _near = new List<(int, double)>();
        readonly int[] _assigned;

        public NearestVoices(int voices)
        {
            if (voices < 1) throw new ArgumentOutOfRangeException(nameof(voices), voices, "at least one voice");
            _slot = new int[voices];
            _assigned = new int[voices];
            for (int i = 0; i < voices; i++) _slot[i] = -1;
        }

        public int Voices => _slot.Length;

        /// <summary>The id each voice is on now, or -1.</summary>
        public int IdOf(int voice) => _slot[voice];

        /// <summary>Clear every voice.</summary>
        public void Reset()
        {
            for (int i = 0; i < _slot.Length; i++) _slot[i] = -1;
        }

        /// <summary>Offer a candidate for this round; only those within <paramref name="range"/> metres count.</summary>
        public void Offer(int id, double distance, double range)
        {
            if (distance <= range) _near.Add((id, distance));
        }

        /// <summary>
        /// Decide the round: a voice stays with its thing if it was offered again and is no further than the voice's cutoff (the distance of the worst thing taking a voice) times
        /// <paramref name="keep"/>; free voices go to the nearest things without one. Afterwards <see cref="IdOf"/> says who has what.
        /// </summary>
        public void Resolve(double keep = 1.35)
        {
            _near.Sort((a, b) => a.distance.CompareTo(b.distance));
            // the nearest N define the cutoff
            double cutoff = _near.Count >= _slot.Length ? _near[_slot.Length - 1].distance : double.PositiveInfinity;
            double stay = cutoff * keep;
            for (int v = 0; v < _slot.Length; v++)
            {
                _assigned[v] = -1;
                int id = _slot[v];
                if (id < 0) continue;
                int found = _near.FindIndex(n => n.id == id);
                if (found >= 0 && _near[found].distance <= stay) _assigned[v] = id;
            }
            for (int i = 0; i < _near.Count; i++)
            {
                int id = _near[i].id;
                bool taken = false;
                for (int v = 0; v < _slot.Length; v++)
                {
                    if (_assigned[v] == id) { taken = true; break; }
                }
                if (taken) continue;
                for (int v = 0; v < _slot.Length; v++)
                {
                    if (_assigned[v] < 0)
                    {
                        _assigned[v] = id;
                        break;
                    }
                }
            }
            Array.Copy(_assigned, _slot, _slot.Length);
            _near.Clear();
        }
    }

    /// <summary>A footfall to play: whose, where, and how hard.</summary>
    public readonly struct Footfall
    {
        public readonly int Person;
        public readonly double X, Z;
        public readonly double Level;
        public readonly bool Hurried;

        public Footfall(int person, double x, double z, double level, bool hurried)
        {
            Person = person;
            X = x;
            Z = z;
            Level = level;
            Hurried = hurried;
        }
    }

    /// <summary>
    /// Footsteps for the people nearest the listener, on their own gait: a foot falls each time a walker's gait phase passes a half turn, so a hurrying person's steps are quicker and the
    /// crowd's steps stay in step with what is drawn.
    /// </summary>
    public sealed class WalkerSteps
    {
        public const double Range = 16;
        public const int Voices = 6;
        /// <summary>A walker slower than this makes no steps (standing, or barely shuffling).</summary>
        public const double MinSpeed = 0.35;

        readonly Dictionary<int, double> _lastPhase = new Dictionary<int, double>();
        readonly List<(Pedestrian p, double d)> _candidates = new List<(Pedestrian, double)>();

        /// <summary>Which half turn of the gait a phase is in.</summary>
        static int Half(double phase) => (int)Math.Floor(phase / Math.PI);

        /// <summary>Footfalls since the last call for the nearest walkers, added to <paramref name="into"/>.</summary>
        public void Update(IReadOnlyList<Pedestrian> people, double listenerX, double listenerZ, List<Footfall> into)
        {
            _candidates.Clear();
            foreach (Pedestrian p in people)
            {
                double dx = p.X - listenerX, dz = p.Z - listenerZ;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d <= Range) _candidates.Add((p, d));
            }
            _candidates.Sort((a, b) => a.d.CompareTo(b.d));
            int n = Math.Min(Voices, _candidates.Count);
            for (int i = 0; i < n; i++)
            {
                Pedestrian p = _candidates[i].p;
                if (_lastPhase.TryGetValue(p.Index, out double last))
                {
                    // a step is a half turn passed, whichever way the phase wrapped
                    int before = Half(last), after = Half(p.Phase);
                    bool stepped = after != before && p.Speed >= MinSpeed;
                    if (stepped) into.Add(new Footfall(p.Index, p.X, p.Z, p.Hurried || p.State == PedState.Hurry ? 0.8 : 0.55, p.State == PedState.Hurry));
                }
                _lastPhase[p.Index] = p.Phase;
            }
            // forget those that have gone out of range, so a person returning does not step on arrival
            if (_lastPhase.Count > 64)
            {
                var keep = new HashSet<int>();
                for (int i = 0; i < n; i++) keep.Add(_candidates[i].p.Index);
                var drop = new List<int>();
                foreach (int k in _lastPhase.Keys) if (!keep.Contains(k)) drop.Add(k);
                foreach (int k in drop) _lastPhase.Remove(k);
            }
        }

        public void Reset() { _lastPhase.Clear(); }
    }

    /// <summary>What a moving vehicle sounds like, from its speed: an engine note that climbs with speed and a tyre hiss that rises with speed and is louder on wet roads.</summary>
    public readonly struct VehicleSound
    {
        public readonly double EngineHz, EngineGain, TyreGain, TyreHz;

        public VehicleSound(double engineHz, double engineGain, double tyreGain, double tyreHz)
        {
            EngineHz = engineHz;
            EngineGain = engineGain;
            TyreGain = tyreGain;
            TyreHz = tyreHz;
        }

        /// <summary>Metres a second at which a car is at its loudest; faster is the same.</summary>
        public const double FullSpeed = 15;

        static double Smooth(double a, double b, double x)
        {
            double t = Math.Min(1, Math.Max(0, (x - a) / (b - a)));
            return t * t * (3 - 2 * t);
        }

        /// <param name="speed">Metres a second.</param>
        /// <param name="length">The vehicle's length: a long one has a deeper voice.</param>
        /// <param name="wet">How wet the road is, 0 to 1.</param>
        public static VehicleSound For(double speed, double length, double wet)
        {
            double s = Math.Min(Math.Max(0, speed), FullSpeed * 1.4);
            double pitch = Math.Min(1.5, Math.Max(0.6, 4.5 / Math.Max(1, length)));
            double engineHz = (62 + 5.5 * s) * pitch;
            double engineGain = 0.18 + 0.022 * s;
            double tyre = 0.34 * (0.4 + Math.Min(1, Math.Max(0, wet))) * Smooth(1, 12, s);
            double tyreHz = 900 + 1500 * Math.Min(1, s / FullSpeed);
            return new VehicleSound(engineHz, engineGain, tyre, tyreHz);
        }
    }
}
