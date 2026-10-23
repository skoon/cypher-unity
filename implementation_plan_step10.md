# Step 10: pedestrians (Milestone 3)

Status: **approved and built 2026-10-22** (see `task.md`), with the answers recorded under "Questions". Part of `implementation_plan_m2.md` (approved 2026-10-20); that plan fixed the
rendering approach (limb animation in the vertex shader, one or two draws) and the pooled, tier-budgeted crowd. This plan fills in how.

## What the player sees

People walk the pavements of the whole 7x7 city, following the ring of kerb nodes round each block and crossing roads at the kerb
crosswalks. They slow for each other, glance at the player, stop and idle for a few seconds, some hurry, some carry umbrellas in the rain.
A handful of the nearest are solid: the player cannot walk through them. They are drawn in a few draw calls, lit like the rest of the city, with
glowing trim on some coats. The crowd is centred on the player and thins out and recycles at the edge of the area they can see, exactly as in the
prototype ([Crowd.js](file:///D:/source/cyberpunkish/src/ai/Crowd.js), 994 lines). Traffic comes in step 11; until then nobody waits at a kerb.

## What is ported, unchanged

All of the behaviour in Crowd.js, with the same numbers and the same random-number order, so it can be checked against the prototype:
the walk/hurry/idle state machine, target picking along the node graph (prefers straight on, dislikes crossings), the lateral offset within the
walking band, the speeds (cruise 1.12 to 1.54 m/s, hurry 1.85x), turn-rate and brake limits, the 4.2 m spatial grid and the ahead-only avoidance push,
player avoidance within 3.2 m and a hard 0.62 m keep-out, the spawn band (112 to 144 m from the player) and the 148 m recycle radius, the 8 leased
capsules (nearest peds within 6 m), the appearance table (scale, build, long coat, umbrella, hood, pack, trim, six skin tones, ten coats, accents),
and the rig itself (proportions, gait, hip drop, shoulder counter-rotation, head glances). Pooling: the pool is built for the top tier; a tier change
retires peds that are out of view and more than 12 m away, or recycles pool members back in out of view.

## Architecture

**Core (pure, no engine, in the 4 s fast loop), new folder `Core/Crowd/`:**
- `PedestrianTraits`: the appearance table from the seeded stream (`makeRng(80553)`, same order as `_buildPeds`).
- `CrowdSim`: the simulation. State in plain structs (`x z y heading phase speed node prevNode target lat crossing state timer ...`), one `Step(dt, observer, player, traffic)`.
  `ITrafficQuery.RoadBusy(x, z, Crossing)` is the seam for step 11; step 10 plugs in a null that is never busy.
  Also the spatial grid, avoidance, recycling, pool resizing, and `NearestForProxies`.
- `PedestrianRig`: the proportions and the body-part meshes (torso short and long, head, hood, arms, thighs, shins, pack, umbrella, trim), built from the existing
  `PropMeshBuilder` primitives. Each vertex carries its part id; positions are in the joint's own frame, as in the prototype. Three LOD meshes: the full rig (about 410
  triangles), a simple 4-part body, a single-box silhouette.
- `PedestrianPose`: the prototype's `_pose` ported to C# as the reference for the shader (same formulas, same constants). It is the oracle in tests, not used per frame.
- `QualityTier` gains `Pedestrians` (and the LOD distances), through `QualityDirector.TierApplied` as the props did.

**Unity, new assembly `Cypher.Actors` (references Core, City, Rendering):**
- `Cypher/Pedestrian` shader: a vertex shader that poses every vertex from its part id and the instance's gait parameters (phase, speed, scale, build, look yaw), reading
  per-instance data from a `StructuredBuffer`; lighting shared with `CityLit` (the moon, the three fill lights, fog, ambient); faceted normals; the trim part is emissive.
  A second, tiny pass draws the blob contact shadows (the prototype's soft disc under each ped; scene shadows are off).
- `CrowdRenderer`: every frame, frustum-tests the active peds on the CPU, sorts the visible ones into the three LOD ranges of one `GraphicsBuffer`, and issues one
  `RenderMeshPrimitives` call per LOD plus one for the shadows: about four draws for the whole crowd. 64 bytes per ped; 400 peds is 25 KB a frame.
- `CrowdController`: a component that waits for the city, seeds the crowd round the player (or the title camera), steps the sim at 60 Hz with an accumulator and interpolation,
  and follows quality tier changes. `CrowdProxies` leases eight kinematic capsule colliders to the nearest peds.
- `CityBuilder` / `CitySlice` gain a `CityNav`, built from the plan right after it (392 nodes, microseconds).

## Decisions I will make without asking (say if you disagree)

- The sim runs at 60 Hz (the prototype's fixed step) with render interpolation, and freezes when the game is paused (time scale 0).
- Shader rig, not CPU posing: posing 14 parts per ped on the CPU is what made the prototype's crowd the dearest CPU system after the renderer.
- The accessories (hood, pack, umbrella, trim) are part of the one full mesh and are collapsed to nothing by the shader when a ped lacks them, rather than separate draws;
  I will measure and split them if the vertex cost shows.
- Physics: eight kinematic capsules (radius 0.30 m, height 1.64 m, as the prototype), nobody else collides.
- No reactions to the player beyond stepping aside and glancing; no combat, no sounds yet (step 12).

## Questions (answered 2026-10-22)

Answers: **1** cheaper bodies on every tier (full rig to about 42 m, simple body to about 92 m, silhouette beyond); **2** no people behind the title screen: the crowd is created round the player when a game starts; **3** the prototype's counts, 36 / 60 / 110 / 320 / 400.

1. **Level of detail.** The prototype only drops to cheaper bodies on its lowest tier. Cheaper bodies beyond about 90 m on every tier saves vertex work that
   nobody can see at 540p, and costs a visible pop at the switch (hidden by hysteresis and fog).
2. **The title screen.** The plan said a live city behind the menu. People walking the pavements below the title flyover would make that true, and cost frames
   that no gate measures.
3. **Starting populations.** The prototype's budgets are 36, 60, 110, 320 and 400 for Performance to Ultra. Gate B (step 13) decides the default tier from measurements, so the
   starting table matters little, but it decides what step 10's own checkpoint measures.

## Build order and tests

1. **Core sim.** `PedestrianTraits`, `CrowdSim`, nearest-for-proxies, pooling. Tests: every ped stays on the graph (its target is always a node of its block ring, a crosswalk
   end, or within the walk band of one), never inside the player's keep-out, speeds within limits, state-machine transitions, recycle lands in the spawn band, avoidance pushes the
   right way and only for people ahead, a kerb wait while the road is busy, a tier change keeps visible peds, determinism for a seed. **Golden check against the prototype:** run
   `Crowd.js` headless under node with stubbed engine context for a few hundred steps and compare every ped's position, heading and state with the C# sim.
2. **Rig.** `PedestrianRig` meshes and `PedestrianPose`. Tests: part ids, bounds (about 1.72 m tall), triangle counts per LOD, pose symmetry and ground contact, and a golden
   check of `_pose`'s part matrices against the prototype for a spread of states.
3. **Unity layer.** Shader, renderer, controller, proxies, builder hook, tier lever, scene wiring (`SliceSceneBuilder`). Tests: shader compiles; the instance buffer's layout and size;
   LOD assignment and hysteresis; EditMode renderer smoke; PlayMode: the crowd appears after the city builds, peds keep to the pavement and crosswalks over 60 simulated seconds, a
   proxy stops the player walking through a ped, a tier change changes the count, pause freezes it. **GPU pose check:** a debug pass writes each vertex's posed position to a
   float texture and the test compares it with `PedestrianPose` (falls back to screenshots if that proves fiddly).
4. **Look and cost.** Capture shots (idle, mid-stride, umbrella, crowd on a street, title flyover). A checkpoint benchmark of the plaza loop with the crowd at Medium, High and
   Ultra, written into `performance_results.md`; Gate B proper is step 13.
5. `task.md`, memory, `-Step All` green.

Size: large. Roughly the work of step 7. The risky part is the shader pose; the oracle and the GPU check are there to catch mistakes in it early.
