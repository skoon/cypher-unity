using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class PerfStatsTests
    {
        [Test]
        public void EmptyStatsReportZeroAndDoNotMeetFloor()
        {
            var s = new PerfStats();
            Assert.AreEqual(0, s.Count);
            Assert.AreEqual(0.0, s.Fps);
            Assert.AreEqual(0.0, s.AverageMs);
            Assert.AreEqual(0.0, s.P99Ms);
            Assert.AreEqual(0.0, s.WorstMs);
            Assert.IsFalse(s.MeetsFloor());
        }

        [Test]
        public void SteadySixtyFpsReportsSixtyAndMeetsFloor()
        {
            var s = new PerfStats(5.0);
            for (int i = 0; i < 300; i++) s.AddFrame(1.0 / 60.0);
            Assert.AreEqual(60.0, s.Fps, 0.5);
            Assert.AreEqual(16.667, s.AverageMs, 0.01);
            Assert.AreEqual(16.667, s.P99Ms, 0.01);
            Assert.IsTrue(s.MeetsFloor());
        }

        [Test]
        public void WindowDropsOldFrames()
        {
            var s = new PerfStats(1.0);
            // Binary-exact frame times so window trimming is not at the mercy of float rounding.
            for (int i = 0; i < 40; i++) s.AddFrame(0.25);          // 10 s of slow frames
            for (int i = 0; i < 200; i++) s.AddFrame(1.0 / 128.0);  // 1.5625 s of fast ones
            Assert.AreEqual(128.0, s.Fps, 1e-9);
            Assert.AreEqual(7.8125, s.WorstMs, 1e-9);
        }

        [Test]
        public void AFrameLongerThanTheWindowIsStillKept()
        {
            var s = new PerfStats(1.0);
            s.AddFrame(3.0);
            Assert.AreEqual(1, s.Count);
            Assert.AreEqual(3000.0, s.WorstMs, 1e-9);
        }

        [Test]
        public void StutterFailsTheFloorEvenWhenTheMeanPasses()
        {
            var s = new PerfStats(10.0);
            // 100 frames: 97 at 10 ms and 3 at 100 ms. Mean is 12.7 ms (~79 FPS).
            for (int i = 0; i < 97; i++) s.AddFrame(0.010);
            for (int i = 0; i < 3; i++) s.AddFrame(0.100);
            Assert.Less(s.AverageMs, 33.3);
            Assert.AreEqual(100.0, s.P99Ms, 1e-9);
            Assert.IsFalse(s.MeetsFloor());
        }

        [Test]
        public void IsolatedSlowFrameBelowOnePercentIsIgnoredByP99()
        {
            var s = new PerfStats(10.0);
            for (int i = 0; i < 199; i++) s.AddFrame(0.010);
            s.AddFrame(0.200);
            Assert.AreEqual(10.0, s.P99Ms, 1e-9);
            Assert.AreEqual(200.0, s.WorstMs, 1e-9);
            Assert.IsTrue(s.MeetsFloor());
        }

        [Test]
        public void FloorBoundaryIsInclusive()
        {
            var s = new PerfStats();
            for (int i = 0; i < 10; i++) s.AddFrame(1.0 / 30.0);
            Assert.IsTrue(s.MeetsFloor(30.0));
            Assert.IsFalse(s.MeetsFloor(31.0));
        }

        [Test]
        public void PercentilesUseNearestRank()
        {
            var s = new PerfStats(100.0);
            for (int i = 1; i <= 100; i++) s.AddFrame(i / 1000.0);   // 1 ms .. 100 ms
            Assert.AreEqual(50.0, s.PercentileMs(0.5), 1e-9);
            Assert.AreEqual(95.0, s.PercentileMs(0.95), 1e-9);
            Assert.AreEqual(99.0, s.PercentileMs(0.99), 1e-9);
            Assert.AreEqual(100.0, s.PercentileMs(1.0), 1e-9);
            Assert.AreEqual(s.P99Ms, s.PercentileMs(0.99));
            Assert.AreEqual(0.0, new PerfStats().PercentileMs(0.5));
        }

        [Test]
        public void StaticPercentileWorksOnAnySequenceAndLeavesItAlone()
        {
            var values = new[] { 5.0, 1.0, 4.0, 2.0, 3.0 };
            Assert.AreEqual(3.0, PerfStats.Percentile(values, 0.5));
            Assert.AreEqual(5.0, PerfStats.Percentile(values, 1.0));
            Assert.AreEqual(1.0, PerfStats.Percentile(values, 0.01));
            Assert.AreEqual(new[] { 5.0, 1.0, 4.0, 2.0, 3.0 }, values, "the input is not sorted in place");
            Assert.AreEqual(0.0, PerfStats.Percentile(new double[0], 0.99));
            Assert.Throws<ArgumentOutOfRangeException>(() => PerfStats.Percentile(values, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => PerfStats.Percentile(values, 1.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => PerfStats.Percentile(values, double.NaN));
        }

        [Test]
        public void WithinBudgetIsInclusiveAtTheBoundary()
        {
            Assert.IsTrue(PerfStats.WithinBudget(1000.0 / 30.0));
            Assert.IsTrue(PerfStats.WithinBudget(33.0));
            Assert.IsFalse(PerfStats.WithinBudget(33.4));
            Assert.IsTrue(PerfStats.WithinBudget(16.0, 60.0));
            Assert.IsFalse(PerfStats.WithinBudget(17.0, 60.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => PerfStats.WithinBudget(10, 0));
        }

        [Test]
        public void ResetClearsEverything()
        {
            var s = new PerfStats();
            s.AddFrame(0.02);
            s.Reset();
            Assert.AreEqual(0, s.Count);
            Assert.AreEqual(0.0, s.Fps);
        }

        [Test]
        public void BadInputFailsLoudly()
        {
            var s = new PerfStats();
            Assert.Throws<ArgumentOutOfRangeException>(() => s.AddFrame(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.AddFrame(-0.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.AddFrame(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.AddFrame(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PerfStats(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.MeetsFloor(0));
            Assert.AreEqual(0, s.Count);
        }
    }
}
