using System;

namespace Cypher.Core
{
    /// <summary>What the world is doing to the sound of the city bed.</summary>
    public readonly struct BedState
    {
        public readonly double Rain, Wind, Indoor, Density;

        /// <param name="rain">0 dry to 1 downpour.</param>
        /// <param name="wind">0 still to 1 gale.</param>
        /// <param name="indoor">0 outside to 1 shut in a small room: what is outside is quieter and duller.</param>
        /// <param name="density">0 empty to 2 packed: how much traffic and crowd there is.</param>
        public BedState(double rain, double wind, double indoor, double density)
        {
            Rain = Clamp(rain, 0, 1);
            Wind = Clamp(wind, 0, 1);
            Indoor = Clamp(indoor, 0, 1);
            Density = Clamp(density, 0, 2);
        }

        static double Clamp(double v, double lo, double hi) => double.IsNaN(v) ? lo : v < lo ? lo : v > hi ? hi : v;

        public bool SameAs(BedState other) => Rain == other.Rain && Wind == other.Wind && Indoor == other.Indoor && Density == other.Density;
    }

    /// <summary>
    /// The city's continuous sound, as the prototype's Ambience.js builds it: distant traffic (a brown rumble and wet tyre hiss), rain (roar, hiss and patter, moved by two slow gusts), wind with an
    /// edge whistle, a crowd murmur of three drifting formants, the mains hum of the neon, and two sounds that stand in a place, the buzz of the nearest neon tube and the roof's air-conditioning.
    /// Every layer is a noise loop (or a few oscillators) moved by slow low-frequency oscillators at rates that do not divide each other, so the whole never repeats in any time you would notice.
    /// Weather, being indoors and the density of people set each layer's level and filter, smoothly.
    /// </summary>
    public sealed class Bed
    {
        public enum EmitterMode
        {
            /// <summary>The neon tube and the air-conditioning are part of the bed's graph, heard from the middle (how the offline comparison with the prototype is made).</summary>
            Inline,
            /// <summary>They are graphs of their own, one for each, to play from a place in the world.</summary>
            Separate,
        }

        const double Tau = 0.9;

        readonly AudioNoiseBank _noise;
        readonly Rng _rng;

        // the layer controls Advance sets
        GainNode _trafficGain, _trafficHiss, _rainGain, _rainRoarGain, _rainHissGain, _rainPatterGain, _windGain, _windWhistle, _crowdGain, _humGain, _neonGain, _acGain;
        BiquadNode _rainRoar, _rainHiss, _windBand;
        BufferSourceNode _rainPatterRate;

        /// <summary>The bed's graph; its output is stereo (the traffic and wind are panned).</summary>
        public SynthGraph Main { get; }

        /// <summary>The neon tube and the air-conditioning as graphs of their own (mono); null when they are <see cref="EmitterMode.Inline"/>.</summary>
        public SynthGraph Neon { get; }
        public SynthGraph AirConditioning { get; }

        /// <summary>When each of the six random events is next due (the prototype's schedule), set at the bed's start.</summary>
        public double NextSiren { get; }
        public double NextImpact { get; }
        public double NextDrip { get; }
        public double NextVoice { get; }
        public double NextBuzz { get; }
        public double NextCar { get; }

        /// <summary>The random stream after the bed has been built: the events draw from it next.</summary>
        public Rng Rng => _rng;

        public Bed(AudioNoiseBank noise, Rng rng, double t0, EmitterMode mode)
        {
            _noise = noise ?? throw new ArgumentNullException(nameof(noise));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            int rate = noise.SampleRate;
            Main = new SynthGraph(rate);
            if (mode == EmitterMode.Separate)
            {
                Neon = new SynthGraph(rate);
                AirConditioning = new SynthGraph(rate);
            }
            double t = t0 + 0.02;
            SynthGraph g = Main;
            GainNode bus = g.CreateGain(1);
            bus.Connect(g.Destination);

            BuildTraffic(g, bus, t);
            BuildRain(g, bus, t);
            BuildWind(g, bus, t);
            BuildCrowd(g, bus, t);
            BuildHum(g, bus, t, mode == EmitterMode.Inline ? g : Neon);
            BuildAirConditioning(g, bus, t, mode == EmitterMode.Inline ? g : AirConditioning);

            // the clocks for the random events
            NextSiren = t + 8 + _rng.Next() * 25;
            NextImpact = t + 12 + _rng.Next() * 30;
            NextDrip = t + 0.5 + _rng.Next() * 2;
            NextVoice = t + 2 + _rng.Next() * 5;
            NextBuzz = t + 4 + _rng.Next() * 10;
            NextCar = t + 5 + _rng.Next() * 12;
        }

        // ---- the helpers the prototype's Ambience uses, over a graph ----

