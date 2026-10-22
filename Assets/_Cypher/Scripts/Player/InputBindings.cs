using System;
using System.Collections.Generic;
using Cypher.Core;

namespace Cypher.Player
{
    /// <summary>
    /// The controls in force, shared between whatever reads input (<see cref="InputSystemCommandSource"/>), whatever edits it (the
    /// Controls options) and whatever saves it (the settings). All the rules are Core's <see cref="BindingMap"/>; this adds the
    /// <see cref="Changed"/> event, which fires after every change that took effect so readers rebuild and the settings are saved.
    /// </summary>
    public sealed class InputBindings
    {
        BindingMap _map = BindingMap.Defaults();

        /// <summary>Raised after the controls changed: assigned, swapped, unbound, reset or loaded.</summary>
        public event Action Changed;

        public string Get(GameAction action, BindingSlot slot) => _map.Get(action, slot);

        public bool IsDefault(GameAction action) => _map.IsDefault(action);

        /// <summary>A copy of the controls in force.</summary>
        public BindingMap Snapshot() => _map.Clone();

        public AssignResult Assign(GameAction action, BindingSlot slot, string path) => Notify(_map.Assign(action, slot, path));

        /// <summary>After a conflict: take the control and give the other action the one this slot had.</summary>
        public AssignResult SwapWith(GameAction action, BindingSlot slot, string path) => Notify(_map.SwapWith(action, slot, path));

        public AssignResult Unbind(GameAction action, BindingSlot slot) => Notify(_map.Unbind(action, slot));

        public AssignResult Reset(GameAction action) => Notify(_map.Reset(action));

        public void ResetAll()
        {
            _map.ResetAll();
            Changed?.Invoke();
        }

        /// <summary>
        /// Replace the controls with the ones stored in the settings. Entries that make no sense are left out; the returned list
        /// names them so the player can be told. Never throws for the content of a settings file.
        /// </summary>
        public IReadOnlyList<string> Load(ControlsOptions controls)
        {
            if (controls == null) throw new ArgumentNullException(nameof(controls));
            _map = BindingMap.FromOverrides(controls.Overrides, out IReadOnlyList<string> problems);
            Changed?.Invoke();
            return problems;
        }

        /// <summary>Put the controls in force into the settings, as differences from the defaults.</summary>
        public void WriteTo(ControlsOptions controls)
        {
            if (controls == null) throw new ArgumentNullException(nameof(controls));
            controls.ResetToDefaults();
            foreach (KeyValuePair<string, string> kv in _map.ToOverrides()) controls.SetOverride(kv.Key, kv.Value);
        }

        AssignResult Notify(AssignResult result)
        {
            if (result.Outcome == AssignOutcome.Applied) Changed?.Invoke();
            return result;
        }
    }
}
