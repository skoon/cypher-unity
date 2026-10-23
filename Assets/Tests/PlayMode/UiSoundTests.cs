#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Cypher.Core;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cypher.Tests
{
    /// <summary>Every interaction with the menus raises the sound it should, heard through <see cref="UiSound.Sink"/> (the audio engine is off here; what plays is the engine's business).</summary>
    public class UiSoundTests : UiTestBase
    {
        readonly List<string> _heard = new List<string>();

        [SetUp]
        public void Listen()
        {
            _heard.Clear();
            UiSound.Sink = s => _heard.Add(s);
        }

        [TearDown]
        public void Stop()
        {
            UiSound.Sink = null;
        }

        [UnityTest]
        public IEnumerator ArrowingDownTheTitleMenuHoversAndPressingAnEntrySelectsAndOpensItsScreen()
        {
            yield return ToTitle();
            _heard.Clear();
            yield return Nav(MoveDirection.Down);
            yield return Frames(2);
            CollectionAssert.Contains(_heard, UiSound.Hover);
            _heard.Clear();
            yield return Submit();
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 2f, "Options did not open");
            Assert.AreEqual(UiSound.Select, _heard[0], "the press");
            CollectionAssert.Contains(_heard, UiSound.Open, "and the screen coming up");
        }

        [UnityTest]
        public IEnumerator PointingAtAnEntryHovers()
        {
            yield return ToTitle();
            _heard.Clear();
            using (var e = PointerEnterEvent.GetPooled())
            {
                e.target = Ui.Title.Options;
                Ui.Title.Options.SendEvent(e);
            }
            Assert.AreEqual(new[] { UiSound.Hover }, _heard.ToArray());
        }

        [UnityTest]
        public IEnumerator NewGameConfirmsAndTheValueControlsTickAsTheyChange()
        {
            yield return ToOptions();
            _heard.Clear();
            Ui.Options.SelectTab(0);
            Ui.Options.FocusFirstOfPage();
            yield return null;
            _heard.Clear();
            yield return Nav(MoveDirection.Right);
            yield return Nav(MoveDirection.Right);
            int ticks = _heard.FindAll(s => s == UiSound.Tick).Count;
            Assert.GreaterOrEqual(ticks, 1, "stepping a control ticks");
            Assert.AreEqual(0, _heard.FindAll(s => s == UiSound.Select).Count, "and does not also 'select'");
        }

        [UnityTest]
        public IEnumerator BackCloseAndThePauseMenuHaveTheirSounds()
        {
            yield return ToOptions();
            _heard.Clear();
            yield return Press(Key.Escape);
            yield return WaitUntil(() => Ui.Top == UiScreen.Title, 2f, "Esc did not close Options");
            CollectionAssert.Contains(_heard, UiSound.Back);

            _heard.Clear();
            Assert.IsTrue(Flow.NewGame());
            yield return Frames(3);
            Assert.IsTrue(Flow.Pause());
            yield return Frames(3);
            CollectionAssert.Contains(_heard, UiSound.Open, "the pause menu opens with a sound");
            _heard.Clear();
            Assert.IsTrue(Flow.Resume());
            yield return Frames(3);
            CollectionAssert.Contains(_heard, UiSound.Close, "and closes with one");
        }

        [UnityTest]
        public IEnumerator AskingToQuitIsAnAlertAndANoticeIsAChime()
        {
            yield return ToTitle();
            _heard.Clear();
            Ui.Title.Quit.Activate();
            yield return WaitUntil(() => Ui.Top == UiScreen.ConfirmQuit, 2f, "the quit question did not appear");
            CollectionAssert.Contains(_heard, UiSound.Alert);
            _heard.Clear();
            Ui.ShowNotice("hello");
            Assert.AreEqual(new[] { UiSound.Notify }, _heard.ToArray());
        }
    }
}
#endif
