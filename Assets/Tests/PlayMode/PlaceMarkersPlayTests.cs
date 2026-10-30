#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Cypher.Core;
using Cypher.Player;
using Cypher.Rpg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The neon markers at the places in the real Slice scene: one per place while a game is on, none at the title; they face the camera, take the jobs' colours and fade as the player walks in.</summary>
    public class PlaceMarkersPlayTests
    {
        [SetUp]
        public void SetUp() => SliceTestScene.Isolate(saves: new MemorySaveStorage());

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static RpgService Rpg => Object.FindAnyObjectByType<RpgService>();

        static PlaceMarkers Markers => Object.FindAnyObjectByType<PlaceMarkers>();

        static FirstPersonController Player => Object.FindAnyObjectByType<FirstPersonController>();

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator EachPlaceHasAMarkerWhileAGameIsOn()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            Assert.IsEmpty(Markers.Keys, "none at the title");
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Frames(3);

            CollectionAssert.AreEqual(Rpg.Core.GetLocationList().Select(l => l.Key), Markers.Keys);
            Vector3 eye = Camera.main != null ? Camera.main.transform.position : Player.transform.position;
            foreach (RpgLocation l in Rpg.Core.GetLocationList())
            {
                var m = Markers.Describe(l.Key);
                Assert.AreEqual((float)l.X, m.Position.x, 1e-3f);
                Assert.AreEqual((float)l.Z, m.Position.z, 1e-3f);
                Vector3 to = new Vector3(eye.x - m.Position.x, 0, eye.z - m.Position.z);
                if (to.magnitude < PlaceMarkers.NearHidden)
                {
                    Assert.IsFalse(m.Drawn, $"{l.Key}: the one the player stands in is not drawn");
                    continue;
                }
                to.Normalize();
                Assert.Greater(Vector3.Dot(m.Facing, to), 0.99f, $"{l.Key} faces the camera");
            }

            Assert.IsTrue(SliceTestScene.Flow.Pause());
            Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle());
            yield return Frames(3);
            Assert.IsEmpty(Markers.Keys, "gone again at the title");
        }

        [UnityTest]
        public IEnumerator TheTrackedJobsPlaceIsAmberAndAMarkerFadesAsThePlayerWalksIn()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(3);
            JsonObject tracked = HudModel.TrackedMarker(Rpg.Core.Quests.Markers());
            Assert.IsNotNull(tracked, "a new game's main job points somewhere");
            string key = (string)tracked["key"];
            Assert.AreEqual(PlaceMarkerArt.Tint.Tracked, Markers.Describe(key).Tint);
            Vector3 at = Player.transform.position;
            string other = Rpg.Core.GetLocationList()
                .Where(l => PlaceMarkerArt.TintFor(l.Key, Rpg.Core.Quests.Markers()) == PlaceMarkerArt.Tint.Place)
                .OrderByDescending(l => RpgLocations.DistanceSq(l, at.x, at.z)).First().Key;
            Assert.AreEqual(PlaceMarkerArt.Tint.Place, Markers.Describe(other).Tint);

            RpgLocation far = Rpg.Core.GetLocation(other);
            Assert.IsTrue(Markers.Describe(other).Drawn);
            CityNav nav = CityNav.Build(Object.FindAnyObjectByType<Cypher.City.CityBuilder>().Slice.Plan);
            Player.Teleport(new Vector3((float)far.X, (float)nav.WalkY + 0.05f, (float)far.Z), 0f);
            Physics.SyncTransforms();
            yield return Frames(3);
            Assert.IsFalse(Markers.Describe(other).Drawn, "standing in it, it is not drawn");
        }
    }
}
#endif
