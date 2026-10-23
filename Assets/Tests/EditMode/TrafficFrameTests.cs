using System;
using System.Linq;
using System.Runtime.InteropServices;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class TrafficFrameTests
    {
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate());

        static TrafficSim Make(int ground = 52, int aircraft = 20)
        {
            TrafficSim sim = TrafficSim.Create(Nav, 0, 0, ground, aircraft);
            for (int i = 0; i < 120; i++) sim.Step(1.0 / 60, 0, 0, 0, 0);
            return sim;
        }

        static Frustum Nothing()
        {
            var f = new Frustum();
            for (int i = 0; i < 6; i++) f.SetPlane(i, 0, 1, 0, -1e9);
            return f;
        }

        [Test]
        public void AVehicleIsFortyEightBytes()
        {
            Assert.AreEqual(VehicleGpuData.Size, Marshal.SizeOf<VehicleGpuData>());
        }

        [Test]
        public void EverythingInViewIsDrawnInARunForEachType()
        {
            TrafficSim sim = Make();
            var frame = new TrafficFrame();
            frame.Build(sim, Frustum.Everything(), 0, 0, 0.5);

            int active = sim.Vehicles.Count(v => v.Active);
            Assert.AreEqual(active, frame.GroundTotal);
            Assert.AreEqual(sim.Airborne.Count, frame.AircraftTotal);
            for (int type = 0; type < 4; type++)
            {
                Assert.AreEqual(sim.Vehicles.Count(v => v.Active && v.TypeIndex == type), frame.GroundCount(type), VehicleCatalog.Ground[type].Key);
                for (int k = 0; k < frame.GroundCount(type); k++) Assert.AreEqual(type, frame.Ground[frame.GroundStart(type) + k].Type, $"{VehicleCatalog.Ground[type].Key} run, entry {k}");
            }
            for (int type = 0; type < 2; type++)
            {
                Assert.AreEqual(sim.Airborne.Count(a => a.TypeIndex == type), frame.AircraftCount(type));
                for (int k = 0; k < frame.AircraftCount(type); k++) Assert.AreEqual(type, frame.Aircraft[frame.AircraftStart(type) + k].Type);
            }
            Assert.AreEqual(frame.GroundTotal, Enumerable.Range(0, 4).Sum(frame.GroundCount));
            Assert.AreEqual(0, frame.GroundStart(0));
            Assert.AreEqual(frame.GroundCount(0), frame.GroundStart(1));
        }

        [Test]
        public void OnlyWhatIsInViewIsDrawn()
        {
            TrafficSim sim = Make();
            var frame = new TrafficFrame();
            frame.Build(sim, Nothing(), 0, 0, 0.5);
            Assert.AreEqual(0, frame.GroundTotal);
            Assert.AreEqual(0, frame.AircraftTotal);
            Assert.AreEqual(0, frame.LightSpots.Count);

            // a frustum that is only the half-space x > 0
            var half = new Frustum();
            for (int i = 0; i < 6; i++) half.SetPlane(i, 1, 0, 0, 0);
            frame.Build(sim, half, 0, 0, 0.5);
            int expected = sim.Vehicles.Count(v => v.Active && v.X > Math.Max(v.Type.Length, v.Type.Width));
            Assert.GreaterOrEqual(frame.GroundTotal, expected, "at least everything clearly inside");
            Assert.Less(frame.GroundTotal, sim.Vehicles.Count(v => v.Active), "and not everything");
            for (int i = 0; i < frame.GroundTotal; i++) Assert.Greater(frame.Ground[i].X, -9, "nothing far outside");
        }

        [Test]
        public void ThePositionIsInterpolatedBetweenTheLastTwoSteps()
        {
            TrafficSim sim = Make(20, 0);
            Vehicle moving = sim.Vehicles.First(v => v.Active && v.Speed > 5);
            var frame = new TrafficFrame();
            foreach ((double alpha, double x, double z) in new[] { (0.0, moving.PrevX, moving.PrevZ), (0.5, (moving.PrevX + moving.X) / 2, (moving.PrevZ + moving.Z) / 2), (1.0, moving.X, moving.Z) })
            {
                frame.Build(sim, Frustum.Everything(), 0, 0, alpha);
                Assert.IsTrue(frame.Ground.Take(frame.GroundTotal).Any(d => Math.Abs(d.X - x) < 1e-3 && Math.Abs(d.Z - z) < 1e-3), $"the moving vehicle at alpha {alpha}");
            }
            Assert.Greater(Math.Abs(moving.X - moving.PrevX) + Math.Abs(moving.Z - moving.PrevZ), 0.05, "it did move in the step");
        }

        [Test]
        public void EachEntryCarriesTheVehiclesLookAndSpin()
        {
            TrafficSim sim = Make(20, 0);
            var frame = new TrafficFrame();
            frame.Build(sim, Frustum.Everything(), 0, 0, 1);
            foreach (Vehicle v in sim.Vehicles.Where(v => v.Active))
            {
                VehicleGpuData d = frame.Ground.Take(frame.GroundTotal).Single(e => Math.Abs(e.X - v.X) < 1e-3 && Math.Abs(e.Z - v.Z) < 1e-3);
                Assert.AreEqual(v.TypeIndex, d.Type);
                Assert.AreEqual(VehicleCatalog.Paint[v.PaintIndex], d.Paint);
                Assert.AreEqual(VehicleCatalog.Neon[v.NeonIndex], d.Neon);
                Assert.AreEqual(v.Spin, d.Spin, 1e-3);
                Assert.AreEqual(v.Glow, d.Glow, 1e-6);
                Assert.AreEqual(v.LampTint, d.LampTint, 1e-6);
                Assert.AreEqual(v.Heading, d.Heading, 1e-5);
                Assert.AreEqual(Nav.RoadY, d.Y);
            }
        }

        [Test]
        public void AnAircraftEntryHasItsBankBobAndPaint()
        {
            TrafficSim sim = Make(0, 20);
            var frame = new TrafficFrame();
            frame.Build(sim, Frustum.Everything(), 0, 0, 1);
            foreach (Aircraft a in sim.Airborne)
            {
                VehicleGpuData d = frame.Aircraft.Take(frame.AircraftTotal).Single(e => Math.Abs(e.X - a.X) < 1e-3 && Math.Abs(e.Z - a.Z) < 1e-3);
                Assert.AreEqual(a.TypeIndex, d.Type);
                Assert.AreEqual(a.Bank, d.Spin, 1e-6, "the bank rides where a vehicle's spin does");
                Assert.AreEqual(a.Y + Math.Sin(a.Bob) * 0.8, d.Y, 1e-3);
                Assert.AreEqual(VehicleCatalog.Paint[a.PaintIndex], d.Paint);
            }
        }

        [Test]
        public void ThePointLightsGoToTheNearestVehiclesWithin70Metres()
        {
            TrafficSim sim = Make(96, 0);
            var frame = new TrafficFrame();
            frame.Build(sim, Frustum.Everything(), 0, 0, 1);
            Assert.Greater(frame.LightSpots.Count, 0);
            for (int i = 0; i < frame.LightSpots.Count; i++)
            {
                Assert.Less(frame.LightSpots[i].d2, TrafficFrame.LightRange * TrafficFrame.LightRange);
                if (i > 0) Assert.GreaterOrEqual(frame.LightSpots[i].d2, frame.LightSpots[i - 1].d2, "nearest first");
            }
            int within = sim.Vehicles.Count(v => v.Active && v.X * v.X + v.Z * v.Z < 4900);
            Assert.AreEqual(within, frame.LightSpots.Count, "one for each vehicle that near");
        }

        [Test]
        public void ALightSitsAtTheNoseAtHeadlampHeight()
        {
            TrafficSim sim = Make(1, 0);
            Vehicle v = sim.Vehicles[0];
            v.Active = true; v.X = 10; v.Z = 0; v.PrevX = 10; v.PrevZ = 0; v.Heading = Math.PI / 2; v.PrevHeading = Math.PI / 2;
            var frame = new TrafficFrame();
            frame.Build(sim, Frustum.Everything(), 0, 0, 1);
            var spot = frame.LightSpots.Single();
            Assert.AreEqual(10 + v.HalfLength, spot.x, 1e-9, "the nose is along +X");
            Assert.AreEqual(v.Type.Height * 0.34, spot.y, 1e-9);
            Assert.AreEqual(0, spot.z, 1e-9);
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            var frame = new TrafficFrame();
            TrafficSim sim = Make(1, 0);
            Assert.Throws<ArgumentNullException>(() => frame.Build(null, Frustum.Everything(), 0, 0, 0.5));
            Assert.Throws<ArgumentNullException>(() => frame.Build(sim, null, 0, 0, 0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.Build(sim, Frustum.Everything(), 0, 0, 1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.Build(sim, Frustum.Everything(), 0, 0, -0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.GroundCount(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.GroundStart(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.AircraftCount(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.AircraftStart(2));
        }
    }
}
