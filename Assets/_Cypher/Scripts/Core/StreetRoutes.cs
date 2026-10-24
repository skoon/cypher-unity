using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Routes for the benchmark's pilot that a person could walk, made of the pavement ring round each block and the mid-block crosswalks between neighbouring blocks: the very graph the
    /// city's people use (<see cref="CityNav"/>). A route is a closed walk through a cycle of neighbouring blocks. Inside a block it goes the short way round the ring from the side it came
    /// in on to the side it leaves by; between blocks it crosses the road at the crosswalk. The tours (<see cref="BenchmarkPilot.CityRoute"/>) walk the road centrelines instead, which is where
    /// traffic stops for the player and then holds it up.
    /// </summary>
    public static class StreetRoutes
    {
        /// <summary>How close to a corner, or to either end of a crosswalk, counts as having arrived: the pavement is only 1.6 m wide.</summary>
        public const double TurnRadius = 1.0;

        /// <summary>A node halfway along a straight stretch of pavement does not need to be hit squarely.</summary>
        public const double PassRadius = BenchmarkPilot.ArrivalRadius;

        // The ring's nodes in cyclic order are SW, S, SE, E, NE, N, NW, W: odd ones are the middles of the sides, where the crosswalks are.
        const int South = 1, East = 3, North = 5, West = 7;

        /// <summary>
        /// A closed walk through <paramref name="cycle"/>, a list of grid cells each next to the one before (and the last next to the first). The walk starts at the first block, on the
        /// side it is entered from, and the pilot's first leg is a walk to that point from wherever it stands. Every <paramref name="sprintEvery"/>th leg along a pavement is run
        /// (0 never); crossings are always walked.
        /// </summary>
        public static IReadOnlyList<BenchmarkPilot.Waypoint> Loop(CityNav nav, IReadOnlyList<(int gx, int gz)> cycle, int sprintEvery)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            if (cycle == null) throw new ArgumentNullException(nameof(cycle));
            if (cycle.Count < 4) throw new ArgumentException("a loop of blocks needs at least four", nameof(cycle));
            if (sprintEvery < 0) throw new ArgumentOutOfRangeException(nameof(sprintEvery), sprintEvery, "a stride cannot be negative");

            var blockAt = new Dictionary<(int, int), int>();
            for (int i = 0; i < nav.Blocks.Count; i++) blockAt[(nav.Blocks[i].Gx, nav.Blocks[i].Gz)] = i;
            foreach ((int gx, int gz) cell in cycle)
            {
                if (!blockAt.ContainsKey(cell)) throw new ArgumentException($"the city has no block at ({cell.gx}, {cell.gz})", nameof(cycle));
            }

            // for each block: the side it is entered from and the side it is left by
            int n = cycle.Count;
            var arrive = new int[n];
            var depart = new int[n];
            for (int i = 0; i < n; i++)
            {
                (int gx, int gz) here = cycle[i], next = cycle[(i + 1) % n];
                depart[i] = SideToward(next.gx - here.gx, next.gz - here.gz, here, next);
                arrive[(i + 1) % n] = SideToward(here.gx - next.gx, here.gz - next.gz, next, here);
            }

            var route = new List<BenchmarkPilot.Waypoint>();
            int ringLegs = 0;
            for (int i = 0; i < n; i++)
            {
                int bi = blockAt[cycle[i]];
                int from = arrive[i], to = depart[i];
                int steps = (to - from + 8) % 8;
                int direction = steps <= 4 ? 1 : -1;
                if (steps > 4) steps = 8 - steps;

                for (int s = 0; s <= steps; s++)
                {
                    int k = ((from + direction * s) % 8 + 8) % 8;
                    int node = bi * CityNav.RingNodes + k;
                    bool corner = k % 2 == 0;
                    bool end = s == 0 || s == steps;
                    bool sprint = false;
                    if (s > 0)
                    {
                        ringLegs++;
                        sprint = sprintEvery > 0 && ringLegs % sprintEvery == 0;
                    }
                    // the leg that ends at a block's first node is the crossing from the block before: always walked
                    route.Add(new BenchmarkPilot.Waypoint(nav.NodeX(node), nav.NodeZ(node), sprint, corner || end ? TurnRadius : PassRadius));
                }
            }
            return route;
        }

        /// <summary>The loop round the eight blocks that surround the block diagonally beyond the plaza, starting with the plaza block itself, where the player stands: about 690 m.</summary>
        public static IReadOnlyList<BenchmarkPilot.Waypoint> PlazaRing(CityNav nav, int sprintEvery = 3)
        {
            int p = CityLayout.PlazaIndex;
            return Loop(nav, new[]
            {
                (p, p), (p + 1, p), (p + 2, p), (p + 2, p + 1), (p + 2, p + 2), (p + 1, p + 2), (p, p + 2), (p, p + 1),
            }, sprintEvery);
        }

        /// <summary>A short loop round the four blocks at one junction and over its four crosswalks, walked; the plaza block is one of them, and the first. About 250 m.</summary>
        public static IReadOnlyList<BenchmarkPilot.Waypoint> JunctionLoop(CityNav nav)
        {
            int p = CityLayout.PlazaIndex;
            return Loop(nav, new[] { (p, p), (p - 1, p), (p - 1, p - 1), (p, p - 1) }, 0);
        }

        /// <summary>The ring node in the middle of the side of a block that faces the neighbour at (dx, dz).</summary>
        static int SideToward(int dx, int dz, (int gx, int gz) from, (int gx, int gz) to)
        {
            if (dx == 1 && dz == 0) return East;
            if (dx == -1 && dz == 0) return West;
            if (dx == 0 && dz == 1) return North;
            if (dx == 0 && dz == -1) return South;
            throw new ArgumentException($"blocks ({from.gx}, {from.gz}) and ({to.gx}, {to.gz}) are not neighbours");
        }
    }
}
