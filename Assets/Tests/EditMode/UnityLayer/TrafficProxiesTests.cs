using System;
using System.Linq;
using Cypher.Actors;
using Cypher.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    public class TrafficProxiesTests
    {
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate());
        TrafficProxies _proxies;
        GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("VehicleProxiesTest");
            _proxies = new TrafficProxies(_parent.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _proxies.Dispose();
            UnityEngine.Object.DestroyImmediate(_parent);
        }

        /// <summary>A traffic sim with the first few vehicles put where the test says, facing +Z, and the rest taken off the road.</summary>
        static TrafficSim Sim(params (double x, double z, int type)[] at)
        {
            TrafficSim sim = TrafficSim.Create(Nav, 0, 0, 96, 0);
            foreach (Vehicle v in sim.Vehicles) v.Active = false;
            for (int i = 0; i < at.Length; i++)
            {
                Vehicle v = sim.Fleet[i];
                v.Type = VehicleCatalog.Ground[at[i].type]; v.TypeIndex = at[i].type;
                v.Active = true; v.X = at[i].x; v.Z = at[i].z; v.Y = 0; v.Heading = 0;
            }
            return sim;
        }

        [Test]
        public void ThereAreFourKinematicBoxes()
        {
            Assert.AreEqual(4, TrafficProxies.Count);
            for (int i = 0; i < TrafficProxies.Count; i++)
            {
                GameObject go = _proxies[i];
                Assert.AreSame(_parent.transform, go.transform.parent);
                Rigidbody body = go.GetComponent<Rigidbody>();
                Assert.IsTrue(body.isKinematic);
                Assert.IsFalse(body.useGravity);
                Assert.IsNotNull(go.GetComponent<BoxCollider>());
            }
        }

        [Test]
        public void UnusedBoxesWaitFarBelowTheCityEachInItsOwnSpot()
        {
            var spots = Enumerable.Range(0, 4).Select(i => _proxies[i].GetComponent<Rigidbody>().position).ToList();
            Assert.AreEqual(4, spots.Distinct().Count());
            Assert.IsTrue(spots.All(s => s.y <= -600f));
            Assert.AreEqual(0, _proxies.Leased);
        }

        [Test]
        public void TheNearestVehiclesGetBoxesTheSizeOfTheirBodies()
        {
            TrafficSim sim = Sim((4, 0, 0), (-8, 0, 2), (30, 0, 1));
            _proxies.Update(sim, 0, 0);
            Assert.AreEqual(2, _proxies.Leased, "two are within twenty metres");
            BoxCollider sedan = _proxies[0].GetComponent<BoxCollider>(), truck = _proxies[1].GetComponent<BoxCollider>();
            Assert.AreEqual(4f, _proxies[0].GetComponent<Rigidbody>().position.x, 1e-5f);
            Assert.AreEqual(new Vector3(1.94f, 1.46f, 4.62f), sedan.size);
            Assert.AreEqual(0.73f, sedan.center.y, 1e-5f, "standing on the road");
            Assert.AreEqual(new Vector3(2.48f, 3.36f, 8.6f), truck.size);
            Assert.AreEqual(-8f, _proxies[1].GetComponent<Rigidbody>().position.x, 1e-5f);
            for (int i = 2; i < 4; i++) Assert.LessOrEqual(_proxies[i].GetComponent<Rigidbody>().position.y, -600f, "the rest wait");
        }

        [Test]
        public void ABoxTurnsWithItsVehicle()
        {
            TrafficSim sim = Sim((5, 0, 0));
            sim.Fleet[0].Heading = Math.PI / 2;
            _proxies.Update(sim, 0, 0);
            Quaternion q = _proxies[0].GetComponent<Rigidbody>().rotation;
            Vector3 nose = q * Vector3.forward;
            Assert.AreEqual(1f, nose.x, 1e-5f, "heading a quarter turn points the nose along +X");
            Assert.AreEqual(0f, nose.z, 1e-5f);
        }

        [Test]
        public void ABoxGoesBackWhenItsVehicleLeaves()
        {
            TrafficSim sim = Sim((5, 0, 0));
            _proxies.Update(sim, 0, 0);
            Assert.AreEqual(1, _proxies.Leased);
            sim.Fleet[0].X = 100;
            _proxies.Update(sim, 0, 0);
            Assert.AreEqual(0, _proxies.Leased);
            Assert.LessOrEqual(_proxies[0].GetComponent<Rigidbody>().position.y, -600f);
        }

        [Test]
        public void ParkAllSendsEveryBoxAway()
        {
            TrafficSim sim = Sim((3, 0, 0), (-3, 0, 0));
            _proxies.Update(sim, 0, 0);
            Assert.AreEqual(2, _proxies.Leased);
            _proxies.ParkAll();
            Assert.AreEqual(0, _proxies.Leased);
            for (int i = 0; i < 4; i++) Assert.AreEqual(TrafficProxies.ParkingSpot(i), _proxies[i].GetComponent<Rigidbody>().position);
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => _proxies.Update(null, 0, 0));
        }
    }
}
