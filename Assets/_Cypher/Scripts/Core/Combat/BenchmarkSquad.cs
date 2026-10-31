using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Gate D's fight (<c>--benchmark-fight</c>): one hostile squad the size of the largest encounter (a heavy and four gunners, as the Jackals) kept in front of the
    /// benchmark's walker for the whole run. It comes on clear pavement round a mark <see cref="Ahead"/> metres ahead (straight ahead if there is room, else a little
    /// nearer, further or to the side: <see cref="Turns"/>), is told where the walker is every <see cref="RemindEvery"/> s (so it is fighting, not searching), and once
    /// the walker has left it more than <see cref="LeftBehind"/> metres behind it is taken away and the next comes ahead. Who may die is the caller's business.
    /// </summary>
    public sealed class BenchmarkSquad
    {
        public const double LeftBehind = 50, Reach = 10, RetryAfter = 0.5, RemindEvery = 3;
        public const string Tag = "benchmark";

        /// <summary>The marks tried, in order: how far ahead, and how far off the walker's heading (radians).</summary>
        public static readonly IReadOnlyList<double> Ahead = new[] { 30.0, 25.0, 35.0 };
        public static readonly IReadOnlyList<double> Turns = new[] { 0.0, 0.35, -0.35, 0.7, -0.7 };

        public static readonly IReadOnlyList<EnemyArchetype> Members = new[] { EnemyTable.Heavy, EnemyTable.Gunner, EnemyTable.Gunner, EnemyTable.Gunner, EnemyTable.Gunner };

        readonly EnemySim _sim;
        readonly CityNav _nav;
        readonly IReadOnlyList<PropBox> _props;
        readonly IReadOnlyList<(double X, double Z, double Width, double Depth)> _walls;
        double _wait, _remind;

        public BenchmarkSquad(EnemySim sim, CityNav nav, IReadOnlyList<PropBox> props, IReadOnlyList<(double X, double Z, double Width, double Depth)> walls)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
            _props = props ?? throw new ArgumentNullException(nameof(props));
            _walls = walls ?? throw new ArgumentNullException(nameof(walls));
        }

        /// <summary>The squad in the world now, or null between one and the next.</summary>
        public EnemySquad Squad { get; private set; }

        /// <summary>How many squads have come so far.</summary>
        public int Spawned { get; private set; }

        /// <summary>Take the squad away (the fights end, or the walker flatlined); the next comes on the next step.</summary>
        public void Reset()
        {
            if (Squad != null && Contains(Squad)) _sim.Remove(Squad);
            Squad = null;
            _wait = 0;
        }

        /// <summary>One step: the squad left behind goes, a new one comes ahead when there is none, and those not fighting are told where the walker is.</summary>
        public void Step(double dt, V3 feet, V3 eye, double yaw)
        {
            if (Squad != null && !Contains(Squad)) Squad = null;   // taken away by someone else (the Trauma Team clears every squad)
            if (Squad != null && Nearest(Squad, feet) > LeftBehind)
            {
                _sim.Remove(Squad);
                Squad = null;
            }
            if (Squad == null)
            {
                _wait -= dt;
                if (_wait > 0) return;
                if (!TrySpawn(feet, yaw))
                {
                    _wait = RetryAfter;
                    return;
                }
            }
            // those not fighting at once; the rest every few seconds, often enough that they never give up searching and seldom enough that they still push
            _remind -= dt;
            bool all = _remind <= 0;
            if (all) _remind = RemindEvery;
            IReadOnlyList<Enemy> members = Squad.All;
            for (int i = 0; i < members.Count; i++)
            {
                Enemy e = members[i];
                if (e.State != EnemyState.Down && (all || e.State != EnemyState.Engaged)) _sim.ShotAt(e, eye);
            }
        }

        bool Contains(EnemySquad squad)
        {
            IReadOnlyList<EnemySquad> squads = _sim.Squads;
            for (int i = 0; i < squads.Count; i++) if (squads[i] == squad) return true;
            return false;
        }

        static double Nearest(EnemySquad squad, V3 p)
        {
            double best = double.PositiveInfinity;
            IReadOnlyList<Enemy> all = squad.All;
            for (int i = 0; i < all.Count; i++)
            {
                double dx = all[i].Position.X - p.X, dz = all[i].Position.Z - p.Z;
                best = Math.Min(best, Math.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        bool TrySpawn(V3 feet, double yaw)
        {
            for (int t = 0; t < Turns.Count; t++)
            {
                for (int a = 0; a < Ahead.Count; a++)
                {
                    double x = feet.X + Math.Sin(yaw + Turns[t]) * Ahead[a], z = feet.Z + Math.Cos(yaw + Turns[t]) * Ahead[a];
                    List<(double Dx, double Dz)> spots = EncounterContent.Spots(x, z, Members.Count, Reach, _nav, _props, _walls);
                    if (spots.Count < Members.Count) continue;
                    EnemySquad squad = _sim.AddSquad(Tag, hostile: true);
                    for (int m = 0; m < Members.Count; m++)
                    {
                        double mx = x + spots[m].Dx, mz = z + spots[m].Dz;
                        _sim.Add(squad, Members[m], new V3(mx, _nav.GroundY(mx, mz), mz), Math.Atan2(feet.X - mx, feet.Z - mz));
                    }
                    Squad = squad;
                    Spawned++;
                    _remind = 0;
                    return true;
                }
            }
            return false;
        }
    }
}
