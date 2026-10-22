using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Decides where every prop, decal, cable and puff of steam goes: a port of the placement half of the prototype's Props system
    /// (the wall-run clutter cursor, alley junk, kerb furniture, the strip just off the kerb, wall fixtures, rooftop gear, the plaza,
    /// ground and wall decals, cables and conduit, steam). It reads the city's plan (facade planes, lots, alleys, volumes) and draws from
    /// the same <see cref="Rng"/> in the prototype's order, so a seed always gives the same street. Left out: facade anchors (the
    /// prototype's City published a list of spots for wall fixtures; the pass that walks every facade covers the same ground, and
    /// places the cash machines those anchors held), pooled light sockets, and the kickable props.
    /// </summary>
    public static class PropPlacer
    {
        public const uint DefaultSeed = 60613;

        public static PropLayout Build(CityPlan plan, CityWindow window, uint seed = DefaultSeed)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return new Run(plan, window, seed).Execute();
        }

        sealed class Opts
        {
            public double? Scale;
            public bool Free, NoCollider, Steam;
            public double SteamY = 1.9, SteamR = 0.5;
        }

        struct Lane
        {
            public bool AxisZ;
            public double Cx, Cz, Len, Wide;
        }

        sealed class Run
        {
            const double Tau = 2 * Math.PI;
            const double SY = CityLayout.SidewalkHeight;

            // outward normal and tangent of each of the four sides of a block (prototype SIDES)
            static readonly (double nx, double nz, double tx, double tz)[] Sides =
            {
                (0, -1, 1, 0), (1, 0, 0, 1), (0, 1, -1, 0), (-1, 0, 0, -1),
            };

            static readonly (string name, double w)[] StreetTable =
            {
                ("vending", 7), ("kiosk", 3.5), ("bin", 10), ("bench", 5), ("dumpster", 3.5), ("crate", 6), ("barrel", 4.5), ("cardbox", 8),
                ("bag", 12), ("pallet", 4), ("utilbox", 6), ("stall", 2.6), ("debris", 7), ("cone", 3.5), ("barrier", 2.5), (null, 30),
            };

            static readonly (string name, double w)[] AlleyTable =
            {
                ("dumpster", 10), ("bag", 17), ("cardbox", 12), ("crate", 9), ("pallet", 7), ("barrel", 8), ("debris", 10), ("bin", 8),
                ("ventstack", 3), ("cone", 3), (null, 16),
            };

            readonly CityPlan _plan;
            readonly CityWindow _window;
            readonly Rng _rng;
            readonly PropLayout _layout = new PropLayout();
            readonly HashSet<long> _occ = new HashSet<long>();
            readonly List<Lane> _lanes = new List<Lane>();
            List<NeonWall> _walls;
            readonly double _hw = CityLayout.Block / 2 + CityLayout.SidewalkPad;   // sidewalk half extent per block

            public Run(CityPlan plan, CityWindow window, uint seed)
            {
                _plan = plan;
                _window = window;
                _rng = new Rng(seed);
            }

            double R() => _rng.Next();

            public PropLayout Execute()
            {
                _walls = new List<NeonWall>();
                foreach (NeonWall w in NeonWalls.Build(_plan, _window))
                {
                    if (w.Ground && (w.Street || w.Alley) && w.Length > 1.6) _walls.Add(w);
                }

                // keep the spawn pocket walkable
                Claim(0, 0, 3.4);

                WallRuns();
                AlleyClutter();
                foreach (StreetCell s in _plan.Streets)
                {
                    if (!_window.Contains(s.Gx, s.Gz)) continue;
                    Kerb(s);
                    Kerbside(s);
                    if (s.IsPlaza) Plaza(s);
                }
                FacadeProps();
                RoofProps();
                GroundDecals();
                Cables();
                Steam();
                return _layout;
            }

            // ------------------------------------------------------------ placing

            /// <summary>Coarse one-metre occupancy grid stops props from growing into each other.</summary>
            bool Claim(double x, double z, double r)
            {
                int i0 = (int)Math.Floor(x - r), i1 = (int)Math.Floor(x + r);
                int j0 = (int)Math.Floor(z - r), j1 = (int)Math.Floor(z + r);
                for (int i = i0; i <= i1; i++)
                {
                    for (int j = j0; j <= j1; j++)
                    {
                        if (_occ.Contains((long)i * 8192 + j)) return false;
                    }
                }
                for (int i = i0; i <= i1; i++)
                {
                    for (int j = j0; j <= j1; j++) _occ.Add((long)i * 8192 + j);
                }
                return true;
            }

            void Add(string name, double x, double y, double z, double ry, double scale = 1) =>
                _layout.Instances.Add(new PropInstance(name, x, y, z, ry, scale));

            /// <summary>Place a kit, claiming its footprint and registering its collider and steam spot when it has them.</summary>
            bool Put(string name, double x, double y, double z, double ry, Opts o = null)
            {
                o = o ?? new Opts();
                PropKit kit = PropKits.Get(name);
                double r = Math.Max(kit.Len, kit.Dep) * 0.42;
                if (!o.Free && !Claim(x, z, r)) return false;

                double scale = o.Scale ?? 1;
                Add(name, x, y, z, ry, scale);

                if (kit.Collider.HasValue && !o.NoCollider)
                {
                    PropCollider c = kit.Collider.Value;
                    double cs = Math.Sin(ry), cc = Math.Cos(ry);
                    _layout.Boxes.Add(new PropBox(
                        x + c.Cx * cc + c.Cz * cs, y + c.Cy * scale, z - c.Cx * cs + c.Cz * cc,
                        c.Hx * scale, c.Hy * scale, c.Hz * scale, ry));
                }
                if (o.Steam) _layout.SteamSpots.Add(new SteamSpot(x, y + o.SteamY, z, o.SteamR));
                return true;
            }

            bool Blocked(double x, double z) => _plan.IsBlocked(x, z);

            /// <summary>True when the point is on a raised sidewalk slab (props belong here, not in the road).</summary>
            bool OnSidewalk(double x, double z, double margin = 0)
            {
                int gx = CityLayout.CellIndex(x), gz = CityLayout.CellIndex(z);
                if (gx < 0 || gz < 0 || gx >= CityLayout.Grid || gz >= CityLayout.Grid) return false;
                double cx = CityLayout.CellCentre(gx), cz = CityLayout.CellCentre(gz);
                double hw = _hw - margin;
                return Math.Abs(x - cx) <= hw && Math.Abs(z - cz) <= hw;
            }

            string Pick((string name, double w)[] table)
            {
                double total = 0;
                foreach (var e in table) total += e.w;
                double r = R() * total;
                foreach (var e in table)
                {
                    r -= e.w;
                    if (r <= 0) return e.name;
                }
                return table[table.Length - 1].name;
            }

            // ------------------------------------------------------------ the clutter cursor

            /// <summary>Walks every ground-level facade dropping props against it: this is what makes the street read as lived in.</summary>
            void WallRuns()
            {
                foreach (NeonWall w in _walls)
                {
                    var table = w.Alley ? AlleyTable : StreetTable;
                    double ry = Math.Atan2(w.Nx, w.Nz);
                    double maxDepth = w.Alley ? 1.0 : 1.5;   // alleys are ~2.6 m wide: keep the clear lane down the middle
                    double s = 0.9;
                    while (s < w.Length - 0.9)
                    {
                        string name = Pick(table);
                        if (name == null) { s += 1.1 + R() * 2.4; continue; }
                        PropKit kit = PropKits.Get(name);
                        if (kit.Dep > maxDepth) { s += 0.8; continue; }
                        double at = s + kit.Len * 0.5;
                        if (at > w.Length - 0.7) break;
                        double off = kit.Dep * 0.5 + 0.16;
                        double x = w.Ox + w.Tx * at + w.Nx * off;
                        double z = w.Oz + w.Tz * at + w.Nz * off;
                        s += kit.Len + 0.22 + R() * (w.Alley ? 1.1 : 1.8);

                        if (Blocked(x, z) || !OnSidewalk(x, z, 0.5)) continue;
                        double turn = ry + (R() - 0.5) * 0.22;
                        double scale = 0.93 + R() * 0.15;
                        bool steams = name == "stall" || name == "ventstack";
                        bool placed = Put(name, x, SY, z, turn, new Opts { Scale = scale, Steam = steams, SteamY = name == "stall" ? 2.0 : 1.85, SteamR = 0.6 });
                        if (!placed) continue;

                        // clutter begets clutter: small piles spill out beside the big stuff
                        if (R() > (w.Alley ? 0.35 : 0.6))
                        {
                            string sat = new[] { "bag", "cardbox", "debris" }[(int)(R() * 3)];
                            double so = at + kit.Len * 0.5 + 0.3 + R() * 0.4;
                            double sx = w.Ox + w.Tx * so + w.Nx * 0.42;
                            double sz = w.Oz + w.Tz * so + w.Nz * 0.42;
                            if (!Blocked(sx, sz) && OnSidewalk(sx, sz, 0.3))
                            {
                                double spin = R() * Tau;
                                double smallScale = 0.78 + R() * 0.4;
                                Put(sat, sx, SY, sz, spin, new Opts { Scale = smallScale });
                            }
                        }
                    }
                }
            }

            /// <summary>Extra junk down the middle of the lot cut-throughs.</summary>
            void AlleyClutter()
            {
                foreach (Alley a in _plan.Alleys)
                {
                    double cx = (a.Rect.X0 + a.Rect.X1) / 2, cz = (a.Rect.Z0 + a.Rect.Z1) / 2;
                    if (!_window.Contains(CityLayout.CellIndex(cx), CityLayout.CellIndex(cz))) continue;

                    bool axisZ = a.Axis == AlleyAxis.Z;   // narrow in X, runs along Z
                    double len = axisZ ? a.Rect.Z1 - a.Rect.Z0 : a.Rect.X1 - a.Rect.X0;
                    double wide = axisZ ? a.Rect.X1 - a.Rect.X0 : a.Rect.Z1 - a.Rect.Z0;
                    _lanes.Add(new Lane { AxisZ = axisZ, Cx = cx, Cz = cz, Len = len, Wide = wide });

                    int n = 2 + (int)(R() * 4);
                    for (int k = 0; k < n; k++)
                    {
                        double t = (R() - 0.5) * (len - 3);
                        double x = axisZ ? cx + (R() - 0.5) * wide * 0.3 : cx + t;
                        double z = axisZ ? cz + t : cz + (R() - 0.5) * wide * 0.3;
                        if (!OnSidewalk(x, z, 0.4) || Blocked(x, z)) continue;
                        double roll = R();
                        string name = roll < 0.36 ? "debris" : roll < 0.62 ? "bag" : roll < 0.82 ? "cardbox" : "cone";
                        double spin = R() * Tau;
                        double scale = 0.8 + R() * 0.4;
                        Put(name, x, SY, z, spin, new Opts { Scale = scale });
                    }
                }
            }

            /// <summary>The kerb line: lights, signals, bollards, meters, rails, shelters.</summary>
            void Kerb(StreetCell s)
            {
                double sw = _hw;
                double lane = sw - 0.95;
                for (int i = 0; i < 4; i++)
                {
                    var sd = Sides[i];
                    double ry = Math.Atan2(sd.nx, sd.nz);

                    bool At(double u, string name, double? turn = null)
                    {
                        double x = s.Cx + sd.nx * lane + sd.tx * u;
                        double z = s.Cz + sd.nz * lane + sd.tz * u;
                        if (Math.Abs(u) > sw - 0.8) return false;
                        if (Blocked(x, z)) return false;
                        return Put(name, x, SY, z, turn ?? ry);
                    }

                    if (R() < 0.85) At(-13 + R() * 3, "streetlight");
                    if (R() < 0.85) At(11 + R() * 3, "streetlight");
                    if (R() < 0.36) At((R() - 0.5) * 22, "shelter");
                    if (R() < 0.32) At((R() - 0.5) * 32, "signpost");
                    if (R() < 0.28) At((R() - 0.5) * 32, "hydrant");
                    if (R() < 0.34) At((R() - 0.5) * 28, "campole");
                    if (R() < 0.2) At((R() - 0.5) * 28, "utilbox", ry);

                    double style = R();
                    if (style < 0.34)
                    {
                        double step = 2.6 + R() * 0.8;
                        for (double u = -sw + 3; u < sw - 3; u += step) At(u, "bollard");
                    }
                    else if (style < 0.58)
                    {
                        double step = 6 + R() * 3;
                        for (double u = -sw + 5; u < sw - 5; u += step) At(u, "meter");
                    }
                    else if (style < 0.78)
                    {
                        double from = -sw + 4 + R() * 6;
                        double to = from + 8 + R() * 16;
                        for (double u = from; u < Math.Min(to, sw - 4); u += 2.45) At(u, "guardrail");
                    }

                    if (R() < 0.55) At((R() < 0.5 ? -1 : 1) * (sw - 2.2), "signal");
                }
            }

            /// <summary>The strip just off the kerb: deliveries, roadworks, food, drone pads.</summary>
            void Kerbside(StreetCell s)
            {
                double sw = _hw;
                for (int i = 0; i < 4; i++)
                {
                    if (R() > 0.55) continue;
                    var sd = Sides[i];
                    double ry = Math.Atan2(sd.nx, sd.nz);
                    double d = sw + 1.15 + R() * 0.5;
                    double u = (R() - 0.5) * 32;
                    double x = s.Cx + sd.nx * d + sd.tx * u;
                    double z = s.Cz + sd.nz * d + sd.tz * u;
                    double roll = R();
                    if (roll < 0.3)
                    {
                        Put("dumpster", x, 0, z, ry + Math.PI);
                        double spin = R() * Tau;
                        Put("bag", x + sd.tx * 1.7, 0, z + sd.tz * 1.7, spin);
                    }
                    else if (roll < 0.52)
                    {
                        for (int k = -1; k <= 1; k++) Put("barrier", x + sd.tx * k * 1.9, 0, z + sd.tz * k * 1.9, ry);
                        Put("cone", x + sd.tx * 3.0 + sd.nx * 0.7, 0, z + sd.tz * 3.0 + sd.nz * 0.7, 0);
                        Put("cone", x - sd.tx * 3.0 + sd.nx * 0.7, 0, z - sd.tz * 3.0 + sd.nz * 0.7, 0);
                        double spin = R() * Tau;
                        Put("debris", x + sd.nx * 0.9, 0, z + sd.nz * 0.9, spin);
                    }
                    else if (roll < 0.7)
                    {
                        Put("stall", x, 0, z, ry + Math.PI, new Opts { Steam = true, SteamY = 2.1, SteamR = 0.8 });
                    }
                    else if (roll < 0.88)
                    {
                        double spin = R() * Tau;
                        Put("pallet", x, 0, z, spin);
                        double spin2 = R() * Tau;
                        Put("crate", x + sd.tx * 1.15, 0, z + sd.tz * 1.15, spin2);
                        double spin3 = R() * Tau;
                        Put("crate", x + sd.tx * 1.15, 0.66, z + sd.tz * 1.15, spin3, new Opts { Free = true, Scale = 0.9 });
                    }
                    else
                    {
                        Put("dronepad", x + sd.nx * 0.7, 0.01, z + sd.nz * 0.7, ry);
                    }
                }
            }

            /// <summary>The plaza block: an open middle, a furnished edge, and a scattering of litter.</summary>
            void Plaza(StreetCell s)
            {
                double sw = _hw;
                for (int i = 0; i < 4; i++)
                {
                    var sd = Sides[i];
                    double ry = Math.Atan2(sd.nx, sd.nz);
                    double d = sw - 3.4;
                    for (int k = -3; k <= 3; k++)
                    {
                        double u = k * 6.5 + (R() - 0.5) * 2;
                        double roll = R();
                        string name = roll < 0.24 ? "bench"
                            : roll < 0.4 ? "bin"
                            : roll < 0.56 ? "vending"
                            : roll < 0.68 ? "kiosk"
                            : roll < 0.79 ? "stall"
                            : roll < 0.88 ? "crate" : null;
                        if (name == null) continue;
                        double turn = ry + Math.PI + (R() - 0.5) * 0.3;
                        Put(name, s.Cx + sd.nx * d + sd.tx * u, SY, s.Cz + sd.nz * d + sd.tz * u, turn,
                            new Opts { Steam = name == "stall", SteamY = 2.0, SteamR = 0.75 });
                    }
                }
                for (int k = 0; k < 30; k++)
                {
                    double x = s.Cx + (R() - 0.5) * 42;
                    double z = s.Cz + (R() - 0.5) * 42;
                    double roll = R();
                    string name = roll < 0.3 ? "debris" : roll < 0.55 ? "bag" : roll < 0.75 ? "cardbox" : roll < 0.9 ? "cone" : "crate";
                    double spin = R() * Tau;
                    double scale = 0.8 + R() * 0.4;
                    Put(name, x, SY, z, spin, new Opts { Scale = scale });
                }
                double padSpin = R() * Tau;
                Put("dronepad", s.Cx + 9, SY + 0.01, s.Cz - 11, padSpin);
            }

            // ------------------------------------------------------------ walls and roofs

            /// <summary>Wall fixtures never take a collider: they hang above head height.</summary>
            void MountWall(string name, double x, double y, double z, double ry) => Add(name, x, y, z, ry);

            /// <summary>A pass over every facade: air conditioners, cameras and dishes up the walls, spray tags and flyposters low down.</summary>
            void FacadeProps()
            {
                foreach (NeonWall w in _walls)
                {
                    double ry = Math.Atan2(w.Nx, w.Nz);
                    double height = w.Y1 - w.Y0;

                    int fixtures = 1 + (int)(R() * 3);
                    for (int k = 0; k < fixtures; k++)
                    {
                        double t = 1.2 + R() * Math.Max(0.1, w.Length - 2.4);
                        double roll = R();
                        string name = roll < 0.5 ? "acunit" : roll < 0.82 ? "camwall" : "satdish";
                        double y = w.Y0 + 3.0 + R() * Math.Max(1, Math.Min(height - 4, 22));
                        MountWall(name, w.Ox + w.Tx * t + w.Nx * 0.05, y, w.Oz + w.Tz * t + w.Nz * 0.05, ry);
                    }
                    if (height > 22 && R() < 0.5)
                    {
                        double t = 1.5 + R() * Math.Max(0.1, w.Length - 3);
                        double y = w.Y0 + 14 + R() * Math.Min(height - 16, 30);
                        MountWall("satdish", w.Ox + w.Tx * t + w.Nx * 0.05, y, w.Oz + w.Tz * t + w.Nz * 0.05, ry);
                    }

                    // a cash machine at head height on some street frontages, where a customer can stand (the prototype put these on its facade
                    // anchors, which this port does not have)
                    if (w.Street && w.Length > 5 && R() < 0.22)
                    {
                        double t = 1.2 + R() * (w.Length - 2.4);
                        double fx = w.Ox + w.Tx * t + w.Nx * 0.9, fz = w.Oz + w.Tz * t + w.Nz * 0.9;
                        if (OnSidewalk(fx, fz, 0.4) && !Blocked(fx, fz)) MountWall("atm", w.Ox + w.Tx * t + w.Nx * 0.05, 1.6, w.Oz + w.Tz * t + w.Nz * 0.05, ry);
                    }

                    // graffiti and flyposters on the bottom few metres
                    int tags = w.Alley ? 3 + (int)(R() * 5) : 1 + (int)(R() * 4);
                    for (int k = 0; k < tags; k++)
                    {
                        double wd = Math.Min(1.3 + R() * 2.4, w.Length - 0.6);
                        if (wd < 0.7) break;
                        double t = wd / 2 + 0.2 + R() * Math.Max(0.1, w.Length - wd - 0.4);
                        double h = wd * (0.55 + R() * 0.6);
                        double y = w.Y0 + 0.55 + R() * 2.5 + h * 0.5;
                        double rz = (R() - 0.5) * 0.14;
                        int tile = (int)(R() * 16);
                        _layout.Decals.Add(new DecalInstance(DecalKind.Graffiti,
                            w.Ox + w.Tx * t + w.Nx * 0.055, y, w.Oz + w.Tz * t + w.Nz * 0.055, wd, h, ry, rz, tile));
                    }
                }
            }

            void RoofProps()
            {
                foreach (Building b in _plan.Buildings)
                {
                    if (!_window.Contains(b.Lot.Gx, b.Lot.Gz)) continue;
                    foreach (Volume v in b.Volumes)
                    {
                        // only roofs with nothing standing on them
                        bool covered = false;
                        foreach (Volume o in b.Volumes)
                        {
                            if (!ReferenceEquals(o, v) && Math.Abs(o.Y0 - v.Y1) < 0.02) covered = true;
                        }
                        if (covered) continue;

                        Rect2 bnd = Bounds(v.Poly);
                        double area = (bnd.X1 - bnd.X0) * (bnd.Z1 - bnd.Z0);
                        if (area < 40) continue;
                        double y = v.Y1 + SY;
                        int n = Math.Min(6, 1 + (int)(area / 130));
                        for (int k = 0; k < n; k++)
                        {
                            for (int tries = 0; tries < 6; tries++)
                            {
                                double x = bnd.X0 + 2.2 + R() * Math.Max(0.1, bnd.X1 - bnd.X0 - 4.4);
                                double z = bnd.Z0 + 2.2 + R() * Math.Max(0.1, bnd.Z1 - bnd.Z0 - 4.4);
                                if (!Inside(v.Poly, x, z)) continue;
                                PlaceRoof(x, y, z);
                                break;
                            }
                        }
                    }
                }
            }

            void PlaceRoof(double x, double y, double z)
            {
                double roll = R();
                string name = roll < 0.38 ? "roofunit"
                    : roll < 0.6 ? "ventstack"
                    : roll < 0.78 ? "satdish"
                    : roll < 0.92 ? "utilbox" : "dronepad";
                double spin = R() * Tau;
                bool steam = name == "ventstack" && R() < 0.55;
                Put(name, x, y, z, spin, new Opts { NoCollider = true, Free = true, Steam = steam, SteamY = 2.0, SteamR = 0.6 });
            }

            static bool Inside(Point2[] poly, double x, double z)
            {
                bool hit = false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    double xi = poly[i].X, zi = poly[i].Z, xj = poly[j].X, zj = poly[j].Z;
                    if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) hit = !hit;
                }
                return hit;
            }

            static Rect2 Bounds(Point2[] poly)
            {
                double x0 = double.PositiveInfinity, z0 = double.PositiveInfinity, x1 = double.NegativeInfinity, z1 = double.NegativeInfinity;
                foreach (Point2 p in poly)
                {
                    x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X);
                    z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z);
                }
                return new Rect2(x0, z0, x1, z1);
            }

            // ------------------------------------------------------------ decals

            void GroundDecals()
            {
                double sw = _hw;
                void Push(double x, double y, double z, double size, int tile, double aspect = 1)
                {
                    double spin = R() * Tau;
                    _layout.Decals.Add(new DecalInstance(DecalKind.Ground, x, y, z, size * aspect, size, 0, spin, tile));
                }
                int PickTile(double r) =>
                    r < 0.22 ? (int)(R() * 4) : r < 0.5 ? 4 + (int)(R() * 4) : r < 0.82 ? 8 + (int)(R() * 4) : 12 + (int)(R() * 4);

                foreach (StreetCell s in _plan.Streets)
                {
                    if (!_window.Contains(s.Gx, s.Gz)) continue;

                    // pavement film, rejection-sampled so nothing lands inside a building
                    int want = s.IsPlaza ? 80 : 60;
                    for (int k = 0, tries = 0; k < want && tries < want * 4; tries++)
                    {
                        double x = s.Cx + (R() - 0.5) * (sw * 2 - 1.5);
                        double z = s.Cz + (R() - 0.5) * (sw * 2 - 1.5);
                        if (Blocked(x, z)) continue;
                        k++;
                        double y = SY + 0.012 + R() * 0.005;
                        double size = 0.7 + R() * 2.2;
                        int tile = PickTile(R());
                        double aspect = 0.8 + R() * 0.5;
                        Push(x, y, z, size, tile, aspect);
                    }
                    // road film: oil and grit where vehicles sit
                    for (int k = 0; k < 18; k++)
                    {
                        var sd = Sides[(int)(R() * 4)];
                        double d = sw + 0.5 + R() * 5.0;
                        double u = (R() - 0.5) * (sw * 2);
                        double y = 0.016 + R() * 0.005;
                        double size = 1.2 + R() * 3.4;
                        int tile = R() < 0.55 ? (int)(R() * 4) : 4 + (int)(R() * 4);
                        double aspect = 0.8 + R() * 0.6;
                        Push(s.Cx + sd.nx * d + sd.tx * u, y, s.Cz + sd.nz * d + sd.tz * u, size, tile, aspect);
                    }
                }

                // alley floors get the worst of it
                foreach (Lane lane in _lanes)
                {
                    int n = 10 + (int)(R() * 10);
                    for (int k = 0; k < n; k++)
                    {
                        double t = (R() - 0.5) * (lane.Len - 1);
                        double o = (R() - 0.5) * lane.Wide * 0.8;
                        double x = lane.AxisZ ? lane.Cx + o : lane.Cx + t;
                        double z = lane.AxisZ ? lane.Cz + t : lane.Cz + o;
                        double y = SY + 0.012 + R() * 0.006;
                        double size = 0.6 + R() * 1.7;
                        int tile = (int)(R() * 16);
                        double aspect = 0.8 + R() * 0.5;
                        Push(x, y, z, size, tile, aspect);
                    }
                }
            }

            // ------------------------------------------------------------ cables and conduit

            PropMeshBuilder PartsAt(double x, double z)
            {
                int gx = Math.Max(0, Math.Min(CityLayout.Grid - 1, CityLayout.CellIndex(x)));
                int gz = Math.Max(0, Math.Min(CityLayout.Grid - 1, CityLayout.CellIndex(z)));
                if (!_layout.CellParts.TryGetValue((gx, gz), out PropMeshBuilder b)) _layout.CellParts.Add((gx, gz), b = new PropMeshBuilder());
                return b;
            }

            void Cable((double x, double y, double z) a, (double x, double y, double z) b, double sag, double radius, uint hex)
            {
                // the prototype samples a Catmull-Rom curve through seven points on the sag function at four tubular segments;
                // sampling the function itself at five points is the same curve
                var path = new Float3[5];
                for (int i = 0; i < path.Length; i++)
                {
                    double t = i / 4.0;
                    path[i] = new Float3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t - Math.Sin(t * Math.PI) * sag, a.z + (b.z - a.z) * t);
                }
                PartsAt((a.x + b.x) / 2, (a.z + b.z) / 2).Tube(path, radius, 3, hex, new PartStyle(rough: 0.5, metal: 0.25, uv: 1));
            }

            /// <summary>March out from a facade until the far side of the street is hit, so a cable lands on the opposite building and not in the air.</summary>
            double SpanTo(NeonWall w, double max)
            {
                for (double d = 2.0; d < max; d += 0.8)
                {
                    if (Blocked(w.Mx + w.Nx * d, w.Mz + w.Nz * d)) return d;
                }
                return 0;
            }

            void Cables()
            {
                // strung across the street, facade to facade
                foreach (NeonWall w in _walls)
                {
                    if (!w.Street || w.Length < 7) continue;
                    if (R() > 0.34) continue;
                    double span = SpanTo(w, 46);
                    if (span < 12) continue;
                    double height = w.Y1 - w.Y0;
                    int n = 1 + (int)(R() * 3);
                    for (int k = 0; k < n; k++)
                    {
                        double y = w.Y0 + 6.5 + R() * Math.Max(0.5, Math.Min(height - 8, 11));
                        double t = w.Length * (0.2 + R() * 0.6);
                        var a = (x: w.Ox + w.Tx * t + w.Nx * 0.2, y, z: w.Oz + w.Tz * t + w.Nz * 0.2);
                        double rise = (R() - 0.5) * 1.8;
                        var b = (x: w.Ox + w.Tx * t + w.Nx * (span - 0.2), y: y + rise, z: w.Oz + w.Tz * t + w.Nz * (span - 0.2));
                        double sag = 1.2 + R() * 2.4;
                        uint colour = R() < 0.25 ? 0x2e2a22u : 0x15171bu;
                        Cable(a, b, sag, 0.032, colour);
                        if (R() < 0.45)
                        {
                            double o = 0.28 + R() * 0.4;
                            double sag2 = 1.7 + R() * 2.4;
                            Cable((a.x + w.Tx * o, a.y + 0.3, a.z + w.Tz * o), (b.x + w.Tx * o, b.y + 0.3, b.z + w.Tz * o), sag2, 0.022, 0x1b1d22);
                        }
                    }
                }

                // across alleys: the ones you actually walk under
                foreach (Lane lane in _lanes)
                {
                    int n = 3 + (int)(R() * 5);
                    double reach = lane.Wide / 2 + 0.4;
                    for (int k = 0; k < n; k++)
                    {
                        double y = 3.6 + R() * 6.5;
                        double t = (R() - 0.5) * (lane.Len - 2);
                        (double x, double y, double z) a = lane.AxisZ ? (lane.Cx - reach, y, lane.Cz + t) : (lane.Cx + t, y, lane.Cz - reach);
                        double rise = (R() - 0.5) * 0.7;
                        (double x, double y, double z) b = lane.AxisZ ? (lane.Cx + reach, y + rise, lane.Cz + t) : (lane.Cx + t, y + rise, lane.Cz + reach);
                        double sag = 0.2 + R() * 0.55;
                        Cable(a, b, sag, 0.026, 0x16181d);
                    }
                }

                // conduit and junction boxes running down facades
                foreach (NeonWall w in _walls)
                {
                    if (R() > 0.4 || w.Length < 3) continue;
                    double t = 0.8 + R() * (w.Length - 1.6);
                    double x = w.Ox + w.Tx * t + w.Nx * 0.11;
                    double z = w.Oz + w.Tz * t + w.Nz * 0.11;
                    double ry = Math.Atan2(w.Nx, w.Nz);
                    double top = 4 + R() * Math.Min(Math.Max(2, w.Y1 - w.Y0 - 2), 24);
                    PropMeshBuilder parts = PartsAt(x, z);
                    parts.Cylinder(0.055, 0.055, top, 5, 0x3a3f47, new PartStyle(x: x, y: w.Y0 + 0.6 + top / 2, z: z, rough: 0.55, metal: 0.5, uv: 1));
                    parts.Box(0.3, 0.42, 0.2, 0x454b55, new PartStyle(x: x + w.Nx * 0.06, y: w.Y0 + 2.2, z: z + w.Nz * 0.06, ry: ry, uv: 1));
                    if (R() < 0.6)
                    {
                        double h = 0.9 + R() * 2.4;
                        parts.Cylinder(0.04, 0.04, h, 5, 0x2f343c, new PartStyle(x: x + w.Tx * 0.16, y: w.Y0 + 3.3, z: z + w.Tz * 0.16, uv: 1, rough: 0.55, metal: 0.5));
                    }
                    // a horizontal run hugging the wall
                    if (R() < 0.5)
                    {
                        double len = Math.Min(2 + R() * 6, w.Length - t - 0.4);
                        if (len > 1.2)
                        {
                            double y = w.Y0 + 3.0 + R() * 1.6;
                            parts.Cylinder(0.045, 0.045, len, 5, 0x3a3f47, new PartStyle(
                                x: x + w.Tx * len * 0.5, y: y, z: z + w.Tz * len * 0.5,
                                rz: Math.PI / 2, ry: Math.Atan2(w.Tx, w.Tz) + Math.PI / 2, uv: 1, rough: 0.55, metal: 0.5));
                        }
                    }
                }
            }

            // ------------------------------------------------------------ steam

            void Steam()
            {
                const int Max = 96;
                var spots = _layout.SteamSpots;
                if (spots.Count == 0) return;
                for (int i = 0; i < spots.Count && _layout.Puffs.Count < Max; i++)
                {
                    SteamSpot s = spots[(int)((long)i * 7919 % spots.Count)];
                    int n = 2 + (int)(R() * 2);
                    for (int k = 0; k < n && _layout.Puffs.Count < Max; k++)
                    {
                        double x = s.X + (R() - 0.5) * s.R * 2;
                        double z = s.Z + (R() - 0.5) * s.R * 2;
                        double phase = R();
                        double speed = 0.16 + R() * 0.16;
                        double size = 0.9 + R() * 1.2;
                        double rise = 1.6 + R() * 1.6;
                        double drift = (R() - 0.5) * 0.6;
                        _layout.Puffs.Add(new SteamPuff(x, s.Y, z, phase, speed, size, rise, drift));
                    }
                }
            }
        }
    }
}
