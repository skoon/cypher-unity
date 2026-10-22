using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cypher.Core
{
    /// <summary>A number the player adjusts in steps between two limits, and how it is written.</summary>
    public sealed class SliderSpec
    {
        public double Min { get; }
        public double Max { get; }
        public double Step { get; }
        readonly Func<double, string> _format;

        public SliderSpec(double min, double max, double step, Func<double, string> format)
        {
            if (!(max > min)) throw new ArgumentOutOfRangeException(nameof(max), max, "max must be above min");
            if (!(step > 0) || step > max - min) throw new ArgumentOutOfRangeException(nameof(step), step, "step must be positive and fit in the range");
            Min = min;
            Max = max;
            Step = step;
            _format = format ?? throw new ArgumentNullException(nameof(format));
        }

        public string Format(double value) => _format(value);

        /// <summary>The nearest value on the grid of steps from the minimum, kept inside the limits.</summary>
        public double Snap(double value)
        {
            if (double.IsNaN(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "value must be a number");
            double clamped = Math.Max(Min, Math.Min(Max, value));
            double snapped = Min + Math.Round((clamped - Min) / Step) * Step;
            return Math.Round(Math.Max(Min, Math.Min(Max, snapped)), 6);
        }

        /// <summary>One step up (+1) or down (-1) from <paramref name="value"/>, stopping at the limits.</summary>
        public double Nudge(double value, int direction) => Snap(Snap(value) + Math.Sign(direction) * Step);

        /// <summary>Where <paramref name="value"/> sits between the limits, 0 to 1, for drawing the bar.</summary>
        public double Fraction(double value) => Math.Max(0, Math.Min(1, (value - Min) / (Max - Min)));

        /// <summary>The value at a position along the bar, 0 to 1, snapped to the steps.</summary>
        public double FromFraction(double fraction) => Snap(Min + Math.Max(0, Math.Min(1, fraction)) * (Max - Min));
    }

    /// <summary>The lists and slider limits behind the Options screen, and the words used for them.</summary>
    public static class OptionChoices
    {
        public static readonly SliderSpec FieldOfView = new SliderSpec(SettingsRanges.FovMin, SettingsRanges.FovMax, 1,
            v => v.ToString("0", CultureInfo.InvariantCulture) + "°");

        public static readonly SliderSpec Volume = new SliderSpec(SettingsRanges.VolumeMin, SettingsRanges.VolumeMax, 0.05,
            v => Math.Round(v * 100).ToString("0", CultureInfo.InvariantCulture) + "%");

        public static readonly SliderSpec Sensitivity = new SliderSpec(SettingsRanges.SensitivityMin, SettingsRanges.SensitivityMax, 0.05,
            v => v.ToString("0.00", CultureInfo.InvariantCulture) + "×");

        static readonly int[] StandardHeights = { SettingsRanges.InternalHeightAuto, 360, 450, 540, 720, 900, 1080 };

        public static IReadOnlyList<QualityPreset> Presets { get; } = (QualityPreset[])Enum.GetValues(typeof(QualityPreset));

        public static IReadOnlyList<WindowMode> Windows { get; } = (WindowMode[])Enum.GetValues(typeof(WindowMode));

        public static string PresetLabel(QualityPreset preset) => preset.ToString();

        /// <summary>One line on what a preset costs and buys, from the tier table.</summary>
        public static string PresetSummary(QualityPreset preset)
        {
            QualityTier t = QualityTiers.For(preset);
            string aa = t.MsaaSamples == 1 ? "no anti-aliasing" : t.MsaaSamples + "x anti-aliasing";
            string bloom = t.Bloom == BloomQuality.Fast ? "light bloom" : t.Bloom == BloomQuality.Standard ? "standard bloom" : "full bloom";
            return $"{t.InternalHeight}p · {aa} · {bloom} · up to {t.RainParticleCap.ToString("N0", CultureInfo.InvariantCulture)} raindrops";
        }

        public static string WindowLabel(WindowMode mode) => mode.ToString();

        public static string ResolutionLabel(int height) => height == SettingsRanges.InternalHeightAuto ? "Auto (follows quality)" : height.ToString(CultureInfo.InvariantCulture) + "p";

        /// <summary>The heights to cycle through, in order: Auto, the standard ones, and <paramref name="current"/> too if the settings hold some other value.</summary>
        public static IReadOnlyList<int> ResolutionHeights(int current)
        {
            var list = new List<int>(StandardHeights);
            if (!list.Contains(current))
            {
                list.Add(current);
                list.Sort();   // Auto is 0, so it stays first
            }
            return list;
        }

        /// <summary>The item after (+1) or before (-1) the one at <paramref name="index"/>, wrapping at the ends.</summary>
        public static int Cycle(int count, int index, int direction)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), count, "there must be something to cycle through");
            if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index), index, "index is outside the list");
            return ((index + Math.Sign(direction)) % count + count) % count;
        }
    }
}
