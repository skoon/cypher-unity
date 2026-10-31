using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// One fighter's body as the crowd renderer draws it: where it stands (now and at the step before, for interpolating), its walk, its look, and its combat pose. Kept by
    /// <see cref="EnemyBodies"/> from what the enemy is doing; read by <see cref="CrowdFrame"/> and, for the hit volumes, <see cref="PedestrianPose.HitShape"/>.
    /// </summary>
    public sealed class CombatBody
    {
        /// <summary>The enemy's id (<see cref="Enemy.Id"/>).</summary>
        public readonly int Id;

        public double X, Y, Z, PrevX, PrevY, PrevZ;
        public double Heading, PrevHeading, Phase, PrevPhase, Speed;
        public double LookYaw;
        public readonly double LookSeed;
        public readonly PedestrianAppearance Appearance;
        public readonly bool Visor;
        public readonly HeldWeapon Weapon;

        /// <summary>The pose: 0 to 1 each (see <see cref="PoseInput"/>), the pitch in radians, the side -1 to 1.</summary>
        public double Aim, AimPitch, Crouch, Recoil, Flinch, Side, Death;

        /// <summary>The level of detail it was last drawn at, for the renderer's hysteresis (255: not yet seen).</summary>
        internal byte Lod = 255;

        internal double LastHealth, DownFor;

        public CombatBody(int id, PedestrianAppearance appearance, bool visor, HeldWeapon weapon, double lookSeed)
        {
            Id = id;
            Appearance = appearance;
            Visor = visor;
            Weapon = weapon;
            LookSeed = lookSeed;
        }

        /// <summary>The pose's input at <paramref name="alpha"/> between the last two steps.</summary>
        public PoseInput Pose(double alpha, double time) => new PoseInput
        {
            PrevX = PrevX, PrevY = PrevY, PrevZ = PrevZ, X = X, Y = Y, Z = Z,
            PrevHeading = PrevHeading, Heading = Heading, PrevPhase = PrevPhase, Phase = Phase,
            Speed = Speed, Scale = Appearance.Scale, Build = Appearance.Build,
            LongCoat = Appearance.LongCoat, Hood = Appearance.Hood, Pack = Appearance.Pack, Umbrella = Appearance.Umbrella, Trim = Appearance.Trim,
            LookSeed = LookSeed, LookYaw = LookYaw, Time = time, Alpha = alpha,
            Aim = Aim, AimPitch = AimPitch, Crouch = Crouch, Recoil = Recoil, Flinch = Flinch, Side = Side, Death = Death,
            Weapon = Weapon, Visor = Visor,
        };
    }

    /// <summary>
    /// The gang look: dark reds and blacks, the trim always on and always the gang's hot red-orange, a glowing visor; the gunner in a coat or a hood, the heavy broad
    /// and bare-headed. Seeded by the enemy's id, so the same fighter looks the same each time it is seen.
    /// </summary>
    public static class EnemyLook
    {
        public static readonly uint[] Coat = { 0x2a1014, 0x1a1a1f, 0x301818, 0x121418, 0x24141a };
        public static readonly uint[] Pants = { 0x121318, 0x1a1616, 0x0e1014 };
        public const uint GangColour = 0xff3b2f;

        public static CombatBody Dress(Enemy enemy, uint seed)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            var rng = new Rng(seed ^ (uint)(enemy.Id * 2654435761u) ^ 0x9e3779b9u);
            EnemyArchetype a = enemy.Archetype;
            bool heavy = a == EnemyTable.Heavy;
            double scale = heavy ? 1.04 + rng.Next() * 0.05 : 0.95 + rng.Next() * 0.10;
            double build = heavy ? 1.24 + rng.Next() * 0.08 : 0.94 + rng.Next() * 0.12;
            bool longCoat = !heavy && rng.Next() < 0.5;
            bool hood = !heavy && rng.Next() < 0.5;
            uint skin = PedestrianFactory.Skin[(int)(rng.Next() * PedestrianFactory.Skin.Length)];
            uint coat = Coat[(int)(rng.Next() * Coat.Length)];
            uint pants = Pants[(int)(rng.Next() * Pants.Length)];
            double trimPower = 2.6 + rng.Next() * 0.8;
            var look = new PedestrianAppearance(scale, build, longCoat, false, hood, false, true, skin, coat, pants, GangColour, trimPower, 0);
            return new CombatBody(enemy.Id, look, true, WeaponOf(a), rng.Next() * 100);
        }

        /// <summary>The model in the hand for an archetype's gun.</summary>
        public static HeldWeapon WeaponOf(EnemyArchetype archetype)
        {
            switch (archetype.WeaponId)
            {
                case "wasp": return HeldWeapon.Smg;
                case "magnat": return HeldWeapon.Revolver;
                default: throw new ArgumentException($"no held model for the gun '{archetype.WeaponId}' ({archetype.Id})", nameof(archetype));
            }
        }
    }

    /// <summary>
    /// The enemies' bodies, kept in step with the brain (<see cref="EnemySim"/>): one per enemy in the world, made when it appears and dropped when it leaves. Each step the
    /// body takes the enemy's place and facing, walks at its speed, raises the gun while it fights (all the way while it fires), crouches when it does, kicks with each
    /// shot, flinches when its health drops (turning away from the player, who did it), and falls when it is down. Call after <see cref="EnemySim.Step"/>, which leaves the
    /// step's shots in its events.
    /// </summary>
    public sealed class EnemyBodies
    {
        /// <summary>The most fighters in the world at once (the largest encounter and the street patrols): the renderer and the hit volumes are sized for this.</summary>
        public const int Max = 24;

        /// <summary>How fast the gun comes up and goes down, and the crouch (a second's fraction a second).</summary>
        public const double AimUp = 6, AimDown = 3, CrouchRate = 5;

        /// <summary>How the gun rests while fighting but not firing: on the move, and standing.</summary>
        public const double AimMoving = 0.3, AimReady = 0.7, MovingSpeed = 2;

        public const double RecoilDecay = 18, FlinchDecay = 8;

        /// <summary>How long a fall takes, seconds.</summary>
        public const double FallSeconds = 0.85;

        public const double MaxPitch = 0.6;

        readonly uint _seed;
        readonly List<CombatBody> _bodies = new List<CombatBody>(Max);

        public EnemyBodies(uint seed)
        {
            _seed = seed;
        }

        public IReadOnlyList<CombatBody> All => _bodies;

        public CombatBody Find(int id)
        {
            for (int i = 0; i < _bodies.Count; i++) if (_bodies[i].Id == id) return _bodies[i];
            return null;
        }

        /// <param name="aimAt">The player's chest: where a raised gun points, and where hits come from.</param>
        public void Step(double dt, EnemySim sim, V3 aimAt)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (!(dt >= 0)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "time runs forward");

            for (int i = _bodies.Count - 1; i >= 0; i--) if (sim.Find(_bodies[i].Id) == null) _bodies.RemoveAt(i);

            IReadOnlyList<Enemy> enemies = sim.Enemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy e = enemies[i];
                CombatBody b = Find(e.Id);
                if (b == null)
                {
                    if (_bodies.Count >= Max) throw new InvalidOperationException($"more than {Max} fighters in the world");
                    b = EnemyLook.Dress(e, _seed);
                    b.X = b.PrevX = e.Position.X; b.Y = b.PrevY = e.Position.Y; b.Z = b.PrevZ = e.Position.Z;
                    b.Heading = b.PrevHeading = e.Yaw;
                    b.LastHealth = e.Vitals.Health;
                    _bodies.Add(b);
                }
                Follow(b, e, dt, aimAt);
            }

            IReadOnlyList<EnemyEvent> events = sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind != EnemyEventKind.Shot) continue;
                CombatBody b = Find(events[i].Enemy);
                if (b != null) b.Recoil = 1;
            }
        }

        static void Follow(CombatBody b, Enemy e, double dt, V3 aimAt)
        {
            b.PrevX = b.X; b.PrevY = b.Y; b.PrevZ = b.Z;
            b.PrevHeading = b.Heading; b.PrevPhase = b.Phase;
            b.X = e.Position.X; b.Y = e.Position.Y; b.Z = e.Position.Z;
            b.Heading = e.Yaw;
            b.Speed = e.Speed;
            b.Phase += Math.PI * (e.Speed / (0.80 * b.Appearance.Scale)) * dt;
            if (b.Phase > Math.PI * 2)
            {
                b.Phase -= Math.PI * 2;
                b.PrevPhase -= Math.PI * 2;
            }

            bool down = e.State == EnemyState.Down;
            bool fighting = e.State == EnemyState.Engaged || e.State == EnemyState.Searching;
            double aim = down ? 0 : e.Firing ? 1 : fighting ? (e.Speed > MovingSpeed ? AimMoving : AimReady) : 0;
            b.Aim = Approach(b.Aim, aim, (aim > b.Aim ? AimUp : AimDown) * dt);
            b.Crouch = Approach(b.Crouch, e.Crouched ? 1 : 0, CrouchRate * dt);

            double dx = aimAt.X - e.Position.X, dz = aimAt.Z - e.Position.Z;
            double eye = (b.Crouch > 0.5 ? CoverMap.CrouchEye : EnemySim.EyeHeight) * b.Appearance.Scale;
            double pitch = Math.Atan2(aimAt.Y - (e.Position.Y + eye), Math.Max(0.5, Math.Sqrt(dx * dx + dz * dz)));
            b.AimPitch = Math.Max(-MaxPitch, Math.Min(MaxPitch, pitch));

            b.Recoil *= Math.Exp(-RecoilDecay * dt);
            b.Flinch *= Math.Exp(-FlinchDecay * dt);
            double health = e.Vitals.Health;
            if (health < b.LastHealth)
            {
                b.Flinch = 1;
                b.Side = Math.Sin(CrowdSim.ShortestAngle(e.Yaw, Math.Atan2(dx, dz)));
            }
            b.LastHealth = health;

            if (down)
            {
                b.DownFor += dt;
                b.Death = Math.Min(1, b.DownFor / FallSeconds);
            }
        }

        static double Approach(double v, double target, double step) => v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);
    }
}
