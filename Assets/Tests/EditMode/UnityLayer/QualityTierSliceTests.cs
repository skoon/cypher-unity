using Cypher.Core;
using Cypher.Rendering;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The Medium tier is the Milestone 1 slice, so it must equal the constants the slice's components default to.</summary>
    public class QualityTierSliceTests
    {
        [Test]
        public void MediumMatchesTheDefaultsOfTheComponentsItDrives()
        {
            QualityTier medium = QualityTiers.For(QualityPreset.Medium);
            Assert.AreEqual(LowResController.DefaultTargetHeight, medium.InternalHeight);
            Assert.AreEqual(RainController.DefaultMaxParticles, medium.RainParticleCap);
            Assert.AreEqual(StreetReflectionProbe.DefaultResolution, medium.ProbeResolution);
        }

        [Test]
        public void TheDefaultPresetIsMedium()
        {
            Assert.AreEqual(QualityPreset.Medium, SettingsRanges.QualityDefault);
        }
    }
}
