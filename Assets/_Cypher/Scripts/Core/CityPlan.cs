using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    public readonly struct Point2
    {
        public readonly double X;
        public readonly double Z;

        public Point2(double x, double z)
        {
            X = x;
            Z = z;
        }
    }

    public struct Rect2
    {
        public double X0, Z0, X1, Z1;

        public Rect2(double x0, double z0, double x1, double z1)
        {
            X0 = x0;
            Z0 = z0;
            X1 = x1;
            Z1 = z1;
        }

        public double Width => X1 - X0;
        public double Depth => Z1 - Z0;
        public double CentreX => (X0 + X1) / 2;
        public double CentreZ => (Z0 + Z1) / 2;
    }

    public enum BuildingType
    {
        Slab,
        LShape,
        Podium,
        Setback,
        Ziggurat,
        Twin,
    }

    public enum AlleyAxis
    {
        X,
        Z,
    }

    /// <summary>One grid cell: its block centre and whether it is the open plaza.</summary>
    public sealed class StreetCell
    {
        public int Gx, Gz;
        public double Cx, Cz;
        public bool IsPlaza;
    }

    public sealed class Lot
    {
        public Rect2 Rect;
        public int Gx, Gz;
        public bool Vacant;
        public Building Building;
    }

    /// <summary>A walkable gap left by a lot subdivision cut.</summary>
    public sealed class Alley
    {
        public Rect2 Rect;
        public AlleyAxis Axis;
    }

    /// <summary>A coarse massing prism: a convex footprint extruded from Y0 to Y1.</summary>
    public sealed class Volume
    {
        public Point2[] Poly;
        public double Y0, Y1;
        public int BuildingIndex;
    }

    public readonly struct Spire
    {
        public readonly double X, Z, Y, Radius, Height;

        public Spire(double x, double z, double y, double radius, double height)
        {
            X = x;
            Z = z;
            Y = y;
            Radius = radius;
            Height = height;
        }
    }

    public readonly struct Mast
    {
        public readonly double X, Z, Y, Height;

        public Mast(double x, double z, double y, double height)
        {
            X = x;
            Z = z;
            Y = y;
            Height = height;
        }
    }

    public sealed class Building
    {
        public double X, Z, Width, Depth, Height;
        public int Floors;
        public BuildingType Type;
        public double[] Tint;
        public int RowBase, ColBase;
        public double UvOffset;
        public readonly List<Volume> Volumes = new List<Volume>();
        public Lot Lot;
        public Spire? Spire;
        public Mast? Mast;
    }

    /// <summary>
    /// The city's massing plan: lots, alleys and building volumes for the whole
    /// Grid x Grid layout. A direct port of City._layout / _subdivide / _makeBuilding
    /// from the web prototype. The Rng is consumed in exactly the prototype's order,
    /// so the same seed yields the same city (verified against golden fixtures).
    /// No Unity types: geometry is built from this elsewhere.
    /// </summary>
    public sealed class CityPlan
    {
        public const uint DefaultSeed = 20771;

        static readonly double[][] Tints =
        {
            new[] { 0.66, 0.68, 0.74 }, new[] { 0.60, 0.57, 0.53 }, new[] { 0.50, 0.55, 0.60 },
            new[] { 0.58, 0.50, 0.45 }, new[] { 0.44, 0.48, 0.52 }, new[] { 0.70, 0.67, 0.61 },
            new[] { 0.52, 0.56, 0.54 }, new[] { 0.63, 0.60, 0.66 },
        };

        public uint Seed { get; }
        public readonly List<StreetCell> Streets = new List<StreetCell>();
        public readonly List<Lot> Lots = new List<Lot>();
        public readonly List<Alley> Alleys = new List<Alley>();
        public readonly List<Volume> Volumes = new List<Volume>();
        public readonly List<Building> Buildings = new List<Building>();

        Rng _rng;

        CityPlan(uint seed)
        {
            Seed = seed;
        }

        public static CityPlan Generate(uint seed = DefaultSeed)
        {
            var plan = new CityPlan(seed) { _rng = new Rng(seed) };
            plan.Layout();
            plan._rng = null;
            return plan;
        }

        /// <summary>True when an XZ point lies inside a non-vacant lot.</summary>
        public bool IsBlocked(double x, double z)
        {
            foreach (Lot l in Lots)
            {
                if (!l.Vacant && x >= l.Rect.X0 && x <= l.Rect.X1 && z >= l.Rect.Z0 && z <= l.Rect.Z1)
                {
                    return true;
                }
            }
            return false;
        }

        static double JsRound(double v) => Math.Floor(v + 0.5);

        static Point2[] RectPoly(double x0, double z0, double x1, double z1) =>
            new[] { new Point2(x0, z0), new Point2(x1, z0), new Point2(x1, z1), new Point2(x0, z1) };

        static Point2[] ChamferPoly(double x0, double z0, double x1, double z1, double c)
        {
            double w = x1 - x0, d = z1 - z0;
            double cc = Math.Min(c, Math.Min(w * 0.35, d * 0.35));
            return new[]
            {
                new Point2(x0 + cc, z0), new Point2(x1 - cc, z0), new Point2(x1, z0 + cc), new Point2(x1, z1 - cc),
                new Point2(x1 - cc, z1), new Point2(x0 + cc, z1), new Point2(x0, z1 - cc), new Point2(x0, z0 + cc),
            };
        }

        static Rect2 PolyBounds(Point2[] poly)
        {
            double x0 = double.PositiveInfinity, z0 = double.PositiveInfinity;
            double x1 = double.NegativeInfinity, z1 = double.NegativeInfinity;
            foreach (Point2 p in poly)
            {
                if (p.X < x0) x0 = p.X;
                if (p.X > x1) x1 = p.X;
                if (p.Z < z0) z0 = p.Z;
                if (p.Z > z1) z1 = p.Z;
            }
            return new Rect2(x0, z0, x1, z1);
        }

        static Rect2 Shrink(Rect2 r, double k) => new Rect2(r.X0 + k, r.Z0 + k, r.X1 - k, r.Z1 - k);

        void Layout()
        {
            int mid = CityLayout.PlazaIndex;
            for (int gz = 0; gz < CityLayout.Grid; gz++)
            {
                for (int gx = 0; gx < CityLayout.Grid; gx++)
                {
                    double cx = CityLayout.CellCentre(gx);
                    double cz = CityLayout.CellCentre(gz);
                    bool isPlaza = gx == mid && gz == mid;
                    Streets.Add(new StreetCell { Gx = gx, Gz = gz, Cx = cx, Cz = cz, IsPlaza = isPlaza });
                    if (isPlaza) continue;

                    // Irregular outline: each side pulls back a different amount.
                    double x0 = cx - CityLayout.Block / 2 + _rng.Next() * 3.6;
                    double x1 = cx + CityLayout.Block / 2 - _rng.Next() * 3.6;
                    double z0 = cz - CityLayout.Block / 2 + _rng.Next() * 3.6;
                    double z1 = cz + CityLayout.Block / 2 - _rng.Next() * 3.6;

                    double dx = gx - mid, dz = gz - mid;
                    double dist = Math.Min(1, Math.Sqrt(dx * dx + dz * dz) / (CityLayout.Grid / 2.0));
                    List<Rect2> rects = Subdivide(new Rect2(x0, z0, x1, z1));

                    var lots = new List<Lot>(rects.Count);
                    foreach (Rect2 r in rects)
                    {
                        var lot = new Lot { Rect = r, Gx = gx, Gz = gz };
                        lot.Vacant = rects.Count > 1 && _rng.Next() < 0.07;
                        lots.Add(lot);
                        Lots.Add(lot);
                    }
                    foreach (Lot lot in lots)
                    {
                        if (lot.Vacant) continue;
                        MakeBuilding(lot, dist);
                    }
                }
            }
        }

        /// <summary>
        /// Recursive BSP with a physical gap left at every cut; the gaps become alleys.
        /// The first cut always spans the whole block so every block has a cut-through.
        /// </summary>
        List<Rect2> Subdivide(Rect2 block)
        {
            var output = new List<Rect2>();
            Recurse(block, 0, output);
            return output;
        }

        void Recurse(Rect2 r, int depth, List<Rect2> output)
        {
            double w = r.Width, d = r.Depth;
            bool split = depth == 0
                ? Math.Max(w, d) > 24
                : depth < 4 && Math.Max(w, d) > 20 && _rng.Next() < 0.78;
            if (!split)
            {
                output.Add(r);
                return;
            }

            double gap = 2.6 + _rng.Next() * 2.6;
            bool alongX = w >= d ? _rng.Next() < 0.85 : _rng.Next() < 0.15;
            double span = alongX ? w : d;
            double t = 0.3 + _rng.Next() * 0.4;
            double cut = (alongX ? r.X0 : r.Z0) + span * t;
            double lo = cut - gap / 2, hi = cut + gap / 2;
            const double minSide = 8.5;

            if (alongX)
            {
                if (lo - r.X0 < minSide || r.X1 - hi < minSide)
                {
                    output.Add(r);
                    return;
                }
                Alleys.Add(new Alley { Rect = new Rect2(lo, r.Z0, hi, r.Z1), Axis = AlleyAxis.Z });
                Recurse(new Rect2(r.X0, r.Z0, lo, r.Z1), depth + 1, output);
                Recurse(new Rect2(hi, r.Z0, r.X1, r.Z1), depth + 1, output);
            }
            else
            {
                if (lo - r.Z0 < minSide || r.Z1 - hi < minSide)
                {
                    output.Add(r);
                    return;
                }
                Alleys.Add(new Alley { Rect = new Rect2(r.X0, lo, r.X1, hi), Axis = AlleyAxis.X });
                Recurse(new Rect2(r.X0, r.Z0, r.X1, lo), depth + 1, output);
                Recurse(new Rect2(r.X0, hi, r.X1, r.Z1), depth + 1, output);
            }
        }

        Volume Push(Building b, Point2[] poly, double y0, double y1)
        {
            var v = new Volume { Poly = poly, Y0 = y0, Y1 = y1, BuildingIndex = Buildings.Count - 1 };
            b.Volumes.Add(v);
            Volumes.Add(v);
            return v;
        }

        /// <summary>Choose a massing archetype for a lot and emit its stack of volumes.</summary>
        void MakeBuilding(Lot lot, double dist)
        {
            Rect2 lr = lot.Rect;
            double w = lr.Width, d = lr.Depth;
            double cx = lr.CentreX, cz = lr.CentreZ;
            const double fh = CityLayout.FloorHeight;

            double baseH = ((1 - dist) * 104 + dist * 24) * (0.40 + _rng.Next() * 1.05);
            int floors = (int)Math.Max(2, JsRound(baseH / fh));
            if (_rng.Next() < 0.06)
            {
                floors = (int)JsRound(floors * (1.5 + _rng.Next() * 0.9));
            }
            floors = Math.Min(floors, 62);
            double h = floors * fh;

            var b = new Building
            {
                X = cx, Z = cz, Width = w, Depth = d, Height = h,
                Floors = floors,
                Lot = lot,
            };
            b.Tint = Tints[(int)(_rng.Next() * Tints.Length)];
            b.RowBase = (int)(_rng.Next() * CityLayout.WindowAtlas);
            b.ColBase = (int)(_rng.Next() * CityLayout.WindowAtlas);
            b.UvOffset = _rng.Next() * 40;
            lot.Building = b;
            Buildings.Add(b);

            // Corner lots get a chamfer, one of the strongest cheap silhouette reads.
            double corner = _rng.Next() < 0.32 ? 1.6 + _rng.Next() * 2.6 : 0;
            Point2[] Poly(Rect2 r) => corner > 0
                ? ChamferPoly(r.X0, r.Z0, r.X1, r.Z1, corner)
                : RectPoly(r.X0, r.Z0, r.X1, r.Z1);

            double pick = _rng.Next();
            BuildingType type;
            if (floors <= 5)
            {
                type = pick < 0.6 ? BuildingType.Slab : BuildingType.LShape;
            }
            else if (floors <= 13)
            {
                type = pick < 0.32 ? BuildingType.Podium
                    : pick < 0.6 ? BuildingType.Slab
                    : pick < 0.82 ? BuildingType.LShape
                    : BuildingType.Setback;
            }
            else
            {
                type = pick < 0.34 ? BuildingType.Setback
                    : pick < 0.58 ? BuildingType.Podium
                    : pick < 0.8 ? BuildingType.Ziggurat
                    : BuildingType.Twin;
            }
            if (Math.Min(w, d) < 15 && (type == BuildingType.Twin || type == BuildingType.Podium))
            {
                type = BuildingType.Setback;
            }
            b.Type = type;

            switch (type)
            {
                case BuildingType.Slab:
                    Push(b, Poly(lr), 0, h);
                    break;
                case BuildingType.LShape:
                    MakeLShape(b, lr, floors, h);
                    break;
                case BuildingType.Podium:
                    MakePodium(b, lr, h, Poly);
                    break;
                case BuildingType.Twin:
                    MakeTwin(b, lr, h, Poly);
                    break;
                default:
                    MakeSetback(b, lr, type, h, Poly);
                    break;
            }

            // Massing may top out below the nominal height; republish the real one.
            double maxY = 0;
            foreach (Volume v in b.Volumes) maxY = Math.Max(maxY, v.Y1);
            b.Height = maxY;
            b.Floors = (int)Math.Max(1, JsRound(b.Height / fh));

            MakeCrown(b, floors);
        }

        void MakeLShape(Building b, Rect2 lot, int floors, double h)
        {
            double w = lot.Width, d = lot.Depth;
            bool horiz = _rng.Next() < 0.5;
            double k = 0.42 + _rng.Next() * 0.22;
            double h2 = Math.Max(2, JsRound(floors * (0.55 + _rng.Next() * 0.4))) * CityLayout.FloorHeight;
            if (horiz)
            {
                double zm = lot.Z0 + d * k;
                Push(b, RectPoly(lot.X0, lot.Z0, lot.X1, zm), 0, h);
                Push(b, RectPoly(lot.X0, zm - 0.4, lot.X0 + w * (0.4 + _rng.Next() * 0.25), lot.Z1), 0, h2);
            }
            else
            {
                double xm = lot.X0 + w * k;
                Push(b, RectPoly(lot.X0, lot.Z0, xm, lot.Z1), 0, h);
                Push(b, RectPoly(xm - 0.4, lot.Z0, lot.X1, lot.Z0 + d * (0.4 + _rng.Next() * 0.25)), 0, h2);
            }
        }

        void MakePodium(Building b, Rect2 lot, double h, Func<Rect2, Point2[]> poly)
        {
            double w = lot.Width, d = lot.Depth;
            int pf = 2 + (int)(_rng.Next() * 4);
            double py = pf * CityLayout.FloorHeight;
            Push(b, poly(lot), 0, py);
            double tw = w * (0.5 + _rng.Next() * 0.28);
            double td = d * (0.5 + _rng.Next() * 0.28);
            double ox = lot.X0 + (w - tw) * _rng.Next();
            double oz = lot.Z0 + (d - td) * _rng.Next();
            var tower = new Rect2(ox, oz, ox + tw, oz + td);
            Push(b, poly(tower), py, Math.Max(py + CityLayout.FloorHeight * 3, h));
        }

        void MakeTwin(Building b, Rect2 lot, double h, Func<Rect2, Point2[]> poly)
        {
            double w = lot.Width, d = lot.Depth;
            int pf = 2 + (int)(_rng.Next() * 3);
            double py = pf * CityLayout.FloorHeight;
            Push(b, poly(lot), 0, py);
            bool alongX = w >= d;
            double g = 3.2 + _rng.Next() * 3;
            if (alongX)
            {
                double hw = (w - g) / 2;
                Push(b, RectPoly(lot.X0 + 1.5, lot.Z0 + 2, lot.X0 + hw, lot.Z1 - 2), py, h);
                Push(b, RectPoly(lot.X1 - hw, lot.Z0 + 2.5, lot.X1 - 1.5, lot.Z1 - 2.5), py, h * (0.7 + _rng.Next() * 0.3));
            }
            else
            {
                double hd = (d - g) / 2;
                Push(b, RectPoly(lot.X0 + 2, lot.Z0 + 1.5, lot.X1 - 2, lot.Z0 + hd), py, h);
                Push(b, RectPoly(lot.X0 + 2.5, lot.Z1 - hd, lot.X1 - 2.5, lot.Z1 - 1.5), py, h * (0.7 + _rng.Next() * 0.3));
            }
        }

        void MakeSetback(Building b, Rect2 lot, BuildingType type, double h, Func<Rect2, Point2[]> poly)
        {
            const double fh = CityLayout.FloorHeight;
            int tiers = type == BuildingType.Ziggurat ? 4 + (int)(_rng.Next() * 3) : 2 + (int)(_rng.Next() * 2);
            Rect2 r = lot;
            double y = 0;
            for (int i = 0; i < tiers; i++)
            {
                double frac = i == tiers - 1 ? 1 : 0.28 + _rng.Next() * 0.3;
                double top = i == tiers - 1
                    ? h
                    : Math.Min(h - fh, y + Math.Max(2, JsRound((h - y) * frac / fh)) * fh);
                if (top <= y + fh) break;
                Push(b, i == 0 ? poly(r) : RectPoly(r.X0, r.Z0, r.X1, r.Z1), y, top);
                y = top;
                double k = Math.Min(Math.Min(r.Width, r.Depth) * 0.16, 2.2 + _rng.Next() * 2.4);
                double drift = (_rng.Next() - 0.5) * k * 0.8;
                r = Shrink(r, k);
                r = new Rect2(r.X0 + drift, r.Z0 - drift, r.X1 + drift, r.Z1 - drift);
                if (Math.Min(r.Width, r.Depth) < 7) break;
            }
        }

        void MakeCrown(Building b, int floors)
        {
            const double fh = CityLayout.FloorHeight;
            Volume top = b.Volumes[0];
            foreach (Volume v in b.Volumes)
            {
                if (v.Y1 > top.Y1) top = v;
            }
            Rect2 tb = PolyBounds(top.Poly);
            double tw = tb.Width, td = tb.Depth;
            double cr = _rng.Next();

            if (floors > 22 && cr < 0.34)
            {
                // Spire: tapered octagon + mast.
                double s0 = Math.Min(tw, td) * 0.5;
                Push(b, ChamferPoly(tb.CentreX - s0 / 2, tb.CentreZ - s0 / 2, tb.CentreX + s0 / 2, tb.CentreZ + s0 / 2, s0 * 0.3),
                    top.Y1, top.Y1 + 6 + _rng.Next() * 8);
                b.Spire = new Spire(tb.CentreX, tb.CentreZ, top.Y1, s0 * 0.5, 14 + _rng.Next() * 26);
            }
            else if (cr < 0.55)
            {
                // Stepped crown: two small setback boxes.
                Rect2 r = tb;
                double y = top.Y1;
                for (int i = 0; i < 2; i++)
                {
                    double k = Math.Min(r.Width * 0.2, 2 + _rng.Next() * 2.5);
                    r = Shrink(r, k);
                    if (Math.Min(r.Width, r.Depth) < 5) break;
                    double hh = fh * (1 + (int)(_rng.Next() * 2));
                    Push(b, RectPoly(r.X0, r.Z0, r.X1, r.Z1), y, y + hh);
                    y += hh;
                }
            }
            else if (cr < 0.7 && Math.Min(tw, td) > 12)
            {
                // Drum / plant enclosure.
                double s0 = Math.Min(tw, td) * (0.42 + _rng.Next() * 0.2);
                Push(b, ChamferPoly(tb.CentreX - s0 / 2, tb.CentreZ - s0 / 2, tb.CentreX + s0 / 2, tb.CentreZ + s0 / 2, s0 * 0.42),
                    top.Y1, top.Y1 + fh * (1.2 + _rng.Next()));
            }

            if (floors > 14 && _rng.Next() < 0.5 && !b.Spire.HasValue)
            {
                double mx = tb.CentreX + (_rng.Next() - 0.5) * tw * 0.5;
                double mz = tb.CentreZ + (_rng.Next() - 0.5) * td * 0.5;
                b.Mast = new Mast(mx, mz, top.Y1, 8 + _rng.Next() * 22);
            }
        }
    }
}
