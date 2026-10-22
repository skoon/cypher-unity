using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class TitleFlightTests
    {
        static double AngleDifference(double a, double b)
        {
            double d = (a - b) % (2 * Math.PI);
            if (d > Math.PI) d -= 2 * Math.PI;
            else if (d < -Math.PI) d += 2 * Math.PI;
            return d;
        }

        [Test]
        public void TheDefaultLoopIsAboutThreeAndAHalfHundredMetresRoundedAtTheJunctions()
        {
            TitleFlight f = TitleFlight.Default();
            Assert.That(f.LoopLength, Is.InRange(345.0, 372.0), "five legs of the 372 m route, shorter because every corner is cut");
            Assert.AreEqual(f.LoopLength / TitleFlight.DefaultSpeed, f.LoopSeconds, 1e-9);
        }

        [Test]
        public void ThePathClosesOnItself()
        {
            TitleFlight f = TitleFlight.Default();
            Point2 a = f.PointAt(0), b = f.PointAt(f.LoopLength);
            Assert.AreEqual(a.X, b.X, 1e-6);
            Assert.AreEqual(a.Z, b.Z, 1e-6);
            Point2 c = f.PointAt(37.5), d = f.PointAt(37.5 + 2 * f.LoopLength), e = f.PointAt(37.5 - f.LoopLength);
            Assert.AreEqual(c.X, d.X, 1e-6);
            Assert.AreEqual(c.Z, e.Z, 1e-6);
        }

        [Test]
        public void ThePathCutsInsideEachCornerAndNeverBulgesOutsideIt()
        {
            // Waypoint 1 (east, east) is a real corner between the leg up the east road and the leg along the north one.
            double near = CityLayout.RoadCentre(CityLayout.PlazaIndex), east = CityLayout.RoadCentre(CityLayout.PlazaIndex + 1);
            double far = CityLayout.RoadCentre(CityLayout.PlazaIndex - 1);
            TitleFlight f = TitleFlight.Default();
            double closest = double.MaxValue;
            for (double d = 0; d < f.LoopLength; d += 0.25)
            {
                Point2 p = f.PointAt(d);
                closest = Math.Min(closest, Math.Sqrt((p.X - east) * (p.X - east) + (p.Z - east) * (p.Z - east)));
                Assert.That(p.X, Is.InRange(near - 1e-6, east + 1e-6), "never wider than the outermost waypoints");
                Assert.That(p.Z, Is.InRange(far - 1e-6, east + 1e-6));
            }
            Assert.Greater(closest, 1.5, "it does not touch the corner point");
            Assert.Less(closest, 5.0, "but passes close to it, through the junction");
        }

        [Test]
        public void TheCameraStaysInsideTheRoadCorridorsForTwoWholeLoops()
        {
            TitleFlight f = TitleFlight.Default();
            for (double t = 0; t < 2 * f.LoopSeconds; t += 0.25)
            {
                FlightPose p = f.PoseAt(t);
                Assert.IsTrue(CityLayout.IsOnRoad(p.X, p.Z), $"t={t}: ({p.X:0.0}, {p.Z:0.0}) is over a building");
            }
        }

        [Test]
        public void TheCameraStaysClearOfTheKerbsToo()
        {
            // Roads are 16 m wide; staying within 6 m of a centre line keeps well clear of the facades.
            TitleFlight f = TitleFlight.Default();
            double[] centres =
            {
                CityLayout.RoadCentre(CityLayout.PlazaIndex - 1), CityLayout.RoadCentre(CityLayout.PlazaIndex), CityLayout.RoadCentre(CityLayout.PlazaIndex + 1),
            };
            for (double t = 0; t < f.LoopSeconds; t += 0.5)
            {
                FlightPose p = f.PoseAt(t);
                double dx = double.MaxValue, dz = double.MaxValue;
                foreach (double c in centres)
                {
                    dx = Math.Min(dx, Math.Abs(p.X - c));
                    dz = Math.Min(dz, Math.Abs(p.Z - c));
                }
                Assert.LessOrEqual(Math.Min(dx, dz), 6.0, $"t={t}");
            }
        }

        [Test]
        public void HeightStaysWithinTheSwayAroundTheSetHeight()
        {
            TitleFlight f = TitleFlight.Default();
            double lo = double.MaxValue, hi = double.MinValue;
            for (double t = 0; t < 120; t += 0.1)
            {
                double y = f.PoseAt(t).Y;
                lo = Math.Min(lo, y);
                hi = Math.Max(hi, y);
            }
            Assert.That(lo, Is.GreaterThanOrEqualTo(TitleFlight.DefaultHeight - TitleFlight.HeightSway - 1e-9));
            Assert.That(hi, Is.LessThanOrEqualTo(TitleFlight.DefaultHeight + TitleFlight.HeightSway + 1e-9));
            Assert.Greater(hi - lo, TitleFlight.HeightSway, "it does bob");
        }

        [Test]
        public void ItTravelsAtAboutTheSetSpeed()
        {
            TitleFlight f = TitleFlight.Default();
            double total = 0;
            FlightPose prev = f.PoseAt(0);
            const double dt = 0.5;
            for (double t = dt; t <= f.LoopSeconds; t += dt)
            {
                FlightPose p = f.PoseAt(t);
                total += Math.Sqrt((p.X - prev.X) * (p.X - prev.X) + (p.Z - prev.Z) * (p.Z - prev.Z));
                prev = p;
            }
            Assert.AreEqual(f.LoopLength, total, f.LoopLength * 0.08, "the path is walked at constant speed (side sway adds a little)");
        }

        [Test]
        public void TheViewTurnsSmoothlyWithNoSnapsAtTheJunctions()
        {
            TitleFlight f = TitleFlight.Default();
            FlightPose prev = f.PoseAt(0);
            double worst = 0;
            const double dt = 0.1;
            for (double t = dt; t < 2 * f.LoopSeconds; t += dt)
            {
                FlightPose p = f.PoseAt(t);
                worst = Math.Max(worst, Math.Abs(AngleDifference(p.Yaw, prev.Yaw)) / dt);
                prev = p;
            }
            Assert.Less(worst, 0.8, "radians per second of turn at the sharpest point");
        }

        [Test]
        public void TheCameraFacesAlongThePathAndLooksSlightlyDown()
        {
            TitleFlight f = TitleFlight.Default();
            for (double t = 0; t < f.LoopSeconds; t += 1.0)
            {
                FlightPose p = f.PoseAt(t);
                Point2 here = f.PointAt(t * TitleFlight.DefaultSpeed), ahead = f.PointAt(t * TitleFlight.DefaultSpeed + 5);
                double heading = Math.Atan2(ahead.X - here.X, ahead.Z - here.Z);
                Assert.Less(Math.Abs(AngleDifference(p.Yaw, heading)), 0.8, $"t={t}: within the turn anticipation");
                Assert.Less(p.Pitch, 0, "looking down at the street");
                Assert.Greater(p.Pitch, -0.3, "but mostly along it");
            }
        }

        [Test]
        public void ThePoseIsAFunctionOfTimeAlone()
        {
            FlightPose a = TitleFlight.Default().PoseAt(61.25), b = TitleFlight.Default().PoseAt(61.25);
            Assert.AreEqual(a.X, b.X);
            Assert.AreEqual(a.Y, b.Y);
            Assert.AreEqual(a.Z, b.Z);
            Assert.AreEqual(a.Yaw, b.Yaw);
            Assert.AreEqual(a.Pitch, b.Pitch);
        }

        [Test]
        public void ChordsMatchArcLengthSoSpeedIsEvenOnTheCurves()
        {
            TitleFlight f = TitleFlight.Default();
            for (double d = 0; d < f.LoopLength; d += 5)
            {
                Point2 a = f.PointAt(d), b = f.PointAt(d + 1);
                double chord = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
                Assert.That(chord, Is.InRange(0.95, 1.0 + 1e-9), $"d={d}");
            }
        }

        [Test]
        public void ARouteWithTightCornersStillGivesAValidLoop()
        {
            var f = new TitleFlight(new[] { new Point2(0, 0), new Point2(30, 0), new Point2(30, 30), new Point2(0, 30) }, speed: 5, height: 10);
            Assert.That(f.LoopLength, Is.InRange(95.0, 120.0));
            for (double t = 0; t < f.LoopSeconds; t += 0.25) Assert.IsFalse(double.IsNaN(f.PoseAt(t).Yaw));
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            var ok = new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) };
            Assert.Throws<ArgumentNullException>(() => new TitleFlight(null));
            Assert.Throws<ArgumentException>(() => new TitleFlight(new[] { new Point2(0, 0), new Point2(10, 0) }));
            Assert.Throws<ArgumentException>(() => new TitleFlight(new[] { new Point2(0, 0), new Point2(0, 0), new Point2(10, 10) }), "repeated point");
            Assert.Throws<ArgumentException>(() => new TitleFlight(new[] { new Point2(0, 0), new Point2(30, 0), new Point2(0, 1) }), "turns back on itself");
            Assert.Throws<ArgumentOutOfRangeException>(() => new TitleFlight(ok, speed: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TitleFlight(ok, speed: double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TitleFlight(ok, height: 0.2));
            var f = new TitleFlight(ok);
            Assert.Throws<ArgumentOutOfRangeException>(() => f.PoseAt(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.PoseAt(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.PointAt(double.NaN));
        }
    }
}
