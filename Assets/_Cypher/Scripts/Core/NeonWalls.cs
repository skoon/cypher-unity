using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Builds the facade plane list the neon planner works from, as City._buildBuilding does: every edge
    /// of every volume, flagged street or alley by what stands in front of it, and dropped when buried.
    /// </summary>
    public static class NeonWalls
    {
        /// <summary>Edges shorter than this are not worth a plane.</summary>
        const double MinEdge = 0.9;

        /// <summary>
        /// Probe close in: alleys are only ~2.6 m wide, so a long probe lands in the opposite lot and
        /// would wrongly mark an alley facade as buried.
        /// </summary>
        const double BuriedProbe = 1.15;

        /// <summary>Distance at which open space means a street rather than an alley.</summary>
        const double StreetProbe = 6;

        public static List<NeonWall> Build(CityPlan plan, CityWindow window)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var walls = new List<NeonWall>();
            double baseY = CityChunker.BuildingBaseY;

            foreach (Building b in plan.Buildings)
            {
                if (!window.Contains(b.Lot.Gx, b.Lot.Gz)) continue;
                foreach (Volume v in b.Volumes)
                {
                    Rect2 bnd = Bounds(v.Poly);
                    int n = v.Poly.Length;
                    for (int e = 0; e < n; e++)
                    {
                        Point2 p = v.Poly[e], q = v.Poly[(e + 1) % n];
                        WallFrame w = WallFrame.Create(p.X, p.Z, q.X, q.Z, bnd.CentreX, bnd.CentreZ);
                        if (w.Length < MinEdge) continue;

                        double mx = (p.X + q.X) / 2, mz = (p.Z + q.Z) / 2;
                        bool buried = plan.IsBlocked(mx + w.Nx * BuriedProbe, mz + w.Nz * BuriedProbe)
                            || InsideSibling(b, v, mx + w.Nx * BuriedProbe, mz + w.Nz * BuriedProbe);
                        bool onRoad = !buried && !plan.IsBlocked(mx + w.Nx * StreetProbe, mz + w.Nz * StreetProbe);

                        walls.Add(new NeonWall
                        {
                            Gx = b.Lot.Gx, Gz = b.Lot.Gz,
                            Ox = w.Ox, Oz = w.Oz, Tx = w.Tx, Tz = w.Tz, Nx = w.Nx, Nz = w.Nz,
                            Length = w.Length, Mx = mx, Mz = mz,
                            Y0 = v.Y0 + baseY, Y1 = v.Y1 + baseY,
                            Street = !buried && onRoad,
                            Alley = !buried && !onRoad,
                            Ground = v.Y0 < 0.01,
                        });
                    }
                }
            }
            return walls;
        }

        /// <summary>The planner's own usability filter (Neon._validWalls): street or alley, long and tall enough.</summary>
        public static bool IsUsable(NeonWall w) =>
            (w.Street || w.Alley) && w.Length > 2.4 && w.Y1 > w.Y0 + 1.5;

        static bool InsideSibling(Building b, Volume vol, double x, double z)
        {
            foreach (Volume o in b.Volumes)
            {
                if (ReferenceEquals(o, vol) || o.Y1 <= vol.Y0 + 0.01 || o.Y0 >= vol.Y1 - 0.01) continue;
                Rect2 bd = Bounds(o.Poly);
                if (x > bd.X0 + 0.2 && x < bd.X1 - 0.2 && z > bd.Z0 + 0.2 && z < bd.Z1 - 0.2) return true;
            }
            return false;
        }

        static Rect2 Bounds(Point2[] poly)
        {
            double x0 = double.PositiveInfinity, z0 = double.PositiveInfinity;
            double x1 = double.NegativeInfinity, z1 = double.NegativeInfinity;
            foreach (Point2 p in poly)
            {
                x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X);
                z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z);
            }
            return new Rect2(x0, z0, x1, z1);
        }
    }
}
