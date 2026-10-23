using System;
using System.Collections.Concurrent;

namespace Cypher.Core
{
    /// <summary>Where the sound ends up: always two channels, a mono input in both.</summary>
    public sealed class DestinationNode : SynthNode
    {
        internal DestinationNode(SynthGraph graph) : base(graph)
        {
            OutChannels = 2;
            InChannels = 2;
        }

        internal override int MinInputChannels => 2;

        internal override int OutputChannels(int inputChannels) => 2;

        internal override void Process(int n)
        {
            SumInputs(n);
            Array.Copy(In0, Out0, n);
            Array.Copy(In1, Out1, n);
        }
    }

    /// <summary>Multiplies its input by a gain that can move: a level, an envelope, a depth for a modulator.</summary>
    public sealed class GainNode : SynthNode
    {
        public readonly AudioParam Gain;

        internal GainNode(SynthGraph graph, double gain) : base(graph)
        {
            Gain = NewParam(gain);
        }

        internal override void Process(int n)
        {
            if (Inputs.Count == 0)
            {
                Array.Clear(Out0, 0, n);
                if (OutChannels == 2) Array.Clear(Out1, 0, n);
                return;
            }
            SumInputs(n);
            Gain.Compute(Graph.Frame, n);
            Apply(In0, Out0, n);
            if (OutChannels == 2) Apply(In1, Out1, n);
        }

        void Apply(float[] input, float[] output, int n)
        {
            if (Gain.IsConstant)
            {
                float g = Gain.Constant;
                for (int i = 0; i < n; i++) output[i] = input[i] * g;
            }
            else
            {
                float[] g = Gain.Values;
                for (int i = 0; i < n; i++) output[i] = input[i] * g[i];
            }
        }
    }

    /// <summary>A steady value, or whatever its offset is scheduled to do: used to watch a parameter's schedule as sound.</summary>
    public sealed class ConstantSourceNode : SynthNode
    {
        public readonly AudioParam Offset;

        internal ConstantSourceNode(SynthGraph graph, double offset) : base(graph)
        {
            Offset = NewParam(offset);
        }

        internal override bool HasInput => false;

        internal override void Process(int n)
        {
            Offset.Compute(Graph.Frame, n);
            if (Offset.IsConstant)
            {
                float c = Offset.Constant;
                for (int i = 0; i < n; i++) Out0[i] = c;
            }
            else
            {
                Array.Copy(Offset.Values, Out0, n);
            }
        }
    }

    /// <summary>A periodic waveform at a frequency that can move: a tone, or a slow modulator when it is slow enough.</summary>
    public sealed class OscillatorNode : SynthNode
    {
        public readonly AudioParam Frequency;
        public readonly AudioParam Detune;

        readonly PeriodicWave _wave;
        double _phase;
        long _start = -1;
        double _startFraction;
        bool _begun;
        long _stop = long.MaxValue;

        internal OscillatorNode(SynthGraph graph, WaveKind kind, double frequency) : base(graph)
        {
            Frequency = NewParam(frequency);
            Detune = NewParam(0);
            _wave = PeriodicWave.Get(kind, graph.SampleRate);
        }

        internal override bool HasInput => false;

        /// <summary>
        /// Begin at <paramref name="when"/>. As in Chromium the first sound comes at the first whole frame at or after it; and past the first block of the context it carries on from the
        /// fraction of a frame in between, so the phase is right to the sample. Within the first block the start is rounded up to the frame.
        /// </summary>
        public OscillatorNode Start(double when)
        {
            if (_start >= 0) throw new InvalidOperationException("an oscillator starts once");
            double s = when * Graph.SampleRate;
            _start = (long)Math.Ceiling(s - 1e-4);
            _startFraction = _start >= SynthGraph.Quantum ? Math.Max(0, _start - s) : 0;
            return this;
        }

        public OscillatorNode Stop(double when)
        {
            if (_start < 0) throw new InvalidOperationException("an oscillator that has not started cannot stop");
            _stop = (long)Math.Ceiling(when * Graph.SampleRate - 1e-4);
            return this;
        }

