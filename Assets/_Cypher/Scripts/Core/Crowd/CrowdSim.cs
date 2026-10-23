using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>Whether a road is too busy to step into: the seam through which traffic makes pedestrians wait at a kerb.</summary>
    public interface ITrafficQuery
    {
        /// <summary>
        /// True when the road is too busy for someone at (<paramref name="fromX"/>, <paramref name="fromZ"/>) to walk across to (<paramref name="toX"/>, <paramref name="toZ"/>)
        /// at <paramref name="walkSpeed"/> metres a second. <paramref name="crossing"/> says which way the traffic across it runs.
        /// </summary>
        bool RoadBusy(double fromX, double fromZ, double toX, double toZ, Crossing crossing, double walkSpeed);
    }

    /// <summary>Who is out on the carriageway, for the traffic to stop for.</summary>
    public interface IPedestrianQuery
    {
        /// <summary>The people on the asphalt now, as opposed to the pavement and its kerb slab. Do not modify.</summary>
        IReadOnlyList<Pedestrian> OnRoad { get; }
    }

    /// <summary>
    /// The pedestrian simulation, a port of the logic in Crowd.js. People follow the kerb-node graph of <see cref="CityNav"/>, turning at corners and crossing roads at
    /// the kerb crosswalks; they slow for each other (a uniform-grid neighbour query, yielding only to people ahead), step aside for the player, stop and idle, and
    /// some hurry. Everyone beyond <see cref="CullRadius"/> of the camera is recycled to a node in the spawn band round the player. The pool is made for the top
    /// quality tier; a smaller tier activates a prefix of it.
    /// <para>
    /// The random draws are consumed in the prototype's order, so a seed gives the same crowd, and the same steps give the same positions.
    /// Positions and headings are in the prototype's coordinates: heading 0 faces +Z and a positive heading turns toward +X.
    /// </para>
    /// </summary>
    public sealed class CrowdSim : IPedestrianQuery
    {
        public const uint DefaultSeed = 80553;

        /// <summary>People farther than this from the camera are recycled.</summary>
        public const double CullRadius = 148;

        /// <summary>Recycled people appear this far from the player: it must stay under <see cref="CullRadius"/> or they would recycle the step they appear.</summary>
        public const double SpawnMin = 112, SpawnMax = 144;

        /// <summary>The neighbour grid's cell, metres.</summary>
        public const double HashCell = 4.2;

        /// <summary>People closer than this to the player can be given a collider.</summary>
        public const double ProxyRange = 6;

        /// <summary>Nobody ends up closer to the player than this.</summary>
        public const double KeepOut = 0.62;

        readonly CityNav _nav;
        readonly Rng _rng;
        readonly CrowdRules _rules;
        readonly List<Pedestrian> _pool = new List<Pedestrian>();
        readonly List<Pedestrian> _active = new List<Pedestrian>();
        readonly List<Pedestrian> _onRoad = new List<Pedestrian>();
        readonly Dictionary<int, List<Pedestrian>> _grid = new Dictionary<int, List<Pedestrian>>();
        readonly Stack<List<Pedestrian>> _spareBuckets = new Stack<List<Pedestrian>>();
        readonly List<(Pedestrian ped, double d2)> _near = new List<(Pedestrian, double)>();

        public CityNav Nav => _nav;

        /// <summary>How people behave around roads.</summary>
        public CrowdRules Rules => _rules;

        /// <summary>The people on the asphalt as of the last step.</summary>
        public IReadOnlyList<Pedestrian> OnRoad => _onRoad;

        /// <summary>Everyone the crowd was built with, active or not.</summary>
        public IReadOnlyList<Pedestrian> Pool => _pool;

        /// <summary>The people being simulated and drawn now.</summary>
        public IReadOnlyList<Pedestrian> Active => _active;

        /// <summary>How many people there should be (a quality tier's budget). The simulation moves toward it a step at a time.</summary>
        public int TargetCount { get; set; }

        /// <summary>Whether a person can be seen. People in view are never retired or introduced; null means everyone is out of view.</summary>
        public Func<Pedestrian, bool> IsVisible { get; set; }

        CrowdSim(CityNav nav, uint seed, CrowdRules rules)
        {
            _nav = nav;
            _rng = new Rng(seed);
            _rules = rules;
        }

        /// <summary>
        /// Build the pool, seed everyone onto the pavements round (<paramref name="playerX"/>, <paramref name="playerZ"/>) and activate the first <paramref name="activeCount"/>.
        /// <paramref name="rules"/> defaults to the prototype's.
        /// </summary>
        public static CrowdSim Create(CityNav nav, int poolSize, int activeCount, double playerX, double playerZ, uint seed = DefaultSeed, CrowdRules rules = null)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            if (poolSize < 1) throw new ArgumentOutOfRangeException(nameof(poolSize), poolSize, "the pool needs at least one person");
            if (activeCount < 0 || activeCount > poolSize) throw new ArgumentOutOfRangeException(nameof(activeCount), activeCount, $"the active count must be 0 to {poolSize}");

            var sim = new CrowdSim(nav, seed, rules ?? CrowdRules.Prototype);
            for (int i = 0; i < poolSize; i++) sim._pool.Add(PedestrianFactory.Create(sim._rng, i));
            foreach (Pedestrian p in sim._pool) sim.Seed(p, playerX, playerZ);
            for (int i = 0; i < activeCount; i++) sim._active.Add(sim._pool[i]);
            sim.TargetCount = activeCount;
            return sim;
        }

        // ---------------------------------------------------------------- seeding, targets, recycling

        void Seed(Pedestrian p, double playerX, double playerZ)
        {
            int node = _nav.NodeInBand(_rng, playerX, playerZ, 4, CullRadius * 0.92);
            p.Node = node;
            p.PrevNode = -1;
            p.X = _nav.NodeX(node) + (_rng.Next() * 2 - 1) * 0.8;
            p.Z = _nav.NodeZ(node) + (_rng.Next() * 2 - 1) * 0.8;
            p.Y = _nav.WalkY;
            PickTarget(p);
            p.Heading = Math.Atan2(p.Tx - p.X, p.Tz - p.Z);
            p.State = _rng.Next() < 0.08 ? PedState.Idle : (p.Hurried && _rng.Next() < 0.5 ? PedState.Hurry : PedState.Walk);
            p.Speed = p.State == PedState.Idle ? 0 : p.Cruise;
            p.PrevX = p.X; p.PrevZ = p.Z; p.PrevY = p.Y;
            p.PrevHeading = p.Heading; p.PrevPhase = p.Phase;
        }

        /// <summary>Choose the next node to walk to: straight on is preferred, crossings are disliked, the node just left is skipped.</summary>
        void PickTarget(Pedestrian p)
        {
            IReadOnlyList<int> adj = _nav.Neighbours(p.Node);
            IReadOnlyList<Crossing> cross = _nav.NeighbourCrossings(p.Node);
            double hx = Math.Sin(p.Heading), hz = Math.Cos(p.Heading);

            int best = -1;
            double bestW = -1;
            for (int k = 0; k < adj.Count; k++)
            {
                int j = adj[k];
                if (j == p.PrevNode && adj.Count > 1) continue;
                double dx = _nav.NodeX(j) - _nav.NodeX(p.Node);
                double dz = _nav.NodeZ(j) - _nav.NodeZ(p.Node);
                double len = Length(dx, dz);
                if (len == 0) len = 1;
                double align = dx / len * hx + dz / len * hz;
                double w = (0.30 + 0.70 * Math.Max(0, align)) * (0.55 + _rng.Next() * 0.9);
                if (cross[k] != Crossing.None) w *= _rules.CrossingWeight;
                if (w > bestW) { bestW = w; best = k; }
            }
            if (best < 0) best = 0;

            int target = adj[best];
            p.PrevNode = p.Node;
            p.Node = target;
            p.Crossing = cross[best];
            // walk to one side of the centreline so people do not file single-lane
            double band = p.Crossing == Crossing.None ? _nav.WalkBand : _nav.AsphaltHalf * 0.45;
            p.Lat = Clamp(p.Lat + (_rng.Next() * 2 - 1) * 0.5, -band, band);
            double ddx = _nav.NodeX(target) - _nav.NodeX(p.PrevNode);
            double ddz = _nav.NodeZ(target) - _nav.NodeZ(p.PrevNode);
            double dl = Length(ddx, ddz);
            if (dl == 0) dl = 1;
            p.Tx = _nav.NodeX(target) + -ddz / dl * p.Lat;
            p.Tz = _nav.NodeZ(target) + ddx / dl * p.Lat;
        }

        /// <summary>Put someone back on a node in the spawn band round (<paramref name="px"/>, <paramref name="pz"/>), heading for a neighbour.</summary>
        void Recycle(Pedestrian p, double px, double pz)
        {
            int node = _nav.NodeInBand(_rng, px, pz, SpawnMin, SpawnMax);
            p.Node = node;
            p.PrevNode = -1;
            p.X = _nav.NodeX(node);
            p.Z = _nav.NodeZ(node);
            p.Y = _nav.WalkY;
            p.Heading = _rng.Next() * Math.PI * 2;
            PickTarget(p);
            p.Heading = Math.Atan2(p.Tx - p.X, p.Tz - p.Z);
            p.State = _rng.Next() < 0.06 ? PedState.Idle : (_rng.Next() < 0.2 ? PedState.Hurry : PedState.Walk);
            p.Speed = p.Cruise;
            p.WaitTimer = 0;
            p.PrevX = p.X; p.PrevZ = p.Z; p.PrevY = p.Y;
            p.PrevHeading = p.Heading;
        }

        // ---------------------------------------------------------------- stepping

        /// <summary>Advance everyone by <paramref name="dt"/> seconds. The camera decides who is recycled; the player decides who steps aside.</summary>
        public void Step(double dt, double camX, double camZ, double playerX, double playerZ, ITrafficQuery traffic = null)
        {
            if (!(dt >= 0)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "a step cannot be negative");
            ResizeToTarget(camX, camZ);
            RebuildGrid();

            double cull2 = CullRadius * CullRadius;
            for (int i = 0; i < _active.Count; i++)
            {
                Pedestrian p = _active[i];
                p.PrevX = p.X; p.PrevZ = p.Z; p.PrevY = p.Y;
                p.PrevHeading = p.Heading; p.PrevPhase = p.Phase;

                double dxc = p.X - camX, dzc = p.Z - camZ;
                if (dxc * dxc + dzc * dzc > cull2)
                {
                    Recycle(p, playerX, playerZ);
                    continue;
                }

                UpdateState(p, dt);

                double targetDx = p.Tx - p.X, targetDz = p.Tz - p.Z;
                double targetD = Length(targetDx, targetDz);

                if (targetD < 0.7)
                {
                    if (p.WaitTimer > 0)
                    {
                        p.WaitTimer -= dt;
                        p.Speed *= 0.80;
                        Integrate(p, dt, playerX, playerZ);
                        continue;
                    }
                    int keepNode = p.Node, keepPrev = p.PrevNode;
                    double keepTx = p.Tx, keepTz = p.Tz;
                    PickTarget(p);
                    // look both ways: hold at the kerb while a vehicle is bearing down
                    if (p.Crossing != Crossing.None && traffic != null && traffic.RoadBusy(p.X, p.Z, p.Tx, p.Tz, p.Crossing, p.Cruise))
                    {
                        p.Node = keepNode;
                        p.PrevNode = keepPrev;
                        p.Tx = keepTx;
                        p.Tz = keepTz;
                        p.Crossing = Crossing.None;
                        p.WaitTimer = 0.4 + _rng.Next() * 0.7;
                        p.Speed *= 0.5;
                        Integrate(p, dt, playerX, playerZ);
                        continue;
                    }
                }

                // look before stepping off the pavement, not only on arriving at the kerb: the road may have filled in the second it takes to walk to the edge
                if (_rules.LookBeforeStepping && p.Crossing != Crossing.None && traffic != null && targetD > 8 && !OnAsphalt(p)
                    && traffic.RoadBusy(p.X, p.Z, p.Tx, p.Tz, p.Crossing, p.Cruise))
                {
                    p.Speed *= 0.5;
                    Integrate(p, dt, playerX, playerZ);
                    continue;
                }

                double desired = Math.Atan2(targetDx, targetDz);

                Avoidance push = Avoid(p, playerX, playerZ);
                if (push.X != 0 || push.Z != 0)
                {
                    double fx = Math.Sin(desired) + push.X, fz = Math.Cos(desired) + push.Z;
                    if (fx != 0 || fz != 0) desired = Math.Atan2(fx, fz);
                }

                double cruise = p.State == PedState.Idle ? 0 : p.State == PedState.Hurry ? p.Cruise * 1.85 : p.Cruise;
                double congestion = Math.Min(0.7, push.Crowd * 0.35);
                double wanted = cruise * (1 - congestion);

                double turn = ShortestAngle(p.Heading, desired);
                double maxTurn = (p.Speed > 0.2 ? 3.2 : 6.0) * dt;
                p.Heading += Clamp(turn, -maxTurn, maxTurn);
                // slow into sharp turns; it stops the crowd pivoting like turrets
                double brake = 1 - Math.Min(0.55, Math.Abs(turn) * 0.35);
                p.Speed += (wanted * brake - p.Speed) * Math.Min(1, 4.5 * dt);

                Integrate(p, dt, playerX, playerZ);
            }

            _onRoad.Clear();
            foreach (Pedestrian p in _active)
            {
                if (OnAsphalt(p)) _onRoad.Add(p);
            }
        }

        bool OnAsphalt(Pedestrian p) => _nav.KerbDistance(p.X, p.Z) > _nav.WalkOuter;

        void UpdateState(Pedestrian p, double dt)
        {
            p.Timer -= dt;
            if (p.State == PedState.Idle)
            {
                if (p.Timer <= 0)
                {
                    p.State = p.Hurried ? PedState.Hurry : PedState.Walk;
                    p.Timer = 6 + _rng.Next() * 14;
                }
            }
            else if (p.Timer <= 0)
            {
                if (_rng.Next() < 0.22 && !(_rules.NoIdleOnRoad && OnAsphalt(p)))
                {
                    p.State = PedState.Idle;
                    p.Timer = 1.5 + _rng.Next() * 4.5;
                }
                else
                {
                    p.State = _rng.Next() < 0.24 ? PedState.Hurry : PedState.Walk;
                    p.Timer = 5 + _rng.Next() * 12;
                }
            }
        }

        void Integrate(Pedestrian p, double dt, double playerX, double playerZ)
        {
            p.X += Math.Sin(p.Heading) * p.Speed * dt;
            p.Z += Math.Cos(p.Heading) * p.Speed * dt;

            // never let anyone end up inside the player's capsule
            double dpx = p.X - playerX, dpz = p.Z - playerZ;
            double dp = Length(dpx, dpz);
            if (dp < KeepOut && dp > 1e-4)
            {
                p.X = playerX + dpx / dp * KeepOut;
                p.Z = playerZ + dpz / dp * KeepOut;
            }

            double targetY = _nav.GroundY(p.X, p.Z);
            p.Y += (targetY - p.Y) * Math.Min(1, 9 * dt);
            p.Phase += Math.PI * (p.Speed / (0.80 * p.Appearance.Scale)) * dt;
            if (p.Phase > Math.PI * 2)
            {
                p.Phase -= Math.PI * 2;
                p.PrevPhase -= Math.PI * 2;
            }
        }

        // ---------------------------------------------------------------- neighbours

        struct Avoidance
        {
            public double X, Z, Crowd;
        }

        static int CellKey(int gx, int gz) => (gx + 2048) * 4096 + (gz + 2048);

        void RebuildGrid()
        {
            foreach (List<Pedestrian> bucket in _grid.Values)
            {
                bucket.Clear();
                _spareBuckets.Push(bucket);
            }
            _grid.Clear();
            foreach (Pedestrian p in _active)
            {
                int key = CellKey((int)Math.Floor(p.X / HashCell), (int)Math.Floor(p.Z / HashCell));
                if (!_grid.TryGetValue(key, out List<Pedestrian> bucket))
                {
                    bucket = _spareBuckets.Count > 0 ? _spareBuckets.Pop() : new List<Pedestrian>();
                    _grid.Add(key, bucket);
                }
                bucket.Add(p);
            }
        }

        /// <summary>The sideways push from people ahead who are close, and from the player; <c>Crowd</c> measures how hemmed in this person is.</summary>
        Avoidance Avoid(Pedestrian p, double playerX, double playerZ)
        {
            int gx = (int)Math.Floor(p.X / HashCell), gz = (int)Math.Floor(p.Z / HashCell);
            double ax = 0, az = 0, crowd = 0;
            double sinH = Math.Sin(p.Heading), cosH = Math.Cos(p.Heading);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!_grid.TryGetValue(CellKey(gx + dx, gz + dz), out List<Pedestrian> bucket)) continue;
                    foreach (Pedestrian o in bucket)
                    {
                        if (ReferenceEquals(o, p)) continue;
                        double ox = p.X - o.X, oz = p.Z - o.Z;
                        double d2 = ox * ox + oz * oz;
                        if (d2 > 2.56 || d2 < 1e-6) continue;
                        double d = Math.Sqrt(d2);
                        // only yield to people who are actually in the way ahead
                        double ahead = -ox / d * sinH + -oz / d * cosH;
                        if (ahead < -0.2) continue;
                        double w = (1.6 - d) / 1.6;
                        ax += ox / d * w * 1.5;
                        az += oz / d * w * 1.5;
                        crowd += w;
                    }
                }
            }
            double px = p.X - playerX, pz = p.Z - playerZ;
            double pd = Length(px, pz);
            if (pd < 3.2 && pd > 1e-4)
            {
                double w = (3.2 - pd) / 3.2;
                ax += px / pd * w * 3.2;
                az += pz / pd * w * 3.2;
                crowd += w;
            }
            return new Avoidance { X = ax, Z = az, Crowd = crowd };
        }

        // ---------------------------------------------------------------- pooling

        void ResizeToTarget(double camX, double camZ)
        {
            int target = TargetCount;
            if (_active.Count == target) return;

            // retire from the back, but not anyone who is near or in view: they finish their walk out of sight first
            for (int i = _active.Count - 1; i >= 0 && _active.Count > target; i--)
            {
                Pedestrian p = _active[i];
                double dx = p.X - camX, dz = p.Z - camZ;
                if (dx * dx + dz * dz < 144 || (IsVisible != null && IsVisible(p))) continue;
                _active.RemoveAt(i);
            }

            if (_active.Count < target)
            {
                var inUse = new HashSet<Pedestrian>(_active);
                foreach (Pedestrian p in _pool)
                {
                    if (inUse.Contains(p)) continue;
                    Recycle(p, camX, camZ);
                    if (IsVisible != null && IsVisible(p)) continue;   // would pop into view: try the next
                    _active.Add(p);
                    if (_active.Count == target) break;
                }
            }
        }

        // ---------------------------------------------------------------- colliders

        /// <summary>
        /// The people nearest the player within <see cref="ProxyRange"/>, nearest first, written into <paramref name="into"/> (as many as it holds).
        /// Returns how many were found. These are the ones to lease a solid capsule to.
        /// </summary>
        public int NearestToPlayer(double playerX, double playerZ, Pedestrian[] into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            _near.Clear();
            double range2 = ProxyRange * ProxyRange;
            foreach (Pedestrian p in _active)
            {
                double dx = p.X - playerX, dz = p.Z - playerZ;
                double d2 = dx * dx + dz * dz;
                if (d2 < range2) _near.Add((p, d2));
            }
            // a stable insertion sort: ties keep their order, as the prototype's sort does
            for (int i = 1; i < _near.Count; i++)
            {
                var item = _near[i];
                int j = i - 1;
                while (j >= 0 && _near[j].d2 > item.d2)
                {
                    _near[j + 1] = _near[j];
                    j--;
                }
                _near[j + 1] = item;
            }
            int n = Math.Min(into.Length, _near.Count);
            for (int i = 0; i < n; i++) into[i] = _near[i].ped;
            return n;
        }

        // ---------------------------------------------------------------- maths (as the prototype's, so the same inputs give the same numbers)

        static double Length(double x, double z) => Math.Sqrt(x * x + z * z);

        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        /// <summary>The signed angle from a to b, wrapped to -pi..pi.</summary>
        public static double ShortestAngle(double a, double b)
        {
            double d = (b - a) % (Math.PI * 2);
            if (d > Math.PI) d -= Math.PI * 2;
            if (d < -Math.PI) d += Math.PI * 2;
            return d;
        }
    }
}
