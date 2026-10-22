using System.Linq;
using Cypher.City;
using Cypher.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    /// <summary>The edge of the world, with the real controller and the real colliders.</summary>
    public class WorldEdgePlayTests
    {
        PlayerTestRig _rig;

        [TearDown]
        public void TearDown() => _rig?.Dispose();

        /// <summary>Stand on the outermost road at the middle of a side, face outward and run into the wall for a while.</summary>
        Vector3 RunIntoTheWall(int side)
        {
            _rig = new PlayerTestRig(windowRadius: 3);
            float road = (float)CityLayout.RoadCentre(CityLayout.Grid);   // the outer road on the high side
            Vector3[] starts = { new Vector3(0f, 1f, -road), new Vector3(0f, 1f, road), new Vector3(-road, 1f, 0f), new Vector3(road, 1f, 0f) };
            float[] yaws = { 180f, 0f, 270f, 90f };
            _rig.PlaceAndSettle(starts[side], yaws[side]);
            _rig.Input.Forward = 1;
            _rig.Input.Sprint = true;
            _rig.Run(10f);   // 74 m at a sprint, against an apron of 38 m
            return _rig.Player.transform.position;
        }

        [TestCase(0, "south")]
        [TestCase(1, "north")]
        [TestCase(2, "west")]
        [TestCase(3, "east")]
        public void TheWallStopsAPlayerRunningOutOnEverySide(int side, string name)
        {
            Vector3 p = RunIntoTheWall(side);
            float limit = (float)WorldEdge.PlayHalf;
            float radius = (float)PlayerMotor.Radius;
            float along = side < 2 ? Mathf.Abs(p.z) : Mathf.Abs(p.x);
            Assert.LessOrEqual(along, limit + 0.05f, $"went through the {name} wall: {p}");
            Assert.GreaterOrEqual(along, limit - radius - 1.5f, $"never reached the {name} wall: {p}");
            Assert.Greater(p.y, -0.1f, "fell off the world");
            Assert.IsTrue(_rig.Player.Motor.Grounded);
        }

        [Test]
        public void JumpingAtTheWallDoesNotGetOverIt()
        {
            _rig = new PlayerTestRig(windowRadius: 3);
            float road = (float)CityLayout.RoadCentre(CityLayout.Grid);
            _rig.PlaceAndSettle(new Vector3(road, 1f, 0f), 90f);
            _rig.Input.Forward = 1;
            _rig.Input.Sprint = true;
            for (int i = 0; i < 12; i++)
            {
                _rig.Input.PressJump();
                _rig.Run(1f);
            }
            Assert.LessOrEqual(_rig.Player.transform.position.x, (float)WorldEdge.PlayHalf + 0.05f);
        }

        [Test]
        public void TheWholeCityHasAnInvisibleWallAndASkylineRingThatCannotBeWalkedInto()
        {
            _rig = new PlayerTestRig(windowRadius: 3);
            Transform root = _rig.City.Root;

            Transform edge = root.Find(CityBuilder.EdgeName);
            Assert.IsNotNull(edge, "no world edge");
            Assert.AreEqual(4, edge.GetComponents<BoxCollider>().Length);
            Assert.IsNull(edge.GetComponent<Renderer>(), "the wall is invisible");

            Transform ring = root.Find(CityBuilder.SkylineName);
            Assert.IsNotNull(ring, "no skyline ring");
            Assert.GreaterOrEqual(ring.childCount, 8);
            Assert.AreEqual(0, ring.GetComponentsInChildren<Collider>().Length, "nobody can reach the towers, so they carry no colliders");
            Assert.Greater(ring.GetComponentsInChildren<MeshRenderer>().Length, 0);
            Assert.AreEqual(SkylineRing.Build().Count, ring.childCount);
            Assert.IsTrue(ring.GetComponentsInChildren<MeshRenderer>().All(r => r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off));
            Assert.IsNotNull(_rig.City.Skyline);
        }

        [Test]
        public void ASliceOfTheCityHasNeitherAnEdgeNorARing()
        {
            _rig = new PlayerTestRig(windowRadius: 1);
            Assert.IsNull(_rig.City.Root.Find(CityBuilder.EdgeName), "a 3x3 window has no edge of the world");
            Assert.IsNull(_rig.City.Root.Find(CityBuilder.SkylineName));
            Assert.IsNull(_rig.City.Skyline);
            Assert.IsFalse(_rig.City.HasWorldEdge);
        }

        [Test]
        public void EveryTowerOfTheRingStandsOnTheRoadbed()
        {
            _rig = new PlayerTestRig(windowRadius: 3);
            Transform ring = _rig.City.Root.Find(CityBuilder.SkylineName);
            Transform roadbed = _rig.City.Root.Find("Roadbed");
            Bounds ground = roadbed.GetComponent<MeshRenderer>().bounds;
            foreach (MeshRenderer r in ring.GetComponentsInChildren<MeshRenderer>())
            {
                Assert.GreaterOrEqual(r.bounds.min.x, ground.min.x, $"{r.transform.parent.name} hangs off the west of the ground");
                Assert.LessOrEqual(r.bounds.max.x, ground.max.x, $"{r.transform.parent.name} hangs off the east");
                Assert.GreaterOrEqual(r.bounds.min.z, ground.min.z, $"{r.transform.parent.name} hangs off the south");
                Assert.LessOrEqual(r.bounds.max.z, ground.max.z, $"{r.transform.parent.name} hangs off the north");
            }
        }
    }
}
