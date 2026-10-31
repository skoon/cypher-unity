using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// A place to fight from: where to stand hidden (<see cref="Hide"/>), where to step to shoot (<see cref="Peek"/>: round the edge of tall cover, or the same spot
    /// standing up behind low cover), and which way the cover faces (<see cref="NormalX"/>, <see cref="NormalZ"/>: out of the cover, towards the one hiding; the
    /// cover itself, and the danger it hides from, lie the other way).
    /// </summary>
    public sealed class CoverPoint
    {
        public readonly int Id;
        public readonly V3 Hide, Peek;
        public readonly double NormalX, NormalZ;

        /// <summary>Low cover (a crate, a barrier): hide crouched, stand to shoot. Tall cover (a corner, a dumpster stood on end): step out to shoot.</summary>
        public readonly bool Low;

        /// <summary>The enemy holding it, or -1.</summary>
        public int TakenBy { get; internal set; } = -1;

        public CoverPoint(int id, V3 hide, V3 peek, double normalX, double normalZ, bool low)
        {
            Id = id; Hide = hide; Peek = peek; NormalX = normalX; NormalZ = normalZ; Low = low;
        }

        /// <summary>Whether this cover is between its holder and a danger at <paramref name="threat"/>: the danger lies behind the cover, not off to its side.</summary>
        public bool Faces(V3 threat)
        {
            double dx = threat.X - Hide.X, dz = threat.Z - Hide.Z, d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-6) return false;
            return (dx * NormalX + dz * NormalZ) / d < -CoverMap.MinFacing;
        }
    }

    /// <summary>
    /// The city's cover, made from what is already there: every prop collider from 0.8 m up (crates, barriers, dumpsters, kiosks; low below 1.4 m) gives points on
    /// its faces, and every building corner gives a point either side of it. Kept in a grid for asking "the best cover near here against a danger there".
    /// </summary>
    public sealed class CoverMap
    {
        public const double Standoff = 0.55;       // metres out from a face
        public const double LowMin = 0.8, TallMin = 1.4;
        public const double MinWidth = 0.7;        // a face narrower than this hides nobody
        public const double PeekStep = 0.85;       // round an edge
        public const double LowSpacing = 1.6;      // between points along a long low face
        public const double MinFacing = 0.35;      // cos of how far off the line behind the cover the danger may be
        public const double CellSize = 8;

        /// <summary>Eye heights hiding behind low cover, and standing.</summary>
        public const double CrouchEye = 0.95;

        readonly List<CoverPoint> _points = new List<CoverPoint>();
        readonly Dictionary<(int, int), List<CoverPoint>> _grid = new Dictionary<(int, int), List<CoverPoint>>();

        public IReadOnlyList<CoverPoint> Points => _points;

        /// <summary>
        /// The cover of a city: its props' colliders (<paramref name="boxes"/>, turned about Y, centre height <c>Cy</c> over the ground they stand on) and its buildings'
        /// footprints (axis-aligned, <paramref name="buildings"/> as centre and size). <paramref name="groundY"/> is where the feet go at a point (the pavement or the road).
        /// </summary>
        public static CoverMap Build(IEnumerable<PropBox> boxes, IEnumerable<(double X, double Z, double Width, double Depth)> buildings, Func<double, double, double> groundY)
        {
            if (boxes == null) throw new ArgumentNullException(nameof(boxes));
            if (buildings == null) throw new ArgumentNullException(nameof(buildings));
            if (groundY == null) throw new ArgumentNullException(nameof(groundY));
            var map = new CoverMap();
            foreach (PropBox b in boxes) map.AddBox(b, groundY);
            foreach (var bl in buildings) map.AddBuilding(bl.X, bl.Z, bl.Width, bl.Depth, groundY);
            return map;
        }

        void Add(double x, double z, double px, double pz, double nx, double nz, bool low, Func<double, double, double> groundY)
        {
            var p = new CoverPoint(_points.Count, new V3(x, groundY(x, z), z), new V3(px, groundY(px, pz), pz), nx, nz, low);
            _points.Add(p);
            var key = ((int)Math.Floor(x / CellSize), (int)Math.Floor(z / CellSize));
            if (!_grid.TryGetValue(key, out List<CoverPoint> cell)) _grid[key] = cell = new List<CoverPoint>();
            cell.Add(p);
        }

        void AddBox(PropBox b, Func<double, double, double> groundY)
        {
            double top = b.Cy + b.Hy - groundY(b.Cx, b.Cz);
            if (top < LowMin) return;
            bool low = top < TallMin;
            // the box's own axes in the world: local x and local z turned by its yaw (the prototype's rotation about Y)
            double c = Math.Cos(b.Yaw), s = Math.Sin(b.Yaw);
            (double X, double Z) ax = (c, -s), az = (s, c);
            foreach ((double nx, double nz, double half, double along, double tx, double tz) in new[]
            {
                (ax.X, ax.Z, b.Hx, b.Hz, az.X, az.Z), (-ax.X, -ax.Z, b.Hx, b.Hz, az.X, az.Z),
                (az.X, az.Z, b.Hz, b.Hx, ax.X, ax.Z), (-az.X, -az.Z, b.Hz, b.Hx, ax.X, ax.Z),
            })
            {
                if (along * 2 < MinWidth) continue;
                double fx = b.Cx + nx * (half + Standoff), fz = b.Cz + nz * (half + Standoff);
                if (low)
                {
                    // along the face, standing up to shoot over it
                    int n = Math.Max(1, (int)Math.Floor(along * 2 / LowSpacing));
                    for (int i = 0; i < n; i++)
                    {
                        double t = n == 1 ? 0 : -along + LowSpacing / 2 + i * (along * 2 - LowSpacing) / (n - 1);
                        double x = fx + tx * t, z = fz + tz * t;
                        Add(x, z, x, z, nx, nz, true, groundY);
                    }
                }
                else
                {
                    // near each end, stepping out round it to shoot
                    foreach (int side in new[] { -1, 1 })
                    {
                        double t = side * Math.Max(0, along - 0.3);
                        double x = fx + tx * t, z = fz + tz * t;
                        double out_ = side * (along + PeekStep);
                        Add(x, z, fx + tx * out_, fz + tz * out_, nx, nz, false, groundY);
                    }
                }
            }
        }

        void AddBuilding(double cx, double cz, double width, double depth, Func<double, double, double> groundY)
        {
            double hx = width / 2, hz = depth / 2;
            foreach (int sx in new[] { -1, 1 })
            {
                foreach (int sz in new[] { -1, 1 })
                {
                    // the corner, and a point on each of its two faces just short of it, stepping out past it to shoot
                    double kx = cx + sx * hx, kz = cz + sz * hz;
                    // on the face whose normal is x: stand 0.4 m short of the corner along z, step out past it
                    Add(kx + sx * Standoff, kz - sz * 0.4, kx + sx * Standoff, kz + sz * PeekStep, sx, 0, false, groundY);
                    Add(kx - sx * 0.4, kz + sz * Standoff, kx + sx * PeekStep, kz + sz * Standoff, 0, sz, false, groundY);
                }
            }
        }

        /// <summary>Every point within <paramref name="radius"/> of a place, nearest first.</summary>
        public List<CoverPoint> Near(V3 at, double radius)
        {
            var found = new List<(double, CoverPoint)>();
            int x0 = (int)Math.Floor((at.X - radius) / CellSize), x1 = (int)Math.Floor((at.X + radius) / CellSize);
            int z0 = (int)Math.Floor((at.Z - radius) / CellSize), z1 = (int)Math.Floor((at.Z + radius) / CellSize);
            for (int gx = x0; gx <= x1; gx++)
            {
                for (int gz = z0; gz <= z1; gz++)
                {
                    if (!_grid.TryGetValue((gx, gz), out List<CoverPoint> cell)) continue;
                    foreach (CoverPoint p in cell)
                    {
                        double dx = p.Hide.X - at.X, dz = p.Hide.Z - at.Z, d = Math.Sqrt(dx * dx + dz * dz);
                        if (d <= radius) found.Add((d, p));
                    }
                }
            }
            found.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.Id.CompareTo(b.Item2.Id));
            var list = new List<CoverPoint>(found.Count);
            foreach (var f in found) list.Add(f.Item2);
            return list;
        }

        /// <summary>
        /// The best free cover for one at <paramref name="from"/> fighting a danger whose eyes are at <paramref name="threatEye"/>: it must face the danger, hide its holder
        /// (no clear line from the hiding eye) and let it shoot (a clear line from the peeking eye), at a distance from the danger near <paramref name="preferred"/>.
        /// Scored by how far off that distance it is plus how far there is to go to it; null when nothing within <paramref name="searchRadius"/> will do. <paramref name="exclude"/>
        /// is the spot being left, which is not offered again.
        /// </summary>
        public CoverPoint Find(V3 from, V3 threatEye, double preferred, IEnemyWorld world, double searchRadius = 25, CoverPoint exclude = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            CoverPoint best = null;
            double bestScore = double.PositiveInfinity;
            foreach (CoverPoint p in Near(from, searchRadius))
            {
                if (p.TakenBy >= 0 || p == exclude || !p.Faces(threatEye)) continue;
                double dx = threatEye.X - p.Hide.X, dz = threatEye.Z - p.Hide.Z, d = Math.Sqrt(dx * dx + dz * dz);
                if (d < preferred * 0.5 || d > preferred * 1.6) continue;
                double travel = Math.Sqrt((p.Hide.X - from.X) * (p.Hide.X - from.X) + (p.Hide.Z - from.Z) * (p.Hide.Z - from.Z));
                double score = Math.Abs(d - preferred) + 0.6 * travel;
                if (score >= bestScore) continue;
                // the expensive checks last
                V3 hideEye = new V3(p.Hide.X, p.Hide.Y + (p.Low ? CrouchEye : EnemySim.EyeHeight), p.Hide.Z);
                V3 peekEye = new V3(p.Peek.X, p.Peek.Y + EnemySim.EyeHeight, p.Peek.Z);
                if (world.Clear(hideEye, threatEye) || !world.Clear(peekEye, threatEye)) continue;
                best = p;
                bestScore = score;
            }
            return best;
        }

        public void Take(CoverPoint p, int enemy)
        {
            if (p.TakenBy >= 0 && p.TakenBy != enemy) throw new InvalidOperationException($"cover {p.Id} is held by {p.TakenBy}");
            p.TakenBy = enemy;
        }

        public void Release(CoverPoint p, int enemy)
        {
            if (p.TakenBy == enemy) p.TakenBy = -1;
        }
    }
}
