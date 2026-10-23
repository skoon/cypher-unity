using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Cypher.Core
{
    /// <summary>
    /// The score's live parts as a synthesis graph: the pad (six detuned oscillators through a moving low-pass), the sidechain duck every sustained part passes through, the dotted-eighth delay for
    /// the arpeggio, the stab and the lead, and three players for notes rendered elsewhere: straight to the output (drums), through the duck (bass), or through the delay and then the duck
    /// (everything else). It is the prototype's Music.start. Changes the score asks for are queued from any thread and applied on the graph's own thread, between blocks.
    /// </summary>
    public sealed class ScoreGraph
    {
        const double BeatSeconds = 60.0 / Score.Bpm;

        readonly ConcurrentQueue<ScoreChange> _changes = new ConcurrentQueue<ScoreChange>();
        readonly OscillatorNode[] _pad = new OscillatorNode[6];
        readonly GainNode _padGain, _delayWet, _duck;
        readonly BiquadNode _padFilter;

        public SynthGraph Graph { get; }

        /// <summary>Notes that go straight to the output.</summary>
        public QueuedPlayerNode Direct { get; }

        /// <summary>Notes that go through the sidechain duck (the bass).</summary>
        public QueuedPlayerNode ThroughDuck { get; }

        /// <summary>Notes that go through the delay and then the duck.</summary>
        public QueuedPlayerNode ThroughDelay { get; }

        public ScoreGraph(int sampleRate)
        {
            SynthGraph g = new SynthGraph(sampleRate);
            Graph = g;

            // the sidechain: everything sustained pumps under the kick
            _duck = g.CreateGain(1);
            _duck.Connect(g.Destination);

            // the dotted-eighth delay for the arp and the lead
            GainNode delayIn = g.CreateGain(1);
            _delayWet = g.CreateGain(0.32);
            DelayNode d = g.CreateDelay(BeatSeconds * 0.75, 1.5);
            GainNode fb = g.CreateGain(0.36);
            BiquadNode damp = g.CreateBiquad(BiquadType.Lowpass, 2600, 0.7);
            delayIn.Connect(_duck);
            delayIn.Connect(d);
            d.Connect(damp);
            damp.Connect(fb);
            fb.Connect(d);
            d.Connect(_delayWet);
            _delayWet.Connect(_duck);

            // the pad: six detuned oscillators, retuned on every chord change
            _padFilter = g.CreateBiquad(BiquadType.Lowpass, 500, 1.6);
            _padGain = g.CreateGain(0.0001);
            _padFilter.Connect(_padGain);
            _padGain.Connect(_duck);
            double t = 0.05;
            for (int i = 0; i < _pad.Length; i++)
            {
                OscillatorNode o = g.CreateOscillator(WaveKind.SoftSaw, Score.PadStartFrequency(i));
                GainNode gv = g.CreateGain(0.17);
                o.Connect(gv);
                gv.Connect(_padFilter);
                o.Start(t);
                _pad[i] = o;
            }
            // a slow chorus on the pad filter keeps it from sounding static
            OscillatorNode lfo = g.CreateOscillator(WaveKind.Sine, 0.053);
            GainNode lg = g.CreateGain(180);
            lfo.Connect(lg);
            lg.Connect(_padFilter.Frequency);
            lfo.Start(t);

            Direct = g.CreateQueuedPlayer();
            Direct.Connect(g.Destination);
            ThroughDuck = g.CreateQueuedPlayer();
            ThroughDuck.Connect(_duck);
            ThroughDelay = g.CreateQueuedPlayer();
            ThroughDelay.Connect(delayIn);
        }

        /// <summary>The player a note of this feed goes to.</summary>
        public QueuedPlayerNode PlayerFor(ScoreFeed feed)
        {
            switch (feed)
            {
                case ScoreFeed.Duck: return ThroughDuck;
                case ScoreFeed.Delay: return ThroughDelay;
                default: return Direct;
            }
        }

        /// <summary>Queue changes to the pad, delay and sidechain; safe from any thread.</summary>
        public void Enqueue(ScoreChange change) => _changes.Enqueue(change);

        /// <summary>Apply what is queued. Call on the graph's thread, before a block.</summary>
        public void ApplyPending()
        {
            while (_changes.TryDequeue(out ScoreChange c)) Apply(c);
        }

        void Apply(ScoreChange c)
        {
            switch (c.Kind)
            {
                case ScoreChangeKind.PadGlide:
                    _pad[c.Index].Frequency.SetTargetAtTime(c.Value, c.Time, c.Tau);
                    break;
                case ScoreChangeKind.PadGain:
                    _padGain.Gain.SetTargetAtTime(Math.Max(0.0001, c.Value), c.Time, c.Tau);
                    break;
                case ScoreChangeKind.PadFilter:
                    _padFilter.Frequency.SetTargetAtTime(c.Value, c.Time, c.Tau);
                    break;
                case ScoreChangeKind.DelayWet:
                    _delayWet.Gain.SetTargetAtTime(c.Value, c.Time, c.Tau);
                    break;
                case ScoreChangeKind.DuckPump:
                    _duck.Gain.CancelScheduledValues(c.Time);
                    _duck.Gain.SetValueAtTime(c.Value, c.Time);
                    _duck.Gain.SetTargetAtTime(1, c.Time + 0.005, c.Tau);
                    break;
            }
        }
    }

    /// <summary>
    /// The sound of one car or truck, from its speed: a sawtooth engine note through a low-pass, and tyre hiss (noise through a band-pass) that rises with speed and with the wet. The speed
    /// and the wet are handed over as plain numbers and the sound follows them over about a sixth of a second.
    /// </summary>
    public sealed class CarVoiceGraph
    {
        const double Tau = 0.15;

        readonly OscillatorNode _engine;
        readonly GainNode _engineGain, _tyreGain;
        readonly BiquadNode _tyreBand;
        double _appliedStamp = -1;

        public SynthGraph Graph { get; }

        public CarVoiceGraph(AudioNoiseBank noise, Rng rng)
        {
            SynthGraph g = new SynthGraph(noise.SampleRate);
            Graph = g;
            _engine = g.CreateOscillator(WaveKind.SoftSaw, 80);
            _engine.Start(0);
            BiquadNode lp = g.CreateBiquad(BiquadType.Lowpass, 420, 2.2);
            _engineGain = g.CreateGain(0.0001);
            SynthNode.Chain(_engine, lp, _engineGain).Connect(g.Destination);

            float[] bed = noise.Get(NoiseKind.White);
            BufferSourceNode s = g.CreateBufferSource(bed);
            s.PlaybackRate.Value = 0.9 + rng.Next() * 0.3;
            s.Start(0, rng.Next() * (bed.Length / (double)g.SampleRate - 0.2));
            _tyreBand = g.CreateBiquad(BiquadType.Bandpass, 1400, 0.9);
            _tyreGain = g.CreateGain(0.0001);
            SynthNode.Chain(s, _tyreBand, _tyreGain).Connect(g.Destination);
            g.CoefficientInterval = 32;
        }

        /// <summary>Head for a sound. <paramref name="stamp"/> says when it was decided: only a newer one is applied. Call on the graph's own thread.</summary>
        public void Apply(VehicleSound sound, double stamp)
        {
            if (stamp == _appliedStamp) return;
            _appliedStamp = stamp;
            double now = Graph.Time;
            _engine.Frequency.SetTargetAtTime(sound.EngineHz, now, Tau);
            _engineGain.Gain.SetTargetAtTime(Math.Max(0.0001, sound.EngineGain * 0.5), now, Tau);
            _tyreBand.Frequency.SetTargetAtTime(sound.TyreHz, now, Tau);
            _tyreGain.Gain.SetTargetAtTime(Math.Max(0.0001, sound.TyreGain), now, Tau);
        }
    }
}
