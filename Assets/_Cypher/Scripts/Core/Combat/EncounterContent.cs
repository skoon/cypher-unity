using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>
    /// The set encounters as the content writes them (<see cref="RpgGameContent.EncountersJson"/>): an id, the tag its kills count as, the RPG place it holds, how far
    /// round it its people stand, who they are, when it is wanted, and how far off the player must be for it to arrive. The people are put on the pavement round the
    /// place by a fixed rule (nearest first, <see cref="Spacing"/> apart, clear of buildings and props, facing out), so the same city gives the same spots.
    /// </summary>
    public static class EncounterContent
    {
        /// <summary>How far apart two of an encounter's people stand at least, how far clear of a wall or a prop, and the nearest to the place's mark.</summary>
        public const double Spacing = 3, Clearance = 0.6, Nearest = 3;

        /// <summary>The rings the spots are looked for on: this far apart, and this far apart round each.</summary>
        public const double RingStep = 1.5, ArcStep = 1.5;

        static readonly HashSet<string> Keys = new HashSet<string> { "id", "tag", "place", "reach", "members", "when", "arriveBeyond" };

        public static List<EncounterDef> Build(string json, IReadOnlyDictionary<string, RpgLocation> places, CityNav nav, IReadOnlyList<PropBox> props,
            IEnumerable<(double X, double Z, double Width, double Depth)> buildings)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (places == null) throw new ArgumentNullException(nameof(places));
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            if (props == null) throw new ArgumentNullException(nameof(props));
            if (buildings == null) throw new ArgumentNullException(nameof(buildings));
            List<(double X, double Z, double Width, double Depth)> walls = buildings.ToList();
            var out_ = new List<EncounterDef>();
            foreach (object item in (List<object>)Json.Parse(json))
            {
                var e = (JsonObject)item;
                foreach (string key in e.Keys) if (!Keys.Contains(key)) throw new ArgumentOutOfRangeException(nameof(json), $"encounter: unknown key \"{key}\"");
                string id = (string)e["id"], place = (string)e["place"];
                if (!places.TryGetValue(place, out RpgLocation at)) throw new ArgumentOutOfRangeException(nameof(json), $"encounter {id}: unknown place \"{place}\"");
                List<string> kinds = ((List<object>)e["members"]).Cast<string>().ToList();
                foreach (string kind in kinds) EnemyTable.Get(kind);
                double reach = RpgJs.Num(e["reach"], "reach");
                List<(double Dx, double Dz)> spots = Spots(at.X, at.Z, kinds.Count, reach, nav, props, walls);
                if (spots.Count < kinds.Count) throw new InvalidOperationException($"encounter {id}: room for {spots.Count} of its {kinds.Count} people within {reach} m of {place}");
                var members = new List<EncounterMember>();
                for (int i = 0; i < kinds.Count; i++)
                {
                    (double dx, double dz) = spots[i];
                    members.Add(new EncounterMember { Archetype = kinds[i], Dx = dx, Dz = dz, Yaw = Math.Atan2(dx, dz) });   // facing out from the place
                }
                JsonObject when = e.TryGetValue("when", out object w) ? (JsonObject)w : null;
                if (when != null) RpgConditions.Assert(when, $"encounter {id}.when");
                out_.Add(new EncounterDef
                {
                    Id = id, Tag = (string)e["tag"], Anchor = new V3(at.X, nav.WalkY, at.Z), Members = members, When = when,
                    ArriveBeyond = e.TryGetValue("arriveBeyond", out object ab) ? RpgJs.Num(ab, "arriveBeyond") : 0,
                });
            }
            return out_;
        }

        /// <summary>Up to <paramref name="count"/> spots (offsets from the mark) on the pavement within <paramref name="reach"/>, nearest first.</summary>
        public static List<(double Dx, double Dz)> Spots(double x, double z, int count, double reach, CityNav nav, IReadOnlyList<PropBox> props,
            IReadOnlyList<(double X, double Z, double Width, double Depth)> walls)
        {
            var spots = new List<(double, double)>();
            for (double r = Nearest; r <= reach + 1e-9 && spots.Count < count; r += RingStep)
            {
                int n = Math.Max(8, (int)Math.Round(2 * Math.PI * r / ArcStep));
                for (int k = 0; k < n && spots.Count < count; k++)
                {
                    double a = 2 * Math.PI * k / n, dx = Math.Sin(a) * r, dz = Math.Cos(a) * r;
                    if (!OnPavement(x + dx, z + dz, nav, props, walls)) continue;
                    bool apart = true;
                    foreach ((double sx, double sz) in spots) if ((sx - dx) * (sx - dx) + (sz - dz) * (sz - dz) < Spacing * Spacing) apart = false;
                    if (apart) spots.Add((dx, dz));
                }
            }
            return spots;
        }

        /// <summary>In the pavement's walking band, and clear of every building and prop by <see cref="Clearance"/>.</summary>
        public static bool OnPavement(double x, double z, CityNav nav, IReadOnlyList<PropBox> props, IReadOnlyList<(double X, double Z, double Width, double Depth)> walls)
        {
            if (Math.Abs(nav.KerbDistance(x, z) - nav.RingRadius) > nav.WalkBand) return false;
            foreach ((double bx, double bz, double w, double d) in walls)
            {
                if (Math.Abs(x - bx) < w / 2 + Clearance && Math.Abs(z - bz) < d / 2 + Clearance) return false;
            }
            foreach (PropBox b in props)
            {
                double c = Math.Cos(b.Yaw), s = Math.Sin(b.Yaw), px = x - b.Cx, pz = z - b.Cz;
                double lx = px * c - pz * s, lz = px * s + pz * c;   // the box's own axes, as CoverMap turns them
                if (Math.Abs(lx) < b.Hx + Clearance && Math.Abs(lz) < b.Hz + Clearance) return false;
            }
            return true;
        }
    }
}
