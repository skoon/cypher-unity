namespace Cypher.Core
{
    /// <summary>
    /// A system that can say, every frame, how much of itself there is to see or hear (people drawn, vehicles drawn, what the sound costs), for the benchmark to average into its summary.
    /// The benchmark finds whatever in the scene offers this; the systems do not know about the benchmark.
    /// </summary>
    public interface IBenchmarkCounters
    {
        int CounterCount { get; }

        /// <summary>A name fit for a key in the summary: lower case, underscores.</summary>
        string CounterName(int index);

        /// <summary>The value this frame.</summary>
        double Counter(int index);
    }
}
