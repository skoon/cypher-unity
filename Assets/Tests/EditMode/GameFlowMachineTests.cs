using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class GameFlowMachineTests
    {
        static GameFlowMachine InState(FlowState target, bool skipTitle = false)
        {
            var m = new GameFlowMachine(skipTitle);
            if (target == FlowState.Boot) return m;
            m.Handle(FlowCommand.Booted);
            if (target == FlowState.Loading) return m;
            m.Handle(FlowCommand.LoadingFinished);
            if (target == FlowState.Title) return m;
            m.Handle(FlowCommand.NewGame);
            if (target == FlowState.Playing) return m;
            m.Handle(FlowCommand.Pause);
            Assert.AreEqual(FlowState.Paused, target);
            return m;
        }

        [Test]
        public void StartsInBoot()
        {
            var m = new GameFlowMachine();
            Assert.AreEqual(FlowState.Boot, m.State);
            Assert.IsFalse(m.QuitRequested);
        }

        [Test]
        public void TheNormalLifeOfASession()
        {
            var m = new GameFlowMachine();
            var seen = new List<FlowTransition>();
            m.Transitioned += seen.Add;

            m.Handle(FlowCommand.Booted);
            m.Handle(FlowCommand.LoadingFinished);
            m.Handle(FlowCommand.NewGame);
            m.Handle(FlowCommand.TogglePause);
            m.Handle(FlowCommand.TogglePause);
            m.Handle(FlowCommand.Pause);
            m.Handle(FlowCommand.Resume);
            m.Handle(FlowCommand.Pause);
            m.Handle(FlowCommand.ReturnToTitle);
            m.Handle(FlowCommand.NewGame);

            CollectionAssert.AreEqual(
                new[] { FlowState.Loading, FlowState.Title, FlowState.Playing, FlowState.Paused, FlowState.Playing, FlowState.Paused, FlowState.Playing, FlowState.Paused, FlowState.Title, FlowState.Playing },
                seen.ConvertAll(t => t.To));
            Assert.AreEqual(FlowState.Playing, m.State);
        }

        [Test]
        public void OnlyEnteringPlayingFromTheTitleOrLoadingIsANewSession()
        {
            var m = new GameFlowMachine();
            var seen = new List<FlowTransition>();
            m.Transitioned += seen.Add;
            m.Handle(FlowCommand.Booted);
            m.Handle(FlowCommand.LoadingFinished);
            m.Handle(FlowCommand.NewGame);       // from Title
            m.Handle(FlowCommand.Pause);
            m.Handle(FlowCommand.Resume);        // from Paused
            m.Handle(FlowCommand.Pause);
            m.Handle(FlowCommand.ReturnToTitle);
            m.Handle(FlowCommand.NewGame);       // from Title again

            var entersPlaying = seen.FindAll(t => t.To == FlowState.Playing);
            Assert.AreEqual(3, entersPlaying.Count);
            Assert.IsTrue(entersPlaying[0].NewSession);
            Assert.IsFalse(entersPlaying[1].NewSession, "resuming is not a new game");
            Assert.IsTrue(entersPlaying[2].NewSession);
            Assert.IsFalse(seen.Find(t => t.To == FlowState.Paused).NewSession);
            Assert.IsFalse(seen.Find(t => t.To == FlowState.Title).NewSession);
        }

        [Test]
        public void TransitionsCarryTheirOriginAndTheCommand()
        {
            var m = InState(FlowState.Playing);
            FlowTransition? last = null;
            m.Transitioned += t => last = t;
            m.Handle(FlowCommand.TogglePause);
            Assert.AreEqual(FlowState.Playing, last.Value.From);
            Assert.AreEqual(FlowState.Paused, last.Value.To);
            Assert.AreEqual(FlowCommand.TogglePause, last.Value.Command);
        }

        [Test]
        public void SkipTitleGoesStraightFromLoadingToANewGame()
        {
            var m = new GameFlowMachine(skipTitle: true);
            FlowTransition? last = null;
            m.Transitioned += t => last = t;
            m.Handle(FlowCommand.Booted);
            m.Handle(FlowCommand.LoadingFinished);
            Assert.AreEqual(FlowState.Playing, m.State);
            Assert.IsTrue(last.Value.NewSession, "the player still has to be spawned");
            m.Handle(FlowCommand.Pause);
            m.Handle(FlowCommand.ReturnToTitle);
            Assert.AreEqual(FlowState.Title, m.State, "the title is still reachable afterwards");
        }

        // ----- which commands mean something where

        static readonly FlowCommand[] Everything = (FlowCommand[])Enum.GetValues(typeof(FlowCommand));

        [TestCase(FlowState.Boot, new[] { FlowCommand.Booted })]
        [TestCase(FlowState.Loading, new[] { FlowCommand.LoadingFinished })]
        [TestCase(FlowState.Title, new[] { FlowCommand.NewGame, FlowCommand.Quit })]
        [TestCase(FlowState.Playing, new[] { FlowCommand.Pause, FlowCommand.TogglePause })]
        [TestCase(FlowState.Paused, new[] { FlowCommand.Resume, FlowCommand.TogglePause, FlowCommand.ReturnToTitle, FlowCommand.Quit })]
        public void EachStateAcceptsExactlyItsCommands(FlowState state, FlowCommand[] accepted)
        {
            foreach (FlowCommand c in Everything)
            {
                bool expected = Array.IndexOf(accepted, c) >= 0;
                Assert.AreEqual(expected, InState(state).Can(c), $"{c} in {state}");
                Assert.AreEqual(expected, InState(state).TryHandle(c), $"TryHandle {c} in {state}");
            }
        }

        [Test]
        public void EscDoesNothingOnTheTitleOrWhileLoading()
        {
            foreach (FlowState s in new[] { FlowState.Boot, FlowState.Loading, FlowState.Title })
            {
                GameFlowMachine m = InState(s);
                Assert.IsFalse(m.TryHandle(FlowCommand.TogglePause), s.ToString());
                Assert.AreEqual(s, m.State);
            }
        }

        [Test]
        public void ARejectedCommandChangesNothingAndRaisesNothing()
        {
            var m = InState(FlowState.Title);
            int events = 0;
            m.Transitioned += _ => events++;
            Assert.IsFalse(m.TryHandle(FlowCommand.Resume));
            Assert.AreEqual(FlowState.Title, m.State);
            Assert.AreEqual(0, events);
        }

        [Test]
        public void HandleThrowsWhenTheCommandDoesNotApply()
        {
            var m = InState(FlowState.Title);
            InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => m.Handle(FlowCommand.Resume));
            StringAssert.Contains("Resume is not valid while Title", e.Message);
            Assert.AreEqual(FlowState.Title, m.State);
        }

        // ----- quitting

        [TestCase(FlowState.Title)]
        [TestCase(FlowState.Paused)]
        public void QuitIsRememberedAndAnnouncedWithoutChangingState(FlowState state)
        {
            var m = InState(state);
            int quits = 0, transitions = 0;
            m.QuitIssued += () => quits++;
            m.Transitioned += _ => transitions++;
            Assert.IsTrue(m.TryHandle(FlowCommand.Quit));
            Assert.IsTrue(m.QuitRequested);
            Assert.AreEqual(1, quits);
            Assert.AreEqual(0, transitions);
            Assert.AreEqual(state, m.State);
        }

        [Test]
        public void QuitIsNotAcceptedWhileBootingLoadingOrPlaying()
        {
            Assert.IsFalse(InState(FlowState.Boot).TryHandle(FlowCommand.Quit));
            Assert.IsFalse(InState(FlowState.Loading).TryHandle(FlowCommand.Quit));
            var playing = InState(FlowState.Playing);
            Assert.IsFalse(playing.TryHandle(FlowCommand.Quit), "quitting goes through the pause screen");
            Assert.IsFalse(playing.QuitRequested);
        }

        // ----- what each state means

        [Test]
        public void OnlyPlayingLocksTheCursorAndRunsThePlayer()
        {
            foreach (FlowState s in (FlowState[])Enum.GetValues(typeof(FlowState)))
            {
                FlowRules r = FlowPolicy.For(s);
                Assert.AreEqual(s == FlowState.Playing, r.CursorLocked, "cursor in " + s);
                Assert.AreEqual(s == FlowState.Playing, r.PlayerActive, "player in " + s);
                Assert.AreEqual(s == FlowState.Playing, r.AdaptiveQualityActive, "governor in " + s);
            }
        }

        [Test]
        public void OnlyPausedStopsTime()
        {
            foreach (FlowState s in (FlowState[])Enum.GetValues(typeof(FlowState)))
            {
                Assert.AreEqual(s == FlowState.Paused ? 0.0 : 1.0, FlowPolicy.For(s).TimeScale, "time in " + s);
            }
        }

        [Test]
        public void TheTitleCameraFliesBeforeTheGameStartsAndNeverWhileTheGameRuns()
        {
            Assert.IsTrue(FlowPolicy.For(FlowState.Boot).TitleCameraActive);
            Assert.IsTrue(FlowPolicy.For(FlowState.Loading).TitleCameraActive);
            Assert.IsTrue(FlowPolicy.For(FlowState.Title).TitleCameraActive);
            Assert.IsFalse(FlowPolicy.For(FlowState.Playing).TitleCameraActive);
            Assert.IsFalse(FlowPolicy.For(FlowState.Paused).TitleCameraActive, "pausing keeps the player's view");
        }

        [Test]
        public void ThePlayerIsNeverActiveAtTimeScaleZero()
        {
            // A controller ticking with a zero delta would be asked to move for no time and throw.
            foreach (FlowState s in (FlowState[])Enum.GetValues(typeof(FlowState)))
            {
                FlowRules r = FlowPolicy.For(s);
                Assert.IsFalse(r.PlayerActive && r.TimeScale == 0, s.ToString());
            }
        }

        [Test]
        public void UnknownStatesFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FlowPolicy.For((FlowState)99));
        }
    }
}
