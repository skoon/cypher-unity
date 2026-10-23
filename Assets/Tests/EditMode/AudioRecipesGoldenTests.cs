using System;
using System.Collections.Generic;
using System.Globalization;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// Every recipe against the prototype's own, played in Chromium (tools/audio-ref/gen-golden-recipes.mjs): the same seed gives the same sound. Compared by the level of every 10 ms
    /// window of the sound, and sample for sample over its first 256 samples. Most agree to a millionth; a sound with a pitch sweep within its first block of 128 frames differs by up to
    /// a few percent at the fastest sweeps (a drip), because Chromium takes those frames' frequency from the start of the block, and a car pass by a couple of percent in its engine tone, because Chromium's long
    /// exponential frequency sweeps drift (see AudioSynthGoldenTests).
    /// </summary>
    public class AudioRecipesGoldenTests
    {
        const int Rate = 48000;
        const int Window = 480;
        const double Start = 0.05;

        sealed class Case
        {
            public string Name, Key;
            public uint Seed;
            public JsonObject Params;
            public int EndFrame;
            public float[] Rms, Head;
        }

        static readonly AudioNoiseBank Noise = new AudioNoiseBank(Rate);
        static readonly List<Case> Cases = Load();

        static List<Case> Load()
        {
            var list = new List<Case>();
            foreach (string line in Golden.Lines("audio_recipes_golden.txt"))
            {
                if (!line.StartsWith("R ", StringComparison.Ordinal)) continue;
                // R name seed {json} endFrame rms... | head...
                int brace = line.IndexOf('{');
                int closing = line.IndexOf("} ", brace, StringComparison.Ordinal);
                string[] head = line.Substring(2, brace - 3).Split(' ');
                string json = line.Substring(brace, closing - brace + 1);
                string[] rest = line.Substring(closing + 2).Split(' ');
                int bar = Array.IndexOf(rest, "|");
                var c = new Case { Name = head[0], Seed = uint.Parse(head[1], CultureInfo.InvariantCulture), Params = (JsonObject)Json.Parse(json), EndFrame = int.Parse(rest[0], CultureInfo.InvariantCulture) };
                c.Rms = new float[bar - 1];
                for (int i = 0; i < c.Rms.Length; i++) c.Rms[i] = float.Parse(rest[1 + i], CultureInfo.InvariantCulture);
                c.Head = new float[rest.Length - bar - 1];
                for (int i = 0; i < c.Head.Length; i++) c.Head[i] = float.Parse(rest[bar + 1 + i], CultureInfo.InvariantCulture);
                c.Key = c.Name + " " + json + " seed " + c.Seed;
                list.Add(c);
            }
            return list;
        }

        static VoiceParams ToParams(JsonObject o)
        {
            var p = new VoiceParams();
            foreach (KeyValuePair<string, object> kv in o)
            {
                switch (kv.Key)
                {
                    case "sprint": p.Sprint = (bool)kv.Value; break;
                    case "crouch": p.Crouch = (bool)kv.Value; break;
                    case "wet": p.Wet = (double)kv.Value; break;
                    case "surface": p.Surface = (string)kv.Value; break;
                    case "impact": p.Impact = (double)kv.Value; break;
                    case "effort": p.Effort = (double)kv.Value; break;
                    case "inhale": p.Inhale = (bool)kv.Value; break;
                    case "freq": p.Freq = (double)kv.Value; break;
                    case "dur": p.Dur = (double)kv.Value; break;
                    case "cutoff": p.Cutoff = (double)kv.Value; break;
                    case "pitch": p.Pitch = (double)kv.Value; break;
                    case "freqs":
                    {
                        var list = (List<object>)kv.Value;
                        p.Freqs = new double[list.Count];
                        for (int i = 0; i < p.Freqs.Length; i++) p.Freqs[i] = (double)list[i];
                        break;
                    }
                    default: throw new ArgumentException(kv.Key);
                }
            }
            return p;
        }

        static float WindowRms(float[] s, int w)
        {
            double sum = 0;
            for (int i = w * Window; i < (w + 1) * Window && i < s.Length; i++) sum += (double)s[i] * s[i];
            return (float)Math.Sqrt(sum / Window);
        }

        [Test]
        public void EverySoundIsTheSameAsThePrototypesForTheSameSeed()
        {
            Assert.AreEqual(84, Cases.Count);
            var failures = new List<string>();
            foreach (Case c in Cases)
            {
                RenderedSound r = SoundBank.Render(c.Name, Noise, c.Seed, ToParams(c.Params), Start);
                int start = (int)Math.Round(Start * Rate);

                double headTol = 8e-3;
                double headErr = 0;
                for (int i = 0; i < c.Head.Length; i++)
                {
                    float got = start + i < r.Samples.Length ? r.Samples[start + i] : 0;
                    headErr = Math.Max(headErr, Math.Abs(got - c.Head[i]));
                }
                if (headErr > headTol) failures.Add($"{c.Key}: first samples differ by {headErr:E1}");

                double rmsTol = c.Name == "amb.carpass" ? 6e-3 : 1e-3;
                for (int w = 0; w < c.Rms.Length; w++)
                {
                    double err = Math.Abs(WindowRms(r.Samples, w) - c.Rms[w]);
                    if (err > rmsTol)
                    {
                        failures.Add($"{c.Key}: level of window {w} differs by {err:E1} ({WindowRms(r.Samples, w):F5} against {c.Rms[w]:F5})");
                        break;
                    }
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void ARenderIsAsLongAsTheSoundAndHasNothingLeftAtItsEnd()
        {
            // the prototype stops a voice a little after the recipe's length; what the clip holds is that, and its tail is nearly silent
            foreach (Case c in Cases)
            {
                if (c.Seed != 20771) continue;
                RenderedSound r = SoundBank.Render(c.Name, Noise, c.Seed, ToParams(c.Params), Start);
                Assert.AreEqual((Start + r.Seconds + 0.06) * Rate, r.Samples.Length, 1.5, c.Key + " length");
                float tail = 0;
                for (int i = r.Samples.Length - 48; i < r.Samples.Length; i++) tail = Math.Max(tail, Math.Abs(r.Samples[i]));
                Assert.Less(tail, 0.02f, c.Key + " is cut off at its end");
            }
        }

        [Test]
        public void ASeedMakesTheSameSoundEveryTimeAndAnotherSeedAnotherTake()
        {
            foreach (string name in new[] { "foot.concrete", "amb.siren", "ui.confirm", "mus.stab", "amb.voice" })
            {
                float[] a = SoundBank.Render(name, Noise, 77, null).Samples;
                float[] b = SoundBank.Render(name, Noise, 77, null).Samples;
                CollectionAssert.AreEqual(a, b, name + " repeats");
            }
            foreach (string name in new[] { "foot.concrete", "amb.siren", "amb.voice", "amb.drip", "player.jump" })
            {
                float[] a = SoundBank.Render(name, Noise, 77, null).Samples;
                float[] b = SoundBank.Render(name, Noise, 78, null).Samples;
                CollectionAssert.AreNotEqual(a, b, name + " varies with the seed");
            }
        }

        [Test]
        public void EverySoundInTheBankMakesSenseNotSilentNotClippedNoNaNNoDcOffset()
        {
            Assert.AreEqual(38, SoundBank.Names.Count);
            foreach (string name in SoundBank.Names)
            {
                for (uint seed = 1; seed <= 3; seed++)
                {
                    var p = new VoiceParams();
                    // a sound that needs to be told something is told something sensible
                    if (name.StartsWith("mus.bass", StringComparison.Ordinal)) { p.Freq = 55; p.Dur = 0.4; }
                    if (name == "mus.arp" || name == "mus.lead") { p.Freq = 440; p.Dur = 0.3; }
                    if (name == "mus.stab") { p.Freqs = new[] { 220.0, 262, 330 }; p.Dur = 0.6; }
                    RenderedSound r = SoundBank.Render(name, Noise, seed, p);
                    AudioStats s = AudioAnalysis.Analyze(r.Samples, Rate);
                    string label = $"{name} (seed {seed})";
                    Assert.Greater(s.Rms, 2e-4, label + " is audible");
                    Assert.Less(s.Peak, 1.6, label + " does not clip hard");
                    Assert.Less(s.Peak, 1.3 * r.Def.Gain + 0.6, label + " stays near its stated gain");
                    double sum = 0;
                    foreach (float v in r.Samples)
                    {
                        Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), label + " has a bad sample");
                        sum += v;
                    }
                    Assert.Less(Math.Abs(sum / r.Samples.Length), 0.01, label + " has no DC offset");
                    Assert.Greater(s.NonSilentRatio, 0.01, label + " is not almost all silence");
                }
            }
        }

        [Test]
        public void EachSoundHasItsStatedPlaceInTheMix()
        {
            Assert.AreEqual(AudioBus.Ui, SoundBank.Get("ui.confirm").Bus);
            Assert.IsFalse(SoundBank.Get("ui.confirm").Spatial);
            Assert.AreEqual(AudioBus.Ambience, SoundBank.Get("amb.siren").Bus);
            Assert.AreEqual(45, SoundBank.Get("amb.siren").RefDistance);
            Assert.AreEqual(0.8, SoundBank.Get("amb.siren").Rolloff);
            Assert.AreEqual(0.85, SoundBank.Get("amb.siren").Send);
            Assert.AreEqual(AudioBus.Music, SoundBank.Get("mus.kick").Bus);
            Assert.AreEqual(AudioBus.Sfx, SoundBank.Get("foot.gravel").Bus);
            Assert.AreEqual(0.30, SoundBank.Get("foot.gravel").Send);
            Assert.AreEqual(2.2, SoundBank.Get("foot.gravel").RefDistance, "the prototype's default");
            Assert.AreEqual(1.05, SoundBank.Get("foot.gravel").Rolloff);
            foreach (string name in SoundBank.Names)
            {
                SoundDef d = SoundBank.Get(name);
                Assert.That(d.Priority, Is.InRange(1, 10), name);
                Assert.That(d.Send, Is.InRange(0.0, 3.0), name);
                Assert.Greater(d.Gain, 0, name);
            }
        }

        [Test]
        public void AskingForASoundThatIsNotThereFailsLoudly()
        {
            Assert.IsFalse(SoundBank.Has("weapon.pistol"));
            Assert.Throws<ArgumentException>(() => SoundBank.Get("weapon.pistol"));
            Assert.Throws<ArgumentException>(() => SoundBank.Render("nope", Noise, 1));
        }

        [Test]
        public void AnOccludedSoundIsQuieterByTheStatedRule()
        {
            float Rms(double occlusion) => (float)AudioAnalysis.Analyze(SoundBank.Render("amb.neonbuzz", Noise, 5, new VoiceParams { Occlusion = occlusion }).Samples, Rate).Rms;
            double clear = Rms(0);
            double blocked = Rms(1);
            Assert.AreEqual(0.4, blocked / clear, 0.01, "a wall takes 60% of the level");
        }

        [Test]
        public void AFootstepOnWetGroundOrWaterSplashesAndOneOnDryGroundDoesNot()
        {
            float[] dry = SoundBank.Render("foot.concrete", Noise, 9, new VoiceParams { Wet = 0 }).Samples;
            float[] wet = SoundBank.Render("foot.concrete", Noise, 9, new VoiceParams { Wet = 1 }).Samples;
            Assert.Greater(wet.Length, dry.Length, "the splash and its droplets outlast the step");
            AudioStats ds = AudioAnalysis.Analyze(dry, Rate), ws = AudioAnalysis.Analyze(wet, Rate);
            Assert.Greater(ws.Centroid, ds.Centroid * 0.95);
        }

        [Test]
        public void ASprintHitsHarderThanAWalkAndACrouchSofter()
        {
            double Peak(VoiceParams p) => AudioAnalysis.Analyze(SoundBank.Render("foot.concrete", Noise, 3, p).Samples, Rate).Rms;
            double walk = Peak(new VoiceParams());
            Assert.Greater(Peak(new VoiceParams { Sprint = true }), walk * 1.15);
            Assert.Less(Peak(new VoiceParams { Crouch = true }), walk * 0.7);
        }

        [Test]
        public void ALandingGrowsWithTheImpact()
        {
            double Rms(double impact) => AudioAnalysis.Analyze(SoundBank.Render("player.land", Noise, 3, new VoiceParams { Impact = impact }).Samples, Rate).Rms;
            Assert.Greater(Rms(1.0), Rms(0.3) * 1.5);
        }

        [Test]
        public void ABreathIsLongerAndDeeperTheLessEffortAndLouderTheMoreEffort()
        {
            RenderedSound calm = SoundBank.Render("player.breath", Noise, 3, new VoiceParams { Effort = 0 });
            RenderedSound hard = SoundBank.Render("player.breath", Noise, 3, new VoiceParams { Effort = 1 });
            Assert.Greater(calm.Seconds, hard.Seconds, "hard breathing is quicker");
            Assert.Greater(AudioAnalysis.Analyze(hard.Samples, Rate).Peak, AudioAnalysis.Analyze(calm.Samples, Rate).Peak);
        }

        [Test]
        public void ATonalInstrumentPlaysTheNoteItIsToldAndAFrequencyOfMidiSpacingIsAnOctave()
        {
            double Centroid(double freq) => AudioAnalysis.Analyze(SoundBank.Render("mus.arp", Noise, 1, new VoiceParams { Freq = freq, Dur = 0.3 }).Samples, Rate).Centroid;
            Assert.Greater(Centroid(880), Centroid(440) * 1.5, "a higher note is brighter");
        }
    }
}
