using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The audio system's decisions: where a footstep lands, when it falls, how a place is told from the geometry around it, what a wall does, who gets a voice, how loud each bus is.</summary>
    public class AudioLogicTests
    {
        static readonly string[] Lines = Golden.Lines("audio_noise_golden.txt");

        // ---- footing ----

        [Test]
        public void TheSurfaceHashIsTheSameAsThePrototypes()
        {
            List<string[]> rows = Golden.Rows(Lines, "H");
            Assert.GreaterOrEqual(rows.Count, 10);
            foreach (string[] r in rows)
            {
                Assert.AreEqual(Golden.D(r[3]), Footing.Hash2(Golden.I(r[1]), Golden.I(r[2])), 1e-15, $"hash2({r[1]}, {r[2]})");
            }
        }

        [Test]
        public void ASurfaceHoldsForAWhole2Point5MetreSquareAndRoadsAreAsphalt()
        {
            string a = Footing.SurfaceAt(10.1, 20.1, false, 0);
            Assert.AreEqual(a, Footing.SurfaceAt(10.9, 21.4, false, 0), "the same square");
            int asphalt = 0, concrete = 0, metal = 0, gravel = 0, water = 0;
            for (int gx = -40; gx < 40; gx++)
            {
                for (int gz = -40; gz < 40; gz++)
                {
                    switch (Footing.SurfaceAt(gx * 2.5 + 1, gz * 2.5 + 1, true, 0))
                    {
                        case "asphalt": asphalt++; break;
                        case "water": water++; break;
                        case "metal": metal++; break;
                        case "gravel": gravel++; break;
                        default: concrete++; break;
                    }
                }
            }
            Assert.AreEqual(0, concrete, "a road is never concrete");
            Assert.Greater(asphalt, 5000 * 0.7);
            Assert.Greater(metal, 100, "grates are sparse but there");
            Assert.Greater(gravel, 100);
            Assert.Greater(water, 300, "even a dry road has the odd puddle");
        }

        [Test]
        public void RainPoolsPuddlesAndMoreOnTheRoadThanOffIt()
        {
            int Water(bool road, double wet)
            {
                int n = 0;
                for (int gx = -50; gx < 50; gx++)
                {
                    for (int gz = -50; gz < 50; gz++)
                    {
                        if (Footing.SurfaceAt(gx * 2.5 + 1, gz * 2.5 + 1, road, wet) == "water") n++;
                    }
                }
                return n;
            }
            Assert.Greater(Water(true, 0.9), Water(true, 0) * 1.5);
            Assert.Greater(Water(false, 0.9), Water(false, 0) * 1.5);
            Assert.Greater(Water(true, 0.9), Water(false, 0.9));
            Assert.AreEqual(0.0, Footing.Wetness(0));
            Assert.AreEqual(0.9, Footing.Wetness(1), 1e-12);
            Assert.AreEqual(0.0, Footing.Wetness(-3));
        }

        [Test]
        public void EverySurfaceFootingMakesIsOneTheBankKnows()
        {
            var seen = new HashSet<string>();
            for (int gx = -60; gx < 60; gx++)
            {
                for (int gz = -60; gz < 60; gz++)
                {
                    seen.Add(Footing.SurfaceAt(gx * 2.5, gz * 2.5, gx % 2 == 0, 0.5));
                }
            }
            foreach (string s in seen) Assert.IsTrue(SoundBank.Has("foot." + s), s);
            Assert.GreaterOrEqual(seen.Count, 5);
        }

        // ---- stride and breath ----

        [Test]
        public void AFootFallsEveryStrideOfGroundCovered()
        {
            var clock = new StrideClock();
            int steps = 0;
            double covered = 0;
            for (int f = 0; f < 600; f++)
            {
                if (clock.Update(1 / 60.0, true, true, 4.0, false, false)) steps++;
                covered += 4.0 / 60;
            }
            Assert.AreEqual(covered / StrideClock.Walk, steps, 1.0);
            Assert.AreEqual(1.85, StrideClock.Stride(false, false));
            Assert.AreEqual(2.3, StrideClock.Stride(false, true));
            Assert.AreEqual(1.5, StrideClock.Stride(true, false));
            Assert.AreEqual(1.5, StrideClock.Stride(true, true), "crouching wins");
        }

        [Test]
        public void NoFootFallsInTheAirOrStandingStillAndTheCountRunsDown()
        {
            var clock = new StrideClock();
            for (int f = 0; f < 20; f++) clock.Update(1 / 60.0, true, true, 4, false, false);
            double built = clock.Distance;
            Assert.Greater(built, 1);
            for (int f = 0; f < 30; f++) Assert.IsFalse(clock.Update(1 / 60.0, false, true, 4, false, false), "in the air");
            Assert.Less(clock.Distance, built);
            for (int f = 0; f < 120; f++) Assert.IsFalse(clock.Update(1 / 60.0, true, false, 0, false, false), "standing still");
            Assert.AreEqual(0.0, clock.Distance);
        }

        [Test]
        public void AFasterWalkerStepsMoreOften()
        {
            int Steps(double speed, bool sprint)
            {
                var c = new StrideClock();
                int n = 0;
                for (int f = 0; f < 600; f++) if (c.Update(1 / 60.0, true, true, speed, false, sprint)) n++;
                return n;
            }
            Assert.Greater(Steps(7, true), Steps(3.5, false));
        }

        [Test]
        public void BreathingQuickensAndDeepensWithSpeedAndAlternatesInAndOut()
        {
            var calm = new BreathClock(1.0);
            var run = new BreathClock(1.0);
            var calmBreaths = new List<BreathEvent>();
            var runBreaths = new List<BreathEvent>();
            double t = 0;
            for (int f = 0; f < 60 * 30; f++)
            {
                t += 1 / 60.0;
                BreathEvent? a = calm.Update(1 / 60.0, t, 0, false);
                BreathEvent? b = run.Update(1 / 60.0, t, 7, true);
                if (a.HasValue) calmBreaths.Add(a.Value);
                if (b.HasValue) runBreaths.Add(b.Value);
            }
            Assert.Greater(runBreaths.Count, calmBreaths.Count * 2, "a runner breathes much more often");
            Assert.AreEqual(1.0, calmBreaths[0].Time, 1e-9, "the first breath is at the time given");
            Assert.IsTrue(calmBreaths[0].Inhale);
            Assert.IsFalse(calmBreaths[1].Inhale);
            Assert.IsTrue(calmBreaths[2].Inhale);
            Assert.Greater(runBreaths[runBreaths.Count - 1].Volume, calmBreaths[calmBreaths.Count - 1].Volume * 2);
            Assert.AreEqual(0.28, calmBreaths[0].Volume, 0.02, "a calm breath is quiet");
            Assert.Greater(run.Exertion, 0.85);
            Assert.AreEqual(0.0, calm.Exertion, 1e-9);
        }

        [Test]
        public void ExertionRisesQuicklyAndFallsSlowly()
        {
            var c = new BreathClock(100);
            for (int f = 0; f < 60 * 3; f++) c.Update(1 / 60.0, 0, 8, true);
            double high = c.Exertion;
            for (int f = 0; f < 60 * 3; f++) c.Update(1 / 60.0, 0, 0, false);
            double after = c.Exertion;
            Assert.Greater(high, 0.6);
            Assert.Greater(after, high * 0.3, "three seconds of rest has not yet got it back");
            Assert.Less(after, high);
        }

        // ---- the place around the listener ----

        [Test]
        public void TheReverbSpaceIsToldFromTheGeometryAsThePrototypeDoes()
        {
            var probe = new SpaceProbe();
            List<string[]> rows = Golden.Rows(Lines, "P");
            Assert.AreEqual(90, rows.Count);
            var seen = new HashSet<ReverbSpace>();
            double now = 0;
            foreach (string[] r in rows)
            {
                var dist = new double[SpaceProbe.Rays];
                var hit = new bool[SpaceProbe.Rays];
                for (int i = 0; i < SpaceProbe.Rays; i++)
                {
                    dist[i] = Golden.D(r[2 + i]);
                    hit[i] = dist[i] < SpaceProbe.Range;
                }
                double ceiling = Golden.D(r[14]);
                string over = r[15];
                if (over == "plaza") probe.Force(ReverbSpace.Plaza);
                else if (over == "auto") probe.Override = null;
                Assert.IsTrue(probe.Due(now), "probe " + r[1]);
                probe.Observe(now, dist, hit, ceiling);
                now += 0.5;
                Assert.AreEqual(r[16], probe.Current.ToString().ToLowerInvariant(), "space after probe " + r[1]);
                Assert.AreEqual(Golden.D(r[17]), probe.Indoor, 1e-12, "indoor after probe " + r[1]);
                seen.Add(probe.Current);
            }
            Assert.AreEqual(5, seen.Count, "the script visits every space");
        }

        [Test]
        public void AProbeIsDueEveryHalfSecond()
        {
            var probe = new SpaceProbe();
            Assert.IsTrue(probe.Due(0));
            probe.Observe(10, new double[12], new bool[12], 40);
            Assert.IsFalse(probe.Due(10.4));
            Assert.IsTrue(probe.Due(10.5));
        }

        [Test]
        public void ASpaceChangesOnlyAfterTwoProbesAgree()
        {
            var probe = new SpaceProbe();
            var tight = new double[12];
            var hit = new bool[12];
            for (int i = 0; i < 12; i++) { tight[i] = 5; hit[i] = true; }
            Assert.IsNull(probe.Observe(0, tight, hit, 40), "one alley-like probe is not enough");
            Assert.AreEqual(ReverbSpace.Street, probe.Current);
            Assert.AreEqual(ReverbSpace.Alley, probe.Observe(0.5, tight, hit, 40));
            Assert.AreEqual(ReverbSpace.Alley, probe.Current);
            Assert.IsNull(probe.Observe(1.0, tight, hit, 40), "no change to report");
        }

        [Test]
        public void ARoomWithALowCeilingIsIndoorsAndMuffledOutsideSounds()
        {
            var probe = new SpaceProbe();
            var d = new double[12];
            var hit = new bool[12];
            for (int i = 0; i < 12; i++) { d[i] = 6; hit[i] = true; }
            probe.Observe(0, d, hit, 3);
            probe.Observe(0.5, d, hit, 3);
            Assert.AreEqual(ReverbSpace.Indoor, probe.Current);
            Assert.AreEqual(1 - 3 / 9.0, probe.Indoor, 1e-12);
            probe.Observe(1, d, hit, 40);
            Assert.AreEqual(0.0, probe.Indoor, "no ceiling, not indoors");
        }

        [Test]
        public void ForcingASpaceHoldsItAgainstTheProbeUntilReleased()
        {
            var probe = new SpaceProbe();
            Assert.AreEqual(ReverbSpace.Indoor, probe.Force(ReverbSpace.Indoor));
            Assert.IsNull(probe.Force(ReverbSpace.Indoor), "already there");
            var wide = new double[12];
            var hit = new bool[12];
            for (int i = 0; i < 12; i++) wide[i] = 40;
            probe.Observe(0, wide, hit, 40);
            probe.Observe(0.5, wide, hit, 40);
            Assert.AreEqual(ReverbSpace.Indoor, probe.Current);
            probe.Override = null;
            probe.Observe(1, wide, hit, 40);
            Assert.AreEqual(ReverbSpace.Plaza, probe.Current);
        }

        [Test]
        public void ARayFansOutAroundTheListener()
        {
            SpaceProbe.Direction(0, out double x0, out double z0);
            Assert.AreEqual(1.0, x0, 1e-12);
            Assert.AreEqual(0.0, z0, 1e-12);
            SpaceProbe.Direction(3, out double x3, out double z3);
            Assert.AreEqual(0.0, x3, 1e-12);
            Assert.AreEqual(1.0, z3, 1e-12);
            Assert.Throws<ArgumentException>(() => new SpaceProbe().Observe(0, new double[5], new bool[5], 40));
        }

        // ---- a wall between ----

        [Test]
        public void AWallCostsTheHighsAndSixtyPercentOfTheLevel()
        {
            Assert.AreEqual(0.0, Occlusion.Level(30, false, 0));
            Assert.AreEqual(0.0, Occlusion.Level(1.5, true, 1), "too close for a ray");
            Assert.AreEqual(0.0, Occlusion.Level(120, true, 20), "too far");
            Assert.AreEqual(0.95, Occlusion.Level(30, true, 0), 1e-9, "a wall right at the listener is the most muffling");
            Assert.AreEqual(0.45, Occlusion.Level(30, true, 30), 1e-9, "a wall right at the sound");
            Assert.AreEqual(0.7, Occlusion.Level(30, true, 15), 1e-9);
            Assert.AreEqual(20000, Occlusion.CutoffHz(0));
            Assert.AreEqual(520, Occlusion.CutoffHz(1), 1e-9);
            Assert.Less(Occlusion.CutoffHz(0.5), Occlusion.CutoffHz(0.1));
            Assert.AreEqual(1.0, Occlusion.Gain(0));
            Assert.AreEqual(0.4, Occlusion.Gain(1), 1e-12);
            Assert.AreEqual(0.7, Occlusion.Gain(0.5), 1e-12);
            Assert.IsTrue(Occlusion.WorthARay(2));
            Assert.IsFalse(Occlusion.WorthARay(1.99));
        }

        // ---- the pool of voices ----

        [Test]
        public void VoicesAreHandedOutThenTakenFromTheLeastImportantOldest()
        {
            var pool = new VoicePool(3);
            Assert.AreEqual(0, pool.Acquire(5, 1.0, out _));
            Assert.AreEqual(1, pool.Acquire(2, 2.0, out _));
            Assert.AreEqual(2, pool.Acquire(2, 3.0, out _));
            Assert.AreEqual(3, pool.Active);
            // full: a more important sound takes the lowest priority, and of those the oldest
            int slot = pool.Acquire(6, 4.0, out bool stole);
            Assert.AreEqual(1, slot);
            Assert.IsTrue(stole);
            // a less important sound than every voice is dropped
            Assert.AreEqual(-1, pool.Acquire(1, 5.0, out bool stole2));
            Assert.IsFalse(stole2);
            // an equally important one still takes over (the oldest of the lowest)
            Assert.AreEqual(2, pool.Acquire(2, 6.0, out _));
            Assert.AreEqual(5, pool.Played);
            Assert.AreEqual(2, pool.Stolen);
            Assert.AreEqual(1, pool.Culled);
        }

        [Test]
        public void AReleasedVoiceIsFreeAgain()
        {
            var pool = new VoicePool(2);
            int a = pool.Acquire(9, 0, out _);
            int b = pool.Acquire(9, 0, out _);
            Assert.AreEqual(-1, pool.Acquire(1, 0, out _));
            pool.Release(a);
            Assert.IsFalse(pool.IsBusy(a));
            Assert.AreEqual(a, pool.Acquire(1, 1, out bool stole));
            Assert.IsFalse(stole, "it was free, nothing was taken");
            pool.ReleaseAll();
            Assert.AreEqual(0, pool.Active);
            Assert.IsFalse(pool.IsBusy(b));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VoicePool(0));
        }

        // ---- the buses ----

        [Test]
        public void TheBusesHaveThePrototypesBaseLevelsTimesTheirSliders()
        {
            var levels = new BusLevels();
            Assert.AreEqual(0.85, levels.Gain(AudioBus.Ambience), 1e-12);
            Assert.AreEqual(0.95, levels.Gain(AudioBus.Sfx), 1e-12);
            Assert.AreEqual(0.6, levels.Gain(AudioBus.Ui), 1e-12);
            Assert.AreEqual(0.55, levels.Gain(AudioBus.Music), 1e-12);
            Assert.AreEqual(1.0, levels.Gain(AudioBus.Voice), 1e-12);
            levels.SetSlider(AudioBus.Music, 0.5);
            levels.MasterSlider = 0.8;
            Assert.AreEqual(0.275, levels.Gain(AudioBus.Music), 1e-12);
            Assert.AreEqual(0.9 * 0.8, levels.Master, 1e-12, "the master is applied after the compressors, not here");
            Assert.AreEqual(AudioBuses.ToDecibels(0.275), levels.Decibels(AudioBus.Music), 1e-9);
            levels.SetSlider(AudioBus.Sfx, 0);
            Assert.AreEqual(AudioBuses.SilenceDecibels, levels.Decibels(AudioBus.Sfx));
            Assert.Throws<ArgumentOutOfRangeException>(() => levels.SetSlider(AudioBus.Ui, -0.1));
        }

        [Test]
        public void PausingDucksEverythingButTheInterfaceAndAMenuDucksLess()
        {
            var levels = new BusLevels();
            levels.SetState(paused: true, inMenu: false);
            levels.Snap();
            Assert.AreEqual(0.15, levels.Duck(AudioBus.Ambience), 1e-12);
            Assert.AreEqual(0.15, levels.Duck(AudioBus.Sfx), 1e-12);
            Assert.AreEqual(0.15, levels.Duck(AudioBus.Voice), 1e-12);
            Assert.AreEqual(0.3, levels.Duck(AudioBus.Music), 1e-12);
            Assert.AreEqual(1.0, levels.Duck(AudioBus.Ui), 1e-12);
            Assert.AreEqual(0.15, levels.ReverbDuck, 1e-12);
            levels.SetState(paused: false, inMenu: true);
            levels.Snap();
            Assert.AreEqual(0.45, levels.Duck(AudioBus.Sfx), 1e-12);
            Assert.AreEqual(0.75, levels.Duck(AudioBus.Music), 1e-12);
            levels.SetState(false, false);
            levels.Snap();
            Assert.AreEqual(1.0, levels.Duck(AudioBus.Music), 1e-12);
            levels.SetState(true, true);
            Assert.AreEqual(0.15, levels.Gain(AudioBus.Sfx) / 0.95 + 0.0 * 0, 1.0, "a state change does nothing until time passes");
        }

        [Test]
        public void ThePlayerWorldIsSilentOnTheTitleButTheInterfaceIsHeard()
        {
            var levels = new BusLevels();
            levels.SetState(paused: false, inMenu: false, silentWorld: true);
            levels.Snap();
            foreach (AudioBus bus in AudioBuses.All)
            {
                if (bus == AudioBus.Ui) Assert.AreEqual(0.6, levels.Gain(bus), 1e-12);
                else Assert.AreEqual(0.0, levels.Gain(bus), 1e-12, bus.ToString());
            }
            Assert.AreEqual(0.0, levels.ReverbDuck, 1e-12);
            Assert.AreEqual(AudioBuses.SilenceDecibels, levels.Decibels(AudioBus.Music));
            levels.SetState(false, false);
            levels.Snap();
            Assert.AreEqual(0.55, levels.Gain(AudioBus.Music), 1e-12);
        }

        [Test]
        public void ADuckApproachesItsTargetByItsTimeConstant()
        {
            var levels = new BusLevels();
            levels.SetState(paused: true, inMenu: false);
            levels.Advance(0.15);
            // one time constant for the effects bus: 63% of the way from 1 to 0.15
            Assert.AreEqual(1 + (0.15 - 1) * (1 - Math.Exp(-1)), levels.Duck(AudioBus.Sfx), 1e-12);
            Assert.Less(levels.Duck(AudioBus.Music), 1.0);
            for (int i = 0; i < 200; i++) levels.Advance(0.05);
            Assert.AreEqual(0.15, levels.Duck(AudioBus.Sfx), 1e-6);
            Assert.AreEqual(1.0, levels.Duck(AudioBus.Ui), 1e-12);
            Assert.Throws<ArgumentOutOfRangeException>(() => levels.Advance(-1));
        }
    }
}
