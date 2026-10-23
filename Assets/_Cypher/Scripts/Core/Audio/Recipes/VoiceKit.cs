using System;

namespace Cypher.Core
{
    /// <summary>
    /// What a sound recipe builds with: the prototype's graph helpers (noise sources, oscillators, gains, filters, envelopes, sweeps) over a synthesis graph, with the stream of random
    /// numbers the helpers draw from. A recipe is written against this as it was against the browser's graph, so it can be ported line for line and give the same sound for the same seed.
    /// </summary>
    public sealed class VoiceKit
    {
        public readonly SynthGraph Graph;
        readonly Rng _graphRng;
        readonly AudioNoiseBank _noise;

        /// <summary>The voice's input: where a recipe sends what it makes.</summary>
        public readonly GainNode Input;

        public VoiceKit(SynthGraph graph, AudioNoiseBank noise, Rng graphRng)
        {
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _noise = noise ?? throw new ArgumentNullException(nameof(noise));
            _graphRng = graphRng ?? throw new ArgumentNullException(nameof(graphRng));
            if (noise.SampleRate != graph.SampleRate) throw new ArgumentException("the noise beds are for another sample rate", nameof(noise));
            Input = graph.CreateGain(1);
            Input.Connect(graph.Destination);
        }

        public int SampleRate => Graph.SampleRate;

        /// <summary>Noise source, started at <paramref name="t"/> for <paramref name="dur"/> seconds, from a random place in the bed unless told where.</summary>
        public BufferSourceNode Src(NoiseKind kind, double t, double dur, double rate = 1, double offset = -1)
        {
            float[] bed = _noise.Get(kind);
            BufferSourceNode s = Graph.CreateBufferSource(bed);
            s.PlaybackRate.Value = rate;
            double seconds = bed.Length / (double)SampleRate;
            double off = offset >= 0 ? offset : _graphRng.Next() * (seconds - 0.2);
            s.Start(t, off, dur + 0.02);
            return s;
        }

        public OscillatorNode Osc(WaveKind kind, double t, double dur, double freq)
        {
            OscillatorNode o = Graph.CreateOscillator(kind, freq);
            o.Start(t);
            o.Stop(t + dur + 0.02);
            return o;
        }

        public GainNode Gain(double v = 1) => Graph.CreateGain(v);

        /// <summary>A filter; its frequency is held to the range the audio can carry.</summary>
        public BiquadNode Filter(BiquadType type, double freq, double q = 1, double gainDb = 0)
        {
            return Graph.CreateBiquad(type, Math.Min(Math.Max(freq, 10), SampleRate * 0.49), q, gainDb);
        }

        public DelayNode Delay(double time, double max = 1.5) => Graph.CreateDelay(time, max);

        public SynthNode Chain(params SynthNode[] nodes) => SynthNode.Chain(nodes);

        /// <summary>Linear attack, exponential decay. Returns the sound's length so a recipe can report how long it lasts.</summary>
        public double Ad(AudioParam param, double t, double peak, double attack, double decay, double floor = 0.0006)
        {
            double p = Math.Max(1e-5, peak);
            param.CancelScheduledValues(t);
            param.SetValueAtTime(0, t);
            if (attack > 0.0002) param.LinearRampToValueAtTime(p, t + attack);
            else param.SetValueAtTime(p, t);
            param.ExponentialRampToValueAtTime(Math.Max(1e-5, p * floor), t + attack + decay);
            param.SetValueAtTime(0, t + attack + decay + 0.002);
            return attack + decay + 0.004;
        }

        /// <summary>Attack, hold, release with a linear tail; better for sustained bursts.</summary>
        public double Ahr(AudioParam param, double t, double peak, double attack, double hold, double release)
        {
            double p = Math.Max(1e-5, peak);
            param.CancelScheduledValues(t);
            param.SetValueAtTime(0, t);
            param.LinearRampToValueAtTime(p, t + attack);
            param.SetValueAtTime(p, t + attack + hold);
            param.LinearRampToValueAtTime(0, t + attack + hold + release);
            return attack + hold + release + 0.002;
        }

        /// <summary>An exponential sweep of a parameter, guarded against targets of zero.</summary>
        public void Sweep(AudioParam param, double t, double from, double to, double time)
        {
            param.CancelScheduledValues(t);
            param.SetValueAtTime(Math.Max(1e-3, from), t);
            param.ExponentialRampToValueAtTime(Math.Max(1e-3, to), t + Math.Max(0.002, time));
        }
    }
}
