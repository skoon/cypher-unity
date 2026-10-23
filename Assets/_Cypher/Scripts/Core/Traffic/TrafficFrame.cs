using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Cypher.Core
{
    /// <summary>
    /// One vehicle or aircraft as the GPU reads it: 48 bytes in a structured buffer, laid out as the vehicle shader's <c>VehicleInstance</c>. Position and heading are already
    /// interpolated to the frame; paint and under-glow colours are sRGB hex (0xRRGGBB) that the shader turns linear.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VehicleGpuData
    {
        public const int Size = 48;

        // float4 a
        public float X, Y, Z, Heading;

        // float4 b: the wheels' spin for a vehicle, the bank for an aircraft
        public float Spin, Glow, LampTint, Type;

        // uint4 colours
        public uint Paint, Neon, Spare0, Spare1;
    }

    /// <summary>
    /// Turns the traffic into one frame's instance data: leaves out whatever is out of view, interpolates between the last two steps, and sorts what remains into a run for each type,
    /// ready for a draw each. Also picks the vehicles nearest the camera, which carry the point lights. Pure computation: a Unity component hands the result to the GPU.
    /// </summary>
    public sealed class TrafficFrame
    {
        /// <summary>How far from the camera a vehicle may be and still carry a point light.</summary>
        public const double LightRange = 70;

        readonly VehicleGpuData[] _ground = new VehicleGpuData[QualityTiers.MaxGroundVehicles];
        readonly VehicleGpuData[] _air = new VehicleGpuData[QualityTiers.MaxAircraft];
        readonly int[] _groundCount = new int[4], _airCount = new int[2];
        readonly List<(double d2, double x, double y, double z)> _lights = new List<(double, double, double, double)>();
        readonly List<Vehicle> _visibleVehicles = new List<Vehicle>(QualityTiers.MaxGroundVehicles);
        readonly List<Aircraft> _visibleAircraft = new List<Aircraft>(QualityTiers.MaxAircraft);
        readonly int[] _groundCursor = new int[4], _airCursor = new int[2];

        /// <summary>The ground vehicles in view, a run for each type in order: <see cref="GroundCount"/> says how long each is. Only the first <see cref="GroundTotal"/> entries are meaningful.</summary>
        public VehicleGpuData[] Ground => _ground;

        public VehicleGpuData[] Aircraft => _air;

        public int GroundTotal { get; private set; }

        public int AircraftTotal { get; private set; }

        public int GroundCount(int type)
        {
            if (type < 0 || type >= _groundCount.Length) throw new ArgumentOutOfRangeException(nameof(type), type, "there are four ground types");
            return _groundCount[type];
        }

        public int GroundStart(int type)
        {
            if (type < 0 || type >= _groundCount.Length) throw new ArgumentOutOfRangeException(nameof(type), type, "there are four ground types");
            int start = 0;
            for (int i = 0; i < type; i++) start += _groundCount[i];
            return start;
        }

        public int AircraftCount(int type)
        {
            if (type < 0 || type >= _airCount.Length) throw new ArgumentOutOfRangeException(nameof(type), type, "there are two aircraft types");
            return _airCount[type];
        }

        public int AircraftStart(int type)
        {
            if (type < 0 || type >= _airCount.Length) throw new ArgumentOutOfRangeException(nameof(type), type, "there are two aircraft types");
            int start = 0;
            for (int i = 0; i < type; i++) start += _airCount[i];
            return start;
        }

        /// <summary>The places for point lights this frame, nearest the camera first: the nose of each of the nearest vehicles in view.</summary>
        public IReadOnlyList<(double d2, double x, double y, double z)> LightSpots => _lights;

        /// <param name="alpha">How far between the last two simulation steps this frame falls, 0 to 1.</param>
        public void Build(TrafficSim sim, Frustum view, double camX, double camZ, double alpha)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (!(alpha >= 0 && alpha <= 1)) throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "the interpolation runs from 0 to 1");

            Array.Clear(_groundCount, 0, _groundCount.Length);
            Array.Clear(_airCount, 0, _airCount.Length);
            _lights.Clear();

            // count first, so each type's run can be filled in place in one pass
            IReadOnlyList<Vehicle> vehicles = sim.Vehicles;
            List<Vehicle> visible = _visibleVehicles;
            visible.Clear();
            foreach (Vehicle v in vehicles)
            {
                if (!v.Active) continue;
                double radius = Math.Max(v.Type.Length, v.Type.Width);
                (double x, double z, _) = VehiclePlacement.Interpolate(v, alpha);
                if (!view.IntersectsSphere(x, v.Y + radius * 0.5, z, radius)) continue;
                visible.Add(v);
                _groundCount[v.TypeIndex]++;
            }
            int[] cursor = _groundCursor;
            cursor[0] = 0;
            for (int type = 1; type < 4; type++) cursor[type] = cursor[type - 1] + _groundCount[type - 1];
            GroundTotal = visible.Count;
            foreach (Vehicle v in visible)
            {
                FillVehicle(ref _ground[cursor[v.TypeIndex]++], v, alpha);
                (double x, double z, double heading) = VehiclePlacement.Interpolate(v, alpha);
                double dx = x - camX, dz = z - camZ, d2 = dx * dx + dz * dz;
                if (d2 < LightRange * LightRange)
                {
                    double nose = v.HalfLength;
                    _lights.Add((d2, x + Math.Sin(heading) * nose, v.Y + v.Type.Height * 0.34, z + Math.Cos(heading) * nose));
                }
            }
            _lights.Sort((a, b) => a.d2.CompareTo(b.d2));

            IReadOnlyList<Aircraft> flying = sim.Airborne;
            List<Aircraft> seen = _visibleAircraft;
            seen.Clear();
            foreach (Aircraft a in flying)
            {
                double radius = Math.Max(a.Type.Length, a.Type.Width);
                double x = a.PrevX + (a.X - a.PrevX) * alpha, z = a.PrevZ + (a.Z - a.PrevZ) * alpha;
                double y = a.PrevY + (a.Y - a.PrevY) * alpha + Math.Sin(a.Bob) * 0.8;
                if (!view.IntersectsSphere(x, y + radius * 0.5, z, radius)) continue;
                seen.Add(a);
                _airCount[a.TypeIndex]++;
            }
            int[] airCursor = _airCursor;
            airCursor[0] = 0;
            airCursor[1] = _airCount[0];
            AircraftTotal = seen.Count;
            foreach (Aircraft a in seen) FillAircraft(ref _air[airCursor[a.TypeIndex]++], a, alpha);
        }

        static void FillVehicle(ref VehicleGpuData d, Vehicle v, double alpha)
        {
            (double x, double z, double heading) = VehiclePlacement.Interpolate(v, alpha);
            d.X = (float)x; d.Y = (float)v.Y; d.Z = (float)z; d.Heading = (float)heading;
            d.Spin = (float)v.Spin;
            d.Glow = (float)v.Glow;
            d.LampTint = (float)v.LampTint;
            d.Type = v.TypeIndex;
            d.Paint = VehicleCatalog.Paint[v.PaintIndex];
            d.Neon = VehicleCatalog.Neon[v.NeonIndex];
            d.Spare0 = d.Spare1 = 0;
        }

        static void FillAircraft(ref VehicleGpuData d, Aircraft a, double alpha)
        {
            d.X = (float)(a.PrevX + (a.X - a.PrevX) * alpha);
            d.Y = (float)(a.PrevY + (a.Y - a.PrevY) * alpha + Math.Sin(a.Bob) * 0.8);
            d.Z = (float)(a.PrevZ + (a.Z - a.PrevZ) * alpha);
            d.Heading = (float)a.Heading;
            d.Spin = (float)a.Bank;
            d.Glow = 0;
            d.LampTint = 1;
            d.Type = a.TypeIndex;
            d.Paint = VehicleCatalog.Paint[a.PaintIndex];
            d.Neon = 0;
            d.Spare0 = d.Spare1 = 0;
        }
    }
}
