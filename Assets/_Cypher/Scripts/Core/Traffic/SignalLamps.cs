using System;

namespace Cypher.Core
{
    /// <summary>
    /// What colour each lamp of a signal head shows (the prototype's _paintSignals), as linear values that bloom: a lit lamp is well above 1, an unlit one a dull speck.
    /// The signal shader works the same out from the same cycle; this is its reference.
    /// </summary>
    public static class SignalLamps
    {
        public const int Red = 0, Amber = 1, Green = 2;

        /// <summary>The glow of a lamp that is off.</summary>
        public const double Off = 0.045;

        /// <summary>
        /// The lamp's colour. A head governs traffic along <paramref name="headAxis"/> (0 for X, 1 for Z); <paramref name="greenAxis"/> and <paramref name="amberPhase"/> are the cycle's
        /// (see <see cref="SignalClock"/>). The head's axis has green when it is the cycle's axis and the cycle is not in amber, amber in amber, and red otherwise.
        /// </summary>
        public static (double r, double g, double b) Colour(int headAxis, int lamp, int greenAxis, bool amberPhase)
        {
            if (lamp < Red || lamp > Green) throw new ArgumentOutOfRangeException(nameof(lamp), lamp, "a head has three lamps: red, amber, green");
            if (headAxis != 0 && headAxis != 1) throw new ArgumentOutOfRangeException(nameof(headAxis), headAxis, "the axis is 0 (along X) or 1 (along Z)");
            bool mine = headAxis == greenAxis;
            bool red = !mine, amber = mine && amberPhase, go = mine && !amberPhase;
            switch (lamp)
            {
                case Red: return red ? (4.2, 0.08, 0.06) : (Off, Off * 0.4, Off * 0.4);
                case Amber: return amber ? (4.0, 1.8, 0.12) : (Off, Off * 0.7, Off * 0.3);
                default: return go ? (0.15, 4.0, 1.1) : (Off * 0.4, Off, Off * 0.6);
            }
        }
    }
}
