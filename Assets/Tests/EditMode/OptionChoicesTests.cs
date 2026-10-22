using System;
using System.Collections.Generic;
using System.Globalization;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class OptionChoicesTests
    {
        // ------------------------------------------------------------------ sliders

        [Test]
        public void SnapKeepsValuesOnTheStepGridAndInsideTheLimits()
        {
            var s = new SliderSpec(0, 1, 0.05, v => v.ToString());
            Assert.AreEqual(0.0, s.Snap(-3));
            Assert.AreEqual(1.0, s.Snap(7));
            Assert.AreEqual(0.35, s.Snap(0.3501), 1e-9);
            Assert.AreEqual(0.4, s.Snap(0.376), 1e-9);
            Assert.AreEqual(0.95, s.Snap(0.97), 1e-9);
        }

        [Test]
        public void SnapStartsTheGridAtTheMinimumNotAtZero()
        {
            var s = new SliderSpec(0.1, 5, 0.05, v => v.ToString());
            Assert.AreEqual(0.1, s.Snap(0.1), 1e-9);
            Assert.AreEqual(0.15, s.Snap(0.14), 1e-9);
            Assert.AreEqual(5.0, s.Snap(5), 1e-9);
        }

        [Test]
        public void NudgeMovesOneStepAndStopsAtTheLimits()
        {
            SliderSpec s = OptionChoices.Volume;
            Assert.AreEqual(0.85, s.Nudge(0.8, 1), 1e-9);
            Assert.AreEqual(0.75, s.Nudge(0.8, -1), 1e-9);
            Assert.AreEqual(1.0, s.Nudge(1.0, 1), 1e-9);
            Assert.AreEqual(0.0, s.Nudge(0.0, -1), 1e-9);
            Assert.AreEqual(0.85, s.Nudge(0.8, 5), 1e-9, "only the sign of the direction counts");
        }

        [Test]
        public void NudgeSnapsAnOffGridValueFirst()
        {
            Assert.AreEqual(0.45, OptionChoices.Volume.Nudge(0.42, 1), 1e-9, "0.42 snaps to 0.40, then one step up");
        }

        [Test]
        public void FractionsMapBetweenValueAndBarPosition()
        {
            SliderSpec fov = OptionChoices.FieldOfView;
            Assert.AreEqual(0.0, fov.Fraction(60), 1e-9);
            Assert.AreEqual(1.0, fov.Fraction(110), 1e-9);
            Assert.AreEqual(0.2, fov.Fraction(70), 1e-9);
            Assert.AreEqual(0.0, fov.Fraction(10), 1e-9, "clamped");
            Assert.AreEqual(1.0, fov.Fraction(500), 1e-9);
            Assert.AreEqual(60.0, fov.FromFraction(-1), 1e-9);
            Assert.AreEqual(85.0, fov.FromFraction(0.5), 1e-9);
            Assert.AreEqual(110.0, fov.FromFraction(2), 1e-9);
            Assert.AreEqual(70.0, fov.FromFraction(0.2), 1e-9);
        }

        [Test]
        public void TheSlidersUseTheSameLimitsAsTheSettings()
        {
            Assert.AreEqual(SettingsRanges.FovMin, OptionChoices.FieldOfView.Min);
            Assert.AreEqual(SettingsRanges.FovMax, OptionChoices.FieldOfView.Max);
            Assert.AreEqual(SettingsRanges.VolumeMin, OptionChoices.Volume.Min);
            Assert.AreEqual(SettingsRanges.VolumeMax, OptionChoices.Volume.Max);
            Assert.AreEqual(SettingsRanges.SensitivityMin, OptionChoices.Sensitivity.Min);
            Assert.AreEqual(SettingsRanges.SensitivityMax, OptionChoices.Sensitivity.Max);
        }

        [Test]
        public void EverySliderPositionIsAValueTheSettingsAccept()
        {
            var g = new GraphicsOptions();
            for (double f = 0; f <= 1.0001; f += 0.013) g.FieldOfView = OptionChoices.FieldOfView.FromFraction(f);
            var a = new AudioOptions();
            for (double f = 0; f <= 1.0001; f += 0.013) a.Master = OptionChoices.Volume.FromFraction(f);
            var p = new GameplayOptions();
            for (double f = 0; f <= 1.0001; f += 0.013) p.Sensitivity = OptionChoices.Sensitivity.FromFraction(f);
        }

        [Test]
        public void ValuesAreWrittenForPeople()
        {
            Assert.AreEqual("70°", OptionChoices.FieldOfView.Format(70));
            Assert.AreEqual("80%", OptionChoices.Volume.Format(0.8));
            Assert.AreEqual("5%", OptionChoices.Volume.Format(0.05));
            Assert.AreEqual("100%", OptionChoices.Volume.Format(1));
            Assert.AreEqual("1.00×", OptionChoices.Sensitivity.Format(1));
            Assert.AreEqual("2.50×", OptionChoices.Sensitivity.Format(2.5));
        }

        [Test]
        public void FormattingIgnoresTheThreadCulture()
        {
            CultureInfo previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                Assert.AreEqual("0.50×", OptionChoices.Sensitivity.Format(0.5));
                StringAssert.Contains("3,000", OptionChoices.PresetSummary(QualityPreset.Medium));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void BadSliderSpecsFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SliderSpec(1, 1, 1, v => ""));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SliderSpec(0, 1, 0, v => ""));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SliderSpec(0, 1, 2, v => ""));
            Assert.Throws<ArgumentNullException>(() => new SliderSpec(0, 1, 0.1, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => OptionChoices.Volume.Snap(double.NaN));
        }

        // ------------------------------------------------------------------ lists

        [Test]
        public void ThereIsAChoiceForEveryPresetAndWindowMode()
        {
            Assert.AreEqual(Enum.GetValues(typeof(QualityPreset)).Length, OptionChoices.Presets.Count);
            Assert.AreEqual(Enum.GetValues(typeof(WindowMode)).Length, OptionChoices.Windows.Count);
            CollectionAssert.AreEqual(new[] { "Performance", "Low", "Medium", "High", "Ultra" }, new List<string>(Names()));
            Assert.AreEqual("Borderless", OptionChoices.WindowLabel(WindowMode.Borderless));
        }

        static IEnumerable<string> Names()
        {
            foreach (QualityPreset p in OptionChoices.Presets) yield return OptionChoices.PresetLabel(p);
        }

        [Test]
        public void EveryPresetHasADescriptionBuiltFromItsTier()
        {
            Assert.AreEqual("360p · no anti-aliasing · light bloom · up to 1,000 raindrops", OptionChoices.PresetSummary(QualityPreset.Performance));
            Assert.AreEqual("540p · no anti-aliasing · standard bloom · up to 3,000 raindrops", OptionChoices.PresetSummary(QualityPreset.Medium));
            Assert.AreEqual("540p · 4x anti-aliasing · full bloom · up to 4,500 raindrops", OptionChoices.PresetSummary(QualityPreset.Ultra));
        }

        [Test]
        public void ResolutionsReadAsAutoOrPixelHeights()
        {
            Assert.AreEqual("Auto (follows quality)", OptionChoices.ResolutionLabel(SettingsRanges.InternalHeightAuto));
            Assert.AreEqual("540p", OptionChoices.ResolutionLabel(540));
        }

        [Test]
        public void TheResolutionListStartsWithAutoAndIsInOrder()
        {
            IReadOnlyList<int> list = OptionChoices.ResolutionHeights(SettingsRanges.InternalHeightAuto);
            Assert.AreEqual(new[] { 0, 360, 450, 540, 720, 900, 1080 }, new List<int>(list).ToArray());
            foreach (int h in list) Assert.DoesNotThrow(() => new GraphicsOptions().InternalHeight = h, h.ToString());
        }

        [Test]
        public void AResolutionFromTheFileThatIsNotOnTheListIsKeptInPlace()
        {
            IReadOnlyList<int> list = OptionChoices.ResolutionHeights(800);
            Assert.AreEqual(new[] { 0, 360, 450, 540, 720, 800, 900, 1080 }, new List<int>(list).ToArray());
            Assert.AreEqual(new[] { 0, 360, 450, 540, 720, 900, 1080, 2000 }, new List<int>(OptionChoices.ResolutionHeights(2000)).ToArray());
        }

        [Test]
        public void CyclingWrapsAtBothEnds()
        {
            Assert.AreEqual(1, OptionChoices.Cycle(5, 0, 1));
            Assert.AreEqual(0, OptionChoices.Cycle(5, 4, 1));
            Assert.AreEqual(4, OptionChoices.Cycle(5, 0, -1));
            Assert.AreEqual(2, OptionChoices.Cycle(5, 3, -1));
            Assert.AreEqual(0, OptionChoices.Cycle(1, 0, 1));
            Assert.AreEqual(0, OptionChoices.Cycle(1, 0, -1));
            Assert.AreEqual(3, OptionChoices.Cycle(5, 3, 0), "no direction, no move");
        }

        [Test]
        public void CyclingRejectsNonsense()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OptionChoices.Cycle(0, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => OptionChoices.Cycle(3, 3, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => OptionChoices.Cycle(3, -1, 1));
        }
    }
}
