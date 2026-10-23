using System;

namespace Cypher.Core
{
    /// <summary>What the player is walking on, and when a step falls: the prototype's surface choice and stride, ported.</summary>
    public static class Footing
    {
        /// <summary>A hash of two whole numbers into [0, 1): picks a surface from where a step lands, the same every time.</summary>
        public static double Hash2(int x, int y)
        {
            unchecked
            {
                uint h = (uint)((x * 0x27d4eb2d) ^ (y * 0x165667b1));
                return (h ^ (h >> 15)) / 4294967296.0;
            }
        }

        /// <summary>How wet the ground is for a rain of 0 to 1.</summary>
        public static double Wetness(double rain) => Math.Min(1, Math.Max(0, rain * 0.9));

        /// <summary>
        /// The surface under a point: a sparse scatter of metal service grates, then puddles wherever rain has pooled (more on the road than off it), a little gravel, and otherwise asphalt on
        /// the road and concrete off it. The ground is cut into 2.5 m squares and each square keeps its surface.
        /// </summary>
        public static string SurfaceAt(double x, double z, bool onRoad, double wetness)
        {
            double h = Hash2((int)Math.Floor(x / 2.5), (int)Math.Floor(z / 2.5));
            if (h > 0.955) return "metal";
            if (onRoad && h < 0.10 + wetness * 0.22) return "water";
            if (!onRoad && h < 0.05 + wetness * 0.10) return "water";
            if (h > 0.90) return "gravel";
            return onRoad ? "asphalt" : "concrete";
        }
    }

    /// <summary>When the player's feet fall: a step every stride's worth of ground covered while walking on the ground, and the count winds down in the air.</summary>
    public sealed class StrideClock
    {
        public const double Walk = 1.85, Sprint = 2.3, Crouch = 1.5;

        double _distance;

        public double Distance => _distance;

        /// <summary>The distance a stride covers in a stance.</summary>
        public static double Stride(bool crouching, bool sprinting) => crouching ? Crouch : sprinting ? Sprint : Walk;

        /// <summary>Advance by one frame; true if a foot falls in it.</summary>
        public bool Update(double dt, bool grounded, bool moving, double speed, bool crouching, bool sprinting)
        {
            if (grounded && moving)
            {
                _distance += speed * dt;
                if (_distance >= Stride(crouching, sprinting))
                {
                    _distance = 0;
                    return true;
                }
            }
            else
            {
                _distance = Math.Max(0, _distance - dt);
            }
            return false;
        }

        public void Reset() { _distance = 0; }
    }

    /// <summary>One breath for the player to make.</summary>
    public readonly struct BreathEvent
    {
        public readonly double Time;
        public readonly bool Inhale;
        public readonly double Effort;
        public readonly double Volume;

        public BreathEvent(double time, bool inhale, double effort, double volume)
        {
            Time = time;
            Inhale = inhale;
            Effort = effort;
            Volume = volume;
        }
    }

    /// <summary>
    /// The player's breathing: exertion rises quickly with speed and falls slowly, and a breath in or out is made every so often, quicker and louder the harder the effort. Breaths alternate
    /// in and out, one shot each, so rate and depth follow exertion with nothing to change in a running sound.
    /// </summary>
    public sealed class BreathClock
    {
        double _exertion;
        double _next;
        bool _in = true;

        public BreathClock(double firstBreath)
        {
            _next = firstBreath;
        }

        public double Exertion => _exertion;

        /// <param name="now">The time on the audio clock, seconds.</param>
        /// <param name="speed">Planar speed, m/s.</param>
        /// <returns>The breath to make now, if one is due.</returns>
        public BreathEvent? Update(double dt, double now, double speed, bool sprinting)
        {
            double want = Math.Min(1, Math.Max(0, sprinting ? 0.9 : speed / 8));
            double k = want > _exertion ? 0.55 : 0.12;
            _exertion += (want - _exertion) * Math.Min(1, Math.Max(0, k * dt));

            if (now < _next) return null;
            double eff = _exertion;
            double half = (2.0 + (0.55 - 2.0) * eff) * 0.5;
            double at = Math.Max(now, _next);
            var breath = new BreathEvent(at, _in, eff, 0.28 + eff * 0.75);
            _in = !_in;
            _next = at + half;
            return breath;
        }
    }
}
