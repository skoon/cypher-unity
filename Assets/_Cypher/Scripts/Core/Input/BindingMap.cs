using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cypher.Core
{
    public enum AssignOutcome
    {
        Applied,

        /// <summary>The slot already had that control.</summary>
        Unchanged,

        /// <summary>Another action already uses that control. Nothing changed; the caller may offer a swap.</summary>
        Conflict,

        /// <summary>Not allowed; <see cref="AssignResult.Message"/> says why. Nothing changed.</summary>
        Rejected,
    }

    public readonly struct AssignResult
    {
        public readonly AssignOutcome Outcome;

        /// <summary>For a <see cref="AssignOutcome.Conflict"/>: who has the control now.</summary>
        public readonly GameAction OtherAction;
        public readonly BindingSlot OtherSlot;

        /// <summary>For a conflict or a rejection, a plain-language sentence for the player; otherwise null.</summary>
        public readonly string Message;

        public AssignResult(AssignOutcome outcome, GameAction otherAction = default, BindingSlot otherSlot = default, string message = null)
        {
            Outcome = outcome;
            OtherAction = otherAction;
            OtherSlot = otherSlot;
            Message = message;
        }

        public bool Succeeded => Outcome == AssignOutcome.Applied || Outcome == AssignOutcome.Unchanged;
    }

    /// <summary>
    /// The controls in force: for every action and slot, the control path or nothing. Starts as the defaults; the player's changes are
    /// stored as a short list of differences (<see cref="ToOverrides"/>) in the settings file. The rules live here, pure and tested:
    /// a control can be used by only one action per kind of device, every action keeps at least one keyboard or mouse control so the
    /// player cannot lock themselves out, and picking a control another action holds is a <see cref="AssignOutcome.Conflict"/> the caller
    /// resolves by swapping (<see cref="SwapWith"/>) or by giving up.
    /// </summary>
    public sealed class BindingMap
    {
        const int Slots = 3;

        readonly string[,] _paths = new string[ControlCatalog.All.Count, Slots];

        BindingMap()
        {
        }

        public static BindingMap Defaults()
        {
            var map = new BindingMap();
            map.SetAllDefaults();
            return map;
        }

        public BindingMap Clone()
        {
            var copy = new BindingMap();
            Array.Copy(_paths, copy._paths, _paths.Length);
            return copy;
        }

        public bool SameValues(BindingMap other)
        {
            if (other == null) return false;
            foreach (ActionInfo a in ControlCatalog.All)
            {
                for (int s = 0; s < Slots; s++)
                {
                    if (!SamePath(_paths[(int)a.Action, s], other._paths[(int)a.Action, s])) return false;
                }
            }
            return true;
        }

        /// <summary>The control in force, or null when the slot is empty (or the action has no such slot).</summary>
        public string Get(GameAction action, BindingSlot slot) => _paths[(int)action, (int)slot];

        public bool IsDefault(GameAction action, BindingSlot slot) => SamePath(Get(action, slot), ControlCatalog.For(action).DefaultFor(slot));

        public bool IsDefault(GameAction action)
        {
            for (int s = 0; s < Slots; s++)
            {
                if (!IsDefault(action, (BindingSlot)s)) return false;
            }
            return true;
        }

        /// <summary>Which action and slot hold <paramref name="path"/> among the slots of the same kind of device as <paramref name="likeSlot"/>.</summary>
        public bool TryFindHolder(BindingSlot likeSlot, string path, out GameAction action, out BindingSlot slot)
        {
            foreach (ActionInfo a in ControlCatalog.All)
            {
                for (int s = 0; s < Slots; s++)
                {
                    if (ControlCatalog.IsKeyboardMouse((BindingSlot)s) != ControlCatalog.IsKeyboardMouse(likeSlot)) continue;
                    if (SamePath(_paths[(int)a.Action, s], path))
                    {
                        action = a.Action;
                        slot = (BindingSlot)s;
                        return true;
                    }
                }
            }
            action = default;
            slot = default;
            return false;
        }

        /// <summary>Put <paramref name="path"/> in a slot, unless another action has it (a conflict) or it is not allowed there.</summary>
        public AssignResult Assign(GameAction action, BindingSlot slot, string path)
        {
            AssignResult? bad = CheckSlot(action, slot, path);
            if (bad.HasValue) return bad.Value;
            if (SamePath(Get(action, slot), path)) return new AssignResult(AssignOutcome.Unchanged);

            if (TryFindHolder(slot, path, out GameAction holder, out BindingSlot holderSlot))
            {
                if (holder == action)
                {
                    return Reject($"{ControlCatalog.For(action).DisplayName} already uses {BindingText.Describe(path)} in its other slot.");
                }
                return new AssignResult(AssignOutcome.Conflict, holder, holderSlot,
                    $"{BindingText.Describe(path)} is already used by {ControlCatalog.For(holder).DisplayName}.");
            }

            _paths[(int)action, (int)slot] = path;
            return new AssignResult(AssignOutcome.Applied);
        }

        /// <summary>
        /// After a <see cref="AssignOutcome.Conflict"/>: give the slot the control and give the other action the control this slot had
        /// (possibly nothing). Refused, with nothing changed, if that would leave the other action without any keyboard or mouse control.
        /// </summary>
        public AssignResult SwapWith(GameAction action, BindingSlot slot, string path)
        {
            AssignResult? bad = CheckSlot(action, slot, path);
            if (bad.HasValue) return bad.Value;
            if (!TryFindHolder(slot, path, out GameAction holder, out BindingSlot holderSlot) || (holder == action && holderSlot == slot))
            {
                return Assign(action, slot, path);   // nothing to swap with
            }
            if (holder == action) return Reject($"{ControlCatalog.For(action).DisplayName} already uses {BindingText.Describe(path)} in its other slot.");

            BindingMap trial = Clone();
            trial._paths[(int)action, (int)slot] = path;
            trial._paths[(int)holder, (int)holderSlot] = Get(action, slot);
            string problem = trial.Validate();
            if (problem != null)
            {
                return Reject($"Swapping would leave {ControlCatalog.For(holder).DisplayName} without a usable control ({problem}). Pick a different control.");
            }

            Array.Copy(trial._paths, _paths, _paths.Length);
            return new AssignResult(AssignOutcome.Applied);
        }

        /// <summary>Empty a slot. Refused for the last keyboard or mouse control of an action.</summary>
        public AssignResult Unbind(GameAction action, BindingSlot slot)
        {
            if (!ControlCatalog.For(action).HasSlot(slot)) return Reject($"{ControlCatalog.For(action).DisplayName} has no {slot} slot.");
            if (Get(action, slot) == null) return new AssignResult(AssignOutcome.Unchanged);

            BindingMap trial = Clone();
            trial._paths[(int)action, (int)slot] = null;
            string problem = trial.Validate();
            if (problem != null) return Reject($"{ControlCatalog.For(action).DisplayName} needs at least one keyboard or mouse control.");

            _paths[(int)action, (int)slot] = null;
            return new AssignResult(AssignOutcome.Applied);
        }

        /// <summary>
        /// Put one action back to its defaults. If another action has taken one of those controls it takes this action's current one
        /// in exchange. All or nothing: if that cannot be done cleanly, nothing changes and the result says why.
        /// </summary>
        public AssignResult Reset(GameAction action)
        {
            ActionInfo info = ControlCatalog.For(action);
            BindingMap trial = Clone();
            for (int s = 0; s < Slots; s++)
            {
                var slot = (BindingSlot)s;
                if (!info.HasSlot(slot)) continue;
                string current = Get(action, slot);
                string def = info.DefaultFor(slot);
                if (SamePath(current, def)) continue;

                // Another action sitting on the default control gets what this slot had; this action's own other slot is just overwritten.
                if (def != null && trial.TryFindHolder(slot, def, out GameAction holder, out BindingSlot holderSlot) && holder != action)
                {
                    trial._paths[(int)holder, (int)holderSlot] = current;
                }
                trial._paths[(int)action, s] = def;
            }

            string problem = trial.Validate();
            if (problem != null) return Reject($"Could not reset {info.DisplayName}: {problem}.");
            Array.Copy(trial._paths, _paths, _paths.Length);
            return new AssignResult(AssignOutcome.Applied);
        }

        public void ResetAll() => SetAllDefaults();

        /// <summary>The differences from the defaults, as <c>action:slot</c> keys (for example <c>jump:0</c>) to a control path or <see cref="ControlCatalog.Unbound"/>.</summary>
        public IReadOnlyDictionary<string, string> ToOverrides()
        {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (ActionInfo a in ControlCatalog.All)
            {
                for (int s = 0; s < Slots; s++)
                {
                    var slot = (BindingSlot)s;
                    if (!a.HasSlot(slot) || IsDefault(a.Action, slot)) continue;
                    result[$"{a.Id}:{s.ToString(CultureInfo.InvariantCulture)}"] = Get(a.Action, slot) ?? ControlCatalog.Unbound;
                }
            }
            return result;
        }

        /// <summary>
        /// Rebuild a map from stored differences. An entry that makes no sense (unknown action or slot, a control not allowed there) is
        /// left out and named in <paramref name="problems"/>. If what remains contradicts itself, for example two actions on one key,
        /// the whole map falls back to the defaults and says so; it is never silently repaired.
        /// </summary>
        public static BindingMap FromOverrides(IReadOnlyDictionary<string, string> overrides, out IReadOnlyList<string> problems)
        {
            if (overrides == null) throw new ArgumentNullException(nameof(overrides));
            var found = new List<string>();
            BindingMap map = Defaults();

            foreach (KeyValuePair<string, string> kv in overrides)
            {
                int colon = kv.Key.LastIndexOf(':');
                if (colon <= 0 || !ControlCatalog.TryFromId(kv.Key.Substring(0, colon), out ActionInfo info)
                    || !int.TryParse(kv.Key.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int slotIndex)
                    || slotIndex >= Slots || !info.HasSlot((BindingSlot)slotIndex))
                {
                    found.Add($"'{kv.Key}' is not a binding this version knows");
                    continue;
                }

                var slot = (BindingSlot)slotIndex;
                if (kv.Value == ControlCatalog.Unbound)
                {
                    map._paths[(int)info.Action, slotIndex] = null;
                    continue;
                }
                string why = ControlCatalog.Check(slot, kv.Value);
                if (why != null)
                {
                    found.Add($"{info.DisplayName} ({slot}): {why}");
                    continue;
                }
                map._paths[(int)info.Action, slotIndex] = kv.Value;
            }

            string contradiction = map.Validate();
            if (contradiction != null)
            {
                found.Add($"the stored controls contradict each other ({contradiction}), so every control was reset to its default");
                map = Defaults();
            }
            problems = found;
            return map;
        }

        /// <summary>Null when the map is consistent, otherwise the first thing wrong with it.</summary>
        public string Validate()
        {
            var seenKeyboard = new Dictionary<string, GameAction>(StringComparer.OrdinalIgnoreCase);
            var seenGamepad = new Dictionary<string, GameAction>(StringComparer.OrdinalIgnoreCase);

            foreach (ActionInfo a in ControlCatalog.All)
            {
                bool hasKeyboard = false;
                for (int s = 0; s < Slots; s++)
                {
                    var slot = (BindingSlot)s;
                    string path = _paths[(int)a.Action, s];
                    if (path == null) continue;
                    if (!a.HasSlot(slot)) return $"{a.DisplayName} has no {slot} slot";
                    string why = ControlCatalog.Check(slot, path);
                    if (why != null) return why;

                    Dictionary<string, GameAction> seen = ControlCatalog.IsKeyboardMouse(slot) ? seenKeyboard : seenGamepad;
                    if (seen.TryGetValue(path, out GameAction other)) return $"{BindingText.Describe(path)} is used by both {ControlCatalog.For(other).DisplayName} and {a.DisplayName}";
                    seen[path] = a.Action;
                    if (ControlCatalog.IsKeyboardMouse(slot)) hasKeyboard = true;
                }
                if (!hasKeyboard) return $"{a.DisplayName} has no keyboard or mouse control";
            }
            return null;
        }

        void SetAllDefaults()
        {
            foreach (ActionInfo a in ControlCatalog.All)
            {
                for (int s = 0; s < Slots; s++) _paths[(int)a.Action, s] = a.DefaultFor((BindingSlot)s);
            }
        }

        static AssignResult? CheckSlot(GameAction action, BindingSlot slot, string path)
        {
            ActionInfo info = ControlCatalog.For(action);
            if (!info.HasSlot(slot)) return Reject($"{info.DisplayName} has no {slot} slot.");
            string why = ControlCatalog.Check(slot, path);
            return why == null ? (AssignResult?)null : Reject(why);
        }

        static AssignResult Reject(string message) => new AssignResult(AssignOutcome.Rejected, message: message);

        static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
