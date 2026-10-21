using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cypher.Core
{
    /// <summary>
    /// Command-line switches for the player's benchmark mode:
    ///   --benchmark [--benchmark-out DIR] [--benchmark-seconds N] [--benchmark-warmup N]
    /// Anything else on the command line (Unity's own -screen-width and friends) is ignored; a malformed
    /// benchmark switch throws, so a typo cannot silently turn into a different measurement.
    /// </summary>
    public sealed class BenchmarkOptions
    {
        public const string BenchmarkFlag = "--benchmark";
        public const string OutFlag = "--benchmark-out";
        public const string SecondsFlag = "--benchmark-seconds";
        public const string WarmupFlag = "--benchmark-warmup";

        public const double DefaultWarmupSeconds = 10;
        public const double DefaultDurationSeconds = 60;

        public bool Enabled { get; }

        /// <summary>Where results are written; null means the caller picks a default.</summary>
        public string OutputDirectory { get; }

        /// <summary>Unrecorded run-in: the first frames hitch while textures upload and the probe renders.</summary>
        public double WarmupSeconds { get; }

        public double DurationSeconds { get; }

        public BenchmarkOptions(bool enabled, string outputDirectory = null, double warmupSeconds = DefaultWarmupSeconds, double durationSeconds = DefaultDurationSeconds)
        {
            if (!(warmupSeconds >= 0) || double.IsInfinity(warmupSeconds)) throw new ArgumentOutOfRangeException(nameof(warmupSeconds), warmupSeconds, "warm-up must be finite and >= 0");
            if (!(durationSeconds > 0) || double.IsInfinity(durationSeconds)) throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "duration must be finite and > 0");
            Enabled = enabled;
            OutputDirectory = outputDirectory;
            WarmupSeconds = warmupSeconds;
            DurationSeconds = durationSeconds;
        }

        public static BenchmarkOptions Disabled { get; } = new BenchmarkOptions(false);

        public static BenchmarkOptions Parse(IReadOnlyList<string> args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            bool enabled = false;
            bool anyValue = false;
            string outDir = null;
            double warmup = DefaultWarmupSeconds;
            double duration = DefaultDurationSeconds;

            for (int i = 0; i < args.Count; i++)
            {
                switch (args[i])
                {
                    case BenchmarkFlag:
                        enabled = true;
                        break;
                    case OutFlag:
                        outDir = Value(args, ref i);
                        anyValue = true;
                        break;
                    case SecondsFlag:
                        duration = Number(args, ref i);
                        anyValue = true;
                        break;
                    case WarmupFlag:
                        warmup = Number(args, ref i);
                        anyValue = true;
                        break;
                }
            }

            if (!enabled && anyValue) throw new ArgumentException($"{OutFlag}, {SecondsFlag} and {WarmupFlag} only make sense with {BenchmarkFlag}");
            return enabled ? new BenchmarkOptions(true, outDir, warmup, duration) : Disabled;
        }

        static string Value(IReadOnlyList<string> args, ref int i)
        {
            string flag = args[i];
            if (i + 1 >= args.Count || args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{flag} needs a value");
            }
            return args[++i];
        }

        static double Number(IReadOnlyList<string> args, ref int i)
        {
            string flag = args[i];
            string text = Value(args, ref i);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                throw new ArgumentException($"{flag} needs a number, got '{text}'");
            }
            return v;
        }
    }
}
