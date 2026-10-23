using System;

namespace Cypher.Core
{
    /// <summary>What a sound is asked to be, besides which sound: the prototype's play() options and the per-call params a recipe reads.</summary>
    public sealed class VoiceParams
    {
        /// <summary>Absolute start time in the render (seconds).</summary>
        public double T;
        /// <summary>This voice's own random stream, drawn from the graph's at the moment of playing.</summary>
        public Rng Rng;
        public double Pitch = 1;
        public double Volume = 1;
        public double Distance;
        public double Occlusion;

        public bool Sprint, Crouch;
        /// <summary>How wet the ground is, 0 to 1 (also how hard it is raining on a car passing by). Unset, a footstep takes 0 and a car pass 0.8.</summary>
        public double? Wet;
        public string Surface;
        public double? Impact;
        public double Effort;
        public bool Inhale = true;
        public double? Freq, Dur, Cutoff;
        public double[] Freqs;

        public VoiceParams Clone() => (VoiceParams)MemberwiseClone();
    }

    /// <summary>
    /// The building blocks of the prototype's sound bank (Bank.js), ported line for line: a noise burst, a struck resonator, a pitch-swept sine, a mechanical click, cloth rustle, a splash and a footstep.
    /// Each draws random numbers in the same order as the original so a seed makes the same sound.
    /// </summary>
    public static class SoundBuilders
    {
        public static double Semi(double n) => Math.Pow(2, n / 12);

        /// <summary>Short filtered noise burst: the workhorse behind most of the bank.</summary>
        public static double Burst(VoiceKit g, SynthNode dest, double t, NoiseKind kind = NoiseKind.White, double dur = 0.05, double peak = 0.5, double attack = 0.0008,
            BiquadType type = BiquadType.Bandpass, double freq = 1200, double q = 1, double sweepTo = 0, double rate = 1, double hp = 0)
        {
            BufferSourceNode s = g.Src(kind, t, dur + attack, rate);
            BiquadNode f = g.Filter(type, freq, q);
            GainNode gn = g.Gain(0);
            SynthNode head = s;
            if (hp != 0) head = g.Chain(s, g.Filter(BiquadType.Highpass, hp, 0.7));
            g.Chain(head, f, gn).Connect(dest);
            if (sweepTo != 0) g.Sweep(f.Frequency, t, freq, sweepTo, dur);
            return g.Ad(gn.Gain, t, peak, attack, dur);
        }

        /// <summary>A struck resonator: a high-Q band around a frequency, excited by a tiny click.</summary>
        public static double Resonate(VoiceKit g, SynthNode dest, double t, double freq, double q = 20, double dur = 0.25, double peak = 0.3, double exciteDur = 0.004)
        {
            BufferSourceNode s = g.Src(NoiseKind.White, t, exciteDur);
            BiquadNode f = g.Filter(BiquadType.Bandpass, freq, q);
            GainNode gn = g.Gain(0);
            g.Chain(s, f, gn).Connect(dest);
            return g.Ad(gn.Gain, t, peak, 0.0004, dur);
        }

        /// <summary>Pitch-swept oscillator: thumps, sub booms, whines.</summary>
        public static double Swoop(VoiceKit g, SynthNode dest, double t, double f0, double f1, double dur = 0.2, double peak = 0.5, double attack = 0.002, WaveKind type = WaveKind.Sine, double bend = 0.5)
        {
            OscillatorNode o = g.Osc(type, t, dur, f0);
            GainNode gn = g.Gain(0);
            o.Connect(gn);
            gn.Connect(dest);
            g.Sweep(o.Frequency, t, f0, f1, dur * bend);
            return g.Ad(gn.Gain, t, peak, attack, dur);
        }

        /// <summary>Metallic mechanism click: a transient plus a short metal ring.</summary>
        public static double Mech(VoiceKit g, SynthNode dest, double t, double freq = 3000, double q = 9, double dur = 0.028, double peak = 0.5, double ring = 0)
        {
            double d = Burst(g, dest, t, dur: dur, peak: peak, freq: freq, q: q, attack: 0.0004);
            if (ring > 0)
            {
                d = Math.Max(d, Resonate(g, dest, t + 0.001, freq * 1.31, q: 26, dur: ring, peak: peak * 0.3));
                d = Math.Max(d, Resonate(g, dest, t + 0.001, freq * 2.14, q: 30, dur: ring * 0.7, peak: peak * 0.16));
            }
            return d;
        }

        /// <summary>Cloth or gear rustle: mid-band noise with a soft double-hump envelope.</summary>
        public static double Cloth(VoiceKit g, SynthNode dest, double t, Rng rng, double peak = 0.16, double dur = 0.16)
        {
            double end = 0;
            int n = 2 + (int)(rng.Next() * 2);
            for (int i = 0; i < n; i++)
            {
                double off = i * (0.028 + rng.Next() * 0.05);
                end = Math.Max(end, off + Burst(g, dest, t + off, kind: NoiseKind.Pink, dur: dur * (0.5 + rng.Next() * 0.7), peak: peak * (0.5 + rng.Next() * 0.6),
                    freq: 2000 + rng.Next() * 2600, q: 0.8, attack: 0.006, sweepTo: 900 + rng.Next() * 700));
            }
            return end;
        }

