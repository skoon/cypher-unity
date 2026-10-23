using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class WavFileTests
    {
        [Test]
        public void AStereoFileHasTheRiffHeaderOfA16BitPcmFile()
        {
            byte[] file = WavFile.Encode(new[] { new float[10], new float[10] }, 44100, out int clipped);
            Assert.AreEqual(0, clipped);
            Assert.AreEqual(WavFile.HeaderBytes + 10 * 2 * 2, file.Length);
            Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(file, 0, 4));
            Assert.AreEqual(file.Length - 8, BitConverter.ToInt32(file, 4));
            Assert.AreEqual("WAVEfmt ", System.Text.Encoding.ASCII.GetString(file, 8, 8));
            Assert.AreEqual(16, BitConverter.ToInt32(file, 16));
            Assert.AreEqual(1, BitConverter.ToInt16(file, 20), "PCM");
            Assert.AreEqual(2, BitConverter.ToInt16(file, 22), "channels");
            Assert.AreEqual(44100, BitConverter.ToInt32(file, 24));
            Assert.AreEqual(44100 * 4, BitConverter.ToInt32(file, 28), "bytes a second");
            Assert.AreEqual(4, BitConverter.ToInt16(file, 32), "bytes a frame");
            Assert.AreEqual(16, BitConverter.ToInt16(file, 34));
            Assert.AreEqual("data", System.Text.Encoding.ASCII.GetString(file, 36, 4));
            Assert.AreEqual(40, BitConverter.ToInt32(file, 40));
        }

        [Test]
        public void SamplesComeBackWithinOneStepAndTheChannelsAreInterleavedLeftFirst()
        {
            var left = new float[100];
            var right = new float[100];
            for (int i = 0; i < 100; i++)
            {
                left[i] = (float)Math.Sin(i * 0.3) * 0.9f;
                right[i] = -left[i] * 0.5f;
            }
            byte[] file = WavFile.Encode(new[] { left, right }, 48000, out _);
            Assert.AreEqual(left[3] * 32767, BitConverter.ToInt16(file, WavFile.HeaderBytes + 3 * 4), 0.6, "left is first");
            Assert.AreEqual(right[3] * 32767, BitConverter.ToInt16(file, WavFile.HeaderBytes + 3 * 4 + 2), 0.6, "then right");
            float[][] back = WavFile.Decode(file, out int rate);
            Assert.AreEqual(48000, rate);
            Assert.AreEqual(2, back.Length);
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(left[i], back[0][i], 1.0 / 32767, "left " + i);
                Assert.AreEqual(right[i], back[1][i], 1.0 / 32767, "right " + i);
            }
        }

        [Test]
        public void AMonoFileHasOneChannel()
        {
            byte[] file = WavFile.Encode(new float[] { 0.5f, -0.5f, 0 }, 22050, out _);
            Assert.AreEqual(WavFile.HeaderBytes + 6, file.Length);
            float[][] back = WavFile.Decode(file, out int rate);
            Assert.AreEqual(22050, rate);
            Assert.AreEqual(1, back.Length);
            Assert.AreEqual(0.5f, back[0][0], 1e-4);
            Assert.AreEqual(-0.5f, back[0][1], 1e-4);
        }

        [Test]
        public void SamplesPastFullScaleAreHeldThereAndCounted()
        {
            byte[] file = WavFile.Encode(new float[] { 2f, -3f, 1f, -1f, 0.99f }, 48000, out int clipped);
            Assert.AreEqual(2, clipped, "exactly full scale is not clipping");
            float[] back = WavFile.Decode(file, out _)[0];
            Assert.AreEqual(1f, back[0], 1e-6);
            Assert.AreEqual(-1f, back[1], 1e-6);
        }

        [Test]
        public void BadInputFailsLoudly()
        {
            Assert.Throws<ArgumentException>(() => WavFile.Encode(new float[0][], 48000, out _));
            Assert.Throws<ArgumentException>(() => WavFile.Encode(new[] { new float[3], new float[4] }, 48000, out _), "channels of different lengths");
            Assert.Throws<ArgumentException>(() => WavFile.Encode(new[] { new float[3], new float[3], new float[3] }, 48000, out _), "more than stereo");
            Assert.Throws<ArgumentOutOfRangeException>(() => WavFile.Encode(new float[3], 0, out _));
            Assert.Throws<ArgumentException>(() => WavFile.Encode(new[] { 0f, float.NaN }, 48000, out _), "a sample that is not a number");
        }

        [Test]
        public void ReadingSomethingThatIsNotAWavFileFailsLoudly()
        {
            Assert.Throws<InvalidDataException>(() => WavFile.Decode(new byte[10], out _));
            Assert.Throws<InvalidDataException>(() => WavFile.Decode(new byte[100], out _));
            byte[] file = WavFile.Encode(new float[8], 48000, out _);
            Assert.Throws<InvalidDataException>(() => WavFile.Decode(file.Take(file.Length - 2).ToArray(), out _), "a truncated file");
        }
    }

    public class ScoreNotesTests
    {
        [Test]
        public void NotesThatSoundTheSameShareAKeyWhateverTheirTimeVolumeOrFeed()
        {
            var a = new ScoreNote(1.5, "mus.bass", 0.9, ScoreFeed.Duck, freq: 55, dur: 0.4, cutoff: 800);
            var b = new ScoreNote(9.0, "mus.bass", 0.6, ScoreFeed.Bus, freq: 55, dur: 0.4, cutoff: 800);
            Assert.AreEqual(ScoreNotes.KeyOf(a), ScoreNotes.KeyOf(b));
            Assert.AreNotEqual(ScoreNotes.KeyOf(a), ScoreNotes.KeyOf(new ScoreNote(1.5, "mus.bass", 0.9, ScoreFeed.Duck, freq: 58, dur: 0.4, cutoff: 800)), "another pitch");
            Assert.AreNotEqual(ScoreNotes.KeyOf(a), ScoreNotes.KeyOf(new ScoreNote(1.5, "mus.lead", 0.9, ScoreFeed.Duck, freq: 55, dur: 0.4, cutoff: 800)), "another sound");
            Assert.AreNotEqual(ScoreNotes.KeyOf(a), ScoreNotes.KeyOf(new ScoreNote(1.5, "mus.bass", 0.9, ScoreFeed.Duck, freq: 55, dur: 0.8, cutoff: 800)), "another length");
        }

        [Test]
        public void ChordsAreKeyedByTheirFrequenciesAndPitchesByTwentiethsOfAStep()
        {
            var chord = new ScoreNote(0, "mus.stab", 1, ScoreFeed.Bus, freqs: new[] { 220.0, 277.18, 329.63 });
            var other = new ScoreNote(0, "mus.stab", 1, ScoreFeed.Bus, freqs: new[] { 220.0, 277.18, 349.23 });
            Assert.AreNotEqual(ScoreNotes.KeyOf(chord), ScoreNotes.KeyOf(other));
            var hat = new ScoreNote(0, "mus.hat", 1, ScoreFeed.Bus, pitch: 1.0);
            Assert.AreEqual(ScoreNotes.KeyOf(hat), ScoreNotes.KeyOf(new ScoreNote(3, "mus.hat", 1, ScoreFeed.Bus, pitch: 1.01)), "a hair of pitch is the same note");
            Assert.AreNotEqual(ScoreNotes.KeyOf(hat), ScoreNotes.KeyOf(new ScoreNote(0, "mus.hat", 1, ScoreFeed.Bus, pitch: 1.1)));
        }

        [Test]
        public void EachTakeHasItsOwnSeedAndTheSameTakeTheSameSeedEveryTime()
        {
            Assert.AreEqual(0x9e3779b9u + 17u, ScoreNotes.SeedOf(1));
            var seeds = Enumerable.Range(1, 500).Select(ScoreNotes.SeedOf).ToList();
            Assert.AreEqual(500, seeds.Distinct().Count());
            Assert.AreEqual(ScoreNotes.SeedOf(37), ScoreNotes.SeedOf(37));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScoreNotes.SeedOf(0));
        }

        [Test]
        public void ThePitchOfANoteIsHeldToTheStepsItsKeyDistinguishes()
        {
            Assert.AreEqual(1.0, ScoreNotes.ParamsOf(new ScoreNote(0, "mus.hat", 1, ScoreFeed.Bus, pitch: 1.01)).Pitch, 1e-12);
            Assert.AreEqual(1.05, ScoreNotes.ParamsOf(new ScoreNote(0, "mus.hat", 1, ScoreFeed.Bus, pitch: 1.04)).Pitch, 1e-12);
            Assert.AreEqual(1.0, ScoreNotes.ParamsOf(new ScoreNote(0, "mus.kick", 1, ScoreFeed.Bus)).Pitch, "no pitch is the recipe's own");
            VoiceParams p = ScoreNotes.ParamsOf(new ScoreNote(0, "mus.bass", 1, ScoreFeed.Duck, freq: 55, dur: 0.4, cutoff: 900));
            Assert.AreEqual(55, p.Freq);
            Assert.AreEqual(0.4, p.Dur);
            Assert.AreEqual(900, p.Cutoff);
        }

        [Test]
        public void TheDrumsMadeBeforeTheFirstBarAreTheSixAndFourHatsAndAllExist()
        {
            var opening = ScoreNotes.Opening().ToList();
            Assert.AreEqual(10, opening.Count);
            CollectionAssert.AreEquivalent(new[] { "mus.kick", "mus.snare", "mus.clap", "mus.openhat", "mus.rim", "mus.noise" }, opening.Take(6).Select(n => n.Sound));
            CollectionAssert.AreEqual(new[] { 0.95, 1.0, 1.05, 1.1 }, opening.Skip(6).Select(n => Math.Round(n.Pitch.Value, 2)));
            Assert.AreEqual(10, opening.Select(ScoreNotes.KeyOf).Distinct().Count(), "ten different sounds");
            foreach (ScoreNote n in opening) Assert.IsTrue(SoundBank.Has(n.Sound), n.Sound);
        }
    }

    public class ScoreRenderTests
    {
        const int Rate = 48000;
        static readonly AudioNoiseBank Noise = new AudioNoiseBank(Rate);

        static float[][] Calm, Driven, CalmAgain, OtherSeed;

        [OneTimeSetUp]
        public void Render()
        {
            ScoreRender.Render(Noise, 3, 16, 0, out float[] l, out float[] r);
            Calm = new[] { l, r };
            ScoreRender.Render(Noise, 3, 16, 0.9, out l, out r);
            Driven = new[] { l, r };
            ScoreRender.Render(Noise, 3, 16, 0, out l, out r);
            CalmAgain = new[] { l, r };
            ScoreRender.Render(Noise, 4, 16, 0, out l, out r);
            OtherSeed = new[] { l, r };
        }

        [Test]
        public void ItIsStereoAsLongAsAskedAndMadeOfRealNumbers()
        {
            foreach (float[][] render in new[] { Calm, Driven })
            {
                Assert.AreEqual(2, render.Length);
                Assert.AreEqual(16 * Rate, render[0].Length);
                Assert.AreEqual(16 * Rate, render[1].Length);
                foreach (float[] channel in render)
                {
                    Assert.IsTrue(channel.All(v => !float.IsNaN(v) && !float.IsInfinity(v)));
                    Assert.Less(channel.Max(Math.Abs), 4f, "no blow-ups");
                }
            }
        }

        [Test]
        public void ItIsMusicNotSilenceAndTheDrivenScoreHasMoreInIt()
        {
            AudioStats calm = AudioAnalysis.Analyze(Calm, Rate), driven = AudioAnalysis.Analyze(Driven, Rate);
            Assert.Greater(calm.Rms, 0.005, "the calm side is heard");
            Assert.Greater(calm.NonSilentRatio, 0.8);
            Assert.Greater(driven.Rms, calm.Rms, "more layers are armed when it is driven hard");
        }

        [Test]
        public void ASeedMakesTheSameMusicEveryTimeAndAnotherSeedMakesOther()
        {
            CollectionAssert.AreEqual(Calm[0], CalmAgain[0]);
            CollectionAssert.AreEqual(Calm[1], CalmAgain[1]);
            CollectionAssert.AreNotEqual(Calm[0], OtherSeed[0]);
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => ScoreRender.Render(null, 1, 1, 0, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScoreRender.Render(Noise, 1, 0, 0, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScoreRender.Render(Noise, 1, 1, 1.5, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScoreRender.Render(Noise, 1, 1, double.NaN, out _, out _));
        }
    }

    public class AudioExportTests
    {
        const int Rate = 24000;

        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cypher-audio-export-test-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        static ExportOptions Small() => new ExportOptions
        {
            SampleRate = Rate,
            BedSeconds = 2,
            ScoreSeconds = 3,
            Sounds = n => n.StartsWith("ui."),
        };

        [Test]
        public void EverySoundTheBedsAndTheScoreAreWrittenAsPlayableFilesWithAReport()
        {
            IReadOnlyList<ExportedFile> files = AudioExport.Run(_dir, Small());
            int ui = ClipPlan.UiSoundNames.Count;
            Assert.AreEqual(ui + 3 + 2, files.Count);
            Assert.AreEqual(ui, files.Count(f => f.Kind == "sound"));
            CollectionAssert.AreEquivalent(new[] { "bed-dry", "bed-rain", "bed-rain-indoors" }, files.Where(f => f.Kind == "bed").Select(f => f.Name));
            CollectionAssert.AreEquivalent(new[] { "score-calm", "score-driven" }, files.Where(f => f.Kind == "score").Select(f => f.Name));

            string report = File.ReadAllText(Path.Combine(_dir, AudioExport.ReportFile));
            foreach (ExportedFile f in files)
            {
                string path = Path.Combine(_dir, f.Path.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(path), f.Path);
                float[][] wav = WavFile.Decode(File.ReadAllBytes(path), out int rate);
                Assert.AreEqual(Rate, rate);
                Assert.AreEqual(f.Kind == "sound" ? 1 : 2, wav.Length, f.Name + " channels");
                Assert.AreEqual(f.Seconds * Rate, wav[0].Length, 1, f.Name + " length");
                StringAssert.Contains(f.Name, report);
                Assert.AreEqual(0, f.Clipped, f.Name + " fits in the file");
            }
        }

        [Test]
        public void FilesAreAtTheLevelTheGameMixesThem()
        {
            IReadOnlyList<ExportedFile> files = AudioExport.Run(_dir, Small());
            foreach (ExportedFile f in files.Where(f => f.Kind == "sound"))
            {
                SoundDef def = SoundBank.Get(f.Name);
                Assert.AreEqual(def.Gain * BusLevels.Base(def.Bus), f.Gain, 1e-12, f.Name);
                float[] wav = WavFile.Decode(File.ReadAllBytes(Path.Combine(_dir, f.Path.Replace('/', Path.DirectorySeparatorChar))), out _)[0];
                double peak = wav.Max(Math.Abs);
                Assert.AreEqual(f.HeardPeakDb, 20 * Math.Log10(peak), 0.1, f.Name + " peak");
            }
            ExportedFile bed = files.Single(f => f.Name == "bed-rain");
            Assert.AreEqual(BusLevels.Base(AudioBus.Ambience), bed.Gain, 1e-12);
            Assert.AreEqual(BusLevels.Base(AudioBus.Music), files.First(f => f.Kind == "score").Gain, 1e-12);
        }

        [Test]
        public void TheWeatherIsHeardInTheBeds()
        {
            IReadOnlyList<ExportedFile> files = AudioExport.Run(_dir, Small());
            double dry = files.Single(f => f.Name == "bed-dry").Raw.Rms, rain = files.Single(f => f.Name == "bed-rain").Raw.Rms, indoors = files.Single(f => f.Name == "bed-rain-indoors").Raw.Rms;
            Assert.Greater(rain, dry, "rain is louder than none");
            Assert.Less(indoors, rain, "and indoors it is muffled");
        }

        [Test]
        public void TheMenuSoundsAreAudibleOverTheBedDuckedForThePauseMenuAndTheReportSaysHowFar()
        {
            IReadOnlyList<ExportedFile> files = AudioExport.Run(_dir, Small());
            ExportedFile rain = files.Single(f => f.Name == "bed-rain");
            double pausedBed = rain.HeardRmsDb + 20 * Math.Log10(BusLevels.DuckPaused);
            foreach (ExportedFile f in files.Where(f => f.Kind == "sound"))
                Assert.Greater(f.HeardPeakDb, pausedBed + 3, f.Name + " is audible over the paused rain");
            string report = File.ReadAllText(Path.Combine(_dir, AudioExport.ReportFile));
            StringAssert.Contains("## Against the bed", report);
            StringAssert.Contains("paused", report);
        }

        [Test]
        public void TheBedsAndTheScoreCanBeLeftOut()
        {
            ExportOptions o = Small();
            o.IncludeBeds = false;
            o.ScoreSeconds = 0;
            IReadOnlyList<ExportedFile> files = AudioExport.Run(_dir, o);
            Assert.AreEqual(ClipPlan.UiSoundNames.Count, files.Count);
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "bed-rain.wav")));
            StringAssert.DoesNotContain("## Beds and score", File.ReadAllText(Path.Combine(_dir, AudioExport.ReportFile)));
        }

        [Test]
        public void NoDirectoryOrOptionsFailsLoudly()
        {
            Assert.Throws<ArgumentException>(() => AudioExport.Run("", Small()));
            Assert.Throws<ArgumentNullException>(() => AudioExport.Run(_dir, null));
        }
    }
}
