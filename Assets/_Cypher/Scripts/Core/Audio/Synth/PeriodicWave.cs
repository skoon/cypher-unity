using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    public enum WaveKind
    {
        Sine,
        Triangle,
        Sawtooth,
        /// <summary>The prototype's band-limited saw: harmonics 1/h, softened by exp(-h/16), to the 39th.</summary>
        SoftSaw,
        /// <summary>Odd harmonics only, 1/h softened the same way: a hollow, clarinet-like tone.</summary>
        Hollow,
        /// <summary>Seven partials with a bell's weights.</summary>
        Bell,
    }

    /// <summary>
    /// A waveform as Web Audio has it: a sum of sines (the Fourier coefficients), scaled so its peak is 1, laid out as a table of one cycle. An oscillator reads a table with the
    /// harmonics that fit under half the sample rate at its frequency, in steps of a third of an octave, so a high note does not alias. Tables are made once per waveform and rate, on first use.
    /// </summary>
    public sealed class PeriodicWave
    {
        public const int TableSize = 4096;
        const int RangesPerOctave = 3;
        const int NativePartials = TableSize / 2;

        static readonly Dictionary<(WaveKind, int), PeriodicWave> Cache = new Dictionary<(WaveKind, int), PeriodicWave>();
        static readonly double[] Sines = MakeSines();

        readonly double[] _imag;
        readonly int _rate;
        readonly double _lowest;
        readonly int _ranges;
        readonly float[][] _tables;
        double _scale = double.NaN;

        PeriodicWave(WaveKind kind, int sampleRate)
        {
            _rate = sampleRate;
            _imag = Coefficients(kind);
            // the lowest range holds every harmonic there is, up to half the table
            _lowest = 0.5 * (sampleRate * 0.5) / NativePartials;
            _ranges = (int)Math.Ceiling(RangesPerOctave * Math.Log(sampleRate * 0.5 / _lowest, 2)) + 1;
            _tables = new float[_ranges][];
        }

        /// <summary>The shared waveform of this kind at this sample rate.</summary>
        public static PeriodicWave Get(WaveKind kind, int sampleRate)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "the sample rate must be positive");
            lock (Cache)
            {
                if (!Cache.TryGetValue((kind, sampleRate), out PeriodicWave wave))
                {
                    wave = new PeriodicWave(kind, sampleRate);
                    Cache[(kind, sampleRate)] = wave;
                }
                return wave;
            }
        }

        static double[] MakeSines()
        {
            var s = new double[TableSize];
            for (int i = 0; i < TableSize; i++) s[i] = Math.Sin(2 * Math.PI * i / TableSize);
            return s;
        }

        /// <summary>The sine coefficients (index = harmonic; index 0 unused) of a waveform: only sines, so every wave starts at zero and rises.</summary>
        public static double[] Coefficients(WaveKind kind)
        {
            switch (kind)
            {
                case WaveKind.Sine:
                    return new[] { 0.0, 1.0 };
                case WaveKind.Triangle:
                {
                    var c = new double[NativePartials];
                    for (int n = 1; n < c.Length; n += 2) c[n] = (((n - 1) / 2) % 2 == 0 ? 1 : -1) * 8 / (Math.PI * Math.PI * n * n);
                    return c;
                }
                case WaveKind.Sawtooth:
                {
                    var c = new double[NativePartials];
                    for (int n = 1; n < c.Length; n++) c[n] = (n % 2 == 1 ? 1 : -1) * 2 / (Math.PI * n);
                    return c;
                }
                case WaveKind.SoftSaw:
                case WaveKind.Hollow:
                {
                    var c = new double[40];
                    for (int h = 1; h < c.Length; h++)
                    {
                        double roll = Math.Exp(-h / 16.0);
                        c[h] = kind == WaveKind.SoftSaw ? 1.0 / h * roll : h % 2 == 1 ? 1.0 / h * roll : 0;
                    }
                    return c;
                }
                case WaveKind.Bell:
                {
                    var c = new double[40];
                    double[] w = { 0, 1, 0.42, 0.28, 0.14, 0.09, 0.05, 0.03 };
                    for (int h = 1; h < c.Length; h++) c[h] = h <= 7 ? w[h] : 0;
                    return c;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown waveform");
            }
        }

        /// <summary>One cycle (and a repeat of its first sample, for interpolating past the end) with the harmonics that fit at this frequency.</summary>
        public float[] TableFor(double frequency) => Table(RangeFor(frequency));

        float[] Table(int range)
        {
            float[] table = _tables[range];
            if (table != null) return table;
            lock (this)
            {
                if (_tables[range] == null)
                {
                    if (double.IsNaN(_scale)) _scale = Scale();
                    _tables[range] = Build(PartialsFor(range), _scale);
                }
                return _tables[range];
            }
        }

        /// <summary>
        /// The two tables for the harmonics that fit at this frequency and how far to the second to blend (0 to 1): a note between two ranges is a cross-fade of their tables, as in Chromium,
        /// so it does not change timbre in steps as it glides.
        /// </summary>
        public void Select(double frequency, out float[] low, out float[] high, out float mix)
        {
            double f = Math.Abs(frequency);
            if (!(f > _lowest) || _imag.Length <= 2)
            {
                low = high = TableFor(0);
                mix = 0;
                return;
            }
            double position = RangesPerOctave * Math.Log(f / _lowest, 2);
            int r = (int)Math.Floor(position);
            if (r >= _ranges - 1)
            {
                low = high = TableFor(_ranges - 1);
                mix = 0;
                return;
            }
            low = Table(r);
            high = Table(r + 1);
            mix = (float)(position - r);
            // the same harmonics in both: nothing to blend
            if (PartialsFor(r) == PartialsFor(r + 1)) high = low;
        }

        int RangeFor(double frequency)
        {
            double f = Math.Abs(frequency);
            if (!(f > _lowest)) return 0;
            int r = (int)Math.Floor(RangesPerOctave * Math.Log(f / _lowest, 2));
            return Math.Min(_ranges - 1, Math.Max(0, r));
        }

        /// <summary>The highest harmonic that stays under half the sample rate at the top of a range.</summary>
        int PartialsFor(int range)
        {
            double top = _lowest * Math.Pow(2, (range + 1) / (double)RangesPerOctave);
            int partials = (int)Math.Floor(_rate * 0.5 / top);
            return Math.Max(1, Math.Min(_imag.Length - 1, partials));
        }

        /// <summary>1 over the peak of the fullest table: all tables share it, so a note keeps its level as it goes up.</summary>
        double Scale()
        {
            double[] sum = Sum(_imag.Length - 1);
            double peak = 0;
            foreach (double v in sum) peak = Math.Max(peak, Math.Abs(v));
            return peak > 0 ? 1 / peak : 1;
        }

        double[] Sum(int partials)
        {
            var sum = new double[TableSize];
            for (int h = 1; h <= partials && h < _imag.Length; h++)
            {
                double c = _imag[h];
                if (c == 0) continue;
                int index = 0;
                for (int j = 0; j < TableSize; j++)
                {
                    sum[j] += c * Sines[index];
                    index = (index + h) & (TableSize - 1);
                }
            }
            return sum;
        }

        float[] Build(int partials, double scale)
        {
            double[] sum = Sum(partials);
            var table = new float[TableSize + 1];
            for (int j = 0; j < TableSize; j++) table[j] = (float)(sum[j] * scale);
            table[TableSize] = table[0];
            return table;
        }
    }
}
