using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class SignalLampsTests
    {
        [Test]
        public void TheHeadWhoseAxisHasGreenShowsGreenAndNothingElse()
        {
            Assert.Greater(SignalLamps.Colour(0, SignalLamps.Green, 0, false).g, 3);
            Assert.AreEqual(SignalLamps.Off, SignalLamps.Colour(0, SignalLamps.Red, 0, false).r, 1e-12);
            Assert.AreEqual(SignalLamps.Off, SignalLamps.Colour(0, SignalLamps.Amber, 0, false).r, 1e-12);
        }

        [Test]
        public void InAmberTheHeadShowsAmberAndTheOtherAxisStaysRed()
        {
            Assert.AreEqual((4.0, 1.8, 0.12), SignalLamps.Colour(0, SignalLamps.Amber, 0, true));
            Assert.AreEqual(SignalLamps.Off * 0.4, SignalLamps.Colour(0, SignalLamps.Green, 0, true).r, 1e-12, "green is out");
            Assert.AreEqual((4.2, 0.08, 0.06), SignalLamps.Colour(1, SignalLamps.Red, 0, true));
        }

        [Test]
        public void TheHeadWhoseAxisLacksGreenShowsRed()
        {
            Assert.AreEqual((4.2, 0.08, 0.06), SignalLamps.Colour(1, SignalLamps.Red, 0, false));
            Assert.AreEqual(SignalLamps.Off, SignalLamps.Colour(1, SignalLamps.Amber, 0, false).r, 1e-12);
            Assert.AreEqual(SignalLamps.Off, SignalLamps.Colour(1, SignalLamps.Green, 0, false).g, 1e-12);
        }

        [Test]
        public void ExactlyOneLampOfEveryHeadIsLitInEveryPhase()
        {
            var clock = new SignalClock();
            for (int step = 0; step < 60 * 40; step++)
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    int lit = 0;
                    for (int lamp = 0; lamp < 3; lamp++)
                    {
                        (double r, double g, double b) = SignalLamps.Colour(axis, lamp, clock.GreenAxis, clock.Amber);
                        if (Math.Max(r, Math.Max(g, b)) > 1) lit++;
                    }
                    Assert.AreEqual(1, lit, $"axis {axis} at phase {clock.Phase}");
                }
                clock.Advance(1.0 / 60);
            }
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SignalLamps.Colour(0, 3, 0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => SignalLamps.Colour(0, -1, 0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => SignalLamps.Colour(2, 0, 0, false));
        }
    }
}
