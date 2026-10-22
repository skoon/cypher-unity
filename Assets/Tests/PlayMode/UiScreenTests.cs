#if UNITY_EDITOR
using System.Collections;
using Cypher.Core;
using Cypher.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>Loading, title, pause, the confirmations and the notices, in the real scene.</summary>
    public class UiScreenTests : UiTestBase
    {
        // ------------------------------------------------------------------ loading and title

        [UnityTest]
        public IEnumerator TheLoadingScreenShowsFirstThenFadesAwayToTheTitle()
        {
            yield return SliceTestScene.Load();
            Assert.IsTrue(Ui.Loading.IsShown, "loading is what the player sees first");
            Assert.IsFalse(Ui.Title.IsShown);
            yield return SliceTestScene.WaitFor(FlowState.Title);
            Assert.AreEqual(UiScreen.Title, Ui.Top);
            yield return WaitUntil(() => !Ui.Loading.IsShown, 3f, "the loading screen never went away");
            Assert.IsTrue(Ui.Title.IsShown);
        }

        [UnityTest]
        public IEnumerator TheLoadingBarFollowsTheFlowsProgress()
        {
            yield return SliceTestScene.Load();
            yield return null;
            Assert.That(Ui.Loading.Progress, Is.InRange(0.0, 1.0));
            StringAssert.Contains("%", Ui.Loading.Status);
            yield return SliceTestScene.WaitFor(FlowState.Title);
        }

        [UnityTest]
        public IEnumerator TheTitleHasItsMenuWithNewGameFocusedAndContinueGreyedOut()
        {
            yield return ToTitle();
            Assert.AreSame(Ui.Title.NewGame, Focused);
            Assert.IsFalse(Ui.Title.Continue.enabledSelf);
            Assert.AreEqual("NEW GAME", Ui.Title.NewGame.Text);
        }

        [UnityTest]
        public IEnumerator ArrowKeysMoveFocusDownTheMenuSkippingTheGreyedOutEntry()
        {
            yield return ToTitle();
            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Ui.Title.Options, Focused, "Continue is greyed out, so it is skipped");
            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Ui.Title.Quit, Focused);
            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Ui.Title.Quit, Focused, "the list does not wrap");
            yield return Nav(MoveDirection.Up);
            Assert.AreSame(Ui.Title.Options, Focused);
        }

        [UnityTest]
        public IEnumerator ClickingAnEntryAlsoPressesIt()
        {
            yield return ToTitle();
            UiEvents.Click(Ui.Title.Options);
            Assert.AreEqual(UiScreen.Options, Ui.Top);
        }

        [UnityTest]
        public IEnumerator ClickingAGreyedOutEntryDoesNothing()
        {
            yield return ToTitle();
            UiEvents.Click(Ui.Title.Continue);
            yield return null;
            Assert.AreEqual(UiScreen.Title, Ui.Top);
            Assert.AreEqual(FlowState.Title, Flow.State);
        }

        [UnityTest]
        public IEnumerator SubmitOnNewGameStartsTheGameAndTheMenusGetOutOfTheWay()
        {
            yield return ToTitle();
            yield return Submit();
            yield return WaitUntil(() => Flow.State == FlowState.Playing, 3f, "Submit did not start a new game");
            yield return null;
            Assert.AreEqual(UiScreen.None, Ui.Top);
            Assert.IsFalse(Ui.Title.IsShown);
            Assert.IsFalse(Ui.Pause.IsShown);
        }

        [UnityTest]
        public IEnumerator EscDoesNothingOnTheTitle()
        {
            yield return ToTitle();
            yield return Press(Key.Escape);
            Assert.AreEqual(FlowState.Title, Flow.State);
            Assert.AreEqual(UiScreen.Title, Ui.Top);
            Assert.AreSame(Ui.Title.NewGame, Focused, "and focus stayed where it was");
        }

        // ------------------------------------------------------------------ pause

        [UnityTest]
        public IEnumerator EscPausesWhilePlayingAndResumesFromThePauseScreen()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return WaitUntil(() => !Ui.Loading.IsShown, 3f, "loading never left");
            yield return Press(Key.Escape);
            Assert.AreEqual(FlowState.Paused, Flow.State);
            Assert.AreEqual(UiScreen.Pause, Ui.Top);
            yield return WaitUntil(() => Focused == Ui.Pause.Resume, 2f, "RESUME should have focus on the pause screen");

            yield return Press(Key.Escape);
            Assert.AreEqual(FlowState.Playing, Flow.State);
            Assert.AreEqual(UiScreen.None, Ui.Top);
        }

        [UnityTest]
        public IEnumerator ThePauseMenuWorksWhileTimeIsStopped()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Press(Key.Escape);
            Assert.AreEqual(0f, Time.timeScale);
            yield return WaitUntil(() => Focused == Ui.Pause.Resume, 2f, "no focus on the pause screen");
            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Ui.Pause.Options, Focused, "navigation still works at time scale 0");
            yield return Submit();
            Assert.AreEqual(UiScreen.Options, Ui.Top, "and so does a press");
        }

        [UnityTest]
        public IEnumerator ResumeOnThePauseScreenContinuesTheGame()
        {
            yield return SliceTestScene.LoadAndPlay();
            Flow.Pause();
            yield return WaitUntil(() => Focused == Ui.Pause.Resume, 2f, "no focus on the pause screen");
            yield return Submit();
            Assert.AreEqual(FlowState.Playing, Flow.State);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator TheGamepadsBackButtonClosesMenusButNeverPausesTheGame()
        {
            yield return SliceTestScene.LoadAndPlay();
            Ui.OnBack(fromBButton: true);
            yield return null;
            Assert.AreEqual(FlowState.Playing, Flow.State, "B must not pause: it can be bound to an action");

            Ui.OnBack(fromBButton: false);   // Start or Esc
            yield return null;
            Assert.AreEqual(FlowState.Paused, Flow.State);

            Ui.OnBack(fromBButton: true);
            yield return null;
            Assert.AreEqual(FlowState.Playing, Flow.State, "in a menu B goes back, and from the pause screen that means resume");
        }

        [UnityTest]
        public IEnumerator EscFromOptionsOpenedInThePauseMenuNeedsOnePressPerLevel()
        {
            yield return SliceTestScene.LoadAndPlay();
            Flow.Pause();
            yield return WaitUntil(() => Focused == Ui.Pause.Resume, 2f, "no focus on the pause screen");
            yield return Nav(MoveDirection.Down);
            yield return Submit();
            Assert.AreEqual(UiScreen.Options, Ui.Top);

            yield return Press(Key.Escape);
            Assert.AreEqual(UiScreen.Pause, Ui.Top, "first Esc closes the options");
            Assert.AreEqual(FlowState.Paused, Flow.State);
            yield return WaitUntil(() => Focused == Ui.Pause.Options, 2f, "focus should return to the OPTIONS entry that opened them");

            yield return Press(Key.Escape);
            Assert.AreEqual(FlowState.Playing, Flow.State, "second Esc resumes");
        }

        // ------------------------------------------------------------------ confirmations

        [UnityTest]
        public IEnumerator QuitOnTheTitleAsksFirstAndNoLeavesEverythingAsItWas()
        {
            yield return ToTitle();
            int quits = 0;
            Flow.QuitAction = () => quits++;
            yield return Nav(MoveDirection.Down);
            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Ui.Title.Quit, Focused);
            yield return Submit();

            Assert.AreEqual(UiScreen.ConfirmQuit, Ui.Top);
            Assert.AreEqual("QUIT CYPHER?", Ui.Dialog.Heading);
            Assert.AreEqual(0, quits, "asking is not quitting");
            yield return WaitUntil(() => Focused == Ui.Dialog.Buttons[1], 2f, "NO is the safe default");
            Assert.AreEqual("NO", Ui.Dialog.Buttons[1].Text);

            yield return Submit();   // NO
            Assert.AreEqual(UiScreen.Title, Ui.Top);
            Assert.AreEqual(0, quits);
            yield return WaitUntil(() => Focused == Ui.Title.Quit, 2f, "focus returns to the entry that asked");
        }

        [UnityTest]
        public IEnumerator YesToQuitQuits()
        {
            yield return ToTitle();
            int quits = 0;
            Flow.QuitAction = () => quits++;
            Ui.Title.Quit.Activate();
            yield return WaitUntil(() => Focused == Ui.Dialog.Buttons[1], 2f, "the dialog never took focus");
            yield return Nav(MoveDirection.Left);
            Assert.AreSame(Ui.Dialog.Buttons[0], Focused);
            yield return Submit();
            Assert.AreEqual(1, quits);
        }

        [UnityTest]
        public IEnumerator EscClosesAConfirmationWithoutConfirming()
        {
            yield return ToTitle();
            int quits = 0;
            Flow.QuitAction = () => quits++;
            Ui.Title.Quit.Activate();
            Assert.AreEqual(UiScreen.ConfirmQuit, Ui.Top);
            yield return Press(Key.Escape);
            Assert.AreEqual(UiScreen.Title, Ui.Top);
            Assert.AreEqual(0, quits);
        }

        [UnityTest]
        public IEnumerator ReturnToTitleAsksFirstThenGoesBackToTheTitle()
        {
            yield return SliceTestScene.LoadAndPlay();
            Flow.Pause();
            yield return WaitUntil(() => Focused == Ui.Pause.Resume, 2f, "no focus on the pause screen");
            Ui.Pause.ReturnToTitle.Focus();   // the player moves onto the entry before pressing it
            yield return null;
            Ui.Pause.ReturnToTitle.Activate();
            Assert.AreEqual(UiScreen.ConfirmTitle, Ui.Top);
            Assert.AreEqual(FlowState.Paused, Flow.State, "still paused while it asks");

            yield return WaitUntil(() => Focused == Ui.Dialog.Buttons[1], 2f, "the dialog never took focus");
            yield return Submit();   // NO
            Assert.AreEqual(UiScreen.Pause, Ui.Top);
            yield return WaitUntil(() => Focused == Ui.Pause.ReturnToTitle, 2f, "focus returns to the entry that asked");

            Ui.Pause.ReturnToTitle.Activate();
            yield return WaitUntil(() => Focused == Ui.Dialog.Buttons[1], 2f, "the dialog never took focus");
            yield return Nav(MoveDirection.Left);
            yield return Submit();   // YES
            Assert.AreEqual(FlowState.Title, Flow.State);
            Assert.AreEqual(UiScreen.Title, Ui.Top);
            Assert.IsEmpty(Ui.Stack.Overlays, "nothing is left open");
            yield return WaitUntil(() => Ui.Title.IsShown && Focused == Ui.Title.NewGame, 2f, "the title did not come back with focus");
        }

        [UnityTest]
        public IEnumerator QuitOnThePauseScreenAsksToo()
        {
            yield return SliceTestScene.LoadAndPlay();
            int quits = 0;
            Flow.QuitAction = () => quits++;
            Flow.Pause();
            Ui.Pause.Quit.Activate();
            Assert.AreEqual(UiScreen.ConfirmQuit, Ui.Top);
            Assert.AreEqual(0, quits);
        }

        [UnityTest]
        public IEnumerator OnlyOneScreenShowsAtATime()
        {
            yield return ToTitle();
            Assert.IsTrue(Ui.Title.IsShown);
            Assert.IsFalse(Ui.Pause.IsShown);
            Assert.IsFalse(Ui.Options.IsShown);
            Assert.IsFalse(Ui.Dialog.IsShown);

            Ui.Title.Options.Activate();
            Assert.IsFalse(Ui.Title.IsShown, "the options replace the title menu");
            Assert.IsTrue(Ui.Options.IsShown);
            Assert.IsFalse(Ui.Dialog.IsShown);
        }

        // ------------------------------------------------------------------ notices

        [UnityTest]
        public IEnumerator ANoticeFromTheSettingsAppearsInTheCornerAtStartup()
        {
            Storage.WriteAll("{ this is not json");
            yield return ToTitle();
            yield return WaitUntil(() => Ui.Toasts.Shown.Count > 0, 2f, "no toast appeared");
            StringAssert.Contains("default settings are in use", Ui.Toasts.Shown[0]);
        }

        [UnityTest]
        public IEnumerator ShowNoticeAddsAToastAndTheSameMessageDoesNotStack()
        {
            yield return ToTitle();
            Ui.ShowNotice("hello there");
            Ui.ShowNotice("hello there");
            Ui.ShowNotice("another");
            yield return WaitUntil(() => Ui.Toasts.Shown.Count == 2, 2f, "expected two distinct toasts");
            CollectionAssert.AreEqual(new[] { "hello there", "another" }, new System.Collections.Generic.List<string>(Ui.Toasts.Shown));
        }

        [UnityTest]
        public IEnumerator ALoweredPresetIsAnnouncedWhileMenusAreUp()
        {
            var ultra = new GameSettings();
            ultra.Graphics.Quality = QualityPreset.Ultra;
            Storage.WriteAll(Json.Write(SettingsSerializer.ToTree(ultra, SettingsStore.CurrentVersion)));
            yield return SliceTestScene.LoadAndPlay();
            var quality = Object.FindFirstObjectByType<Cypher.Rendering.QualityDirector>();
            quality.enabled = false;
            for (int i = 0; i < 20 * 8; i++) quality.Sample(1.0 / 20.0);
            yield return WaitUntil(() => Ui.Toasts.Shown.Count > 0, 2f, "no toast for the lowered preset");
            StringAssert.Contains("Quality lowered from Ultra", Ui.Toasts.Shown[0]);
        }
    }
}
#endif
