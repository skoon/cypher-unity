using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>Which way the traffic runs over a crossing a pedestrian is about to step into (the prototype's CROSS_* constants).</summary>
    public enum Crossing
    {
        /// <summary>An edge along the pavement: no road to cross.</summary>
        None = 0,

        /// <summary>The pedestrian walks along Z over a road that carries traffic along X.</summary>
        X = 1,

        /// <summary>The pedestrian walks along X over a road that carries traffic along Z.</summary>
        Z = 2,
    }

    /// <summary>One block of the street grid: where it is on the grid and the middle of its footprint.</summary>
    public readonly struct NavBlock
    {
        public readonly int Gx, Gz;
        public readonly double Cx, Cz;

        public NavBlock(int gx, int gz, double cx, double cz)
        {
            Gx = gx; Gz = gz; Cx = cx; Cz = cz;
        }
    }

    /// <summary>The measurements navigation needs from the city's geometry.</summary>
    public readonly struct NavGeometry
    {
        /// <summary>Distance between block centres.</summary>
        public readonly double Cell;

        /// <summary>Side of the buildable block footprint.</summary>
        public readonly double BlockSize;

        /// <summary>How far the raised kerb slab reaches from a block's centre (Chebyshev): the outer edge of the pavement.</summary>
        public readonly double WalkOuter;

        /// <summary>Height of the pavement surface and of the asphalt.</summary>
        public readonly double WalkY, RoadY;

        public NavGeometry(double cell, double blockSize, double walkOuter, double walkY, double roadY)
        {
            if (!(cell > 0)) throw new ArgumentOutOfRangeException(nameof(cell), cell, "the cell must be positive");
            if (!(blockSize > 0 && blockSize < cell)) throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize, "the block must be smaller than the cell");
            if (!(walkOuter > blockSize / 2 && walkOuter < cell / 2)) throw new ArgumentOutOfRangeException(nameof(walkOuter), walkOuter, "the pavement must lie between the block and the road's middle");
            Cell = cell; BlockSize = blockSize; WalkOuter = walkOuter; WalkY = walkY; RoadY = roadY;
        }

        /// <summary>The real city: 62 m cells, 46 m blocks, a pavement 2.5 m beyond each block's footprint, kerb at <see cref="CityLayout.SidewalkHeight"/>.</summary>
        public static NavGeometry City => new NavGeometry(CityLayout.Cell, CityLayout.Block, StreetMesher.SidewalkHalfWidth, CityLayout.SidewalkHeight, 0);
    }

    /// <summary>
    /// Shared navigation data for the people and the traffic, a port of CityNav.js. The prototype probed the sidewalk with physics raycasts to learn
    /// where the kerb ends; the Core knows the geometry exactly, so it is given (<see cref="NavGeometry"/>) and nothing here touches the engine.
    /// <para>
    /// Two things come out. A pedestrian graph: a ring of eight nodes round every block (four corners and four kerb-edge midpoints, in cyclic order)
    /// plus a crossing edge between the facing midpoints of neighbouring blocks, i.e. a mid-block crosswalk. And a vehicle grid: the road centrelines
    /// (<see cref="XLines"/>, <see cref="ZLines"/>) and the intersections they make.
    /// </para>
    /// The blocks must form a full rectangular grid, as the city's do.
    /// </summary>
    public sealed class CityNav
    {
        /// <summary>Nodes round each block.</summary>
        public const int RingNodes = 8;

        // Ring node offsets in units of the ring radius, in cyclic order round a block.
        static readonly int[] RingDx = { -1, 0, 1, 1, 1, 0, -1, -1 };
        static readonly int[] RingDz = { -1, -1, -1, 0, 1, 1, 1, 0 };

        readonly double[] _nx, _nz;
        readonly int[] _nodeBlock;
        readonly int[][] _adj;
        readonly Crossing[][] _adjCross;
        readonly double[] _xCentres, _zCentres;

        public double Cell { get; }
        public double BlockHalf { get; }

        /// <summary>Outer edge of the pavement, as a Chebyshev distance from a block centre.</summary>
        public double WalkOuter { get; }

        public double WalkY { get; }
        public double RoadY { get; }

        /// <summary>Half the width of the drivable asphalt: what the kerb slabs leave behind, at least 2.6 m.</summary>
        public double AsphaltHalf { get; }

        /// <summary>Radius (Chebyshev) of the ring of nodes round a block: the middle of the usable pavement band.</summary>
        public double RingRadius { get; }

        /// <summary>Half the width of the pavement band a pedestrian may wander in, at least 0.25 m.</summary>
        public double WalkBand { get; }

        public IReadOnlyList<NavBlock> Blocks { get; }

        /// <summary>The road centrelines that run along Z (their X coordinates), ascending: one more than there are block columns.</summary>
        public IReadOnlyList<double> XLines { get; }

        /// <summary>The road centrelines that run along X (their Z coordinates), ascending.</summary>
        public IReadOnlyList<double> ZLines { get; }

        public int NodeCount => _nx.Length;

        public static CityNav Build(CityPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return Build(plan.Streets, NavGeometry.City);
        }

        public static CityNav Build(IReadOnlyList<StreetCell> streets, NavGeometry geometry)
        {
            if (streets == null) throw new ArgumentNullException(nameof(streets));
            if (streets.Count == 0) throw new ArgumentException("a city needs at least one block", nameof(streets));
            var blocks = new NavBlock[streets.Count];
            for (int i = 0; i < blocks.Length; i++) blocks[i] = new NavBlock(streets[i].Gx, streets[i].Gz, streets[i].Cx, streets[i].Cz);
            return new CityNav(blocks, geometry);
        }

        CityNav(NavBlock[] blocks, NavGeometry g)
        {
            Cell = g.Cell;
            BlockHalf = g.BlockSize / 2;
            WalkOuter = g.WalkOuter;
            WalkY = g.WalkY;
            RoadY = g.RoadY;
            Blocks = blocks;

            _xCentres = UniqueAxis(blocks, b => b.Cx);
            _zCentres = UniqueAxis(blocks, b => b.Cz);
            if (blocks.Length != _xCentres.Length * _zCentres.Length)
            {
                throw new ArgumentException($"{blocks.Length} blocks do not make a full grid of {_xCentres.Length} columns by {_zCentres.Length} rows");
            }
            XLines = Centrelines(_xCentres, Cell);
            ZLines = Centrelines(_zCentres, Cell);

            AsphaltHalf = Math.Max(2.6, Cell * 0.5 - WalkOuter);
            double bandLo = BlockHalf + 0.45;
            double bandHi = Math.Max(bandLo + 0.3, WalkOuter - 0.45);
            RingRadius = (bandLo + bandHi) * 0.5;
            WalkBand = Math.Max(0.25, (bandHi - bandLo) * 0.5);

            int count = blocks.Length * RingNodes;
            _nx = new double[count];
            _nz = new double[count];
            _nodeBlock = new int[count];
            _adj = new int[count][];
            _adjCross = new Crossing[count][];

            var byGrid = new Dictionary<(int, int), int>();
            for (int bi = 0; bi < blocks.Length; bi++)
            {
                if (!byGrid.TryAdd((blocks[bi].Gx, blocks[bi].Gz), bi)) throw new ArgumentException($"there are two blocks at grid cell ({blocks[bi].Gx}, {blocks[bi].Gz})");
            }

            // Every node starts with its two ring neighbours; crossing edges are appended below.
            var adj = new List<int>[count];
            var cross = new List<Crossing>[count];
            for (int bi = 0; bi < blocks.Length; bi++)
            {
                for (int k = 0; k < RingNodes; k++)
                {
                    int i = bi * RingNodes + k;
                    _nx[i] = blocks[bi].Cx + RingDx[k] * RingRadius;
                    _nz[i] = blocks[bi].Cz + RingDz[k] * RingRadius;
                    _nodeBlock[i] = bi;
                    adj[i] = new List<int> { bi * RingNodes + (k + 7) % RingNodes, bi * RingNodes + (k + 1) % RingNodes };
                    cross[i] = new List<Crossing> { Crossing.None, Crossing.None };
                }
            }

            for (int bi = 0; bi < blocks.Length; bi++)
            {
                NavBlock b = blocks[bi];
                if (byGrid.TryGetValue((b.Gx, b.Gz - 1), out int south)) Link(adj, cross, bi * RingNodes + 1, south * RingNodes + 5, Crossing.X);
                if (byGrid.TryGetValue((b.Gx + 1, b.Gz), out int east)) Link(adj, cross, bi * RingNodes + 3, east * RingNodes + 7, Crossing.Z);
            }

            for (int i = 0; i < count; i++)
            {
                _adj[i] = adj[i].ToArray();
                _adjCross[i] = cross[i].ToArray();
            }
        }

        static void Link(List<int>[] adj, List<Crossing>[] cross, int a, int b, Crossing kind)
        {
            adj[a].Add(b); cross[a].Add(kind);
            adj[b].Add(a); cross[b].Add(kind);
        }

        public double NodeX(int node) => _nx[Check(node)];

        public double NodeZ(int node) => _nz[Check(node)];

        /// <summary>The index (into <see cref="Blocks"/>) of the block a node rings.</summary>
        public int NodeBlock(int node) => _nodeBlock[Check(node)];

        /// <summary>The nodes a node connects to: the two ring neighbours first (previous, next), then a crossing if it is a kerb-edge midpoint facing a neighbour block. Do not modify.</summary>
        public IReadOnlyList<int> Neighbours(int node) => _adj[Check(node)];

        /// <summary>For each entry of <see cref="Neighbours"/>, whether that edge crosses a road and which way the traffic on it runs. Do not modify.</summary>
        public IReadOnlyList<Crossing> NeighbourCrossings(int node) => _adjCross[Check(node)];

        /// <summary>Chebyshev distance from a point to its nearest block centre.</summary>
        public double KerbDistance(double x, double z)
        {
            // the blocks are a full grid, so the nearest one in Chebyshev distance is found one axis at a time
            return Math.Max(Nearest(_xCentres, x), Nearest(_zCentres, z));
        }

        /// <summary>Ground height for a point: the raised kerb inside the slab, the asphalt outside.</summary>
        public double GroundY(double x, double z) => KerbDistance(x, z) <= WalkOuter ? WalkY : RoadY;

        /// <summary>A node within an annulus around (<paramref name="px"/>, <paramref name="pz"/>). Falls back to the last node inside the outer radius, or to a random one.</summary>
        public int NodeInBand(Rng rng, double px, double pz, double minDistance, double maxDistance, int tries = 48)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (minDistance < 0 || maxDistance < minDistance) throw new ArgumentOutOfRangeException(nameof(maxDistance), maxDistance, "the band must have 0 <= min <= max");
            if (tries < 1) throw new ArgumentOutOfRangeException(nameof(tries), tries, "at least one try is needed");

            double min2 = minDistance * minDistance, max2 = maxDistance * maxDistance;
            int fallback = (int)(rng.Next() * NodeCount);
            for (int t = 0; t < tries; t++)
            {
                int i = (int)(rng.Next() * NodeCount);
                double dx = _nx[i] - px, dz = _nz[i] - pz;
                double d2 = dx * dx + dz * dz;
                if (d2 >= min2 && d2 <= max2) return i;
                if (d2 <= max2) fallback = i;
            }
            return fallback;
        }

        int Check(int node)
        {
            if (node < 0 || node >= _nx.Length) throw new ArgumentOutOfRangeException(nameof(node), node, $"there are {_nx.Length} nodes");
            return node;
        }

        static double Nearest(double[] centres, double v)
        {
            double best = double.PositiveInfinity;
            foreach (double c in centres) best = Math.Min(best, Math.Abs(v - c));
            return best;
        }

        /// <summary>The distinct values (within half a metre of each other counting as one), ascending.</summary>
        static double[] UniqueAxis(NavBlock[] blocks, Func<NavBlock, double> axis)
        {
            var values = new List<double>();
            foreach (NavBlock b in blocks)
            {
                double v = axis(b);
                if (!values.Exists(o => Math.Abs(o - v) < 0.5)) values.Add(v);
            }
            values.Sort();
            return values.ToArray();
        }

        /// <summary>Road centrelines from the block centres: halfway between neighbours, and one spacing's half beyond each end.</summary>
        static double[] Centrelines(double[] centres, double cell)
        {
            if (centres.Length == 1) return new[] { centres[0] - cell / 2, centres[0] + cell / 2 };
            var lines = new List<double> { centres[0] - (centres[1] - centres[0]) / 2 };
            for (int i = 0; i + 1 < centres.Length; i++) lines.Add((centres[i] + centres[i + 1]) / 2);
            int n = centres.Length;
            lines.Add(centres[n - 1] + (centres[n - 1] - centres[n - 2]) / 2);
            return lines.ToArray();
        }
    }
}
