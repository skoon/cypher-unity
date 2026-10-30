using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// The guns' rules against the prototype's numbers (<c>player/Weapons.js</c>): draw, the three trigger modes, the fire rate on a 60 Hz step, dry fire into a
    /// reload, the reload's seat, the revolver's chamber check, switching, spread, sprint, menus, and a seeded session that repeats.
    /// </summary>
    public class WeaponRigTests
    {
        const double Dt = WeaponRig.FixedStep;

        static readonly WeaponBody Still = new WeaponBody { Grounded = true };

        static WeaponInput Act(bool fire = false, bool click = false, bool aim = false, bool reload = false, int select = 0) =>
            new WeaponInput { Acting = true, FireHeld = fire, FireClicked = click, AimHeld = aim, ReloadPressed = reload, Select = select };

        static int Run(WeaponRig rig, double seconds, Func<int, WeaponInput> input, WeaponBody? body = null)
        {
            int shots = 0, steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                rig.Step(Dt, input(i), body ?? Still);
                shots += rig.Shots.Count;
            }
            return shots;
        }

        static WeaponRig Ready(int slot = 0)
        {
            var rig = new WeaponRig();
            if (slot != 0) rig.RequestSwitch(slot);
            Run(rig, 1.2, _ => Act());
            Assert.AreEqual(WeaponState.Idle, rig.State);
            Assert.AreEqual(slot, rig.Index);
            return rig;
        }

        [Test]
        public void ANewGunIsDrawnFullAndThenReady()
        {
            var rig = new WeaponRig();
            Assert.AreEqual(WeaponState.Draw, rig.State);
            Assert.AreEqual("wasp", rig.Current.Config.Id);
            Assert.IsTrue(rig.Slots.All(s => s.Ammo == s.Config.MagSize && s.Reserve == s.Config.Reserve));
            Assert.AreEqual(0, Run(rig, 0.30, _ => Act(fire: true)), "nothing fires while drawing");
            Run(rig, 0.1, _ => Act());
            Assert.AreEqual(WeaponState.Idle, rig.State);
        }

        [Test]
        public void TheWaspFiresTwelveTimesASecondWhileHeld()
        {
            WeaponRig rig = Ready();
            Assert.AreEqual(12, Run(rig, 1.0, _ => Act(fire: true)), "880 rpm on a 60 Hz step is a shot every fifth step");
            Assert.AreEqual(20, rig.Current.Ammo);
        }

        [Test]
        public void TheRevolverFiresOncePerClickNotWhileHeld()
        {
            WeaponRig rig = Ready(1);
            Assert.AreEqual(1, Run(rig, 0.5, i => Act(fire: true, click: i == 0 || i == 10)), "held does not refire, and a click inside the 172 rpm interval does nothing");
            Assert.AreEqual(5, rig.Current.Ammo);
            Assert.AreEqual(1, Run(rig, 0.1, i => Act(fire: true, click: i == 0)), "a click after the interval fires");
        }

        [Test]
        public void TheRailFiresOnReleaseWithPowerFromTheCharge()
        {
            WeaponRig rig = Ready(2);
            WeaponConfig cfg = rig.Current.Config;
            Assert.AreEqual(0, Run(rig, 0.5, _ => Act(fire: true)), "holding only charges");
            Assert.AreEqual(Math.Min(1, 30 * Dt / cfg.ChargeTime), rig.Charge, 1e-9);
            double charge = rig.Charge;
            rig.Step(Dt, Act(), Still);
            Assert.AreEqual(1, rig.Shots.Count);
            Assert.AreEqual(0.4 + charge * 0.6, rig.Shots[0].Power, 1e-9);
            Assert.IsTrue(rig.Events.Any(e => e.Kind == WeaponEventKind.Charge));

            Run(rig, 1.0, _ => Act());
            Assert.AreEqual(0, Run(rig, 0.1, _ => Act(fire: true)) + Run(rig, Dt, _ => Act()), "a tap under the minimum charge does not fire");
        }

        [Test]
        public void AnEmptyGunDryFiresIntoAReloadThatFillsItAtTheSeat()
        {
            WeaponRig rig = Ready();
            Run(rig, 3.0, _ => Act(fire: true));
            Assert.AreEqual(WeaponState.Reload, rig.State, "dry fire starts a reload when there is reserve");
            WeaponSlot w = rig.Current;
            Assert.AreEqual(0, w.Ammo);
            double seatAt = w.Config.ReloadSeat * w.Config.ReloadTime;
            Run(rig, seatAt - 0.05 - rig.AnimTime, _ => Act());
            Assert.AreEqual(0, w.Ammo, "not yet seated");
            Run(rig, 0.1, _ => Act());
            Assert.AreEqual(w.Config.MagSize, w.Ammo);
            Assert.AreEqual(w.Config.Reserve - w.Config.MagSize, w.Reserve);
            Run(rig, w.Config.ReloadTime, _ => Act());
            Assert.AreEqual(WeaponState.Idle, rig.State);
        }

        [Test]
        public void ReloadingTopsUpFromTheReserveAndAFullRevolverIsCheckedInstead()
        {
            WeaponRig rig = Ready(1);
            Run(rig, 0.02, i => Act(fire: true, click: i == 0));
            Assert.AreEqual(5, rig.Current.Ammo);
            Run(rig, 0.5, i => Act(reload: i == 0));
            Assert.AreEqual(WeaponState.Reload, rig.State);
            Run(rig, 3.0, _ => Act());
            Assert.AreEqual(6, rig.Current.Ammo);
            Assert.AreEqual(47, rig.Current.Reserve, "only the one round came from the reserve");

            rig.Step(Dt, Act(reload: true), Still);
            Assert.AreEqual(WeaponState.Inspect, rig.State, "R on a full revolver opens the cylinder for a look");
        }

        [Test]
        public void SwitchingHolstersThenDrawsTheNewGun()
        {
            WeaponRig rig = Ready();
            rig.Step(Dt, Act(select: 3), Still);
            Assert.AreEqual(WeaponState.Holster, rig.State);
            Assert.AreEqual(0, rig.Index);
            Run(rig, 0.25, _ => Act());
            Assert.AreEqual(2, rig.Index);
            Assert.AreEqual(WeaponState.Draw, rig.State);
            Run(rig, 0.5, _ => Act());
            Assert.AreEqual(WeaponState.Idle, rig.State);
            rig.Step(Dt, Act(select: 3), Still);
            Assert.AreEqual(WeaponState.Idle, rig.State, "asking for the gun in hand does nothing");
        }

        [Test]
        public void ADefaultInputAsksForNoGun()
        {
            WeaponRig rig = Ready(2);
            Run(rig, 0.5, _ => new WeaponInput { Acting = true });
            Assert.AreEqual(2, rig.Index, "slot 0 is no slot: the rail stays in hand");
            Assert.AreEqual(WeaponState.Idle, rig.State);
        }

        [Test]
        public void SpreadGrowsWithEachShotAndSettlesBackToRest()
        {
            WeaponRig rig = Ready();
            WeaponConfig cfg = rig.Current.Config;
            Assert.AreEqual(cfg.Spread.Base, rig.Spread, 1e-5);
            Run(rig, 0.5, _ => Act(fire: true));
            Assert.Greater(rig.Spread, cfg.Spread.Base * 2, "per-shot growth against the decay settles near 0.011");
            Assert.LessOrEqual(rig.Spread, cfg.Spread.Max);
            Run(rig, 2.0, _ => Act());
            Assert.AreEqual(cfg.Spread.Base, rig.Spread, 1e-4);
            Assert.AreEqual(cfg.Spread.Air, WeaponRig.MoveSpread(new WeaponBody { Grounded = false }, cfg));
            Assert.AreEqual(cfg.Spread.Move * 1.9 * 0.4, WeaponRig.MoveSpread(new WeaponBody { Grounded = true, Speed = 20, Crouching = true }, cfg), 1e-12);
        }

        [Test]
        public void ShotsStayInsideTheSpreadCone()
        {
            WeaponRig rig = Ready();
            for (int i = 0; i < 120; i++)
            {
                double before = rig.Spread;
                rig.Step(Dt, Act(fire: true), Still);
                foreach (WeaponShot s in rig.Shots)
                {
                    double r = Math.Sqrt(s.SpreadRight * s.SpreadRight + s.SpreadUp * s.SpreadUp);
                    Assert.LessOrEqual(r, rig.Current.Config.Spread.Max + 1e-12);
                    Assert.Greater(s.RecoilPitch, 0, "every shot kicks the view up");
                }
            }
        }

        [Test]
        public void SprintingOrAMenuHoldsFire()
        {
            WeaponRig rig = Ready();
            var sprint = new WeaponBody { Grounded = true, Sprinting = true, Speed = 7 };
            Run(rig, 0.5, _ => Act(), sprint);
            Assert.Greater(rig.Sprint, WeaponRig.SprintBlocksFire);
            Assert.AreEqual(0, Run(rig, 0.5, _ => Act(fire: true), sprint), "the low-ready holds fire");
            Run(rig, 1.0, _ => Act());
            Assert.AreEqual(0, Run(rig, 0.5, _ => new WeaponInput { Acting = false, FireHeld = true, Select = 3 }), "a menu holds the trigger and the keys");
            Assert.AreEqual(0, rig.Index);
        }

        [Test]
        public void AimingNarrowsTheShotAndCannotHappenWhileSprinting()
        {
            WeaponRig rig = Ready();
            Run(rig, 0.6, _ => Act(aim: true));
            Assert.Greater(rig.Ads, 0.99);
            Assert.IsTrue(rig.AdsHeld);
            Run(rig, 0.6, _ => Act(aim: true), new WeaponBody { Grounded = true, Sprinting = true, Speed = 7 });
            Assert.IsFalse(rig.AdsHeld);
        }

        [Test]
        public void TheSameSeedPlaysTheSameSession()
        {
            string Session(uint seed)
            {
                var rig = new WeaponRig(seed);
                var log = new System.Text.StringBuilder();
                for (int i = 0; i < 400; i++)
                {
                    rig.Step(Dt, Act(fire: i % 50 < 30, click: i % 50 == 0, select: i == 200 ? 2 : 0), Still);
                    foreach (WeaponShot s in rig.Shots) log.Append($"{s.Index}:{s.SpreadRight:R},{s.SpreadUp:R},{s.RecoilYaw:R};");
                }
                return log.ToString();
            }
            Assert.AreEqual(Session(7), Session(7));
            Assert.AreNotEqual(Session(7), Session(8));
        }

        [Test]
        public void AHitOnAPersonIsMarkedCriticalAndFades()
        {
            WeaponRig rig = Ready();
            rig.RegisterHit(person: true, prop: false);
            Assert.AreEqual(1, rig.HitCritical);
            rig.RegisterHit(person: false, prop: false);
            Assert.AreEqual(1, rig.HitFlash);
            Run(rig, 0.5, _ => Act());
            Assert.AreEqual(0, rig.HitFlash);
        }

        [Test]
        public void BadStepsFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeaponRig().Step(0, Act(), Still));
            Assert.Throws<ArgumentOutOfRangeException>(() => WeaponTable.Get("nerf"));
        }
    }

    /// <summary>The viewmodel pose and the camera kick: ADS centres the sight, sprint lowers the gun, the scripted moves show and hide the parts, kicks decay.</summary>
    public class ViewmodelPoseTests
    {
        const double Dt = WeaponRig.FixedStep;

        static void Settle(WeaponRig rig, ViewmodelPose pose, WeaponInput input, WeaponBody body, double seconds)
        {
            for (int i = 0; i < (int)(seconds / Dt); i++)
            {
                rig.Step(Dt, input, body);
                pose.Update(rig, body, Dt, 0, 0, 0, 0);
            }
        }

        [Test]
        public void AimingPutsTheSightOnTheCentreAndNarrowsTheView()
        {
            var rig = new WeaponRig();
            var pose = new ViewmodelPose();
            var still = new WeaponBody { Grounded = true };
            Settle(rig, pose, new WeaponInput { Acting = true }, still, 1.0);
            WeaponConfig cfg = rig.Current.Config;
            Assert.AreEqual(cfg.Hip.X, pose.X, 0.01);
            Assert.AreEqual(ViewmodelPose.CameraFov, pose.Fov);
            Settle(rig, pose, new WeaponInput { Acting = true, AimHeld = true }, still, 1.0);
            Assert.AreEqual(-cfg.Sight.X, pose.X, 0.004);
            Assert.AreEqual(-cfg.Sight.Y, pose.Y, 0.006, "time 0: breathing adds next to nothing");
            Assert.AreEqual(cfg.AdsZ, pose.Z, 0.002);
            Assert.AreEqual(cfg.AdsFov, pose.Fov, 0.05);
            Assert.AreEqual(cfg.VmFovAds, pose.VmFov, 0.05);
        }

        [Test]
        public void SprintingLowersTheGun()
        {
            var rig = new WeaponRig();
            var pose = new ViewmodelPose();
            Settle(rig, pose, new WeaponInput { Acting = true }, new WeaponBody { Grounded = true }, 1.0);
            double hipY = pose.Y;
            Settle(rig, pose, new WeaponInput { Acting = true }, new WeaponBody { Grounded = true, Sprinting = true }, 1.0);
            Assert.Less(pose.Y, hipY - 0.04);
        }

        [Test]
        public void TheWaspsMagazineLeavesAndComesBackDuringAReload()
        {
            var rig = new WeaponRig();
            var pose = new ViewmodelPose();
            var still = new WeaponBody { Grounded = true };
            Settle(rig, pose, new WeaponInput { Acting = true }, still, 1.0);
            Settle(rig, pose, new WeaponInput { Acting = true, FireHeld = true }, still, 0.5);
            rig.Step(Dt, new WeaponInput { Acting = true, ReloadPressed = true }, still);
            Assert.AreEqual(WeaponState.Reload, rig.State);
            bool hidden = false, back = false;
            for (int i = 0; i < 130; i++)
            {
                rig.Step(Dt, new WeaponInput { Acting = true }, still);
                pose.Update(rig, still, Dt, 0, 0, 0, 0);
                if (pose.Parts.TryGetValue("mag", out PartPose m))
                {
                    if (!m.Visible) hidden = true;
                    if (hidden && m.Visible && Math.Abs(m.DY) < 0.01) back = true;
                }
            }
            Assert.IsTrue(hidden, "the old magazine drops out of sight");
            Assert.IsTrue(back, "the new one is seated");
        }

        [Test]
        public void TheEaseAndBumpCurvesAreThePrototypes()
        {
            Assert.AreEqual(0.5, ViewmodelPose.Ease(0.5), 1e-12);
            Assert.AreEqual(0, ViewmodelPose.Seg(0.1, 0.2, 0.4));
            Assert.AreEqual(1, ViewmodelPose.Seg(0.5, 0.2, 0.4));
            Assert.AreEqual(1, ViewmodelPose.BumpOf(0.5, 0, 1), 1e-12);
            Assert.AreEqual(0, ViewmodelPose.BumpOf(1, 0, 1), 1e-12);
        }

        [Test]
        public void ACameraKickDecaysBackToTheAim()
        {
            var kick = new CameraKick();
            kick.Recoil(2.55, 0.5);
            kick.AddShake(0.3);
            double peak = 0;
            for (int i = 0; i < 12; i++)
            {
                kick.Step(Dt);
                peak = Math.Max(peak, kick.Pitch);
            }
            Assert.Greater(peak, 0.02);
            for (int i = 0; i < 180; i++) kick.Step(Dt);
            Assert.AreEqual(0, kick.Pitch, 1e-3);
            Assert.AreEqual(0, kick.Shake);
            kick.AddShake(5);
            Assert.AreEqual(1.2, kick.Shake, "shake is capped");
        }
    }
}
