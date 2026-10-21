using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class PlayerMotorTests
    {
        const double Dt = 1.0 / 60;

        static PlayerCommand Walk(double forward = 1, double strafe = 0, bool sprint = false, bool crouch = false, bool jump = false) =>
            new PlayerCommand(forward, strafe, sprint, crouch, jump);

        static readonly PlayerCommand Idle = new PlayerCommand(0, 0, false, false, false);

        /// <summary>Run steps as if standing on flat ground: every move ends grounded.</summary>
        static void RunGrounded(PlayerMotor m, PlayerCommand cmd, double seconds, double yaw = 0, bool headBlocked = false)
        {
            int n = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < n; i++)
            {
                m.Step(cmd, yaw, Dt, headBlocked);
                m.ReportResult(true, false);
            }
        }

        static PlayerMotor Grounded()
        {
            var m = new PlayerMotor();
            RunGrounded(m, Idle, 0.1);
            return m;
        }

        // ---- ground movement ----

        [Test]
        public void StandingStillOnTheGroundStaysAtRest()
        {
            PlayerMotor m = Grounded();
            Assert.AreEqual(0.0, m.PlanarSpeed, 1e-9);
            Assert.AreEqual(0.0, m.VelocityY, 1e-9);
            Assert.IsTrue(m.Grounded);
        }

        [Test]
        public void WalkingForwardReachesWalkSpeedTowardPlusZ()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(), 1);
            Assert.AreEqual(PlayerMotor.WalkSpeed, m.PlanarSpeed, 0.01);
            Assert.Greater(m.VelocityZ, 0);
            Assert.AreEqual(0.0, m.VelocityX, 1e-6);
        }

        [Test]
        public void StrafingRightMovesTowardPlusXAtYawZero()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(forward: 0, strafe: 1), 1);
            Assert.AreEqual(PlayerMotor.WalkSpeed, m.VelocityX, 0.01);
            Assert.AreEqual(0.0, m.VelocityZ, 1e-6);
        }

        [Test]
        public void YawRotatesTheWishDirection()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(), 1, yaw: Math.PI / 2);
            Assert.AreEqual(PlayerMotor.WalkSpeed, m.VelocityX, 0.01, "facing +X means forward is +X");
            Assert.AreEqual(0.0, m.VelocityZ, 1e-6);
        }

        [Test]
        public void DiagonalIsNotFasterThanStraight()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(forward: 1, strafe: 1), 1);
            Assert.AreEqual(PlayerMotor.WalkSpeed, m.PlanarSpeed, 0.01);
        }

        [Test]
        public void AnalogHalfPushWalksAtHalfSpeed()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(forward: 0.5), 1);
            Assert.AreEqual(PlayerMotor.WalkSpeed / 2, m.PlanarSpeed, 0.01);
        }

        [Test]
        public void ReleasingInputBringsThePlayerToAStop()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(), 1);
            RunGrounded(m, Idle, 1);
            Assert.AreEqual(0.0, m.PlanarSpeed, 0.01);
        }

        [Test]
        public void DisplacementIsVelocityTimesDt()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(), 1);
            Vec3 d = m.Step(Walk(), 0, Dt, false);
            Assert.AreEqual(m.VelocityZ * Dt, d.Z, 1e-12);
            Assert.AreEqual(m.VelocityY * Dt, d.Y, 1e-12);
        }

        [Test]
        public void GroundedPlayerStillPushesDownEachStepSoTheControllerStaysGrounded()
        {
            PlayerMotor m = Grounded();
            Vec3 d = m.Step(Idle, 0, Dt, false);
            Assert.Less(d.Y, 0);
        }

        // ---- sprint ----

        [Test]
        public void SprintingForwardReachesSprintSpeed()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(sprint: true), 1);
            Assert.IsTrue(m.Sprinting);
            Assert.AreEqual(PlayerMotor.SprintSpeed, m.PlanarSpeed, 0.01);
        }

        [Test]
        public void SprintNeedsForwardMotion()
        {
            PlayerMotor strafe = Grounded();
            RunGrounded(strafe, Walk(forward: 0, strafe: 1, sprint: true), 0.5);
            Assert.IsFalse(strafe.Sprinting, "pure strafe");

            PlayerMotor back = Grounded();
            RunGrounded(back, Walk(forward: -1, sprint: true), 0.5);
            Assert.IsFalse(back.Sprinting, "backwards");

            PlayerMotor still = Grounded();
            RunGrounded(still, Walk(forward: 0, sprint: true), 0.5);
            Assert.IsFalse(still.Sprinting, "standing still");
        }

        [Test]
        public void ForwardDiagonalStillSprints()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(forward: 1, strafe: 0.5, sprint: true), 0.5);
            Assert.IsTrue(m.Sprinting);
        }

        // ---- crouch ----

        [Test]
        public void CrouchingShrinksTheColliderAndSlowsTheWalk()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(crouch: true), 1);
            Assert.IsTrue(m.Crouching);
            Assert.AreEqual(PlayerMotor.HeightCrouch, m.ColliderHeight);
            Assert.AreEqual(PlayerMotor.CrouchSpeed, m.PlanarSpeed, 0.01);
        }

        [Test]
        public void CrouchingCancelsSprint()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(sprint: true, crouch: true), 1);
            Assert.IsFalse(m.Sprinting);
            Assert.AreEqual(PlayerMotor.CrouchSpeed, m.PlanarSpeed, 0.01);
        }

        [Test]
        public void StandsUpOnlyOnceThereIsHeadroom()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(forward: 0, crouch: true), 0.2);
            Assert.IsTrue(m.Crouching);

            RunGrounded(m, Idle, 0.2, headBlocked: true);
            Assert.IsTrue(m.Crouching, "still crouched while blocked");
            Assert.AreEqual(PlayerMotor.HeightCrouch, m.ColliderHeight);

            RunGrounded(m, Idle, 0.1, headBlocked: false);
            Assert.IsFalse(m.Crouching);
            Assert.AreEqual(PlayerMotor.HeightStand, m.ColliderHeight);
        }

        [Test]
        public void HeadroomDoesNotStopTheStartOfACrouch()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(forward: 0, crouch: true), 0.1, headBlocked: true);
            Assert.IsTrue(m.Crouching);
        }

        [Test]
        public void EyeHeightEasesDownToCrouchAndBackUp()
        {
            PlayerMotor m = Grounded();
            Assert.AreEqual(PlayerMotor.EyeStand, m.EyeHeight, 1e-9);

            double previous = m.EyeHeight;
            for (int i = 0; i < 60; i++)
            {
                m.Step(Walk(forward: 0, crouch: true), 0, Dt, false);
                m.ReportResult(true, false);
                Assert.LessOrEqual(m.EyeHeight, previous + 1e-12, "monotonic descent");
                previous = m.EyeHeight;
            }
            Assert.AreEqual(PlayerMotor.EyeCrouch, m.EyeHeight, 0.01);

            RunGrounded(m, Idle, 1);
            Assert.AreEqual(PlayerMotor.EyeStand, m.EyeHeight, 0.01);
        }

        // ---- jumping ----

        [Test]
        public void JumpingFromTheGroundLaunchesUpward()
        {
            PlayerMotor m = Grounded();
            Vec3 d = m.Step(Walk(forward: 0, jump: true), 0, Dt, false);
            Assert.IsTrue(m.Jumped);
            Assert.AreEqual(PlayerMotor.JumpVelocity + PlayerMotor.Gravity * Dt, m.VelocityY, 1e-9);
            Assert.Greater(d.Y, 0);
            Assert.IsFalse(m.Grounded);
        }

        [Test]
        public void JumpPeakMatchesBallisticHeight()
        {
            PlayerMotor m = Grounded();
            double y = 0, peak = 0;
            Vec3 d = m.Step(Walk(forward: 0, jump: true), 0, Dt, false);
            y += d.Y;
            for (int i = 0; i < 120 && y >= 0; i++)
            {
                m.ReportResult(false, false);
                d = m.Step(Idle, 0, Dt, false);
                y += d.Y;
                peak = Math.Max(peak, y);
            }
            double ideal = PlayerMotor.JumpVelocity * PlayerMotor.JumpVelocity / (2 * -PlayerMotor.Gravity);
            Assert.AreEqual(ideal, peak, 0.1, "peak height of a 6.4 m/s jump under 22 m/s^2");
        }

        [Test]
        public void HoldingJumpDoesNotBunnyHopWithoutANewPress()
        {
            PlayerMotor m = Grounded();
            m.Step(Walk(forward: 0, jump: true), 0, Dt, false);
            Assert.IsTrue(m.Jumped);
            m.ReportResult(true, false);   // touched down straight away
            m.Step(Idle, 0, Dt, false);
            Assert.IsFalse(m.Jumped, "a spent press must not fire twice");
        }

        [Test]
        public void JumpPressedJustBeforeLandingStillJumps()
        {
            var m = new PlayerMotor();   // starts airborne with no coyote time
            m.Step(Walk(forward: 0, jump: true), 0, Dt, false);
            Assert.IsFalse(m.Jumped, "nothing to jump from yet");

            m.ReportResult(true, false);   // lands within the buffer window
            m.Step(Idle, 0, Dt, false);
            Assert.IsTrue(m.Jumped);
        }

        [Test]
        public void JumpPressedTooEarlyIsForgotten()
        {
            var m = new PlayerMotor();
            m.Step(Walk(forward: 0, jump: true), 0, Dt, false);
            for (int i = 0; i < 12; i++)   // 0.2 s in the air, longer than the buffer
            {
                m.ReportResult(false, false);
                m.Step(Idle, 0, Dt, false);
            }
            m.ReportResult(true, false);
            m.Step(Idle, 0, Dt, false);
            Assert.IsFalse(m.Jumped);
        }

        [Test]
        public void CoyoteTimeAllowsAJumpJustAfterWalkingOffALedge()
        {
            PlayerMotor m = Grounded();
            m.Step(Idle, 0, Dt, false);
            m.ReportResult(false, false);   // stepped off
            for (int i = 0; i < 3; i++)     // 0.05 s later
            {
                m.Step(Idle, 0, Dt, false);
                m.ReportResult(false, false);
            }
            m.Step(Walk(forward: 0, jump: true), 0, Dt, false);
            Assert.IsTrue(m.Jumped);
        }

        [Test]
        public void CoyoteTimeRunsOut()
        {
            PlayerMotor m = Grounded();
            m.Step(Idle, 0, Dt, false);
            m.ReportResult(false, false);
            for (int i = 0; i < 12; i++)    // 0.2 s later
            {
                m.Step(Idle, 0, Dt, false);
                m.ReportResult(false, false);
            }
            m.Step(Walk(forward: 0, jump: true), 0, Dt, false);
            Assert.IsFalse(m.Jumped);
        }

        [Test]
        public void CannotJumpWhileCrouching()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(forward: 0, crouch: true), 0.2);
            m.Step(Walk(forward: 0, crouch: true, jump: true), 0, Dt, false);
            Assert.IsFalse(m.Jumped);
        }

        // ---- air, gravity, landing ----

        [Test]
        public void FallingAcceleratesThenClampsAtTerminalVelocity()
        {
            var m = new PlayerMotor();
            m.Step(Idle, 0, Dt, false);
            Assert.AreEqual(PlayerMotor.Gravity * Dt, m.VelocityY, 1e-9);

            for (int i = 0; i < 600; i++)
            {
                m.Step(Idle, 0, Dt, false);
                m.ReportResult(false, false);
            }
            Assert.AreEqual(PlayerMotor.TerminalVelocity, m.VelocityY, 1e-9);
        }

        [Test]
        public void AirControlIsWeakerThanGroundControl()
        {
            PlayerMotor ground = Grounded();
            ground.Step(Walk(), 0, Dt, false);

            var air = new PlayerMotor();
            air.Step(Walk(), 0, Dt, false);

            Assert.Less(air.PlanarSpeed, ground.PlanarSpeed / 3);
            Assert.Greater(air.PlanarSpeed, 0);
        }

        [Test]
        public void HardLandingReportsAnImpactScaledByFallSpeed()
        {
            var m = new PlayerMotor();
            for (int i = 0; i < 60; i++)   // one second of falling, about 22 m/s
            {
                m.Step(Idle, 0, Dt, false);
                m.ReportResult(false, false);
            }
            m.Step(Idle, 0, Dt, false);
            double fall = -m.VelocityY;
            m.ReportResult(true, false);

            Assert.AreEqual(Math.Min(1, fall / PlayerMotor.LandingFullImpactSpeed), m.LandImpact, 1e-9);
            Assert.AreEqual(1.0, m.LandImpact, 1e-9, "22 m/s is past the full-impact speed");
            Assert.AreEqual(0.0, m.VelocityY, 1e-12, "landing zeroes the fall");
        }

        [Test]
        public void MediumLandingGivesAPartialImpact()
        {
            var m = new PlayerMotor();
            for (int i = 0; i < 24; i++)   // 0.4 s of falling, about 8.8 m/s
            {
                m.Step(Idle, 0, Dt, false);
                m.ReportResult(false, false);
            }
            m.Step(Idle, 0, Dt, false);
            m.ReportResult(true, false);
            Assert.That(m.LandImpact, Is.GreaterThan(0).And.LessThan(1));
        }

        [Test]
        public void GentleStepDownIsSilent()
        {
            var m = new PlayerMotor();
            m.Step(Idle, 0, Dt, false);   // one frame of gravity, about 0.37 m/s
            m.ReportResult(true, false);
            Assert.AreEqual(0.0, m.LandImpact);
        }

        [Test]
        public void LandingImpactClearsOnTheNextStep()
        {
            var m = new PlayerMotor();
            for (int i = 0; i < 60; i++)
            {
                m.Step(Idle, 0, Dt, false);
                m.ReportResult(false, false);
            }
            m.Step(Idle, 0, Dt, false);
            m.ReportResult(true, false);
            Assert.Greater(m.LandImpact, 0);
            m.Step(Idle, 0, Dt, false);
            Assert.AreEqual(0.0, m.LandImpact);
        }

        [Test]
        public void HittingACeilingWhileRisingKillsUpwardVelocity()
        {
            PlayerMotor m = Grounded();
            m.Step(Walk(forward: 0, jump: true), 0, Dt, false);
            Assert.Greater(m.VelocityY, 0);
            m.ReportResult(false, true);
            Assert.AreEqual(0.0, m.VelocityY, 1e-12);
        }

        [Test]
        public void RisingWithoutHittingAnythingKeepsItsVelocity()
        {
            PlayerMotor m = Grounded();
            m.Step(Walk(forward: 0, jump: true), 0, Dt, false);
            double vy = m.VelocityY;
            m.ReportResult(false, false);
            Assert.AreEqual(vy, m.VelocityY, 1e-12);
        }

        // ---- misc ----

        [Test]
        public void NonPositiveOrNonFiniteDtFailsLoudly()
        {
            var m = new PlayerMotor();
            Assert.Throws<ArgumentOutOfRangeException>(() => m.Step(Idle, 0, 0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => m.Step(Idle, 0, -0.01, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => m.Step(Idle, 0, double.NaN, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => m.Step(Idle, 0, double.PositiveInfinity, false));
        }

        [Test]
        public void ResetMotionClearsVelocityAndPendingJumps()
        {
            PlayerMotor m = Grounded();
            RunGrounded(m, Walk(sprint: true), 1);
            m.Step(Walk(jump: true), 0, Dt, false);
            m.ResetMotion();

            Assert.AreEqual(0.0, m.PlanarSpeed);
            Assert.AreEqual(0.0, m.VelocityY);
            Assert.IsFalse(m.Jumped);
            m.ReportResult(true, false);
            m.Step(Idle, 0, Dt, false);
            Assert.IsFalse(m.Jumped, "the buffered press was discarded");
        }

        [Test]
        public void ConstantsMatchThePrototype()
        {
            Assert.AreEqual(1.68, PlayerMotor.EyeStand);
            Assert.AreEqual(1.02, PlayerMotor.EyeCrouch);
            Assert.AreEqual(0.34, PlayerMotor.Radius);
            Assert.AreEqual(1.8, PlayerMotor.HeightStand);
            Assert.AreEqual(1.15, PlayerMotor.HeightCrouch);
            Assert.AreEqual(4.1, PlayerMotor.WalkSpeed);
            Assert.AreEqual(7.4, PlayerMotor.SprintSpeed);
            Assert.AreEqual(1.9, PlayerMotor.CrouchSpeed);
            Assert.AreEqual(6.4, PlayerMotor.JumpVelocity);
            Assert.AreEqual(-22.0, PlayerMotor.Gravity);
        }
    }
}
