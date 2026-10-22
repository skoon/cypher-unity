using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class ScreenStackTests
    {
        static ScreenStack In(FlowState state)
        {
            var s = new ScreenStack();
            s.SetFlowState(state);
            return s;
        }

        [TestCase(FlowState.Boot, UiScreen.Loading)]
        [TestCase(FlowState.Loading, UiScreen.Loading)]
        [TestCase(FlowState.Title, UiScreen.Title)]
        [TestCase(FlowState.Playing, UiScreen.None)]
        [TestCase(FlowState.Paused, UiScreen.Pause)]
        public void TheBaseScreenFollowsTheFlow(FlowState state, UiScreen expected)
        {
            ScreenStack s = In(state);
            Assert.AreEqual(expected, s.Base);
            Assert.AreEqual(expected, s.Top);
            Assert.IsEmpty(s.Overlays);
        }

        [Test]
        public void ANewStackStartsInBootShowingTheLoadingScreen()
        {
            var s = new ScreenStack();
            Assert.AreEqual(FlowState.Boot, s.FlowState);
            Assert.AreEqual(UiScreen.Loading, s.Top);
        }

        // ------------------------------------------------------------------ what may open where

        [Test]
        public void OptionsOpenFromTheTitleAndFromThePauseScreenOnly()
        {
            Assert.IsTrue(In(FlowState.Title).CanPush(UiScreen.Options));
            Assert.IsTrue(In(FlowState.Paused).CanPush(UiScreen.Options));
            Assert.IsFalse(In(FlowState.Playing).CanPush(UiScreen.Options), "nothing is showing to open them from");
            Assert.IsFalse(In(FlowState.Loading).CanPush(UiScreen.Options));
            Assert.IsFalse(In(FlowState.Boot).CanPush(UiScreen.Options));
        }

        [Test]
        public void QuitConfirmationOpensFromTheTitleAndThePauseScreen()
        {
            Assert.IsTrue(In(FlowState.Title).CanPush(UiScreen.ConfirmQuit));
            Assert.IsTrue(In(FlowState.Paused).CanPush(UiScreen.ConfirmQuit));
            Assert.IsFalse(In(FlowState.Playing).CanPush(UiScreen.ConfirmQuit));
        }

        [Test]
        public void ReturnToTitleConfirmationOpensOnlyFromThePauseScreen()
        {
            Assert.IsTrue(In(FlowState.Paused).CanPush(UiScreen.ConfirmTitle));
            Assert.IsFalse(In(FlowState.Title).CanPush(UiScreen.ConfirmTitle), "already there");
        }

        [Test]
        public void TheKeyPromptOpensOnlyOverOptionsAndTheConflictQuestionOnlyOverTheKeyPrompt()
        {
            ScreenStack s = In(FlowState.Paused);
            Assert.IsFalse(s.CanPush(UiScreen.Rebind), "not before the options are open");
            Assert.IsFalse(s.CanPush(UiScreen.Conflict));
            Assert.IsTrue(s.TryPush(UiScreen.Options));
            Assert.IsFalse(s.CanPush(UiScreen.Conflict));
            Assert.IsTrue(s.TryPush(UiScreen.Rebind));
            Assert.IsTrue(s.CanPush(UiScreen.Conflict));
        }

        [Test]
        public void DialogsCannotOpenOverEachOther()
        {
            ScreenStack s = In(FlowState.Paused);
            s.TryPush(UiScreen.ConfirmQuit);
            Assert.IsFalse(s.CanPush(UiScreen.Options));
            Assert.IsFalse(s.CanPush(UiScreen.ConfirmTitle));
            Assert.IsFalse(s.CanPush(UiScreen.ConfirmQuit));
        }

        [Test]
        public void NothingOpensOverTheScreenAlreadyOnTop()
        {
            ScreenStack s = In(FlowState.Title);
            Assert.IsTrue(s.TryPush(UiScreen.Options));
            Assert.IsFalse(s.TryPush(UiScreen.Options), "a second click on Options does nothing");
            Assert.AreEqual(1, s.Overlays.Count);
        }

        [Test]
        public void ScreensThatBelongToTheFlowCannotBePushed()
        {
            ScreenStack s = In(FlowState.Paused);
            foreach (UiScreen screen in new[] { UiScreen.None, UiScreen.Loading, UiScreen.Title, UiScreen.Pause })
            {
                Assert.IsFalse(s.CanPush(screen), screen.ToString());
                Assert.IsFalse(s.TryPush(screen));
            }
            Assert.IsEmpty(s.Overlays);
        }

        // ------------------------------------------------------------------ stacking

        [Test]
        public void OverlaysStackAndPopInOrder()
        {
            ScreenStack s = In(FlowState.Paused);
            s.TryPush(UiScreen.Options);
            s.TryPush(UiScreen.Rebind);
            CollectionAssert.AreEqual(new[] { UiScreen.Options, UiScreen.Rebind }, s.Overlays);
            Assert.AreEqual(UiScreen.Rebind, s.Top);
            Assert.AreEqual(UiScreen.Pause, s.Base);

            Assert.IsTrue(s.Pop());
            Assert.AreEqual(UiScreen.Options, s.Top);
            Assert.IsTrue(s.Pop());
            Assert.AreEqual(UiScreen.Pause, s.Top);
            Assert.IsFalse(s.Pop(), "nothing left to close");
        }

        [Test]
        public void TheKeyPromptCanBeReplacedByTheConflictQuestionAndBack()
        {
            ScreenStack s = In(FlowState.Title);
            s.TryPush(UiScreen.Options);
            s.TryPush(UiScreen.Rebind);
            Assert.IsTrue(s.TryReplaceTop(UiScreen.Conflict));
            CollectionAssert.AreEqual(new[] { UiScreen.Options, UiScreen.Conflict }, s.Overlays);
        }

        [Test]
        public void ReplacingWithAScreenThatIsNotAllowedThereChangesNothing()
        {
            ScreenStack s = In(FlowState.Title);
            s.TryPush(UiScreen.Options);
            s.TryPush(UiScreen.Rebind);
            Assert.IsFalse(s.TryReplaceTop(UiScreen.ConfirmQuit));
            CollectionAssert.AreEqual(new[] { UiScreen.Options, UiScreen.Rebind }, s.Overlays);
            Assert.IsFalse(In(FlowState.Title).TryReplaceTop(UiScreen.Options), "nothing to replace");
        }

        [Test]
        public void AFlowChangeClosesEverythingThatWasOpen()
        {
            ScreenStack s = In(FlowState.Paused);
            s.TryPush(UiScreen.Options);
            s.TryPush(UiScreen.Rebind);
            s.SetFlowState(FlowState.Playing);
            Assert.IsEmpty(s.Overlays);
            Assert.AreEqual(UiScreen.None, s.Top);
        }

        // ------------------------------------------------------------------ Esc

        [Test]
        public void BackClosesTheTopOverlayFirst()
        {
            ScreenStack s = In(FlowState.Paused);
            s.TryPush(UiScreen.Options);
            s.TryPush(UiScreen.Rebind);
            Assert.AreEqual(BackResult.ClosedOverlay, s.Back());
            Assert.AreEqual(UiScreen.Options, s.Top);
            Assert.AreEqual(BackResult.ClosedOverlay, s.Back());
            Assert.AreEqual(UiScreen.Pause, s.Top);
        }

        [Test]
        public void BackOnThePauseScreenResumes()
        {
            Assert.AreEqual(BackResult.Resume, In(FlowState.Paused).Back());
        }

        [Test]
        public void BackWhilePlayingPauses()
        {
            Assert.AreEqual(BackResult.Pause, In(FlowState.Playing).Back());
        }

        [Test]
        public void BackOnTheTitleOrWhileLoadingDoesNothing()
        {
            Assert.AreEqual(BackResult.Nothing, In(FlowState.Title).Back());
            Assert.AreEqual(BackResult.Nothing, In(FlowState.Loading).Back());
            Assert.AreEqual(BackResult.Nothing, In(FlowState.Boot).Back());
        }

        [Test]
        public void BackOnTheTitleWithOptionsOpenClosesOptionsAndNotTheGame()
        {
            ScreenStack s = In(FlowState.Title);
            s.TryPush(UiScreen.Options);
            Assert.AreEqual(BackResult.ClosedOverlay, s.Back());
            Assert.AreEqual(BackResult.Nothing, s.Back());
        }

        [Test]
        public void EscFromDeepInThePauseMenuNeedsOneBackPerLevelBeforeItResumes()
        {
            ScreenStack s = In(FlowState.Paused);
            s.TryPush(UiScreen.Options);
            Assert.AreEqual(BackResult.ClosedOverlay, s.Back());
            Assert.AreEqual(BackResult.Resume, s.Back());
        }
    }
}
