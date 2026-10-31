using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Cypher.Core
{
    /// <summary>
    /// One pedestrian as the GPU reads it: 112 bytes in a structured buffer, laid out as the pedestrian shader's <c>PedInstance</c>. Positions, heading and phase are
    /// already interpolated to the frame; colours are sRGB hex (0xRRGGBB) that the shader turns linear. The last two fields are a fighter's pose (zero for everyone else).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PedestrianGpuData
    {
        public const int Size = 112;

        public const uint FlagLongCoat = 1, FlagUmbrella = 2, FlagHood = 4, FlagPack = 8, FlagTrim = 16, FlagVisor = 32;

        // float4 a
        public float X, Y, Z, Heading;

        // float4 b
        public float Phase, Speed, Scale, Build;

        // float4 c
        public float LookYaw, LookSeed, TrimPower, Aim;

        // uint4 colours
        public uint Coat, Skin, Pants, Trim;

        // uint4 extra: the weapon is a HeldWeapon
        public uint Umbrella, Flags, Weapon, Spare0;

        // float4 d
        public float AimPitch, Crouch, Death, Recoil;

        // float4 e
        public float Flinch, Side, Spare1, Spare2;
    }

    /// <summary>A view frustum as six inward-facing planes (a x + b y + c z + d >= 0 is inside), for cheap sphere tests.</summary>
    public sealed class Frustum
    {
        readonly double[] _planes = new double[24];

        /// <summary>Set plane <paramref name="index"/> (0 to 5): its unit normal pointing into the frustum, and its offset.</summary>
        public void SetPlane(int index, double nx, double ny, double nz, double d)
        {
            if (index < 0 || index > 5) throw new ArgumentOutOfRangeException(nameof(index), index, "a frustum has six planes");
            _planes[index * 4] = nx; _planes[index * 4 + 1] = ny; _planes[index * 4 + 2] = nz; _planes[index * 4 + 3] = d;
        }

        /// <summary>Whether a sphere is at least partly inside every plane.</summary>
        public bool IntersectsSphere(double x, double y, double z, double radius)
        {
            for (int i = 0; i < 6; i++)
            {
                double dist = _planes[i * 4] * x + _planes[i * 4 + 1] * y + _planes[i * 4 + 2] * z + _planes[i * 4 + 3];
                if (dist < -radius) return false;
            }
            return true;
        }

        /// <summary>A frustum that contains everything (nothing set yet is nothing to be outside of).</summary>
        public static Frustum Everything()
        {
            var f = new Frustum();
            for (int i = 0; i < 6; i++) f.SetPlane(i, 0, 1, 0, 1e9);
            return f;
        }
    }

    /// <summary>
    /// Turns the simulation's people into one frame's instance data: leaves out whoever is out of view, interpolates between the last two steps, decides each person's
    /// level of detail (with hysteresis, so nobody flickers between bodies on a boundary) and sorts them into three runs, near, middle and far, ready for three draws.
    /// The fighters (<see cref="CombatBody"/>) go into the same runs, so they cost no draws of their own. Pure computation: a Unity component hands the result to the GPU.
    /// </summary>
    public sealed class CrowdFrame
    {
        /// <summary>People whose bounding sphere (this radius, centred 1.5 m up) touches the frustum are drawn.</summary>
        public const double CullRadius = 3;

        /// <summary>A person has to move this far past a distance threshold before their level of detail changes.</summary>
        public const double Hysteresis = 6;

        /// <summary>The head's smoothing: a fifth of the way a frame at 60 frames a second (the prototype's 0.12 a frame), scaled to the real frame time.</summary>
        public const double LookSmoothing = 0.12;

        const byte Unknown = 255;

        PedestrianGpuData[] _data = Array.Empty<PedestrianGpuData>();
        byte[] _lod = Array.Empty<byte>();
        int[][] _byLod = { Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>() };
        readonly int[] _count = new int[3];
        readonly int[] _cursor = new int[3];

        /// <summary>The instances, nearest run first: <see cref="Count"/> says how many of each level of detail, in order. Only the first <see cref="Total"/> entries are meaningful.</summary>
        public PedestrianGpuData[] Data => _data;

        public int Total { get; private set; }

        /// <summary>How many of the visible people are at a level of detail (0 full, 1 simple, 2 silhouette).</summary>
        public int Count(RigLod lod)
        {
            CheckLod(lod);
            return _count[(int)lod];
        }

        /// <summary>Where a level of detail's run starts in <see cref="Data"/>.</summary>
        public int Start(RigLod lod)
        {
            CheckLod(lod);
            int start = 0;
            for (int i = 0; i < (int)lod; i++) start += _count[i];
            return start;
        }

        static void CheckLod(RigLod lod)
        {
            if ((int)lod < 0 || (int)lod > 2) throw new ArgumentOutOfRangeException(nameof(lod), lod, "unknown level of detail");
        }

        /// <param name="alpha">How far between the last two simulation steps this frame falls, 0 to 1.</param>
        /// <param name="time">Seconds, for the head's slow wandering look.</param>
        /// <param name="dt">The frame time, for the head's smoothing.</param>
        /// <param name="near">People closer than this are drawn as the full rig; beyond <paramref name="far"/>, as a box.</param>
        /// <param name="fighters">Optional: the enemies' bodies, drawn with the crowd.</param>
        public void Build(CrowdSim sim, Frustum view, double camX, double camZ, double alpha, double time, double dt, double near, double far,
            IReadOnlyList<CombatBody> fighters = null)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (!(alpha >= 0 && alpha <= 1)) throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "the interpolation runs from 0 to 1");
            if (!(near > 0) || !(far > near)) throw new ArgumentOutOfRangeException(nameof(far), far, "the detail distances must run near < far");

            int pool = sim.Pool.Count;
            int need = pool + (fighters?.Count ?? 0);
            if (_data.Length < need)
            {
                _data = new PedestrianGpuData[need];
                for (int i = 0; i < 3; i++) _byLod[i] = new int[need];
            }
            if (_lod.Length < pool)
            {
                int before = _lod.Length;
                Array.Resize(ref _lod, pool);
                for (int i = before; i < pool; i++) _lod[i] = Unknown;
            }

            _count[0] = _count[1] = _count[2] = 0;
            var active = sim.Active;
            double lookK = 1 - Math.Pow(1 - LookSmoothing, Math.Max(0, dt) * 60);
            for (int i = 0; i < active.Count; i++)
            {
                Pedestrian p = active[i];
                double x = p.PrevX + (p.X - p.PrevX) * alpha;
                double z = p.PrevZ + (p.Z - p.PrevZ) * alpha;
                double y = p.PrevY + (p.Y - p.PrevY) * alpha;
                if (!view.IntersectsSphere(x, y + CullRadius * 0.5, z, CullRadius)) continue;

                double dx = x - camX, dz = z - camZ;
                // someone seen for the first time has no previous level to be loyal to
                int previous = _lod[p.Index];
                int lod = previous == Unknown ? DistanceLod(Math.Sqrt(dx * dx + dz * dz), near, far, 1, 0) : DistanceLod(Math.Sqrt(dx * dx + dz * dz), near, far, previous, Hysteresis);
                _lod[p.Index] = (byte)lod;
                _byLod[lod][_count[lod]++] = i;

                if (lod == 0)
                {
                    double heading = p.PrevHeading + CrowdSim.ShortestAngle(p.PrevHeading, p.Heading) * alpha;
                    double target = PedestrianPose.LookTarget(heading, x, z, true, camX, camZ, time, p.LookSeed);
                    p.LookYaw += (target - p.LookYaw) * lookK;
                }
            }

            // the fighters, kept apart in the runs by a complemented index
            int fighterCount = fighters?.Count ?? 0;
            for (int i = 0; i < fighterCount; i++)
            {
                CombatBody b = fighters[i];
                double x = b.PrevX + (b.X - b.PrevX) * alpha;
                double z = b.PrevZ + (b.Z - b.PrevZ) * alpha;
                double y = b.PrevY + (b.Y - b.PrevY) * alpha;
                if (!view.IntersectsSphere(x, y + CullRadius * 0.5, z, CullRadius)) continue;

                double dx = x - camX, dz = z - camZ;
                int lod = b.Lod == Unknown ? DistanceLod(Math.Sqrt(dx * dx + dz * dz), near, far, 1, 0) : DistanceLod(Math.Sqrt(dx * dx + dz * dz), near, far, b.Lod, Hysteresis);
                b.Lod = (byte)lod;
                _byLod[lod][_count[lod]++] = ~i;

                if (lod == 0)
                {
                    double heading = b.PrevHeading + CrowdSim.ShortestAngle(b.PrevHeading, b.Heading) * alpha;
                    double target = PedestrianPose.LookTarget(heading, x, z, true, camX, camZ, time, b.LookSeed);
                    b.LookYaw += (target - b.LookYaw) * lookK;
                }
            }

            _cursor[0] = 0;
            _cursor[1] = _count[0];
            _cursor[2] = _count[0] + _count[1];
            Total = _count[0] + _count[1] + _count[2];
            for (int lod = 0; lod < 3; lod++)
            {
                for (int k = 0; k < _count[lod]; k++)
                {
                    int who = _byLod[lod][k];
                    if (who >= 0) Fill(ref _data[_cursor[lod]++], active[who], alpha);
                    else Fill(ref _data[_cursor[lod]++], fighters[~who], alpha);
                }
            }
        }

        static void Fill(ref PedestrianGpuData d, Pedestrian p, double alpha)
        {
            PedestrianAppearance a = p.Appearance;
            d.X = (float)(p.PrevX + (p.X - p.PrevX) * alpha);
            d.Y = (float)(p.PrevY + (p.Y - p.PrevY) * alpha);
            d.Z = (float)(p.PrevZ + (p.Z - p.PrevZ) * alpha);
            d.Heading = (float)(p.PrevHeading + CrowdSim.ShortestAngle(p.PrevHeading, p.Heading) * alpha);
            d.Phase = (float)(p.PrevPhase + (p.Phase - p.PrevPhase) * alpha);
            d.Speed = (float)p.Speed;
            d.Scale = (float)a.Scale;
            d.Build = (float)a.Build;
            d.LookYaw = (float)p.LookYaw;
            d.LookSeed = (float)p.LookSeed;
            d.TrimPower = (float)a.TrimPower;
            d.Coat = a.Coat; d.Skin = a.Skin; d.Pants = a.Pants; d.Trim = a.TrimColour;
            d.Umbrella = a.UmbrellaColour;
            d.Flags = Flags(a, false);
            d.Aim = 0; d.Weapon = 0;
            d.AimPitch = d.Crouch = d.Death = d.Recoil = d.Flinch = d.Side = 0;
            d.Spare0 = 0; d.Spare1 = d.Spare2 = 0;
        }

        static void Fill(ref PedestrianGpuData d, CombatBody b, double alpha)
        {
            PedestrianAppearance a = b.Appearance;
            d.X = (float)(b.PrevX + (b.X - b.PrevX) * alpha);
            d.Y = (float)(b.PrevY + (b.Y - b.PrevY) * alpha);
            d.Z = (float)(b.PrevZ + (b.Z - b.PrevZ) * alpha);
            d.Heading = (float)(b.PrevHeading + CrowdSim.ShortestAngle(b.PrevHeading, b.Heading) * alpha);
            d.Phase = (float)(b.PrevPhase + (b.Phase - b.PrevPhase) * alpha);
            d.Speed = (float)b.Speed;
            d.Scale = (float)a.Scale;
            d.Build = (float)a.Build;
            d.LookYaw = (float)b.LookYaw;
            d.LookSeed = (float)b.LookSeed;
            d.TrimPower = (float)a.TrimPower;
            d.Coat = a.Coat; d.Skin = a.Skin; d.Pants = a.Pants; d.Trim = a.TrimColour;
            d.Umbrella = a.UmbrellaColour;
            d.Flags = Flags(a, b.Visor);
            d.Aim = (float)b.Aim; d.Weapon = (uint)b.Weapon;
            d.AimPitch = (float)b.AimPitch; d.Crouch = (float)b.Crouch; d.Death = (float)b.Death; d.Recoil = (float)b.Recoil;
            d.Flinch = (float)b.Flinch; d.Side = (float)b.Side;
            d.Spare0 = 0; d.Spare1 = d.Spare2 = 0;
        }

        static uint Flags(PedestrianAppearance a, bool visor) =>
            (a.LongCoat ? PedestrianGpuData.FlagLongCoat : 0u) | (a.Umbrella ? PedestrianGpuData.FlagUmbrella : 0u) | (a.Hood ? PedestrianGpuData.FlagHood : 0u)
            | (a.Pack ? PedestrianGpuData.FlagPack : 0u) | (a.Trim ? PedestrianGpuData.FlagTrim : 0u) | (visor ? PedestrianGpuData.FlagVisor : 0u);

        /// <summary>The level of detail for a distance (the prototype's distanceLod): 0 near, 1 middle, 2 far, with hysteresis about both thresholds.</summary>
        public static int DistanceLod(double distance, double near, double far, int previous, double hysteresis)
        {
            if (previous == 0 && distance <= near + hysteresis) return 0;
            if (previous == 2 && distance >= far - hysteresis) return 2;
            if (distance < near - (previous > 0 ? hysteresis : 0)) return 0;
            return distance > far + (previous < 2 ? hysteresis : 0) ? 2 : 1;
        }
    }
}
