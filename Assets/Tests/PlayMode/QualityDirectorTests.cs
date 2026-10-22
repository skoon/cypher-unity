using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cypher.Core;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    public class QualityDirectorTests
    {
        QualityRig _rig;

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        /// <summary>Make the rig and let the director's Start run; the director's own Update is then switched off so the test drives time.</summary>
        IEnumerator Start()
        {
            _rig = new QualityRig();
            yield return null;
            _rig.Director.enabled = false;
        }

        static GraphicsOptions Options(QualityPreset preset, bool adaptive = true, int height = SettingsRanges.InternalHeightAuto)
        {
            var g = new GraphicsOptions { Quality = preset, AdaptiveQuality = adaptive };
            g.InternalHeight = height;
            return g;
        }

        void AssertAppliedTier(QualityPreset preset, int expectedHeight)
        {
            QualityTier tier = QualityTiers.For(preset);
            Assert.AreEqual(expectedHeight, _rig.LowRes.TargetHeight, preset + " height");
            Assert.AreEqual(tier.MsaaSamples, _rig.LowRes.ActiveAsset.msaaSampleCount, preset + " msaa");
            Assert.AreEqual(tier.RainParticleCap, _rig.Rain.MaxParticles, preset + " rain cap");
            Assert.AreEqual(tier.RainParticleCap, _rig.Rain.System.main.maxParticles, preset + " rain system cap");
            Assert.AreEqual(tier.ProbeResolution, _rig.Probe.Resolution, preset + " probe");
            Assert.AreEqual(tier.ProbeResolution, _rig.Probe.Probe.resolution, preset + " probe cubemap");

            Bloom bloom = _rig.LiveBloom;
            switch (tier.Bloom)
            {
                case BloomQuality.Fast:
                    Assert.AreEqual(BloomDownscaleMode.Quarter, bloom.downscale.value);
                    Assert.AreEqual(4, bloom.maxIterations.value);
                    Assert.IsFalse(bloom.highQualityFiltering.value);
                    break;
                case BloomQuality.Standard:
                    Assert.AreEqual(BloomDownscaleMode.Half, bloom.downscale.value);
                    Assert.AreEqual(6, bloom.maxIterations.value);
                    Assert.IsFalse(bloom.highQualityFiltering.value);
                    break;
                case BloomQuality.High:
                    Assert.AreEqual(BloomDownscaleMode.Half, bloom.downscale.value);
                    Assert.AreEqual(6, bloom.maxIterations.value);
                    Assert.IsTrue(bloom.highQualityFiltering.value);
                    break;
            }
        }

        // ------------------------------------------------------------------ applying tiers

        [UnityTest]
        public IEnumerator StartsAtMediumWhichIsTheMilestoneOneLook()
        {
            yield return Start();
            Assert.AreEqual(QualityPreset.Medium, _rig.Director.Preset);
            Assert.IsTrue(_rig.Director.Adaptive);
            AssertAppliedTier(QualityPreset.Medium, 540);
            Assert.AreEqual(LowResController.DefaultTargetHeight, _rig.LowRes.TargetHeight);
            Assert.AreEqual(RainController.DefaultMaxParticles, _rig.Rain.MaxParticles);
            Assert.AreEqual(StreetReflectionProbe.DefaultResolution, _rig.Probe.Resolution);
        }

        [UnityTest]
        public IEnumerator EveryPresetAppliesItsWholeTier()
        {
            yield return Start();
            foreach (QualityTier tier in QualityTiers.All)
            {
                _rig.Director.Apply(Options(tier.Preset));
                Assert.AreEqual(tier.Preset, _rig.Director.Preset);
                AssertAppliedTier(tier.Preset, tier.InternalHeight);
            }
        }

        [UnityTest]
        public IEnumerator AutoResolutionFollowsThePresetAndAPinnedHeightWins()
        {
            yield return Start();
            _rig.Director.Apply(Options(QualityPreset.Performance));
            Assert.AreEqual(360, _rig.LowRes.TargetHeight);

            _rig.Director.Apply(Options(QualityPreset.Performance, height: 720));
            Assert.AreEqual(720, _rig.LowRes.TargetHeight, "pinned: the preset no longer picks the height");
            Assert.AreEqual(1, _rig.LowRes.ActiveAsset.msaaSampleCount, "but its other levers still apply");

            _rig.Director.Apply(Options(QualityPreset.Ultra, height: 720));
            Assert.AreEqual(720, _rig.LowRes.TargetHeight);
            Assert.AreEqual(4, _rig.LowRes.ActiveAsset.msaaSampleCount);

            _rig.Director.Apply(Options(QualityPreset.Ultra));
            Assert.AreEqual(540, _rig.LowRes.TargetHeight, "back to Auto");
        }

        [UnityTest]
        public IEnumerator TheRenderScaleFollowsTheAppliedHeight()
        {
            yield return Start();
            _rig.Director.Apply(Options(QualityPreset.Performance));
            Assert.AreEqual(ResolutionScaler.RenderScale(360, Screen.height), _rig.LowRes.ActiveAsset.renderScale, 1e-5f);
        }

        [UnityTest]
        public IEnumerator TheProjectAssetsAreNeverTouched()
        {
            yield return Start();
            _rig.Director.Apply(Options(QualityPreset.Ultra));

            Assert.IsTrue(_rig.LiveBloom.highQualityFiltering.value, "the live copy changed");
            Assert.IsFalse(_rig.SharedBloom.highQualityFiltering.value, "the shared profile did not");
            Assert.AreEqual(BloomDownscaleMode.Half, _rig.SharedBloom.downscale.value);
#if UNITY_EDITOR
            var projectAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Assert.IsNotNull(projectAsset);
            Assert.AreEqual(1, projectAsset.msaaSampleCount, "the project's pipeline asset keeps MSAA off");
            Assert.AreEqual(1f, projectAsset.renderScale, "and its render scale");
#endif
        }

        // ------------------------------------------------------------------ the player's choice

        [UnityTest]
        public IEnumerator ChoosingAPresetFiresChangedAsAPlayerChangeWithNoNotice()
        {
            yield return Start();
            var changes = new List<QualityChange>();
            _rig.Director.Changed += changes.Add;

            _rig.Director.Apply(Options(QualityPreset.High));
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(QualityPreset.Medium, changes[0].From);
            Assert.AreEqual(QualityPreset.High, changes[0].To);
            Assert.AreEqual(QualityChangeReason.Player, changes[0].Reason);
            Assert.IsNull(changes[0].Notice());

            _rig.Director.Apply(Options(QualityPreset.High, adaptive: false));
            Assert.AreEqual(1, changes.Count, "same preset: nothing changed");
            Assert.IsFalse(_rig.Director.Adaptive);
        }

        [UnityTest]
        public IEnumerator OptionsReturnsACopyOfWhatIsInForce()
        {
            yield return Start();
            _rig.Director.Apply(Options(QualityPreset.Low, adaptive: false, height: 720));
            GraphicsOptions copy = _rig.Director.Options;
            Assert.AreEqual(QualityPreset.Low, copy.Quality);
            Assert.IsFalse(copy.AdaptiveQuality);
            Assert.AreEqual(720, copy.InternalHeight);
            copy.Quality = QualityPreset.Ultra;
            Assert.AreEqual(QualityPreset.Low, _rig.Director.Preset, "editing the copy changes nothing");
        }

        [UnityTest]
        public IEnumerator ApplyBeforeStartIsHeldAndAppliedAtStart()
        {
            _rig = new QualityRig(activate: false);
            _rig.Director.Apply(Options(QualityPreset.Low, height: 900));
            _rig.Director.gameObject.SetActive(true);
            yield return null;
            _rig.Director.enabled = false;

            AssertAppliedTier(QualityPreset.Low, 900);
            Assert.AreEqual(QualityPreset.Low, _rig.Director.Preset);
        }

        // ------------------------------------------------------------------ adaptive quality

        [UnityTest]
        public IEnumerator SlowFramesLowerThePresetApplyItAndFireChangedWithANotice()
        {
            yield return Start();
            _rig.Director.Apply(Options(QualityPreset.Ultra));
            var changes = new List<QualityChange>();
            _rig.Director.Changed += changes.Add;

            for (int i = 0; i < 20 * 8; i++) _rig.Director.Sample(1.0 / 20.0);   // eight seconds at 20 fps

            Assert.GreaterOrEqual(changes.Count, 1);
            QualityChange first = changes[0];
            Assert.AreEqual(QualityPreset.Ultra, first.From);
            Assert.AreEqual(QualityPreset.High, first.To, "one tier at 20 fps, not catastrophic");
            Assert.AreEqual(QualityChangeReason.Adaptive, first.Reason);
            Assert.AreEqual(20.0, first.Fps, 1.0);
            StringAssert.Contains("Quality lowered from Ultra to High", first.Notice());
            Assert.AreEqual(changes[changes.Count - 1].To, _rig.Director.Preset);
            AssertAppliedTier(_rig.Director.Preset, QualityTiers.For(_rig.Director.Preset).InternalHeight);
        }

        [UnityTest]
        public IEnumerator ALoweredPresetShowsInOptionsSoItCanBeSaved()
        {
            yield return Start();
            _rig.Director.Apply(Options(QualityPreset.Ultra));
            for (int i = 0; i < 20 * 8; i++) _rig.Director.Sample(1.0 / 20.0);
            Assert.AreEqual(_rig.Director.Preset, _rig.Director.Options.Quality, "what is applied is what Options reports");
            Assert.Less((int)_rig.Director.Options.Quality, (int)QualityPreset.Ultra);
        }

        [UnityTest]
        public IEnumerator AdaptiveOffNeverLowersThePresetButStillMeasures()
        {
            yield return Start();
            _rig.Director.Apply(Options(QualityPreset.Ultra, adaptive: false));
            var changes = new List<QualityChange>();
            _rig.Director.Changed += changes.Add;

            for (int i = 0; i < 20 * 12; i++) _rig.Director.Sample(1.0 / 20.0);

            Assert.IsEmpty(changes);
            Assert.AreEqual(QualityPreset.Ultra, _rig.Director.Preset);
            Assert.AreEqual(20.0, _rig.Director.Fps, 1.0);
        }

        [UnityTest]
        public IEnumerator FastFramesNeverLowerThePreset()
        {
            yield return Start();
            _rig.Director.Apply(Options(QualityPreset.Ultra));
            for (int i = 0; i < 100 * 30; i++) _rig.Director.Sample(1.0 / 100.0);
            Assert.AreEqual(QualityPreset.Ultra, _rig.Director.Preset);
        }

        [UnityTest]
        public IEnumerator SuspendedFramesAreNotJudgedAndResumingRearmsTheWarmup()
        {
            yield return Start();
            _rig.Director.Apply(Options(QualityPreset.Ultra));
            _rig.Director.Suspended = true;
            for (int i = 0; i < 20 * 20; i++) _rig.Director.Sample(1.0 / 20.0);
            Assert.AreEqual(QualityPreset.Ultra, _rig.Director.Preset, "a paused game is not a slow game");

            _rig.Director.Suspended = false;
            Assert.IsTrue(_rig.Director.Settling, "warm-up again after resume");
            for (int i = 0; i < 20 * 3; i++) _rig.Director.Sample(1.0 / 20.0);
            Assert.AreEqual(QualityPreset.Ultra, _rig.Director.Preset, "still inside the warm-up");
            for (int i = 0; i < 20 * 6; i++) _rig.Director.Sample(1.0 / 20.0);
            Assert.AreNotEqual(QualityPreset.Ultra, _rig.Director.Preset, "after it, slow frames count again");
        }

        [UnityTest]
        public IEnumerator RaisingThePresetAfterADowngradeGivesItAFreshWarmup()
        {
            yield return Start();
            _rig.Director.Apply(Options(QualityPreset.Ultra));
            for (int i = 0; i < 20 * 8; i++) _rig.Director.Sample(1.0 / 20.0);
            Assert.AreNotEqual(QualityPreset.Ultra, _rig.Director.Preset);

            _rig.Director.Apply(Options(QualityPreset.Ultra));
            Assert.AreEqual(QualityPreset.Ultra, _rig.Director.Preset, "the player's choice is the new ceiling");
            AssertAppliedTier(QualityPreset.Ultra, 540);
            for (int i = 0; i < 20 * 3; i++) _rig.Director.Sample(1.0 / 20.0);
            Assert.AreEqual(QualityPreset.Ultra, _rig.Director.Preset, "stale history does not overrule it");
        }

        [UnityTest]
        public IEnumerator PinnedResolutionSurvivesADowngrade()
        {
            yield return Start();
            _rig.Director.Apply(Options(QualityPreset.Ultra, height: 720));
            for (int i = 0; i < 20 * 8; i++) _rig.Director.Sample(1.0 / 20.0);
            Assert.AreNotEqual(QualityPreset.Ultra, _rig.Director.Preset);
            Assert.AreEqual(720, _rig.LowRes.TargetHeight, "adaptive quality does not touch a resolution the player pinned");
        }

        // ------------------------------------------------------------------ failures

        [UnityTest]
        public IEnumerator AMissingPieceFailsLoudlyAtStart()
        {
            LogAssert.Expect(LogType.Exception, new Regex("QualityDirector needs the LowResController"));
            _rig = new QualityRig(configure: false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AVolumeWithoutBloomFailsLoudlyAtStart()
        {
            _rig = new QualityRig(activate: false);
            var empty = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            _rig.PostVolume.sharedProfile = empty;
            LogAssert.Expect(LogType.Exception, new Regex("no Bloom override"));
            _rig.Director.gameObject.SetActive(true);
            yield return null;
            Object.DestroyImmediate(empty);
        }

        [UnityTest]
        public IEnumerator NullOptionsAreRejected()
        {
            yield return Start();
            Assert.Throws<System.ArgumentNullException>(() => _rig.Director.Apply(null));
        }
    }
}
