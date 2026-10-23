using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>Small helpers for building the screens.</summary>
    static class Ui
    {
        public static Label Text(string text, params string[] classes)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("cy-text");
            foreach (string c in classes) label.AddToClassList(c);
            return label;
        }

        public static Label Display(string text, params string[] classes)
        {
            Label label = Text(text, classes);
            label.AddToClassList("cy-display");
            return label;
        }

        public static VisualElement Div(params string[] classes)
        {
            var e = new VisualElement();
            foreach (string c in classes) e.AddToClassList(c);
            return e;
        }

        public static void Show(VisualElement e, bool visible)
        {
            e.EnableInClassList("cy-hidden", !visible);
        }

        public static bool IsShown(VisualElement e) => !e.ClassListContains("cy-hidden");
    }

    /// <summary>
    /// Base for everything the player can point at or move onto: it can take focus, selects itself when clicked, and runs
    /// <see cref="OnActivate"/> on a click and on the Submit action (Enter, Space or the gamepad's A button).
    /// </summary>
    public abstract class FocusControl : VisualElement
    {
        protected FocusControl()
        {
            focusable = true;
            tabIndex = 0;
            RegisterCallback<PointerDownEvent>(_ => Focus());
            RegisterCallback<NavigationSubmitEvent>(e =>
            {
                Trigger();
                e.StopPropagation();
            });
            RegisterCallback<ClickEvent>(_ => Trigger());
            // pointing at it, or arrowing onto it, is heard; focus placed by the screen itself is not
            RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (enabledInHierarchy) UiSound.Raise(UiSound.Hover);
            });
            RegisterCallback<FocusInEvent>(e =>
            {
                if (e.direction != FocusChangeDirection.unspecified && e.direction != FocusChangeDirection.none && enabledInHierarchy) UiSound.Raise(UiSound.Hover);
            });
        }

        /// <summary>The sound a press makes; null for a control that makes its own (a slider ticks as it moves).</summary>
        public string ActivateSound { get; set; } = UiSound.Select;

        protected virtual void OnActivate()
        {
        }

        void Trigger()
        {
            if (ActivateSound != null && enabledInHierarchy) UiSound.Raise(ActivateSound);
            OnActivate();
        }

        /// <summary>Do what a click or the Submit action does. Public for the screenshot mode and for tests.</summary>
        public void Activate() => Trigger();
    }

    /// <summary>A menu entry: big text, an optional small note on the right, and an accent bar when focused or hovered.</summary>
    public sealed class MenuButton : FocusControl
    {
        readonly Label _label;
        readonly Label _note;

        public event Action Pressed;

        public MenuButton(string text, string note = null, bool danger = false, bool compact = false)
        {
            AddToClassList("cy-btn");
            if (danger) AddToClassList("cy-btn--danger");
            if (compact) AddToClassList("cy-btn--compact");
            _label = Ui.Display(text);
            _note = Ui.Text(note ?? "", "cy-btn__note");
            Ui.Show(_note, !string.IsNullOrEmpty(note));
            Add(_label);
            Add(_note);
        }

        public string Text
        {
            get => _label.text;
            set => _label.text = value;
        }

        public void SetNote(string note)
        {
            _note.text = note ?? "";
            Ui.Show(_note, !string.IsNullOrEmpty(note));
        }

        protected override void OnActivate()
        {
            if (enabledInHierarchy) Pressed?.Invoke();
        }
    }

    /// <summary>A row of the Options screen: a title with an optional hint on the left and a value control on the right. The whole row takes focus.</summary>
    public abstract class SettingField : FocusControl
    {
        readonly Label _hint;

        protected VisualElement ValueArea { get; }

        protected SettingField(string title, string hint)
        {
            AddToClassList("cy-field");
            ActivateSound = null;   // a field ticks when its value changes, not when it is pressed
            var label = Ui.Div("cy-field__label");
            label.Add(Ui.Display(title, "cy-field__title"));
            _hint = Ui.Text(hint ?? "", "cy-field__hint");
            Ui.Show(_hint, !string.IsNullOrEmpty(hint));
            label.Add(_hint);
            Add(label);
            ValueArea = Ui.Div("cy-field__value");
            Add(ValueArea);
        }

        public void SetHint(string hint)
        {
            _hint.text = hint ?? "";
            Ui.Show(_hint, !string.IsNullOrEmpty(hint));
        }

        /// <summary>Handle a left or right move with <paramref name="step"/> (-1 or +1) and stop it moving focus; up and down are left to the screen.</summary>
        protected void HandleHorizontal(NavigationMoveEvent e, Action<int> step)
        {
            int direction = e.direction == NavigationMoveEvent.Direction.Left ? -1 : e.direction == NavigationMoveEvent.Direction.Right ? 1 : 0;
            if (direction == 0) return;
            step(direction);
            e.PreventDefault();
            e.StopPropagation();
        }
    }

    /// <summary>One value out of a short list: left and right (or a click) step through it and wrap around. No pop-up, so it works the same on every device.</summary>
    public sealed class CycleField : SettingField
    {
        readonly Label _value = Ui.Text("", "cy-value");
        IReadOnlyList<string> _labels = new string[0];

        public event Action<int> Changed;

        public int Index { get; private set; }

        public string ValueText => _value.text;

        public CycleField(string title, string hint = null) : base(title, hint)
        {
            var left = Ui.Text("‹", "cy-arrow");
            var right = Ui.Text("›", "cy-arrow");
            left.pickingMode = right.pickingMode = PickingMode.Position;
            left.RegisterCallback<ClickEvent>(e => { Step(-1); e.StopPropagation(); });
            right.RegisterCallback<ClickEvent>(e => { Step(1); e.StopPropagation(); });
            ValueArea.Add(left);
            ValueArea.Add(_value);
            ValueArea.Add(right);
            RegisterCallback<NavigationMoveEvent>(e => HandleHorizontal(e, Step));
        }

        /// <summary>Replace the list and show item <paramref name="index"/>, without raising <see cref="Changed"/>.</summary>
        public void SetChoices(IReadOnlyList<string> labels, int index)
        {
            if (labels == null || labels.Count == 0) throw new ArgumentException("a cycle needs at least one choice", nameof(labels));
            _labels = labels;
            SetIndex(index);
        }

        public void SetIndex(int index)
        {
            if (index < 0 || index >= _labels.Count) throw new ArgumentOutOfRangeException(nameof(index), index, "no such choice");
            Index = index;
            _value.text = _labels[index];
        }

        public void Step(int direction)
        {
            if (_labels.Count == 0) return;
            SetIndex(OptionChoices.Cycle(_labels.Count, Index, direction));
            UiSound.Raise(UiSound.Tick);
            Changed?.Invoke(Index);
        }

        protected override void OnActivate() => Step(1);
    }

    /// <summary>A number between two limits: drag the bar, or step with left and right.</summary>
    public sealed class SliderField : SettingField
    {
        readonly SliderSpec _spec;
        readonly VisualElement _track = Ui.Div("cy-slider__track");
        readonly VisualElement _fill = Ui.Div("cy-slider__fill");
        readonly Label _value = Ui.Text("", "cy-slider__value");

        public event Action<double> Changed;

        public double Value { get; private set; }

        public string ValueText => _value.text;

        public SliderField(string title, SliderSpec spec, string hint = null) : base(title, hint)
        {
            _spec = spec ?? throw new ArgumentNullException(nameof(spec));
            _track.Add(_fill);
            ValueArea.Add(_track);
            ValueArea.Add(_value);
            SetValue(spec.Min);

            _track.RegisterCallback<PointerDownEvent>(e =>
            {
                _track.CapturePointer(e.pointerId);
                DragTo(e.localPosition.x);
                e.StopPropagation();
            });
            _track.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_track.HasPointerCapture(e.pointerId)) DragTo(e.localPosition.x);
            });
            _track.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_track.HasPointerCapture(e.pointerId)) _track.ReleasePointer(e.pointerId);
            });
            RegisterCallback<NavigationMoveEvent>(e => HandleHorizontal(e, Nudge));
        }

        /// <summary>Show <paramref name="value"/> (snapped to the steps) without raising <see cref="Changed"/>.</summary>
        public void SetValue(double value)
        {
            Value = _spec.Snap(value);
            _fill.style.width = Length.Percent((float)(_spec.Fraction(Value) * 100));
            _value.text = _spec.Format(Value);
        }

        public void Nudge(int direction)
        {
            double next = _spec.Nudge(Value, direction);
            if (next == Value) return;
            SetValue(next);
            UiSound.Raise(UiSound.Tick);
            Changed?.Invoke(Value);
        }

        void DragTo(float x)
        {
            float width = _track.resolvedStyle.width;
            if (!(width > 0)) return;
            double next = _spec.FromFraction(x / width);
            if (next == Value) return;
            SetValue(next);
            UiSound.Raise(UiSound.Tick);
            Changed?.Invoke(Value);
        }
    }

    /// <summary>On or off: Enter or a click flips it, left means off and right means on.</summary>
    public sealed class ToggleField : SettingField
    {
        readonly Label _state = Ui.Text("OFF");

        public event Action<bool> Changed;

        public bool Value { get; private set; }

        public ToggleField(string title, string hint = null) : base(title, hint)
        {
            var pill = Ui.Div("cy-toggle__pill");
            pill.pickingMode = PickingMode.Ignore;
            pill.Add(_state);
            ValueArea.Add(pill);
            AddToClassList("cy-toggle");
            RegisterCallback<NavigationMoveEvent>(e =>
            {
                HandleHorizontal(e, direction =>
                {
                    bool wanted = direction > 0;
                    if (wanted != Value) Set(wanted, notify: true);
                });
            });
        }

        public void SetValue(bool value) => Set(value, notify: false);

        void Set(bool value, bool notify)
        {
            Value = value;
            _state.text = value ? "ON" : "OFF";
            EnableInClassList("cy-toggle--on", value);
            if (notify)
            {
                UiSound.Raise(UiSound.Tick);
                Changed?.Invoke(value);
            }
        }

        protected override void OnActivate() => Set(!Value, notify: true);
    }

    /// <summary>One binding slot on the Controls screen: shows the control in it and opens the key prompt when activated.</summary>
    public sealed class BindButton : FocusControl
    {
        readonly Label _text;

        public GameAction Action { get; }
        public BindingSlot Slot { get; }

        public event Action<GameAction, BindingSlot> Pressed;

        public string Text => _text.text;

        public BindButton(GameAction action, BindingSlot slot)
        {
            Action = action;
            Slot = slot;
            AddToClassList("cy-bind");
            _text = Ui.Text("");
            Add(_text);
        }

        /// <summary>Show <paramref name="path"/> (null for an empty slot); <paramref name="changed"/> marks a control the player chose.</summary>
        public void Show(string path, bool changed)
        {
            _text.text = path == null ? "—" : BindingText.Describe(path);
            EnableInClassList("cy-bind--empty", path == null);
            EnableInClassList("cy-bind--changed", changed && path != null);
        }

        protected override void OnActivate()
        {
            if (enabledInHierarchy) Pressed?.Invoke(Action, Slot);
        }
    }

    /// <summary>
    /// Up, down, left and right across a table of focusable elements, with holes allowed. The movement itself is Core's
    /// <see cref="GridFocus"/>; this finds where focus is and moves it. Listens on <paramref name="scope"/>, so a control that handles a
    /// direction itself (a slider using left and right) stops the event first and the grid never sees it.
    /// </summary>
    public sealed class FocusGrid
    {
        readonly List<List<VisualElement>> _rows = new List<List<VisualElement>>();

        public FocusGrid(VisualElement scope)
        {
            scope.RegisterCallback<NavigationMoveEvent>(OnMove);
        }

        /// <summary>Replace the table. A null cell is a hole.</summary>
        public void SetRows(IEnumerable<IEnumerable<VisualElement>> rows)
        {
            _rows.Clear();
            foreach (IEnumerable<VisualElement> row in rows) _rows.Add(new List<VisualElement>(row));
        }

        public bool FocusFirst()
        {
            GridPosition? first = GridFocus.First(Available());
            if (!first.HasValue) return false;
            _rows[first.Value.Row][first.Value.Column].Focus();
            return true;
        }

        /// <summary>The cell of <paramref name="element"/>, if it is in the table.</summary>
        public bool TryFind(VisualElement element, out GridPosition position)
        {
            for (int r = 0; r < _rows.Count; r++)
            {
                int c = _rows[r].IndexOf(element);
                if (c >= 0)
                {
                    position = new GridPosition(r, c);
                    return true;
                }
            }
            position = default;
            return false;
        }

        List<IReadOnlyList<bool>> Available()
        {
            var table = new List<IReadOnlyList<bool>>();
            foreach (List<VisualElement> row in _rows)
            {
                var cells = new List<bool>();
                foreach (VisualElement e in row) cells.Add(e != null && e.enabledInHierarchy && e.resolvedStyle.display != DisplayStyle.None && e.focusable);
                table.Add(cells);
            }
            return table;
        }

        void OnMove(NavigationMoveEvent e)
        {
            MoveDirection? direction = Map(e.direction);
            if (!direction.HasValue) return;

            VisualElement focused = (e.target as VisualElement)?.panel?.focusController?.focusedElement as VisualElement;
            if (focused == null || !TryFind(focused, out GridPosition from)) return;

            List<IReadOnlyList<bool>> available = Available();
            if (!available[from.Row][from.Column]) return;
            GridPosition to = GridFocus.Move(available, from, direction.Value);
            if (to.Row != from.Row || to.Column != from.Column)
            {
                _rows[to.Row][to.Column].Focus();
                UiSound.Raise(UiSound.Hover);
            }

            e.PreventDefault();   // the grid owns arrow movement here, even at an edge, so focus never jumps out by itself
            e.StopPropagation();
        }

        static MoveDirection? Map(NavigationMoveEvent.Direction d)
        {
            switch (d)
            {
                case NavigationMoveEvent.Direction.Up: return MoveDirection.Up;
                case NavigationMoveEvent.Direction.Down: return MoveDirection.Down;
                case NavigationMoveEvent.Direction.Left: return MoveDirection.Left;
                case NavigationMoveEvent.Direction.Right: return MoveDirection.Right;
                default: return null;
            }
        }
    }
}
