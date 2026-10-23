using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cypher.Core
{
    /// <summary>
    /// How a score's notes are turned into sounds, shared by the player in the game and the offline render: which notes sound the same (and so are made once), which seed each take is made from and
    /// what the notes the drums need before the first bar are.
    /// </summary>
    public static class ScoreNotes
    {
        /// <summary>A note that sounds the same as another has the same key: the notes made differ only by what the recipe reads.</summary>
        public static string KeyOf(ScoreNote n)
        {
            double? pitch = n.Pitch.HasValue ? Math.Round(n.Pitch.Value * 20) / 20 : (double?)null;
            string freqs = n.Freqs == null ? "-" : string.Join(",", Array.ConvertAll(n.Freqs, f => f.ToString("0.###", CultureInfo.InvariantCulture)));
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1:0.###}|{2:0.###}|{3:0.#}|{4:0.##}|{5}", n.Sound, n.Freq ?? 0, n.Dur ?? 0, n.Cutoff ?? 0, pitch ?? 1, freqs);
        }

        /// <summary>The seed the <paramref name="take"/>th distinct note is made from (counting from 1).</summary>
        public static uint SeedOf(int take)
        {
            if (take < 1) throw new ArgumentOutOfRangeException(nameof(take), take, "takes count from 1");
            return unchecked(0x9e3779b9u * (uint)take + 17u);
        }

        /// <summary>The recipe's settings for a note; a pitch is held to the steps the key distinguishes.</summary>
        public static VoiceParams ParamsOf(ScoreNote n)
        {
            VoiceParams p = n.ToParams();
            if (n.Pitch.HasValue) p.Pitch = Math.Round(n.Pitch.Value * 20) / 20;
            return p;
        }

        /// <summary>The drums are the same every time: these are made before the first bar.</summary>
        public static IEnumerable<ScoreNote> Opening()
        {
            foreach (string drum in new[] { "mus.kick", "mus.snare", "mus.clap", "mus.openhat", "mus.rim", "mus.noise" })
                yield return new ScoreNote(0, drum, 1, ScoreFeed.Bus, pitch: 1);
            for (int p = 0; p <= 3; p++)
                yield return new ScoreNote(0, "mus.hat", 1, ScoreFeed.Bus, pitch: 0.95 + p * 0.05);
        }
    }
}
