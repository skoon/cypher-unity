using System;
using System.Linq;
using Cypher.Actors;
using Cypher.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    public class CrowdProxiesTests
    {
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate());
        CrowdProxies _proxies;
        GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("ProxiesTest");
            _proxies = new CrowdProxies(_parent.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _proxies.Dispose();
            UnityEngine.Object.DestroyImmediate(_parent);
        }

        static CrowdSim Sim(params (double x, double z)[] at)
        {
            CrowdSim sim = CrowdSim.Create(Nav, Math.Max(1, at.Length), at.Length, 0, 0);
            for (int i = 0; i < at.Length; i++)
            {
                Pedestrian p = sim.Active[i];
                p.X = at[i].x; p.Z = at[i].z; p.Y = 0.185;
            }
            return sim;
        }

        [Test]
        public void ThereAreEightKinematicCapsulesTheSizeOfAPerson()
        {
            Assert.AreEqual(8, CrowdProxies.Count);
            for (int i = 0; i < CrowdProxies.Count; i++)
            {
                GameObject go = _proxies[i];
                Assert.AreSame(_parent.transform, go.transform.parent);
                Rigidbody body = go.GetComponent<Rigidbody>();
                Assert.IsTrue(body.isKinematic);
                Assert.IsFalse(body.useGravity);
                CapsuleCollider capsule = go.GetComponent<CapsuleCollider>();
                Assert.AreEqual(0.30f, capsule.radius, 1e-6f);
                Assert.AreEqual(1.64f, capsule.height, 1e-6f);
                Assert.AreEqual(1, capsule.direction, "upright");
            }
        }

        [Test]
        public void UnusedCapsulesWaitFarBelowTheCityEachInItsOwnSpot()
        {
            var spots = Enumerable.Range(0, 8).Select(i => _proxies[i].GetComponent<Rigidbody>().position).ToList();
            Assert.AreEqual(8, spots.Distinct().Count());
            Assert.IsTrue(spots.All(s => s.y <= -600f));
            Assert.AreEqual(0, _proxies.Leased);
        }

        [Test]
        public void TheNearestPeopleGetCapsulesAtTheirChests()
        {
            CrowdSim sim = Sim((3, 0), (1, 0), (5, 0), (20, 0));
            _proxies.Update(sim, 0, 0);
            Assert.AreEqual(3, _proxies.Leased, "three are within six metres");
            Rigidbody nearest = _proxies[0].GetComponent<Rigidbody>();
            Pedestrian p = sim.Active[1];
            Assert.AreEqual(1f, nearest.position.x, 1e-5f);
            Assert.AreEqual(0.185f + 0.86f * (float)p.Appearance.Scale, nearest.position.y, 1e-4f, "centred 0.86 m up a person of scale 1, more for a taller one");
            for (int i = 3; i < 8; i++) Assert.LessOrEqual(_proxies[i].GetComponent<Rigidbody>().position.y, -600f, "the rest wait");
        }

        [Test]
        public void ACapsuleFollowsItsPersonAndGoesBackWhenTheyLeave()
        {
            CrowdSim sim = Sim((2, 0));
            _proxies.Update(sim, 0, 0);
            Assert.AreEqual(2f, _proxies[0].GetComponent<Rigidbody>().position.x, 1e-5f);
            sim.Active[0].X = 2.5;
            _proxies.Update(sim, 0, 0);
            Assert.AreEqual(2.5f, _proxies[0].GetComponent<Rigidbody>().position.x, 1e-5f);
            sim.Active[0].X = 40;
            _proxies.Update(sim, 0, 0);
            Assert.AreEqual(0, _proxies.Leased);
            Assert.LessOrEqual(_proxies[0].GetComponent<Rigidbody>().position.y, -600f);
        }

        [Test]
        public void ParkAllSendsEveryCapsuleAway()
        {
            CrowdSim sim = Sim((1, 0), (2, 0));
            _proxies.Update(sim, 0, 0);
            Assert.AreEqual(2, _proxies.Leased);
            _proxies.ParkAll();
            Assert.AreEqual(0, _proxies.Leased);
            for (int i = 0; i < 8; i++) Assert.AreEqual(CrowdProxies.ParkingSpot(i), _proxies[i].GetComponent<Rigidbody>().position);
        }

        [Test]
        public void AParkingSpotIsDirectlyBelowTheOriginAndFurtherDownForEachCapsule()
        {
            Assert.AreEqual(new Vector3(0, -600, 0), CrowdProxies.ParkingSpot(0));
            Assert.AreEqual(new Vector3(0, -607, 0), CrowdProxies.ParkingSpot(7));
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => _proxies.Update(null, 0, 0));
        }
    }
}
