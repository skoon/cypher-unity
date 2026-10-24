using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cypher.Core
{
    /// <summary>One frame of the benchmark. A zero CPU/GPU time or a negative count means "not available".</summary>
    public readonly struct FrameSample
    {
        public readonly double TimeSeconds;
        public readonly double FrameMs;
        public readonly double CpuMs;
        public readonly double GpuMs;
        public readonly long DrawCalls;
        public readonly long Triangles;
        public readonly double X, Z;

        /// <summary>Bytes the main thread allocated this frame (garbage to collect); negative when the platform could not say.</summary>
        public readonly long AllocBytes;

        /// <summary>Whether a garbage collection ran during this frame.</summary>
        public readonly bool Collected;

        public FrameSample(double timeSeconds, double frameMs, double cpuMs, double gpuMs, long drawCalls, long triangles, double x, double z, long allocBytes = -1, bool collected = false)
        {
            AllocBytes = allocBytes;
            Collected = collected;
            TimeSeconds = timeSeconds;
            FrameMs = frameMs;
            CpuMs = cpuMs;
            GpuMs = gpuMs;
            DrawCalls = drawCalls;
            Triangles = triangles;
            X = x;
            Z = z;
        }
    }

    public enum BenchmarkVerdict
    {
        Pass,

        /// <summary>The run was valid but p99 frame time is over the budget.</summary>
        Fail,

        /// <summary>The run did not measure what it should have: no frames, or the player barely moved.</summary>
        Invalid,
    }

    /// <summary>One counter the game's systems reported every frame: the number of people drawn, say.</summary>
    public readonly struct CounterSummary
    {
        public readonly string Name;
        public readonly double Average, Max, Last;

        public CounterSummary(string name, double average, double max, double last)
        {
            Name = name;
            Average = average;
            Max = max;
            Last = last;
        }
    }

    public sealed class BenchmarkSummary
    {
        public int Frames { get; internal set; }
        public double DurationSeconds { get; internal set; }
        public double AverageFps { get; internal set; }
        public double AverageMs { get; internal set; }
        public double P50Ms { get; internal set; }
        public double P95Ms { get; internal set; }
        public double P99Ms { get; internal set; }
        public double WorstMs { get; internal set; }
        public int FramesOverBudget { get; internal set; }
        public double FloorFps { get; internal set; }

        /// <summary>Zero when the platform gave no timings.</summary>
        public double CpuAverageMs { get; internal set; }
        public double GpuAverageMs { get; internal set; }
        public double GpuP99Ms { get; internal set; }

        /// <summary>Negative when the counters were not available (release player).</summary>
        public double DrawCallsAverage { get; internal set; }
        public long DrawCallsMax { get; internal set; }
        public double TrianglesAverage { get; internal set; }
        public long TrianglesMax { get; internal set; }

        public double DistanceMetres { get; internal set; }

        /// <summary>Seconds the pilot was held up by something in its way, and how many times it stepped aside.</summary>
        public double StalledSeconds { get; internal set; }
        public int Detours { get; internal set; }

        /// <summary>Bytes allocated a frame on the main thread, on average and at most; negative when not available.</summary>
        public double AllocAverageBytes { get; internal set; } = -1;
        public long AllocMaxBytes { get; internal set; } = -1;

        /// <summary>Frames in which a garbage collection ran.</summary>
        public int Collections { get; internal set; }

        /// <summary>What the game's systems reported each frame (people drawn, vehicles drawn, the sound's cost), in the order they were first reported.</summary>
        public IReadOnlyList<CounterSummary> Counters { get; internal set; } = Array.Empty<CounterSummary>();

        public BenchmarkVerdict Verdict { get; internal set; }

        /// <summary>Key/value lines, one per field, suitable for a log or a results file.</summary>
        public string ToText(IEnumerable<KeyValuePair<string, string>> environment = null)
        {
            var sb = new StringBuilder();
            if (environment != null)
            {
                foreach (KeyValuePair<string, string> kv in environment) sb.Append(kv.Key).Append(": ").Append(kv.Value).Append('\n');
            }

            Line(sb, "frames", Frames.ToString(CultureInfo.InvariantCulture));
            Line(sb, "duration_s", F(DurationSeconds, 2));
            Line(sb, "avg_fps", F(AverageFps, 1));
            Line(sb, "avg_ms", F(AverageMs, 2));
            Line(sb, "p50_ms", F(P50Ms, 2));
            Line(sb, "p95_ms", F(P95Ms, 2));
            Line(sb, "p99_ms", F(P99Ms, 2));
            Line(sb, "worst_ms", F(WorstMs, 2));
            Line(sb, "budget_ms", F(1000.0 / FloorFps, 2));
            Line(sb, "frames_over_budget", FramesOverBudget.ToString(CultureInfo.InvariantCulture));
            Line(sb, "cpu_avg_ms", CpuAverageMs > 0 ? F(CpuAverageMs, 2) : "n/a");
            Line(sb, "gpu_avg_ms", GpuAverageMs > 0 ? F(GpuAverageMs, 2) : "n/a");
            Line(sb, "gpu_p99_ms", GpuP99Ms > 0 ? F(GpuP99Ms, 2) : "n/a");
            Line(sb, "draw_calls_avg", DrawCallsAverage >= 0 ? F(DrawCallsAverage, 0) : "n/a");
            Line(sb, "draw_calls_max", DrawCallsMax >= 0 ? DrawCallsMax.ToString(CultureInfo.InvariantCulture) : "n/a");
            Line(sb, "triangles_avg", TrianglesAverage >= 0 ? F(TrianglesAverage, 0) : "n/a");
            Line(sb, "triangles_max", TrianglesMax >= 0 ? TrianglesMax.ToString(CultureInfo.InvariantCulture) : "n/a");
            Line(sb, "distance_m", F(DistanceMetres, 1));
            Line(sb, "stalled_s", F(StalledSeconds, 1));
            Line(sb, "detours", Detours.ToString(CultureInfo.InvariantCulture));
            Line(sb, "alloc_kb_avg", AllocAverageBytes >= 0 ? F(AllocAverageBytes / 1024.0, 2) : "n/a");
            Line(sb, "alloc_kb_max", AllocMaxBytes >= 0 ? F(AllocMaxBytes / 1024.0, 1) : "n/a");
            Line(sb, "gc_frames", Collections.ToString(CultureInfo.InvariantCulture));
            foreach (CounterSummary c in Counters)
            {
                Line(sb, c.Name + "_avg", F(c.Average, 2));
                Line(sb, c.Name + "_max", F(c.Max, 2));
            }
            Line(sb, "floor_fps", F(FloorFps, 0));
            Line(sb, "result", Verdict.ToString().ToUpperInvariant());
            return sb.ToString();
        }

        static void Line(StringBuilder sb, string key, string value) => sb.Append(key).Append(": ").Append(value).Append('\n');

        static string F(double v, int decimals) => v.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Collects the benchmark's per-frame samples, then reduces them to the numbers the 30 FPS gate is
    /// judged on (p99 frame time, not the mean) and writes them out as CSV.
    /// </summary>
    public sealed class BenchmarkRecorder
    {
        public const string CsvHeader = "frame,time_s,frame_ms,cpu_ms,gpu_ms,draw_calls,triangles,x,z,alloc_bytes,gc";
        public const string HitchesHeader = "time_s,frame_ms,cpu_ms,gpu_ms,x,z,alloc_bytes,gc";

        /// <summary>
        /// CPU and GPU times above this are taken as the platform's garbage, not a measurement: the frame
        /// timing API has been seen returning 1.8e13 ms for the frame after a screenshot. A frame that
        /// really took this long shows up in the frame time anyway.
        /// </summary>
        public const double MaxPlausibleTimingMs = 1000;

        /// <summary>A run that covers less than this share of the walking distance it should have is not a valid measurement.</summary>
        public const double MinimumDistanceShare = 0.5;

        /// <summary>Nor is one in which the pilot was held up for more than this share of the time: it measured a walker stuck against something, not the route.</summary>
        public const double MaximumStalledShare = 0.15;

        readonly List<FrameSample> _samples = new List<FrameSample>();
        readonly double _floorFps;

        sealed class Counter
        {
            public string Name;
            public double Sum, Max = double.NegativeInfinity, Last;
            public int Count;
        }

        readonly List<Counter> _counters = new List<Counter>();

        public BenchmarkRecorder(double floorFps = PerfStats.FloorFps)
        {
            if (!(floorFps > 0)) throw new ArgumentOutOfRangeException(nameof(floorFps), floorFps, "floor must be positive");
            _floorFps = floorFps;
        }

        public int Count => _samples.Count;

        public IReadOnlyList<FrameSample> Samples => _samples;

        /// <summary>A value a system reported for this frame. Counters are averaged over the frames they were reported in.</summary>
        public void AddCounter(string name, double value)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("a counter has a name", nameof(name));
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), value, $"counter '{name}' is not a number");
            Counter c = _counters.Find(x => x.Name == name);
            if (c == null)
            {
                c = new Counter { Name = name };
                _counters.Add(c);
            }
            c.Sum += value;
            c.Count++;
            c.Last = value;
            if (value > c.Max) c.Max = value;
        }

        public void Add(FrameSample sample)
        {
            if (double.IsNaN(sample.FrameMs) || double.IsInfinity(sample.FrameMs) || sample.FrameMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sample), sample.FrameMs, "frame time must be finite and > 0");
            }
            _samples.Add(sample);
        }

        /// <param name="stalledSeconds">How long the pilot was held up, from <see cref="BenchmarkPilot.StalledSeconds"/>.</param>
        /// <param name="detours">How many times it stepped aside.</param>
        public BenchmarkSummary Summarise(double stalledSeconds = 0, int detours = 0)
        {
            if (!(stalledSeconds >= 0) || double.IsInfinity(stalledSeconds)) throw new ArgumentOutOfRangeException(nameof(stalledSeconds), stalledSeconds, "time held up is finite and not negative");
            if (detours < 0) throw new ArgumentOutOfRangeException(nameof(detours), detours, "a count cannot be negative");
            var summary = new BenchmarkSummary { FloorFps = _floorFps, DrawCallsAverage = -1, DrawCallsMax = -1, TrianglesAverage = -1, TrianglesMax = -1 };
            if (_samples.Count == 0)
            {
                summary.Verdict = BenchmarkVerdict.Invalid;
                return summary;
            }

            var frameMs = new List<double>(_samples.Count);
            var gpuMs = new List<double>();
            double total = 0, cpuTotal = 0, gpuTotal = 0, drawTotal = 0, triTotal = 0, distance = 0, allocTotal = 0;
            int cpuCount = 0, drawCount = 0, over = 0, allocCount = 0, collections = 0;
            long allocMax = 0;
            double worst = 0;

            for (int i = 0; i < _samples.Count; i++)
            {
                FrameSample s = _samples[i];
                frameMs.Add(s.FrameMs);
                total += s.FrameMs;
                worst = Math.Max(worst, s.FrameMs);
                if (!PerfStats.WithinBudget(s.FrameMs, _floorFps)) over++;

                if (s.AllocBytes >= 0)
                {
                    allocTotal += s.AllocBytes;
                    allocCount++;
                    allocMax = Math.Max(allocMax, s.AllocBytes);
                }
                if (s.Collected) collections++;
                if (IsPlausibleTiming(s.CpuMs)) { cpuTotal += s.CpuMs; cpuCount++; }
                if (IsPlausibleTiming(s.GpuMs)) { gpuTotal += s.GpuMs; gpuMs.Add(s.GpuMs); }
                if (s.DrawCalls >= 0 && s.Triangles >= 0)
                {
                    drawTotal += s.DrawCalls;
                    triTotal += s.Triangles;
                    drawCount++;
                    summary.DrawCallsMax = Math.Max(summary.DrawCallsMax, s.DrawCalls);
                    summary.TrianglesMax = Math.Max(summary.TrianglesMax, s.Triangles);
                }
                if (i > 0)
                {
                    double ddx = s.X - _samples[i - 1].X, ddz = s.Z - _samples[i - 1].Z;
                    distance += Math.Sqrt(ddx * ddx + ddz * ddz);
                }
            }

            summary.Frames = _samples.Count;
            summary.DurationSeconds = total / 1000.0;
            summary.AverageMs = total / _samples.Count;
            summary.AverageFps = _samples.Count / summary.DurationSeconds;
            summary.P50Ms = PerfStats.Percentile(frameMs, 0.5);
            summary.P95Ms = PerfStats.Percentile(frameMs, 0.95);
            summary.P99Ms = PerfStats.Percentile(frameMs, 0.99);
            summary.WorstMs = worst;
            summary.FramesOverBudget = over;
            summary.CpuAverageMs = cpuCount > 0 ? cpuTotal / cpuCount : 0;
            summary.GpuAverageMs = gpuMs.Count > 0 ? gpuTotal / gpuMs.Count : 0;
            summary.GpuP99Ms = gpuMs.Count > 0 ? PerfStats.Percentile(gpuMs, 0.99) : 0;
            if (drawCount > 0)
            {
                summary.DrawCallsAverage = drawTotal / drawCount;
                summary.TrianglesAverage = triTotal / drawCount;
            }
            summary.DistanceMetres = distance;
            summary.StalledSeconds = stalledSeconds;
            summary.Detours = detours;
            if (allocCount > 0)
            {
                summary.AllocAverageBytes = allocTotal / allocCount;
                summary.AllocMaxBytes = allocMax;
            }
            summary.Collections = collections;
            var counters = new List<CounterSummary>(_counters.Count);
            foreach (Counter c in _counters) counters.Add(new CounterSummary(c.Name, c.Sum / c.Count, c.Max, c.Last));
            summary.Counters = counters;

            double minimumDistance = MinimumDistanceShare * PlayerMotor.WalkSpeed * summary.DurationSeconds;
            summary.Verdict = distance < minimumDistance || stalledSeconds > MaximumStalledShare * summary.DurationSeconds ? BenchmarkVerdict.Invalid
                : PerfStats.WithinBudget(summary.P99Ms, _floorFps) ? BenchmarkVerdict.Pass
                : BenchmarkVerdict.Fail;
            return summary;
        }

        static bool IsPlausibleTiming(double ms) => ms > 0 && ms <= MaxPlausibleTimingMs;

        public string ToCsv()
        {
            var sb = new StringBuilder(64 + _samples.Count * 64);
            sb.Append(CsvHeader).Append('\n');
            for (int i = 0; i < _samples.Count; i++)
            {
                FrameSample s = _samples[i];
                sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.TimeSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.FrameMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.CpuMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.GpuMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.DrawCalls.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.Triangles.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.X.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.Z.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.AllocBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.Collected ? '1' : '0').Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>The frames that went over the budget, each with where and when, whether a collection ran in it, and what the CPU and GPU were doing: the hitches the gate's p99 hides.</summary>
        public string HitchesCsv()
        {
            var sb = new StringBuilder();
            sb.Append(HitchesHeader).Append('\n');
            foreach (FrameSample s in _samples)
            {
                if (PerfStats.WithinBudget(s.FrameMs, _floorFps)) continue;
                sb.Append(s.TimeSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.FrameMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.CpuMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.GpuMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.X.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.Z.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.AllocBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(s.Collected ? '1' : '0').Append('\n');
            }
            return sb.ToString();
        }
    }
}
