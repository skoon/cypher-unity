using System;

namespace Cypher.Core
{
    /// <summary>The rooms the reverb can be in (the prototype's five): what the sound of the place around the listener is.</summary>
    public enum ReverbSpace
    {
        Open,
        Street,
        Alley,
        Indoor,
        Plaza,
    }

    /// <summary>
    /// Works out which reverb space the listener is in from the geometry around them: twelve rays fanned out horizontally and one straight up, every half second. Tight on all sides is an alley;
    /// tight under a ceiling is indoors; wide open is a plaza. It takes two probes that agree before it says the space has changed, so a passing doorway does not flip it.
    /// </summary>
    public sealed class SpaceProbe
    {
        public const int Rays = 12;
        /// <summary>How far each horizontal ray looks (a ray that finds nothing counts as this).</summary>
        public const double Range = 40;
        public const double CeilingRange = 25;
        public const double Interval = 0.5;

        double _nextProbe;
        ReverbSpace _candidate = ReverbSpace.Street;
        int _streak;

        /// <summary>The space the sound is in now.</summary>
        public ReverbSpace Current { get; private set; } = ReverbSpace.Street;

        /// <summary>0 in the open, up to 1 right under a low ceiling in a small space: how much of what is outside is muffled.</summary>
        public double Indoor { get; private set; }

        /// <summary>Hands control to a fixed space (null returns it to the probe).</summary>
        public ReverbSpace? Override { get; set; }

        public bool Due(double now) => now >= _nextProbe;

        /// <summary>The direction of horizontal ray <paramref name="i"/>: (cos, sin) of its angle.</summary>
        public static void Direction(int i, out double x, out double z)
        {
            double a = (double)i / Rays * Math.PI * 2;
            x = Math.Cos(a);
            z = Math.Sin(a);
        }

        /// <summary>
        /// Take a probe's rays. <paramref name="distance"/> holds each horizontal ray's distance to what it hit (<see cref="Range"/> where it hit nothing), <paramref name="hit"/> whether it hit anything,
        /// and <paramref name="ceiling"/> the distance up to a ceiling (<see cref="Range"/> where there is none within <see cref="CeilingRange"/>).
        /// </summary>
        /// <returns>The new space if it has changed.</returns>
        public ReverbSpace? Observe(double now, double[] distance, bool[] hit, double ceiling)
        {
            if (distance.Length != Rays || hit.Length != Rays) throw new ArgumentException($"a probe is {Rays} rays");
            _nextProbe = now + Interval;
            double sum = 0;
            int hits = 0;
            for (int i = 0; i < Rays; i++)
            {
                sum += distance[i];
                if (hit[i]) hits++;
            }
            double mean = sum / Rays;
            double enclosed = (double)hits / Rays;
            Indoor = ceiling < 9 && mean < 14 ? Math.Min(1, Math.Max(0, 1 - ceiling / 9)) : 0;

            ReverbSpace want;
            if (ceiling < 9 && mean < 16) want = ReverbSpace.Indoor;
            else if (mean < 9 && enclosed > 0.55) want = ReverbSpace.Alley;
            else if (mean > 26 || enclosed < 0.25) want = ReverbSpace.Plaza;
            else if (mean < 20) want = ReverbSpace.Street;
            else want = ReverbSpace.Open;

            // two probes in a row that agree before the space changes
            if (want == _candidate) _streak++;
            else
            {
                _candidate = want;
                _streak = 0;
            }
            if (_streak < 1) return null;

            ReverbSpace target = Override ?? want;
            if (target == Current) return null;
            Current = target;
            return target;
        }

        /// <summary>Fix the space (the override); returns it if that changes the current space.</summary>
        public ReverbSpace? Force(ReverbSpace space)
        {
            Override = space;
            if (space == Current) return null;
            Current = space;
            return space;
        }
    }
}
