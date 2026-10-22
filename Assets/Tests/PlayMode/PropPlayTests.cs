#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Cypher.City;
using Cypher.Core;
using Cypher.Game;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The street props in the running game: real colliders, the scene's own dressing, and the quality tiers driving it.</summary>
    public class PropPlayTests
    {
        PlayerTestRig _rig;

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            SliceTestScene.Restore();
        }

        [Test]
        public void EveryColliderBoxIsReallyThereInThePhysicsWorld()
        {
            _rig = new PlayerTestRig(windowRadius: 1, withProps: true);
            Physics.SyncTransforms();
            var window = CityWindow.AroundPlaza(1);
            var boxes = _rig.City.Props.Cells.Where(kv => window.Contains(kv.Key.Gx, kv.Key.Gz)).SelectMany(kv => kv.Value.Boxes).ToList();
            Assert.Greater(boxes.Count, 50);
            int missing = 0;
            foreach (PropBox b in boxes)
            {
                var centre = new Vector3((float)b.Cx, (float)b.Cy, (float)b.Cz);
                var half = new Vector3((float)b.Hx * 0.9f, (float)b.Hy * 0.9f, (float)b.Hz * 0.9f);
                if (!Physics.CheckBox(centre, half, Quaternion.Euler(0f, (float)(b.Yaw * 180.0 / Math.PI), 0f))) missing++;
            }
            Assert.AreEqual(0, missing, "every prop box has a collider where it should be");
        }

        [Test]
        public void ABoxJustOutsideAPropIsClearSoTheColliderIsNoBiggerThanTheProp()
        {
            _rig = new PlayerTestRig(windowRadius: 1, withProps: true);
            Physics.SyncTransforms();
            // a barrier is 1.72 m long and 0.54 deep; look for it with a probe a little way in front of it where nothing else stands
            PropInstance barrier = _rig.City.Props.Layout.Instances.First(i => i.Kit == "barrier" && i.Y < 0.5 && Math.Abs(i.X) < 90 && Math.Abs(i.Z) < 90);
            double forwardX = Math.Sin(barrier.Ry), forwardZ = Math.Cos(barrier.Ry);
            var probe = new Vector3((float)(barrier.X + forwardX * 1.2), 0.5f, (float)(barrier.Z + forwardZ * 1.2));
            Assert.IsFalse(Physics.CheckSphere(probe, 0.2f), "open air in front of the barrier");
        }

        [Test]
        public void ABarrierStopsAPlayerWalkingIntoIt()
        {
            _rig = new PlayerTestRig(windowRadius: 1, withProps: true);
            var window = CityWindow.AroundPlaza(1);
            Rect2 e = window.RoadExtent;
            var barriers = _rig.City.Props.Layout.Instances
                .Where(i => i.Kit == "barrier" && i.Y < 0.5 && i.X > e.X0 + 20 && i.X < e.X1 - 20 && i.Z > e.Z0 + 20 && i.Z < e.Z1 - 20)
                .Take(6).ToList();
            Assert.IsNotEmpty(barriers);

            int stopped = 0;
            foreach (PropInstance barrier in barriers)
            {
                double fx = Math.Sin(barrier.Ry), fz = Math.Cos(barrier.Ry);
                var start = new Vector3((float)(barrier.X + fx * 3.0), 1f, (float)(barrier.Z + fz * 3.0));
                _rig.Place(start, (float)((barrier.Ry + Math.PI) * 180.0 / Math.PI));
                _rig.Run(0.3f);
                _rig.Input.Forward = 1;
                _rig.Run(4f);
                _rig.Input.Clear();

                Vector3 p = _rig.Player.transform.position;
                // distance along the barrier's own facing, from its centre: it must not have got through it
                double along = (p.x - barrier.X) * fx + (p.z - barrier.Z) * fz;
                Assert.GreaterOrEqual(along, 0.5, $"walked through the barrier at ({barrier.X:0}, {barrier.Z:0}); ended {along:0.00} m in front of its centre");
                if (along < 1.6) stopped++;
            }
            Assert.Greater(stopped, 0, "at least one barrier was walked up to and stopped against");
        }

        [UnityTest]
        public IEnumerator TheSceneIsDressedAndTheTierSetsHowFarThePropsReach()
        {
            SliceTestScene.Isolate();
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);

            var city = UnityEngine.Object.FindFirstObjectByType<CityBuilder>();
            Assert.IsNotNull(city.Dressing, "the scene's city has its props");
            Assert.AreEqual(49, city.Dressing.BlockCount);
            Assert.Greater(city.PropColliderCount, 1000);
            Assert.IsNotNull(city.Root.Find(CityBuilder.SteamName));
            Assert.AreEqual(QualityTiers.For(QualityPreset.Medium).PropDistance, city.Dressing.DrawDistance, "Medium is the default tier");

            var director = UnityEngine.Object.FindFirstObjectByType<QualityDirector>();
            GraphicsOptions options = director.Options;
            options.Quality = QualityPreset.Performance;
            director.Apply(options);
            Assert.AreEqual(70f, city.Dressing.DrawDistance);
            Assert.IsFalse(city.Dressing.SteamVisible);

            options.Quality = QualityPreset.Ultra;
            director.Apply(options);
            Assert.AreEqual(210f, city.Dressing.DrawDistance);
            Assert.IsTrue(city.Dressing.SteamVisible);
        }

        [UnityTest]
        public IEnumerator FarPropsAreSwitchedOffAroundTheTitleCameraAndTheCityStillShowsNearOnes()
        {
            SliceTestScene.Isolate();
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            for (int i = 0; i < 20; i++) yield return null;   // a few refreshes

            var city = UnityEngine.Object.FindFirstObjectByType<CityBuilder>();
            Assert.Greater(city.Dressing.VisibleBlocks, 3, "the blocks around the camera show their props");
            Assert.Less(city.Dressing.VisibleBlocks, 40, "the ones far from it do not");
        }

        [UnityTest]
        public IEnumerator PlayingOnForSecondsWithPropsKeepsThePlayerOnTheGround()
        {
            SliceTestScene.Isolate();
            yield return SliceTestScene.LoadAndPlay();
            var player = UnityEngine.Object.FindFirstObjectByType<Cypher.Player.FirstPersonController>();
            float end = Time.time + 3f;
            while (Time.time < end)
            {
                Assert.Greater(player.transform.position.y, -0.1f, "fell through the world");
                yield return null;
            }
            Assert.IsTrue(player.Motor.Grounded);
            Assert.AreEqual(0f, player.transform.position.x, 0.5f, "no prop pushed the player off the spawn point");
            Assert.AreEqual(0f, player.transform.position.z, 0.5f);
        }
    }
}
#endif