        internal override void Process(int n)
        {
            long f0 = Graph.Frame;
            if (_start < 0 || f0 + n <= _start || f0 >= _stop)
            {
                Array.Clear(Out0, 0, n);
                return;
            }
            Frequency.Compute(f0, n);
            Detune.Compute(f0, n);
            int first = (int)Math.Max(0, _start - f0);
            int last = (int)Math.Min(n, _stop - f0);
            if (first > 0) Array.Clear(Out0, 0, first);
            if (last < n) Array.Clear(Out0, last, n - last);

            double invRate = 1.0 / Graph.SampleRate;
            if (!_begun)
            {
                _begun = true;
                _phase = _startFraction * FrequencyAt(first) * invRate;
                _phase -= Math.Floor(_phase);
            }

            // the tables for the harmonics that fit at this frequency, and the cross-fade between the two nearest
            _wave.Select(FrequencyAt(first), out float[] low, out float[] high, out float mix);
            bool blend = !ReferenceEquals(low, high) && mix > 0;
            float keep = 1 - mix;
            double phase = _phase;
            bool flat = Frequency.IsConstant && Detune.IsConstant;
            double step = flat ? FrequencyAt(0) * invRate : 0;
            for (int i = first; i < last; i++)
            {
                double x = phase * PeriodicWave.TableSize;
                int index = (int)x;
                float frac = (float)(x - index);
                float a = low[index];
                float v = a + (low[index + 1] - a) * frac;
                if (blend)
                {
                    float b = high[index];
                    v = v * keep + (b + (high[index + 1] - b) * frac) * mix;
                }
                Out0[i] = v;
                phase += flat ? step : FrequencyAt(i) * invRate;
                if (phase >= 1.0 || phase < 0.0) phase -= Math.Floor(phase);
            }
            _phase = phase;
        }

        double FrequencyAt(int i)
        {
            double f = Frequency.IsConstant ? Frequency.Constant : Frequency.Values[i];
            double d = Detune.IsConstant ? Detune.Constant : Detune.Values[i];
            return d == 0 ? f : f * Math.Pow(2, d / 1200.0);
        }
    }

    /// <summary>
    /// Plays a buffer (here always a noise bed) in a loop at a rate that can move, from an offset, for a stated length. Like Chromium's it starts between frames (the first frame at or after
    /// the time reads a little way in), takes its playback rate once a block, and at a rate of exactly 1 begins at a whole frame of the buffer.
    /// </summary>
    public sealed class BufferSourceNode : SynthNode
    {
        public readonly AudioParam PlaybackRate;

        readonly float[] _data;
        double _position;
        long _start = -1;
        double _startFraction;
        bool _begun;
        double _remaining = double.PositiveInfinity;
        bool _finished;

        internal BufferSourceNode(SynthGraph graph, float[] data) : base(graph)
        {
            if (data == null || data.Length < 2) throw new ArgumentException("a buffer needs at least two samples", nameof(data));
            _data = data;
            PlaybackRate = NewParam(1);
        }

        internal override bool HasInput => false;

