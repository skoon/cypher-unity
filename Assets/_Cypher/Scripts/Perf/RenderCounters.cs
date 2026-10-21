using System;
using Unity.Profiling;

namespace Cypher.Perf
{
    /// <summary>
    /// Draw calls and triangles of the last rendered frame, read from the engine's profiler counters.
    /// Only the Editor and Development players report them; a release player reads -1.
    /// </summary>
    public sealed class RenderCounters : IDisposable
    {
        ProfilerRecorder _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        ProfilerRecorder _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");

        public bool Available => _draws.Valid && _triangles.Valid;

        public long DrawCalls => _draws.Valid ? _draws.LastValue : -1;

        public long Triangles => _triangles.Valid ? _triangles.LastValue : -1;

        public void Dispose()
        {
            _draws.Dispose();
            _triangles.Dispose();
        }
    }
}
