using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// A node of a synthesis graph, shaped like Web Audio's: sound flows from the nodes connected to its input, and its parameters can have other nodes connected to them. Output is a
    /// block of <see cref="SynthGraph.Quantum"/> frames in one or two channels.
    /// </summary>
    public abstract class SynthNode
    {
        protected readonly SynthGraph Graph;
        internal readonly List<SynthNode> Inputs = new List<SynthNode>(2);
        internal readonly List<AudioParam> Params = new List<AudioParam>(2);
        internal float[] Out0, Out1;
        internal float[] In0, In1;
        internal int OutChannels = 1;
        internal int InChannels = 1;
        internal int VisitState;
        internal int OrderIndex = -1;

        protected SynthNode(SynthGraph graph)
        {
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            graph.Register(this);
        }

        /// <summary>True for a node whose output does not depend on this block's input (a delay), which is what lets a loop of nodes be rendered.</summary>
        internal virtual bool SplitsCycle => false;

        /// <summary>The fewest channels a node's input is mixed to.</summary>
        internal virtual int MinInputChannels => 1;

        /// <summary>The channels a node outputs given the most channels any input carries.</summary>
        internal virtual int OutputChannels(int inputChannels) => inputChannels;

        internal abstract void Process(int n);

        /// <summary>After every node has been processed: a delay takes in this block's input.</summary>
        internal virtual void Commit(int n) { }

        protected AudioParam NewParam(double initial)
        {
            var p = new AudioParam(Graph, initial);
            Params.Add(p);
            return p;
        }

        /// <summary>Send this node's output to another's input. Connecting the same pair twice does nothing. Returns the destination so connections can be chained.</summary>
        public SynthNode Connect(SynthNode destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (destination.Graph != Graph) throw new ArgumentException("the nodes belong to different graphs", nameof(destination));
            if (destination is OscillatorNode || destination is BufferSourceNode || destination is ConstantSourceNode)
                throw new ArgumentException("a source has no input to connect to", nameof(destination));
            if (!destination.Inputs.Contains(this)) destination.Inputs.Add(this);
            Graph.MarkDirty();
            return destination;
        }

        /// <summary>Send this node's output into a parameter, added to the value the schedule gives.</summary>
        public void Connect(AudioParam param)
        {
            if (param == null) throw new ArgumentNullException(nameof(param));
            if (!param.Inputs.Contains(this)) param.Inputs.Add(this);
            Graph.MarkDirty();
        }

        /// <summary>Connects the nodes in order and returns the last.</summary>
        public static SynthNode Chain(params SynthNode[] nodes)
        {
            SynthNode last = null;
            foreach (SynthNode n in nodes)
            {
                if (n == null) continue;
                last?.Connect(n);
                last = n;
            }
            return last;
        }

        /// <summary>Adds up the inputs into <see cref="In0"/> (and <see cref="In1"/>), a mono input in both channels of a stereo node.</summary>
        protected void SumInputs(int n)
        {
            ClearInput(n);
            for (int j = 0; j < Inputs.Count; j++) AddInput(Inputs[j].Out0, Inputs[j].Out1, Inputs[j].OutChannels, n);
        }

        protected void ClearInput(int n)
        {
            Array.Clear(In0, 0, n);
            if (InChannels == 2) Array.Clear(In1, 0, n);
        }

        /// <summary>Adds one input's block (<paramref name="x1"/> is null when it is mono) into the sum.</summary>
        protected void AddInput(float[] x0, float[] x1, int channels, int n)
        {
            float[] a = In0;
            float[] b = InChannels == 2 ? In1 : null;
            if (channels == 2)
            {
                if (b != null)
                {
                    for (int i = 0; i < n; i++) { a[i] += x0[i]; b[i] += x1[i]; }
                }
                else
                {
                    for (int i = 0; i < n; i++) a[i] += (x0[i] + x1[i]) * 0.5f;
                }
            }
            else
            {
                for (int i = 0; i < n; i++) a[i] += x0[i];
                if (b != null)
                {
                    for (int i = 0; i < n; i++) b[i] += x0[i];
                }
            }
        }

        internal void AllocateBuffers(int quantum)
        {
            if (Out0 == null) Out0 = new float[quantum];
            if (OutChannels == 2 && Out1 == null) Out1 = new float[quantum];
            if (HasInput)
            {
                if (In0 == null) In0 = new float[quantum];
                if (InChannels == 2 && In1 == null) In1 = new float[quantum];
            }
        }

        /// <summary>Whether the node has data inputs to sum (a source has none).</summary>
        internal virtual bool HasInput => true;
    }

    /// <summary>Where a graph's time goes: the Stopwatch ticks spent in each kind of node, and how many times each kind was run (see <see cref="SynthGraph.Profile"/>).</summary>
    public sealed class GraphProfile
    {
        public readonly Dictionary<string, long> Ticks = new Dictionary<string, long>();
        public readonly Dictionary<string, int> Runs = new Dictionary<string, int>();
        public int Blocks;
    }

    /// <summary>
    /// A graph of synthesis nodes rendered in blocks of 128 frames, offline into arrays or live into a device buffer, without allocating once built. It does what the prototype asks of
    /// Web Audio: noise loops and oscillators through filters, gains and delays, with automation, and the recipes are written against it as they were against the browser's.
    /// A graph belongs to one thread at a time.
    /// </summary>
    public sealed class SynthGraph
    {
        public const int Quantum = 128;

        readonly List<SynthNode> _nodes = new List<SynthNode>();
        readonly List<SynthNode> _order = new List<SynthNode>();
        readonly List<SynthNode> _delays = new List<SynthNode>();
        bool _dirty = true;

        float[] _carryLeft, _carryRight;
        int _carryPos, _carryCount;

        public SynthGraph(int sampleRate)
        {
            if (sampleRate < 8000 || sampleRate > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "not a sample rate");
            SampleRate = sampleRate;
            Destination = new DestinationNode(this);
        }

        public int SampleRate { get; }

        long _frame;

        /// <summary>The frame the next block starts at. Safe to read from another thread than the one rendering.</summary>
        public long Frame => System.Threading.Volatile.Read(ref _frame);

        /// <summary>Seconds of sound rendered so far; the present, as far as scheduling is concerned.</summary>
        public double Time => Frame / (double)SampleRate;

        public DestinationNode Destination { get; }

        /// <summary>How many frames a filter keeps its coefficients for while one of its parameters is moving. One, as in Web Audio, is exact and dearest; a live graph with slow modulators can use more.</summary>
        public int CoefficientInterval { get; set; } = 1;

        /// <summary>When set, each block adds the time its nodes took to this, by kind of node. Off (null) by default; it is for finding where a graph's cost goes.</summary>
        public GraphProfile Profile { get; set; }

        internal void Register(SynthNode node)
        {
            _nodes.Add(node);
            _dirty = true;
        }

        internal void MarkDirty() { _dirty = true; }

        public GainNode CreateGain(double gain = 1) => new GainNode(this, gain);

        public WaveShaperNode CreateWaveShaper(float[] curve, bool oversample2x = false) => new WaveShaperNode(this, curve, oversample2x);

        public OscillatorNode CreateOscillator(WaveKind kind = WaveKind.Sine, double frequency = 440) => new OscillatorNode(this, kind, frequency);

        public BufferSourceNode CreateBufferSource(float[] data) => new BufferSourceNode(this, data);

        public BiquadNode CreateBiquad(BiquadType type = BiquadType.Lowpass, double frequency = 350, double q = 1, double gainDb = 0) => new BiquadNode(this, type, frequency, q, gainDb);

        public DelayNode CreateDelay(double seconds, double maxSeconds = 1.5) => new DelayNode(this, seconds, maxSeconds);

        public StereoPannerNode CreateStereoPanner(double pan = 0) => new StereoPannerNode(this, pan);

        public ConstantSourceNode CreateConstantSource(double offset = 1) => new ConstantSourceNode(this, offset);

        public QueuedPlayerNode CreateQueuedPlayer() => new QueuedPlayerNode(this);

        public DynamicsCompressorNode CreateDynamicsCompressor() => new DynamicsCompressorNode(this);

        /// <summary>Works out the order to render nodes in, and how many channels each carries.</summary>
        void Prepare()
        {
            foreach (SynthNode n in _nodes) n.VisitState = 0;
            _order.Clear();
            _delays.Clear();
            Visit(Destination);
            // a delay's input is rendered before the delay takes it in, but nothing waits on it
            for (int i = 0; i < _delays.Count; i++)
            {
                SynthNode d = _delays[i];
                for (int j = 0; j < d.Inputs.Count; j++) Visit(d.Inputs[j]);
            }

            for (int i = 0; i < _order.Count; i++) _order[i].OrderIndex = i;
            foreach (SynthNode d in _delays) ((DelayNode)d).FindBackEdges();

            bool changed = true;
            for (int pass = 0; changed && pass < 8; pass++)
            {
                changed = false;
                foreach (SynthNode n in _order)
                {
                    int inCh = n.MinInputChannels;
                    foreach (SynthNode input in n.Inputs) inCh = Math.Max(inCh, input.OutChannels);
                    int outCh = n.OutputChannels(inCh);
                    if (inCh != n.InChannels || outCh != n.OutChannels)
                    {
                        n.InChannels = inCh;
                        n.OutChannels = outCh;
                        changed = true;
                    }
                }
            }
            foreach (SynthNode n in _order) n.AllocateBuffers(Quantum);
            _dirty = false;
        }

        void Visit(SynthNode n)
        {
            if (n.VisitState != 0) return;
            n.VisitState = 1;
            if (!n.SplitsCycle)
            {
                for (int i = 0; i < n.Inputs.Count; i++) Visit(n.Inputs[i]);
            }
            for (int i = 0; i < n.Params.Count; i++)
            {
                List<SynthNode> modulators = n.Params[i].Inputs;
                for (int j = 0; j < modulators.Count; j++) Visit(modulators[j]);
            }
            _order.Add(n);
            if (n.SplitsCycle) _delays.Add(n);
        }

        /// <summary>Renders one block; the result is in <see cref="Destination"/>'s outputs.</summary>
        public void RenderBlock()
        {
            if (_dirty) Prepare();
            if (Profile != null)
            {
                RenderBlockProfiled(Profile);
                return;
            }
            for (int i = 0; i < _order.Count; i++) _order[i].Process(Quantum);
            for (int i = 0; i < _delays.Count; i++) _delays[i].Commit(Quantum);
            System.Threading.Volatile.Write(ref _frame, _frame + Quantum);
        }

        void RenderBlockProfiled(GraphProfile profile)
        {
            for (int i = 0; i < _order.Count; i++)
            {
                SynthNode node = _order[i];
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                node.Process(Quantum);
                Add(profile, node, System.Diagnostics.Stopwatch.GetTimestamp() - start);
            }
            for (int i = 0; i < _delays.Count; i++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                _delays[i].Commit(Quantum);
                Add(profile, _delays[i], System.Diagnostics.Stopwatch.GetTimestamp() - start);
            }
            profile.Blocks++;
            System.Threading.Volatile.Write(ref _frame, _frame + Quantum);
        }

        static void Add(GraphProfile profile, SynthNode node, long ticks)
        {
            string name = node.GetType().Name;
            profile.Ticks.TryGetValue(name, out long t);
            profile.Ticks[name] = t + ticks;
            profile.Runs.TryGetValue(name, out int r);
            profile.Runs[name] = r + 1;
        }

        /// <summary>Renders from the start, as an offline context does: <paramref name="frames"/> frames of both channels.</summary>
        public void Render(int frames, out float[] left, out float[] right)
        {
            left = new float[frames];
            right = new float[frames];
            RenderInto(left, right, frames);
        }

        /// <summary>The left channel only, for sounds that are mono throughout.</summary>
        public float[] RenderMono(int frames)
        {
            Render(frames, out float[] left, out _);
            return left;
        }

        void RenderInto(float[] left, float[] right, int frames)
        {
            int done = 0;
            while (done < frames)
            {
                RenderBlock();
                int take = Math.Min(Quantum, frames - done);
                Array.Copy(Destination.Out0, 0, left, done, take);
                Array.Copy(Destination.Out1, 0, right, done, take);
                done += take;
            }
        }

        /// <summary>
        /// Fills a device buffer of interleaved samples, whatever its length (a block left over is kept for the next call). Channels beyond the second are silent.
        /// </summary>
        public void Fill(float[] data, int channels, int frames, bool add = false)
        {
            if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels), channels, "no channels");
            if (frames * channels > data.Length) throw new ArgumentException("the buffer is too short", nameof(data));
            int written = 0;
            while (written < frames)
            {
                if (_carryCount == 0)
                {
                    RenderBlock();
                    if (_carryLeft == null)
                    {
                        _carryLeft = new float[Quantum];
                        _carryRight = new float[Quantum];
                    }
                    Array.Copy(Destination.Out0, _carryLeft, Quantum);
                    Array.Copy(Destination.Out1, _carryRight, Quantum);
                    _carryPos = 0;
                    _carryCount = Quantum;
                }
                int take = Math.Min(_carryCount, frames - written);
                for (int i = 0; i < take; i++)
                {
                    int o = (written + i) * channels;
                    float l = _carryLeft[_carryPos + i];
                    float r = _carryRight[_carryPos + i];
                    if (channels == 1)
                    {
                        float m = (l + r) * 0.5f;
                        data[o] = add ? data[o] + m : m;
                    }
                    else
                    {
                        data[o] = add ? data[o] + l : l;
                        data[o + 1] = add ? data[o + 1] + r : r;
                        for (int c = 2; c < channels; c++) data[o + c] = add ? data[o + c] : 0;
                    }
                }
                _carryPos += take;
                _carryCount -= take;
                written += take;
            }
        }

        /// <summary>Frame number of a time (seconds), to the nearest frame.</summary>
        public long FrameOf(double seconds) => (long)Math.Floor(seconds * SampleRate + 0.5);
    }
}
