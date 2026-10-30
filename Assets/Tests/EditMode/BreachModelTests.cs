using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The breach screen's view of the core's breach: live and spent cells, the line in force, daemon progress and failure, the timer, and the outcome.</summary>
    public class BreachModelTests
    {
        static RpgCore Core() => new RpgCore(null, 20771, new MemorySaveStorage(), () => 1700000000000) { AutosaveEnabled = false };

        [Test]
        public void ANewBreachLightsRowZeroAndNothingIsSpent()
        {
            RpgCore core = Core();
            Assert.IsTrue(core.StartBreach("market_terminal").Ok);
            var m = new BreachModel();
            m.Show(core.GetBreach());
            Assert.IsTrue(m.Running);
            Assert.AreEqual(m.Size, m.Matrix.Length);
            Assert.IsNotEmpty(m.TargetName);
            for (int r = 0; r < m.Size; r++)
            {
                for (int c = 0; c < m.Size; c++)
                {
                    Assert.AreEqual(r == 0 ? BreachCell.Live : BreachCell.Dim, m.Cell(r, c), $"{r},{c}");
                    Assert.AreEqual(r == 0, m.OnLine(r, c));
                }
            }
            Assert.IsEmpty(m.Buffer);
            Assert.AreEqual(1.0, m.TimeShare, 1e-3, "the core rounds the time to hundredths");
            Assert.IsFalse(m.Urgent);
            Assert.IsTrue(m.Daemons.All(d => !d.Solved && !d.Failed && d.Matched == 0));
            Assert.AreEqual("", m.Outcome());
        }

        [Test]
        public void APickSpendsTheCellAndTurnsTheLineToItsColumn()
        {
            RpgCore core = Core();
            core.StartBreach("market_terminal");
            Assert.IsTrue(core.BreachSelect(0, 2).Ok);
            var m = new BreachModel();
            m.Show(new JsonObject { ["session"] = core.GetBreach() });
            Assert.AreEqual(BreachCell.Spent, m.Cell(0, 2));
            Assert.AreEqual("col", m.Axis);
            Assert.AreEqual(2, m.Lock);
            Assert.AreEqual(BreachCell.Live, m.Cell(1, 2));
            Assert.AreEqual(BreachCell.Dim, m.Cell(1, 1));
            Assert.AreEqual(1, m.Buffer.Count);
        }

        [Test]
        public void FollowingTheSolutionUploadsEveryDaemonAndEndsTheBreach()
        {
            RpgCore core = Core();
            core.StartBreach("market_terminal");
            var m = new BreachModel();
            foreach (GridCell p in core.Breach.S.SolutionPath)
            {
                if (core.Breach.Done) break;
                Assert.IsTrue(core.BreachSelect(p.R, p.C).Ok);
            }
            m.Show(core.GetBreach());
            Assert.IsFalse(m.Running);
            Assert.AreEqual("done", m.State);
            Assert.IsTrue(m.Daemons.Any(d => d.Solved));
            StringAssert.StartsWith("BREACH COMPLETE", m.Outcome());
            for (int r = 0; r < m.Size; r++)
            {
                for (int c = 0; c < m.Size; c++) Assert.AreNotEqual(BreachCell.Live, m.Cell(r, c), "nothing is live once it is over");
            }
        }

        [Test]
        public void TheTailOfTheBufferLightsADaemonsFirstCodesAndADaemonFailsWhenItCannotFit()
        {
            var buffer = new List<string> { "1C", "55", "BD" };
            Assert.AreEqual(2, BreachModel.MatchOffset(buffer, new[] { "55", "BD", "E9" }));
            Assert.AreEqual(1, BreachModel.MatchOffset(buffer, new[] { "BD", "FF" }));
            Assert.AreEqual(0, BreachModel.MatchOffset(buffer, new[] { "E9", "FF" }));
            Assert.AreEqual(0, BreachModel.MatchOffset(new List<string>(), new[] { "E9" }));
            Assert.IsTrue(BreachModel.CanStillFit(buffer, new[] { "55", "BD", "E9" }, 4), "one slot left, one code to go");
            Assert.IsFalse(BreachModel.CanStillFit(buffer, new[] { "E9", "FF" }, 4), "one slot left, two codes to go");
            Assert.IsTrue(BreachModel.CanStillFit(buffer, new[] { "E9", "FF" }, 5));
        }

        [Test]
        public void TheTimerTurnsUrgentInItsLastQuarterAndAFailureSaysSo()
        {
            RpgCore core = Core();
            core.StartBreach("market_terminal");
            double limit = core.Breach.S.TimeLimit;
            core.Tick(limit * 0.8);
            var m = new BreachModel();
            m.Show(core.GetBreach());
            Assert.IsTrue(m.Urgent);
            core.Tick(limit);
            m.Show(core.GetBreach());
            Assert.AreEqual("failed", m.State);
            Assert.IsFalse(m.Urgent, "not urgent once it is over");
            Assert.AreEqual("BREACH FAILED", m.Outcome());
        }
    }
}
