using System;

namespace Cypher.Core
{
    /// <summary>
    /// An inclusive rectangle of grid cells. The slice builds only a window of the
    /// Grid x Grid city so the walkable area stays inside the triangle budget.
    /// </summary>
    public readonly struct CityWindow
    {
        public readonly int Gx0, Gz0, Gx1, Gz1;

        public CityWindow(int gx0, int gz0, int gx1, int gz1)
        {
            if (gx0 > gx1 || gz0 > gz1) throw new ArgumentException("window is empty");
            if (gx0 < 0 || gz0 < 0 || gx1 >= CityLayout.Grid || gz1 >= CityLayout.Grid)
            {
                throw new ArgumentOutOfRangeException(nameof(gx0), "window leaves the city grid");
            }
            Gx0 = gx0; Gz0 = gz0; Gx1 = gx1; Gz1 = gz1;
        }

        /// <summary>Square window centred on the plaza cell, radius cells each way.</summary>
        public static CityWindow AroundPlaza(int radius)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            int mid = CityLayout.PlazaIndex;
            return new CityWindow(mid - radius, mid - radius, mid + radius, mid + radius);
        }

        public bool Contains(int gx, int gz) => gx >= Gx0 && gx <= Gx1 && gz >= Gz0 && gz <= Gz1;

        /// <summary>
        /// World XZ extent including the road corridor that borders the window on its
        /// low and high sides (each cell's road is its low edge, so the high side adds a Road).
        /// </summary>
        public Rect2 RoadExtent => new Rect2(
            CityLayout.Base + Gx0 * CityLayout.Cell,
            CityLayout.Base + Gz0 * CityLayout.Cell,
            CityLayout.Base + (Gx1 + 1) * CityLayout.Cell + CityLayout.Road,
            CityLayout.Base + (Gz1 + 1) * CityLayout.Cell + CityLayout.Road);
    }
}