        /// <summary>Begin looping at <paramref name="when"/>, <paramref name="offset"/> seconds into the buffer, for <paramref name="duration"/> seconds of the buffer (negative: for ever).</summary>
        public BufferSourceNode Start(double when, double offset = 0, double duration = -1)
        {
            if (_start >= 0) throw new InvalidOperationException("a buffer source starts once");
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), offset, "the offset cannot be negative");
            double s = when * Graph.SampleRate;
            _start = (long)Math.Ceiling(s - 1e-4);
            _startFraction = Math.Max(0, _start - s);
            _position = (offset * Graph.SampleRate) % _data.Length;
            _remaining = duration < 0 ? double.PositiveInfinity : duration * Graph.SampleRate;
            return this;
        }

        internal override void Process(int n)
        {
            long f0 = Graph.Frame;
            if (_start < 0 || _finished || f0 + n <= _start)
            {
                Array.Clear(Out0, 0, n);
                return;
            }
            PlaybackRate.Compute(f0, n);
            int first = (int)Math.Max(0, _start - f0);
            if (first > 0) Array.Clear(Out0, 0, first);
            int length = _data.Length;
            double rate = PlaybackRate.IsConstant ? PlaybackRate.Constant : PlaybackRate.Values[0];
            double position = _position;
            if (!_begun)
            {
                _begun = true;
                // played at exactly its own rate it needs no resampling, and Chromium then starts on a whole frame of the buffer
                if (rate == 1) position = Math.Floor(position + 0.5) % length;
                position += _startFraction * rate;
                if (position >= length) position -= length;
            }
            double remaining = _remaining;
            int i = first;
            for (; i < n; i++)
            {
                int index = (int)position;
                float frac = (float)(position - index);
                float a = _data[index];
                float b = _data[index + 1 < length ? index + 1 : 0];
                Out0[i] = a + (b - a) * frac;
                position += rate;
                if (position >= length) position -= length;
                else if (position < 0) position += length;
                remaining -= rate;
                if (remaining <= 0)
                {
                    _finished = true;
                    i++;
                    break;
                }
            }
            if (i < n) Array.Clear(Out0, i, n - i);
            _position = position;
            _remaining = remaining;
        }
    }

    public enum BiquadType
    {
        Lowpass,
        Highpass,
        Bandpass,
        Peaking,
    }

    /// <summary>
    /// A second-order filter with Web Audio's definitions of its four types (as Chromium works them out): a low- or high-pass takes its Q in decibels of resonance, a band-pass or peak its Q as a ratio.
    /// While a parameter is moving its coefficients are remade every <see cref="SynthGraph.CoefficientInterval"/> frames.
    /// </summary>
    public sealed class BiquadNode : SynthNode
    {
        public readonly AudioParam Frequency;
        public readonly AudioParam Detune;
        public readonly AudioParam Q;
        public readonly AudioParam Gain;

        public BiquadType Type;

        double _b0, _b1, _b2, _a1, _a2;
        double _x1L, _x2L, _y1L, _y2L, _x1R, _x2R, _y1R, _y2R;
        double _lastF = double.NaN, _lastQ = double.NaN, _lastG = double.NaN;
        BiquadType _lastType;

        internal BiquadNode(SynthGraph graph, BiquadType type, double frequency, double q, double gainDb) : base(graph)
        {
            Type = type;
            Frequency = NewParam(frequency);
            Detune = NewParam(0);
            Q = NewParam(q);
            Gain = NewParam(gainDb);
        }

        internal override int OutputChannels(int inputChannels) => Math.Min(2, inputChannels);

        internal override void Process(int n)
        {
            if (Inputs.Count == 0)
            {
                Array.Clear(Out0, 0, n);
                if (OutChannels == 2) Array.Clear(Out1, 0, n);
                return;
            }
            SumInputs(n);
            long f0 = Graph.Frame;
            Frequency.Compute(f0, n);
            Detune.Compute(f0, n);
            Q.Compute(f0, n);
            Gain.Compute(f0, n);
            bool still = Frequency.IsConstant && Detune.IsConstant && Q.IsConstant && Gain.IsConstant;
            int interval = still ? n : Math.Max(1, Graph.CoefficientInterval);
            for (int from = 0; from < n; from += interval)
            {
                int count = Math.Min(interval, n - from);
                Update(from);
                Run(from, count);
            }
            Flush();
        }

        void Update(int i)
        {
            double f = Frequency.IsConstant ? Frequency.Constant : Frequency.Values[i];
            double d = Detune.IsConstant ? Detune.Constant : Detune.Values[i];
            if (d != 0) f *= Math.Pow(2, d / 1200.0);
            double q = Q.IsConstant ? Q.Constant : Q.Values[i];
            double g = Gain.IsConstant ? Gain.Constant : Gain.Values[i];
            if (f == _lastF && q == _lastQ && g == _lastG && Type == _lastType) return;
            _lastF = f; _lastQ = q; _lastG = g; _lastType = Type;
            double nyquist = Graph.SampleRate * 0.5;
            BiquadMath.Coefficients(Type, f / nyquist, q, g, out _b0, out _b1, out _b2, out _a1, out _a2);
        }

        void Run(int from, int count)
        {
            Channel(In0, Out0, from, count, ref _x1L, ref _x2L, ref _y1L, ref _y2L);
            if (OutChannels == 2) Channel(In1, Out1, from, count, ref _x1R, ref _x2R, ref _y1R, ref _y2R);
        }

        /// <summary>Direct form I: with coefficients that move, it rings differently from the transposed form, and Chromium's is this one.</summary>
        void Channel(float[] input, float[] output, int from, int count, ref double x1, ref double x2, ref double y1, ref double y2)
        {
            double b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2;
            double px1 = x1, px2 = x2, py1 = y1, py2 = y2;
            for (int i = from; i < from + count; i++)
            {
                double x = input[i];
                double y = b0 * x + b1 * px1 + b2 * px2 - a1 * py1 - a2 * py2;
                px2 = px1;
                px1 = x;
                py2 = py1;
                py1 = y;
                output[i] = (float)y;
            }
            x1 = px1; x2 = px2; y1 = py1; y2 = py2;
        }

        /// <summary>A filter whose input has stopped rings down to denormal numbers, which are slow; once it is that quiet it is silent.</summary>
        void Flush()
        {
            if (Math.Abs(_y1L) < 1e-30 && Math.Abs(_y2L) < 1e-30) { _y1L = 0; _y2L = 0; }
            if (Math.Abs(_y1R) < 1e-30 && Math.Abs(_y2R) < 1e-30) { _y1R = 0; _y2R = 0; }
        }
    }

    /// <summary>The coefficients of the biquad types, from Web Audio as Chromium implements it.</summary>
    public static class BiquadMath
    {
        /// <param name="frequency">Normalised: 1 is half the sample rate.</param>
        /// <param name="q">Resonance in decibels for a low- or high-pass, a ratio for the others.</param>
        /// <param name="gainDb">Only the peaking filter has one.</param>
        public static void Coefficients(BiquadType type, double frequency, double q, double gainDb, out double b0, out double b1, out double b2, out double a1, out double a2)
        {
            switch (type)
            {
                case BiquadType.Lowpass: Lowpass(frequency, q, out b0, out b1, out b2, out a1, out a2); break;
                case BiquadType.Highpass: Highpass(frequency, q, out b0, out b1, out b2, out a1, out a2); break;
                case BiquadType.Bandpass: Bandpass(frequency, q, out b0, out b1, out b2, out a1, out a2); break;
                case BiquadType.Peaking: Peaking(frequency, q, gainDb, out b0, out b1, out b2, out a1, out a2); break;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "unknown filter type");
            }
        }

        static void Normalise(double b0, double b1, double b2, double a0, double a1, double a2, out double nb0, out double nb1, out double nb2, out double na1, out double na2)
        {
            double inv = 1 / a0;
            nb0 = b0 * inv; nb1 = b1 * inv; nb2 = b2 * inv; na1 = a1 * inv; na2 = a2 * inv;
        }

        static double Clamp01(double x) => double.IsNaN(x) ? 0 : x < 0 ? 0 : x > 1 ? 1 : x;

        static void Lowpass(double cutoff, double resonance, out double b0, out double b1, out double b2, out double a1, out double a2)
        {
            cutoff = Clamp01(cutoff);
            if (cutoff == 1 || cutoff == 0)
            {
                // all of it passes, or none of it
                b0 = cutoff; b1 = 0; b2 = 0; a1 = 0; a2 = 0;
                return;
            }
            double w0 = Math.PI * cutoff;
            double alpha = Math.Sin(w0) / (2 * Math.Pow(10, resonance / 20));
            double k = Math.Cos(w0);
            Normalise((1 - k) / 2, 1 - k, (1 - k) / 2, 1 + alpha, -2 * k, 1 - alpha, out b0, out b1, out b2, out a1, out a2);
        }

        static void Highpass(double cutoff, double resonance, out double b0, out double b1, out double b2, out double a1, out double a2)
        {
            cutoff = Clamp01(cutoff);
            if (cutoff == 1 || cutoff == 0)
            {
                b0 = 1 - cutoff; b1 = 0; b2 = 0; a1 = 0; a2 = 0;
                return;
            }
            double w0 = Math.PI * cutoff;
            double alpha = Math.Sin(w0) / (2 * Math.Pow(10, resonance / 20));
            double k = Math.Cos(w0);
            Normalise((1 + k) / 2, -(1 + k), (1 + k) / 2, 1 + alpha, -2 * k, 1 - alpha, out b0, out b1, out b2, out a1, out a2);
        }

        static void Bandpass(double frequency, double q, out double b0, out double b1, out double b2, out double a1, out double a2)
        {
            frequency = Math.Max(0, frequency);
            q = Math.Max(0, q);
            if (frequency > 0 && frequency < 1)
            {
                if (q > 0)
                {
                    double w0 = Math.PI * frequency;
                    double alpha = Math.Sin(w0) / (2 * q);
                    double k = Math.Cos(w0);
                    Normalise(alpha, 0, -alpha, 1 + alpha, -2 * k, 1 - alpha, out b0, out b1, out b2, out a1, out a2);
                }
                else
                {
                    // the limit as Q falls to zero: everything passes
                    b0 = 1; b1 = 0; b2 = 0; a1 = 0; a2 = 0;
                }
            }
            else
            {
                b0 = 0; b1 = 0; b2 = 0; a1 = 0; a2 = 0;
            }
        }

        static void Peaking(double frequency, double q, double gainDb, out double b0, out double b1, out double b2, out double a1, out double a2)
        {
            frequency = Clamp01(frequency);
            q = Math.Max(0, q);
            double a = Math.Pow(10, gainDb / 40);
            if (frequency > 0 && frequency < 1)
            {
                if (q > 0)
                {
                    double w0 = Math.PI * frequency;
                    double alpha = Math.Sin(w0) / (2 * q);
                    double k = Math.Cos(w0);
                    Normalise(1 + alpha * a, -2 * k, 1 - alpha * a, 1 + alpha / a, -2 * k, 1 - alpha / a, out b0, out b1, out b2, out a1, out a2);
                }
                else
                {
                    b0 = a * a; b1 = 0; b2 = 0; a1 = 0; a2 = 0;
                }
            }
            else
            {
                b0 = 1; b1 = 0; b2 = 0; a1 = 0; a2 = 0;
            }
        }
    }

    /// <summary>
    /// A delay line that may sit in a loop. It reads what was written before this block and takes in this block's input afterwards. As in Chromium, the input that closes a loop (one
    /// rendered after the delay) arrives a block (128 frames) late, so a loop's round trip is its delay and a block more, and a delay in a loop is at least a block long.
    /// </summary>
    public sealed class DelayNode : SynthNode
    {
        public readonly AudioParam DelayTime;

        readonly float[][] _ring = new float[2][];
        readonly int _size;
        long _written;
        bool[] _closesLoop = new bool[0];
        float[][] _late0 = new float[0][], _late1 = new float[0][];

        internal DelayNode(SynthGraph graph, double seconds, double maxSeconds) : base(graph)
        {
            if (maxSeconds <= 0 || maxSeconds > 20) throw new ArgumentOutOfRangeException(nameof(maxSeconds), maxSeconds, "not a sensible maximum delay");
            DelayTime = NewParam(seconds);
            _size = (int)Math.Ceiling(maxSeconds * graph.SampleRate) + 2 * SynthGraph.Quantum + 2;
            _ring[0] = new float[_size];
            _ring[1] = new float[_size];
        }

        internal override bool SplitsCycle => true;

        internal override int OutputChannels(int inputChannels) => Math.Min(2, inputChannels);

        /// <summary>The inputs that depend on this delay's own output are the ones that close a loop through it.</summary>
        internal void FindBackEdges()
        {
            _closesLoop = new bool[Inputs.Count];
            _late0 = new float[Inputs.Count][];
            _late1 = new float[Inputs.Count][];
            for (int j = 0; j < Inputs.Count; j++) _closesLoop[j] = DependsOnThis(Inputs[j], new System.Collections.Generic.HashSet<SynthNode>());
        }

        bool DependsOnThis(SynthNode node, System.Collections.Generic.HashSet<SynthNode> seen)
        {
            if (node == this) return true;
            if (!seen.Add(node)) return false;
            foreach (SynthNode input in node.Inputs)
            {
                if (DependsOnThis(input, seen)) return true;
            }
            foreach (AudioParam p in node.Params)
            {
                foreach (SynthNode modulator in p.Inputs)
                {
                    if (DependsOnThis(modulator, seen)) return true;
                }
            }
            return false;
        }

        internal override void Process(int n)
        {
            DelayTime.Compute(Graph.Frame, n);
            double maxDelay = _size - 2 * SynthGraph.Quantum - 1;
            for (int c = 0; c < OutChannels; c++)
            {
                float[] ring = _ring[c];
                float[] output = c == 0 ? Out0 : Out1;
                for (int i = 0; i < n; i++)
                {
                    // a single-precision product like Chromium's (0.01 s is exactly 480 frames at 48 kHz); the cast stops a runtime that works wider from landing a hair off
                    double delay = (float)((DelayTime.IsConstant ? DelayTime.Constant : DelayTime.Values[i]) * (float)Graph.SampleRate);
                    delay = Math.Min(maxDelay, Math.Max(SynthGraph.Quantum, delay));
                    double at = _written + i - delay;
                    long whole = (long)Math.Floor(at);
                    float frac = (float)(at - whole);
                    float a = whole < 0 ? 0 : ring[(int)(whole % _size)];
                    float b = whole + 1 < 0 ? 0 : ring[(int)((whole + 1) % _size)];
                    output[i] = a + (b - a) * frac;
                }
            }
        }

        internal override void Commit(int n)
        {
            ClearInput(n);
            for (int j = 0; j < Inputs.Count; j++)
            {
                SynthNode input = Inputs[j];
                if (!_closesLoop[j])
                {
                    AddInput(input.Out0, input.Out1, input.OutChannels, n);
                    continue;
                }
                // the input that closes a loop is last block's; this block's is kept for the next
                if (_late0[j] != null) AddInput(_late0[j], _late1[j], input.OutChannels, n);
                else
                {
                    _late0[j] = new float[SynthGraph.Quantum];
                    _late1[j] = input.OutChannels == 2 ? new float[SynthGraph.Quantum] : null;
                }
                Array.Copy(input.Out0, _late0[j], n);
                if (input.OutChannels == 2) Array.Copy(input.Out1, _late1[j], n);
            }
            for (int c = 0; c < OutChannels; c++)
            {
                float[] ring = _ring[c];
                float[] input = c == 0 ? In0 : In1;
                for (int i = 0; i < n; i++) ring[(int)((_written + i) % _size)] = input[i];
            }
            // an unconnected delay still moves on
            _written += n;
        }
    }

    /// <summary>A dynamics compressor in the graph (Web Audio's, see <see cref="DynamicsCompressor"/>), one or two channels.</summary>
    public sealed class DynamicsCompressorNode : SynthNode
    {
        public readonly AudioParam Threshold, Knee, Ratio, Attack, Release;

        DynamicsCompressor _mono, _stereo;

        internal DynamicsCompressorNode(SynthGraph graph) : base(graph)
        {
            Threshold = NewParam(-24);
            Knee = NewParam(30);
            Ratio = NewParam(12);
            Attack = NewParam(0.003);
            Release = NewParam(0.25);
        }

        internal override int OutputChannels(int inputChannels) => Math.Min(2, inputChannels);

        internal override void Process(int n)
        {
            if (Inputs.Count == 0)
            {
                Array.Clear(Out0, 0, n);
                if (OutChannels == 2) Array.Clear(Out1, 0, n);
                return;
            }
            SumInputs(n);
            long f0 = Graph.Frame;
            Threshold.Compute(f0, n);
            Knee.Compute(f0, n);
            Ratio.Compute(f0, n);
            Attack.Compute(f0, n);
            Release.Compute(f0, n);
            DynamicsCompressor c;
            if (OutChannels == 2) c = _stereo ?? (_stereo = new DynamicsCompressor(Graph.SampleRate, 2));
            else c = _mono ?? (_mono = new DynamicsCompressor(Graph.SampleRate, 1));
            // the parameters are read once a block
            c.Threshold = Threshold.IsConstant ? Threshold.Constant : Threshold.Values[0];
            c.Knee = Knee.IsConstant ? Knee.Constant : Knee.Values[0];
            c.Ratio = Ratio.IsConstant ? Ratio.Constant : Ratio.Values[0];
            c.Attack = Attack.IsConstant ? Attack.Constant : Attack.Values[0];
            c.Release = Release.IsConstant ? Release.Constant : Release.Values[0];
            Array.Copy(In0, Out0, n);
            if (OutChannels == 2)
            {
                Array.Copy(In1, Out1, n);
                c.Process(Out0, Out1, n);
            }
            else c.Process(Out0, null, n);
        }
    }

    /// <summary>Places a mono sound between two speakers with the equal-power law.</summary>
    public sealed class StereoPannerNode : SynthNode
    {
        public readonly AudioParam Pan;

        internal StereoPannerNode(SynthGraph graph, double pan) : base(graph)
        {
            Pan = NewParam(pan);
        }

        internal override int OutputChannels(int inputChannels)
        {
            if (inputChannels > 1) throw new NotSupportedException("the stereo panner takes a mono input here");
            return 2;
        }

        internal override void Process(int n)
        {
            if (Inputs.Count == 0)
            {
                Array.Clear(Out0, 0, n);
                Array.Clear(Out1, 0, n);
                return;
            }
            SumInputs(n);
            Pan.Compute(Graph.Frame, n);
            const double quarter = Math.PI / 2;
            if (Pan.IsConstant)
            {
                double x = (Math.Max(-1, Math.Min(1, Pan.Constant)) + 1) * 0.5;
                float l = (float)Math.Cos(x * quarter), r = (float)Math.Sin(x * quarter);
                for (int i = 0; i < n; i++)
                {
                    Out0[i] = In0[i] * l;
                    Out1[i] = In0[i] * r;
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    double x = (Math.Max(-1, Math.Min(1, Pan.Values[i])) + 1) * 0.5;
                    Out0[i] = In0[i] * (float)Math.Cos(x * quarter);
                    Out1[i] = In0[i] * (float)Math.Sin(x * quarter);
                }
            }
        }
    }

    /// <summary>
    /// Plays sounds that were rendered elsewhere, each from a stated frame: how the score's notes (rendered a little ahead on another thread) join the live graph. Scheduling is safe
    /// from any thread; the graph's thread mixes them in.
    /// </summary>
    public sealed class QueuedPlayerNode : SynthNode
    {
        struct Playing
        {
            public long Start;
            public float[] Data;
            public float Gain;
        }

        readonly ConcurrentQueue<Playing> _incoming = new ConcurrentQueue<Playing>();
        Playing[] _active = new Playing[16];
        int _count;

        internal QueuedPlayerNode(SynthGraph graph) : base(graph) { }

        internal override bool HasInput => false;

        /// <summary>Play <paramref name="data"/> from <paramref name="startFrame"/> (a frame already past starts at once, part-way in).</summary>
        public void Schedule(long startFrame, float[] data, float gain = 1f)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            _incoming.Enqueue(new Playing { Start = startFrame, Data = data, Gain = gain });
        }

        internal override void Process(int n)
        {
            while (_incoming.TryDequeue(out Playing p))
            {
                if (_count == _active.Length) Array.Resize(ref _active, _active.Length * 2);
                _active[_count++] = p;
            }
            Array.Clear(Out0, 0, n);
            long f0 = Graph.Frame;
            for (int k = 0; k < _count; k++)
            {
                Playing p = _active[k];
                long offset = f0 - p.Start;
                if (offset + n <= 0) continue;
                int from = (int)Math.Max(0, -offset);
                long dataFrom = offset + from;
                long left = p.Data.Length - dataFrom;
                int count = (int)Math.Min(n - from, left);
                for (int i = 0; i < count; i++) Out0[from + i] += p.Data[dataFrom + i] * p.Gain;
            }
            // finished sounds leave
            int kept = 0;
            for (int k = 0; k < _count; k++)
            {
                if (f0 + n < _active[k].Start + _active[k].Data.Length) _active[kept++] = _active[k];
            }
            for (int k = kept; k < _count; k++) _active[k] = default(Playing);
            _count = kept;
        }
    }
}
