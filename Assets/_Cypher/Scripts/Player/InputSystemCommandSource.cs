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
    /// Keyboard, mouse and gamepad input for the first-person controller. The keyboard, mouse-button and gamepad-button controls come
    /// from <see cref="InputBindings"/> (the defaults mirror the web prototype: WASD or arrows, Space, Shift, Ctrl or C) and are
    /// rebuilt whenever the player changes them. Fixed: mouse movement and the right stick look, the left stick moves.
    /// </summary>
    public sealed class InputSystemCommandSource : IPlayerCommandSource, IDisposable
    {
        /// <summary>Right-stick look speed expressed as mouse pixels per second.</summary>
        public const double StickLookPixelsPerSecond = 900;

        readonly InputBindings _bindings;
        readonly InputAction _lookMouse = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta", expectedControlType: "Vector2");
        readonly InputAction _lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlType: "Vector2");

        InputAction _move;
        InputAction _jump;
        InputAction _sprint;
        InputAction _crouch;

        public InputSystemCommandSource() : this(new InputBindings())
        {
        }

        public InputSystemCommandSource(InputBindings bindings)
        {
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _lookMouse.Enable();
            _lookStick.Enable();
            BuildButtons();
            _bindings.Changed += BuildButtons;
        }

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
            _bindings.Changed -= BuildButtons;
            DisposeButtons();
            _lookMouse.Dispose();
            _lookStick.Dispose();
        }

        /// <summary>(Re)create the actions that depend on the player's controls from what <see cref="InputBindings"/> holds now.</summary>
        void BuildButtons()
        {
            DisposeButtons();
            BindingMap map = _bindings.Snapshot();

            _move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            AddMovement(_move, map, BindingSlot.Primary);
            AddMovement(_move, map, BindingSlot.Secondary);
            _move.AddBinding("<Gamepad>/leftStick");

            _jump = Button("Jump", map, GameAction.Jump);
            _sprint = Button("Sprint", map, GameAction.Sprint);
            _crouch = Button("Crouch", map, GameAction.Crouch);

            _move.Enable();
            _jump.Enable();
            _sprint.Enable();
            _crouch.Enable();
        }

        void DisposeButtons()
        {
            foreach (InputAction a in new[] { _move, _jump, _sprint, _crouch }) a?.Dispose();
            _move = _jump = _sprint = _crouch = null;
        }

        /// <summary>One 2D-vector binding per slot, built from the four directions' controls in that slot; directions with none are left out.</summary>
        static void AddMovement(InputAction move, BindingMap map, BindingSlot slot)
        {
            string up = map.Get(GameAction.MoveForward, slot), down = map.Get(GameAction.MoveBack, slot);
            string left = map.Get(GameAction.MoveLeft, slot), right = map.Get(GameAction.MoveRight, slot);
            if (up == null && down == null && left == null && right == null) return;

            var composite = move.AddCompositeBinding("2DVector");
            if (up != null) composite = composite.With("Up", up);
            if (down != null) composite = composite.With("Down", down);
            if (left != null) composite = composite.With("Left", left);
            if (right != null) composite = composite.With("Right", right);
        }

        static InputAction Button(string name, BindingMap map, GameAction action)
        {
            var input = new InputAction(name, InputActionType.Button);
            foreach (BindingSlot slot in new[] { BindingSlot.Primary, BindingSlot.Secondary, BindingSlot.Gamepad })
            {
                string path = map.Get(action, slot);
                if (path != null) input.AddBinding(path);
            }
            return input;
        }
    }
}
