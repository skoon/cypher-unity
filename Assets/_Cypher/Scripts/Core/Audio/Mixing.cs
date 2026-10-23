using System;

namespace Cypher.Core
{
    /// <summary>The rule for a wall between the listener and a sound: one ray toward the sound; what it hits costs the sound its highs and 60% of its level.</summary>
    public static class Occlusion
    {
        public const double MinDistance = 2, MaxDistance = 90;
        /// <summary>Rays cast for this in one frame, so a burst of sounds cannot spike the physics.</summary>
        public const int RaysPerFrame = 6;
        /// <summary>The low-pass's resonance (dB) when a sound is occluded.</summary>
        public const double FilterQ = 0.5;
        public const double OpenCutoff = 20000, ClosedCutoff = 520;

        /// <summary>Whether a sound this far away is worth a ray at all.</summary>
        public static bool WorthARay(double distance) => distance >= MinDistance && distance <= MaxDistance;

        /// <summary>0 for a clear path, up to 0.95 for a sound well behind something. <paramref name="hitDistance"/> is how far along the ray the first thing was; deeper geometry between means more muffling.</summary>
        public static double Level(double distance, bool hit, double hitDistance)
        {
            if (!hit || !WorthARay(distance)) return 0;
            return Math.Min(0.95, Math.Max(0, 0.45 + (1 - hitDistance / distance) * 0.5));
        }

        /// <summary>The low-pass cutoff (Hz) for an occlusion level; fully open below the smallest level that is worth a filter.</summary>
        public static double CutoffHz(double occlusion)
        {
            if (occlusion <= 0.001) return OpenCutoff;
            double t = Math.Pow(Math.Min(1, occlusion), 0.7);
            return OpenCutoff + (ClosedCutoff - OpenCutoff) * t;
        }

        /// <summary>The gain for an occlusion level: 1 clear, 0.4 at full occlusion.</summary>
        public static double Gain(double occlusion) => 1 + (0.4 - 1) * Math.Min(1, Math.Max(0, occlusion));
    }

    /// <summary>
    /// A fixed number of sound voices handed out by priority. When none is free the quietest-priority, oldest voice is taken over by a sound at least as important, and a sound less
    /// important than all of them is dropped.
    /// </summary>
    public sealed class VoicePool
    {
        struct Slot
        {
            public bool Busy;
            public int Priority;
            public double Start;
        }

        readonly Slot[] _slots;

        public VoicePool(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "a pool needs a voice");
            _slots = new Slot[capacity];
        }

        public int Capacity => _slots.Length;
        public int Played { get; private set; }
        public int Stolen { get; private set; }
        public int Culled { get; private set; }

        public int Active
        {
            get
            {
                int n = 0;
                foreach (Slot s in _slots) if (s.Busy) n++;
                return n;
            }
        }

        public bool IsBusy(int slot) => _slots[slot].Busy;

