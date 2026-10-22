using System;
using System.Collections;
using Cypher.Core;
using Cypher.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    public class BindingCaptureTests
    {
        VirtualInput _input;
        BindingCapture _capture;

        [SetUp]
        public void SetUp() => _input = new VirtualInput();

        [TearDown]
        public void TearDown()
        {
            _capture?.Dispose();
            _input.Dispose();
        }

        sealed class Outcome
        {
            public string Captured;
            public int CapturedCount;
            public int CancelledCount;
            public bool Done => CapturedCount + CancelledCount > 0;
        }

        Outcome Begin(BindingSlot slot, float timeout = BindingCapture.DefaultTimeoutSeconds)
        {
            var o = new Outcome();
            _capture = BindingCapture.Begin(slot, path => { o.Captured = path; o.CapturedCount++; }, () => o.CancelledCount++, timeout);
            return o;
        }

        static IEnumerator WaitUntilDone(Outcome o, float seconds = 3f)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!o.Done && Time.realtimeSinceStartup < until) yield return null;
        }

        IEnumerator PressKey(Key key)
        {
            _input.Hold(key);
            yield return null;
            yield return null;
            _input.ReleaseAll();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AKeyPressIsReportedAsItsPath()
        {
            Outcome o = Begin(BindingSlot.Primary);
            Assert.IsTrue(_capture.IsActive);
            yield return PressKey(Key.J);
            yield return WaitUntilDone(o);

            Assert.AreEqual(1, o.CapturedCount);
            Assert.AreEqual("<Keyboard>/j", o.Captured);
            Assert.AreEqual(0, o.CancelledCount);
            Assert.IsFalse(_capture.IsActive, "a finished capture is over");
        }

        [UnityTest]
        public IEnumerator ModifierAndArrowKeysAreCapturedToo()
        {
            Outcome o = Begin(BindingSlot.Secondary);
            yield return PressKey(Key.LeftAlt);
            yield return WaitUntilDone(o);
            Assert.AreEqual("<Keyboard>/leftAlt", o.Captured);

            _capture.Dispose();
            o = Begin(BindingSlot.Primary);
            yield return PressKey(Key.DownArrow);
            yield return WaitUntilDone(o);
            Assert.AreEqual("<Keyboard>/downArrow", o.Captured);
        }

        [UnityTest]
        public IEnumerator AMouseButtonIsCapturedInAKeyboardSlot()
        {
            Outcome o = Begin(BindingSlot.Primary);
            _input.PressMouse(MouseButton.Middle, true);
            yield return null;
            yield return null;
            _input.PressMouse(MouseButton.Middle, false);
            yield return WaitUntilDone(o);
            Assert.AreEqual("<Mouse>/middleButton", o.Captured);
        }

        [UnityTest]
        public IEnumerator MouseMovementAndScrollAreIgnoredWhileWaitingForARealPress()
        {
            Outcome o = Begin(BindingSlot.Primary);
            InputSystem.QueueStateEvent(_input.Mouse, new MouseState { delta = new Vector2(40f, -25f), scroll = new Vector2(0f, 120f) });
            yield return null;
            yield return null;
            Assert.IsFalse(o.Done, "moving the mouse is not a choice");
            Assert.IsTrue(_capture.IsActive);

            yield return PressKey(Key.U);
            yield return WaitUntilDone(o);
            Assert.AreEqual("<Keyboard>/u", o.Captured);
        }

        [UnityTest]
        public IEnumerator EscapeCancelsAndBindsNothing()
        {
            Outcome o = Begin(BindingSlot.Primary);
            yield return PressKey(Key.Escape);
            yield return WaitUntilDone(o);

            Assert.AreEqual(1, o.CancelledCount);
            Assert.AreEqual(0, o.CapturedCount);
            Assert.IsNull(o.Captured);
            Assert.IsFalse(_capture.IsActive);
        }

        [UnityTest]
        public IEnumerator TheGamepadSlotCapturesAGamepadButtonAndIgnoresTheKeyboard()
        {
            Outcome o = Begin(BindingSlot.Gamepad);
            yield return PressKey(Key.K);
            yield return null;
            Assert.IsFalse(o.Done, "a key is not a gamepad button");

            _input.PressPad(GamepadButton.North, true);
            yield return null;
            yield return null;
            _input.PressPad(GamepadButton.North, false);
            yield return WaitUntilDone(o);
            Assert.AreEqual("<Gamepad>/buttonNorth", o.Captured);
        }

        [UnityTest]
        public IEnumerator StartAndSelectOnTheGamepadAreNotCandidatesAndDoNotCancelByThemselves()
        {
            Outcome o = Begin(BindingSlot.Gamepad);
            _input.PressPad(GamepadButton.Start, true);
            yield return null;
            yield return null;
            _input.PressPad(GamepadButton.Start, false);
            _input.PressPad(GamepadButton.Select, true);
            yield return null;
            yield return null;
            _input.PressPad(GamepadButton.Select, false);
            yield return null;
            Assert.IsFalse(o.Done, "they are reserved: the menus use Start to cancel, so the capture itself ignores it");
            Assert.IsTrue(_capture.IsActive);
        }

        [UnityTest]
        public IEnumerator TheGamepadSlotIgnoresStickMovement()
        {
            Outcome o = Begin(BindingSlot.Gamepad);
            InputState.Change(_input.Pad.leftStick, new Vector2(1f, 0f));
            yield return null;
            yield return null;
            Assert.IsFalse(o.Done, "a stick is not a button");
            InputState.Change(_input.Pad.leftStick, Vector2.zero);
        }

        [UnityTest]
        public IEnumerator EscapeOnTheKeyboardCancelsAGamepadCapture()
        {
            Outcome o = Begin(BindingSlot.Gamepad);
            yield return PressKey(Key.Escape);
            yield return WaitUntilDone(o);
            Assert.AreEqual(1, o.CancelledCount);
        }

        [UnityTest]
        public IEnumerator CancelStopsListeningAndLaterPressesAreIgnored()
        {
            Outcome o = Begin(BindingSlot.Primary);
            _capture.Cancel();
            Assert.AreEqual(1, o.CancelledCount);
            Assert.IsFalse(_capture.IsActive);

            yield return PressKey(Key.J);
            yield return null;
            Assert.AreEqual(0, o.CapturedCount);
            Assert.AreEqual(1, o.CancelledCount, "and it did not report a second time");
            _capture.Cancel();   // harmless
            Assert.AreEqual(1, o.CancelledCount);
        }

        [UnityTest]
        public IEnumerator ACaptureThatIsNeverAnsweredTimesOutAsACancel()
        {
            Outcome o = Begin(BindingSlot.Primary, timeout: 0.4f);
            yield return WaitUntilDone(o, 3f);
            Assert.AreEqual(1, o.CancelledCount);
            Assert.AreEqual(0, o.CapturedCount);
        }

        [UnityTest]
        public IEnumerator CapturesCanFollowOneAnotherInTheSameKeyboardAndGamepad()
        {
            Outcome first = Begin(BindingSlot.Primary);
            yield return PressKey(Key.J);
            yield return WaitUntilDone(first);
            Assert.AreEqual("<Keyboard>/j", first.Captured);

            Outcome second = Begin(BindingSlot.Gamepad);
            _input.PressPad(GamepadButton.RightShoulder, true);
            yield return null;
            yield return null;
            _input.PressPad(GamepadButton.RightShoulder, false);
            yield return WaitUntilDone(second);
            Assert.AreEqual("<Gamepad>/rightShoulder", second.Captured, "the second capture works after the first one finished");

            Outcome third = Begin(BindingSlot.Secondary);
            yield return PressKey(Key.K);
            yield return WaitUntilDone(third);
            Assert.AreEqual("<Keyboard>/k", third.Captured);
        }

        [UnityTest]
        public IEnumerator DisposingWhileWaitingReportsNothing()
        {
            Outcome o = Begin(BindingSlot.Primary);
            _capture.Dispose();
            Assert.IsFalse(_capture.IsActive);
            yield return PressKey(Key.J);
            yield return null;
            Assert.IsFalse(o.Done);
            _capture.Dispose();   // twice is fine
        }

        [UnityTest]
        public IEnumerator ACapturedControlGoesStraightIntoTheMapForTheRulesToJudge()
        {
            var map = BindingMap.Defaults();

            Outcome free = Begin(BindingSlot.Primary);
            yield return PressKey(Key.J);
            yield return WaitUntilDone(free);
            Assert.AreEqual(AssignOutcome.Applied, map.Assign(GameAction.Jump, BindingSlot.Primary, free.Captured).Outcome);

            Outcome taken = Begin(BindingSlot.Primary);
            yield return PressKey(Key.W);
            yield return WaitUntilDone(taken);
            Assert.AreEqual(AssignOutcome.Conflict, map.Assign(GameAction.Crouch, BindingSlot.Primary, taken.Captured).Outcome);

            Outcome reserved = Begin(BindingSlot.Primary);
            yield return PressKey(Key.Enter);
            yield return WaitUntilDone(reserved);
            Assert.AreEqual(AssignOutcome.Rejected, map.Assign(GameAction.Crouch, BindingSlot.Primary, reserved.Captured).Outcome, "captured, then refused by the rules");
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => BindingCapture.Begin(BindingSlot.Primary, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => BindingCapture.Begin(BindingSlot.Primary, _ => { }, null, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => BindingCapture.Begin(BindingSlot.Primary, _ => { }, null, -1f));
        }
    }
}
