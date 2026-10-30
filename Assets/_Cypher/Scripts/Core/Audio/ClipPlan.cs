using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>One sound to make ahead of time: what to call it, which recipe, which take (seed), and what it is told.</summary>
    public sealed class ClipSpec
    {
        public string Key;
        public string Sound;
        public uint Seed;
        public VoiceParams Params;
    }

    /// <summary>
    /// The sounds that are made while the game loads: the menu sounds, the player's footsteps on every surface at every pace and wetness, jumps, landings and breaths, and the city's ambient one-shots,
    /// a few takes of each so that the same step does not play twice in a row. Rendering is on worker threads. Which take plays is chosen at random but never the one just played.
    /// </summary>
    public static class ClipPlan
    {
        public const int UiTakes = 2;
        public const int StepTakes = 3;
        public const int LandTakes = 2;
        public const int JumpTakes = 3;
        public const int BreathTakes = 2;

        /// <summary>How wet the ground is, in the three steps a footstep is made at.</summary>
        public static readonly double[] WetLevels = { 0, 0.45, 0.85 };

        public static readonly double[] LandImpacts = { 0.35, 0.65, 1.0 };

        public static readonly double[] BreathEfforts = { 0, 0.35, 0.7, 1.0 };

        public static readonly string[] AmbientSounds = { "amb.siren", "amb.rumbleimpact", "amb.drip", "amb.voice", "amb.neonbuzz", "amb.carpass" };

        public static int AmbientTakes(string sound)
        {
            switch (sound)
            {
                case "amb.drip": return 6;
                case "amb.voice": return 4;
                case "amb.neonbuzz": return 4;
                default: return 3;
            }
        }

        public enum Stance { Walk, Sprint, Crouch }

        /// <summary>The step of wetness a ground wetness (0 to 1) is made at: dry, damp or soaked.</summary>
        public static int WetBucket(double wetness) => wetness < 0.15 ? 0 : wetness < 0.65 ? 1 : 2;

        public static int ImpactBucket(double impact)
        {
            int best = 0;
            for (int i = 1; i < LandImpacts.Length; i++)
            {
                if (Math.Abs(LandImpacts[i] - impact) < Math.Abs(LandImpacts[best] - impact)) best = i;
            }
            return best;
        }

        public static int EffortBucket(double effort)
        {
            int best = 0;
            for (int i = 1; i < BreathEfforts.Length; i++)
            {
                if (Math.Abs(BreathEfforts[i] - effort) < Math.Abs(BreathEfforts[best] - effort)) best = i;
            }
            return best;
        }

        public static string StepKey(string surface, Stance stance, int wetBucket, int take) =>
            surface == "water" ? $"foot.water|{stance}|{take}" : $"foot.{surface}|{stance}|w{wetBucket}|{take}";

        public static string LandKey(string surface, int impactBucket, int wetBucket, int take) => $"land|{surface}|i{impactBucket}|w{(wetBucket > 0 ? 1 : 0)}|{take}";

        public static string BreathKey(bool inhale, int effortBucket, int take) => $"breath|{(inhale ? "in" : "out")}|e{effortBucket}|{take}";

        public static string JumpKey(int take) => $"jump|{take}";

        public static string UiKey(string sound, int take) => $"{sound}|{take}";

        public static string AmbientKey(string sound, int take) => $"{sound}|{take}";

        static uint SeedOf(string key)
        {
            // a stable hash of the key (not string.GetHashCode, which differs between runs), so a take is the same take every time the game starts
            uint h = 2166136261;
            foreach (char c in key)
            {
                h ^= c;
                h *= 16777619;
            }
            return h;
        }

        static readonly string[] UiSounds = { "ui.hover", "ui.tick", "ui.select", "ui.confirm", "ui.back", "ui.open", "ui.close", "ui.error", "ui.alert", "ui.notify" };

        public static IReadOnlyList<string> UiSoundNames => UiSounds;

        /// <summary>The menu sounds.</summary>
        public static IEnumerable<ClipSpec> Ui()
        {
            foreach (string sound in UiSounds)
            {
                for (int take = 0; take < UiTakes; take++)
                {
                    string key = UiKey(sound, take);
                    yield return new ClipSpec { Key = key, Sound = sound, Seed = SeedOf(key), Params = new VoiceParams() };
                }
            }
        }

        /// <summary>Footsteps at every surface, pace and wetness; jumps, landings and breaths.</summary>
        public static IEnumerable<ClipSpec> Foley()
        {
            foreach (SoundBuilders.Surface surface in SoundBuilders.Surfaces)
            {
                bool water = surface.Name == "water";
                foreach (Stance stance in new[] { Stance.Walk, Stance.Sprint, Stance.Crouch })
                {
                    for (int wet = 0; wet < (water ? 1 : WetLevels.Length); wet++)
                    {
                        for (int take = 0; take < StepTakes; take++)
                        {
                            string key = StepKey(surface.Name, stance, wet, take);
                            yield return new ClipSpec
                            {
                                Key = key, Sound = "foot." + surface.Name, Seed = SeedOf(key),
                                Params = new VoiceParams { Sprint = stance == Stance.Sprint, Crouch = stance == Stance.Crouch, Wet = water ? 0 : WetLevels[wet] },
                            };
                        }
                    }
                }
            }
            for (int take = 0; take < JumpTakes; take++)
            {
                string key = JumpKey(take);
                yield return new ClipSpec { Key = key, Sound = "player.jump", Seed = SeedOf(key), Params = new VoiceParams() };
            }
            foreach (SoundBuilders.Surface surface in SoundBuilders.Surfaces)
            {
                for (int impact = 0; impact < LandImpacts.Length; impact++)
                {
                    for (int wet = 0; wet < 2; wet++)
                    {
                        for (int take = 0; take < LandTakes; take++)
                        {
                            string key = LandKey(surface.Name, impact, wet, take);
                            yield return new ClipSpec
                            {
                                Key = key, Sound = "player.land", Seed = SeedOf(key),
                                Params = new VoiceParams { Impact = LandImpacts[impact], Surface = surface.Name, Wet = wet == 0 ? 0 : WetLevels[2] },
                            };
                        }
                    }
                }
            }
            foreach (bool inhale in new[] { true, false })
            {
                for (int effort = 0; effort < BreathEfforts.Length; effort++)
                {
                    for (int take = 0; take < BreathTakes; take++)
                    {
                        string key = BreathKey(inhale, effort, take);
                        yield return new ClipSpec { Key = key, Sound = "player.breath", Seed = SeedOf(key), Params = new VoiceParams { Inhale = inhale, Effort = BreathEfforts[effort] } };
                    }
                }
            }
        }

        /// <summary>The city's ambient one-shots.</summary>
        public static IEnumerable<ClipSpec> Ambient()
        {
            foreach (string sound in AmbientSounds)
            {
                for (int take = 0; take < AmbientTakes(sound); take++)
                {
                    string key = AmbientKey(sound, take);
                    yield return new ClipSpec { Key = key, Sound = sound, Seed = SeedOf(key), Params = new VoiceParams { Wet = sound == "amb.carpass" ? 0.8 : (double?)null } };
                }
            }
        }

        /// <summary>The guns' sounds the game plays (the three guns' shots, the crack, the mechanics, brass, and the three impacts) and how many takes of each.</summary>
        public static readonly IReadOnlyList<(string Sound, int Takes)> WeaponSounds = new[]
        {
            ("weapon.smg", 4), ("weapon.revolver", 4), ("weapon.energy", 3), ("weapon.crack", 3), ("weapon.dryfire", 2), ("weapon.switch", 2), ("weapon.reload", 2),
            ("weapon.shell", 4), ("impact.concrete", 4), ("impact.metal", 4), ("impact.flesh", 3),
        };

        public static string WeaponKey(string sound, int take) => $"{sound}|{take}";

        /// <summary>The guns' clips.</summary>
        public static IEnumerable<ClipSpec> Weapons()
        {
            foreach ((string sound, int takes) in WeaponSounds)
            {
                for (int take = 0; take < takes; take++)
                {
                    string key = WeaponKey(sound, take);
                    yield return new ClipSpec { Key = key, Sound = sound, Seed = SeedOf(key), Params = new VoiceParams() };
                }
            }
        }

        public static IEnumerable<ClipSpec> All()
        {
            foreach (ClipSpec s in Ui()) yield return s;
            foreach (ClipSpec s in Foley()) yield return s;
            foreach (ClipSpec s in Ambient()) yield return s;
            foreach (ClipSpec s in Weapons()) yield return s;
        }
    }

    /// <summary>Picks one of several takes at random but never the one picked last for the same sound.</summary>
    public sealed class TakePicker
    {
        readonly Rng _rng;
        readonly Dictionary<string, int> _last = new Dictionary<string, int>();

        public TakePicker(uint seed)
        {
            _rng = new Rng(seed);
        }

        public int Pick(string family, int takes)
        {
            if (takes < 1) throw new ArgumentOutOfRangeException(nameof(takes), takes, "at least one take");
            if (takes == 1) return 0;
            int last = _last.TryGetValue(family, out int l) ? l : -1;
            int pick = (int)(_rng.Next() * (takes - (last >= 0 ? 1 : 0)));
            if (last >= 0 && pick >= last) pick++;
            _last[family] = pick;
            return pick;
        }
    }
}
