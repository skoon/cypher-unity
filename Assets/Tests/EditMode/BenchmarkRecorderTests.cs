using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class BenchmarkRecorderTests
    {
        static FrameSample Frame(double ms, double x = 0, double z = 0, double cpu = 0, double gpu = 0, long draws = -1, long tris = -1, double t = 0)
            => new FrameSample(t, ms, cpu, gpu, draws, tris, x, z);

        /// <summary>A recorder fed n frames of the given length while walking along +X at walking pace.</summary>
        static BenchmarkRecorder Walking(int n, double ms, Func<int, double> frameMs = null)
        {
            var r = new BenchmarkRecorder();
            double x = 0;
            for (int i = 0; i < n; i++)
            {
                double m = frameMs != null ? frameMs(i) : ms;
                x += PlayerMotor.WalkSpeed * m / 1000.0;
                r.Add(Frame(m, x));
            }
            return r;
        }

        [Test]
        public void NoFramesIsInvalidNotAPass()
        {
            BenchmarkSummary s = new BenchmarkRecorder().Summarise();
            Assert.AreEqual(BenchmarkVerdict.Invalid, s.Verdict);
            Assert.AreEqual(0, s.Frames);
        }

        [Test]
        public void SteadyFramesGiveTheExpectedNumbers()
        {
            BenchmarkSummary s = Walking(600, 20.0).Summarise();   // 20 ms = 50 FPS
            Assert.AreEqual(600, s.Frames);
            Assert.AreEqual(12.0, s.DurationSeconds, 1e-9);
            Assert.AreEqual(50.0, s.AverageFps, 1e-9);
            Assert.AreEqual(20.0, s.AverageMs, 1e-9);
            Assert.AreEqual(20.0, s.P50Ms, 1e-9);
            Assert.AreEqual(20.0, s.P99Ms, 1e-9);
            Assert.AreEqual(20.0, s.WorstMs, 1e-9);
            Assert.AreEqual(0, s.FramesOverBudget);
            Assert.AreEqual(BenchmarkVerdict.Pass, s.Verdict);
        }

        [Test]
        public void TheGateIsP99NotTheMean()
        {
            // 3% of frames at 100 ms: the mean is fine, p99 is not.
            BenchmarkSummary s = Walking(1000, 0, i => i % 100 < 3 ? 100.0 : 10.0).Summarise();
            Assert.Less(s.AverageMs, 33.3);
            Assert.AreEqual(100.0, s.P99Ms, 1e-9);
            Assert.AreEqual(30, s.FramesOverBudget);
            Assert.AreEqual(BenchmarkVerdict.Fail, s.Verdict);
        }

        [Test]
        public void AnIsolatedHitchBelowOnePercentStillPasses()
        {
            BenchmarkSummary s = Walking(1000, 0, i => i == 500 ? 250.0 : 12.0).Summarise();
            Assert.AreEqual(12.0, s.P99Ms, 1e-9);
            Assert.AreEqual(250.0, s.WorstMs, 1e-9);
            Assert.AreEqual(1, s.FramesOverBudget);
            Assert.AreEqual(BenchmarkVerdict.Pass, s.Verdict);
        }

        [Test]
        public void ThirtyFpsExactlyIsAPass()
        {
            BenchmarkSummary s = Walking(300, 1000.0 / 30.0).Summarise();
            Assert.AreEqual(BenchmarkVerdict.Pass, s.Verdict);
            Assert.AreEqual(0, s.FramesOverBudget);
        }

        [Test]
        public void StandingStillIsInvalidWhateverTheFrameRate()
        {
            var r = new BenchmarkRecorder();
            for (int i = 0; i < 600; i++) r.Add(Frame(10, 5, 5));
            BenchmarkSummary s = r.Summarise();
            Assert.AreEqual(0.0, s.DistanceMetres, 1e-9);
            Assert.AreEqual(BenchmarkVerdict.Invalid, s.Verdict);
        }

        [Test]
        public void WalkingLessThanHalfTheExpectedDistanceIsInvalid()
        {
            var r = new BenchmarkRecorder();
            double x = 0;
            for (int i = 0; i < 1000; i++)
            {
                x += PlayerMotor.WalkSpeed * 0.010 * 0.4;   // 40% of a walker's pace
                r.Add(Frame(10, x));
            }
            Assert.AreEqual(BenchmarkVerdict.Invalid, r.Summarise().Verdict);
        }

        [Test]
        public void TimeHeldUpAndDetoursAreInTheSummaryAndItsText()
        {
            BenchmarkSummary s = Walking(600, 20.0).Summarise(stalledSeconds: 1.5, detours: 1);
            Assert.AreEqual(1.5, s.StalledSeconds, 1e-12);
            Assert.AreEqual(1, s.Detours);
            Assert.AreEqual(BenchmarkVerdict.Pass, s.Verdict);
            string text = s.ToText();
            StringAssert.Contains("stalled_s: 1.5", text);
            StringAssert.Contains("detours: 1", text);
            Assert.AreEqual(0, Walking(600, 20.0).Summarise().Detours, "none unless told");
        }

        [Test]
        public void ARunMostlySpentStuckIsInvalidWhateverTheFrameRate()
        {
            BenchmarkRecorder r = Walking(600, 20.0);   // 12 s
            Assert.AreEqual(BenchmarkVerdict.Pass, r.Summarise(stalledSeconds: 12 * BenchmarkRecorder.MaximumStalledShare).Verdict, "exactly the limit passes");
            Assert.AreEqual(BenchmarkVerdict.Invalid, r.Summarise(stalledSeconds: 12 * BenchmarkRecorder.MaximumStalledShare + 0.01).Verdict);
            Assert.AreEqual(BenchmarkVerdict.Invalid, r.Summarise(stalledSeconds: 12, detours: 8).Verdict);
        }

        [Test]
        public void BeingHeldUpStillLeavesASlowRunFailingNotInvalidWhenItIsWithinTheLimit()
        {
            BenchmarkSummary s = Walking(1000, 0, i => i % 100 < 3 ? 100.0 : 10.0).Summarise(stalledSeconds: 1.0, detours: 1);
            Assert.AreEqual(BenchmarkVerdict.Fail, s.Verdict);
        }

        [Test]
        public void ImpossibleHeldUpTimesAreRefused()
        {
            BenchmarkRecorder r = Walking(10, 20.0);
            Assert.Throws<ArgumentOutOfRangeException>(() => r.Summarise(stalledSeconds: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.Summarise(stalledSeconds: double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.Summarise(stalledSeconds: double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.Summarise(detours: -1));
        }

        [Test]
        public void AllocationPerFrameAndTheFramesInWhichTheCollectorRanAreSummarised()
        {
            var r = new BenchmarkRecorder();
            double x = 0;
            long[] bytes = { 1024, 0, 3072, 2048 };
            for (int i = 0; i < 4; i++)
            {
                x += PlayerMotor.WalkSpeed * 0.02;
                r.Add(new FrameSample(i * 0.02, 20, 0, 0, -1, -1, x, 0, bytes[i], i == 2));
            }
            BenchmarkSummary s = r.Summarise();
            Assert.AreEqual(1536, s.AllocAverageBytes, 1e-9);
            Assert.AreEqual(3072, s.AllocMaxBytes);
            Assert.AreEqual(1, s.Collections);
            string text = s.ToText();
            StringAssert.Contains("alloc_kb_avg: 1.50", text);
            StringAssert.Contains("alloc_kb_max: 3.0", text);
            StringAssert.Contains("gc_frames: 1", text);
        }

        [Test]
        public void AllocationNotAvailableReadsAsNotAvailable()
        {
            BenchmarkSummary s = Walking(10, 16.0).Summarise();
            Assert.Less(s.AllocAverageBytes, 0);
            Assert.Less(s.AllocMaxBytes, 0);
            StringAssert.Contains("alloc_kb_avg: n/a", s.ToText());
            Assert.AreEqual(0, s.Collections);
        }

        [Test]
        public void WhatTheSystemsReportEveryFrameIsAveragedAndTheMostIsKept()
        {
            BenchmarkRecorder r = Walking(4, 20.0);
            double[] people = { 20, 40, 60, 40 };
            for (int i = 0; i < people.Length; i++)
            {
                r.AddCounter("people_drawn", people[i]);
                if (i < 2) r.AddCounter("vehicles_drawn", 10);   // reported in some frames only
            }
            BenchmarkSummary s = r.Summarise();
            Assert.AreEqual(2, s.Counters.Count);
            Assert.AreEqual("people_drawn", s.Counters[0].Name, "in the order first reported");
            Assert.AreEqual(40, s.Counters[0].Average, 1e-12);
            Assert.AreEqual(60, s.Counters[0].Max);
            Assert.AreEqual(40, s.Counters[0].Last);
            Assert.AreEqual(10, s.Counters[1].Average, 1e-12, "averaged over the frames it was reported in");
            string text = s.ToText();
            StringAssert.Contains("people_drawn_avg: 40.00", text);
            StringAssert.Contains("people_drawn_max: 60.00", text);
            StringAssert.Contains("vehicles_drawn_avg: 10.00", text);
        }

        [Test]
        public void ACounterNeedsANameAndANumber()
        {
            var r = new BenchmarkRecorder();
            Assert.Throws<ArgumentException>(() => r.AddCounter("", 1));
            Assert.Throws<ArgumentException>(() => r.AddCounter(null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.AddCounter("x", double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.AddCounter("x", double.PositiveInfinity));
        }

        [Test]
        public void TheHitchListHasOnlyTheFramesOverTheBudgetWithWhereAndWhenAndWhetherTheCollectorRan()
        {
            var r = new BenchmarkRecorder();
            r.Add(new FrameSample(1.0, 10, 8, 9, -1, -1, 5, 6, 100, false));
            r.Add(new FrameSample(1.2, 48.5, 40, 12, -1, -1, 7.25, -8.5, 90000, true));
            r.Add(new FrameSample(1.3, 33.3, 0, 0, -1, -1, 8, 9));   // exactly at the budget is within it
            r.Add(new FrameSample(1.9, 36, 20, 30, -1, -1, 9, 10));
            string[] lines = r.HitchesCsv().TrimEnd('\n').Split('\n');
            Assert.AreEqual(BenchmarkRecorder.HitchesHeader, lines[0]);
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("1.200,48.500,40.000,12.000,7.25,-8.50,90000,1", lines[1]);
            Assert.AreEqual("1.900,36.000,20.000,30.000,9.00,10.00,-1,0", lines[2]);
            Assert.AreEqual(1, new BenchmarkRecorder().HitchesCsv().TrimEnd('\n').Split('\n').Length, "just the header with no frames");
        }

        [Test]
        public void DistanceSumsStraightLegsBetweenSamples()
        {
            var r = new BenchmarkRecorder();
            r.Add(Frame(10, 0, 0));
            r.Add(Frame(10, 3, 4));
            r.Add(Frame(10, 3, 10));
            Assert.AreEqual(11.0, r.Summarise().DistanceMetres, 1e-9);
        }

        [Test]
        public void CpuGpuAndRendererCountersAreAveragedOverTheFramesThatHaveThem()
        {
            var r = new BenchmarkRecorder();
            r.Add(Frame(20, 0, cpu: 8, gpu: 15, draws: 100, tris: 10000));
            r.Add(Frame(20, 1, cpu: 0, gpu: 0, draws: -1, tris: -1));   // this one has no timings or counters
            r.Add(Frame(20, 2, cpu: 10, gpu: 25, draws: 200, tris: 30000));
            BenchmarkSummary s = r.Summarise();
            Assert.AreEqual(9.0, s.CpuAverageMs, 1e-9);
            Assert.AreEqual(20.0, s.GpuAverageMs, 1e-9);
            Assert.AreEqual(25.0, s.GpuP99Ms, 1e-9);
            Assert.AreEqual(150.0, s.DrawCallsAverage, 1e-9);
            Assert.AreEqual(200, s.DrawCallsMax);
            Assert.AreEqual(20000.0, s.TrianglesAverage, 1e-9);
            Assert.AreEqual(30000, s.TrianglesMax);
        }

        [Test]
        public void ImplausibleCpuAndGpuTimingsAreIgnored()
        {
            var r = new BenchmarkRecorder();
            r.Add(Frame(20, 0, cpu: 8, gpu: 10));
            r.Add(Frame(20, 1, cpu: 1.8e13, gpu: 1.8e13));   // what the frame timing API returned after a screenshot
            r.Add(Frame(20, 2, cpu: double.NaN, gpu: -5));
            r.Add(Frame(20, 3, cpu: 12, gpu: BenchmarkRecorder.MaxPlausibleTimingMs));
            BenchmarkSummary s = r.Summarise();
            Assert.AreEqual(10.0, s.CpuAverageMs, 1e-9);
            Assert.AreEqual((10.0 + BenchmarkRecorder.MaxPlausibleTimingMs) / 2, s.GpuAverageMs, 1e-9);
            Assert.AreEqual(BenchmarkRecorder.MaxPlausibleTimingMs, s.GpuP99Ms, 1e-9);
        }

        [Test]
        public void MissingTimingsAndCountersReadAsNotAvailable()
        {
            BenchmarkSummary s = Walking(100, 10.0).Summarise();
            string text = s.ToText();
            StringAssert.Contains("gpu_avg_ms: n/a", text);
            StringAssert.Contains("cpu_avg_ms: n/a", text);
            StringAssert.Contains("draw_calls_avg: n/a", text);
            StringAssert.Contains("triangles_max: n/a", text);
        }

        [Test]
        public void SummaryTextListsEnvironmentFirstAndEndsWithTheVerdict()
        {
            BenchmarkSummary s = Walking(300, 16.0).Summarise();
            string text = s.ToText(new[]
            {
                new KeyValuePair<string, string>("gpu", "Radeon Vega 8"),
                new KeyValuePair<string, string>("window", "1920x1080"),
            });
            string[] lines = text.TrimEnd('\n').Split('\n');
            Assert.AreEqual("gpu: Radeon Vega 8", lines[0]);
            Assert.AreEqual("window: 1920x1080", lines[1]);
            Assert.AreEqual("result: PASS", lines[lines.Length - 1]);
            StringAssert.Contains("p99_ms: 16.00", text);
            StringAssert.Contains("budget_ms: 33.33", text);
            StringAssert.Contains("frames: 300", text);
        }

        [Test]
        public void TextAndCsvUseInvariantFormattingWhateverTheCulture()
        {
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            try
            {
                BenchmarkRecorder r = Walking(10, 16.5);
                StringAssert.Contains("p99_ms: 16.50", r.Summarise().ToText());
                StringAssert.Contains(",16.500,", r.ToCsv());
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void CsvHasAHeaderAndOneRowPerFrame()
        {
            var r = new BenchmarkRecorder();
            r.Add(new FrameSample(1.5, 16.667, 7.25, 12.5, 412, 13498, 31.0, -42.25, 2048, true));
            r.Add(new FrameSample(1.517, 17.0, 0, 0, -1, -1, 31.5, -42.0));
            string[] lines = r.ToCsv().TrimEnd('\n').Split('\n');
            Assert.AreEqual(BenchmarkRecorder.CsvHeader, lines[0]);
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("0,1.500,16.667,7.250,12.500,412,13498,31.00,-42.25,2048,1", lines[1]);
            Assert.AreEqual("1,1.517,17.000,0.000,0.000,-1,-1,31.50,-42.00,-1,0", lines[2]);
            Assert.AreEqual(11, lines[1].Split(',').Length);
        }

        [Test]
        public void BadInputFailsLoudly()
        {
            var r = new BenchmarkRecorder();
            Assert.Throws<ArgumentOutOfRangeException>(() => r.Add(Frame(0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.Add(Frame(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.Add(Frame(double.NaN)));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.Add(Frame(double.PositiveInfinity)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BenchmarkRecorder(0));
            Assert.AreEqual(0, r.Count);
        }
    }
}
