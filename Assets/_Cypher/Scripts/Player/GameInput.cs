using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine.InputSystem;

namespace Cypher.Player
{
    /// <summary>
    /// The game's buttons beyond walking (interact, scan, the menus, the guns), from the controls in force in <see cref="InputBindings"/>, rebuilt whenever the
    /// player changes them. Read by whoever acts on them: the RPG service, the HUD, the weapons.
    /// </summary>
    public sealed class GameInput : IDisposable
    {
        /// <summary>The actions read here: everything that is not movement and not the controller's own (jump, sprint, crouch).</summary>
        public static readonly IReadOnlyList<GameAction> Actions = new[]
        {
            GameAction.Interact, GameAction.Scan, GameAction.Inventory, GameAction.Journal, GameAction.Fire, GameAction.Aim, GameAction.Reload,
            GameAction.Weapon1, GameAction.Weapon2, GameAction.Weapon3, GameAction.Character,
        };

        readonly InputBindings _bindings;
        readonly Dictionary<GameAction, InputAction> _actions = new Dictionary<GameAction, InputAction>();

        public GameInput(InputBindings bindings)
        {
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            Build();
            _bindings.Changed += Build;
        }

        /// <summary>Pressed this frame.</summary>
        public bool WasPressed(GameAction action) => Get(action).WasPressedThisFrame();

        /// <summary>Held now.</summary>
        public bool IsPressed(GameAction action) => Get(action).IsPressed();

        public bool WasReleased(GameAction action) => Get(action).WasReleasedThisFrame();

        InputAction Get(GameAction action) =>
            _actions.TryGetValue(action, out InputAction a) ? a : throw new ArgumentOutOfRangeException(nameof(action), action, "not one of the game's buttons");

        void Build()
        {
            DisposeActions();
            BindingMap map = _bindings.Snapshot();
            foreach (GameAction action in Actions)
            {
                var input = new InputAction(action.ToString(), InputActionType.Button);
                foreach (BindingSlot slot in new[] { BindingSlot.Primary, BindingSlot.Secondary, BindingSlot.Gamepad })
                {
                    string path = map.Get(action, slot);
                    if (path != null) input.AddBinding(path);
                }
                input.Enable();
                _actions[action] = input;
            }
        }

        void DisposeActions()
        {
            foreach (InputAction a in _actions.Values) a.Dispose();
            _actions.Clear();
        }

        public void Dispose()
        {
            _bindings.Changed -= Build;
            DisposeActions();
        }
    }
}
