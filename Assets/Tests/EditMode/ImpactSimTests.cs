using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The gunfire effects (the prototype's <c>combat/Impacts.js</c>): the surface choice, the composite impact's counts, the pools recycling at their caps, and each pool's motion.</summary>
    public class ImpactSimTests
    {
        const double Dt = 1.0 / 60;
        static readonly V3 Up = new V3(0, 1, 0);

        [Test]
        public void TheSurfaceFollowsWhatWasHitAndHowTheRailShoots()
        {
            Assert.AreEqual(ImpactSurface.Flesh, ImpactSim.SurfaceFor(ShotTarget.Person, true, 0, 1));
            Assert.AreEqual(ImpactSurface.Energy, ImpactSim.SurfaceFor(ShotTarget.World, true, 1, 0));
            Assert.AreEqual(ImpactSurface.Metal, ImpactSim.SurfaceFor(ShotTarget.Prop, false, 0, 1));
            Assert.AreEqual(ImpactSurface.Metal, ImpactSim.SurfaceFor(ShotTarget.Vehicle, false, 0, 1));
            Assert.AreEqual(ImpactSurface.Asphalt, ImpactSim.SurfaceFor(ShotTarget.World, false, 0.9, 0.1));
            Assert.AreEqual(ImpactSurface.Concrete, ImpactSim.SurfaceFor(ShotTarget.World, false, 0.9, 3.0), "a roof is concrete");
            Assert.AreEqual(ImpactSurface.Concrete, ImpactSim.SurfaceFor(ShotTarget.World, false, 0, 1), "a wall is concrete");
        }

        [Test]
        public void AnImpactSpawnsThePrototypesCounts()
        {
            var sim = new ImpactSim();
            sim.Impact(new V3(0, 1, 0), new V3(0, 0, 1), ImpactSurface.Metal, 0.5, 0);
            var live = sim.Live();
            Assert.AreEqual((int)Math.Round(16 * 1.05, MidpointRounding.AwayFromZero), live.Sparks);
            Assert.AreEqual((int)Math.Round(2 * 0.9, MidpointRounding.AwayFromZero), live.Debris);
            Assert.AreEqual(1, live.Smoke);
            Assert.AreEqual(1, live.Decals);
            var decal = sim.Decals.First(d => d.Live);
            Assert.AreEqual(1.009, decal.Pos.Z + 1, 1e-9, "set off the wall by 9 mm");
            Assert.AreEqual(0.10 * (0.8 + 0.5 * 0.55), decal.Size, 1e-12);

            sim.Impact(new V3(0, 1, 0), new V3(0, 0, 1), ImpactSurface.Flesh, 0.5, 0);
            Assert.AreEqual(1, sim.Live().Decals, "people take no decal");
        }

        [Test]
        public void ClearingPutsEverythingOutAndStartsThePoolsAgain()
        {
            var sim = new ImpactSim();
            sim.Impact(new V3(0, 1, 0), new V3(0, 0, 1), ImpactSurface.Metal, 0.5, 0);
            sim.SpawnTracer(new V3(0, 1, 0), new V3(0, 1, -50), 0xffffff, 0.01, 0.1, 500);
            sim.EjectShell(new V3(0, 1, 0), new V3(1, 1, 0), 1, 0);
            sim.Flash(new V3(0, 1, 0), 0xffffff, 20, 0.05, 8);
            sim.Clear();
            Assert.AreEqual((0, 0, 0, 0, 0, 0, 0), sim.Live());
            sim.AddDecal(new V3(0, 1, 0), new V3(0, 0, 1), 0.1, 0xffffff);
            Assert.IsTrue(sim.Decals[0].Live, "the pool starts from its first slot again");
        }

        [Test]
        public void PoolsRecycleTheOldestAtTheirCaps()
        {
            var sim = new ImpactSim();
            for (int i = 0; i < 300; i++) sim.Impact(new V3(i, 1, 0), Up, ImpactSurface.Metal, 1.0, 0);
            var live = sim.Live();
            Assert.AreEqual(ImpactSim.MaxDecals, live.Decals);
            Assert.AreEqual(ImpactSim.MaxSparks, live.Sparks);
            Assert.LessOrEqual(live.Debris, ImpactSim.MaxDebris);
            Assert.IsTrue(sim.Decals.Any(d => d.Pos.X > 290), "the newest replaced the oldest");
            for (int i = 0; i < 20; i++) sim.SpawnTracer(new V3(0, 0, 0), new V3(0, 0, 10), 0xffffff, 0.01, 0.1, 0);
            Assert.AreEqual(ImpactSim.MaxTracers, sim.Live().Tracers);
        }

        [Test]
        public void SparksFallAndFadeSmokeRisesAndGrowsDebrisBouncesOnTheGround()
        {
            var sim = new ImpactSim();
            sim.SpawnSpark(new V3(0, 2, 0), new V3(1, 0, 0), 0xffffff, 0.02, 0.5);
            sim.SpawnSmoke(new V3(0, 2, 0), new V3(0, 0, 0), 0x888888, 0.1, 1.0, 0.3);
            sim.SpawnDebris(new V3(0, 0.5, 0), new V3(0, 0, 0), 0x888888, 0.02, 2.0, 0.25);
            for (int i = 0; i < 12; i++) sim.Update(Dt);
            Assert.Less(sim.Sparks[0].Pos.Y, 2, "sparks fall");
            Assert.Less(sim.Sparks[0].Alpha, 1);
            Assert.Greater(sim.SmokePuffs[0].Pos.Y, 2, "smoke rises");
            Assert.Greater(sim.SmokePuffs[0].DrawSize, 0.1, "and spreads");
            for (int i = 0; i < 60; i++) sim.Update(Dt);
            Assert.GreaterOrEqual(sim.Chunks[0].Pos.Y, 0.25 - 1e-9, "debris lands on the ground it was thrown over");
            Assert.AreEqual(0, sim.Sparks[0].Alpha, "the spark is out");
        }

        [Test]
        public void ATracerRunsFromTheMuzzleAtItsSpeedAndAnInstantBeamFillsAtOnce()
        {
            var sim = new ImpactSim();
            sim.SpawnTracer(new V3(0, 0, 0), new V3(0, 0, 100), 0xffffff, 0.01, 0.2, 520);
            sim.SpawnTracer(new V3(0, 0, 0), new V3(0, 0, 100), 0xffffff, 0.04, 0.2, 0);
            sim.Update(0.05);
            Assert.AreEqual(26, sim.Tracers[0].Head.Z, 1e-9, "520 m/s for 0.05 s");
            Assert.AreEqual(26 - 520 * 0.045, sim.Tracers[0].Tail.Z, 1e-9);
            Assert.AreEqual(100, sim.Tracers[1].Head.Z, 1e-9);
            Assert.AreEqual(0, sim.Tracers[1].Tail.Z, 1e-9);
        }

        [Test]
        public void AShellArcsBouncesComesToRestAndGoes()
        {
            var sim = new ImpactSim();
            sim.EjectShell(new V3(0, 1.4, 0), new V3(2, 2, 0), 1, 0);
            double top = 0;
            for (int i = 0; i < 60; i++)
            {
                sim.Update(Dt);
                top = Math.Max(top, sim.Shells[0].Pos.Y);
            }
            Assert.Greater(top, 1.4, "thrown up first");
            for (int i = 0; i < 120; i++) sim.Update(Dt);
            Assert.IsTrue(sim.Shells[0].Resting);
            Assert.AreEqual(0.0052, sim.Shells[0].Pos.Y, 1e-9, "lying on the ground");
            for (int i = 0; i < 300; i++) sim.Update(Dt);
            Assert.AreEqual(0, sim.Live().Shells);
        }

        [Test]
        public void AMuzzleLightFadesAsTheSquareOfItsLife()
        {
            var sim = new ImpactSim();
            sim.Flash(new V3(0, 1, 0), 0xffc26a, 26, 0.1, 12);
            sim.Update(0.05);
            Assert.AreEqual(26 * 0.25, sim.Lights[0].Intensity, 1e-9);
            sim.Update(0.06);
            Assert.AreEqual(0, sim.Lights[0].Intensity);
        }

        [Test]
        public void TheSameSeedMakesTheSameEffects()
        {
            string Run(uint seed)
            {
                var sim = new ImpactSim(seed);
                sim.Impact(new V3(0, 1, 0), new V3(0, 0, 1), ImpactSurface.Concrete, 0.7, 0);
                sim.Update(Dt);
                return string.Join(";", sim.Sparks.Where(p => p.Life > 0).Select(p => $"{p.Pos.X:R},{p.Pos.Y:R}"));
            }
            Assert.AreEqual(Run(3), Run(3));
            Assert.AreNotEqual(Run(3), Run(4));
        }
    }
}
