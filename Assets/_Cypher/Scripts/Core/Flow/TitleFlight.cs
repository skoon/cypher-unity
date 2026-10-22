using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    public readonly struct FlightPose
    {
        public readonly double X, Y, Z;

        /// <summary>Radians, 0 facing +Z and growing toward +X (the player's convention).</summary>
        public readonly double Yaw;

        /// <summary>Radians, positive looks up.</summary>
        public readonly double Pitch;

        public FlightPose(double x, double y, double z, double yaw, double pitch)
        {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            Pitch = pitch;
        }
    }

    /// <summary>
    /// The camera path behind the title screen: a slow glide along the road centres of the city in a closed loop, eight metres
    /// up, looking ahead along the street. Each corner of the route is rounded with a curve that stays inside the junction, so the
    /// camera turns smoothly instead of snapping and never swings wide over a building; it is walked at constant speed, with slow
    /// independent sways in height, side offset and aim so it reads as a drifting camera rather than a rail. Pure and deterministic: the pose depends only on the time.
    /// </summary>
    public sealed class TitleFlight
    {
        public const double DefaultSpeed = 3.0;
        public const double DefaultHeight = 8.0;

        public const double HeightSway = 0.4;
        public const double SideSway = 1.2;
        public const double AimSway = 0.05;
        public const double BasePitch = -0.14;
        public const double PitchSway = 0.02;

        /// <summary>The camera aims at the point this far ahead on the path, which smooths its turns before the junctions.</summary>
        public const double LookAheadMetres = 14.0;

        /// <summary>How far before a corner the turn starts, in metres (less on short legs).</summary>
        public const double CornerCut = 9.0;

        const int SamplesPerCorner = 24;
        const double MinSegmentLength = 0.01;
        const double ReversalDot = -0.5;   // a turn sharper than 120 degrees cannot be rounded inside the road

        readonly double _speed;
        readonly double _height;
        readonly Point2[] _samples;
        readonly double[] _cumulative;   // distance along the loop at each sample; one extra entry for the closing point

        public TitleFlight(IReadOnlyList<Point2> loop, double speed = DefaultSpeed, double height = DefaultHeight)
        {
            if (loop == null) throw new ArgumentNullException(nameof(loop));
            if (loop.Count < 3) throw new ArgumentException("a loop needs at least three points", nameof(loop));
            if (!(speed > 0) || double.IsInfinity(speed)) throw new ArgumentOutOfRangeException(nameof(speed), speed, "speed must be positive and finite");
            if (!(height > HeightSway) || double.IsInfinity(height)) throw new ArgumentOutOfRangeException(nameof(height), height, "height must be above the sway so the camera stays off the ground");
            for (int i = 0; i < loop.Count; i++)
            {
                if (Distance(loop[i], loop[(i + 1) % loop.Count]) < MinSegmentLength) throw new ArgumentException($"points {i} and {(i + 1) % loop.Count} are the same place", nameof(loop));
            }

            _speed = speed;
            _height = height;

            int n = loop.Count;
            var points = new List<Point2>(n * (SamplesPerCorner + 1));
            for (int i = 0; i < n; i++)
            {
                Point2 prev = loop[(i + n - 1) % n], corner = loop[i], next = loop[(i + 1) % n];
                double lenIn = Distance(prev, corner), lenOut = Distance(corner, next);
                double ux = (corner.X - prev.X) / lenIn, uz = (corner.Z - prev.Z) / lenIn;
                double vx = (next.X - corner.X) / lenOut, vz = (next.Z - corner.Z) / lenOut;
                if (ux * vx + uz * vz < ReversalDot) throw new ArgumentException($"the route turns back on itself at point {i}", nameof(loop));

                double cut = Math.Min(CornerCut, 0.45 * Math.Min(lenIn, lenOut));
                var a = new Point2(corner.X - ux * cut, corner.Z - uz * cut);
                var b = new Point2(corner.X + vx * cut, corner.Z + vz * cut);
                for (int s = 0; s <= SamplesPerCorner; s++) points.Add(QuadraticBezier(a, corner, b, (double)s / SamplesPerCorner));
            }
            _samples = points.ToArray();

            _cumulative = new double[_samples.Length + 1];
            for (int i = 0; i < _samples.Length; i++) _cumulative[i + 1] = _cumulative[i] + Distance(_samples[i], _samples[(i + 1) % _samples.Length]);
        }

        /// <summary>The route the title uses: the loop of road centres through the 3x3 slice that the benchmark also walks.</summary>
        public static TitleFlight Default()
        {
            double near = CityLayout.RoadCentre(CityLayout.PlazaIndex);
            double east = CityLayout.RoadCentre(CityLayout.PlazaIndex + 1);
            double far = CityLayout.RoadCentre(CityLayout.PlazaIndex - 1);
            return new TitleFlight(new[]
            {
                new Point2(east, near),
                new Point2(east, east),
                new Point2(near, east),
                new Point2(near, far),
                new Point2(east, far),
            });
        }

        /// <summary>Length of the smoothed loop in metres.</summary>
        public double LoopLength => _cumulative[_cumulative.Length - 1];

        public double LoopSeconds => LoopLength / _speed;

        /// <summary>The point on the loop <paramref name="distance"/> metres along it, wrapping around.</summary>
        public Point2 PointAt(double distance)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance)) throw new ArgumentOutOfRangeException(nameof(distance), distance, "distance must be finite");
            double d = distance % LoopLength;
            if (d < 0) d += LoopLength;

            int lo = 0, hi = _samples.Length;   // find the last sample at or before d
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_cumulative[mid] <= d) lo = mid;
                else hi = mid;
            }
            double span = _cumulative[lo + 1] - _cumulative[lo];
            double f = span > 0 ? (d - _cumulative[lo]) / span : 0;
            Point2 a = _samples[lo], b = _samples[(lo + 1) % _samples.Length];
            return new Point2(a.X + (b.X - a.X) * f, a.Z + (b.Z - a.Z) * f);
        }

        public FlightPose PoseAt(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "time must be finite");
            double d = seconds * _speed;
            Point2 here = PointAt(d);
            Point2 ahead = PointAt(d + LookAheadMetres);
            double heading = Math.Atan2(ahead.X - here.X, ahead.Z - here.Z);

            double side = SideSway * Math.Sin(2 * Math.PI * seconds / 23.0);
            double x = here.X + Math.Cos(heading) * side;   // the right-hand direction of a camera facing `heading`
            double z = here.Z - Math.Sin(heading) * side;
            double y = _height + HeightSway * Math.Sin(2 * Math.PI * seconds / 13.0);
            double yaw = heading + AimSway * Math.Sin(2 * Math.PI * seconds / 17.0);
            double pitch = BasePitch + PitchSway * Math.Sin(2 * Math.PI * seconds / 11.0);
            return new FlightPose(x, y, z, yaw, pitch);
        }

        static double Distance(Point2 a, Point2 b)
        {
            double dx = a.X - b.X, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Quadratic Bezier from a to b with the corner as its control point: tangent to both legs at the ends and always inside the
        /// triangle a-corner-b, so it can only cut the corner, never overshoot it.
        /// </summary>
        static Point2 QuadraticBezier(Point2 a, Point2 control, Point2 b, double t)
        {
            double wa = (1 - t) * (1 - t), wc = 2 * (1 - t) * t, wb = t * t;
            return new Point2(a.X * wa + control.X * wc + b.X * wb, a.Z * wa + control.Z * wc + b.Z * wb);
        }
    }
}
