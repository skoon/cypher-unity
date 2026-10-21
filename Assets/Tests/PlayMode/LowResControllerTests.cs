using System;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cypher.Tests
{
    public class LowResControllerTests
    {
        GameObject _go;
        RenderPipelineAsset _originalActive;
        RenderPipelineAsset _originalQuality;

        [SetUp]
        public void SetUp()
        {
            _originalActive = GraphicsSettings.currentRenderPipeline;
            _originalQuality = QualitySettings.renderPipeline;
            Assert.IsInstanceOf<UniversalRenderPipelineAsset>(_originalActive, "the project must render with URP");
            _go = new GameObject("LowRes");
            _go.SetActive(false);   // enable explicitly in each test
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_go);
            Assert.AreSame(_originalActive, GraphicsSettings.currentRenderPipeline, "the project's pipeline asset must be restored");
            Assert.AreSame(_originalQuality, QualitySettings.renderPipeline);
        }

        LowResController Enabled(int targetHeight = LowResController.DefaultTargetHeight)
        {
            var c = _go.AddComponent<LowResController>();
            c.TargetHeight = targetHeight;
            _go.SetActive(true);
            return c;
        }

        [Test]
        public void EnablingInstallsARuntimeCloneWithPointUpscaling()
        {
            LowResController c = Enabled();
            var active = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.IsNotNull(active);
            Assert.AreSame(c.ActiveAsset, active);
            Assert.AreNotSame(_originalActive, active, "must not edit the project's own asset");
            Assert.AreEqual(UpscalingFilterSelection.Point, active.upscalingFilter);
        }

        [Test]
        public void ProjectAssetIsLeftUntouched()
        {
            var original = (UniversalRenderPipelineAsset)_originalActive;
            float scale = original.renderScale;
            UpscalingFilterSelection filter = original.upscalingFilter;

            LowResController c = Enabled();
            c.Apply(2160);
            _go.SetActive(false);

            Assert.AreEqual(scale, original.renderScale);
            Assert.AreEqual(filter, original.upscalingFilter);
        }

        [Test]
        public void RenderScaleTracksTheDisplayHeight()
        {
            LowResController c = Enabled(540);
            c.Apply(1080);
            Assert.AreEqual(0.5f, c.ActiveAsset.renderScale, 1e-6f);
            c.Apply(2160);
            Assert.AreEqual(0.25f, c.ActiveAsset.renderScale, 1e-6f);
            c.Apply(540);
            Assert.AreEqual(1f, c.ActiveAsset.renderScale, 1e-6f);
            Assert.AreEqual(c.ActiveAsset.renderScale, c.CurrentScale);
        }

        [Test]
        public void NeverRendersAboveNativeResolution()
        {
            LowResController c = Enabled(540);
            c.Apply(480);
            Assert.AreEqual(1f, c.ActiveAsset.renderScale, 1e-6f);
        }

        [Test]
        public void ChangingTheTargetHeightReappliesImmediately()
        {
            LowResController c = Enabled(540);
            c.Apply(1080);
            c.TargetHeight = 360;
            Assert.AreEqual(360f / 1080f, c.ActiveAsset.renderScale, 1e-6f);
        }

        [Test]
        public void DisablingRestoresTheOriginalPipeline()
        {
            LowResController c = Enabled();
            _go.SetActive(false);
            Assert.AreSame(_originalActive, GraphicsSettings.currentRenderPipeline);
            Assert.IsNull(c.ActiveAsset);
            Assert.AreEqual(1f, c.CurrentScale);
        }

        [Test]
        public void ReEnablingWorksAfterADisable()
        {
            LowResController c = Enabled();
            _go.SetActive(false);
            _go.SetActive(true);
            Assert.IsNotNull(c.ActiveAsset);
            Assert.AreEqual(UpscalingFilterSelection.Point, c.ActiveAsset.upscalingFilter);
        }

        [Test]
        public void BadInputFailsLoudly()
        {
            var c = _go.AddComponent<LowResController>();
            Assert.Throws<ArgumentOutOfRangeException>(() => c.TargetHeight = 0);
            Assert.Throws<InvalidOperationException>(() => c.Apply(1080), "not enabled yet");

            _go.SetActive(true);
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Apply(0));
        }
    }
}
