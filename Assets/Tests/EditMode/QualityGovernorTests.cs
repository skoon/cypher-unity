using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>Port of the prototype's tests/qualitygovernor.test.mjs, plus what the C# version adds.</summary>
    public class QualityGovernorTests
    {
        const double Warmup = 5;

        /// <summary>Run <paramref name="seconds"/> of steady <paramref name="fps"/>, returning every downgrade it decided.</summary>
        static List<QualityPreset> Feed(QualityGovernor gov, double fps, double seconds)
        {
            double dt = 1.0 / fps;
            var drops = new List<QualityPreset>();
            for (int i = 0; i < (int)Math.Ceiling(seconds / dt); i++)
            {
                QualityPreset? d = gov.Sample(dt);
                if (d.HasValue) drops.Add(d.Value);
            }
            return drops;
        }

        // ------------------------------------------------------------ construction

        [Test]
        public void DefaultsToTheTopPresetAndTheDocumentedTarget()
        {
            var g = new QualityGovernor();
            Assert.AreEqual(QualityPreset.Ultra, g.Quality);
            Assert.AreEqual(30.0, QualityGovernor.TargetFps);
            Assert.AreEqual(new[] { QualityPreset.Performance, QualityPreset.Low, QualityPreset.Medium, QualityPreset.High, QualityPreset.Ultra },
                (QualityPreset[])Enum.GetValues(typeof(QualityPreset)));
        }

        [Test]
        public void AdoptsAStartingPreset()
        {
            Assert.AreEqual(QualityPreset.Medium, new QualityGovernor(QualityPreset.Medium).Quality);
        }

        [Test]
        public void RejectsNonsenseConfigurationRatherThanLimpingOn()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityGovernor(target: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityGovernor(target: -30));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityGovernor(target: double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityGovernor(window: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityGovernor(evalEvery: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityGovernor(warmup: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityGovernor(cooldown: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityGovernor(severeRatio: -0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityGovernor((QualityPreset)42));
        }

        // ------------------------------------------------------------------ warm-up

        [Test]
        public void IgnoresTheOpeningSecondsHoweverBadTheyAre()
        {
            // Boot compiles shaders and uploads textures; every machine misses the target here.
            var g = new QualityGovernor();
            Assert.IsEmpty(Feed(g, 4, Warmup - 1));
            Assert.AreEqual(QualityPreset.Ultra, g.Quality);
            Assert.IsTrue(g.Settling);
        }

        [Test]
        public void ActsOnceWarmupHasPassed()
        {
            var g = new QualityGovernor();
            Assert.IsTrue(g.Settling, "starts warming up");
            List<QualityPreset> drops = Feed(g, 25, Warmup + 2);
            Assert.GreaterOrEqual(drops.Count, 1, "a sustained 25 fps must downgrade");
            Assert.AreNotEqual(QualityPreset.Ultra, g.Quality);
        }

        // ------------------------------------------------------------------ the policy

        [Test]
        public void SustainedFramesUnderTargetStepTheTierDown()
        {
            var g = new QualityGovernor();
            List<QualityPreset> drops = Feed(g, 25, Warmup + 2);
            Assert.AreEqual(QualityPreset.High, drops[0], "ultra drops one tier at 25 fps");
            Assert.AreEqual(QualityPreset.High, g.Quality);
        }

        [Test]
        public void ACatastrophicFrameRateSkipsATier()
        {
            var g = new QualityGovernor();
            List<QualityPreset> drops = Feed(g, 10, Warmup + 2);
            Assert.AreEqual(QualityPreset.Medium, drops[0], "ultra skips past high at 10 fps");
        }

        [Test]
        public void FramesComfortablyOverTargetNeverDowngrade()
        {
            var g = new QualityGovernor();
            Assert.IsEmpty(Feed(g, 60, 60));
            Assert.AreEqual(QualityPreset.Ultra, g.Quality);
        }

        [Test]
        public void FramesExactlyAtTargetDoNotDowngrade()
        {
            var g = new QualityGovernor();
            Assert.IsEmpty(Feed(g, QualityGovernor.TargetFps, 30));
        }

        [Test]
        public void ALoneHitchDoesNotCountAsATrend()
        {
            var g = new QualityGovernor();
            Feed(g, 60, Warmup + 1);
            var drops = new List<QualityPreset>();
            for (int i = 0; i < 600; i++)
            {
                QualityPreset? d = g.Sample(i % 200 == 0 ? 0.25 : 1.0 / 60);
                if (d.HasValue) drops.Add(d.Value);
            }
            Assert.IsEmpty(drops, "periodic spikes among good frames are not a downgrade");
        }

        [Test]
        public void SustainedStutterDowngradesEvenWhenTheMedianIsFine()
        {
            // 15% of frames at 60 ms among 10 ms frames: the median is 100 fps, but a sixth of the window blows the budget.
            var g = new QualityGovernor();
            Feed(g, 100, Warmup + 1);
            var drops = new List<QualityPreset>();
            for (int i = 0; i < 1000 && drops.Count == 0; i++)
            {
                QualityPreset? d = g.Sample(i % 20 < 3 ? 0.060 : 0.010);
                if (d.HasValue) drops.Add(d.Value);
            }
            Assert.AreEqual(1, drops.Count);
            Assert.GreaterOrEqual(g.SlowFraction, 0.10);
        }

        // ------------------------------------------------------------------ cool-down

        [Test]
        public void DoesNotCascadeWhileTheSwitchIsStillSettling()
        {
            var g = new QualityGovernor(severeRatio: 0);   // force single steps
            Assert.AreEqual(1, Feed(g, 25, Warmup + 1).Count);
            Assert.IsEmpty(Feed(g, 25, 2), "cool-down suppresses the next decision");
        }

        [Test]
        public void ResumesDowngradingOnceTheCooldownExpires()
        {
            var g = new QualityGovernor(severeRatio: 0);
            Feed(g, 25, Warmup + 1);
            Assert.AreEqual(QualityPreset.High, g.Quality);
            Assert.Contains(QualityPreset.Medium, Feed(g, 25, 6), "still slow after cool-down, so it steps again");
        }

        // ------------------------------------------------------------------ the floor

        [Test]
        public void PerformanceIsTheFloorAndItStopsThere()
        {
            var g = new QualityGovernor(QualityPreset.Performance);
            Assert.IsEmpty(Feed(g, 3, 60), "nothing left to give up");
            Assert.AreEqual(QualityPreset.Performance, g.Quality);
            Assert.IsTrue(g.AtFloor);
        }

        [Test]
        public void WalksAllTheWayDownAndThenStaysPut()
        {
            var g = new QualityGovernor();
            Feed(g, 8, 60);
            Assert.AreEqual(QualityPreset.Performance, g.Quality);
            Assert.IsTrue(g.AtFloor);
        }

        [Test]
        public void NeverReturnsAHigherTierBecauseThePolicyIsSticky()
        {
            var g = new QualityGovernor();
            Feed(g, 10, 30);
            Assert.AreEqual(QualityPreset.Performance, g.Quality);
            Assert.IsEmpty(Feed(g, 144, 60), "headroom does not climb back on its own");
            Assert.AreEqual(QualityPreset.Performance, g.Quality);
        }

        // ------------------------------------------------------------------ disarmed

        [Test]
        public void DisabledStillMeasuresButNeverActs()
        {
            var g = new QualityGovernor(enabled: false);
            Assert.IsEmpty(Feed(g, 10, 30), "no downgrades while disarmed");
            Assert.AreEqual(QualityPreset.Ultra, g.Quality, "quality did not drift");
            Assert.Greater(g.Fps, 0, "but the frame rate is still being measured");
            Assert.AreEqual(10.0, g.Fps, 1.0);
        }

        [Test]
        public void ReArmingActsOnTheCurrentStateNotADriftedOne()
        {
            var g = new QualityGovernor(enabled: false);
            Feed(g, 10, 30);
            Assert.AreEqual(QualityPreset.Ultra, g.Quality);

            g.Enabled = true;
            List<QualityPreset> drops = Feed(g, 10, 5);
            Assert.AreEqual(QualityPreset.Medium, drops[0], "steps down from where it really was");
        }

        // ------------------------------------------------------------------ reset and suspend

        [Test]
        public void ResetAdoptsThePlayerChoiceAndReArms()
        {
            var g = new QualityGovernor();
            Feed(g, 10, 30);
            Assert.AreEqual(QualityPreset.Performance, g.Quality);

            g.Reset(QualityPreset.High);   // the player raised it by hand
            Assert.AreEqual(QualityPreset.High, g.Quality);
            Assert.IsTrue(g.Settling, "a manual choice is not second-guessed on stale history");
            Assert.IsEmpty(Feed(g, 10, Warmup - 1), "and gets a full warm-up to prove itself");
        }

        [Test]
        public void ResetRejectsAnUnknownPreset()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityGovernor().Reset((QualityPreset)99));
        }

        [Test]
        public void SuspendDiscardsTheMenuDeltaInsteadOfReadingItAsAStall()
        {
            var g = new QualityGovernor();
            Feed(g, 60, Warmup + 1);
            g.Suspend();
            Assert.IsTrue(g.Settling);
            Assert.AreEqual(0.0, g.Fps);
            Assert.IsNull(g.Sample(4.0), "the single enormous delta on resume decides nothing");
            Assert.AreEqual(QualityPreset.Ultra, g.Quality);
        }

        // ------------------------------------------------------------------ readout

        [Test]
        public void ReportsTheMedianFrameRateForTheReadout()
        {
            var g = new QualityGovernor();
            Feed(g, 60, Warmup + 2);
            Assert.AreEqual(60.0, g.Fps, 1.0);
        }

        [Test]
        public void FpsIsZeroUntilThereIsAFullWindowToJudge()
        {
            var g = new QualityGovernor();
            Assert.AreEqual(0.0, g.Fps);
            Feed(g, 60, 1);
            Assert.AreEqual(0.0, g.Fps);
        }

        [Test]
        public void TheReadoutSurvivesADowngrade()
        {
            var g = new QualityGovernor();
            Feed(g, 20, Warmup + 2);
            Assert.AreNotEqual(QualityPreset.Ultra, g.Quality);
            Assert.AreEqual(20.0, g.Fps, 1.0, "the reading that caused the drop stays until the next one");
        }

        // ------------------------------------------------------------------ bad clocks

        [Test]
        public void IgnoresNonPositiveAndNonFiniteDeltas()
        {
            var g = new QualityGovernor();
            Assert.IsNull(g.Sample(0));
            Assert.IsNull(g.Sample(-1));
            Assert.IsNull(g.Sample(double.NaN));
            Assert.IsNull(g.Sample(double.PositiveInfinity));
            Assert.IsTrue(g.Settling, "a dead clock does not burn the warm-up");
            Assert.IsEmpty(Feed(g, 60, Warmup + 2), "and does not poison the window either");
        }

        [Test]
        public void TheSampleWindowDoesNotGrowWithoutBound()
        {
            var g = new QualityGovernor(window: 2);
            // Hold the governor mid-warm-up so the long run cannot decide anything, then check it still keeps up with frames.
            Feed(g, 60, 3600);
            Assert.AreEqual(60.0, g.Fps, 1.0, "an hour of frames did not slow or break the window");
        }

        // ------------------------------------------------------------------ the game's wiring

        [Test]
        public void TheGameAimsFiveFramesAboveTheFloor()
        {
            // The director builds the governor with target 35 so a machine holds the floor with headroom.
            var g = new QualityGovernor(QualityPreset.Ultra, target: QualityGovernor.TargetFps + 5);
            Assert.IsEmpty(Feed(g, 40, 30), "40 fps clears a 35 fps target");
            var tight = new QualityGovernor(QualityPreset.Ultra, target: QualityGovernor.TargetFps + 5);
            Assert.IsNotEmpty(Feed(tight, 33, Warmup + 2), "33 fps meets the floor but not the headroom");
        }
    }
}
