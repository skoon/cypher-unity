using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class AudioBusesTests
    {
        [Test]
        public void ThereAreFiveBusesInThePrototypesOrder()
        {
            CollectionAssert.AreEqual(new[] { AudioBus.Ambience, AudioBus.Sfx, AudioBus.Ui, AudioBus.Music, AudioBus.Voice }, new List<AudioBus>(AudioBuses.All));
        }

        [Test]
        public void TheDryTierHasNoSendAndTheOthersRiseInSteps()
        {
            Assert.AreEqual(7, AudioBuses.TierCount);
            Assert.IsTrue(double.IsNegativeInfinity(AudioBuses.TierSendDecibels(0)));
            for (int t = 2; t < AudioBuses.TierCount; t++)
            {
                double step = AudioBuses.TierSendDecibels(t) - AudioBuses.TierSendDecibels(t - 1);
                Assert.That(step, Is.InRange(3.0, 7.0), $"tier {t} is a few dB above tier {t - 1}");
            }
            Assert.Less(AudioBuses.TierSendDecibels(AudioBuses.TierCount - 1), 0.0, "the loudest send is still under unity");
        }

        [Test]
        public void ASendPicksTheTierNearestInDecibels()
        {
            Assert.AreEqual(0, AudioBuses.TierFor(0));
            Assert.AreEqual(0, AudioBuses.TierFor(0.009));
            Assert.AreEqual(1, AudioBuses.TierFor(0.01), "the quietest sends all use the first wet tier");
            Assert.AreEqual(1, AudioBuses.TierFor(0.05), "a UI sound's send");
            Assert.AreEqual(3, AudioBuses.TierFor(0.22), "the default send");
            Assert.AreEqual(AudioBuses.TierCount - 1, AudioBuses.TierFor(0.9), "a distant siren's send");
            Assert.AreEqual(AudioBuses.TierCount - 1, AudioBuses.TierFor(3.0), "a send above unity is the loudest tier");
        }

        [Test]
        public void EveryRecipeSendIsWithinThreeDecibelsOfItsTier()
        {
            // the sends the prototype's recipes ask for (Bank.js)
            foreach (double send in new[] { 0.05, 0.08, 0.1, 0.12, 0.15, 0.2, 0.22, 0.25, 0.3, 0.32, 0.4, 0.45, 0.5, 0.55, 0.6, 0.7, 0.85, 0.9 })
            {
                int tier = AudioBuses.TierFor(send);
                double error = Math.Abs(20 * Math.Log10(send) - AudioBuses.TierSendDecibels(tier));
                Assert.Less(error, 3.0, $"send {send} lands in tier {tier}, {error:F1} dB away");
            }
        }

        [Test]
        public void TierPickingIsMonotonic()
        {
            int last = 0;
            for (double send = 0; send <= 3; send += 0.005)
            {
                int tier = AudioBuses.TierFor(send);
                Assert.GreaterOrEqual(tier, last, $"send {send}");
                last = tier;
            }
        }

        [Test]
        public void GroupAndParameterNamesAreUniqueAcrossBusesAndTiers()
        {
            var groups = new HashSet<string> { AudioBuses.ReverbGroupName };
            var parameters = new HashSet<string> { AudioBuses.ReverbVolumeParameter };
            foreach (AudioBus bus in AudioBuses.All)
            {
                for (int t = 0; t < AudioBuses.TierCount; t++)
                {
                    Assert.IsTrue(groups.Add(AudioBuses.GroupName(bus, t)), AudioBuses.GroupName(bus, t));
                    Assert.IsTrue(parameters.Add(AudioBuses.VolumeParameter(bus, t)), AudioBuses.VolumeParameter(bus, t));
                }
            }
            Assert.AreEqual(1 + 5 * 7, groups.Count);
            Assert.AreEqual("Sfx", AudioBuses.GroupName(AudioBus.Sfx));
            Assert.AreEqual("SfxWet3", AudioBuses.GroupName(AudioBus.Sfx, 3));
            Assert.AreEqual("UiWet1Vol", AudioBuses.VolumeParameter(AudioBus.Ui, 1));
        }

        [Test]
        public void UnknownBusesAndTiersAreRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioBuses.GroupName((AudioBus)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioBuses.GroupName(AudioBus.Sfx, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioBuses.GroupName(AudioBus.Sfx, 7));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioBuses.TierSendDecibels(7));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioBuses.TierFor(-0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioBuses.TierFor(double.NaN));
        }

        [Test]
        public void DecibelsAndGainsConvertBothWaysAndFloorAtSilence()
        {
            Assert.AreEqual(0.0, AudioBuses.ToDecibels(1.0), 1e-12);
            Assert.AreEqual(-6.0206, AudioBuses.ToDecibels(0.5), 1e-3);
            Assert.AreEqual(20.0, AudioBuses.ToDecibels(10.0), 1e-9);
            Assert.AreEqual(AudioBuses.SilenceDecibels, AudioBuses.ToDecibels(0.0));
            Assert.AreEqual(AudioBuses.SilenceDecibels, AudioBuses.ToDecibels(1e-9));
            Assert.AreEqual(0.0, AudioBuses.ToGain(AudioBuses.SilenceDecibels));
            foreach (double gain in new[] { 0.001, 0.15, 0.45, 0.9, 1.0, 2.5 })
            {
                Assert.AreEqual(gain, AudioBuses.ToGain(AudioBuses.ToDecibels(gain)), gain * 1e-9);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioBuses.ToDecibels(-0.1));
        }

        [Test]
        public void TheReverbSettingsListEveryParameterOnceAndExposeUniqueNames()
        {
            var effect = new HashSet<string>();
            var exposed = new HashSet<string>();
            foreach (MixerReverb.Setting s in MixerReverb.Settings)
            {
                Assert.IsTrue(effect.Add(s.Effect), s.Effect);
                if (s.Exposed != null) Assert.IsTrue(exposed.Add(s.Exposed), s.Exposed);
            }
            Assert.AreEqual(14, MixerReverb.Settings.Length, "the effect has fourteen parameters");
            Assert.AreEqual(MixerReverb.Settings.Length, MixerReverb.EffectParameters.Length);
            Assert.IsTrue(exposed.Contains("ReverbDecay"));
            Assert.AreEqual(-10000, Array.Find(MixerReverb.Settings, s => s.Effect == "Dry Level").Default, "only the wet signal comes back from the reverb");
        }
    }
}
