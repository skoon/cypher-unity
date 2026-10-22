using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>The five quality presets of the prototype, cheapest first. What each one costs is defined in the quality tiers.</summary>
    public enum QualityPreset { Performance, Low, Medium, High, Ultra }

    public enum WindowMode { Windowed, Borderless, Fullscreen }

    /// <summary>Allowed ranges and defaults for every setting; the model, the file reader and the Options screen share them.</summary>
    public static class SettingsRanges
    {
        public const double FovMin = 60, FovMax = 110, FovDefault = 70;
        public const int InternalHeightMin = 240, InternalHeightMax = 2160;

        /// <summary>Resolution left on Auto: the quality preset picks the internal height.</summary>
        public const int InternalHeightAuto = 0;
        public const int InternalHeightDefault = InternalHeightAuto;
        public const double VolumeMin = 0, VolumeMax = 1;
        public const double SensitivityMin = 0.1, SensitivityMax = 5, SensitivityDefault = 1;

        /// <summary>Provisional until the populated-city measurements decide it.</summary>
        public const QualityPreset QualityDefault = QualityPreset.Medium;
        public const WindowMode WindowDefault = WindowMode.Borderless;
    }

    static class Check
    {
        public static double InRange(string name, double value, double min, double max)
        {
            // Written so NaN fails too.
            if (!(value >= min && value <= max)) throw new ArgumentOutOfRangeException(name, value, $"{name} must be between {min} and {max}");
            return value;
        }
    }

    public sealed class GraphicsOptions
    {
        QualityPreset _quality = SettingsRanges.QualityDefault;
        int _internalHeight = SettingsRanges.InternalHeightDefault;
        double _fieldOfView = SettingsRanges.FovDefault;

        public QualityPreset Quality
        {
            get => _quality;
            set => _quality = Enum.IsDefined(typeof(QualityPreset), value) ? value : throw new ArgumentOutOfRangeException(nameof(Quality), value, "unknown quality preset");
        }

        /// <summary>Lowers quality by itself when frames miss the budget; never raises it.</summary>
        public bool AdaptiveQuality { get; set; } = true;

        /// <summary>
        /// Height in pixels the 3D scene renders at before it is upscaled to the window: <see cref="SettingsRanges.InternalHeightAuto"/>
        /// (0) to let the quality preset decide, or a fixed value.
        /// </summary>
        public int InternalHeight
        {
            get => _internalHeight;
            set
            {
                if (value != SettingsRanges.InternalHeightAuto) Check.InRange(nameof(InternalHeight), value, SettingsRanges.InternalHeightMin, SettingsRanges.InternalHeightMax);
                _internalHeight = value;
            }
        }

        public WindowMode Window { get; set; } = SettingsRanges.WindowDefault;

        public bool VSync { get; set; } = true;

        public double FieldOfView
        {
            get => _fieldOfView;
            set => _fieldOfView = Check.InRange(nameof(FieldOfView), value, SettingsRanges.FovMin, SettingsRanges.FovMax);
        }

        /// <summary>Rain and the other weather effects; wet streets stay either way.</summary>
        public bool Weather { get; set; } = true;

        public bool ShowPerfOverlay { get; set; } = true;

        public GraphicsOptions Clone() => (GraphicsOptions)MemberwiseClone();

        public void ResetToDefaults()
        {
            var d = new GraphicsOptions();
            Quality = d.Quality;
            AdaptiveQuality = d.AdaptiveQuality;
            InternalHeight = d.InternalHeight;
            Window = d.Window;
            VSync = d.VSync;
            FieldOfView = d.FieldOfView;
            Weather = d.Weather;
            ShowPerfOverlay = d.ShowPerfOverlay;
        }

        public bool SameValues(GraphicsOptions o) =>
            o != null && Quality == o.Quality && AdaptiveQuality == o.AdaptiveQuality && InternalHeight == o.InternalHeight
            && Window == o.Window && VSync == o.VSync && FieldOfView == o.FieldOfView && Weather == o.Weather
            && ShowPerfOverlay == o.ShowPerfOverlay;
    }

    /// <summary>Linear volumes, 0 silent to 1 full. Master scales the other five.</summary>
    public sealed class AudioOptions
    {
        double _master = 0.8, _music = 0.7, _sfx = 1, _ambience = 1, _ui = 1, _voice = 1;

        public double Master { get => _master; set => _master = Volume(nameof(Master), value); }
        public double Music { get => _music; set => _music = Volume(nameof(Music), value); }
        public double Sfx { get => _sfx; set => _sfx = Volume(nameof(Sfx), value); }
        public double Ambience { get => _ambience; set => _ambience = Volume(nameof(Ambience), value); }
        public double Ui { get => _ui; set => _ui = Volume(nameof(Ui), value); }
        public double Voice { get => _voice; set => _voice = Volume(nameof(Voice), value); }

        static double Volume(string name, double v) => Check.InRange(name, v, SettingsRanges.VolumeMin, SettingsRanges.VolumeMax);

        public AudioOptions Clone() => (AudioOptions)MemberwiseClone();

        public void ResetToDefaults()
        {
            var d = new AudioOptions();
            Master = d.Master;
            Music = d.Music;
            Sfx = d.Sfx;
            Ambience = d.Ambience;
            Ui = d.Ui;
            Voice = d.Voice;
        }

        public bool SameValues(AudioOptions o) =>
            o != null && Master == o.Master && Music == o.Music && Sfx == o.Sfx && Ambience == o.Ambience && Ui == o.Ui && Voice == o.Voice;
    }

    public sealed class GameplayOptions
    {
        double _sensitivity = SettingsRanges.SensitivityDefault;

        /// <summary>Multiplier on the default look speed (<see cref="LookState.DefaultSensitivity"/> radians per mouse pixel).</summary>
        public double Sensitivity
        {
            get => _sensitivity;
            set => _sensitivity = Check.InRange(nameof(Sensitivity), value, SettingsRanges.SensitivityMin, SettingsRanges.SensitivityMax);
        }

        /// <summary>Radians per mouse pixel after the multiplier.</summary>
        public double LookSensitivity => LookState.DefaultSensitivity * _sensitivity;

        public bool InvertY { get; set; }

        public GameplayOptions Clone() => (GameplayOptions)MemberwiseClone();

        public void ResetToDefaults()
        {
            var d = new GameplayOptions();
            Sensitivity = d.Sensitivity;
            InvertY = d.InvertY;
        }

        public bool SameValues(GameplayOptions o) => o != null && Sensitivity == o.Sensitivity && InvertY == o.InvertY;
    }

    /// <summary>
    /// The player's changes to the default key bindings: a key naming one binding slot of one action (for example
    /// <c>jump:0</c>) mapped to an Input System control path (<c>&lt;Keyboard&gt;/space</c>). What a key means is up to the
    /// input layer; this only stores and persists them, in key order so the file is stable.
    /// </summary>
    public sealed class ControlsOptions
    {
        readonly SortedDictionary<string, string> _overrides = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, string> Overrides => _overrides;

        public void SetOverride(string key, string controlPath)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("binding key must not be empty", nameof(key));
            if (string.IsNullOrWhiteSpace(controlPath)) throw new ArgumentException("control path must not be empty", nameof(controlPath));
            _overrides[key] = controlPath;
        }

        public bool RemoveOverride(string key) => _overrides.Remove(key);

        public ControlsOptions Clone()
        {
            var c = new ControlsOptions();
            foreach (KeyValuePair<string, string> kv in _overrides) c._overrides[kv.Key] = kv.Value;
            return c;
        }

        public void ResetToDefaults() => _overrides.Clear();

        public bool SameValues(ControlsOptions o)
        {
            if (o == null || _overrides.Count != o._overrides.Count) return false;
            foreach (KeyValuePair<string, string> kv in _overrides)
            {
                if (!o._overrides.TryGetValue(kv.Key, out string other) || other != kv.Value) return false;
            }
            return true;
        }
    }

    /// <summary>Everything the Options screen edits and the settings file stores.</summary>
    public sealed class GameSettings
    {
        public GraphicsOptions Graphics { get; private set; } = new GraphicsOptions();
        public AudioOptions Audio { get; private set; } = new AudioOptions();
        public GameplayOptions Gameplay { get; private set; } = new GameplayOptions();
        public ControlsOptions Controls { get; private set; } = new ControlsOptions();

        public GameSettings Clone() => new GameSettings
        {
            Graphics = Graphics.Clone(),
            Audio = Audio.Clone(),
            Gameplay = Gameplay.Clone(),
            Controls = Controls.Clone(),
        };

        public void ResetToDefaults()
        {
            Graphics.ResetToDefaults();
            Audio.ResetToDefaults();
            Gameplay.ResetToDefaults();
            Controls.ResetToDefaults();
        }

        public bool SameValues(GameSettings o) =>
            o != null && Graphics.SameValues(o.Graphics) && Audio.SameValues(o.Audio) && Gameplay.SameValues(o.Gameplay) && Controls.SameValues(o.Controls);
    }
}
