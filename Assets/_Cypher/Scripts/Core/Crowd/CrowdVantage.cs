using System;

namespace Cypher.Core
{
    /// <summary>Where to put a camera to look at one person: the camera's position, and the point it looks at.</summary>
    public readonly struct PersonShot
    {
        public readonly string Name;
        public readonly double CamX, CamY, CamZ;
        public readonly double TargetX, TargetY, TargetZ;

        public PersonShot(string name, double camX, double camY, double camZ, double targetX, double targetY, double targetZ)
        {
            Name = name;
            CamX = camX; CamY = camY; CamZ = camZ;
            TargetX = targetX; TargetY = targetY; TargetZ = targetZ;
        }
    }

    /// <summary>Picks people worth a screenshot (the nearest, and the nearest with an umbrella, a long coat, a hood, a pack or glowing trim) and frames them from the front-left.</summary>
    public static class CrowdVantage
    {
        /// <summary>The active person nearest (<paramref name="x"/>, <paramref name="z"/>) who satisfies <paramref name="wanted"/> and is within <paramref name="maxDistance"/>, or null.</summary>
        public static Pedestrian Nearest(CrowdSim sim, double x, double z, double maxDistance, Func<Pedestrian, bool> wanted = null)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            Pedestrian best = null;
            double bestD2 = maxDistance * maxDistance;
            foreach (Pedestrian p in sim.Active)
            {
                if (wanted != null && !wanted(p)) continue;
                double dx = p.X - x, dz = p.Z - z, d2 = dx * dx + dz * dz;
                if (d2 <= bestD2) { bestD2 = d2; best = p; }
            }
            return best;
        }

        /// <summary>A camera <paramref name="distance"/> metres from the person, ahead of them and to their left, at head height, looking at their chest.</summary>
        public static PersonShot Frame(string name, Pedestrian p, double distance)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (!(distance > 0)) throw new ArgumentOutOfRangeException(nameof(distance), distance, "the camera needs to stand some way off");
            double fx = Math.Sin(p.Heading), fz = Math.Cos(p.Heading);       // the way they face
            double lx = -fz, lz = fx;                                         // their left, seen from above: a quarter turn from the front
            // forty-five degrees between the front and the left
            double ox = (fx + lx) * Math.Sqrt(0.5) * distance, oz = (fz + lz) * Math.Sqrt(0.5) * distance;
            double scale = p.Appearance.Scale;
            return new PersonShot(name, p.X + ox, p.Y + 1.5 * scale, p.Z + oz, p.X, p.Y + 1.0 * scale, p.Z);
        }

        /// <summary>The shots for a crowd: whichever of the kinds of person are within <paramref name="range"/> of the player. A kind nobody is close enough to is left out.</summary>
        public static System.Collections.Generic.IReadOnlyList<PersonShot> Pick(CrowdSim sim, double playerX, double playerZ, double range = 45)
        {
            var shots = new System.Collections.Generic.List<PersonShot>();
            void Add(string name, double distance, Func<Pedestrian, bool> wanted)
            {
                Pedestrian p = Nearest(sim, playerX, playerZ, range, wanted);
                if (p != null) shots.Add(Frame(name, p, distance));
            }
            Add("person_nearest", 3.2, null);
            Add("person_umbrella", 3.6, p => p.Appearance.Umbrella);
            Add("person_long_coat", 3.2, p => p.Appearance.LongCoat && !p.Appearance.Umbrella);
            Add("person_hood", 3.0, p => p.Appearance.Hood);
            Add("person_pack_and_trim", 3.0, p => p.Appearance.Pack && p.Appearance.Trim);
            return shots;
        }
    }
}
