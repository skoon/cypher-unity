using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>What an enemy is doing.</summary>
    public enum EnemyState
    {
        /// <summary>At its post or walking its patrol, unaware (or, for a patrol not yet provoked, aware and not caring).</summary>
        Idle,
        /// <summary>Heard a shot or glimpsed something: turns to it, goes to look.</summary>
        Suspicious,
        /// <summary>Knows where the player is (or was a moment ago) and fights.</summary>
        Engaged,
        /// <summary>Lost the player: goes to where it last saw them and looks around before giving up.</summary>
        Searching,
        /// <summary>Dead.</summary>
        Down,
    }

    public enum EnemyEventKind { Suspicious, Engaged, Searching, Idle, BurstStart, Shot, Hurt, Died, Provoked }

    /// <summary>
    /// Something an enemy did this step, for the game to show and sound: a change of state, a burst, a shot (hit or miss, and its damage before the player's armour;
    /// <see cref="Victim"/> is the enemy it was fired at, or -1 for the player), a wound, a death, a patrol turning hostile.
    /// </summary>
    public readonly struct EnemyEvent
    {
        public readonly EnemyEventKind Kind;
        public readonly int Enemy;
        public readonly bool Hit;
        public readonly double Damage;
        public readonly V3 From, To;
        public readonly int Victim;

        public EnemyEvent(EnemyEventKind kind, int enemy, bool hit = false, double damage = 0, V3 from = default, V3 to = default, int victim = -1)
        {
            Kind = kind; Enemy = enemy; Hit = hit; Damage = damage; From = from; To = to; Victim = victim;
        }
    }

    /// <summary>The player as the enemies see them: where their feet are, how high their eyes are, how fast they move, whether they crouch, whether they are alive.</summary>
    public readonly struct PlayerView
    {
        public readonly V3 Feet;
        public readonly double EyeHeight, Speed;
        public readonly bool Crouched, Alive;

        public PlayerView(V3 feet, double eyeHeight, double speed, bool crouched, bool alive = true)
        {
            Feet = feet; EyeHeight = eyeHeight; Speed = speed; Crouched = crouched; Alive = alive;
        }

        public V3 Eye => new V3(Feet.X, Feet.Y + EyeHeight, Feet.Z);

        /// <summary>Where an enemy aims: the chest.</summary>
        public V3 Chest => new V3(Feet.X, Feet.Y + EyeHeight * 0.75, Feet.Z);
    }

    /// <summary>What the enemies need from the world: whether a line between two eyes is clear (walls, props and parked cars block it), and whether one can walk straight between two places.</summary>
    public interface IEnemyWorld
    {
        bool Clear(V3 from, V3 to);

        bool Walkable(V3 from, V3 to);
    }

    /// <summary>
    /// A group that fights together: one that engages calls the rest in, and only so many of them fire at once. A squad that is not <see cref="Hostile"/> (a street
    /// patrol) minds its own business until provoked.
    /// </summary>
    public sealed class EnemySquad
    {
        public readonly int Id;
        /// <summary>What the squad's kills count as for jobs (<see cref="RpgCore.CombatKill"/>'s tag).</summary>
        public readonly string Tag;
        public bool Hostile { get; internal set; }
        internal readonly List<Enemy> Members = new List<Enemy>();
        internal int Firing;
        internal double Provocation;

        public EnemySquad(int id, string tag, bool hostile)
        {
            Id = id;
            Tag = tag;
            Hostile = hostile;
        }

        public IReadOnlyList<Enemy> All => Members;

        public bool AllDown
        {
            get
            {
                foreach (Enemy e in Members) if (e.State != EnemyState.Down) return false;
                return true;
            }
        }

        public bool AnyEngaged
        {
            get
            {
                foreach (Enemy e in Members) if (e.State == EnemyState.Engaged || e.State == EnemyState.Searching) return true;
                return false;
            }
        }

        /// <summary>How many may be in a burst at once: half the living, rounded up, at least one, so the rest are moving or hiding and the fire comes in turns.</summary>
        public int MaxFiring
        {
            get
            {
                int alive = 0;
                foreach (Enemy e in Members) if (e.State != EnemyState.Down) alive++;
                return Math.Max(1, (alive + 1) / 2);
            }
        }
    }

    /// <summary>One enemy: its body (where it stands, which way it faces, whether it crouches), its health, what it knows, and the timers of what it is doing.</summary>
    public sealed class Enemy
    {
        public readonly int Id;
        public readonly EnemyArchetype Archetype;
        public readonly EnemySquad Squad;
        public readonly EnemyVitals Vitals;

        /// <summary>Feet, on the ground; and the yaw it faces (0 is +Z, as the player's look).</summary>
        public V3 Position;
        public double Yaw;

        public EnemyState State { get; internal set; } = EnemyState.Idle;

        /// <summary>0 to 1: how sure it is that it has seen someone. At 1 it engages.</summary>
        public double Awareness { get; internal set; }

        /// <summary>Where it last saw the player, or the place it is going to look at.</summary>
        public V3 Target { get; internal set; }

        /// <summary>Whether it can see the player this step.</summary>
        public bool SeesPlayer { get; internal set; }

        /// <summary>The cover it holds, or null; whether it is crouched behind it.</summary>
        public CoverPoint Cover { get; internal set; }
        public bool Crouched { get; internal set; }

        /// <summary>Its post (where it goes back to) and its patrol, walked in a loop when idle; empty for a guard who stands.</summary>
        public readonly V3 Home;
        public readonly double HomeYaw;
        public readonly IReadOnlyList<V3> Patrol;

        internal int PatrolIndex;
        internal double StateTime, Unseen, Reaction, CalloutIn = -1, AimTime, NextShot, BurstLeft, Pause, Close, ShortcutIn;
        internal int BurstsHere, BurstsBeforeMoving;
        internal bool InBurst;

        // it was fighting a squadmate (a berserk, or under one): when that ends it turns back on the player
        internal bool FoughtSquadmate;

        // the way it is walking: where to, and the points round the blocks to get there
        internal V3 PathGoal;
        internal List<V3> Path;
        internal int PathIndex;

        /// <summary>In a burst: firing or between the shots of one.</summary>
        public bool Firing => InBurst;

        /// <summary>How fast it is moving this step (metres a second), for the walk.</summary>
        public double Speed { get; internal set; }

        /// <summary>The quickhacks on it (<see cref="HackEffects"/>): its optics are down (it sees no one); it is marked for the player; it fights its own squad.</summary>
        public bool Blind { get; internal set; }
        public bool Revealed { get; internal set; }
        public bool Berserk { get; internal set; }

        internal Enemy(int id, EnemyArchetype archetype, EnemySquad squad, V3 home, double homeYaw, IReadOnlyList<V3> patrol)
        {
            Id = id;
            Archetype = archetype;
            Squad = squad;
            Vitals = new EnemyVitals(archetype);
            Home = home;
            HomeYaw = homeYaw;
            Patrol = patrol ?? Array.Empty<V3>();
            Position = home;
            Yaw = homeYaw;
        }

        public V3 Eye => new V3(Position.X, Position.Y + (Crouched ? CoverMap.CrouchEye : EnemySim.EyeHeight), Position.Z);

        /// <summary>This one as a fighter sees it: a target to shoot at, as the player is.</summary>
        internal PlayerView AsTarget => new PlayerView(Position, Crouched ? CoverMap.CrouchEye : EnemySim.EyeHeight, Speed, Crouched, State != EnemyState.Down);
    }

    /// <summary>
    /// The enemies' minds, stepped at the game's fixed 60 Hz: what they see (a cone, a range, a clear line, a few metres all round) and hear (gunshots), how sure they are,
    /// and what they do about it: stand or patrol, turn and go to look, fight (the gunner from cover, stepping out for its bursts and moving on after a few; the heavy in
    /// the open, coming on) in bursts that take turns across the squad, chase what they lost, give up. Street patrols stay neutral until provoked. They walk straight where
    /// the way is open and round the blocks on the pavement graph where it is not. Pure and seeded: the same seed and the same inputs make the same fight.
    /// </summary>
    public sealed class EnemySim
    {
        public const double EyeHeight = 1.55;
        public const double WalkSpeed = 1.4, StepOutSpeed = 3.0;

        /// <summary>Seconds to stand up from behind low cover before the first shot.</summary>
        public const double StandUp = 0.25;
        public const double TurnRate = 4.0;   // radians a second

        /// <summary>Close enough to sense the player whichever way it faces.</summary>
        public const double CloseSense = 2.5;

        /// <summary>Awareness a second, close in and at the edge of sight; how fast it fades unseen; how much a crouch hides.</summary>
        public const double AwareNear = 3.0, AwareFar = 0.5, AwareFade = 0.3, CrouchHides = 0.6;

        /// <summary>How much faster one that is searching for the player becomes sure of them.</summary>
        public const double SearchingSees = 1.5;

        /// <summary>Seconds: unseen before a fighter goes searching; searching before giving up; suspicious before giving up; before a fighter moves on the last place it saw the player.</summary>
        public const double LoseAfter = 8, SearchFor = 10, SuspiciousFor = 6, PushAfter = 1.5;

        /// <summary>How far a squad's call carries, and how long the others take to answer it.</summary>
        public const double CalloutRange = 40, CalloutDelayMin = 0.5, CalloutDelayMax = 1.0;

        /// <summary>A shot is only taken when the target is within this many radians of where it faces.</summary>
        public const double FireCone = 0.26;

        /// <summary>What provokes a patrol: a gunshot this close, standing this close for this long, being aimed at for this long within this range.</summary>
        public const double ProvokeHearing = 25, ProvokeClose = 3, ProvokeCloseFor = 1.5, ProvokeAimFor = 1.0, ProvokeAimRange = 30;

        /// <summary>How far a fighter looks for cover, and how often a walker asks for a new way when its goal moves.</summary>
        public const double CoverSearch = 25, RepathDistance = 2;

        /// <summary>Seconds between a walker's looks for a short cut along its way.</summary>
        public const double ShortcutEvery = 0.25;

        readonly List<Enemy> _enemies = new List<Enemy>();
        readonly List<EnemySquad> _squads = new List<EnemySquad>();
        readonly List<EnemyEvent> _events = new List<EnemyEvent>();
        readonly Rng _rng;
        int _nextEnemy, _nextSquad;
        IEnemyWorld _world;

        public EnemySim(uint seed, CoverMap cover = null, EnemyPaths paths = null)
        {
            _rng = new Rng(seed);
            Cover = cover;
            Paths = paths;
        }

        /// <summary>The city's cover (null: fighters stand in the open) and its pavement routes (null: everyone walks straight).</summary>
        public CoverMap Cover { get; }
        public EnemyPaths Paths { get; }

        public IReadOnlyList<Enemy> Enemies => _enemies;
        public IReadOnlyList<EnemySquad> Squads => _squads;

        /// <summary>What happened in the last step (and since, through <see cref="Hear"/>, <see cref="ShotAt"/>, <see cref="Hit"/> and the rest); cleared at the start of each step.</summary>
        public IReadOnlyList<EnemyEvent> Events => _events;

        public Enemy Find(int id)
        {
            foreach (Enemy e in _enemies) if (e.Id == id) return e;
            return null;
        }

        public EnemySquad AddSquad(string tag, bool hostile = true)
        {
            var s = new EnemySquad(_nextSquad++, tag, hostile);
            _squads.Add(s);
            return s;
        }

        public Enemy Add(EnemySquad squad, EnemyArchetype archetype, V3 home, double homeYaw, IReadOnlyList<V3> patrol = null)
        {
            if (squad == null || !_squads.Contains(squad)) throw new ArgumentException("the squad is not this sim's", nameof(squad));
            if (archetype == null) throw new ArgumentNullException(nameof(archetype));
            var e = new Enemy(_nextEnemy++, archetype, squad, home, homeYaw, patrol);
            _enemies.Add(e);
            squad.Members.Add(e);
            return e;
        }

        /// <summary>Take a squad out of the world (the player went far away, or the fight is long over): its members and the cover they held.</summary>
        public void Remove(EnemySquad squad)
        {
            if (squad == null || !_squads.Remove(squad)) throw new ArgumentException("the squad is not this sim's", nameof(squad));
            foreach (Enemy e in squad.Members)
            {
                ReleaseCover(e);
                _enemies.Remove(e);
            }
        }

        double Range(double lo, double hi) => lo + (hi - lo) * _rng.Next();

        static double Flat(V3 a, V3 b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        static double YawTo(V3 from, V3 to) => Math.Atan2(to.X - from.X, to.Z - from.Z);

        static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        // ------------------------------------------------------------ what happens to them

        /// <summary>A gunshot at <paramref name="at"/>: those in earshot who are not already fighting turn to it and go to look; a patrol this close is provoked.</summary>
        public void Hear(V3 at)
        {
            foreach (Enemy e in _enemies)
            {
                if (e.State == EnemyState.Down || e.State == EnemyState.Engaged) continue;
                double d = Flat(e.Position, at);
                if (!e.Squad.Hostile)
                {
                    if (d <= ProvokeHearing) Provoke(e.Squad, at);
                    continue;
                }
                if (d > e.Archetype.HearingRange) continue;
                e.Awareness = Math.Max(e.Awareness, 0.5);
                e.Target = at;
                if (e.State != EnemyState.Searching) Enter(e, EnemyState.Suspicious);
            }
        }

        /// <summary>A shot passed close to or struck near <paramref name="enemy"/>, fired from <paramref name="from"/>: it knows it is in a fight and where from.</summary>
        public void ShotAt(Enemy enemy, V3 from)
        {
            if (enemy.State == EnemyState.Down) return;
            if (!enemy.Squad.Hostile) Provoke(enemy.Squad, from);
            Engage(enemy, from);
        }

        /// <summary>The player held their aim on <paramref name="enemy"/> for <paramref name="dt"/> from <paramref name="from"/>: a patrol stands for it only so long.</summary>
        public void AimedAt(Enemy enemy, V3 from, double dt)
        {
            if (enemy.State == EnemyState.Down || enemy.Squad.Hostile) return;
            if (Flat(enemy.Position, from) > ProvokeAimRange) return;
            enemy.Squad.Provocation += dt;
            if (enemy.Squad.Provocation >= ProvokeAimFor) Provoke(enemy.Squad, from);
        }

        /// <summary>The player's shot hit <paramref name="enemy"/> from <paramref name="from"/>. True when it killed.</summary>
        public bool Hit(Enemy enemy, EnemyHit hit, V3 from)
        {
            if (enemy.State == EnemyState.Down) return false;
            if (!enemy.Squad.Hostile) Provoke(enemy.Squad, from);
            if (enemy.Vitals.Take(hit))
            {
                Die(enemy);
                return true;
            }
            _events.Add(new EnemyEvent(EnemyEventKind.Hurt, enemy.Id, damage: hit.Damage));
            Engage(enemy, from);
            return false;
        }

        /// <summary>Damage that is not a shot (a quickhack's burn or shock). True when it killed.</summary>
        public bool Harm(Enemy enemy, double damage)
        {
            if (enemy.State == EnemyState.Down) return false;
            if (enemy.Vitals.Take(damage))
            {
                Die(enemy);
                return true;
            }
            _events.Add(new EnemyEvent(EnemyEventKind.Hurt, enemy.Id, damage: damage));
            return false;
        }

        void Provoke(EnemySquad squad, V3 at)
        {
            if (squad.Hostile) return;
            squad.Hostile = true;
            foreach (Enemy m in squad.Members)
            {
                if (m.State == EnemyState.Down) continue;
                _events.Add(new EnemyEvent(EnemyEventKind.Provoked, m.Id));
                Engage(m, at);
            }
        }

        void Die(Enemy e)
        {
            EndBurst(e);
            ReleaseCover(e);
            e.State = EnemyState.Down;
            e.Speed = 0;
            e.Crouched = false;
            _events.Add(new EnemyEvent(EnemyEventKind.Died, e.Id));
        }

        void Enter(Enemy e, EnemyState s)
        {
            if (e.State == s) return;
            if (s != EnemyState.Engaged)
            {
                EndBurst(e);
                ReleaseCover(e);
                e.Crouched = false;
            }
            e.State = s;
            e.StateTime = 0;
            _events.Add(new EnemyEvent(s == EnemyState.Suspicious ? EnemyEventKind.Suspicious : s == EnemyState.Engaged ? EnemyEventKind.Engaged
                : s == EnemyState.Searching ? EnemyEventKind.Searching : EnemyEventKind.Idle, e.Id));
        }

        void Engage(Enemy e, V3 at)
        {
            e.Target = at;
            e.Unseen = 0;
            e.Awareness = 1;
            e.CalloutIn = -1;
            if (e.State == EnemyState.Engaged) return;
            e.Reaction = e.Archetype.ReactionTime;
            Enter(e, EnemyState.Engaged);
            // the call goes out: the rest of the squad within earshot join in a moment later
            foreach (Enemy m in e.Squad.Members)
            {
                if (m == e || m.State == EnemyState.Down || m.State == EnemyState.Engaged || m.CalloutIn >= 0) continue;
                if (Flat(m.Position, e.Position) > CalloutRange) continue;
                m.CalloutIn = Range(CalloutDelayMin, CalloutDelayMax);
                m.Target = at;
            }
        }

        void EndBurst(Enemy e)
        {
            if (!e.InBurst) return;
            e.InBurst = false;
            e.Squad.Firing--;
        }

        void ReleaseCover(Enemy e)
        {
            if (e.Cover == null) return;
            Cover?.Release(e.Cover, e.Id);
            e.Cover = null;
        }

        // ------------------------------------------------------------ the step

        /// <summary>One fixed step of every enemy against where the player is now.</summary>
        public void Step(double dt, PlayerView player, IEnemyWorld world)
        {
            if (!(dt > 0)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "a step must move time on");
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _events.Clear();
            foreach (EnemySquad s in _squads)
            {
                if (!s.Hostile) s.Provocation = Math.Max(0, s.Provocation - dt * 0.5);
            }
            foreach (Enemy e in _enemies)
            {
                if (e.State == EnemyState.Down) continue;
                e.StateTime += dt;
                if (e.CalloutIn >= 0)
                {
                    e.CalloutIn -= dt;
                    if (e.CalloutIn < 0) Engage(e, e.Target);
                }
                if (!e.Squad.Hostile)
                {
                    StepNeutral(e, dt, player);
                    continue;
                }
                Enemy foe = FoeOf(e);
                PlayerView view = foe == null ? player : foe.AsTarget;
                if (foe == null && e.FoughtSquadmate && e.State == EnemyState.Engaged && player.Alive)
                {
                    // the fight with its own is over: it knows who started it, and where they were
                    e.Target = player.Feet;
                    e.Unseen = 0;
                }
                e.FoughtSquadmate = foe != null;
                if (foe == null) Perceive(e, dt, player, world);
                else PerceiveSquadmate(e, foe, view, world);
                switch (e.State)
                {
                    case EnemyState.Idle: StepIdle(e, dt); break;
                    case EnemyState.Suspicious: StepSuspicious(e, dt); break;
                    case EnemyState.Engaged: StepEngaged(e, dt, view, foe); break;
                    case EnemyState.Searching: StepSearching(e, dt); break;
                }
            }
        }

        /// <summary>
        /// Who <paramref name="e"/> fights: under berserk, its nearest living squadmate; beside a berserk squadmate within call, that one; otherwise (null) the player.
        /// </summary>
        Enemy FoeOf(Enemy e)
        {
            Enemy best = null;
            double bestD = double.MaxValue;
            foreach (Enemy m in e.Squad.Members)
            {
                if (m == e || m.State == EnemyState.Down) continue;
                if (!e.Berserk && !m.Berserk) continue;
                double d = Flat(e.Position, m.Position);
                if (!e.Berserk && d > CalloutRange) continue;
                if (d < bestD)
                {
                    bestD = d;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>A squadmate to fight is never lost: it knows where they are; it only needs to see them to shoot (and not if its optics are down).</summary>
        void PerceiveSquadmate(Enemy e, Enemy foe, PlayerView view, IEnemyWorld world)
        {
            e.SeesPlayer = CanSee(e, view, world);
            if (e.State != EnemyState.Engaged) Engage(e, foe.Position);
            e.Target = foe.Position;
            e.Unseen = 0;
        }

        /// <summary>A patrol not yet provoked: walks its beat, sees the player and lets them be, unless they stand in its face too long.</summary>
        void StepNeutral(Enemy e, double dt, PlayerView player)
        {
            StepIdle(e, dt);
            if (player.Alive && Flat(e.Position, player.Feet) <= ProvokeClose)
            {
                e.Close += dt;
                if (e.Close >= ProvokeCloseFor) Provoke(e.Squad, player.Feet);
            }
            else e.Close = 0;
        }

        void Perceive(Enemy e, double dt, PlayerView player, IEnemyWorld world)
        {
            e.SeesPlayer = player.Alive && CanSee(e, player, world);
            if (e.SeesPlayer)
            {
                if (e.State == EnemyState.Engaged)
                {
                    e.Target = player.Feet;
                    e.Unseen = 0;
                    return;
                }
                double d = Flat(e.Position, player.Feet);
                double rate = AwareFar + (AwareNear - AwareFar) * Math.Max(0, 1 - d / e.Archetype.SightRange);
                if (player.Crouched) rate *= CrouchHides;
                if (e.State == EnemyState.Searching) rate *= SearchingSees;   // already looking for someone
                e.Awareness = Math.Min(1, e.Awareness + rate * dt);
                if (e.Awareness >= 1) Engage(e, player.Feet);
                else
                {
                    e.Target = player.Feet;
                    if (e.State == EnemyState.Idle) Enter(e, EnemyState.Suspicious);
                    else e.StateTime = 0;   // still something there: keep looking
                }
            }
            else
            {
                if (e.State == EnemyState.Engaged) e.Unseen += dt;
                else e.Awareness = Math.Max(0, e.Awareness - AwareFade * dt);
            }
        }

        /// <summary>
        /// The player is in sight: close enough all round, or within range and the cone; and the line from eye to eye is clear. One behind cover looks from where it
        /// would step out to (it glances round the edge between bursts).
        /// </summary>
        public static bool CanSee(Enemy e, PlayerView player, IEnemyWorld world)
        {
            if (e.Blind) return false;
            V3 at = e.Cover != null && Flat(e.Position, e.Cover.Hide) < 0.5 ? e.Cover.Peek : e.Position;
            double d = Flat(at, player.Feet);
            if (d > e.Archetype.SightRange) return false;
            if (d > CloseSense)
            {
                double off = Math.Abs(Wrap(YawTo(at, player.Feet) - e.Yaw));
                if (off > e.Archetype.SightHalfAngle * Math.PI / 180) return false;
            }
            return world.Clear(new V3(at.X, at.Y + EyeHeight, at.Z), player.Eye);
        }

        void StepIdle(Enemy e, double dt)
        {
            if (e.Patrol.Count == 0)
            {
                if (Flat(e.Position, e.Home) > 0.3) Goto(e, e.Home, WalkSpeed, dt);
                else
                {
                    e.Speed = 0;
                    Turn(e, e.HomeYaw, dt);
                }
                return;
            }
            V3 next = e.Patrol[e.PatrolIndex];
            if (Flat(e.Position, next) < 0.5) e.PatrolIndex = (e.PatrolIndex + 1) % e.Patrol.Count;
            Goto(e, e.Patrol[e.PatrolIndex], WalkSpeed, dt);
        }

        void StepSuspicious(Enemy e, double dt)
        {
            // turn to it, then after a moment walk over to look
            if (e.StateTime < 1 || Flat(e.Position, e.Target) < 3)
            {
                e.Speed = 0;
                Turn(e, YawTo(e.Position, e.Target), dt);
            }
            else Goto(e, e.Target, WalkSpeed, dt);
            if (e.StateTime > SuspiciousFor && e.Awareness <= 0.01) Enter(e, EnemyState.Idle);
        }

        void StepSearching(Enemy e, double dt)
        {
            if (Flat(e.Position, e.Target) > 1) Goto(e, e.Target, WalkSpeed * 1.4, dt);
            else
            {
                // there: look round
                e.Speed = 0;
                e.Yaw = Wrap(e.Yaw + 1.2 * dt);
            }
            if (e.StateTime > SearchFor) Enter(e, EnemyState.Idle);
        }

        void StepEngaged(Enemy e, double dt, PlayerView player, Enemy foe)
        {
            if (e.Unseen > LoseAfter || !player.Alive)
            {
                e.Awareness = 0.5;
                Enter(e, EnemyState.Searching);
                return;
            }
            EnemyArchetype a = e.Archetype;
            bool fromCover = a.UsesCover && Cover != null && TakeCover(e, player);
            if (!fromCover)
            {
                // in the open: keep to its range while it sees the player; push on to the last place when it has not for a while
                double d = Flat(e.Position, e.Target);
                if (e.SeesPlayer)
                {
                    if (d > a.PreferredRange * 1.25) Goto(e, e.Target, a.RunSpeed, dt, face: false);
                    else if (d < a.PreferredRange * 0.6)
                    {
                        V3 away = e.Position + (e.Position - e.Target).Normalized * 2;
                        Goto(e, away, WalkSpeed, dt, face: false);
                    }
                    else e.Speed = 0;
                }
                else if (e.Unseen > PushAfter) Goto(e, e.Target, a.RunSpeed, dt, face: false);
                else e.Speed = 0;
            }
            else StepInCover(e, dt);
            Turn(e, YawTo(e.Position, e.Target), dt);

            if (e.Reaction > 0)
            {
                e.Reaction -= dt;
                return;
            }
            // from cover a burst starts only once it is there
            if (fromCover && !AtCover(e) && !e.InBurst) return;
            Fire(e, dt, player, foe);
        }

        static bool AtCover(Enemy e) => e.Cover != null && (Flat(e.Position, e.Cover.Hide) < 0.4 || Flat(e.Position, e.Cover.Peek) < 0.4);

        /// <summary>Keep the cover it has while it still hides it from the player, and it has not fired from it long enough; otherwise find another. False when there is none.</summary>
        bool TakeCover(Enemy e, PlayerView player)
        {
            CoverPoint c = e.Cover;
            bool keep = c != null && c.Faces(player.Eye) && e.BurstsHere < e.BurstsBeforeMoving;
            if (keep) return true;
            if (e.InBurst) return c != null;   // finish the burst first
            CoverPoint leaving = c;
            ReleaseCover(e);
            c = Cover.Find(e.Position, new V3(e.Target.X, e.Target.Y + player.EyeHeight, e.Target.Z), e.Archetype.PreferredRange, _world, CoverSearch, leaving);
            if (c == null && leaving != null && leaving.Faces(player.Eye)) c = leaving;   // nowhere else to go: stay
            if (c == null) return false;
            Cover.Take(c, e.Id);
            e.Cover = c;
            e.BurstsHere = 0;
            e.BurstsBeforeMoving = e.Archetype.BurstsPerCoverMin + (int)Math.Floor(_rng.Next() * (e.Archetype.BurstsPerCoverMax - e.Archetype.BurstsPerCoverMin + 1));
            return true;
        }

        /// <summary>Run to the cover; once there, hide (crouched behind low cover), and step out (or stand up) for each burst.</summary>
        void StepInCover(Enemy e, double dt)
        {
            CoverPoint c = e.Cover;
            if (!AtCover(e))
            {
                e.Crouched = false;
                Goto(e, c.Hide, e.Archetype.RunSpeed, dt, face: false);
                return;
            }
            V3 spot = e.InBurst ? c.Peek : c.Hide;
            Goto(e, spot, StepOutSpeed, dt, face: false);
            e.Crouched = c.Low && !e.InBurst;
        }

        /// <summary>Take turns in bursts at <paramref name="player"/>: the player, or the squadmate <paramref name="foe"/> it is fighting, whom a hit harms at once.</summary>
        void Fire(Enemy e, double dt, PlayerView player, Enemy foe)
        {
            EnemyArchetype a = e.Archetype;
            bool onTarget = e.SeesPlayer && Math.Abs(Wrap(YawTo(e.Position, player.Feet) - e.Yaw)) < FireCone;
            if (onTarget) e.AimTime += dt;
            else if (!e.InBurst) e.AimTime = 0;

            if (!e.InBurst)
            {
                if (e.Pause > 0)
                {
                    e.Pause -= dt;
                    return;
                }
                if (!e.SeesPlayer || e.Squad.Firing >= e.Squad.MaxFiring) return;
                if (e.Cover == null && !onTarget) return;
                e.InBurst = true;
                e.Squad.Firing++;
                e.BurstLeft = a.BurstMin + Math.Floor(_rng.Next() * (a.BurstMax - a.BurstMin + 1));
                // from cover, the first shot waits for the step out, or for standing up
                if (e.Cover != null)
                {
                    e.NextShot = Math.Max(StandUp, Flat(e.Position, e.Cover.Peek) / StepOutSpeed);
                    e.Crouched = false;
                }
                else e.NextShot = 0;
                _events.Add(new EnemyEvent(EnemyEventKind.BurstStart, e.Id));
            }

            e.NextShot -= dt;
            if (e.NextShot > 0) return;
            if (!onTarget || !_world.Clear(e.Eye, player.Eye))
            {
                // no line: the burst ends
                EndBurst(e);
                e.Pause = a.BurstPause * 0.5;
                return;
            }
            double chance = DamageModel.EnemyHitChance(a, Flat(e.Position, player.Feet), player.Speed, player.Crouched, e.AimTime / a.AimTime);
            bool hit = _rng.Next() < chance;
            _events.Add(new EnemyEvent(EnemyEventKind.Shot, e.Id, hit, hit ? a.ShotDamage : 0, e.Eye, player.Chest, foe?.Id ?? -1));
            if (hit && foe != null) Harm(foe, a.ShotDamage);
            e.BurstLeft--;
            e.NextShot = a.ShotInterval;
            if (e.BurstLeft <= 0)
            {
                EndBurst(e);
                e.BurstsHere++;
                e.Pause = a.BurstPause * Range(0.8, 1.2);
            }
        }

        static void Turn(Enemy e, double yaw, double dt)
        {
            double d = Wrap(yaw - e.Yaw);
            double step = TurnRate * dt;
            e.Yaw = Math.Abs(d) <= step ? Wrap(yaw) : Wrap(e.Yaw + Math.Sign(d) * step);
        }

        /// <summary>Walk or run towards a place: straight when the way is open (or there are no routes), round the blocks otherwise.</summary>
        void Goto(Enemy e, V3 goal, double speed, double dt, bool face = true)
        {
            V3 next = goal;
            if (Paths != null)
            {
                bool fresh = e.Path == null || Flat(e.PathGoal, goal) > RepathDistance;
                if (fresh)
                {
                    e.PathGoal = goal;
                    e.Path = _world.Walkable(e.Position, goal) ? null : Paths.Waypoints(e.Position, goal);
                    e.PathIndex = 0;
                }
                if (e.Path != null)
                {
                    while (e.PathIndex < e.Path.Count - 1 && Flat(e.Position, e.Path[e.PathIndex]) < 0.6) e.PathIndex++;
                    // a few times a second, cut the corner when the next point but one is in a straight line (each look is a ray in the game)
                    e.ShortcutIn -= dt;
                    if (e.ShortcutIn <= 0)
                    {
                        e.ShortcutIn = ShortcutEvery;
                        if (e.PathIndex < e.Path.Count - 1 && _world.Walkable(e.Position, e.Path[e.PathIndex + 1])) e.PathIndex++;
                    }
                    next = e.Path[e.PathIndex];
                }
            }
            StepTowards(e, next, speed, dt, face);
        }

        static void StepTowards(Enemy e, V3 to, double speed, double dt, bool face)
        {
            double d = Flat(e.Position, to);
            if (d < 1e-6)
            {
                e.Speed = 0;
                return;
            }
            double step = Math.Min(d, speed * dt);
            double dx = (to.X - e.Position.X) / d, dz = (to.Z - e.Position.Z) / d;
            e.Position = new V3(e.Position.X + dx * step, to.Y, e.Position.Z + dz * step);
            e.Speed = step / dt;
            if (face) Turn(e, Math.Atan2(dx, dz), dt);
        }
    }
}
