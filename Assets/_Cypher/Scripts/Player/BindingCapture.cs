using System;
using Cypher.Core;
using UnityEngine.InputSystem;

namespace Cypher.Player
{
    /// <summary>
    /// Waits for the player to press the control they want for a slot and reports its path: a keyboard key or mouse button for the
    /// keyboard and mouse slots, a gamepad button for the gamepad slot. Esc cancels, as does timing out. It only <i>captures</i>; whether
    /// the control is allowed or already taken is <see cref="BindingMap"/>'s decision, made on the path it reports. One of the three
    /// callbacks fires exactly once, and the capture is over by then.
    /// </summary>
    public sealed class BindingCapture : IDisposable
    {
        public const float DefaultTimeoutSeconds = 12f;

        /// <summary>Pressing Esc cancels a capture. It is also reserved, so it can never be bound by mistake.</summary>
        public const string CancelControl = "<Keyboard>/escape";

        readonly InputAction _probe;
        readonly InputActionRebindingExtensions.RebindingOperation _operation;
        readonly Action<string> _captured;
        readonly Action _cancelled;
        string _path;
        bool _disposed;

        public bool IsActive { get; private set; }

        BindingCapture(BindingSlot slot, Action<string> captured, Action cancelled, float timeoutSeconds)
        {
            _captured = captured;
            _cancelled = cancelled;

            // The action is never enabled and never read: it only gives the Input System something to rebind.
            _probe = new InputAction("BindingCapture", InputActionType.Button, slot == BindingSlot.Gamepad ? "<Gamepad>/buttonSouth" : "<Keyboard>/space");
            _operation = _probe.PerformInteractiveRebinding(0)
                .WithExpectedControlType("Button")   // keys and buttons only: no sticks, mouse movement, position or scroll
                .WithCancelingThrough(CancelControl)
                .WithTimeout(timeoutSeconds)
                .OnMatchWaitForAnother(0.05f)
                .OnApplyBinding((op, path) => _path = path)
                .OnComplete(op => Finish(captured: true))
                .OnCancel(op => Finish(captured: false));

            if (slot == BindingSlot.Gamepad)
            {
                // Start and Select are the game's own: Start cancels the prompt (the menus listen for it), so it is never a candidate.
                _operation.WithControlsHavingToMatchPath("<Gamepad>/*").WithControlsExcluding("<Gamepad>/start").WithControlsExcluding("<Gamepad>/select");
            }
            else
            {
                _operation.WithControlsHavingToMatchPath("<Keyboard>/*").WithControlsHavingToMatchPath("<Mouse>/*").WithControlsExcluding("<Keyboard>/anyKey");
            }

            IsActive = true;
            _operation.Start();
        }

        /// <summary>Start listening. <paramref name="captured"/> gets the control path; <paramref name="cancelled"/> (optional) runs on Esc, a timeout or <see cref="Cancel"/>.</summary>
        public static BindingCapture Begin(BindingSlot slot, Action<string> captured, Action cancelled = null, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (captured == null) throw new ArgumentNullException(nameof(captured));
            if (!(timeoutSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), timeoutSeconds, "the timeout must be positive");
            return new BindingCapture(slot, captured, cancelled, timeoutSeconds);
        }

        /// <summary>Stop listening and report a cancel. Does nothing if the capture already finished.</summary>
        public void Cancel()
        {
            if (!IsActive) return;
            _operation.Cancel();
        }

        /// <summary>Stop without reporting anything and release the listener. Safe to call more than once; a finished capture has already done it.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IsActive = false;
            _operation.Dispose();
            _probe.Dispose();
        }

        void Finish(bool captured)
        {
            if (!IsActive) return;
            IsActive = false;
            try
            {
                if (captured) _captured(_path);
                else _cancelled?.Invoke();
            }
            finally
            {
                Dispose();   // a finished capture has nothing left to do
            }
        }
    }
}
