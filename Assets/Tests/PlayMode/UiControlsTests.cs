#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Cypher.Core;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The Controls page, end to end: the table, moving around it, and rebinding with real (virtual) key, mouse and gamepad presses.</summary>
    public class UiControlsTests : UiTestBase
    {
        OptionsScreen Options => Ui.Options;

        BindButton Button(GameAction a, BindingSlot s) => Options.Controls.Button(a, s);

        string StoredOverride(string key)
        {
            var overrides = new SettingsStore(Storage).Load().Settings.Controls.Overrides;
            return overrides.TryGetValue(key, out string v) ? v : null;
        }

        /// <summary>Open the key prompt for a slot the way the player does: focus its button and press it.</summary>
        IEnumerator OpenPrompt(GameAction action, BindingSlot slot)
        {
            for (int i = 0; i < 4; i++) yield return null;   // a closing dialog hands focus back a frame or two later: let that finish first
            Button(action, slot).Focus();
            yield return null;
            Assert.AreSame(Button(action, slot), Focused, "the button to press has the focus");
            yield return Submit();
            yield return WaitUntil(() => Ui.Top == UiScreen.Rebind && Ui.IsCapturing, 2f, "the key prompt did not open");
        }

        IEnumerator PressKeyForPrompt(Key key)
        {
            Input.Hold(key);
            yield return null;
            yield return null;
            Input.ReleaseAll();
            yield return null;
        }

        // ------------------------------------------------------------------ the table

        [UnityTest]
        public IEnumerator TheTableListsEveryActionWithItsCurrentControls()
        {
            yield return ToControlsTab();
            Assert.AreEqual("CONTROLS", Options.ActiveTitle);
            Assert.AreEqual("W", Button(GameAction.MoveForward, BindingSlot.Primary).Text);
            Assert.AreEqual("Up Arrow", Button(GameAction.MoveForward, BindingSlot.Secondary).Text);
            Assert.IsNull(Button(GameAction.MoveForward, BindingSlot.Gamepad), "movement has no gamepad binding: the stick moves");
            Assert.AreEqual("Space", Button(GameAction.Jump, BindingSlot.Primary).Text);
            Assert.AreEqual("—", Button(GameAction.Jump, BindingSlot.Secondary).Text, "an empty slot");
            Assert.AreEqual("A", Button(GameAction.Jump, BindingSlot.Gamepad).Text);
            Assert.AreEqual("Left Shift", Button(GameAction.Sprint, BindingSlot.Primary).Text);
            Assert.AreEqual("L3", Button(GameAction.Sprint, BindingSlot.Gamepad).Text);
            Assert.AreEqual("Left Ctrl", Button(GameAction.Crouch, BindingSlot.Primary).Text);
            Assert.AreEqual("C", Button(GameAction.Crouch, BindingSlot.Secondary).Text);
            Assert.AreEqual("B", Button(GameAction.Crouch, BindingSlot.Gamepad).Text);
            foreach (ActionInfo info in ControlCatalog.All)
            {
                Assert.IsFalse(Options.Controls.ResetButton(info.Action).enabledSelf, info.Id + " is at its defaults, so there is nothing to reset");
                Assert.IsFalse(Button(info.Action, BindingSlot.Primary).ClassListContains("cy-bind--changed"));
            }
        }

        [UnityTest]
        public IEnumerator ArrowKeysMoveAcrossTheTableAndSkipTheMissingGamepadCells()
        {
            yield return ToControlsTab();
            Service.Bindings.Assign(GameAction.MoveBack, BindingSlot.Primary, "<Keyboard>/y");   // gives that row a Reset button that can be reached
            yield return null;
            Assert.AreSame(Button(GameAction.MoveForward, BindingSlot.Primary), Focused);
            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Button(GameAction.MoveBack, BindingSlot.Primary), Focused);
            yield return Nav(MoveDirection.Right);
            Assert.AreSame(Button(GameAction.MoveBack, BindingSlot.Secondary), Focused);
            yield return Nav(MoveDirection.Right);
            Assert.AreSame(Options.Controls.ResetButton(GameAction.MoveBack), Focused, "the hole where a gamepad button would be is skipped");
            yield return Nav(MoveDirection.Left);
            Assert.AreSame(Button(GameAction.MoveBack, BindingSlot.Secondary), Focused);
            yield return Nav(MoveDirection.Right);
            yield return Nav(MoveDirection.Left);
            yield return Nav(MoveDirection.Left);
            yield return Nav(MoveDirection.Left);
            Assert.AreSame(Button(GameAction.MoveBack, BindingSlot.Primary), Focused, "the left edge stays put");

            for (int i = 0; i < 3; i++) yield return Nav(MoveDirection.Down);   // back, left, right, jump
            Assert.AreSame(Button(GameAction.Jump, BindingSlot.Primary), Focused);
            yield return Nav(MoveDirection.Right);
            yield return Nav(MoveDirection.Right);
            Assert.AreSame(Button(GameAction.Jump, BindingSlot.Gamepad), Focused);
            yield return Nav(MoveDirection.Up);
            Assert.AreSame(Button(GameAction.MoveRight, BindingSlot.Secondary), Focused, "up into a row without that column takes the nearest cell");
        }

        [UnityTest]
        public IEnumerator AResetButtonThatHasNothingToResetCannotBeFocused()
        {
            yield return ToControlsTab();
            Button(GameAction.MoveBack, BindingSlot.Secondary).Focus();
            yield return null;
            yield return Nav(MoveDirection.Right);
            Assert.AreSame(Button(GameAction.MoveBack, BindingSlot.Secondary), Focused, "the disabled Reset is not a place to go");
        }

        [UnityTest]
        public IEnumerator DownFromTheLastRowReachesTheFooterResetAllButton()
        {
            yield return ToControlsTab();
            var visited = new System.Collections.Generic.List<string>();
            for (int i = 0; i < ControlCatalog.All.Count - 1; i++)
            {
                yield return Nav(MoveDirection.Down);
                ActionInfo row = ControlCatalog.All.FirstOrDefault(a => Focused == Button(a.Action, BindingSlot.Primary));
                visited.Add(row?.Id ?? (Focused == null ? "nothing" : Focused.GetType().Name));
            }
            Assert.AreSame(Button(ControlCatalog.All[ControlCatalog.All.Count - 1].Action, BindingSlot.Primary), Focused, "visited " + string.Join(", ", visited));
            yield return Nav(MoveDirection.Down);
            Assert.AreSame(Options.ResetButton, Focused);
            Assert.AreEqual("RESET ALL CONTROLS", Options.ResetButton.Text);
        }

        // ------------------------------------------------------------------ the key prompt

        [UnityTest]
        public IEnumerator PressingABindingOpensTheKeyPromptAndNothingBehindItTakesKeys()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            Assert.AreEqual("REBIND JUMP", Ui.Dialog.Heading);
            StringAssert.Contains("key or mouse button", Ui.Dialog.Message);
            StringAssert.Contains("KEYBOARD / MOUSE", Ui.Dialog.Message);
            StringAssert.Contains("Esc cancels", Ui.Dialog.Message);
            Assert.IsFalse(Ui.Dialog.Buttons[0].focusable, "an Enter or Space meant for the prompt must not press a button");
            Assert.IsFalse(Ui.Dialog.Buttons[1].focusable);
            yield return WaitUntil(() => Focused == null, 2f, "nothing may hold focus while a key is being captured");
        }

        [UnityTest]
        public IEnumerator ClickingABindingOpensThePromptToo()
        {
            yield return ToControlsTab();
            UiEvents.Click(Button(GameAction.Sprint, BindingSlot.Secondary));
            Assert.AreEqual(UiScreen.Rebind, Ui.Top);
            Assert.IsTrue(Ui.IsCapturing);
            StringAssert.Contains("ALTERNATE", Ui.Dialog.Message);
        }

        [UnityTest]
        public IEnumerator AFreeKeyReplacesTheControlAndIsSavedAtOnce()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            yield return PressKeyForPrompt(Key.Y);
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 3f, "the prompt did not close after the key");

            Assert.AreEqual("<Keyboard>/y", Service.Bindings.Get(GameAction.Jump, BindingSlot.Primary));
            Assert.AreEqual("Y", Button(GameAction.Jump, BindingSlot.Primary).Text);
            Assert.IsTrue(Button(GameAction.Jump, BindingSlot.Primary).ClassListContains("cy-bind--changed"), "a control the player chose is marked");
            Assert.IsTrue(Options.Controls.ResetButton(GameAction.Jump).enabledSelf);
            Assert.AreEqual("<Keyboard>/y", StoredOverride("jump:0"), "saved the moment it was made, not on a timer");
            yield return WaitUntil(() => Focused == Button(GameAction.Jump, BindingSlot.Primary), 2f, "focus should come back to the button that was pressed");
            Assert.IsFalse(Ui.IsCapturing);
        }

        [UnityTest]
        public IEnumerator TheNewKeyReallyDrivesTheGame()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            yield return PressKeyForPrompt(Key.Y);
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 3f, "the prompt did not close after the key");
            Options.BackButton.Activate();

            Flow.NewGame();
            var player = Object.FindFirstObjectByType<Cypher.Player.FirstPersonController>();
            yield return WaitUntil(() => player.Motor.Grounded, 5f, "the player never landed");
            float before = player.transform.position.y;
            Input.Hold(Key.Y);
            float top = before;
            float until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until)
            {
                top = Mathf.Max(top, player.transform.position.y);
                yield return null;
            }
            Input.ReleaseAll();
            Assert.Greater(top, before + 0.3f, "J jumps");
        }

        [UnityTest]
        public IEnumerator EscCancelsThePromptWithoutAlsoClosingTheOptions()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            yield return Press(Key.Escape);

            Assert.AreEqual(UiScreen.Options, Ui.Top, "one Esc closes the prompt, not the prompt and the options");
            Assert.IsFalse(Ui.IsCapturing);
            Assert.IsTrue(Service.Bindings.IsDefault(GameAction.Jump), "and nothing was bound");
            Assert.AreEqual(FlowState.Title, Flow.State);
            yield return WaitUntil(() => Focused == Button(GameAction.Jump, BindingSlot.Primary), 2f, "focus returns to the button");
        }

        [UnityTest]
        public IEnumerator TheCancelButtonCancelsThePromptToo()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            Ui.Dialog.Buttons[1].Activate();
            yield return null;
            Assert.AreEqual(UiScreen.Options, Ui.Top);
            Assert.IsFalse(Ui.IsCapturing);
            Assert.IsTrue(Service.Bindings.IsDefault(GameAction.Jump));
        }

        [UnityTest]
        public IEnumerator AMouseButtonCanBeChosen()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Secondary);
            Input.PressMouse(MouseButton.Middle, true);
            yield return null;
            yield return null;
            Input.PressMouse(MouseButton.Middle, false);
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 3f, "the prompt did not close after the click");
            Assert.AreEqual("<Mouse>/middleButton", Service.Bindings.Get(GameAction.Jump, BindingSlot.Secondary));
            Assert.AreEqual("Middle Mouse", Button(GameAction.Jump, BindingSlot.Secondary).Text);
        }

        // ------------------------------------------------------------------ conflicts

        [UnityTest]
        public IEnumerator AKeyAnotherActionUsesAsksWhetherToSwapAndCancelChangesNothing()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            yield return PressKeyForPrompt(Key.W);
            yield return WaitUntil(() => Ui.Top == UiScreen.Conflict, 3f, "no conflict question");

            Assert.AreEqual("ALREADY IN USE", Ui.Dialog.Heading);
            StringAssert.Contains("W is already used by Move forward.", Ui.Dialog.Message);
            Assert.AreEqual("SWAP", Ui.Dialog.Buttons[0].Text);
            Assert.AreEqual("CANCEL", Ui.Dialog.Buttons[1].Text);
            yield return WaitUntil(() => Focused == Ui.Dialog.Buttons[1], 2f, "CANCEL is the safe default");
            Assert.AreEqual("<Keyboard>/space", Service.Bindings.Get(GameAction.Jump, BindingSlot.Primary), "nothing changes until the player says so");

            yield return Submit();   // CANCEL
            Assert.AreEqual(UiScreen.Options, Ui.Top);
            Assert.IsTrue(Service.Bindings.IsDefault(GameAction.Jump));
            Assert.IsTrue(Service.Bindings.IsDefault(GameAction.MoveForward));
            Assert.IsFalse(Storage.Exists, "and nothing was saved");
        }

        [UnityTest]
        public IEnumerator SwapGivesEachActionTheOthersKeyAndSavesIt()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            yield return PressKeyForPrompt(Key.W);
            yield return WaitUntil(() => Ui.Top == UiScreen.Conflict, 3f, "no conflict question");
            yield return WaitUntil(() => Focused == Ui.Dialog.Buttons[1], 2f, "the dialog never took focus");
            yield return Nav(MoveDirection.Left);
            Assert.AreSame(Ui.Dialog.Buttons[0], Focused);
            yield return Submit();   // SWAP

            Assert.AreEqual(UiScreen.Options, Ui.Top);
            Assert.AreEqual("<Keyboard>/w", Service.Bindings.Get(GameAction.Jump, BindingSlot.Primary));
            Assert.AreEqual("<Keyboard>/space", Service.Bindings.Get(GameAction.MoveForward, BindingSlot.Primary));
            Assert.AreEqual("W", Button(GameAction.Jump, BindingSlot.Primary).Text);
            Assert.AreEqual("Space", Button(GameAction.MoveForward, BindingSlot.Primary).Text);
            Assert.AreEqual("<Keyboard>/w", StoredOverride("jump:0"));
            Assert.AreEqual("<Keyboard>/space", StoredOverride("moveForward:0"));
        }

        [UnityTest]
        public IEnumerator TheGamepadsBButtonCanBeChosenSoItIsNotTreatedAsCancel()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Gamepad);
            StringAssert.Contains("gamepad button", Ui.Dialog.Message);
            StringAssert.Contains("Start or Esc cancels", Ui.Dialog.Message);
            Input.PressPad(GamepadButton.East, true);   // B: Crouch's default
            yield return null;
            yield return null;
            Input.PressPad(GamepadButton.East, false);
            yield return WaitUntil(() => Ui.Top == UiScreen.Conflict, 3f, "B should have been captured and found in use");
            StringAssert.Contains("B is already used by Crouch.", Ui.Dialog.Message);
        }

        [UnityTest]
        public IEnumerator AGamepadButtonIsCapturedInTheGamepadSlot()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Gamepad);
            Input.PressPad(GamepadButton.RightStick, true);
            yield return null;
            yield return null;
            Input.PressPad(GamepadButton.RightStick, false);
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 3f, "the prompt did not close");
            Assert.AreEqual("<Gamepad>/rightStickPress", Service.Bindings.Get(GameAction.Jump, BindingSlot.Gamepad));
            Assert.AreEqual("R3", Button(GameAction.Jump, BindingSlot.Gamepad).Text);
        }

        [UnityTest]
        public IEnumerator StartOnTheGamepadCancelsAGamepadPrompt()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Gamepad);
            Input.PressPad(GamepadButton.Start, true);
            yield return null;
            yield return null;
            Input.PressPad(GamepadButton.Start, false);
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 3f, "Start did not cancel the prompt");
            Assert.IsTrue(Service.Bindings.IsDefault(GameAction.Jump), "Start is not a candidate for the binding");
            Assert.AreEqual(0, Ui.Toasts.Shown.Count, "and no complaint about it");
        }

        // ------------------------------------------------------------------ refusals and clearing

        [UnityTest]
        public IEnumerator AReservedKeyIsRefusedWithAMessage()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            yield return PressKeyForPrompt(Key.Enter);
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 3f, "the prompt did not close");
            yield return WaitUntil(() => Ui.Toasts.Shown.Count > 0, 2f, "no message about the refusal");
            StringAssert.Contains("reserved by the game", Ui.Toasts.Shown[0]);
            Assert.IsTrue(Service.Bindings.IsDefault(GameAction.Jump));
        }

        [UnityTest]
        public IEnumerator ClearEmptiesASlotThatCanBeEmptied()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Sprint, BindingSlot.Secondary);
            Ui.Dialog.Buttons[0].Activate();   // CLEAR THIS BINDING
            yield return null;
            Assert.AreEqual(UiScreen.Options, Ui.Top);
            Assert.IsFalse(Ui.IsCapturing, "the capture was stopped");
            Assert.IsNull(Service.Bindings.Get(GameAction.Sprint, BindingSlot.Secondary));
            Assert.AreEqual("—", Button(GameAction.Sprint, BindingSlot.Secondary).Text);
            Assert.IsTrue(Button(GameAction.Sprint, BindingSlot.Secondary).ClassListContains("cy-bind--empty"));
            Assert.AreEqual("none", StoredOverride("sprint:1"));
        }

        [UnityTest]
        public IEnumerator ClearRefusesToEmptyTheLastKeyboardControlAndSaysWhy()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            Ui.Dialog.Buttons[0].Activate();
            yield return null;
            Assert.AreEqual(UiScreen.Options, Ui.Top);
            Assert.AreEqual("<Keyboard>/space", Service.Bindings.Get(GameAction.Jump, BindingSlot.Primary));
            yield return WaitUntil(() => Ui.Toasts.Shown.Count > 0, 2f, "no message about the refusal");
            StringAssert.Contains("at least one keyboard or mouse control", Ui.Toasts.Shown[0]);
        }

        // ------------------------------------------------------------------ resetting

        [UnityTest]
        public IEnumerator ResetOnARowPutsItsDefaultsBack()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            yield return PressKeyForPrompt(Key.Y);
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 3f, "the prompt did not close");

            Options.Controls.ResetButton(GameAction.Jump).Activate();
            Assert.IsTrue(Service.Bindings.IsDefault(GameAction.Jump));
            Assert.AreEqual("Space", Button(GameAction.Jump, BindingSlot.Primary).Text);
            Assert.IsFalse(Options.Controls.ResetButton(GameAction.Jump).enabledSelf, "nothing left to reset");
            Assert.IsNull(StoredOverride("jump:0"), "the saved file no longer records a difference");
        }

        [UnityTest]
        public IEnumerator ResetAllControlsPutsEveryDefaultBack()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            yield return PressKeyForPrompt(Key.Y);
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 3f, "the prompt did not close");
            yield return OpenPrompt(GameAction.Crouch, BindingSlot.Gamepad);
            Input.PressPad(GamepadButton.RightStick, true);
            yield return null;
            yield return null;
            Input.PressPad(GamepadButton.RightStick, false);
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 3f, "the second prompt did not close (top " + Ui.Top + ", capturing " + Ui.IsCapturing + ", tab " + Options.ActiveTitle + ")");

            Options.ResetButton.Activate();
            Assert.IsTrue(Service.Bindings.Snapshot().SameValues(BindingMap.Defaults()));
            Assert.AreEqual("Space", Button(GameAction.Jump, BindingSlot.Primary).Text);
            Assert.AreEqual("B", Button(GameAction.Crouch, BindingSlot.Gamepad).Text);
            Assert.AreEqual(0, new SettingsStore(Storage).Load().Settings.Controls.Overrides.Count);
        }

        [UnityTest]
        public IEnumerator ReboundControlsComeBackOnTheNextLaunch()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            yield return PressKeyForPrompt(Key.Y);
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 3f, "the prompt did not close");

            yield return ToControlsTab();   // a fresh load of the scene with the same file
            Assert.AreEqual("Y", Button(GameAction.Jump, BindingSlot.Primary).Text);
            Assert.IsTrue(Button(GameAction.Jump, BindingSlot.Primary).ClassListContains("cy-bind--changed"));
        }

        [UnityTest]
        public IEnumerator ChangingGraphicsLaterDoesNotUndoARebind()
        {
            yield return ToControlsTab();
            yield return OpenPrompt(GameAction.Jump, BindingSlot.Primary);
            yield return PressKeyForPrompt(Key.Y);
            yield return WaitUntil(() => Ui.Top == UiScreen.Options, 3f, "the prompt did not close");

            Options.SelectTab(0);
            Options.FocusFirstOfPage();
            yield return Nav(MoveDirection.Right);   // quality up: applies a whole settings copy
            Assert.AreEqual("<Keyboard>/y", Service.Bindings.Get(GameAction.Jump, BindingSlot.Primary));
            Options.BackButton.Activate();
            Assert.AreEqual("<Keyboard>/y", StoredOverride("jump:0"));
        }
    }
}
#endif
