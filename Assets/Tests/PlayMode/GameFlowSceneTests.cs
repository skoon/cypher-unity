#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The flow, driven against the real saved scene: what each state does to the cursor, time, camera and player.</summary>
    public class GameFlowSceneTests
    {
        readonly List<InputDevice> _devices = new List<InputDevice>();
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehavior;

        [SetUp]
        public void SetUp()
        {
            SliceTestScene.Isolate();
            _savedBackground = InputSystem.settings.backgroundBehavior;
            _savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (InputDevice d in _devices) InputSystem.RemoveDevice(d);
            _devices.Clear();
            InputSystem.settings.backgroundBehavior = _savedBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = _savedEditorBehavior;
            SliceTestScene.Restore();
        }

        static FirstPersonController Player => Object.FindFirstObjectByType<FirstPersonController>();
        static TitleCamera TitleCam => Object.FindFirstObjectByType<TitleCamera>();
        static QualityDirector Quality => Object.FindFirstObjectByType<QualityDirector>();

        // ------------------------------------------------------------------ launch

        [UnityTest]
        public IEnumerator LaunchEndsLoadingOnTheTitleOverTheLiveCity()
        {
            yield return SliceTestScene.Load();
            GameFlow flow = SliceTestScene.Flow;
            yield return SliceTestScene.WaitFor(FlowState.Title);

            Assert.AreEqual(1.0, flow.LoadProgress, 1e-9);
            Assert.AreEqual("Ready", flow.LoadStatus);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
            Assert.IsFalse(SliceTestScene.Flow.CursorLockRequested);
            Assert.IsTrue(Cursor.visible);
            Assert.IsFalse(Player.enabled, "nobody controls the player on the title screen");
            Assert.IsTrue(TitleCam.enabled);
            Assert.AreSame(TitleCam.transform, Camera.main.transform.parent, "the camera rides the title flight");
            Assert.IsTrue(Quality.Suspended, "title frames are not a fair sample for adaptive quality");
        }

        [UnityTest]
        public IEnumerator TheCameraGlidesWhileTheTitleIsUp()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);

            Vector3 start = Camera.main.transform.position;
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Vector3 end = Camera.main.transform.position;

            Assert.Greater(Vector3.Distance(start, end), 1.5f, "about 3 m/s along the avenue");
            Assert.That(end.y, Is.InRange(7f, 9f), "eight metres up");
            Assert.IsTrue(CityLayout.IsOnRoad(end.x, end.z), "over the street, not over a building");
        }

        [UnityTest]
        public IEnumerator LoadingProgressNeverGoesBackwardsAndEndsAtOne()
        {
            yield return SliceTestScene.Load();
            GameFlow flow = SliceTestScene.Flow;
            double last = flow.LoadProgress;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (flow.State != FlowState.Title && Time.realtimeSinceStartup < deadline)
            {
                Assert.GreaterOrEqual(flow.LoadProgress, last - 1e-12);
                last = flow.LoadProgress;
                yield return null;
            }
            Assert.AreEqual(1.0, flow.LoadProgress, 1e-9);
        }

        [UnityTest]
        public IEnumerator SkipTitleStartsTheGameAsSoonAsLoadingEnds()
        {
            SliceTestScene.Isolate(options: new FlowOptions(skipTitle: true));
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Playing);
            yield return null;

            Assert.IsTrue(Player.enabled);
            Assert.AreSame(Player.transform, Camera.main.transform.parent);
            Assert.IsTrue(SliceTestScene.Flow.CursorLockRequested, "the engine only honours a lock with a focused window, so the request is what is checked");
            Assert.IsFalse(Quality.Suspended);
        }

        // ------------------------------------------------------------------ playing and pausing

        [UnityTest]
        public IEnumerator NewGameSpawnsThePlayerAndHandsTheCameraOver()
        {
            yield return SliceTestScene.LoadAndPlay();
            GameFlow flow = SliceTestScene.Flow;
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.IsTrue(Player.enabled);
            Assert.IsFalse(TitleCam.enabled);
            Assert.AreSame(Player.transform, Camera.main.transform.parent);
            Assert.AreEqual((float)PlayerMotor.EyeStand, Camera.main.transform.localPosition.y, 0.01f);
            Assert.IsTrue(SliceTestScene.Flow.CursorLockRequested, "the engine only honours a lock with a focused window, so the request is what is checked");
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(Quality.Suspended, "gameplay frames are what adaptive quality judges");

            float deadline = Time.realtimeSinceStartup + 5f;
            while (!Player.Motor.Grounded && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(Player.Motor.Grounded);
            Vector3 p = Player.transform.position;
            Assert.AreEqual(0f, p.x, 0.05f);
            Assert.AreEqual(0f, p.z, 0.05f);
        }

        [UnityTest]
        public IEnumerator PauseFreezesTimeFreesTheCursorAndStopsThePlayerAndResumeRestoresThem()
        {
            yield return SliceTestScene.LoadAndPlay();
            GameFlow flow = SliceTestScene.Flow;

            Assert.IsTrue(flow.Pause());
            Assert.AreEqual(FlowState.Paused, flow.State);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
            Assert.IsFalse(SliceTestScene.Flow.CursorLockRequested);
            Assert.IsTrue(Cursor.visible);
            Assert.IsFalse(Player.enabled, "the controller is off while paused");
            Assert.AreSame(Player.transform, Camera.main.transform.parent, "the view stays with the player behind the pause screen");
            Assert.IsTrue(Quality.Suspended);

            yield return null;
            yield return null;   // frames keep coming while paused, and nothing throws

            Assert.IsTrue(flow.Resume());
            Assert.AreEqual(FlowState.Playing, flow.State);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(SliceTestScene.Flow.CursorLockRequested, "the engine only honours a lock with a focused window, so the request is what is checked");
            Assert.IsTrue(Player.enabled);
            Assert.IsFalse(Quality.Suspended);
        }

        [UnityTest]
        public IEnumerator ResumingDoesNotRespawnThePlayer()
        {
            yield return SliceTestScene.LoadAndPlay();
            GameFlow flow = SliceTestScene.Flow;
            Player.Teleport(new Vector3(31f, 0.3f, -31f), 90f);
            Physics.SyncTransforms();

            flow.Pause();
            yield return null;
            flow.Resume();
            yield return null;

            Assert.AreEqual(31f, Player.transform.position.x, 0.1f);
            Assert.AreEqual(-31f, Player.transform.position.z, 0.1f);
        }

        [UnityTest]
        public IEnumerator ReturningToTheTitleBringsBackTheFlightAndANewGameRespawns()
        {
            yield return SliceTestScene.LoadAndPlay();
            GameFlow flow = SliceTestScene.Flow;
            Player.Teleport(new Vector3(31f, 0.3f, -31f), 90f);
            Physics.SyncTransforms();
            flow.Pause();

            Assert.IsTrue(flow.ReturnToTitle());
            Assert.AreEqual(FlowState.Title, flow.State);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
            Assert.IsFalse(SliceTestScene.Flow.CursorLockRequested);
            Assert.IsFalse(Player.enabled);
            Assert.IsTrue(TitleCam.enabled);
            Assert.AreSame(TitleCam.transform, Camera.main.transform.parent);

            Assert.IsTrue(flow.NewGame());
            yield return null;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!Player.Motor.Grounded && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(0f, Player.transform.position.x, 0.05f, "back on the plaza, not where the last game ended");
            Assert.AreEqual(0f, Player.transform.position.z, 0.05f);
            Assert.AreEqual(0.0, Player.Look.Yaw, 1e-6, "facing the way a new game faces");
            Assert.AreSame(Player.transform, Camera.main.transform.parent);
        }

        [UnityTest]
        public IEnumerator CommandsThatDoNotApplyDoNothing()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            GameFlow flow = SliceTestScene.Flow;
            Assert.IsFalse(flow.TogglePause(), "Esc on the title screen");
            Assert.IsFalse(flow.Resume());
            Assert.IsFalse(flow.ReturnToTitle());
            Assert.AreEqual(FlowState.Title, flow.State);
        }

        [UnityTest]
        public IEnumerator StateChangesAreAnnouncedWithTheirCause()
        {
            yield return SliceTestScene.LoadAndPlay();
            GameFlow flow = SliceTestScene.Flow;
            var seen = new List<FlowTransition>();
            flow.StateChanged += seen.Add;
            flow.Pause();
            flow.Resume();
            Assert.AreEqual(2, seen.Count);
            Assert.AreEqual(FlowState.Paused, seen[0].To);
            Assert.AreEqual(FlowState.Playing, seen[1].To);
            Assert.IsFalse(seen[1].NewSession);
        }

        // ------------------------------------------------------------------ focus and quitting

        [UnityTest]
        public IEnumerator LosingFocusPausesAPlayingGameButNotTheTitleAndRegainingItDoesNotResume()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            GameFlow flow = SliceTestScene.Flow;

            flow.HandleFocusChanged(false);
            Assert.AreEqual(FlowState.Title, flow.State, "nothing to pause on the title");

            flow.NewGame();
            flow.HandleFocusChanged(true);
            Assert.AreEqual(FlowState.Playing, flow.State);

            flow.HandleFocusChanged(false);
            Assert.AreEqual(FlowState.Paused, flow.State);
            flow.HandleFocusChanged(true);
            Assert.AreEqual(FlowState.Paused, flow.State, "the player resumes when ready");
        }

        [UnityTest]
        public IEnumerator NoAutoPauseKeepsPlayingWhenFocusIsLost()
        {
            SliceTestScene.Isolate(options: new FlowOptions(noAutoPause: true));
            yield return SliceTestScene.LoadAndPlay();
            SliceTestScene.Flow.HandleFocusChanged(false);
            Assert.AreEqual(FlowState.Playing, SliceTestScene.Flow.State);
        }

        [UnityTest]
        public IEnumerator QuitReachesTheQuitActionFromTheTitleAndFromThePauseScreen()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            GameFlow flow = SliceTestScene.Flow;
            int quits = 0;
            flow.QuitAction = () => quits++;

            Assert.IsTrue(flow.Quit());
            Assert.AreEqual(1, quits);

            flow.NewGame();
            Assert.IsFalse(flow.Quit(), "not while playing");
            flow.Pause();
            Assert.IsTrue(flow.Quit());
            Assert.AreEqual(2, quits);
        }
    }
}
#endif
