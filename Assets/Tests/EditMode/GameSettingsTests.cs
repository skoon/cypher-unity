using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class GameSettingsTests
    {
        [Test]
        public void DefaultsMatchTheGameAsItShipsToday()
        {
            var s = new GameSettings();
            Assert.AreEqual(QualityPreset.Medium, s.Graphics.Quality);
            Assert.IsTrue(s.Graphics.AdaptiveQuality);
            Assert.AreEqual(SettingsRanges.InternalHeightAuto, s.Graphics.InternalHeight, "Auto: the preset picks the height");
            Assert.AreEqual(WindowMode.Borderless, s.Graphics.Window);
            Assert.IsTrue(s.Graphics.VSync);
            Assert.AreEqual(70.0, s.Graphics.FieldOfView, "the camera's field of view in the scene");
            Assert.IsTrue(s.Graphics.Weather);
            Assert.IsTrue(s.Graphics.ShowPerfOverlay);
            Assert.AreEqual(0.8, s.Audio.Master);
            Assert.AreEqual(1.0, s.Gameplay.Sensitivity);
            Assert.IsFalse(s.Gameplay.InvertY);
            Assert.AreEqual(0, s.Controls.Overrides.Count);
        }

        [Test]
        public void TheDefaultResolutionIsAutoWhichMediumResolvesToTheMilestoneOneHeight()
        {
            Assert.AreEqual(SettingsRanges.InternalHeightAuto, SettingsRanges.InternalHeightDefault);
            Assert.AreEqual(0, SettingsRanges.InternalHeightAuto);
            Assert.AreEqual(540, QualityTiers.EffectiveInternalHeight(SettingsRanges.QualityDefault, SettingsRanges.InternalHeightDefault));
        }

        [Test]
        public void LookSensitivityScalesThePrototypeDefault()
        {
            var g = new GameplayOptions();
            Assert.AreEqual(LookState.DefaultSensitivity, g.LookSensitivity, 1e-15);
            g.Sensitivity = 2.5;
            Assert.AreEqual(LookState.DefaultSensitivity * 2.5, g.LookSensitivity, 1e-15);
        }

        [Test]
        public void SettersAcceptTheEdgesOfTheirRanges()
        {
            var s = new GameSettings();
            s.Graphics.FieldOfView = SettingsRanges.FovMin;
            s.Graphics.FieldOfView = SettingsRanges.FovMax;
            s.Graphics.InternalHeight = SettingsRanges.InternalHeightMin;
            s.Graphics.InternalHeight = SettingsRanges.InternalHeightMax;
            s.Graphics.InternalHeight = SettingsRanges.InternalHeightAuto;
            s.Audio.Master = 0;
            s.Audio.Voice = 1;
            s.Gameplay.Sensitivity = SettingsRanges.SensitivityMin;
            s.Gameplay.Sensitivity = SettingsRanges.SensitivityMax;
            Assert.AreEqual(SettingsRanges.SensitivityMax, s.Gameplay.Sensitivity);
        }

        [Test]
        public void SettersRejectOutOfRangeAndNonFiniteValuesAndKeepTheOldOne()
        {
            var s = new GameSettings();
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Graphics.FieldOfView = 59.9);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Graphics.FieldOfView = 110.1);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Graphics.FieldOfView = double.NaN);
            Assert.AreEqual(70.0, s.Graphics.FieldOfView);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Graphics.InternalHeight = 239);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Graphics.InternalHeight = 2161);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Graphics.InternalHeight = -1);
            Assert.AreEqual(SettingsRanges.InternalHeightAuto, s.Graphics.InternalHeight);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Graphics.Quality = (QualityPreset)99);
            Assert.AreEqual(QualityPreset.Medium, s.Graphics.Quality);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Audio.Master = -0.01);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Audio.Music = 1.01);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Audio.Sfx = double.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Audio.Ambience = double.PositiveInfinity);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Audio.Ui = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Audio.Voice = 2);
            Assert.AreEqual(0.8, s.Audio.Master);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Gameplay.Sensitivity = 0.05);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Gameplay.Sensitivity = 5.1);
        }

        [Test]
        public void CloneIsIndependentOfTheOriginal()
        {
            var a = new GameSettings();
            a.Graphics.Quality = QualityPreset.Ultra;
            a.Audio.Music = 0.2;
            a.Gameplay.InvertY = true;
            a.Controls.SetOverride("jump:0", "<Keyboard>/z");

            GameSettings b = a.Clone();
            Assert.IsTrue(a.SameValues(b));

            b.Graphics.Quality = QualityPreset.Low;
            b.Audio.Music = 0.9;
            b.Gameplay.InvertY = false;
            b.Controls.SetOverride("jump:0", "<Keyboard>/x");
            b.Controls.SetOverride("crouch:0", "<Keyboard>/c");

            Assert.AreEqual(QualityPreset.Ultra, a.Graphics.Quality);
            Assert.AreEqual(0.2, a.Audio.Music);
            Assert.IsTrue(a.Gameplay.InvertY);
            Assert.AreEqual("<Keyboard>/z", a.Controls.Overrides["jump:0"]);
            Assert.AreEqual(1, a.Controls.Overrides.Count);
            Assert.IsFalse(a.SameValues(b));
        }

        [Test]
        public void SameValuesNoticesEveryField()
        {
            var baseline = new GameSettings();
            var mutations = new Action<GameSettings>[]
            {
                s => s.Graphics.Quality = QualityPreset.High,
                s => s.Graphics.AdaptiveQuality = false,
                s => s.Graphics.InternalHeight = 720,
                s => s.Graphics.Window = WindowMode.Fullscreen,
                s => s.Graphics.VSync = false,
                s => s.Graphics.FieldOfView = 90,
                s => s.Graphics.Weather = false,
                s => s.Graphics.ShowPerfOverlay = false,
                s => s.Audio.Master = 0.1,
                s => s.Audio.Music = 0.1,
                s => s.Audio.Sfx = 0.1,
                s => s.Audio.Ambience = 0.1,
                s => s.Audio.Ui = 0.1,
                s => s.Audio.Voice = 0.1,
                s => s.Gameplay.Sensitivity = 2,
                s => s.Gameplay.InvertY = true,
                s => s.Controls.SetOverride("jump:0", "<Keyboard>/z"),
            };
            for (int i = 0; i < mutations.Length; i++)
            {
                GameSettings changed = baseline.Clone();
                mutations[i](changed);
                Assert.IsFalse(baseline.SameValues(changed), "mutation " + i + " went unnoticed");
            }
            Assert.IsFalse(baseline.SameValues(null));
        }

        [Test]
        public void ResetToDefaultsRestoresEverythingOrOneSection()
        {
            var s = new GameSettings();
            s.Graphics.Quality = QualityPreset.Ultra;
            s.Graphics.FieldOfView = 100;
            s.Audio.Master = 0.1;
            s.Gameplay.Sensitivity = 3;
            s.Controls.SetOverride("jump:0", "<Keyboard>/z");

            s.Audio.ResetToDefaults();
            Assert.AreEqual(0.8, s.Audio.Master);
            Assert.AreEqual(QualityPreset.Ultra, s.Graphics.Quality, "other sections untouched");

            s.ResetToDefaults();
            Assert.IsTrue(s.SameValues(new GameSettings()));
        }

        [Test]
        public void OverridesAreKeptInKeyOrderAndValidated()
        {
            var c = new ControlsOptions();
            c.SetOverride("sprint:0", "<Keyboard>/leftShift");
            c.SetOverride("jump:0", "<Keyboard>/space");
            c.SetOverride("jump:0", "<Keyboard>/z");   // replaces
            CollectionAssert.AreEqual(new[] { "jump:0", "sprint:0" }, new System.Collections.Generic.List<string>(c.Overrides.Keys));
            Assert.AreEqual("<Keyboard>/z", c.Overrides["jump:0"]);

            Assert.IsTrue(c.RemoveOverride("jump:0"));
            Assert.IsFalse(c.RemoveOverride("jump:0"));
            Assert.Throws<ArgumentException>(() => c.SetOverride("", "<Keyboard>/z"));
            Assert.Throws<ArgumentException>(() => c.SetOverride("  ", "<Keyboard>/z"));
            Assert.Throws<ArgumentException>(() => c.SetOverride("jump:0", ""));
            Assert.Throws<ArgumentException>(() => c.SetOverride("jump:0", null));
            Assert.AreEqual(1, c.Overrides.Count);
        }
    }
}
