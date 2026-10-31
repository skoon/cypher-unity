using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// The quickhacks on the enemies, run from the RPG's deck (<see cref="QuickhackDeck.Active"/>, the one record of what is running, where and for how long): the
    /// hacks there whose target is <c>"enemy:&lt;id&gt;"</c> do what their tables say.
    /// <list type="bullet">
    /// <item><c>reveal</c> (Ping): the target and its squad are marked for the player.</item>
    /// <item><c>blind</c> (Reboot Optics): the target sees no one.</item>
    /// <item><c>burn</c> (Overheat): Power a second, in ticks on its own clock: one as it lands, then every <see cref="TickEvery"/> for its time.</item>
    /// <item><c>shock</c> (Short Circuit): Power at once, on upload.</item>
    /// <item><c>contagion</c>: burns as Overheat does, and each second jumps to those within its Spread who have none, for the time it has left.</item>
    /// <item><c>berserk</c> (Cyberpsychosis): the target fights its own squad, and they fight it.</item>
    /// </list>
    /// Damage is scaled by the player's hack strength. The damaging hacks and berserk provoke the target's squad as a shot would; Ping and Reboot Optics are quiet.
    /// A hack on one who died, or who is gone, is taken off the deck.
    /// </summary>
    public sealed class HackEffects
    {
        public const string TargetPrefix = "enemy:";

        /// <summary>Seconds between the ticks of burning damage, and between contagion's jumps.</summary>
        public const double TickEvery = 0.5, SpreadEvery = 1.0;

        readonly EnemySim _sim;
        double _spread;

        // how many ticks each burning hack has dealt (by the deck's entry: a refresh is a new entry, and starts again)
        readonly List<(ActiveHack Hack, int Ticks)> _burns = new List<(ActiveHack, int)>();

        public HackEffects(EnemySim sim)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
        }

        public static string TargetOf(Enemy e) => TargetPrefix + e.Id;

        /// <summary>The enemy id a hack's target names, or -1 when it is not an enemy.</summary>
        public static int EnemyId(string targetId)
        {
            if (targetId == null || !targetId.StartsWith(TargetPrefix, StringComparison.Ordinal)) return -1;
            if (!int.TryParse(targetId.Substring(TargetPrefix.Length), out int id) || id < 0) throw new FormatException($"a bad enemy target \"{targetId}\"");
            return id;
        }

        /// <summary>How much the player's hack strength adds to a hack's damage.</summary>
        public static double Scale(double hackStrength) => 1 + Math.Max(0, hackStrength) / 100;

        static bool Provokes(string effect) => effect == "burn" || effect == "shock" || effect == "contagion" || effect == "berserk";

        static bool Burns(string effect) => effect == "burn" || effect == "contagion";

        /// <summary>
        /// A hack was just uploaded onto an enemy by the player at <paramref name="from"/>: a loud one provokes its squad; a shock strikes now; the marks are on at once.
        /// True when it killed.
        /// </summary>
        public bool Applied(ActiveHack hack, V3 from, double hackStrength)
        {
            if (hack == null) throw new ArgumentNullException(nameof(hack));
            Enemy e = _sim.Find(EnemyId(hack.TargetId));
            if (e == null || e.State == EnemyState.Down) return false;
            if (Provokes(hack.Effect)) _sim.ShotAt(e, from);
            Mark(e, hack.Effect);
            return hack.Effect == "shock" && _sim.Harm(e, hack.Power * Scale(hackStrength));
        }

        /// <summary>One fixed step of the hacks on the deck; those killed by them are added to <paramref name="killed"/>.</summary>
        public void Step(double dt, List<ActiveHack> active, double hackStrength, List<Enemy> killed)
        {
            if (!(dt > 0)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "a step must move time on");
            if (active == null) throw new ArgumentNullException(nameof(active));
            if (killed == null) throw new ArgumentNullException(nameof(killed));

            // the dead and the gone carry nothing
            for (int i = active.Count - 1; i >= 0; i--)
            {
                int id = EnemyId(active[i].TargetId);
                if (id < 0) continue;
                Enemy e = _sim.Find(id);
                if (e == null || e.State == EnemyState.Down) active.RemoveAt(i);
            }

            for (int i = 0; i < _sim.Enemies.Count; i++) Unmark(_sim.Enemies[i]);
            foreach (ActiveHack h in active)
            {
                Enemy e = Target(h);
                if (e != null) Mark(e, h.Effect);
            }

            Burn(active, Scale(hackStrength), killed);

            _spread += dt;
            while (_spread >= SpreadEvery)
            {
                _spread -= SpreadEvery;
                Spread(active);
            }
        }

        void Burn(List<ActiveHack> active, double scale, List<Enemy> killed)
        {
            for (int i = _burns.Count - 1; i >= 0; i--)
            {
                if (!active.Contains(_burns[i].Hack)) _burns.RemoveAt(i);
            }
            foreach (ActiveHack h in active)
            {
                if (!Burns(h.Effect)) continue;
                int k = -1;
                for (int j = 0; j < _burns.Count && k < 0; j++) if (_burns[j].Hack == h) k = j;
                if (k < 0)
                {
                    _burns.Add((h, 0));
                    k = _burns.Count - 1;
                }
                int all = (int)Math.Ceiling(h.Duration / TickEvery - 1e-9);
                int due = Math.Min(all, (int)Math.Floor((h.Duration - h.TimeLeft) / TickEvery + 1e-9) + 1);
                for (int t = _burns[k].Ticks; t < due; t++)
                {
                    Enemy e = Target(h);
                    if (e == null || e.State == EnemyState.Down) break;
                    if (_sim.Harm(e, h.Power * TickEvery * scale)) killed.Add(e);
                }
                _burns[k] = (h, Math.Max(_burns[k].Ticks, due));
            }
        }

        /// <summary>Each contagion jumps to the living within its spread who have none, carrying the time it has left.</summary>
        void Spread(List<ActiveHack> active)
        {
            int n = active.Count;   // only those running now jump; the new ones wait for the next
            for (int i = 0; i < n; i++)
            {
                ActiveHack h = active[i];
                if (h.Effect != "contagion") continue;
                Enemy from = Target(h);
                if (from == null || from.State == EnemyState.Down) continue;
                double reach = RpgItems.Template(h.HackId)?.Hack?.Spread ?? throw new InvalidOperationException($"the contagion {h.HackId} has no spread");
                for (int k = 0; k < _sim.Enemies.Count; k++)
                {
                    Enemy e = _sim.Enemies[k];
                    if (e == from || e.State == EnemyState.Down || Has(active, e, "contagion")) continue;
                    double dx = e.Position.X - from.Position.X, dz = e.Position.Z - from.Position.Z;
                    if (dx * dx + dz * dz > reach * reach) continue;
                    ActiveHack copy = h.Copy();
                    copy.TargetId = TargetOf(e);
                    copy.Duration = copy.TimeLeft;   // its own clock: it burns for what is left
                    copy.Uid = $"{h.HackId}:{copy.TargetId}:spread";
                    active.Add(copy);
                }
            }
        }

        static bool Has(List<ActiveHack> active, Enemy e, string effect)
        {
            string target = TargetOf(e);
            foreach (ActiveHack h in active) if (h.Effect == effect && h.TargetId == target) return true;
            return false;
        }

        Enemy Target(ActiveHack h)
        {
            int id = EnemyId(h.TargetId);
            return id < 0 ? null : _sim.Find(id);
        }

        void Mark(Enemy e, string effect)
        {
            switch (effect)
            {
                case "blind": e.Blind = true; break;
                case "berserk": e.Berserk = true; break;
                case "reveal":
                    for (int i = 0; i < e.Squad.All.Count; i++) e.Squad.All[i].Revealed = true;
                    break;
            }
        }

        static void Unmark(Enemy e) => e.Blind = e.Revealed = e.Berserk = false;

        /// <summary>Take every hack on an enemy off the deck (the fights were cleared away) and wipe the marks.</summary>
        public void Clear(List<ActiveHack> active)
        {
            if (active == null) throw new ArgumentNullException(nameof(active));
            active.RemoveAll(h => EnemyId(h.TargetId) >= 0);
            for (int i = 0; i < _sim.Enemies.Count; i++) Unmark(_sim.Enemies[i]);
            _spread = 0;
            _burns.Clear();
        }
    }
}
