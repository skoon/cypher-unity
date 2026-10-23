#ifndef CYPHER_PEDESTRIAN_POSE_INCLUDED
#define CYPHER_PEDESTRIAN_POSE_INCLUDED

// The pedestrian rig posed on the GPU: a line-for-line port of Cypher.Core.PedestrianPose (itself a port of Crowd._pose in the prototype).
// A vertex carries the id of its body part and its position in that part's joint frame; this file turns the pair into a world position from
// the person's gait. Pure maths, no render pipeline includes, so the pedestrian shader and the pose test's compute shader share it.
//
// Matrices are 3x3 plus a translation. Rotations are the right-handed ones the Core uses (Rx tips +Y toward +Z, Ry swings +Z toward +X), applied
// to column vectors with mul(M, v); a joint is parent * T(t) * R * S.

// Part ids: the same numbers as Cypher.Core.RigPart (a test reads this file and compares them).
#define PED_PART_STATIC 0
#define PED_PART_TORSO_SHORT 1
#define PED_PART_TORSO_LONG 2
#define PED_PART_TRIM 3
#define PED_PART_HEAD 4
#define PED_PART_HOOD 5
#define PED_PART_ARM_L 6
#define PED_PART_ARM_R 7
#define PED_PART_UMBRELLA 8
#define PED_PART_PACK 9
#define PED_PART_THIGH_L 10
#define PED_PART_SHIN_L 11
#define PED_PART_THIGH_R 12
#define PED_PART_SHIN_R 13

// Flag bits: the same as Cypher.Core.PedestrianGpuData.Flag*.
#define PED_FLAG_LONG_COAT 1u
#define PED_FLAG_UMBRELLA 2u
#define PED_FLAG_HOOD 4u
#define PED_FLAG_PACK 8u
#define PED_FLAG_TRIM 16u

// One pedestrian, 80 bytes: the same layout as Cypher.Core.PedestrianGpuData.
struct PedInstance
{
    float4 a;        // x, y, z, heading
    float4 b;        // phase, speed, scale, build
    float4 c;        // look yaw, look seed, trim power, spare
    uint4 colours;   // coat, skin, pants, trim as 0xRRGGBB (sRGB)
    uint4 extra;     // umbrella colour, flags, spare, spare
};

// The rig's proportions, metres for a 1.0-scale figure.
static const float PED_HIP_Y = 0.89;
static const float PED_THIGH_L = 0.45;
static const float PED_LEG_L = 0.89;
static const float PED_SHOULDER_Y = 0.56;
static const float PED_SHOULDER_X = 0.185;
static const float PED_HIP_X = 0.098;
static const float PED_HEAD_Y = 0.70;

struct PedJoint
{
    float3x3 A;
    float3 T;
};

float3x3 PedRotX(float angle)
{
    float s, c;
    sincos(angle, s, c);
    return float3x3(1, 0, 0,
                    0, c, -s,
                    0, s, c);
}

float3x3 PedRotY(float angle)
{
    float s, c;
    sincos(angle, s, c);
    return float3x3(c, 0, s,
                    0, 1, 0,
                    -s, 0, c);
}

// Rx(rx) * Ry(ry): the turn about Y first, then the tip about X.
float3x3 PedRotXY(float rx, float ry)
{
    return mul(PedRotX(rx), PedRotY(ry));
}

// parent * T(t) * R * S: the scale is applied first, in the joint's own axes.
PedJoint PedLink(PedJoint parent, float3 t, float3x3 r, float3 s)
{
    PedJoint j;
    j.A = mul(parent.A, float3x3(r[0] * s, r[1] * s, r[2] * s));
    j.T = mul(parent.A, t) + parent.T;
    return j;
}

static const float3x3 PED_IDENTITY = float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);

// Whether this person wears a part: only one of the two coats, and the accessories only if they have them.
bool PedWorn(uint part, uint flags)
{
    if (part == PED_PART_TORSO_SHORT) return (flags & PED_FLAG_LONG_COAT) == 0u;
    if (part == PED_PART_TORSO_LONG) return (flags & PED_FLAG_LONG_COAT) != 0u;
    if (part == PED_PART_TRIM) return (flags & PED_FLAG_TRIM) != 0u;
    if (part == PED_PART_HOOD) return (flags & PED_FLAG_HOOD) != 0u;
    if (part == PED_PART_UMBRELLA) return (flags & PED_FLAG_UMBRELLA) != 0u;
    if (part == PED_PART_PACK) return (flags & PED_FLAG_PACK) != 0u;
    return true;
}

