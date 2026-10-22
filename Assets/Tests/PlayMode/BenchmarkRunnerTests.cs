using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using Cypher.Core;
using Cypher.Perf;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    public class BenchmarkRunnerTests
    {
        PlayerTestRig _rig;
        QualityRig _quality;
        GameObject _runnerGo;
        string _dir;
        int _savedVSync;
        int _savedTargetFrameRate;
        bool _savedBackground;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cypher-benchmark-test-" + System.Guid.NewGuid().ToString("N"));
            _savedVSync = QualitySettings.vSyncCount;
            _savedTargetFrameRate = Application.targetFrameRate;
            _savedBackground = Application.runInBackground;
        }

        [TearDown]
        public void TearDown()
        {
            if (_runnerGo) Object.DestroyImmediate(_runnerGo);
            _quality?.Dispose();
            _quality = null;
            _rig?.Dispose();
            _rig = null;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            QualitySettings.vSyncCount = _savedVSync;
            Application.targetFrameRate = _savedTargetFrameRate;
            Application.runInBackground = _savedBackground;
        }

        BenchmarkRunner MakeRunner(BenchmarkOptions options, System.Action<int> quit)
        {
            _runnerGo = new GameObject("BenchmarkRunnerTest");
            _runnerGo.SetActive(false);   // configure before Awake reads the command line
            var runner = _runnerGo.AddComponent<BenchmarkRunner>();
            runner.Configure(options, quit, screenshots: false);
            _runnerGo.SetActive(true);
            return runner;
        }

        [Test]
        public void DoesNothingWithoutTheFlagOnTheCommandLine()
        {
            _runnerGo = new GameObject("NoFlag");
            var runner = _runnerGo.AddComponent<BenchmarkRunner>();   // reads the real command line, which has no --benchmark
            Assert.IsFalse(runner.enabled);
            Assert.AreEqual(BenchmarkRunner.Phase.Idle, runner.State);
        }

        [UnityTest]
        public IEnumerator WalksTheRouteRecordsEveryFrameAndWritesTheResults()
        {
            _rig = new PlayerTestRig();
            _rig.PlaceAndSettle(new Vector3(0f, (float)CityLayout.SidewalkHeight + 0.1f, 0f));
            _rig.Player.AutoTick = true;

            int? exit = null;
            BenchmarkRunner runner = MakeRunner(new BenchmarkOptions(true, _dir, warmupSeconds: 0.3, durationSeconds: 6), code => exit = code);

            float deadline = Time.realtimeSinceStartup + 90f;
            while (runner.State != BenchmarkRunner.Phase.Done && Time.realtimeSinceStartup < deadline) yield return null;

            Assert.AreEqual(BenchmarkRunner.Phase.Done, runner.State, "the run finished");
            Assert.IsNotNull(exit, "the runner asked to quit");
            Assert.AreEqual(runner.Summary.Verdict == BenchmarkVerdict.Pass ? BenchmarkRunner.ExitPass : BenchmarkRunner.ExitFail, exit,
                "a run that walked is a pass or a fail of the gate, never invalid");

            Assert.GreaterOrEqual(runner.Summary.DurationSeconds, 6.0);
            Assert.Greater(runner.Summary.Frames, 20);
            Assert.Greater(runner.Summary.DistanceMetres, 10.0, "the pilot walked");

            string csv = File.ReadAllText(Path.Combine(_dir, BenchmarkRunner.CsvFile));
            string[] rows = csv.TrimEnd('\n').Split('\n');
            Assert.AreEqual(BenchmarkRecorder.CsvHeader, rows[0]);
            Assert.AreEqual(runner.Summary.Frames + 1, rows.Length);

            string summary = File.ReadAllText(Path.Combine(_dir, BenchmarkRunner.SummaryFile));
            StringAssert.Contains("gpu: ", summary);
            StringAssert.Contains("window: ", summary);
            StringAssert.Contains("city_build_ms: ", summary);
            StringAssert.Contains("p99_ms: ", summary);
            StringAssert.Contains("result: " + runner.Summary.Verdict.ToString().ToUpperInvariant(), summary);
        }

        [UnityTest]
        public IEnumerator TurnsVsyncOffWhileRunningAndRestoresItAfterwards()
        {
            _rig = new PlayerTestRig();
            _rig.PlaceAndSettle(new Vector3(0f, (float)CityLayout.SidewalkHeight + 0.1f, 0f));
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 60;
            Application.runInBackground = false;

            MakeRunner(new BenchmarkOptions(true, _dir, 0.1, 0.5), _ => { });
            yield return null;

            Assert.AreEqual(0, QualitySettings.vSyncCount);
            Assert.AreEqual(-1, Application.targetFrameRate);
            Assert.IsTrue(Application.runInBackground);

            Object.DestroyImmediate(_runnerGo);
            Assert.AreEqual(1, QualitySettings.vSyncCount);
            Assert.AreEqual(60, Application.targetFrameRate);
            Assert.IsFalse(Application.runInBackground);
        }

        [UnityTest]
        public IEnumerator PinsTheRequestedPresetAndTurnsAdaptiveOffWhileMeasuring()
        {
            _rig = new PlayerTestRig();
            _rig.PlaceAndSettle(new Vector3(0f, (float)CityLayout.SidewalkHeight + 0.1f, 0f));
            _quality = new QualityRig();
            yield return null;
            Assert.AreEqual(QualityPreset.Medium, _quality.Director.Preset);
            Assert.IsTrue(_quality.Director.Adaptive);

            MakeRunner(new BenchmarkOptions(true, _dir, 5, 5, QualityPreset.Low), _ => { });
            yield return null;

            Assert.AreEqual(QualityPreset.Low, _quality.Director.Preset);
            Assert.IsFalse(_quality.Director.Adaptive, "adaptive quality would change the preset mid-run");
            Assert.AreEqual(QualityTiers.For(QualityPreset.Low).InternalHeight, _quality.LowRes.TargetHeight);
        }

        [UnityTest]
        public IEnumerator WithoutAPresetItKeepsTheScenePresetButStillTurnsAdaptiveOff()
        {
            _rig = new PlayerTestRig();
            _rig.PlaceAndSettle(new Vector3(0f, (float)CityLayout.SidewalkHeight + 0.1f, 0f));
            _quality = new QualityRig();
            yield return null;

            MakeRunner(new BenchmarkOptions(true, _dir, 5, 5), _ => { });
            yield return null;

            Assert.AreEqual(QualityPreset.Medium, _quality.Director.Preset);
            Assert.IsFalse(_quality.Director.Adaptive);
        }

        [UnityTest]
        public IEnumerator RequestingAPresetWithoutADirectorEndsWithTheExceptionCode()
        {
            _rig = new PlayerTestRig();
            LogAssert.Expect(LogType.Exception, new Regex("needs a QualityDirector"));
            int? exit = null;
            MakeRunner(new BenchmarkOptions(true, _dir, 0.1, 0.5, QualityPreset.High), code => exit = code);
            yield return null;
            Assert.AreEqual(BenchmarkRunner.ExitException, exit);
        }

        [UnityTest]
        public IEnumerator HandsTheCharacterToThePilot()
        {
            _rig = new PlayerTestRig();
            _rig.PlaceAndSettle(new Vector3(0f, (float)CityLayout.SidewalkHeight + 0.1f, 0f));
            MakeRunner(new BenchmarkOptions(true, _dir, 5, 5), _ => { });
            yield return null;

            Assert.IsInstanceOf<PilotCommandSource>(_rig.Player.CommandSource);
        }

        [UnityTest]
        public IEnumerator AnExceptionDuringTheRunEndsItWithTheExceptionExitCode()
        {
            LogAssert.Expect(LogType.Exception, new Regex("needs a FirstPersonController"));
            int? exit = null;
            MakeRunner(new BenchmarkOptions(true, _dir, 0.1, 0.5), code => exit = code);   // no player in the scene
            yield return null;

            Assert.AreEqual(BenchmarkRunner.ExitException, exit);
        }

        [Test]
        public void ConfigureRejectsNulls()
        {
            _runnerGo = new GameObject("Nulls");
            _runnerGo.SetActive(false);
            var runner = _runnerGo.AddComponent<BenchmarkRunner>();
            Assert.Throws<System.ArgumentNullException>(() => runner.Configure(null, _ => { }, false));
            Assert.Throws<System.ArgumentNullException>(() => runner.Configure(BenchmarkOptions.Disabled, null, false));
        }
    }
}
