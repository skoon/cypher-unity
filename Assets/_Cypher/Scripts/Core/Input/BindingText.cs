using System;
using System.Collections.Generic;
using System.Text;

namespace Cypher.Core
{
    /// <summary>Player-facing names for Input System control paths: <c>&lt;Keyboard&gt;/leftShift</c> is "Left Shift", <c>&lt;Gamepad&gt;/buttonSouth</c> is "A".</summary>
    public static class BindingText
    {
        static readonly Dictionary<string, string> Gamepad = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["buttonSouth"] = "A",
            ["buttonEast"] = "B",
            ["buttonWest"] = "X",
            ["buttonNorth"] = "Y",
            ["leftShoulder"] = "LB",
            ["rightShoulder"] = "RB",
            ["leftTrigger"] = "LT",
            ["rightTrigger"] = "RT",
            ["leftStickPress"] = "L3",
            ["rightStickPress"] = "R3",
            ["start"] = "Start",
            ["select"] = "Select",
            ["dpad/up"] = "D-Pad Up",
            ["dpad/down"] = "D-Pad Down",
            ["dpad/left"] = "D-Pad Left",
            ["dpad/right"] = "D-Pad Right",
        };

        static readonly Dictionary<string, string> Mouse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["leftButton"] = "Left Mouse",
            ["rightButton"] = "Right Mouse",
            ["middleButton"] = "Middle Mouse",
            ["backButton"] = "Mouse 4",
            ["forwardButton"] = "Mouse 5",
        };

        public static string Describe(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path == ControlCatalog.Unbound) return "Unbound";

            int close = path.IndexOf(">/", StringComparison.Ordinal);
            if (!path.StartsWith("<", StringComparison.Ordinal) || close < 0) return path;
            string device = path.Substring(1, close - 1);
            string name = path.Substring(close + 2);

            if (device.Equals("Gamepad", StringComparison.OrdinalIgnoreCase)) return Gamepad.TryGetValue(name, out string g) ? g : Words(name);
            if (device.Equals("Mouse", StringComparison.OrdinalIgnoreCase)) return Mouse.TryGetValue(name, out string m) ? m : Words(name);
            if (device.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)) return Key(name);
            return path;
        }

        static string Key(string name)
        {
            if (name.Length == 1) return name.ToUpperInvariant();
            if (name.StartsWith("digit", StringComparison.OrdinalIgnoreCase) && name.Length == 6) return name.Substring(5);
            return Words(name);
        }

        /// <summary>"leftShift" becomes "Left Shift", "numpad1" becomes "Numpad 1", "f5" becomes "F5".</summary>
        static string Words(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool boundary = i > 0 && ((char.IsUpper(c) && !char.IsUpper(name[i - 1])) || (char.IsDigit(c) && char.IsLetter(name[i - 1]) && name.Length > 2 && !IsFunctionKey(name)));
                if (boundary) sb.Append(' ');
                sb.Append(i == 0 || boundary ? char.ToUpperInvariant(c) : c);
            }
            return sb.ToString();
        }

        static bool IsFunctionKey(string name) => name.Length <= 3 && (name[0] == 'f' || name[0] == 'F') && char.IsDigit(name[1]);
    }
}
