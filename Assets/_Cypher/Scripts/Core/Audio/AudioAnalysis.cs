using System;

namespace Cypher.Core
{
    /// <summary>What <see cref="AudioAnalysis.Analyze"/> measures of a sound: its level, where its energy sits in the spectrum, and how much of it is not silence.</summary>
    public readonly struct AudioStats
    {
        public readonly double Peak, Rms, PeakDb, RmsDb, Centroid;
        /// <summary>Share of the spectrum's magnitude in each band (sub 20-80 Hz, low 80-300, mid 300-1500, high 1.5-6 kHz, air 6-20 kHz), around the loudest 10 ms.</summary>
        public readonly double Sub, Low, Mid, High, Air;
        public readonly double NonSilentRatio, DurationSeconds;
        public readonly int Channels;

        public AudioStats(double peak, double rms, double peakDb, double rmsDb, double centroid, double sub, double low, double mid, double high, double air,
            double nonSilentRatio, double durationSeconds, int channels)
        {
            Peak = peak; Rms = rms; PeakDb = peakDb; RmsDb = rmsDb; Centroid = centroid;
            Sub = sub; Low = low; Mid = mid; High = high; Air = air;
            NonSilentRatio = nonSilentRatio; DurationSeconds = durationSeconds; Channels = channels;
        }

        public double Band(int i)
        {
            switch (i)
            {
                case 0: return Sub;
                case 1: return Low;
                case 2: return Mid;
                case 3: return High;
                case 4: return Air;
                default: throw new ArgumentOutOfRangeException(nameof(i), i, "there are five bands");
            }
        }
    }

    /// <summary>
    /// The prototype's Analysis.js: peak, RMS, spectral centroid and band shares of a sound, taken from a Hann-windowed 4096-point transform around its loudest 10 ms, with
    /// the same single-precision stores as its Float32Arrays, so the numbers agree with the browser's. These are what the sounds are checked and tuned by, since nothing can listen.
    /// </summary>
    public static class AudioAnalysis
    {
        const int FftSize = 4096;
        static readonly double[] BandLow = { 20, 80, 300, 1500, 6000 };
        static readonly double[] BandHigh = { 80, 300, 1500, 6000, 20000 };

        /// <summary>In-place iterative radix-2 transform; the arrays are single precision as in the prototype and must be a power of two long.</summary>
        static void Fft(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    float t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        double ur = re[i + k], ui = im[i + k];
                        double vr = re[i + k + len / 2] * cr - im[i + k + len / 2] * ci;
                        double vi = re[i + k + len / 2] * ci + im[i + k + len / 2] * cr;
                        re[i + k] = (float)(ur + vr); im[i + k] = (float)(ui + vi);
                        re[i + k + len / 2] = (float)(ur - vr); im[i + k + len / 2] = (float)(ui - vi);
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }

        static double Db(double v) => v > 1e-7 ? 20 * Math.Log10(v) : -140;

        /// <param name="channels">One array per channel, all the same length.</param>
        public static AudioStats Analyze(float[][] channels, int sampleRate)
        {
            if (channels == null || channels.Length == 0) throw new ArgumentException("a sound has at least one channel", nameof(channels));
            int n = channels[0].Length;
            int ch = channels.Length;
            if (n == 0) throw new ArgumentException("the sound is empty", nameof(channels));
            var mono = new float[n];
            for (int c = 0; c < ch; c++)
            {
                float[] d = channels[c];
                if (d.Length != n) throw new ArgumentException("the channels differ in length", nameof(channels));
                for (int i = 0; i < n; i++) mono[i] = (float)(mono[i] + (double)d[i] / ch);
            }

            double peak = 0, sum = 0, loud = 0;
            int loudIndex = 0, nonSilent = 0;
            int win = Math.Max(1, (int)Math.Floor(sampleRate * 0.01));
            double running = 0;
            for (int i = 0; i < n; i++)
            {
                double v = mono[i];
                double a = v < 0 ? -v : v;
                if (a > peak) peak = a;
                if (a > 1e-4) nonSilent++;
                sum += v * v;
                running += v * v;
                if (i >= win) running -= (double)mono[i - win] * mono[i - win];
                if (running > loud)
                {
                    loud = running;
                    loudIndex = i;
                }
            }
            double rms = Math.Sqrt(sum / n);

            int start = Math.Max(0, Math.Min(n - FftSize, loudIndex - (FftSize >> 1)));
            var re = new float[FftSize];
            var im = new float[FftSize];
            for (int i = 0; i < FftSize; i++)
            {
                double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
                re[i] = (float)((start + i < n ? mono[start + i] : 0) * w);
            }
            Fft(re, im);

            var bands = new double[5];
            double num = 0, den = 0, total = 0;
            double binHz = (double)sampleRate / FftSize;
            for (int k = 1; k < FftSize / 2; k++)
            {
                double mag = Math.Sqrt((double)re[k] * re[k] + (double)im[k] * im[k]);
                double f = k * binHz;
                num += f * mag;
                den += mag;
                total += mag;
                for (int b = 0; b < bands.Length; b++)
                {
                    if (f >= BandLow[b] && f < BandHigh[b])
                    {
                        bands[b] += mag;
                        break;
                    }
                }
            }
            for (int b = 0; b < bands.Length; b++) bands[b] = total > 0 ? bands[b] / total : 0;

            return new AudioStats(peak, rms, Db(peak), Db(rms), den > 0 ? num / den : 0, bands[0], bands[1], bands[2], bands[3], bands[4],
                (double)nonSilent / n, (double)n / sampleRate, ch);
        }

        public static AudioStats Analyze(float[] mono, int sampleRate) => Analyze(new[] { mono }, sampleRate);

        /// <summary>0 for a mono sound, up to 1 for a stereo pair that has nothing in common.</summary>
        public static double StereoWidth(float[] left, float[] right)
        {
            double ms = 0, ss = 0;
            for (int i = 0; i < left.Length; i++)
            {
                double m = ((double)left[i] + right[i]) * 0.5;
                double s = ((double)left[i] - right[i]) * 0.5;
                ms += m * m;
                ss += s * s;
            }
            return ms + ss > 0 ? ss / (ms + ss) : 0;
        }

        /// <summary>Reverberation time from the backward energy integral of an impulse response: the time to fall <paramref name="dropDb"/> after the first 5 dB, scaled to 60.</summary>
        public static double DecayTime(float[] impulse, int sampleRate, double dropDb = 40)
        {
            int n = impulse.Length;
            var cum = new double[n + 1];
            for (int i = n - 1; i >= 0; i--) cum[i] = cum[i + 1] + (double)impulse[i] * impulse[i];
            if (cum[0] <= 0) return 0;
            double reference = cum[0];
            int t5 = 0, tX = 0;
            for (int i = 0; i < n; i++)
            {
                double db = 10 * Math.Log10(Math.Max(1e-20, cum[i] / reference));
                if (t5 == 0 && db <= -5) t5 = i;
                if (db <= -(5 + dropDb))
                {
                    tX = i;
                    break;
                }
            }
            if (tX == 0) return (double)n / sampleRate;
            return (tX - t5) / (double)sampleRate * (60 / dropDb);
        }
    }
}
