using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class VehicleCatalogTests
    {
        // ---------------------------------------------------------------- the tables

        [Test]
        public void TheGroundTypesAreTheFourOfThePrototype()
        {
            CollectionAssert.AreEqual(new[] { "sedan", "van", "truck", "bike" }, VehicleCatalog.Ground.Select(t => t.Key).ToArray());
            CollectionAssert.AreEqual(new[] { 0.40, 0.19, 0.13, 0.28 }, VehicleCatalog.Ground.Select(t => t.Share).ToArray());
            CollectionAssert.AreEqual(new[] { 4.62, 5.42, 8.6, 2.26 }, VehicleCatalog.Ground.Select(t => t.Length).ToArray());
            CollectionAssert.AreEqual(new[] { 4, 4, 4, 2 }, VehicleCatalog.Ground.Select(t => t.WheelCount).ToArray());
            CollectionAssert.AreEqual(new[] { false, false, false, true }, VehicleCatalog.Ground.Select(t => t.Rider).ToArray(), "only the bike has a rider");
            CollectionAssert.AreEqual(new[] { 1.0, 1.0, 1.0, 1.32 }, VehicleCatalog.Ground.Select(t => t.LaneScale).ToArray(), "the bike keeps further over");
        }

        [Test]
        public void TheAircraftTypesAreTheTwoOfThePrototype()
        {
            CollectionAssert.AreEqual(new[] { "spinner", "freighter" }, VehicleCatalog.Aircraft.Select(t => t.Key).ToArray());
            CollectionAssert.AreEqual(new[] { 0.66, 0.34 }, VehicleCatalog.Aircraft.Select(t => t.Share).ToArray());
            CollectionAssert.AreEqual(new[] { 17.0, 11.0 }, VehicleCatalog.Aircraft.Select(t => t.SpeedMin).ToArray());
            CollectionAssert.AreEqual(new[] { 30.0, 19.0 }, VehicleCatalog.Aircraft.Select(t => t.SpeedMax).ToArray());
        }

        [Test]
        public void TheSharesOfEachFleetSumToOne()
        {
            Assert.AreEqual(1.0, VehicleCatalog.Ground.Sum(t => t.Share), 1e-12);
            Assert.AreEqual(1.0, VehicleCatalog.Aircraft.Sum(t => t.Share), 1e-12);
        }

        [Test]
        public void TheHullProfilesAreClosedShapesWithinTheirVehiclesBoxes()
        {
            foreach (GroundVehicleType t in VehicleCatalog.Ground)
            {
                Assert.GreaterOrEqual(t.Hull.Profile.Length, 7, t.Key);
                Assert.AreEqual(t.Length / 2, t.Hull.Profile.Max(p => Math.Abs(p.Z)), 0.05, $"{t.Key}'s hull spans its length");
                if (t.Rider) Assert.Less(t.Hull.Profile.Max(p => p.Y), t.Height, $"{t.Key}'s rider stands above the bike");
                else Assert.AreEqual(t.Height, t.Hull.Profile.Max(p => p.Y), 0.05, $"{t.Key}'s hull is as tall as the vehicle");
                Assert.LessOrEqual(t.Hull.HalfWidth * 2, t.Width + 1e-9, $"{t.Key}'s hull is no wider than the vehicle");
                Assert.That(t.Hull.RoofY, Is.InRange(0, 1));
            }
            foreach (AircraftType t in VehicleCatalog.Aircraft)
            {
                Assert.AreEqual(t.Length / 2, t.Hull.Profile.Max(p => Math.Abs(p.Z)), 0.15, $"{t.Key}'s hull spans its length");
                Assert.LessOrEqual(t.Hull.Profile.Max(p => p.Y), t.Height + 1e-9);
                Assert.Greater(t.WingSpan, t.Width, $"{t.Key}'s wing is wider than its body");
            }
        }

        [Test]
        public void TheWheelsSitInsideTheBodyAlongItsLength()
        {
            foreach (GroundVehicleType t in VehicleCatalog.Ground)
            {
                Assert.Less(Math.Abs(t.WheelZFront), t.Length / 2, t.Key);
                Assert.Less(Math.Abs(t.WheelZRear), t.Length / 2, t.Key);
                Assert.Greater(t.WheelZFront, t.WheelZRear, $"{t.Key}: the front wheel is ahead");
                Assert.LessOrEqual(t.WheelX + t.WheelWidth / 2, t.Width / 2 + 0.1, t.Key);
            }
        }

        [Test]
        public void ThePalettesAreThePrototypes()
        {
            Assert.AreEqual(10, VehicleCatalog.Paint.Length);
            Assert.AreEqual(6, VehicleCatalog.Neon.Length);
            Assert.AreEqual(0x1b1f2au, VehicleCatalog.Paint[0]);
            Assert.AreEqual(0x35383eu, VehicleCatalog.Paint[9]);
            Assert.AreEqual(0x2ff0ffu, VehicleCatalog.Neon[0]);
            Assert.AreEqual(0x2f6bffu, VehicleCatalog.Neon[5]);
        }

        // ---------------------------------------------------------------- dividing a fleet

        [TestCase(96, new[] { 38, 18, 13, 27 })]
        [TestCase(52, new[] { 21, 10, 7, 14 })]
        [TestCase(18, new[] { 7, 4, 2, 5 })]
        [TestCase(0, new[] { 0, 0, 0, 0 })]
        [TestCase(1, new[] { 1, 0, 0, 0 })]
        public void AGroundFleetIsSharedOutByLargestRemainder(int total, int[] expected)
        {
            CollectionAssert.AreEqual(expected, FleetBudget.Distribute(total, VehicleCatalog.GroundShares()));
        }

        [TestCase(36, new[] { 24, 12 })]
        [TestCase(20, new[] { 13, 7 })]
        [TestCase(5, new[] { 3, 2 })]
        public void AnAircraftFleetIsSharedOutByLargestRemainder(int total, int[] expected)
        {
            CollectionAssert.AreEqual(expected, FleetBudget.Distribute(total, VehicleCatalog.AircraftShares()));
        }

        [Test]
        public void ThePartsAlwaysSumToTheTotal()
        {
            for (int total = 0; total <= 120; total++)
            {
                Assert.AreEqual(total, FleetBudget.Distribute(total, VehicleCatalog.GroundShares()).Sum(), $"ground {total}");
                Assert.AreEqual(total, FleetBudget.Distribute(total, VehicleCatalog.AircraftShares()).Sum(), $"aircraft {total}");
            }
        }

        [Test]
        public void TiesInTheRemainderGoToTheEarlierType()
        {
            CollectionAssert.AreEqual(new[] { 1, 0 }, FleetBudget.Distribute(1, new[] { 1.0, 1.0 }));
            CollectionAssert.AreEqual(new[] { 2, 1, 1 }, FleetBudget.Distribute(4, new[] { 1.0, 1.0, 1.0 }));
        }

        [Test]
        public void EveryPartIsWithinOneOfItsExactShare()
        {
            double[] shares = VehicleCatalog.GroundShares();
            for (int total = 0; total <= 120; total++)
            {
                int[] counts = FleetBudget.Distribute(total, shares);
                for (int i = 0; i < counts.Length; i++) Assert.Less(Math.Abs(counts[i] - total * shares[i]), 1.0, $"type {i} at {total}");
            }
        }

        [Test]
        public void BadFleetsFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FleetBudget.Distribute(-1, new[] { 1.0 }));
            Assert.Throws<ArgumentException>(() => FleetBudget.Distribute(5, new double[0]));
            Assert.Throws<ArgumentException>(() => FleetBudget.Distribute(5, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => FleetBudget.Distribute(5, new[] { 1.0, 0.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => FleetBudget.Distribute(5, new[] { 1.0, -1.0 }));
        }

        // ---------------------------------------------------------------- the signal

        [Test]
        public void TheSignalGoesGreenForXAmberForXGreenForZAmberForZ()
        {
            var clock = new SignalClock();
            var seen = new System.Collections.Generic.List<(int phase, int axis, bool amber)>();
            int last = -1;
            for (int i = 0; i < 60 * 40; i++)
            {
                if (clock.Phase != last)
                {
                    seen.Add((clock.Phase, clock.GreenAxis, clock.Amber));
                    last = clock.Phase;
                }
                clock.Advance(1.0 / 60);
            }
            CollectionAssert.AreEqual(new[] { (0, 0, false), (1, 0, true), (2, 1, false), (3, 1, true), (0, 0, false) }, seen);
        }

        [Test]
        public void TheSignalPhasesLastAsLongAsThePrototypes()
        {
            Assert.AreEqual(12.5, SignalClock.Duration(0));
            Assert.AreEqual(2.6, SignalClock.Duration(1));
            Assert.AreEqual(12.5, SignalClock.Duration(2));
            Assert.AreEqual(2.6, SignalClock.Duration(3));
            Assert.AreEqual(30.2, SignalClock.CycleLength, 1e-12);

            var clock = new SignalClock();
            clock.Advance(12.49);
            Assert.AreEqual(0, clock.Phase);
            clock.Advance(0.02);
            Assert.AreEqual(1, clock.Phase, "amber after 12.5 s");
            Assert.AreEqual(0, clock.PhaseTime, "the time does not carry over, as in the prototype");
        }

        [Test]
        public void TimeCannotRunBackwards()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SignalClock().Advance(-0.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SignalClock().Advance(double.NaN));
        }

        [Test]
        public void TheManoeuvreValuesAreThePrototypes()
        {
            Assert.AreEqual(0, (int)Manoeuvre.Straight);
            Assert.AreEqual(1, (int)Manoeuvre.Turn);
            Assert.AreEqual(-1, (int)Manoeuvre.CrossingTurn);
            Assert.AreEqual(2, (int)Manoeuvre.UTurn);
        }
    }
}
