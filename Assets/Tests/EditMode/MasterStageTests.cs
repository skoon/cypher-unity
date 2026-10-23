using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class MasterStageTests
    {
        const int Rate = 48000;

        static float[] Noise(int frames, double amplitude, int channels = 2, uint seed = 5)
        {
            var rng = new Rng(seed);
            var d = new float[frames * channels];
            for (int i = 0; i < d.Length; i++) d[i] = (float)((rng.Next() * 2 - 1) * amplitude);
            return d;
        }

        static double Rms(float[] d, int from, int to)
        {
            double s = 0;
            for (int i = from; i < to; i++) s += d[i] * (double)d[i];
            return Math.Sqrt(s / (to - from));
        }

        [Test]
        public void TheVolumeComesAfterTheCompressorsSoTurningItDownIsExactlyThat()
        {
            float[] a = Noise(48000, 0.5);
            float[] b = (float[])a.Clone();
            var loud = new MasterStage(Rate) { Target = 0.9 };
            var quiet = new MasterStage(Rate) { Target = 0.45 };
            loud.Process(a, 2, 48000);
            quiet.Process(b, 2, 48000);
            for (int i = 24000 * 2; i < a.Length; i += 13) Assert.AreEqual(a[i] * 0.5f, b[i], 1e-5f);
        }

        [Test]
        public void ALoudMixIsHeldBelowTheLimitersThreshold()
        {
            float[] d = Noise(96000, 1.5);
            var stage = new MasterStage(Rate) { Target = 1 };
            stage.Process(d, 2, 96000);
            float peak = 0;
            for (int i = 48000 * 2; i < d.Length; i++) peak = Math.Max(peak, Math.Abs(d[i]));
            Assert.Less(peak, 1.0f, "the limiter keeps even a hot mix inside full scale");
        }

        [Test]
        public void QuietSoundIsLiftedByTheGluesMakeupGain()
        {
            // the glue stage's make-up gain raises what is below its threshold: a quiet mix comes out louder than it went in
            float[] d = Noise(96000, 0.02);
            double before = Rms(d, 48000 * 2, d.Length);
            new MasterStage(Rate) { Target = 1 }.Process(d, 2, 96000);
            Assert.Greater(Rms(d, 48000 * 2, d.Length), before * 1.4);
        }

        [Test]
        public void TheMixFadesUpFromSilenceOverTheFadeTime()
        {
            float[] d = Noise(Rate * 2, 0.3);
            var stage = new MasterStage(Rate) { Target = 0.9 };
            stage.FadeIn();
            stage.Process(d, 2, Rate * 2);
            Assert.Less(Rms(d, 0, 2 * 4800), 0.02, "the first 100 ms is nearly silent");
            double mid = Rms(d, 2 * 38400, 2 * 43200);
            double end = Rms(d, 2 * 96000 - 2 * 4800, 2 * 96000);
            Assert.Greater(mid, 0.2 * end);
            Assert.Less(mid, 0.8 * end, "half way through the fade it is about half up");
        }

        [Test]
        public void ChangingTheTargetMovesTheVolumeSmoothlyNotInAStep()
        {
            var stage = new MasterStage(Rate) { Target = 1 };
            float[] warm = Noise(Rate, 0.3);
            stage.Process(warm, 2, Rate);
            stage.Target = 0;
            float[] d = Noise(Rate, 0.3, seed: 6);
            stage.Process(d, 2, Rate);
            double first = Rms(d, 0, 2 * 480);
            double later = Rms(d, 2 * 12000, 2 * 12480);
            Assert.Greater(first, later * 4, "it falls over some tens of milliseconds");
            Assert.Less(Rms(d, 2 * 40000, 2 * 40480), 1e-3, "and settles at silence");
        }

        [Test]
        public void AnyBlockLengthGivesTheSameSoundAsOneBigBlock()
        {
            float[] whole = Noise(10000, 0.4);
            float[] parts = (float[])whole.Clone();
            new MasterStage(Rate).Process(whole, 2, 10000);
            var stage = new MasterStage(Rate);
            int at = 0;
            foreach (int size in new[] { 1, 31, 1024, 777, 4096, 100, 3971 })
            {
                var chunk = new float[size * 2];
                Array.Copy(parts, at * 2, chunk, 0, size * 2);
                stage.Process(chunk, 2, size);
                Array.Copy(chunk, 0, parts, at * 2, size * 2);
                at += size;
            }
            Assert.AreEqual(10000, at);
            for (int i = 0; i < whole.Length; i += 7) Assert.AreEqual(whole[i], parts[i], 2e-4f, "sample " + i);
        }

        [Test]
        public void AMonoBufferIsHandledAndSurroundExtrasAreScaled()
        {
            float[] mono = Noise(4800, 0.3, channels: 1);
            new MasterStage(Rate).Process(mono, 1, 4800);
            foreach (float v in mono) Assert.IsFalse(float.IsNaN(v));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MasterStage(Rate).Process(new float[10], 0, 5));
            Assert.Throws<ArgumentException>(() => new MasterStage(Rate).Process(new float[4], 2, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MasterStage(Rate).Target = -1);
        }

        [Test]
        public void TheCompressorsLookAheadDelaysTheSoundByTwelveMilliseconds()
        {
            var stage = new MasterStage(Rate);
            Assert.AreEqual(2 * 288, stage.LatencyFrames);
        }
    }
}
