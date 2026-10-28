using System;
using System.IO;
using Cypher.City;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using UnityEngine;

namespace Cypher.Rpg
{
    /// <summary>
    /// The RPG in the running game: owns the <see cref="RpgCore"/> of the game in progress, makes a fresh one (with the starting kit and the jobs) on New Game and
    /// loads the saved one on Continue, puts the named places on the built city, ticks it at the core's fixed 60 Hz with where the player stands (only while
    /// playing: time stands still when paused), sends E to it, and keeps the one save slot (autosave every 120 s, Save in the pause menu), which also remembers where
    /// the player stood. Every <c>rpg:*</c> event is passed on as <see cref="Event"/> for the HUD. Gone again at the title.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(45)]   // after the player has moved this frame
    public sealed class RpgService : MonoBehaviour, ISaveGameHost
    {
        /// <summary>The core's fixed step, as the prototype's.</summary>
        public const double StepSeconds = 1.0 / 60;

        /// <summary>After a long frame the core catches up by at most this many steps and drops the rest.</summary>
        public const int MaxStepsPerFrame = 4;

        /// <summary>The run seed: the same city, places, loot and puzzles every game (the prototype's).</summary>
        public const uint Seed = 20771;

        public const string Slot = "auto";
        public const string SaveFolder = "saves", EditorSaveFolder = "saves.editor";

        [SerializeField] GameFlow flow;
        [SerializeField] CityBuilder city;
        [SerializeField] FirstPersonController player;
        [SerializeField] SettingsService settings;

        /// <summary>Where saves are kept; tests replace it. By default a folder beside the settings, or memory when the run ignores user files.</summary>
        public static Func<ISaveStorage> StorageFactory { get; set; }

        ISaveStorage _storage;
        GameInput _input;
        RpgCore _core;
        double _accumulator;
        string _prompt;
        bool _subscribed;

        /// <summary>The game in progress, or null (before a game, and at the title).</summary>
        public RpgCore Core => _core;

        /// <summary>Every event of the core, as it happens: type and payload (see the prototype's <c>rpg/Systems.js</c> for the list).</summary>
        public event Action<string, JsonObject> Event;

        /// <summary>A game started (fresh or continued) or ended: the new core, or null.</summary>
        public event Action<RpgCore> CoreChanged;

        /// <summary>The game's buttons beyond walking, for whoever needs them (the HUD, the guns).</summary>
        public GameInput Input => _input;

        public void Configure(GameFlow gameFlow, CityBuilder cityBuilder, FirstPersonController firstPerson, SettingsService settingsService)
        {
            flow = gameFlow;
            city = cityBuilder;
            player = firstPerson;
            settings = settingsService;
        }

        public static string DefaultFolder() => Path.Combine(Application.persistentDataPath, Application.isEditor ? EditorSaveFolder : SaveFolder);

        void Start()
        {
            if (flow == null || city == null || player == null || settings == null)
            {
                throw new InvalidOperationException("RpgService needs the flow, the city, the player and the settings; call Configure.");
            }
            _storage = StorageFactory?.Invoke()
                ?? (LaunchOptions.Current.IgnoreUserSettings ? (ISaveStorage)new MemorySaveStorage() : new FolderSaveStorage(DefaultFolder()));
            _input = new GameInput(settings.Bindings);
            flow.StateChanged += OnStateChanged;
            flow.SaveHost = this;
            _subscribed = true;
        }

        void OnDestroy()
        {
            if (_subscribed)
            {
                flow.StateChanged -= OnStateChanged;
                if (flow.SaveHost == (ISaveGameHost)this) flow.SaveHost = null;
            }
            _input?.Dispose();
            _core = null;
        }

        // ------------------------------------------------------------ saving

        public bool HasSave => _storage != null && _storage.GetItem(RpgSave.KeyPrefix + Slot) != null;

        public string SaveNow()
        {
            if (_core == null) return "There is no game to save.";
            _core.Save(Slot);
            return null;
        }

