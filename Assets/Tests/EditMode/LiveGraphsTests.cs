using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class LiveGraphsTests
    {
        const int Rate = 48000;

        static float[] Click()
        {
            var c = new float[64];
            for (int i = 0; i < c.Length; i++) c[i] = (float)(0.8 * Math.Sin(2 * Math.PI * 1000 * i / Rate) * (1 - i / 64.0));
            return c;
        }

        static double Rms(float[] d, int from, int to)
        {
            double s = 0;
            for (int i = from; i < to; i++) s += d[i] * (double)d[i];
            return Math.Sqrt(s / Math.Max(1, to - from));
        }

        // ---- the score's graph ----

        [Test]
        public void ANoteForTheOutputPlaysUntouchedAtItsFrame()
        {
            var score = new ScoreGraph(Rate);
            score.Direct.Schedule(5000, Click(), 1f);
            float[] o = score.Graph.RenderMono(8000);
            Assert.AreEqual(0.0, Rms(o, 0, 4900), 1e-3, "nothing before it but the pad's silence");
            Assert.Greater(Rms(o, 5000, 5064), 0.1);
        }

        [Test]
        public void ANoteThroughTheDuckIsPulledDownByAKickAndOneForTheOutputIsNot()
        {
            float[] Level(ScoreFeed feed, bool kick)
            {
                var score = new ScoreGraph(Rate);
                var tone = new float[4800];
                for (int i = 0; i < tone.Length; i++) tone[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / Rate));
                score.PlayerFor(feed).Schedule(2000, tone, 1f);
                if (kick) score.Enqueue(new ScoreChange(ScoreChangeKind.DuckPump, 0.4, 3000.0 / Rate, 0.1));
                score.ApplyPending();
                return score.Graph.RenderMono(8000);
            }
            double ducked = Rms(Level(ScoreFeed.Duck, true), 3200, 3600);
            double free = Rms(Level(ScoreFeed.Duck, false), 3200, 3600);
            Assert.Less(ducked, free * 0.6, "the sidechain pumps what goes through it");
            double direct = Rms(Level(ScoreFeed.Bus, true), 3200, 3600);
            double directFree = Rms(Level(ScoreFeed.Bus, false), 3200, 3600);
            Assert.AreEqual(directFree, direct, directFree * 0.01, "drums are not ducked");
            double later = Rms(Level(ScoreFeed.Duck, true), 5500, 6000);
            Assert.Greater(later, ducked * 1.3, "and it recovers");
        }

        [Test]
        public void ANoteThroughTheDelayEchoesAfterADottedEighth()
        {
            var score = new ScoreGraph(Rate);
            score.ThroughDelay.Schedule(1000, Click(), 1f);
            score.ApplyPending();
            float[] o = score.Graph.RenderMono(40000);
            int echo = 1000 + (int)Math.Round(60.0 / 108 * 0.75 * Rate);
            Assert.Greater(Rms(o, 1000, 1064), 0.1, "the note itself");
            Assert.Greater(Rms(o, echo - 20, echo + 120), 0.01, "the echo, about " + echo);
            Assert.Less(Rms(o, echo - 3000, echo - 500), 0.002, "and quiet between");
        }

        [Test]
        public void ThePadIsSilentUntilTheScoreOpensItAndThenSoundsLowAndSteady()
        {
            var score = new ScoreGraph(Rate);
            float[] quiet = score.Graph.RenderMono(Rate / 2);
            Assert.Less(Rms(quiet, Rate / 4, Rate / 2), 0.002);
            score.Enqueue(new ScoreChange(ScoreChangeKind.PadGain, 0.16, 0, 0.05));
            score.Enqueue(new ScoreChange(ScoreChangeKind.PadFilter, 1500, 0, 0.05));
            score.ApplyPending();
            float[] loud = score.Graph.RenderMono(Rate);
            Assert.Greater(Rms(loud, Rate / 2, Rate), 0.02);
        }

        [Test]
        public void TheChordsGlideRetunesThePad()
        {
            float Centroid(double glideTo)
            {
                var score = new ScoreGraph(Rate);
                score.Enqueue(new ScoreChange(ScoreChangeKind.PadGain, 0.16, 0, 0.05));
                score.Enqueue(new ScoreChange(ScoreChangeKind.PadFilter, 3000, 0, 0.05));
                for (int i = 0; i < 6; i++) score.Enqueue(new ScoreChange(ScoreChangeKind.PadGlide, glideTo * (1 + i * 0.25), 0.1, 0.05, i));
                score.ApplyPending();
                float[] o = score.Graph.RenderMono(Rate);
                var tail = new float[8192];
                Array.Copy(o, Rate - 8192, tail, 0, 8192);
                return (float)AudioAnalysis.Analyze(tail, Rate).Centroid;
            }
            Assert.Greater(Centroid(400), Centroid(100) * 1.5);
        }

        [Test]
        public void ChangesQueuedFromAnotherThreadAreAppliedOnTheGraphsThread()
        {
            var score = new ScoreGraph(Rate);
            var thread = new System.Threading.Thread(() =>
            {
                for (int i = 0; i < 100; i++) score.Enqueue(new ScoreChange(ScoreChangeKind.DelayWet, 0.3, i * 0.01, 0.1));
            });
            thread.Start();
            thread.Join();
            score.ApplyPending();
            score.ApplyPending();
            float[] o = score.Graph.RenderMono(2000);
            foreach (float v in o) Assert.IsFalse(float.IsNaN(v));
        }

        [Test]
        public void EachFeedHasItsOwnPlayer()
        {
            var score = new ScoreGraph(Rate);
            Assert.AreSame(score.Direct, score.PlayerFor(ScoreFeed.Bus));
            Assert.AreSame(score.ThroughDuck, score.PlayerFor(ScoreFeed.Duck));
            Assert.AreSame(score.ThroughDelay, score.PlayerFor(ScoreFeed.Delay));
            Assert.AreNotSame(score.Direct, score.ThroughDuck);
        }

        // ---- a car ----

        [Test]
        public void ACarGetsLouderBrighterAndHissierWithSpeed()
        {
            var noise = new AudioNoiseBank(Rate);
            double[] Measure(double speed)
            {
                var car = new CarVoiceGraph(noise, new Rng(3));
                car.Apply(VehicleSound.For(speed, 4.5, 0.5), 1);
                float[] o = car.Graph.RenderMono(Rate * 2);
                var tail = new float[8192];
                Array.Copy(o, o.Length - 8192, tail, 0, 8192);
                AudioStats s = AudioAnalysis.Analyze(tail, Rate);
                return new[] { s.Rms, s.Centroid };
            }
            double[] slow = Measure(1);
            double[] fast = Measure(14);
            Assert.Greater(fast[0], slow[0] * 2, "louder");
            Assert.Greater(fast[1], slow[1] * 1.3, "brighter");
        }

        [Test]
        public void ASpeedOnlyChangesTheSoundWhenItIsANewDecision()
        {
            var car = new CarVoiceGraph(new AudioNoiseBank(Rate), new Rng(3));
            car.Apply(VehicleSound.For(2, 4.5, 0), 5);
            float[] a = car.Graph.RenderMono(Rate);
            car.Apply(VehicleSound.For(14, 4.5, 0), 5); // the same stamp: ignored
            float[] b = car.Graph.RenderMono(Rate);
            Assert.AreEqual(Rms(a, Rate / 2, Rate), Rms(b, Rate / 2, Rate), Rms(a, Rate / 2, Rate) * 0.1);
            car.Apply(VehicleSound.For(14, 4.5, 0), 6);
            float[] c = car.Graph.RenderMono(Rate * 2);
            Assert.Greater(Rms(c, Rate, Rate * 2), Rms(a, Rate / 2, Rate) * 1.5);
        }
    }
}
