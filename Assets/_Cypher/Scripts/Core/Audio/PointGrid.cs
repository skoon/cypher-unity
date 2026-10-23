using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Points in space indexed by a grid on the ground, to find the nearest to a place without looking at all of them. The city has thousands of tubes and vents and the sound wants the nearest
    /// of each a few times a second.
    /// </summary>
    public sealed class PointGrid
    {
        readonly double[] _x, _y, _z;
        readonly double _cell;
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

        public PointGrid(IReadOnlyList<(double x, double y, double z)> points, double cellSize)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (cellSize <= 0 || double.IsNaN(cellSize)) throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "a cell has a size");
            _cell = cellSize;
            int n = points.Count;
            _x = new double[n];
            _y = new double[n];
            _z = new double[n];
            for (int i = 0; i < n; i++)
            {
                (double x, double y, double z) = points[i];
                if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) throw new ArgumentException($"point {i} is not a place", nameof(points));
                _x[i] = x;
                _y[i] = y;
                _z[i] = z;
                long key = Key(CellOf(x), CellOf(z));
                if (!_cells.TryGetValue(key, out List<int> list)) _cells[key] = list = new List<int>();
                list.Add(i);
            }
        }

        public int Count => _x.Length;

        long CellOf(double v) => (long)Math.Floor(v / _cell);

        static long Key(long cx, long cz) => (cx << 32) ^ (cz & 0xffffffffL);

        /// <summary>The index of the point nearest (x, y, z) that is less than <paramref name="range"/> away, or -1 if there is none. The nearer of two equally near is the one added first.</summary>
        public int Nearest(double x, double y, double z, double range)
        {
            if (range <= 0 || double.IsNaN(range)) throw new ArgumentOutOfRangeException(nameof(range), range, "a range is positive");
            double best = range * range;
            int found = -1;
            long cx0 = CellOf(x - range), cx1 = CellOf(x + range), cz0 = CellOf(z - range), cz1 = CellOf(z + range);
            if ((double)(cx1 - cx0 + 1) * (cz1 - cz0 + 1) > _cells.Count)
            {
                // a range wider than the points: cheaper to look at every cell that has any
                foreach (List<int> list in _cells.Values) Consider(list, x, y, z, ref best, ref found);
                return found;
            }
            for (long cx = cx0; cx <= cx1; cx++)
            {
                for (long cz = cz0; cz <= cz1; cz++)
                {
                    if (_cells.TryGetValue(Key(cx, cz), out List<int> list)) Consider(list, x, y, z, ref best, ref found);
                }
            }
            return found;
        }

        void Consider(List<int> list, double x, double y, double z, ref double best, ref int found)
        {
            for (int k = 0; k < list.Count; k++)
            {
                int i = list[k];
                double dx = _x[i] - x, dy = _y[i] - y, dz = _z[i] - z;
                double d = dx * dx + dy * dy + dz * dz;
                if (d < best || (d == best && found >= 0 && i < found))
                {
                    best = d;
                    found = i;
                }
            }
        }

        public (double x, double y, double z) Point(int index)
        {
            if (index < 0 || index >= _x.Length) throw new ArgumentOutOfRangeException(nameof(index), index, "no such point");
            return (_x[index], _y[index], _z[index]);
        }
    }
}
