# Step 11: traffic, aircraft and people who cross the road (Milestone 3)

Status: **approved and built 2026-10-22 and 23** (see `task.md`), with the answers recorded under "Questions". Part of `implementation_plan_m2.md`. Steps 9 and 10 built the navigation graph and the crowd; this step puts vehicles on the road lattice, aircraft in the
sky, and, as you asked, makes people cross roads.

## What the player sees

Cars, vans, trucks and motorbikes drive the 8x8 lattice of roads (64 junctions) in lane, queue behind each other, stop at red signals, turn left, right or straight at each junction, and stop for the
player standing in the road. Every junction corner has a signal mast with three lamps that really change. Headlamps, tail lamps, indicator strips, under-glow and light cones are glowing
geometry that blooms, and the nearest few cars carry real point lights so the wet road reacts. Spinners and freighters cross the canyons between the towers in six altitude bands, each with a
thrust glow and red and green navigation lamps. **People cross the road at the mid-block crosswalks, wait at the kerb for a gap, and cars stop for anyone already on the road.** A few vehicles
near the player are solid.

The behaviour is [Traffic.js](file:///D:/source/cyberpunkish/src/ai/Traffic.js) (1,246 lines), ported with the same numbers and the same random-number order, so it can be checked against the prototype
the way the crowd was. One side effect: the port's city is the prototype's mirror image, so traffic keeps to the left. With the katakana signage it suits the place; it is a one-sign change if not.

## What is ported, unchanged

The signal cycle (12.5 s green, 2.6 s amber, per axis); the lane-following pursuit controller (yaw-rate limited, so turns arc for free); car-following (brake to the car ahead within 26 m);
braking to the stop line on red (3.4 m/s2), the amber rule, slowing into turns; turn choice (straight 3.0, one side 1.05, the other 0.95, U-turn only when cornered); left turns giving way to oncoming
traffic near the junction; spawn band 120 to 215 m and recycle at 250 m from the player; the four vehicle types (shares 40 / 19 / 13 / 28 %, sizes, cruise speeds, acceleration) and two aircraft
(66 / 34 %); the aircraft altitude bands (24 to 118 m), canyon runs locked to a road centreline below 70 m and free flight above; the prototype's budgets (18 / 30 / 52 / 74 / 96 vehicles and
5 / 12 / 20 / 28 / 36 aircraft on Performance to Ultra); the nearest four vehicles within 20 m leased a solid box.

## New: the crossing bias

Today people almost never cross (the finding from step 10). A crosswalk joins the middle of a block's kerb to the middle of the facing block's, at right angles to the way a person is walking. It
scores `0.45 x 0.30 x (0.55..1.45)`, at most 0.196, against at least 0.55 for walking on, so it never wins, except for someone just spawned. Three changes, each switched on only for the game, with the
prototype's values kept as a preset so the golden tests still pin the port to it:

1. **A crossing weight.** The 0.45 becomes `CrowdRules.CrossingWeight`; the game uses 2.0 (the prototype preset keeps 0.45). I estimate this makes about 1 in 10 arrivals at a kerb midpoint cross
   (1 in 15 for someone just spawned), which puts roughly 1 person in 45 on a road at any moment, two or three on Medium, about one in view. Same number of random draws, so nothing else shifts.
2. **Looking properly.** The prototype's check (a moving vehicle within 24 m ahead and 4.5 m sideways of the far kerb) misses the near lane and a car 25 m away that arrives in under two seconds.
   `RoadBusy` becomes a gap check: busy if any vehicle on that road would reach the crossing line before the walker is across plus a 2 s margin, or is standing on the line (a queue stopped on the
   crosswalk). The walker gives up, waits 0.4 to 1.1 s and chooses again, as the prototype's does. `ITrafficQuery` gains the start of the crossing and the walker's speed; the golden tests' stub ignores them.
3. **Cars stop for people.** The prototype's cars only stop for the player. A car now treats anyone on the carriageway in its lane corridor as it treats the player: it brakes to stop short of them (a firmer
   6 m/s2, 3 m clear), whatever its type and speed. People on a road never stop to idle there. The crowd publishes who is on the asphalt (`IPedestrianQuery`), so the cost is a short list.

