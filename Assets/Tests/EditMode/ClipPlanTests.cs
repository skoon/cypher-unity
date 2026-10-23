using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class ClipPlanTests
    {
        [Test]
        public void EveryClipHasItsOwnKeyAndAKnownSound()
        {
            var keys = new HashSet<string>();
            foreach (ClipSpec s in ClipPlan.All())
            {
                Assert.IsTrue(keys.Add(s.Key), "duplicate key " + s.Key);
                Assert.IsTrue(SoundBank.Has(s.Sound), s.Key + " names a sound the bank has");
                Assert.IsNotNull(s.Params);
            }
            Assert.Greater(keys.Count, 200);
            Assert.Less(keys.Count, 400, "a number of clips that renders in a second or two on a worker");
        }

        [Test]
        public void TheSeedsAreStableAndDistinctSoATakeIsTheSameTakeEveryRun()
        {
            var seeds = new HashSet<uint>();
            foreach (ClipSpec s in ClipPlan.All()) Assert.IsTrue(seeds.Add(s.Seed), "two clips share the seed of " + s.Key);
            ClipSpec first = ClipPlan.All().First(x => x.Key == "ui.hover|0");
            Assert.AreEqual(first.Seed, ClipPlan.All().First(x => x.Key == "ui.hover|0").Seed);
        }

        [Test]
        public void ThereIsAFootstepForEverySurfaceStanceAndWetnessAGameCanAskFor()
        {
            var keys = new HashSet<string>(ClipPlan.Foley().Select(s => s.Key));
            foreach (SoundBuilders.Surface surface in SoundBuilders.Surfaces)
            {
                foreach (ClipPlan.Stance stance in new[] { ClipPlan.Stance.Walk, ClipPlan.Stance.Sprint, ClipPlan.Stance.Crouch })
                {
                    for (int rainStep = 0; rainStep <= 10; rainStep++)
                    {
                        int wet = ClipPlan.WetBucket(Footing.Wetness(rainStep / 10.0));
                        for (int take = 0; take < ClipPlan.StepTakes; take++)
                            Assert.IsTrue(keys.Contains(ClipPlan.StepKey(surface.Name, stance, wet, take)), $"{surface.Name} {stance} wet {wet} take {take}");
                    }
                }
            }
        }

        [Test]
        public void ThereIsALandingForEverySurfaceAndImpactAndABreathForEveryEffort()
        {
            var keys = new HashSet<string>(ClipPlan.Foley().Select(s => s.Key));
            foreach (SoundBuilders.Surface surface in SoundBuilders.Surfaces)
            {
                for (double impact = 0.1; impact <= 1.4; impact += 0.1)
                {
                    for (int wet = 0; wet < 3; wet++)
                        for (int take = 0; take < ClipPlan.LandTakes; take++)
                            Assert.IsTrue(keys.Contains(ClipPlan.LandKey(surface.Name, ClipPlan.ImpactBucket(impact), wet, take)), $"{surface.Name} {impact} {wet}");
                }
            }
            foreach (bool inhale in new[] { true, false })
                for (double effort = 0; effort <= 1; effort += 0.05)
                    for (int take = 0; take < ClipPlan.BreathTakes; take++)
                        Assert.IsTrue(keys.Contains(ClipPlan.BreathKey(inhale, ClipPlan.EffortBucket(effort), take)));
            for (int take = 0; take < ClipPlan.JumpTakes; take++) Assert.IsTrue(keys.Contains(ClipPlan.JumpKey(take)));
        }

        [Test]
        public void EveryMenuSoundAndAmbientSoundIsThere()
        {
            var keys = new HashSet<string>(ClipPlan.Ui().Select(s => s.Key));
            foreach (string name in ClipPlan.UiSoundNames)
                for (int take = 0; take < ClipPlan.UiTakes; take++) Assert.IsTrue(keys.Contains(ClipPlan.UiKey(name, take)), name);
            var ambient = new HashSet<string>(ClipPlan.Ambient().Select(s => s.Key));
            foreach (string name in ClipPlan.AmbientSounds)
                for (int take = 0; take < ClipPlan.AmbientTakes(name); take++) Assert.IsTrue(ambient.Contains(ClipPlan.AmbientKey(name, take)), name);
        }

        [Test]
        public void WetnessAndImpactAndEffortAreBucketedToTheNearest()
        {
            Assert.AreEqual(0, ClipPlan.WetBucket(0));
            Assert.AreEqual(0, ClipPlan.WetBucket(0.14));
            Assert.AreEqual(1, ClipPlan.WetBucket(0.4));
            Assert.AreEqual(2, ClipPlan.WetBucket(0.9));
            Assert.AreEqual(0, ClipPlan.ImpactBucket(0.1));
            Assert.AreEqual(1, ClipPlan.ImpactBucket(0.6));
            Assert.AreEqual(2, ClipPlan.ImpactBucket(1.4));
            Assert.AreEqual(0, ClipPlan.EffortBucket(0.1));
            Assert.AreEqual(3, ClipPlan.EffortBucket(0.95));
        }

        [Test]
        public void ATakeIsNeverTheSameAsTheOneBeforeItAndAllTakesGetPlayed()
        {
            var picker = new TakePicker(5);
            int last = -1;
            var seen = new HashSet<int>();
            for (int i = 0; i < 200; i++)
            {
                int take = picker.Pick("foot.concrete", 3);
                Assert.That(take, Is.InRange(0, 2));
                Assert.AreNotEqual(last, take);
                seen.Add(take);
                last = take;
            }
            Assert.AreEqual(3, seen.Count);
            Assert.AreEqual(0, picker.Pick("one", 1));
            Assert.AreEqual(0, picker.Pick("one", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => picker.Pick("none", 0));
        }

        [Test]
        public void TheFamiliesOfTakesAreIndependent()
        {
            var picker = new TakePicker(5);
            int a1 = picker.Pick("a", 2);
            picker.Pick("b", 2);
            int a2 = picker.Pick("a", 2);
            Assert.AreNotEqual(a1, a2, "with two takes, the next is the other");
        }
    }
}
