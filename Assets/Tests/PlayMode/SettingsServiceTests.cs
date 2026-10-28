#if UNITY_EDITOR
using System.Collections;
using System.IO;
using Cypher.Core;
using Cypher.Game;
using Cypher.Perf;
using Cypher.Player;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>Loads and applies the player's settings in the real scene; the file itself is an in-memory stand-in.</summary>
    public class SettingsServiceTests
    {
        MemorySettingsStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _storage = new MemorySettingsStorage();
            SliceTestScene.Isolate(_storage);
        }

        [TearDown]
        public void TearDown() => SliceTestScene.Restore();

        static SettingsService Service => Object.FindFirstObjectByType<SettingsService>();
        static FirstPersonController Player => Object.FindFirstObjectByType<FirstPersonController>();
        static QualityDirector Quality => Object.FindFirstObjectByType<QualityDirector>();

        static GameSettings Custom()
        {
            var s = new GameSettings();
            s.Graphics.Quality = QualityPreset.High;
            s.Graphics.FieldOfView = 90;
            s.Graphics.Weather = false;
            s.Graphics.ShowPerfOverlay = false;
            s.Graphics.InternalHeight = 720;
            s.Gameplay.Sensitivity = 2;
            s.Gameplay.InvertY = true;
            return s;
        }

        static string Json_(GameSettings s) => Json.Write(SettingsSerializer.ToTree(s, SettingsStore.CurrentVersion));

        [UnityTest]
        public IEnumerator WithNoFileTheDefaultsAreAppliedAndNothingIsWritten()
        {
            yield return SliceTestScene.Load();
            SettingsService service = Service;

            Assert.AreEqual(SettingsLoadOutcome.Defaults, service.LoadResult.Outcome);
            Assert.IsNull(service.Notice);
            Assert.IsTrue(service.Current.SameValues(new GameSettings()));
            Assert.AreEqual(QualityPreset.Medium, Quality.Preset);
            Assert.AreEqual(70f, Camera.main.fieldOfView, 1e-4f);
            Assert.IsTrue(Object.FindFirstObjectByType<RainController>().Raining);
            Assert.IsTrue(Object.FindFirstObjectByType<PerfOverlay>().Visible);
            Assert.IsFalse(_storage.Exists, "a settings file appears only when something is saved");
        }

        [UnityTest]
        public IEnumerator ASavedFileIsAppliedToTheWholeScene()
        {
            _storage.WriteAll(Json_(Custom()));
            yield return SliceTestScene.Load();

            Assert.AreEqual(SettingsLoadOutcome.Loaded, Service.LoadResult.Outcome);
            Assert.AreEqual(QualityPreset.High, Quality.Preset);
            Assert.AreEqual(720, Object.FindFirstObjectByType<LowResController>().TargetHeight, "the pinned resolution");
            Assert.AreEqual(2, Quality.MsaaSamples, "High's anti-aliasing");
            Assert.AreEqual(90f, Camera.main.fieldOfView, 1e-4f);
            Assert.IsFalse(Object.FindFirstObjectByType<RainController>().Raining);
            Assert.IsFalse(Object.FindFirstObjectByType<PerfOverlay>().Visible);
            Assert.AreEqual((float)(LookState.DefaultSensitivity * 2), Player.Sensitivity, 1e-7f);
            Assert.IsTrue(Player.InvertY);
        }

        [UnityTest]
        public IEnumerator AnUnusableFileIsSetAsideTheDefaultsAreUsedAndThePlayerIsTold()
        {
            _storage.WriteAll("{ this is not json");
            yield return SliceTestScene.Load();

            Assert.AreEqual(SettingsLoadOutcome.Recovered, Service.LoadResult.Outcome);
            StringAssert.Contains("default settings are in use", Service.Notice);
            Assert.AreEqual(QualityPreset.Medium, Quality.Preset);
            Assert.AreEqual(1, _storage.SetAsideLog.Count);
            Assert.AreEqual("bad: { this is not json", _storage.SetAsideLog[0]);
        }

        [UnityTest]
        public IEnumerator ApplyTakesEffectAtOnceAndSaveWritesTheFile()
        {
            yield return SliceTestScene.Load();
            SettingsService service = Service;
            GameSettings changed = service.Current;
            changed.Graphics.Quality = QualityPreset.Low;
            changed.Graphics.FieldOfView = 100;
            changed.Gameplay.InvertY = true;
            GameSettings seenByEvent = null;
            service.Applied += s => seenByEvent = s;

            service.Apply(changed);

            Assert.AreEqual(QualityPreset.Low, Quality.Preset);
            Assert.AreEqual(100f, Camera.main.fieldOfView, 1e-4f);
            Assert.IsTrue(Player.InvertY);
            Assert.IsTrue(changed.SameValues(service.Current));
            Assert.IsTrue(changed.SameValues(seenByEvent));
            Assert.IsFalse(_storage.Exists, "applying is not saving");

            service.Save();
            Assert.IsTrue(_storage.Exists);
            Assert.IsTrue(changed.SameValues(new SettingsStore(_storage).Load().Settings));
        }

        [UnityTest]
        public IEnumerator CurrentIsACopyThatChangesNothingUntilApplied()
        {
            yield return SliceTestScene.Load();
            GameSettings copy = Service.Current;
            copy.Graphics.FieldOfView = 105;
            Assert.AreEqual(70.0, Service.Current.Graphics.FieldOfView);
            Assert.AreEqual(70f, Camera.main.fieldOfView, 1e-4f);
        }

        [UnityTest]
        public IEnumerator ALoweredPresetIsSavedAndTheNoticeShown()
        {
            var ultra = new GameSettings();
            ultra.Graphics.Quality = QualityPreset.Ultra;
            _storage.WriteAll(Json_(ultra));
            yield return SliceTestScene.LoadAndPlay();

            string noticed = null;
            Service.NoticeRaised += n => noticed = n;
            Quality.enabled = false;   // the test supplies the frame times
            for (int i = 0; i < 20 * 8; i++) Quality.Sample(1.0 / 20.0);

            Assert.Less((int)Quality.Preset, (int)QualityPreset.Ultra);
            StringAssert.Contains("Quality lowered from Ultra to", noticed);
            Assert.AreEqual(noticed, Service.Notice);
            Assert.AreEqual(Quality.Preset, Service.Current.Graphics.Quality);
            Assert.AreEqual(Quality.Preset, new SettingsStore(_storage).Load().Settings.Graphics.Quality, "and it is on disk, so the next launch starts there");
        }

        [UnityTest]
        public IEnumerator ChoosingAPresetByHandIsNotAnAdaptiveNotice()
        {
            yield return SliceTestScene.Load();
            string noticed = null;
            Service.NoticeRaised += n => noticed = n;
            GameSettings changed = Service.Current;
            changed.Graphics.Quality = QualityPreset.Ultra;
            Service.Apply(changed);
            Assert.IsNull(noticed);
            Assert.IsFalse(_storage.Exists, "and nothing was saved behind the player's back");
        }

        [UnityTest]
        public IEnumerator ABenchmarkLaunchUsesDefaultsAndNeverTouchesTheRealFile()
        {
            SettingsService.StorageFactory = null;   // fall through to the real choice of storage
            LaunchOptions.Override = new FlowOptions(skipTitle: true, ignoreUserSettings: true, noAutoPause: true, noAudio: true);
            string path = SettingsService.DefaultPath();
            bool existed = File.Exists(path);
            System.DateTime stamp = existed ? File.GetLastWriteTimeUtc(path) : default;

            yield return SliceTestScene.Load();
            Assert.AreEqual(SettingsLoadOutcome.Defaults, Service.LoadResult.Outcome);
            Assert.IsTrue(Service.Current.SameValues(new GameSettings()));
            Service.Save();

            Assert.AreEqual(existed, File.Exists(path), "no file was created");
            if (existed) Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(path), "and the existing one was not rewritten");
        }

        [UnityTest]
        public IEnumerator ApplyingBeforeStartIsAnError()
        {
            var go = new GameObject("EarlyService");
            var service = go.AddComponent<SettingsService>();
            Assert.Throws<System.InvalidOperationException>(() => service.Apply(new GameSettings()));
            Object.DestroyImmediate(go);
            yield break;
        }

        [UnityTest]
        public IEnumerator ARebindIsSavedAtOnceAndComesBackNextLaunch()
        {
            yield return SliceTestScene.Load();
            Assert.IsFalse(_storage.Exists);
            Assert.AreEqual(AssignOutcome.Applied, Service.Bindings.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/k").Outcome);
            Assert.IsTrue(_storage.Exists, "a rebind is saved the moment it is made");
            StringAssert.Contains("jump:0", _storage.ReadAll());

            yield return SliceTestScene.Load();   // a fresh start of the game, reading the same file
            Assert.AreEqual("<Keyboard>/k", Service.Bindings.Get(GameAction.Jump, BindingSlot.Primary));
            Assert.AreEqual(SettingsLoadOutcome.Loaded, Service.LoadResult.Outcome);
            Assert.IsNull(Service.Notice);
        }

        [UnityTest]
        public IEnumerator ASwapIsSavedAsOneConsistentChange()
        {
            yield return SliceTestScene.Load();
            Service.Bindings.SwapWith(GameAction.MoveForward, BindingSlot.Primary, "<Keyboard>/a");
            yield return SliceTestScene.Load();
            Assert.AreEqual("<Keyboard>/a", Service.Bindings.Get(GameAction.MoveForward, BindingSlot.Primary));
            Assert.AreEqual("<Keyboard>/w", Service.Bindings.Get(GameAction.MoveLeft, BindingSlot.Primary));
            Assert.IsNull(Service.Notice, "a swap reads back as a consistent whole, with no complaint");
        }

        [UnityTest]
        public IEnumerator ApplyingAnOlderCopyOfTheSettingsDoesNotUndoARebind()
        {
            yield return SliceTestScene.Load();
            GameSettings stale = Service.Current;                 // taken before the rebind, as an Options screen would
            Service.Bindings.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/k");

            stale.Graphics.FieldOfView = 95;
            Service.Apply(stale);

            Assert.AreEqual("<Keyboard>/k", Service.Bindings.Get(GameAction.Jump, BindingSlot.Primary), "the rebind survives");
            Assert.AreEqual(95f, Camera.main.fieldOfView, 1e-4f, "while the rest of the copy applied");
            Assert.AreEqual("<Keyboard>/k", Service.Current.Controls.Overrides["jump:0"]);
            Service.Save();
            Assert.AreEqual("<Keyboard>/k", new SettingsStore(_storage).Load().Settings.Controls.Overrides["jump:0"]);
        }

        [UnityTest]
        public IEnumerator ControlsInAFileThatMakeNoSenseAreResetAndTheNoticeNamesThem()
        {
            _storage.WriteAll("{\"version\":1,\"controls\":{\"overrides\":{\"jump:9\":\"<Keyboard>/k\",\"crouch:1\":\"<Gamepad>/buttonNorth\",\"sprint:1\":\"<Mouse>/middleButton\"}}}");
            yield return SliceTestScene.Load();

            StringAssert.Contains("could not be used and were reset", Service.Notice);
            StringAssert.Contains("'jump:9'", Service.Notice);
            StringAssert.Contains("Crouch", Service.Notice);
            Assert.AreEqual("<Mouse>/middleButton", Service.Bindings.Get(GameAction.Sprint, BindingSlot.Secondary), "the one good entry still applied");
            Assert.IsTrue(Service.Bindings.IsDefault(GameAction.Crouch));
            Assert.IsFalse(Service.Current.Controls.Overrides.ContainsKey("jump:9"), "and the bad entries are gone from what will be saved");
        }

        [UnityTest]
        public IEnumerator ContradictoryControlsInAFileAllFallBackToDefaults()
        {
            _storage.WriteAll("{\"version\":1,\"controls\":{\"overrides\":{\"moveLeft:0\":\"<Keyboard>/w\"}}}");
            yield return SliceTestScene.Load();
            StringAssert.Contains("contradict each other", Service.Notice);
            Assert.AreEqual("<Keyboard>/a", Service.Bindings.Get(GameAction.MoveLeft, BindingSlot.Primary));
            Assert.AreEqual("<Keyboard>/w", Service.Bindings.Get(GameAction.MoveForward, BindingSlot.Primary));
        }

        [UnityTest]
        public IEnumerator ThePlayerWalksOnTheKeysTheFileSays()
        {
            var changed = new GameSettings();
            changed.Controls.SetOverride("moveForward:0", "<Keyboard>/o");
            changed.Controls.SetOverride("moveForward:1", ControlCatalog.Unbound);   // no arrow key either
            _storage.WriteAll(Json_(changed));
            using (var input = new VirtualInput())
            {
                yield return SliceTestScene.LoadAndPlay();
                float settle = Time.realtimeSinceStartup + 5f;
                while (!Player.Motor.Grounded && Time.realtimeSinceStartup < settle) yield return null;

                float z0 = Player.transform.position.z;
                input.Hold(UnityEngine.InputSystem.Key.W);   // the old forward key
                float until = Time.realtimeSinceStartup + 0.4f;
                while (Time.realtimeSinceStartup < until) yield return null;
                input.ReleaseAll();
                yield return null;
                Assert.AreEqual(z0, Player.transform.position.z, 0.1f, "W no longer walks forward");

                input.Hold(UnityEngine.InputSystem.Key.O);
                until = Time.realtimeSinceStartup + 0.6f;
                while (Time.realtimeSinceStartup < until) yield return null;
                input.ReleaseAll();
                Assert.Greater(Player.transform.position.z, z0 + 0.5f, "O does");
            }
        }

        [Test]
        public void WindowModesMapToUnitysFullScreenModes()
        {
            Assert.AreEqual(FullScreenMode.Windowed, SettingsService.MapWindow(WindowMode.Windowed));
            Assert.AreEqual(FullScreenMode.FullScreenWindow, SettingsService.MapWindow(WindowMode.Borderless));
            Assert.AreEqual(FullScreenMode.ExclusiveFullScreen, SettingsService.MapWindow(WindowMode.Fullscreen));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SettingsService.MapWindow((WindowMode)9));
        }

        [Test]
        public void TheEditorKeepsItsSettingsInAFileOfItsOwn()
        {
            string path = SettingsService.DefaultPath().Replace('\\', '/');
            StringAssert.EndsWith("/Cypher/CYPHER/" + SettingsService.EditorFileName, path);
        }
    }
}
#endif
