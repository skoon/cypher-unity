using System;
using System.Collections;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    public class StreetReflectionProbeTests
    {
        GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go) UnityEngine.Object.DestroyImmediate(_go);
        }

        StreetReflectionProbe Make(bool captureAutomatically, int resolution = 32)
        {
            _go = new GameObject("ProbeTest");
            _go.SetActive(false);   // Configure before Awake/Start run
            var p = _go.AddComponent<StreetReflectionProbe>();
            p.Configure(new Vector3(1f, 6f, 2f), new Vector3(100f, 40f, 80f), resolution, captureAutomatically);
            _go.SetActive(true);
            return p;
        }

        /// <summary>Rendering a probe needs a graphics device, which -nographics runs do not have.</summary>
        static void RequireGpu() =>
            Assume.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null), "reflection probes need a GPU (run without -nographics)");

        static IEnumerator WaitCaptured(StreetReflectionProbe p)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!p.IsCaptured && Time.realtimeSinceStartup < deadline) yield return null;
        }

        [Test]
        public void ProbeIsARealtimeScriptTriggeredBoxProjectedHdrProbe()
        {
            StreetReflectionProbe p = Make(captureAutomatically: false, resolution: 64);
            ReflectionProbe probe = p.Probe;
            Assert.IsNotNull(probe);
            Assert.AreEqual(ReflectionProbeMode.Realtime, probe.mode);
            Assert.AreEqual(ReflectionProbeRefreshMode.ViaScripting, probe.refreshMode);
            Assert.AreEqual(ReflectionProbeTimeSlicingMode.NoTimeSlicing, probe.timeSlicingMode);
            Assert.IsTrue(probe.boxProjection);
            Assert.IsTrue(probe.hdr);
            Assert.AreEqual(64, probe.resolution);
            Assert.AreEqual(new Vector3(100f, 40f, 80f), probe.size);
            Assert.AreEqual(new Vector3(1f, 6f, 2f), probe.transform.position);
        }

        [UnityTest]
        public IEnumerator CapturesOnceAutomaticallyAfterTheSettleFrames()
        {
            RequireGpu();
            StreetReflectionProbe p = Make(captureAutomatically: true);
            Assert.AreEqual(0, p.CaptureCount, "nothing before the settle frames");

            for (int i = 0; i < StreetReflectionProbe.SettleFrames + 2; i++) yield return null;
            yield return WaitCaptured(p);

            Assert.AreEqual(1, p.CaptureCount);
            Assert.IsTrue(p.IsCaptured);
            Assert.IsNotNull(p.Probe.texture, "the capture produced a cubemap");
            Assert.AreEqual(TextureDimension.Cube, p.Probe.texture.dimension);
        }

        [UnityTest]
        public IEnumerator WithAutomaticCaptureOffNothingRendersUntilAsked()
        {
            RequireGpu();
            StreetReflectionProbe p = Make(captureAutomatically: false);
            for (int i = 0; i < 6; i++) yield return null;
            Assert.AreEqual(0, p.CaptureCount);
            Assert.IsFalse(p.IsCaptured);

            p.Capture();
            yield return WaitCaptured(p);
            Assert.AreEqual(1, p.CaptureCount);
            Assert.IsTrue(p.IsCaptured);
        }

        [Test]
        public void BadConfigurationFailsLoudly()
        {
            StreetReflectionProbe p = Make(captureAutomatically: false);
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Configure(Vector3.zero, new Vector3(0f, 10f, 10f), 64, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Configure(Vector3.zero, Vector3.one, 8, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Configure(Vector3.zero, Vector3.one, 1024, false));
        }

        [Test]
        public void CaptureFailsLoudlyWhenTheProjectDisablesRealtimeProbes()
        {
            StreetReflectionProbe p = Make(captureAutomatically: false);
            bool saved = QualitySettings.realtimeReflectionProbes;
            QualitySettings.realtimeReflectionProbes = false;
            try
            {
                var e = Assert.Throws<InvalidOperationException>(() => p.Capture());
                StringAssert.Contains("Realtime Reflection Probes", e.Message);
                Assert.AreEqual(0, p.CaptureCount);
            }
            finally
            {
                QualitySettings.realtimeReflectionProbes = saved;
            }
        }

        [Test]
        public void ProjectEnablesRealtimeReflectionProbes()
        {
            Assert.IsTrue(QualitySettings.realtimeReflectionProbes, "the street probe depends on this setting");
        }

        [Test]
        public void CaptureBeforeAwakeFailsLoudly()
        {
            _go = new GameObject("Inactive");
            _go.SetActive(false);
            var p = _go.AddComponent<StreetReflectionProbe>();
            Assert.Throws<InvalidOperationException>(() => p.Capture());
        }
    }
}
