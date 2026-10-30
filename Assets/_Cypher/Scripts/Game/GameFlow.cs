using System;
using System.Collections;
using Cypher.City;
using Cypher.Core;
using Cypher.Player;
using Cypher.Rendering;
using UnityEngine;

namespace Cypher.Game
{
    /// <summary>What keeps the saved game: whether there is one to continue, and saving now. The RPG service is one; without one there is nothing to continue.</summary>
    public interface ISaveGameHost
    {
        bool HasSave { get; }

        /// <summary>Save now; null when it worked, otherwise why not (for the player).</summary>
        string SaveNow();
    }

    /// <summary>
    /// The game's top-level flow in one scene: loading, the title over the live city, playing, pausing, back to the title.
    /// <see cref="GameFlowMachine"/> decides which command does what, <see cref="FlowPolicy"/> says what each state means, and this
    /// component makes it so: it parks the camera on the title flight or on the player, locks or frees the cursor, sets the
    /// time scale, switches the player controller, tells the quality governor when frames count, and spawns the player for a new
    /// game. Return to Title does not reload anything.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-30)]   // after SettingsService, before everything that reads the state
    public sealed class GameFlow : MonoBehaviour
    {
        public const string StepSettings = "settings";
        public const string StepCity = "city";
        public const string StepReflections = "reflections";
        public const string StepSettle = "settle";

        /// <summary>Frames to let pass once everything exists, so the first picture is the finished one.</summary>
        public const int SettleFrames = 5;

        public const double ProbeTimeoutSeconds = 30;

        /// <summary>A step that is still working reports no more than this, so the bar only completes when the step says it has.</summary>
        const double StepAlmost = 0.999;

        [SerializeField] FirstPersonController player;
        [SerializeField, Tooltip("The Main Camera; it rides the player while playing and the title flight otherwise.")] Transform cameraTransform;
        [SerializeField, Tooltip("Where a new game starts: position and facing.")] Transform spawn;
        [SerializeField] TitleCamera titleCamera;
        [SerializeField] QualityDirector quality;
        [SerializeField] CityBuilder city;
        [SerializeField] StreetReflectionProbe probe;
        [SerializeField, Tooltip("Pause when the window loses focus while playing (never in the Editor).")] bool autoPauseOnFocusLoss = true;

        GameFlowMachine _machine;
        LoadTracker _tracker;
        FlowOptions _options;

        public FlowState State => _machine.State;

        /// <summary>Whether the flow currently wants the cursor locked and hidden (only while playing). The engine may refuse, e.g. without a focused window.</summary>
        public bool CursorLockRequested { get; private set; }

        /// <summary>0 to 1 across loading; 1 once everything is ready.</summary>
        public double LoadProgress => _tracker.Progress;

        public string LoadStatus => _tracker.Status;

        /// <summary>The load steps, so the loading screen can show them and the build code can report into them.</summary>
        public LoadTracker Loading => _tracker;

        public event Action<FlowTransition> StateChanged;

        /// <summary>Who keeps the saved game; set by the RPG service. Null means there is no saving and nothing to continue.</summary>
        public ISaveGameHost SaveHost { get; set; }

        /// <summary>Whether Continue would do anything: on the title, with a saved game.</summary>
        public bool CanContinue => State == FlowState.Title && SaveHost != null && SaveHost.HasSave;

        /// <summary>What Quit does; tests replace it. The default ends the game, or leaves Play mode in the Editor.</summary>
        public Action QuitAction { get; set; } = DefaultQuit;

        public void Configure(FirstPersonController firstPerson, Transform camera, Transform spawnPoint, TitleCamera title, QualityDirector qualityDirector, CityBuilder cityBuilder, StreetReflectionProbe reflectionProbe)
        {
            player = firstPerson;
            cameraTransform = camera;
            spawn = spawnPoint;
            titleCamera = title;
            quality = qualityDirector;
            city = cityBuilder;
            probe = reflectionProbe;
        }

        void Awake()
        {
            _options = LaunchOptions.Current;
            _machine = new GameFlowMachine(_options.SkipTitle);
            _machine.Transitioned += OnTransitioned;
            _machine.QuitIssued += () => QuitAction();
            _tracker = new LoadTracker(new[]
            {
                new LoadStep(StepSettings, "Reading settings", 1),
                new LoadStep(StepCity, "Building the city", 6),
                new LoadStep(StepReflections, "Rendering reflections", 2),
                new LoadStep(StepSettle, "Getting ready", 1),
            });
        }

        void Start()
        {
            if (player == null || cameraTransform == null || spawn == null || titleCamera == null || quality == null || city == null || probe == null)
            {
                throw new InvalidOperationException("GameFlow needs the player, camera, spawn point, title camera, quality director, city and reflection probe; call Configure.");
            }
            // the street props follow the quality tier: how far they are drawn, whether the steam shows
            city.ApplyQuality(quality.Tier);
            quality.TierApplied += city.ApplyQuality;

            Apply(FlowState.Boot, newSession: false);
            _machine.Handle(FlowCommand.Booted);
            StartCoroutine(Load());
        }

        void OnDestroy()
        {
            if (quality != null && city != null) quality.TierApplied -= city.ApplyQuality;

            // Leave the process the way it was found: tests and the Editor outlive this scene.
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!Application.isEditor) HandleFocusChanged(hasFocus);
        }

        /// <summary>Losing focus while playing pauses the game; regaining it never resumes by itself.</summary>
        public void HandleFocusChanged(bool hasFocus)
        {
            if (hasFocus || !autoPauseOnFocusLoss || _options.NoAutoPause) return;
            if (State == FlowState.Playing) Pause();
        }

