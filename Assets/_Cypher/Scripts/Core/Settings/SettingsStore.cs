using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    public enum SettingsLoadOutcome
    {
        /// <summary>There was no file; these are the defaults.</summary>
        Defaults,

        /// <summary>Read as written.</summary>
        Loaded,

        /// <summary>Written by an older build and upgraded in memory; save to make it permanent.</summary>
        Migrated,

        /// <summary>The file was unusable, was set aside, and these are the defaults. <see cref="SettingsLoadResult.Notice"/> says why.</summary>
        Recovered,
    }

    public sealed class SettingsLoadResult
    {
        public GameSettings Settings { get; }
        public SettingsLoadOutcome Outcome { get; }

        /// <summary>Plain-language message for the player when something was wrong, otherwise null.</summary>
        public string Notice { get; }

        /// <summary>Where the unusable file went, when <see cref="Outcome"/> is <see cref="SettingsLoadOutcome.Recovered"/>.</summary>
        public string SetAsideTo { get; }

        public SettingsLoadResult(GameSettings settings, SettingsLoadOutcome outcome, string notice = null, string setAsideTo = null)
        {
            Settings = settings;
            Outcome = outcome;
            Notice = notice;
            SetAsideTo = setAsideTo;
        }
    }

    /// <summary>
    /// Loads and saves <see cref="GameSettings"/> as versioned JSON. An unusable file (not JSON, wrong shape, a value out
    /// of range, or written by a newer build) is never silently replaced and never crashes the game: it is set aside
    /// for the user ("bad", or "newer" for a newer build's file), defaults are used, and the result carries a notice
    /// to show. Genuine I/O errors and missing migrations are bugs and propagate.
    /// </summary>
    public sealed class SettingsStore
    {
        public const int CurrentVersion = 1;

        readonly ISettingsStorage _storage;
        readonly int _currentVersion;
        readonly IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> _migrations;

        /// <param name="migrations">Maps a version to the function that upgrades a document of that version to the next one.</param>
        public SettingsStore(ISettingsStorage storage, int currentVersion = CurrentVersion, IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> migrations = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            if (currentVersion < 1) throw new ArgumentOutOfRangeException(nameof(currentVersion), currentVersion, "version must be at least 1");
            _currentVersion = currentVersion;
            _migrations = migrations ?? new Dictionary<int, Func<JsonObject, JsonObject>>();
        }

        public SettingsLoadResult Load()
        {
            if (!_storage.Exists) return new SettingsLoadResult(new GameSettings(), SettingsLoadOutcome.Defaults);

            string text = _storage.ReadAll();
            try
            {
                var root = Json.Parse(text) as JsonObject ?? throw new SettingsFormatException("", "the file must hold a JSON object");
                int version = ReadVersion(root);
                if (version > _currentVersion) return SetAside("newer", $"were saved by a newer version of the game (settings version {version}, this build reads up to {_currentVersion})");

                bool migrated = version < _currentVersion;
                root = Migrate(root, version);
                GameSettings settings = SettingsSerializer.FromTree(root);
                return new SettingsLoadResult(settings, migrated ? SettingsLoadOutcome.Migrated : SettingsLoadOutcome.Loaded);
            }
            catch (JsonException e)
            {
                return SetAside("bad", "could not be read: " + e.Message);
            }
            catch (SettingsFormatException e)
            {
                return SetAside("bad", "could not be used: " + e.Message);
            }
        }

        public void Save(GameSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _storage.WriteAll(Json.Write(SettingsSerializer.ToTree(settings, _currentVersion)));
        }

        SettingsLoadResult SetAside(string label, string reason)
        {
            string moved = _storage.SetAside(label);
            string notice = $"Your settings {reason}. The file was kept as {moved}, and the default settings are in use.";
            return new SettingsLoadResult(new GameSettings(), SettingsLoadOutcome.Recovered, notice, moved);
        }

        static int ReadVersion(JsonObject root)
        {
            if (!root.TryGetValue("version", out object raw)) throw new SettingsFormatException("version", "is missing");
            if (!(raw is double d) || d != Math.Floor(d) || d < 1 || d > int.MaxValue) throw new SettingsFormatException("version", "must be a whole number of at least 1");
            return (int)d;
        }

        JsonObject Migrate(JsonObject root, int version)
        {
            for (int v = version; v < _currentVersion; v++)
            {
                if (!_migrations.TryGetValue(v, out Func<JsonObject, JsonObject> step))
                {
                    throw new InvalidOperationException($"No settings migration from version {v} to {v + 1}; this is a bug in the game, not in the file.");
                }
                root = step(root) ?? throw new InvalidOperationException($"The settings migration from version {v} returned nothing.");
                root["version"] = v + 1;
            }
            return root;
        }
    }
}
