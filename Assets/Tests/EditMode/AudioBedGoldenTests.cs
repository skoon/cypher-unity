using System;
using System.Collections.Generic;
using System.Globalization;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// The city bed against the prototype's own Ambience.js, played in Chromium (tools/audio-ref/gen-golden-bed.mjs): the same seed builds the same bed, and the same weather moves it the same way.
    /// Compared by the level of every 100 ms window of both channels, and sample by sample over short stretches in the middle of the render.
    /// </summary>
    public class AudioBedGoldenTests
    {
        const int Rate = 48000;
        const int Window = 4800;

        static readonly string[] Lines = Golden.Lines("audio_bed_golden.txt");
        static readonly AudioNoiseBank Noise = new AudioNoiseBank(Rate);

        sealed class Scenario
        {
            public string Name;
            public double Seed, Seconds;
            public bool EventsOnly;
            public List<(double time, BedState state)> Steps = new List<(double, BedState)>();
        }

        static List<Scenario> Scenarios()
        {
            var list = new List<Scenario>();
            foreach (string line in Lines)
            {
                if (!line.StartsWith("SCN ", StringComparison.Ordinal)) continue;
                var j = (JsonObject)Json.Parse(line.Substring(4));
                var s = new Scenario { Name = (string)j["name"], Seed = (double)j["seed"], Seconds = (double)j["seconds"], EventsOnly = (bool)j["eventsOnly"] };
                foreach (object o in (List<object>)j["steps"])
                {
                    var step = (List<object>)o;
                    var st = (JsonObject)step[1];
                    s.Steps.Add(((double)step[0], new BedState((double)st["rain"], (double)st["wind"], (double)st["indoor"], (double)st["density"])));
                }
                list.Add(s);
            }
            return list;
        }

        static void RenderBed(Scenario s, int interval, out float[] left, out float[] right)
        {
            var bed = new Bed(Noise, new Rng((uint)s.Seed), 0.01, Bed.EmitterMode.Inline);
            bed.Main.CoefficientInterval = interval;
            int step = 0;
            for (double t = 0; t < s.Seconds; t += 0.1)
            {
                while (step + 1 < s.Steps.Count && s.Steps[step + 1].time <= t + 1e-9) step++;
                bed.Advance(t, s.Steps[step].state);
            }
            bed.Main.Render((int)Math.Ceiling(s.Seconds * Rate), out left, out right);
        }

        static float[] Row(string tag, string name, string channel, out int at)
        {
            foreach (string line in Lines)
            {
                string[] f = line.Split(' ');
                if (f[0] == tag && f[1] == name && f[2] == channel)
                {
                    int skip = tag == "S" ? 4 : 3;
                    at = tag == "S" ? int.Parse(f[3], CultureInfo.InvariantCulture) : 0;
                    var a = new float[f.Length - skip];
                    for (int i = 0; i < a.Length; i++) a[i] = float.Parse(f[skip + i], CultureInfo.InvariantCulture);
                    return a;
                }
            }
            throw new InvalidOperationException($"no {tag} row for {name} {channel}");
        }

        static float WindowRms(float[] s, int w)
        {
            double sum = 0;
            for (int i = w * Window; i < (w + 1) * Window; i++) sum += (double)s[i] * s[i];
            return (float)Math.Sqrt(sum / Window);
        }

        static List<AmbientShot> Shots(Scenario s)
        {
            var bed = new Bed(Noise, new Rng((uint)s.Seed), 0.01, Bed.EmitterMode.Inline);
            var events = new AmbientEvents(bed, 3);
            var shots = new List<AmbientShot>();
            int step = 0;
            for (double t = 0; t < s.Seconds; t += 0.1)
            {
                while (step + 1 < s.Steps.Count && s.Steps[step + 1].time <= t + 1e-9) step++;
                events.Advance(t, 0.25, s.Steps[step].state, (0, 1.7, 0), (6, 1.7 + 4.2, -5), shots);
            }
            return shots;
        }

        [Test]
        public void TheRandomClocksFireAtTheSameTimesAndPlacesAsThePrototypes()
        {
            int total = 0;
            var kinds = new HashSet<string>();
            foreach (Scenario s in Scenarios())
            {
                var want = new List<string[]>();
                foreach (string line in Lines)
                {
                    string[] f = line.Split(' ');
                    if (f[0] == "E" && f[1] == s.Name) want.Add(f);
                }
                List<AmbientShot> got = Shots(s);
                Assert.AreEqual(want.Count, got.Count, s.Name + " shot count");
                for (int i = 0; i < want.Count; i++)
                {
                    string[] f = want[i];
                    AmbientShot g = got[i];
                    string label = $"{s.Name} shot {i}";
                    Assert.AreEqual(f[3], g.Sound, label);
                    Assert.AreEqual(Golden.D(f[2]), g.Time, 1e-9, label + " time");
                    Assert.AreEqual(Golden.D(f[4]), g.X, 1e-9, label + " x");
                    Assert.AreEqual(Golden.D(f[5]), g.Y, 1e-9, label + " y");
                    Assert.AreEqual(Golden.D(f[6]), g.Z, 1e-9, label + " z");
                    Assert.AreEqual(Golden.D(f[7]), g.Volume, 1e-9, label + " volume");
                    if (f[8] == "-") Assert.IsFalse(g.Pitch.HasValue, label + " pitch");
                    else Assert.AreEqual(Golden.D(f[8]), g.Pitch.Value, 1e-9, label + " pitch");
                    if (f[9] == "-") Assert.IsFalse(g.Wet.HasValue, label + " wet");
                    else Assert.AreEqual(Golden.D(f[9]), g.Wet.Value, 1e-9, label + " wet");
                    kinds.Add(g.Sound);
                }
                total += want.Count;
            }
            Assert.Greater(total, 600);
            Assert.AreEqual(6, kinds.Count, "every clock fires");
        }

        [Test]
        public void ACarPassAndAVoiceCanBeLeftToTheRealTrafficAndPeopleWithoutChangingTheOtherClocks()
        {
            Scenario s = Scenarios().Find(x => x.Name == "events-long");
            var all = Shots(s);
            var bed = new Bed(Noise, new Rng((uint)s.Seed), 0.01, Bed.EmitterMode.Inline);
            var events = new AmbientEvents(bed, 3) { RandomCars = false, RandomVoices = false };
            var shots = new List<AmbientShot>();
            int step = 0;
            for (double t = 0; t < s.Seconds; t += 0.1)
            {
                while (step + 1 < s.Steps.Count && s.Steps[step + 1].time <= t + 1e-9) step++;
                events.Advance(t, 0.25, s.Steps[step].state, (0, 1.7, 0), (6, 5.9, -5), shots);
            }
            var rest = all.FindAll(a => a.Sound != "amb.carpass" && a.Sound != "amb.voice");
            Assert.AreEqual(rest.Count, shots.Count);
            for (int i = 0; i < rest.Count; i++) Assert.AreEqual(rest[i].Time, shots[i].Time, 1e-12);
            Assert.IsFalse(shots.Exists(a => a.Sound == "amb.carpass" || a.Sound == "amb.voice"));
        }

        [Test]
        public void ADripNeedsRainAndTheNeonBuzzComesFromTheNearestTubeWhenThereIsOne()
        {
            Scenario dry = new Scenario { Name = "x", Seed = 5, Seconds = 120 };
            dry.Steps.Add((0, new BedState(0, 0, 0, 1)));
            Assert.IsFalse(Shots(dry).Exists(a => a.Sound == "amb.drip"), "no drip in the dry");
            var bed = new Bed(Noise, new Rng(5), 0, Bed.EmitterMode.Inline);
            var events = new AmbientEvents(bed);
            var shots = new List<AmbientShot>();
            for (double t = 0; t < 200; t += 0.1) events.Advance(t, 0.25, new BedState(0, 0, 0, 1), (10, 1.7, 20), (11, 6, 17), shots);
            var buzz = shots.FindAll(a => a.Sound == "amb.neonbuzz");
            Assert.Greater(buzz.Count, 3);
            foreach (AmbientShot b in buzz)
            {
                Assert.AreEqual(11, b.X);
                Assert.AreEqual(6, b.Y);
                Assert.AreEqual(17, b.Z);
            }
        }

        [Test]
        public void AfterALongStallTheClocksResyncInsteadOfBurstingOutABacklog()
        {
            var bed = new Bed(Noise, new Rng(11), 0, Bed.EmitterMode.Inline);
            var events = new AmbientEvents(bed);
            var shots = new List<AmbientShot>();
            events.Advance(1, 0.25, new BedState(1, 0, 0, 1), (0, 1.7, 0), null, shots);
            shots.Clear();
            events.Advance(1000, 0.25, new BedState(1, 0, 0, 1), (0, 1.7, 0), null, shots);
            Assert.LessOrEqual(shots.Count, 6, "at most a few, not a thousand seconds' worth");
            foreach (AmbientShot a in shots) Assert.GreaterOrEqual(a.Time, 1000 - 0.25);
        }

        [Test]
        public void EveryWindowOfEveryWeatherIsAsLoudAsThePrototypesWithinAPercent()
        {
            List<Scenario> scenarios = Scenarios().FindAll(x => !x.EventsOnly);
            Assert.AreEqual(4, scenarios.Count);
            var failures = new List<string>();
            foreach (Scenario s in scenarios)
            {
                RenderBed(s, 1, out float[] left, out float[] right);
                foreach (var ch in new[] { ("L", left), ("R", right) })
                {
                    float[] want = Row("W", s.Name, ch.Item1, out _);
                    Assert.AreEqual(80, want.Length);
                    for (int w = 0; w < want.Length; w++)
                    {
                        double got = WindowRms(ch.Item2, w);
                        if (Math.Abs(got - want[w]) > 1.5e-3 + 0.01 * want[w]) failures.Add($"{s.Name} {ch.Item1} window {w}: {got:F5} against {want[w]:F5}");
                    }
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void ADryQuietBedAgreesSampleForSampleWithTheBrowsers()
        {
            // with no rain the patter's sparse impulses are quiet; everything else is the same noise, filtered and modulated the same way
            Scenario s = Scenarios().Find(x => x.Name == "dry-indoors-quiet");
            RenderBed(s, 1, out float[] left, out float[] right);
            foreach (var ch in new[] { ("L", left), ("R", right) })
            {
                foreach (int probe in new[] { 96000, 336000 })
                {
                    foreach (string line in Lines)
                    {
                        string[] f = line.Split(' ');
                        if (f[0] != "S" || f[1] != s.Name || f[2] != ch.Item1 || int.Parse(f[3], CultureInfo.InvariantCulture) != probe) continue;
                        for (int i = 4; i < f.Length; i++)
                        {
                            int idx = probe + (i - 4) * 8;
                            Assert.AreEqual(float.Parse(f[i], CultureInfo.InvariantCulture), ch.Item2[idx], 1e-4f, $"{ch.Item1} sample {idx}");
                        }
                    }
                }
            }
        }

        [Test]
        public void TheWeatherTurningIsFollowedSmoothlyNotInAStep()
        {
            Scenario s = Scenarios().Find(x => x.Name == "weather-turns");
            RenderBed(s, 1, out float[] left, out _);
            // the rain arrives at 3 s: the level over the next 100 ms windows climbs over a second or two rather than jumping
            double before = WindowRms(left, 29);
            double soon = WindowRms(left, 32);
            double later = WindowRms(left, 45);
            Assert.Greater(later, before * 1.3, "heavy rain is louder");
            Assert.Less(soon, before + (later - before) * 0.8, "but not at once");
        }

        [Test]
        public void ARenderIsTheSameEveryTimeForASeedAndDiffersForAnother()
        {
            float[] Make(uint seed)
            {
                var bed = new Bed(Noise, new Rng(seed), 0, Bed.EmitterMode.Inline);
                bed.Advance(0, new BedState(0.5, 0.5, 0, 1));
                return bed.Main.RenderMono(20000);
            }
            CollectionAssert.AreEqual(Make(5), Make(5));
            CollectionAssert.AreNotEqual(Make(5), Make(6));
        }

        [Test]
        public void TheEmittersAsGraphsOfTheirOwnAddUpToTheBedWithThemInside()
        {
            var state = new BedState(0.4, 0.3, 0.2, 1);
            var inline = new Bed(Noise, new Rng(77), 0, Bed.EmitterMode.Inline);
            inline.Advance(0, state);
            float[] whole = inline.Main.RenderMono(30000);

            var apart = new Bed(Noise, new Rng(77), 0, Bed.EmitterMode.Separate);
            apart.Advance(0, state);
            apart.AdvanceNeon(0, state);
            apart.AdvanceAirConditioning(0, state);
            float[] main = apart.Main.RenderMono(30000);
            float[] neon = apart.Neon.RenderMono(30000);
            float[] ac = apart.AirConditioning.RenderMono(30000);
            double neonLevel = 0, acLevel = 0;
            for (int i = 0; i < whole.Length; i++)
            {
                Assert.AreEqual(whole[i], main[i] + neon[i] + ac[i], 2e-5f, "sample " + i);
                neonLevel += neon[i] * (double)neon[i];
                acLevel += ac[i] * (double)ac[i];
            }
            Assert.Greater(neonLevel, 0);
            Assert.Greater(acLevel, 0);
            Assert.AreEqual(inline.NextSiren, apart.NextSiren, "the random events are scheduled the same either way");
            Assert.AreEqual(inline.NextCar, apart.NextCar);
        }

        [Test]
        public void BeingIndoorsQuietensTheOutsideAndTheWeatherAndDensityMoveTheLevels()
        {
            double Level(BedState state)
            {
                var bed = new Bed(Noise, new Rng(9), 0, Bed.EmitterMode.Separate);
                bed.Advance(0, state);
                // settle: render several seconds, then measure
                float[] o = bed.Main.RenderMono(Rate * 6);
                double s = 0;
                for (int i = Rate * 4; i < o.Length; i++) s += o[i] * (double)o[i];
                return Math.Sqrt(s / (o.Length - Rate * 4));
            }
            double outside = Level(new BedState(0, 0, 0, 1));
            Assert.Less(Level(new BedState(0, 0, 1, 1)), outside * 0.6, "shut indoors");
            Assert.Greater(Level(new BedState(1, 0, 0, 1)), outside * 1.3, "rain");
            Assert.Greater(Level(new BedState(0, 1, 0, 1)), outside * 1.02, "wind");
            Assert.Greater(Level(new BedState(0, 0, 0, 2)), outside * 1.3, "a crowded city");
            Assert.Less(Level(new BedState(0, 0, 0, 0)), outside * 0.8, "an empty one");
        }

        [Test]
        public void AMinuteOfTheHeaviestWeatherIsFiniteAndBounded()
        {
            var bed = new Bed(Noise, new Rng(42), 0, Bed.EmitterMode.Separate);
            bed.Main.CoefficientInterval = 32;
            bed.Advance(0, new BedState(1, 1, 0, 2));
            double peak = 0;
            double sum = 0;
            var buffer = new float[2 * 4096];
            for (int block = 0; block < Rate * 60 / 4096; block++)
            {
                bed.Main.Fill(buffer, 2, 4096);
                foreach (float v in buffer)
                {
                    Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v));
                    peak = Math.Max(peak, Math.Abs(v));
                    sum += v;
                }
            }
            Assert.Less(peak, 1.5, "the heaviest weather is loud but bounded; the master limiter deals with the rest");
            Assert.Greater(peak, 0.05);
            Assert.Less(Math.Abs(sum / (Rate * 60 * 2)), 0.01, "no DC offset");
        }

        [Test]
        public void TheStateIsHeldInRange()
        {
            var s = new BedState(5, -1, 9, 7);
            Assert.AreEqual(1.0, s.Rain);
            Assert.AreEqual(0.0, s.Wind);
            Assert.AreEqual(1.0, s.Indoor);
            Assert.AreEqual(2.0, s.Density);
            Assert.AreEqual(0.0, new BedState(double.NaN, double.NaN, double.NaN, double.NaN).Rain);
        }
    }
}
