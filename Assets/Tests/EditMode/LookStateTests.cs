using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class LookStateTests
    {
        [Test]
        public void StartsFacingPlusZLevel()
        {
            var look = new LookState();
            Assert.AreEqual(0.0, look.Yaw);
            Assert.AreEqual(0.0, look.Pitch);
        }

        [Test]
        public void MovingTheMouseRightTurnsRightAndUpLooksUp()
        {
            var look = new LookState();
            look.Apply(100, 50, sensitivity: 0.01);
            Assert.AreEqual(1.0, look.Yaw, 1e-12);
            Assert.AreEqual(0.5, look.Pitch, 1e-12);
        }

        [Test]
        public void SensitivityScalesTheTurn()
        {
            var a = new LookState();
            var b = new LookState();
            a.Apply(10, 10, 0.001);
            b.Apply(10, 10, 0.002);
            Assert.AreEqual(a.Yaw * 2, b.Yaw, 1e-12);
            Assert.AreEqual(a.Pitch * 2, b.Pitch, 1e-12);
        }

        [Test]
        public void DefaultSensitivityIsThePrototypes()
        {
            var look = new LookState();
            look.Apply(1000, 0);
            Assert.AreEqual(1000 * 0.0022, look.Yaw, 1e-12);
        }

        [Test]
        public void InvertYFlipsOnlyThePitch()
        {
            var look = new LookState();
            look.Apply(10, 10, 0.01, invertY: true);
            Assert.AreEqual(0.1, look.Yaw, 1e-12);
            Assert.AreEqual(-0.1, look.Pitch, 1e-12);
        }

        [Test]
        public void PitchClampsJustShortOfStraightUpAndDown()
        {
            var look = new LookState();
            look.Apply(0, 1e6);
            Assert.AreEqual(LookState.PitchLimit, look.Pitch, 1e-12);
            Assert.Less(look.Pitch, Math.PI / 2);

            look.Apply(0, -1e7);
            Assert.AreEqual(-LookState.PitchLimit, look.Pitch, 1e-12);
        }

        [Test]
        public void YawIsNotClamped()
        {
            var look = new LookState();
            look.Apply(10000, 0, 0.01);
            Assert.AreEqual(100.0, look.Yaw, 1e-9);
        }

        [Test]
        public void SetClampsPitchButKeepsYaw()
        {
            var look = new LookState();
            look.Set(2.5, 9);
            Assert.AreEqual(2.5, look.Yaw);
            Assert.AreEqual(LookState.PitchLimit, look.Pitch, 1e-12);
        }

        [Test]
        public void NonPositiveSensitivityFailsLoudly()
        {
            var look = new LookState();
            Assert.Throws<ArgumentOutOfRangeException>(() => look.Apply(1, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => look.Apply(1, 1, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => look.Apply(1, 1, double.NaN));
        }
    }
}
