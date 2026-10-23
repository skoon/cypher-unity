using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Actors;
using Cypher.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    public class TrafficLightsTests
    {
        TrafficLights _lights;
        GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("VehicleLightsTest");
            _lights = new TrafficLights(_parent.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _lights.Dispose();
            UnityEngine.Object.DestroyImmediate(_parent);
        }

        static IReadOnlyList<(double d2, double x, double y, double z)> Spots(int n) =>
            Enumerable.Range(0, n).Select(i => ((double)(i * i), 10.0 + i, 0.5, 20.0 - i)).ToList();

        [Test]
        public void ThereAreSixPointLightsWithoutShadowsAndTheProtypesRange()
        {
            Assert.AreEqual(QualityTiers.MaxVehicleLights, _lights.Lights.Count);
            foreach (Light l in _lights.Lights)
            {
                Assert.AreEqual(LightType.Point, l.type);
                Assert.AreEqual(LightShadows.None, l.shadows);
                Assert.AreEqual(26f, l.range);
                Assert.AreEqual(18f / Mathf.PI, l.intensity, 1e-5f, "the prototype's 18, as the night fills have their pi taken out");
                Assert.IsFalse(l.enabled, "dark until given a vehicle");
            }
        }

        [Test]
        public void TheNearestSpotsGetTheLightsUpToTheTiersNumber()
        {
            _lights.Apply(Spots(10), 6);
            Assert.AreEqual(6, _lights.Lit);
            for (int i = 0; i < 6; i++)
            {
                Assert.IsTrue(_lights.Lights[i].enabled);
                Assert.AreEqual(new Vector3(10f + i, 0.5f, 20f - i), _lights.Lights[i].transform.position);
            }
            _lights.Apply(Spots(10), 2);
            Assert.AreEqual(2, _lights.Lit, "Performance has two");
            Assert.IsTrue(_lights.Lights[1].enabled);
            Assert.IsFalse(_lights.Lights[2].enabled);
        }

        [Test]
        public void FewerSpotsThanLightsLeavesTheRestDark()
        {
            _lights.Apply(Spots(3), 6);
            Assert.AreEqual(3, _lights.Lit);
            Assert.AreEqual(3, _lights.Lights.Count(l => l.enabled));
            _lights.Apply(Spots(0), 6);
            Assert.AreEqual(0, _lights.Lights.Count(l => l.enabled));
        }

        [Test]
        public void OffSwitchesEveryLightOff()
        {
            _lights.Apply(Spots(6), 6);
            _lights.Off();
            Assert.AreEqual(0, _lights.Lit);
            Assert.IsFalse(_lights.Lights.Any(l => l.enabled));
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => _lights.Apply(null, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => _lights.Apply(Spots(2), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _lights.Apply(Spots(2), 7));
        }
    }
}
