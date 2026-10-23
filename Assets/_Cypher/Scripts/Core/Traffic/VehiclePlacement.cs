using System;

namespace Cypher.Core
{
    /// <summary>Where each part of one ground vehicle goes this frame: the transforms Traffic.js's lateUpdate writes. The wheel list always holds four; a bike's last two are unused.</summary>
    public sealed class VehiclePose
    {
        public Mat4 Body, Rig, Underglow, Beams, Shadow;
        public readonly Mat4[] Wheels = new Mat4[4];
    }

    /// <summary>
    /// The prototype's per-frame placement of vehicle parts (Traffic.lateUpdate), as a reference. The vehicle shader does the same maths on the GPU from the vehicle's record;
    /// this is what it is checked against, and what the first-person tests use to find a vehicle's body in the world.
    /// </summary>
    public static class VehiclePlacement
    {
        /// <summary>The rate of the under-glow's pulse, and of its phase from a vehicle's glow value.</summary>
        public const double PulseRate = 1.7, PulsePhase = 9;

        /// <summary>A vehicle's position and heading <paramref name="alpha"/> of the way from the last step to this one; the heading takes the short way round.</summary>
        public static (double x, double z, double heading) Interpolate(Vehicle v, double alpha)
        {
            double x = v.PrevX + (v.X - v.PrevX) * alpha;
            double z = v.PrevZ + (v.Z - v.PrevZ) * alpha;
            double heading = v.PrevHeading + CrowdSim.ShortestAngle(v.PrevHeading, v.Heading) * alpha;
            return (x, z, heading);
        }

        /// <summary>How big the under-glow is at a time: its pulse, 0.5 to 1.</summary>
        public static double Pulse(Vehicle v, double elapsed) => 0.75 + Math.Sin(elapsed * PulseRate + v.Glow * PulsePhase) * 0.25;

        /// <summary>Where the parts of <paramref name="v"/> are, <paramref name="alpha"/> of the way through the step, at game time <paramref name="elapsed"/>. <paramref name="roadY"/> is the asphalt's height.</summary>
        public static VehiclePose Place(Vehicle v, double alpha, double elapsed, double roadY)
        {
            (double x, double z, double heading) = Interpolate(v, alpha);
            double y = v.Y;
            GroundVehicleType t = v.Type;
            double fx = Math.Sin(heading), fz = Math.Cos(heading);
            Quat turn = Quat.AboutY(heading);

            var pose = new VehiclePose { Body = Compose(x, y, z, turn, 1, 1, 1) };

            for (int k = 0; k < 4; k++)
            {
                if (k >= t.WheelCount)
                {
                    pose.Wheels[k] = Mat4.Compose(0, -900, 0, 0, 0, 0, 1, 0.001, 0.001, 0.001);
                    continue;
                }
                double side = t.WheelCount == 2 ? 0 : (k % 2 == 0 ? -1 : 1);
                double wz = t.WheelCount == 2 ? (k == 0 ? t.WheelZFront : t.WheelZRear) : (k < 2 ? t.WheelZFront : t.WheelZRear);
                double wx = side * t.WheelX;
                Quat q = Quat.Multiply(turn, Quat.AboutX(v.Spin));
                pose.Wheels[k] = Compose(x + fx * wz + fz * wx, y + t.WheelRadius, z + fz * wz - fx * wx, q, t.WheelWidth, t.WheelRadius, t.WheelRadius);
            }

            pose.Rig = Compose(x, y, z, turn, t.Width, t.Height, t.Length);

            double pulse = Pulse(v, elapsed);
            pose.Underglow = Compose(x, roadY + 0.03, z, turn, t.Width * pulse, 1, t.Length * pulse);

            double nose = t.Length * 0.5 - 0.15;
            double beamWidth = t.Width / 1.94;
            pose.Beams = Compose(x + fx * nose, y + t.Height * 0.36, z + fz * nose, turn, beamWidth, 1, 1);

            pose.Shadow = Compose(x, roadY + 0.02, z, turn, t.Width * 0.85, 1, t.Length * 0.62);
            return pose;
        }

        /// <summary>An aircraft's position <paramref name="alpha"/> of the way through the step, with its gentle bobbing; and the transform of its body (scale 1) and of its glow (scaled into its box).</summary>
        public static (Mat4 body, Mat4 glow) PlaceAircraft(Aircraft a, double alpha)
        {
            double x = a.PrevX + (a.X - a.PrevX) * alpha;
            double z = a.PrevZ + (a.Z - a.PrevZ) * alpha;
            double y = a.PrevY + (a.Y - a.PrevY) * alpha + Math.Sin(a.Bob) * 0.8;
            Quat q = Quat.Multiply(Quat.AboutY(a.Heading), Quat.AboutZ(a.Bank));
            AircraftType t = a.Type;
            return (Compose(x, y, z, q, 1, 1, 1), Compose(x, y, z, q, t.Width, t.Height, t.Length));
        }

        static Mat4 Compose(double x, double y, double z, Quat q, double sx, double sy, double sz) => Mat4.Compose(x, y, z, q.X, q.Y, q.Z, q.W, sx, sy, sz);

        /// <summary>A rotation as a quaternion (x, y, z, w).</summary>
        public readonly struct Quat
        {
            public readonly double X, Y, Z, W;

            public Quat(double x, double y, double z, double w)
            {
                X = x; Y = y; Z = z; W = w;
            }

            public static Quat AboutX(double angle) => new Quat(Math.Sin(angle / 2), 0, 0, Math.Cos(angle / 2));

            public static Quat AboutY(double angle) => new Quat(0, Math.Sin(angle / 2), 0, Math.Cos(angle / 2));

            public static Quat AboutZ(double angle) => new Quat(0, 0, Math.Sin(angle / 2), Math.Cos(angle / 2));

            /// <summary>The rotation that turns by <paramref name="b"/> first and then by <paramref name="a"/> (three.js's a.multiply(b)).</summary>
            public static Quat Multiply(Quat a, Quat b) => new Quat(
                a.X * b.W + a.W * b.X + a.Y * b.Z - a.Z * b.Y,
                a.Y * b.W + a.W * b.Y + a.Z * b.X - a.X * b.Z,
                a.Z * b.W + a.W * b.Z + a.X * b.Y - a.Y * b.X,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
        }
    }
}
