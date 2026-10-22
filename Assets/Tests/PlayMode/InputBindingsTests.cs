using System.Collections.Generic;
using Cypher.Core;
using Cypher.Player;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Cypher.Tests
{
    /// <summary>Rebinding changes what real (virtual) devices do, through the same command source the game uses.</summary>
    public class InputBindingsTests
    {
        VirtualInput _input;
        InputBindings _bindings;
        InputSystemCommandSource _source;

        [SetUp]
        public void SetUp()
        {
            _input = new VirtualInput();
            _bindings = new InputBindings();
            _source = new InputSystemCommandSource(_bindings);
        }

        [TearDown]
        public void TearDown()
        {
            _source.Dispose();
            _input.Dispose();
        }

        PlayerCommand Poll(double dt = 1.0 / 60)
        {
            InputSystem.Update();
            return _source.Poll(dt);
        }

        // ------------------------------------------------------------------ buttons

        [Test]
        public void ReboundJumpFollowsTheNewKeyAndTheOldKeyDoesNothing()
        {
            Assert.AreEqual(AssignOutcome.Applied, _bindings.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/j").Outcome);

            _input.Hold(Key.Space);
            Assert.IsFalse(Poll().JumpPressed, "Space is no longer jump");
            _input.ReleaseAll();
            Poll();

            _input.Hold(Key.J);
            Assert.IsTrue(Poll().JumpPressed, "J is");
        }

        [Test]
        public void ASwapMakesBothActionsFollowTheirNewKeys()
        {
            Assert.AreEqual(AssignOutcome.Conflict, _bindings.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/w").Outcome);
            Assert.AreEqual(AssignOutcome.Applied, _bindings.SwapWith(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/w").Outcome);

            _input.Hold(Key.W);
            PlayerCommand wPressed = Poll();
            Assert.IsTrue(wPressed.JumpPressed, "W jumps now");
            Assert.AreEqual(0.0, wPressed.Forward, 1e-6, "and no longer moves forward");
            _input.ReleaseAll();
            Poll();

            _input.Hold(Key.Space);
            PlayerCommand spacePressed = Poll();
            Assert.IsFalse(spacePressed.JumpPressed);
            Assert.AreEqual(1.0, spacePressed.Forward, 1e-6, "Space moves forward");
        }

        [Test]
        public void UnbindingASecondaryKeyStopsItWorkingAndLeavesThePrimaryAlone()
        {
            Assert.AreEqual(AssignOutcome.Applied, _bindings.Unbind(GameAction.Sprint, BindingSlot.Secondary).Outcome);

            _input.Hold(Key.RightShift);
            Assert.IsFalse(Poll().Sprint);
            _input.ReleaseAll();
            _input.Hold(Key.LeftShift);
            Assert.IsTrue(Poll().Sprint);
        }

        [Test]
        public void AMouseButtonCanDriveAnAction()
        {
            _bindings.Assign(GameAction.Jump, BindingSlot.Secondary, "<Mouse>/rightButton");
            _input.PressMouse(MouseButton.Right, true);
            Assert.IsTrue(Poll().JumpPressed);
        }

        [Test]
        public void AReboundGamepadButtonReplacesTheDefaultOne()
        {
            _bindings.Assign(GameAction.Jump, BindingSlot.Gamepad, "<Gamepad>/buttonNorth");

            _input.PressPad(GamepadButton.South, true);
            Assert.IsFalse(Poll().JumpPressed, "A no longer jumps");
            _input.PressPad(GamepadButton.South, false);
            Poll();

            _input.PressPad(GamepadButton.North, true);
            Assert.IsTrue(Poll().JumpPressed, "Y does");
        }

        [Test]
        public void CrouchAndSprintFollowTheirReboundKeysAsHeldStates()
        {
            _bindings.Assign(GameAction.Crouch, BindingSlot.Primary, "<Keyboard>/z");
            _bindings.Assign(GameAction.Sprint, BindingSlot.Primary, "<Keyboard>/x");
            _input.Hold(Key.Z);
            Assert.IsTrue(Poll().Crouch);
            Assert.IsTrue(Poll().Crouch, "still held");
            _input.Release(Key.Z);
            Assert.IsFalse(Poll().Crouch);
            _input.Hold(Key.X);
            Assert.IsTrue(Poll().Sprint);
        }

        // ------------------------------------------------------------------ movement

        [Test]
        public void ReboundMovementKeysMoveInTheRightDirections()
        {
            _bindings.Assign(GameAction.MoveForward, BindingSlot.Primary, "<Keyboard>/i");
            _bindings.Assign(GameAction.MoveBack, BindingSlot.Primary, "<Keyboard>/k");
            _bindings.Assign(GameAction.MoveLeft, BindingSlot.Primary, "<Keyboard>/j");
            _bindings.Assign(GameAction.MoveRight, BindingSlot.Primary, "<Keyboard>/l");

            _input.Hold(Key.I);
            Assert.AreEqual(1.0, Poll().Forward, 1e-6);
            _input.ReleaseAll();
            _input.Hold(Key.K);
            Assert.AreEqual(-1.0, Poll().Forward, 1e-6);
            _input.ReleaseAll();
            _input.Hold(Key.J);
            Assert.AreEqual(-1.0, Poll().Strafe, 1e-6);
            _input.ReleaseAll();
            _input.Hold(Key.L);
            Assert.AreEqual(1.0, Poll().Strafe, 1e-6);
        }

        [Test]
        public void TheOldMovementKeysStopButTheArrowKeysStillWork()
        {
            _bindings.Assign(GameAction.MoveForward, BindingSlot.Primary, "<Keyboard>/i");
            _input.Hold(Key.W);
            Assert.AreEqual(0.0, Poll().Forward, 1e-6, "W was rebound away");
            _input.ReleaseAll();
            _input.Hold(Key.UpArrow);
            Assert.AreEqual(1.0, Poll().Forward, 1e-6, "the secondary slot is untouched");
        }

        [Test]
        public void SwappingTwoMovementKeysSwapsTheirDirections()
        {
            _bindings.SwapWith(GameAction.MoveForward, BindingSlot.Primary, "<Keyboard>/s");   // forward gets S, back gets W
            _input.Hold(Key.S);
            Assert.AreEqual(1.0, Poll().Forward, 1e-6);
            _input.ReleaseAll();
            _input.Hold(Key.W);
            Assert.AreEqual(-1.0, Poll().Forward, 1e-6);
        }

        [Test]
        public void EmptyingAMovementSlotLeavesTheOtherSlotWorking()
        {
            _bindings.Unbind(GameAction.MoveForward, BindingSlot.Primary);
            _input.Hold(Key.W);
            Assert.AreEqual(0.0, Poll().Forward, 1e-6);
            _input.ReleaseAll();
            _input.Hold(Key.UpArrow);
            Assert.AreEqual(1.0, Poll().Forward, 1e-6);
        }

        [Test]
        public void DiagonalMovementOnReboundKeysIsStillClampedToUnitLength()
        {
            _bindings.Assign(GameAction.MoveForward, BindingSlot.Primary, "<Keyboard>/i");
            _bindings.Assign(GameAction.MoveRight, BindingSlot.Primary, "<Keyboard>/l");
            _input.Hold(Key.I);
            _input.Hold(Key.L);
            PlayerCommand c = Poll();
            Assert.AreEqual(1.0, System.Math.Sqrt(c.Forward * c.Forward + c.Strafe * c.Strafe), 1e-5);
        }

        [Test]
        public void TheLeftStickAndMouseLookAreNotAffectedByRebinding()
        {
            _bindings.Assign(GameAction.MoveForward, BindingSlot.Primary, "<Keyboard>/i");
            InputState.Change(_input.Pad.leftStick, new UnityEngine.Vector2(0f, 1f));
            Assert.AreEqual(1.0, Poll().Forward, 1e-5);
            InputState.Change(_input.Pad.leftStick, UnityEngine.Vector2.zero);
            InputSystem.QueueStateEvent(_input.Mouse, new MouseState { delta = new UnityEngine.Vector2(7f, 3f) });
            Assert.AreEqual(7.0, Poll().LookX, 1e-4);
        }

        // ------------------------------------------------------------------ reset and lifetime

        [Test]
        public void ResetPutsTheDefaultKeysBack()
        {
            _bindings.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/j");
            _bindings.Reset(GameAction.Jump);
            _input.Hold(Key.Space);
            Assert.IsTrue(Poll().JumpPressed);
        }

        [Test]
        public void ResetAllPutsEveryDefaultBack()
        {
            _bindings.SwapWith(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/w");
            _bindings.Assign(GameAction.Crouch, BindingSlot.Primary, "<Keyboard>/z");
            _bindings.ResetAll();
            _input.Hold(Key.W);
            PlayerCommand c = Poll();
            Assert.AreEqual(1.0, c.Forward, 1e-6);
            Assert.IsFalse(c.JumpPressed);
        }

        [Test]
        public void ADisposedSourceIgnoresLaterChangesWithoutThrowing()
        {
            _source.Dispose();
            Assert.DoesNotThrow(() => _bindings.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/j"));
            _source = new InputSystemCommandSource(_bindings);   // so TearDown has something to dispose
        }

        // ------------------------------------------------------------------ the shared object

        [Test]
        public void ChangedFiresOnlyWhenSomethingActuallyChanged()
        {
            int changes = 0;
            _bindings.Changed += () => changes++;

            _bindings.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/j");   // applied
            Assert.AreEqual(1, changes);
            _bindings.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/j");   // unchanged
            _bindings.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/w");   // conflict
            _bindings.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/escape");   // rejected
            _bindings.Unbind(GameAction.Jump, BindingSlot.Primary);                   // refused: the only key
            Assert.AreEqual(1, changes);

            _bindings.SwapWith(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/w");
            Assert.AreEqual(2, changes);
            _bindings.Reset(GameAction.Jump);
            Assert.AreEqual(3, changes);
            _bindings.ResetAll();
            Assert.AreEqual(4, changes);
        }

        [Test]
        public void StoredControlsRoundTripThroughTheSettings()
        {
            _bindings.SwapWith(GameAction.MoveLeft, BindingSlot.Primary, "<Keyboard>/d");
            _bindings.Assign(GameAction.Jump, BindingSlot.Secondary, "<Mouse>/middleButton");
            _bindings.Unbind(GameAction.Sprint, BindingSlot.Secondary);

            var controls = new ControlsOptions();
            _bindings.WriteTo(controls);
            Assert.AreEqual(4, controls.Overrides.Count);

            var other = new InputBindings();
            IReadOnlyList<string> problems = other.Load(controls);
            Assert.IsEmpty(problems);
            Assert.IsTrue(_bindings.Snapshot().SameValues(other.Snapshot()));
        }

        [Test]
        public void WritingDropsEntriesThatAreBackAtTheirDefaults()
        {
            var controls = new ControlsOptions();
            controls.SetOverride("jump:0", "<Keyboard>/j");
            _bindings.WriteTo(controls);   // nothing is changed in force
            Assert.AreEqual(0, controls.Overrides.Count);
        }

        [Test]
        public void LoadingBadEntriesKeepsTheGoodOnesAndNamesTheRest()
        {
            var controls = new ControlsOptions();
            controls.SetOverride("jump:1", "<Mouse>/middleButton");
            controls.SetOverride("fly:0", "<Keyboard>/f");
            var other = new InputBindings();
            IReadOnlyList<string> problems = other.Load(controls);
            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual("<Mouse>/middleButton", other.Get(GameAction.Jump, BindingSlot.Secondary));
        }

        [Test]
        public void LoadingRaisesChangedSoReadersRebuild()
        {
            int changes = 0;
            _bindings.Changed += () => changes++;
            _bindings.Load(new ControlsOptions());
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void NullArgumentsFailLoudly()
        {
            Assert.Throws<System.ArgumentNullException>(() => new InputSystemCommandSource(null));
            Assert.Throws<System.ArgumentNullException>(() => _bindings.Load(null));
            Assert.Throws<System.ArgumentNullException>(() => _bindings.WriteTo(null));
        }
    }
}
