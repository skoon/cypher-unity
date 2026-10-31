using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>One of an encounter's people: what kind, where it stands from the encounter's anchor, which way it faces, and its beat if it walks one (offsets too).</summary>
    public sealed class EncounterMember
    {
        public string Archetype;
        public double Dx, Dz, Yaw;
        public IReadOnlyList<(double Dx, double Dz)> Beat;
    }

    /// <summary>
    /// A gang holding a place: an id (kept in the save once cleared), the tag its kills count as for jobs, where it is, and who. <see cref="When"/> (the conditions'
    /// grammar, null for always) says when it is wanted, a job's stage for the content's; <see cref="ArriveBeyond"/> keeps it from appearing while the player is that
    /// close, so they come while you are away (Marlene's collectors).
    /// </summary>
    public sealed class EncounterDef
    {
        public string Id, Tag;
        public V3 Anchor;
        public IReadOnlyList<EncounterMember> Members;
        public JsonObject When;
        public double ArriveBeyond;
    }

    /// <summary>
    /// The set encounters: each comes into the world when it is wanted and the player is within <see cref="SpawnWithin"/> of it (and beyond its
    /// <see cref="EncounterDef.ArriveBeyond"/>), and leaves again when they are beyond <see cref="DespawnBeyond"/> or it is no longer wanted, and it is not fighting.
    /// Those of its people who went down stay down: it comes back with the survivors only (<see cref="Down"/>, kept in the save), so a job's kill count and the
    /// encounter always agree and walking away and back pays nothing. Once all are down it is cleared, said once through <see cref="Cleared"/>, and never comes back.
    /// </summary>
    public sealed class EncounterDirector
    {
        public const double SpawnWithin = 75, DespawnBeyond = 120;

        readonly EnemySim _sim;
        readonly IReadOnlyList<EncounterDef> _defs;
        readonly Func<string, bool> _isCleared;
        readonly Func<EncounterDef, bool> _wanted;
        readonly Dictionary<string, (EnemySquad Squad, List<int> Members)> _live = new Dictionary<string, (EnemySquad, List<int>)>();
        readonly Dictionary<string, HashSet<int>> _down = new Dictionary<string, HashSet<int>>();
        readonly HashSet<string> _cleared = new HashSet<string>();
        readonly List<string> _justCleared = new List<string>();
        readonly List<(string Id, int Member)> _justDown = new List<(string, int)>();

        /// <param name="isCleared">Whether the save says an encounter is already cleared.</param>
        /// <param name="downInSave">Which of an encounter's members (by index) the save says are down; null for none.</param>
        /// <param name="wanted">Whether an encounter is wanted now; null for always.</param>
        public EncounterDirector(EnemySim sim, IReadOnlyList<EncounterDef> defs, Func<string, bool> isCleared, Func<string, IEnumerable<int>> downInSave = null,
            Func<EncounterDef, bool> wanted = null)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _defs = defs ?? throw new ArgumentNullException(nameof(defs));
            _isCleared = isCleared ?? throw new ArgumentNullException(nameof(isCleared));
            _wanted = wanted ?? (d => true);
            var ids = new HashSet<string>();
            foreach (EncounterDef d in defs)
            {
                if (string.IsNullOrEmpty(d.Id) || !ids.Add(d.Id)) throw new ArgumentException($"encounter ids must be set and unique ('{d.Id}')", nameof(defs));
                if (d.Members == null || d.Members.Count == 0) throw new ArgumentException($"encounter '{d.Id}' has nobody in it", nameof(defs));
                foreach (EncounterMember m in d.Members) EnemyTable.Get(m.Archetype);   // an unknown kind is a content bug
                if (d.When != null) RpgConditions.Assert(d.When, $"encounter {d.Id}.when");
                var down = new HashSet<int>();
                foreach (int i in downInSave?.Invoke(d.Id) ?? Array.Empty<int>())
                {
                    if (i < 0 || i >= d.Members.Count) throw new ArgumentOutOfRangeException(nameof(downInSave), i, $"encounter '{d.Id}' has no member {i}");
                    down.Add(i);
                }
                _down[d.Id] = down;
            }
        }

        /// <summary>The encounters cleared in the last step, to be kept in the save.</summary>
        public IReadOnlyList<string> Cleared => _justCleared;

        /// <summary>The members (encounter, index) that went down since the last step or reset, to be kept in the save.</summary>
        public IReadOnlyList<(string Id, int Member)> JustDown => _justDown;

        /// <summary>Which of an encounter's members are down for good.</summary>
        public IReadOnlyCollection<int> Down(string id) => _down[id];

        public EnemySquad Live(string id) => _live.TryGetValue(id, out var l) ? l.Squad : null;

        /// <summary>Take every encounter in the world out of it (the player flatlined): those not cleared come back with their survivors when the player next comes near.</summary>
        public void Reset()
        {
            _justDown.Clear();
            foreach (KeyValuePair<string, (EnemySquad Squad, List<int> Members)> kv in _live)
            {
                Sweep(kv.Key, kv.Value);
                _sim.Remove(kv.Value.Squad);
            }
            _live.Clear();
            _justCleared.Clear();
        }

        public void Step(V3 player)
        {
            _justCleared.Clear();
            _justDown.Clear();
            for (int k = 0; k < _defs.Count; k++)
            {
                EncounterDef d = _defs[k];
                double dx = d.Anchor.X - player.X, dz = d.Anchor.Z - player.Z, dist = Math.Sqrt(dx * dx + dz * dz);
                if (_live.TryGetValue(d.Id, out var live))
                {
                    Sweep(d.Id, live);
                    if (live.Squad.AllDown)
                    {
                        if (_cleared.Add(d.Id)) _justCleared.Add(d.Id);
                        // the bodies lie until the player is well away
                        if (dist > DespawnBeyond) Despawn(d.Id, live.Squad);
                    }
                    else if ((dist > DespawnBeyond || !_wanted(d)) && !live.Squad.AnyEngaged) Despawn(d.Id, live.Squad);
                    continue;
                }
                if (dist > SpawnWithin || dist < d.ArriveBeyond || _cleared.Contains(d.Id) || _isCleared(d.Id) || !_wanted(d)) continue;
                if (_down[d.Id].Count >= d.Members.Count)
                {
                    // all down in the save but never said cleared (a save from between the two): say it now
                    if (_cleared.Add(d.Id)) _justCleared.Add(d.Id);
                    continue;
                }
                _live[d.Id] = Spawn(d);
            }
        }

        void Despawn(string id, EnemySquad squad)
        {
            _sim.Remove(squad);
            _live.Remove(id);
        }

        /// <summary>Note who of a live encounter has gone down since last looked.</summary>
        void Sweep(string id, (EnemySquad Squad, List<int> Members) live)
        {
            HashSet<int> down = _down[id];
            for (int i = 0; i < live.Members.Count; i++)
            {
                if (live.Squad.All[i].State == EnemyState.Down && down.Add(live.Members[i])) _justDown.Add((id, live.Members[i]));
            }
        }

        (EnemySquad, List<int>) Spawn(EncounterDef d)
        {
            EnemySquad squad = _sim.AddSquad(d.Tag, hostile: true);
            var members = new List<int>();
            HashSet<int> down = _down[d.Id];
            for (int i = 0; i < d.Members.Count; i++)
            {
                if (down.Contains(i)) continue;
                EncounterMember m = d.Members[i];
                var home = new V3(d.Anchor.X + m.Dx, d.Anchor.Y, d.Anchor.Z + m.Dz);
                List<V3> beat = null;
                if (m.Beat != null && m.Beat.Count > 0)
                {
                    beat = new List<V3>();
                    foreach ((double bx, double bz) in m.Beat) beat.Add(new V3(d.Anchor.X + bx, d.Anchor.Y, d.Anchor.Z + bz));
                }
                _sim.Add(squad, EnemyTable.Get(m.Archetype), home, m.Yaw, beat);
                members.Add(i);
            }
            return (squad, members);
        }
    }

    /// <summary>
    /// The street patrols: <see cref="Squads"/> small groups out at a time, each two or three (now and then with a heavy), walking a beat of the pavement graph, neutral
    /// until provoked. They come into the world on a pavement node 80 to 130 m from the player and out of their sight, and leave when beyond 160 m and not fighting;
    /// after one leaves or is wiped out the next waits <see cref="RespawnDelay"/>. Seeded.
    /// </summary>
    public sealed class PatrolDirector
    {
        public const int Squads = 2;
        public const double SpawnMin = 80, SpawnMax = 130, DespawnBeyond = 160, RespawnDelay = 30, ClearBodiesAfter = 20;
        public const int BeatNodes = 10, SpawnTries = 12;

        readonly EnemySim _sim;
        readonly EnemyPaths _paths;
        readonly string _tag;
        readonly Rng _rng;
        readonly List<(EnemySquad Squad, double DownFor)> _live = new List<(EnemySquad, double)>();
        double _wait;

        public PatrolDirector(EnemySim sim, EnemyPaths paths, uint seed, string tag)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _tag = tag ?? throw new ArgumentNullException(nameof(tag));
            _rng = new Rng(seed);
        }

        public int Count => _live.Count;

        /// <summary>Take every patrol out of the world (the player flatlined); the next comes after <see cref="RespawnDelay"/>.</summary>
        public void Reset()
        {
            foreach (var l in _live) _sim.Remove(l.Squad);
            _live.Clear();
            _wait = RespawnDelay;
        }

        public IEnumerable<EnemySquad> All
        {
            get
            {
                foreach (var l in _live) yield return l.Squad;
            }
        }

        public void Step(double dt, V3 playerEye, IEnemyWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                (EnemySquad squad, double downFor) = _live[i];
                bool gone = false;
                if (squad.AllDown)
                {
                    downFor += dt;
                    gone = downFor > ClearBodiesAfter && Distance(squad, playerEye) > 40;
                }
                else gone = Distance(squad, playerEye) > DespawnBeyond && !squad.AnyEngaged;
                if (gone)
                {
                    _sim.Remove(squad);
                    _live.RemoveAt(i);
                    _wait = RespawnDelay;
                    continue;
                }
                _live[i] = (squad, downFor);
            }
            if (_wait > 0)
            {
                _wait -= dt;
                return;
            }
            if (_live.Count < Squads && TrySpawn(playerEye, world)) _wait = 2;   // not two on the same step
        }

        static double Distance(EnemySquad squad, V3 p)
        {
            double best = double.PositiveInfinity;
            foreach (Enemy e in squad.All)
            {
                double dx = e.Position.X - p.X, dz = e.Position.Z - p.Z;
                best = Math.Min(best, Math.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        bool TrySpawn(V3 playerEye, IEnemyWorld world)
        {
            CityNav nav = _paths.Nav;
            for (int t = 0; t < SpawnTries; t++)
            {
                int node = nav.NodeInBand(_rng, playerEye.X, playerEye.Z, SpawnMin, SpawnMax);
                double x = nav.NodeX(node), z = nav.NodeZ(node), y = nav.GroundY(x, z);
                double dx = x - playerEye.X, dz = z - playerEye.Z, d = Math.Sqrt(dx * dx + dz * dz);
                if (d < SpawnMin || d > SpawnMax) continue;
                if (world.Clear(playerEye, new V3(x, y + EnemySim.EyeHeight, z))) continue;   // the player would see them appear
                Spawn(node);
                return true;
            }
            return false;
        }

        void Spawn(int start)
        {
            CityNav nav = _paths.Nav;
            // the beat: a walk of a few nodes that does not turn straight back, then back the way it came
            var nodes = new List<int> { start };
            int prev = -1, cur = start;
            for (int i = 1; i < BeatNodes; i++)
            {
                IReadOnlyList<int> next = nav.Neighbours(cur);
                var choices = new List<int>();
                foreach (int n in next) if (n != prev) choices.Add(n);
                if (choices.Count == 0) choices.AddRange(next);
                int pick = choices[(int)Math.Floor(_rng.Next() * choices.Count)];
                prev = cur;
                cur = pick;
                nodes.Add(cur);
            }
            for (int i = nodes.Count - 2; i > 0; i--) nodes.Add(nodes[i]);

            int size = _rng.Next() < 0.5 ? 2 : 3;
            bool heavy = _rng.Next() < 0.25;
            EnemySquad squad = _sim.AddSquad(_tag, hostile: false);
            for (int m = 0; m < size; m++)
            {
                // walking side by side, a little apart
                double ox = (m - (size - 1) / 2.0) * 0.9, oz = (m % 2) * 0.6;
                var beat = new List<V3>(nodes.Count);
                foreach (int n in nodes) beat.Add(new V3(nav.NodeX(n) + ox, nav.GroundY(nav.NodeX(n), nav.NodeZ(n)), nav.NodeZ(n) + oz));
                EnemyArchetype kind = heavy && m == 0 ? EnemyTable.Heavy : EnemyTable.Gunner;
                Enemy e = _sim.Add(squad, kind, beat[0], 0, beat);
                e.PatrolIndex = 1 % beat.Count;
            }
            _live.Add((squad, 0));
        }
    }
}
