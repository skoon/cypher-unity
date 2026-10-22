using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class LoadTrackerTests
    {
        static LoadTracker Make() => new LoadTracker(new[]
        {
            new LoadStep("settings", "Reading settings", 1),
            new LoadStep("city", "Building the city", 6),
            new LoadStep("probe", "Rendering reflections", 3),
        });

        [Test]
        public void StartsAtZeroWithTheFirstStepsLabel()
        {
            LoadTracker t = Make();
            Assert.AreEqual(0.0, t.Progress);
            Assert.IsFalse(t.IsDone);
            Assert.AreEqual("Reading settings", t.Status);
        }

        [Test]
        public void ProgressIsWeightedAcrossTheSteps()
        {
            LoadTracker t = Make();
            t.Complete("settings");
            Assert.AreEqual(0.1, t.Progress, 1e-12);
            t.Set("city", 0.5);
            Assert.AreEqual(0.4, t.Progress, 1e-12);
            t.Complete("city");
            Assert.AreEqual(0.7, t.Progress, 1e-12);
            t.Complete("probe");
            Assert.AreEqual(1.0, t.Progress, 1e-12);
            Assert.IsTrue(t.IsDone);
            Assert.AreEqual("Ready", t.Status);
        }

        [Test]
        public void StatusNamesTheFirstUnfinishedStep()
        {
            LoadTracker t = Make();
            t.Complete("settings");
            Assert.AreEqual("Building the city", t.Status);
            t.Complete("probe");   // out of order is fine
            Assert.AreEqual("Building the city", t.Status);
            t.Complete("city");
            Assert.AreEqual("Ready", t.Status);
        }

        [Test]
        public void ReportingTheSameFractionAgainIsFine()
        {
            LoadTracker t = Make();
            t.Set("city", 0.4);
            t.Set("city", 0.4);
            Assert.AreEqual(0.4, t.Fraction("city"));
        }

        [Test]
        public void AStepGoingBackwardsIsABug()
        {
            LoadTracker t = Make();
            t.Set("city", 0.8);
            InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => t.Set("city", 0.3));
            StringAssert.Contains("went backwards", e.Message);
            Assert.AreEqual(0.8, t.Fraction("city"), "and nothing changed");
        }

        [TestCase(-0.1)]
        [TestCase(1.1)]
        [TestCase(double.NaN)]
        public void FractionsOutsideZeroToOneAreRejected(double fraction)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Make().Set("city", fraction));
        }

        [Test]
        public void UnknownStepsAreRejected()
        {
            LoadTracker t = Make();
            Assert.Throws<ArgumentException>(() => t.Set("audio", 0.5));
            Assert.Throws<ArgumentException>(() => t.Complete(null));
            Assert.Throws<ArgumentException>(() => t.Fraction("nope"));
        }

        [Test]
        public void BadStepListsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new LoadTracker(null));
            Assert.Throws<ArgumentException>(() => new LoadTracker(new LoadStep[0]));
            Assert.Throws<ArgumentException>(() => new LoadTracker(new[] { new LoadStep("", "x", 1) }));
            Assert.Throws<ArgumentException>(() => new LoadTracker(new[] { new LoadStep("a", "x", 1), new LoadStep("a", "y", 1) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LoadTracker(new[] { new LoadStep("a", "x", 0) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LoadTracker(new[] { new LoadStep("a", "x", -1) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LoadTracker(new[] { new LoadStep("a", "x", double.PositiveInfinity) }));
        }

        [Test]
        public void ASingleStepWorks()
        {
            var t = new LoadTracker(new[] { new LoadStep("only", "Loading", 5) });
            t.Set("only", 0.25);
            Assert.AreEqual(0.25, t.Progress, 1e-12);
            t.Complete("only");
            Assert.IsTrue(t.IsDone);
        }
    }
}
