using System;
using System.IO;
using Cypher.Core;
using UnityEditor;
using UnityEngine;

namespace Cypher.EditorTools
{
    /// <summary>Writes every sound, the beds and the score as WAV files to listen to (see <see cref="AudioExport"/>): Cypher > Export Audio Samples, or in batch mode with -exportDir.</summary>
    public static class AudioExporter
    {
        public static string DefaultDirectory => Path.Combine(Path.GetTempPath(), "cypher-unity", "audio-export");

        [MenuItem("Cypher/Export Audio Samples")]
        public static void ExportFromMenu()
        {
            string directory = DefaultDirectory;
            Export(directory);
            EditorUtility.RevealInFinder(Path.Combine(directory, AudioExport.ReportFile));
        }

        /// <summary>For batch mode: -executeMethod Cypher.EditorTools.AudioExporter.ExportFromCommandLine [-exportDir path] [-bedSeconds n] [-scoreSeconds n].</summary>
        public static void ExportFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Arg(string name) { int at = Array.IndexOf(args, name); return at >= 0 && at + 1 < args.Length ? args[at + 1] : null; }
            var options = new ExportOptions { SampleRate = AudioSettings.outputSampleRate };
            if (Arg("-bedSeconds") != null) options.BedSeconds = double.Parse(Arg("-bedSeconds"), System.Globalization.CultureInfo.InvariantCulture);
            if (Arg("-scoreSeconds") != null) options.ScoreSeconds = double.Parse(Arg("-scoreSeconds"), System.Globalization.CultureInfo.InvariantCulture);
            Export(Arg("-exportDir") ?? DefaultDirectory, options);
        }

        static void Export(string directory) => Export(directory, new ExportOptions { SampleRate = AudioSettings.outputSampleRate });

        static void Export(string directory, ExportOptions options)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var files = AudioExport.Run(directory, options);
            Debug.Log($"[Cypher] audio export: {files.Count} files to {directory} in {clock.Elapsed.TotalSeconds:0.0} s");
        }
    }
}
