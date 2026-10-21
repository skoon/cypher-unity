using System;

namespace Cypher.Core
{
    /// <summary>
    /// The numbers behind the camera-following rain: how long a streak lives, how many to emit per second to
    /// keep a given density, how far it stretches. Pure so the particle setup in Unity stays a thin layer
    /// over arithmetic that has tests.
    /// </summary>
    public static class RainModel
    {
        /// <summary>Terminal-ish speed of the streaks, m/s. Rain looks fast; physical drops are about 9 m/s.</summary>
        public const double DefaultFallSpeed = 24;

        /// <summary>Depth of the rain volume above and below the camera, m.</summary>
        public const double DefaultHeight = 30;

        /// <summary>Visible length of a streak, m.</summary>
        public const double DefaultStreakLength = 0.9;

        /// <summary>Sideways drift, m/s, at full wind (the prototype's wind is 0..1, default 0.35).</summary>
        public const double MaxWindDrift = 4;

        public const double DefaultWind = 0.35;

        /// <summary>Seconds a streak takes to cross the volume from top to bottom.</summary>
        public static double Lifetime(double height, double fallSpeed)
        {
            if (!(height > 0)) throw new ArgumentOutOfRangeException(nameof(height), height, "height must be positive");
            if (!(fallSpeed > 0)) throw new ArgumentOutOfRangeException(nameof(fallSpeed), fallSpeed, "speed must be positive");
            return height / fallSpeed;
        }

        /// <summary>
        /// Particles per second that holds <c>maxParticles * intensity</c> streaks alive at once: in steady
        /// state the count is rate x lifetime.
        /// </summary>
        public static double EmissionRate(int maxParticles, double intensity, double lifetime)
        {
            if (maxParticles < 0) throw new ArgumentOutOfRangeException(nameof(maxParticles), maxParticles, "cap cannot be negative");
            if (!(intensity >= 0 && intensity <= 1)) throw new ArgumentOutOfRangeException(nameof(intensity), intensity, "intensity must be 0..1");
            if (!(lifetime > 0)) throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "lifetime must be positive");
            return maxParticles * intensity / lifetime;
        }

        /// <summary>Streaks alive at once for an emission rate and lifetime.</summary>
        public static double SteadyCount(double emissionRate, double lifetime) => emissionRate * lifetime;

        /// <summary>
        /// Unity stretches a billboard by speed x scale, so this is the scale that makes a streak travelling
        /// at fallSpeed come out streakLength metres long.
        /// </summary>
        public static double StretchVelocityScale(double streakLength, double fallSpeed)
        {
            if (!(streakLength > 0)) throw new ArgumentOutOfRangeException(nameof(streakLength), streakLength, "length must be positive");
            if (!(fallSpeed > 0)) throw new ArgumentOutOfRangeException(nameof(fallSpeed), fallSpeed, "speed must be positive");
            return streakLength / fallSpeed;
        }

        /// <summary>Horizontal drift in m/s for a wind of 0..1.</summary>
        public static double WindDrift(double wind)
        {
            if (!(wind >= 0 && wind <= 1)) throw new ArgumentOutOfRangeException(nameof(wind), wind, "wind must be 0..1");
            return wind * MaxWindDrift;
        }

        /// <summary>
        /// Height above the camera at which streaks are born, so the volume is centred a little above the
        /// eye and the camera sits two thirds of the way down it.
        /// </summary>
        public static double SpawnHeightAboveCamera(double height) => height / 3;
    }
}
