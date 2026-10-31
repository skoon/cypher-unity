using System;
using Cypher.Actors;
using Cypher.City;
using Cypher.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    public class EnemyProxiesTests
    {
        EnemyProxies _proxies;
        GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("EnemyProxiesTest");
            _proxies = new EnemyProxies(_parent.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _proxies.Dispose();
            UnityEngine.Object.DestroyImmediate(_parent);
        }

        static CombatBody Fighter(int id, double x, double z, double crouch = 0, double death = 0)
        {
            var look = new PedestrianAppearance(1.0, 1.1, false, false, false, false, true, 0, 0, 0, EnemyLook.GangColour, 3, 0);
            return new CombatBody(id, look, true, HeldWeapon.Smg, 2)
            {
                X = x, PrevX = x, Z = z, PrevZ = z, Y = 0.185, PrevY = 0.185, Heading = 0.5, PrevHeading = 0.5, Crouch = crouch, Death = death,
            };
        }

        static Vector3 V(V3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        [Test]
        public void EveryFighterSlotHasAKinematicBodyAndHeadMarkedAsAnEnemy()
        {
            Assert.AreEqual(EnemyBodies.Max, EnemyProxies.Count);
            for (int i = 0; i < EnemyProxies.Count * 2; i++)
            {
                GameObject go = _proxies[i];
                Assert.AreSame(_parent.transform, go.transform.parent);
                Assert.IsTrue(go.GetComponent<Rigidbody>().isKinematic);
                Assert.IsFalse(go.GetComponent<Rigidbody>().useGravity);
                Assert.AreEqual(ShotTarget.Enemy, go.GetComponent<ShotSurface>().Target);
                EnemyHitbox hit = go.GetComponent<EnemyHitbox>();
                Assert.AreEqual(i % 2 == 1, hit.Head, "even slots are bodies, odd ones heads");
                Assert.AreEqual(-1, hit.Enemy);
                Assert.AreEqual(i % 2 == 1, go.GetComponent<SphereCollider>() != null);
                Assert.AreEqual(i % 2 == 0, go.GetComponent<CapsuleCollider>() != null);
            }
        }

        [Test]
        public void TheVolumesArePosedOnEachLivingFighter()
        {
            CombatBody standing = Fighter(4, 10, 20), crouched = Fighter(9, -6, 3, crouch: 1);
            _proxies.Update(new[] { standing, crouched }, 1, 2);
            Assert.AreEqual(2, _proxies.Leased);
            foreach ((CombatBody f, int slot) in new[] { (standing, 0), (crouched, 1) })
            {
                PoseHitShape h = PedestrianPose.HitShape(f.Pose(1, 2));
                Rigidbody body = _proxies[slot * 2].GetComponent<Rigidbody>(), head = _proxies[slot * 2 + 1].GetComponent<Rigidbody>();
                Assert.Less(Vector3.Distance((V(h.BodyA) + V(h.BodyB)) * 0.5f, body.position), 1e-4f);
                Assert.Less(Vector3.Distance(V(h.Head), head.position), 1e-4f);
                Assert.Less(Vector3.Angle(body.rotation * Vector3.up, V(h.BodyB) - V(h.BodyA)), 0.5f, "the capsule lies along the spine");
                CapsuleCollider c = _proxies.Capsule(slot);
                Assert.AreEqual((float)h.BodyRadius, c.radius, 1e-6f);
                Assert.AreEqual(Vector3.Distance(V(h.BodyA), V(h.BodyB)) + 2 * c.radius, c.height, 1e-4f);
                Assert.AreEqual((float)h.HeadRadius, _proxies.Sphere(slot).radius, 1e-6f);
                Assert.AreEqual(f.Id, _proxies[slot * 2].GetComponent<EnemyHitbox>().Enemy);
                Assert.AreEqual(f.Id, _proxies[slot * 2 + 1].GetComponent<EnemyHitbox>().Enemy);
            }
            Assert.Less(_proxies[3].GetComponent<Rigidbody>().position.y, _proxies[1].GetComponent<Rigidbody>().position.y - 0.5f, "the crouched head is lower");
        }

        [Test]
        public void TheDeadAreNotTargetsAndSpareSlotsWaitBelowTheCity()
        {
            _proxies.Update(new[] { Fighter(1, 0, 0, death: 0.1), Fighter(2, 5, 5) }, 1, 0);
            Assert.AreEqual(1, _proxies.Leased);
            Assert.AreEqual(2, _proxies[0].GetComponent<EnemyHitbox>().Enemy, "the living one takes the first slot");
            for (int i = 2; i < EnemyProxies.Count * 2; i++)
            {
                Assert.AreEqual(-1, _proxies[i].GetComponent<EnemyHitbox>().Enemy);
                Assert.AreEqual(EnemyProxies.ParkingSpot(i), _proxies[i].GetComponent<Rigidbody>().position);
                Assert.Less(EnemyProxies.ParkingSpot(i).y, -600f - 8, "below the crowd's parked capsules too");
            }
            _proxies.ParkAll();
            Assert.AreEqual(0, _proxies.Leased);
            Assert.AreEqual(-1, _proxies[0].GetComponent<EnemyHitbox>().Enemy);
        }

        [Test]
        public void TooManyLivingFightersIsABug()
        {
            var many = new CombatBody[EnemyProxies.Count + 1];
            for (int i = 0; i < many.Length; i++) many[i] = Fighter(i, i, 0);
            Assert.Throws<InvalidOperationException>(() => _proxies.Update(many, 1, 0));
        }
    }
}
