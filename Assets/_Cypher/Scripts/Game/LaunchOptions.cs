using System;
using Cypher.Core;

namespace Cypher.Game
{
    /// <summary>The command-line switches this launch was started with, in one place so tests can stand in for the command line.</summary>
    public static class LaunchOptions
    {
        /// <summary>Tests set this instead of the command line; null means read the real one.</summary>
        public static FlowOptions Override { get; set; }

        public static FlowOptions Current => Override ?? FlowOptions.Parse(Environment.GetCommandLineArgs());
    }
}
