using System;
using System.Collections.Generic;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using Cypher.Rpg;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>
    /// The menus: loading, title, pause, options, and the dialogs on top of them, drawn with UI Toolkit over the game. It follows the
    /// game flow (<see cref="GameFlow"/>), shows which screen <see cref="ScreenStack"/> says, and turns the player's choices into flow
    /// commands (New Game, Resume, Return to Title, Quit) and settings changes (<see cref="SettingsService"/>). It also owns the one
    /// Back action (Esc, the gamepad's Start and B): Esc pauses while playing, closes the top screen in a menu, and resumes from the
    /// pause screen. Keyboard, mouse and gamepad navigation come from the engine's UI input; the focus rules are in <see cref="FocusGrid"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-20)]   // after SettingsService and GameFlow have made what the screens show
    public sealed partial class UiRoot : MonoBehaviour
    {
        const string ThemePath = "CypherUI/CypherTheme";
        const string StylePath = "CypherUI/Cypher";

        [SerializeField] GameFlow flow;
        [SerializeField] SettingsService settings;
        [SerializeField] RpgService rpg;
        [SerializeField] Camera gameCamera;
        [SerializeField] Cypher.Actors.CrowdController crowd;
        [SerializeField] Cypher.Actors.TrafficController traffic;
        [SerializeField] Cypher.Combat.WeaponSystem guns;
        [SerializeField] Cypher.Fight.EnemyController fight;

        readonly ScreenStack _stack = new ScreenStack();
        readonly NoticeQueue _notices = new NoticeQueue();

        PanelSettings _panel;
        VisualElement _root;
        LoadingScreen _loading;
        TitleScreen _title;
        PauseScreen _pause;
        OptionsScreen _options;
        DialogView _dialog;
        ToastLayer _toasts;
        Hud _hud;
        InputAction _back;
        InputAction _tabPrevious;
        InputAction _tabNext;
        UiScreen _loggedTop = UiScreen.None;
        BindingCapture _capture;
        GameAction _rebindAction;
        BindingSlot _rebindSlot;
        bool _started;

        public UiScreen Top => _stack.Top;

        public ScreenStack Stack => _stack;

        public VisualElement Root => _root;

        public LoadingScreen Loading => _loading;

        public TitleScreen Title => _title;

        public PauseScreen Pause => _pause;

        public OptionsScreen Options => _options;

        public DialogView Dialog => _dialog;

        public ToastLayer Toasts => _toasts;

        /// <summary>The in-game HUD, or null when the UI was made without the RPG (the menu-only test scenes).</summary>
        public Hud Hud => _hud;

        public bool IsCapturing => _capture != null && _capture.IsActive;

        public void Configure(GameFlow gameFlow, SettingsService settingsService)
        {
            flow = gameFlow;
            settings = settingsService;
        }

        /// <summary>The menus and the HUD, which reads the RPG and places the waypoint with the game camera.</summary>
        public void Configure(GameFlow gameFlow, SettingsService settingsService, RpgService rpgService, Camera camera)
        {
            Configure(gameFlow, settingsService);
            rpg = rpgService ?? throw new ArgumentNullException(nameof(rpgService));
            gameCamera = camera ?? throw new ArgumentNullException(nameof(camera));
        }

        void Awake()
        {
            if (flow == null || settings == null) throw new InvalidOperationException("UiRoot needs the GameFlow and SettingsService; call Configure.");
            BuildDocument();
            BuildScreens();
        }

        void Start()
        {
            _started = true;
            flow.StateChanged += OnFlowChanged;
            settings.NoticeRaised += OnNotice;
            settings.Bindings.Changed += OnBindingsChanged;
            if (settings.Notice != null) OnNotice(settings.Notice);

            _back = new InputAction("UiBack", InputActionType.Button);
            _back.AddBinding("<Keyboard>/escape");
            _back.AddBinding("<Gamepad>/start");
            _back.AddBinding("<Gamepad>/buttonEast");
            _back.performed += ctx => OnBack(fromBButton: ctx.control.name == "buttonEast");
            _back.Enable();

            _tabPrevious = new InputAction("UiTabPrevious", InputActionType.Button);
            _tabPrevious.AddBinding("<Gamepad>/leftShoulder");
            _tabPrevious.AddBinding("<Keyboard>/pageUp");
            _tabPrevious.performed += _ => SwitchOptionsTab(-1);
            _tabPrevious.Enable();
            _tabNext = new InputAction("UiTabNext", InputActionType.Button);
            _tabNext.AddBinding("<Gamepad>/rightShoulder");
            _tabNext.AddBinding("<Keyboard>/pageDown");
            _tabNext.performed += _ => SwitchOptionsTab(1);
            _tabNext.Enable();

            _stack.SetFlowState(flow.State);
            Refresh();
        }

        void OnDestroy()
        {
            if (flow != null) flow.StateChanged -= OnFlowChanged;
            if (settings != null)
            {
                settings.NoticeRaised -= OnNotice;
                settings.Bindings.Changed -= OnBindingsChanged;
            }
            _capture?.Dispose();
            _hud?.Detach();
            DetachGameScreens();
            foreach (InputAction a in new[] { _back, _tabPrevious, _tabNext }) a?.Dispose();
            if (_panel != null) Destroy(_panel);
        }

        void Update()
        {
            if (_loading.IsShown && !_loading.IsLeaving) _loading.SetProgress(flow.LoadProgress, flow.LoadStatus);
            _toasts.Update(Time.unscaledTimeAsDouble);
            if (_hud != null)
            {
                bool playing = flow.State == FlowState.Playing && rpg.Core != null;
                _hud.SetShown(playing && (_gameScreen == UiScreen.None || ModalFor(_gameScreen) == FlowModal.Interaction));
                UpdateGameScreens(Time.unscaledDeltaTime);
                if (playing) _hud.Update(gameCamera, Time.unscaledTimeAsDouble, Time.unscaledDeltaTime);
            }
        }

        // ------------------------------------------------------------------ building

        void BuildDocument()
        {
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "CypherUI panel";
            _panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>(ThemePath)
                ?? throw new InvalidOperationException($"The UI theme Resources/{ThemePath} is missing.");
            _panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panel.referenceResolution = new Vector2Int(1920, 1080);
            _panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _panel.match = 1f;   // follow the window height, so the menus keep their size relative to the picture
            _panel.sortingOrder = 500;

            // Created inactive so the panel is in place before the document first enables.
            var go = new GameObject("CypherUI Document");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = _panel;
            go.SetActive(true);

            _root = document.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;
            _root.AddToClassList("cy-root");
            _root.styleSheets.Add(Resources.Load<StyleSheet>(StylePath) ?? throw new InvalidOperationException($"The UI style sheet Resources/{StylePath} is missing."));
        }

        void BuildScreens()
        {
            string version = Application.version + (Debug.isDebugBuild ? "  DEVELOPMENT BUILD" : "");
            _title = new TitleScreen("CYPHER  " + version);
            _pause = new PauseScreen();
            _options = new OptionsScreen(settings);
            _dialog = new DialogView();
            _loading = new LoadingScreen();
            _toasts = new ToastLayer(_notices);

            _title.NewGameRequested += () => flow.NewGame();
            _title.ContinueRequested += () => flow.Continue();
            _title.OptionsRequested += OpenOptions;
            _title.QuitRequested += AskQuit;
            _pause.ResumeRequested += () => flow.Resume();
            _pause.SaveRequested += SaveFromPause;
            _pause.OptionsRequested += OpenOptions;
            _pause.ReturnToTitleRequested += AskReturnToTitle;
            _pause.QuitRequested += AskQuit;
            _options.BackRequested += () => CloseTop();
            _options.RebindRequested += BeginRebind;

            if (rpg != null)
            {
                if (gameCamera == null) throw new InvalidOperationException("UiRoot's HUD needs the game camera; call Configure with it.");
                _hud = new Hud(rpg);
                if (guns != null) _hud.AttachGuns(guns);
                if (fight != null) _hud.AttachFight(fight);
                _hud.SetInteractKey(() => BindingText.Describe(settings.Bindings.Get(GameAction.Interact, BindingSlot.Primary)).ToUpperInvariant());
                _hud.SetShown(false);
                _root.Add(_hud.Root);   // beneath the menus
                BuildGameScreens();
            }
            foreach (MenuScreen s in new MenuScreen[] { _title, _pause, _options, _dialog, _loading }) _root.Add(s.Root);
            _root.Add(_toasts.Root);
            UiFonts.Apply(_root);

            _title.SetShown(false);
            _pause.SetShown(false);
            _options.SetShown(false);
            _dialog.SetShown(false);
            _loading.Show();
        }

        // ------------------------------------------------------------------ what shows

        void OnFlowChanged(FlowTransition t)
        {
            CancelCapture();
            _stack.SetFlowState(t.To);
            FollowFlowForGameScreens(t);
            if (t.To == FlowState.Paused) _pause.Remembered = null;
            // the pause menu opens and closes with a sound; the title, appearing at the start, does not
            if (t.To == FlowState.Paused) UiSound.Raise(UiSound.Open);
            else if (t.From == FlowState.Paused && t.To == FlowState.Playing) UiSound.Raise(UiSound.Close);
            Refresh();
        }

        /// <summary>Save from the pause menu and say how it went.</summary>
        void SaveFromPause()
        {
            string problem = flow.SaveGame();
            OnNotice(problem ?? "GAME SAVED");
        }

        void OnNotice(string text)
        {
            _notices.Add(text, Time.unscaledTimeAsDouble);
            UiSound.Raise(UiSound.Notify);
        }

        /// <summary>Show a message in the corner for a few seconds.</summary>
        public void ShowNotice(string text) => OnNotice(text);

        void OnBindingsChanged()
        {
            if (_started) _options.RefreshControls();
            _hud?.KeyChanged();
        }

        void Refresh()
        {
            UiScreen baseScreen = _stack.Base;
            bool overlay = _stack.Overlays.Count > 0;

            if (baseScreen == UiScreen.Loading) _loading.Show();
            else _loading.Leave();

            _title.SetContinue(flow.CanContinue);
            _title.SetShown(baseScreen == UiScreen.Title && !overlay);
            _pause.SetShown(baseScreen == UiScreen.Pause && !overlay);
            _options.SetShown(Contains(UiScreen.Options));
            _dialog.SetShown(IsDialog(_stack.Top));
            ShowGameScreens();
            FocusSoon();

            if (_stack.Top != _loggedTop)
            {
                _loggedTop = _stack.Top;
                Debug.Log($"[Cypher] ui screen {_loggedTop}");
            }
        }

        bool Contains(UiScreen screen)
        {
            foreach (UiScreen s in _stack.Overlays)
            {
                if (s == screen) return true;
            }
            return false;
        }

        static bool IsDialog(UiScreen s) => s == UiScreen.ConfirmQuit || s == UiScreen.ConfirmTitle || s == UiScreen.Rebind || s == UiScreen.Conflict;

        MenuScreen ScreenFor(UiScreen screen)
        {
            switch (screen)
            {
                case UiScreen.Title: return _title;
                case UiScreen.Pause: return _pause;
                case UiScreen.Options: return _options;
                case UiScreen.ConfirmQuit:
                case UiScreen.ConfirmTitle:
                case UiScreen.Rebind:
                case UiScreen.Conflict: return _dialog;
                case UiScreen.Dialogue: return _conversation;
                case UiScreen.Breach: return _breach;
                case UiScreen.GameMenu: return _menu;
                case UiScreen.Vendor: return _vendor;
                default: return null;
            }
        }

        /// <summary>Remember what is focused on the screen that is about to be covered, so closing what covers it brings the player back to it.</summary>
        void RememberFocus()
        {
            MenuScreen screen = ScreenFor(_stack.Top);
            if (screen == null || screen == _dialog) return;
            if (_root.panel?.focusController?.focusedElement is VisualElement focused && screen.Root.Contains(focused)) screen.Remembered = focused;
        }

        void FocusSoon() => _root.schedule.Execute(FocusTop);

        void FocusTop()
        {
            MenuScreen screen = ScreenFor(_stack.Top);
            if (screen == null)
            {
                _root.panel?.focusController?.focusedElement?.Blur();
                return;
            }
            if (screen == _dialog) _dialog.FocusDefault();
            else screen.FocusRemembered();
        }

        // ------------------------------------------------------------------ opening and closing

        void OpenOptions()
        {
            RememberFocus();
            if (!_stack.TryPush(UiScreen.Options)) return;
            UiSound.Raise(UiSound.Open);
            _options.Open();
            Refresh();
        }

        void AskQuit()
        {
            RememberFocus();
            if (!_stack.TryPush(UiScreen.ConfirmQuit)) return;
            UiSound.Raise(UiSound.Alert);
            _dialog.Configure("QUIT CYPHER?", "Leave the game and go back to the desktop.",
                new[] { new DialogChoice("YES, QUIT", () => flow.Quit(), danger: true), new DialogChoice("NO", () => CloseTop()) }, defaultIndex: 1);
            Refresh();
        }

        void AskReturnToTitle()
        {
            RememberFocus();
            if (!_stack.TryPush(UiScreen.ConfirmTitle)) return;
            UiSound.Raise(UiSound.Alert);
            _dialog.Configure("RETURN TO TITLE?", "You will leave the game you are in.",
                new[] { new DialogChoice("YES, LEAVE", () => flow.ReturnToTitle(), danger: true), new DialogChoice("NO", () => CloseTop()) }, defaultIndex: 1);
            Refresh();
        }

        /// <summary>Close the top screen or dialog, saving first if it was the options.</summary>
        void CloseTop()
        {
            if (_stack.Top == UiScreen.Options) _options.Flush();
            if (_stack.Pop())
            {
                UiSound.Raise(UiSound.Close);
                if (_stack.Top == UiScreen.Options) _options.RefreshControls();
                Refresh();
            }
        }

        // ------------------------------------------------------------------ Back, tabs

        /// <summary>The Back action. <paramref name="fromBButton"/> is the gamepad's B, which must not pause the game or cancel a key prompt (B can be bound).</summary>
        public void OnBack(bool fromBButton)
        {
            if (_stack.Top == UiScreen.Rebind)
            {
                // Esc cancels the capture by itself; Start cancels it here. B is a candidate for the binding, so it never cancels.
                if (!fromBButton) _capture?.Cancel();
                return;
            }

            if (IsGameScreen(_stack.Top))
            {
                LeaveGameScreen();
                return;
            }

            if (_stack.Base == UiScreen.None && _stack.Overlays.Count == 0)
            {
                if (!fromBButton) flow.Pause();
                return;
            }

            if (_stack.Top == UiScreen.Options) _options.Flush();
            switch (_stack.Back())
            {
                case BackResult.ClosedOverlay:
                    UiSound.Raise(UiSound.Back);
                    if (_stack.Top == UiScreen.Options) _options.RefreshControls();
                    Refresh();
                    break;
                case BackResult.Resume:
                    flow.Resume();
                    break;
            }
        }

        void SwitchOptionsTab(int direction)
        {
            if (_stack.Top == UiScreen.GameMenu)
            {
                ShowMenuTab(_menu.Step(direction));
                return;
            }
            if (_stack.Top != UiScreen.Options) return;
            _options.SwitchTab(direction);
            FocusSoon();
        }

        // ------------------------------------------------------------------ rebinding

        void BeginRebind(GameAction action, BindingSlot slot)
        {
            RememberFocus();
            if (!_stack.TryPush(UiScreen.Rebind)) return;
            UiSound.Raise(UiSound.Open);
            _rebindAction = action;
            _rebindSlot = slot;

            string device = slot == BindingSlot.Gamepad ? "a gamepad button" : "a key or mouse button";
            string where = slot == BindingSlot.Primary ? "KEYBOARD / MOUSE" : slot == BindingSlot.Secondary ? "ALTERNATE" : "GAMEPAD";
            string escape = slot == BindingSlot.Gamepad ? "Start or Esc cancels." : "Esc cancels.";
            _dialog.Configure($"REBIND {ControlCatalog.For(action).DisplayName.ToUpperInvariant()}", $"Press {device} for {where}. {escape}",
                new[] { new DialogChoice("CLEAR THIS BINDING", ClearRebindSlot), new DialogChoice("CANCEL", () => _capture?.Cancel()) },
                defaultIndex: 1, prompt: true, focusable: false);
            Refresh();

            _capture = BindingCapture.Begin(slot, OnCaptured, OnCaptureCancelled);
        }

        void OnCaptured(string path)
        {
            _capture = null;
            AssignResult result = settings.Bindings.Assign(_rebindAction, _rebindSlot, path);
            switch (result.Outcome)
            {
                case AssignOutcome.Conflict:
                    AskSwap(result, path);
                    return;
                case AssignOutcome.Rejected:
                    OnNotice(result.Message);
                    break;
            }
            CloseTop();
        }

        void OnCaptureCancelled()
        {
            _capture = null;
            if (_stack.Top == UiScreen.Rebind) CloseTop();
        }

        void ClearRebindSlot()
        {
            _capture?.Dispose();
            _capture = null;
            AssignResult result = settings.Bindings.Unbind(_rebindAction, _rebindSlot);
            if (result.Outcome == AssignOutcome.Rejected) OnNotice(result.Message);
            CloseTop();
        }

        void AskSwap(AssignResult conflict, string path)
        {
            GameAction action = _rebindAction;
            BindingSlot slot = _rebindSlot;
            if (!_stack.TryReplaceTop(UiScreen.Conflict)) return;
            UiSound.Raise(UiSound.Error);
            _dialog.Configure("ALREADY IN USE", conflict.Message + " Swap them?",
                new[]
                {
                    new DialogChoice("SWAP", () =>
                    {
                        AssignResult swapped = settings.Bindings.SwapWith(action, slot, path);
                        if (swapped.Outcome == AssignOutcome.Rejected) OnNotice(swapped.Message);
                        CloseTop();
                    }),
                    new DialogChoice("CANCEL", () => CloseTop()),
                }, defaultIndex: 1);
            Refresh();
        }

        void CancelCapture()
        {
            _capture?.Dispose();
            _capture = null;
        }
    }
}
