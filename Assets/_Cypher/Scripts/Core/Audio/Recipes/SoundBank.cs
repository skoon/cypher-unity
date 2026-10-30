using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>A sound in the bank: where it plays, how loud, how much it sends to the reverb, and how it is made.</summary>
    public sealed class SoundDef
    {
        public string Name;
        public AudioBus Bus = AudioBus.Sfx;
        /// <summary>Whether it has a place in the world; a menu click and the score do not.</summary>
        public bool Spatial = true;
        public int Priority = 5;
        public double Gain = 1;
        /// <summary>How much goes to the reverb (the prototype's 0 to 3 scale).</summary>
        public double Send = 0.22;
        /// <summary>Distance at which the sound begins to fall off, and how fast (Web Audio's inverse model).</summary>
        public double RefDistance = 2.2;
        public double Rolloff = 1.05;
        /// <summary>Builds the sound; returns its length in seconds.</summary>
        public Func<VoiceKit, VoiceParams, double> Build;
    }

    /// <summary>A sound made from a recipe: mono samples at the graph's sample rate, ending where the sound does.</summary>
    public sealed class RenderedSound
    {
        public SoundDef Def;
        public float[] Samples;
        public int SampleRate;
        /// <summary>The length the recipe reported, in seconds.</summary>
        public double Seconds;
    }

    /// <summary>
    /// The prototype's sound bank for what this game plays: the menu sounds, the city's ambient one-shots, the player's footsteps and foley, the score's instruments, and the guns and their bullet impacts. Every sound is made
    /// from noise and oscillators; given a seed the same sound comes out every time, and each seed is another take of it.
    /// </summary>
    public static partial class SoundBank
    {
        const double UiRoot = 233.08;

        static readonly Dictionary<string, SoundDef> Defs = new Dictionary<string, SoundDef>();
        static readonly List<string> Order = new List<string>();

        static SoundBank()
        {
            RegisterFoley();
            RegisterUi();
            RegisterAmbient();
            RegisterMusic();
            RegisterWeapons();
        }

        public static IReadOnlyList<string> Names => Order;

        public static bool Has(string name) => Defs.ContainsKey(name);

        public static SoundDef Get(string name)
        {
            if (!Defs.TryGetValue(name, out SoundDef def)) throw new ArgumentException($"there is no sound called '{name}'", nameof(name));
            return def;
        }

        static void Def(SoundDef def)
        {
            Defs.Add(def.Name, def);
            Order.Add(def.Name);
        }

        /// <summary>
        /// Makes one take of a sound, as the prototype's graph plays one: the seed starts the graph's stream, one draw of which seeds the voice's own, and the recipe draws from both.
        /// </summary>
        /// <param name="options">Per-call settings (a footstep's surface and speed, a breath's effort); null for none.</param>
        /// <param name="startTime">Where in the render the sound begins; the clip is as long as the sound.</param>
        public static RenderedSound Render(string name, AudioNoiseBank noise, uint seed, VoiceParams options = null, double startTime = 0)
        {
            SoundDef def = Get(name);
            var graph = new SynthGraph(noise.SampleRate);
            var graphRng = new Rng(seed);
            var kit = new VoiceKit(graph, noise, graphRng);
            VoiceParams p = options ?? new VoiceParams();
            p.T = startTime;
            p.Rng = new Rng((uint)(graphRng.Next() * 4294967296.0));
            double occlusion = Math.Min(1, Math.Max(0, p.Occlusion));
            kit.Input.Gain.SetValueAtTime(def.Gain * p.Volume * (1 + (0.4 - 1) * occlusion), startTime);

            double dur = def.Build(kit, p);
            if (dur == 0) dur = 0.25;
            int frames = (int)Math.Ceiling((startTime + dur + 0.06) * noise.SampleRate);
            return new RenderedSound { Def = def, Samples = graph.RenderMono(frames), SampleRate = noise.SampleRate, Seconds = dur };
        }

        // ------------------------------------------------------------------ foley

        static void RegisterFoley()
        {
            foreach (SoundBuilders.Surface surface in SoundBuilders.Surfaces)
            {
                string surfaceName = surface.Name;
                Def(new SoundDef { Name = "foot." + surfaceName, Priority = 6, Gain = 0.55, Send = 0.30, Build = (g, p) => SoundBuilders.Footstep(g, p, surfaceName) });
            }

            Def(new SoundDef
            {
                Name = "player.jump", Priority = 6, Gain = 0.6, Send = 0.25,
                Build = (g, p) =>
                {
                    double t = p.T;
                    Rng rng = p.Rng;
                    double e = SoundBuilders.Cloth(g, g.Input, t, rng, peak: 0.34, dur: 0.2);
                    e = Math.Max(e, SoundBuilders.Burst(g, g.Input, t, dur: 0.06, peak: 0.4, freq: 620, q: 1.2, sweepTo: 1500, attack: 0.004));
                    // effort breath
                    e = Math.Max(e, SoundBuilders.Burst(g, g.Input, t + 0.01, kind: NoiseKind.Pink, dur: 0.18, peak: 0.22, freq: 700, q: 1.4, sweepTo: 380, attack: 0.02));
                    return e;
                },
            });

            Def(new SoundDef
            {
                Name = "player.land", Priority = 7, Gain = 0.9, Send = 0.32,
                Build = (g, p) =>
                {
                    double t = p.T;
                    Rng rng = p.Rng;
                    double i = Math.Min(1.4, p.Impact ?? 0.6);
                    VoiceParams step = p.Clone();
                    step.Sprint = i > 0.5;
                    step.Crouch = false;
                    step.Pitch = p.Pitch * (1 - i * 0.12);
                    double e = SoundBuilders.Footstep(g, step, p.Surface ?? "concrete");
                    e = Math.Max(e, SoundBuilders.Swoop(g, g.Input, t, 76 - i * 16, 34, dur: 0.16 + i * 0.12, peak: 0.75 * i, attack: 0.0018));
                    e = Math.Max(e, SoundBuilders.Cloth(g, g.Input, t + 0.004, rng, peak: 0.2 * i, dur: 0.22));
                    // gear rattle on a hard landing
                    if (i > 0.45)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            double off = 0.03 + rng.Next() * 0.13;
                            e = Math.Max(e, off + SoundBuilders.Mech(g, g.Input, t + off, freq: 2400 + rng.Next() * 2000, q: 10, dur: 0.02, peak: 0.10 * i, ring: 0.03));
                        }
                    }
                    return e;
                },
            });

            Def(new SoundDef { Name = "player.cloth", Priority = 2, Gain = 0.5, Send = 0.15, Build = (g, p) => SoundBuilders.Cloth(g, g.Input, p.T, p.Rng, peak: 0.3, dur: 0.18) });

            // breathing is one shot per breath; the caller schedules them back to back so rate and depth follow exertion
            Def(new SoundDef
            {
                Name = "player.breath", Priority = 2, Gain = 0.5, Send = 0.08,
                Build = (g, p) =>
                {
                    double t = p.T;
                    Rng rng = p.Rng;
                    bool inhale = p.Inhale;
                    double eff = p.Effort;
                    double dur = (inhale ? 0.42 : 0.5) * (1 - eff * 0.42);
                    double f0 = inhale ? 430 : 620;
                    double f1 = inhale ? 900 : 320;
                    BufferSourceNode s = g.Src(NoiseKind.Pink, t, dur, 0.9 + rng.Next() * 0.2);
                    BiquadNode bp = g.Filter(BiquadType.Bandpass, f0, 1.1 + eff * 0.9);
                    BiquadNode hp = g.Filter(BiquadType.Highpass, 220, 0.7);
                    GainNode gn = g.Gain(0);
                    g.Chain(s, hp, bp, gn).Connect(g.Input);
                    g.Sweep(bp.Frequency, t, f0, f1, dur * 0.85);
                    // a hint of voiced rasp once the player is really working
                    if (eff > 0.55)
                    {
                        OscillatorNode o = g.Osc(WaveKind.Sine, t, dur, (inhale ? 118 : 96) * (0.9 + rng.Next() * 0.2));
                        GainNode og = g.Gain(0);
                        o.Connect(og);
                        og.Connect(g.Input);
                        g.Ahr(og.Gain, t, 0.06 * (eff - 0.5) * 2, dur * 0.3, dur * 0.2, dur * 0.5);
                    }
                    return g.Ahr(gn.Gain, t, 0.16 + eff * 0.5, dur * 0.35, dur * 0.15, dur * 0.5);
                },
            });
        }

        // ------------------------------------------------------------------ the interface

        /// <summary>The shared colour of the menu sounds: a short feedback delay plus a presence lift, which makes them read as one set.</summary>
        static GainNode UiOut(VoiceKit g, double mix)
        {
            GainNode pre = g.Gain(1);
            BiquadNode tilt = g.Filter(BiquadType.Peaking, 2600, 1.1, 3);
            DelayNode d = g.Delay(0.063);
            GainNode fb = g.Gain(0.34);
            BiquadNode damp = g.Filter(BiquadType.Lowpass, 4200, 0.7);
            GainNode wet = g.Gain(mix);
            g.Chain(pre, tilt);
            tilt.Connect(g.Input);
            tilt.Connect(d);
            g.Chain(d, damp, fb).Connect(d);
            d.Connect(wet);
            wet.Connect(g.Input);
            return pre;
        }

        static double UiTone(VoiceKit g, SynthNode dest, double t, double semi, double dur = 0.18, double peak = 0.4, WaveKind wave = WaveKind.Bell, double detune = 0, double attack = 0.002)
        {
            double f = UiRoot * SoundBuilders.Semi(semi) * (1 + detune);
            OscillatorNode o = g.Osc(wave, t, dur, f);
            GainNode gn = g.Gain(0);
            BiquadNode lp = g.Filter(BiquadType.Lowpass, Math.Min(15000, f * 9), 0.8);
            g.Chain(o, lp, gn).Connect(dest);
            return g.Ad(gn.Gain, t, peak, attack, dur);
        }

        static double UiTick(VoiceKit g, SynthNode dest, double t, double peak = 0.16)
        {
            return SoundBuilders.Burst(g, dest, t, dur: 0.008, peak: peak, type: BiquadType.Highpass, freq: 4800, attack: 0.0004);
        }

        static void Ui(string name, double gain, Func<VoiceKit, VoiceParams, double> build)
        {
            Def(new SoundDef { Name = name, Bus = AudioBus.Ui, Spatial = false, Priority = 9, Gain = gain, Send = 0.05, Build = build });
        }

        static void RegisterUi()
        {
            Ui("ui.hover", 0.5, (g, p) =>
            {
                GainNode o = UiOut(g, 0.18);
                double e = Math.Max(UiTone(g, o, p.T, 22, dur: 0.06, peak: 0.22, attack: 0.001), UiTick(g, o, p.T, 0.08));
                return e + 0.3;
            });
            Ui("ui.tick", 0.45, (g, p) =>
            {
                GainNode o = UiOut(g, 0.12);
                return Math.Max(UiTone(g, o, p.T, 19, dur: 0.035, peak: 0.24, attack: 0.0008), UiTick(g, o, p.T, 0.1)) + 0.25;
            });
            Ui("ui.select", 0.6, (g, p) =>
            {
                GainNode o = UiOut(g, 0.22);
                double e = UiTone(g, o, p.T, 12, dur: 0.09, peak: 0.34, attack: 0.001);
                e = Math.Max(e, UiTick(g, o, p.T, 0.16));
                return e + 0.35;
            });
            Ui("ui.confirm", 0.75, (g, p) =>
            {
                GainNode o = UiOut(g, 0.3);
                double e = UiTone(g, o, p.T, 12, dur: 0.16, peak: 0.34);
                e = Math.Max(e, 0.055 + UiTone(g, o, p.T + 0.055, 19, dur: 0.34, peak: 0.30));
                e = Math.Max(e, 0.055 + UiTone(g, o, p.T + 0.055, 24, dur: 0.3, peak: 0.13, detune: 0.001));
                e = Math.Max(e, UiTick(g, o, p.T, 0.14));
                return e + 0.45;
            });
            Ui("ui.back", 0.65, (g, p) =>
            {
                GainNode o = UiOut(g, 0.26);
                double e = UiTone(g, o, p.T, 19, dur: 0.1, peak: 0.28);
                e = Math.Max(e, 0.05 + UiTone(g, o, p.T + 0.05, 10, dur: 0.26, peak: 0.3));
                e = Math.Max(e, UiTick(g, o, p.T, 0.1));
                return e + 0.4;
            });
            Ui("ui.open", 0.85, (g, p) =>
            {
                GainNode o = UiOut(g, 0.32);
                double t = p.T;
                double e = SoundBuilders.Burst(g, o, t, kind: NoiseKind.Pink, dur: 0.3, peak: 0.24, freq: 320, q: 1.6, sweepTo: 5200, attack: 0.02);
                e = Math.Max(e, SoundBuilders.Swoop(g, o, t, 68, 132, dur: 0.26, peak: 0.4, attack: 0.01));
                int[] steps = { 0, 7, 15 };
                for (int i = 0; i < steps.Length; i++)
                {
                    e = Math.Max(e, 0.12 + i * 0.03 + UiTone(g, o, t + 0.12 + i * 0.03, steps[i], dur: 0.5, peak: 0.2));
                }
                return e + 0.5;
            });
            Ui("ui.close", 0.8, (g, p) =>
            {
                GainNode o = UiOut(g, 0.28);
                double t = p.T;
                double e = SoundBuilders.Burst(g, o, t, kind: NoiseKind.Pink, dur: 0.22, peak: 0.2, freq: 4600, q: 1.6, sweepTo: 300, attack: 0.006);
                int[] steps = { 15, 7, 0 };
                for (int i = 0; i < steps.Length; i++)
                {
                    e = Math.Max(e, 0.02 + i * 0.03 + UiTone(g, o, t + 0.02 + i * 0.03, steps[i], dur: 0.3, peak: 0.2));
                }
                e = Math.Max(e, SoundBuilders.Swoop(g, o, t, 120, 54, dur: 0.2, peak: 0.3, attack: 0.004));
                return e + 0.45;
            });
            Ui("ui.error", 0.85, (g, p) =>
            {
                GainNode o = UiOut(g, 0.2);
                double t = p.T;
                double e = 0;
                // a tritone, gated three times: "denied" in any language
                for (int i = 0; i < 3; i++)
                {
                    double off = i * 0.085;
                    foreach (int s in new[] { 3, 9 })
                    {
                        e = Math.Max(e, off + UiTone(g, o, t + off, s, dur: 0.055, peak: 0.26, wave: WaveKind.Hollow, attack: 0.001));
                    }
                }
                e = Math.Max(e, SoundBuilders.Burst(g, o, t, dur: 0.24, peak: 0.14, freq: 220, q: 1.0, sweepTo: 110, attack: 0.002));
                return e + 0.4;
            });
            Ui("ui.alert", 0.9, (g, p) =>
            {
                GainNode o = UiOut(g, 0.24);
                double t = p.T;
                double e = 0;
                for (int i = 0; i < 2; i++)
                {
                    double off = i * 0.19;
                    e = Math.Max(e, off + UiTone(g, o, t + off, 3, dur: 0.16, peak: 0.3, wave: WaveKind.Hollow));
                    e = Math.Max(e, off + UiTone(g, o, t + off, 4, dur: 0.16, peak: 0.18, wave: WaveKind.Hollow));
                }
                e = Math.Max(e, SoundBuilders.Swoop(g, o, t, 90, 62, dur: 0.4, peak: 0.3, attack: 0.006));
                return e + 0.45;
            });
            Ui("ui.notify", 0.7, (g, p) =>
            {
                GainNode o = UiOut(g, 0.34);
                double t = p.T;
                double e = UiTick(g, o, t, 0.12);
                int[] steps = { 12, 15, 22 };
                for (int i = 0; i < steps.Length; i++)
                {
                    e = Math.Max(e, i * 0.075 + UiTone(g, o, t + i * 0.075, steps[i], dur: 0.28 + i * 0.1, peak: 0.24));
                }
                return e + 0.5;
            });
        }

        // ------------------------------------------------------------------ the city's ambient one-shots

        static void RegisterAmbient()
        {
            Def(new SoundDef
            {
                Name = "amb.siren", Bus = AudioBus.Ambience, Priority = 4, Gain = 0.5, Send = 0.85, RefDistance = 45, Rolloff = 0.8,
                Build = (g, p) =>
                {
                    double t = p.T;
                    Rng rng = p.Rng;
                    double dur = 4.5 + rng.Next() * 3;
                    double baseFreq = 620 + rng.Next() * 220;
                    OscillatorNode o = g.Osc(WaveKind.SoftSaw, t, dur, baseFreq);
                    // wail: a slow triangle on pitch, plus a drift over the whole pass
                    OscillatorNode lfo = g.Osc(WaveKind.Triangle, t, dur, 0.34 + rng.Next() * 0.2);
                    GainNode lfoG = g.Gain(baseFreq * 0.42);
                    lfo.Connect(lfoG);
                    lfoG.Connect(o.Frequency);
                    BiquadNode bp = g.Filter(BiquadType.Bandpass, baseFreq * 1.4, 1.6);
                    BiquadNode lp = g.Filter(BiquadType.Lowpass, 1500, 0.8);
                    GainNode gn = g.Gain(0);
                    g.Chain(o, bp, lp, gn).Connect(g.Input);
                    g.Sweep(o.Frequency, t, baseFreq, baseFreq * (0.86 + rng.Next() * 0.3), dur);
                    return g.Ahr(gn.Gain, t, 0.5, dur * 0.35, dur * 0.15, dur * 0.5);
                },
            });

            Def(new SoundDef
            {
                Name = "amb.rumbleimpact", Bus = AudioBus.Ambience, Priority = 4, Gain = 0.7, Send = 0.9, RefDistance = 40, Rolloff = 0.75,
                Build = (g, p) =>
                {
                    double t = p.T;
                    Rng rng = p.Rng;
                    double dur = 1.6 + rng.Next() * 1.4;
                    double e = SoundBuilders.Burst(g, g.Input, t, kind: NoiseKind.Brown, dur: dur, peak: 0.62, type: BiquadType.Lowpass, freq: 150, q: 1.4,
                        sweepTo: 45, attack: 0.05, rate: 0.6 + rng.Next() * 0.4);
                    e = Math.Max(e, SoundBuilders.Burst(g, g.Input, t, dur: 0.2, peak: 0.22, freq: 430, q: 1.0, sweepTo: 120, attack: 0.004));
                    e = Math.Max(e, SoundBuilders.Swoop(g, g.Input, t, 52, 28, dur: dur * 0.8, peak: 0.35, attack: 0.03));
                    return e;
                },
            });

            Def(new SoundDef
            {
                Name = "amb.drip", Bus = AudioBus.Ambience, Priority = 2, Gain = 0.45, Send = 0.7,
                Build = (g, p) =>
                {
                    double t = p.T;
                    Rng rng = p.Rng;
                    double f = 1100 + rng.Next() * 1600;
                    double e = SoundBuilders.Swoop(g, g.Input, t, f, f * 2.1, dur: 0.05 + rng.Next() * 0.04, peak: 0.4, attack: 0.0008, bend: 0.7);
                    e = Math.Max(e, SoundBuilders.Burst(g, g.Input, t, dur: 0.008, peak: 0.16, type: BiquadType.Highpass, freq: 3600));
                    return e;
                },
            });

            // indistinct distant speech: three formants over a noise excitation
            Def(new SoundDef
            {
                Name = "amb.voice", Bus = AudioBus.Ambience, Priority = 2, Gain = 0.5, Send = 0.6, RefDistance = 5, Rolloff = 1.0,
                Build = (g, p) =>
                {
                    double t = p.T;
                    Rng rng = p.Rng;
                    double dur = 0.35 + rng.Next() * 0.7;
                    bool male = rng.Next() < 0.55;
                    double f0 = (male ? 105 : 190) * (0.85 + rng.Next() * 0.3);
                    OscillatorNode src = g.Osc(WaveKind.SoftSaw, t, dur, f0);
                    BufferSourceNode noise = g.Src(NoiseKind.Pink, t, dur, 0.8 + rng.Next() * 0.4);
                    GainNode mix = g.Gain(1);
                    src.Connect(mix);
                    GainNode ng = g.Gain(0.22);
                    noise.Connect(ng);
                    ng.Connect(mix);
                    double[][] formants = { new[] { 520.0, 7 }, new[] { 1320.0, 9 }, new[] { 2600.0, 11 } };
                    GainNode sum = g.Gain(0);
                    foreach (double[] formant in formants)
                    {
                        double ff = formant[0], q = formant[1];
                        BiquadNode bp = g.Filter(BiquadType.Bandpass, ff * (male ? 0.9 : 1.15) * (0.85 + rng.Next() * 0.3), q);
                        GainNode bg = g.Gain(1 / Math.Pow(q, 0.5));
                        mix.Connect(bp);
                        bp.Connect(bg);
                        bg.Connect(sum);
                        // vowel movement
                        g.Sweep(bp.Frequency, t, bp.Frequency.Value, bp.Frequency.Value * (0.7 + rng.Next() * 0.7), dur * 0.8);
                    }
                    BiquadNode lp = g.Filter(BiquadType.Lowpass, 2600, 0.8);
                    g.Chain(sum, lp).Connect(g.Input);
                    g.Sweep(src.Frequency, t, f0, f0 * (0.8 + rng.Next() * 0.45), dur);
                    // syllabic amplitude, not a flat blob
                    int syl = 2 + (int)(rng.Next() * 3);
                    double e = 0;
                    for (int i = 0; i < syl; i++)
                    {
                        double off = i * dur / syl;
                        double w = dur / syl * 0.85;
                        e = Math.Max(e, off + g.Ahr(sum.Gain, t + off, 0.5 + rng.Next() * 0.5, w * 0.25, w * 0.35, w * 0.4));
                    }
                    return Math.Max(e, dur);
                },
            });

            Def(new SoundDef
            {
                Name = "amb.neonbuzz", Bus = AudioBus.Ambience, Priority = 2, Gain = 0.4, Send = 0.4,
                Build = (g, p) =>
                {
                    double t = p.T;
                    Rng rng = p.Rng;
                    double dur = 0.25 + rng.Next() * 0.6;
                    OscillatorNode o = g.Osc(WaveKind.Hollow, t, dur, 118 + rng.Next() * 6);
                    BiquadNode hp = g.Filter(BiquadType.Highpass, 900, 0.8);
                    BiquadNode bp = g.Filter(BiquadType.Bandpass, 2400 + rng.Next() * 1800, 3);
                    GainNode gate = g.Gain(0);
                    g.Chain(o, hp, bp, gate).Connect(g.Input);
                    // stutter gate: a dying tube
                    double e = 0;
                    double x = 0;
                    while (x < dur)
                    {
                        double on = 0.01 + rng.Next() * 0.05;
                        g.Ahr(gate.Gain, t + x, 0.35 + rng.Next() * 0.4, 0.002, on, 0.006);
                        x += on + 0.01 + rng.Next() * 0.07;
                        e = x;
                    }
                    e = Math.Max(e, SoundBuilders.Burst(g, g.Input, t, dur: 0.01, peak: 0.2, type: BiquadType.Highpass, freq: 5200));
                    return Math.Max(e, dur);
                },
            });

            Def(new SoundDef
            {
                Name = "amb.carpass", Bus = AudioBus.Ambience, Priority = 3, Gain = 0.6, Send = 0.5, RefDistance = 12, Rolloff = 0.9,
                Build = (g, p) =>
                {
                    double t = p.T;
                    Rng rng = p.Rng;
                    double dur = 2.2 + rng.Next() * 1.6;
                    // engine
                    OscillatorNode o = g.Osc(WaveKind.SoftSaw, t, dur, 78 + rng.Next() * 34);
                    BiquadNode lp = g.Filter(BiquadType.Lowpass, 420, 2.2);
                    GainNode og = g.Gain(0);
                    g.Chain(o, lp, og).Connect(g.Input);
                    g.Sweep(o.Frequency, t, (78 + rng.Next() * 34) * 1.12, (78 + rng.Next() * 34) * 0.82, dur);
                    double e = g.Ahr(og.Gain, t, 0.4, dur * 0.42, dur * 0.06, dur * 0.5);
                    // tyre spray on wet asphalt: the loudest part of a car in the rain
                    BufferSourceNode s = g.Src(NoiseKind.White, t, dur, 0.9 + rng.Next() * 0.3);
                    BiquadNode bp = g.Filter(BiquadType.Bandpass, 1400, 0.9);
                    GainNode sg = g.Gain(0);
                    g.Chain(s, bp, sg).Connect(g.Input);
                    g.Sweep(bp.Frequency, t, 900, 2400, dur * 0.5);
                    e = Math.Max(e, g.Ahr(sg.Gain, t, 0.34 * (0.4 + (p.Wet ?? 0.8)), dur * 0.45, dur * 0.05, dur * 0.5));
                    return e;
                },
            });
        }

        // ------------------------------------------------------------------ the score's instruments

        static void Perc(string name, double gain, Func<VoiceKit, VoiceParams, double> build)
        {
            Def(new SoundDef { Name = name, Bus = AudioBus.Music, Spatial = false, Priority = 4, Gain = gain, Send = 0.12, Build = build });
        }

        static void Tonal(string name, double gain, Func<VoiceKit, VoiceParams, double> build)
        {
            Def(new SoundDef { Name = name, Bus = AudioBus.Music, Spatial = false, Priority = 3, Gain = gain, Send = 0.1, Build = build });
        }

        static void RegisterMusic()
        {
            Perc("mus.kick", 1.0, (g, p) =>
            {
                double t = p.T;
                double e = SoundBuilders.Swoop(g, g.Input, t, 155 * p.Pitch, 44 * p.Pitch, dur: 0.34, peak: 0.9, attack: 0.001, bend: 0.16);
                e = Math.Max(e, SoundBuilders.Burst(g, g.Input, t, dur: 0.006, peak: 0.3, type: BiquadType.Highpass, freq: 1800));
                return e;
            });
            Perc("mus.snare", 0.8, (g, p) =>
            {
                double t = p.T;
                double e = SoundBuilders.Burst(g, g.Input, t, dur: 0.16, peak: 0.55, freq: 1900, q: 0.9, attack: 0.0006);
                e = Math.Max(e, SoundBuilders.Burst(g, g.Input, t, dur: 0.05, peak: 0.3, type: BiquadType.Highpass, freq: 6000, attack: 0.0004));
                e = Math.Max(e, SoundBuilders.Swoop(g, g.Input, t, 210, 150, dur: 0.09, peak: 0.35, attack: 0.001));
                return e;
            });
            Perc("mus.clap", 0.75, (g, p) =>
            {
                double t = p.T;
                double e = 0;
                for (int i = 0; i < 3; i++)
                {
                    e = Math.Max(e, i * 0.009 + SoundBuilders.Burst(g, g.Input, t + i * 0.009, dur: 0.02, peak: 0.4, freq: 1500, q: 1.4, attack: 0.0004));
                }
                e = Math.Max(e, 0.02 + SoundBuilders.Burst(g, g.Input, t + 0.02, dur: 0.19, peak: 0.3, freq: 1250, q: 1.1, attack: 0.002));
                return e;
            });
            Perc("mus.hat", 0.55, (g, p) => SoundBuilders.Burst(g, g.Input, p.T, dur: 0.032, peak: 0.4, type: BiquadType.Highpass, freq: 8200, attack: 0.0004, rate: p.Pitch));
            Perc("mus.openhat", 0.5, (g, p) => SoundBuilders.Burst(g, g.Input, p.T, dur: 0.24, peak: 0.32, type: BiquadType.Highpass, freq: 7000, attack: 0.0006, rate: p.Pitch));
            Perc("mus.rim", 0.5, (g, p) => SoundBuilders.Mech(g, g.Input, p.T, freq: 2300, q: 14, dur: 0.02, peak: 0.4, ring: 0.05));
            Perc("mus.noise", 0.5, (g, p) => SoundBuilders.Burst(g, g.Input, p.T, kind: NoiseKind.Pink, dur: 0.5, peak: 0.22, freq: 900, q: 0.8, sweepTo: 6000, attack: 0.3));

            Tonal("mus.bass", 0.9, (g, p) =>
            {
                double t = p.T;
                double f = p.Freq ?? 55;
                double dur = p.Dur ?? 0.2;
                double cut = p.Cutoff ?? 900;
                BiquadNode lp = g.Filter(BiquadType.Lowpass, cut, 2.6);
                GainNode gn = g.Gain(0);
                foreach (double[] voice in new[] { new[] { 1.0, 0.3 }, new[] { 1.007, 0.3 } })
                {
                    OscillatorNode o = g.Osc(WaveKind.Sawtooth, t, dur, f * voice[0]);
                    GainNode og = g.Gain(voice[1]);
                    o.Connect(og);
                    og.Connect(lp);
                }
                // a sine on the fundamental: saws alone leave nothing under 80 Hz once the resonant lowpass has done its work
                OscillatorNode sub = g.Osc(WaveKind.Sine, t, dur, f);
                GainNode sg = g.Gain(0.4);
                sub.Connect(sg);
                sg.Connect(gn);
                g.Chain(lp, gn).Connect(g.Input);
                g.Sweep(lp.Frequency, t, cut * 2.6, cut * 0.65, dur * 0.55);
                return g.Ad(gn.Gain, t, 0.55, 0.005, dur);
            });
            Tonal("mus.arp", 0.7, (g, p) =>
            {
                double t = p.T;
                double f = p.Freq ?? 440;
                double dur = p.Dur ?? 0.16;
                OscillatorNode o1 = g.Osc(WaveKind.Hollow, t, dur, f);
                OscillatorNode o2 = g.Osc(WaveKind.Triangle, t, dur, f * 2.003);
                BiquadNode bp = g.Filter(BiquadType.Bandpass, f * 2.2, 1.4);
                GainNode gn = g.Gain(0);
                o1.Connect(bp);
                GainNode g2 = g.Gain(0.35);
                o2.Connect(g2);
                g2.Connect(bp);
                g.Chain(bp, gn).Connect(g.Input);
                g.Sweep(bp.Frequency, t, f * 5, f * 1.4, dur * 0.6);
                return g.Ad(gn.Gain, t, 0.3, 0.003, dur);
            });
            Tonal("mus.stab", 0.85, (g, p) =>
            {
                double t = p.T;
                double dur = p.Dur ?? 0.5;
                double cut = p.Cutoff ?? 1600;
                BiquadNode lp = g.Filter(BiquadType.Lowpass, cut, 3);
                GainNode gn = g.Gain(0);
                foreach (double f in p.Freqs ?? new[] { 220.0, 262, 330 })
                {
                    foreach (double d in new[] { 0.996, 1.004 })
                    {
                        OscillatorNode o = g.Osc(WaveKind.SoftSaw, t, dur, f * d);
                        GainNode og = g.Gain(0.17);
                        o.Connect(og);
                        og.Connect(lp);
                    }
                }
                g.Chain(lp, gn).Connect(g.Input);
                g.Sweep(lp.Frequency, t, cut * 3, cut * 0.6, dur * 0.5);
                return g.Ad(gn.Gain, t, 0.34, 0.006, dur);
            });
            Tonal("mus.lead", 0.8, (g, p) =>
            {
                double t = p.T;
                double f = p.Freq ?? 440;
                double dur = p.Dur ?? 0.5;
                OscillatorNode o1 = g.Osc(WaveKind.SoftSaw, t, dur, f);
                OscillatorNode o2 = g.Osc(WaveKind.SoftSaw, t, dur, f * 1.011);
                BiquadNode lp = g.Filter(BiquadType.Lowpass, f * 3, 2.2);
                GainNode gn = g.Gain(0);
                GainNode ig = g.Gain(0.4);
                o1.Connect(ig);
                o2.Connect(ig);
                ig.Connect(lp);
                g.Chain(lp, gn).Connect(g.Input);
                OscillatorNode vib = g.Osc(WaveKind.Sine, t, dur, 5.2);
                GainNode vg = g.Gain(f * 0.008);
                vib.Connect(vg);
                vg.Connect(o1.Frequency);
                vg.Connect(o2.Frequency);
                g.Sweep(lp.Frequency, t, f * 6, f * 2, dur * 0.7);
                return g.Ahr(gn.Gain, t, 0.3, Math.Min(0.06, dur * 0.2), dur * 0.4, dur * 0.5);
            });
        }
    }
}
