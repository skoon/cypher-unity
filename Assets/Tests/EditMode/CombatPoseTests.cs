using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The fighters' poses on the pedestrian rig: where the parts end up, checked as geometry (there is no prototype to match).</summary>
    public class CombatPoseTests
    {
        static PoseInput Standing(double aim = 0, double crouch = 0, double death = 0, double recoil = 0, double flinch = 0, double side = 0, double pitch = 0,
            HeldWeapon weapon = HeldWeapon.Smg, double speed = 0, double phase = 1, double heading = 0, double scale = 1) => new PoseInput
        {
            Scale = scale, Build = 1, Speed = speed, PrevPhase = phase, Phase = phase, PrevHeading = heading, Heading = heading, Alpha = 1, Time = 3,
            LookSeed = 7, Aim = aim, Crouch = crouch, Death = death, Recoil = recoil, Flinch = flinch, Side = side, AimPitch = pitch, Weapon = weapon, Visor = true, Trim = true,
        };

        static (double X, double Y, double Z) At(Mat4 m, double x = 0, double y = 0, double z = 0) => m.Transform(x, y, z);

        static (double X, double Y, double Z) Muzzle(PedestrianPoseResult r) => At(r.Gun, 0, 0.03, 0.28);

        static (double X, double Y, double Z) Barrel(PedestrianPoseResult r)
        {
            var a = At(r.Gun);
            var b = At(r.Gun, 0, 0, 1);
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return (dx / d, dy / d, dz / d);
        }

        static double FootY(Mat4 shin) => At(shin, 0, -PedestrianPose.ShinLength, 0).Y;

        [Test]
        public void StandingTheHeadIsAtFullHeightAndCrouchedItIsAtTheCoverMapsCrouchingEye()
        {
            PedestrianPoseResult up = PedestrianPose.Pose(Standing());
            PedestrianPoseResult down = PedestrianPose.Pose(Standing(crouch: 1));
            Assert.AreEqual(1.57, At(up.Head).Y, 0.04);
            Assert.AreEqual(CoverMap.CrouchEye, At(down.Head).Y, 0.1, "the cover map hides a crouching enemy behind low cover by this eye height");
            Assert.Less(At(down.Head).Y, At(up.Head).Y - 0.6);
        }

        [TestCase(0.0)]
        [TestCase(0.5)]
        [TestCase(1.0)]
        public void CrouchingBothFeetStayOnTheGround(double crouch)
        {
            PedestrianPoseResult r = PedestrianPose.Pose(Standing(crouch: crouch));
            Assert.AreEqual(-0.02, FootY(r.ShinL), 0.03);
            Assert.AreEqual(-0.02, FootY(r.ShinR), 0.03);
        }

        [TestCase(0.0)]
        [TestCase(1.0)]
        public void AimingTheGunIsForwardAtShoulderHeightAndLevelStandingOrCrouched(double crouch)
        {
            PedestrianPoseResult r = PedestrianPose.Pose(Standing(aim: 1, crouch: crouch));
            var muzzle = Muzzle(r);
            var shoulder = At(r.ArmR);
            var barrel = Barrel(r);
            Assert.Greater(muzzle.Z - At(r.Torso).Z, 0.45, "held out in front");
            Assert.AreEqual(shoulder.Y, muzzle.Y, 0.15, "at shoulder height");
            Assert.Greater(barrel.Z, 0.95, "pointing forward");
            Assert.AreEqual(0, barrel.Y, 0.08, "and level");
            Assert.Less(Math.Abs(muzzle.X), 0.2, "in front of the body, not off to the side");
        }

        [Test]
        public void AimPitchTipsTheBarrelUpAndDown()
        {
            Assert.AreEqual(Math.Sin(0.3), Barrel(PedestrianPose.Pose(Standing(aim: 1, pitch: 0.3))).Y, 0.08);
            Assert.AreEqual(Math.Sin(-0.3), Barrel(PedestrianPose.Pose(Standing(aim: 1, pitch: -0.3))).Y, 0.08);
        }

        [Test]
        public void NotAimingTheGunHangsLowInTheHand()
        {
            PedestrianPoseResult r = PedestrianPose.Pose(Standing());
            Assert.Less(Muzzle(r).Y, 1.1, "below the chest");
            Assert.Less(Barrel(r).Y, -0.3, "pointing down");
            Assert.Greater(Barrel(r).Z, 0.2, "and forward");
        }

        [Test]
        public void TheGunIsInTheRightHand()
        {
            PedestrianPoseResult r = PedestrianPose.Pose(Standing(aim: 0.6));
            var hand = At(r.ArmR, 0, -0.60, 0.055);
            var grip = At(r.Gun);
            double d = Math.Sqrt((hand.X - grip.X) * (hand.X - grip.X) + (hand.Y - grip.Y) * (hand.Y - grip.Y) + (hand.Z - grip.Z) * (hand.Z - grip.Z));
            Assert.Less(d, 0.05);
        }

        [Test]
        public void RecoilKicksTheMuzzleUp()
        {
            double still = Muzzle(PedestrianPose.Pose(Standing(aim: 1))).Y;
            double kicked = Muzzle(PedestrianPose.Pose(Standing(aim: 1, recoil: 1))).Y;
            Assert.Greater(kicked, still + 0.05);
        }

        [Test]
        public void AFlinchJerksTheHeadBackAndTurnsTheShouldersTheWayOfTheSide()
        {
            PedestrianPoseResult calm = PedestrianPose.Pose(Standing());
            PedestrianPoseResult left = PedestrianPose.Pose(Standing(flinch: 1, side: -1));
            PedestrianPoseResult right = PedestrianPose.Pose(Standing(flinch: 1, side: 1));
            Assert.Less(At(left.Head).Z, At(calm.Head).Z - 0.1, "back");
            double turnL = At(left.ArmR).Z - At(left.ArmL).Z, turnR = At(right.ArmR).Z - At(right.ArmL).Z;
            Assert.AreEqual(Math.Sign(turnR), -Math.Sign(turnL), "the two sides turn opposite ways");
            Assert.Greater(Math.Abs(turnR), 0.05);
        }

        [Test]
        public void AFallEndsWithTheBodyFlatOnItsBackBehindItsFeet()
        {
            PedestrianPoseResult r = PedestrianPose.Pose(Standing(death: 1, aim: 0));
            foreach (Mat4 m in new[] { r.Torso, r.Head, r.ArmL, r.ArmR, r.ThighL, r.ThighR, r.ShinL, r.ShinR, r.Gun })
            {
                Assert.That(At(m).Y, Is.InRange(-0.05, 0.45), "every joint near the ground");
            }
            foreach (Mat4 arm in new[] { r.ArmL, r.ArmR }) Assert.That(At(arm, 0, -0.60, 0).Y, Is.InRange(-0.05, 0.45), "the hands too");
            Assert.That(FootY(r.ShinL), Is.InRange(-0.05, 0.3));
            Assert.Less(At(r.Head).Z, -1.3, "lying back from where it stood");
            var torso = At(r.Torso);
            var front = At(r.Torso, 0, 0, 1);
            Assert.Greater(front.Y - torso.Y, 0.8, "face up");
        }

        [Test]
        public void HalfwayThroughAFallTheBodyIsGoingDownAndBack()
        {
            double standing = At(PedestrianPose.Pose(Standing()).Head).Y;
            var mid = At(PedestrianPose.Pose(Standing(death: 0.5)).Head);
            var end = At(PedestrianPose.Pose(Standing(death: 1)).Head);
            Assert.Less(mid.Y, standing);
            Assert.Greater(mid.Y, end.Y);
            Assert.Less(mid.Z, 0);
        }

        [Test]
        public void ACivilianWithNoGunHasNoGunAndNoVisor()
        {
            PoseInput p = Standing(weapon: HeldWeapon.None);
            p.Visor = false;
            PedestrianPoseResult r = PedestrianPose.Pose(p);
            Assert.IsNull(r.Gun);
            Assert.IsNull(r.Visor);
        }

        [Test]
        public void TheCheapBodiesCrouchAndFallToo()
        {
            var top = PedestrianPose.StaticBody(0, 0, 0, 0, 1, 1).Transform(0, 1.62, 0);
            var crouched = PedestrianPose.StaticBody(0, 0, 0, 0, 1, 1, crouch: 1).Transform(0, 1.62, 0);
            var dead = PedestrianPose.StaticBody(0, 0, 0, 0, 1, 1, death: 1).Transform(0, 1.62, 0);
            Assert.AreEqual(1.62, top.Y, 1e-9);
            Assert.AreEqual(1.62 * (1 - PedestrianPose.StaticCrouch), crouched.Y, 1e-9);
            Assert.Less(dead.Y, 0.3);
            Assert.Less(dead.Z, -1.4, "the box lies back along the ground");
        }

        // ---------------------------------------------------------------- the hit volumes

        static PoseInput Random(Rng rng)
        {
            PoseInput p = Standing(aim: rng.Next(), crouch: rng.Next() < 0.4 ? rng.Next() : 0, death: rng.Next() < 0.3 ? rng.Next() : 0, recoil: rng.Next(),
                flinch: rng.Next(), side: rng.Next() * 2 - 1, pitch: (rng.Next() - 0.5) * 0.8, speed: rng.Next() * 3.5, phase: rng.Next() * 6.28,
                heading: rng.Next() * 6.28 - 3.14, scale: 0.95 + rng.Next() * 0.15);
            p.PrevX = rng.Next() * 40 - 20; p.X = p.PrevX + rng.Next() - 0.5;
            p.PrevZ = rng.Next() * 40 - 20; p.Z = p.PrevZ + rng.Next() - 0.5;
            p.PrevY = p.Y = 0.185;
            p.PrevHeading = p.Heading - 0.2;
            p.Alpha = rng.Next();
            p.Build = 0.9 + rng.Next() * 0.4;
            return p;
        }

        [Test]
        public void TheHeadVolumeIsOnTheHeadAndTheBodyVolumeRunsUpTheSpineInAnyPose()
        {
            var rng = new Rng(77);
            for (int k = 0; k < 300; k++)
            {
                PoseInput p = Random(rng);
                PedestrianPoseResult r = PedestrianPose.Pose(p);
                PoseHitShape h = PedestrianPose.HitShape(p);
                var head = At(r.Head);
                Assert.AreEqual(head.X, h.Head.X, 1e-9, $"state {k}");
                Assert.AreEqual(head.Y, h.Head.Y, 1e-9, $"state {k}");
                Assert.AreEqual(head.Z, h.Head.Z, 1e-9, $"state {k}");
                var shoulders = At(r.Torso, 0, PedestrianPose.HitShoulders, 0);
                Assert.AreEqual(shoulders.X, h.BodyB.X, 1e-9, $"state {k}");
                Assert.AreEqual(shoulders.Y, h.BodyB.Y, 1e-9, $"state {k}");
                Assert.AreEqual(shoulders.Z, h.BodyB.Z, 1e-9, $"state {k}");
                Assert.AreEqual(PedestrianPose.HitHeadRadius * p.Scale, h.HeadRadius, 1e-12);
            }
        }

        [Test]
        public void TheBodyVolumeStartsAboveTheFeetStandingAndAlongTheGroundLying()
        {
            PoseHitShape up = PedestrianPose.HitShape(Standing());
            Assert.AreEqual(-0.02 + PedestrianPose.HitLegs, up.BodyA.Y, 1e-9);
            Assert.AreEqual(0, up.BodyA.Z, 1e-9);
            PoseHitShape dead = PedestrianPose.HitShape(Standing(death: 1));
            Assert.AreEqual(-0.02 + PedestrianPose.FallLift, dead.BodyA.Y, 1e-6);
            Assert.AreEqual(-PedestrianPose.HitLegs, dead.BodyA.Z, 1e-6);
            Assert.Less(dead.BodyB.Y, 0.35);
            Assert.Less(dead.Head.Y, 0.35);
        }

        [Test]
        public void HeadAndShouldersDoNotShareAVolumeStanding()
        {
            PoseHitShape h = PedestrianPose.HitShape(Standing());
            double top = h.BodyB.Y + h.BodyRadius, bottom = h.Head.Y - h.HeadRadius;
            Assert.Less(top - bottom, 0.12, "a shot at the face meets the head first");
        }

        [Test]
        public void FindingTheHitVolumesAllocatesNothing()
        {
            PoseInput p = Standing(aim: 0.5, crouch: 0.3);
            PedestrianPose.HitShape(p);
            long before = GC.GetAllocatedBytesForCurrentThread();
            double sum = 0;
            for (int i = 0; i < 1000; i++)
            {
                p.Aim = i / 1000.0;
                sum += PedestrianPose.HitShape(p).Head.Y;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Greater(sum, 0);
            Assert.AreEqual(0, after - before);
        }
    }
}
