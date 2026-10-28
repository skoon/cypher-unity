using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Cypher.Core
{
    /// <summary>The actions the player can rebind. New ones go at the end: the order is the Controls screen's and the table's.</summary>
    public enum GameAction
    {
        MoveForward, MoveBack, MoveLeft, MoveRight, Jump, Sprint, Crouch,
        Interact, Scan, Inventory, Journal, Fire, Aim, Reload, Weapon1, Weapon2, Weapon3,
    }

    /// <summary>One binding place of an action: two for keyboard and mouse, one for a gamepad.</summary>
    public enum BindingSlot { Primary = 0, Secondary = 1, Gamepad = 2 }

    public enum ActionGroup { Movement, Actions, Combat, Menus }

    public sealed class ActionInfo
    {
        readonly string[] _defaults;

        public GameAction Action { get; }

        /// <summary>Stable name used in the settings file, e.g. <c>moveForward</c>.</summary>
        public string Id { get; }

        public string DisplayName { get; }
        public ActionGroup Group { get; }

        /// <summary>False for movement: the gamepad moves with the left stick, which cannot be rebound.</summary>
        public bool HasGamepadSlot { get; }

        public ActionInfo(GameAction action, string id, string displayName, ActionGroup group, string primary, string secondary, string gamepad)
        {
            Action = action;
            Id = id;
            DisplayName = displayName;
            Group = group;
            HasGamepadSlot = gamepad != null;
            _defaults = new[] { primary, secondary, gamepad };
        }

        public bool HasSlot(BindingSlot slot) => slot != BindingSlot.Gamepad || HasGamepadSlot;

        /// <summary>The control the action starts with in <paramref name="slot"/>, or null when that slot starts empty.</summary>
        public string DefaultFor(BindingSlot slot) => _defaults[(int)slot];
    }

    /// <summary>
    /// What can be rebound, the default controls, and which controls are allowed in which slot. Control paths are Input System paths
    /// such as <c>&lt;Keyboard&gt;/space</c>. Keyboard and mouse slots take keyboard keys and mouse buttons; the gamepad slot takes gamepad
    /// buttons. A few controls are reserved for the game itself (Esc, Enter, F3 and the gamepad's Start and Select).
    /// </summary>
    public static class ControlCatalog
    {
        /// <summary>How an unbound slot is written in the settings file.</summary>
        public const string Unbound = "none";

        const string Keyboard = "<Keyboard>/";
        const string Mouse = "<Mouse>/";
        const string Gamepad = "<Gamepad>/";

        static readonly ActionInfo[] Table =
        {
            new ActionInfo(GameAction.MoveForward, "moveForward", "Move forward", ActionGroup.Movement, "<Keyboard>/w", "<Keyboard>/upArrow", null),
            new ActionInfo(GameAction.MoveBack, "moveBack", "Move back", ActionGroup.Movement, "<Keyboard>/s", "<Keyboard>/downArrow", null),
            new ActionInfo(GameAction.MoveLeft, "moveLeft", "Move left", ActionGroup.Movement, "<Keyboard>/a", "<Keyboard>/leftArrow", null),
            new ActionInfo(GameAction.MoveRight, "moveRight", "Move right", ActionGroup.Movement, "<Keyboard>/d", "<Keyboard>/rightArrow", null),
            new ActionInfo(GameAction.Jump, "jump", "Jump", ActionGroup.Actions, "<Keyboard>/space", null, "<Gamepad>/buttonSouth"),
            new ActionInfo(GameAction.Sprint, "sprint", "Sprint", ActionGroup.Actions, "<Keyboard>/leftShift", "<Keyboard>/rightShift", "<Gamepad>/leftStickPress"),
            new ActionInfo(GameAction.Crouch, "crouch", "Crouch", ActionGroup.Actions, "<Keyboard>/leftCtrl", "<Keyboard>/c", "<Gamepad>/buttonEast"),
            // the prototype's keys: E talks, breaches and trades; F scans; Tab and J open the inventory and the journal; the mouse and R shoot; 1 to 3 pick a gun
            new ActionInfo(GameAction.Interact, "interact", "Interact", ActionGroup.Actions, "<Keyboard>/e", null, "<Gamepad>/buttonWest"),
            new ActionInfo(GameAction.Scan, "scan", "Scan", ActionGroup.Actions, "<Keyboard>/f", null, "<Gamepad>/leftShoulder"),
            new ActionInfo(GameAction.Inventory, "inventory", "Inventory", ActionGroup.Menus, "<Keyboard>/tab", "<Keyboard>/i", "<Gamepad>/dpad/up"),
            new ActionInfo(GameAction.Journal, "journal", "Journal", ActionGroup.Menus, "<Keyboard>/j", null, "<Gamepad>/dpad/down"),
            new ActionInfo(GameAction.Fire, "fire", "Fire", ActionGroup.Combat, "<Mouse>/leftButton", null, "<Gamepad>/rightTrigger"),
            new ActionInfo(GameAction.Aim, "aim", "Aim", ActionGroup.Combat, "<Mouse>/rightButton", null, "<Gamepad>/leftTrigger"),
            new ActionInfo(GameAction.Reload, "reload", "Reload", ActionGroup.Combat, "<Keyboard>/r", null, "<Gamepad>/buttonNorth"),
            new ActionInfo(GameAction.Weapon1, "weapon1", "Weapon 1", ActionGroup.Combat, "<Keyboard>/digit1", null, "<Gamepad>/dpad/left"),
            new ActionInfo(GameAction.Weapon2, "weapon2", "Weapon 2", ActionGroup.Combat, "<Keyboard>/digit2", null, "<Gamepad>/dpad/right"),
            new ActionInfo(GameAction.Weapon3, "weapon3", "Weapon 3", ActionGroup.Combat, "<Keyboard>/digit3", null, "<Gamepad>/rightShoulder"),
        };

        static readonly HashSet<string> MouseButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "leftButton", "rightButton", "middleButton", "forwardButton", "backButton",
        };

        static readonly HashSet<string> GamepadButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder", "leftTrigger", "rightTrigger",
            "leftStickPress", "rightStickPress", "dpad/up", "dpad/down", "dpad/left", "dpad/right",
        };

        static readonly HashSet<string> ReservedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "escape", "enter", "numpadEnter", "f3" };

        static readonly HashSet<string> ReservedGamepad = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "start", "select" };

        static readonly Regex KeyName = new Regex("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled);

        public static IReadOnlyList<ActionInfo> All => Table;

        public static ActionInfo For(GameAction action)
        {
            int i = (int)action;
            if (i < 0 || i >= Table.Length || Table[i].Action != action) throw new ArgumentOutOfRangeException(nameof(action), action, "unknown action");
            return Table[i];
        }

        public static bool TryFromId(string id, out ActionInfo info)
        {
            foreach (ActionInfo a in Table)
            {
                if (a.Id == id)
                {
                    info = a;
                    return true;
                }
            }
            info = null;
            return false;
        }

        /// <summary>True for the two keyboard/mouse slots; those two share one pool of controls, the gamepad slot has its own.</summary>
        public static bool IsKeyboardMouse(BindingSlot slot) => slot != BindingSlot.Gamepad;

        /// <summary>Null when <paramref name="path"/> may go in <paramref name="slot"/>, otherwise why it may not.</summary>
        public static string Check(BindingSlot slot, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "No control was chosen.";

            if (IsKeyboardMouse(slot))
            {
                if (path.StartsWith(Keyboard, StringComparison.OrdinalIgnoreCase))
                {
                    string key = path.Substring(Keyboard.Length);
                    if (!KeyName.IsMatch(key) || string.Equals(key, "anyKey", StringComparison.OrdinalIgnoreCase)) return $"'{path}' is not a single key.";
                    if (ReservedKeys.Contains(key)) return $"{BindingText.Describe(path)} is reserved by the game and cannot be rebound.";
                    return null;
                }
                if (path.StartsWith(Mouse, StringComparison.OrdinalIgnoreCase))
                {
                    return MouseButtons.Contains(path.Substring(Mouse.Length)) ? null : $"'{path}' is not a mouse button.";
                }
                return "Keyboard and mouse slots take keyboard keys and mouse buttons.";
            }

            if (!path.StartsWith(Gamepad, StringComparison.OrdinalIgnoreCase)) return "The gamepad slot takes gamepad buttons.";
            string button = path.Substring(Gamepad.Length);
            if (ReservedGamepad.Contains(button)) return $"{BindingText.Describe(path)} is reserved by the game and cannot be rebound.";
            return GamepadButtons.Contains(button) ? null : $"'{path}' is not a gamepad button that can be bound.";
        }
    }
}
