#if UNITY_EDITOR
using System;
using System.Collections;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Cypher.Tests
{
    /// <summary>
    /// Shared set-up for tests of the menus in the real scene: settings live in memory, a virtual keyboard, mouse and gamepad exist, and
    /// the helpers move, submit and click the way the engine's UI input would. (UI Toolkit ignores input while the window is unfocused,
    /// as a batch-mode Editor is, so navigation is sent as events straight to the focused element; the game's own Esc and tab actions,
    /// and key capture, do receive virtual key presses.)
    /// </summary>
    public abstract class UiTestBase
    {
        protected MemorySettingsStorage Storage { get; private set; }
        protected VirtualInput Input { get; private set; }

        [SetUp]
        public void BaseSetUp()
        {
            Storage = new MemorySettingsStorage();
            SliceTestScene.Isolate(Storage);
            Input = new VirtualInput();
        }

        [TearDown]
        public void BaseTearDown()
        {
            Input.Dispose();
            SliceTestScene.Restore();
        }

        protected static UiRoot Ui => UnityEngine.Object.FindFirstObjectByType<UiRoot>();
        protected static SettingsService Service => UnityEngine.Object.FindFirstObjectByType<SettingsService>();
        protected static GameFlow Flow => SliceTestScene.Flow;
        protected static VisualElement Focused => Ui.Root.panel.focusController.focusedElement as VisualElement;

        protected static IEnumerator WaitUntil(Func<bool> condition, float seconds, string failure)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(condition(), failure);
        }

        /// <summary>Load the scene and wait until the title is up, its menu has focus, and the loading screen has faded away.</summary>
        protected static IEnumerator ToTitle()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            yield return WaitUntil(() => !Ui.Loading.IsShown && Focused != null, 5f, "the title never showed with something focused");
        }

        /// <summary>Title, then Options (by pressing the entry), with the first control focused.</summary>
        protected IEnumerator ToOptions()
        {
            yield return ToTitle();
            yield return Nav(MoveDirection.Down);
            yield return Submit();
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 2f, "Options did not open");
            yield return WaitUntil(() => Focused != null && Ui.Options.Root.Contains(Focused), 2f, "nothing in Options took focus");
        }

        protected IEnumerator ToControlsTab()
        {
            yield return ToOptions();
            Ui.Options.SelectTab(3);
            Ui.Options.FocusFirstOfPage();
            yield return null;
        }

        /// <summary>The Esc action and the tab keys are the game's own, so real (virtual) key presses reach them.</summary>
        protected IEnumerator Press(Key key)
        {
            Input.Hold(key);
            yield return null;
            yield return null;
            Input.ReleaseAll();
            yield return null;
            yield return null;
        }

        protected static IEnumerator Nav(MoveDirection direction)
        {
            UiEvents.Move(Ui.Root, direction);
            yield return null;
        }

        protected static IEnumerator Submit()
        {
            UiEvents.Submit(Ui.Root);
            yield return null;
        }

        protected static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }
    }
}
#endif
