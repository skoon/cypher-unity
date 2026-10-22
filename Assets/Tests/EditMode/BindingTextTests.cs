using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class BindingTextTests
    {
        [TestCase("<Keyboard>/w", "W")]
        [TestCase("<Keyboard>/space", "Space")]
        [TestCase("<Keyboard>/leftShift", "Left Shift")]
        [TestCase("<Keyboard>/rightCtrl", "Right Ctrl")]
        [TestCase("<Keyboard>/upArrow", "Up Arrow")]
        [TestCase("<Keyboard>/numpad5", "Numpad 5")]
        [TestCase("<Keyboard>/numpadEnter", "Numpad Enter")]
        [TestCase("<Keyboard>/digit3", "3")]
        [TestCase("<Keyboard>/f3", "F3")]
        [TestCase("<Keyboard>/f12", "F12")]
        [TestCase("<Keyboard>/capsLock", "Caps Lock")]
        [TestCase("<Keyboard>/pageUp", "Page Up")]
        [TestCase("<Keyboard>/escape", "Escape")]
        [TestCase("<Mouse>/leftButton", "Left Mouse")]
        [TestCase("<Mouse>/rightButton", "Right Mouse")]
        [TestCase("<Mouse>/middleButton", "Middle Mouse")]
        [TestCase("<Mouse>/backButton", "Mouse 4")]
        [TestCase("<Mouse>/forwardButton", "Mouse 5")]
        [TestCase("<Gamepad>/buttonSouth", "A")]
        [TestCase("<Gamepad>/buttonEast", "B")]
        [TestCase("<Gamepad>/buttonWest", "X")]
        [TestCase("<Gamepad>/buttonNorth", "Y")]
        [TestCase("<Gamepad>/leftStickPress", "L3")]
        [TestCase("<Gamepad>/rightShoulder", "RB")]
        [TestCase("<Gamepad>/leftTrigger", "LT")]
        [TestCase("<Gamepad>/dpad/up", "D-Pad Up")]
        [TestCase("<Gamepad>/start", "Start")]
        public void ControlsReadTheWayAPlayerWouldSayThem(string path, string expected)
        {
            Assert.AreEqual(expected, BindingText.Describe(path));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("none")]
        public void NothingIsUnbound(string path)
        {
            Assert.AreEqual("Unbound", BindingText.Describe(path));
        }

        [TestCase("<Touchscreen>/press")]
        [TestCase("garbage")]
        [TestCase("<Keyboard")]
        public void UnrecognisedPathsComeBackAsTheyWere(string path)
        {
            Assert.AreEqual(path, BindingText.Describe(path));
        }

        [Test]
        public void UnknownGamepadAndMouseNamesAreStillReadable()
        {
            Assert.AreEqual("Some Button", BindingText.Describe("<Gamepad>/someButton"));
            Assert.AreEqual("Wheel Click", BindingText.Describe("<Mouse>/wheelClick"));
        }

        [Test]
        public void EveryDefaultControlHasANonEmptyName()
        {
            foreach (ActionInfo a in ControlCatalog.All)
            {
                foreach (BindingSlot s in new[] { BindingSlot.Primary, BindingSlot.Secondary, BindingSlot.Gamepad })
                {
                    string def = a.DefaultFor(s);
                    if (def != null) Assert.That(BindingText.Describe(def), Is.Not.Empty.And.Not.EqualTo("Unbound"), def);
                }
            }
        }
    }
}
