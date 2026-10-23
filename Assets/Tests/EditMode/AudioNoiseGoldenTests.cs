using System;
using System.Collections.Generic;
using System.Globalization;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The noise beds and the analysis helper against the prototype's own (tools/gen-golden-audio.mjs runs its Dsp.js and Analysis.js under node).</summary>
    public class AudioNoiseGoldenTests
    {
        static readonly string[] Lines = Golden.Lines("audio_noise_golden.txt");

        static NoiseKind Kind(string s)
        {
            switch (s)
            {
                case "white": return NoiseKind.White;
                case "pink": return NoiseKind.Pink;
                case "brown": return NoiseKind.Brown;
                case "velvet": return NoiseKind.Velvet;
                default: throw new ArgumentException(s);
            }
        }

        /// <summary>FNV-1a over the bytes of the samples as single-precision floats, little-endian, as the generator hashes them.</summary>
        static string Fnv(float[] f)
        {
            uint h = 0x811c9dc5;
            foreach (float v in f)
            {
                int bits = BitConverter.SingleToInt32Bits(v);
                for (int b = 0; b < 4; b++)
                {
                    h ^= (uint)((bits >> (8 * b)) & 0xff);
                    h = unchecked(h * 0x01000193);
                }
            }
            return h.ToString("x8", CultureInfo.InvariantCulture);
        }

        [Test]
        public void EveryNoiseBedIsTheSameSampleForSampleAsThePrototypes()
        {
            List<string[]> rows = Golden.Rows(Lines, "N");
            Assert.AreEqual(8, rows.Count, "four kinds at two rates");
            foreach (string[] r in rows)
            {
                var kind = Kind(r[1]);
                int rate = Golden.I(r[2]);
                float[] bed = AudioNoise.Make(kind, rate);
                Assert.AreEqual(Golden.I(r[3]), bed.Length, $"{r[1]}@{rate} length");
                Assert.AreEqual(r[4], Fnv(bed), $"{r[1]}@{rate} every sample");
                int[] probes = { 0, 1, 2, 3, 100, 1000, 2879, 2880, 50000, bed.Length - 1 };
                for (int i = 0; i < probes.Length; i++)
                {
                    Assert.AreEqual((float)Golden.D(r[5 + i]), bed[probes[i]], $"{r[1]}@{rate}[{probes[i]}]");
                }
            }
        }

        [Test]
        public void ADifferentSeedGivesADifferentBedAndTheSameSeedTheSameOne()
        {
            string[] r = Golden.Rows(Lines, "S")[0];
            float[] a = AudioNoise.Make(Kind(r[1]), Golden.I(r[2]), uint.Parse(r[3], CultureInfo.InvariantCulture));
            Assert.AreEqual(Golden.I(r[4]), a.Length);
            Assert.AreEqual(r[5], Fnv(a));
            Assert.AreEqual(Fnv(a), Fnv(AudioNoise.Make(NoiseKind.Pink, 48000, 12345)));
            Assert.AreNotEqual(Fnv(a), Fnv(AudioNoise.Make(NoiseKind.Pink, 48000, 12346)));
        }

        [Test]
        public void TheBankMakesEachBedOnceAndHandsOutTheSameArray()
        {
            var bank = new AudioNoiseBank(48000);
            float[] a = bank.Get(NoiseKind.Brown);
            Assert.AreSame(a, bank.Get(NoiseKind.Brown));
            Assert.AreNotSame(a, bank.Get(NoiseKind.Pink));
            Assert.AreEqual(48000, bank.SampleRate);
        }

        [Test]
        public void ABedLoopsWithoutAStepAtTheSeam()
        {
            foreach (NoiseKind kind in new[] { NoiseKind.White, NoiseKind.Pink, NoiseKind.Brown })
            {
                float[] bed = AudioNoise.Make(kind, 48000);
                float peak = 0;
                foreach (float v in bed) peak = Math.Max(peak, Math.Abs(v));
                Assert.AreEqual(0.9f, peak, 0.05f, $"{kind} is normalised to about 0.9");
                // the crossfade makes the last sample lead into the first like any two neighbours do
                double typicalStep = 0;
                for (int i = 1; i < 2000; i++) typicalStep += Math.Abs(bed[i] - bed[i - 1]);
                typicalStep /= 1999;
                double seamStep = Math.Abs(bed[0] - bed[bed.Length - 1]);
                Assert.Less(seamStep, typicalStep * 12 + 0.05, $"{kind} seam");
            }
        }

        [Test]
        public void VelvetIsSparseSignedImpulsesAtTheStatedDensity()
        {
            float[] v = AudioNoise.Make(NoiseKind.Velvet, 48000);
            int hits = 0;
            foreach (float s in v)
            {
                if (s != 0)
                {
                    hits++;
                    Assert.IsTrue(s == 1f || s == -1f || Math.Abs(s) < 1f, "an impulse is at most full scale");
                }
            }
            Assert.AreEqual(0.02, (double)hits / v.Length, 0.004);
        }

        [Test]
        public void ANoiseOfNoSampleRateIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioNoise.Make(NoiseKind.White, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioNoise.Make((NoiseKind)9, 48000));
        }

        static float[] Input(string[] r, out float[] second)
        {
            second = null;
            int sr = Golden.I(r[2]);
            int n = sr;
            var f = new float[n];
            switch (r[1])
            {
                case "sine":
                    for (int i = 0; i < n; i++) f[i] = (float)(Golden.D(r[4]) * Math.Sin(2 * Math.PI * Golden.D(r[3]) * i / sr));
                    break;
                case "noise":
                    Array.Copy(AudioNoise.Make(NoiseKind.White, sr, uint.Parse(r[3], CultureInfo.InvariantCulture)), f, n);
                    break;
                case "burst":
                {
                    float[] noise = AudioNoise.Make(NoiseKind.White, sr, uint.Parse(r[3], CultureInfo.InvariantCulture));
                    int at = Golden.I(r[4]), len = Golden.I(r[5]);
                    double gain = Golden.D(r[6]);
                    for (int i = 0; i < len; i++) f[i + at] = (float)(noise[i] * (1 - (double)i / len) * gain);
                    break;
                }
                case "stereo":
                {
                    second = new float[n];
                    double hz = Golden.D(r[3]), amp = Golden.D(r[4]), phase = Golden.D(r[5]);
                    for (int i = 0; i < n; i++)
                    {
                        f[i] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / sr));
                        second[i] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / sr + phase));
                    }
                    break;
                }
                default: throw new ArgumentException(r[1]);
            }
            return f;
        }

        [Test]
        public void AnalysisAgreesWithThePrototypesOnTheSameBuffers()
        {
            var inputs = new Dictionary<string, string[]>();
            foreach (string[] r in Golden.Rows(Lines, "I")) inputs[r[1]] = r;
            List<string[]> rows = Golden.Rows(Lines, "A");
            Assert.AreEqual(4, rows.Count);
            foreach (string[] r in rows)
            {
                float[] a = Input(inputs[r[1]], out float[] b);
                int sr = Golden.I(inputs[r[1]][2]);
                AudioStats s = b == null ? AudioAnalysis.Analyze(a, sr) : AudioAnalysis.Analyze(new[] { a, b }, sr);
                string tag = r[1];
                double rel(string field, double got, double want) { Assert.AreEqual(want, got, Math.Max(1e-9, Math.Abs(want) * 1e-6), tag + " " + field); return got; }
                rel("peak", s.Peak, Golden.D(r[2]));
                rel("rms", s.Rms, Golden.D(r[3]));
                rel("peakDb", s.PeakDb, Golden.D(r[4]));
                rel("rmsDb", s.RmsDb, Golden.D(r[5]));
                rel("centroid", s.Centroid, Golden.D(r[6]));
                for (int band = 0; band < 5; band++) rel("band " + band, s.Band(band), Golden.D(r[7 + band]));
                rel("nonSilent", s.NonSilentRatio, Golden.D(r[12]));
                rel("duration", s.DurationSeconds, Golden.D(r[13]));
                Assert.AreEqual(Golden.I(r[14]), s.Channels, tag + " channels");
                if (b != null) rel("width", AudioAnalysis.StereoWidth(a, b), Golden.D(r[15]));
            }
        }

        [Test]
        public void AToneIsMeasuredAtItsLevelAndFrequencyAndSilenceIsSilence()
        {
            var tone = new float[48000];
            for (int i = 0; i < tone.Length; i++) tone[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / 48000.0));
            AudioStats s = AudioAnalysis.Analyze(tone, 48000);
            Assert.AreEqual(0.5, s.Peak, 1e-4);
            Assert.AreEqual(0.5 / Math.Sqrt(2), s.Rms, 1e-4);
            Assert.AreEqual(1000, s.Centroid, 40);
            Assert.Greater(s.Mid, 0.9, "a 1 kHz tone is in the mid band");
            AudioStats silent = AudioAnalysis.Analyze(new float[4800], 48000);
            Assert.AreEqual(0, silent.Peak);
            Assert.AreEqual(-140, silent.RmsDb);
            Assert.AreEqual(0, silent.NonSilentRatio);
            Assert.AreEqual(0, silent.Centroid);
        }

        [Test]
        public void AnalysisRefusesNothingToAnalyse()
        {
            Assert.Throws<ArgumentException>(() => AudioAnalysis.Analyze(new float[0][], 48000));
            Assert.Throws<ArgumentException>(() => AudioAnalysis.Analyze(new float[0], 48000));
            Assert.Throws<ArgumentException>(() => AudioAnalysis.Analyze(new[] { new float[10], new float[11] }, 48000));
        }

        [Test]
        public void ADecayingImpulseHasTheDecayTimeItWasMadeWith()
        {
            int sr = 48000;
            var ir = new float[sr * 2];
            double k = Math.Exp(-6.907755 / (1.0 * sr)); // -60 dB at one second
            double env = 1;
            var rng = new Rng(3);
            for (int i = 0; i < ir.Length; i++)
            {
                ir[i] = (float)((rng.Next() * 2 - 1) * env);
                env *= k;
            }
            Assert.AreEqual(1.0, AudioAnalysis.DecayTime(ir, sr, 40), 0.08);
            Assert.AreEqual(0, AudioAnalysis.DecayTime(new float[100], sr));
        }
    }
}
