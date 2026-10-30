#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Cypher.Core;
using Cypher.Player;
using Cypher.Rpg;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The scanner in the real Slice scene: holding F charges it over play without stopping anything, it tags real contacts in view, and letting go fades it.</summary>
    public class ScannerPlayTests
    {
        [SetUp]
        public void SetUp() => SliceTestScene.Isolate(saves: new MemorySaveStorage());

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static RpgService Rpg => Object.FindAnyObjectByType<RpgService>();

        static UiRoot Ui => Object.FindAnyObjectByType<UiRoot>();

        static FirstPersonController Player => Object.FindAnyObjectByType<FirstPersonController>();

        [UnityTest]
        public IEnumerator HoldingScanTagsThePlaceInFrontAndLettingGoFadesIt()
        {
            using (var input = new VirtualInput())
            {
                yield return SliceTestScene.LoadAndPlay();
                yield return null;
                // stand 20 m short of the noodle bar, facing it
                RpgLocation noodle = Rpg.Core.GetLocation("noodle");
                RpgLocation plaza = Rpg.Core.GetLocation("plaza");
                Vector3 towards = new Vector3((float)(plaza.X - noodle.X), 0, (float)(plaza.Z - noodle.Z)).normalized;
                Vector3 at = new Vector3((float)noodle.X, Player.transform.position.y + 0.3f, (float)noodle.Z) + towards * 20;
                Player.Teleport(at, Mathf.Atan2(-towards.x, -towards.z) * Mathf.Rad2Deg);
                Physics.SyncTransforms();
                yield return null;

                input.Hold(Key.F);
                yield return new WaitForSecondsRealtime(1f);
                Assert.Greater(Ui.Scanner.Charge, 0.9);
                Assert.AreEqual(UiScreen.None, Ui.Top, "an overlay, not a screen");
                Assert.AreEqual(1f, Time.timeScale);
                Assert.IsTrue(Player.enabled, "the player can still move while scanning");
                Assert.IsNotEmpty(Ui.Scanner.Tagged);
                Assert.IsTrue(Ui.Scanner.Tagged.Any(t => t.Contact.Name == noodle.Name.ToUpperInvariant() && t.Contact.Action == "TALK TO MAMA KAZ"), "the noodle bar is tagged with what can be done there");
                StringAssert.EndsWith("CONTACTS", Ui.Scanner.State.TrimEnd());

                input.Release(Key.F);
                yield return new WaitForSecondsRealtime(1f);
                Assert.Less(Ui.Scanner.Charge, 0.01);
                Assert.IsEmpty(Ui.Scanner.Tagged);
            }
        }

        [UnityTest]
        public IEnumerator TheScannerDoesNotRunUnderAGameScreen()
        {
            using (var input = new VirtualInput())
            {
                yield return SliceTestScene.LoadAndPlay();
                yield return null;
                Ui.ToggleMenu(MenuTab.Inventory);
                yield return null;
                input.Hold(Key.F);
                yield return new WaitForSecondsRealtime(0.5f);
                Assert.Less(Ui.Scanner.Charge, 0.01);
                input.Release(Key.F);
            }
        }
    }
}
#endif
