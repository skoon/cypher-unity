using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>A one-shot the city's random clocks ask for: which sound, when, where, and how loud.</summary>
    public readonly struct AmbientShot
    {
        public readonly string Sound;
        public readonly double Time;
        public readonly double X, Y, Z;
        public readonly double Volume;
        public readonly double? Pitch;
        /// <summary>For a car pass: how wet the road is.</summary>
        public readonly double? Wet;

        public AmbientShot(string sound, double time, double x, double y, double z, double volume, double? pitch = null, double? wet = null)
        {
            Sound = sound;
            Time = time;
            X = x;
            Y = y;
            Z = z;
            Volume = volume;
            Pitch = pitch;
            Wet = wet;
        }
    }

    /// <summary>
    /// The six clocks that keep the bed from ever repeating, as the prototype's Ambience.js runs them: drips while it rains, a snatch of speech, a failing neon tube, a car hissing past on the wet,
    /// a distant siren and a distant impact, each at random gaps and at random places round the listener. Called often (ten times a second) with a lookahead, it says which shots are due
    /// in the next stretch. After a long stall it resyncs rather than firing a backlog.
    /// </summary>
    public sealed class AmbientEvents
    {
        enum Clock { Drip, Voice, Buzz, Car, Siren, Impact }

        readonly Rng _rng;
        readonly double[] _next = new double[6];
        readonly double _eventScale;

        // the call in progress
        List<AmbientShot> _into;
        BedState _state;
        double _lx, _ly, _lz;
        bool _hasNeon;
        double _nx, _ny, _nz;

        /// <summary>Whether a car pass and a voice are made up here. Off when the game has real cars and people to make those sounds instead.</summary>
        public bool RandomCars { get; set; } = true;
        public bool RandomVoices { get; set; } = true;

        /// <param name="rng">The stream the bed was built with, carried on.</param>
        /// <param name="eventScale">1 for the game; the prototype's offline measurement uses 3 to hear more in a short render.</param>
        public AmbientEvents(Bed bed, double eventScale = 1)
        {
            _rng = bed.Rng;
            _eventScale = eventScale;
            _next[(int)Clock.Siren] = bed.NextSiren;
            _next[(int)Clock.Impact] = bed.NextImpact;
            _next[(int)Clock.Drip] = bed.NextDrip;
            _next[(int)Clock.Voice] = bed.NextVoice;
            _next[(int)Clock.Buzz] = bed.NextBuzz;
            _next[(int)Clock.Car] = bed.NextCar;
        }

        /// <summary>A random point round the listener on the ground-ish, at about <paramref name="dist"/> metres.</summary>
        void Around(double dist, double y, double spread, out double x, out double yy, out double z)
        {
            double a = _rng.Next() * Math.PI * 2;
            double d = dist * (0.7 + _rng.Next() * 0.6);
            x = _lx + Math.Cos(a) * d;
            yy = _ly + y + (spread != 0 ? (_rng.Next() - 0.5) * spread : 0);
            z = _lz + Math.Sin(a) * d;
        }

        double Gap(double min, double max) => (min + _rng.Next() * (max - min)) / Math.Max(0.05, _eventScale);

        void Run(Clock clock, double now, double lookahead, double min, double max)
        {
            ref double next = ref _next[(int)clock];
            // after a long stall (the game paused) resync rather than firing a backlog all at once
            if (next < now - 4) next = now + _rng.Next() * Gap(min, max);
            int guard = 0;
            while (next < now + lookahead && guard++ < 6)
            {
                if (next >= now - 0.25) Fire(clock, next);
                next += Gap(min, max);
            }
        }

        void Fire(Clock clock, double t)
        {
            double x, y, z;
            switch (clock)
            {
                case Clock.Drip:
                    if (_state.Rain < 0.05) return;
                    Around(4, 1.5, 3, out x, out y, out z);
                    _into.Add(new AmbientShot("amb.drip", t, x, y, z, 0.4 + _rng.Next() * 0.6));
                    break;
                case Clock.Voice:
                    Around(11, 0.4, 1.2, out x, out y, out z);
                    {
                        double volume = (0.25 + _rng.Next() * 0.5) * _state.Density;
                        double pitch = 0.9 + _rng.Next() * 0.25;
                        if (RandomVoices) _into.Add(new AmbientShot("amb.voice", t, x, y, z, volume, pitch));
                    }
                    break;
                case Clock.Buzz:
                    if (_hasNeon)
                    {
                        x = _nx; y = _ny; z = _nz;
                    }
                    else Around(7, 4, 0, out x, out y, out z);
                    _into.Add(new AmbientShot("amb.neonbuzz", t, x, y, z, 0.4 + _rng.Next() * 0.5));
                    break;
                case Clock.Car:
                    Around(26, 0.5, 0, out x, out y, out z);
                    {
                        double volume = 0.35 + _rng.Next() * 0.5;
                        if (RandomCars) _into.Add(new AmbientShot("amb.carpass", t, x, y, z, volume, null, _state.Rain));
                    }
                    break;
                case Clock.Siren:
                    Around(95, 14, 0, out x, out y, out z);
                    _into.Add(new AmbientShot("amb.siren", t, x, y, z, 0.5 + _rng.Next() * 0.5));
                    break;
                case Clock.Impact:
                    Around(85, 6, 0, out x, out y, out z);
                    _into.Add(new AmbientShot("amb.rumbleimpact", t, x, y, z, 0.4 + _rng.Next() * 0.6));
                    break;
            }
        }

        /// <summary>The shots due between <paramref name="now"/> and <paramref name="now"/> + <paramref name="lookahead"/>, added to <paramref name="into"/>.</summary>
        /// <param name="listener">Where the listener is, metres; the shots are placed around them.</param>
        /// <param name="neon">Where the nearest neon tube is, if there is one; otherwise the buzz is placed at random.</param>
        public void Advance(double now, double lookahead, BedState state, (double x, double y, double z) listener, (double x, double y, double z)? neon, List<AmbientShot> into)
        {
            _into = into;
            _state = state;
            _lx = listener.x; _ly = listener.y; _lz = listener.z;
            _hasNeon = neon.HasValue;
            if (neon.HasValue) { _nx = neon.Value.x; _ny = neon.Value.y; _nz = neon.Value.z; }
            double rain = state.Rain;
            Run(Clock.Drip, now, lookahead, 0.35 / (0.15 + rain), 3.5 / (0.15 + rain));
            Run(Clock.Voice, now, lookahead, 1.6, 7);
            Run(Clock.Buzz, now, lookahead, 4, 16);
            Run(Clock.Car, now, lookahead, 6, 22);
            Run(Clock.Siren, now, lookahead, 28, 85);
            Run(Clock.Impact, now, lookahead, 22, 70);
            _into = null;
        }
    }
}
