using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// The synthesis engine against Chromium's Web Audio on the same small graphs (tools/audio-ref/gen-golden-synth.mjs renders them there). Oscillators, noise loops, filters, delays (loops
    /// included), panning and every kind of parameter schedule agree to the precision of single-precision arithmetic, which is what the sounds are made from. (Not compared: a long
    /// exponential sweep of an oscillator's frequency. Chromium works the ramp out by repeated multiplication in single precision, which drifts a percent over a few seconds; the
    /// engine works it out exactly. And an oscillator that starts part-way through a block of 128 frames takes the first frames' frequency from the start of the block.)
    /// </summary>
    public class AudioSynthGoldenTests
    {
        /// <summary>How far a rendering may differ from Chromium's, by scenario; everything not listed must agree to 3e-5.</summary>
        static double Tolerance(string name)
        {
            // Chromium's wave tables and ours interpolate a little differently, and a crowded sawtooth shows it most
            if (name.StartsWith("osc-saw-")) return 2e-4;
            // a frequency in hertz, not a level
            if (name == "param-envelope-sweep") return 2e-3;
            // Chromium reads a fractional delay to 1/256 of a frame
            if (name == "delay-fractional") return 1e-3;
            // the compressor works in single precision in Chromium, through an envelope that feeds back on itself
            if (name.StartsWith("comp-")) return 5e-4;
            if (name == "src-white-rate-automated" || name == "param-target-then-target" || name == "param-cancel-then-set") return 1e-4;
            return 3e-5;
        }

        [Test]
        public void EveryScenarioAgreesWithChromium()
        {
            List<SynthScenarios.Scenario> scenarios = SynthScenarios.Load();
            Assert.GreaterOrEqual(scenarios.Count, 80);
            var failures = new List<string>();
            foreach (SynthScenarios.Scenario s in scenarios)
            {
                SynthScenarios.Render(s, 1, out float[] left, out float[] right);
                double tol = Tolerance(s.Name);
                double e = SynthScenarios.MaxError(left, s.Expected, s.Stride, out int at);
                if (!(e <= tol)) failures.Add($"{s.Name}: differs by {e:E2} at frame {at} (allowed {tol:E0})");
                if (s.ExpectedRight != null)
                {
                    double er = SynthScenarios.MaxError(right, s.ExpectedRight, s.Stride, out int atR);
                    if (!(er <= tol)) failures.Add($"{s.Name} (right): differs by {er:E2} at frame {atR} (allowed {tol:E0})");
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void TheScenariosAreNotSilent()
        {
            // an agreement on silence would prove nothing
            var silent = new List<string>();
            foreach (SynthScenarios.Scenario s in SynthScenarios.Load())
            {
                float peak = 0;
                foreach (float v in s.Expected) peak = Math.Max(peak, Math.Abs(v));
                bool allowedQuiet = s.Name.EndsWith("-zero") || s.Name.Contains("above-nyquist") || s.Name == "pan-right";
                if (peak < 1e-3f && !allowedQuiet) silent.Add(s.Name);
            }
            Assert.IsEmpty(silent, string.Join(", ", silent));
        }

        [Test]
        public void ADefaultGraphUsesTheFinestCoefficientIntervalAndCoarserOnesStayClose()
        {
            Assert.AreEqual(1, new SynthGraph(48000).CoefficientInterval);
            // a live graph with slow modulators may update less often: on a fast sweep the result stays within about a percent of the exact one
            SynthScenarios.Scenario sweep = SynthScenarios.Load().Find(x => x.Name == "biquad-sweep-lowpass");
            SynthScenarios.Render(sweep, 8, out float[] coarse, out _);
            Assert.Less(SynthScenarios.MaxError(coarse, sweep.Expected, sweep.Stride, out _), 0.02);
        }
    }
}
