using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class CrowdVantageTests
    {
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate());

        static CrowdSim Sim(int n = 200) => CrowdSim.Create(Nav, n, n, 0, 0);

        [Test]
        public void TheNearestPersonIsTheNearest()
        {
            CrowdSim sim = Sim();
            Pedestrian p = CrowdVantage.Nearest(sim, 30, 40, 1000);
            double d = Math.Sqrt((p.X - 30) * (p.X - 30) + (p.Z - 40) * (p.Z - 40));
            Assert.AreEqual(sim.Active.Min(q => Math.Sqrt((q.X - 30) * (q.X - 30) + (q.Z - 40) * (q.Z - 40))), d, 1e-9);
        }

        [Test]
        public void AFilterAndARangeNarrowTheSearch()
        {
            CrowdSim sim = Sim();
            Pedestrian umbrella = CrowdVantage.Nearest(sim, 0, 0, 1000, p => p.Appearance.Umbrella);
            Assert.IsTrue(umbrella.Appearance.Umbrella);
            Assert.IsNull(CrowdVantage.Nearest(sim, 5000, 5000, 10), "nobody for miles");
            Assert.IsNull(CrowdVantage.Nearest(sim, 0, 0, 1000, p => false));
        }

        [Test]
        public void TheCameraStandsAheadAndToTheLeftLookingAtTheChest()
        {
            CrowdSim sim = Sim(1);
            Pedestrian p = sim.Active[0];
            p.X = 10; p.Z = 20; p.Y = 0.185; p.Heading = 0;   // facing +Z
            PersonShot shot = CrowdVantage.Frame("x", p, 4);
            Assert.AreEqual(10 - 4 * Math.Sqrt(0.5), shot.CamX, 1e-9, "to their left, which is -X when facing +Z");
            Assert.AreEqual(20 + 4 * Math.Sqrt(0.5), shot.CamZ, 1e-9, "and ahead of them");
            Assert.AreEqual(4.0, Math.Sqrt((shot.CamX - p.X) * (shot.CamX - p.X) + (shot.CamZ - p.Z) * (shot.CamZ - p.Z)), 1e-9);
            Assert.AreEqual(p.Y + 1.5 * p.Appearance.Scale, shot.CamY, 1e-9);
            Assert.AreEqual(10.0, shot.TargetX, 1e-9);
            Assert.AreEqual(p.Y + p.Appearance.Scale, shot.TargetY, 1e-9);
        }

        [Test]
        public void TheLeftTurnsWithTheHeading()
        {
            CrowdSim sim = Sim(1);
            Pedestrian p = sim.Active[0];
            p.X = 0; p.Z = 0; p.Heading = Math.PI / 2;   // facing +X
            PersonShot shot = CrowdVantage.Frame("x", p, 2);
            Assert.Greater(shot.CamX, 0, "ahead is +X");
            Assert.Greater(shot.CamZ, 0, "and their left is +Z when facing +X");
        }

        [Test]
        public void PickGivesAShotForEachKindOfPersonThereIs()
        {
            CrowdSim sim = Sim(400);
            var shots = CrowdVantage.Pick(sim, 0, 0, range: 400);
            CollectionAssert.AreEqual(new[] { "person_nearest", "person_umbrella", "person_long_coat", "person_hood", "person_pack_and_trim" }, shots.Select(s => s.Name).ToArray());
        }

        [Test]
        public void AKindNobodyIsCloseEnoughToIsLeftOut()
        {
            CrowdSim sim = Sim(30);
            Assert.AreEqual(0, CrowdVantage.Pick(sim, 5000, 5000, range: 10).Count);
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            CrowdSim sim = Sim(5);
            Assert.Throws<ArgumentNullException>(() => CrowdVantage.Nearest(null, 0, 0, 10));
            Assert.Throws<ArgumentNullException>(() => CrowdVantage.Frame("x", null, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => CrowdVantage.Frame("x", sim.Active[0], 0));
        }
    }
}