// The world position of a vertex given in its part's joint frame. time is seconds, for the idle sway; the head's turn arrives in the instance.
float3 PedPoseVertex(uint part, float3 local, PedInstance p, float time)
{
    float heading = p.a.w;
    float phase = p.b.x, speed = p.b.y, s = p.b.z, bw = p.b.w;
    float lookYaw = p.c.x, seed = p.c.y;
    uint flags = p.extra.y;

    PedJoint origin;
    origin.A = PED_IDENTITY;
    origin.T = float3(0, 0, 0);

    // the cheap levels of detail are one rigid body: scaled by build and height, turned to face the way the person walks
    if (part == PED_PART_STATIC)
    {
        PedJoint body = PedLink(origin, p.a.xyz, PedRotY(heading), float3(s * bw, s, s));
        return mul(body.A, local) + body.T;
    }

    float gait = clamp(speed / 1.15, 0.0, 1.35);
    float sinP, cosP;
    sincos(phase, sinP, cosP);

    // gait
    float thighA = (0.26 + 0.14 * speed) * gait;
    float kneeA = (0.62 + 0.26 * speed) * gait;
    float armA = (0.16 + 0.15 * speed) * gait;
    float thighL = thighA * cosP, thighR = -thighA * cosP;
    float kneeL = kneeA * max(0.0, -sinP), kneeR = kneeA * max(0.0, sinP);

    // hips drop as the stance leg extends so the planted foot stays planted
    float stance = max(abs(thighL), abs(thighR));
    float idleBreath = sin(time * 1.15 + seed) * 0.008 * (1.0 - min(1.0, gait));
    float hipY = PED_HIP_Y - 0.75 * PED_LEG_L * (1.0 - cos(stance)) + idleBreath;
    float sway = 0.024 * sinP * gait + sin(time * 0.7 + seed) * 0.012 * (1.0 - min(1.0, gait));

    float hipTwist = 0.095 * cosP * gait;
    float spineTwist = -0.21 * cosP * gait;
    float lean = 0.045 + speed * 0.045;

    float bd = 0.94 + (bw - 1.0) * 0.6;
    float armLimb = 0.92 + (bw - 1.0) * 0.5;
    float legW = 0.94 + (bw - 1.0) * 0.55;

    PedJoint root = PedLink(origin, float3(p.a.x, p.a.y - 0.02, p.a.z), PedRotY(heading), float3(s, s, s));
    PedJoint hips = PedLink(root, float3(sway, hipY, 0), PedRotY(hipTwist), float3(1, 1, 1));
    PedJoint spine = PedLink(hips, float3(0, 0, 0), PedRotXY(lean, spineTwist), float3(1, 1, 1));

    PedJoint j;
    if (part == PED_PART_TORSO_SHORT || part == PED_PART_TORSO_LONG)
    {
        j = PedLink(spine, float3(0, 0, 0), PED_IDENTITY, float3(bw, 1, bd));
    }
    else if (part == PED_PART_TRIM)
    {
        j = PedLink(spine, float3(0, 0, 0), PED_IDENTITY, float3(bw * 1.02, 1, bd * 1.02));
    }
    else if (part == PED_PART_HEAD || part == PED_PART_HOOD)
    {
        // the head cancels the shoulder counter-rotation and adds the turn the CPU worked out
        PedJoint head = PedLink(spine, float3(0, PED_HEAD_Y, 0), PedRotXY(-lean * 0.85 + sin(phase * 2.0) * 0.018 * gait, -spineTwist * 0.55 + lookYaw), float3(1, 1, 1));
        if (part == PED_PART_HEAD) j = head;
        else j = PedLink(head, float3(0, 0.012, 0), PED_IDENTITY, float3(1.02, 1.02, 1.02));
    }
    else if (part == PED_PART_ARM_L)
    {
        j = PedLink(spine, float3(-PED_SHOULDER_X * bw, PED_SHOULDER_Y, 0), PedRotX(armA * cosP), float3(armLimb, 1, armLimb));
    }
    else if (part == PED_PART_ARM_R)
    {
        // an umbrella locks the right arm into a carry
        float3x3 swing = (flags & PED_FLAG_UMBRELLA) != 0u ? PedRotXY(-1.12, 0.16) : PedRotX(-(armA * cosP));
        j = PedLink(spine, float3(PED_SHOULDER_X * bw, PED_SHOULDER_Y, 0), swing, float3(armLimb, 1, armLimb));
    }
    else if (part == PED_PART_UMBRELLA)
    {
        j = PedLink(spine, float3(0.175, 0.50, 0.16), PedRotXY(0.10 + sin(time * 0.9 + seed) * 0.03, sin(time * 0.6 + seed) * 0.06), float3(1, 1, 1));
    }
    else if (part == PED_PART_PACK)
    {
        j = PedLink(spine, float3(0, 0.30, -0.155), PedRotX(0.06), float3(bw, 1, 1));
    }
    else
    {
        // legs: the thigh from the hip, and the shin hanging off the knee
        bool left = part == PED_PART_THIGH_L || part == PED_PART_SHIN_L;
        float side = left ? -1.0 : 1.0;
        float swing = left ? thighL : thighR;
        float knee = left ? kneeL : kneeR;
        PedJoint thigh = PedLink(hips, float3(side * PED_HIP_X * bw, 0, 0), PedRotX(-swing), float3(1, 1, 1));
        if (part == PED_PART_THIGH_L || part == PED_PART_THIGH_R)
        {
            j = PedLink(thigh, float3(0, 0, 0), PED_IDENTITY, float3(legW, 1, legW));
        }
        else
        {
            PedJoint kneeJoint = PedLink(thigh, float3(0, -PED_THIGH_L, 0), PedRotX(knee), float3(1, 1, 1));
            j = PedLink(kneeJoint, float3(0, 0, 0), PED_IDENTITY, float3(legW, 1, legW));
        }
    }
    return mul(j.A, local) + j.T;
}

#endif
