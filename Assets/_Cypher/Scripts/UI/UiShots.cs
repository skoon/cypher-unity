using System;
using System.Collections;
using System.IO;
using System.Text;
using Cypher.Core;
using Cypher.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cypher.UI
{
    /// <summary>
    /// <c>--shots DIR</c>: in a built player, walks through every menu screen and saves a screenshot of each, then quits. It drives the
    /// menus through their own controls (the same code a click runs) and presses the keys for a rebind on a virtual keyboard, so the
    /// pictures show the real screens, fonts and layout at the real window size, which an Editor test run cannot. Exit code 0 when it
    /// finished, 1 on a timeout, 2 on an exception. Does nothing without the switch.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiShots : MonoBehaviour
    {
        /// <summary>Time for fades, layout and focus to settle before each picture.</summary>
        const float SettleSeconds = 0.45f;

        const float WatchdogSeconds = 240f;

        [SerializeField] UiRoot ui;
        [SerializeField] GameFlow flow;
        [SerializeField] SettingsService settings;

        ShotsOptions _options;
        readonly StringBuilder _taken = new StringBuilder();
        Keyboard _keyboard;
        float _started;
        bool _finished;

        public void Configure(UiRoot uiRoot, GameFlow gameFlow, SettingsService settingsService)
        {
            ui = uiRoot;
            flow = gameFlow;
            settings = settingsService;
        }

        void Awake()
        {
            _options = ShotsOptions.Parse(Environment.GetCommandLineArgs());
            if (_options == null || Application.isEditor)
            {
                enabled = false;
                return;
            }
            Application.logMessageReceived += OnLog;
        }

        void OnDestroy() => Application.logMessageReceived -= OnLog;

        IEnumerator Start()
        {
            if (_options == null || Application.isEditor) yield break;
            _started = Time.realtimeSinceStartup;
            Directory.CreateDirectory(_options.Directory);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            yield return Walk();
        }

        void Update()
        {
            if (!_finished && Time.realtimeSinceStartup - _started > WatchdogSeconds && _started > 0)
            {
                Debug.LogError("[Cypher] the screenshot walk timed out");
                Finish(1);
            }
        }

        IEnumerator Walk()
        {
            // The loading screen is the first thing seen, so it is taken before anything else has happened.
            yield return null;
            yield return Shot("01_loading");

            yield return WaitFor(() => flow.State == FlowState.Title && !ui.Loading.IsShown, "the title screen");
            yield return Shot("02_title");

            ui.Title.Options.Activate();
            yield return Settle();
            yield return Shot("03_options_graphics");
            ui.Options.SelectTab(1);
            yield return Shot("04_options_audio");
            ui.Options.SelectTab(2);
            yield return Shot("05_options_gameplay");
            ui.Options.SelectTab(3);
            yield return Shot("06_options_controls");

            // A key prompt, then a key another action already uses (the conflict question), then a free one.
            ui.Options.Controls.Button(GameAction.Jump, BindingSlot.Primary).Activate();
            yield return Shot("07_rebind_prompt");
            PressVirtual(Key.W);
            yield return WaitFor(() => ui.Top == UiScreen.Conflict, "the conflict question");
            yield return Shot("08_rebind_conflict");
            ui.Dialog.Buttons[1].Activate();   // CANCEL
            yield return Settle();

            ui.Options.Controls.Button(GameAction.Jump, BindingSlot.Primary).Activate();
            yield return Settle();
            PressVirtual(Key.J);
            yield return WaitFor(() => ui.Top == UiScreen.Options, "the options after a rebind");
            ui.Options.Controls.Button(GameAction.Sprint, BindingSlot.Secondary).Activate();
            yield return Settle();
            ui.Dialog.Buttons[0].Activate();   // CLEAR THIS BINDING
            yield return Settle();
            yield return Shot("09_controls_changed");

            ui.Options.ResetButton.Activate();   // RESET ALL CONTROLS, so the walk leaves no trace
            ui.OnBack(fromBButton: false);
            yield return Settle();

            ui.Title.Quit.Activate();
            yield return Shot("10_confirm_quit");
            ui.Dialog.Buttons[1].Activate();   // NO
            yield return Settle();

            flow.NewGame();
            yield return WaitFor(() => flow.State == FlowState.Playing, "the game");
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("11_playing");
            flow.Pause();
            yield return Shot("12_pause");
            ui.Pause.ReturnToTitle.Activate();
            yield return Shot("13_confirm_title");
            ui.Dialog.Buttons[1].Activate();   // NO
            yield return Settle();
            ui.ShowNotice("Quality lowered from High to Medium because the frame rate was about 24 FPS. You can raise it in Options.");
            yield return Shot("14_notice");

            File.WriteAllText(Path.Combine(_options.Directory, "shots.txt"), _taken.ToString());
            Finish(0);
        }

        IEnumerator Settle() => new WaitForSecondsRealtime(SettleSeconds);

        IEnumerator Shot(string name)
        {
            yield return Settle();
            string path = Path.Combine(_options.Directory, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            _taken.Append(name).Append(".png  ").Append(ui.Top).Append("  ").Append(Screen.width).Append('x').Append(Screen.height).Append('\n');
        }

        IEnumerator WaitFor(Func<bool> condition, string what)
        {
            float until = Time.realtimeSinceStartup + 30f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until) throw new TimeoutException($"Waited 30 s for {what}.");
                yield return null;
            }
            yield return Settle();
        }

        void PressVirtual(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(key));
            StartCoroutine(ReleaseSoon());
        }

        IEnumerator ReleaseSoon()
        {
            yield return new WaitForSecondsRealtime(0.15f);
            InputSystem.QueueStateEvent(_keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception && !_finished) Finish(2);
        }

        void Finish(int code)
        {
            if (_finished) return;
            _finished = true;
            Application.Quit(code);
        }
    }
}
