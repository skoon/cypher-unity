using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>Where to put a camera to look at something in the traffic: the camera's position, and the point it looks at.</summary>
    public readonly struct TrafficShot
    {
        public readonly string Name;
        public readonly double CamX, CamY, CamZ;
        public readonly double TargetX, TargetY, TargetZ;

        public TrafficShot(string name, double camX, double camY, double camZ, double targetX, double targetY, double targetZ)
        {
            Name = name;
            CamX = camX; CamY = camY; CamZ = camZ;
            TargetX = targetX; TargetY = targetY; TargetZ = targetZ;
        }
    }

    /// <summary>
    /// Picks things in the traffic worth a screenshot (the nearest vehicle of each kind, an aircraft of each, a junction with its signals, and a stretch of road) and frames them.
    /// </summary>
    public static class TrafficVantage
    {
        /// <summary>The active vehicle of type <paramref name="type"/> (any type if negative) nearest (<paramref name="x"/>, <paramref name="z"/>) within <paramref name="maxDistance"/>, or null.</summary>
        public static Vehicle NearestVehicle(TrafficSim sim, double x, double z, double maxDistance, int type = -1)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            Vehicle best = null;
            double bestD2 = maxDistance * maxDistance;
            foreach (Vehicle v in sim.Vehicles)
            {
                if (!v.Active || (type >= 0 && v.TypeIndex != type)) continue;
                double dx = v.X - x, dz = v.Z - z, d2 = dx * dx + dz * dz;
                if (d2 <= bestD2) { bestD2 = d2; best = v; }
            }
            return best;
        }

        /// <summary>The aircraft of type <paramref name="type"/> nearest (<paramref name="x"/>, <paramref name="z"/>) within <paramref name="maxDistance"/>, or null.</summary>
        public static Aircraft NearestAircraft(TrafficSim sim, double x, double z, double maxDistance, int type = -1)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            Aircraft best = null;
            double bestD2 = maxDistance * maxDistance;
            foreach (Aircraft a in sim.Airborne)
            {
                if (type >= 0 && a.TypeIndex != type) continue;
                double dx = a.X - x, dz = a.Z - z, d2 = dx * dx + dz * dz;
                if (d2 <= bestD2) { bestD2 = d2; best = a; }
            }
            return best;
        }

        /// <summary>A camera <paramref name="distance"/> metres from the vehicle, ahead of it and to its left, a little above its roof line, looking at its middle.</summary>
        public static TrafficShot FrameVehicle(string name, Vehicle v, double distance)
        {
            if (v == null) throw new ArgumentNullException(nameof(v));
            if (!(distance > 0)) throw new ArgumentOutOfRangeException(nameof(distance), distance, "the camera needs to stand some way off");
            double fx = Math.Sin(v.Heading), fz = Math.Cos(v.Heading);
            double lx = -fz, lz = fx;   // its left, seen from above
            double ox = (fx + lx) * Math.Sqrt(0.5) * distance, oz = (fz + lz) * Math.Sqrt(0.5) * distance;
            return new TrafficShot(name, v.X + ox, v.Y + v.Type.Height * 0.7 + 0.6, v.Z + oz, v.X, v.Y + v.Type.Height * 0.45, v.Z);
        }

        /// <summary>A camera <paramref name="distance"/> metres from the aircraft, abeam of it, level with it, looking at its middle.</summary>
        public static TrafficShot FrameAircraft(string name, Aircraft a, double distance)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (!(distance > 0)) throw new ArgumentOutOfRangeException(nameof(distance), distance, "the camera needs to stand some way off");
            double fx = Math.Sin(a.Heading), fz = Math.Cos(a.Heading);
            double lx = -fz, lz = fx;
            // ahead of the side view a little, so it is seen three-quarters on
            double ox = (lx * 0.9 + fx * 0.45) * distance, oz = (lz * 0.9 + fz * 0.45) * distance;
            return new TrafficShot(name, a.X + ox, a.Y + 1.5, a.Z + oz, a.X, a.Y, a.Z);
        }

        /// <summary>The junction nearest (<paramref name="x"/>, <paramref name="z"/>), as indices into the road lines.</summary>
        public static (int ix, int iz) NearestJunction(CityNav nav, double x, double z)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            int bi = 0, bj = 0;
            for (int i = 0; i < nav.XLines.Count; i++)
            {
                if (Math.Abs(nav.XLines[i] - x) < Math.Abs(nav.XLines[bi] - x)) bi = i;
            }
            for (int j = 0; j < nav.ZLines.Count; j++)
            {
                if (Math.Abs(nav.ZLines[j] - z) < Math.Abs(nav.ZLines[bj] - z)) bj = j;
            }
            return (bi, bj);
        }

        /// <summary>A camera <paramref name="back"/> metres short of a junction along -Z, <paramref name="height"/> up, looking at the junction's middle: the signal heads of both axes, and whatever waits or crosses.</summary>
        public static TrafficShot Junction(string name, CityNav nav, int ix, int iz, double back = 24, double height = 3.2)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            double cx = nav.XLines[ix], cz = nav.ZLines[iz];
            return new TrafficShot(name, cx + 3.5, nav.WalkY + height, cz - back, cx, nav.WalkY + 3.2, cz);
        }

        /// <summary>The shots for the traffic: the nearest vehicle of each kind within <paramref name="range"/> of the player, an aircraft of each kind, and the nearest junction.</summary>
        public static IReadOnlyList<TrafficShot> Pick(TrafficSim sim, double playerX, double playerZ, double range = 400)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            var shots = new List<TrafficShot>();
            for (int type = 0; type < VehicleCatalog.Ground.Count; type++)
            {
                Vehicle v = NearestVehicle(sim, playerX, playerZ, range, type);
                if (v != null) shots.Add(FrameVehicle("vehicle_" + VehicleCatalog.Ground[type].Key, v, 3.5 + v.Type.Length * 0.9));
            }
            for (int type = 0; type < VehicleCatalog.Aircraft.Count; type++)
            {
                Aircraft a = NearestAircraft(sim, playerX, playerZ, range, type);
                if (a != null) shots.Add(FrameAircraft("aircraft_" + VehicleCatalog.Aircraft[type].Key, a, 12 + a.Type.Length * 2.2));
            }
            (int ix, int iz) = NearestJunction(sim.Nav, playerX, playerZ);
            shots.Add(Junction("junction_signals", sim.Nav, ix, iz));
            return shots;
        }
    }
}
