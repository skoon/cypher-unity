#if UNITY_EDITOR
using System.Collections;
using Cypher.Core;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cypher.Tests
{
    /// <summary>
    /// UI Toolkit only receives input while the application window is focused, which a batch-mode Editor never is, so tests cannot
    /// press keys at the menus. They dispatch the same navigation events the engine would produce straight to the focused element.
    /// This pins down how a direction maps to those events.
    /// </summary>
    static class UiEvents
    {
        public static VisualElement FocusedIn(VisualElement root) => root.panel.focusController.focusedElement as VisualElement;

        public static void Move(VisualElement root, MoveDirection direction)
        {
            Vector2 v;
            switch (direction)
            {
                case MoveDirection.Up: v = new Vector2(0f, 1f); break;
                case MoveDirection.Down: v = new Vector2(0f, -1f); break;
                case MoveDirection.Left: v = new Vector2(-1f, 0f); break;
                default: v = new Vector2(1f, 0f); break;
            }
            VisualElement target = FocusedIn(root) ?? root;
            using (NavigationMoveEvent e = NavigationMoveEvent.GetPooled(v))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        public static void Submit(VisualElement root)
        {
            VisualElement target = FocusedIn(root) ?? root;
            using (NavigationSubmitEvent e = NavigationSubmitEvent.GetPooled())
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        public static void Click(VisualElement target)
        {
            using (ClickEvent e = ClickEvent.GetPooled())
            {
                e.target = target;
                target.SendEvent(e);
            }
        }
    }

    public class UiInputDiagnostic
    {
        [UnityTest]
        public IEnumerator DirectionsMapToTheMatchingNavigationDirections()
        {
            SliceTestScene.Isolate();
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            yield return new WaitForSeconds(1.0f);
            VisualElement root = Object.FindFirstObjectByType<UiRoot>().Root;

            NavigationMoveEvent.Direction seen = NavigationMoveEvent.Direction.None;
            root.RegisterCallback<NavigationMoveEvent>(e => seen = e.direction, TrickleDown.TrickleDown);

            foreach ((MoveDirection ours, NavigationMoveEvent.Direction theirs) in new[]
            {
                (MoveDirection.Up, NavigationMoveEvent.Direction.Up),
                (MoveDirection.Down, NavigationMoveEvent.Direction.Down),
                (MoveDirection.Left, NavigationMoveEvent.Direction.Left),
                (MoveDirection.Right, NavigationMoveEvent.Direction.Right),
            })
            {
                seen = NavigationMoveEvent.Direction.None;
                UiEvents.Move(root, ours);
                Assert.AreEqual(theirs, seen, ours.ToString());
            }
            SliceTestScene.Restore();
        }
    }
}
#endif
