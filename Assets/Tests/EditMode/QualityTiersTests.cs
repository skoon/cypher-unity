using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class QualityTiersTests
    {
        [Test]
        public void ThereIsOneTierPerPresetInOrderCheapestFirst()
        {
            Assert.AreEqual(Enum.GetValues(typeof(QualityPreset)).Length, QualityTiers.All.Count);
            for (int i = 0; i < QualityTiers.All.Count; i++) Assert.AreEqual((QualityPreset)i, QualityTiers.All[i].Preset);
            foreach (QualityPreset p in Enum.GetValues(typeof(QualityPreset))) Assert.AreEqual(p, QualityTiers.For(p).Preset);
        }

        [Test]
        public void MediumIsExactlyTheMilestoneOneSlice()
        {
            QualityTier m = QualityTiers.For(QualityPreset.Medium);
            Assert.AreEqual(540, m.InternalHeight);
            Assert.AreEqual(1, m.MsaaSamples, "M1 ran without MSAA");
            Assert.AreEqual(3000, m.RainParticleCap, "RainController.DefaultMaxParticles");
            Assert.AreEqual(128, m.ProbeResolution, "StreetReflectionProbe.DefaultResolution");
            Assert.AreEqual(BloomQuality.Standard, m.Bloom, "M1 bloom: half resolution, standard filtering");
        }

        [Test]
        public void TheTableMatchesTheAgreedValues()
        {
            Assert.AreEqual(new[] { 360, 450, 540, 540, 540 }, QualityTiers.All.Select(t => t.InternalHeight).ToArray());
            Assert.AreEqual(new[] { 1, 1, 1, 2, 4 }, QualityTiers.All.Select(t => t.MsaaSamples).ToArray());
            Assert.AreEqual(new[] { BloomQuality.Fast, BloomQuality.Standard, BloomQuality.Standard, BloomQuality.High, BloomQuality.High },
                QualityTiers.All.Select(t => t.Bloom).ToArray());
            Assert.AreEqual(new[] { 1000, 2000, 3000, 3000, 4500 }, QualityTiers.All.Select(t => t.RainParticleCap).ToArray());
            Assert.AreEqual(new[] { 64, 128, 128, 256, 256 }, QualityTiers.All.Select(t => t.ProbeResolution).ToArray());
        }

        [Test]
        public void NoLeverGetsCheaperAsThePresetGoesUp()
        {
            for (int i = 1; i < QualityTiers.All.Count; i++)
            {
                QualityTier lo = QualityTiers.All[i - 1], hi = QualityTiers.All[i];
                Assert.GreaterOrEqual(hi.InternalHeight, lo.InternalHeight, hi.Preset + " height");
                Assert.GreaterOrEqual(hi.MsaaSamples, lo.MsaaSamples, hi.Preset + " msaa");
                Assert.GreaterOrEqual((int)hi.Bloom, (int)lo.Bloom, hi.Preset + " bloom");
                Assert.GreaterOrEqual(hi.RainParticleCap, lo.RainParticleCap, hi.Preset + " rain");
                Assert.GreaterOrEqual(hi.ProbeResolution, lo.ProbeResolution, hi.Preset + " probe");
            }
        }

        [Test]
        public void EveryTierIsInsideTheRangesTheSettingsAllow()
        {
            foreach (QualityTier t in QualityTiers.All)
            {
                Assert.That(t.InternalHeight, Is.InRange(SettingsRanges.InternalHeightMin, SettingsRanges.InternalHeightMax));
                CollectionAssert.Contains(new[] { 1, 2, 4 }, t.MsaaSamples);
                Assert.That(t.ProbeResolution & (t.ProbeResolution - 1), Is.EqualTo(0), "power of two");
            }
        }

        [Test]
        public void AutoFollowsThePresetAndAPinnedHeightWins()
        {
            Assert.AreEqual(360, QualityTiers.EffectiveInternalHeight(QualityPreset.Performance, SettingsRanges.InternalHeightAuto));
            Assert.AreEqual(450, QualityTiers.EffectiveInternalHeight(QualityPreset.Low, SettingsRanges.InternalHeightAuto));
            Assert.AreEqual(540, QualityTiers.EffectiveInternalHeight(QualityPreset.Ultra, SettingsRanges.InternalHeightAuto));
            Assert.AreEqual(1080, QualityTiers.EffectiveInternalHeight(QualityPreset.Performance, 1080));
            Assert.AreEqual(240, QualityTiers.EffectiveInternalHeight(QualityPreset.Ultra, 240));
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => QualityTiers.For((QualityPreset)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => QualityTiers.EffectiveInternalHeight(QualityPreset.Low, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => QualityTiers.EffectiveInternalHeight(QualityPreset.Low, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => QualityTiers.EffectiveInternalHeight(QualityPreset.Low, 5000));
            var ok = QualityPreset.Low;
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityTier(ok, 100, 1, BloomQuality.Fast, 1000, 128));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityTier(ok, 540, 3, BloomQuality.Fast, 1000, 128));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityTier(ok, 540, 8, BloomQuality.Fast, 1000, 128));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityTier(ok, 540, 1, BloomQuality.Fast, 99, 128));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityTier(ok, 540, 1, BloomQuality.Fast, 1000, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityTier(ok, 540, 1, BloomQuality.Fast, 1000, 8));
        }

        [Test]
        public void ADowngradeNoticeNamesBothPresetsAndTheFrameRate()
        {
            var c = new QualityChange(QualityPreset.High, QualityPreset.Low, 23.7, QualityChangeReason.Adaptive);
            Assert.AreEqual("Quality lowered from High to Low because the frame rate was about 24 FPS. You can raise it in Options.", c.Notice());
        }

        [Test]
        public void AChangeThePlayerMadeHasNoNotice()
        {
            Assert.IsNull(new QualityChange(QualityPreset.Low, QualityPreset.High, 0, QualityChangeReason.Player).Notice());
        }
    }
}
