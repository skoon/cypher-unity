using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Cypher.Tests
{
    /// <summary>
    /// A keyboard, a mouse and a gamepad that exist only for a test, with the Input System told to ignore window focus (a headless run is
    /// never focused). Dispose removes the devices and puts the settings back.
    /// </summary>
    public sealed class VirtualInput : IDisposable
    {
        readonly List<InputDevice> _devices = new List<InputDevice>();
        readonly HashSet<Key> _down = new HashSet<Key>();
        readonly InputSettings.BackgroundBehavior _savedBackground;
        readonly InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehavior;

        public Keyboard Keyboard { get; }
        public Mouse Mouse { get; }
        public Gamepad Pad { get; }

        public VirtualInput()
        {
            _savedBackground = InputSystem.settings.backgroundBehavior;
            _savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            Keyboard = Add<Keyboard>();
            Mouse = Add<Mouse>();
            Pad = Add<Gamepad>();
        }

        T Add<T>() where T : InputDevice
        {
            T device = InputSystem.AddDevice<T>();
            _devices.Add(device);
            return device;
        }

        public void Hold(Key key)
        {
            _down.Add(key);
            Send();
        }

        public void Release(Key key)
        {
            _down.Remove(key);
            Send();
        }

        public void ReleaseAll()
        {
            _down.Clear();
            Send();
        }

        void Send()
        {
            var keys = new Key[_down.Count];
            _down.CopyTo(keys);
            InputSystem.QueueStateEvent(Keyboard, new KeyboardState(keys));
        }

        public void PressMouse(MouseButton button, bool down)
        {
            var state = new MouseState();
            state.WithButton(button, down);
            InputSystem.QueueStateEvent(Mouse, state);
        }

        public void PressPad(GamepadButton button, bool down)
        {
            var state = new GamepadState();
            if (down) state.buttons |= 1u << (int)button;
            InputSystem.QueueStateEvent(Pad, state);
        }

        public void Dispose()
        {
            foreach (InputDevice d in _devices) InputSystem.RemoveDevice(d);
            _devices.Clear();
            InputSystem.settings.backgroundBehavior = _savedBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = _savedEditorBehavior;
        }
    }
}
