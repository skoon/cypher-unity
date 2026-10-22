#if UNITY_EDITOR
using System.Collections;
using Cypher.Core;
using Cypher.Game;
using Cypher.Perf;
using Cypher.Rendering;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cypher.Tests
{
    /// <summary>The Options screen: every page, live application, saving, resetting, and moving around it.</summary>
    public class UiOptionsTests : UiTestBase
    {
        OptionsScreen Options => Ui.Options;

        GameSettings Saved() => new SettingsStore(Storage).Load().Settings;

        // ------------------------------------------------------------------ opening

        [UnityTest]
        public IEnumerator OptionsOpenOnGraphicsWithTheFirstRowFocused()
        {
            yield return ToOptions();
            Assert.AreEqual(0, Options.ActiveTab);
            Assert.AreEqual("GRAPHICS", Options.ActiveTitle);
            Assert.AreSame(Options.Graphics.Quality, Focused);
            Assert.AreEqual("RESET GRAPHICS", Options.ResetButton.Text);
        }

        [UnityTest]
        public IEnumerator EscClosesOptionsAndFocusReturnsToTheTitleEntry()
        {
            yield return ToOptions();
            yield return Press(Key.Escape);
            Assert.AreEqual(UiScreen.Title, Ui.Top);
            yield return WaitUntil(() => Focused == Ui.Title.Options, 2f, "focus should return to OPTIONS on the title");
        }

        [UnityTest]
        public IEnumerator TheBackButtonClosesOptionsToo()
        {
            yield return ToOptions();
            Options.BackButton.Activate();
            Assert.AreEqual(UiScreen.Title, Ui.Top);
        }

        // ------------------------------------------------------------------ pages

        [UnityTest]
        public IEnumerator PageUpAndPageDownSwitchPagesAndWrap()
        {
            yield return ToOptions();
            yield return Press(Key.PageDown);
            Assert.AreEqual("AUDIO", Options.ActiveTitle);
            yield return Press(Key.PageDown);
            Assert.AreEqual("GAMEPLAY", Options.ActiveTitle);
            yield return Press(Key.PageDown);
            Assert.AreEqual("CONTROLS", Options.ActiveTitle);
            yield return Press(Key.PageDown);
            Assert.AreEqual("GRAPHICS", Options.ActiveTitle, "wraps to the first page");
            yield return Press(Key.PageUp);
            Assert.AreEqual("CONTROLS", Options.ActiveTitle, "and back to the last");
        }

        [UnityTest]
        public IEnumerator SwitchingPageMovesFocusIntoTheNewPageAndRelabelsReset()
        {
            yield return ToOptions();
            yield return Press(Key.PageDown);
            yield return WaitUntil(() => Focused == Options.Audio.Master, 2f, "the Audio page should focus its first slider");
            Assert.AreEqual("RESET AUDIO", Options.ResetButton.Text);
            yield return Press(Key.PageDown);
            yield return Press(Key.PageDown);
            yield return WaitUntil(() => Options.ResetButton.Text == "RESET ALL CONTROLS", 2f, "Controls resets everything");
        }

        [UnityTest]
        public IEnumerator PageKeysDoNothingOutsideTheOptions()
        {
            yield return ToTitle();
            yield return Press(Key.PageDown);
            Assert.AreEqual(0, Options.ActiveTab);
            Assert.AreEqual(UiScreen.Title, Ui.Top);
        }

        // ------------------------------------------------------------------ graphics, live

        [UnityTest]
        public IEnumerator QualityCyclesLiveAndWrapsAndTheHintDescribesIt()
        {
            yield return ToOptions();
            CycleField quality = Options.Graphics.Quality;
            Assert.AreEqual("Medium", quality.ValueText);

            yield return Nav(MoveDirection.Right);
            Assert.AreEqual("High", quality.ValueText);
            Assert.AreEqual(QualityPreset.High, Service.Current.Graphics.Quality);
            Assert.AreEqual(QualityPreset.High, Object.FindFirstObjectByType<QualityDirector>().Preset, "applied to the scene at once");

            yield return Nav(MoveDirection.Left);
            yield return Nav(MoveDirection.Left);
            yield return Nav(MoveDirection.Left);
            Assert.AreEqual("Performance", quality.ValueText);
            yield return Nav(MoveDirection.Left);
            Assert.AreEqual("Ultra", quality.ValueText, "wraps from the first to the last");
            Assert.AreEqual(QualityPreset.Ultra, Service.Current.Graphics.Quality);
        }

        [UnityTest]
        public IEnumerator SubmitOnACycleStepsForwardAndAClickDoesToo()
        {
            yield return ToOptions();
            yield return Submit();
            Assert.AreEqual("High", Options.Graphics.Quality.ValueText);
            UiEvents.Click(Options.Graphics.Quality);
            Assert.AreEqual("Ultra", Options.Graphics.Quality.ValueText);
        }

        [UnityTest]
        public IEnumerator TogglesFlipOnSubmitAndLeftAndRightSetThemOffAndOn()
        {
            yield return ToOptions();
            yield return Nav(MoveDirection.Down);
            ToggleField adaptive = Options.Graphics.Adaptive;
            Assert.AreSame(adaptive, Focused);
            Assert.IsTrue(adaptive.Value);

            yield return Submit();
            Assert.IsFalse(adaptive.Value);
            Assert.IsFalse(Service.Current.Graphics.AdaptiveQuality);
            Assert.IsFalse(Object.FindFirstObjectByType<QualityDirector>().Adaptive);

            yield return Nav(MoveDirection.Right);
            Assert.IsTrue(adaptive.Value, "right means on");
            yield return Nav(MoveDirection.Left);
            Assert.IsFalse(adaptive.Value, "left means off");
            yield return Nav(MoveDirection.Left);
            Assert.IsFalse(adaptive.Value, "and left again changes nothing");
        }

        [UnityTest]
        public IEnumerator ResolutionStepsFromAutoToAFixedHeightAndBack()
        {
            yield return ToOptions();
            yield return Nav(MoveDirection.Down);
            yield return Nav(MoveDirection.Down);
            CycleField resolution = Options.Graphics.Resolution;
            Assert.AreSame(resolution, Focused);
            Assert.AreEqual("Auto (follows quality)", resolution.ValueText);

            yield return Nav(MoveDirection.Right);
            Assert.AreEqual("360p", resolution.ValueText);
            Assert.AreEqual(360, Service.Current.Graphics.InternalHeight);
            Assert.AreEqual(360, Object.FindFirstObjectByType<LowResController>().TargetHeight, "the picture is drawn at the new height");

            yield return Nav(MoveDirection.Left);
            Assert.AreEqual(SettingsRanges.InternalHeightAuto, Service.Current.Graphics.InternalHeight);
            Assert.AreEqual(540, Object.FindFirstObjectByType<LowResController>().TargetHeight, "Auto follows the Medium preset again");
        }

        [UnityTest]
        public IEnumerator ACustomResolutionFromTheFileCyclesOnWithoutBreakingTheList()
        {
            var custom = new GameSettings();
            custom.Graphics.InternalHeight = 800;
            Storage.WriteAll(Json.Write(SettingsSerializer.ToTree(custom, SettingsStore.CurrentVersion)));
            yield return ToOptions();
            yield return Nav(MoveDirection.Down);
            yield return Nav(MoveDirection.Down);
            CycleField resolution = Options.Graphics.Resolution;
            Assert.AreEqual("800p", resolution.ValueText, "the stored value is shown even though it is not a standard one");

            yield return Nav(MoveDirection.Right);
            Assert.AreEqual("900p", resolution.ValueText);
            yield return Nav(MoveDirection.Right);
            Assert.AreEqual("1080p", resolution.ValueText);
            yield return Nav(MoveDirection.Right);
            Assert.AreEqual("Auto (follows quality)", resolution.ValueText);
            Assert.AreEqual(SettingsRanges.InternalHeightAuto, Service.Current.Graphics.InternalHeight, "800 left the list when the player moved off it, and the list did not break");
        }

        [UnityTest]
        public IEnumerator WindowModeVsyncFieldOfViewWeatherAndOverlayApplyLive()
        {
            yield return ToOptions();
            for (int i = 0; i < 3; i++) yield return Nav(MoveDirection.Down);
            Assert.AreSame(Options.Graphics.Window, Focused);
            yield return Nav(MoveDirection.Right);
            Assert.AreEqual(WindowMode.Fullscreen, Service.Current.Graphics.Window);
            Assert.AreEqual("Fullscreen", Options.Graphics.Window.ValueText);

            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Options.Graphics.VSync, Focused);
            yield return Nav(MoveDirection.Left);
            Assert.IsFalse(Service.Current.Graphics.VSync);

            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Options.Graphics.FieldOfView, Focused);
            yield return Nav(MoveDirection.Right);
            Assert.AreEqual(71.0, Service.Current.Graphics.FieldOfView);
            Assert.AreEqual(71f, Camera.main.fieldOfView, 1e-4f, "the camera changed at once");
            Assert.AreEqual("71°", Options.Graphics.FieldOfView.ValueText);

            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Options.Graphics.Weather, Focused);
            yield return Nav(MoveDirection.Left);
            Assert.IsFalse(Object.FindFirstObjectByType<RainController>().Raining, "the rain stopped");

            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Options.Graphics.Overlay, Focused);
            yield return Nav(MoveDirection.Left);
            Assert.IsFalse(Object.FindFirstObjectByType<PerfOverlay>().Visible, "the overlay went away");
        }

        [UnityTest]
        public IEnumerator DownFromTheLastRowReachesTheFooterAndUpComesBack()
        {
            yield return ToOptions();
            for (int i = 0; i < 7; i++) yield return Nav(MoveDirection.Down);
            Assert.AreSame(Options.Graphics.Overlay, Focused);
            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Options.ResetButton, Focused);
            yield return Nav(MoveDirection.Right);
            Assert.AreSame(Options.BackButton, Focused);
            yield return Nav(MoveDirection.Up);
            Assert.AreSame(Options.Graphics.Overlay, Focused, "up from the footer returns to the nearest row");
            yield return Nav(MoveDirection.Down);
            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Options.ResetButton, Focused, "the footer is the bottom edge");
        }

        // ------------------------------------------------------------------ the other pages

        [UnityTest]
        public IEnumerator AudioSlidersStepInFivePercentAndApply()
        {
            yield return ToOptions();
            Options.SelectTab(1);
            Options.FocusFirstOfPage();
            yield return null;
            Assert.AreSame(Options.Audio.Master, Focused);
            Assert.AreEqual("80%", Options.Audio.Master.ValueText);

            yield return Nav(MoveDirection.Right);
            Assert.AreEqual("85%", Options.Audio.Master.ValueText);
            Assert.AreEqual(0.85, Service.Current.Audio.Master, 1e-9);

            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Options.Audio.Music, Focused);
            for (int i = 0; i < 20; i++) yield return Nav(MoveDirection.Left);
            Assert.AreEqual(0.0, Service.Current.Audio.Music, 1e-9, "stops at zero");
            Assert.AreEqual("0%", Options.Audio.Music.ValueText);
        }

        [UnityTest]
        public IEnumerator TheAudioPageSaysThereIsNoSoundYet()
        {
            yield return ToOptions();
            Options.SelectTab(1);
            yield return null;
            bool found = false;
            Options.Audio.Content.Query<UnityEngine.UIElements.Label>().ForEach(l => found |= l.text.Contains("no sound in this build"));
            Assert.IsTrue(found);
        }

        [UnityTest]
        public IEnumerator GameplaySensitivityAndInvertApplyToThePlayer()
        {
            yield return ToOptions();
            Options.SelectTab(2);
            Options.FocusFirstOfPage();
            yield return null;
            Assert.AreSame(Options.Gameplay.Sensitivity, Focused);
            yield return Nav(MoveDirection.Right);
            Assert.AreEqual(1.05, Service.Current.Gameplay.Sensitivity, 1e-9);
            var player = Object.FindFirstObjectByType<Cypher.Player.FirstPersonController>();
            Assert.AreEqual((float)(LookState.DefaultSensitivity * 1.05), player.Sensitivity, 1e-7f);

            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Options.Gameplay.InvertY, Focused);
            yield return Submit();
            Assert.IsTrue(player.InvertY);
        }

        // ------------------------------------------------------------------ saving and resetting

        [UnityTest]
        public IEnumerator ChangesAreSavedAMomentLaterAndBackSavesAtOnce()
        {
            yield return ToOptions();
            yield return Nav(MoveDirection.Right);
            Assert.IsTrue(Options.HasUnsavedChanges);
            Assert.IsFalse(Storage.Exists, "nothing is written while the player is still choosing");

            yield return WaitUntil(() => Storage.Exists, 3f, "the change was never saved");
            Assert.AreEqual(QualityPreset.High, Saved().Graphics.Quality);
            Assert.IsFalse(Options.HasUnsavedChanges);

            yield return Nav(MoveDirection.Right);
            Assert.AreEqual(QualityPreset.High, Saved().Graphics.Quality, "the newest change is still pending");
            Options.BackButton.Activate();
            Assert.AreEqual(QualityPreset.Ultra, Saved().Graphics.Quality, "Back saves at once");
        }

        [UnityTest]
        public IEnumerator NothingIsWrittenIfNothingWasChanged()
        {
            yield return ToOptions();
            yield return Press(Key.Escape);
            yield return new WaitForSecondsRealtime(0.9f);
            Assert.IsFalse(Storage.Exists);
        }

        [UnityTest]
        public IEnumerator ResetGraphicsPutsTheDefaultsBackAndSavesThem()
        {
            yield return ToOptions();
            yield return Nav(MoveDirection.Right);
            for (int i = 0; i < 5; i++) yield return Nav(MoveDirection.Down);
            yield return Nav(MoveDirection.Right);   // field of view 71
            Assert.AreEqual(QualityPreset.High, Service.Current.Graphics.Quality);

            Options.ResetButton.Activate();
            Assert.IsTrue(Service.Current.SameValues(new GameSettings()));
            Assert.AreEqual("Medium", Options.Graphics.Quality.ValueText, "the screen shows the defaults again");
            Assert.AreEqual("70°", Options.Graphics.FieldOfView.ValueText);
            Assert.AreEqual(QualityPreset.Medium, Object.FindFirstObjectByType<QualityDirector>().Preset);
            Assert.IsTrue(Saved().SameValues(new GameSettings()));
        }

        [UnityTest]
        public IEnumerator ResetOnOnePageLeavesTheOthersAlone()
        {
            yield return ToOptions();
            Options.SelectTab(1);
            Options.FocusFirstOfPage();
            yield return null;
            yield return Nav(MoveDirection.Left);   // master volume down
            Options.SelectTab(0);
            Options.FocusFirstOfPage();
            yield return Nav(MoveDirection.Right);   // quality up
            Options.SelectTab(1);
            Options.ResetButton.Activate();
            Assert.AreEqual(0.8, Service.Current.Audio.Master, 1e-9);
            Assert.AreEqual(QualityPreset.High, Service.Current.Graphics.Quality, "graphics were not on the page that was reset");
        }

        [UnityTest]
        public IEnumerator SettingsComeBackOnTheNextLaunch()
        {
            yield return ToOptions();
            yield return Nav(MoveDirection.Right);
            Options.BackButton.Activate();

            yield return ToTitle();   // a fresh load of the scene with the same file
            Assert.AreEqual(QualityPreset.High, Object.FindFirstObjectByType<QualityDirector>().Preset);
            Ui.Title.Options.Activate();
            yield return null;
            Assert.AreEqual("High", Options.Graphics.Quality.ValueText);
        }

        [UnityTest]
        public IEnumerator ReopeningOptionsShowsAPresetTheGovernorLoweredInTheMeantime()
        {
            yield return ToOptions();
            Options.BackButton.Activate();
            yield return null;

            var quality = Object.FindFirstObjectByType<QualityDirector>();
            var ultra = Service.Current;
            ultra.Graphics.Quality = QualityPreset.Ultra;
            Service.Apply(ultra);
            quality.enabled = false;
            Flow.NewGame();
            yield return null;
            for (int i = 0; i < 20 * 8; i++) quality.Sample(1.0 / 20.0);
            Assert.AreNotEqual(QualityPreset.Ultra, quality.Preset);

            Flow.Pause();
            yield return WaitUntil(() => Focused == Ui.Pause.Resume, 2f, "no focus on the pause screen");
            Ui.Pause.Options.Activate();
            Assert.AreEqual(OptionChoices.PresetLabel(quality.Preset), Options.Graphics.Quality.ValueText, "the screen reflects what is in force, not what was chosen");
        }
    }
}
#endif
