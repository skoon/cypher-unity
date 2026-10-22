using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Command-line switches that change how the game starts: <c>--skip-title</c> goes straight into the game,
    /// <c>--default-settings</c> ignores the player's settings file (and never writes one), <c>--no-auto-pause</c> keeps playing
    /// when the window loses focus. <c>--benchmark</c> implies all three, so a measurement never depends on, or alters, the
    /// machine owner's settings; <c>--shots</c> (the screenshot walk) implies the last two. Unknown arguments are ignored.
    /// </summary>
    public sealed class FlowOptions
    {
        public const string SkipTitleFlag = "--skip-title";
        public const string DefaultSettingsFlag = "--default-settings";
        public const string NoAutoPauseFlag = "--no-auto-pause";

        public bool SkipTitle { get; }
        public bool IgnoreUserSettings { get; }
        public bool NoAutoPause { get; }

        public FlowOptions(bool skipTitle = false, bool ignoreUserSettings = false, bool noAutoPause = false)
        {
            SkipTitle = skipTitle;
            IgnoreUserSettings = ignoreUserSettings;
            NoAutoPause = noAutoPause;
        }

        public static FlowOptions Parse(IReadOnlyList<string> args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            bool benchmark = false, shots = false, skip = false, defaults = false, noPause = false;
            foreach (string a in args)
            {
                if (a == BenchmarkOptions.BenchmarkFlag) benchmark = true;
                else if (a == ShotsOptions.Flag) shots = true;
                else if (a == SkipTitleFlag) skip = true;
                else if (a == DefaultSettingsFlag) defaults = true;
                else if (a == NoAutoPauseFlag) noPause = true;
            }
            return new FlowOptions(skip || benchmark, defaults || benchmark || shots, noPause || benchmark || shots);
        }
    }
}