        BufferSourceNode Loop(SynthGraph g, NoiseKind kind, double t, double rate = 1)
        {
            float[] bed = _noise.Get(kind);
            BufferSourceNode s = g.CreateBufferSource(bed);
            s.PlaybackRate.Value = rate;
            s.Start(t, _rng.Next() * (bed.Length / (double)g.SampleRate - 0.1));
            return s;
        }

        static BiquadNode Filter(SynthGraph g, BiquadType type, double f, double q = 1) => g.CreateBiquad(type, f, q);

        /// <summary>LFO to depth to parameter: the engine of every slow movement. A small start offset per LFO keeps the layers from breathing in lockstep.</summary>
        static void Lfo(SynthGraph g, double t, double rate, double depth, AudioParam param, double phase = 0)
        {
            OscillatorNode o = g.CreateOscillator(WaveKind.Sine, rate);
            GainNode d = g.CreateGain(depth);
            o.Connect(d);
            d.Connect(param);
            o.Start(t + phase);
        }

        // ---- the layers ----

        void BuildTraffic(SynthGraph g, GainNode bus, double t)
        {
            GainNode output = g.CreateGain(0.0001);
            output.Connect(bus);
            _trafficGain = output;
            foreach (double[] side in new[] { new[] { -0.75, 0.87 }, new[] { 0.75, 1.09 } })
            {
                BufferSourceNode s = Loop(g, NoiseKind.Brown, t, side[1]);
                BiquadNode lp = Filter(g, BiquadType.Lowpass, 165, 1.6);
                BiquadNode hp = Filter(g, BiquadType.Highpass, 26, 0.7);
                GainNode gg = g.CreateGain(0.9);
                SynthNode.Chain(s, hp, lp, gg, g.CreateStereoPanner(side[0])).Connect(output);
                Lfo(g, t, 0.037 + _rng.Next() * 0.02, 55, lp.Frequency, _rng.Next() * 2);
                Lfo(g, t, 0.061 + _rng.Next() * 0.03, 0.35, gg.Gain, _rng.Next() * 3);
            }
            // the tyre spray band: the sound of a wet arterial two blocks away
            BufferSourceNode hiss = Loop(g, NoiseKind.White, t, 0.95);
            BiquadNode bp = Filter(g, BiquadType.Bandpass, 620, 0.75);
            GainNode hg = g.CreateGain(0.16);
            _trafficHiss = hg;
            SynthNode.Chain(hiss, bp, hg).Connect(output);
            Lfo(g, t, 0.089, 180, bp.Frequency, _rng.Next());
            Lfo(g, t, 0.113, 0.06, hg.Gain, _rng.Next() * 4);
        }

        void BuildRain(SynthGraph g, GainNode bus, double t)
        {
            GainNode output = g.CreateGain(0.0001);
            output.Connect(bus);
            _rainGain = output;
            GainNode gust = g.CreateGain(1);
            gust.Connect(output);
            // two slow gusts at incommensurate rates
            Lfo(g, t, 0.047, 0.22, gust.Gain, _rng.Next() * 5);
            Lfo(g, t, 0.0193, 0.16, gust.Gain, _rng.Next() * 7);

            BufferSourceNode roarS = Loop(g, NoiseKind.White, t, 0.8);
            BiquadNode roarF = Filter(g, BiquadType.Bandpass, 640, 0.6);
            GainNode roarG = g.CreateGain(0.5);
            _rainRoar = roarF;
            _rainRoarGain = roarG;
            SynthNode.Chain(roarS, roarF, roarG).Connect(gust);
            Lfo(g, t, 0.071, 140, roarF.Frequency, _rng.Next() * 2);

            BufferSourceNode hissS = Loop(g, NoiseKind.White, t, 1.13);
            BiquadNode hissF = Filter(g, BiquadType.Highpass, 2600, 0.7);
            // the prototype asks for a 4 dB lift at 6.2 kHz here but its filter helper drops the gain, so the peak is flat; the same is done
            BiquadNode hissT = Filter(g, BiquadType.Peaking, 6200, 1.2);
            GainNode hissG = g.CreateGain(0.32);
            _rainHiss = hissF;
            _rainHissGain = hissG;
            SynthNode.Chain(hissS, hissF, hissT, hissG).Connect(gust);
            Lfo(g, t, 0.0311, 900, hissF.Frequency, _rng.Next() * 6);
            Lfo(g, t, 0.1499, 0.07, hissG.Gain, _rng.Next() * 3);

            // velvet noise: discrete droplets striking hard surfaces
            BufferSourceNode patS = Loop(g, NoiseKind.Velvet, t, 1.0);
            BiquadNode patF = Filter(g, BiquadType.Bandpass, 2900, 1.1);
            GainNode patG = g.CreateGain(0.4);
            _rainPatterRate = patS;
            _rainPatterGain = patG;
            SynthNode.Chain(patS, patF, patG).Connect(gust);
            Lfo(g, t, 0.0587, 700, patF.Frequency, _rng.Next() * 4);
        }

