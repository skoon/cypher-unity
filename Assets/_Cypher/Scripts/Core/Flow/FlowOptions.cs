using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Command-line switches that change how the game starts: <c>--skip-title</c> goes straight into the game,
    /// <c>--default-settings</c> ignores the player's settings file (and never writes one), <c>--no-auto-pause</c> keeps playing
    /// when the window loses focus. <c>--benchmark</c> implies all three, so a measurement never depends on, or alters, the
    /// machine owner's settings; <c>--shots</c> (the screenshot walk) implies the last two. <c>--no-crowd</c> leaves the pavements empty and <c>--no-traffic</c> the roads and
    /// the sky, <c>--no-vehicle-lights</c> the point lights on the vehicles, and <c>--no-audio</c> every sound (controls for measuring what the people, the traffic, its lights and the audio cost; implied by nothing). Unknown arguments are ignored.
    /// </summary>
    public sealed class FlowOptions
    {
        public const string SkipTitleFlag = "--skip-title";
        public const string DefaultSettingsFlag = "--default-settings";
        public const string NoAutoPauseFlag = "--no-auto-pause";
        public const string NoCrowdFlag = "--no-crowd";
        public const string NoTrafficFlag = "--no-traffic";
        public const string NoVehicleLightsFlag = "--no-vehicle-lights";
        public const string NoAudioFlag = "--no-audio";

        public bool SkipTitle { get; }
        public bool IgnoreUserSettings { get; }
        public bool NoAutoPause { get; }

        /// <summary>No pedestrians, whatever the quality tier says.</summary>
        public bool NoCrowd { get; }

        /// <summary>No vehicles, aircraft or signals, whatever the quality tier says.</summary>
        public bool NoTraffic { get; }

        /// <summary>The vehicles carry no point lights, whatever the quality tier says.</summary>
        public bool NoVehicleLights { get; }

        /// <summary>No sound at all: no bed, no score, no menu sounds, no mixer work. A control for measuring the audio's cost.</summary>
        public bool NoAudio { get; }

        public FlowOptions(bool skipTitle = false, bool ignoreUserSettings = false, bool noAutoPause = false, bool noCrowd = false, bool noTraffic = false, bool noVehicleLights = false, bool noAudio = false)
        {
            SkipTitle = skipTitle;
            IgnoreUserSettings = ignoreUserSettings;
            NoAutoPause = noAutoPause;
            NoCrowd = noCrowd;
            NoTraffic = noTraffic;
            NoVehicleLights = noVehicleLights;
            NoAudio = noAudio;
        }

        public static FlowOptions Parse(IReadOnlyList<string> args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            bool benchmark = false, shots = false, skip = false, defaults = false, noPause = false, noCrowd = false, noTraffic = false, noLights = false, noAudio = false;
            foreach (string a in args)
            {
                if (a == BenchmarkOptions.BenchmarkFlag) benchmark = true;
                else if (a == ShotsOptions.Flag) shots = true;
                else if (a == SkipTitleFlag) skip = true;
                else if (a == DefaultSettingsFlag) defaults = true;
                else if (a == NoAutoPauseFlag) noPause = true;
                else if (a == NoCrowdFlag) noCrowd = true;
                else if (a == NoTrafficFlag) noTraffic = true;
                else if (a == NoVehicleLightsFlag) noLights = true;
                else if (a == NoAudioFlag) noAudio = true;
            }
            return new FlowOptions(skip || benchmark, defaults || benchmark || shots, noPause || benchmark || shots, noCrowd, noTraffic, noLights, noAudio);
        }
    }
}
