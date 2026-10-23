using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// A parameter of a synthesis node, shaped like Web Audio's AudioParam: a value over time made from scheduled events (set a value, ramp to one linearly or exponentially,
    /// approach one with a time constant), with other nodes' output added on top. Values are worked out per sample in doubles and handed to the node as a block.
    /// Not thread-safe: a graph belongs to one thread at a time.
    /// </summary>
    public sealed class AudioParam
    {
        enum Kind { Set, Linear, Exponential, Target }

        struct Event
        {
            public Kind Kind;
            public double Time;
            public double Value;
            public double TimeConstant;
            /// <summary>For a target event: the value the timeline had where it began, once worked out (so older events can be dropped).</summary>
            public double StartValue;
            public bool HasStartValue;
        }

        readonly SynthGraph _graph;
        readonly List<Event> _events = new List<Event>(8);
        double _base;

        internal readonly List<SynthNode> Inputs = new List<SynthNode>(2);

        /// <summary>This block's values; only meaningful when <see cref="IsConstant"/> is false.</summary>
        internal float[] Values;

        internal bool IsConstant = true;
        internal float Constant;

        internal AudioParam(SynthGraph graph, double initial)
        {
            _graph = graph;
            _base = initial;
            Constant = (float)initial;
            Values = new float[SynthGraph.Quantum];
        }

        /// <summary>The value when nothing is scheduled. Setting it while events are scheduled is the same as setting a value at the present time.</summary>
        public double Value
        {
            get => _base;
            set
            {
                if (_events.Count == 0) _base = value;
                else SetValueAtTime(value, _graph.Time);
            }
        }

        public bool HasEvents => _events.Count > 0;

        public AudioParam SetValueAtTime(double value, double time) => Add(new Event { Kind = Kind.Set, Time = time, Value = value });

        public AudioParam LinearRampToValueAtTime(double value, double time) => Add(new Event { Kind = Kind.Linear, Time = time, Value = value });

        public AudioParam ExponentialRampToValueAtTime(double value, double time)
        {
            if (value == 0) throw new ArgumentOutOfRangeException(nameof(value), "an exponential ramp cannot end at zero");
            return Add(new Event { Kind = Kind.Exponential, Time = time, Value = value });
        }

        /// <summary>Approach <paramref name="target"/> from <paramref name="start"/> with the given time constant (seconds): the gap closes by 63% each time constant.</summary>
        public AudioParam SetTargetAtTime(double target, double start, double timeConstant)
        {
            if (timeConstant < 0) throw new ArgumentOutOfRangeException(nameof(timeConstant), "a time constant cannot be negative");
            return Add(new Event { Kind = Kind.Target, Time = start, Value = target, TimeConstant = timeConstant });
        }

        /// <summary>Forget every event at or after <paramref name="time"/>.</summary>
        public AudioParam CancelScheduledValues(double time)
        {
            int keep = 0;
            while (keep < _events.Count && _events[keep].Time < time) keep++;
            // a target event that remains must know where it began before what it began from is gone
            _events.RemoveRange(keep, _events.Count - keep);
            return this;
        }

        AudioParam Add(Event e)
        {
            if (double.IsNaN(e.Time) || double.IsNaN(e.Value)) throw new ArgumentException("an event needs a time and a value");
            if (e.Time < 0) throw new ArgumentOutOfRangeException(nameof(e), "events cannot be before time zero");
            if (e.Kind == Kind.Linear || e.Kind == Kind.Exponential)
            {
                int last = _events.Count - 1;
                if (last >= 0 && _events[last].Kind == Kind.Target && _events[last].Time <= e.Time)
                    throw new NotSupportedException("a ramp that follows a setTarget is not supported");
            }
            int at = _events.Count;
            while (at > 0 && _events[at - 1].Time > e.Time) at--;
            // an event of the same kind at the same moment replaces the one before it
            if (at > 0 && _events[at - 1].Time == e.Time && _events[at - 1].Kind == e.Kind)
            {
                _events[at - 1] = e;
                return this;
            }
            _events.Insert(at, e);
            return this;
        }

        /// <summary>The value the schedule gives at a time, not counting other nodes' output.</summary>
        public double ValueAt(double time)
        {
            int k = LastAtOrBefore(time);
            return Segment(k, time);
        }

        int LastAtOrBefore(double time)
        {
            int k = -1;
            for (int i = 0; i < _events.Count && _events[i].Time <= time; i++) k = i;
            return k;
        }

        /// <summary>The value at <paramref name="time"/> given that the latest event at or before it is number <paramref name="k"/>.</summary>
        double Segment(int k, double time)
        {
            if (k + 1 < _events.Count)
            {
                Event next = _events[k + 1];
                if (next.Kind == Kind.Linear || next.Kind == Kind.Exponential)
                {
                    double t0, v0;
                    if (k < 0) { t0 = 0; v0 = _base; }
                    else { t0 = _events[k].Time; v0 = HoldEnd(k); }
                    double f = next.Time > t0 ? (time - t0) / (next.Time - t0) : 1;
                    if (next.Kind == Kind.Linear) return v0 + (next.Value - v0) * f;
                    // opposite signs or a zero start cannot be ramped between exponentially: the start value holds until the end
                    if (v0 == 0 || (v0 < 0) != (next.Value < 0)) return v0;
                    return v0 * Math.Pow(next.Value / v0, f);
                }
            }
            if (k < 0) return _base;
            return Hold(k, time);
        }

        /// <summary>The value an event leaves when nothing follows to ramp towards (its own value, or the target curve at that time).</summary>
        double Hold(int k, double time)
        {
            Event e = _events[k];
            if (e.Kind != Kind.Target) return e.Value;
            double v0 = StartValue(k);
            if (e.TimeConstant <= 0) return e.Value;
            return e.Value + (v0 - e.Value) * Math.Exp(-(time - e.Time) / e.TimeConstant);
        }

        /// <summary>The value an event leaves behind it for a ramp that begins there.</summary>
        double HoldEnd(int k)
        {
            Event e = _events[k];
            if (e.Kind == Kind.Target) throw new NotSupportedException("a ramp that follows a setTarget is not supported");
            return e.Value;
        }

        /// <summary>Where a target event began: the schedule's value at its time, from the events before it.</summary>
        double StartValue(int k)
        {
            Event e = _events[k];
            if (e.HasStartValue) return e.StartValue;
            if (k == 0) return _base;
            return Hold(k - 1, e.Time);
        }

        /// <summary>Worked out for a block that begins at frame <paramref name="start"/>: leaves <see cref="Constant"/> or <see cref="Values"/> ready, other nodes' output added.</summary>
        internal void Compute(long start, int n)
        {
            int rate = _graph.SampleRate;
            double t0 = start / (double)rate;
            bool constant = true;
            int k = LastAtOrBefore(t0);

            // events older than the block only matter through the one in force: drop the rest, once it knows where it began
            if (k > 0)
            {
                if (_events[k].Kind == Kind.Target)
                {
                    Event e = _events[k];
                    e.StartValue = StartValue(k);
                    e.HasStartValue = true;
                    _events[k] = e;
                }
                _events.RemoveRange(0, k);
                k = 0;
            }

            if (_events.Count == 0)
            {
                Constant = (float)_base;
            }
            else
            {
                constant = FillTimeline(k, start, n, t0, rate);
            }

            if (Inputs.Count > 0)
            {
                if (constant)
                {
                    float c = Constant;
                    for (int i = 0; i < n; i++) Values[i] = c;
                    constant = false;
                }
                for (int j = 0; j < Inputs.Count; j++)
                {
                    SynthNode input = Inputs[j];
                    float[] a = input.Out0;
                    if (input.OutChannels == 2)
                    {
                        float[] b = input.Out1;
                        for (int i = 0; i < n; i++) Values[i] += (a[i] + b[i]) * 0.5f;
                    }
                    else
                    {
                        for (int i = 0; i < n; i++) Values[i] += a[i];
                    }
                }
            }
            IsConstant = constant;
        }

        /// <summary>Fills <see cref="Constant"/> (and returns true) if the whole block has one value, else <see cref="Values"/>.</summary>
        bool FillTimeline(int k, long start, int n, double t0, int rate)
        {
            double dt = 1.0 / rate;
            // the common case: the block lies within a hold
            double blockEnd = (start + n) / (double)rate;
            bool nextIsRamp = k + 1 < _events.Count && (_events[k + 1].Kind == Kind.Linear || _events[k + 1].Kind == Kind.Exponential);
            if (!nextIsRamp && (k < 0 || _events[k].Kind != Kind.Target) && (k + 1 >= _events.Count || _events[k + 1].Time >= blockEnd))
            {
                Constant = (float)(k < 0 ? _base : _events[k].Value);
                return true;
            }

            // a target that has all but arrived, to the float a block is made of, holds there: the gap only narrows, so a block that starts on the target ends on it
            if (k >= 0 && !nextIsRamp && _events[k].Kind == Kind.Target && _events[k].TimeConstant > 0 && (k + 1 >= _events.Count || _events[k + 1].Time >= blockEnd))
            {
                Event e = _events[k];
                double gap = (StartValue(k) - e.Value) * Math.Exp(-(t0 - e.Time) / e.TimeConstant);
                float arrived = (float)e.Value;
                if ((float)(e.Value + gap) == arrived)
                {
                    Constant = arrived;
                    return true;
                }
            }

            int i = 0;
            while (i < n)
            {
                double t = (start + i) / (double)rate;
                // the segment in force at this sample, and the first sample it does not cover
                while (k + 1 < _events.Count && _events[k + 1].Time <= t) k++;
                int end = n;
                if (k + 1 < _events.Count) end = Math.Min(n, Math.Max(i + 1, FirstFrameAtOrAfter(_events[k + 1].Time, start, rate)));

                Event next = k + 1 < _events.Count ? _events[k + 1] : default(Event);
                bool ramp = k + 1 < _events.Count && IsRamp(next.Kind);
                if (ramp)
                {
                    double ta, va;
                    if (k < 0) { ta = 0; va = _base; }
                    else { ta = _events[k].Time; va = HoldEnd(k); }
                    double span = next.Time - ta;
                    if (next.Kind == Kind.Linear)
                    {
                        for (int s = i; s < end; s++)
                        {
                            double f = span > 0 ? ((start + s) * dt - ta) / span : 1;
                            Values[s] = (float)(va + (next.Value - va) * f);
                        }
                    }
                    else if (va == 0 || (va < 0) != (next.Value < 0))
                    {
                        for (int s = i; s < end; s++) Values[s] = (float)va;
                    }
                    else
                    {
                        double ratio = next.Value / va;
                        double f0 = span > 0 ? ((start + i) * dt - ta) / span : 1;
                        double v = va * Math.Pow(ratio, f0);
                        double step = span > 0 ? Math.Pow(ratio, dt / span) : 1;
                        for (int s = i; s < end; s++)
                        {
                            Values[s] = (float)v;
                            v *= step;
                        }
                    }
                }
                else if (k < 0)
                {
                    float b = (float)_base;
                    for (int s = i; s < end; s++) Values[s] = b;
                }
                else if (_events[k].Kind == Kind.Target && _events[k].TimeConstant > 0)
                {
                    Event e = _events[k];
                    double v0 = StartValue(k);
                    double gap = (v0 - e.Value) * Math.Exp(-(t - e.Time) / e.TimeConstant);
                    double decay = Math.Exp(-dt / e.TimeConstant);
                    for (int s = i; s < end; s++)
                    {
                        Values[s] = (float)(e.Value + gap);
                        gap *= decay;
                    }
                }
                else
                {
                    float v = (float)_events[k].Value;
                    for (int s = i; s < end; s++) Values[s] = v;
                }
                i = end;
            }
            return false;
        }

        /// <summary>The offset into the block (it may be past its end) of the first frame whose time is at or after <paramref name="time"/>.</summary>
        static int FirstFrameAtOrAfter(double time, long start, int rate)
        {
            long frame = (long)Math.Ceiling(time * rate);
            while (frame > 0 && (frame - 1) / (double)rate >= time) frame--;
            while (frame / (double)rate < time) frame++;
            return (int)Math.Min(int.MaxValue, Math.Max(0, frame - start));
        }

        static bool IsRamp(Kind kind) => kind == Kind.Linear || kind == Kind.Exponential;
    }
}
