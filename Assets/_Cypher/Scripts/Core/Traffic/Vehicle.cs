using System;

namespace Cypher.Core
{
    /// <summary>What a vehicle will do at the junction ahead. The values are the prototype's; its frame is right-handed, with traffic keeping to the right.</summary>
    public enum Manoeuvre
    {
        Straight = 0,

        /// <summary>A turn that stays on its own side of the road.</summary>
        Turn = 1,

        /// <summary>The turn that crosses the oncoming lane, and so gives way to oncoming traffic.</summary>
        CrossingTurn = -1,

        /// <summary>Cornered at the edge of the grid: turn round through the junction.</summary>
        UTurn = 2,
    }

    /// <summary>One ground vehicle. The simulation owns the state; the renderer reads it.</summary>
    public sealed class Vehicle
    {
        /// <summary>Its place in the whole fleet, and in its own type's run.</summary>
        public int Index, Slot;

        public int TypeIndex;
        public GroundVehicleType Type;

        public double X, Z, Y, Heading;

        /// <summary>Where it was a step ago, for interpolating between steps.</summary>
        public double PrevX, PrevZ, PrevHeading;

        public double Speed;

        /// <summary>The speed it wants when the road is clear.</summary>
        public double Cruise;

        /// <summary>The junction it is heading for, as indices into the road lines.</summary>
        public int Ix, Iz;

        /// <summary>The way it is travelling now, and the way it will leave the junction: unit steps along X or Z.</summary>
        public int Dx, Dz, Ndx, Ndz;

        /// <summary>0 while approaching the junction, 1 while crossing it.</summary>
        public int Leg;

        public Manoeuvre Turn;

        /// <summary>The point it is steering for.</summary>
        public double WpX, WpZ;

        /// <summary>Wheel rotation, radians.</summary>
        public double Spin;

        /// <summary>A phase for the under-glow's pulse, 0 to 1.</summary>
        public double Glow;

        /// <summary>A tint on the lamps, 0.9 to 1.15.</summary>
        public double LampTint;

        /// <summary>Which of <see cref="VehicleCatalog.Paint"/> and <see cref="VehicleCatalog.Neon"/> it wears.</summary>
        public int PaintIndex, NeonIndex;

        /// <summary>False while it has no place on the road (it waits to be put back).</summary>
        public bool Active;

        /// <summary>Metres from the front to the back, and across.</summary>
        public double HalfLength => Type.Length * 0.5;

        public double HalfWidth => Type.Width * 0.5;
    }

    /// <summary>One aircraft, flying a straight line through the canyons or over the roofs.</summary>
    public sealed class Aircraft
    {
        public int Index, Slot, TypeIndex;
        public AircraftType Type;

        public double X, Y, Z, Heading;
        public double PrevX, PrevY, PrevZ, PrevHeading;
        public double Speed;

        /// <summary>The unit direction it flies in.</summary>
        public double DirX, DirZ;

        /// <summary>A phase for the gentle bobbing and banking.</summary>
        public double Bob;

        public double Bank;

        public int PaintIndex;
    }

    /// <summary>The signals' cycle: which axis holds green, then amber, then the other axis, then its amber. The same at every junction.</summary>
    public sealed class SignalClock
    {
        // axis 0 = traffic travelling along X; 1 = along Z
        static readonly int[] Axes = { 0, 0, 1, 1 };
        static readonly bool[] Ambers = { false, true, false, true };
        static readonly double[] Durations = { 12.5, 2.6, 12.5, 2.6 };

        /// <summary>0 to 3: green for X, amber for X, green for Z, amber for Z.</summary>
        public int Phase { get; private set; }

        /// <summary>Seconds into the phase.</summary>
        public double PhaseTime { get; private set; }

        /// <summary>The axis (0 for X, 1 for Z) that holds green or amber.</summary>
        public int GreenAxis => Axes[Phase];

        public bool Amber => Ambers[Phase];

        public static double Duration(int phase) => Durations[phase];

        /// <summary>Seconds until traffic along <paramref name="axis"/> (0 for X, 1 for Z) next has green: 0 while it has green or amber.</summary>
        public double TimeUntilGreen(int axis)
        {
            if (axis != 0 && axis != 1) throw new ArgumentOutOfRangeException(nameof(axis), axis, "the axis is 0 (along X) or 1 (along Z)");
            if (Axes[Phase] == axis) return 0;
            double wait = Durations[Phase] - PhaseTime;
            for (int p = (Phase + 1) % Axes.Length; Axes[p] != axis; p = (p + 1) % Axes.Length) wait += Durations[p];
            return wait;
        }

        public static double CycleLength => Durations[0] + Durations[1] + Durations[2] + Durations[3];

        public void Advance(double dt)
        {
            if (!(dt >= 0)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "time cannot run backwards");
            PhaseTime += dt;
            if (PhaseTime >= Durations[Phase])
            {
                PhaseTime = 0;
                Phase = (Phase + 1) % Axes.Length;
            }
        }
    }
}
