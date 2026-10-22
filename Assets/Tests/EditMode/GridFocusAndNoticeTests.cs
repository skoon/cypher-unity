using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class GridFocusTests
    {
        // The Controls screen: four movement rows with two columns, three action rows with three.
        static readonly IReadOnlyList<IReadOnlyList<bool>> Controls = new List<IReadOnlyList<bool>>
        {
            new[] { true, true, false },
            new[] { true, true, false },
            new[] { true, true, false },
            new[] { true, true, false },
            new[] { true, true, true },
            new[] { true, true, true },
            new[] { true, true, true },
        };

        static GridPosition P(int r, int c) => new GridPosition(r, c);

        static void AssertAt(GridPosition expected, GridPosition actual, string why = null) =>
            Assert.AreEqual((expected.Row, expected.Column), (actual.Row, actual.Column), why);

        [Test]
        public void DownAndUpKeepTheColumn()
        {
            AssertAt(P(1, 1), GridFocus.Move(Controls, P(0, 1), MoveDirection.Down));
            AssertAt(P(0, 1), GridFocus.Move(Controls, P(1, 1), MoveDirection.Up));
            AssertAt(P(5, 2), GridFocus.Move(Controls, P(4, 2), MoveDirection.Down));
        }

        [Test]
        public void MovingFromAWideRowToANarrowOneTakesTheNearestExistingCell()
        {
            AssertAt(P(3, 1), GridFocus.Move(Controls, P(4, 2), MoveDirection.Up), "the gamepad column has no cell in the movement rows");
            AssertAt(P(4, 1), GridFocus.Move(Controls, P(3, 1), MoveDirection.Down));
        }

        [Test]
        public void LeftAndRightMoveAlongTheRowAndSkipMissingCells()
        {
            AssertAt(P(4, 1), GridFocus.Move(Controls, P(4, 0), MoveDirection.Right));
            AssertAt(P(4, 2), GridFocus.Move(Controls, P(4, 1), MoveDirection.Right));
            AssertAt(P(4, 1), GridFocus.Move(Controls, P(4, 2), MoveDirection.Left));
            AssertAt(P(0, 1), GridFocus.Move(Controls, P(0, 0), MoveDirection.Right));
        }

        [Test]
        public void NothingWrapsAndTheEdgesStayPut()
        {
            AssertAt(P(0, 0), GridFocus.Move(Controls, P(0, 0), MoveDirection.Up));
            AssertAt(P(6, 2), GridFocus.Move(Controls, P(6, 2), MoveDirection.Down));
            AssertAt(P(0, 0), GridFocus.Move(Controls, P(0, 0), MoveDirection.Left));
            AssertAt(P(0, 1), GridFocus.Move(Controls, P(0, 1), MoveDirection.Right), "the third cell of a movement row does not exist");
            AssertAt(P(4, 2), GridFocus.Move(Controls, P(4, 2), MoveDirection.Right));
        }

        [Test]
        public void RowsWithNoCellsAreSkipped()
        {
            var grid = new List<IReadOnlyList<bool>> { new[] { true, true }, new[] { false, false }, new[] { true, true } };
            AssertAt(P(2, 0), GridFocus.Move(grid, P(0, 0), MoveDirection.Down));
            AssertAt(P(0, 1), GridFocus.Move(grid, P(2, 1), MoveDirection.Up));
        }

        [Test]
        public void ATieBetweenTwoNearestCellsGoesToTheLowerColumn()
        {
            var grid = new List<IReadOnlyList<bool>> { new[] { false, true, false }, new[] { true, false, true } };
            AssertAt(P(1, 0), GridFocus.Move(grid, P(0, 1), MoveDirection.Down));
        }

        [Test]
        public void FirstIsTheTopLeftAvailableCell()
        {
            Assert.AreEqual((0, 0), (GridFocus.First(Controls).Value.Row, GridFocus.First(Controls).Value.Column));
            var grid = new List<IReadOnlyList<bool>> { new[] { false, false }, new[] { false, true } };
            Assert.AreEqual((1, 1), (GridFocus.First(grid).Value.Row, GridFocus.First(grid).Value.Column));
            Assert.IsNull(GridFocus.First(new List<IReadOnlyList<bool>> { new[] { false } }));
            Assert.IsNull(GridFocus.First(new List<IReadOnlyList<bool>>()));
        }

        [Test]
        public void StartingFromACellThatDoesNotExistIsAnError()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GridFocus.Move(Controls, P(0, 2), MoveDirection.Down));
            Assert.Throws<ArgumentOutOfRangeException>(() => GridFocus.Move(Controls, P(7, 0), MoveDirection.Down));
            Assert.Throws<ArgumentOutOfRangeException>(() => GridFocus.Move(Controls, P(-1, 0), MoveDirection.Down));
            Assert.Throws<ArgumentNullException>(() => GridFocus.Move(null, P(0, 0), MoveDirection.Down));
        }
    }

    public class NoticeQueueTests
    {
        [Test]
        public void AMessageShowsUntilItsTimeIsUp()
        {
            var q = new NoticeQueue();
            q.Add("hello", 100);
            CollectionAssert.AreEqual(new[] { "hello" }, q.Visible(100));
            CollectionAssert.AreEqual(new[] { "hello" }, q.Visible(100 + NoticeQueue.LifetimeSeconds - 0.01));
            Assert.IsEmpty(q.Visible(100 + NoticeQueue.LifetimeSeconds));
        }

        [Test]
        public void MessagesStackOldestFirstAndOnlyTheNewestAreKept()
        {
            var q = new NoticeQueue();
            q.Add("one", 0);
            q.Add("two", 1);
            q.Add("three", 2);
            q.Add("four", 3);
            CollectionAssert.AreEqual(new[] { "two", "three", "four" }, q.Visible(3));
            Assert.AreEqual(3, NoticeQueue.MaxVisible);
        }

        [Test]
        public void TheSameMessageAgainRefreshesItsTimeInsteadOfStacking()
        {
            var q = new NoticeQueue();
            q.Add("same", 0);
            q.Add("other", 1);
            q.Add("same", 8);
            CollectionAssert.AreEqual(new[] { "same", "other" }, q.Visible(9), "still in its original place");
            CollectionAssert.AreEqual(new[] { "same" }, q.Visible(11), "the other expired, the refreshed one did not");
            Assert.IsEmpty(q.Visible(8 + NoticeQueue.LifetimeSeconds));
        }

        [Test]
        public void ARepeatOfAnExpiredMessageIsANewOne()
        {
            var q = new NoticeQueue();
            q.Add("again", 0);
            q.Add("again", 50);
            CollectionAssert.AreEqual(new[] { "again" }, q.Visible(51));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void EmptyMessagesAreIgnored(string text)
        {
            var q = new NoticeQueue();
            q.Add(text, 0);
            Assert.IsEmpty(q.Visible(0));
        }

        [Test]
        public void ThePlayerCanDismissOne()
        {
            var q = new NoticeQueue();
            q.Add("a", 0);
            q.Add("b", 0);
            Assert.IsTrue(q.Dismiss(0, 1));
            CollectionAssert.AreEqual(new[] { "b" }, q.Visible(1));
            Assert.IsFalse(q.Dismiss(5, 1));
            Assert.IsFalse(q.Dismiss(-1, 1));
        }

        [Test]
        public void ClearEmptiesIt()
        {
            var q = new NoticeQueue();
            q.Add("a", 0);
            q.Clear();
            Assert.IsEmpty(q.Visible(0));
        }
    }
}