        static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        JsonObject CaptureWorld()
        {
            Vector3 p = player.transform.position;
            return new JsonObject
            {
                ["player"] = new JsonObject { ["x"] = p.x, ["y"] = p.y, ["z"] = p.z, ["yaw"] = player.Look.Yaw * Mathf.Rad2Deg },
            };
        }

        void RestorePlayer(JsonObject world)
        {
            if (world == null || !(world.TryGetValue("player", out object po) && po is JsonObject p)) return;   // a save from before positions were kept: stay at the spawn
            player.Teleport(new Vector3((float)(double)p["x"], (float)(double)p["y"], (float)(double)p["z"]), (float)(double)p["yaw"]);
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------ games

        void OnStateChanged(FlowTransition t)
        {
            if (t.To == FlowState.Playing && t.NewSession) Begin(t.Command == FlowCommand.Continue);
            else if (t.To == FlowState.Title) End();
        }

        /// <summary>Start the RPG for a game: a fresh character, or the saved one.</summary>
        public void Begin(bool continueSaved)
        {
            if (city.Slice == null) throw new InvalidOperationException("the RPG needs the city to be built first");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _core = new RpgCore(Forward, Seed, _storage, Now);
            _core.CaptureWorld = CaptureWorld;
            _core.SetCity(RpgCityMeta.From(city.Slice.Plan));
            if (continueSaved)
            {
                RpgResult loaded = _core.Load(Slot);
                if (!loaded.Ok) throw new InvalidOperationException($"Continue found no saved game ({loaded.Reason})");
                RestorePlayer(_core.LoadedWorld);
            }
            else
            {
                _core.GiveStartingKit();
            }
            _accumulator = 0;
            _prompt = null;
            Debug.Log($"[Cypher] rpg: {(continueSaved ? "continued the saved game" : "new game")}, level {_core.Stats.Level}, {_core.GetLocationList().Count} places; ready in {clock.ElapsedMilliseconds} ms");
            CoreChanged?.Invoke(_core);
        }

        /// <summary>End the game's RPG (the title has none). Nothing is saved: the last save stands.</summary>
        public void End()
        {
            if (_core == null) return;
            _core = null;
            CoreChanged?.Invoke(null);
        }

        void Forward(string type, JsonObject payload) => Event?.Invoke(type, payload);

        void Update()
        {
            if (_core == null || flow.State != FlowState.Playing) return;

            _accumulator += Time.deltaTime;
            Vector3 p = player.transform.position;
            _core.SetPlayerPosition(p.x, p.y, p.z);
            int steps = 0;
            while (_accumulator >= StepSeconds && steps < MaxStepsPerFrame)
            {
                // the prototype's sky clock never moves, so no game hours pass for the vendors' restock clocks
                _core.Tick(StepSeconds);
                _accumulator -= StepSeconds;
                steps++;
            }
            if (_accumulator > StepSeconds) _accumulator = 0;
            SyncPrompt();

            if (_input.WasPressed(GameAction.Interact) && !_core.Dialogue.Active) _core.Interact();
        }

        /// <summary>Announce the interaction prompt when it changes (<c>rpg:promptChanged</c>, as the prototype's system does), for the HUD.</summary>
        void SyncPrompt()
        {
            JsonObject target = _core.GetNearbyInteractable();
            string key = target != null ? $"{target["kind"]}:{target["id"]}" : null;
            if (key == _prompt) return;
            _prompt = key;
            Forward("rpg:promptChanged", target != null
                ? new JsonObject { ["text"] = target["prompt"], ["key"] = "E", ["targetId"] = target["id"], ["kind"] = target["kind"] }
                : new JsonObject { ["text"] = null, ["key"] = null, ["targetId"] = null, ["kind"] = null });
        }

        /// <summary>The prompt the HUD would show now, or null.</summary>
        public string Prompt => _prompt;
    }
}
