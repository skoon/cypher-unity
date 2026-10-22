using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary><c>--shots DIR</c>: walk through the menus and save a screenshot of each into DIR, then quit. For checking how the screens look in a real player.</summary>
    public sealed class ShotsOptions
    {
        public const string Flag = "--shots";

        /// <summary>Where the screenshots go.</summary>
        public string Directory { get; }

        public ShotsOptions(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("a screenshot folder is needed", nameof(directory));
            Directory = directory;
        }

        /// <summary>The options for this launch, or null when the switch is absent. The switch without a folder is an error.</summary>
        public static ShotsOptions Parse(IReadOnlyList<string> args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] != Flag) continue;
                if (i + 1 >= args.Count || args[i + 1].StartsWith("-", StringComparison.Ordinal)) throw new ArgumentException($"{Flag} needs a folder");
                return new ShotsOptions(args[i + 1]);
            }
            return null;
        }
    }
}
