using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    public enum NoiseKind
    {
        White,
        Pink,
        Brown,
        Velvet,
    }

    /// <summary>
    /// The four noise beds of the prototype's Dsp.js, sample for sample: the same generators on the same seeded stream, normalised and cross-faded into a seamless loop the same way, with the
    /// arithmetic in doubles and the stores in single precision as the prototype's Float32Arrays have it. Everything else in the audio is made from these and oscillators.
    /// </summary>
    public static class AudioNoise
    {
        public const double Seconds = 4.0;
        public const double LoopFadeSeconds = 0.06;

        /// <summary>The seed of the prototype's DspCache.</summary>
        public const int DspSeed = 7717;

        /// <summary>Added to the cache's seed (times 977) for each kind.</summary>
        public static int Salt(NoiseKind kind)
        {
            switch (kind)
            {
                case NoiseKind.White: return 11;
                case NoiseKind.Pink: return 23;
                case NoiseKind.Brown: return 37;
                case NoiseKind.Velvet: return 53;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown noise");
            }
        }

        /// <summary>A seamlessly loopable noise bed of <see cref="Seconds"/> less the cross-fade, as the prototype's makeNoise.</summary>
        public static float[] Make(NoiseKind kind, int sampleRate, uint seed)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "the sample rate must be positive");
            int n = JsRound(Seconds * sampleRate);
            var buf = new float[n];
            var rng = new Rng(seed);
            switch (kind)
            {
                case NoiseKind.Pink: FillPink(buf, rng); break;
                case NoiseKind.Brown: FillBrown(buf, rng); break;
                case NoiseKind.Velvet: FillVelvet(buf, rng, 0.02); break;
                case NoiseKind.White: FillWhite(buf, rng); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown noise");
            }
            NormalizePeak(buf, kind == NoiseKind.Velvet ? 1.0 : 0.9);
            return CrossfadeLoop(buf, JsRound(LoopFadeSeconds * sampleRate));
        }

        /// <summary>The bed the prototype's DspCache holds for this kind at this rate.</summary>
        public static float[] Make(NoiseKind kind, int sampleRate) => Make(kind, sampleRate, unchecked((uint)(DspSeed + Salt(kind) * 977)));

        /// <summary>JavaScript's Math.round: halves go up.</summary>
        static int JsRound(double x) => (int)Math.Floor(x + 0.5);

        static void FillWhite(float[] output, Rng rng)
        {
            for (int i = 0; i < output.Length; i++) output[i] = (float)(rng.Next() * 2 - 1);
        }

        /// <summary>Paul Kellet's refined pink filter, about -3 dB per octave.</summary>
        static void FillPink(float[] output, Rng rng)
        {
            double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
            for (int i = 0; i < output.Length; i++)
            {
                double w = rng.Next() * 2 - 1;
                b0 = 0.99886 * b0 + w * 0.0555179;
                b1 = 0.99332 * b1 + w * 0.0750759;
                b2 = 0.96900 * b2 + w * 0.1538520;
                b3 = 0.86650 * b3 + w * 0.3104856;
                b4 = 0.55000 * b4 + w * 0.5329522;
                b5 = -0.7616 * b5 - w * 0.0168980;
                output[i] = (float)((b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362) * 0.11);
                b6 = w * 0.115926;
            }
        }

        /// <summary>A leaky integrator over white noise, -6 dB per octave.</summary>
        static void FillBrown(float[] output, Rng rng)
        {
            double last = 0;
            for (int i = 0; i < output.Length; i++)
            {
                double w = rng.Next() * 2 - 1;
                last = (last + 0.02 * w) / 1.02;
                output[i] = (float)(last * 3.4);
            }
        }

        /// <summary>Sparse signed impulses; rain on a hard surface.</summary>
        static void FillVelvet(float[] output, Rng rng, double density)
        {
            Array.Clear(output, 0, output.Length);
            for (int i = 0; i < output.Length; i++)
            {
                if (rng.Next() < density) output[i] = rng.Next() < 0.5 ? -1f : 1f;
            }
        }

        static void NormalizePeak(float[] buf, double target)
        {
            double peak = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                double a = buf[i] < 0 ? -buf[i] : buf[i];
                if (a > peak) peak = a;
            }
            if (peak < 1e-9) return;
            double k = target / peak;
            for (int i = 0; i < buf.Length; i++) buf[i] = (float)(buf[i] * k);
        }

        /// <summary>Fades the tail into the head and drops the overlap, so the loop has no step at its seam.</summary>
        static float[] CrossfadeLoop(float[] buf, int fadeN)
        {
            int n = buf.Length;
            if (fadeN <= 0 || fadeN * 2 >= n) return buf;
            var output = new float[n - fadeN];
            Array.Copy(buf, output, n - fadeN);
            for (int i = 0; i < fadeN; i++)
            {
                double t = (double)i / fadeN;
                output[i] = (float)(buf[i] * t + buf[n - fadeN + i] * (1 - t));
            }
            return output;
        }
    }

    /// <summary>Holds the noise beds for one sample rate, made once on first use; safe to ask from any thread.</summary>
    public sealed class AudioNoiseBank
    {
        readonly int _sampleRate;
        readonly Dictionary<NoiseKind, float[]> _beds = new Dictionary<NoiseKind, float[]>();

        public AudioNoiseBank(int sampleRate)
        {
            _sampleRate = sampleRate;
        }

        public int SampleRate => _sampleRate;

        public float[] Get(NoiseKind kind)
        {
            lock (_beds)
            {
                if (!_beds.TryGetValue(kind, out float[] bed))
                {
                    bed = AudioNoise.Make(kind, _sampleRate);
                    _beds[kind] = bed;
                }
                return bed;
            }
        }
    }
}