        /// <summary>A voice for a sound, or -1 if there is none it may have. <paramref name="stolenSlot"/> is the slot that was taken over, or -1.</summary>
        public int Acquire(int priority, double startTime, out bool stolenFromAnother)
        {
            stolenFromAnother = false;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].Busy) return Take(i, priority, startTime);
            }
            int worst = -1;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (worst < 0 || _slots[i].Priority < _slots[worst].Priority
                    || (_slots[i].Priority == _slots[worst].Priority && _slots[i].Start < _slots[worst].Start)) worst = i;
            }
            if (_slots[worst].Priority > priority)
            {
                Culled++;
                return -1;
            }
            Stolen++;
            stolenFromAnother = true;
            return Take(worst, priority, startTime);
        }

        int Take(int slot, int priority, double startTime)
        {
            _slots[slot] = new Slot { Busy = true, Priority = priority, Start = startTime };
            Played++;
            return slot;
        }

        public void Release(int slot)
        {
            _slots[slot].Busy = false;
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < _slots.Length; i++) _slots[i].Busy = false;
        }
    }

    /// <summary>
    /// The levels of the buses: each bus's base level (the prototype's mix) times its Options slider, ducked toward a lower level when the game is paused or a menu is open, each with its own
    /// time constant (the music stays more present under a menu; the interface never ducks). The master level is applied after the compressors, in <see cref="MasterStage"/>.
    /// </summary>
    public sealed class BusLevels
    {
        public const double MasterBase = MasterStage.BaseGain;
        public const double DuckPaused = 0.15, DuckMenu = 0.45;
        public const double MusicDuckPaused = 0.3, MusicDuckMenu = 0.75;

        readonly double[] _duck = { 1, 1, 1, 1, 1 };
        double _reverbDuck = 1;
        double _duckTarget = 1, _musicDuckTarget = 1;

        public double MasterSlider { get; set; } = 1;

        readonly double[] _slider = { 1, 1, 1, 1, 1 };

        /// <summary>The prototype's base level of each bus.</summary>
        public static double Base(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Ambience: return 0.85;
                case AudioBus.Sfx: return 0.95;
                case AudioBus.Ui: return 0.6;
                case AudioBus.Music: return 0.55;
                case AudioBus.Voice: return 1.0;
                default: throw new ArgumentOutOfRangeException(nameof(bus), bus, "unknown bus");
            }
        }

        /// <summary>The time constant (seconds) a bus ducks with.</summary>
        public static double DuckTime(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Ambience: return 0.2;
                case AudioBus.Sfx: return 0.15;
                case AudioBus.Voice: return 0.15;
                case AudioBus.Music: return 0.25;
                case AudioBus.Ui: return 0.05;
                default: throw new ArgumentOutOfRangeException(nameof(bus), bus, "unknown bus");
            }
        }

        public const double ReverbDuckTime = 0.2;

        public void SetSlider(AudioBus bus, double value)
        {
            if (double.IsNaN(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "a volume cannot be negative");
            _slider[(int)bus] = value;
        }

        public double Slider(AudioBus bus) => _slider[(int)bus];

        /// <summary>
        /// Say what the game is doing: paused (everything but the interface goes nearly quiet), in a menu (it goes down by more than half), or on the title screen
        /// (<paramref name="silentWorld"/>: nothing but the interface is heard).
        /// </summary>
        public void SetState(bool paused, bool inMenu, bool silentWorld = false)
        {
            _duckTarget = silentWorld ? 0 : paused ? DuckPaused : inMenu ? DuckMenu : 1;
            _musicDuckTarget = silentWorld ? 0 : paused ? MusicDuckPaused : inMenu ? MusicDuckMenu : 1;
        }

        double DuckTarget(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Ui: return 1;
                case AudioBus.Music: return _musicDuckTarget;
                default: return _duckTarget;
            }
        }

        /// <summary>Move every duck toward its target by <paramref name="dt"/> seconds, each with its own time constant (the exponential approach of Web Audio's setTargetAtTime).</summary>
        public void Advance(double dt)
        {
            if (dt < 0) throw new ArgumentOutOfRangeException(nameof(dt), dt, "time does not run backward");
            foreach (AudioBus bus in AudioBuses.All)
            {
                int i = (int)bus;
                _duck[i] += (DuckTarget(bus) - _duck[i]) * (1 - Math.Exp(-dt / DuckTime(bus)));
            }
            _reverbDuck += (_duckTarget - _reverbDuck) * (1 - Math.Exp(-dt / ReverbDuckTime));
        }

        /// <summary>Settle every duck at its target at once (the state when a game begins).</summary>
        public void Snap()
        {
            foreach (AudioBus bus in AudioBuses.All) _duck[(int)bus] = DuckTarget(bus);
            _reverbDuck = _duckTarget;
        }

        public double Duck(AudioBus bus) => _duck[(int)bus];

        public double ReverbDuck => _reverbDuck;

        /// <summary>A bus's linear gain: its base level, its slider and its duck.</summary>
        public double Gain(AudioBus bus) => Base(bus) * _slider[(int)bus] * _duck[(int)bus];

        /// <summary>The master's level: its base level and its slider (the ducks do not touch it).</summary>
        public double Master => MasterBase * MasterSlider;

        /// <summary>The bus's mixer volume in decibels.</summary>
        public double Decibels(AudioBus bus) => AudioBuses.ToDecibels(Gain(bus));
    }
}
