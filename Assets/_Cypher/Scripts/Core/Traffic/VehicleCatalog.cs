using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>A point of a vehicle's side profile: along the vehicle (Z, towards the nose) and up (Y), metres.</summary>
    public readonly struct ProfilePoint
    {
        public readonly double Z, Y;

        public ProfilePoint(double z, double y)
        {
            Z = z; Y = y;
        }
    }

    /// <summary>How a hull is extruded from its side profile: the half-width tapers towards the roof and towards the very nose and tail.</summary>
    public sealed class HullShape
    {
        public readonly ProfilePoint[] Profile;
        public readonly double HalfWidth, RoofY, RoofTaper, NoseTaper;

        public HullShape(ProfilePoint[] profile, double halfWidth, double roofY, double roofTaper, double noseTaper)
        {
            Profile = profile; HalfWidth = halfWidth; RoofY = roofY; RoofTaper = roofTaper; NoseTaper = noseTaper;
        }
    }

    /// <summary>One kind of ground vehicle (the prototype's GROUND_TYPES).</summary>
    public sealed class GroundVehicleType
    {
        public string Key { get; }

        /// <summary>The share of the fleet.</summary>
        public double Share { get; }

        public double Length { get; }
        public double Width { get; }
        public double Height { get; }
        public HullShape Hull { get; }

        /// <summary>A motorbike has a rider on it.</summary>
        public bool Rider { get; }

        public double WheelRadius { get; }
        public double WheelWidth { get; }

        /// <summary>How far the wheels sit from the middle, sideways.</summary>
        public double WheelX { get; }

        /// <summary>Where the front and rear wheels sit along the vehicle.</summary>
        public double WheelZFront { get; }

        public double WheelZRear { get; }
        public int WheelCount { get; }
        public double CruiseMin { get; }
        public double CruiseMax { get; }

        /// <summary>Metres a second squared.</summary>
        public double Accel { get; }

        /// <summary>Bikes ride nearer the middle of the road than the lane the others keep.</summary>
        public double LaneScale { get; }

        public GroundVehicleType(string key, double share, double length, double width, double height, HullShape hull, bool rider, double wheelRadius, double wheelWidth,
            double wheelX, double wheelZFront, double wheelZRear, int wheelCount, double cruiseMin, double cruiseMax, double accel, double laneScale)
        {
            Key = key; Share = share; Length = length; Width = width; Height = height; Hull = hull; Rider = rider;
            WheelRadius = wheelRadius; WheelWidth = wheelWidth; WheelX = wheelX; WheelZFront = wheelZFront; WheelZRear = wheelZRear; WheelCount = wheelCount;
            CruiseMin = cruiseMin; CruiseMax = cruiseMax; Accel = accel; LaneScale = laneScale;
        }
    }

    /// <summary>One kind of aircraft (the prototype's AERIAL_TYPES).</summary>
    public sealed class AircraftType
    {
        public string Key { get; }
        public double Share { get; }
        public double Length { get; }
        public double Width { get; }
        public double Height { get; }
        public HullShape Hull { get; }

        /// <summary>The wing: a flat box of this span across, chord along and thickness, centred this far along the aircraft.</summary>
        public double WingSpan { get; }

        public double WingChord { get; }
        public double WingThickness { get; }
        public double WingZ { get; }
        public double SpeedMin { get; }
        public double SpeedMax { get; }

        public AircraftType(string key, double share, double length, double width, double height, HullShape hull, double wingSpan, double wingChord, double wingThickness,
            double wingZ, double speedMin, double speedMax)
        {
            Key = key; Share = share; Length = length; Width = width; Height = height; Hull = hull;
            WingSpan = wingSpan; WingChord = wingChord; WingThickness = wingThickness; WingZ = wingZ; SpeedMin = speedMin; SpeedMax = speedMax;
        }
    }

    /// <summary>The prototype's vehicle tables (Traffic.js) and its paint and neon palettes.</summary>
    public static class VehicleCatalog
    {
        static ProfilePoint[] P(params double[] zy)
        {
            var points = new ProfilePoint[zy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new ProfilePoint(zy[i * 2], zy[i * 2 + 1]);
            return points;
        }

        static readonly GroundVehicleType[] GroundTypes =
        {
            new GroundVehicleType("sedan", 0.40, 4.62, 1.94, 1.46,
                new HullShape(P(-2.31, 0.26, 2.31, 0.26, 2.33, 0.66, 1.55, 0.92, 0.62, 1.44, -0.72, 1.46, -1.62, 1.02, -2.29, 0.86), 0.97, 0.56, 0.32, 0.26),
                false, 0.34, 0.22, 0.86, 1.42, -1.46, 4, 11.5, 16.5, 5.4, 1.0),
            new GroundVehicleType("van", 0.19, 5.42, 2.16, 2.42,
                new HullShape(P(-2.71, 0.34, 2.71, 0.34, 2.73, 0.82, 2.50, 1.44, 1.88, 2.12, 1.56, 2.40, -2.58, 2.42, -2.71, 1.05), 1.08, 0.72, 0.16, 0.16),
                false, 0.38, 0.24, 0.94, 1.74, -1.70, 4, 9.5, 13.5, 4.0, 1.0),
            new GroundVehicleType("truck", 0.13, 8.6, 2.48, 3.36,
                new HullShape(P(-4.30, 0.48, 4.30, 0.48, 4.32, 1.12, 4.12, 2.40, 3.32, 2.66, 2.12, 2.66, 2.06, 3.36, -4.24, 3.36, -4.30, 1.20), 1.24, 0.80, 0.10, 0.10),
                false, 0.46, 0.30, 1.06, 3.05, -2.55, 4, 8.0, 11.5, 3.0, 1.0),
            new GroundVehicleType("bike", 0.28, 2.26, 0.80, 1.56,
                new HullShape(P(-1.13, 0.34, 1.13, 0.30, 1.17, 0.70, 0.78, 1.06, 0.10, 0.98, -0.55, 0.94, -1.07, 0.80), 0.30, 0.5, 0.34, 0.34),
                true, 0.32, 0.11, 0, 0.88, -0.88, 2, 13.0, 19.0, 7.5, 1.32),
        };

        static readonly AircraftType[] AircraftTypes =
        {
            new AircraftType("spinner", 0.66, 6.4, 2.7, 1.66,
                new HullShape(P(-3.20, 0.42, 3.20, 0.20, 3.28, 0.60, 2.30, 1.14, 0.40, 1.62, -1.50, 1.54, -3.15, 1.02), 1.05, 0.5, 0.42, 0.34),
                3.9, 1.25, 0.13, -0.6, 17, 30),
            new AircraftType("freighter", 0.34, 12.4, 4.3, 3.1,
                new HullShape(P(-6.20, 0.55, 6.20, 0.40, 6.30, 1.05, 4.90, 1.95, 2.20, 2.55, -2.60, 2.60, -5.40, 2.20, -6.20, 1.45), 1.7, 0.62, 0.24, 0.22),
                6.4, 2.2, 0.22, -1.6, 11, 19),
        };

        public static IReadOnlyList<GroundVehicleType> Ground => GroundTypes;

        public static IReadOnlyList<AircraftType> Aircraft => AircraftTypes;

        /// <summary>The body paints, sRGB hex.</summary>
        public static readonly uint[] Paint =
        {
            0x1b1f2a, 0x2c3242, 0x101216, 0x3a2230, 0x1d3340, 0x40332a, 0x232323, 0x14232b, 0x2f2038, 0x35383e,
        };

        /// <summary>The under-glow colours, sRGB hex.</summary>
        public static readonly uint[] Neon = { 0x2ff0ff, 0xff2f9e, 0xa64bff, 0x36ff8a, 0xff7a1f, 0x2f6bff };

        /// <summary>The shares of the ground types, in table order.</summary>
        public static double[] GroundShares()
        {
            var shares = new double[GroundTypes.Length];
            for (int i = 0; i < shares.Length; i++) shares[i] = GroundTypes[i].Share;
            return shares;
        }

        public static double[] AircraftShares()
        {
            var shares = new double[AircraftTypes.Length];
            for (int i = 0; i < shares.Length; i++) shares[i] = AircraftTypes[i].Share;
            return shares;
        }
    }

    /// <summary>Splitting a fleet between its types.</summary>
    public static class FleetBudget
    {
        /// <summary>
        /// Divide <paramref name="total"/> between types in proportion to their shares, by largest remainder, so the parts sum exactly to the total
        /// (the prototype's distributeBudget). Ties in the remainder go to the earlier type.
        /// </summary>
        public static int[] Distribute(int total, IReadOnlyList<double> shares)
        {
            if (total < 0) throw new ArgumentOutOfRangeException(nameof(total), total, "a fleet cannot be negative");
            if (shares == null || shares.Count == 0) throw new ArgumentException("a fleet needs at least one type", nameof(shares));
            double weight = 0;
            foreach (double s in shares)
            {
                if (!(s > 0)) throw new ArgumentOutOfRangeException(nameof(shares), s, "every share must be positive");
                weight += s;
            }

            int n = shares.Count;
            var counts = new int[n];
            var fraction = new double[n];
            int assigned = 0;
            for (int i = 0; i < n; i++)
            {
                double raw = total * shares[i] / weight;
                counts[i] = (int)Math.Floor(raw);
                fraction[i] = raw - counts[i];
                assigned += counts[i];
            }

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int byFraction = fraction[b].CompareTo(fraction[a]);
                return byFraction != 0 ? byFraction : a.CompareTo(b);
            });
            for (int k = 0, remaining = total - assigned; k < remaining; k++) counts[order[k]]++;
            return counts;
        }
    }
}
