#ifndef CYPHER_VEHICLE_POSE_INCLUDED
#define CYPHER_VEHICLE_POSE_INCLUDED

// Where each part of a vehicle goes, computed on the GPU: a line-for-line port of Cypher.Core.VehiclePlacement (itself a port of the placement in Traffic.lateUpdate in the prototype).
// A vertex is in its part's own frame (a hull about the middle of the vehicle, a wheel as a unit cylinder, a lamp rig in the vehicle's unit box); the vehicle's record gives its
// position, heading, spin and type, and the type table below gives the sizes. Pure maths, no render pipeline includes, so the vehicle shaders and the placement test's compute shader
// share it. Rotations are the right-handed ones the Core uses (Rx tips +Y toward +Z, Ry swings +Z toward +X, Rz swings +X toward +Y).

// What a draw's _Kind means for the vehicle body shader.
#define VEH_KIND_BODY 0       // a ground vehicle's hull, one instance per vehicle
#define VEH_KIND_WHEEL 1      // four wheel slots for each vehicle: instance / 4 is the vehicle, instance % 4 the wheel
#define VEH_KIND_AIRCRAFT 2   // an aircraft's hull
#define VEH_KIND_WORLD 3      // not an instance at all: the mesh is already in the world (the signal masts)

// The ground glow's kinds of geometry (Uv1.x): the same numbers as Cypher.Core.GlowPart (a test reads this file and compares them).
#define VEH_GLOW_LAMPS 0
#define VEH_GLOW_UNDERGLOW 1
#define VEH_GLOW_BEAMS 2

// One vehicle or aircraft, 48 bytes: the same layout as Cypher.Core.VehicleGpuData.
struct VehicleInstance
{
    float4 a;        // x, y, z, heading
    float4 b;        // wheel spin (an aircraft's bank), glow phase, lamp tint, type
    uint4 colours;   // paint, under-glow as 0xRRGGBB (sRGB), spare, spare
};

// The vehicle types in the order of Cypher.Core.VehicleCatalog.Ground: sedan, van, truck, bike. Width, height, length; and the wheels.
static const float3 VEH_SIZE[4] =
{
    float3(1.94, 1.46, 4.62),
    float3(2.16, 2.42, 5.42),
    float3(2.48, 3.36, 8.6),
    float3(0.8, 1.56, 2.26),
};

// Wheel radius, wheel width, how far the wheels sit from the middle sideways, and how many wheels (the rest of the four slots are hidden).
static const float4 VEH_WHEEL[4] =
{
    float4(0.34, 0.22, 0.86, 4),
    float4(0.38, 0.24, 0.94, 4),
    float4(0.46, 0.30, 1.06, 4),
    float4(0.32, 0.11, 0.0, 2),
};

// Where the front and the rear wheels sit along the vehicle.
static const float2 VEH_WHEEL_Z[4] =
{
    float2(1.42, -1.46),
    float2(1.74, -1.70),
    float2(3.05, -2.55),
    float2(0.88, -0.88),
};

// The aircraft types in the order of Cypher.Core.VehicleCatalog.Aircraft: spinner, freighter. Width, height, length.
static const float3 AIR_SIZE[2] =
{
    float3(2.7, 1.66, 6.4),
    float3(4.3, 3.1, 12.4),
};

static const float VEH_PULSE_RATE = 1.7;
static const float VEH_PULSE_PHASE = 9.0;

float3 VehRotX(float3 v, float a)
{
    float s, c;
    sincos(a, s, c);
    return float3(v.x, c * v.y - s * v.z, s * v.y + c * v.z);
}

float3 VehRotY(float3 v, float a)
{
    float s, c;
    sincos(a, s, c);
    return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
}

float3 VehRotZ(float3 v, float a)
{
    float s, c;
    sincos(a, s, c);
    return float3(c * v.x - s * v.y, s * v.x + c * v.y, v.z);
}

uint VehType(VehicleInstance v)
{
    return (uint)(v.b.w + 0.5);
}

// A ground vehicle's hull.
float3 VehBodyWorld(float3 p, VehicleInstance v)
{
    return VehRotY(p, v.a.w) + v.a.xyz;
}

// An aircraft's hull: banked about its nose, then turned.
float3 AirBodyWorld(float3 p, VehicleInstance v)
{
    return VehRotY(VehRotZ(p, v.b.x), v.a.w) + v.a.xyz;
}

// Wheel slot k of a vehicle. present is false for the slots a two-wheeled bike does not use.
float3 VehWheelWorld(float3 p, VehicleInstance v, uint k, out bool present)
{
    uint type = VehType(v);
    float4 wheel = VEH_WHEEL[type];
    float2 zs = VEH_WHEEL_Z[type];
    present = (float)k < wheel.w;
    bool bike = wheel.w < 2.5;
    float side = bike ? 0.0 : (((k & 1u) == 0u) ? -1.0 : 1.0);
    float wz = bike ? (k == 0u ? zs.x : zs.y) : (k < 2u ? zs.x : zs.y);
    float wx = side * wheel.z;
    float heading = v.a.w;
    float2 f = float2(sin(heading), cos(heading));
    float3 local = float3(p.x * wheel.y, p.y * wheel.x, p.z * wheel.x);   // scaled to the wheel's width and radius
    float3 centre = float3(v.a.x + f.x * wz + f.y * wx, v.a.y + wheel.x, v.a.z + f.y * wz - f.x * wx);
    return VehRotY(VehRotX(local, v.b.x), heading) + centre;
}

// The ground vehicle's glowing geometry: lamps in its unit box, the under-glow pool on the road, the headlight cones off the nose.
float3 VehGlowWorld(float3 p, uint part, VehicleInstance v, float time)
{
    float3 size = VEH_SIZE[VehType(v)];
    float heading = v.a.w;
    if (part == VEH_GLOW_UNDERGLOW)
    {
        float pulse = 0.75 + sin(time * VEH_PULSE_RATE + v.b.y * VEH_PULSE_PHASE) * 0.25;
        return VehRotY(p * float3(size.x * pulse, 1.0, size.z * pulse), heading) + float3(v.a.x, v.a.y + 0.03, v.a.z);
    }
    if (part == VEH_GLOW_BEAMS)
    {
        float nose = size.z * 0.5 - 0.15;
        float2 f = float2(sin(heading), cos(heading));
        return VehRotY(p * float3(size.x / 1.94, 1.0, 1.0), heading) + float3(v.a.x + f.x * nose, v.a.y + size.y * 0.36, v.a.z + f.y * nose);
    }
    return VehRotY(p * size, heading) + v.a.xyz;
}

// The aircraft's glowing geometry, scaled into its box.
float3 AirGlowWorld(float3 p, VehicleInstance v)
{
    float3 size = AIR_SIZE[VehType(v)];
    return VehRotY(VehRotZ(p * size, v.b.x), v.a.w) + v.a.xyz;
}

// The soft shadow under a ground vehicle.
float3 VehShadowWorld(float3 p, VehicleInstance v)
{
    float3 size = VEH_SIZE[VehType(v)];
    return VehRotY(p * float3(size.x * 0.85, 1.0, size.z * 0.62), v.a.w) + float3(v.a.x, v.a.y + 0.02, v.a.z);
}

#endif
