using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cypher.Core
{
    /// <summary>A settings document parsed as JSON but wrong as settings. <see cref="Path"/> says where, e.g. <c>graphics.fieldOfView</c>.</summary>
    public sealed class SettingsFormatException : Exception
    {
        public string Path { get; }

        public SettingsFormatException(string path, string message) : base(path.Length == 0 ? message : $"{path}: {message}")
        {
            Path = path;
        }
    }

    /// <summary>
    /// Settings to and from a JSON tree. Reading is strict about what is present: a value of the wrong type, an unknown
    /// enum name or a number outside its range is an error, never clamped. A key that is absent keeps its default,
    /// and keys this build does not know are ignored, so a file survives a downgrade and hand editing.
    /// </summary>
    public static class SettingsSerializer
    {
        public static JsonObject ToTree(GameSettings s, int version)
        {
            var graphics = new JsonObject
            {
                ["quality"] = s.Graphics.Quality.ToString(),
                ["adaptiveQuality"] = s.Graphics.AdaptiveQuality,
                ["internalHeight"] = s.Graphics.InternalHeight,
                ["window"] = s.Graphics.Window.ToString(),
                ["vsync"] = s.Graphics.VSync,
                ["fieldOfView"] = s.Graphics.FieldOfView,
                ["weather"] = s.Graphics.Weather,
                ["showPerfOverlay"] = s.Graphics.ShowPerfOverlay,
            };
            var audio = new JsonObject
            {
                ["master"] = s.Audio.Master,
                ["music"] = s.Audio.Music,
                ["sfx"] = s.Audio.Sfx,
                ["ambience"] = s.Audio.Ambience,
                ["ui"] = s.Audio.Ui,
                ["voice"] = s.Audio.Voice,
            };
            var gameplay = new JsonObject
            {
                ["sensitivity"] = s.Gameplay.Sensitivity,
                ["invertY"] = s.Gameplay.InvertY,
            };
            var overrides = new JsonObject();
            foreach (KeyValuePair<string, string> kv in s.Controls.Overrides) overrides[kv.Key] = kv.Value;

            return new JsonObject
            {
                ["version"] = version,
                ["graphics"] = graphics,
                ["audio"] = audio,
                ["gameplay"] = gameplay,
                ["controls"] = new JsonObject { ["overrides"] = overrides },
            };
        }

        public static GameSettings FromTree(JsonObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var s = new GameSettings();

            JsonObject graphics = Section(root, "graphics");
            if (graphics != null)
            {
                string at = "graphics";
                if (Enum(graphics, at, "quality", out QualityPreset quality)) s.Graphics.Quality = quality;
                if (Bool(graphics, at, "adaptiveQuality", out bool adaptive)) s.Graphics.AdaptiveQuality = adaptive;
                if (Integer(graphics, at, "internalHeight", SettingsRanges.InternalHeightAuto, SettingsRanges.InternalHeightMax, out int height))
                {
                    if (height != SettingsRanges.InternalHeightAuto && height < SettingsRanges.InternalHeightMin)
                    {
                        throw new SettingsFormatException("graphics.internalHeight", $"must be 0 (Auto) or between {SettingsRanges.InternalHeightMin} and {SettingsRanges.InternalHeightMax}, got {height}");
                    }
                    s.Graphics.InternalHeight = height;
                }
                if (Enum(graphics, at, "window", out WindowMode window)) s.Graphics.Window = window;
                if (Bool(graphics, at, "vsync", out bool vsync)) s.Graphics.VSync = vsync;
                if (Number(graphics, at, "fieldOfView", SettingsRanges.FovMin, SettingsRanges.FovMax, out double fov)) s.Graphics.FieldOfView = fov;
                if (Bool(graphics, at, "weather", out bool weather)) s.Graphics.Weather = weather;
                if (Bool(graphics, at, "showPerfOverlay", out bool overlay)) s.Graphics.ShowPerfOverlay = overlay;
            }

            JsonObject audio = Section(root, "audio");
            if (audio != null)
            {
                string at = "audio";
                if (Volume(audio, at, "master", out double master)) s.Audio.Master = master;
                if (Volume(audio, at, "music", out double music)) s.Audio.Music = music;
                if (Volume(audio, at, "sfx", out double sfx)) s.Audio.Sfx = sfx;
                if (Volume(audio, at, "ambience", out double ambience)) s.Audio.Ambience = ambience;
                if (Volume(audio, at, "ui", out double ui)) s.Audio.Ui = ui;
                if (Volume(audio, at, "voice", out double voice)) s.Audio.Voice = voice;
            }

            JsonObject gameplay = Section(root, "gameplay");
            if (gameplay != null)
            {
                string at = "gameplay";
                if (Number(gameplay, at, "sensitivity", SettingsRanges.SensitivityMin, SettingsRanges.SensitivityMax, out double sensitivity)) s.Gameplay.Sensitivity = sensitivity;
                if (Bool(gameplay, at, "invertY", out bool invertY)) s.Gameplay.InvertY = invertY;
            }

            JsonObject controls = Section(root, "controls");
            if (controls != null && controls.TryGetValue("overrides", out object raw))
            {
                if (!(raw is JsonObject overrides)) throw new SettingsFormatException("controls.overrides", "must be an object");
                foreach (KeyValuePair<string, object> kv in overrides)
                {
                    string path = "controls.overrides." + kv.Key;
                    if (string.IsNullOrWhiteSpace(kv.Key)) throw new SettingsFormatException("controls.overrides", "binding keys must not be empty");
                    if (!(kv.Value is string control) || string.IsNullOrWhiteSpace(control)) throw new SettingsFormatException(path, "must be a non-empty control path string");
                    s.Controls.SetOverride(kv.Key, control);
                }
            }
            return s;
        }

        static JsonObject Section(JsonObject root, string name)
        {
            if (!root.TryGetValue(name, out object value)) return null;
            return value as JsonObject ?? throw new SettingsFormatException(name, "must be an object");
        }

        static bool Bool(JsonObject o, string at, string key, out bool value)
        {
            value = false;
            if (!o.TryGetValue(key, out object raw)) return false;
            if (!(raw is bool b)) throw new SettingsFormatException($"{at}.{key}", "must be true or false");
            value = b;
            return true;
        }

        static bool Number(JsonObject o, string at, string key, double min, double max, out double value)
        {
            value = 0;
            if (!o.TryGetValue(key, out object raw)) return false;
            if (!(raw is double d)) throw new SettingsFormatException($"{at}.{key}", "must be a number");
            if (!(d >= min && d <= max)) throw new SettingsFormatException($"{at}.{key}", $"must be between {Fmt(min)} and {Fmt(max)}, got {Fmt(d)}");
            value = d;
            return true;
        }

        static bool Integer(JsonObject o, string at, string key, int min, int max, out int value)
        {
            value = 0;
            if (!Number(o, at, key, min, max, out double d)) return false;
            if (d != Math.Floor(d)) throw new SettingsFormatException($"{at}.{key}", $"must be a whole number, got {Fmt(d)}");
            value = (int)d;
            return true;
        }

        static bool Volume(JsonObject o, string at, string key, out double value) =>
            Number(o, at, key, SettingsRanges.VolumeMin, SettingsRanges.VolumeMax, out value);

        static bool Enum<T>(JsonObject o, string at, string key, out T value) where T : struct
        {
            value = default;
            if (!o.TryGetValue(key, out object raw)) return false;
            string[] names = System.Enum.GetNames(typeof(T));
            // Exact names only: Enum.TryParse would also take "2" or "Low, High".
            if (!(raw is string name) || Array.IndexOf(names, name) < 0)
            {
                throw new SettingsFormatException($"{at}.{key}", $"must be one of {string.Join(", ", names)}");
            }
            value = (T)System.Enum.Parse(typeof(T), name);
            return true;
        }

        static string Fmt(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
