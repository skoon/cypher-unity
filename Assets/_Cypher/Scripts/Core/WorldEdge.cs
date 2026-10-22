using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Where the playable world ends. The city's outer roads are the last places that belong to it; beyond them is a strip
    /// of open roadbed (the apron), and then an invisible wall. The skyline ring (<see cref="SkylineRing"/>) stands further
    /// out still, so the city seems to carry on into the haze without there being anywhere to walk.
    /// </summary>
    public static class WorldEdge
    {
        /// <summary>Half the width of the whole city including its outer roads, about the plaza at 0,0.</summary>
        public const double CityHalf = -CityLayout.Base;

        /// <summary>Open roadbed between the outermost road and the invisible wall.</summary>
        public const double Apron = 30;

        /// <summary>The player may stand anywhere with |x| and |z| at most this.</summary>
        public const double PlayHalf = CityHalf + Apron;

        public const double WallThickness = 20;
        public const double WallHeight = 400;

        public static bool IsInsidePlayArea(double x, double z) => Math.Abs(x) <= PlayHalf && Math.Abs(z) <= PlayHalf;

        /// <summary>
        /// Four boxes that enclose the play area, west, east, south, north. Thick and tall so nothing tunnels through or
        /// jumps over, and overlapping at the corners so there is no gap.
        /// </summary>
        public static IReadOnlyList<Aabb> Walls()
        {
            double inner = PlayHalf;
            double outer = PlayHalf + WallThickness;
            return new[]
            {
                new Aabb(-outer, -1, -outer, -inner, WallHeight, outer),   // west
                new Aabb(inner, -1, -outer, outer, WallHeight, outer),     // east
                new Aabb(-outer, -1, -outer, outer, WallHeight, -inner),   // south
                new Aabb(-outer, -1, inner, outer, WallHeight, outer),     // north
            };
        }
    }
}
