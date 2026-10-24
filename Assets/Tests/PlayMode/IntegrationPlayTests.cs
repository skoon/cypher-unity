#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cypher.Actors;
using Cypher.Audio;
using Cypher.City;
using Cypher.Core;
using Cypher.Game;
using Cypher.Perf;
using Cypher.Player;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>
    /// Everything at once, in the real scene with the people, the traffic, the aircraft and the sound all on: a game started and ended more than once, the tier changed under it, a slow machine, a pause,
    /// and a long walk on the street route. Each system is tested on its own elsewhere; these are what only shows when they run together.
    /// </summary>
    public class IntegrationPlayTests
    {
        /// <summary>Costs 40 ms a frame: a machine too slow for the tier.</summary>
        sealed class FrameBurner : MonoBehaviour
        {
            void Update() => Thread.Sleep(40);
        }

        [SetUp]
        public void SetUp() => SliceTestScene.Isolate(audio: true);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static CrowdController Crowd => UnityEngine.Object.FindFirstObjectByType<CrowdController>();
        static TrafficController Traffic => UnityEngine.Object.FindFirstObjectByType<TrafficController>();
        static AudioEngine Sound => UnityEngine.Object.FindFirstObjectByType<AudioEngine>();
        static QualityDirector Director => UnityEngine.Object.FindFirstObjectByType<QualityDirector>();
        static FirstPersonController Player => UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();

        static IEnumerator Seconds(float s)
        {
            float until = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        static IEnumerator ToTitle()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            Assume.That(Sound != null && Sound.Active, "the audio engine did not start");
            float until = Time.realtimeSinceStartup + 40f;
            while (Sound.Clips.Pending > 0 && Time.realtimeSinceStartup < until) yield return null;
        }

        /// <summary>The pilot walking the street route, as the benchmark does.</summary>
        static BenchmarkPilot WalkTheStreets()
        {
            CityBuilder city = UnityEngine.Object.FindFirstObjectByType<CityBuilder>();
            var pilot = new BenchmarkPilot(StreetRoutes.PlazaRing(CityNav.Build(city.Slice.Plan)), Player.Sensitivity);
            Player.CommandSource = new PilotCommandSource(Player, pilot);
            return pilot;
        }

        static void SetPreset(QualityPreset preset, bool adaptive = false)
        {
            GraphicsOptions g = Director.Options;
            g.Quality = preset;
            g.AdaptiveQuality = adaptive;
            Director.Apply(g);
        }

        /// <summary>How many of each kind of thing exist, to compare one game's leavings with another's.</summary>
        static Dictionary<string, long> Census()
        {
            GC.Collect();
            return new Dictionary<string, long>
            {
                ["GameObjects"] = Resources.FindObjectsOfTypeAll<GameObject>().Length,
                ["AudioSources"] = Resources.FindObjectsOfTypeAll<AudioSource>().Length,
                ["AudioClips"] = Resources.FindObjectsOfTypeAll<AudioClip>().Length,
                ["Meshes"] = Resources.FindObjectsOfTypeAll<Mesh>().Length,
                ["Materials"] = Resources.FindObjectsOfTypeAll<Material>().Length,
                ["Textures"] = Resources.FindObjectsOfTypeAll<Texture>().Length,
                ["ManagedKB"] = GC.GetTotalMemory(true) / 1024,
            };
        }

        static string Describe(Dictionary<string, long> census) => string.Join(", ", census.Select(kv => kv.Key + " " + kv.Value));

        // ---------------------------------------------------------------- a session, again and again

        [UnityTest]
        public IEnumerator AGameStartedAndEndedThreeTimesLeavesNothingBehind()
        {
            yield return ToTitle();
            Assert.IsFalse(Crowd.IsActive);
            Assert.IsFalse(Traffic.IsActive);
            Assert.IsFalse(Sound.WorldRunning);

            var afterEach = new List<Dictionary<string, long>>();
            for (int game = 1; game <= 3; game++)
            {
                Assert.IsTrue(SliceTestScene.Flow.NewGame(), "game " + game);
                yield return Seconds(1.0f);
                WalkTheStreets();
                yield return Seconds(7.0f);
                Assert.IsTrue(Crowd.IsActive, "game " + game + ": people");
                Assert.IsTrue(Traffic.IsActive, "game " + game + ": traffic");
                Assert.IsTrue(Sound.WorldRunning, "game " + game + ": sound");
                Assert.Greater(Crowd.Sim.Active.Count, 0);
                Assert.Greater(Traffic.Sim.Vehicles.Count, 0);

                Assert.IsTrue(SliceTestScene.Flow.Pause(), "game " + game);
                yield return Seconds(0.3f);
                Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle(), "game " + game);
                yield return Seconds(3.0f);

                Assert.IsFalse(Crowd.IsActive, "game " + game + ": the people are gone at the title");
                Assert.IsFalse(Traffic.IsActive, "game " + game + ": the traffic is gone");
                Assert.IsFalse(Sound.WorldRunning, "game " + game + ": the world's sound is gone");
                Assert.IsNull(Sound.Master.Failure);
                yield return Seconds(1.5f);   // the last menu sound and any tail
                Assert.AreEqual(0, Sound.Voices.Active, "game " + game + ": every voice was given back");
                afterEach.Add(Census());
                Debug.Log($"[Cypher] integration: after game {game}: {Describe(afterEach[game - 1])}");
            }

            // the first game may make things that stay for good (the pools, the caches): the second and third must not add to them
            foreach (string key in afterEach[1].Keys.Where(k => k != "ManagedKB"))
            {
                Assert.AreEqual(afterEach[1][key], afterEach[2][key], $"{key} after the third game against the second ({Describe(afterEach[1])} / {Describe(afterEach[2])})");
            }
            Assert.Less(afterEach[2]["ManagedKB"] - afterEach[1]["ManagedKB"], 8 * 1024, "the managed heap grew by more than 8 MB over a game");
        }

        // ---------------------------------------------------------------- the tier changes under it all

        [UnityTest]
        public IEnumerator AllFiveTiersInTurnWhilePlayingResizeThePeopleTheVehiclesTheAircraftAndTheLights()
        {
            yield return ToTitle();
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Seconds(1.0f);
            // nobody in view, so people and vehicles can come and go at once instead of waiting to be out of sight
            Crowd.Sim.IsVisible = _ => false;
            Traffic.Sim.InView = (x, y, z, r) => false;

            foreach (QualityPreset preset in new[] { QualityPreset.Performance, QualityPreset.Low, QualityPreset.Medium, QualityPreset.High, QualityPreset.Ultra, QualityPreset.Medium })
            {
                QualityTier tier = QualityTiers.For(preset);
                SetPreset(preset);
                yield return Seconds(0.8f);
                Assert.AreEqual(preset, Director.Preset);
                Assert.AreEqual(tier.Pedestrians, Crowd.Sim.Active.Count, preset + ": people");
                Assert.AreEqual(tier.GroundVehicles, Traffic.Sim.Vehicles.Count, preset + ": vehicles");
                Assert.AreEqual(tier.AircraftCount, Traffic.Sim.Airborne.Count, preset + ": aircraft");
                Assert.LessOrEqual(Traffic.Lights.Lit, tier.VehicleLights, preset + ": lights");
                Assert.IsTrue(Sound.WorldRunning, preset + ": the sound goes on");
                Assert.IsNull(Sound.Bed.Failure);
                Assert.IsNull(Sound.Actors.Failure);
            }
        }

        // ---------------------------------------------------------------- a machine too slow for the tier

        [UnityTest]
        public IEnumerator ASlowMachineMakesTheGovernorStepTheTierDownAndHoldsThereOnceTheLoadIsGone()
        {
            yield return ToTitle();
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Seconds(1.0f);
            SetPreset(QualityPreset.Ultra, adaptive: true);
            var changes = new List<QualityChange>();
            Director.Changed += changes.Add;
            float grow = Time.realtimeSinceStartup + 8f;
            while (Crowd.Sim.Active.Count < 400 && Time.realtimeSinceStartup < grow) yield return null;
            Assert.AreEqual(400, Crowd.Sim.Active.Count, "the crowd grew to Ultra's before the slow machine began");

            var burner = new GameObject("slow machine").AddComponent<FrameBurner>();
            float until = Time.realtimeSinceStartup + 40f;
            while (changes.Count == 0 && Time.realtimeSinceStartup < until) yield return null;
            UnityEngine.Object.Destroy(burner.gameObject);

            Assert.GreaterOrEqual(changes.Count, 1, "the governor stepped down within 40 s of 25 FPS");
            QualityChange first = changes[0];
            Assert.AreEqual(QualityChangeReason.Adaptive, first.Reason);
            Assert.AreEqual(QualityPreset.Ultra, first.From);
            Assert.Less((int)first.To, (int)QualityPreset.Ultra);
            StringAssert.Contains(first.To.ToString(), first.Notice() ?? "", "and the player is told");

            // without the load it settles where it is: it never climbs back by itself
            QualityPreset settled = Director.Preset;
            yield return Seconds(14f);
            Assert.AreEqual(settled, Director.Preset);
            Assert.AreEqual(QualityTiers.For(settled).Pedestrians, Crowd.Sim.Active.Count, "and the people follow it");
        }

        // ---------------------------------------------------------------- pause and resume

        [UnityTest]
        public IEnumerator PausingFreezesEveryoneAndTheSoundDucksAndResumingCarriesOnFromTheSamePlace()
        {
            yield return ToTitle();
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Seconds(1.0f);
            WalkTheStreets();
            yield return Seconds(5.0f);

            List<Vector3> Snapshot() => Crowd.Sim.Active.Take(20).Select(p => new Vector3((float)p.X, 0, (float)p.Z))
                .Concat(Traffic.Sim.Vehicles.Take(20).Select(v => new Vector3((float)v.X, 0, (float)v.Z)))
                .Append(Player.transform.position).ToList();

            Assert.IsTrue(SliceTestScene.Flow.Pause());
            yield return Seconds(0.5f);
            List<Vector3> frozen = Snapshot();
            yield return Seconds(1.5f);
            List<Vector3> later = Snapshot();
            for (int i = 0; i < frozen.Count; i++) Assert.AreEqual(frozen[i], later[i], $"entity {i} moved while paused");
            Assert.AreEqual(BusLevels.DuckPaused, Sound.Levels.Duck(AudioBus.Ambience), 0.03, "the sound is ducked");
            Assert.IsTrue(Sound.WorldRunning, "but the world's sound is still there");

            Assert.IsTrue(SliceTestScene.Flow.Resume());
            yield return Seconds(1.0f);
            List<Vector3> resumed = Snapshot();
            int moved = 0;
            for (int i = 0; i < frozen.Count; i++)
            {
                float d = Vector3.Distance(frozen[i], resumed[i]);
                if (d > 0.05f) moved++;
                Assert.Less(d, 40f, $"entity {i} jumped on resuming");
            }
            Assert.Greater(moved, frozen.Count / 3, "and the world moves again");
            Assert.AreEqual(1.0, Sound.Levels.Duck(AudioBus.Ambience), 0.05, "the sound comes back up");
        }

        // ---------------------------------------------------------------- the whole of it, for a while

        [UnityTest]
        public IEnumerator NinetySecondsOnTheStreetRouteAtUltraWithEverythingOnRunsCleanAndGetsAround()
        {
            yield return ToTitle();
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Seconds(1.0f);
            SetPreset(QualityPreset.Ultra);
            BenchmarkPilot pilot = WalkTheStreets();
            QualityTier tier = QualityTiers.For(QualityPreset.Ultra);

            float start = Time.realtimeSinceStartup;
            Vector3 last = Player.transform.position;
            double walked = 0;
            int checks = 0;
            while (Time.realtimeSinceStartup - start < 90f)
            {
                Vector3 p = Player.transform.position;
                walked += Vector3.Distance(p, last);
                last = p;
                if (checks++ % 30 == 0)
                {
                    Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z), "the player's position");
                    Assert.LessOrEqual(Crowd.Sim.Active.Count, tier.Pedestrians);
                    Assert.LessOrEqual(Traffic.Sim.Vehicles.Count, tier.GroundVehicles);
                    Assert.LessOrEqual(Traffic.Sim.Airborne.Count, tier.AircraftCount);
                    foreach (Pedestrian person in Crowd.Sim.Active.Take(50)) Assert.IsFalse(double.IsNaN(person.X) || double.IsNaN(person.Z), "a person's position");
                    foreach (Vehicle v in Traffic.Sim.Vehicles.Take(50)) Assert.IsFalse(double.IsNaN(v.X) || double.IsNaN(v.Z), "a vehicle's position");
                    Assert.LessOrEqual(Sound.Voices.Active, OneShotVoices.SpatialVoices + OneShotVoices.FlatVoices);
                    Assert.IsNull(Sound.Master.Failure);
                    Assert.IsNull(Sound.Bed.Failure);
                    Assert.IsNull(Sound.Score.Failure);
                    Assert.IsNull(Sound.Actors.Failure);
                    Assert.IsNull(Sound.Clips.Failure);
                }
                yield return null;
            }

            Debug.Log($"[Cypher] integration: 90 s on the street route at Ultra: {walked:0} m walked, {pilot.WaypointsReached} waypoints, held up {pilot.StalledSeconds:0.0} s in {pilot.Detours} detours; " +
                      $"{Crowd.Sim.Active.Count} people, {Traffic.Sim.Vehicles.Count} vehicles, {Traffic.Sim.Airborne.Count} aircraft, {Sound.Status()}");
            Assert.Less(pilot.StalledSeconds, 0.15 * 90, "held up for no more than the benchmark allows");
            Assert.GreaterOrEqual(pilot.WaypointsReached, 12, "and well round the route");
            Assert.Greater(walked, 150, "on its own two feet");
        }
    }
}
#endif
