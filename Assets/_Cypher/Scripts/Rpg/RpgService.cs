using System;
using System.Collections.Generic;
using System.Linq;
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
        string _prompt, _promptKind, _promptId;
        bool _subscribed;

        /// <summary>The game in progress, or null (before a game, and at the title).</summary>
        public RpgCore Core => _core;

        /// <summary>Every event of the core, as it happens: type and payload (see the prototype's <c>rpg/Systems.js</c> for the list).</summary>
        public event Action<string, JsonObject> Event;

        /// <summary>A game started (fresh or continued) or ended: the new core, or null.</summary>
        public event Action<RpgCore> CoreChanged;

        /// <summary>The game's buttons beyond walking, for whoever needs them (the HUD, the guns).</summary>
        public GameInput Input => _input;

        /// <summary>The player, for whoever draws where they are (the compass, the minimap).</summary>
        public FirstPersonController Player => player;

        /// <summary>The city as the places were put on it (blocks and buildings), for the minimap; null before the first game.</summary>
        public RpgCityMeta CityMeta { get; private set; }

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

        // ------------------------------------------------------------ combat

        /// <summary>An enemy of this kind died to the player at a place: its XP, cred and loot, and the kill counted for jobs by <paramref name="tag"/>.</summary>
        public void Kill(EnemyArchetype enemy, string tag, Vector3 at)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            if (_core == null) throw new InvalidOperationException("A kill with no game running.");
            _core.CombatKill(enemy.Xp, enemy.Cred, tag, enemy.LootTier, new JsonObject { ["x"] = (double)at.x, ["y"] = (double)at.y, ["z"] = (double)at.z });
        }

        /// <summary>The player was hit for this much (before armour) by <paramref name="source"/>; the player's health after.</summary>
        public double HurtPlayer(double amount, string source)
        {
            if (_core == null) throw new InvalidOperationException("A hit on the player with no game running.");
            return _core.PlayerDamage(amount, source);
        }

        /// <summary>The player flatlined: the Trauma Team bills them (<see cref="RpgCore.TraumaTeam"/>) and leaves them at Doc Vey's. Returns the fee.</summary>
        public int TraumaTeam()
        {
            if (_core == null) throw new InvalidOperationException("No game running.");
            int fee = _core.TraumaTeam();
            RpgLocation clinic = _core.GetLocation(RpgCore.TraumaTeamLocation);
            player.Teleport(new Vector3((float)clinic.X, (float)clinic.Y, (float)clinic.Z), (float)(player.Look.Yaw * Mathf.Rad2Deg));
            Physics.SyncTransforms();
            return fee;
        }

        /// <summary>Whether the save says an encounter was cleared, and marking one cleared (kept in the save's flags).</summary>
        public bool EncounterCleared(string id) => _core != null && _core.GetFlag(EncounterFlag(id)) is bool b && b;

        public void ClearEncounter(string id)
        {
            if (_core == null) throw new InvalidOperationException("No game running.");
            _core.SetFlag(EncounterFlag(id), true);
        }

        public static string EncounterFlag(string id) => "encounter.cleared." + id;

        /// <summary>Which of an encounter's people (by index) the save says are down for good, and marking one down (a flag holding the indices, "0,3").</summary>
        public IEnumerable<int> EncounterDown(string id)
        {
            if (_core == null) throw new InvalidOperationException("No game running.");
            if (!(_core.GetFlag(EncounterDownFlag(id)) is string list) || list.Length == 0) return Array.Empty<int>();
            return list.Split(',').Select(i => int.Parse(i, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        }

        public void MarkEncounterDown(string id, int member)
        {
            if (_core == null) throw new InvalidOperationException("No game running.");
            List<int> down = EncounterDown(id).ToList();
            if (down.Contains(member)) return;
            down.Add(member);
            down.Sort();
            _core.SetFlag(EncounterDownFlag(id), string.Join(",", down));
        }

        public static string EncounterDownFlag(string id) => "encounter.down." + id;

        /// <summary>The player's numbers that change what a shot does, as the RPG derives them now.</summary>
        public ShooterStats Shooter
        {
            get
            {
                if (_core == null) throw new InvalidOperationException("No game running.");
                DerivedStats d = _core.Derived;
                return new ShooterStats(d["rangedDamage"], d["critChance"], d["critDamage"]);
            }
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
            _core = new RpgCore(Forward, Seed, _storage, Now, RpgGameContent.Quests, RpgGameContent.Dialogues);
            _core.CaptureWorld = CaptureWorld;
            CityMeta = CityMeta ?? RpgCityMeta.From(city.Slice.Plan);
            _core.SetCity(CityMeta);
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
            _prompt = _promptKind = _promptId = null;
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

            // E does nothing while a game screen is open (it would talk over the conversation, or trade from inside a menu)
            if (_input.WasPressed(GameAction.Interact) && !_core.Dialogue.Active && flow.Modal == FlowModal.None) _core.Interact();
        }

        /// <summary>Announce the interaction prompt when it changes (<c>rpg:promptChanged</c>, as the prototype's system does), for the HUD.</summary>
        void SyncPrompt()
        {
            // the cheap question every frame; the payload is built only when the answer changes
            var action = _core.NearbyAction();
            if (action?.Kind == _promptKind && action?.Id == _promptId) return;
            _promptKind = action?.Kind;
            _promptId = action?.Id;
            _prompt = action != null ? action.Value.Kind + ":" + action.Value.Id : null;
            JsonObject target = _core.GetNearbyInteractable();
            Forward("rpg:promptChanged", target != null
                ? new JsonObject { ["text"] = target["prompt"], ["key"] = "E", ["targetId"] = target["id"], ["kind"] = target["kind"] }
                : new JsonObject { ["text"] = null, ["key"] = null, ["targetId"] = null, ["kind"] = null });
        }

        /// <summary>The prompt the HUD would show now, or null.</summary>
        public string Prompt => _prompt;
    }
}
