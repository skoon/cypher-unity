using System.Collections.Generic;
using Cypher.Core;
using Cypher.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Cypher.Tests
{
    /// <summary>
    /// Drives the real bindings with virtual devices. Devices are added and removed per test so
    /// nothing leaks into other tests, and focus is ignored because a headless run is never focused.
    /// </summary>
    public class InputSystemCommandSourceTests
    {
        readonly List<InputDevice> _devices = new List<InputDevice>();
        readonly HashSet<Key> _down = new HashSet<Key>();
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehavior;
        InputSystemCommandSource _source;
        Keyboard _keyboard;
        Mouse _mouse;
        Gamepad _pad;

        [SetUp]
        public void SetUp()
        {
            // A headless run has no focused game view; without these the devices are ignored.
            _savedBackground = InputSystem.settings.backgroundBehavior;
            _savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            _down.Clear();
            _keyboard = Add<Keyboard>();
            _mouse = Add<Mouse>();
            _pad = Add<Gamepad>();
            _source = new InputSystemCommandSource();
        }

        [TearDown]
        public void TearDown()
        {
            _source.Dispose();
            foreach (InputDevice d in _devices) InputSystem.RemoveDevice(d);
            _devices.Clear();
            InputSystem.settings.backgroundBehavior = _savedBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = _savedEditorBehavior;
        }

        T Add<T>() where T : InputDevice
        {
            T device = InputSystem.AddDevice<T>();
            _devices.Add(device);
            return device;
        }

        void SendKeys()
        {
            Key[] keys = new Key[_down.Count];
            _down.CopyTo(keys);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));   // processed by the next Poll's single update
        }

        void Hold(Key key)
        {
            _down.Add(key);
            SendKeys();
        }

        void Release(Key key)
        {
            _down.Remove(key);
            SendKeys();
        }

        PlayerCommand Poll(double dt = 1.0 / 60)
        {
            InputSystem.Update();
            return _source.Poll(dt);
        }

        [Test]
        public void NothingPressedIsNeutral()
        {
            PlayerCommand c = Poll();
            Assert.AreEqual(0.0, c.Forward);
            Assert.AreEqual(0.0, c.Strafe);
            Assert.IsFalse(c.Sprint);
            Assert.IsFalse(c.Crouch);
            Assert.IsFalse(c.JumpPressed);
        }

        [Test]
        public void WasdMovesForwardBackLeftRight()
        {
            Hold(Key.W);
            Assert.AreEqual(1.0, Poll().Forward, 1e-6);
            Release(Key.W);

            Hold(Key.S);
            Assert.AreEqual(-1.0, Poll().Forward, 1e-6);
            Release(Key.S);

            Hold(Key.D);
            Assert.AreEqual(1.0, Poll().Strafe, 1e-6);
            Release(Key.D);

            Hold(Key.A);
            Assert.AreEqual(-1.0, Poll().Strafe, 1e-6);
        }

        [Test]
        public void ArrowKeysMoveToo()
        {
            Hold(Key.UpArrow);
            Assert.AreEqual(1.0, Poll().Forward, 1e-6);
            Release(Key.UpArrow);
            Hold(Key.RightArrow);
            Assert.AreEqual(1.0, Poll().Strafe, 1e-6);
        }

        [Test]
        public void DiagonalKeyboardInputIsClampedToUnitLength()
        {
            Hold(Key.W);
            Hold(Key.D);
            PlayerCommand c = Poll();
            Assert.AreEqual(1.0, System.Math.Sqrt(c.Forward * c.Forward + c.Strafe * c.Strafe), 1e-5);
            Assert.Greater(c.Forward, 0);
            Assert.Greater(c.Strafe, 0);
        }

        [Test]
        public void GamepadLeftStickMovesAnalog()
        {
            InputState.Change(_pad.leftStick, new Vector2(0.5f, 0f));
            PlayerCommand c = Poll();
            Assert.That(c.Strafe, Is.GreaterThan(0.2).And.LessThan(0.8));
        }

        [Test]
        public void JumpIsAnEdgeNotALevel()
        {
            Hold(Key.Space);
            Assert.IsTrue(Poll().JumpPressed, "first frame after the press");
            Assert.IsFalse(Poll().JumpPressed, "held, but already reported");
        }

        [Test]
        public void SprintAndCrouchAreHeldStates()
        {
            Hold(Key.LeftShift);
            Assert.IsTrue(Poll().Sprint);
            Assert.IsTrue(Poll().Sprint, "still held");
            Release(Key.LeftShift);
            Assert.IsFalse(Poll().Sprint);

            Hold(Key.LeftCtrl);
            Assert.IsTrue(Poll().Crouch);
            Release(Key.LeftCtrl);
            Hold(Key.C);
            Assert.IsTrue(Poll().Crouch, "C is an alternative crouch key");
        }

        [Test]
        public void MouseDeltaBecomesLook()
        {
            InputSystem.QueueStateEvent(_mouse, new MouseState { delta = new Vector2(12f, -5f) });
            PlayerCommand c = Poll();
            Assert.AreEqual(12.0, c.LookX, 1e-4);
            Assert.AreEqual(-5.0, c.LookY, 1e-4);
        }

        [Test]
        public void RightStickLooksAtAFrameRateIndependentSpeed()
        {
            InputState.Change(_pad.rightStick, new Vector2(1f, 0f));
            PlayerCommand slow = Poll(1.0 / 30);
            PlayerCommand fast = Poll(1.0 / 60);
            Assert.AreEqual(slow.LookX, fast.LookX * 2, 1e-3, "twice the frame time, twice the turn");
            Assert.Greater(fast.LookX, 0);
        }
    }
}
