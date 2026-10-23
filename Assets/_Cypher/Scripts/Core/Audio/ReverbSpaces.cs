using System;

namespace Cypher.Core
{
    /// <summary>The settings of the mixer's reverb (Unity's SFX Reverb), in the units its parameters take.</summary>
    public readonly struct ReverbSettings
    {
        /// <summary>Overall level of the reflections and the reverb, millibels (hundredths of a decibel).</summary>
        public readonly double RoomMb;
        /// <summary>Extra attenuation of the highs, millibels (0 or negative).</summary>
        public readonly double RoomHfMb;
        /// <summary>Time for the tail to fall 60 dB, seconds.</summary>
        public readonly double DecayTime;
        /// <summary>How long the highs last compared with the middle: 1 the same, less than 1 shorter.</summary>
        public readonly double DecayHfRatio;
        public readonly double ReflectionsMb;
        public readonly double ReflectDelay;
        public readonly double LateMb;
        public readonly double LateDelay;
        public readonly double HfReference;

        public ReverbSettings(double roomMb, double roomHfMb, double decayTime, double decayHfRatio, double reflectionsMb, double reflectDelay, double lateMb, double lateDelay, double hfReference)
        {
            RoomMb = roomMb;
            RoomHfMb = roomHfMb;
            DecayTime = decayTime;
            DecayHfRatio = decayHfRatio;
            ReflectionsMb = reflectionsMb;
            ReflectDelay = reflectDelay;
            LateMb = lateMb;
            LateDelay = lateDelay;
            HfReference = hfReference;
        }

        public static ReverbSettings Lerp(ReverbSettings a, ReverbSettings b, double t)
        {
            double L(double x, double y) => x + (y - x) * t;
            return new ReverbSettings(L(a.RoomMb, b.RoomMb), L(a.RoomHfMb, b.RoomHfMb), L(a.DecayTime, b.DecayTime), L(a.DecayHfRatio, b.DecayHfRatio),
                L(a.ReflectionsMb, b.ReflectionsMb), L(a.ReflectDelay, b.ReflectDelay), L(a.LateMb, b.LateMb), L(a.LateDelay, b.LateDelay), L(a.HfReference, b.HfReference));
        }
    }

    /// <summary>
    /// The five places a sound can be in (the prototype's spaces) as settings of Unity's reverb. The prototype convolves with a made-up impulse response for each; Unity has no convolution, so
    /// the response's character is read off its recipe instead: how long it lasts, how fast the highs die, how soon it begins and how thickly the first reflections arrive. It will ring the
    /// same way (an alley rings, a plaza is long, indoors is dead) without sounding identical.
    /// </summary>
    public static class ReverbSpaces
    {
        /// <summary>The recipe of a space's impulse response in the prototype (Dsp.js REVERB_SPACES), the numbers the settings are worked out from.</summary>
        readonly struct Recipe
        {
            public readonly double Predelay, Decay, HfStart, HfEnd, Damping, Build, Taps, Spread;

            public Recipe(double predelay, double decay, double hfStart, double hfEnd, double damping, double build, double taps, double spread)
            {
                Predelay = predelay; Decay = decay; HfStart = hfStart; HfEnd = hfEnd; Damping = damping; Build = build; Taps = taps; Spread = spread;
            }
        }

        const double HfReference = 5000;
        const double ReferenceDecay = 1.75;

        static Recipe RecipeOf(ReverbSpace space)
        {
            switch (space)
            {
                case ReverbSpace.Open: return new Recipe(0.010, 0.85, 11000, 1800, 3.2, 0.02, 5, 0.10);
                case ReverbSpace.Street: return new Recipe(0.019, 1.75, 8200, 900, 1.7, 0.035, 14, 0.062);
                case ReverbSpace.Alley: return new Recipe(0.005, 1.25, 6800, 620, 1.15, 0.006, 24, 0.030);
                case ReverbSpace.Indoor: return new Recipe(0.004, 0.55, 5200, 430, 3.4, 0.004, 20, 0.017);
                case ReverbSpace.Plaza: return new Recipe(0.036, 2.95, 9600, 1450, 0.95, 0.095, 9, 0.16);
                default: throw new ArgumentOutOfRangeException(nameof(space), space, "unknown space");
            }
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>The reverb settings for a space.</summary>
        public static ReverbSettings For(ReverbSpace space)
        {
            Recipe r = RecipeOf(space);

            // the highs: in the made-up response the filter's cutoff slides from hfStart down to hfEnd, so the highs are gone once it passes the reference frequency
            double tHigh;
            if (r.HfStart <= HfReference) tHigh = 0.05;
            else if (r.HfEnd >= HfReference) tHigh = r.Decay;
            else tHigh = -Math.Log((HfReference - r.HfEnd) / (r.HfStart - r.HfEnd)) / r.Damping;
            double decayHf = Clamp(tHigh / r.Decay, 0.1, 1.0);

            // thick early reflections (many taps in a short spread) are what makes an alley ring and a closet boxy
            double density = r.Taps / r.Spread;
            double reflections = -2400 + 1800 * Clamp(Math.Log10(density / 50) / Math.Log10(1200.0 / 50), 0, 1);

            // the prototype scales every response to the same total energy, so a longer tail starts quieter: level the late reverb the same way
            double late = 1000 * Math.Log10(ReferenceDecay / r.Decay);

            return new ReverbSettings(
                roomMb: -1000,
                roomHfMb: -400,
                decayTime: r.Decay,
                decayHfRatio: decayHf,
                reflectionsMb: reflections,
                reflectDelay: Clamp(r.Predelay, 0, 0.3),
                lateMb: late,
                lateDelay: Clamp(r.Build, 0, 0.1),
                hfReference: HfReference);
        }
    }

    /// <summary>
    /// A change of space is a crossfade, not a cut: the reverb settles over about a second and a third into the new place. (The prototype fades one convolver down and another up.)
    /// </summary>
    public sealed class ReverbBlend
    {
        public const double FadeSeconds = 1.3;

        ReverbSettings _from, _to;
        double _progress = 1;

        public ReverbBlend(ReverbSpace start)
        {
            _from = _to = ReverbSpaces.For(start);
            Space = start;
        }

        public ReverbSpace Space { get; private set; }

        public bool Settled => _progress >= 1;

        /// <summary>Head for a new space, from wherever the reverb is now.</summary>
        public void MoveTo(ReverbSpace space, double fadeSeconds = FadeSeconds)
        {
            if (space == Space && Settled) return;
            _from = Current;
            _to = ReverbSpaces.For(space);
            Space = space;
            _progress = fadeSeconds <= 0 ? 1 : 0;
            _fadeSeconds = fadeSeconds;
        }

        double _fadeSeconds = FadeSeconds;

        public void Advance(double dt)
        {
            if (dt < 0) throw new ArgumentOutOfRangeException(nameof(dt), dt, "time does not run backward");
            if (_progress < 1) _progress = Math.Min(1, _progress + dt / _fadeSeconds);
        }

        /// <summary>The settings now.</summary>
        public ReverbSettings Current => ReverbSettings.Lerp(_from, _to, _progress);
    }
}
