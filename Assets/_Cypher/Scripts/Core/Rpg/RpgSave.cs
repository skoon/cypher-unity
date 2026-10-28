using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>Where saves are kept, as the prototype's <c>localStorage</c>: a string per key. In memory for tests; a folder of files in the game.</summary>
    public interface ISaveStorage
    {
        /// <summary>The stored text, or null when the key is empty.</summary>
        string GetItem(string key);
        void SetItem(string key, string value);
        void RemoveItem(string key);
        IReadOnlyList<string> Keys();
    }

    public sealed class MemorySaveStorage : ISaveStorage
    {
        readonly List<string> _order = new List<string>();
        readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public string GetItem(string key) => _values.TryGetValue(key, out string v) ? v : null;

        public void SetItem(string key, string value)
        {
            if (!_values.ContainsKey(key)) _order.Add(key);
            _values[key] = value;
        }

        public void RemoveItem(string key)
        {
            if (_values.Remove(key)) _order.Remove(key);
        }

        public IReadOnlyList<string> Keys() => _order.ToArray();
    }

    /// <summary>
    /// Saves as files in one folder, a file per slot (<c>save_&lt;slot&gt;.json</c>), each written through a temporary file so a crash cannot leave half a save.
    /// Only the save manager's keys can be stored. I/O failures are never swallowed.
    /// </summary>
    public sealed class FolderSaveStorage : ISaveStorage
    {
        static readonly System.Text.UTF8Encoding Utf8NoBom = new System.Text.UTF8Encoding(false);
        const string FilePrefix = "save_", FileSuffix = ".json";

        public string Folder { get; }

        public FolderSaveStorage(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("folder must not be empty", nameof(folder));
            Folder = folder;
        }

        /// <summary>The file a key is kept in.</summary>
        public string PathOf(string key)
        {
            if (key == null || !key.StartsWith(RpgSave.KeyPrefix, StringComparison.Ordinal)) throw new ArgumentException($"not a save key: \"{key}\"", nameof(key));
            string slot = key.Substring(RpgSave.KeyPrefix.Length);
            if (slot.Length == 0 || slot.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || slot.Contains(".."))
            {
                throw new ArgumentException($"a save slot name must be usable as a file name: \"{slot}\"", nameof(key));
            }
            return System.IO.Path.Combine(Folder, FilePrefix + slot + FileSuffix);
        }

        public string GetItem(string key)
        {
            string path = PathOf(key);
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8) : null;
        }

        public void SetItem(string key, string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            string path = PathOf(key);
            System.IO.Directory.CreateDirectory(Folder);
            string temp = path + ".tmp";
            System.IO.File.WriteAllText(temp, value, Utf8NoBom);
            if (System.IO.File.Exists(path)) System.IO.File.Replace(temp, path, null);
            else System.IO.File.Move(temp, path);
        }

        public void RemoveItem(string key)
        {
            string path = PathOf(key);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }

        public IReadOnlyList<string> Keys()
        {
            if (!System.IO.Directory.Exists(Folder)) return Array.Empty<string>();
            return System.IO.Directory.GetFiles(Folder, FilePrefix + "*" + FileSuffix)
                .Select(System.IO.Path.GetFileName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .Select(n => RpgSave.KeyPrefix + n.Substring(FilePrefix.Length, n.Length - FilePrefix.Length - FileSuffix.Length))
                .ToList();
        }
    }

    public sealed class SaveSlotInfo
    {
        public string Slot;
        public double? Version;
        public double SavedAt;
        public double? Level;
        public bool Corrupt;
    }

    /// <summary>
    /// Save and load with a version field and a migration chain: a port of <c>rpg/Save.js</c>. A save is a JSON object
    /// (<c>{ version, savedAt, slot, stats, perks, cyberware, inventory, quests, dialogue, flags, economy, hacking, vitals, world }</c>); an older one is migrated
    /// step by step, a newer one is refused (a half-understood save is worse than none), and corrupt JSON throws rather than reading as an empty slot.
    /// </summary>
    public static class RpgSave
    {
        public const int Version = 2;
        public const string KeyPrefix = "nightsector:save:";

        /// <summary>Version n → the step that upgrades a save of that version to n + 1.</summary>
        static readonly Dictionary<int, Func<JsonObject, JsonObject>> Migrations = new Dictionary<int, Func<JsonObject, JsonObject>>
        {
            // v1 kept the eddies at the top level and had no street-cred track
            [1] = data =>
            {
                if (!data.TryGetValue("economy", out object eco) || eco == null)
                {
                    data["economy"] = new JsonObject { ["money"] = data.TryGetValue("money", out object m) && m != null ? m : 0.0, ["vendors"] = new List<object>() };
                }
                data.Remove("money");
                if (!data.TryGetValue("stats", out object st) || st == null) data["stats"] = new JsonObject();
                var stats = (JsonObject)data["stats"];
                if (!stats.ContainsKey("credXp")) stats["credXp"] = 0.0;
                if (!data.TryGetValue("hacking", out object h) || h == null)
                {
                    data["hacking"] = new JsonObject { ["ram"] = null, ["active"] = new List<object>(), ["buffs"] = new List<object>() };
                }
                return data;
            },
        };

        public static JsonObject Migrate(object payload)
        {
            if (!(payload is JsonObject data)) throw new ArgumentException("migrate: save payload must be an object");
            object v = data.TryGetValue("version", out object vv) ? vv : null;
            if (!(v is double vd) || vd != Math.Floor(vd) || vd < 1) throw new InvalidOperationException($"Save has an invalid version field: {Json.Write(v, false)}");
            int version = (int)vd;
            if (version > Version) throw new InvalidOperationException($"Save version {version} is newer than this build ({Version}). Refusing to load.");
            JsonObject out_ = data;
            for (int n = version; n < Version; n++)
            {
                if (!Migrations.TryGetValue(n, out Func<JsonObject, JsonObject> step)) throw new InvalidOperationException($"No migration from save version {n} to {n + 1}");
                out_ = step(out_);
                out_["version"] = (double)(n + 1);
            }
            return out_;
        }
    }

    public sealed class SaveManager
    {
        readonly ISaveStorage _storage;
        readonly string _prefix;
        readonly Func<double> _clock;

        /// <param name="clock">Milliseconds since the epoch, as <c>Date.now()</c> (injected so tests are repeatable).</param>
        public SaveManager(ISaveStorage storage, Func<double> clock, string prefix = RpgSave.KeyPrefix)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _prefix = prefix;
        }

        public string Key(string slot) => _prefix + slot;

        /// <summary>Write a payload to a slot, stamped with the version, the slot and the time.</summary>
        public (string Slot, int Version, int Bytes) Write(string slot, JsonObject payload)
        {
            if (string.IsNullOrEmpty(slot)) throw new ArgumentException("SaveManager.write: slot must be a non-empty string");
            var body = new JsonObject();
            foreach (KeyValuePair<string, object> kv in payload) body[kv.Key] = kv.Value;
            body["version"] = (double)RpgSave.Version;
            body["slot"] = slot;
            body["savedAt"] = _clock();
            string json = Json.Write(body, false);
            _storage.SetItem(Key(slot), json);
            return (slot, RpgSave.Version, json.Length);
        }

        /// <summary>The migrated payload, or null for an empty slot. Corrupt JSON throws.</summary>
        public JsonObject Read(string slot)
        {
            string raw = _storage.GetItem(Key(slot));
            if (raw == null) return null;
            object parsed;
            try
            {
                parsed = Json.Parse(raw);
            }
            catch (JsonException err)
            {
                throw new InvalidOperationException($"Save slot \"{slot}\" is corrupt: {err.Message}", err);
            }
            return RpgSave.Migrate(parsed);
        }

        public bool Has(string slot) => _storage.GetItem(Key(slot)) != null;

        public void Remove(string slot) => _storage.RemoveItem(Key(slot));

        /// <summary>The slots in storage, newest first.</summary>
        public List<SaveSlotInfo> List()
        {
            var out_ = new List<SaveSlotInfo>();
            foreach (string key in _storage.Keys())
            {
                if (key == null || !key.StartsWith(_prefix, StringComparison.Ordinal)) continue;
                string slot = key.Substring(_prefix.Length);
                try
                {
                    var parsed = Json.Parse(_storage.GetItem(key)) as JsonObject;
                    if (parsed == null) throw new JsonException("not an object", 0);
                    double? level = null;
                    if (parsed.TryGetValue("summary", out object s) && s is JsonObject so && so.TryGetValue("level", out object l) && l is double ld) level = ld;
                    out_.Add(new SaveSlotInfo
                    {
                        Slot = slot, Version = parsed.TryGetValue("version", out object v) && v is double vd ? vd : (double?)null,
                        SavedAt = parsed.TryGetValue("savedAt", out object at) && at is double ad ? ad : 0, Level = level,
                    });
                }
                catch (JsonException)
                {
                    out_.Add(new SaveSlotInfo { Slot = slot, SavedAt = 0, Corrupt = true });
                }
            }
            return out_.OrderByDescending(i => i.SavedAt).ToList();
        }
    }
}
