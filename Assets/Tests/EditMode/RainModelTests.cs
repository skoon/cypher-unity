using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class RainModelTests
    {
        [Test]
        public void LifetimeIsTheTimeToCrossTheVolume()
        {
            Assert.AreEqual(1.25, RainModel.Lifetime(RainModel.DefaultHeight, RainModel.DefaultFallSpeed), 1e-12);
            Assert.AreEqual(2.0, RainModel.Lifetime(30, 15), 1e-12);
        }

        [Test]
        public void SteadyStateCountEqualsTheCapTimesIntensity()
        {
            double life = RainModel.Lifetime(30, 24);
            foreach (double intensity in new[] { 0.0, 0.1, 0.45, 1.0 })
            {
                double rate = RainModel.EmissionRate(3000, intensity, life);
                Assert.AreEqual(3000 * intensity, RainModel.SteadyCount(rate, life), 1e-9, $"intensity {intensity}");
            }
        }

        [Test]
        public void IntensityZeroEmitsNothingAndMoreIntensityEmitsMore()
        {
            Assert.AreEqual(0.0, RainModel.EmissionRate(3000, 0, 1.25));
            Assert.Greater(RainModel.EmissionRate(3000, 0.8, 1.25), RainModel.EmissionRate(3000, 0.4, 1.25));
        }

        [Test]
        public void StretchScaleGivesTheRequestedStreakLengthAtFallSpeed()
        {
            double scale = RainModel.StretchVelocityScale(RainModel.DefaultStreakLength, RainModel.DefaultFallSpeed);
            Assert.AreEqual(RainModel.DefaultStreakLength, scale * RainModel.DefaultFallSpeed, 1e-12);
        }

        [Test]
        public void WindDriftScalesLinearlyFromZeroToTheMaximum()
        {
            Assert.AreEqual(0.0, RainModel.WindDrift(0));
            Assert.AreEqual(RainModel.MaxWindDrift, RainModel.WindDrift(1));
            Assert.AreEqual(RainModel.MaxWindDrift * RainModel.DefaultWind, RainModel.WindDrift(RainModel.DefaultWind), 1e-12);
        }

        [Test]
        public void SpawnHeightPutsTheCameraTwoThirdsOfTheWayDown()
        {
            double h = RainModel.DefaultHeight;
            double spawn = RainModel.SpawnHeightAboveCamera(h);
            Assert.AreEqual(h / 3, spawn);
            Assert.AreEqual(h - spawn, h * 2 / 3, 1e-12, "the volume extends twice as far below the camera as above it");
        }

        [Test]
        public void DefaultsAreSane()
        {
            Assert.Greater(RainModel.DefaultFallSpeed, 9, "faster than physical drops, which reads better as streaks");
            Assert.Less(RainModel.DefaultStreakLength, 3);
            Assert.That(RainModel.DefaultWind, Is.InRange(0.0, 1.0));
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RainModel.Lifetime(0, 24));
            Assert.Throws<ArgumentOutOfRangeException>(() => RainModel.Lifetime(30, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RainModel.Lifetime(double.NaN, 24));
            Assert.Throws<ArgumentOutOfRangeException>(() => RainModel.EmissionRate(-1, 0.5, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => RainModel.EmissionRate(100, 1.1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => RainModel.EmissionRate(100, -0.1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => RainModel.EmissionRate(100, 0.5, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RainModel.StretchVelocityScale(0, 24));
            Assert.Throws<ArgumentOutOfRangeException>(() => RainModel.StretchVelocityScale(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RainModel.WindDrift(1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => RainModel.WindDrift(-0.1));
        }
    }
}
