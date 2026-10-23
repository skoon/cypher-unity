using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// The ground traffic and aircraft, a port of the logic in Traffic.js. Vehicles follow the road lattice of <see cref="CityNav"/> in lane: a pursuit controller steers them
    /// round junctions, they brake for the vehicle ahead and for a red or amber signal, queue at the stop line, and choose to go straight or turn at every junction (a turn across
    /// the oncoming lane waits for a gap). Everything beyond <see cref="CullRadius"/> of the player is put back in the spawn band round them. Aircraft cross the canyons in altitude bands.
    /// The fleet is built for the top quality tier; a smaller tier uses a prefix of each type's run of it.
    /// <para>
    /// The random draws are consumed in the prototype's order, so a seed gives the same fleet, and the same steps give the same vehicles. Positions and headings are in the prototype's
    /// coordinates: heading 0 faces +Z and a positive heading turns toward +X.
    /// </para>
    /// <para>
    /// Two things are not the prototype's. <see cref="RoadBusy"/> is a gap check (the prototype's looked 24 m ahead and missed the near lane), and vehicles stop short of anyone out on the
    /// road. Neither does anything when nobody is crossing, so a run without people matches the prototype step for step, under <see cref="TrafficRules.Prototype"/>. The game's
    /// <see cref="TrafficRules.City"/> also mends two faults of the prototype's.
    /// </para>
    /// </summary>
    public sealed class TrafficSim : ITrafficQuery
    {
        public const uint DefaultSeed = 31771;

        /// <summary>Vehicles farther than this from the player are put back in the spawn band.</summary>
        public const double CullRadius = 250;

        public const double SpawnMin = 120, SpawnMax = 215;

        /// <summary>The neighbour grid's cell, metres.</summary>
        public const double HashCell = 14;

        /// <summary>Vehicles closer than this to the player can be given a collider.</summary>
        public const double ProxyRange = 20;

        /// <summary>Aircraft farther than this from the player are sent round again; they appear this far upstream.</summary>
        public const double AircraftCullRadius = 300, AircraftReach = 230;

        // Stopping for people out on the road: a firmer deceleration than for a red light, and a clear gap left in front of them.
        public const double PersonBraking = 6.0, PersonClearance = 3.0, PersonLookAhead = 40, PersonSideMargin = 1.0;

        // The gap a walker needs: the vehicle must be this many seconds later than they are across, allowing for it to speed up as hard as it can.
        public const double CrossMargin = 2.0;

        // How much room a vehicle's body is given either side of where a walker crosses, and the slack on the width of the road.
        public const double BodyClearance = 1.0, RoadSlack = 1.0;

        static readonly double[] AircraftBands = { 24, 33, 45, 62, 88, 118 };

        readonly CityNav _nav;
        readonly Rng _rng;
        readonly TrafficRules _rules;
        readonly double _lane, _stopOff, _exitOff;
        readonly SignalClock _signal = new SignalClock();
        readonly List<Vehicle> _fleet = new List<Vehicle>();
        readonly List<Vehicle> _vehicles = new List<Vehicle>();
        readonly List<Aircraft> _aircraftFleet = new List<Aircraft>();
        readonly List<Aircraft> _aircraft = new List<Aircraft>();
        readonly Dictionary<int, List<Vehicle>> _grid = new Dictionary<int, List<Vehicle>>();
        readonly Stack<List<Vehicle>> _spareBuckets = new Stack<List<Vehicle>>();
        readonly List<(Vehicle vehicle, double d2)> _near = new List<(Vehicle, double)>();
        readonly int[] _optionDx = new int[3], _optionDz = new int[3];
        readonly Manoeuvre[] _optionTurn = new Manoeuvre[3];
        readonly double[] _optionWeight = new double[3];
        int[] _groundTarget, _aircraftTarget;
        int _groundBudget, _aircraftBudget;
        bool _pending;
        IPedestrianQuery _people;   // who is out on the road during a step, so that nothing is put down on top of them

        public CityNav Nav => _nav;

        public TrafficRules Rules => _rules;

        public SignalClock Signal => _signal;

        /// <summary>Every vehicle the traffic was built with, active or not.</summary>
        public IReadOnlyList<Vehicle> Fleet => _fleet;

        /// <summary>The vehicles on the road now.</summary>
        public IReadOnlyList<Vehicle> Vehicles => _vehicles;

        public IReadOnlyList<Aircraft> AircraftFleet => _aircraftFleet;

        /// <summary>The aircraft in the air now.</summary>
        public IReadOnlyList<Aircraft> Airborne => _aircraft;

        /// <summary>How far to the side of the lane centre a vehicle keeps (a bike keeps further over).</summary>
        public double Lane => _lane;

        /// <summary>Whether a sphere (centre x, y, z, then radius) is in the camera's view. Vehicles and aircraft in view are never retired or introduced; null means nothing is in view.</summary>
        public Func<double, double, double, double, bool> InView { get; set; }

        TrafficSim(CityNav nav, uint seed, TrafficRules rules)
        {
            _nav = nav;
            _rng = new Rng(seed);
            _rules = rules;
            _lane = Math.Max(1.4, nav.AsphaltHalf * 0.5);
            _stopOff = nav.AsphaltHalf + 1.1;
            _exitOff = nav.AsphaltHalf + 1.6;
        }

        /// <summary>
        /// Build the whole fleet, put it on the road round (<paramref name="playerX"/>, <paramref name="playerZ"/>) and keep <paramref name="ground"/> vehicles and
        /// <paramref name="aircraft"/> aircraft of it in use. <paramref name="rules"/> defaults to the prototype's.
        /// </summary>
        public static TrafficSim Create(CityNav nav, double playerX, double playerZ, int ground, int aircraft, uint seed = DefaultSeed, TrafficRules rules = null)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            if (ground < 0 || ground > QualityTiers.MaxGroundVehicles) throw new ArgumentOutOfRangeException(nameof(ground), ground, $"the road holds 0 to {QualityTiers.MaxGroundVehicles} vehicles");
            if (aircraft < 0 || aircraft > QualityTiers.MaxAircraft) throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, $"the sky holds 0 to {QualityTiers.MaxAircraft} aircraft");

            var sim = new TrafficSim(nav, seed, rules ?? TrafficRules.Prototype);
            sim.BuildFleet();
            sim._vehicles.AddRange(sim._fleet);
            sim._aircraft.AddRange(sim._aircraftFleet);
            foreach (Vehicle v in sim._fleet) sim.Respawn(v, playerX, playerZ, 8, 210);
            foreach (Aircraft a in sim._aircraftFleet) sim.RespawnAircraft(a, playerX, playerZ, true);

            sim.SetBudget(ground, aircraft);
            int[] groundCounts = sim._groundTarget, aircraftCounts = sim._aircraftTarget;
            sim._vehicles.RemoveAll(v => v.Slot >= groundCounts[v.TypeIndex]);
            sim._aircraft.RemoveAll(a => a.Slot >= aircraftCounts[a.TypeIndex]);
            sim._pending = false;
            return sim;
        }

        /// <summary>How many vehicles and aircraft there should be (a quality tier's budget). The simulation moves toward it a step at a time.</summary>
        public void SetBudget(int ground, int aircraft)
        {
            if (ground < 0 || ground > QualityTiers.MaxGroundVehicles) throw new ArgumentOutOfRangeException(nameof(ground), ground, $"the road holds 0 to {QualityTiers.MaxGroundVehicles} vehicles");
            if (aircraft < 0 || aircraft > QualityTiers.MaxAircraft) throw new ArgumentOutOfRangeException(nameof(aircraft), aircraft, $"the sky holds 0 to {QualityTiers.MaxAircraft} aircraft");
            _groundBudget = ground;
            _aircraftBudget = aircraft;
            _groundTarget = FleetBudget.Distribute(ground, VehicleCatalog.GroundShares());
            _aircraftTarget = FleetBudget.Distribute(aircraft, VehicleCatalog.AircraftShares());
            _pending = true;
        }

        // ---------------------------------------------------------------- building the fleet

        void BuildFleet()
        {
            IReadOnlyList<GroundVehicleType> ground = VehicleCatalog.Ground;
            int[] counts = FleetBudget.Distribute(QualityTiers.MaxGroundVehicles, VehicleCatalog.GroundShares());
            for (int ti = 0; ti < ground.Count; ti++)
            {
                GroundVehicleType type = ground[ti];
                for (int k = 0; k < counts[ti]; k++)
                {
                    var v = new Vehicle { Type = type, TypeIndex = ti, Slot = k, Index = _fleet.Count, Y = _nav.RoadY, Dx = 1, Ndx = 1 };
                    v.Cruise = type.CruiseMin + _rng.Next() * (type.CruiseMax - type.CruiseMin);
                    v.Spin = _rng.Next() * Math.PI * 2;
                    _rng.Next();   // the prototype's indicator blink phase: drawn, never used
                    v.Glow = _rng.Next();
                    v.LampTint = 0.9 + _rng.Next() * 0.25;
                    _fleet.Add(v);
                }
            }

            IReadOnlyList<AircraftType> air = VehicleCatalog.Aircraft;
            int[] airCounts = FleetBudget.Distribute(QualityTiers.MaxAircraft, VehicleCatalog.AircraftShares());
            for (int ti = 0; ti < air.Count; ti++)
            {
                for (int k = 0; k < airCounts[ti]; k++)
                {
                    var a = new Aircraft { Type = air[ti], TypeIndex = ti, Slot = k, Index = _aircraftFleet.Count, Y = 40, PrevY = 40, Speed = 20 };
                    a.Bob = _rng.Next() * Math.PI * 2;
                    _aircraftFleet.Add(a);
                }
            }

            // the prototype picks each one's paint (and a vehicle's under-glow) when it builds the meshes, after the fleet and before anything is placed
            foreach (Vehicle v in _fleet)
            {
                v.PaintIndex = (int)(_rng.Next() * VehicleCatalog.Paint.Length);
                v.NeonIndex = (int)(_rng.Next() * VehicleCatalog.Neon.Length);
            }
            foreach (Aircraft a in _aircraftFleet) a.PaintIndex = (int)(_rng.Next() * VehicleCatalog.Paint.Length);
        }

        // ---------------------------------------------------------------- the road lattice

        bool InGrid(int ix, int iz) => ix >= 0 && ix < _nav.XLines.Count && iz >= 0 && iz < _nav.ZLines.Count;

        /// <summary>Aim for the lane centre just short of the junction being entered, and choose what to do there.</summary>
        void SetApproach(Vehicle v)
        {
            double cx = _nav.XLines[v.Ix], cz = _nav.ZLines[v.Iz];
            double rx = -v.Dz, rz = v.Dx;
            double lane = _lane * v.Type.LaneScale;
            v.WpX = cx - v.Dx * _stopOff + rx * lane;
            v.WpZ = cz - v.Dz * _stopOff + rz * lane;
            v.Leg = 0;
            ChooseTurn(v);
        }

        /// <summary>Aim for the lane centre just beyond the junction, along the way chosen.</summary>
        void SetExit(Vehicle v)
        {
            double cx = _nav.XLines[v.Ix], cz = _nav.ZLines[v.Iz];
            double rx = -v.Ndz, rz = v.Ndx;
            double lane = _lane * v.Type.LaneScale;
            v.WpX = cx + v.Ndx * _exitOff + rx * lane;
            v.WpZ = cz + v.Ndz * _exitOff + rz * lane;
            v.Leg = 1;
        }

        /// <summary>Pick the manoeuvre for the junction ahead, refusing exits from the grid.</summary>
        void ChooseTurn(Vehicle v)
        {
            int valid = 0;
            double total = 0;
            for (int k = 0; k < 3; k++)
            {
                int dx, dz;
                Manoeuvre turn;
                double w;
                if (k == 0) { dx = v.Dx; dz = v.Dz; turn = Manoeuvre.Straight; w = 3.0; }
                else if (k == 1) { dx = -v.Dz; dz = v.Dx; turn = Manoeuvre.Turn; w = 1.05; }
                else { dx = v.Dz; dz = -v.Dx; turn = Manoeuvre.CrossingTurn; w = 0.95; }
                if (!InGrid(v.Ix + dx, v.Iz + dz)) continue;
                _optionDx[valid] = dx; _optionDz[valid] = dz; _optionTurn[valid] = turn; _optionWeight[valid] = w;
                valid++;
                total += w;
            }
            if (valid == 0)
            {
                // cornered at the boundary: turn round through the junction
                v.Ndx = -v.Dx; v.Ndz = -v.Dz; v.Turn = Manoeuvre.UTurn;
                return;
            }
            double r = _rng.Next() * total;
            int pick = valid - 1;
            for (int k = 0; k < valid; k++)
            {
                r -= _optionWeight[k];
                if (r <= 0) { pick = k; break; }
            }
            v.Ndx = _optionDx[pick];
            v.Ndz = _optionDz[pick];
            v.Turn = _optionTurn[pick];
        }

        /// <summary>Put a vehicle on the road at a random spot between <paramref name="minD"/> and <paramref name="maxD"/> from (px, pz), clear of the others; if none is found it waits, unused.</summary>
        void Respawn(Vehicle v, double px, double pz, double minD, double maxD)
        {
            int nx = _nav.XLines.Count, nz = _nav.ZLines.Count;
            for (int t = 0; t < 40; t++)
            {
                bool alongX = _rng.Next() < 0.5;
                int dir = _rng.Next() < 0.5 ? 1 : -1;
                int ix = (int)(_rng.Next() * nx);
                int iz = (int)(_rng.Next() * nz);
                int dx = alongX ? dir : 0, dz = alongX ? 0 : dir;
                if (!InGrid(ix + dx, iz + dz)) continue;
                // travel from this junction toward the next, somewhere along the leg
                double s = 0.12 + _rng.Next() * 0.72;
                double cx = _nav.XLines[ix], cz = _nav.ZLines[iz];
                double tx = _nav.XLines[ix + dx], tz = _nav.ZLines[iz + dz];
                double rx = -dz, rz = dx;
                double lane = _lane * v.Type.LaneScale;
                double x = cx + (tx - cx) * s + rx * lane;
                double z = cz + (tz - cz) * s + rz * lane;
                double d = Hypot(x - px, z - pz);
                if (d < minD || d > maxD) continue;
                if (Occupied(x, z, v) || NearAPerson(x, z)) continue;

                v.X = x; v.Z = z;
                v.Y = _nav.RoadY;
                v.Dx = dx; v.Dz = dz;
                v.Ix = ix + dx; v.Iz = iz + dz;
                v.Heading = Math.Atan2(dx, dz);
                v.Speed = v.Cruise * (0.6 + _rng.Next() * 0.4);
                v.Active = true;
                SetApproach(v);
                v.PrevX = v.X; v.PrevZ = v.Z; v.PrevHeading = v.Heading;
                return;
            }
            v.Active = false;
            v.X = 0; v.Z = 0; v.Y = -900;
        }

        /// <summary>Whether someone out on the road is within 12 m of (x, z): a vehicle is not put down there.</summary>
        bool NearAPerson(double x, double z)
        {
            if (_people == null) return false;
            IReadOnlyList<Pedestrian> onRoad = _people.OnRoad;
            for (int i = 0; i < onRoad.Count; i++)
            {
                double dx = onRoad[i].X - x, dz = onRoad[i].Z - z;
                if (dx * dx + dz * dz < 144) return true;
            }
            return false;
        }

        bool Occupied(double x, double z, Vehicle self)
        {
            foreach (Vehicle o in _vehicles)
            {
                if (ReferenceEquals(o, self) || !o.Active) continue;
                if ((o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) < 121) return true;
            }
            return false;
        }

        /// <summary>Send an aircraft round again: choose its altitude, speed and line, upstream of the player (or anywhere along it when <paramref name="initial"/>).</summary>
        void RespawnAircraft(Aircraft a, double px, double pz, bool initial = false)
        {
            double band = AircraftBands[(int)(_rng.Next() * AircraftBands.Length)];
            a.Y = band + _rng.Next() * 6;
            a.Speed = a.Type.SpeedMin + _rng.Next() * (a.Type.SpeedMax - a.Type.SpeedMin);
            const double reach = AircraftReach;

            if (band < 70)
            {
                // canyon runs: lock onto a road centreline so they thread the towers
                bool alongX = _rng.Next() < 0.5;
                IReadOnlyList<double> lines = alongX ? _nav.ZLines : _nav.XLines;
                double line = lines[(int)(_rng.Next() * lines.Count)];
                // prefer a canyon near the player so the pass is actually visible
                double best = line, bestD = double.PositiveInfinity;
                foreach (double l in lines)
                {
                    double d = Math.Abs(l - (alongX ? pz : px)) + _rng.Next() * 70;
                    if (d < bestD) { bestD = d; best = l; }
                }
                line = best + (_rng.Next() * 2 - 1) * (_nav.AsphaltHalf * 0.5);
                int dir = _rng.Next() < 0.5 ? 1 : -1;
                if (alongX)
                {
                    a.DirX = dir; a.DirZ = 0;
                    a.Z = line;
                    a.X = px - dir * (initial ? (_rng.Next() * 2 - 1) * reach : reach);
                }
                else
                {
                    a.DirX = 0; a.DirZ = dir;
                    a.X = line;
                    a.Z = pz - dir * (initial ? (_rng.Next() * 2 - 1) * reach : reach);
                }
            }
            else
            {
                double angle = _rng.Next() * Math.PI * 2;
                a.DirX = Math.Sin(angle);
                a.DirZ = Math.Cos(angle);
                double start = initial ? _rng.Next() * reach : reach;
                a.X = px - a.DirX * start + (_rng.Next() * 2 - 1) * 90;
                a.Z = pz - a.DirZ * start + (_rng.Next() * 2 - 1) * 90;
            }
            a.Heading = Math.Atan2(a.DirX, a.DirZ);
            a.Bank = 0;
            a.Bob = _rng.Next() * Math.PI * 2;
            a.PrevX = a.X; a.PrevY = a.Y; a.PrevZ = a.Z; a.PrevHeading = a.Heading;
        }

        // ---------------------------------------------------------------- stepping

        /// <summary>
        /// Advance everything by <paramref name="dt"/> seconds. The player decides who is put back in the spawn band and who stops in their way; the camera decides who may be
        /// retired or introduced when the budget changes. <paramref name="people"/>, when given, are the walkers the vehicles stop for.
        /// </summary>
        public void Step(double dt, double camX, double camZ, double playerX, double playerZ, IPedestrianQuery people = null)
        {
            if (!(dt >= 0)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "a step cannot be negative");
            _people = people;
            Reconcile(camX, camZ);
            _signal.Advance(dt);
            RebuildGrid();

            double cull2 = CullRadius * CullRadius;
            for (int i = 0; i < _vehicles.Count; i++)
            {
                Vehicle v = _vehicles[i];
                v.PrevX = v.X; v.PrevZ = v.Z; v.PrevHeading = v.Heading;
                if (!v.Active)
                {
                    Respawn(v, playerX, playerZ, SpawnMin, SpawnMax);
                    continue;
                }

                double dxp = v.X - playerX, dzp = v.Z - playerZ;
                if (dxp * dxp + dzp * dzp > cull2)
                {
                    Respawn(v, playerX, playerZ, SpawnMin, SpawnMax);
                    continue;
                }

                double toX = v.WpX - v.X, toZ = v.WpZ - v.Z;
                double toD = Hypot(toX, toZ);

                // --- speed target ---
                double target = v.Cruise;

                if (v.Leg == 0)
                {
                    int axis = v.Dx != 0 ? 0 : 1;
                    bool canGo = _signal.GreenAxis == axis && (!_signal.Amber || toD < v.Speed * 1.15 + 1.5);
                    if (!canGo) target = Math.Min(target, Math.Sqrt(2 * 3.4 * Math.Max(0, toD - 0.4)));
                    if (v.Turn != Manoeuvre.Straight && toD < 16) target = Math.Min(target, v.Turn == Manoeuvre.UTurn ? 3.6 : 6.2);
                }
                else if (v.Turn != Manoeuvre.Straight)
                {
                    target = Math.Min(target, v.Turn == Manoeuvre.UTurn ? 3.6 : 6.4);
                }

                (double gapDist, double gapSpeed) = GapAhead(v);
                if (gapDist < 26) target = Math.Min(target, Math.Max(0, gapSpeed + (gapDist - 7.5) * 1.15));

                // the player standing in the road is a hard stop, not a speed bump
                double sinH = Math.Sin(v.Heading), cosH = Math.Cos(v.Heading);
                if (_rules.StopForPlayerLikePeople)
                {
                    target = Math.Min(target, SpeedLimitForPerson(v, sinH, cosH, playerX, playerZ, 0, 0));
                }
                else
                {
                    double rel = (playerX - v.X) * sinH + (playerZ - v.Z) * cosH;
                    double lat = (playerX - v.X) * cosH - (playerZ - v.Z) * sinH;
                    if (rel > 0 && rel < 14 && Math.Abs(lat) < 2.6) target = Math.Min(target, Math.Max(0, (rel - 5.0) * 1.4));
                }

                if (people != null) target = Math.Min(target, PeopleSpeedLimit(v, sinH, cosH, people));
                target = Math.Max(0, target);

                double rate = target > v.Speed ? v.Type.Accel : 11.0;
                v.Speed += Clamp(target - v.Speed, -rate * dt, rate * dt);
                if (v.Speed < 0.02) v.Speed = 0;

                // --- steering: a yaw-rate-limited pursuit gives the turn arc for free ---
                if (toD > (_rules.HoldHeadingNearWaypoint ? 1.0 : 0.05))
                {
                    double want = Math.Atan2(toX, toZ);
                    double diff = CrowdSim.ShortestAngle(v.Heading, want);
                    double maxYaw = Clamp(7.0 / Math.Max(v.Speed, 2.0), 0.3, 2.4) * dt;
                    v.Heading += Clamp(diff, -maxYaw, maxYaw);
                }

                v.X += Math.Sin(v.Heading) * v.Speed * dt;
                v.Z += Math.Cos(v.Heading) * v.Speed * dt;
                v.Spin += v.Speed * dt / v.Type.WheelRadius;

                // --- waypoint advance (guard against sailing past the target) ---
                double along = _rules.ProgressAlongTheRoad
                    ? (v.WpX - v.X) * (v.Leg == 0 ? v.Dx : v.Ndx) + (v.WpZ - v.Z) * (v.Leg == 0 ? v.Dz : v.Ndz)
                    : (v.WpX - v.X) * Math.Sin(v.Heading) + (v.WpZ - v.Z) * Math.Cos(v.Heading);
                if (v.Leg == 0)
                {
                    if (toD < 1.6 || along < -0.5)
                    {
                        int axis = v.Dx != 0 ? 0 : 1;
                        bool committed = along < -2.5;
                        bool clear = v.Turn != Manoeuvre.CrossingTurn || OncomingClear(v);
                        if (committed || (_signal.GreenAxis == axis && clear)) SetExit(v);
                    }
                }
                else if (toD < 2.0 || along < -0.5)
                {
                    v.Dx = v.Ndx; v.Dz = v.Ndz;
                    v.Ix += v.Dx; v.Iz += v.Dz;
                    if (!InGrid(v.Ix, v.Iz))
                    {
                        Respawn(v, playerX, playerZ, SpawnMin, SpawnMax);
                        continue;
                    }
                    SetApproach(v);
                }
            }

            double aircraftCull2 = AircraftCullRadius * AircraftCullRadius;
            foreach (Aircraft a in _aircraft)
            {
                a.PrevX = a.X; a.PrevY = a.Y; a.PrevZ = a.Z; a.PrevHeading = a.Heading;
                a.X += a.DirX * a.Speed * dt;
                a.Z += a.DirZ * a.Speed * dt;
                a.Bob += dt * 0.7;
                a.Bank = Math.Sin(a.Bob * 0.8) * 0.10;
                if ((a.X - playerX) * (a.X - playerX) + (a.Z - playerZ) * (a.Z - playerZ) > aircraftCull2) RespawnAircraft(a, playerX, playerZ);
            }
        }

        /// <summary>The fastest a vehicle may go for the walkers out on the road: slow enough to stop 3 m short of anyone in its way.</summary>
        double PeopleSpeedLimit(Vehicle v, double sinH, double cosH, IPedestrianQuery people)
        {
            double limit = double.PositiveInfinity;
            IReadOnlyList<Pedestrian> onRoad = people.OnRoad;
            for (int i = 0; i < onRoad.Count; i++)
            {
                Pedestrian q = onRoad[i];
                limit = Math.Min(limit, SpeedLimitForPerson(v, sinH, cosH, q.X, q.Z, Math.Sin(q.Heading) * q.Speed, Math.Cos(q.Heading) * q.Speed));
            }
            return limit;
        }

        /// <summary>
        /// The fastest a vehicle may go with a person at (<paramref name="qx"/>, <paramref name="qz"/>) walking at (<paramref name="qvx"/>, <paramref name="qvz"/>): unlimited unless they are ahead
        /// within <see cref="PersonLookAhead"/> and in its way, now or by the time it would get to them.
        /// </summary>
        double SpeedLimitForPerson(Vehicle v, double sinH, double cosH, double qx, double qz, double qvx, double qvz)
        {
            double dx = qx - v.X, dz = qz - v.Z;
            double rel = dx * sinH + dz * cosH - v.HalfLength;
            if (rel < -0.5 || rel > PersonLookAhead) return double.PositiveInfinity;
            double side = v.HalfWidth + PersonSideMargin;
            double lat = dx * cosH - dz * sinH;
            if (Math.Abs(lat) > side)
            {
                // beside its path now, but walking into it: where they will be when it gets there
                double sideways = qvx * cosH - qvz * sinH;
                double arrives = Math.Max(rel, 0) / Math.Max(v.Speed, 3.0);
                if (Math.Abs(lat + sideways * arrives) > side) return double.PositiveInfinity;
            }
            return Math.Sqrt(2 * PersonBraking * Math.Max(0, rel - PersonClearance));
        }

        // ---------------------------------------------------------------- neighbours

        static int CellKey(int gx, int gz) => (gx + 2048) * 4096 + (gz + 2048);

        void RebuildGrid()
        {
            foreach (List<Vehicle> bucket in _grid.Values)
            {
                bucket.Clear();
                _spareBuckets.Push(bucket);
            }
            _grid.Clear();
            foreach (Vehicle v in _vehicles)
            {
                if (!v.Active) continue;
                int key = CellKey((int)Math.Floor(v.X / HashCell), (int)Math.Floor(v.Z / HashCell));
                if (!_grid.TryGetValue(key, out List<Vehicle> bucket))
                {
                    bucket = _spareBuckets.Count > 0 ? _spareBuckets.Pop() : new List<Vehicle>();
                    _grid.Add(key, bucket);
                }
                bucket.Add(v);
            }
        }

        /// <summary>The distance to the back of the nearest vehicle ahead in the same lane corridor, and its speed.</summary>
        (double dist, double speed) GapAhead(Vehicle v)
        {
            int gx = (int)Math.Floor(v.X / HashCell), gz = (int)Math.Floor(v.Z / HashCell);
            double fx = Math.Sin(v.Heading), fz = Math.Cos(v.Heading);
            double best = double.PositiveInfinity, bestSpeed = 0;
            double halfLength = v.HalfLength;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!_grid.TryGetValue(CellKey(gx + dx, gz + dz), out List<Vehicle> bucket)) continue;
                    foreach (Vehicle o in bucket)
                    {
                        if (ReferenceEquals(o, v)) continue;
                        double ox = o.X - v.X, oz = o.Z - v.Z;
                        double ahead = ox * fx + oz * fz;
                        if (ahead <= 0 || ahead > 30) continue;
                        double side = ox * fz - oz * fx;
                        if (Math.Abs(side) > 2.3) continue;
                        if (Math.Sin(o.Heading) * fx + Math.Cos(o.Heading) * fz < 0.35) continue;
                        double d = ahead - halfLength - o.HalfLength;
                        if (d < best) { best = d; bestSpeed = o.Speed; }
                    }
                }
            }
            return (best, bestSpeed);
        }

        /// <summary>A turn across the oncoming lane gives way to oncoming traffic already close to the junction.</summary>
        bool OncomingClear(Vehicle v)
        {
            double cx = _nav.XLines[v.Ix], cz = _nav.ZLines[v.Iz];
            foreach (Vehicle o in _vehicles)
            {
                if (ReferenceEquals(o, v) || !o.Active || o.Speed < 2) continue;
                if (o.Dx != -v.Dx || o.Dz != -v.Dz) continue;
                double d = Hypot(o.X - cx, o.Z - cz);
                if (d < 26 && o.Leg == 0) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- pooling

        /// <summary>Move the numbers of vehicles and aircraft toward the budget, never retiring or introducing anything that can be seen or is within 20 m of the camera.</summary>
        void Reconcile(double camX, double camZ)
        {
            if (_vehicles.Count == _groundBudget && _aircraft.Count == _aircraftBudget && !_pending) return;

            bool groundPending = ReconcileGround(camX, camZ);
            bool aircraftPending = ReconcileAircraft(camX, camZ);
            _pending = groundPending || aircraftPending;
        }

        bool InSight(double x, double y, double z) => InView != null && InView(x, y + 5, z, 10);

        bool ReconcileGround(double camX, double camZ)
        {
            int types = _groundTarget.Length;
            var counts = new int[types];
            foreach (Vehicle v in _vehicles) counts[v.TypeIndex]++;
            for (int i = _vehicles.Count - 1; i >= 0; i--)
            {
                Vehicle v = _vehicles[i];
                if (counts[v.TypeIndex] <= _groundTarget[v.TypeIndex]) continue;
                if ((v.X - camX) * (v.X - camX) + (v.Z - camZ) * (v.Z - camZ) < 400 || InSight(v.X, v.Y, v.Z)) continue;
                _vehicles.RemoveAt(i);
                counts[v.TypeIndex]--;
            }
            var inUse = new HashSet<Vehicle>(_vehicles);
            foreach (Vehicle v in _fleet)
            {
                if (counts[v.TypeIndex] >= _groundTarget[v.TypeIndex] || inUse.Contains(v)) continue;
                Respawn(v, camX, camZ, SpawnMin, SpawnMax);
                if (InSight(v.X, v.Y, v.Z)) continue;   // would pop into view: try the next
                _vehicles.Add(v);
                counts[v.TypeIndex]++;
            }
            for (int ti = 0; ti < types; ti++)
            {
                if (counts[ti] != _groundTarget[ti]) return true;
            }
            return false;
        }

        bool ReconcileAircraft(double camX, double camZ)
        {
            int types = _aircraftTarget.Length;
            var counts = new int[types];
            foreach (Aircraft a in _aircraft) counts[a.TypeIndex]++;
            for (int i = _aircraft.Count - 1; i >= 0; i--)
            {
                Aircraft a = _aircraft[i];
                if (counts[a.TypeIndex] <= _aircraftTarget[a.TypeIndex]) continue;
                if ((a.X - camX) * (a.X - camX) + (a.Z - camZ) * (a.Z - camZ) < 400 || InSight(a.X, a.Y, a.Z)) continue;
                _aircraft.RemoveAt(i);
                counts[a.TypeIndex]--;
            }
            var inUse = new HashSet<Aircraft>(_aircraft);
            foreach (Aircraft a in _aircraftFleet)
            {
                if (counts[a.TypeIndex] >= _aircraftTarget[a.TypeIndex] || inUse.Contains(a)) continue;
                RespawnAircraft(a, camX, camZ);
                if (InSight(a.X, a.Y, a.Z)) continue;
                _aircraft.Add(a);
                counts[a.TypeIndex]++;
            }
            for (int ti = 0; ti < types; ti++)
            {
                if (counts[ti] != _aircraftTarget[ti]) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- colliders

        /// <summary>
        /// The vehicles nearest the player within <see cref="ProxyRange"/>, nearest first, written into <paramref name="into"/> (as many as it holds).
        /// Returns how many were found. These are the ones to lease a solid box to.
        /// </summary>
        public int NearestToPlayer(double playerX, double playerZ, Vehicle[] into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            _near.Clear();
            double range2 = ProxyRange * ProxyRange;
            foreach (Vehicle v in _vehicles)
            {
                if (!v.Active) continue;
                double dx = v.X - playerX, dz = v.Z - playerZ;
                double d2 = dx * dx + dz * dz;
                if (d2 < range2) _near.Add((v, d2));
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
            for (int i = 0; i < n; i++) into[i] = _near[i].vehicle;
            return n;
        }

        // ---------------------------------------------------------------- people crossing

        /// <summary>
        /// Whether the road is too busy for a walker to cross from (<paramref name="fromX"/>, <paramref name="fromZ"/>) to (<paramref name="toX"/>, <paramref name="toZ"/>) at
        /// <paramref name="walkSpeed"/>. Busy if any vehicle on that road would reach the walker's line before they are across plus <see cref="CrossMargin"/> seconds, or has its
        /// body on the line. A vehicle is taken to speed up as hard as it can toward the line, from rest if it is standing; one waiting at a red signal sets off only when its
        /// light turns green.
        /// </summary>
        public bool RoadBusy(double fromX, double fromZ, double toX, double toZ, Crossing crossing, double walkSpeed)
        {
            if (crossing != Crossing.X && crossing != Crossing.Z) return false;
            if (!(walkSpeed > 0)) throw new ArgumentOutOfRangeException(nameof(walkSpeed), walkSpeed, "a walker has to be moving");

            bool alongX = crossing == Crossing.X;   // the traffic runs along X, so the walker goes along Z
            double lineA = alongX ? fromX : fromZ, lineB = alongX ? toX : toZ;
            double lineMin = Math.Min(lineA, lineB), lineMax = Math.Max(lineA, lineB);
            double spanA = alongX ? fromZ : fromX, spanB = alongX ? toZ : toX;
            double roadLow = Math.Min(spanA, spanB) - RoadSlack, roadHigh = Math.Max(spanA, spanB) + RoadSlack;
            double window = Hypot(toX - fromX, toZ - fromZ) / walkSpeed + CrossMargin;

            foreach (Vehicle v in _vehicles)
            {
                if (!v.Active) continue;
                bool travelsAlong = (v.Dx != 0) == alongX;
                // a vehicle at or approaching a junction that will turn onto this road is as much a threat as one already on it
                bool entersRoad = !travelsAlong && (v.Leg == 1 || v.Turn != Manoeuvre.Straight) && v.Turn != Manoeuvre.UTurn && (v.Ndx != 0) == alongX;
                if (!travelsAlong && !entersRoad) continue;
                if (travelsAlong && v.Leg == 1 && (v.Ndx != 0) != alongX) continue;   // turning off this road, inside the junction

                double wait = v.Speed < 0.5 ? _signal.TimeUntilGreen(v.Dx != 0 ? 0 : 1) : 0;
                double body = v.HalfLength + BodyClearance;
                if (entersRoad)
                {
                    double jx = _nav.XLines[v.Ix], jz = _nav.ZLines[v.Iz];
                    double junctionAcross = alongX ? jz : jx, junctionAlong = alongX ? jx : jz;
                    if (junctionAcross < roadLow || junctionAcross > roadHigh) continue;   // a junction of another road
                    int exit = alongX ? v.Ndx : v.Ndz;
                    double ea = (lineMin - junctionAlong) * exit, eb = (lineMax - junctionAlong) * exit;
                    if (Math.Max(ea, eb) < 0) continue;   // the line is behind the way it will leave
                    double path = Hypot(jx - v.X, jz - v.Z) + Math.Max(0, Math.Min(ea, eb));
                    if (wait + TimeToCover(path - v.HalfLength, v.Speed, v.Type.Accel, v.Cruise) < window) return true;
                    continue;
                }

                double across = alongX ? v.Z : v.X;
                if (across < roadLow || across > roadHigh) continue;   // another road

                double position = alongX ? v.X : v.Z;
                int dir = alongX ? v.Dx : v.Dz;
                // how far in front of the vehicle's middle the line is (negative: behind it)
                double a = (lineMin - position) * dir, b = (lineMax - position) * dir;
                double near = Math.Min(a, b), far = Math.Max(a, b);
                if (near <= body && far >= -body) return true;   // its body is on the line
                if (far < 0) continue;   // already past
                if (wait + TimeToCover(near - v.HalfLength, v.Speed, v.Type.Accel, v.Cruise) < window) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- maths

        /// <summary>The time to cover <paramref name="distance"/> from speed <paramref name="v0"/>, speeding up at <paramref name="accel"/> until <paramref name="cruise"/>.</summary>
        static double TimeToCover(double distance, double v0, double accel, double cruise)
        {
            if (distance <= 0) return 0;
            v0 = Math.Min(v0, cruise);
            double toCruise = (cruise * cruise - v0 * v0) / (2 * accel);
            if (distance <= toCruise) return (-v0 + Math.Sqrt(v0 * v0 + 2 * accel * distance)) / accel;
            return (cruise - v0) / accel + (distance - toCruise) / cruise;
        }

        static double Hypot(double x, double z) => Math.Sqrt(x * x + z * z);

        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
    }
}
