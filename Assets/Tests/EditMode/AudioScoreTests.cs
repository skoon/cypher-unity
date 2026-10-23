using System;
using System.Collections.Generic;
using System.Globalization;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The generative score against the prototype's own Music.js, run in Chromium with its calls recorded (tools/audio-ref/gen-golden-music.mjs): the same notes at the same times.</summary>
    public class AudioScoreTests
    {
        static readonly string[] Lines = Golden.Lines("audio_music_golden.txt");

        static double ScriptIntensity(double t)
        {
            if (t < 20) return 0.12;
            if (t < 40) return 0.5;
            if (t < 70) return 0.95;
            if (t < 100) return 0.12;
            if (t < 130) return 0.7;
            if (t < 160) return 0.3;
            return 0.55;
        }

        sealed class Run
        {
            public uint Seed;
            public double Seconds;
            public List<ScoreNote> Notes = new List<ScoreNote>();
            public List<ScoreChange> Changes = new List<ScoreChange>();
            public List<double> Intensities = new List<double>();
            public Score Score;
            public double NextStepTime;
        }

        static Run Play(uint seed, double seconds)
        {
            var score = new Score(seed);
            var run = new Run { Seed = seed, Seconds = seconds, Score = score };
            score.Start(0.01, run.Changes);
            run.NextStepTime = score.NextStepTime;
            run.Changes.Clear(); // the opening chord is checked on its own
            bool bumped = false;
            for (double t = 0; t < seconds; t += 0.1)
            {
                score.SetIntensity(ScriptIntensity(t));
                if (!bumped && t >= 70)
                {
                    score.Bump(0.35);
                    bumped = true;
                }
                score.Tick(0.1);
                score.Advance(t, 0.25, run.Notes, run.Changes);
                if (Math.Abs(t * 10 - Math.Round(t * 10)) < 1e-6 && Math.Round(t * 10) % 50 == 0) run.Intensities.Add(score.Intensity);
            }
            return run;
        }

        static IEnumerable<string[]> Rows(string tag, uint seed)
        {
            foreach (string line in Lines)
            {
                string[] f = line.Split(' ');
                if (f[0] == tag && f.Length > 1 && f[1] == seed.ToString(CultureInfo.InvariantCulture)) yield return f;
            }
        }

        static ScoreFeed Feed(string s)
        {
            switch (s)
            {
                case "duck": return ScoreFeed.Duck;
                case "delay": return ScoreFeed.Delay;
                default: return ScoreFeed.Bus;
            }
        }

        [Test]
        public void TheSameNotesAreAskedForAtTheSameTimesAsThePrototypes()
        {
            int totalNotes = 0;
            var sounds = new HashSet<string>();
            foreach (uint seed in new uint[] { 4242, 99 })
            {
                Run run = Play(seed, 200);
                var want = new List<string[]>(Rows("N", seed));
                Assert.AreEqual(want.Count, run.Notes.Count, $"seed {seed} note count");
                for (int i = 0; i < want.Count; i++)
                {
                    string[] f = want[i];
                    ScoreNote n = run.Notes[i];
                    string label = $"seed {seed} note {i} ({f[3]})";
                    Assert.AreEqual(Golden.D(f[2]), n.Time, 1e-9, label + " time");
                    Assert.AreEqual(f[3], n.Sound, label);
                    Assert.AreEqual(Golden.D(f[4]), n.Volume, 1e-9, label + " volume");
                    Assert.AreEqual(Feed(f[5]), n.Feed, label + " feed");
                    if (f[6] == "-") Assert.IsFalse(n.Freq.HasValue, label + " freq"); else Assert.AreEqual(Golden.D(f[6]), n.Freq.Value, 1e-9, label + " freq");
                    if (f[7] == "-") Assert.IsFalse(n.Dur.HasValue, label + " dur"); else Assert.AreEqual(Golden.D(f[7]), n.Dur.Value, 1e-9, label + " dur");
                    if (f[8] == "-") Assert.IsFalse(n.Cutoff.HasValue, label + " cutoff"); else Assert.AreEqual(Golden.D(f[8]), n.Cutoff.Value, 1e-9, label + " cutoff");
                    if (f[9] == "-") Assert.IsFalse(n.Pitch.HasValue, label + " pitch"); else Assert.AreEqual(Golden.D(f[9]), n.Pitch.Value, 1e-9, label + " pitch");
                    if (f[10] == "-") Assert.IsNull(n.Freqs, label + " freqs");
                    else
                    {
                        string[] fs = f[10].Split(',');
                        Assert.AreEqual(fs.Length, n.Freqs.Length, label + " freqs");
                        for (int k = 0; k < fs.Length; k++) Assert.AreEqual(Golden.D(fs[k]), n.Freqs[k], 1e-9, label + " freq " + k);
                    }
                    sounds.Add(n.Sound);
                }
                totalNotes += want.Count;
            }
            Assert.Greater(totalNotes, 2500);
            foreach (string s in new[] { "mus.bass", "mus.arp", "mus.kick", "mus.snare", "mus.clap", "mus.hat", "mus.openhat", "mus.rim", "mus.stab", "mus.lead", "mus.noise" })
                Assert.IsTrue(sounds.Contains(s), s + " is played in the script");
        }

        [Test]
        public void ThePadTheDelayAndTheSidechainMoveAsThePrototypesDo()
        {
            foreach (uint seed in new uint[] { 4242, 99 })
            {
                Run run = Play(seed, 200);
                // expand the changes into the calls the browser's parameters saw
                var got = new List<string>();
                foreach (ScoreChange c in run.Changes)
                {
                    switch (c.Kind)
                    {
                        case ScoreChangeKind.PadGlide: got.Add($"pad{c.Index} setTargetAtTime {c.Value:R} {c.Time:R} {c.Tau:R}"); break;
                        case ScoreChangeKind.PadGain: got.Add($"padGain setTargetAtTime {c.Value:R} {c.Time:R} {c.Tau:R}"); break;
                        case ScoreChangeKind.PadFilter: got.Add($"padFilter setTargetAtTime {c.Value:R} {c.Time:R} {c.Tau:R}"); break;
                        case ScoreChangeKind.DelayWet: got.Add($"delayWet setTargetAtTime {c.Value:R} {c.Time:R} {c.Tau:R}"); break;
                        case ScoreChangeKind.DuckPump:
                            got.Add($"duck cancelScheduledValues {c.Time:R}");
                            got.Add($"duck setValueAtTime {c.Value:R} {c.Time:R}");
                            got.Add($"duck setTargetAtTime 1 {c.Time + 0.005:R} {c.Tau:R}");
                            break;
                    }
                }
                var want = new List<string[]>(Rows("A", seed));
                Assert.AreEqual(want.Count, got.Count, $"seed {seed} change count");
                for (int i = 0; i < want.Count; i++)
                {
                    string[] w = want[i];
                    string[] g = got[i].Split(' ');
                    string label = $"seed {seed} change {i} ({w[2]} {w[3]})";
                    Assert.AreEqual(w[2], g[0], label);
                    Assert.AreEqual(w[3], g[1], label);
                    Assert.AreEqual(w.Length - 2, g.Length, label + " arguments");
                    for (int k = 4; k < w.Length; k++) Assert.AreEqual(Golden.D(w[k]), Golden.D(g[k - 2]), 1e-9, label + " argument " + (k - 4));
                }
            }
        }

        [Test]
        public void TheIntensityFollowsTheScriptAsThePrototypesDoesAndTheSongIsTheSame()
        {
            foreach (uint seed in new uint[] { 4242, 99 })
            {
                Run run = Play(seed, 200);
                var want = new List<string[]>(Rows("I", seed));
                Assert.AreEqual(want.Count, run.Intensities.Count);
                for (int i = 0; i < want.Count; i++) Assert.AreEqual(Golden.D(want[i][2]), run.Intensities[i], 1e-12, $"seed {seed} intensity at {i * 5} s");
                foreach (string line in Lines)
                {
                    if (!line.StartsWith("SCN ", StringComparison.Ordinal)) continue;
                    var j = (JsonObject)Json.Parse(line.Substring(4));
                    if ((uint)(double)j["seed"] != seed) continue;
                    var fresh = new Score(seed);
                    Assert.AreEqual((string)j["progression"], string.Join(",", fresh.Progression), "the chord progression");
                    Assert.AreEqual((string)j["arpShape"], string.Join(",", fresh.ArpShape), "the first arpeggio shape");
                    Assert.AreEqual((double)j["nextStepTime"], run.NextStepTime, 1e-12, "the first step");
                }
            }
        }

        [Test]
        public void TheFirstChordIsTheOnesRootInTwoStackedTriadsGlidingOver0Point4Seconds()
        {
            var changes = new List<ScoreChange>();
            var score = new Score(4242);
            score.Start(0.01, changes);
            Assert.AreEqual(6, changes.Count);
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(ScoreChangeKind.PadGlide, changes[i].Kind);
                Assert.AreEqual(i, changes[i].Index);
                Assert.AreEqual(0.4, changes[i].Tau);
                Assert.AreEqual(score.NextStepTime, changes[i].Time, 1e-12);
            }
            // the i chord (A minor): octaves 2 and 3 of its triad, A at 55 Hz being semitone 0; detuned a little either way
            if (score.Progression[0] == "i")
            {
                Assert.AreEqual(55 * Math.Pow(2, 24 / 12.0) * 0.996, changes[0].Value, 1e-9);
                Assert.AreEqual(55 * Math.Pow(2, 27 / 12.0) * 1.004, changes[1].Value, 1e-9);
            }
            Assert.AreEqual(55 * Math.Pow(2, 24 / 12.0), Score.PadStartFrequency(0), 1e-9);
            Assert.AreEqual(55 * Math.Pow(2, 28 / 12.0), Score.PadStartFrequency(1), 1e-9);
        }

        [Test]
        public void IntensityRisesFastFallsSlowlyAndABumpDecaysOverFourteenSeconds()
        {
            var score = new Score(1);
            score.SetIntensity(1);
            for (int i = 0; i < 20; i++) score.Tick(0.1);
            double rise = score.Intensity;
            Assert.Greater(rise, 0.5, "two seconds up");
            score.SetIntensity(0);
            for (int i = 0; i < 20; i++) score.Tick(0.1);
            Assert.Greater(score.Intensity, rise * 0.5, "two seconds down is slower");
            var calm = new Score(1);
            calm.Bump(0.8);
            Assert.AreEqual(0.8, calm.Heat, 1e-12);
            for (int i = 0; i < 70; i++) calm.Tick(0.1);
            Assert.AreEqual(0.8 - 7.0 / 14, calm.Heat, 1e-9);
            for (int i = 0; i < 200; i++) calm.Tick(0.1);
            Assert.AreEqual(0.0, calm.Heat);
            calm.Bump(3);
            Assert.AreEqual(1.0, calm.Heat, "a bump is held at 1");
        }

        [Test]
        public void ACalmScoreIsBassAndKicksAndAHotOneHasEveryLayer()
        {
            var changes = new List<ScoreChange>();
            var notes = new List<ScoreNote>();
            var calm = new Score(7);
            calm.Start(0, changes);
            calm.SetIntensity(0.12);
            for (double t = 0; t < 60; t += 0.1) { calm.Tick(0.1); calm.Advance(t, 0.25, notes, changes); }
            var calmSounds = new HashSet<string>();
            foreach (ScoreNote n in notes) calmSounds.Add(n.Sound);
            Assert.IsTrue(calmSounds.Contains("mus.kick"));
            Assert.IsTrue(calmSounds.Contains("mus.bass"));
            Assert.IsFalse(calmSounds.Contains("mus.arp"), "no arpeggio until the intensity passes 0.22");
            Assert.IsFalse(calmSounds.Contains("mus.snare"));
            Assert.IsFalse(calmSounds.Contains("mus.hat"));

            notes.Clear();
            var hot = new Score(7);
            hot.Start(0, changes);
            hot.SetIntensity(1);
            for (double t = 0; t < 120; t += 0.1) { hot.Tick(0.1); hot.Advance(t, 0.25, notes, changes); }
            var hotSounds = new HashSet<string>();
            foreach (ScoreNote n in notes) hotSounds.Add(n.Sound);
            foreach (string s in new[] { "mus.kick", "mus.bass", "mus.arp", "mus.snare", "mus.hat", "mus.stab" }) Assert.IsTrue(hotSounds.Contains(s), s);
        }

        [Test]
        public void ADisabledScoreAsksForNothingAndAScoreThatHasNotStartedNeitherAndAStallResyncs()
        {
            var changes = new List<ScoreChange>();
            var notes = new List<ScoreNote>();
            var s = new Score(3);
            s.Advance(0, 1, notes, changes);
            Assert.AreEqual(0, notes.Count + changes.Count, "not started");
            s.Start(0, changes);
            changes.Clear();
            s.Enabled = false;
            s.Advance(10, 1, notes, changes);
            Assert.AreEqual(0, notes.Count + changes.Count, "disabled");
            s.Enabled = true;
            s.Advance(1000, 0.25, notes, changes);
            Assert.LessOrEqual(notes.Count, 8, "after a long stall it does not play a backlog");
            foreach (ScoreNote n in notes) Assert.GreaterOrEqual(n.Time, 999.9);
        }

        [Test]
        public void TheTempoIsFixedAt108BeatsAPerMinuteInSixteenthSteps()
        {
            var s = new Score(1);
            Assert.AreEqual(60.0 / 108 / 4, s.StepDuration, 1e-12);
            Assert.AreEqual(16, Score.StepsPerBar);
            Assert.AreEqual(108.0, Score.Bpm);
        }
    }
}
