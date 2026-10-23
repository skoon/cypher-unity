namespace Cypher.Core
{
    /// <summary>
    /// How the traffic behaves where the prototype's behaviour is a fault. The prototype's rules are kept as a preset because the golden tests pin the port to them; the game plays
    /// by <see cref="City"/>.
    /// </summary>
    public sealed class TrafficRules
    {
        public static readonly TrafficRules Prototype = new TrafficRules(false, false, false);

        public static readonly TrafficRules City = new TrafficRules(true, true, true);

        /// <summary>
        /// A vehicle does not steer for a waypoint within a metre: it is where it means to be. The prototype steers for anything beyond 5 cm, so a vehicle waiting at a signal, stopped a
        /// few centimetres short of or past its waypoint, swings round on the spot to face it.
        /// </summary>
        public bool HoldHeadingNearWaypoint { get; }

        /// <summary>
        /// A waypoint has been passed when the vehicle is beyond it along the road it is on. The prototype asks whether the waypoint is behind the way the vehicle happens to be pointing,
        /// so a vehicle that has overshot a signal's line at speed and turned back, or has spun, finds the next waypoint behind it too, and the next, skips through the junctions of half
        /// the grid in a few steps and drives off across the blocks (4 of 96 vehicles in 3 minutes).
        /// </summary>
        public bool ProgressAlongTheRoad { get; }

        /// <summary>
        /// The player in the road is stopped for like anyone else on it: the vehicle plans its stop 40 m ahead and comes to rest 3 m short. The prototype's rule begins only 14 m ahead,
        /// which is too late for anything faster than about 13 m/s.
        /// </summary>
        public bool StopForPlayerLikePeople { get; }

        public TrafficRules(bool holdHeadingNearWaypoint, bool progressAlongTheRoad, bool stopForPlayerLikePeople)
        {
            HoldHeadingNearWaypoint = holdHeadingNearWaypoint;
            ProgressAlongTheRoad = progressAlongTheRoad;
            StopForPlayerLikePeople = stopForPlayerLikePeople;
        }
    }
}
