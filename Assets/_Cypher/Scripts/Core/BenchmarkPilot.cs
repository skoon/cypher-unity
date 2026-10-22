using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Plays the part of a person for the benchmark: walks a fixed route of waypoints, sprinting on some
    /// legs, and sweeps the view up and down so the camera sees the skyline and the street. It steers
    /// toward the next waypoint with ordinary look and move commands, so the real character controller,
    /// collisions and kerbs are all exercised. Closed loop on position, so the route stays the same
    /// whatever the frame rate. Pure: the caller supplies the character's pose and applies the command.
    /// </summary>
    public sealed class BenchmarkPilot
    {
        public readonly struct Waypoint
        {
            public readonly double X, Z;

            /// <summary>Run on the leg that ends at this waypoint.</summary>
            public readonly bool Sprint;

            public Waypoint(double x, double z, bool sprint)
            {
                X = x;
                Z = z;
                Sprint = sprint;
            }
        }

        public const double ArrivalRadius = 2.5;
        public const double MaxTurnRate = 2.2;        // rad/s
        public const double MaxPitchRate = 1.2;       // rad/s
        public const double PitchCentre = 0.1;        // rad, a touch above level
        public const double PitchAmplitude = 0.35;    // rad
        public const double PitchPeriod = 9;          // s

        /// <summary>Turning more than this (rad) off the heading slows the walk to a crawl.</summary>
        const double SlowTurnAngle = 1.2;
        const double MinForward = 0.15;

        /// <summary>No sprinting while still turning.</summary>
        const double SprintAngle = 0.3;

        readonly Waypoint[] _route;
        readonly double _sensitivity;

        public BenchmarkPilot(IReadOnlyList<Waypoint> route, double sensitivity = LookState.DefaultSensitivity)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (route.Count < 2) throw new ArgumentException("a route needs at least two waypoints", nameof(route));
            if (!(sensitivity > 0)) throw new ArgumentOutOfRangeException(nameof(sensitivity), sensitivity, "sensitivity must be positive");
            _route = new Waypoint[route.Count];
            for (int i = 0; i < _route.Length; i++) _route[i] = route[i];
            _sensitivity = sensitivity;
        }

        /// <summary>
        /// A loop through the slice that covers the central avenue and both cross streets: the short
        /// legs walk, the two long ones run. About 372 m, a bit over a minute.
        /// </summary>
        public static IReadOnlyList<Waypoint> DefaultRoute { get; } = BuildDefaultRoute();

        static IReadOnlyList<Waypoint> BuildDefaultRoute()
        {
            double near = CityLayout.RoadCentre(CityLayout.PlazaIndex);        // the road beside the plaza block
            double east = CityLayout.RoadCentre(CityLayout.PlazaIndex + 1);
            double far = CityLayout.RoadCentre(CityLayout.PlazaIndex - 1);     // the outer road of the 3x3 window
            return new[]
            {
                new Waypoint(east, near, false),
                new Waypoint(east, east, true),
                new Waypoint(near, east, false),
                new Waypoint(near, far, true),
                new Waypoint(east, far, false),
            };
        }

        /// <summary>
        /// A tour of the whole city for the full-grid measurement: down the avenue beside the plaza to the north edge, round the
        /// outermost roads past every edge and corner (the apron and the skyline ring show there), and on round again.
        /// Every leg runs along a road corridor, so the route never meets a wall, and the closing leg runs back along the road
        /// beside the plaza. A lap is about 2.2 km: sprinting nearly all of it, five minutes or so.
        /// </summary>
        public static IReadOnlyList<Waypoint> CityRoute { get; } = BuildCityRoute();

        static IReadOnlyList<Waypoint> BuildCityRoute()
        {
            double plaza = CityLayout.RoadCentre(CityLayout.PlazaIndex);        // the road beside the plaza block
            double side = CityLayout.RoadCentre(CityLayout.PlazaIndex + 1);
            double low = CityLayout.RoadCentre(0);                              // the outermost roads, on all four sides
            double high = CityLayout.RoadCentre(CityLayout.Grid);
            return new[]
            {
                new Waypoint(side, plaza, false),
                new Waypoint(side, high, true),
                new Waypoint(high, high, true),
                new Waypoint(high, low, true),
                new Waypoint(low, low, true),
                new Waypoint(low, high, true),
                new Waypoint(low, plaza, true),
            };
        }

        public static IReadOnlyList<Waypoint> Route(BenchmarkRoute route)
        {
            switch (route)
            {
                case BenchmarkRoute.Centre: return DefaultRoute;
                case BenchmarkRoute.City: return CityRoute;
                default: throw new ArgumentOutOfRangeException(nameof(route), route, "unknown benchmark route");
            }
        }

        /// <summary>Index of the waypoint being walked toward.</summary>
        public int Target { get; private set; }

        /// <summary>Waypoints reached so far, counting every lap.</summary>
        public int WaypointsReached { get; private set; }

        public int Laps => WaypointsReached / _route.Length;

        /// <summary>Seconds of simulated time fed in so far.</summary>
        public double Elapsed { get; private set; }

        public int WaypointCount => _route.Length;

        /// <summary>
        /// The command for this frame, given where the character is and which way it looks (yaw 0 faces +Z,
        /// positive toward +X; pitch positive looks up).
        /// </summary>
        public PlayerCommand Next(double x, double z, double yaw, double pitch, double dt)
        {
            if (!(dt > 0) || double.IsInfinity(dt)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "dt must be positive and finite");

            Elapsed += dt;

            double dx = _route[Target].X - x, dz = _route[Target].Z - z;
            if (dx * dx + dz * dz <= ArrivalRadius * ArrivalRadius)
            {
                WaypointsReached++;
                Target = (Target + 1) % _route.Length;
                dx = _route[Target].X - x;
                dz = _route[Target].Z - z;
            }

            double error = WrapToPi(Math.Atan2(dx, dz) - yaw);
            double turn = Clamp(error, -MaxTurnRate * dt, MaxTurnRate * dt);
            double off = Math.Abs(error);
            double forward = Clamp(1 - off / SlowTurnAngle, MinForward, 1);
            bool sprint = _route[Target].Sprint && off < SprintAngle;

            double wantedPitch = PitchCentre + PitchAmplitude * Math.Sin(2 * Math.PI * Elapsed / PitchPeriod);
            double tilt = Clamp(wantedPitch - pitch, -MaxPitchRate * dt, MaxPitchRate * dt);

            return new PlayerCommand(forward, 0, sprint, false, false, turn / _sensitivity, tilt / _sensitivity);
        }

        static double WrapToPi(double angle)
        {
            double a = angle % (2 * Math.PI);
            if (a > Math.PI) a -= 2 * Math.PI;
            else if (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
    }
}
