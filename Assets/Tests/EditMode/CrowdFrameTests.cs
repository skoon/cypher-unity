using System;
using System.Linq;
using System.Runtime.InteropServices;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class CrowdFrameTests
    {
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate());

        /// <summary>A crowd of <paramref name="n"/> people standing still, placed by hand along +X at the given distances from the origin.</summary>
        static CrowdSim StandingAt(params double[] distances)
        {
            CrowdSim sim = CrowdSim.Create(Nav, distances.Length, distances.Length, 0, 0);
            for (int i = 0; i < distances.Length; i++)
            {
                Pedestrian p = sim.Active[i];
                p.X = p.PrevX = distances[i]; p.Z = p.PrevZ = 0; p.Y = p.PrevY = 0.185;
                p.Heading = p.PrevHeading = 0; p.Phase = p.PrevPhase = 1; p.Speed = 0;
            }
            return sim;
        }

        static Frustum Box(double x0, double x1, double z0, double z1)
        {
            var f = new Frustum();
            f.SetPlane(0, 1, 0, 0, -x0);
            f.SetPlane(1, -1, 0, 0, x1);
            f.SetPlane(2, 0, 0, 1, -z0);
            f.SetPlane(3, 0, 0, -1, z1);
            f.SetPlane(4, 0, 1, 0, 100);
            f.SetPlane(5, 0, -1, 0, 100);
            return f;
        }

        static CrowdFrame Build(CrowdSim sim, Frustum view = null, double camX = 0, double camZ = 0, double alpha = 1, double dt = 1.0 / 60, double near = 42, double far = 92)
        {
            var frame = new CrowdFrame();
            frame.Build(sim, view ?? Frustum.Everything(), camX, camZ, alpha, 5.0, dt, near, far);
            return frame;
        }

        // ---------------------------------------------------------------- the GPU record

        [Test]
        public void TheGpuRecordIsAHundredAndTwelveBytesInTheShadersOrder()
        {
            Assert.AreEqual(112, Marshal.SizeOf<PedestrianGpuData>());
            Assert.AreEqual(PedestrianGpuData.Size, Marshal.SizeOf<PedestrianGpuData>());
            Assert.AreEqual(0, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.X)));
            Assert.AreEqual(12, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Heading)));
            Assert.AreEqual(16, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Phase)));
            Assert.AreEqual(28, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Build)));
            Assert.AreEqual(32, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.LookYaw)));
            Assert.AreEqual(40, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.TrimPower)));
            Assert.AreEqual(48, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Coat)));
            Assert.AreEqual(60, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Trim)));
            Assert.AreEqual(64, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Umbrella)));
            Assert.AreEqual(68, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Flags)));
            Assert.AreEqual(44, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Aim)));
            Assert.AreEqual(72, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Weapon)));
            Assert.AreEqual(80, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.AimPitch)));
            Assert.AreEqual(92, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Recoil)));
            Assert.AreEqual(96, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Flinch)));
            Assert.AreEqual(100, (int)Marshal.OffsetOf<PedestrianGpuData>(nameof(PedestrianGpuData.Side)));
        }

        [Test]
        public void TheFlagBitsAreDistinct()
        {
            uint[] flags = { PedestrianGpuData.FlagLongCoat, PedestrianGpuData.FlagUmbrella, PedestrianGpuData.FlagHood, PedestrianGpuData.FlagPack, PedestrianGpuData.FlagTrim, PedestrianGpuData.FlagVisor };
            Assert.AreEqual(6, flags.Distinct().Count());
            foreach (uint f in flags) Assert.IsTrue(f != 0 && (f & (f - 1)) == 0, "exactly one bit");
        }

        [Test]
        public void ARecordCarriesThePersonsLookAndPosition()
        {
            CrowdSim sim = StandingAt(10, 11, 12, 13, 14, 15, 16, 17);
            CrowdFrame frame = Build(sim);
            Assert.AreEqual(8, frame.Total);
            for (int k = 0; k < 8; k++)
            {
                PedestrianGpuData d = frame.Data[k];
                Pedestrian p = sim.Active.Single(q => Math.Abs(q.X - d.X) < 1e-4);
                PedestrianAppearance a = p.Appearance;
                Assert.AreEqual(a.Scale, d.Scale, 1e-6);
                Assert.AreEqual(a.Build, d.Build, 1e-6);
                Assert.AreEqual(a.Coat, d.Coat);
                Assert.AreEqual(a.Skin, d.Skin);
                Assert.AreEqual(a.Pants, d.Pants);
                Assert.AreEqual(a.TrimColour, d.Trim);
                Assert.AreEqual(a.UmbrellaColour, d.Umbrella);
                Assert.AreEqual(a.TrimPower, d.TrimPower, 1e-5);
                Assert.AreEqual(p.LookSeed, d.LookSeed, 1e-4);
                Assert.AreEqual(p.Y, d.Y, 1e-6);
                uint expected = (a.LongCoat ? 1u : 0) | (a.Umbrella ? 2u : 0) | (a.Hood ? 4u : 0) | (a.Pack ? 8u : 0) | (a.Trim ? 16u : 0);
                Assert.AreEqual(expected, d.Flags);
                Assert.AreEqual(0u, d.Weapon, "a civilian is unarmed");
                Assert.AreEqual(0, d.Aim);
                Assert.AreEqual(0, d.Death);
            }
        }

        // ---------------------------------------------------------------- fighters

        static CombatBody Fighter(int id, double x, double z, HeldWeapon weapon = HeldWeapon.Smg)
        {
            var look = new PedestrianAppearance(1.02, 1.1, false, false, true, false, true, 0x2a1014, 0x2a1014, 0x121318, EnemyLook.GangColour, 3, 0);
            return new CombatBody(id, look, true, weapon, 4.5)
            {
                X = x, PrevX = x - 0.5, Z = z, PrevZ = z, Y = 0.185, PrevY = 0.185, Heading = 1, PrevHeading = 1, Phase = 2, PrevPhase = 1.8, Speed = 1.2,
                Aim = 0.8, AimPitch = 0.1, Crouch = 0.3, Recoil = 0.6, Flinch = 0.2, Side = -0.5, Death = 0.25,
            };
        }

        [Test]
        public void FightersAreDrawnInTheCrowdsRunsWithTheirPose()
        {
            CrowdSim sim = StandingAt(10, 60, 100);
            var fighters = new[] { Fighter(7, 20, 0), Fighter(8, 70, 0, HeldWeapon.Revolver), Fighter(9, 150, 0) };
            var frame = new CrowdFrame();
            frame.Build(sim, Frustum.Everything(), 0, 0, 0.5, 5.0, 1.0 / 60, 42, 92, fighters);
            Assert.AreEqual(6, frame.Total);
            Assert.AreEqual(2, frame.Count(RigLod.Full));
            Assert.AreEqual(2, frame.Count(RigLod.Simple));
            Assert.AreEqual(2, frame.Count(RigLod.Silhouette));
            var armed = frame.Data.Take(frame.Total).Where(d => d.Weapon != 0).ToList();
            Assert.AreEqual(3, armed.Count);
            PedestrianGpuData near = frame.Data.Take(frame.Count(RigLod.Full)).Single(d => d.Weapon != 0);
            Assert.AreEqual((uint)HeldWeapon.Smg, near.Weapon);
            Assert.AreEqual(19.75, near.X, 1e-4, "interpolated like everyone");
            Assert.AreEqual(0.8, near.Aim, 1e-6);
            Assert.AreEqual(0.1, near.AimPitch, 1e-6);
            Assert.AreEqual(0.3, near.Crouch, 1e-6);
            Assert.AreEqual(0.6, near.Recoil, 1e-6);
            Assert.AreEqual(0.2, near.Flinch, 1e-6);
            Assert.AreEqual(-0.5, near.Side, 1e-6);
            Assert.AreEqual(0.25, near.Death, 1e-6);
            Assert.AreEqual(1.9, near.Phase, 1e-5);
            Assert.AreEqual(EnemyLook.GangColour, near.Trim);
            Assert.AreEqual(PedestrianGpuData.FlagHood | PedestrianGpuData.FlagTrim | PedestrianGpuData.FlagVisor, near.Flags);
            Assert.AreEqual((uint)HeldWeapon.Revolver, frame.Data.Skip(frame.Start(RigLod.Simple)).Take(frame.Count(RigLod.Simple)).Single(d => d.Weapon != 0).Weapon);
        }

        [Test]
        public void FightersOutOfViewAreLeftOutAndKeepTheirDetailLevelLikeEveryone()
        {
            CrowdSim sim = StandingAt(10);
            CombatBody f = Fighter(3, 40, 0);
            var frame = new CrowdFrame();
            frame.Build(sim, Box(-5, 15, -5, 5), 0, 0, 1, 5.0, 1.0 / 60, 42, 92, new[] { f });
            Assert.AreEqual(1, frame.Total, "the fighter at 40 m is outside the box");
            frame.Build(sim, Frustum.Everything(), 0, 0, 1, 5.0, 1.0 / 60, 42, 92, new[] { f });
            Assert.AreEqual(2, frame.Count(RigLod.Full), "at 40 m, first seen: full, with the civilian at 10 m");
            f.X = f.PrevX = 46;   // past 42 but inside the hysteresis
            frame.Build(sim, Frustum.Everything(), 0, 0, 1, 5.0, 1.0 / 60, 42, 92, new[] { f });
            Assert.AreEqual(2, frame.Count(RigLod.Full), "it stays at full detail within the hysteresis");
        }

        [Test]
        public void TheFrameGrowsToHoldTheFightersAndCiviliansStayUnarmedAfterThem()
        {
            CrowdSim sim = StandingAt(10, 11);
            var frame = new CrowdFrame();
            frame.Build(sim, Frustum.Everything(), 0, 0, 1, 5.0, 1.0 / 60, 42, 92, new[] { Fighter(1, 12, 0), Fighter(2, 13, 0) });
            Assert.AreEqual(4, frame.Total);
            frame.Build(sim, Frustum.Everything(), 0, 0, 1, 5.0, 1.0 / 60, 42, 92);
            Assert.AreEqual(2, frame.Total);
            Assert.IsTrue(frame.Data.Take(2).All(d => d.Weapon == 0 && d.Aim == 0 && d.Death == 0 && (d.Flags & PedestrianGpuData.FlagVisor) == 0), "the records are reused: every field is written");
        }

        // ---------------------------------------------------------------- culling

        [Test]
        public void OnlyPeopleInViewAreIncluded()
        {
            CrowdSim sim = StandingAt(10, 20, 30, 40, 50);
            CrowdFrame frame = Build(sim, Box(15, 45, -5, 5));
            Assert.AreEqual(3, frame.Total);
            CollectionAssert.AreEquivalent(new[] { 20f, 30f, 40f }, frame.Data.Take(3).Select(d => (float)Math.Round(d.X)));
        }

        [Test]
        public void ASphereTouchingTheEdgeCountsAsInView()
        {
            CrowdSim sim = StandingAt(14);   // 1 m outside the box, but the bounding sphere is 3 m
            Assert.AreEqual(1, Build(sim, Box(15, 45, -5, 5)).Total);
            CrowdSim far = StandingAt(10);   // 5 m outside: out
            Assert.AreEqual(0, Build(far, Box(15, 45, -5, 5)).Total);
        }

        [Test]
        public void ThePeopleNotActiveAreNeverDrawn()
        {
            CrowdSim sim = CrowdSim.Create(Nav, 20, 5, 0, 0);
            Assert.AreEqual(5, Build(sim).Total);
        }

        [Test]
        public void AnEmptyCrowdMakesAnEmptyFrame()
        {
            CrowdSim sim = CrowdSim.Create(Nav, 5, 0, 0, 0);
            CrowdFrame frame = Build(sim);
            Assert.AreEqual(0, frame.Total);
            Assert.AreEqual(0, frame.Count(RigLod.Full) + frame.Count(RigLod.Simple) + frame.Count(RigLod.Silhouette));
        }

        // ---------------------------------------------------------------- levels of detail

        [Test]
        public void PeopleAreSortedNearMiddleFarWithTheirRunsInOrder()
        {
            CrowdSim sim = StandingAt(100, 10, 60, 20, 95, 30, 50);
            CrowdFrame frame = Build(sim);
            Assert.AreEqual(3, frame.Count(RigLod.Full), "10, 20, 30");
            Assert.AreEqual(2, frame.Count(RigLod.Simple), "60, 50");
            Assert.AreEqual(2, frame.Count(RigLod.Silhouette), "100, 95");
            Assert.AreEqual(0, frame.Start(RigLod.Full));
            Assert.AreEqual(3, frame.Start(RigLod.Simple));
            Assert.AreEqual(5, frame.Start(RigLod.Silhouette));
            for (int i = 0; i < 3; i++) Assert.Less(frame.Data[i].X, 40);
            for (int i = 3; i < 5; i++) Assert.That(frame.Data[i].X, Is.InRange(42f, 92f));
            for (int i = 5; i < 7; i++) Assert.Greater(frame.Data[i].X, 92f);
        }

        [Test]
        public void WithinARunPeopleKeepTheirOrder()
        {
            CrowdSim sim = StandingAt(30, 10, 20);
            CrowdFrame frame = Build(sim);
            CollectionAssert.AreEqual(new[] { 30f, 10f, 20f }, frame.Data.Take(3).Select(d => (float)Math.Round(d.X)));
        }

        [Test]
        public void TheThresholdsAreTheTiersOwn()
        {
            CrowdSim sim = StandingAt(30);
            Assert.AreEqual(1, Build(sim, near: 25, far: 55).Count(RigLod.Simple), "30 m is the middle distance on Performance");
            Assert.AreEqual(1, Build(sim, near: 42, far: 92).Count(RigLod.Full), "and near on the others");
        }

        [TestCase(0, 10.0, 0)]
        [TestCase(0, 47.9, 0)]
        [TestCase(0, 48.1, 1)]
        [TestCase(1, 40.0, 1)]
        [TestCase(1, 35.9, 0)]
        [TestCase(1, 90.0, 1)]
        [TestCase(1, 97.9, 1)]
        [TestCase(1, 98.1, 2)]
        [TestCase(2, 120.0, 2)]
        [TestCase(2, 88.1, 2)]
        [TestCase(2, 85.9, 1)]
        public void DistanceLodHasHysteresisAboutBothThresholds(int previous, double distance, int expected)
        {
            Assert.AreEqual(expected, CrowdFrame.DistanceLod(distance, 42, 92, previous, 6));
        }

        [Test]
        public void SomeoneOnAThresholdDoesNotFlickerWhenTheyJiggle()
        {
            CrowdSim sim = StandingAt(41);
            var frame = new CrowdFrame();
            var seen = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 20; i++)
            {
                sim.Active[0].X = sim.Active[0].PrevX = 42 + (i % 2 == 0 ? -1.5 : 1.5);   // 40.5 and 43.5, either side of the line
                frame.Build(sim, Frustum.Everything(), 0, 0, 1, 5.0, 1.0 / 60, 42, 92);
                seen.Add(frame.Count(RigLod.Full) == 1 ? 0 : 1);
            }
            Assert.AreEqual(1, seen.Distinct().Count(), "one level the whole time");
        }

        [Test]
        public void ADepartingPersonSwitchesOnceAndComingBackSwitchesBackLaterThanThat()
        {
            CrowdSim sim = StandingAt(30);
            var frame = new CrowdFrame();
            int Lod(double d)
            {
                sim.Active[0].X = sim.Active[0].PrevX = d;
                frame.Build(sim, Frustum.Everything(), 0, 0, 1, 5.0, 1.0 / 60, 42, 92);
                return frame.Count(RigLod.Full) == 1 ? 0 : frame.Count(RigLod.Simple) == 1 ? 1 : 2;
            }
            Assert.AreEqual(0, Lod(30));
            Assert.AreEqual(0, Lod(46), "still near: inside the margin");
            Assert.AreEqual(1, Lod(50));
            Assert.AreEqual(1, Lod(45), "staying simple until clearly back");
            Assert.AreEqual(0, Lod(35));
        }

        // ---------------------------------------------------------------- interpolation

        [Test]
        public void PositionHeadingAndPhaseAreInterpolatedBetweenSteps()
        {
            CrowdSim sim = StandingAt(20);
            Pedestrian p = sim.Active[0];
            p.PrevX = 20; p.X = 21; p.PrevZ = 0; p.Z = 2; p.PrevY = 0.2; p.Y = 0.4;
            p.PrevHeading = 3.0; p.Heading = -3.0;   // a short way round through pi, not a long way back
            p.PrevPhase = 1; p.Phase = 2;
            PedestrianGpuData d = Build(sim, alpha: 0.5).Data[0];
            Assert.AreEqual(20.5, d.X, 1e-5);
            Assert.AreEqual(1.0, d.Z, 1e-5);
            Assert.AreEqual(0.3, d.Y, 1e-5);
            Assert.AreEqual(3.0 + 0.5 * (2 * Math.PI - 6.0), d.Heading, 1e-5, "the short way round");
            Assert.AreEqual(1.5, d.Phase, 1e-5);
        }

        [Test]
        public void AlphaZeroIsTheLastStepAndOneIsThisOne()
        {
            CrowdSim sim = StandingAt(20);
            Pedestrian p = sim.Active[0];
            p.PrevX = 20; p.X = 22;
            Assert.AreEqual(20.0, Build(sim, alpha: 0).Data[0].X, 1e-5);
            Assert.AreEqual(22.0, Build(sim, alpha: 1).Data[0].X, 1e-5);
        }

        // ---------------------------------------------------------------- the head

        [Test]
        public void ANearPersonTurnsTheirHeadTowardTheCameraAndALoneRunnerDoesNot()
        {
            CrowdSim sim = StandingAt(5, 60);
            Pedestrian near = sim.Active[0], far = sim.Active[1];
            near.Heading = near.PrevHeading = Math.PI / 2 + 0.3;   // walking toward +X, roughly: the camera at the origin is behind-ish... face it instead
            near.Heading = near.PrevHeading = -Math.PI / 2;          // walking toward -X, i.e. toward the camera at the origin
            near.LookYaw = 0.5; far.LookYaw = 0.5;
            var frame = new CrowdFrame();
            for (int i = 0; i < 120; i++) frame.Build(sim, Frustum.Everything(), 0, 0, 1, 5.0, 1.0 / 60, 42, 92);
            double want = PedestrianPose.LookTarget(near.Heading, 5, 0, true, 0, 0, 5.0, near.LookSeed);
            Assert.AreEqual(want, near.LookYaw, 0.01, "the head has settled on the target");
            Assert.AreEqual(0.5, far.LookYaw, 1e-12, "a far person's head is not simulated");
        }

        [Test]
        public void TheHeadEasesAFixedShareEachFrameAtSixtyAFrame()
        {
            CrowdSim sim = StandingAt(5);
            Pedestrian p = sim.Active[0];
            p.LookYaw = 0;
            double target = PedestrianPose.LookTarget(p.Heading, 5, 0, true, 0, 0, 5.0, p.LookSeed);
            Build(sim, dt: 1.0 / 60);
            Assert.AreEqual(target * 0.12, p.LookYaw, 1e-9);
        }

        [Test]
        public void ASlowFrameEasesTheHeadFurtherSoItIsFrameRateIndependent()
        {
            CrowdSim a = StandingAt(5), b = StandingAt(5);
            a.Active[0].LookYaw = b.Active[0].LookYaw = 0;
            Build(a, dt: 2.0 / 60);
            Build(b, dt: 1.0 / 60);
            Build(b, dt: 1.0 / 60);
            Assert.AreEqual(b.Active[0].LookYaw, a.Active[0].LookYaw, 1e-9, "one slow frame equals two fast ones");
        }

        // ---------------------------------------------------------------- the look function, and the frustum

        [Test]
        public void TheHeadWandersSlowlyWhenNobodyIsNear()
        {
            double a = PedestrianPose.LookTarget(0, 0, 0, false, 0, 0, 0, 1);
            double b = PedestrianPose.LookTarget(0, 0, 0, false, 0, 0, 3, 1);
            Assert.AreNotEqual(a, b);
            Assert.LessOrEqual(Math.Abs(a), 0.22);
            Assert.LessOrEqual(Math.Abs(b), 0.22);
        }

        [Test]
        public void ThePersonGlancesAtTheCameraOnlyWhenItIsInFrontAndClose()
        {
            double wander = PedestrianPose.LookTarget(0, 0, 0, true, 0, 50, 0, 1);   // 50 m away: wander
            Assert.AreEqual(Math.Sin(0 * 0.42 + 1 * 3.1) * 0.22, wander, 1e-12);
            Assert.AreEqual(0.7 * Math.Atan2(1, 2), PedestrianPose.LookTarget(0, 0, 0, true, 1, 2, 0, 1), 1e-12, "close and in front: 70% of the way to look at it");
            Assert.AreEqual(Math.Sin(3.1) * 0.22, PedestrianPose.LookTarget(0, 0, 0, true, 0, -3, 0, 1), 1e-12, "behind: the head cannot turn that far, so it wanders");
        }

        [Test]
        public void TheFrustumTestsSpheresAgainstSixPlanes()
        {
            Frustum f = Box(0, 10, 0, 10);
            Assert.IsTrue(f.IntersectsSphere(5, 0, 5, 0.1));
            Assert.IsTrue(f.IntersectsSphere(-0.5, 0, 5, 1), "poking in from outside");
            Assert.IsFalse(f.IntersectsSphere(-2, 0, 5, 1));
            Assert.IsFalse(f.IntersectsSphere(5, 0, 12, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.SetPlane(6, 0, 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.SetPlane(-1, 0, 1, 0, 0));
            Assert.IsTrue(Frustum.Everything().IntersectsSphere(1e5, 50, -1e5, 1));
        }

        // ---------------------------------------------------------------- errors

        [Test]
        public void BadArgumentsFailLoudly()
        {
            CrowdSim sim = StandingAt(10);
            var frame = new CrowdFrame();
            Assert.Throws<ArgumentNullException>(() => frame.Build(null, Frustum.Everything(), 0, 0, 1, 0, 0.01, 42, 92));
            Assert.Throws<ArgumentNullException>(() => frame.Build(sim, null, 0, 0, 1, 0, 0.01, 42, 92));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.Build(sim, Frustum.Everything(), 0, 0, 1.5, 0, 0.01, 42, 92));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.Build(sim, Frustum.Everything(), 0, 0, -0.1, 0, 0.01, 42, 92));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.Build(sim, Frustum.Everything(), 0, 0, 1, 0, 0.01, 0, 92));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.Build(sim, Frustum.Everything(), 0, 0, 1, 0, 0.01, 50, 50));
            CrowdFrame built = Build(sim);
            Assert.Throws<ArgumentOutOfRangeException>(() => built.Count((RigLod)5));
            Assert.Throws<ArgumentOutOfRangeException>(() => built.Start((RigLod)5));
        }
    }
}
