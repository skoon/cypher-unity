#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Cypher.City;
using Cypher.Combat;
using Cypher.Core;
using Cypher.Player;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>
    /// The guns in the real Slice scene: holstered on the title, drawn in a game; the mouse fires and the shots are traced through the city (a wall takes a
    /// decal, a person proxy is marked as a hit on a person); the view kicks and settles; 1-3 switch; a game screen holds the trigger; the gun draws without
    /// shader errors.
    /// </summary>
    public class GunPlayTests
    {
        [SetUp]
        public void SetUp() => SliceTestScene.Isolate();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static WeaponSystem Guns => Object.FindAnyObjectByType<WeaponSystem>();

        static FirstPersonController Player => Object.FindAnyObjectByType<FirstPersonController>();

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator Ready()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return new WaitForSeconds(0.6f);   // past the draw
            Assert.AreEqual(WeaponState.Idle, Guns.Rig.State);
        }

        [UnityTest]
        public IEnumerator TheGunIsHolsteredOnTheTitleAndDrawnInAGame()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            yield return null;
            Assert.IsFalse(Guns.Drawn);
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Frames(3);
            Assert.IsTrue(Guns.Drawn);
            Assert.AreEqual("wasp", Guns.Rig.Current.Config.Id);
        }

        [UnityTest]
        public IEnumerator HoldingTheMouseFiresAndTheShotsHitTheCity()
        {
            using (var input = new VirtualInput())
            {
                yield return Ready();
                // look down at the street in front: every shot lands
                Player.Look.Set(Player.Look.Yaw, -0.6);
                yield return null;
                Guns.TakeShots();
                input.PressMouse(MouseButton.Left, true);
                yield return new WaitForSeconds(0.5f);
                input.PressMouse(MouseButton.Left, false);
                yield return Frames(2);
                var shots = Guns.TakeShots();
                Assert.GreaterOrEqual(shots.Count, 4, "the Wasp fires about twelve a second");
                Assert.IsTrue(shots.All(s => s.Hit), "looking down, every shot meets the ground");
                Assert.IsTrue(shots.All(s => s.Target == ShotTarget.World));
                Assert.Less(Guns.Rig.Current.Ammo, 32);
                Assert.Greater(Guns.Effects.Live().Decals, 0, "the street takes holes");
                Assert.Greater(Player.Kick.Pitch + 0.0001, 0, "the view kicked up");
                yield return new WaitForSeconds(1.5f);
                Assert.AreEqual(0, Player.Kick.Pitch, 0.002, "and settled back to the aim");
            }
        }

        [UnityTest]
        public IEnumerator AShotThatHitsAPersonIsMarkedAsAHitOnAPerson()
        {
            yield return Ready();
            // a pedestrian proxy stood in front of the camera
            Transform cam = Camera.main != null ? Camera.main.transform : Player.CameraPivot;
            var dummy = new GameObject("Shot dummy");
            dummy.transform.position = cam.position + cam.forward * 6;
            var capsule = dummy.AddComponent<CapsuleCollider>();
            capsule.radius = 0.4f;
            capsule.height = 2;
            dummy.AddComponent<ShotSurface>().Target = ShotTarget.Person;
            Physics.SyncTransforms();
            Guns.TakeShots();
            Guns.Step(new WeaponInput { Acting = true, FireHeld = true }, new WeaponBody { Grounded = true });
            var shot = Guns.TakeShots().Single();
            Assert.IsTrue(shot.Hit);
            Assert.AreEqual(ShotTarget.Person, shot.Target);
            Assert.AreEqual(ImpactSurface.Flesh, shot.Surface);
            Assert.AreEqual(1, Guns.Rig.HitCritical, "the hit marker shows a critical");
            Object.Destroy(dummy);
        }

        [UnityTest]
        public IEnumerator TheNumberKeysSwitchGunsAndAGameScreenHoldsTheTrigger()
        {
            using (var input = new VirtualInput())
            {
                yield return Ready();
                input.Hold(Key.Digit3);
                yield return Frames(2);
                input.Release(Key.Digit3);
                yield return new WaitForSeconds(1.0f);
                Assert.AreEqual("voltline", Guns.Rig.Current.Config.Id);
                Assert.AreEqual(WeaponState.Idle, Guns.Rig.State);

                Assert.IsTrue(SliceTestScene.Flow.SetModal(FlowModal.Interaction));
                Guns.TakeShots();
                input.PressMouse(MouseButton.Left, true);
                yield return new WaitForSeconds(1.2f);
                input.PressMouse(MouseButton.Left, false);
                yield return Frames(2);
                Assert.IsEmpty(Guns.TakeShots(), "no shots while a conversation holds the player");
                Assert.AreEqual(0, Guns.Rig.Charge);
                SliceTestScene.Flow.SetModal(FlowModal.None);
            }
        }

        [UnityTest]
        public IEnumerator AimingNarrowsTheViewAndLettingGoGivesItBack()
        {
            using (var input = new VirtualInput())
            {
                yield return Ready();
                Camera cam = Camera.main;
                Assert.IsNotNull(cam);
                float hip = cam.fieldOfView;
                input.PressMouse(MouseButton.Right, true);
                yield return new WaitForSeconds(0.6f);
                Assert.Less(cam.fieldOfView, hip - 5);
                input.PressMouse(MouseButton.Right, false);
                yield return new WaitForSeconds(0.8f);
                Assert.AreEqual(hip, cam.fieldOfView, 0.01f);
            }
        }

        [UnityTest]
        public IEnumerator TheHudShowsTheGunItsAmmoAndTheCrosshairOpensWithFire()
        {
            yield return Ready();
            GunHud hud = Object.FindAnyObjectByType<Cypher.UI.UiRoot>().Hud.Gun;
            Assert.IsNotNull(hud, "the scene's HUD has the gun's panel");
            yield return null;
            Assert.AreEqual("WASP-9", hud.NameText);
            Assert.AreEqual("32", hud.AmmoText);
            double rest = hud.Reticle.Gap;
            for (int i = 0; i < 15; i++) Guns.Step(new WeaponInput { Acting = true, FireHeld = true }, new WeaponBody { Grounded = true });
            yield return null;
            Assert.AreEqual(Guns.Rig.Current.Ammo.ToString("00"), hud.AmmoText);
            Assert.Less(Guns.Rig.Current.Ammo, 32);
            Assert.Greater(hud.Reticle.Gap, rest + 3, "the crosshair blooms open");
        }

        [UnityTest]
        public IEnumerator AShotIsHeardAndSoIsWhereItLands()
        {
            SliceTestScene.Isolate(audio: true);
            yield return Ready();
            var audio = Object.FindAnyObjectByType<Cypher.Audio.AudioEngine>();
            Assert.IsNotNull(audio.Guns, "the engine plays the guns");
            Player.Look.Set(Player.Look.Yaw, -0.6);
            yield return null;
            // the clips are made while the city loads; fire until the shot's own clip is ready
            float until = Time.realtimeSinceStartup + 30;
            while (!audio.Guns.Played.Contains("weapon.smg") && Time.realtimeSinceStartup < until)
            {
                Guns.Step(new WeaponInput { Acting = true, FireHeld = true }, new WeaponBody { Grounded = true });
                yield return new WaitForSecondsRealtime(0.1f);
            }
            CollectionAssert.Contains(audio.Guns.Played, "weapon.smg", "the Wasp sounds like an SMG");
            CollectionAssert.Contains(audio.Guns.Played, "impact.concrete", "and the street answers");
            CollectionAssert.Contains(audio.Guns.Played, "weapon.shell");
            Assert.AreEqual("weapon.revolver", Cypher.Audio.GunAudio.ShotSound(WeaponTable.Get("magnat")));
            Assert.AreEqual("weapon.energy", Cypher.Audio.GunAudio.ShotSound(WeaponTable.Get("voltline")));
        }

        [UnityTest]
        public IEnumerator TheBenchmarksTriggerFiresInBurstsAndNeverRunsDry()
        {
            yield return Ready();
            Player.Look.Set(Player.Look.Yaw, -0.6);
            Guns.ScriptedFire = true;
            Guns.TakeShots();
            yield return new WaitForSeconds(2.0f);
            int burst = Guns.TakeShots().Count;
            Assert.Greater(burst, 15, "the trigger is held in the first seconds of play");
            Assert.AreEqual(WeaponTable.Get("wasp").Reserve, Guns.Rig.Current.Reserve, "the reserve is kept full");
            yield return new WaitForSeconds(2.5f);   // past the burst, and any reload it ended in
            Guns.TakeShots();
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(0, Guns.TakeShots().Count, "and let go for the rest of the ten seconds");
        }
}
}
#endif
