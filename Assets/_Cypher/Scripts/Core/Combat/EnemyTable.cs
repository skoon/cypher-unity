using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// A kind of enemy: how much it takes to put down, how it shoots, how far it sees and hears, where it likes to fight from, and what it pays. The numbers are first
    /// guesses (the prototype has no enemies to copy), kept in one table to be tuned by playing.
    /// </summary>
    public sealed class EnemyArchetype
    {
        public string Id, Name;

        /// <summary>Health, and armour as the RPG reckons it for the player: damage is scaled by 100 / (100 + armour).</summary>
        public double MaxHealth, Armor;

        /// <summary>What a shot to the head is worth against a shot to the body.</summary>
        public double HeadMultiplier;

        /// <summary>The player's gun this one's weapon is modelled on (sound, flash, tracer); its own damage per hit is <see cref="ShotDamage"/>.</summary>
        public string WeaponId;

        public double ShotDamage;

        /// <summary>A burst: this many shots, this many seconds apart, then a pause of this many seconds.</summary>
        public int BurstMin, BurstMax;
        public double ShotInterval, BurstPause;

        /// <summary>
        /// Chance to hit a still, standing target close by, before distance and movement; it halves at <see cref="HalfAccuracyRange"/> metres. The first shot of a burst
        /// is less sure: aim settles over <see cref="AimTime"/> seconds.
        /// </summary>
        public double Accuracy, HalfAccuracyRange, AimTime;

        /// <summary>Seconds from first seeing the player to opening fire.</summary>
        public double ReactionTime;

        /// <summary>Sight: metres and the half-angle of the cone in degrees; hearing: how far a gunshot carries to it.</summary>
        public double SightRange, SightHalfAngle, HearingRange;

        /// <summary>The distance it tries to fight from, in metres, and how fast it runs to get there.</summary>
        public double PreferredRange, RunSpeed;

        /// <summary>Whether it fights from cover (hides, steps out for a burst, moves on after a few), or stands in the open and comes on.</summary>
        public bool UsesCover;

        /// <summary>Bursts from one cover before it moves to another.</summary>
        public int BurstsPerCoverMin, BurstsPerCoverMax;

        /// <summary>What a kill pays: XP, street cred, and the tier of the loot it drops.</summary>
        public double Xp, Cred, LootTier;
    }

    /// <summary>The two kinds of enemy (the user's choice for M5): the gunner, who holds cover and lays down fire, and the heavy, armoured, who pushes in with a revolver and has a weak head.</summary>
    public static class EnemyTable
    {
        public static readonly EnemyArchetype Gunner = new EnemyArchetype
        {
            Id = "gunner", Name = "GUNNER",
            MaxHealth = 110, Armor = 10, HeadMultiplier = 2.0,
            WeaponId = "wasp", ShotDamage = 6,
            BurstMin = 3, BurstMax = 6, ShotInterval = 0.11, BurstPause = 1.1,
            Accuracy = 0.55, HalfAccuracyRange = 22, AimTime = 0.6,
            ReactionTime = 0.55,
            SightRange = 45, SightHalfAngle = 60, HearingRange = 55,
            PreferredRange = 16, RunSpeed = 3.5, UsesCover = true, BurstsPerCoverMin = 3, BurstsPerCoverMax = 6,
            Xp = 50, Cred = 2, LootTier = 1,
        };

        public static readonly EnemyArchetype Heavy = new EnemyArchetype
        {
            Id = "heavy", Name = "HEAVY",
            MaxHealth = 320, Armor = 60, HeadMultiplier = 3.0,
            WeaponId = "magnat", ShotDamage = 22,
            BurstMin = 1, BurstMax = 2, ShotInterval = 0.45, BurstPause = 1.4,
            Accuracy = 0.7, HalfAccuracyRange = 16, AimTime = 0.4,
            ReactionTime = 0.8,
            SightRange = 40, SightHalfAngle = 50, HearingRange = 55,
            PreferredRange = 8, RunSpeed = 2.8,
            Xp = 120, Cred = 5, LootTier = 2,
        };

        public static readonly IReadOnlyList<EnemyArchetype> All = new[] { Gunner, Heavy };

        public static EnemyArchetype Get(string id)
        {
            foreach (EnemyArchetype a in All) if (a.Id == id) return a;
            throw new ArgumentOutOfRangeException(nameof(id), id, "no such enemy");
        }
    }
}
