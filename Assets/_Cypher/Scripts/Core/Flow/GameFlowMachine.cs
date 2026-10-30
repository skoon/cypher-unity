using System;

namespace Cypher.Core
{
    public enum FlowState
    {
        /// <summary>The program has started and nothing is ready yet.</summary>
        Boot,

        /// <summary>Building the city and everything the first frame needs.</summary>
        Loading,

        /// <summary>The title screen, over the live city.</summary>
        Title,

        Playing,

        Paused,
    }

    public enum FlowCommand
    {
        Booted,
        LoadingFinished,
        NewGame,

        /// <summary>From the title: carry on the saved game (the game decides whether there is one).</summary>
        Continue,

        Pause,
        Resume,

        /// <summary>The Esc key: pauses while playing, resumes while paused, and does nothing anywhere else.</summary>
        TogglePause,

        ReturnToTitle,
        Quit,
    }

    public readonly struct FlowTransition
    {
        public readonly FlowState From;
        public readonly FlowState To;
        public readonly FlowCommand Command;

        /// <summary>True when this enters <see cref="FlowState.Playing"/> for a fresh game (spawn the player), false when resuming.</summary>
        public readonly bool NewSession;

        public FlowTransition(FlowState from, FlowState to, FlowCommand command, bool newSession)
        {
            From = from;
            To = to;
            Command = command;
            NewSession = newSession;
        }
    }

    /// <summary>
    /// The game's top-level state: Boot, Loading, Title, Playing and Paused, and which commands move between them. Pure, so the
    /// rules are tested without Unity. What each state means for the cursor, time, the player and the camera is
    /// <see cref="FlowPolicy"/>; making it so is the game component's job.
    /// </summary>
    public sealed class GameFlowMachine
    {
        readonly bool _skipTitle;

        /// <param name="skipTitle">Go from Loading straight to Playing, for benchmarks and scripted runs.</param>
        public GameFlowMachine(bool skipTitle = false)
        {
            _skipTitle = skipTitle;
        }

        public FlowState State { get; private set; } = FlowState.Boot;

        /// <summary>Set once <see cref="FlowCommand.Quit"/> is accepted; the state itself does not change.</summary>
        public bool QuitRequested { get; private set; }

        public event Action<FlowTransition> Transitioned;

        public event Action QuitIssued;

        /// <summary>True when <paramref name="command"/> does something in the current state.</summary>
        public bool Can(FlowCommand command) => Resolve(command, out _, out _);

        /// <summary>Apply the command if it means something now; false when it does not (Esc on the title screen is not an error).</summary>
        public bool TryHandle(FlowCommand command)
        {
            if (!Resolve(command, out FlowState to, out bool quit)) return false;

            if (quit)
            {
                QuitRequested = true;
                QuitIssued?.Invoke();
                return true;
            }

            FlowState from = State;
            bool newSession = to == FlowState.Playing && from != FlowState.Paused;
            State = to;
            Transitioned?.Invoke(new FlowTransition(from, to, command, newSession));
            return true;
        }

        /// <summary>Apply the command or throw: for callers that know the command must be valid, where a miss is a bug.</summary>
        public void Handle(FlowCommand command)
        {
            if (!TryHandle(command)) throw new InvalidOperationException($"{command} is not valid while {State}.");
        }

        bool Resolve(FlowCommand command, out FlowState to, out bool quit)
        {
            quit = false;
            to = State;
            switch (State)
            {
                case FlowState.Boot:
                    if (command == FlowCommand.Booted) { to = FlowState.Loading; return true; }
                    return false;

                case FlowState.Loading:
                    if (command == FlowCommand.LoadingFinished) { to = _skipTitle ? FlowState.Playing : FlowState.Title; return true; }
                    return false;

                case FlowState.Title:
                    if (command == FlowCommand.NewGame || command == FlowCommand.Continue) { to = FlowState.Playing; return true; }
                    break;

                case FlowState.Playing:
                    // Quitting goes through the pause screen: no key pressed by accident during play may end the game.
                    if (command == FlowCommand.Pause || command == FlowCommand.TogglePause) { to = FlowState.Paused; return true; }
                    return false;

                case FlowState.Paused:
                    if (command == FlowCommand.Resume || command == FlowCommand.TogglePause) { to = FlowState.Playing; return true; }
                    if (command == FlowCommand.ReturnToTitle) { to = FlowState.Title; return true; }
                    break;
            }

            if (command == FlowCommand.Quit)
            {
                quit = true;
                return true;
            }
            return false;
        }
    }

    /// <summary>What the world must look like in a flow state.</summary>
    public readonly struct FlowRules
    {
        /// <summary>The first-person controller runs. Off while paused because a controller ticking at time scale 0 would be asked to move for no time.</summary>
        public readonly bool PlayerActive;

        public readonly bool CursorLocked;

        /// <summary>The camera flies the title route instead of riding the player.</summary>
        public readonly bool TitleCameraActive;

        /// <summary>The adaptive quality governor judges frames; menus and loading are not a fair sample.</summary>
        public readonly bool AdaptiveQualityActive;

        public readonly double TimeScale;

        public FlowRules(bool playerActive, bool cursorLocked, bool titleCameraActive, bool adaptiveQualityActive, double timeScale)
        {
            PlayerActive = playerActive;
            CursorLocked = cursorLocked;
            TitleCameraActive = titleCameraActive;
            AdaptiveQualityActive = adaptiveQualityActive;
            TimeScale = timeScale;
        }
    }

    /// <summary>A screen of the game open over play: none, a menu that stops the world (inventory, character, journal, vendor), or an interaction that only stops the player (a conversation, a breach, whose timer runs).</summary>
    public enum FlowModal
    {
        None,
        Menu,
        Interaction,
    }

    public static class FlowPolicy
    {
        /// <summary>The rules of a state with a game screen open. A modal only means anything while playing; elsewhere it is ignored.</summary>
        public static FlowRules For(FlowState state, FlowModal modal)
        {
            FlowRules rules = For(state);
            if (state != FlowState.Playing) return rules;
            switch (modal)
            {
                case FlowModal.None: return rules;
                case FlowModal.Menu: return new FlowRules(playerActive: false, cursorLocked: false, titleCameraActive: false, adaptiveQualityActive: false, timeScale: 0);
                case FlowModal.Interaction: return new FlowRules(playerActive: false, cursorLocked: false, titleCameraActive: false, adaptiveQualityActive: false, timeScale: 1);
                default: throw new ArgumentOutOfRangeException(nameof(modal), modal, "unknown modal");
            }
        }

        public static FlowRules For(FlowState state)
        {
            switch (state)
            {
                case FlowState.Boot:
                case FlowState.Loading:
                case FlowState.Title:
                    return new FlowRules(playerActive: false, cursorLocked: false, titleCameraActive: true, adaptiveQualityActive: false, timeScale: 1);
                case FlowState.Playing:
                    return new FlowRules(playerActive: true, cursorLocked: true, titleCameraActive: false, adaptiveQualityActive: true, timeScale: 1);
                case FlowState.Paused:
                    return new FlowRules(playerActive: false, cursorLocked: false, titleCameraActive: false, adaptiveQualityActive: false, timeScale: 0);
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, "unknown flow state");
            }
        }
    }
}