        /// <summary>How a surface sounds underfoot. <c>ThumpF</c> to <c>ThumpTo</c> is the heel's body, <c>Grit</c> the loose debris the sole drags, <c>Bright</c> the toe scuff.</summary>
        public readonly struct Surface
        {
            public readonly string Name;
            public readonly double ThumpF, ThumpTo, Body, BodyQ, Grit, GritF, Bright, Ring;

            public Surface(string name, double thumpF, double thumpTo, double body, double bodyQ, double grit, double gritF, double bright, double ring)
            {
                Name = name; ThumpF = thumpF; ThumpTo = thumpTo; Body = body; BodyQ = bodyQ; Grit = grit; GritF = gritF; Bright = bright; Ring = ring;
            }
        }

        public static readonly Surface[] Surfaces =
        {
            new Surface("concrete", 96, 52, 430, 1.4, 0.30, 2900, 0.55, 0),
            new Surface("asphalt", 82, 44, 330, 1.1, 0.22, 2100, 0.38, 0),
            new Surface("metal", 128, 74, 700, 2.2, 0.18, 4200, 0.80, 0.34),
            new Surface("wood", 110, 62, 480, 5.0, 0.12, 2400, 0.42, 0.16),
            new Surface("gravel", 74, 42, 300, 0.9, 0.85, 3600, 0.30, 0),
            new Surface("carpet", 68, 40, 240, 0.8, 0.06, 1200, 0.10, 0),
            new Surface("water", 88, 46, 520, 1.0, 0.10, 2000, 0.35, 0),
        };

        public static Surface SurfaceNamed(string name)
        {
            foreach (Surface s in Surfaces)
            {
                if (s.Name == name) return s;
            }
            return Surfaces[0];
        }

        public static double Splash(VoiceKit g, SynthNode dest, double t, Rng rng, double amount = 1)
        {
            double end = Burst(g, dest, t, dur: 0.13 * (0.8 + rng.Next() * 0.5), peak: 0.42 * amount, attack: 0.003, freq: 700, sweepTo: 2900, q: 1.1);
            end = Math.Max(end, Swoop(g, dest, t, 190, 88, dur: 0.07, peak: 0.20 * amount));
            // droplets falling back down sell it more than the impact itself
            int drops = 2 + (int)(rng.Next() * 4);
            for (int i = 0; i < drops; i++)
            {
                double off = 0.05 + rng.Next() * 0.24;
                end = Math.Max(end, off + Burst(g, dest, t + off, dur: 0.012, peak: 0.11 * amount * (0.4 + rng.Next()), freq: 2600 + rng.Next() * 3400, q: 6, attack: 0.0006));
            }
            return end;
        }

        public static double Footstep(VoiceKit g, VoiceParams p, string surfaceName)
        {
            Surface s = SurfaceNamed(surfaceName);
            Rng rng = p.Rng;
            double t = p.T;
            bool sprint = p.Sprint;
            bool crouch = p.Crouch;
            double wet = Math.Min(1, p.Wet ?? 0);
            // weight and brightness separate a sprint from a crouch-walk; the pitch jitter stops twenty steps sounding like one sample
            double level = crouch ? 0.34 : sprint ? 1.25 : 0.85;
            double pitch = p.Pitch * (crouch ? 1.1 : sprint ? 0.94 : 1) * (0.9 + rng.Next() * 0.2);
            SynthNode dest = g.Input;

            double end = 0;
            // heel
            end = Math.Max(end, Swoop(g, dest, t, s.ThumpF * pitch, s.ThumpTo * pitch,
                dur: (crouch ? 0.05 : 0.085) * (0.8 + rng.Next() * 0.4), peak: 0.62 * level, attack: 0.0015));
            end = Math.Max(end, Burst(g, dest, t, dur: 0.045 * (0.7 + rng.Next() * 0.7), peak: 0.55 * level, attack: 0.0007,
                freq: s.Body * pitch * (0.85 + rng.Next() * 0.3), q: s.BodyQ, sweepTo: s.Body * 0.55 * pitch));
            end = Math.Max(end, Burst(g, dest, t, dur: 0.012, peak: 0.30 * level * s.Bright, attack: 0.0003,
                type: BiquadType.Highpass, freq: 3400 + rng.Next() * 1800, q: 0.6));
            if (s.Grit > 0.05)
            {
                end = Math.Max(end, Burst(g, dest, t + 0.004, kind: NoiseKind.Velvet, dur: 0.075 * (0.6 + rng.Next()), peak: 0.30 * level * s.Grit,
                    freq: s.GritF * (0.8 + rng.Next() * 0.4), q: 1.0, attack: 0.002, rate: 0.7 + rng.Next() * 0.8));
            }
            if (s.Ring > 0)
            {
                double[][] partials = { new[] { 1.0, 22 }, new[] { 2.41, 26 }, new[] { 4.07, 30 } };
                foreach (double[] partial in partials)
                {
                    end = Math.Max(end, Resonate(g, dest, t + 0.002, s.Body * partial[0] * pitch, q: partial[1], dur: 0.22 * s.Ring * 3, peak: 0.16 * level * s.Ring));
                }
            }
            // toe, a beat behind the heel: what reads as a step and not a hit
            double toe = 0.022 + rng.Next() * 0.026;
            end = Math.Max(end, toe + Burst(g, dest, t + toe, dur: 0.03 * (0.7 + rng.Next() * 0.6), peak: 0.28 * level, attack: 0.001,
                freq: s.Body * 1.6 * pitch, q: s.BodyQ * 0.8, sweepTo: s.Body * 0.9 * pitch));

            if (surfaceName == "water" || wet > 0.15)
            {
                end = Math.Max(end, Splash(g, dest, t + 0.002, rng, surfaceName == "water" ? level : wet * 0.5 * level));
            }
            if (!crouch) end = Math.Max(end, Cloth(g, dest, t + 0.01, rng, peak: 0.05 * level, dur: 0.12));
            return end;
        }
    }
}
