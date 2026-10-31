using System;

namespace Cypher.Core
{
    /// <summary>
    /// The HUD's hit direction arcs: each hit on the player leaves an arc round the crosshair pointing at the shooter, fading over <see cref="Life"/>. Hits from about
    /// the same direction feed one arc; at most <see cref="Max"/> show (a new one replaces the faintest). Bearings are world yaws from the player to the shooter.
    /// </summary>
    public sealed class DamageArcs
    {
        public const int Max = 4;
        public const double Life = 1.4;

        /// <summary>Hits within this angle (radians) of an arc feed it rather than starting another.</summary>
        public const double Merge = 0.35;

        readonly double[] _bearing = new double[Max], _age = new double[Max], _strength = new double[Max];

        public int Count { get; private set; }

        /// <param name="strength">0 to 1: how hard the hit was, for how strongly the arc shows.</param>
        public void Add(double bearing, double strength)
        {
            if (double.IsNaN(bearing) || double.IsNaN(strength)) throw new ArgumentException("an arc needs a bearing and a strength");
            strength = Math.Max(0, Math.Min(1, strength));
            for (int i = 0; i < Count; i++)
            {
                if (Math.Abs(CrowdSim.ShortestAngle(_bearing[i], bearing)) > Merge) continue;
                _strength[i] = Math.Min(1, Math.Max(Alpha(i), strength) + 0.15);
                _bearing[i] = bearing;
                _age[i] = 0;
                return;
            }
            int slot = Count;
            if (Count == Max)
            {
                slot = 0;
                for (int i = 1; i < Max; i++) if (Alpha(i) < Alpha(slot)) slot = i;
            }
            else
            {
                Count++;
            }
            _bearing[slot] = bearing;
            _age[slot] = 0;
            _strength[slot] = strength;
        }

        public void Update(double dt)
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                _age[i] += dt;
                if (_age[i] < Life) continue;
                Count--;
                _bearing[i] = _bearing[Count]; _age[i] = _age[Count]; _strength[i] = _strength[Count];
            }
        }

        public void Clear() => Count = 0;

        static double Fade(double age) => Math.Max(0, 1 - age / Life);

        double Alpha(int i) => _strength[i] * Fade(_age[i]);

        /// <summary>Arc <paramref name="i"/> as the HUD draws it: its angle from straight ahead (radians, right positive) for a player facing <paramref name="playerYaw"/>, and its opacity.</summary>
        public (double Relative, double Alpha) Get(int i, double playerYaw)
        {
            if (i < 0 || i >= Count) throw new ArgumentOutOfRangeException(nameof(i), i, $"there are {Count} arcs");
            return (CrowdSim.ShortestAngle(playerYaw, _bearing[i]), Alpha(i));
        }

        /// <summary>The world bearing from a player at (<paramref name="px"/>, <paramref name="pz"/>) to a shooter.</summary>
        public static double BearingTo(double px, double pz, double sx, double sz) => Math.Atan2(sx - px, sz - pz);
    }

    /// <summary>Where an enemy's shot goes: a hit ends at the player's chest; a miss passes close by them, to one side or over, and flies on.</summary>
    public static class EnemyShots
    {
        public const double MissMin = 0.45, MissMax = 1.3;

        /// <summary>A point the missed shot passes through: beside the chest by <see cref="MissMin"/> to <see cref="MissMax"/> metres, a little up or down.</summary>
        public static V3 MissPoint(V3 from, V3 chest, Rng rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            double dx = chest.X - from.X, dz = chest.Z - from.Z, d = Math.Sqrt(dx * dx + dz * dz);
            // sideways is across the line of fire; straight down it there is no across, so any way will do
            double sx = d > 1e-6 ? -dz / d : 1, sz = d > 1e-6 ? dx / d : 0;
            double off = (MissMin + (MissMax - MissMin) * rng.Next()) * (rng.Next() < 0.5 ? -1 : 1);
            double up = -0.35 + 0.95 * rng.Next();
            return new V3(chest.X + sx * off, chest.Y + up, chest.Z + sz * off);
        }

        /// <summary>The far end of a shot from <paramref name="from"/> through <paramref name="through"/> at <paramref name="range"/>.</summary>
        public static V3 Onward(V3 from, V3 through, double range)
        {
            V3 dir = (through - from).Normalized;
            return from + dir * range;
        }
    }
}
