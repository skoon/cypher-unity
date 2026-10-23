using System;

namespace Cypher.Core
{
    /// <summary>
    /// A 4x4 transform in the same layout as three.js's Matrix4.elements: column-major, translation in elements 12 to 14. Just what the pedestrian rig needs:
    /// compose from a position, a quaternion and a scale, and multiply.
    /// </summary>
    public sealed class Mat4
    {
        public readonly double[] E = new double[16];

        /// <summary>The matrix T * R * S: scale first, then turn, then move.</summary>
        public static Mat4 Compose(double px, double py, double pz, double qx, double qy, double qz, double qw, double sx, double sy, double sz)
        {
            var m = new Mat4();
            double[] te = m.E;
            double x2 = qx + qx, y2 = qy + qy, z2 = qz + qz;
            double xx = qx * x2, xy = qx * y2, xz = qx * z2, yy = qy * y2, yz = qy * z2, zz = qz * z2;
            double wx = qw * x2, wy = qw * y2, wz = qw * z2;
            te[0] = (1 - (yy + zz)) * sx; te[1] = (xy + wz) * sx; te[2] = (xz - wy) * sx; te[3] = 0;
            te[4] = (xy - wz) * sy; te[5] = (1 - (xx + zz)) * sy; te[6] = (yz + wx) * sy; te[7] = 0;
            te[8] = (xz + wy) * sz; te[9] = (yz - wx) * sz; te[10] = (1 - (xx + yy)) * sz; te[11] = 0;
            te[12] = px; te[13] = py; te[14] = pz; te[15] = 1;
            return m;
        }

