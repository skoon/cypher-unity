using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>Where a note of the score goes: straight to the music bus (drums), through the sidechain duck (bass), or through the dotted-eighth delay and then the duck (arpeggio, stab, lead, riser).</summary>
    public enum ScoreFeed
    {
        Bus,
        Duck,
        Delay,
    }

    /// <summary>One note the score asks for.</summary>
    public readonly struct ScoreNote
    {
        public readonly double Time;
        public readonly string Sound;
        public readonly double Volume;
        public readonly ScoreFeed Feed;
        public readonly double? Freq, Dur, Cutoff, Pitch;
        public readonly double[] Freqs;

        public ScoreNote(double time, string sound, double volume, ScoreFeed feed, double? freq = null, double? dur = null, double? cutoff = null, double? pitch = null, double[] freqs = null)
        {
            Time = time;
            Sound = sound;
            Volume = volume;
            Feed = feed;
            Freq = freq;
            Dur = dur;
            Cutoff = cutoff;
            Pitch = pitch;
            Freqs = freqs;
        }

        /// <summary>The recipe's parameters for this note.</summary>
        public VoiceParams ToParams() => new VoiceParams { Freq = Freq, Dur = Dur, Cutoff = Cutoff, Pitch = Pitch ?? 1, Freqs = Freqs };
    }

    public enum ScoreChangeKind
    {
        /// <summary>Pad oscillator <see cref="ScoreChange.Index"/> glides to a new frequency.</summary>
        PadGlide,
        PadGain,
        PadFilter,
        DelayWet,
        /// <summary>The sidechain: duck to <see cref="ScoreChange.Value"/> at <see cref="ScoreChange.Time"/>, then recover from 1 ms later with the time constant <see cref="ScoreChange.Tau"/>.</summary>
        DuckPump,
    }

    /// <summary>A change to the score's live parts: its pad, its delay and its sidechain.</summary>
    public readonly struct ScoreChange
    {
        public readonly ScoreChangeKind Kind;
        public readonly int Index;
        public readonly double Value, Time, Tau;

        public ScoreChange(ScoreChangeKind kind, double value, double time, double tau, int index = 0)
        {
            Kind = kind;
            Value = value;
            Time = time;
            Tau = tau;
            Index = index;
        }
    }

    /// <summary>
    /// The generative score, the prototype's Music.js ported: synthwave on a fixed tempo (108 bpm, sixteen steps to the bar), a pad of six detuned oscillators that glides to each chord, a bass,
    /// an arpeggio, drums, a chord stab, a lead and a riser, whose layers arm on the bar as the intensity rises. Tempo never changes; intensity does, so it goes from "walking home in the rain"
    /// to something much louder without a hard cut. This class is the scheduler: it says which notes to play and when, and how the pad, the delay and the sidechain move. It makes no sound itself.
    /// </summary>
    public sealed class Score
    {
        public const double Bpm = 108;
        public const int StepsPerBar = 16;

        sealed class Chord
        {
            public readonly int Root;
            public readonly int[] Intervals;

            public Chord(int root, params int[] intervals)
            {
                Root = root;
                Intervals = intervals;
            }
        }

        static readonly Dictionary<string, Chord> Chords = new Dictionary<string, Chord>
        {
            ["i"] = new Chord(0, 0, 3, 7, 10),
            ["iv"] = new Chord(5, 0, 3, 7, 10),
            ["v"] = new Chord(7, 0, 3, 7, 10),
            ["VI"] = new Chord(-4, 0, 4, 7, 11),
            ["III"] = new Chord(3, 0, 4, 7, 11),
            ["VII"] = new Chord(-2, 0, 4, 7, 10),
            ["bII"] = new Chord(1, 0, 4, 7, 11),
        };

        static readonly string[][] Progressions =
        {
            new[] { "i", "VI", "III", "VII" },
            new[] { "i", "VII", "VI", "VII" },
            new[] { "i", "iv", "VI", "v" },
            new[] { "i", "bII", "VI", "VII" },
            new[] { "i", "III", "VII", "VI" },
        };

        // -1 means no note; numbers are semitone offsets from the chord root
        static readonly int[][] BassPatterns =
        {
            new[] { 0, -1, -1, -1, -1, -1, -1, -1, 0, -1, -1, -1, -1, -1, -1, -1 },
            new[] { 0, -1, -1, -1, 0, -1, -1, -1, 0, -1, -1, -1, 7, -1, -1, -1 },
            new[] { 0, -1, 0, -1, 0, -1, 12, -1, 0, -1, 0, -1, 7, -1, 12, -1 },
            new[] { 0, 0, 12, 0, 0, 0, 12, 0, 0, 0, 12, 0, 7, 7, 12, 0 },
        };

        static readonly int[][] ArpShapes =
        {
            new[] { 0, 1, 2, 1 },
            new[] { 0, 1, 2, 3, 2, 1 },
            new[] { 0, 2, 1, 3 },
            new[] { 0, 1, 2, 3, 2, 3, 1, 2 },
        };

        readonly Rng _rng;
        readonly double _stepDuration = 60.0 / Bpm / 4;
        readonly string[] _progression;
        int[] _arpShape;
        int _chordIndex;
        int _arpPos;
        int _step;
        double _nextStepTime;
        double _target = 0.12;
        double _heat;
        bool _started;

        public Score(uint seed)
        {
            _rng = new Rng(seed);
            Intensity = 0.12;
            BarIntensity = 0.12;
            _progression = Progressions[(int)(_rng.Next() * Progressions.Length)];
            _arpShape = ArpShapes[(int)(_rng.Next() * ArpShapes.Length)];
        }

        /// <summary>Whether the score is playing; off, it asks for nothing.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Where the intensity has got to (smoothed toward what is wanted).</summary>
        public double Intensity { get; private set; }

        /// <summary>The intensity the arrangement of the current bar was chosen at.</summary>
        public double BarIntensity { get; private set; }

        public double Heat => _heat;

        public IReadOnlyList<string> Progression => _progression;

        public IReadOnlyList<int> ArpShape => _arpShape;

        public double StepDuration => _stepDuration;

        public double NextStepTime => _nextStepTime;

        static double Lerp(double a, double b, double t) => a + (b - a) * t;

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        static double FreqOf(double semi) => 55 * Math.Pow(2, semi / 12);

        /// <summary>The frequency of each pad oscillator before the first chord: the pad is six of them, a major third apart, and an octave up.</summary>
        public static double PadStartFrequency(int oscillator) => FreqOf(24 + oscillator * 4);

        /// <summary>Begin at time <paramref name="t0"/>; adds the pad's first chord to <paramref name="changes"/>.</summary>
        public void Start(double t0, List<ScoreChange> changes)
        {
            if (_started) return;
            _started = true;
            double t = t0 + 0.05;
            _nextStepTime = t + 0.1;
            _step = 0;
            ApplyChord(_nextStepTime, 0.4, changes);
        }

        /// <summary>Set the intensity wanted, 0 (explore) to 1 (full combat).</summary>
        public void SetIntensity(double v) { _target = Clamp(v, 0, 1); }

        /// <summary>Transient excitement (a hard landing, damage); decays over about fourteen seconds.</summary>
        public void Bump(double v = 0.35) { _heat = Clamp(_heat + v, 0, 1); }

        /// <summary>Smooth the intensity toward what is wanted; rises fast and falls slowly. Call once a frame.</summary>
        public void Tick(double dt)
        {
            _heat = Math.Max(0, _heat - dt / 14);
            double want = Clamp(Math.Max(_target, _heat), 0, 1);
            double rate = want > Intensity ? 0.55 : 0.16;
            Intensity += (want - Intensity) * Clamp(rate * dt, 0, 1);
        }

        Chord CurrentChord()
        {
            return Chords.TryGetValue(_progression[_chordIndex], out Chord c) ? c : Chords["i"];
        }

        /// <summary>Two stacked triads, each pad oscillator gliding to its new note.</summary>
        void ApplyChord(double t, double glide, List<ScoreChange> changes)
        {
            Chord c = CurrentChord();
            int[] voicing =
            {
                c.Intervals[0] + 24, c.Intervals[1] + 24, c.Intervals[2] + 24,
                c.Intervals[0] + 36, c.Intervals[2] + 36, c.Intervals[3] + 36,
            };
            for (int i = 0; i < voicing.Length; i++)
            {
                double f = FreqOf(c.Root + voicing[i]);
                changes.Add(new ScoreChange(ScoreChangeKind.PadGlide, f * (1 + (i % 2 == 1 ? 0.004 : -0.004)), t, glide, i));
            }
        }

        /// <summary>The notes and changes that fall in the next <paramref name="lookahead"/> seconds, added to the lists.</summary>
        public void Advance(double now, double lookahead, List<ScoreNote> notes, List<ScoreChange> changes)
        {
            if (!_started || !Enabled) return;
            if (_nextStepTime < now - 1.5) _nextStepTime = now + 0.05;
            int guard = 0;
            while (_nextStepTime < now + lookahead && guard++ < 64)
            {
                EmitStep(_step, _nextStepTime, notes, changes);
                _step++;
                _nextStepTime += _stepDuration;
            }
        }

        void EmitStep(int i, double t, List<ScoreNote> notes, List<ScoreChange> changes)
        {
            int bar = i / StepsPerBar;
            int s = i % StepsPerBar;

            if (s == 0)
            {
                // layer arming is quantised to the bar: that is what makes intensity changes sound like arrangement and not a fader move
                BarIntensity = Intensity;
                if (bar % 2 == 0)
                {
                    _chordIndex = (_chordIndex + (bar == 0 ? 0 : 1)) % _progression.Length;
                    ApplyChord(t, 0.18, changes);
                    if (_chordIndex == 0 && _rng.Next() < 0.3) _arpShape = ArpShapes[(int)(_rng.Next() * ArpShapes.Length)];
                }
            }

            double I = BarIntensity;
            Chord c = CurrentChord();
            double tt = t + (_rng.Next() - 0.5) * 0.006;

            // pad
            if (s == 0)
            {
                changes.Add(new ScoreChange(ScoreChangeKind.PadGain, Lerp(0.16, 0.10, I), t, 1.2));
                changes.Add(new ScoreChange(ScoreChangeKind.PadFilter, Lerp(420, 2800, I), t, 1.5));
                changes.Add(new ScoreChange(ScoreChangeKind.DelayWet, Lerp(0.34, 0.2, I), t, 1.2));
            }

            // bass
            int[] bp = BassPatterns[I > 0.72 ? 3 : I > 0.42 ? 2 : I > 0.18 ? 1 : 0];
            int bn = bp[s];
            if (bn >= 0)
            {
                double dur = _stepDuration * (I > 0.42 ? 1.7 : 3.4);
                notes.Add(new ScoreNote(tt, "mus.bass", 0.85 + _rng.Next() * 0.15, ScoreFeed.Duck, freq: FreqOf(c.Root + bn), dur: dur, cutoff: Lerp(420, 1500, I)));
            }

            // arpeggio
            bool arpOn = I > 0.22 && (I > 0.55 || s % 2 == 0);
            if (arpOn)
            {
                int[] shape = _arpShape;
                int deg = shape[_arpPos % shape.Length];
                _arpPos++;
                int oct = deg >= c.Intervals.Length ? 12 : 0;
                int semi = c.Root + c.Intervals[deg % c.Intervals.Length] + 36 + oct;
                double volume = (0.5 + I * 0.5) * (s % 4 == 0 ? 1 : 0.7) * (0.85 + _rng.Next() * 0.3);
                notes.Add(new ScoreNote(tt, "mus.arp", volume, ScoreFeed.Delay, freq: FreqOf(semi), dur: _stepDuration * 1.6));
            }

            // drums
            bool kick = I > 0.6 ? (s == 0 || s == 4 || s == 8 || s == 12) : I > 0.3 ? (s == 0 || s == 8 || s == 14) : (s == 0 || s == 8);
            if (kick && I > 0.08)
            {
                notes.Add(new ScoreNote(tt, "mus.kick", 0.8 + I * 0.35, ScoreFeed.Bus, pitch: 1));
                // sidechain pump
                changes.Add(new ScoreChange(ScoreChangeKind.DuckPump, Lerp(0.72, 0.4, I), tt, 0.075 + 0.05 * (1 - I)));
            }
            if (I > 0.48 && (s == 4 || s == 12))
            {
                notes.Add(new ScoreNote(tt, I > 0.75 ? "mus.snare" : "mus.clap", 0.55 + I * 0.4, ScoreFeed.Bus));
            }
            if (I > 0.35)
            {
                bool hatOn = I > 0.66 ? s % 2 == 0 : s % 4 == 2;
                if (hatOn && s != 14)
                {
                    double volume = (s % 4 == 0 ? 0.6 : 0.32) * (0.8 + _rng.Next() * 0.4);
                    notes.Add(new ScoreNote(tt, "mus.hat", volume, ScoreFeed.Bus, pitch: 0.95 + _rng.Next() * 0.15));
                }
                if (s == 14 && _rng.Next() < 0.6) notes.Add(new ScoreNote(tt, "mus.openhat", 0.34, ScoreFeed.Bus, pitch: 1));
            }
            if (I < 0.3 && _rng.Next() < 0.02)
            {
                notes.Add(new ScoreNote(tt, "mus.rim", 0.2 + _rng.Next() * 0.15, ScoreFeed.Bus));
            }

            // chord stab on the downbeat when things are hot
            if (I > 0.68 && s == 0 && bar % 2 == 1)
            {
                double volume = 0.5 + _rng.Next() * 0.3;
                var freqs = new double[3];
                for (int k = 0; k < 3; k++) freqs[k] = FreqOf(c.Root + c.Intervals[k] + 36);
                notes.Add(new ScoreNote(tt, "mus.stab", volume, ScoreFeed.Delay, dur: _stepDuration * 6, cutoff: Lerp(900, 2600, I), freqs: freqs));
            }
            if (I > 0.5 && s == 8 && _rng.Next() < 0.35)
            {
                int deg = c.Intervals[(int)(_rng.Next() * c.Intervals.Length)];
                double volume = 0.4 + _rng.Next() * 0.3;
                notes.Add(new ScoreNote(tt, "mus.lead", volume, ScoreFeed.Delay, freq: FreqOf(c.Root + deg + 48), dur: _stepDuration * (3 + (int)(_rng.Next() * 4))));
            }

            // riser into the top of the eight-bar cycle
            if (s == 12 && bar % 8 == 7 && I > 0.4)
            {
                notes.Add(new ScoreNote(tt, "mus.noise", 0.4 + I * 0.4, ScoreFeed.Delay));
            }
        }
    }
}
