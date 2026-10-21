using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Rolling frame-time statistics over a trailing time window. Pure so the 30 FPS
    /// gate maths is unit-tested. The gate is p99 frame time, not the mean: a stable
    /// 40 FPS average with a stutter every second is a failure.
    /// </summary>
    public sealed class PerfStats
    {
        public const double FloorFps = 30.0;

        readonly double _windowSeconds;
        readonly Queue<double> _frames = new Queue<double>();
        double _sum;

        public PerfStats(double windowSeconds = 5.0)
        {
            if (!(windowSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
            _windowSeconds = windowSeconds;
        }

        public int Count => _frames.Count;

        /// <summary>Add one frame time in seconds. Rejects non-finite or non-positive values.</summary>
        public void AddFrame(double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "frame time must be finite and > 0");
            }
            _frames.Enqueue(deltaSeconds);
            _sum += deltaSeconds;
            // Keep at least the newest frame even if it alone exceeds the window.
            while (_frames.Count > 1 && _sum - _frames.Peek() >= _windowSeconds)
            {
                _sum -= _frames.Dequeue();
            }
        }

        public void Reset()
        {
            _frames.Clear();
            _sum = 0;
        }

        public double AverageMs => Count == 0 ? 0 : _sum / Count * 1000.0;

        public double Fps => _sum <= 0 ? 0 : Count / _sum;

        public double WorstMs
        {
            get
            {
                double worst = 0;
                foreach (double f in _frames) worst = Math.Max(worst, f);
                return worst * 1000.0;
            }
        }

        /// <summary>99th percentile frame time in ms (nearest-rank).</summary>
        public double P99Ms => PercentileMs(0.99);

        /// <summary>Frame time in ms at <paramref name="fraction"/> (0..1] of the window, nearest-rank; 0 when empty.</summary>
        public double PercentileMs(double fraction) => Percentile(_frames, fraction) * 1000.0;

        /// <summary>Nearest-rank percentile of <paramref name="values"/> in their own unit; 0 when empty.</summary>
        public static double Percentile(IEnumerable<double> values, double fraction)
        {
            if (!(fraction > 0 && fraction <= 1)) throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "fraction must be in (0, 1]");
            double[] sorted = new List<double>(values).ToArray();
            if (sorted.Length == 0) return 0;
            Array.Sort(sorted);
            int rank = (int)Math.Ceiling(fraction * sorted.Length);
            return sorted[Math.Max(1, rank) - 1];
        }

        /// <summary>True when p99 frame time is within the budget for <paramref name="floorFps"/>.</summary>
        public bool MeetsFloor(double floorFps = FloorFps)
        {
            if (!(floorFps > 0)) throw new ArgumentOutOfRangeException(nameof(floorFps));
            return Count > 0 && WithinBudget(P99Ms, floorFps);
        }

        /// <summary>True when a frame time in ms fits the budget for <paramref name="floorFps"/>.</summary>
        public static bool WithinBudget(double frameMs, double floorFps = FloorFps)
        {
            if (!(floorFps > 0)) throw new ArgumentOutOfRangeException(nameof(floorFps));
            // The epsilon absorbs float rounding at the boundary (1/30 s * 1000 vs 1000 / 30).
            return frameMs <= 1000.0 / floorFps + 1e-9;
        }
    }
}
