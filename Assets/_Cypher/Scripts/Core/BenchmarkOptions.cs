using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cypher.Core
{
    /// <summary>Which loop the benchmark's pilot walks.</summary>
    public enum BenchmarkRoute
    {
        /// <summary>The loop round the plaza that every earlier measurement used, so results stay comparable.</summary>
        Centre,

        /// <summary>A tour of the whole city including its edges and corners.</summary>
        City,
    }

    /// <summary>
    /// Command-line switches for the player's benchmark mode:
    ///   --benchmark [--benchmark-out DIR] [--benchmark-seconds N] [--benchmark-warmup N] [--benchmark-quality PRESET] [--benchmark-route centre|city]
    /// Anything else on the command line (Unity's own -screen-width and friends) is ignored; a malformed
    /// benchmark switch throws, so a typo cannot silently turn into a different measurement.
    /// </summary>
    public sealed class BenchmarkOptions
    {
        public const string BenchmarkFlag = "--benchmark";
        public const string OutFlag = "--benchmark-out";
        public const string SecondsFlag = "--benchmark-seconds";
        public const string WarmupFlag = "--benchmark-warmup";
        public const string QualityFlag = "--benchmark-quality";
        public const string RouteFlag = "--benchmark-route";

        public const double DefaultWarmupSeconds = 10;
        public const double DefaultDurationSeconds = 60;

        /// <summary>The city tour is long, so it records longer unless told otherwise.</summary>
        public const double DefaultCityDurationSeconds = 240;

        public bool Enabled { get; }

        /// <summary>Where results are written; null means the caller picks a default.</summary>
        public string OutputDirectory { get; }

        /// <summary>Unrecorded run-in: the first frames hitch while textures upload and the probe renders.</summary>
        public double WarmupSeconds { get; }

        public double DurationSeconds { get; }

        /// <summary>Quality preset to measure, pinned with adaptive quality off; null measures whatever the scene starts with, adaptive off.</summary>
        public QualityPreset? Quality { get; }

        public BenchmarkRoute Route { get; }

        public BenchmarkOptions(bool enabled, string outputDirectory = null, double warmupSeconds = DefaultWarmupSeconds, double durationSeconds = DefaultDurationSeconds, QualityPreset? quality = null, BenchmarkRoute route = BenchmarkRoute.Centre)
        {
            if (!(warmupSeconds >= 0) || double.IsInfinity(warmupSeconds)) throw new ArgumentOutOfRangeException(nameof(warmupSeconds), warmupSeconds, "warm-up must be finite and >= 0");
            if (!(durationSeconds > 0) || double.IsInfinity(durationSeconds)) throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "duration must be finite and > 0");
            Enabled = enabled;
            OutputDirectory = outputDirectory;
            WarmupSeconds = warmupSeconds;
            DurationSeconds = durationSeconds;
            Quality = quality;
            Route = route;
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
            bool durationGiven = false;
            QualityPreset? quality = null;
            BenchmarkRoute route = BenchmarkRoute.Centre;

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
                        durationGiven = true;
                        anyValue = true;
                        break;
                    case WarmupFlag:
                        warmup = Number(args, ref i);
                        anyValue = true;
                        break;
                    case QualityFlag:
                        quality = Preset(args, ref i);
                        anyValue = true;
                        break;
                    case RouteFlag:
                        route = RouteName(args, ref i);
                        anyValue = true;
                        break;
                }
            }

            if (!enabled && anyValue) throw new ArgumentException($"{OutFlag}, {SecondsFlag}, {WarmupFlag}, {QualityFlag} and {RouteFlag} only make sense with {BenchmarkFlag}");
            if (!durationGiven && route == BenchmarkRoute.City) duration = DefaultCityDurationSeconds;
            return enabled ? new BenchmarkOptions(true, outDir, warmup, duration, quality, route) : Disabled;
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

        static QualityPreset Preset(IReadOnlyList<string> args, ref int i)
        {
            string flag = args[i];
            string text = Value(args, ref i);
            foreach (string name in Enum.GetNames(typeof(QualityPreset)))
            {
                if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase)) return (QualityPreset)Enum.Parse(typeof(QualityPreset), name);
            }
            throw new ArgumentException($"{flag} needs one of {string.Join(", ", Enum.GetNames(typeof(QualityPreset)))}, got '{text}'");
        }

        static BenchmarkRoute RouteName(IReadOnlyList<string> args, ref int i)
        {
            string flag = args[i];
            string text = Value(args, ref i);
            foreach (string name in Enum.GetNames(typeof(BenchmarkRoute)))
            {
                if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase)) return (BenchmarkRoute)Enum.Parse(typeof(BenchmarkRoute), name);
            }
            throw new ArgumentException($"{flag} needs one of {string.Join(", ", Enum.GetNames(typeof(BenchmarkRoute)))}, got '{text}'");
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
