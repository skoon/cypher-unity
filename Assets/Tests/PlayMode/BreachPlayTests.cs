#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Cypher.Core;
using Cypher.Player;
using Cypher.Rpg;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>
    /// The breach screen in the real Slice scene: a breach opens it and holds the player while its clock runs; picks go through the cells to the core; solving it
    /// shows the outcome and closes; Esc aborts; running out of time fails.
    /// </summary>
    public class BreachPlayTests
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

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator StartBreach()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Assert.IsTrue(Rpg.Core.StartBreach("market_terminal").Ok);
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator ABreachOpensTheScreenAndItsClockRuns()
        {
            yield return StartBreach();
            Assert.AreEqual(UiScreen.Breach, Ui.Top);
            Assert.IsTrue(Ui.Breach.IsShown);
            Assert.AreEqual(FlowModal.Interaction, SliceTestScene.Flow.Modal);
            Assert.IsFalse(Player.enabled);
            Assert.AreEqual(Rpg.Core.Breach.S.Size, Ui.Breach.Cells.Count);
            Assert.IsTrue(Ui.Breach.Cells[0].All(c => c.ClassListContains("cy-cell--live")), "row 0 is lit");
            double t = Rpg.Core.Breach.S.TimeLeft;
            yield return Frames(10);
            Assert.Less(Rpg.Core.Breach.S.TimeLeft, t, "the clock runs while the screen is open");
        }

        [UnityTest]
        public IEnumerator AClickOnALiveCellPicksItAndOnADimOneDoesNothing()
        {
            yield return StartBreach();
            Ui.Breach.Cells[1][1].Activate();
            yield return null;
            Assert.IsEmpty(Rpg.Core.Breach.S.Buffer, "a dim cell is not picked");
            Ui.Breach.Cells[0][2].Activate();
            yield return null;
            Assert.AreEqual(1, Rpg.Core.Breach.S.Buffer.Count);
            Assert.IsTrue(Ui.Breach.Cells[0][2].ClassListContains("cy-cell--spent"));
            Assert.IsTrue(Ui.Breach.Cells[1][2].ClassListContains("cy-cell--live"), "the column of the pick is lit next");
        }

        [UnityTest]
        public IEnumerator SolvingItShowsTheOutcomeThenClosesAndGivesThePlayerBack()
        {
            yield return StartBreach();
            foreach (GridCell p in Rpg.Core.Breach.S.SolutionPath.ToList())
            {
                if (Rpg.Core.Breach.Done) break;
                Ui.Breach.Cells[p.R][p.C].Activate();
                yield return null;
            }
            Assert.IsTrue(Rpg.Core.Breach.Done);
            StringAssert.StartsWith("BREACH COMPLETE", Ui.Breach.Outcome);
            Assert.AreEqual(UiScreen.Breach, Ui.Top, "the outcome stays up for a moment");
            yield return new WaitForSecondsRealtime((float)BreachScreen.OutcomeSeconds + 0.3f);
            Assert.AreEqual(UiScreen.None, Ui.Top);
            Assert.AreEqual(FlowModal.None, SliceTestScene.Flow.Modal);
            Assert.IsTrue(Player.enabled);
        }

        [UnityTest]
        public IEnumerator EscAbortsTheBreach()
        {
            yield return StartBreach();
            Ui.OnBack(fromBButton: false);
            yield return null;
            Assert.IsTrue(Rpg.Core.Breach.Done);
            Assert.AreEqual("cancelled", Rpg.Core.Breach.S.Reason);
            Assert.AreEqual(UiScreen.None, Ui.Top);
            Assert.AreEqual(FlowModal.None, SliceTestScene.Flow.Modal);
        }

        [UnityTest]
        public IEnumerator RunningOutOfTimeFails()
        {
            yield return StartBreach();
            Rpg.Core.Breach.S.TimeLeft = 0.05;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Frames(10);
            Assert.AreEqual("timeout", Rpg.Core.Breach.S.Reason);
            Assert.AreEqual("BREACH FAILED", Ui.Breach.Outcome);
        }
    }
}
#endif
