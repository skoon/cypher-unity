using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Cypher.EditorTools
{
    /// <summary>
    /// Builds the Slice scene as a Windows Development player for the frame-rate gate. Command line:
    ///   Unity.exe -batchmode -quit -projectPath ... -executeMethod Cypher.EditorTools.PlayerBuilder.BuildFromCommandLine [-buildOutput PATH] [-buildRelease]
    /// -buildRelease leaves out the Development flag, which also compiles the game's scripts with optimisation: the sound's cost is measured in both, since the audio thread's loops are hot.
    /// The scene must already exist (SliceSceneBuilder.Build). Exit code 0 only when the build succeeded.
    /// </summary>
    public static class PlayerBuilder
    {
        public const string DefaultOutput = "Builds/Dev/Cypher.exe";

        [MenuItem("Cypher/Build Development Player")]
        public static void BuildFromMenu() => Build(Path.GetFullPath(DefaultOutput));

        public static void BuildFromCommandLine()
        {
            string output = Arg("-buildOutput") ?? Path.GetFullPath(DefaultOutput);
            bool release = Array.IndexOf(Environment.GetCommandLineArgs(), "-buildRelease") >= 0;
            BuildReport report = null;
            try
            {
                report = Build(output, release);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public static BuildReport Build(string outputPath, bool release = false)
        {
            if (!File.Exists(SliceSceneBuilder.ScenePath))
            {
                throw new FileNotFoundException($"{SliceSceneBuilder.ScenePath} does not exist; run Cypher/Build Slice Scene first.");
            }

            // The benchmark reads CPU and GPU frame times from FrameTimingManager, which stays off without this.
            PlayerSettings.enableFrameTimingStats = true;

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SliceSceneBuilder.ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = release ? BuildOptions.None : BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[Cypher] {(release ? "release" : "development")} player build {summary.result}: {summary.totalErrors} errors, {summary.totalWarnings} warnings, " +
                      $"{summary.totalSize / (1024 * 1024)} MB in {summary.totalTime.TotalSeconds:0} s -> {summary.outputPath}");
            if (summary.result != BuildResult.Succeeded) Debug.LogError($"[Cypher] player build did not succeed: {summary.result}");
            return report;
        }

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
