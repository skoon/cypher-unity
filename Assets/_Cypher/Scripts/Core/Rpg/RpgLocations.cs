using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>The city as the named places are put on it: block size, grid, the blocks (with which is the plaza) and the buildings (centre, footprint, height).</summary>
    public sealed class RpgCityMeta
    {
        public double BlockSize, Cell;
        public int Grid;
        public List<(int Gx, int Gz, double Cx, double Cz, bool IsPlaza)> Streets = new List<(int, int, double, double, bool)>();
        public List<(double X, double Z, double W, double D, double H)> Buildings = new List<(double, double, double, double, double)>();

        /// <summary>The metadata of a generated city, as the prototype's City publishes it.</summary>
        public static RpgCityMeta From(CityPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var meta = new RpgCityMeta { BlockSize = CityLayout.Block, Cell = CityLayout.Cell, Grid = CityLayout.Grid };
            foreach (StreetCell s in plan.Streets) meta.Streets.Add((s.Gx, s.Gz, s.Cx, s.Cz, s.IsPlaza));
            foreach (Building b in plan.Buildings) meta.Buildings.Add((b.X, b.Z, b.Width, b.Depth, b.Height));
            return meta;
        }
    }

    /// <summary>A named place, resolved onto the city: where it is and how close counts as arrived.</summary>
    public sealed class RpgLocation
    {
        public string Key, Name, Kind, Blurb;
        public double X, Y, Z, Radius;
        /// <summary>The block it was put on, as the prototype writes it (a street cell, or the tallest building's centre for the spire).</summary>
        public JsonObject Block;

        public JsonObject ToJson() => new JsonObject
        {
            ["key"] = Key, ["name"] = Name, ["kind"] = Kind, ["blurb"] = Blurb, ["x"] = X, ["y"] = Y, ["z"] = Z, ["radius"] = Radius, ["block"] = Block,
        };
    }

    /// <summary>
    /// The named places (the noodle bar, the clinic, the data core…) and where they go in a city: a port of <c>rpg/Locations.js</c>. Nothing is a fixed coordinate:
    /// each place takes a block in its ring round the plaza, chosen by the seed, so the same seed puts the noodle bar on the same corner and another city moves every
    /// marker with it. Quests and dialogue refer to places by key only.
    /// </summary>
    public static class RpgLocations
    {
        /// <summary>The prototype's sidewalk height; the game puts markers on its own walking surface.</summary>
        public const double SidewalkY = 0.25;

        static List<JsonObject> _defs;

        /// <summary>The place definitions, in the order written (key, name, kind, radius, placement, blurb).</summary>
        public static IReadOnlyList<JsonObject> Defs => _defs ?? (_defs = ((List<object>)Json.Parse(RpgContent.LocationsJson)).Cast<JsonObject>().ToList());

        public static JsonObject Def(string key)
        {
            JsonObject d = Defs.FirstOrDefault(x => (string)x["key"] == key);
            if (d == null) throw new ArgumentOutOfRangeException(nameof(key), $"Unknown location key \"{key}\"");
            return d;
        }

        public static bool IsKey(string key) => key != null && Defs.Any(x => (string)x["key"] == key);

        /// <summary>Put every place on the city. Returns them in the definitions' order, by key.</summary>
        public static List<RpgLocation> Build(RpgCityMeta city, uint seed = 7717)
        {
            if (city == null) throw new ArgumentException("buildLocations: city metadata required");
            if (city.Streets == null || city.Streets.Count == 0) throw new ArgumentException("buildLocations: city.streets must be a non-empty array");
            if (city.Buildings == null) throw new ArgumentException("buildLocations: city.buildings must be an array");
            var rng = new Rng(seed);
            double centre = (city.Grid - 1) / 2.0;
            double half = city.BlockSize / 2;
            double edgeOffset = half + 2;   // on the raised sidewalk slab, just inside its outer edge

            // candidate blocks by ring (distance from the centre, the blocks touching the plaza being ring 0), in a fixed order so placement depends only on the seed
            var rings = new Dictionary<int, List<(int Gx, int Gz, double Cx, double Cz, bool IsPlaza)>>();
            foreach (var s in city.Streets)
            {
                if (s.IsPlaza) continue;
                double cheb = Math.Max(Math.Abs(s.Gx - centre), Math.Abs(s.Gz - centre));
                int r = (int)Math.Max(0, RpgJs.Round(cheb - 1));
                if (!rings.TryGetValue(r, out var list)) rings[r] = list = new List<(int, int, double, double, bool)>();
                list.Add(s);
            }
            foreach (int key in rings.Keys.ToList())
            {
                rings[key] = rings[key].OrderBy(s => s.Gz).ThenBy(s => s.Gx).ToList();
            }

            var used = new HashSet<(int, int)>();
            (int Gx, int Gz, double Cx, double Cz, bool IsPlaza) TakeBlock(int wantRing)
            {
                foreach (int r in rings.Keys.OrderBy(k => Math.Abs(k - wantRing)).ThenBy(k => k))
                {
                    var pool = rings[r].Where(s => !used.Contains((s.Gx, s.Gz))).ToList();
                    if (pool.Count == 0) continue;
                    var block = pool[(int)Math.Floor(rng.Next() * pool.Count) % pool.Count];
                    used.Add((block.Gx, block.Gz));
                    return block;
                }
                throw new InvalidOperationException("buildLocations: ran out of city blocks to place landmarks");
            }

            JsonObject StreetJson((int Gx, int Gz, double Cx, double Cz, bool IsPlaza) s) =>
                new JsonObject { ["gx"] = (double)s.Gx, ["gz"] = (double)s.Gz, ["cx"] = s.Cx, ["cz"] = s.Cz, ["isPlaza"] = s.IsPlaza };

            var out_ = new List<RpgLocation>();
            foreach (JsonObject def in Defs)
            {
                var placement = (JsonObject)def["placement"];
                string type = (string)placement["type"];
                double x, z;
                JsonObject block;
                if (type == "plaza")
                {
                    var plaza = city.Streets.Any(s => s.IsPlaza) ? city.Streets.First(s => s.IsPlaza) : city.Streets[0];
                    x = plaza.Cx;
                    z = plaza.Cz;
                    block = StreetJson(plaza);
                }
                else if (type == "tallest")
                {
                    if (city.Buildings.Count == 0) throw new InvalidOperationException("buildLocations: no buildings to place a landmark against");
                    var tall = city.Buildings[0];
                    foreach (var b in city.Buildings) if (b.H > tall.H) tall = b;
                    x = tall.X + tall.W / 2 + 3.5;
                    z = tall.Z;
                    block = new JsonObject { ["cx"] = tall.X, ["cz"] = tall.Z };
                }
                else
                {
                    int ring = placement.TryGetValue("ring", out object rv) && rv != null ? RpgJs.Int(rv, "ring") : 1;
                    var b = TakeBlock(ring);
                    block = StreetJson(b);
                    // on one of the block's four sidewalk edges, by the seed
                    int side = (int)Math.Floor(rng.Next() * 4) % 4;
                    double jitter = (rng.Next() - 0.5) * city.BlockSize * 0.5;
                    if (side == 0) { x = b.Cx + jitter; z = b.Cz - edgeOffset; }
                    else if (side == 1) { x = b.Cx + edgeOffset; z = b.Cz + jitter; }
                    else if (side == 2) { x = b.Cx + jitter; z = b.Cz + edgeOffset; }
                    else { x = b.Cx - edgeOffset; z = b.Cz + jitter; }
                }
                out_.Add(new RpgLocation
                {
                    Key = (string)def["key"], Name = (string)def["name"], Kind = (string)def["kind"], Blurb = (string)def["blurb"],
                    X = RpgJs.Round(x * 100) / 100, Y = SidewalkY, Z = RpgJs.Round(z * 100) / 100,
                    Radius = def.TryGetValue("radius", out object rad) && rad != null ? RpgJs.Num(rad, "radius") : 7, Block = block,
                });
            }
            return out_;
        }

        public static double DistanceSq(RpgLocation loc, double x, double z)
        {
            double dx = loc.X - x, dz = loc.Z - z;
            return dx * dx + dz * dz;
        }

        /// <summary>The nearest place to a point, or null.</summary>
        public static (RpgLocation Location, double Distance)? Nearest(IEnumerable<RpgLocation> locations, double x, double z)
        {
            RpgLocation best = null;
            double bestD = double.PositiveInfinity;
            foreach (RpgLocation loc in locations)
            {
                double d = DistanceSq(loc, x, z);
                if (d < bestD)
                {
                    bestD = d;
                    best = loc;
                }
            }
            return best != null ? (best, Math.Sqrt(bestD)) : ((RpgLocation, double)?)null;
        }
    }
}
