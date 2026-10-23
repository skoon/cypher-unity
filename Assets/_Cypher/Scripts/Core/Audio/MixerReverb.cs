namespace Cypher.Core
{
    /// <summary>The settings of the mixer's reverb (Unity's "SFX Reverb"): the effect's own parameter names, the exposed names the game sets them by, and the starting values.</summary>
    public static class MixerReverb
    {
        public readonly struct Setting
        {
            public readonly string Effect;
            /// <summary>The name the mixer exposes it as, or null if it is fixed.</summary>
            public readonly string Exposed;
            public readonly double Default;

            public Setting(string effect, string exposed, double @default)
            {
                Effect = effect;
                Exposed = exposed;
                Default = @default;
            }
        }

        /// <summary>The effect's parameters in the order the editor lists them. Levels are in millibels, times in seconds, the reference frequencies in hertz, diffusion and density in percent.</summary>
        public static readonly Setting[] Settings =
        {
            new Setting("Dry Level", null, -10000),
            new Setting("Room", "ReverbRoom", -1000),
            new Setting("Room HF", "ReverbRoomHF", -400),
            new Setting("Decay Time", "ReverbDecay", 1.75),
            new Setting("Decay HF Ratio", "ReverbDecayHF", 0.5),
            new Setting("Reflections", "ReverbReflections", -1200),
            new Setting("Reflect Delay", "ReverbReflectDelay", 0.019),
            new Setting("Reverb", "ReverbLate", 0),
            new Setting("Reverb Delay", "ReverbLateDelay", 0.04),
            new Setting("Diffusion", null, 100),
            new Setting("Density", null, 100),
            new Setting("HF Reference", "ReverbHFReference", 5000),
            new Setting("Room LF", null, 0),
            new Setting("LF Reference", null, 250),
        };

        /// <summary>The effect's parameter names in order, as the mixer file lists them.</summary>
        public static string[] EffectParameters
        {
            get
            {
                var names = new string[Settings.Length];
                for (int i = 0; i < names.Length; i++) names[i] = Settings[i].Effect;
                return names;
            }
        }
    }
}
