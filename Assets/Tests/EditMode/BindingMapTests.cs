using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class BindingMapTests
    {
        const string W = "<Keyboard>/w";
        const string A = "<Keyboard>/a";
        const string Space = "<Keyboard>/space";
        const string K = "<Keyboard>/y";
        const string LeftShift = "<Keyboard>/leftShift";
        const string RightShift = "<Keyboard>/rightShift";
        const string LeftCtrl = "<Keyboard>/leftCtrl";
        const string C = "<Keyboard>/c";

        static BindingMap Fresh() => BindingMap.Defaults();

        // ------------------------------------------------------------------ defaults

        [Test]
        public void DefaultsMatchTheCatalogAndAreConsistent()
        {
            BindingMap m = Fresh();
            foreach (ActionInfo a in ControlCatalog.All)
            {
                foreach (BindingSlot s in Enum.GetValues(typeof(BindingSlot))) Assert.AreEqual(a.DefaultFor(s), m.Get(a.Action, s), $"{a.Id} {s}");
            }
            Assert.IsNull(m.Validate());
            Assert.IsEmpty(m.ToOverrides());
            Assert.IsTrue(m.IsDefault(GameAction.Jump));
        }

        // ------------------------------------------------------------------ assigning

        [Test]
        public void AFreeControlIsApplied()
        {
            BindingMap m = Fresh();
            AssignResult r = m.Assign(GameAction.Jump, BindingSlot.Primary, K);
            Assert.AreEqual(AssignOutcome.Applied, r.Outcome);
            Assert.AreEqual(K, m.Get(GameAction.Jump, BindingSlot.Primary));
            Assert.IsFalse(m.IsDefault(GameAction.Jump, BindingSlot.Primary));
            Assert.IsNull(m.Validate());
        }

        [Test]
        public void AFreeControlCanFillAnEmptySlot()
        {
            BindingMap m = Fresh();
            Assert.AreEqual(AssignOutcome.Applied, m.Assign(GameAction.Jump, BindingSlot.Secondary, "<Mouse>/backButton").Outcome);
            Assert.AreEqual("<Mouse>/backButton", m.Get(GameAction.Jump, BindingSlot.Secondary));
        }

        [Test]
        public void AssigningWhatTheSlotAlreadyHasChangesNothing()
        {
            BindingMap m = Fresh();
            Assert.AreEqual(AssignOutcome.Unchanged, m.Assign(GameAction.Jump, BindingSlot.Primary, Space).Outcome);
            Assert.AreEqual(AssignOutcome.Unchanged, m.Assign(GameAction.Jump, BindingSlot.Primary, "<keyboard>/SPACE").Outcome, "case does not matter");
            Assert.IsTrue(m.IsDefault(GameAction.Jump));
        }

        [Test]
        public void AControlAnotherActionHoldsIsAConflictThatChangesNothing()
        {
            BindingMap m = Fresh();
            AssignResult r = m.Assign(GameAction.Jump, BindingSlot.Primary, W);
            Assert.AreEqual(AssignOutcome.Conflict, r.Outcome);
            Assert.AreEqual(GameAction.MoveForward, r.OtherAction);
            Assert.AreEqual(BindingSlot.Primary, r.OtherSlot);
            Assert.AreEqual("W is already used by Move forward.", r.Message);
            Assert.AreEqual(Space, m.Get(GameAction.Jump, BindingSlot.Primary));
            Assert.AreEqual(W, m.Get(GameAction.MoveForward, BindingSlot.Primary));
        }

        [Test]
        public void TheConflictCanBeInEitherKeyboardSlotOfTheOtherAction()
        {
            BindingMap m = Fresh();
            AssignResult r = m.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/upArrow");
            Assert.AreEqual(AssignOutcome.Conflict, r.Outcome);
            Assert.AreEqual(GameAction.MoveForward, r.OtherAction);
            Assert.AreEqual(BindingSlot.Secondary, r.OtherSlot);
        }

        [Test]
        public void TheSameControlTwiceInOneActionIsRefusedNotASwap()
        {
            BindingMap m = Fresh();
            AssignResult r = m.Assign(GameAction.Sprint, BindingSlot.Primary, RightShift);   // Sprint's own Secondary
            Assert.AreEqual(AssignOutcome.Rejected, r.Outcome);
            StringAssert.Contains("already uses Right Shift in its other slot", r.Message);
        }

        [Test]
        public void KeyboardAndGamepadControlsNeverConflictWithEachOther()
        {
            // The gamepad pool is separate, so the same gamepad button on two actions is a conflict only among gamepad slots.
            BindingMap m = Fresh();
            Assert.AreEqual(AssignOutcome.Conflict, m.Assign(GameAction.Sprint, BindingSlot.Gamepad, "<Gamepad>/buttonSouth").Outcome);
            Assert.AreEqual(AssignOutcome.Applied, m.Assign(GameAction.Sprint, BindingSlot.Gamepad, "<Gamepad>/rightStickPress").Outcome);
        }

        [Test]
        public void RejectionsSayWhy()
        {
            BindingMap m = Fresh();
            AssignResult escape = m.Assign(GameAction.Jump, BindingSlot.Primary, "<Keyboard>/escape");
            Assert.AreEqual(AssignOutcome.Rejected, escape.Outcome);
            StringAssert.Contains("reserved", escape.Message);

            Assert.AreEqual(AssignOutcome.Rejected, m.Assign(GameAction.Jump, BindingSlot.Primary, "<Gamepad>/buttonNorth").Outcome, "a gamepad button in a keyboard slot");
            Assert.AreEqual(AssignOutcome.Rejected, m.Assign(GameAction.Jump, BindingSlot.Gamepad, "<Keyboard>/j").Outcome, "a key in the gamepad slot");
            Assert.AreEqual(AssignOutcome.Rejected, m.Assign(GameAction.Jump, BindingSlot.Primary, "").Outcome);
            AssignResult noSlot = m.Assign(GameAction.MoveForward, BindingSlot.Gamepad, "<Gamepad>/buttonNorth");
            Assert.AreEqual(AssignOutcome.Rejected, noSlot.Outcome);
            StringAssert.Contains("no Gamepad slot", noSlot.Message);
            Assert.IsTrue(m.IsDefault(GameAction.Jump), "and nothing changed");
        }

        // ------------------------------------------------------------------ swapping

        [Test]
        public void ASwapGivesTheOtherActionTheControlTheSlotHad()
        {
            BindingMap m = Fresh();
            Assert.AreEqual(AssignOutcome.Conflict, m.Assign(GameAction.Jump, BindingSlot.Primary, W).Outcome);

            AssignResult r = m.SwapWith(GameAction.Jump, BindingSlot.Primary, W);
            Assert.AreEqual(AssignOutcome.Applied, r.Outcome);
            Assert.AreEqual(W, m.Get(GameAction.Jump, BindingSlot.Primary));
            Assert.AreEqual(Space, m.Get(GameAction.MoveForward, BindingSlot.Primary), "forward took jump's old key");
            Assert.IsNull(m.Validate());
        }

        [Test]
        public void SwappingTwoKeysOfKeyboardActionsRoundTripsThroughTheOverrides()
        {
            BindingMap m = Fresh();
            m.SwapWith(GameAction.MoveForward, BindingSlot.Primary, A);   // W and A trade places
            Assert.AreEqual(A, m.Get(GameAction.MoveForward, BindingSlot.Primary));
            Assert.AreEqual(W, m.Get(GameAction.MoveLeft, BindingSlot.Primary));

            BindingMap back = BindingMap.FromOverrides(m.ToOverrides(), out IReadOnlyList<string> problems);
            Assert.IsEmpty(problems, "a swap is consistent as a whole even though each entry alone clashes with a default");
            Assert.IsTrue(m.SameValues(back));
        }

        [Test]
        public void ASwapIntoAnEmptySlotLeavesTheOtherActionWithItsOtherKey()
        {
            // Jump has nothing in its Secondary slot. Taking Move forward's W for it leaves Move forward with only its arrow key.
            BindingMap m = Fresh();
            Assert.AreEqual(AssignOutcome.Conflict, m.Assign(GameAction.Jump, BindingSlot.Secondary, W).Outcome);
            AssignResult r = m.SwapWith(GameAction.Jump, BindingSlot.Secondary, W);
            Assert.AreEqual(AssignOutcome.Applied, r.Outcome);
            Assert.AreEqual(W, m.Get(GameAction.Jump, BindingSlot.Secondary));
            Assert.IsNull(m.Get(GameAction.MoveForward, BindingSlot.Primary));
            Assert.AreEqual("<Keyboard>/upArrow", m.Get(GameAction.MoveForward, BindingSlot.Secondary));
            Assert.IsNull(m.Validate());
        }

        [Test]
        public void ASwapThatWouldLeaveTheOtherActionWithNoKeyboardControlIsRefusedAndChangesNothing()
        {
            BindingMap m = Fresh();
            m.Unbind(GameAction.Jump, BindingSlot.Secondary);                       // already empty
            m.Assign(GameAction.MoveForward, BindingSlot.Secondary, "<Keyboard>/g"); // forward: W and G
            Assert.AreEqual(AssignOutcome.Applied, m.Unbind(GameAction.MoveForward, BindingSlot.Secondary).Outcome);   // forward: W only
            // Jump (Secondary, empty) now takes W from Move forward: forward would have nothing.
            BindingMap before = m.Clone();
            AssignResult r = m.SwapWith(GameAction.Jump, BindingSlot.Secondary, W);
            Assert.AreEqual(AssignOutcome.Rejected, r.Outcome);
            StringAssert.Contains("Move forward", r.Message);
            Assert.IsTrue(before.SameValues(m));
        }

        [Test]
        public void SwapWithOnAFreeControlJustAssigns()
        {
            BindingMap m = Fresh();
            Assert.AreEqual(AssignOutcome.Applied, m.SwapWith(GameAction.Jump, BindingSlot.Primary, K).Outcome);
            Assert.AreEqual(K, m.Get(GameAction.Jump, BindingSlot.Primary));
        }

        [Test]
        public void SwapWithTheSameActionsOtherSlotIsRefused()
        {
            BindingMap m = Fresh();
            Assert.AreEqual(AssignOutcome.Rejected, m.SwapWith(GameAction.Sprint, BindingSlot.Primary, RightShift).Outcome);
            Assert.IsTrue(m.IsDefault(GameAction.Sprint));
        }

        [Test]
        public void GamepadSwapsWorkAmongGamepadSlotsOnly()
        {
            BindingMap m = Fresh();
            Assert.AreEqual(AssignOutcome.Conflict, m.Assign(GameAction.Jump, BindingSlot.Gamepad, "<Gamepad>/buttonEast").Outcome);
            Assert.AreEqual(AssignOutcome.Applied, m.SwapWith(GameAction.Jump, BindingSlot.Gamepad, "<Gamepad>/buttonEast").Outcome);
            Assert.AreEqual("<Gamepad>/buttonEast", m.Get(GameAction.Jump, BindingSlot.Gamepad));
            Assert.AreEqual("<Gamepad>/buttonSouth", m.Get(GameAction.Crouch, BindingSlot.Gamepad));
        }

        // ------------------------------------------------------------------ unbinding

        [Test]
        public void ASecondarySlotCanBeEmptied()
        {
            BindingMap m = Fresh();
            Assert.AreEqual(AssignOutcome.Applied, m.Unbind(GameAction.Sprint, BindingSlot.Secondary).Outcome);
            Assert.IsNull(m.Get(GameAction.Sprint, BindingSlot.Secondary));
            Assert.AreEqual(AssignOutcome.Unchanged, m.Unbind(GameAction.Sprint, BindingSlot.Secondary).Outcome);
        }

        [Test]
        public void TheGamepadSlotCanBeEmptied()
        {
            BindingMap m = Fresh();
            Assert.AreEqual(AssignOutcome.Applied, m.Unbind(GameAction.Jump, BindingSlot.Gamepad).Outcome);
            Assert.IsNull(m.Get(GameAction.Jump, BindingSlot.Gamepad));
            Assert.IsNull(m.Validate());
        }

        [Test]
        public void TheLastKeyboardControlOfAnActionCannotBeRemoved()
        {
            BindingMap m = Fresh();
            Assert.AreEqual(AssignOutcome.Rejected, m.Unbind(GameAction.Jump, BindingSlot.Primary).Outcome, "jump has only Space");
            Assert.AreEqual(Space, m.Get(GameAction.Jump, BindingSlot.Primary));

            m.Unbind(GameAction.Sprint, BindingSlot.Secondary);
            AssignResult r = m.Unbind(GameAction.Sprint, BindingSlot.Primary);
            Assert.AreEqual(AssignOutcome.Rejected, r.Outcome);
            StringAssert.Contains("at least one keyboard or mouse control", r.Message);
        }

        [Test]
        public void AFreedControlCanBeUsedByAnotherAction()
        {
            BindingMap m = Fresh();
            m.Unbind(GameAction.Sprint, BindingSlot.Secondary);
            Assert.AreEqual(AssignOutcome.Applied, m.Assign(GameAction.Jump, BindingSlot.Secondary, RightShift).Outcome);
        }

        [Test]
        public void UnbindingASlotTheActionDoesNotHaveIsRefused()
        {
            Assert.AreEqual(AssignOutcome.Rejected, Fresh().Unbind(GameAction.MoveLeft, BindingSlot.Gamepad).Outcome);
        }

        // ------------------------------------------------------------------ reset

        [Test]
        public void ResettingAnActionRestoresItsDefaults()
        {
            BindingMap m = Fresh();
            m.Assign(GameAction.Jump, BindingSlot.Primary, K);
            m.Assign(GameAction.Jump, BindingSlot.Secondary, "<Mouse>/middleButton");
            m.Assign(GameAction.Jump, BindingSlot.Gamepad, "<Gamepad>/buttonNorth");

            Assert.AreEqual(AssignOutcome.Applied, m.Reset(GameAction.Jump).Outcome);
            Assert.IsTrue(m.IsDefault(GameAction.Jump));
            Assert.IsNull(m.Get(GameAction.Jump, BindingSlot.Secondary), "back to empty");
            Assert.IsNull(m.Validate());
        }

        [Test]
        public void ResettingTakesBackADefaultAnotherActionHadTakenAndGivesItWhatThisOneHad()
        {
            BindingMap m = Fresh();
            m.SwapWith(GameAction.Jump, BindingSlot.Primary, W);          // jump has W, forward has Space
            Assert.AreEqual(AssignOutcome.Applied, m.Reset(GameAction.Jump).Outcome);
            Assert.AreEqual(Space, m.Get(GameAction.Jump, BindingSlot.Primary));
            Assert.AreEqual(W, m.Get(GameAction.MoveForward, BindingSlot.Primary), "forward is back where it was too");
            Assert.IsNull(m.Validate());
        }

        [Test]
        public void ResettingWhenAnotherActionTookADefaultHandsItTheOldControl()
        {
            BindingMap m = Fresh();
            m.Assign(GameAction.Crouch, BindingSlot.Primary, "<Keyboard>/x");   // crouch: x + c; leftCtrl is free
            m.Assign(GameAction.Jump, BindingSlot.Secondary, LeftCtrl);        // jump grabbed crouch's old default
            Assert.AreEqual(AssignOutcome.Applied, m.Reset(GameAction.Crouch).Outcome);
            Assert.AreEqual(LeftCtrl, m.Get(GameAction.Crouch, BindingSlot.Primary));
            Assert.AreEqual("<Keyboard>/x", m.Get(GameAction.Jump, BindingSlot.Secondary), "jump got the key crouch had been using");
            Assert.IsNull(m.Validate());
        }

        [Test]
        public void ResettingAnActionWhoseOwnSlotsTradedPlacesWorks()
        {
            BindingMap m = Fresh();
            m.Assign(GameAction.Sprint, BindingSlot.Primary, "<Keyboard>/q");        // q + rightShift
            m.Assign(GameAction.Sprint, BindingSlot.Secondary, LeftShift);            // q + leftShift: the defaults' keys, in the wrong slots
            Assert.IsFalse(m.IsDefault(GameAction.Sprint));
            Assert.AreEqual(AssignOutcome.Applied, m.Reset(GameAction.Sprint).Outcome);
            Assert.IsTrue(m.IsDefault(GameAction.Sprint));
            Assert.IsNull(m.Validate());
        }

        [Test]
        public void ResetAllRestoresEverything()
        {
            BindingMap m = Fresh();
            m.SwapWith(GameAction.Jump, BindingSlot.Primary, W);
            m.Assign(GameAction.Crouch, BindingSlot.Gamepad, "<Gamepad>/rightShoulder");
            m.Unbind(GameAction.Sprint, BindingSlot.Secondary);
            m.ResetAll();
            Assert.IsTrue(m.SameValues(Fresh()));
            Assert.IsEmpty(m.ToOverrides());
        }

        // ------------------------------------------------------------------ persistence

        [Test]
        public void OnlyDifferencesFromTheDefaultsAreStoredWithStableKeys()
        {
            BindingMap m = Fresh();
            m.Assign(GameAction.Jump, BindingSlot.Primary, K);
            m.Unbind(GameAction.Sprint, BindingSlot.Secondary);
            m.Assign(GameAction.Crouch, BindingSlot.Gamepad, "<Gamepad>/rightStickPress");

            IReadOnlyDictionary<string, string> o = m.ToOverrides();
            Assert.AreEqual(3, o.Count);
            Assert.AreEqual(K, o["jump:0"]);
            Assert.AreEqual("none", o["sprint:1"]);
            Assert.AreEqual("<Gamepad>/rightStickPress", o["crouch:2"]);
            CollectionAssert.AreEqual(new[] { "crouch:2", "jump:0", "sprint:1" }, new List<string>(o.Keys), "in key order, so the file is stable");
        }

        [Test]
        public void AnyConsistentMapSurvivesAStoreAndLoad()
        {
            BindingMap m = Fresh();
            m.SwapWith(GameAction.MoveLeft, BindingSlot.Primary, "<Keyboard>/d");
            m.Assign(GameAction.Jump, BindingSlot.Secondary, "<Mouse>/forwardButton");
            m.Unbind(GameAction.Crouch, BindingSlot.Secondary);
            m.Assign(GameAction.Sprint, BindingSlot.Gamepad, "<Gamepad>/dpad/up");

            BindingMap loaded = BindingMap.FromOverrides(m.ToOverrides(), out IReadOnlyList<string> problems);
            Assert.IsEmpty(problems);
            Assert.IsTrue(m.SameValues(loaded));
        }

        [Test]
        public void NoOverridesMeansDefaultsWithNoProblems()
        {
            BindingMap m = BindingMap.FromOverrides(new Dictionary<string, string>(), out IReadOnlyList<string> problems);
            Assert.IsEmpty(problems);
            Assert.IsTrue(m.SameValues(Fresh()));
        }

        [Test]
        public void UnknownAndNonsenseEntriesAreLeftOutAndNamed()
        {
            var overrides = new Dictionary<string, string>
            {
                ["fly:0"] = "<Keyboard>/f",                // unknown action
                ["jump:7"] = "<Keyboard>/j",               // unknown slot
                ["jump"] = "<Keyboard>/j",                 // no slot
                ["moveLeft:2"] = "<Gamepad>/buttonNorth",  // movement has no gamepad slot
                ["sprint:0"] = "<Keyboard>/escape",        // reserved
                ["crouch:1"] = "<Gamepad>/buttonNorth",    // wrong device for the slot
                ["jump:1"] = "<Mouse>/middleButton",       // fine
            };
            BindingMap m = BindingMap.FromOverrides(overrides, out IReadOnlyList<string> problems);

            Assert.AreEqual(6, problems.Count);
            Assert.AreEqual("<Mouse>/middleButton", m.Get(GameAction.Jump, BindingSlot.Secondary), "the good entry still applied");
            Assert.IsTrue(m.IsDefault(GameAction.Sprint));
            Assert.IsTrue(m.IsDefault(GameAction.Crouch));
            Assert.IsNull(m.Validate());
            StringAssert.Contains("'fly:0'", string.Join("|", problems));
            StringAssert.Contains("reserved", string.Join("|", problems));
        }

        [Test]
        public void ContradictoryStoredControlsFallBackToDefaultsAndSayWhy()
        {
            var overrides = new Dictionary<string, string>
            {
                ["moveLeft:0"] = W,   // forward already has W, and nothing moved forward off it
                ["jump:1"] = "<Mouse>/middleButton",
            };
            BindingMap m = BindingMap.FromOverrides(overrides, out IReadOnlyList<string> problems);
            Assert.IsTrue(m.SameValues(Fresh()), "every control reset, including the valid jump entry");
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("contradict each other", problems[0]);
            StringAssert.Contains("W is used by both Move forward and Move left", problems[0]);
        }

        [Test]
        public void UnbindingEveryKeyboardControlOfAnActionInTheFileFallsBackToDefaults()
        {
            var overrides = new Dictionary<string, string> { ["jump:0"] = "none" };
            BindingMap m = BindingMap.FromOverrides(overrides, out IReadOnlyList<string> problems);
            Assert.IsTrue(m.SameValues(Fresh()));
            StringAssert.Contains("Jump has no keyboard or mouse control", problems[0]);
        }

        [Test]
        public void AFileThatEmptiesAGamepadSlotIsFine()
        {
            var overrides = new Dictionary<string, string> { ["jump:2"] = "none" };
            BindingMap m = BindingMap.FromOverrides(overrides, out IReadOnlyList<string> problems);
            Assert.IsEmpty(problems);
            Assert.IsNull(m.Get(GameAction.Jump, BindingSlot.Gamepad));
        }

        [Test]
        public void NegativeOrJunkSlotNumbersAreNamedNotCrashed()
        {
            var overrides = new Dictionary<string, string> { ["jump:-1"] = K, ["jump:x"] = K, ["jump:"] = K, [":0"] = K };
            BindingMap m = BindingMap.FromOverrides(overrides, out IReadOnlyList<string> problems);
            Assert.AreEqual(4, problems.Count);
            Assert.IsTrue(m.SameValues(Fresh()));
        }

        // ------------------------------------------------------------------ copies and lookups

        [Test]
        public void ACloneIsIndependent()
        {
            BindingMap a = Fresh();
            BindingMap b = a.Clone();
            Assert.IsTrue(a.SameValues(b));
            b.Assign(GameAction.Jump, BindingSlot.Primary, K);
            Assert.AreEqual(Space, a.Get(GameAction.Jump, BindingSlot.Primary));
            Assert.IsFalse(a.SameValues(b));
            Assert.IsFalse(a.SameValues(null));
        }

        [Test]
        public void FindingWhoHoldsAControlLooksOnlyAmongTheSameKindOfSlot()
        {
            BindingMap m = Fresh();
            Assert.IsTrue(m.TryFindHolder(BindingSlot.Primary, Space, out GameAction a, out BindingSlot s));
            Assert.AreEqual(GameAction.Jump, a);
            Assert.AreEqual(BindingSlot.Primary, s);
            Assert.IsFalse(m.TryFindHolder(BindingSlot.Gamepad, Space, out _, out _));
            Assert.IsTrue(m.TryFindHolder(BindingSlot.Gamepad, "<Gamepad>/buttonEast", out a, out s));
            Assert.AreEqual(GameAction.Crouch, a);
            Assert.IsFalse(m.TryFindHolder(BindingSlot.Primary, K, out _, out _));
        }

        [Test]
        public void ValidateNamesTheProblemsItFinds()
        {
            BindingMap m = Fresh();
            m.Assign(GameAction.Jump, BindingSlot.Secondary, "<Mouse>/leftButton");
            Assert.IsNull(m.Validate());
            Assert.Throws<ArgumentNullException>(() => BindingMap.FromOverrides(null, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => ControlCatalog.For((GameAction)42));
        }
    }
}