        // Each returns whether the command meant anything right now.
        public bool NewGame() => _machine.TryHandle(FlowCommand.NewGame);

        /// <summary>Carry on the saved game: the world is set up as for a new game, then the save host puts the game back. False when there is no save.</summary>
        public bool Continue() => CanContinue && _machine.TryHandle(FlowCommand.Continue);

        /// <summary>Save while paused (or playing); null when it worked, otherwise why not.</summary>
        public string SaveGame()
        {
            if (State != FlowState.Paused && State != FlowState.Playing) return "There is no game to save.";
            if (SaveHost == null) return "Saving is not available.";
            return SaveHost.SaveNow();
        }
        /// <summary>A game screen open over play (see <see cref="FlowModal"/>). It outlasts a pause, so resuming goes back to the screen; a new game or the title ends it.</summary>
        public FlowModal Modal { get; private set; }

        public event Action<FlowModal> ModalChanged;

        /// <summary>Open or close a game screen's hold on the world. Only while playing (closing is allowed at any time).</summary>
        public bool SetModal(FlowModal modal)
        {
            if (modal != FlowModal.None && State != FlowState.Playing) return false;
            if (modal == Modal) return true;
            Modal = modal;
            if (State == FlowState.Playing) Apply(State, newSession: false);
            Debug.Log($"[Cypher] flow modal {modal}");
            ModalChanged?.Invoke(modal);
            return true;
        }

        public bool Pause() => _machine.TryHandle(FlowCommand.Pause);
        public bool Resume() => _machine.TryHandle(FlowCommand.Resume);
        public bool TogglePause() => _machine.TryHandle(FlowCommand.TogglePause);
        public bool ReturnToTitle() => _machine.TryHandle(FlowCommand.ReturnToTitle);
        public bool Quit() => _machine.TryHandle(FlowCommand.Quit);

        IEnumerator Load()
        {
            _tracker.Complete(StepSettings);   // SettingsService.Start ran before this one

            // The city may be building itself over frames; its own progress drives the bar, and the bar never reaches the end until it is done.
            while (!city.IsBuilt)
            {
                _tracker.Set(StepCity, Math.Min(city.Progress, StepAlmost));
                yield return null;
            }
            _tracker.Complete(StepCity);
            Debug.Log($"[Cypher] load: city ready at {Time.realtimeSinceStartup:0.0} s");

            // The probes photograph the city, so they wait until all of it is there.
            if (probe.CaptureCount == 0)
            {
                for (int i = 0; i < StreetReflectionProbe.SettleFrames; i++) yield return null;
                probe.Capture();
            }
            double deadline = Time.realtimeSinceStartupAsDouble + ProbeTimeoutSeconds;
            while (!probe.IsCaptured)
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new InvalidOperationException("The reflection probe never finished rendering, so loading cannot finish.");
                yield return null;
            }
            _tracker.Complete(StepReflections);
            Debug.Log($"[Cypher] load: reflections ready at {Time.realtimeSinceStartup:0.0} s ({probe.Probes.Count} probes)");

            for (int i = 1; i <= SettleFrames; i++)
            {
                yield return null;
                _tracker.Set(StepSettle, (double)i / SettleFrames);
            }
            _machine.Handle(FlowCommand.LoadingFinished);
        }

        void OnTransitioned(FlowTransition t)
        {
            Debug.Log($"[Cypher] flow {t.From} -> {t.To} ({t.Command}) at {Time.realtimeSinceStartup:0.0} s");
            bool modalEnds = Modal != FlowModal.None && (t.NewSession || (t.To != FlowState.Playing && t.To != FlowState.Paused));
            if (modalEnds) Modal = FlowModal.None;
            Apply(t.To, t.NewSession);
            if (modalEnds) ModalChanged?.Invoke(FlowModal.None);
            StateChanged?.Invoke(t);
        }

        /// <summary>Make the scene match what <see cref="FlowPolicy"/> says about a state.</summary>
        void Apply(FlowState state, bool newSession)
        {
            FlowRules rules = FlowPolicy.For(state, Modal);

            Time.timeScale = (float)rules.TimeScale;
            CursorLockRequested = rules.CursorLocked;
            Cursor.lockState = rules.CursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !rules.CursorLocked;

            if (rules.TitleCameraActive) ParkCameraOnTitle();
            else ParkCameraOnPlayer();
            titleCamera.enabled = rules.TitleCameraActive;

            if (newSession) SpawnPlayer();
            player.enabled = rules.PlayerActive;
            quality.Suspended = !rules.AdaptiveQualityActive;
        }

        void ParkCameraOnTitle()
        {
            if (cameraTransform.parent == titleCamera.transform) return;
            cameraTransform.SetParent(titleCamera.transform, false);
            cameraTransform.localPosition = Vector3.zero;
            cameraTransform.localRotation = Quaternion.identity;
            titleCamera.Place();
        }

        void ParkCameraOnPlayer()
        {
            if (cameraTransform.parent == player.transform) return;
            cameraTransform.SetParent(player.transform, false);
            cameraTransform.localPosition = new Vector3(0f, (float)PlayerMotor.EyeStand, 0f);
            cameraTransform.localRotation = Quaternion.identity;
        }

        void SpawnPlayer()
        {
            float yaw = spawn.eulerAngles.y;
            player.Teleport(spawn.position, yaw);
            player.Look.Set(yaw * Mathf.Deg2Rad, 0.0);
            Physics.SyncTransforms();
        }

        static void DefaultQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
