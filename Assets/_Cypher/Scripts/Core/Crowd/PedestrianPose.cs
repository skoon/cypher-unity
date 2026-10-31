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

        // A fighter's pose, all zero for a civilian (which leaves the walk exactly as it was). Each runs 0 to 1 except the pitch (radians, up positive) and the side
        // (-1 to 1, which way a hit or a fall turns the body).

        /// <summary>Gun up at the shoulder, both hands on it.</summary>
        public double Aim, AimPitch;

        /// <summary>Knees bent behind low cover.</summary>
        public double Crouch;

        /// <summary>The kick of a shot, decaying.</summary>
        public double Recoil;

        /// <summary>The jerk of being hit, decaying, and which way it turns.</summary>
        public double Flinch, Side;

        /// <summary>How far through falling dead: 0 standing, 1 lying on the back.</summary>
        public double Death;

        /// <summary>What is in the right hand.</summary>
        public HeldWeapon Weapon;

        /// <summary>A glowing visor across the eyes (the gang look).</summary>
        public bool Visor;
    }

    /// <summary>The gun a figure carries: drawn in the right hand, collapsed for everyone else.</summary>
    public enum HeldWeapon
    {
        None = 0,
        Smg = 1,
        Revolver = 2,
    }

    /// <summary>The joint transforms of one posed pedestrian (the prototype's rig, in the order Crowd._pose writes them), and the head's new turn.</summary>
    public sealed class PedestrianPoseResult
    {
        public Mat4 Torso, Trim, Head, Hood, ArmL, ArmR, Umbrella, Pack, ThighL, ShinL, ThighR, ShinR;

        /// <summary>The held gun (in the right hand) and the visor (the head's frame); null when the figure has neither.</summary>
        public Mat4 Gun, Visor;

        public double LookYaw;
    }

    /// <summary>The volumes a shot can hit on a posed figure: a capsule from the legs to the shoulders, and a sphere for the head. World metres.</summary>
    public readonly struct PoseHitShape
    {
        public readonly V3 BodyA, BodyB, Head;
        public readonly double BodyRadius, HeadRadius;

        public PoseHitShape(V3 bodyA, V3 bodyB, double bodyRadius, V3 head, double headRadius)
        {
            BodyA = bodyA; BodyB = bodyB; BodyRadius = bodyRadius; Head = head; HeadRadius = headRadius;
        }
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

        // Combat poses (radians and metres; the shader has the same numbers).
        public const double CrouchThigh = 1.40, CrouchKnee = 2.40, CrouchLean = 0.30;
        public const double AimLean = 0.08, AimRaiseR = 1.35, AimTurnR = -0.12, AimRaiseL = 1.25, AimTurnL = 0.62;
        public const double CarryRaise = 0.45, CarrySwing = 0.35;
        public const double RecoilKick = 0.25, RecoilLean = 0.05;
        public const double FlinchLean = 0.35, FlinchTwist = 0.40;
        public const double FallAngle = Math.PI / 2, FallLift = 0.13, FallTwist = 0.50, FallBuckle = 0.60, FallArms = 0.10;   // the arms end lying at the sides

        /// <summary>How much a crouch squashes the cheap bodies.</summary>
        public const double StaticCrouch = 0.35;

        /// <summary>Where the gun sits in the right arm's frame (at the hand), and its turn: barrel along the forearm, top to the palm.</summary>
        public const double GunY = -0.62, GunZ = 0.045, GunTurn = Math.PI / 2 - 0.22;

        /// <summary>The hit volumes: the body capsule's ends (up the root from the feet, and up the spine from the hips) and radius, and the head sphere's.</summary>
        public const double HitLegs = 0.30, HitShoulders = 0.42, HitBodyRadius = 0.24, HitHeadRadius = 0.14;

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

            // combat: every term below is zero for a civilian
            double aim = Clamp(p.Aim, 0, 1), crouch = Clamp(p.Crouch, 0, 1), death = Clamp(p.Death, 0, 1);
            double recoil = Clamp(p.Recoil, 0, 1), flinch = Clamp(p.Flinch, 0, 1), side = Clamp(p.Side, -1, 1);
            double fall = death * death;
            double bend = Bend(crouch, death);
            double pitch = p.AimPitch;

            // gait
            double thighA = (0.26 + 0.14 * speed) * gait;
            double kneeA = (0.62 + 0.26 * speed) * gait;
            double armA = (0.16 + 0.15 * speed) * gait;
            double thighL = thighA * cosP, thighR = -thighA * cosP;
            double kneeL = kneeA * Math.Max(0, -sinP), kneeR = kneeA * Math.Max(0, sinP);

            // hips drop as the stance leg extends so the planted foot stays planted, and as the knees bend so both feet stay down
            double stance = Math.Max(Math.Abs(thighL), Math.Abs(thighR));
            double idleBreath = Math.Sin(t * 1.15 + p.LookSeed) * 0.008 * (1 - Math.Min(1, gait));
            double hipY = HipY - 0.75 * LegLength * (1 - Math.Cos(stance)) + idleBreath - CrouchDrop(bend);
            double sway = 0.024 * sinP * gait + Math.Sin(t * 0.7 + p.LookSeed) * 0.012 * (1 - Math.Min(1, gait));
            thighL += CrouchThigh * bend; thighR += CrouchThigh * bend;
            kneeL += CrouchKnee * bend; kneeR += CrouchKnee * bend;

            double hipTwist = 0.095 * cosP * gait;
            double spineTwist = -0.21 * cosP * gait * (1 - aim) + FlinchTwist * flinch * side;
            double lean = 0.045 + speed * 0.045 + CrouchLean * bend + AimLean * aim - RecoilLean * recoil - FlinchLean * flinch;

            // head: cancels the shoulder counter-rotation, glances at the camera (not while aiming down the gun), and looks where the gun points
            double lookYaw = LookTarget(heading, x, z, p.Near, p.CamX, p.CamZ, t, p.LookSeed);
            double look = p.LookYaw + (lookYaw - p.LookYaw) * 0.12;

            var r = new PedestrianPoseResult { LookYaw = look };

            // the root tips back about the feet as the body falls, lifted so the back lies on the ground and not in it
            double[] turn = QYX(heading + FallTwist * side * fall, -FallAngle * fall);
            Mat4 root = Mat4.Compose(x, y - 0.02 + FallLift * fall, z, turn[0], turn[1], turn[2], turn[3], s, s, s);
            Mat4 hips = Link(root, sway, hipY, 0, QY(hipTwist));
            Mat4 spine = Link(hips, 0, 0, 0, QXY(lean, spineTwist));

            double bw = p.Build;
            double bd = 0.94 + (p.Build - 1) * 0.6;
            r.Torso = Link(spine, 0, 0, 0, QY(0), bw, 1, bd);
            if (p.Trim) r.Trim = Link(spine, 0, 0, 0, QY(0), bw * 1.02, 1, bd * 1.02);

            Mat4 head = Link(spine, 0, HeadY, 0, QXY(-lean * 0.85 + Math.Sin(phase * 2) * 0.018 * gait - pitch * aim, -spineTwist * 0.55 + look * (1 - aim)));
            r.Head = Link(head, 0, 0, 0, QY(0));
            if (p.Hood) r.Hood = Link(head, 0, 0.012, 0, QY(0), 1.02, 1.02, 1.02);
            if (p.Visor) r.Visor = r.Head;

            // arms counter-swing the legs; an umbrella locks the right arm into a carry, a gun into a low carry; aiming brings both up to the gun; a fall drops them
            double armLimb = 0.92 + (p.Build - 1) * 0.5;
            double carry = p.Weapon != HeldWeapon.None ? 1 : 0;
            double walkL = armA * cosP, walkR = -(armA * cosP) * (1 - CarrySwing * carry) - CarryRaise * carry;
            double kick = RecoilKick * recoil;
            // (the raised arms take out the spine's lean, so a crouched fighter's gun is still level over the cover)
            double rxL = Lerp(Lerp(walkL, -(AimRaiseL + pitch + lean) - kick, aim), -FallArms, fall), ryL = AimTurnL * aim * (1 - fall);
            double rxR = Lerp(Lerp(walkR, -(AimRaiseR + pitch + lean) - kick, aim), -FallArms, fall), ryR = AimTurnR * aim * (1 - fall);
            r.ArmL = Link(spine, -ShoulderX * bw, ShoulderY, 0, QYX(ryL, rxL), armLimb, 1, armLimb);
            Mat4 armRJoint;
            if (p.Umbrella)
            {
                armRJoint = Link(spine, ShoulderX * bw, ShoulderY, 0, QXY(-1.12, 0.16));
                r.Umbrella = Link(spine, 0.175, 0.50, 0.16, QXY(0.10 + Math.Sin(t * 0.9 + p.LookSeed) * 0.03, Math.Sin(t * 0.6 + p.LookSeed) * 0.06));
            }
            else
            {
                armRJoint = Link(spine, ShoulderX * bw, ShoulderY, 0, QYX(ryR, rxR));
            }
            r.ArmR = Link(armRJoint, 0, 0, 0, QY(0), armLimb, 1, armLimb);
            if (p.Weapon != HeldWeapon.None) r.Gun = Link(armRJoint, 0, GunY, GunZ, QX(GunTurn));

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

        /// <summary>How bent the knees are: the crouch, plus a buckle through the middle of a fall.</summary>
        static double Bend(double crouch, double death) => Math.Min(1, crouch + Math.Sin(Math.PI * death) * FallBuckle);

        /// <summary>How far the hips come down when the knees bend by <paramref name="bend"/> (thigh forward, shin back) and both feet stay on the ground.</summary>
        static double CrouchDrop(double bend) =>
            LegLength - (ThighLength * Math.Cos(CrouchThigh * bend) + ShinLength * Math.Cos((CrouchThigh - CrouchKnee) * bend));

        /// <summary>
        /// Where a shot can hit a posed figure, without the matrices <see cref="Pose"/> builds (this runs for every fighter every step): the root, hips and spine joints
        /// only, from the same angles, so the volumes sit where <see cref="Pose"/>'s joints do.
        /// </summary>
        public static PoseHitShape HitShape(in PoseInput p)
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
            double aim = Clamp(p.Aim, 0, 1), crouch = Clamp(p.Crouch, 0, 1), death = Clamp(p.Death, 0, 1);
            double recoil = Clamp(p.Recoil, 0, 1), flinch = Clamp(p.Flinch, 0, 1), side = Clamp(p.Side, -1, 1);
            double fall = death * death;
            double bend = Bend(crouch, death);
            double thighA = (0.26 + 0.14 * speed) * gait;
            double stance = Math.Abs(thighA * cosP);
            double idleBreath = Math.Sin(t * 1.15 + p.LookSeed) * 0.008 * (1 - Math.Min(1, gait));
            double hipY = HipY - 0.75 * LegLength * (1 - Math.Cos(stance)) + idleBreath - CrouchDrop(bend);
            double sway = 0.024 * sinP * gait + Math.Sin(t * 0.7 + p.LookSeed) * 0.012 * (1 - Math.Min(1, gait));
            double hipTwist = 0.095 * cosP * gait;
            double spineTwist = -0.21 * cosP * gait * (1 - aim) + FlinchTwist * flinch * side;
            double lean = 0.045 + speed * 0.045 + CrouchLean * bend + AimLean * aim - RecoilLean * recoil - FlinchLean * flinch;

            Frame root = Frame.Root(x, y - 0.02 + FallLift * fall, z, heading + FallTwist * side * fall, -FallAngle * fall, s);
            Frame hips = root.Then(sway, hipY, 0, 0, hipTwist);
            Frame spine = hips.Then(0, 0, 0, lean, spineTwist);
            return new PoseHitShape(root.Point(0, HitLegs, 0), spine.Point(0, HitShoulders, 0), HitBodyRadius * s, spine.Point(0, HeadY, 0), HitHeadRadius * s);
        }

        /// <summary>A joint as a 3x3 and a translation, on the stack: just enough for <see cref="HitShape"/>.</summary>
        readonly struct Frame
        {
            // rows: a b c / d e f / g h i
            readonly double _a, _b, _c, _d, _e, _f, _g, _h, _i, _tx, _ty, _tz;

            Frame(double a, double b, double c, double d, double e, double f, double g, double h, double i, double tx, double ty, double tz)
            {
                _a = a; _b = b; _c = c; _d = d; _e = e; _f = f; _g = g; _h = h; _i = i; _tx = tx; _ty = ty; _tz = tz;
            }

            /// <summary>T(x, y, z) * Ry(yaw) * Rx(tip) * S(scale): the root.</summary>
            public static Frame Root(double x, double y, double z, double yaw, double tip, double scale)
            {
                double sy = Math.Sin(yaw), cy = Math.Cos(yaw), sx = Math.Sin(tip), cx = Math.Cos(tip);
                return new Frame(cy * scale, sy * sx * scale, sy * cx * scale, 0, cx * scale, -sx * scale, -sy * scale, cy * sx * scale, cy * cx * scale, x, y, z);
            }

            /// <summary>this * T(tx, ty, tz) * Rx(rx) * Ry(ry): a child joint as <see cref="Link"/> makes one from QXY.</summary>
            public Frame Then(double tx, double ty, double tz, double rx, double ry)
            {
                double sx = Math.Sin(rx), cx = Math.Cos(rx), sy = Math.Sin(ry), cy = Math.Cos(ry);
                double m00 = cy, m01 = 0, m02 = sy;
                double m10 = sx * sy, m11 = cx, m12 = -sx * cy;
                double m20 = -cx * sy, m21 = sx, m22 = cx * cy;
                V3 o = Point(tx, ty, tz);
                return new Frame(
                    _a * m00 + _b * m10 + _c * m20, _a * m01 + _b * m11 + _c * m21, _a * m02 + _b * m12 + _c * m22,
                    _d * m00 + _e * m10 + _f * m20, _d * m01 + _e * m11 + _f * m21, _d * m02 + _e * m12 + _f * m22,
                    _g * m00 + _h * m10 + _i * m20, _g * m01 + _h * m11 + _i * m21, _g * m02 + _h * m12 + _i * m22,
                    o.X, o.Y, o.Z);
            }

            public V3 Point(double x, double y, double z) => new V3(_a * x + _b * y + _c * z + _tx, _d * x + _e * y + _f * z + _ty, _g * x + _h * y + _i * z + _tz);
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

        /// <summary>
        /// The matrix of a pedestrian drawn as a single static body (the cheaper levels of detail): scaled by build and height, turned to face the way they walk; a
        /// fighter's crouch squashes it and a fall tips it onto its back.
        /// </summary>
        public static Mat4 StaticBody(double x, double y, double z, double heading, double scale, double build, double crouch = 0, double death = 0)
        {
            double fall = Clamp(death, 0, 1) * Clamp(death, 0, 1);
            double[] q = QYX(heading, -FallAngle * fall);
            return Mat4.Compose(x, y + FallLift * fall, z, q[0], q[1], q[2], q[3], scale * build, scale * (1 - StaticCrouch * Clamp(crouch, 0, 1)), scale);
        }

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

        /// <summary>Ry(ry) * Rx(rx) as a quaternion: the tip about X first, then the turn about Y (a raised arm swung across, a body tipped and then turned).</summary>
        static double[] QYX(double ry, double rx)
        {
            double hx = rx * 0.5, hy = ry * 0.5;
            double sx = Math.Sin(hx), cx = Math.Cos(hx), sy = Math.Sin(hy), cy = Math.Cos(hy);
            return new[] { cy * sx, sy * cx, -sy * sx, cy * cx };
        }

        static double[] QY(double ry) => new[] { 0, Math.Sin(ry * 0.5), 0, Math.Cos(ry * 0.5) };

        static double[] QX(double rx) => new[] { Math.Sin(rx * 0.5), 0, 0, Math.Cos(rx * 0.5) };

        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
