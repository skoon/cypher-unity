using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Ways across the city on foot: the shortest route over the pavement graph (<see cref="CityNav"/>: round the blocks and over the crosswalks) between the nodes
    /// nearest two places, A*. Where the straight line is open the caller walks it instead; this is for going round buildings.
    /// </summary>
    public sealed class EnemyPaths
    {
        readonly CityNav _nav;

        public EnemyPaths(CityNav nav)
        {
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        }

        public CityNav Nav => _nav;

        public int NearestNode(double x, double z)
        {
            int best = 0;
            double bestD = double.PositiveInfinity;
            for (int i = 0; i < _nav.NodeCount; i++)
            {
                double dx = _nav.NodeX(i) - x, dz = _nav.NodeZ(i) - z, d = dx * dx + dz * dz;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        double Dist(int a, int b)
        {
            double dx = _nav.NodeX(a) - _nav.NodeX(b), dz = _nav.NodeZ(a) - _nav.NodeZ(b);
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The nodes from <paramref name="from"/> to <paramref name="to"/>, both included; a single node when they are the same.</summary>
        public List<int> Route(int from, int to)
        {
            int n = _nav.NodeCount;
            if (from < 0 || from >= n) throw new ArgumentOutOfRangeException(nameof(from));
            if (to < 0 || to >= n) throw new ArgumentOutOfRangeException(nameof(to));
            var g = new double[n];
            var came = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                g[i] = double.PositiveInfinity;
                came[i] = -1;
            }
            g[from] = 0;
            var open = new List<int> { from };
            while (open.Count > 0)
            {
                // the open node with the least g + h (the graph is a few hundred nodes: a list will do)
                int bi = 0;
                double bf = double.PositiveInfinity;
                for (int i = 0; i < open.Count; i++)
                {
                    double f = g[open[i]] + Dist(open[i], to);
                    if (f < bf || (f == bf && open[i] < open[bi]))
                    {
                        bf = f;
                        bi = i;
                    }
                }
                int cur = open[bi];
                open.RemoveAt(bi);
                if (cur == to) break;
                if (closed[cur]) continue;
                closed[cur] = true;
                foreach (int nb in _nav.Neighbours(cur))
                {
                    if (closed[nb]) continue;
                    double cost = g[cur] + Dist(cur, nb);
                    if (cost >= g[nb]) continue;
                    g[nb] = cost;
                    came[nb] = cur;
                    open.Add(nb);
                }
            }
            if (double.IsPositiveInfinity(g[to])) throw new InvalidOperationException($"no way on foot from node {from} to node {to}");
            var path = new List<int>();
            for (int c = to; c != -1; c = came[c]) path.Add(c);
            path.Reverse();
            return path;
        }

        /// <summary>Points to walk from one place to another round the blocks: the nearest node to the start, the route, and the end itself.</summary>
        public List<V3> Waypoints(V3 from, V3 to)
        {
            List<int> nodes = Route(NearestNode(from.X, from.Z), NearestNode(to.X, to.Z));
            var points = new List<V3>(nodes.Count + 1);
            foreach (int i in nodes) points.Add(new V3(_nav.NodeX(i), _nav.GroundY(_nav.NodeX(i), _nav.NodeZ(i)), _nav.NodeZ(i)));
            points.Add(to);
            return points;
        }
    }
}
