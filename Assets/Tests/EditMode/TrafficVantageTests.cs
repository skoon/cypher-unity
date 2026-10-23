using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class TrafficVantageTests
    {
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate());

        static TrafficSim Make() => TrafficSim.Create(Nav, 0, 0, 96, 36);

        [Test]
        public void TheNearestVehicleIsTheNearestOfItsKind()
        {
            TrafficSim sim = Make();
            foreach (int type in new[] { -1, 0, 1, 2, 3 })
            {
                Vehicle v = TrafficVantage.NearestVehicle(sim, 10, 20, 1000, type);
                Assert.IsNotNull(v);
                if (type >= 0) Assert.AreEqual(type, v.TypeIndex);
                double d2 = (v.X - 10) * (v.X - 10) + (v.Z - 20) * (v.Z - 20);
                foreach (Vehicle o in sim.Vehicles.Where(o => o.Active && (type < 0 || o.TypeIndex == type))) Assert.LessOrEqual(d2, (o.X - 10) * (o.X - 10) + (o.Z - 20) * (o.Z - 20) + 1e-9);
            }
            Assert.IsNull(TrafficVantage.NearestVehicle(sim, 5000, 5000, 100), "nothing within 100 m of somewhere far away");
        }

        [Test]
        public void TheNearestAircraftIsTheNearestOfItsKind()
        {
            TrafficSim sim = Make();
            Aircraft a = TrafficVantage.NearestAircraft(sim, 0, 0, 10000, 1);
            Assert.IsNotNull(a);
            Assert.AreEqual(1, a.TypeIndex);
            Assert.IsNull(TrafficVantage.NearestAircraft(sim, 90000, 0, 100));
        }

        [Test]
        public void AVehicleShotIsFromAheadAndTheLeftAtTheGivenDistanceLookingAtTheVehicle()
        {
            Vehicle v = new Vehicle { Type = VehicleCatalog.Ground[0], X = 10, Z = 20, Y = 0, Heading = 0 };
            TrafficShot shot = TrafficVantage.FrameVehicle("x", v, 6);
            double dx = shot.CamX - v.X, dz = shot.CamZ - v.Z;
            Assert.AreEqual(6.0, Math.Sqrt(dx * dx + dz * dz), 1e-9);
            Assert.Greater(dz, 0, "ahead of a vehicle facing +Z");
            Assert.Less(dx, 0, "and on its left: the left of +Z seen from above is -X");
            Assert.AreEqual(v.X, shot.TargetX);
            Assert.AreEqual(v.Z, shot.TargetZ);
            Assert.Greater(shot.CamY, v.Type.Height * 0.7, "above its roof line");
        }

        [Test]
        public void AnAircraftShotIsAbeamAndLevel()
        {
            var a = new Aircraft { Type = VehicleCatalog.Aircraft[0], X = 0, Y = 50, Z = 0, Heading = Math.PI / 2 };
            TrafficShot shot = TrafficVantage.FrameAircraft("x", a, 20);
            Assert.AreEqual(50 + 1.5, shot.CamY, 1e-9);
            Assert.AreEqual(50.0, shot.TargetY);
            double dist = Math.Sqrt(shot.CamX * shot.CamX + shot.CamZ * shot.CamZ);
            Assert.AreEqual(20 * Math.Sqrt(0.9 * 0.9 + 0.45 * 0.45), dist, 1e-9);
        }

        [Test]
        public void TheNearestJunctionIsFoundOnBothAxes()
        {
            (int ix, int iz) = TrafficVantage.NearestJunction(Nav, 40, -80);
            Assert.AreEqual(31, Nav.XLines[ix], 1e-9);
            Assert.AreEqual(-93, Nav.ZLines[iz], 1e-9);
        }

        [Test]
        public void TheJunctionShotLooksAtTheMiddleOfTheJunctionFromShortOfIt()
        {
            TrafficShot shot = TrafficVantage.Junction("j", Nav, 4, 3);
            Assert.AreEqual(Nav.XLines[4], shot.TargetX);
            Assert.AreEqual(Nav.ZLines[3], shot.TargetZ);
            Assert.AreEqual(Nav.ZLines[3] - 24, shot.CamZ, 1e-9);
        }

        [Test]
        public void PickGivesAShotForEachKindThatIsNearEnoughAndTheJunction()
        {
            TrafficSim sim = Make();
            var shots = TrafficVantage.Pick(sim, 0, 0, 1000);
            CollectionAssert.AreEqual(new[] { "vehicle_sedan", "vehicle_van", "vehicle_truck", "vehicle_bike", "aircraft_spinner", "aircraft_freighter", "junction_signals" }, shots.Select(s => s.Name).ToArray());
            Assert.AreEqual(shots.Count, shots.Select(s => s.Name).Distinct().Count());
            Assert.Less(TrafficVantage.Pick(sim, 5000, 5000, 50).Count, 7, "kinds nobody is close to are left out");
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            TrafficSim sim = Make();
            Assert.Throws<ArgumentNullException>(() => TrafficVantage.NearestVehicle(null, 0, 0, 1));
            Assert.Throws<ArgumentNullException>(() => TrafficVantage.NearestAircraft(null, 0, 0, 1));
            Assert.Throws<ArgumentNullException>(() => TrafficVantage.FrameVehicle("x", null, 3));
            Assert.Throws<ArgumentNullException>(() => TrafficVantage.FrameAircraft("x", null, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => TrafficVantage.FrameVehicle("x", sim.Vehicles[0], 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TrafficVantage.FrameAircraft("x", sim.Airborne[0], -1));
            Assert.Throws<ArgumentNullException>(() => TrafficVantage.NearestJunction(null, 0, 0));
            Assert.Throws<ArgumentNullException>(() => TrafficVantage.Junction("x", null, 0, 0));
            Assert.Throws<ArgumentNullException>(() => TrafficVantage.Pick(null, 0, 0));
        }
    }
}
