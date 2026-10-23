using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class VehiclePlacementTests
    {
        static Vehicle Make(int type, double x = 0, double z = 0, double heading = 0)
        {
            return new Vehicle
            {
                Type = VehicleCatalog.Ground[type], TypeIndex = type, X = x, Z = z, Y = 0, Heading = heading,
                PrevX = x, PrevZ = z, PrevHeading = heading, Glow = 0.25, Spin = 0,
            };
        }

        static (double x, double y, double z) At(Mat4 m) => (m.E[12], m.E[13], m.E[14]);

        // ---------------------------------------------------------------- interpolation

        [Test]
        public void PlacementRunsFromTheLastStepToThisOne()
        {
            Vehicle v = Make(0, 10, 20, 0.2);
            v.PrevX = 8; v.PrevZ = 22; v.PrevHeading = 0.0;
            (double x0, double z0, double h0) = VehiclePlacement.Interpolate(v, 0);
            (double x1, double z1, double h1) = VehiclePlacement.Interpolate(v, 1);
            (double xh, double zh, double hh) = VehiclePlacement.Interpolate(v, 0.5);
            Assert.AreEqual((8.0, 22.0, 0.0), (x0, z0, h0));
            Assert.AreEqual((10.0, 20.0, 0.2), (x1, z1, h1));
            Assert.AreEqual((9.0, 21.0, 0.1), (xh, zh, hh));
        }

        [Test]
        public void TheHeadingTakesTheShortWayRound()
        {
            Vehicle v = Make(0);
            v.PrevHeading = 3.0; v.Heading = -3.0;   // a turn of 0.283 rad through pi, not 6 rad the long way
            double mid = VehiclePlacement.Interpolate(v, 0.5).heading;
            Assert.AreEqual(3.0 + (2 * Math.PI - 6.0) / 2, mid, 1e-12);
            Assert.Greater(Math.Abs(mid), 3.1);
        }

        // ---------------------------------------------------------------- the parts

        [Test]
        public void TheBodyStandsWhereTheVehicleIsAndTurnsWithIt()
        {
            Vehicle v = Make(0, 10, 20, Math.PI / 2);
            VehiclePose pose = VehiclePlacement.Place(v, 1, 0, 0);
            Assert.AreEqual((10.0, 0.0, 20.0), At(pose.Body));
            // its nose (+Z in its own frame) points along +X when the heading is a quarter turn
            (double nx, double ny, double nz) = pose.Body.Transform(0, 0, 1);
            Assert.AreEqual(11.0, nx, 1e-9);
            Assert.AreEqual(20.0, nz, 1e-9);
        }

        [Test]
        public void AFourWheeledVehicleHasItsWheelsAtTheCornersAndOnTheGround()
        {
            Vehicle v = Make(0);
            GroundVehicleType t = v.Type;
            VehiclePose pose = VehiclePlacement.Place(v, 1, 0, 0);
            (double x, double y, double z)[] w = pose.Wheels.Select(At).ToArray();
            Assert.AreEqual(-t.WheelX, w[0].x, 1e-9); Assert.AreEqual(t.WheelZFront, w[0].z, 1e-9);
            Assert.AreEqual(t.WheelX, w[1].x, 1e-9); Assert.AreEqual(t.WheelZFront, w[1].z, 1e-9);
            Assert.AreEqual(-t.WheelX, w[2].x, 1e-9); Assert.AreEqual(t.WheelZRear, w[2].z, 1e-9);
            Assert.AreEqual(t.WheelX, w[3].x, 1e-9); Assert.AreEqual(t.WheelZRear, w[3].z, 1e-9);
            foreach (var wheel in w) Assert.AreEqual(t.WheelRadius, wheel.y, 1e-9, "the axle is a radius above the road");
        }

        [Test]
        public void AWheelIsAsWideAndRoundAsTheTypeSaysAndSpinsAboutItsAxle()
        {
            Vehicle v = Make(1);
            v.Spin = 0.7;
            GroundVehicleType t = v.Type;
            Mat4 wheel = VehiclePlacement.Place(v, 1, 0, 0).Wheels[0];
            // the unit wheel's axle is X: it maps to a vector of the wheel's width along X, and a point on its rim at radius 1 goes round
            (double ax, double ay, double az) = wheel.Transform(0.5, 0, 0);
            (double bx, double by, double bz) = wheel.Transform(-0.5, 0, 0);
            Assert.AreEqual(t.WheelWidth, ax - bx, 1e-9, "the width");
            Assert.AreEqual(0, ay - by, 1e-9);
            (double px, double py, double pz) = wheel.Transform(0, 1, 0);
            (double cx, double cy, double cz) = At(wheel);
            Assert.AreEqual(t.WheelRadius, Math.Sqrt((py - cy) * (py - cy) + (pz - cz) * (pz - cz)), 1e-9, "the radius");
            Assert.AreEqual(Math.Cos(0.7) * t.WheelRadius, py - cy, 1e-9, "turned by the spin");
        }

        [Test]
        public void ABikeHasTwoWheelsOnItsCentreLineAndTheOtherSlotsAreHiddenBelowTheCity()
        {
            Vehicle v = Make(3);
            VehiclePose pose = VehiclePlacement.Place(v, 1, 0, 0);
            Assert.AreEqual((0.0, v.Type.WheelRadius, v.Type.WheelZFront), At(pose.Wheels[0]));
            Assert.AreEqual((0.0, v.Type.WheelRadius, v.Type.WheelZRear), At(pose.Wheels[1]));
            Assert.AreEqual(-900.0, At(pose.Wheels[2]).y);
            Assert.AreEqual(-900.0, At(pose.Wheels[3]).y);
            Assert.AreEqual(0.001, pose.Wheels[2].E[0], 1e-12, "and collapsed to a speck");
        }

        [Test]
        public void TheWheelsTurnWithTheHeading()
        {
            Vehicle v = Make(0, 100, 50, Math.PI / 2);
            var front = At(VehiclePlacement.Place(v, 1, 0, 0).Wheels[1]);   // front right in the vehicle's frame
            // heading a quarter turn: the nose points along +X, so the front wheels are ahead of the middle by the wheel base along +X
            Assert.AreEqual(100 + v.Type.WheelZFront, front.x, 1e-9);
            Assert.AreEqual(50 - v.Type.WheelX, front.z, 1e-9);
        }

        [Test]
        public void TheLampsRigIsScaledIntoTheVehiclesBox()
        {
            Vehicle v = Make(2, 5, 6, 0);
            Mat4 rig = VehiclePlacement.Place(v, 1, 0, 0).Rig;
            GroundVehicleType t = v.Type;
            Assert.AreEqual((5 + t.Width / 2, 0 + t.Height, 6 + t.Length / 2), rig.Transform(0.5, 1, 0.5), "the unit box's far top corner is the vehicle's");
            Assert.AreEqual((5 - t.Width / 2, 0.0, 6 - t.Length / 2), rig.Transform(-0.5, 0, -0.5));
        }

        [Test]
        public void TheUnderglowPulsesBetweenHalfAndFullSizeOnTheRoad()
        {
            Vehicle v = Make(0);
            double least = double.PositiveInfinity, most = 0;
            for (int i = 0; i < 400; i++)
            {
                Mat4 glow = VehiclePlacement.Place(v, 1, i * 0.05, 0).Underglow;
                double scale = glow.E[0] / v.Type.Width;
                least = Math.Min(least, scale); most = Math.Max(most, scale);
                Assert.AreEqual(0.03, At(glow).y, 1e-12, "just above the asphalt");
                Assert.AreEqual(glow.E[10] / v.Type.Length, scale, 1e-12, "the same pulse both ways");
            }
            Assert.AreEqual(0.5, least, 0.01);
            Assert.AreEqual(1.0, most, 0.01);
        }

        [Test]
        public void TheHeadlightConesHangOffTheNose()
        {
            Vehicle v = Make(0, 0, 0, 0);
            Mat4 beams = VehiclePlacement.Place(v, 1, 0, 0).Beams;
            (double x, double y, double z) = At(beams);
            Assert.AreEqual(v.Type.Length * 0.5 - 0.15, z, 1e-9);
            Assert.AreEqual(v.Type.Height * 0.36, y, 1e-9);
            Assert.AreEqual(v.Type.Width / 1.94, beams.E[0], 1e-9, "a wider vehicle has wider cones");
        }

        [Test]
        public void TheShadowLiesOnTheRoadAndIsSmallerThanTheVehicle()
        {
            Vehicle v = Make(1);
            Mat4 shadow = VehiclePlacement.Place(v, 1, 0, 0.0).Shadow;
            Assert.AreEqual(0.02, At(shadow).y, 1e-12);
            Assert.Less(shadow.E[0], v.Type.Width);
            Assert.Less(shadow.E[10], v.Type.Length);
        }

        [Test]
        public void ARoadAboveZeroLiftsTheGlowAndTheShadowWithIt()
        {
            Vehicle v = Make(0);
            VehiclePose pose = VehiclePlacement.Place(v, 1, 0, 1.5);
            Assert.AreEqual(1.53, At(pose.Underglow).y, 1e-12);
            Assert.AreEqual(1.52, At(pose.Shadow).y, 1e-12);
        }

        // ---------------------------------------------------------------- aircraft

        [Test]
        public void AnAircraftBobsAboutItsAltitudeAndBanksAboutItsNose()
        {
            var a = new Aircraft { Type = VehicleCatalog.Aircraft[0], X = 10, Y = 50, Z = 20, PrevX = 10, PrevY = 50, PrevZ = 20, Heading = 0, Bank = 0.1, Bob = Math.PI / 2 };
            (Mat4 body, Mat4 glow) = VehiclePlacement.PlaceAircraft(a, 1);
            Assert.AreEqual((10.0, 50.8, 20.0), At(body), "0.8 m above its altitude at the top of the bob");
            Assert.AreEqual(At(body), At(glow));
            (double lx, double ly, double lz) = body.Transform(1, 0, 0);
            Assert.AreEqual(Math.Cos(0.1) + 10, lx, 1e-9, "its right wing tip is banked about the nose axis");
            Assert.AreEqual(50.8 + Math.Sin(0.1), ly, 1e-9);
            Assert.AreEqual(a.Type.Width, glow.E[0] / Math.Cos(0.1), 1e-9, "the glow is scaled into the aircraft's box");
        }

        [Test]
        public void AnAircraftFlyingNorthEastPointsThatWay()
        {
            var a = new Aircraft { Type = VehicleCatalog.Aircraft[1], Heading = Math.PI / 4 };
            (Mat4 body, _) = VehiclePlacement.PlaceAircraft(a, 1);
            (double nx, double ny, double nz) = body.Transform(0, 0, 1);
            Assert.AreEqual(Math.Sin(Math.PI / 4), nx, 1e-9);
            Assert.AreEqual(Math.Cos(Math.PI / 4), nz, 1e-9);
        }

        [Test]
        public void TheQuaternionsComposeAsThreeJsDoes()
        {
            // turn about X by a quarter, then about Y by a quarter (three.js's qY.multiply(qX)): +Z goes up to... apply X first: +Y -> +Z, +Z -> -Y; then Y: +Z -> +X
            VehiclePlacement.Quat q = VehiclePlacement.Quat.Multiply(VehiclePlacement.Quat.AboutY(Math.PI / 2), VehiclePlacement.Quat.AboutX(Math.PI / 2));
            Mat4 m = Mat4.Compose(0, 0, 0, q.X, q.Y, q.Z, q.W, 1, 1, 1);
            (double x, double y, double z) = m.Transform(0, 1, 0);
            Assert.AreEqual(1.0, x, 1e-12, "+Y turns to +Z about X, then +Z turns to +X about Y");
            Assert.AreEqual(0.0, y, 1e-12);
            Assert.AreEqual(0.0, z, 1e-12);
        }
    }
}
