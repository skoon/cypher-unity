using System;

namespace Cypher.Core
{
    /// <summary>
    /// How the crowd behaves around roads. The prototype's rules are kept as a preset because the golden tests pin the port to them; the game plays by <see cref="City"/>.
    /// </summary>
    public sealed class CrowdRules
    {
        /// <summary>The prototype's: a crossing is weighted 0.45 against walking on, which it never wins, and people may stop to idle wherever they are.</summary>
        public static readonly CrowdRules Prototype = new CrowdRules(0.45, false, false);

        /// <summary>The game's: crossings are likelier (about one arrival at a kerb midpoint in ten), nobody stops to idle on a road, and everyone looks before stepping off the pavement.</summary>
        public static readonly CrowdRules City = new CrowdRules(2.0, true, true);

        /// <summary>
        /// What a crossing's score is multiplied by when someone chooses the way on at a kerb. Walking on scores 0.55 to 1.45; a crosswalk, at right angles to the way they walk,
        /// 0.30 x (0.55 to 1.45) x this. At 0.45 it can never win; at 2.0 it wins about one arrival in ten; at 3.0 about four in ten.
        /// </summary>
        public double CrossingWeight { get; }

        /// <summary>Nobody turns to stand idle while on the asphalt.</summary>
        public bool NoIdleOnRoad { get; }

        /// <summary>
        /// Someone who is going to cross checks the road not only when they arrive at the kerb but every step until they leave the pavement, and stands still while it is busy. The
        /// prototype checks once, at the node, so a person who is made facing a crossing (by being put down there) or who has a car appear in the second it takes them to reach the kerb
        /// walks out in front of it.
        /// </summary>
        public bool LookBeforeStepping { get; }

        public CrowdRules(double crossingWeight, bool noIdleOnRoad, bool lookBeforeStepping = false)
        {
            if (!(crossingWeight > 0) || double.IsInfinity(crossingWeight)) throw new ArgumentOutOfRangeException(nameof(crossingWeight), crossingWeight, "the crossing weight must be positive and finite");
            CrossingWeight = crossingWeight;
            NoIdleOnRoad = noIdleOnRoad;
            LookBeforeStepping = lookBeforeStepping;
        }
    }
}
