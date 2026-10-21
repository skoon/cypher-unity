using System;

namespace Cypher.Core
{
    /// <summary>
    /// Wall frame for a footprint edge, ported from makeWall() in City.js. The
    /// tangent is oriented so the quad (s0,y0)->(s1,y0)->(s1,y1)->(s0,y1) faces
    /// the outward normal.
    /// </summary>
    public readonly struct WallFrame
    {
        public readonly double Ox, Oz, Tx, Tz, Nx, Nz, Length;

        WallFrame(double ox, double oz, double tx, double tz, double nx, double nz, double length)
        {
            Ox = ox; Oz = oz; Tx = tx; Tz = tz; Nx = nx; Nz = nz; Length = length;
        }

        public static WallFrame Create(double ax, double az, double bx, double bz, double cx, double cz)
        {
            double ex = bx - ax, ez = bz - az;
            double len = Math.Max(Math.Sqrt(ex * ex + ez * ez), 1e-6);
            double nx = ez / len, nz = -ex / len;
            double mx = (ax + bx) / 2, mz = (az + bz) / 2;
            double dIn = (mx - cx) * (mx - cx) + (mz - cz) * (mz - cz);
            double dOut = (mx + nx - cx) * (mx + nx - cx) + (mz + nz - cz) * (mz + nz - cz);
            if (dOut < dIn)
            {
                nx = -nx;
                nz = -nz;
            }
            double tx = nz, tz = -nx;
            bool flip = ex * tx + ez * tz < 0;
            return new WallFrame(flip ? bx : ax, flip ? bz : az, tx, tz, nx, nz, len);
        }

        public Float3 At(double s, double y, double offset = 0) =>
            new Float3(Ox + Tx * s + Nx * offset, y, Oz + Tz * s + Nz * offset);
    }

    /// <summary>
    /// Turns building volumes into two meshes: windowed facade walls and plain
    /// roofs. This is the Milestone 1 massing-level look: one tiled-atlas quad per
    /// wall, no recessed openings. Walls hidden inside a sibling volume are skipped.
    /// </summary>
    public sealed class BuildingMesher
    {
        readonly MeshBuilder _walls;
        readonly MeshBuilder _roofs;

        public BuildingMesher(MeshBuilder walls, MeshBuilder roofs)
        {
            _walls = walls ?? throw new ArgumentNullException(nameof(walls));
            _roofs = roofs ?? throw new ArgumentNullException(nameof(roofs));
        }

        /// <summary>Vertical grime gradient: buildings are filthy at street level.</summary>
        public static float Shade(double y)
        {
            double g = 0.40 + 0.60 * (1 - Math.Exp(-Math.Max(0, y) / 12));
            return (float)g;
        }

        public void AddBuilding(Building b)
        {
            Rgb tint = new Rgb((float)b.Tint[0], (float)b.Tint[1], (float)b.Tint[2]);
            foreach (Volume v in b.Volumes)
            {
                AddWalls(b, v, tint);
                AddRoof(v, tint);
            }
        }

        void AddWalls(Building b, Volume v, Rgb tint)
        {
            if (v.Y1 <= v.Y0) throw new InvalidOperationException("volume has no height");
            Rect2 bounds = Bounds(v.Poly);
            double ccx = bounds.CentreX, ccz = bounds.CentreZ;
            Rgb low = tint.Scale(Shade(v.Y0));
            Rgb high = tint.Scale(Shade(v.Y1));

            for (int i = 0; i < v.Poly.Length; i++)
            {
                Point2 p = v.Poly[i];
                Point2 q = v.Poly[(i + 1) % v.Poly.Length];
                WallFrame w = WallFrame.Create(p.X, p.Z, q.X, q.Z, ccx, ccz);
                if (w.Length < 0.05) continue;

                double mx = (p.X + q.X) / 2 + w.Nx * 0.05;
                double mz = (p.Z + q.Z) / 2 + w.Nz * 0.05;
                double yBottom = LowestVisibleY(b, v, mx, mz);
                if (yBottom >= v.Y1 - 0.01) continue;

                // Atlas UVs: one pane per window pitch / storey, offset per building
                // so neighbouring towers do not light up in lockstep.
                float u0 = (float)((b.ColBase + 0.0) / CityLayout.WindowAtlas);
                float u1 = (float)((b.ColBase + w.Length / CityLayout.WindowPitch) / CityLayout.WindowAtlas);
                float v0 = (float)((b.RowBase + yBottom / CityLayout.FloorHeight) / CityLayout.WindowAtlas);
                float v1 = (float)((b.RowBase + v.Y1 / CityLayout.FloorHeight) / CityLayout.WindowAtlas);
                Rgb bottom = yBottom > v.Y0 ? tint.Scale(Shade(yBottom)) : low;

                _walls.Quad(
                    w.At(0, yBottom), w.At(w.Length, yBottom), w.At(w.Length, v.Y1), w.At(0, v.Y1),
                    new[] { u0, v0, u1, v0, u1, v1, u0, v1 }, bottom, high);
            }
        }

        void AddRoof(Volume v, Rgb tint)
        {
            var pts = new Float3[v.Poly.Length];
            var uv = new float[v.Poly.Length * 2];
            // Fan triangulation facing +Y needs the polygon wound so cross(b-a,c-a) points up.
            bool up = SignedAreaXZ(v.Poly) < 0;
            for (int i = 0; i < v.Poly.Length; i++)
            {
                Point2 p = v.Poly[up ? i : v.Poly.Length - 1 - i];
                pts[i] = new Float3(p.X, v.Y1, p.Z);
                uv[i * 2] = (float)(p.X / MeshBuilder.ConcreteUvScale);
                uv[i * 2 + 1] = (float)(p.Z / MeshBuilder.ConcreteUvScale);
            }
            _roofs.Face(pts, uv, tint.Scale(0.55f));
        }

        static double SignedAreaXZ(Point2[] poly)
        {
            double a = 0;
            for (int i = 0; i < poly.Length; i++)
            {
                Point2 p = poly[i], q = poly[(i + 1) % poly.Length];
                a += p.X * q.Z - q.X * p.Z;
            }
            return a / 2;
        }

        /// <summary>
        /// Lowest Y at which a wall point (x,z) is not buried inside a sibling volume.
        /// The prototype skips the wall outright (City._insideSibling); raising the
        /// bottom instead keeps the part of a tall wing that stands above a short one.
        /// </summary>
        static double LowestVisibleY(Building b, Volume vol, double x, double z)
        {
            double y = vol.Y0;
            bool raised = true;
            while (raised)
            {
                raised = false;
                foreach (Volume o in b.Volumes)
                {
                    if (ReferenceEquals(o, vol) || o.Y0 > y + 0.01 || o.Y1 <= y + 0.01) continue;
                    Rect2 bd = Bounds(o.Poly);
                    if (x > bd.X0 + 0.2 && x < bd.X1 - 0.2 && z > bd.Z0 + 0.2 && z < bd.Z1 - 0.2)
                    {
                        y = o.Y1;
                        raised = true;
                    }
                }
            }
            return Math.Min(y, vol.Y1);
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