        /// <summary>a * b: apply b, then a.</summary>
        public static Mat4 Multiply(Mat4 a, Mat4 b)
        {
            var m = new Mat4();
            double[] ae = a.E, be = b.E, te = m.E;
            for (int col = 0; col < 4; col++)
            {
                for (int row = 0; row < 4; row++)
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++) sum += ae[k * 4 + row] * be[col * 4 + k];
                    te[col * 4 + row] = sum;
                }
            }
            return m;
        }

        /// <summary>Where a point goes (w = 1).</summary>
        public (double X, double Y, double Z) Transform(double x, double y, double z) => (
            E[0] * x + E[4] * y + E[8] * z + E[12],
            E[1] * x + E[5] * y + E[9] * z + E[13],
            E[2] * x + E[6] * y + E[10] * z + E[14]);
    }

    /// <summary>What a pose needs to know about one pedestrian. Positions and angles run from the previous step to the current one; <c>Alpha</c> says how far between.</summary>
    public struct PoseInput
    {
        public double PrevX, PrevZ, PrevY, X, Z, Y;
        public double PrevHeading, Heading, PrevPhase, Phase;
        public double Speed, Scale, Build;
        public bool LongCoat, Hood, Pack, Umbrella, Trim;
        public double LookSeed;

        /// <summary>The head's current turn: smoothed from step to step, so the pose both reads it and returns the new value.</summary>
        public double LookYaw;

        /// <summary>People near the camera glance at it (the prototype's lod 0).</summary>
        public bool Near;

        public double Time, CamX, CamZ, Alpha;
    }

    /// <summary>The joint transforms of one posed pedestrian (the prototype's rig, in the order Crowd._pose writes them), and the head's new turn.</summary>
    public sealed class PedestrianPoseResult
    {
        public Mat4 Torso, Trim, Head, Hood, ArmL, ArmR, Umbrella, Pack, ThighL, ShinL, ThighR, ShinR;
        public double LookYaw;
    }

    /// <summary>
    /// The pedestrian rig's proportions and its pose, a line-for-line port of Crowd._pose. The shader poses every vertex from the same formulas on the GPU; this is
    /// the reference it is checked against (and the prototype's matrices are what this is checked against). Not used per frame.
    /// </summary>
    public static class PedestrianPose
    {
        // Rig proportions, in metres for a 1.0-scale (about 1.72 m) figure.
        public const double HipY = 0.89;
        public const double ThighLength = 0.45;
        public const double ShinLength = 0.44;
        public const double LegLength = ThighLength + ShinLength;
        public const double ShoulderY = 0.56;
        public const double ShoulderX = 0.185;
        public const double HipX = 0.098;
        public const double HeadY = 0.70;

        public static PedestrianPoseResult Pose(PoseInput p)
        {
            double s = p.Scale;
            double x = p.PrevX + (p.X - p.PrevX) * p.Alpha;
            double z = p.PrevZ + (p.Z - p.PrevZ) * p.Alpha;
            double y = p.PrevY + (p.Y - p.PrevY) * p.Alpha;
            double heading = p.PrevHeading + CrowdSim.ShortestAngle(p.PrevHeading, p.Heading) * p.Alpha;
            double phase = p.PrevPhase + (p.Phase - p.PrevPhase) * p.Alpha;
            double t = p.Time;

            double speed = p.Speed;
            double gait = Clamp(speed / 1.15, 0, 1.35);
            double cosP = Math.Cos(phase), sinP = Math.Sin(phase);

            // gait
            double thighA = (0.26 + 0.14 * speed) * gait;
            double kneeA = (0.62 + 0.26 * speed) * gait;
            double armA = (0.16 + 0.15 * speed) * gait;
            double thighL = thighA * cosP, thighR = -thighA * cosP;
            double kneeL = kneeA * Math.Max(0, -sinP), kneeR = kneeA * Math.Max(0, sinP);

            // hips drop as the stance leg extends so the planted foot stays planted
            double stance = Math.Max(Math.Abs(thighL), Math.Abs(thighR));
            double idleBreath = Math.Sin(t * 1.15 + p.LookSeed) * 0.008 * (1 - Math.Min(1, gait));
            double hipY = HipY - 0.75 * LegLength * (1 - Math.Cos(stance)) + idleBreath;
            double sway = 0.024 * sinP * gait + Math.Sin(t * 0.7 + p.LookSeed) * 0.012 * (1 - Math.Min(1, gait));

            double hipTwist = 0.095 * cosP * gait;
            double spineTwist = -0.21 * cosP * gait;
            double lean = 0.045 + speed * 0.045;

            // head: cancels the shoulder counter-rotation, glances at the camera
            double lookYaw = LookTarget(heading, x, z, p.Near, p.CamX, p.CamZ, t, p.LookSeed);
            double look = p.LookYaw + (lookYaw - p.LookYaw) * 0.12;

            var r = new PedestrianPoseResult { LookYaw = look };

            Mat4 root = Mat4.Compose(x, y - 0.02, z, 0, Math.Sin(heading * 0.5), 0, Math.Cos(heading * 0.5), s, s, s);
            Mat4 hips = Link(root, sway, hipY, 0, QY(hipTwist));
            Mat4 spine = Link(hips, 0, 0, 0, QXY(lean, spineTwist));

            double bw = p.Build;
            double bd = 0.94 + (p.Build - 1) * 0.6;
            r.Torso = Link(spine, 0, 0, 0, QY(0), bw, 1, bd);
            if (p.Trim) r.Trim = Link(spine, 0, 0, 0, QY(0), bw * 1.02, 1, bd * 1.02);

            Mat4 head = Link(spine, 0, HeadY, 0, QXY(-lean * 0.85 + Math.Sin(phase * 2) * 0.018 * gait, -spineTwist * 0.55 + look));
            r.Head = Link(head, 0, 0, 0, QY(0));
            if (p.Hood) r.Hood = Link(head, 0, 0.012, 0, QY(0), 1.02, 1.02, 1.02);

            // arms counter-swing the legs; an umbrella locks the right arm into a carry
            double armLimb = 0.92 + (p.Build - 1) * 0.5;
            r.ArmL = Link(spine, -ShoulderX * bw, ShoulderY, 0, QX(armA * cosP), armLimb, 1, armLimb);
            if (p.Umbrella)
            {
                r.ArmR = Link(spine, ShoulderX * bw, ShoulderY, 0, QXY(-1.12, 0.16), armLimb, 1, armLimb);
                r.Umbrella = Link(spine, 0.175, 0.50, 0.16, QXY(0.10 + Math.Sin(t * 0.9 + p.LookSeed) * 0.03, Math.Sin(t * 0.6 + p.LookSeed) * 0.06));
            }
            else
            {
                r.ArmR = Link(spine, ShoulderX * bw, ShoulderY, 0, QX(-(armA * cosP)), armLimb, 1, armLimb);
            }

            if (p.Pack) r.Pack = Link(spine, 0, 0.30, -0.155, QX(0.06), bw, 1, 1);

            // legs: the thigh and then the shin hanging off its knee, each scaled a little by build
            double legW = 0.94 + (p.Build - 1) * 0.55;
            Mat4 thighLJoint = Link(hips, -HipX * bw, 0, 0, QX(-thighL));
            r.ThighL = Link(thighLJoint, 0, 0, 0, QY(0), legW, 1, legW);
            r.ShinL = Link(Link(thighLJoint, 0, -ThighLength, 0, QX(kneeL)), 0, 0, 0, QY(0), legW, 1, legW);

            Mat4 thighRJoint = Link(hips, HipX * bw, 0, 0, QX(-thighR));
            r.ThighR = Link(thighRJoint, 0, 0, 0, QY(0), legW, 1, legW);
            r.ShinR = Link(Link(thighRJoint, 0, -ThighLength, 0, QX(kneeR)), 0, 0, 0, QY(0), legW, 1, legW);
            return r;
        }

        /// <summary>
        /// Where a person's head wants to point, relative to their body: a slow wander, or, for someone near enough (within about 9.5 m, and the camera not behind them), a
        /// glance at the camera. The head eases toward this a little each frame.
        /// </summary>
        public static double LookTarget(double heading, double x, double z, bool near, double camX, double camZ, double time, double lookSeed)
        {
            double lookYaw = Math.Sin(time * 0.42 + lookSeed * 3.1) * 0.22;
            if (near)
            {
                double dx = camX - x, dz = camZ - z;
                double d2 = dx * dx + dz * dz;
                if (d2 < 90)
                {
                    double want = CrowdSim.ShortestAngle(heading, Math.Atan2(dx, dz));
                    if (Math.Abs(want) < 1.25) lookYaw = want * 0.7;
                }
            }
            return lookYaw;
        }

        /// <summary>The matrix of a pedestrian drawn as a single static body (the cheaper levels of detail): scaled by build and height, turned to face the way they walk.</summary>
        public static Mat4 StaticBody(double x, double y, double z, double heading, double scale, double build) =>
            Mat4.Compose(x, y, z, 0, Math.Sin(heading * 0.5), 0, Math.Cos(heading * 0.5), scale * build, scale, scale);

        /// <summary>parent * T(t) * R(q) * S(s).</summary>
        static Mat4 Link(Mat4 parent, double tx, double ty, double tz, double[] q, double sx = 1, double sy = 1, double sz = 1) =>
            Mat4.Multiply(parent, Mat4.Compose(tx, ty, tz, q[0], q[1], q[2], q[3], sx, sy, sz));

        /// <summary>Rx(rx) * Ry(ry) as a quaternion (x, y, z, w).</summary>
        static double[] QXY(double rx, double ry)
        {
            double hx = rx * 0.5, hy = ry * 0.5;
            double sx = Math.Sin(hx), cx = Math.Cos(hx), sy = Math.Sin(hy), cy = Math.Cos(hy);
            return new[] { sx * cy, cx * sy, sx * sy, cx * cy };
        }

        static double[] QY(double ry) => new[] { 0, Math.Sin(ry * 0.5), 0, Math.Cos(ry * 0.5) };

        static double[] QX(double rx) => new[] { Math.Sin(rx * 0.5), 0, 0, Math.Cos(rx * 0.5) };

        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
    }
}
