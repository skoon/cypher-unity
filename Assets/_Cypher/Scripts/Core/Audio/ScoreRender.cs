using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// The score rendered offline, for listening: the same <see cref="Score"/>, the same live graph and the same notes the game plays, made one after another on this thread instead of a second
    /// ahead on workers. The game's player and this make the same sounds from the same seed.
    /// </summary>
    public static class ScoreRender
    {
        const double LookaheadSeconds = 1.0;
        // a whole number of the graph's blocks, about a tenth of a second
        const int ChunkFrames = SynthGraph.Quantum * 38;

        /// <param name="intensity">How hard the music is driven, 0 (the calm side the game starts on) to 1.</param>
        /// <param name="profile">If given, filled with where the live graph's time went.</param>
        public static void Render(AudioNoiseBank noise, uint seed, double seconds, double intensity, out float[] left, out float[] right, GraphProfile profile = null)
        {
            if (noise == null) throw new ArgumentNullException(nameof(noise));
            if (seconds <= 0 || double.IsNaN(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "nothing to render");
            if (double.IsNaN(intensity) || intensity < 0 || intensity > 1) throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "intensity is from 0 to 1");

            int rate = noise.SampleRate;
            int frames = (int)Math.Ceiling(seconds * rate);
            var graph = new ScoreGraph(rate);
            graph.Graph.Profile = profile;
            // as the game plays it (ScorePlayer)
            graph.Graph.CoefficientInterval = 32;
            var score = new Score(seed);
            var changes = new List<ScoreChange>();
            score.Start(0, changes);
            foreach (ScoreChange c in changes) graph.Enqueue(c);
            score.SetIntensity(intensity);

            var cache = new Dictionary<string, float[]>();
            int takes = 0;
            float[] Make(ScoreNote note, string key)
            {
                if (cache.TryGetValue(key, out float[] made)) return made;
                made = SoundBank.Render(note.Sound, noise, ScoreNotes.SeedOf(++takes), ScoreNotes.ParamsOf(note)).Samples;
                cache[key] = made;
                return made;
            }
            foreach (ScoreNote opening in ScoreNotes.Opening()) Make(opening, ScoreNotes.KeyOf(opening));

            left = new float[frames];
            right = new float[frames];
            var notes = new List<ScoreNote>();
            double dt = (double)ChunkFrames / rate;
            for (int done = 0; done < frames; done += ChunkFrames)
            {
                score.Tick(dt);
                graph.ApplyPending();
                notes.Clear();
                changes.Clear();
                score.Advance(graph.Graph.Time, LookaheadSeconds, notes, changes);
                foreach (ScoreChange c in changes) graph.Enqueue(c);
                foreach (ScoreNote n in notes)
                {
                    float[] samples = Make(n, ScoreNotes.KeyOf(n));
                    graph.PlayerFor(n.Feed).Schedule(graph.Graph.FrameOf(n.Time), samples, (float)(SoundBank.Get(n.Sound).Gain * n.Volume));
                }
                graph.Graph.Render(ChunkFrames, out float[] l, out float[] r);
                int take = Math.Min(ChunkFrames, frames - done);
                Array.Copy(l, 0, left, done, take);
                Array.Copy(r, 0, right, done, take);
            }
        }
    }
}
