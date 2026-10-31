#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cypher.Actors;
using Cypher.Audio;
using Cypher.City;
using Cypher.Combat;
using Cypher.Core;
using Cypher.Fight;
using Cypher.Game;
using Cypher.Perf;
using Cypher.Player;
using Cypher.Rendering;
using Cypher.Rpg;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
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
        public void SetUp() => SliceTestScene.Isolate(audio: true, saves: new MemorySaveStorage());

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
        static RpgService Rpg => UnityEngine.Object.FindFirstObjectByType<RpgService>();
        static UiRoot Ui => UnityEngine.Object.FindFirstObjectByType<UiRoot>();
        static WeaponSystem Guns => UnityEngine.Object.FindFirstObjectByType<WeaponSystem>();
        static EnemyController Fight => UnityEngine.Object.FindFirstObjectByType<EnemyController>();

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
                ["UiElements"] = UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.VisualElement>(Ui.Root).ToList().Count,
                ["ManagedKB"] = GC.GetTotalMemory(true) / 1024,
            };
        }

        static string Describe(Dictionary<string, long> census) => string.Join(", ", census.Select(kv => kv.Key + " " + kv.Value));

        // ---------------------------------------------------------------- a session, again and again

        [UnityTest]
        public IEnumerator AGameStartedAndEndedThreeTimesWithTheJobsTheMenusAndTheGunLeavesNothingBehind()
        {
            yield return ToTitle();
            Assert.IsFalse(Crowd.IsActive);
            Assert.IsFalse(Traffic.IsActive);
            Assert.IsFalse(Sound.WorldRunning);

            var afterEach = new List<Dictionary<string, long>>();
            for (int game = 1; game <= 3; game++)
            {
                // the first game is new; the others Continue the save the one before made
                Assert.IsTrue(game == 1 ? SliceTestScene.Flow.NewGame() : SliceTestScene.Flow.Continue(), "game " + game);
                yield return Seconds(1.0f);
                Assert.AreEqual(0, Guns.Effects.Live().Decals, "game " + game + ": no holes left from the game before");
                Assert.AreEqual(0, Guns.Effects.Live().Tracers, "game " + game + ": no tracers left from the game before's fight");
                Assert.AreEqual(0, Guns.Effects.Live().Lights, "game " + game + ": no muzzle flashes left from it");
                IPlayerCommandSource own = Player.CommandSource;
                WalkTheStreets();
                yield return Seconds(7.0f);
                Assert.IsTrue(Crowd.IsActive, "game " + game + ": people");
                Assert.IsTrue(Traffic.IsActive, "game " + game + ": traffic");
                Assert.IsTrue(Sound.WorldRunning, "game " + game + ": sound");
                Assert.Greater(Crowd.Sim.Active.Count, 0);
                Assert.Greater(Traffic.Sim.Vehicles.Count, 0);
                yield return PlayALittle(game, own);
                yield return FightALittle(game);

                Assert.IsTrue(SliceTestScene.Flow.Pause(), "game " + game);
                Assert.IsNull(SliceTestScene.Flow.SaveGame(), "game " + game + " saves");
                yield return Seconds(0.3f);
                Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle(), "game " + game);
                yield return Seconds(3.0f);

                Assert.IsFalse(Crowd.IsActive, "game " + game + ": the people are gone at the title");
                Assert.IsFalse(Traffic.IsActive, "game " + game + ": the traffic is gone");
                Assert.IsFalse(Sound.WorldRunning, "game " + game + ": the world's sound is gone");
                Assert.IsNull(Rpg.Core, "game " + game + ": no RPG behind the title");
                Assert.IsFalse(Guns.Drawn, "game " + game + ": the gun is put away");
                Assert.IsFalse(Fight.IsActive, "game " + game + ": no fights behind the title");
                Assert.AreEqual(0, Fight.Proxies.Leased, "game " + game + ": every enemy volume parked");
                Assert.IsNull(Crowd.Fighters, "game " + game + ": no fighters drawn");
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

        /// <summary>What a player does in a few seconds of a game: talk to Kaz and leave, open the menu on each tab and close it, fire a burst.</summary>
        static IEnumerator PlayALittle(int game, IPlayerCommandSource own)
        {
            Player.CommandSource = own;   // the pilot stops walking
            RpgLocation noodle = Rpg.Core.GetLocation("noodle");
            Player.Teleport(new Vector3((float)noodle.X, Player.transform.position.y, (float)noodle.Z), 0f);
            Physics.SyncTransforms();
            yield return Seconds(0.2f);
            Assert.IsTrue(Rpg.Core.Interact().Ok, "game " + game + ": Kaz talks");
            yield return null;
            Assert.AreEqual(UiScreen.Dialogue, Ui.Top);
            Ui.OnBack(fromBButton: false);
            yield return null;
            foreach (MenuTab tab in new[] { MenuTab.Inventory, MenuTab.Character, MenuTab.Journal })
            {
                Ui.ToggleMenu(tab);
                yield return Seconds(0.1f);
                Assert.AreEqual(UiScreen.GameMenu, Ui.Top, $"game {game}: the {tab} tab");
            }
            Ui.ToggleMenu(MenuTab.Journal);
            yield return null;
            Assert.AreEqual(UiScreen.None, Ui.Top, "game " + game + ": the menu closed");
            Player.Look.Set(Player.Look.Yaw, -0.5);
            for (int i = 0; i < 30; i++)
            {
                Guns.Step(new WeaponInput { Acting = true, FireHeld = true }, new WeaponBody { Grounded = true });
                yield return null;
            }
            Guns.Step(new WeaponInput { Acting = true }, new WeaponBody { Grounded = true });
            Assert.Greater(Guns.Effects.Live().Decals, 0, "game " + game + ": the shots left holes");
            yield return Seconds(1.0f);
        }

        /// <summary>
        /// A fight in each game: the Jackals at the garage (the gig taken in the first game and carried on by the save, so each game has one fewer), shots both ways,
        /// a Ping, and one of them down.
        /// </summary>
        static IEnumerator FightALittle(int game)
        {
            const string gig = "q_gig_jackals", encounter = "enc_garage_jackals";
            if (!Rpg.Core.Quests.IsActive(gig))
            {
                Assert.AreEqual(1, game, "the gig carries on in the save");
                Assert.IsTrue(Rpg.Core.Quests.Start(gig, force: true).Ok);
                for (int guard = 0; guard < 10 && Rpg.Core.Quests.GetState(gig).Stage != "s_clear"; guard++)
                {
                    JsonObject open = ((List<object>)Rpg.Core.Quests.Snapshot(gig)["objectives"]).Cast<JsonObject>().First(o => !(bool)o["done"]);
                    Assert.IsTrue(Rpg.Core.Quests.CompleteObjective(gig, (string)open["id"]).Ok);
                }
            }
            Assert.AreEqual("s_clear", Rpg.Core.Quests.GetState(gig).Stage, "game " + game);
            RpgLocation garage = Rpg.Core.GetLocation("garage");
            Player.Teleport(new Vector3((float)garage.X, Player.transform.position.y, (float)garage.Z), 0f);
            Physics.SyncTransforms();
            EnemySquad jackals = null;
            float until = Time.realtimeSinceStartup + 5f;
            while ((jackals = Fight.EncounterDirector.Live(encounter)) == null && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsNotNull(jackals, "game " + game + ": the Jackals are at the garage");
            Assert.AreEqual(6 - game, jackals.All.Count, "game " + game + ": the survivors of the games before");

            int hitsTaken = 0;
            Action<Vector3, double> taken = (from, d) => hitsTaken++;
            Fight.PlayerHit += taken;
            using (var input = new VirtualInput())
            {
                Enemy first = jackals.All[0];
                Vector3 chest = new Vector3((float)first.Position.X, (float)first.Position.Y + EnemyController.ChestHeight, (float)first.Position.Z), eye = Camera.main.transform.position;
                Player.Look.Set(Math.Atan2(chest.x - eye.x, chest.z - eye.z), Math.Atan2(chest.y - eye.y, Vector2.Distance(new Vector2(chest.x, chest.z), new Vector2(eye.x, eye.z))));
                RpgResult given = Rpg.Core.GiveItem("qh_ping");
                Assert.IsTrue(given.Ok, given.Reason);
                Item ping = given.Item ?? Rpg.Core.Inventory.Items.Last(it => it.TemplateId == "qh_ping");
                Rpg.Core.Inventory.UnequipHack(0);
                Assert.IsTrue(Rpg.Core.Inventory.EquipHack(ping.Uid, 0).Ok);
                Rpg.Core.Deck.Fill();
                input.Hold(Key.F);
                yield return null;
                yield return null;
                yield return null;
                if (Fight.HackTarget != null) Assert.IsTrue(Fight.UploadHack().Ok, "game " + game + ": a Ping");
                input.Release(Key.F);
                yield return null;

                // a short burst (a few rounds: not enough to kill, so each game leaves one fewer than the last), then their answer
                for (int i = 0; i < 20; i++)
                {
                    Guns.Step(new WeaponInput { Acting = true, FireHeld = true }, new WeaponBody { Grounded = true });
                    yield return null;
                }
                Guns.Step(new WeaponInput { Acting = true }, new WeaponBody { Grounded = true });
                until = Time.realtimeSinceStartup + 8f;
                while (hitsTaken == 0 && Time.realtimeSinceStartup < until)
                {
                    Rpg.Core.Heal(Rpg.Core.Derived["maxHealth"]);
                    yield return null;
                }
            }
            Fight.PlayerHit -= taken;
            Assert.Greater(hitsTaken, 0, "game " + game + ": they fired back");

            Enemy down = jackals.All.First(e => e.State != EnemyState.Down);
            Assert.IsTrue(Fight.Sim.Harm(down, 1e6));
            Rpg.Kill(down.Archetype, down.Squad.Tag, new Vector3((float)down.Position.X, (float)down.Position.Y, (float)down.Position.Z));
            yield return Seconds(0.5f);
            Rpg.Core.Heal(Rpg.Core.Derived["maxHealth"]);
            Assert.IsFalse(Fight.Downed, "game " + game);
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
            int checks = 0, detours = 0;
            var blockers = new List<string>();
            var near = new Collider[16];
            while (Time.realtimeSinceStartup - start < 90f)
            {
                Vector3 p = Player.transform.position;
                walked += Vector3.Distance(p, last);
                last = p;
                if (pilot.Detours != detours)
                {
                    // held up: what is in front of the walker
                    detours = pilot.Detours;
                    Vector3 ahead = p + Player.transform.forward * 0.6f;
                    int n = Physics.OverlapCapsuleNonAlloc(ahead + Vector3.up * 0.4f, ahead + Vector3.up * 1.5f, 0.45f, near, ~0, QueryTriggerInteraction.Ignore);
                    var what = new List<string>();
                    for (int i = 0; i < n; i++) if (near[i] != Player.Controller) what.Add(near[i].name);
                    blockers.Add($"{Time.realtimeSinceStartup - start:0.0} s at ({p.x:0},{p.z:0}): {(what.Count > 0 ? string.Join(", ", what) : "nothing in front")}");
                }
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
                      $"{Crowd.Sim.Active.Count} people, {Traffic.Sim.Vehicles.Count} vehicles, {Traffic.Sim.Airborne.Count} aircraft, {Sound.Status()}" +
                      (blockers.Count > 0 ? "; held up by:\n  " + string.Join("\n  ", blockers) : ""));
            Assert.Less(pilot.StalledSeconds, 0.15 * 90, "held up for no more than the benchmark allows");
            Assert.GreaterOrEqual(pilot.WaypointsReached, 12, "and well round the route");
            Assert.Greater(walked, 150, "on its own two feet");
        }
    }
}
#endif
