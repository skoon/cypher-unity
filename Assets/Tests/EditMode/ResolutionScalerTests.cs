using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class ResolutionScalerTests
    {
        [Test]
        public void ScalesDownToTargetHeight()
        {
            Assert.AreEqual(0.5f, ResolutionScaler.RenderScale(540, 1080), 1e-6);
            Assert.AreEqual(0.25f, ResolutionScaler.RenderScale(540, 2160), 1e-6);
        }

        [Test]
        public void NeverUpscalesAboveNative()
        {
            Assert.AreEqual(1f, ResolutionScaler.RenderScale(540, 480));
            Assert.AreEqual(1f, ResolutionScaler.RenderScale(540, 540));
        }

        [Test]
        public void ClampsToMinimumScale()
        {
            Assert.AreEqual(ResolutionScaler.MinScale, ResolutionScaler.RenderScale(10, 4320));
        }

        [Test]
        public void InternalSizePreservesAspectRatio()
        {
            var (w, h) = ResolutionScaler.InternalSize(1920, 1080, 540);
            Assert.AreEqual(960, w);
            Assert.AreEqual(540, h);
            (w, h) = ResolutionScaler.InternalSize(3440, 1440, 540);
            Assert.AreEqual(540, h);
            Assert.AreEqual(1290, w);   // 3440 * 0.375
        }

        [Test]
        public void InternalSizeIsNativeWhenScreenIsSmallerThanTarget()
        {
            var (w, h) = ResolutionScaler.InternalSize(640, 360, 540);
            Assert.AreEqual(640, w);
            Assert.AreEqual(360, h);
        }

        [Test]
        public void InvalidSizesFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ResolutionScaler.RenderScale(0, 1080));
            Assert.Throws<ArgumentOutOfRangeException>(() => ResolutionScaler.RenderScale(540, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ResolutionScaler.InternalSize(0, 1080, 540));
        }
    }
}
