using System.Globalization;

namespace Cypher.Core
{
    /// <summary>The perf overlay's three lines. Pure so the wording and the gate verdict are tested.</summary>
    public static class PerfOverlayText
    {
        /// <summary>
        /// FPS with the gate verdict, frame time (average, worst and p99 of the stats window) and the
        /// renderer counters. A negative counter means the platform does not report it.
        /// </summary>
        public static string Format(PerfStats stats, long drawCalls, long triangles)
        {
            if (stats.Count == 0) return "-- FPS\nframe -- ms\n" + Counters(drawCalls, triangles);

            string verdict = stats.MeetsFloor() ? "OK" : "BELOW " + PerfStats.FloorFps.ToString("0", CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture,
                "{0:0.0} FPS  {1}\nframe {2:0.0} avg  {3:0.0} worst  {4:0.0} p99 ms\n{5}",
                stats.Fps, verdict, stats.AverageMs, stats.WorstMs, stats.P99Ms, Counters(drawCalls, triangles));
        }

        static string Counters(long drawCalls, long triangles)
        {
            string draws = drawCalls >= 0 ? drawCalls.ToString("N0", CultureInfo.InvariantCulture) : "n/a";
            string tris = triangles >= 0 ? triangles.ToString("N0", CultureInfo.InvariantCulture) : "n/a";
            return "draws " + draws + "  tris " + tris;
        }
    }
}