None of this affects a car's motion when nobody is on the road, so the traffic golden check below still holds. The safety net is an end-to-end test: run both simulations for ten simulated minutes at a
heavy bias and assert that no person is ever inside a vehicle's footprint.

## Architecture

**Core (pure, in the fast loop), new folder `Core/Traffic/`:**
- `VehicleCatalog` (the four ground and two aerial types: dimensions, hull profiles, wheels, cruise range, acceleration, share), `FleetBudget.Distribute` (largest-remainder allocation across types, as the prototype's).
- `Vehicle`, `Flyer`, `VehicleFactory` (the fleet, paint and under-glow picks drawn in the prototype's order from `Rng(31771)`), `SignalClock`.
- `TrafficSim`: `Create(nav, player)`, `Step(dt, observer, people)`, spawn and recycle, resizing to a tier's budget without popping anyone into view, `RoadBusy`, the four nearest vehicles for colliders.
- `VehicleMeshes`: hulls extruded from the side profiles with the roof and nose taper, wheels, the rider, wings, and the glow rigs (lamps, indicators, under-glow, beams, thrust streaks), the contact-shadow disc and the signal masts, as plain
  vertex and triangle arrays like `PedestrianRig`.
- `TrafficFrame` and `VehicleGpuData` (64 bytes a vehicle): cull, interpolate, order by type.
- `QualityTier` gains `GroundVehicles`, `Aircraft`, `VehicleLights`; `FlowOptions.NoTraffic` (`--no-traffic`) for measuring.
- In `Crowd/`: `CrowdRules` (`Prototype` and `City` presets), `IPedestrianQuery`, the extended `ITrafficQuery`.

**Unity, assembly `Cypher.Actors`:**
- `Cypher/Vehicle` shader: URP PBR with the city's night fills, faceted normals from screen-space derivatives (as the people), paint, rubber and rider surfaces; wheel and light placement is in the vertex shader from the vehicle's
  record, so a draw covers every vehicle of a type. `Cypher/VehicleGlow`: additive vertex-colour geometry with fog, for the ground rig and the aircraft. `Cypher/Signal`: the lens colours come from one global (which axis is green, amber or not),
  so a changing signal costs no upload.
- `TrafficRenderer`: about twelve draws for everything (six hulls, wheels, ground glow, aircraft glow, shadows, signal lenses, one static mesh for the 256 masts).
- `TrafficController`: starts at New Game, removed at the title; steps at 60 Hz with interpolation like the crowd; follows the tier. `TrafficProxies` (four kinematic boxes), `TrafficLights` (up to six pooled URP point lights,
  no shadows, riding the nearest cars; the renderer is already Forward+, so they cost little).
- `CrowdController` is given the traffic as its `ITrafficQuery`, and the traffic is given the crowd as its `IPedestrianQuery`; the crowd steps first.

## Decisions I will make without asking (say if you disagree)

- Same as the crowd: traffic and aircraft are made round the player when a game starts and removed at the title.
- Drop the prototype's box level of detail for vehicles. A hull is 24 to 32 triangles, so it saves nothing worth four more draw calls; wheels (36 triangles each) dominate. I will measure first.
- The prototype's budgets as the starting counts, to be trimmed at Gate B.
- The prototype's `roadBusy` is replaced, not ported, because it only works when nobody crosses; the rest of `Traffic.js` is ported as is.
- Vehicles are solid to the player and stop for the player in their lane, as in the prototype; nothing is hurt, no sounds yet (step 12).

## Questions (answered 2026-10-22)

Answers: **1** light, about 1 in 10 arrivals at a kerb cross (weight 2.0); **2** people wait for a gap, and cars stop only for someone already on the road.

1. **How many people should cross?** The weight is one constant, easy to retune at Gate B (3.0 would be about 4 in 10, 4.0 about 7 in 10).
2. **Who has priority at a crosswalk?** The crosswalks are mid-block, with no signal.

## What building it changed (2026-10-23)

The plan held except for these, found by the tests.

- **The prototype's traffic has three faults, mended under `TrafficRules.City`** (the golden tests use `TrafficRules.Prototype` and pin the port to the prototype exactly):
  (a) a vehicle waiting at a signal, a few centimetres short of or past its waypoint, steers back for it and **swings round on the spot** (it now holds its heading within a metre of its waypoint);
  (b) a vehicle that overshoots a waypoint or has spun finds the next waypoints "behind" it too and **skips through the junctions of half the grid in a few steps, then drives across the blocks**
  (4 of 96 vehicles in 3 minutes in the prototype itself; waypoints now count as passed when the vehicle is beyond them along its road);
  (c) the player standing in the road is only stopped for from 14 m, **too late for anything over about 13 m/s**, so a bike runs up to them (the player is now stopped for like anyone on the road: planned 40 m out, 3 m short).
- **The crossing rules grew.** `CrowdRules.City` also has `LookBeforeStepping`: a walker re-checks the road every step until they leave the pavement (the prototype checks once, on arriving at the kerb, so someone put down
  facing a crossing, or with a car appearing in the second it takes to reach the kerb, walks out in front of it). `RoadBusy` allows for vehicles speeding up as hard as they can (not at their current speed), for one waiting at a
  red signal setting off when its light turns green, and for **vehicles about to turn onto the road** (the prototype's, and my first version, only saw vehicles already on it). A vehicle also watches where a walker
  is heading, not only where they are, and brakes for someone about to step into its lane. A vehicle is not put down within 12 m of anyone on a road.
- 48 bytes a vehicle (not 64); the solid boxes are the size of the vehicle they are leased to (the prototype's was one size); the draw count is 12 at most.
- **How safe is it?** In ten simulated minutes at Medium (110 people, 52 vehicles) nobody is ever inside a vehicle (closest 0.7 m). With the top tier's full crowd and fleet, nothing moving at more than 2 m/s ever hits anyone. What the
  crowd does not do, as the prototype's does not, is steer round vehicles: in a crush (400 people, a heavy bias) someone is now and then pushed against the side of a vehicle that is stopped or creeping.

## Build order and tests

1. **Crossing bias** (small, first). `CrowdRules`; `CrowdSim` takes it; `ITrafficQuery` extended; `OnRoad`; no idling on the road. Tests: the prototype preset still passes every golden test unchanged; at weight 2.0 the share of
   kerb arrivals that cross is within the estimate over thousands of arrivals; people stay on the graph; a busy road makes them wait and choose again; nobody idles on the asphalt; `OnRoad` is exact.
2. **Traffic sim and golden check.** `tools/gen-golden-traffic.mjs` runs the prototype's real `Traffic.js` headless (as for the crowd): the fleet table, the seeded start, and every vehicle's position, heading, speed, leg and turn after
   0 to 3,600 steps, the signal phase, and the aircraft; also the hull geometry's vertices. Tests beyond the golden: stays in lane; never overlaps the car ahead; stops at red and goes on green; amber rule; turn mix; recycles
   in the spawn band; resizing keeps vehicles in view; the nearest four are chosen correctly; determinism.
3. **Meeting the crowd.** Gap check and stopping distance: for every type and speed, a car approaching someone in its lane stops short; a walker who sets off on a clear gap is across before the car arrives; the ten-minute
   no-overlap run; a queue on the crosswalk blocks it.
4. **Unity layer.** Shaders, renderer, controller, proxies, lights, signals, scene wiring (`SliceSceneBuilder`), `-NoTraffic` in `unity-check.ps1`. Tests: shaders compile; the buffer layout; EditMode renderer smoke; PlayMode: traffic
   appears when a game starts and is gone at the title, vehicles stay on the lattice, a signal cycles and a queue forms at red, people cross with cars stopping for them over 60 simulated seconds, a proxy stops the player walking through
   a car, a tier change changes the count. **GPU check:** the shader's wheel and light placement against `VehicleMeshes` for every type, as the pedestrian pose was checked.
5. **Look and cost.** Capture shots (each vehicle type, a junction at red and at green, queue, headlight beams on wet road, a freighter in a canyon, a crosswalk with people and cars). Benchmark Medium, High and Ultra with and without
   traffic, and with and without the point lights; written into `performance_results.md`. Gate B proper is step 13.
6. `task.md`, memory, `-Step All` green.

Size: large, about the work of step 10, plus the crowd changes. Risks: the new yielding and gap rules are the only behaviour not in the prototype, hence the long no-overlap test; chaotic divergence from the prototype over long runs
could force shorter golden checkpoints (the crowd needed a tolerance of 1e-4); the glow cones add overdraw, so they are measured separately.
