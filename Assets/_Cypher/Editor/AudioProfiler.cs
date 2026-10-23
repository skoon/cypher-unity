using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cypher.Core;
using UnityEditor;
using UnityEngine;

namespace Cypher.EditorTools
{
    /// <summary>
    /// Where the audio thread's time goes, measured on this machine in the Editor's own runtime: each live graph (the bed and its two emitters, a car, the score) rendered as the game renders them
    /// with the cost of each kind of node counted, and the master stage on a mix. Cypher > Profile Audio, or in batch mode with -executeMethod Cypher.EditorTools.AudioProfiler.ProfileFromCommandLine.
    /// </summary>
    public static class AudioProfiler
    {
        const int Rate = 48000;
        // a device block of the project's audio settings, in graph blocks
        const int BlocksPerCallback = 1024 / SynthGraph.Quantum;
        // long enough for the weather to arrive: the bed moves toward it over a few seconds' time constant
        const int WarmCallbacks = 2000, MeasuredCallbacks = 600;

        [MenuItem("Cypher/Profile Audio")]
        public static void ProfileFromMenu() => Debug.Log(Profile());

        public static void ProfileFromCommandLine()
        {
            Debug.Log(Profile());
        }

        public static string Profile()
        {
            var noise = new AudioNoiseBank(Rate);
            var report = new StringBuilder("[Cypher] audio profile (ms per 1024-frame callback; a block lasts 21.3 ms)\n");

            var bed = new Bed(noise, new Rng(1), 0, Bed.EmitterMode.Separate);
            var weather = new BedState(NightRig.DefaultRain, RainModel.DefaultWind, 0, 1);
            bed.Advance(0, weather);
            bed.AdvanceNeon(0, weather);
            bed.AdvanceAirConditioning(0, weather);
            foreach ((string name, SynthGraph graph) in new[] { ("bed", bed.Main), ("neon", bed.Neon), ("air conditioning", bed.AirConditioning) })
            {
                graph.CoefficientInterval = 32;
                Measure(report, name, graph);
            }

            var car = new CarVoiceGraph(noise, new Rng(77));
            car.Apply(VehicleSound.For(12, 4.5, 0.5), 1);
            Measure(report, "one car", car.Graph);

            var scoreProfile = new GraphProfile();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            ScoreRender.Render(noise, 1, 30, 0, out _, out _, scoreProfile);
            Append(report, "score (calm, 30 s; the notes' own rendering is not counted)", scoreProfile);

            report.Append(MeasureMaster());
            return report.ToString();
        }

        static void Measure(StringBuilder report, string name, SynthGraph graph)
        {
            graph.Render(WarmCallbacks * 1024, out _, out _);
            var profile = new GraphProfile();
            graph.Profile = profile;
            graph.Render(MeasuredCallbacks * 1024, out _, out _);
            graph.Profile = null;
            Append(report, name, profile);
        }

        static void Append(StringBuilder report, string name, GraphProfile profile)
        {
            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            double callbacks = profile.Blocks / (double)BlocksPerCallback;
            double total = profile.Ticks.Values.Sum() * toMs / callbacks;
            report.AppendFormat("  {0}: {1:0.000} ms\n", name, total);
            foreach (KeyValuePair<string, long> kind in profile.Ticks.OrderByDescending(k => k.Value))
            {
                double perCallbackRuns = profile.Runs[kind.Key] / callbacks / BlocksPerCallback;
                report.AppendFormat("      {0,-26} {1,3:0} nodes {2,8:0.000} ms ({3,4:0}%)\n", kind.Key, perCallbackRuns, kind.Value * toMs / callbacks, 100.0 * kind.Value / profile.Ticks.Values.Sum());
            }
        }

        static string MeasureMaster()
        {
            var stage = new MasterStage(Rate);
            var rng = new Rng(3);
            var data = new float[2048];
            for (int i = 0; i < data.Length; i++) data[i] = (float)((rng.Next() - 0.5) * 0.8);
            var copy = new float[data.Length];
            for (int i = 0; i < WarmCallbacks; i++)
            {
                Array.Copy(data, copy, data.Length);
                stage.Process(copy, 2, 1024);
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < MeasuredCallbacks; i++)
            {
                Array.Copy(data, copy, data.Length);
                stage.Process(copy, 2, 1024);
            }
            return $"  master stage (glue, limiter, volume; a loud mix): {clock.Elapsed.TotalMilliseconds / MeasuredCallbacks:0.000} ms\n";
        }
    }
}
