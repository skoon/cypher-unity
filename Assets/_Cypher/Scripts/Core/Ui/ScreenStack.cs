using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    public enum UiScreen
    {
        /// <summary>Nothing of the menus is showing: the game is being played.</summary>
        None,
        Loading,
        Title,
        Pause,
        Options,
        ConfirmTitle,
        ConfirmQuit,

        /// <summary>Waiting for the player to press the control they want.</summary>
        Rebind,

        /// <summary>The chosen control belongs to another action: swap or cancel.</summary>
        Conflict,
    }

    public enum BackResult
    {
        Nothing,
        ClosedOverlay,
        Resume,
        Pause,
    }

    /// <summary>
    /// Which menu screen is showing. The bottom of the stack follows the game flow (Loading, Title, Pause, or nothing while playing);
    /// Options and the dialogs open on top of it. Pure, so the rules, above all what Esc does in each situation, are tested.
    /// A change of flow state closes whatever was open.
    /// </summary>
    public sealed class ScreenStack
    {
        readonly List<UiScreen> _overlays = new List<UiScreen>();

        public FlowState FlowState { get; private set; } = FlowState.Boot;

        public UiScreen Base
        {
            get
            {
                switch (FlowState)
                {
                    case FlowState.Boot:
                    case FlowState.Loading: return UiScreen.Loading;
                    case FlowState.Title: return UiScreen.Title;
                    case FlowState.Paused: return UiScreen.Pause;
                    default: return UiScreen.None;
                }
            }
        }

        public IReadOnlyList<UiScreen> Overlays => _overlays;

        public UiScreen Top => _overlays.Count > 0 ? _overlays[_overlays.Count - 1] : Base;

        /// <summary>Follow the game flow. Any open overlay is closed: a new flow state starts clean.</summary>
        public void SetFlowState(FlowState state)
        {
            FlowState = state;
            _overlays.Clear();
        }

        /// <summary>True when <paramref name="screen"/> may open on top of what is showing now.</summary>
        public bool CanPush(UiScreen screen)
        {
            UiScreen top = Top;
            if (top == screen) return false;
            switch (screen)
            {
                case UiScreen.Options: return top == UiScreen.Title || top == UiScreen.Pause;
                case UiScreen.ConfirmQuit: return top == UiScreen.Title || top == UiScreen.Pause;
                case UiScreen.ConfirmTitle: return top == UiScreen.Pause;
                case UiScreen.Rebind: return top == UiScreen.Options;
                case UiScreen.Conflict: return top == UiScreen.Rebind;
                default: return false;   // None, Loading, Title and Pause come from the flow, not from the stack
            }
        }

        public bool TryPush(UiScreen screen)
        {
            if (!CanPush(screen)) return false;
            _overlays.Add(screen);
            return true;
        }

        /// <summary>Close the top overlay. False when there is none.</summary>
        public bool Pop()
        {
            if (_overlays.Count == 0) return false;
            _overlays.RemoveAt(_overlays.Count - 1);
            return true;
        }

        /// <summary>Swap the top overlay for another that is allowed over the one beneath it, e.g. the key prompt for the conflict question.</summary>
        public bool TryReplaceTop(UiScreen screen)
        {
            if (_overlays.Count == 0) return false;
            UiScreen removed = _overlays[_overlays.Count - 1];
            _overlays.RemoveAt(_overlays.Count - 1);
            // The conflict question takes the place of the key prompt that found the conflict, so it is allowed to replace it.
            bool allowed = CanPush(screen) || (screen == UiScreen.Conflict && removed == UiScreen.Rebind);
            _overlays.Add(allowed ? screen : removed);
            return allowed;
        }

        /// <summary>
        /// What Esc (or the gamepad's Back) does: close the top overlay if any; otherwise resume from the pause screen, pause while playing, and
        /// nothing on the title or while loading. Applies the stack's part of that and says what the caller must do for the rest.
        /// </summary>
        public BackResult Back()
        {
            if (Pop()) return BackResult.ClosedOverlay;
            switch (Base)
            {
                case UiScreen.Pause: return BackResult.Resume;
                case UiScreen.None: return BackResult.Pause;
                default: return BackResult.Nothing;
            }
        }
    }
}
