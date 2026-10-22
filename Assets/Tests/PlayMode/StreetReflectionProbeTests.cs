using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
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

        StreetReflectionProbe MakeDistricts(bool captureAutomatically, int resolution = 32)
        {
            _go = new GameObject("ProbeDistrictsTest");
            _go.SetActive(false);
            var p = _go.AddComponent<StreetReflectionProbe>();
            p.Configure(ProbeLayout.Build(CityWindow.AroundPlaza(3), ProbeLayout.DefaultPerAxis, WorldEdge.Apron), resolution, captureAutomatically);
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
        public void ALayoutMakesAProbePerDistrictRenderedFromItsJunctionAndProjectedOntoItsBox()
        {
            IReadOnlyList<ProbeRegion> layout = ProbeLayout.Build(CityWindow.AroundPlaza(3), ProbeLayout.DefaultPerAxis, WorldEdge.Apron);
            StreetReflectionProbe p = MakeDistricts(captureAutomatically: false);

            Assert.AreEqual(9, p.Count);
            Assert.AreEqual(9, p.Probes.Count);
            for (int i = 0; i < layout.Count; i++)
            {
                ReflectionProbe probe = p.Probes[i];
                Assert.AreEqual((float)layout[i].CaptureX, probe.transform.position.x, 1e-3f, $"probe {i} capture x");
                Assert.AreEqual((float)layout[i].CaptureZ, probe.transform.position.z, 1e-3f, $"probe {i} capture z");
                Vector3 boxCentre = probe.transform.TransformPoint(probe.center);
                Assert.AreEqual((float)layout[i].BoxCentreX, boxCentre.x, 1e-3f, $"probe {i} box centre x");
                Assert.AreEqual((float)layout[i].BoxCentreZ, boxCentre.z, 1e-3f, $"probe {i} box centre z");
                Assert.AreEqual((float)layout[i].SizeX, probe.size.x, 1e-3f);
                Assert.AreEqual(ProbeLayout.Overlap, probe.blendDistance, 1e-3f, "blends across the overlap");
                Assert.IsTrue(probe.boxProjection);
            }
            Assert.AreSame(p.Probes[0], p.Probe);
        }

        [UnityTest]
        public IEnumerator EveryProbeIsCapturedAndTheSetIsOnlyCapturedWhenAllAre()
        {
            RequireGpu();
            StreetReflectionProbe p = MakeDistricts(captureAutomatically: false);
            Assert.IsFalse(p.IsCaptured);
            p.Capture();
            Assert.AreEqual(1, p.CaptureCount, "one round, however many probes");
            yield return WaitCaptured(p);
            Assert.IsTrue(p.IsCaptured);
            foreach (ReflectionProbe probe in p.Probes)
            {
                Assert.IsNotNull(probe.texture, "a cubemap was rendered");
                Assert.AreEqual(TextureDimension.Cube, probe.texture.dimension);
            }
        }

        [Test]
        public void ChangingTheResolutionChangesEveryProbe()
        {
            StreetReflectionProbe p = MakeDistricts(captureAutomatically: false, resolution: 32);
            p.SetResolution(64);
            Assert.AreEqual(64, p.Resolution);
            Assert.IsTrue(p.Probes.All(probe => probe.resolution == 64));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.SetResolution(48), "not a power of two");
        }

        [UnityTest]
        public IEnumerator ReconfiguringWithADifferentNumberOfDistrictsRebuildsTheProbes()
        {
            StreetReflectionProbe p = MakeDistricts(captureAutomatically: false);
            p.Configure(ProbeLayout.Build(CityWindow.AroundPlaza(1), 1), 32, false);
            Assert.AreEqual(1, p.Count);
            Assert.AreEqual(1, p.Probes.Count);
            Assert.AreEqual(0, p.CaptureCount, "a rebuilt set has captured nothing yet");
            Assert.IsFalse(p.IsCaptured);
            yield return null;   // destroyed objects go at the end of the frame
            Assert.AreEqual(1, _go.GetComponentsInChildren<ReflectionProbe>().Length, "the old probes are gone");
        }

        [Test]
        public void ABadLayoutFailsLoudly()
        {
            StreetReflectionProbe p = MakeDistricts(captureAutomatically: false);
            Assert.Throws<ArgumentNullException>(() => p.Configure((IReadOnlyList<ProbeRegion>)null, 32, false));
            Assert.Throws<ArgumentException>(() => p.Configure(new List<ProbeRegion>(), 32, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Configure(ProbeLayout.Build(CityWindow.AroundPlaza(1), 1), 8, false));
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