        void BuildWind(SynthGraph g, GainNode bus, double t)
        {
            GainNode output = g.CreateGain(0.0001);
            output.Connect(bus);
            _windGain = output;
            BufferSourceNode s = Loop(g, NoiseKind.Pink, t, 0.7);
            BiquadNode bp = Filter(g, BiquadType.Bandpass, 480, 1.6);
            GainNode gg = g.CreateGain(0.9);
            _windBand = bp;
            SynthNode.Chain(s, bp, gg, g.CreateStereoPanner(-0.25)).Connect(output);
            Lfo(g, t, 0.0271, 260, bp.Frequency, _rng.Next() * 3);
            Lfo(g, t, 0.0433, 0.45, gg.Gain, _rng.Next() * 5);

            BufferSourceNode s2 = Loop(g, NoiseKind.White, t, 1.0);
            BiquadNode wh = Filter(g, BiquadType.Bandpass, 1500, 9);
            GainNode wg = g.CreateGain(0.0);
            _windWhistle = wg;
            SynthNode.Chain(s2, wh, wg, g.CreateStereoPanner(0.35)).Connect(output);
            Lfo(g, t, 0.0179, 620, wh.Frequency, _rng.Next() * 2);
            Lfo(g, t, 0.0367, 0.05, wg.Gain, _rng.Next() * 6);
        }

        void BuildCrowd(SynthGraph g, GainNode bus, double t)
        {
            GainNode output = g.CreateGain(0.0001);
            output.Connect(bus);
            _crowdGain = output;
            BufferSourceNode s = Loop(g, NoiseKind.Pink, t, 0.85);
            GainNode sum = g.CreateGain(0.5);
            foreach (double[] formant in new[] { new[] { 480.0, 5, 1.0, 0.0231 }, new[] { 1180.0, 7, 0.7, 0.0313 }, new[] { 2450.0, 9, 0.4, 0.0417 } })
            {
                BiquadNode bp = Filter(g, BiquadType.Bandpass, formant[0], formant[1]);
                GainNode bg = g.CreateGain(formant[2]);
                s.Connect(bp);
                bp.Connect(bg);
                bg.Connect(sum);
                Lfo(g, t, formant[3], formant[0] * 0.22, bp.Frequency, _rng.Next() * 4);
            }
            BiquadNode lp = Filter(g, BiquadType.Lowpass, 3200, 0.7);
            // the modulation rides its own unity gain so the level control stays clean
            GainNode mod = g.CreateGain(1);
            SynthNode.Chain(sum, lp, mod).Connect(output);
            Lfo(g, t, 0.0139, 0.35, mod.Gain, _rng.Next() * 8);
        }

        void BuildHum(SynthGraph g, GainNode bus, double t, SynthGraph emitterGraph)
        {
            GainNode output = g.CreateGain(0.0001);
            output.Connect(bus);
            _humGain = output;
            GainNode sum = g.CreateGain(1);
            foreach (double[] partial in new[] { new[] { 60.0, 0.5 }, new[] { 120.0, 0.35 }, new[] { 180.0, 0.14 }, new[] { 360.0, 0.05 } })
            {
                OscillatorNode o = g.CreateOscillator(partial[0] == 60 ? WaveKind.Sine : WaveKind.Triangle, partial[0]);
                GainNode gg = g.CreateGain(partial[1]);
                o.Connect(gg);
                gg.Connect(sum);
                o.Start(t);
            }
            BiquadNode hp = Filter(g, BiquadType.Highpass, 45, 0.7);
            BiquadNode lp = Filter(g, BiquadType.Lowpass, 900, 0.9);
            GainNode mod = g.CreateGain(1);
            SynthNode.Chain(sum, hp, lp, mod).Connect(output);
            Lfo(g, t, 0.0233, 0.3, mod.Gain, _rng.Next());

            // the tube nearest the player: mains buzz plus a flickering ballast hiss
            SynthGraph eg = emitterGraph;
            GainNode neonIn = eg.CreateGain(1);
            BufferSourceNode es = Loop(eg, NoiseKind.White, t, 1.0);
            BiquadNode ebp = Filter(eg, BiquadType.Bandpass, 2200, 6);
            GainNode egain = eg.CreateGain(0.09);
            GainNode emod = eg.CreateGain(1);
            OscillatorNode eo = eg.CreateOscillator(WaveKind.Triangle, 120);
            GainNode eog = eg.CreateGain(0.22);
            eo.Connect(eog);
            eog.Connect(neonIn);
            eo.Start(t);
            SynthNode.Chain(es, ebp, egain, emod).Connect(neonIn);
            GainNode neonOut = eg.CreateGain(0.0001);
            neonIn.Connect(neonOut);
            neonOut.Connect(ReferenceEquals(eg, g) ? (SynthNode)bus : eg.Destination);
            _neonGain = neonOut;
            Lfo(eg, t, 0.31, 0.4, emod.Gain, _rng.Next() * 3);
        }

