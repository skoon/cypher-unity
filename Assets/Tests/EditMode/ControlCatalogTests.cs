using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class ControlCatalogTests
    {
        [Test]
        public void ThereIsOneEntryPerActionInEnumOrder()
        {
            Assert.AreEqual(Enum.GetValues(typeof(GameAction)).Length, ControlCatalog.All.Count);
            for (int i = 0; i < ControlCatalog.All.Count; i++) Assert.AreEqual((GameAction)i, ControlCatalog.All[i].Action);
            foreach (GameAction a in Enum.GetValues(typeof(GameAction))) Assert.AreEqual(a, ControlCatalog.For(a).Action);
        }

        [Test]
        public void IdsAreUniqueStableCamelCaseNamesThatRoundTrip()
        {
            Assert.AreEqual(ControlCatalog.All.Count, ControlCatalog.All.Select(a => a.Id).Distinct().Count());
            Assert.AreEqual(new[]
            {
                "moveForward", "moveBack", "moveLeft", "moveRight", "jump", "sprint", "crouch",
                "interact", "scan", "inventory", "journal", "fire", "aim", "reload", "weapon1", "weapon2", "weapon3", "character",
            }, ControlCatalog.All.Select(a => a.Id).ToArray(), "ids are stored in settings files: never renamed, new ones at the end");
            foreach (ActionInfo a in ControlCatalog.All)
            {
                Assert.IsTrue(ControlCatalog.TryFromId(a.Id, out ActionInfo found));
                Assert.AreSame(a, found);
                Assert.IsFalse(string.IsNullOrWhiteSpace(a.DisplayName));
            }
            Assert.IsFalse(ControlCatalog.TryFromId("fly", out _));
            Assert.IsFalse(ControlCatalog.TryFromId(null, out _));
            Assert.IsFalse(ControlCatalog.TryFromId("Jump", out _), "ids are case sensitive");
        }

        [Test]
        public void DefaultsMatchTheBindingsTheGameShippedWith()
        {
            ActionInfo forward = ControlCatalog.For(GameAction.MoveForward);
            Assert.AreEqual("<Keyboard>/w", forward.DefaultFor(BindingSlot.Primary));
            Assert.AreEqual("<Keyboard>/upArrow", forward.DefaultFor(BindingSlot.Secondary));
            Assert.IsNull(forward.DefaultFor(BindingSlot.Gamepad));
            Assert.AreEqual("<Keyboard>/a", ControlCatalog.For(GameAction.MoveLeft).DefaultFor(BindingSlot.Primary));
            Assert.AreEqual("<Keyboard>/rightArrow", ControlCatalog.For(GameAction.MoveRight).DefaultFor(BindingSlot.Secondary));

            ActionInfo jump = ControlCatalog.For(GameAction.Jump);
            Assert.AreEqual("<Keyboard>/space", jump.DefaultFor(BindingSlot.Primary));
            Assert.IsNull(jump.DefaultFor(BindingSlot.Secondary));
            Assert.AreEqual("<Gamepad>/buttonSouth", jump.DefaultFor(BindingSlot.Gamepad));

            ActionInfo sprint = ControlCatalog.For(GameAction.Sprint);
            Assert.AreEqual("<Keyboard>/leftShift", sprint.DefaultFor(BindingSlot.Primary));
            Assert.AreEqual("<Keyboard>/rightShift", sprint.DefaultFor(BindingSlot.Secondary));
            Assert.AreEqual("<Gamepad>/leftStickPress", sprint.DefaultFor(BindingSlot.Gamepad));

            ActionInfo crouch = ControlCatalog.For(GameAction.Crouch);
            Assert.AreEqual("<Keyboard>/leftCtrl", crouch.DefaultFor(BindingSlot.Primary));
            Assert.AreEqual("<Keyboard>/c", crouch.DefaultFor(BindingSlot.Secondary));
            Assert.AreEqual("<Gamepad>/buttonEast", crouch.DefaultFor(BindingSlot.Gamepad));
        }

        [Test]
        public void MovementHasNoGamepadSlotBecauseTheStickMoves()
        {
            foreach (ActionInfo a in ControlCatalog.All.Where(a => a.Group == ActionGroup.Movement))
            {
                Assert.IsFalse(a.HasGamepadSlot, a.Id);
                Assert.IsFalse(a.HasSlot(BindingSlot.Gamepad));
                Assert.IsTrue(a.HasSlot(BindingSlot.Primary));
            }
            foreach (ActionInfo a in ControlCatalog.All.Where(a => a.Group == ActionGroup.Actions)) Assert.IsTrue(a.HasGamepadSlot, a.Id);
        }

        [Test]
        public void EveryDefaultIsAllowedInItsOwnSlot()
        {
            foreach (ActionInfo a in ControlCatalog.All)
            {
                foreach (BindingSlot s in Enum.GetValues(typeof(BindingSlot)))
                {
                    string def = a.DefaultFor(s);
                    if (def != null) Assert.IsNull(ControlCatalog.Check(s, def), $"{a.Id} {s}");
                }
            }
        }

        [Test]
        public void EveryActionStartsWithAKeyboardControl()
        {
            foreach (ActionInfo a in ControlCatalog.All) Assert.IsNotNull(a.DefaultFor(BindingSlot.Primary), a.Id);
        }

        [Test]
        public void TheKeyboardAndMouseSlotsShareOnePoolAndTheGamepadHasItsOwn()
        {
            Assert.IsTrue(ControlCatalog.IsKeyboardMouse(BindingSlot.Primary));
            Assert.IsTrue(ControlCatalog.IsKeyboardMouse(BindingSlot.Secondary));
            Assert.IsFalse(ControlCatalog.IsKeyboardMouse(BindingSlot.Gamepad));
        }

        [TestCase("<Keyboard>/space")]
        [TestCase("<Keyboard>/j")]
        [TestCase("<Keyboard>/leftAlt")]
        [TestCase("<Keyboard>/numpad5")]
        [TestCase("<Keyboard>/f5")]
        [TestCase("<Keyboard>/digit3")]
        [TestCase("<Keyboard>/3")]
        [TestCase("<Keyboard>/0")]
        [TestCase("<Keyboard>/backquote")]
        [TestCase("<Keyboard>/tab")]
        [TestCase("<Mouse>/leftButton")]
        [TestCase("<Mouse>/rightButton")]
        [TestCase("<Mouse>/middleButton")]
        [TestCase("<Mouse>/forwardButton")]
        [TestCase("<Mouse>/backButton")]
        public void KeyboardAndMouseSlotsAcceptKeysAndMouseButtons(string path)
        {
            Assert.IsNull(ControlCatalog.Check(BindingSlot.Primary, path));
            Assert.IsNull(ControlCatalog.Check(BindingSlot.Secondary, path));
        }

        [TestCase("<Keyboard>/escape")]
        [TestCase("<Keyboard>/enter")]
        [TestCase("<Keyboard>/numpadEnter")]
        [TestCase("<Keyboard>/f3")]
        [TestCase("<Keyboard>/F3")]
        public void ReservedKeysAreRefusedWithAReason(string path)
        {
            string why = ControlCatalog.Check(BindingSlot.Primary, path);
            StringAssert.Contains("reserved by the game", why);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void AnEmptyChoiceIsRefused(string path)
        {
            StringAssert.Contains("No control", ControlCatalog.Check(BindingSlot.Primary, path));
            StringAssert.Contains("No control", ControlCatalog.Check(BindingSlot.Gamepad, path));
        }

        [TestCase("<Keyboard>/")]
        [TestCase("<Keyboard>/anyKey")]
        [TestCase("<Keyboard>/a/b")]
        [TestCase("<Keyboard>/#(a)")]
        [TestCase("<Mouse>/position")]
        [TestCase("<Mouse>/delta")]
        [TestCase("<Mouse>/scroll")]
        [TestCase("<Mouse>/")]
        [TestCase("<Gamepad>/buttonSouth")]
        [TestCase("<Touchscreen>/press")]
        [TestCase("space")]
        [TestCase("none")]
        public void ThingsThatAreNotKeysOrMouseButtonsAreRefusedInKeyboardSlots(string path)
        {
            Assert.IsNotNull(ControlCatalog.Check(BindingSlot.Primary, path), path);
            Assert.IsNotNull(ControlCatalog.Check(BindingSlot.Secondary, path), path);
        }

        [TestCase("<Gamepad>/buttonSouth")]
        [TestCase("<Gamepad>/buttonEast")]
        [TestCase("<Gamepad>/buttonWest")]
        [TestCase("<Gamepad>/buttonNorth")]
        [TestCase("<Gamepad>/leftShoulder")]
        [TestCase("<Gamepad>/rightShoulder")]
        [TestCase("<Gamepad>/leftTrigger")]
        [TestCase("<Gamepad>/rightTrigger")]
        [TestCase("<Gamepad>/leftStickPress")]
        [TestCase("<Gamepad>/rightStickPress")]
        [TestCase("<Gamepad>/dpad/up")]
        [TestCase("<Gamepad>/dpad/left")]
        public void TheGamepadSlotAcceptsGamepadButtons(string path)
        {
            Assert.IsNull(ControlCatalog.Check(BindingSlot.Gamepad, path));
        }

        [TestCase("<Gamepad>/start")]
        [TestCase("<Gamepad>/select")]
        public void StartAndSelectAreReservedOnTheGamepad(string path)
        {
            StringAssert.Contains("reserved by the game", ControlCatalog.Check(BindingSlot.Gamepad, path));
        }

        [TestCase("<Gamepad>/leftStick")]
        [TestCase("<Gamepad>/rightStick")]
        [TestCase("<Gamepad>/dpad")]
        [TestCase("<Gamepad>/leftStick/x")]
        [TestCase("<Keyboard>/space")]
        [TestCase("<Mouse>/leftButton")]
        [TestCase("<Gamepad>/")]
        public void ThingsThatAreNotBindableGamepadButtonsAreRefused(string path)
        {
            Assert.IsNotNull(ControlCatalog.Check(BindingSlot.Gamepad, path), path);
        }

        [Test]
        public void PathsAreMatchedIgnoringCase()
        {
            Assert.IsNull(ControlCatalog.Check(BindingSlot.Primary, "<keyboard>/Space"));
            Assert.IsNull(ControlCatalog.Check(BindingSlot.Gamepad, "<gamepad>/ButtonSouth"));
        }

        [Test]
        public void UnknownActionsFailLoudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ControlCatalog.For((GameAction)99));
        }
    }
}
