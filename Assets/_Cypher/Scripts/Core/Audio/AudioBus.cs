using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>The five mixing buses (the prototype's BUSES); the master bus sits over all of them.</summary>
    public enum AudioBus
    {
        Ambience,
        Sfx,
        Ui,
        Music,
        Voice,
    }

    /// <summary>
    /// The buses as the mixer has them. A sound's reverb send is a property of the sound (the prototype gives each its own), but a mixer group has one send level, so each bus is a family of groups,
    /// one for every send <see cref="Tier"/>: the dry one with no send, then six that feed the reverb at rising levels. A sound plays through the group of the tier nearest the send it wants.
    /// </summary>
    public static class AudioBuses
    {
        /// <summary>The quietest level the mixer takes: a linear gain of zero is this.</summary>
        public const double SilenceDecibels = -80;

        /// <summary>The send level (dB) of each tier above the dry one, which has none. The recipes ask for sends of 0.05 to 0.9, so this spans -26 to -1 dB.</summary>
        static readonly double[] TierDecibels = { double.NegativeInfinity, -24, -18, -13, -9, -5, -1.5 };

        static readonly AudioBus[] Every = { AudioBus.Ambience, AudioBus.Sfx, AudioBus.Ui, AudioBus.Music, AudioBus.Voice };

        // the names are asked for every frame, so they are made once
        static readonly string[,] GroupNames = MakeNames(""), VolumeNames = MakeNames("Vol");

        static string[,] MakeNames(string suffix)
        {
            var names = new string[Every.Length, TierDecibels.Length];
            foreach (AudioBus bus in Every)
            {
                for (int tier = 0; tier < TierDecibels.Length; tier++) names[(int)bus, tier] = (tier == 0 ? bus.ToString() : bus + "Wet" + tier) + suffix;
            }
            return names;
        }

        static void CheckBusAndTier(AudioBus bus, int tier)
        {
            if ((uint)bus >= (uint)Every.Length) throw new ArgumentOutOfRangeException(nameof(bus), bus, "unknown bus");
            if (tier < 0 || tier >= TierDecibels.Length) throw new ArgumentOutOfRangeException(nameof(tier), tier, "no such send tier");
        }

        public static IReadOnlyList<AudioBus> All => Every;

        public static int TierCount => TierDecibels.Length;

        /// <summary>The send (dB) a tier feeds the reverb at; <see cref="double.NegativeInfinity"/> for the dry tier.</summary>
        public static double TierSendDecibels(int tier)
        {
            if (tier < 0 || tier >= TierDecibels.Length) throw new ArgumentOutOfRangeException(nameof(tier), tier, "no such send tier");
            return TierDecibels[tier];
        }

        /// <summary>The tier whose send is nearest (in dB) the wanted linear send; a send under -40 dB is dry.</summary>
        public static int TierFor(double sendGain)
        {
            if (double.IsNaN(sendGain) || sendGain < 0) throw new ArgumentOutOfRangeException(nameof(sendGain), sendGain, "a send cannot be negative");
            if (sendGain < 0.01) return 0;
            double db = 20 * Math.Log10(sendGain);
            int best = 1;
            for (int t = 2; t < TierDecibels.Length; t++)
            {
                if (Math.Abs(TierDecibels[t] - db) < Math.Abs(TierDecibels[best] - db)) best = t;
            }
            return best;
        }

        /// <summary>The mixer group's name for a bus at a send tier ("Sfx", "SfxWet3").</summary>
        public static string GroupName(AudioBus bus, int tier = 0)
        {
            CheckBusAndTier(bus, tier);
            return GroupNames[(int)bus, tier];
        }

        /// <summary>The name of the exposed volume parameter of a bus's group at a tier.</summary>
        public static string VolumeParameter(AudioBus bus, int tier = 0)
        {
            CheckBusAndTier(bus, tier);
            return VolumeNames[(int)bus, tier];
        }

        /// <summary>The group the reverb's wet signal is returned through, and its exposed volume.</summary>
        public const string ReverbGroupName = "Reverb";

        public const string ReverbVolumeParameter = "ReverbVol";

        /// <summary>Decibels for a linear gain, floored at <see cref="SilenceDecibels"/>.</summary>
        public static double ToDecibels(double gain)
        {
            if (double.IsNaN(gain) || gain < 0) throw new ArgumentOutOfRangeException(nameof(gain), gain, "a gain cannot be negative");
            return gain <= 1e-4 ? SilenceDecibels : Math.Max(SilenceDecibels, 20 * Math.Log10(gain));
        }

        public static double ToGain(double decibels) => decibels <= SilenceDecibels ? 0 : Math.Pow(10, decibels / 20);
    }
}
