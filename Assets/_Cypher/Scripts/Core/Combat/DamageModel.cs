using System;

namespace Cypher.Core
{
    /// <summary>The player's shot as it lands on an enemy: the damage after armour, what it was before, and whether it was a critical or a headshot.</summary>
    public readonly struct EnemyHit
    {
        public readonly double Damage, Raw;
        public readonly bool Critical, Head;

        public EnemyHit(double damage, double raw, bool critical, bool head)
        {
            Damage = damage; Raw = raw; Critical = critical; Head = head;
        }
    }

    /// <summary>The player's numbers that change what a shot does: the RPG's derived ranged damage, crit chance and crit damage.</summary>
    public readonly struct ShooterStats
    {
        public readonly double RangedDamage, CritChance, CritDamage;

        public ShooterStats(double rangedDamage, double critChance, double critDamage)
        {
            if (!(rangedDamage > 0)) throw new ArgumentOutOfRangeException(nameof(rangedDamage), rangedDamage, "ranged damage must be positive");
            if (!(critChance >= 0 && critChance <= 1)) throw new ArgumentOutOfRangeException(nameof(critChance), critChance, "crit chance is a probability");
            if (!(critDamage >= 1)) throw new ArgumentOutOfRangeException(nameof(critDamage), critDamage, "crit damage is a multiplier of at least 1");
            RangedDamage = rangedDamage; CritChance = critChance; CritDamage = critDamage;
        }
    }

    /// <summary>
    /// Damage both ways. The player's gun does its damage (times the rail's charge) scaled by the RPG's ranged damage, a crit roll and the head, less the enemy's armour by the
    /// same rule the RPG applies to the player. An enemy's shot hits by chance (accuracy, distance, how the target moves, how settled the aim is) and, when it does, goes to
    /// <see cref="RpgCore.Damage"/>, where the player's armour applies. Rolls are passed in, so the caller's seeded stream decides them.
    /// </summary>
    public static class DamageModel
    {
        /// <summary>How much a target running flat out (6.5 m/s and over) is harder to hit, and a crouching one.</summary>
        public const double MovingTargetFactor = 0.5, CrouchFactor = 0.8;

        /// <summary>The share of a settled shot's accuracy that the first shot of a burst has.</summary>
        public const double UnsettledAim = 0.6;

        /// <summary>The RPG's armour rule: damage times 100 / (100 + armour).</summary>
        public static double Mitigate(double raw, double armor)
        {
            if (double.IsNaN(raw) || raw < 0) throw new ArgumentOutOfRangeException(nameof(raw), raw, "damage must be at least 0");
            if (double.IsNaN(armor) || armor < 0) throw new ArgumentOutOfRangeException(nameof(armor), armor, "armour must be at least 0");
            return raw * (100 / (100 + armor));
        }

        /// <summary>The player's shot on an enemy. <paramref name="critRoll"/> is a draw in [0, 1): below the crit chance is a critical.</summary>
        public static EnemyHit PlayerShot(WeaponConfig gun, double power, ShooterStats shooter, EnemyArchetype target, bool head, double critRoll)
        {
            if (gun == null) throw new ArgumentNullException(nameof(gun));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!(power >= 0)) throw new ArgumentOutOfRangeException(nameof(power), power, "power must be at least 0");
            if (!(critRoll >= 0 && critRoll < 1)) throw new ArgumentOutOfRangeException(nameof(critRoll), critRoll, "a roll is in [0, 1)");
            bool crit = critRoll < shooter.CritChance;
            double raw = gun.Damage * power * shooter.RangedDamage * (crit ? shooter.CritDamage : 1) * (head ? target.HeadMultiplier : 1);
            return new EnemyHit(Mitigate(raw, target.Armor), raw, crit, head);
        }

        /// <summary>
        /// The chance an enemy's shot hits the player: its accuracy, halved at its half-accuracy range (falling as 1 / (1 + (d / half)²)), less against a target moving fast or
        /// crouching, and less until its aim has settled (<paramref name="aimed"/>, 0 to 1, how much of its aim time has passed).
        /// </summary>
        public static double EnemyHitChance(EnemyArchetype shooter, double distance, double targetSpeed, bool targetCrouched, double aimed)
        {
            if (shooter == null) throw new ArgumentNullException(nameof(shooter));
            if (!(distance >= 0)) throw new ArgumentOutOfRangeException(nameof(distance), distance, "distance must be at least 0");
            if (!(targetSpeed >= 0)) throw new ArgumentOutOfRangeException(nameof(targetSpeed), targetSpeed, "speed must be at least 0");
            double d = distance / shooter.HalfAccuracyRange;
            double falloff = 1 / (1 + d * d);
            double moving = 1 - (1 - MovingTargetFactor) * Math.Min(1, targetSpeed / 6.5);
            double settle = UnsettledAim + (1 - UnsettledAim) * Math.Max(0, Math.Min(1, aimed));
            double p = shooter.Accuracy * falloff * moving * (targetCrouched ? CrouchFactor : 1) * settle;
            return Math.Max(0, Math.Min(1, p));
        }
    }

    /// <summary>An enemy's health: hits take it down, the hit that empties it is the kill, and nothing lands after that.</summary>
    public sealed class EnemyVitals
    {
        public readonly EnemyArchetype Archetype;
        public double Health { get; private set; }
        public bool Dead => Health <= 0;

        public EnemyVitals(EnemyArchetype archetype)
        {
            Archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
            Health = archetype.MaxHealth;
        }

        /// <summary>Apply a hit; true when it was the killing one. A hit on the dead does nothing.</summary>
        public bool Take(EnemyHit hit)
        {
            if (Dead) return false;
            Health = Math.Max(0, Health - hit.Damage);
            return Dead;
        }

        /// <summary>Damage that is not a gunshot (a quickhack's burn or shock), already after whatever applies; true when it killed.</summary>
        public bool Take(double damage)
        {
            if (double.IsNaN(damage) || damage < 0) throw new ArgumentOutOfRangeException(nameof(damage), damage, "damage must be at least 0");
            if (Dead) return false;
            Health = Math.Max(0, Health - damage);
            return Dead;
        }
    }
}
