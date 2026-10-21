using System;

namespace Cypher.Core
{
    /// <summary>
    /// Yaw and pitch for a first-person camera. Radians; yaw 0 faces +Z and grows toward +X,
    /// pitch 0 is level and positive looks up. Pitch is clamped just short of straight up or
    /// down so the view never flips.
    /// </summary>
    public sealed class LookState
    {
        /// <summary>Prototype default: radians per mouse pixel.</summary>
        public const double DefaultSensitivity = 0.0022;

        public const double PitchLimit = Math.PI / 2 - 0.02;

        public double Yaw { get; private set; }
        public double Pitch { get; private set; }

        public void Apply(double lookX, double lookY, double sensitivity = DefaultSensitivity, bool invertY = false)
        {
            if (!(sensitivity > 0)) throw new ArgumentOutOfRangeException(nameof(sensitivity), sensitivity, "sensitivity must be positive");

            Yaw += lookX * sensitivity;
            Pitch += lookY * sensitivity * (invertY ? -1 : 1);
            Pitch = Math.Max(-PitchLimit, Math.Min(PitchLimit, Pitch));
        }

        public void Set(double yaw, double pitch)
        {
            Yaw = yaw;
            Pitch = Math.Max(-PitchLimit, Math.Min(PitchLimit, pitch));
        }
    }
}
