using System;
using System.Collections.Generic;
using System.Globalization;
using Cypher.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>A texture made in code so the menus need no image files: opaque on the left, fading to clear on the right.</summary>
    static class UiTextures
    {
        static Texture2D _fade;

        /// <summary>Opacity of the title fade at <paramref name="t"/> (0 = left edge, 1 = right edge): solid behind the whole menu column, then a soft edge.</summary>
        internal static float FadeAlpha(float t) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, t));

        public static Texture2D HorizontalFade()
        {
            if (_fade != null) return _fade;
            const int width = 256;
            var pixels = new Color32[width];
            for (int x = 0; x < width; x++)
            {
                float t = (float)x / (width - 1);
                float alpha = FadeAlpha(t);
                pixels[x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
            _fade = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "CypherUI fade" };
            _fade.SetPixels32(pixels);
            _fade.Apply(false, true);
            return _fade;
        }
    }

    /// <summary>One screen of the menus. Knows how to build itself and which element should have focus when it appears.</summary>
    public abstract class MenuScreen
    {
        public VisualElement Root { get; protected set; }

        /// <summary>The element that had focus when the screen was last left, so coming back puts the player where they were.</summary>
        public VisualElement Remembered { get; set; }

        public bool IsShown => Ui.IsShown(Root);

        public void SetShown(bool shown) => Ui.Show(Root, shown);

        /// <summary>Focus the default element.</summary>
        public abstract void FocusDefault();

        /// <summary>Focus the remembered element if it is still usable, otherwise the default one.</summary>
        public void FocusRemembered()
        {
            if (Remembered != null && Remembered.panel != null && Remembered.enabledInHierarchy && Remembered.resolvedStyle.display != DisplayStyle.None) Remembered.Focus();
            else FocusDefault();
        }
    }

    /// <summary>The loading bar over a black screen. It fades out when the game is ready and then gets out of the way.</summary>
    public sealed class LoadingScreen : MenuScreen
    {
        readonly VisualElement _fill = Ui.Div("cy-bar__fill");
        readonly Label _status = Ui.Text("", "cy-status");
        bool _leaving;

        public double Progress { get; private set; }

        public string Status => _status.text;

        public bool IsLeaving => _leaving;

        public LoadingScreen()
        {
            Root = Ui.Div("cy-screen", "cy-loading");
            var logo = Ui.Div("cy-logo-row");
            logo.Add(Ui.Display("CY", "cy-logo"));
            logo.Add(Ui.Display("PHER", "cy-logo", "cy-logo--accent"));
            Root.Add(logo);
            var bar = Ui.Div("cy-bar");
            bar.Add(_fill);
            Root.Add(bar);
            Root.Add(_status);
        }

        public void Show()
        {
            _leaving = false;
            Root.RemoveFromClassList("cy-loading--leaving");
            SetShown(true);
        }

        public void SetProgress(double progress, string status)
        {
            Progress = Math.Max(0, Math.Min(1, progress));
            _fill.style.width = Length.Percent((float)(Progress * 100));
            _status.text = string.Format(CultureInfo.InvariantCulture, "{0}  {1:0}%", status.ToUpperInvariant(), Progress * 100);
        }

        /// <summary>Fade out, then stop being drawn at all.</summary>
        public void Leave()
        {
            if (_leaving || !IsShown) return;
            _leaving = true;
            Root.AddToClassList("cy-loading--leaving");
            Root.schedule.Execute(() =>
            {
                if (_leaving) SetShown(false);
            }).StartingIn(700);
        }

        public override void FocusDefault()
        {
        }
    }

    /// <summary>A vertical list of <see cref="MenuButton"/>s with up and down moving between the enabled ones.</summary>
    public abstract class ButtonListScreen : MenuScreen
    {
        protected FocusGrid Grid;
        protected readonly List<MenuButton> Buttons = new List<MenuButton>();

        protected MenuButton AddButton(VisualElement parent, MenuButton button)
        {
            Buttons.Add(button);
            parent.Add(button);
            return button;
        }

        protected void BuildGrid(VisualElement scope)
        {
            Grid = new FocusGrid(scope);
            var rows = new List<IEnumerable<VisualElement>>();
            foreach (MenuButton b in Buttons) rows.Add(new VisualElement[] { b });
            Grid.SetRows(rows);
        }

        public override void FocusDefault() => Grid.FocusFirst();
    }

    /// <summary>The title screen: the logo, the district, and the main menu, on the left over the city's flyover.</summary>
    public sealed class TitleScreen : ButtonListScreen
    {
        public MenuButton NewGame { get; }
        public MenuButton Continue { get; }
        public MenuButton Options { get; }
        public MenuButton Quit { get; }

        public event Action NewGameRequested;
        public event Action OptionsRequested;
        public event Action QuitRequested;

        public TitleScreen(string version)
        {
            Root = Ui.Div("cy-screen", "cy-title");

            var scrim = Ui.Div("cy-title__scrim");
            scrim.pickingMode = PickingMode.Ignore;
            scrim.style.backgroundImage = new StyleBackground(UiTextures.HorizontalFade());
            scrim.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
            Root.Add(scrim);

            var column = Ui.Div("cy-title__column");
            var logo = Ui.Div("cy-logo-row");
            logo.Add(Ui.Display("CY", "cy-logo"));
            logo.Add(Ui.Display("PHER", "cy-logo", "cy-logo--accent"));
            column.Add(logo);
            column.Add(Ui.Display("KABUKI // NIGHT SECTOR", "cy-subtitle"));

            var menu = Ui.Div("cy-menu");
            NewGame = AddButton(menu, new MenuButton("NEW GAME"));
            Continue = AddButton(menu, new MenuButton("CONTINUE", "NO SAVE YET"));
            Continue.SetEnabled(false);
            Options = AddButton(menu, new MenuButton("OPTIONS"));
            Quit = AddButton(menu, new MenuButton("QUIT", danger: true));
            column.Add(menu);
            Root.Add(column);

            Root.Add(Ui.Text(version, "cy-title__foot"));
            Root.Add(Ui.Text("↑ ↓  SELECT      ENTER  CONFIRM      ESC  BACK", "cy-title__keys"));

            NewGame.Pressed += () => NewGameRequested?.Invoke();
            Options.Pressed += () => OptionsRequested?.Invoke();
            Quit.Pressed += () => QuitRequested?.Invoke();
            BuildGrid(Root);
        }
    }

    /// <summary>The pause menu: Resume, Options, Return to Title, Quit.</summary>
    public sealed class PauseScreen : ButtonListScreen
    {
        public MenuButton Resume { get; }
        public MenuButton Options { get; }
        public MenuButton ReturnToTitle { get; }
        public MenuButton Quit { get; }

        public event Action ResumeRequested;
        public event Action OptionsRequested;
        public event Action ReturnToTitleRequested;
        public event Action QuitRequested;

        public PauseScreen()
        {
            Root = Ui.Div("cy-screen", "cy-scrim");
            var panel = Ui.Div("cy-panel", "cy-panel--pause");
            panel.Add(Ui.Display("PAUSED", "cy-heading"));
            panel.Add(Ui.Text("SIMULATION SUSPENDED", "cy-sub"));
            Resume = AddButton(panel, new MenuButton("RESUME"));
            Options = AddButton(panel, new MenuButton("OPTIONS"));
            ReturnToTitle = AddButton(panel, new MenuButton("RETURN TO TITLE"));
            Quit = AddButton(panel, new MenuButton("QUIT", danger: true));
            Root.Add(panel);

            Resume.Pressed += () => ResumeRequested?.Invoke();
            Options.Pressed += () => OptionsRequested?.Invoke();
            ReturnToTitle.Pressed += () => ReturnToTitleRequested?.Invoke();
            Quit.Pressed += () => QuitRequested?.Invoke();
            BuildGrid(Root);
        }
    }

    /// <summary>A button of a <see cref="DialogView"/>.</summary>
    public readonly struct DialogChoice
    {
        public readonly string Text;
        public readonly Action Chosen;
        public readonly bool Danger;

        public DialogChoice(string text, Action chosen, bool danger = false)
        {
            Text = text;
            Chosen = chosen;
            Danger = danger;
        }
    }

    /// <summary>One modal box for every question and prompt: a heading, a message and a row of buttons.</summary>
    public sealed class DialogView : MenuScreen
    {
        readonly Label _heading = Ui.Display("", "cy-heading", "cy-heading--small");
        readonly Label _message = Ui.Text("", "cy-message");
        readonly VisualElement _actions = Ui.Div("cy-actions");
        readonly List<MenuButton> _buttons = new List<MenuButton>();
        readonly FocusGrid _grid;
        int _defaultIndex;

        public string Heading => _heading.text;
        public string Message => _message.text;

        public IReadOnlyList<MenuButton> Buttons => _buttons;

        public DialogView()
        {
            Root = Ui.Div("cy-screen", "cy-scrim", "cy-scrim--dialog");
            var panel = Ui.Div("cy-panel", "cy-panel--dialog");
            panel.Add(_heading);
            panel.Add(_message);
            panel.Add(_actions);
            Root.Add(panel);
            _grid = new FocusGrid(Root);
        }

        /// <param name="prompt">Draw the message large and in the accent colour: it is what the player has to act on.</param>
        /// <param name="focusable">False while a key is being captured: nothing may take the key presses meant for the capture.</param>
        public void Configure(string heading, string message, IReadOnlyList<DialogChoice> choices, int defaultIndex, bool prompt = false, bool focusable = true)
        {
            _heading.text = heading;
            _message.text = message;
            _message.EnableInClassList("cy-message--prompt", prompt);
            _actions.Clear();
            _buttons.Clear();
            foreach (DialogChoice choice in choices)
            {
                var button = new MenuButton(choice.Text, danger: choice.Danger, compact: true) { focusable = focusable };
                Action chosen = choice.Chosen;
                button.Pressed += () => chosen();
                _buttons.Add(button);
                _actions.Add(button);
            }
            _defaultIndex = Math.Max(0, Math.Min(choices.Count - 1, defaultIndex));
            _grid.SetRows(new IEnumerable<VisualElement>[] { _buttons });
            Remembered = null;
        }

        /// <summary>Update the prompt line while the dialog stays open.</summary>
        public void SetMessage(string message) => _message.text = message;

        public override void FocusDefault()
        {
            if (_buttons.Count > 0 && _buttons[_defaultIndex].focusable)
            {
                _buttons[_defaultIndex].Focus();
                return;
            }
            // Nothing in the dialog may take keys (a key is being captured), so nothing behind it may either.
            Root.panel?.focusController?.focusedElement?.Blur();
        }
    }

    /// <summary>Notices in the top-right corner, from the queue: new ones appear, expired ones go.</summary>
    public sealed class ToastLayer
    {
        readonly NoticeQueue _queue;
        readonly List<string> _shown = new List<string>();

        public VisualElement Root { get; } = Ui.Div("cy-toasts");

        public IReadOnlyList<string> Shown => _shown;

        public ToastLayer(NoticeQueue queue)
        {
            _queue = queue;
            Root.pickingMode = PickingMode.Ignore;
        }

        public void Update(double now)
        {
            IReadOnlyList<string> visible = _queue.Visible(now);
            bool same = visible.Count == _shown.Count;
            for (int i = 0; same && i < visible.Count; i++) same = visible[i] == _shown[i];
            if (same) return;

            _shown.Clear();
            _shown.AddRange(visible);
            Root.Clear();
            foreach (string text in visible)
            {
                var toast = Ui.Div("cy-toast");
                toast.pickingMode = PickingMode.Ignore;
                toast.Add(Ui.Text(text));
                Root.Add(toast);
            }
        }
    }
}
