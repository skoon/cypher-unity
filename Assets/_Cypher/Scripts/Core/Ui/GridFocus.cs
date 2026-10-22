using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    public enum MoveDirection { Up, Down, Left, Right }

    public readonly struct GridPosition
    {
        public readonly int Row;
        public readonly int Column;

        public GridPosition(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public override string ToString() => $"({Row}, {Column})";
    }

    /// <summary>
    /// Arrow-key and d-pad movement over a table of focusable cells where some cells do not exist (the movement rows of the Controls
    /// screen have no gamepad column). Up and Down keep the column when they can, otherwise take the nearest cell in the new row;
    /// Left and Right move along the row; nothing wraps. Pure, so the movement is tested without a screen.
    /// </summary>
    public static class GridFocus
    {
        /// <summary>The cell to move to, or <paramref name="from"/> itself when there is nowhere to go in that direction.</summary>
        public static GridPosition Move(IReadOnlyList<IReadOnlyList<bool>> available, GridPosition from, MoveDirection direction)
        {
            if (available == null) throw new ArgumentNullException(nameof(available));
            if (from.Row < 0 || from.Row >= available.Count || from.Column < 0 || from.Column >= available[from.Row].Count || !available[from.Row][from.Column])
            {
                throw new ArgumentOutOfRangeException(nameof(from), from, "the starting cell is not an available cell of the table");
            }

            switch (direction)
            {
                case MoveDirection.Left:
                case MoveDirection.Right:
                    int step = direction == MoveDirection.Right ? 1 : -1;
                    for (int c = from.Column + step; c >= 0 && c < available[from.Row].Count; c += step)
                    {
                        if (available[from.Row][c]) return new GridPosition(from.Row, c);
                    }
                    return from;

                default:
                    int rowStep = direction == MoveDirection.Down ? 1 : -1;
                    for (int r = from.Row + rowStep; r >= 0 && r < available.Count; r += rowStep)
                    {
                        int column = NearestAvailable(available[r], from.Column);
                        if (column >= 0) return new GridPosition(r, column);
                    }
                    return from;
            }
        }

        /// <summary>The first available cell, reading across then down; null when there are none.</summary>
        public static GridPosition? First(IReadOnlyList<IReadOnlyList<bool>> available)
        {
            for (int r = 0; r < available.Count; r++)
            {
                for (int c = 0; c < available[r].Count; c++)
                {
                    if (available[r][c]) return new GridPosition(r, c);
                }
            }
            return null;
        }

        static int NearestAvailable(IReadOnlyList<bool> row, int wanted)
        {
            int best = -1;
            int bestDistance = int.MaxValue;
            for (int c = 0; c < row.Count; c++)
            {
                if (!row[c]) continue;
                int distance = Math.Abs(c - wanted);
                if (distance < bestDistance)   // on a tie the lower column wins, because it is found first
                {
                    best = c;
                    bestDistance = distance;
                }
            }
            return best;
        }
    }
}
