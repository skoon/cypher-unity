using System;
using Cypher.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cypher.Player
{
    /// <summary>Produces one PlayerCommand per frame. Poll exactly once per frame.</summary>
    public interface IPlayerCommandSource
    {
        PlayerCommand Poll(double dt);
    }

    /// <summary>
    /// Keyboard, mouse and gamepad bindings for the first-person controller, built in code so the
    /// bindings live next to the logic that consumes them. Keys mirror the web prototype:
    /// WASD / arrows, Space, Shift, Ctrl or C.
    /// </summary>
    public sealed class InputSystemCommandSource : IPlayerCommandSource, IDisposable
    {
        /// <summary>Right-stick look speed expressed as mouse pixels per second.</summary>
        public const double StickLookPixelsPerSecond = 900;

        readonly InputAction _move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        readonly InputAction _lookMouse = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta", expectedControlType: "Vector2");
        readonly InputAction _lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlType: "Vector2");
        readonly InputAction _jump = new InputAction("Jump", InputActionType.Button);
        readonly InputAction _sprint = new InputAction("Sprint", InputActionType.Button);
        readonly InputAction _crouch = new InputAction("Crouch", InputActionType.Button);

        public InputSystemCommandSource()
        {
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");

            _jump.AddBinding("<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _sprint.AddBinding("<Keyboard>/leftShift");
            _sprint.AddBinding("<Keyboard>/rightShift");
            _sprint.AddBinding("<Gamepad>/leftStickPress");

            _crouch.AddBinding("<Keyboard>/leftCtrl");
            _crouch.AddBinding("<Keyboard>/c");
            _crouch.AddBinding("<Gamepad>/buttonEast");

            foreach (InputAction a in All) a.Enable();
        }

        InputAction[] All => new[] { _move, _lookMouse, _lookStick, _jump, _sprint, _crouch };

        public PlayerCommand Poll(double dt)
        {
            Vector2 move = Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
            Vector2 look = _lookMouse.ReadValue<Vector2>() + _lookStick.ReadValue<Vector2>() * (float)(StickLookPixelsPerSecond * dt);
            return new PlayerCommand(
                forward: move.y,
                strafe: move.x,
                sprint: _sprint.IsPressed(),
                crouch: _crouch.IsPressed(),
                jumpPressed: _jump.WasPressedThisFrame(),
                lookX: look.x,
                lookY: look.y);
        }

        public void Dispose()
        {
            foreach (InputAction a in All) a.Dispose();
        }
    }
}
