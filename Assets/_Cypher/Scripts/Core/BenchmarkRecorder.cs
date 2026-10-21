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

        public FrameSample(double timeSeconds, double frameMs, double cpuMs, double gpuMs, long drawCalls, long triangles, double x, double z)
        {
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
        public const string CsvHeader = "frame,time_s,frame_ms,cpu_ms,gpu_ms,draw_calls,triangles,x,z";

        /// <summary>
        /// CPU and GPU times above this are taken as the platform's garbage, not a measurement: the frame
        /// timing API has been seen returning 1.8e13 ms for the frame after a screenshot. A frame that
        /// really took this long shows up in the frame time anyway.
        /// </summary>
        public const double MaxPlausibleTimingMs = 1000;

        /// <summary>A run that covers less than this share of the walking distance it should have is not a valid measurement.</summary>
        public const double MinimumDistanceShare = 0.5;

        readonly List<FrameSample> _samples = new List<FrameSample>();
        readonly double _floorFps;

        public BenchmarkRecorder(double floorFps = PerfStats.FloorFps)
        {
            if (!(floorFps > 0)) throw new ArgumentOutOfRangeException(nameof(floorFps), floorFps, "floor must be positive");
            _floorFps = floorFps;
        }

        public int Count => _samples.Count;

        public IReadOnlyList<FrameSample> Samples => _samples;

        public void Add(FrameSample sample)
        {
            if (double.IsNaN(sample.FrameMs) || double.IsInfinity(sample.FrameMs) || sample.FrameMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sample), sample.FrameMs, "frame time must be finite and > 0");
            }
            _samples.Add(sample);
        }

        public BenchmarkSummary Summarise()
        {
            var summary = new BenchmarkSummary { FloorFps = _floorFps, DrawCallsAverage = -1, DrawCallsMax = -1, TrianglesAverage = -1, TrianglesMax = -1 };
            if (_samples.Count == 0)
            {
                summary.Verdict = BenchmarkVerdict.Invalid;
                return summary;
            }

            var frameMs = new List<double>(_samples.Count);
            var gpuMs = new List<double>();
            double total = 0, cpuTotal = 0, gpuTotal = 0, drawTotal = 0, triTotal = 0, distance = 0;
            int cpuCount = 0, drawCount = 0, over = 0;
            double worst = 0;

            for (int i = 0; i < _samples.Count; i++)
            {
                FrameSample s = _samples[i];
                frameMs.Add(s.FrameMs);
                total += s.FrameMs;
                worst = Math.Max(worst, s.FrameMs);
                if (!PerfStats.WithinBudget(s.FrameMs, _floorFps)) over++;

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

            double minimumDistance = MinimumDistanceShare * PlayerMotor.WalkSpeed * summary.DurationSeconds;
            summary.Verdict = distance < minimumDistance ? BenchmarkVerdict.Invalid
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
                  .Append(s.Z.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
            }
            return sb.ToString();
        }
    }
}