        void BuildAirConditioning(SynthGraph g, GainNode bus, double t, SynthGraph emitterGraph)
        {
            SynthGraph eg = emitterGraph;
            GainNode acIn = eg.CreateGain(1);
            BufferSourceNode s = Loop(eg, NoiseKind.Brown, t, 1.2);
            BiquadNode lp = Filter(eg, BiquadType.Lowpass, 300, 3.2);
            GainNode gg = eg.CreateGain(0.55);
            SynthNode.Chain(s, lp, gg).Connect(acIn);
            OscillatorNode o = eg.CreateOscillator(WaveKind.Sine, 47);
            GainNode og = eg.CreateGain(0.16);
            OscillatorNode fm = eg.CreateOscillator(WaveKind.Sine, 0.9);
            GainNode fmg = eg.CreateGain(1.6);
            fm.Connect(fmg);
            fmg.Connect(o.Frequency);
            o.Connect(og);
            og.Connect(acIn);
            o.Start(t);
            fm.Start(t);
            GainNode outG = eg.CreateGain(0.0001);
            acIn.Connect(outG);
            outG.Connect(ReferenceEquals(eg, g) ? (SynthNode)bus : eg.Destination);
            _acGain = outG;
            Lfo(eg, t, 0.0847, 40, lp.Frequency, _rng.Next() * 2);
        }

        // ---- the weather and the room set each layer ----

        static double Lerp(double a, double b, double t) => a + (b - a) * t;

        static void Set(AudioParam param, double now, double v) => param.SetTargetAtTime(Math.Max(0.0001, v), now, Tau);

        /// <summary>
        /// Move the main bed's layers toward what the state says, from time <paramref name="now"/> (graph time), each over about a second. In inline mode this covers the two emitters too; in
        /// separate mode use <see cref="AdvanceNeon"/> and <see cref="AdvanceAirConditioning"/> on their own graphs.
        /// </summary>
        public void Advance(double now, BedState s)
        {
            double rain = s.Rain, wind = s.Wind, indoor = s.Indoor, density = s.Density;
            // indoors everything outside is muffled and quieter
            double outside = Lerp(1, 0.32, indoor);

            Set(_trafficGain.Gain, now, (0.30 + rain * 0.10) * density * outside);
            Set(_trafficHiss.Gain, now, (0.06 + rain * 0.16) * outside);

            Set(_rainGain.Gain, now, rain * 0.62 * Lerp(1, 0.45, indoor));
            Set(_rainRoarGain.Gain, now, 0.28 + rain * 0.34);
            Set(_rainHissGain.Gain, now, 0.10 + rain * 0.30);
            _rainRoar.Frequency.SetTargetAtTime(Lerp(880, 470, rain), now, Tau);
            _rainHiss.Frequency.SetTargetAtTime(Lerp(3400, 1900, rain), now, Tau);
            _rainPatterGain.Gain.SetTargetAtTime(0.08 + rain * 0.42, now, Tau);
            _rainPatterRate.PlaybackRate.SetTargetAtTime(0.75 + rain * 0.7, now, Tau);

            Set(_windGain.Gain, now, (0.06 + wind * 0.42) * outside);
            _windBand.Frequency.SetTargetAtTime(Lerp(360, 760, wind), now, Tau);
            Set(_windWhistle.Gain, now, Math.Max(0.0001, (wind - 0.35) * 0.16) * outside);

            Set(_crowdGain.Gain, now, 0.13 * density * Lerp(1, 0.5, rain) * outside);
            Set(_humGain.Gain, now, 0.055 * Lerp(1, 0.6, indoor));

            if (Neon == null && AirConditioning == null)
            {
                AdvanceNeon(now, s);
                AdvanceAirConditioning(now, s);
            }
        }

        /// <summary>The neon tube's level (call on the graph that holds it).</summary>
        public void AdvanceNeon(double now, BedState s) => Set(_neonGain.Gain, now, 0.5 * Lerp(1, 0.35, s.Indoor));

        /// <summary>The air-conditioning's level (call on the graph that holds it).</summary>
        public void AdvanceAirConditioning(double now, BedState s) => Set(_acGain.Gain, now, 0.5 * Lerp(1, 1.5, s.Indoor));
    }
}
