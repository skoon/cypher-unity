using Cypher.Core;
using Cypher.Player;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    public class FirstPersonControllerTests
    {
        const float Kerb = (float)CityLayout.SidewalkHeight;

        PlayerTestRig _rig;

        [SetUp]
        public void SetUp() => _rig = new PlayerTestRig();

        [TearDown]
        public void TearDown() => _rig.Dispose();

        // The plaza deck is a 51 m square centred on the origin, 0.185 m above the road. The road to its
        // north spans z 25.5..36.5 (between two decks), so (0, 31) is road level.
        static readonly Vector3 PlazaCentre = new Vector3(0f, 1f, 0f);
        static readonly Vector3 RoadNorthOfPlaza = new Vector3(0f, 1f, 31f);

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator AFrameThatTookNoTimeIsSkippedNotSimulated()
        {
            // The first frame after unpausing switches the controller on in a frame whose delta is already zero.
            _rig.PlaceAndSettle(PlazaCentre);
            _rig.Player.AutoTick = true;
            _rig.Input.Forward = 1;
            Vector3 before = _rig.Player.transform.position;
            float savedScale = Time.timeScale;
            Time.timeScale = 0f;
            try
            {
                yield return null;
                yield return null;
                yield return null;
            }
            finally
            {
                Time.timeScale = savedScale;
            }
            Assert.AreEqual(before.x, _rig.Player.transform.position.x, 1e-4f);
            Assert.AreEqual(before.z, _rig.Player.transform.position.z, 1e-4f);
        }

        [Test]
        public void AnExplicitTickWithNoTimeStillFailsLoudly()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _rig.Player.Tick(0f));
        }

        [Test]
        public void SettlesOnThePlazaDeckAtKerbHeight()
        {
            _rig.PlaceAndSettle(PlazaCentre);
            Assert.That(_rig.Player.transform.position.y, Is.InRange(Kerb - 0.01f, Kerb + 0.05f));
        }

        [Test]
        public void SettlesOnTheRoadAtStreetLevel()
        {
            _rig.PlaceAndSettle(RoadNorthOfPlaza);
            Assert.That(_rig.Player.transform.position.y, Is.InRange(-0.01f, 0.05f));
        }

        [Test]
        public void ClimbsAKerbByAutostep()
        {
            _rig.PlaceAndSettle(RoadNorthOfPlaza, yawDegrees: 180f);   // facing -Z, toward the plaza deck
            _rig.Input.Forward = 1;
            _rig.Run(2f);

            Vector3 p = _rig.Player.transform.position;
            Assert.Less(p.z, 25.4f, "walked past the kerb edge at z = 25.5");
            Assert.That(p.y, Is.InRange(Kerb - 0.01f, Kerb + 0.05f), "ended up on the deck, not stuck at the kerb");
            Assert.IsTrue(_rig.Player.Motor.Grounded);
        }

        [Test]
        public void WalksOffTheKerbAndLandsOnTheRoad()
        {
            _rig.PlaceAndSettle(new Vector3(0f, 1f, 20f), yawDegrees: 0f);   // on the deck, facing the road
            _rig.Input.Forward = 1;
            _rig.Run(2f);

            Vector3 p = _rig.Player.transform.position;
            Assert.Greater(p.z, 26f);
            Assert.That(p.y, Is.InRange(-0.01f, 0.05f));
        }

        [Test]
        public void WalkingNeverFallsThroughTheWorld()
        {
            _rig.PlaceAndSettle(PlazaCentre);
            _rig.Input.Forward = 1;
            _rig.Input.Sprint = true;
            for (int i = 0; i < 600; i++)
            {
                _rig.Player.Tick(PlayerTestRig.Dt);
                Assert.Greater(_rig.Player.transform.position.y, -0.1f, $"fell through at step {i}");
            }
        }

        [Test]
        public void BuildingsStopThePlayerAtTheirWalls()
        {
            // Find a spot where a building wall is dead ahead, using the colliders themselves.
            const float startZ = 40f;
            float wallZ = float.NaN, x = 0f;
            for (float tryX = -20f; tryX <= 20f; tryX += 2f)
            {
                if (Physics.Raycast(new Vector3(tryX, 1f, startZ), Vector3.forward, out RaycastHit hit, 80f))
                {
                    wallZ = hit.point.z;
                    x = tryX;
                    break;
                }
            }
            Assert.IsFalse(float.IsNaN(wallZ), "no building found north of the plaza to walk into");

            _rig.PlaceAndSettle(new Vector3(x, 1f, startZ), yawDegrees: 0f);
            _rig.Input.Forward = 1;
            _rig.Run(8f);

            float z = _rig.Player.transform.position.z;
            float stopped = wallZ - (float)PlayerMotor.Radius - FirstPersonController.SkinWidth;
            Assert.AreEqual(stopped, z, 0.15f, "stopped one capsule radius short of the wall");
        }

        [Test]
        public void JumpLeavesTheGroundPeaksNearTheBallisticHeightAndLands()
        {
            _rig.PlaceAndSettle(PlazaCentre);
            float floor = _rig.Player.transform.position.y;

            _rig.Input.PressJump();
            float peak = floor;
            bool jumped = false;
            for (int i = 0; i < 90; i++)
            {
                _rig.Player.Tick(PlayerTestRig.Dt);
                jumped |= _rig.Player.Motor.Jumped;
                peak = Mathf.Max(peak, _rig.Player.transform.position.y);
            }

            Assert.IsTrue(jumped);
            float ideal = (float)(PlayerMotor.JumpVelocity * PlayerMotor.JumpVelocity / (2 * -PlayerMotor.Gravity));
            Assert.AreEqual(ideal, peak - floor, 0.12f);
            Assert.IsTrue(_rig.Player.Motor.Grounded, "back on the ground after 1.5 s");
            Assert.AreEqual(floor, _rig.Player.transform.position.y, 0.03f);
        }

        [Test]
        public void SprintCoversMoreGroundThanWalking()
        {
            _rig.PlaceAndSettle(PlazaCentre);
            Vector3 start = _rig.Player.transform.position;
            _rig.Input.Forward = 1;
            _rig.Run(1f);
            float walked = (_rig.Player.transform.position - start).magnitude;

            _rig.PlaceAndSettle(PlazaCentre);
            start = _rig.Player.transform.position;
            _rig.Input.Forward = 1;
            _rig.Input.Sprint = true;
            _rig.Run(1f);
            float sprinted = (_rig.Player.transform.position - start).magnitude;

            Assert.Greater(sprinted, walked * 1.5f);
        }

        [Test]
        public void CrouchingShrinksTheCapsuleAboutTheFeet()
        {
            _rig.PlaceAndSettle(PlazaCentre);
            float feet = _rig.Player.transform.position.y;

            _rig.Input.Crouch = true;
            _rig.Run(0.2f);
            CharacterController cc = _rig.Player.Controller;
            Assert.AreEqual((float)PlayerMotor.HeightCrouch, cc.height, 1e-4f);
            Assert.AreEqual((float)PlayerMotor.HeightCrouch / 2f, cc.center.y, 1e-4f);
            Assert.AreEqual(feet, _rig.Player.transform.position.y, 0.03f, "feet did not move");

            _rig.Input.Crouch = false;
            _rig.Run(0.2f);
            Assert.AreEqual((float)PlayerMotor.HeightStand, cc.height, 1e-4f);
        }

        [Test]
        public void CannotStandUpUnderALowCeiling()
        {
            _rig.PlaceAndSettle(PlazaCentre);
            _rig.Input.Crouch = true;
            _rig.Run(0.3f);

            // A slab whose underside is 1.4 m above the street: room to crouch, none to stand.
            var ceiling = _rig.Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ceiling.transform.position = new Vector3(0f, 1.5f + Kerb, 0f);
            ceiling.transform.localScale = new Vector3(6f, 0.2f, 6f);
            Physics.SyncTransforms();

            _rig.Input.Crouch = false;
            _rig.Run(0.5f);
            Assert.IsTrue(_rig.Player.Motor.Crouching, "must stay crouched under the slab");
            Assert.AreEqual((float)PlayerMotor.HeightCrouch, _rig.Player.Controller.height, 1e-4f);

            Object.DestroyImmediate(ceiling);
            Physics.SyncTransforms();
            _rig.Run(0.2f);
            Assert.IsFalse(_rig.Player.Motor.Crouching);
            Assert.AreEqual((float)PlayerMotor.HeightStand, _rig.Player.Controller.height, 1e-4f);
        }

        [Test]
        public void CameraSitsAtEyeHeightAndFollowsTheLook()
        {
            _rig.PlaceAndSettle(PlazaCentre);
            Assert.AreEqual((float)PlayerMotor.EyeStand, _rig.CameraPivot.localPosition.y, 1e-3f);

            _rig.Input.LookX = 1000;   // 2.2 rad of yaw at the default sensitivity
            _rig.Input.LookY = 100;    // 0.22 rad up
            _rig.Run(PlayerTestRig.Dt);

            float yawDeg = (float)(1000 * LookState.DefaultSensitivity * Mathf.Rad2Deg);
            Assert.AreEqual(yawDeg % 360f, _rig.Player.transform.eulerAngles.y, 0.01f);
            float pitchDeg = (float)(100 * LookState.DefaultSensitivity * Mathf.Rad2Deg);
            Assert.AreEqual(360f - pitchDeg, _rig.CameraPivot.localEulerAngles.x, 0.01f, "looking up tilts the pivot negatively about X");
        }

        [Test]
        public void EyeHeightDropsWhileCrouching()
        {
            _rig.PlaceAndSettle(PlazaCentre);
            _rig.Input.Crouch = true;
            _rig.Run(1f);
            Assert.AreEqual((float)PlayerMotor.EyeCrouch, _rig.CameraPivot.localPosition.y, 0.01f);
        }

        [Test]
        public void MovingForwardAfterTurningGoesWhereTheCameraLooks()
        {
            _rig.PlaceAndSettle(PlazaCentre, yawDegrees: 90f);   // facing +X
            Vector3 start = _rig.Player.transform.position;
            _rig.Input.Forward = 1;
            _rig.Run(0.5f);
            Vector3 d = _rig.Player.transform.position - start;
            Assert.Greater(d.x, 1.5f);
            Assert.AreEqual(0f, d.z, 0.05f);
        }

        [Test]
        public void TeleportStopsAllMotion()
        {
            _rig.PlaceAndSettle(PlazaCentre);
            _rig.Input.Forward = 1;
            _rig.Input.Sprint = true;
            _rig.Run(1f);
            Assert.Greater(_rig.Player.Motor.PlanarSpeed, 5f);

            _rig.Input.Clear();
            _rig.Place(new Vector3(10f, 1f, 10f));
            Assert.AreEqual(0.0, _rig.Player.Motor.PlanarSpeed);
            Assert.AreEqual(new Vector3(10f, 1f, 10f), _rig.Player.transform.position);
        }

        [Test]
        public void TickWithoutACommandSourceFailsLoudly()
        {
            // Inactive, so OnEnable never installs the default Input System source.
            var go = _rig.Track(new GameObject("Orphan"));
            go.SetActive(false);
            var fpc = go.AddComponent<FirstPersonController>();
            Assert.Throws<System.InvalidOperationException>(() => fpc.Tick(PlayerTestRig.Dt));
        }
    }
}
