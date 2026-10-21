using System.Collections;
using System.Collections.Generic;
using Cypher.Perf;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    public class PerfOverlayTests
    {
        readonly List<Object> _owned = new List<Object>();
        readonly List<InputDevice> _devices = new List<InputDevice>();
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehavior;

        [SetUp]
        public void SetUp()
        {
            _savedBackground = InputSystem.settings.backgroundBehavior;
            _savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _owned)
            {
                if (o) Object.DestroyImmediate(o);
            }
            _owned.Clear();
            foreach (InputDevice d in _devices) InputSystem.RemoveDevice(d);
            _devices.Clear();
            InputSystem.settings.backgroundBehavior = _savedBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = _savedEditorBehavior;
        }

        PerfOverlay MakeOverlay()
        {
            var go = new GameObject("OverlayTest");
            _owned.Add(go);
            return go.AddComponent<PerfOverlay>();
        }

        [UnityTest]
        public IEnumerator ShowsPlaceholdersBeforeAnyFrameThenLiveNumbers()
        {
            PerfOverlay overlay = MakeOverlay();
            StringAssert.StartsWith("-- FPS", overlay.Text);

            float end = Time.realtimeSinceStartup + 0.8f;
            while (Time.realtimeSinceStartup < end) yield return null;

            Assert.Greater(overlay.Stats.Count, 3);
            StringAssert.Contains(" FPS", overlay.Text);
            StringAssert.Contains("worst", overlay.Text);
            StringAssert.Contains("p99", overlay.Text);
            StringAssert.Contains("draws", overlay.Text);
            StringAssert.DoesNotStartWith("--", overlay.Text);
        }

        [UnityTest]
        public IEnumerator ColourFollowsTheThirtyFpsGate()
        {
            PerfOverlay overlay = MakeOverlay();
            yield return null;

            overlay.Stats.Reset();
            for (int i = 0; i < 100; i++) overlay.Stats.AddFrame(1.0 / 60.0);
            overlay.Refresh();
            Assert.Greater(overlay.TextColor.g, overlay.TextColor.r, "green when the gate holds");

            overlay.Stats.Reset();
            for (int i = 0; i < 100; i++) overlay.Stats.AddFrame(1.0 / 20.0);
            overlay.Refresh();
            Assert.Greater(overlay.TextColor.r, overlay.TextColor.g, "red below the gate");
            StringAssert.Contains("BELOW 30", overlay.Text);
        }

        [UnityTest]
        public IEnumerator RenderCountersExistInTheEditor()
        {
            // The Editor creates the counters but only a Development player fills them in (draw calls and
            // triangles are 0 here); the benchmark summary of a built player is what proves the values.
            PerfOverlay overlay = MakeOverlay();
            yield return null;

            Assert.IsTrue(overlay.Counters.Available, "the profiler counters should exist in the Editor");
            Assert.GreaterOrEqual(overlay.Counters.DrawCalls, 0);
            Assert.GreaterOrEqual(overlay.Counters.Triangles, 0);
        }

        [UnityTest]
        public IEnumerator TheFirstDeltaAfterLoadingIsNotCountedAsAFrame()
        {
            PerfOverlay overlay = MakeOverlay();
            yield return null;
            Assert.AreEqual(0, overlay.Stats.Count, "the first update only marks the end of loading");
            yield return null;
            Assert.AreEqual(1, overlay.Stats.Count);
        }

        [Test]
        public void VisibleSwitchesTheLabelOnAndOff()
        {
            PerfOverlay overlay = MakeOverlay();
            Assert.IsTrue(overlay.Visible, "on by default");
            overlay.Visible = false;
            Assert.IsFalse(overlay.Visible);
            overlay.Visible = true;
            Assert.IsTrue(overlay.Visible);
        }

        [UnityTest]
        public IEnumerator F3TogglesTheOverlay()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            _devices.Add(keyboard);
            PerfOverlay overlay = MakeOverlay();
            yield return null;
            Assert.IsTrue(overlay.Visible);

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F3));
            yield return null;
            Assert.IsFalse(overlay.Visible, "first press hides it");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F3));
            yield return null;
            Assert.IsTrue(overlay.Visible, "second press shows it again");
        }
    }
}
