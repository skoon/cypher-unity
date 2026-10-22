using System;
using System.Collections.Generic;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>One page of the Options screen: its rows, how to fill them from the settings, and how to reset them.</summary>
    abstract class OptionsTab
    {
        protected readonly Action<Action<GameSettings>> Change;

        public VisualElement Content { get; } = Ui.Div("cy-content");

        public abstract string Title { get; }

        public abstract string ResetLabel { get; }

        protected OptionsTab(Action<Action<GameSettings>> change)
        {
            Change = change;
        }

        /// <summary>Show the current values without raising any change.</summary>
        public abstract void Sync(GameSettings settings);

        /// <summary>Put this page's settings back to their defaults.</summary>
        public abstract void Reset(GameSettings settings);

        /// <summary>The rows of focusable elements on this page, null for a hole, in reading order.</summary>
        public abstract IEnumerable<IEnumerable<VisualElement>> FocusRows();

        protected static IEnumerable<IEnumerable<VisualElement>> Single(params VisualElement[] fields)
        {
            foreach (VisualElement f in fields) yield return new[] { f };
        }
    }

    sealed class GraphicsTab : OptionsTab
    {
        readonly CycleField _quality = new CycleField("QUALITY");
        readonly ToggleField _adaptive = new ToggleField("ADAPTIVE QUALITY", "Lowers the quality by itself when frames drop. It never raises it again.");
        readonly CycleField _resolution = new CycleField("RESOLUTION", "The height the city is drawn at before it is scaled up. Auto follows the quality.");
        readonly CycleField _window = new CycleField("WINDOW");
        readonly ToggleField _vsync = new ToggleField("VSYNC");
        readonly SliderField _fov = new SliderField("FIELD OF VIEW", OptionChoices.FieldOfView);
        readonly ToggleField _weather = new ToggleField("WEATHER", "Rain and weather effects. The streets stay wet either way.");
        readonly ToggleField _overlay = new ToggleField("PERFORMANCE OVERLAY", "Frame rate, frame time, draw calls and triangles. F3 shows and hides it in game.");

        public CycleField Quality => _quality;
        public ToggleField Adaptive => _adaptive;
        public CycleField Resolution => _resolution;
        public CycleField Window => _window;
        public ToggleField VSync => _vsync;
        public SliderField FieldOfView => _fov;
        public ToggleField Weather => _weather;
        public ToggleField Overlay => _overlay;

        public override string Title => "GRAPHICS";
        public override string ResetLabel => "RESET GRAPHICS";

        public GraphicsTab(Action<Action<GameSettings>> change) : base(change)
        {
            foreach (SettingField f in new SettingField[] { _quality, _adaptive, _resolution, _window, _vsync, _fov, _weather, _overlay }) Content.Add(f);

            _quality.Changed += i => Change(s =>
            {
                s.Graphics.Quality = OptionChoices.Presets[i];
                _quality.SetHint(OptionChoices.PresetSummary(s.Graphics.Quality));
            });
            _adaptive.Changed += v => Change(s => s.Graphics.AdaptiveQuality = v);
            _resolution.Changed += i => Change(s =>
            {
                s.Graphics.InternalHeight = OptionChoices.ResolutionHeights(s.Graphics.InternalHeight)[i];
                ShowResolution(s.Graphics);   // the list can lose or gain a custom entry, so its indexes change
            });
            _window.Changed += i => Change(s => s.Graphics.Window = OptionChoices.Windows[i]);
            _vsync.Changed += v => Change(s => s.Graphics.VSync = v);
            _fov.Changed += v => Change(s => s.Graphics.FieldOfView = v);
            _weather.Changed += v => Change(s => s.Graphics.Weather = v);
            _overlay.Changed += v => Change(s => s.Graphics.ShowPerfOverlay = v);
        }

        public override void Sync(GameSettings settings)
        {
            GraphicsOptions g = settings.Graphics;
            var presets = new List<string>();
            foreach (QualityPreset p in OptionChoices.Presets) presets.Add(OptionChoices.PresetLabel(p));
            _quality.SetChoices(presets, (int)g.Quality);
            _quality.SetHint(OptionChoices.PresetSummary(g.Quality));
            _adaptive.SetValue(g.AdaptiveQuality);

            ShowResolution(g);

            var windows = new List<string>();
            foreach (WindowMode m in OptionChoices.Windows) windows.Add(OptionChoices.WindowLabel(m));
            _window.SetChoices(windows, (int)g.Window);
            _vsync.SetValue(g.VSync);
            _fov.SetValue(g.FieldOfView);
            _weather.SetValue(g.Weather);
            _overlay.SetValue(g.ShowPerfOverlay);
        }

        void ShowResolution(GraphicsOptions g)
        {
            IReadOnlyList<int> heights = OptionChoices.ResolutionHeights(g.InternalHeight);
            var labels = new List<string>();
            int index = 0;
            for (int i = 0; i < heights.Count; i++)
            {
                labels.Add(OptionChoices.ResolutionLabel(heights[i]));
                if (heights[i] == g.InternalHeight) index = i;
            }
            _resolution.SetChoices(labels, index);
        }

        public override void Reset(GameSettings settings) => settings.Graphics.ResetToDefaults();

        public override IEnumerable<IEnumerable<VisualElement>> FocusRows() => Single(_quality, _adaptive, _resolution, _window, _vsync, _fov, _weather, _overlay);
    }

    sealed class AudioTab : OptionsTab
    {
        readonly SliderField _master = new SliderField("MASTER", OptionChoices.Volume);
        readonly SliderField _music = new SliderField("MUSIC", OptionChoices.Volume);
        readonly SliderField _sfx = new SliderField("EFFECTS", OptionChoices.Volume);
        readonly SliderField _ambience = new SliderField("AMBIENCE", OptionChoices.Volume);
        readonly SliderField _ui = new SliderField("INTERFACE", OptionChoices.Volume);
        readonly SliderField _voice = new SliderField("VOICES", OptionChoices.Volume);

        public SliderField Master => _master;
        public SliderField Music => _music;

        public override string Title => "AUDIO";
        public override string ResetLabel => "RESET AUDIO";

        public AudioTab(Action<Action<GameSettings>> change) : base(change)
        {
            Content.Add(Ui.Text("There is no sound in this build yet. These levels are saved and will apply when it arrives.", "cy-note"));
            foreach (SliderField f in new[] { _master, _music, _sfx, _ambience, _ui, _voice }) Content.Add(f);

            _master.Changed += v => Change(s => s.Audio.Master = v);
            _music.Changed += v => Change(s => s.Audio.Music = v);
            _sfx.Changed += v => Change(s => s.Audio.Sfx = v);
            _ambience.Changed += v => Change(s => s.Audio.Ambience = v);
            _ui.Changed += v => Change(s => s.Audio.Ui = v);
            _voice.Changed += v => Change(s => s.Audio.Voice = v);
        }

        public override void Sync(GameSettings settings)
        {
            AudioOptions a = settings.Audio;
            _master.SetValue(a.Master);
            _music.SetValue(a.Music);
            _sfx.SetValue(a.Sfx);
            _ambience.SetValue(a.Ambience);
            _ui.SetValue(a.Ui);
            _voice.SetValue(a.Voice);
        }

        public override void Reset(GameSettings settings) => settings.Audio.ResetToDefaults();

        public override IEnumerable<IEnumerable<VisualElement>> FocusRows() => Single(_master, _music, _sfx, _ambience, _ui, _voice);
    }

    sealed class GameplayTab : OptionsTab
    {
        readonly SliderField _sensitivity = new SliderField("MOUSE SENSITIVITY", OptionChoices.Sensitivity, "How fast the view turns with the mouse. The gamepad's right stick follows it too.");
        readonly ToggleField _invertY = new ToggleField("INVERT VERTICAL LOOK", "Pushing the mouse or stick up looks down.");

        public SliderField Sensitivity => _sensitivity;
        public ToggleField InvertY => _invertY;

        public override string Title => "GAMEPLAY";
        public override string ResetLabel => "RESET GAMEPLAY";

        public GameplayTab(Action<Action<GameSettings>> change) : base(change)
        {
            Content.Add(_sensitivity);
            Content.Add(_invertY);
            _sensitivity.Changed += v => Change(s => s.Gameplay.Sensitivity = v);
            _invertY.Changed += v => Change(s => s.Gameplay.InvertY = v);
        }

        public override void Sync(GameSettings settings)
        {
            _sensitivity.SetValue(settings.Gameplay.Sensitivity);
            _invertY.SetValue(settings.Gameplay.InvertY);
        }

        public override void Reset(GameSettings settings) => settings.Gameplay.ResetToDefaults();

        public override IEnumerable<IEnumerable<VisualElement>> FocusRows() => Single(_sensitivity, _invertY);
    }

    /// <summary>
    /// The Controls page: a table with a row per action and a button for each binding slot, then a Reset button for the row. The
    /// buttons only ask for a rebind (<see cref="RebindRequested"/>); the key prompt, the conflict question and the saving are the root's.
    /// </summary>
    sealed class ControlsTab : OptionsTab
    {
        sealed class Row
        {
            public ActionInfo Info;
            public readonly BindButton[] Buttons = new BindButton[3];
            public MenuButton Reset;
        }

        readonly InputBindings _bindings;
        readonly List<Row> _rows = new List<Row>();

        public event Action<GameAction, BindingSlot> RebindRequested;

        public override string Title => "CONTROLS";
        public override string ResetLabel => "RESET ALL CONTROLS";

        public ControlsTab(InputBindings bindings) : base(null)
        {
            _bindings = bindings;

            var head = Ui.Div("cy-bindrow", "cy-bindrow--head");
            head.Add(Ui.Text("ACTION", "cy-bindrow__name"));
            foreach (string title in new[] { "KEYBOARD / MOUSE", "ALTERNATE", "GAMEPAD" })
            {
                var cell = Ui.Div("cy-bindrow__cell");
                cell.Add(Ui.Text(title));
                head.Add(cell);
            }
            Content.Add(head);

            foreach (ActionInfo info in ControlCatalog.All)
            {
                var row = new Row { Info = info };
                var line = Ui.Div("cy-bindrow");
                var name = Ui.Div("cy-bindrow__name");
                name.Add(Ui.Display(info.DisplayName.ToUpperInvariant()));
                line.Add(name);

                foreach (BindingSlot slot in new[] { BindingSlot.Primary, BindingSlot.Secondary, BindingSlot.Gamepad })
                {
                    var cell = Ui.Div("cy-bindrow__cell");
                    if (info.HasSlot(slot))
                    {
                        var button = new BindButton(info.Action, slot);
                        button.Pressed += (a, s) => RebindRequested?.Invoke(a, s);
                        row.Buttons[(int)slot] = button;
                        cell.Add(button);
                    }
                    else
                    {
                        var none = Ui.Div("cy-bind", "cy-bind--none");
                        none.pickingMode = PickingMode.Ignore;
                        none.Add(Ui.Text("—"));
                        cell.Add(none);
                    }
                    line.Add(cell);
                }

                row.Reset = new MenuButton("RESET", compact: true);
                GameAction action = info.Action;
                row.Reset.Pressed += () => _bindings.Reset(action);
                line.Add(row.Reset);
                _rows.Add(row);
                Content.Add(line);
            }
        }

        /// <summary>The binding buttons of an action, for tests: primary, alternate, gamepad (null where the action has none).</summary>
        public BindButton Button(GameAction action, BindingSlot slot) => _rows[(int)action].Buttons[(int)slot];

        public MenuButton ResetButton(GameAction action) => _rows[(int)action].Reset;

        public override void Sync(GameSettings settings) => Refresh();

        /// <summary>Redraw every binding from <see cref="InputBindings"/>.</summary>
        public void Refresh()
        {
            BindingMap map = _bindings.Snapshot();
            foreach (Row row in _rows)
            {
                foreach (BindingSlot slot in new[] { BindingSlot.Primary, BindingSlot.Secondary, BindingSlot.Gamepad })
                {
                    BindButton button = row.Buttons[(int)slot];
                    if (button != null) button.Show(map.Get(row.Info.Action, slot), changed: !map.IsDefault(row.Info.Action, slot));
                }
                row.Reset.SetEnabled(!map.IsDefault(row.Info.Action));
            }
        }

        public override void Reset(GameSettings settings) => _bindings.ResetAll();

        public override IEnumerable<IEnumerable<VisualElement>> FocusRows()
        {
            foreach (Row row in _rows) yield return new VisualElement[] { row.Buttons[0], row.Buttons[1], row.Buttons[2], row.Reset };
        }
    }

    /// <summary>
    /// The Options screen: four pages (Graphics, Audio, Gameplay, Controls) switched with the tab buttons, the shoulder buttons or Page
    /// Up and Down. Changes take effect at once through <see cref="SettingsService.Apply"/> and are saved a moment after the last one,
    /// or immediately on Back. Controls are the exception to the saving rule: they save the instant they change, in the service.
    /// </summary>
    public sealed class OptionsScreen : MenuScreen
    {
        /// <summary>How long after the last change the settings are written.</summary>
        public const long SaveDelayMilliseconds = 600;

        readonly SettingsService _settings;
        readonly List<OptionsTab> _tabs = new List<OptionsTab>();
        readonly List<Label> _tabLabels = new List<Label>();
        readonly List<VisualElement> _tabButtons = new List<VisualElement>();
        readonly ScrollView _scroll = new ScrollView(ScrollViewMode.Vertical);
        readonly MenuButton _reset = new MenuButton("RESET", compact: true);
        readonly MenuButton _back = new MenuButton("BACK", compact: true);
        readonly FocusGrid _grid;
        IVisualElementScheduledItem _saveTimer;
        bool _dirty;

        public int ActiveTab { get; private set; }

        internal GraphicsTab Graphics { get; }
        internal AudioTab Audio { get; }
        internal GameplayTab Gameplay { get; }
        internal ControlsTab Controls { get; }

        public MenuButton ResetButton => _reset;
        public MenuButton BackButton => _back;

        public string ActiveTitle => _tabs[ActiveTab].Title;

        public bool HasUnsavedChanges => _dirty;

        public event Action BackRequested;
        public event Action<GameAction, BindingSlot> RebindRequested;

        public OptionsScreen(SettingsService settings)
        {
            _settings = settings;
            Graphics = new GraphicsTab(Change);
            Audio = new AudioTab(Change);
            Gameplay = new GameplayTab(Change);
            Controls = new ControlsTab(settings.Bindings);
            _tabs.AddRange(new OptionsTab[] { Graphics, Audio, Gameplay, Controls });
            Controls.RebindRequested += (a, s) => RebindRequested?.Invoke(a, s);

            Root = Ui.Div("cy-screen", "cy-scrim");
            var panel = Ui.Div("cy-panel", "cy-panel--options");
            panel.Add(Ui.Display("OPTIONS", "cy-heading"));

            var tabBar = Ui.Div("cy-tabs");
            for (int i = 0; i < _tabs.Count; i++)
            {
                int index = i;
                var tab = Ui.Div("cy-tab");
                var label = Ui.Display(_tabs[i].Title);
                tab.Add(label);
                tab.RegisterCallback<ClickEvent>(_ => SelectTab(index));
                _tabButtons.Add(tab);
                _tabLabels.Add(label);
                tabBar.Add(tab);
            }
            tabBar.Add(Ui.Text("PAGE UP / PAGE DOWN  OR  LB / RB  SWITCH PAGE", "cy-keys-hint"));
            panel.Add(tabBar);

            _scroll.AddToClassList("cy-scroll");
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            panel.Add(_scroll);

            var footer = Ui.Div("cy-footer");
            footer.Add(_reset);
            footer.Add(_back);
            panel.Add(footer);
            Root.Add(panel);

            _grid = new FocusGrid(Root);
            _reset.Pressed += ResetActiveTab;
            _back.Pressed += () => BackRequested?.Invoke();
            Sync();
            SelectTab(0);
        }

        /// <summary>Show the current settings on every page and go back to the first one.</summary>
        public void Open()
        {
            Sync();
            SelectTab(0);
        }

        public void SwitchTab(int direction) => SelectTab(OptionChoices.Cycle(_tabs.Count, ActiveTab, direction));

        public void SelectTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) throw new ArgumentOutOfRangeException(nameof(index), index, "no such page");
            ActiveTab = index;
            for (int i = 0; i < _tabs.Count; i++) _tabButtons[i].EnableInClassList("cy-tab--active", i == index);

            _scroll.Clear();
            _scroll.Add(_tabs[index].Content);
            _scroll.scrollOffset = UnityEngine.Vector2.zero;
            _reset.Text = _tabs[index].ResetLabel;

            var rows = new List<IEnumerable<VisualElement>>(_tabs[index].FocusRows());
            rows.Add(new VisualElement[] { _reset, _back });
            _grid.SetRows(rows);
            Remembered = null;
        }

        /// <summary>Redraw the Controls page after a rebind.</summary>
        public void RefreshControls() => Controls.Refresh();

        /// <summary>Show the current settings on every page, without changing anything.</summary>
        public void Sync()
        {
            GameSettings current = _settings.Current;
            foreach (OptionsTab tab in _tabs) tab.Sync(current);
        }

        /// <summary>Write any pending change now instead of waiting for the timer.</summary>
        public void Flush()
        {
            _saveTimer?.Pause();
            if (!_dirty) return;
            _dirty = false;
            _settings.Save();
        }

        public override void FocusDefault() => _grid.FocusFirst();

        /// <summary>The element to focus first on the active page, for tests.</summary>
        public bool FocusFirstOfPage() => _grid.FocusFirst();

        void Change(Action<GameSettings> edit)
        {
            GameSettings next = _settings.Current;
            edit(next);
            _settings.Apply(next);
            _dirty = true;
            if (_saveTimer == null) _saveTimer = Root.schedule.Execute(Flush);
            _saveTimer.ExecuteLater(SaveDelayMilliseconds);
        }

        void ResetActiveTab()
        {
            GameSettings next = _settings.Current;
            _tabs[ActiveTab].Reset(next);
            _settings.Apply(next);
            _dirty = true;
            Flush();
            Sync();
        }
    }
}
