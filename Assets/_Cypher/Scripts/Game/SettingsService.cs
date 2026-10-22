using System;
using System.Collections.Generic;
using System.IO;
using Cypher.Core;
using Cypher.Perf;
using Cypher.Player;
using Cypher.Rendering;
using UnityEngine;

namespace Cypher.Game
{
    /// <summary>
    /// Loads the player's settings at start, applies them to the running scene, saves changes, and keeps the file honest when
    /// adaptive quality lowers the preset. The rules (ranges, file format, what to do with a bad file) are Core's
    /// <see cref="SettingsStore"/>; this is the wiring to the pieces that act on them. A launch with <c>--benchmark</c> or
    /// <c>--default-settings</c> uses defaults and never reads or writes the player's file, so a measurement is repeatable.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]   // after the quality director's first apply (-50), before everything else
    public sealed class SettingsService : MonoBehaviour
    {
        public const string FileName = "settings.json";

        /// <summary>The Editor uses its own file so playing in the Editor never reads or changes the installed game's settings.</summary>
        public const string EditorFileName = "settings.editor.json";

        /// <summary>Tests set this to keep the real settings file out of the picture; null means the normal file.</summary>
        public static Func<ISettingsStorage> StorageFactory { get; set; }

        [SerializeField] QualityDirector quality;
        [SerializeField] Camera playerCamera;
        [SerializeField] RainController rain;
        [SerializeField] PerfOverlay overlay;
        [SerializeField] FirstPersonController player;

        readonly InputBindings _bindings = new InputBindings();
        SettingsStore _store;
        GameSettings _current = new GameSettings();
        FlowOptions _flow;
        bool _started;
        bool _loadingBindings;
        IReadOnlyList<string> _bindingProblems = new string[0];

        public SettingsLoadResult LoadResult { get; private set; }

        /// <summary>The latest message for the player (a recovered settings file, a lowered preset), or null.</summary>
        public string Notice { get; private set; }

        public event Action<string> NoticeRaised;

        /// <summary>Raised after settings were applied to the scene, with a copy of them.</summary>
        public event Action<GameSettings> Applied;

        /// <summary>A copy of the settings in force; change the copy and pass it to <see cref="Apply"/>.</summary>
        public GameSettings Current => _current.Clone();

        /// <summary>
        /// The controls in force. Changing them (assign, swap, unbind, reset) takes effect at once and is saved at once: a rebind is a
        /// deliberate act, unlike a slider that is still being dragged.
        /// </summary>
        public InputBindings Bindings => _bindings;

        public void Configure(QualityDirector qualityDirector, Camera camera, RainController rainController, PerfOverlay perfOverlay, FirstPersonController firstPerson)
        {
            quality = qualityDirector;
            playerCamera = camera;
            rain = rainController;
            overlay = perfOverlay;
            player = firstPerson;
        }

        public static string DefaultPath() =>
            Path.Combine(Application.persistentDataPath, Application.isEditor ? EditorFileName : FileName);

        void Awake()
        {
            _flow = LaunchOptions.Current;
            ISettingsStorage storage = StorageFactory?.Invoke()
                ?? (_flow.IgnoreUserSettings ? (ISettingsStorage)new MemorySettingsStorage() : new FileSettingsStorage(DefaultPath()));
            _store = new SettingsStore(storage);
            LoadResult = _store.Load();
            _current = LoadResult.Settings;
            _bindingProblems = LoadBindings(_current.Controls);
            if (_bindingProblems.Count > 0) _bindings.WriteTo(_current.Controls);   // what is in force, so the next save drops the bad entries
        }

        void Start()
        {
            if (quality == null || playerCamera == null || rain == null || overlay == null || player == null)
            {
                throw new InvalidOperationException("SettingsService needs the QualityDirector, camera, RainController, PerfOverlay and FirstPersonController; call Configure.");
            }
            _started = true;
            quality.Changed += OnQualityChanged;
            player.Bindings = _bindings;
            _bindings.Changed += OnBindingsChanged;
            Apply(_current);
            if (LoadResult.Notice != null) Raise(LoadResult.Notice);
            RaiseBindingProblems(_bindingProblems);
        }

        void OnDestroy()
        {
            if (quality != null) quality.Changed -= OnQualityChanged;
            _bindings.Changed -= OnBindingsChanged;
        }

        /// <summary>
        /// Put these settings into effect now. They are not written to disk until <see cref="Save"/>. Graphics, audio and gameplay come
        /// from <paramref name="settings"/>; the controls are always the ones in <see cref="Bindings"/>.
        /// </summary>
        public void Apply(GameSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!_started) throw new InvalidOperationException("SettingsService has not started yet.");

            // Controls belong to Bindings, which saves them itself the moment they change. Taking them from a settings copy that was made
            // earlier would undo rebinds made since, so whatever the copy says about controls is ignored.
            _current = settings.Clone();
            _bindings.WriteTo(_current.Controls);
            GraphicsOptions g = _current.Graphics;
            quality.Apply(g);
            playerCamera.fieldOfView = (float)g.FieldOfView;
            rain.Raining = g.Weather;
            overlay.Visible = g.ShowPerfOverlay;
            player.ApplyGameplay(_current.Gameplay);
            if (!_flow.IgnoreUserSettings) ApplyDisplay(g);
            Applied?.Invoke(Current);
        }

        public void Save() => _store.Save(_current);

        public static FullScreenMode MapWindow(WindowMode mode)
        {
            switch (mode)
            {
                case WindowMode.Windowed: return FullScreenMode.Windowed;
                case WindowMode.Borderless: return FullScreenMode.FullScreenWindow;
                case WindowMode.Fullscreen: return FullScreenMode.ExclusiveFullScreen;
                default: throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown window mode");
            }
        }

        /// <summary>Vsync and window mode. Left alone in the Editor, where they belong to the Game view, not to us.</summary>
        static void ApplyDisplay(GraphicsOptions g)
        {
            if (Application.isEditor) return;
            QualitySettings.vSyncCount = g.VSync ? 1 : 0;
            Screen.fullScreenMode = MapWindow(g.Window);
        }

        /// <summary>Put the stored controls into force without treating that as the player changing them.</summary>
        IReadOnlyList<string> LoadBindings(ControlsOptions controls)
        {
            _loadingBindings = true;
            try
            {
                return _bindings.Load(controls);
            }
            finally
            {
                _loadingBindings = false;
            }
        }

        void OnBindingsChanged()
        {
            if (_loadingBindings) return;
            _bindings.WriteTo(_current.Controls);
            Save();
        }

        void RaiseBindingProblems(IReadOnlyList<string> problems)
        {
            if (problems.Count == 0) return;
            Raise("Some saved key bindings could not be used and were reset to their defaults: " + string.Join("; ", problems) + ".");
        }

        void OnQualityChanged(QualityChange change)
        {
            if (change.Reason != QualityChangeReason.Adaptive) return;
            _current.Graphics.Quality = change.To;
            Save();
            Raise(change.Notice());
        }

        void Raise(string notice)
        {
            Notice = notice;
            NoticeRaised?.Invoke(notice);
        }
    }
}
