using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>The guns' part of the bank: the prototype's Bank.js weapons and bullet impacts, ported line for line (random draws in the same order).</summary>
    public static partial class SoundBank
    {
        /// <summary>A gun's four-part anatomy (transient, crack, body, tail) as numbers: the prototype's <c>GUNS</c>.</summary>
        public sealed class GunProfile
        {
            public double Body, BodyQ, BodyD, Sub, SubTo, Tail, TailF, Crack, Level, Mech;
        }

        public static readonly IReadOnlyDictionary<string, GunProfile> Guns = new Dictionary<string, GunProfile>
        {
            ["pistol"] = new GunProfile { Body = 640, BodyQ = 3.2, BodyD = 0.075, Sub = 112, SubTo = 46, Tail = 0.45, TailF = 1500, Crack = 5200, Level = 1.00, Mech = 0.55 },
            ["revolver"] = new GunProfile { Body = 500, BodyQ = 3.6, BodyD = 0.095, Sub = 96, SubTo = 40, Tail = 0.62, TailF = 1250, Crack = 4600, Level = 1.15, Mech = 0.42 },
            ["smg"] = new GunProfile { Body = 800, BodyQ = 2.6, BodyD = 0.048, Sub = 132, SubTo = 62, Tail = 0.26, TailF = 1950, Crack = 6300, Level = 0.84, Mech = 0.65 },
            ["rifle"] = new GunProfile { Body = 540, BodyQ = 3.4, BodyD = 0.090, Sub = 94, SubTo = 40, Tail = 0.72, TailF = 1200, Crack = 7000, Level = 1.15, Mech = 0.60 },
            ["shotgun"] = new GunProfile { Body = 340, BodyQ = 2.0, BodyD = 0.160, Sub = 74, SubTo = 31, Tail = 0.88, TailF = 820, Crack = 3300, Level = 1.30, Mech = 0.75 },
            ["sniper"] = new GunProfile { Body = 430, BodyQ = 4.0, BodyD = 0.115, Sub = 82, SubTo = 34, Tail = 1.15, TailF = 950, Crack = 8200, Level = 1.35, Mech = 0.50 },
        };

        /// <summary>
        /// The prototype's fuzzy name to profile (<c>gunProfile</c>). It is given the gun's class here ("SMG // KINETIC", "POWER REVOLVER", "TECH RAILGUN"):
        /// the prototype passed its ids (wasp, magnat, voltline), which match nothing, so every gun played the pistol.
        /// </summary>
        public static string GunProfileFor(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(n, "plasma|laser|energy|ion|tech|smart|rail")) return "energy";
            foreach (string k in new[] { "shotgun", "sniper", "revolver", "rifle", "smg", "pistol" }) if (n.Contains(k)) return k;
            if (System.Text.RegularExpressions.Regex.IsMatch(n, "shot|gauge|boom")) return "shotgun";
            if (System.Text.RegularExpressions.Regex.IsMatch(n, @"ar\b|carbine|assault")) return "rifle";
            if (System.Text.RegularExpressions.Regex.IsMatch(n, "smg|sub|mac")) return "smg";
            return "pistol";
        }

        /// <summary>The prototype's <c>impactMaterial</c>: which impact sound a hit makes.</summary>
        public static string ImpactFor(ImpactSurface surface)
        {
            switch (surface)
            {
                case ImpactSurface.Metal: return "metal";
                case ImpactSurface.Flesh: return "flesh";
                default: return "concrete";   // asphalt and the rail's energy hits fall back to concrete there too
            }
        }

        static void RegisterWeapons()
        {
            foreach (KeyValuePair<string, GunProfile> kv in Guns)
            {
                GunProfile cfg = kv.Value;
                Def(new SoundDef { Name = "weapon." + kv.Key, Priority = 9, Gain = 0.95, Send = 0.55, Build = (g, p) => Gunshot(g, p, cfg) });
            }
            Def(new SoundDef { Name = "weapon.energy", Priority = 9, Gain = 0.9, Send = 0.5, Build = EnergyShot });

            Def(new SoundDef
            {
                Name = "weapon.crack", Priority = 8, Gain = 0.7, Send = 0.7,
                Build = (g, p) =>
                {
                    double t = p.T;
                    // an N-wave: two shocks a fraction of a millisecond apart
                    double e = SoundBuilders.Burst(g, g.Input, t, dur: 0.0022, peak: 1.0, type: BiquadType.Highpass, freq: 2600, attack: 0.00015);
                    e = Math.Max(e, SoundBuilders.Burst(g, g.Input, t + 0.0016, dur: 0.0035, peak: 0.55, type: BiquadType.Highpass, freq: 1500, attack: 0.0002));
                    e = Math.Max(e, SoundBuilders.Burst(g, g.Input, t + 0.004, dur: 0.10, peak: 0.22, freq: 4200, q: 1.4, sweepTo: 800, attack: 0.002, rate: 1 + p.Rng.Next() * 0.3));
                    return e;
                },
            });

            Def(new SoundDef
            {
                Name = "weapon.dryfire", Priority = 7, Gain = 0.7, Send = 0.2,
                Build = (g, p) => SoundBuilders.Mech(g, g.Input, p.T, freq: 3600, q: 10, dur: 0.02, peak: 0.6, ring: 0.05),
            });

            Def(new SoundDef
            {
                Name = "weapon.switch", Priority = 7, Gain = 0.7, Send = 0.2,
                Build = (g, p) =>
                {
                    double t = p.T;
                    double e = SoundBuilders.Cloth(g, g.Input, t, p.Rng, peak: 0.2, dur: 0.14);
                    e = Math.Max(e, SoundBuilders.Mech(g, g.Input, t + 0.06, freq: 1700, q: 6, dur: 0.05, peak: 0.4, ring: 0.06));
                    e = Math.Max(e, 0.14 + SoundBuilders.Mech(g, g.Input, t + 0.14, freq: 3000, q: 9, dur: 0.03, peak: 0.34, ring: 0.05));
                    return e;
                },
            });

            Def(new SoundDef { Name = "weapon.reload", Priority = 8, Gain = 0.8, Send = 0.25, Build = Reload });

            Def(new SoundDef
            {
                Name = "weapon.shell", Priority = 2, Gain = 0.5, Send = 0.3,
                Build = (g, p) =>
                {
                    double t = p.T;
                    Rng rng = p.Rng;
                    double e = 0;
                    int n = 2 + (int)(rng.Next() * 2);
                    for (int i = 0; i < n; i++)
                    {
                        double off = 0.28 + i * (0.06 + rng.Next() * 0.07);
                        double freq = 3200 + rng.Next() * 3600;
                        e = Math.Max(e, off + SoundBuilders.Resonate(g, g.Input, t + off, freq, q: 26, dur: 0.06 + rng.Next() * 0.05, peak: 0.22 / (1 + i)));
                        e = Math.Max(e, off + SoundBuilders.Burst(g, g.Input, t + off, dur: 0.01, peak: 0.14 / (1 + i), type: BiquadType.Highpass, freq: 5200));
                    }
                    return e;
                },
            });

            Def(new SoundDef { Name = "impact.concrete", Priority = 6, Gain = 0.8, Send = 0.4, Build = (g, p) => ImpactConcrete(g, g.Input, p.T, p.Rng) });
            Def(new SoundDef { Name = "impact.metal", Priority = 6, Gain = 0.8, Send = 0.4, Build = (g, p) => ImpactMetal(g, g.Input, p.T, p.Rng) });
            Def(new SoundDef { Name = "impact.flesh", Priority = 6, Gain = 0.8, Send = 0.4, Build = (g, p) => ImpactFlesh(g, g.Input, p.T, p.Rng) });
        }

        static double Gunshot(VoiceKit g, VoiceParams p, GunProfile cfg)
        {
            double t = p.T;
            Rng rng = p.Rng;
            double pitch = p.Pitch * (0.97 + rng.Next() * 0.06);
            SynthNode dest = g.Input;
            double lvl = cfg.Level, end = 0;

            // 1. transient: 3 ms of broadband, the reason a shot has snap
            end = Math.Max(end, SoundBuilders.Burst(g, dest, t, dur: 0.0045, peak: 0.95 * lvl, attack: 0.00025, type: BiquadType.Highpass, freq: 2200, q: 0.5));
            // 2. crack: the supersonic edge
            end = Math.Max(end, SoundBuilders.Burst(g, dest, t + 0.0008, dur: 0.022, peak: 0.72 * lvl, attack: 0.0004, freq: cfg.Crack * pitch, q: 0.7, sweepTo: cfg.Crack * 0.4 * pitch));
            // 3. body: saturated, so it distorts as a real recording clips
            {
                BufferSourceNode s = g.Src(NoiseKind.White, t, cfg.BodyD + 0.02);
                BiquadNode lp = g.Filter(BiquadType.Lowpass, cfg.Body * 2.4 * pitch, cfg.BodyQ);
                WaveShaperNode sh = g.Shaper(9);
                GainNode gn = g.Gain(0);
                g.Chain(s, lp, sh, gn).Connect(dest);
                g.Sweep(lp.Frequency, t, cfg.Body * 2.4 * pitch, cfg.Body * 0.5 * pitch, cfg.BodyD * 0.9);
                end = Math.Max(end, g.Ad(gn.Gain, t, 0.85 * lvl, 0.0006, cfg.BodyD));
            }
            // 4. sub: the chest punch
            end = Math.Max(end, SoundBuilders.Swoop(g, dest, t, cfg.Sub * pitch, cfg.SubTo * pitch, dur: cfg.BodyD * 2.2, peak: 0.80 * lvl, attack: 0.0012, bend: 0.35));
            // 5. tail: mostly reverb send, the part the city answers with
            {
                BufferSourceNode s = g.Src(NoiseKind.Pink, t + 0.006, cfg.Tail);
                BiquadNode lp = g.Filter(BiquadType.Lowpass, cfg.TailF, 0.9);
                BiquadNode hp = g.Filter(BiquadType.Highpass, 190, 0.7);
                GainNode gn = g.Gain(0);
                g.Chain(s, hp, lp, gn);
                GainNode dry = g.Gain(0.55), wet = g.Gain(1.35);
                gn.Connect(dry);
                dry.Connect(dest);
                gn.Connect(wet);
                wet.Connect(g.Send);
                g.Sweep(lp.Frequency, t + 0.006, cfg.TailF, cfg.TailF * 0.32, cfg.Tail * 0.8);
                end = Math.Max(end, 0.006 + g.Ad(gn.Gain, t + 0.006, 0.30 * lvl, 0.004, cfg.Tail));
            }
            // 6. mechanism: slide or bolt cycling a beat later
            if (cfg.Mech > 0)
            {
                double mt = t + 0.026 + rng.Next() * 0.012;
                end = Math.Max(end, 0.026 + SoundBuilders.Mech(g, dest, mt, freq: 3100 + rng.Next() * 900, q: 8, dur: 0.03, peak: 0.22 * cfg.Mech, ring: 0.05));
                end = Math.Max(end, 0.07 + SoundBuilders.Mech(g, dest, mt + 0.044, freq: 2100 + rng.Next() * 600, q: 6, dur: 0.035, peak: 0.16 * cfg.Mech, ring: 0.04));
            }
            return end + 0.05;
        }

        static double EnergyShot(VoiceKit g, VoiceParams p)
        {
            double t = p.T;
            Rng rng = p.Rng;
            SynthNode dest = g.Input;
            double pitch = p.Pitch * (0.96 + rng.Next() * 0.08), end = 0;

            // FM zap: the carrier dives and the modulator's index collapses, which reads as a discharge
            OscillatorNode car = g.Osc(WaveKind.Sine, t, 0.30, 1900 * pitch);
            OscillatorNode mod = g.Osc(WaveKind.Sine, t, 0.30, 1900 * 2.71 * pitch);
            GainNode modGain = g.Gain(2400);
            mod.Connect(modGain);
            modGain.Connect(car.Frequency);
            BiquadNode bp = g.Filter(BiquadType.Bandpass, 1500, 1.1);
            GainNode cg = g.Gain(0);
            g.Chain(car, bp, cg).Connect(dest);
            g.Sweep(car.Frequency, t, 1900 * pitch, 190 * pitch, 0.22);
            g.Sweep(modGain.Gain, t, 2400, 40, 0.16);
            g.Sweep(bp.Frequency, t, 4200, 700, 0.24);
            end = Math.Max(end, g.Ad(cg.Gain, t, 0.72, 0.0008, 0.28));

            end = Math.Max(end, SoundBuilders.Burst(g, dest, t, dur: 0.05, peak: 0.5, freq: 5200, q: 0.8, sweepTo: 1400, attack: 0.0004));
            end = Math.Max(end, SoundBuilders.Swoop(g, dest, t, 150, 52, dur: 0.22, peak: 0.55, attack: 0.002));

            // a sizzling capacitor tail, hard into the reverb
            BufferSourceNode s = g.Src(NoiseKind.White, t + 0.01, 0.5);
            BiquadNode f = g.Filter(BiquadType.Bandpass, 3200, 2.2);
            GainNode trem = g.Gain(0);
            OscillatorNode lfo = g.Osc(WaveKind.Square, t, 0.5, 34);
            GainNode lfoG = g.Gain(0.4);
            lfo.Connect(lfoG);
            lfoG.Connect(trem.Gain);
            GainNode tg = g.Gain(0);
            g.Chain(s, f, trem, tg);
            GainNode dry = g.Gain(0.6), wet = g.Gain(1.1);
            tg.Connect(dry);
            dry.Connect(dest);
            tg.Connect(wet);
            wet.Connect(g.Send);
            g.Sweep(f.Frequency, t + 0.01, 4600, 1100, 0.4);
            end = Math.Max(end, 0.01 + g.Ad(tg.Gain, t + 0.01, 0.26, 0.004, 0.46));
            return end + 0.05;
        }

        static double Reload(VoiceKit g, VoiceParams p)
        {
            double t = p.T;
            Rng rng = p.Rng;
            double J() => (rng.Next() - 0.5) * 0.02;
            SynthNode d = g.Input;
            double e = 0;
            void At(double off, Func<double, double> fn) => e = Math.Max(e, off + fn(t + off));

            // the magazine is released and slides out
            At(0, x => SoundBuilders.Mech(g, d, x, freq: 2700, q: 11, dur: 0.022, peak: 0.5, ring: 0.04));
            At(0.07 + J(), x => SoundBuilders.Burst(g, d, x, dur: 0.15, peak: 0.28, freq: 1900, q: 2.4, sweepTo: 700, attack: 0.004, kind: NoiseKind.Pink));
            At(0.19 + J(), x => SoundBuilders.Mech(g, d, x, freq: 1400, q: 7, dur: 0.04, peak: 0.24, ring: 0.05));
            // a fresh one goes in
            At(0.44 + J(), x => SoundBuilders.Burst(g, d, x, dur: 0.09, peak: 0.24, freq: 1500, q: 1.8, sweepTo: 2600, attack: 0.006, kind: NoiseKind.Pink));
            At(0.56 + J(), x =>
            {
                double a = SoundBuilders.Mech(g, d, x, freq: 900, q: 4, dur: 0.055, peak: 0.55, ring: 0.07);
                return Math.Max(a, SoundBuilders.Swoop(g, d, x, 150, 70, dur: 0.07, peak: 0.3));
            });
            At(0.68 + J(), x => SoundBuilders.Mech(g, d, x, freq: 2200, q: 9, dur: 0.025, peak: 0.3, ring: 0.05));
            // the slide or bolt: pulled, then let go
            At(0.92 + J(), x => SoundBuilders.Burst(g, d, x, dur: 0.08, peak: 0.3, freq: 2600, q: 3, sweepTo: 1300, attack: 0.003));
            At(1.03 + J(), x => SoundBuilders.Mech(g, d, x, freq: 3200, q: 10, dur: 0.035, peak: 0.62, ring: 0.09));
            At(1.06 + J(), x => SoundBuilders.Swoop(g, d, x, 210, 95, dur: 0.06, peak: 0.26));
            At(0.02, x => SoundBuilders.Cloth(g, d, x, rng, peak: 0.12, dur: 0.3));
            return e;
        }

        static double ImpactConcrete(VoiceKit g, SynthNode dest, double t, Rng rng)
        {
            double e = SoundBuilders.Burst(g, dest, t, dur: 0.006, peak: 0.8, type: BiquadType.Highpass, freq: 3000, attack: 0.0003);
            e = Math.Max(e, SoundBuilders.Burst(g, dest, t, dur: 0.065, peak: 0.62, freq: 900, q: 1.6, sweepTo: 300, attack: 0.0006));
            e = Math.Max(e, SoundBuilders.Swoop(g, dest, t, 130, 58, dur: 0.09, peak: 0.32, attack: 0.001));
            // dust and chips scattering
            e = Math.Max(e, SoundBuilders.Burst(g, dest, t + 0.01, kind: NoiseKind.Velvet, dur: 0.24, peak: 0.20, freq: 4200, q: 1.2, attack: 0.004, rate: 1.3));
            for (int i = 0; i < 3; i++)
            {
                double off = 0.05 + rng.Next() * 0.22;
                double peak = 0.09 * rng.Next(), freq = 1800 + rng.Next() * 2600;
                e = Math.Max(e, off + SoundBuilders.Burst(g, dest, t + off, dur: 0.02, peak: peak, freq: freq, q: 4));
            }
            return e;
        }

        static double ImpactMetal(VoiceKit g, SynthNode dest, double t, Rng rng)
        {
            double e = SoundBuilders.Burst(g, dest, t, dur: 0.005, peak: 0.9, type: BiquadType.Highpass, freq: 3800, attack: 0.0002);
            double b = 900 + rng.Next() * 700;
            foreach ((double m, double q, double d) in new[] { (1.0, 26.0, 0.34), (2.37, 30.0, 0.26), (4.1, 34.0, 0.18), (6.9, 30.0, 0.11) })
            {
                e = Math.Max(e, SoundBuilders.Resonate(g, dest, t, b * m, q: q, dur: d, peak: 0.3 / Math.Sqrt(m)));
            }
            e = Math.Max(e, SoundBuilders.Burst(g, dest, t, dur: 0.09, peak: 0.28, freq: 5200, q: 0.9, sweepTo: 2000, attack: 0.0006));
            if (rng.Next() < 0.55) e = Math.Max(e, Ricochet(g, dest, t + 0.012, rng) + 0.012);
            return e;
        }

        static double ImpactFlesh(VoiceKit g, SynthNode dest, double t, Rng rng)
        {
            double e = SoundBuilders.Burst(g, dest, t, dur: 0.05, peak: 0.7, type: BiquadType.Lowpass, freq: 420, q: 1.4, attack: 0.0008);
            e = Math.Max(e, SoundBuilders.Swoop(g, dest, t, 105, 46, dur: 0.11, peak: 0.5, attack: 0.001));
            e = Math.Max(e, SoundBuilders.Burst(g, dest, t + 0.006, dur: 0.13, peak: 0.30, freq: 1300, q: 5.5, sweepTo: 380, attack: 0.003, rate: 0.8 + rng.Next() * 0.4));
            return e;
        }

        /// <summary>The ricochet whine: a resonant band chasing a wandering sine.</summary>
        static double Ricochet(VoiceKit g, SynthNode dest, double t, Rng rng)
        {
            bool up = rng.Next() < 0.4;
            double f0 = up ? 900 + rng.Next() * 500 : 2400 + rng.Next() * 1800;
            double f1 = up ? 3000 + rng.Next() * 1500 : 620 + rng.Next() * 400;
            double dur = 0.28 + rng.Next() * 0.3;
            OscillatorNode o = g.Osc(WaveKind.Sine, t, dur, f0);
            OscillatorNode vib = g.Osc(WaveKind.Sine, t, dur, 5 + rng.Next() * 6);
            GainNode vibG = g.Gain(f0 * 0.05);
            vib.Connect(vibG);
            vibG.Connect(o.Frequency);
            BiquadNode bp = g.Filter(BiquadType.Bandpass, f0, 3);
            GainNode gn = g.Gain(0);
            g.Chain(o, bp, gn).Connect(dest);
            g.Sweep(o.Frequency, t, f0, f1, dur * 0.9);
            g.Sweep(bp.Frequency, t, f0, f1, dur * 0.9);
            return g.Ad(gn.Gain, t, 0.30, 0.006, dur);
        }
    }
}
