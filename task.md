# Milestone 2 — task checklist

Plan: `implementation_plan_m2.md` (approved 2026-10-20). Milestone 2 is complete: Gate A passed 2026-10-22. Milestone 3 (steps 9-13) is complete: Gate B passed 2026-10-23 at Medium (a partial measurement, see step 13).

- [x] 1. Settings core (2026-10-20): `GameSettings` (graphics, audio, gameplay, controls overrides) with validated ranges,
      dependency-free `Json`, `SettingsSerializer` (strict about wrong values, tolerant of missing/unknown keys),
      versioned `SettingsStore` with migrations, unusable files set aside as `.bad` / `.newer` with a notice,
      atomic `FileSettingsStorage`. Product/company name set to CYPHER / Cypher. 453 EditMode + 71 PlayMode pass,
      404 in the fast loop (which now runs `[TestCase]`s and `[TearDown]`; it used to skip them silently).
      Section classes are `*Options` because `GraphicsSettings` and `AudioSettings` collide with Unity's own.
- [x] 2. Quality tiers and adaptive governor (2026-10-20). Core: `QualityTiers` (five presets: internal height 360/450/540/540/540,
      MSAA 1/1/1/2/4, bloom Fast/Standard/Standard/High/High, rain cap 1000/2000/3000/3000/4500, probe 64/128/128/256/256;
      Medium is exactly the M1 look), `QualityGovernor` (port of the prototype's, all 24 of its tests ported), `QualityChange`
      with the player notice. Settings: Resolution Auto (0) lets the preset pick the height, a fixed value pins it.
      Unity: `QualityDirector` applies a tier to the low-res controller, the runtime pipeline asset (MSAA), the post volume's
      own profile copy (bloom), the rain cap and the probe, and runs the governor (target 35 FPS, downgrade-only; fires
      `Changed` for the caller to save and show the notice, wired in step 3). Benchmark gained `--benchmark-quality` and always
      runs with adaptive quality off. 504 EditMode + 94 PlayMode pass, 453 in the fast loop. All five tiers measured:
      p99 6.9 to 10.3 ms (see `performance_results.md`).
- [x] 3. Game flow (2026-10-20). Core (`Core/Flow`): `GameFlowMachine` (Boot, Loading, Title, Playing, Paused; which command does
      what; Quit only from the title and pause screens), `FlowPolicy` (what each state means: cursor, time scale, player, camera,
      governor), `LoadTracker` (weighted loading progress), `FlowOptions` (`--skip-title`, `--default-settings`, `--no-auto-pause`;
      `--benchmark` implies all three), `TitleFlight` (the title camera path: a slow 8 m glide along the avenues, corners
      rounded inside the junction, constant speed, gentle sways). New assembly `Cypher.Game`: `GameFlow` (applies the rules; one scene,
      city built once, Return to Title reloads nothing; pause = time scale 0; focus loss pauses a playing game, never in the Editor),
      `SettingsService` (loads settings at start, applies them to the scene, saves, saves and announces a lowered preset; the Editor
      uses `settings.editor.json`), `TitleCamera` (the placeholder keys and hint label that stood in until step 5 are gone: Esc pause/resume,
      Enter new game, Backspace title, Q quit; a line of text with progress, keys and notices). `FirstPersonController` now skips a
      zero-length frame (found when unpausing) and takes its look settings from Options. The benchmark waits for the game to start.
      554 EditMode + 122 PlayMode pass, 503 in the fast loop. The capture tool now also takes four title-screen frames.
- [x] 4. Input actions and rebinding (2026-10-21). Core (`Core/Input`): `ControlCatalog` (seven rebindable actions: the four move directions, Jump,
      Sprint, Crouch; two keyboard/mouse slots plus one gamepad slot, movement has no gamepad slot because the stick moves; allowed controls per
      slot; Esc, Enter, F3 and the gamepad's Start/Select reserved), `BindingMap` (the rules: one action per control per kind of device, every
      action keeps a keyboard or mouse control, a taken control is a Conflict the caller resolves with `SwapWith`, all-or-nothing Reset,
      differences-only persistence with bad entries named and contradictions falling back to defaults), `BindingText` (player-facing names).
      Unity (`Cypher.Player`): `InputBindings` (shared, `Changed` event), `InputSystemCommandSource` rebuilt from it on every change,
      `BindingCapture` (waits for a key, mouse button or gamepad button; Esc or a timeout cancels). `SettingsService.Bindings` saves a rebind the
      moment it is made and restores it at launch; applying an older settings copy never undoes a rebind. 691 EditMode + 162 PlayMode pass,
      640 in the fast loop. The Controls screen that uses all this is step 5.
- [x] 5. UI Toolkit front end (2026-10-21). New assembly `Cypher.UI`: `UiRoot` (owns the UIDocument and a code-made PanelSettings, 1920x1080
      reference, the `ScreenStack`, notices, the Back action on Esc / gamepad Start / gamepad B, tab switching on PageUp/PageDown / LB/RB, and
      the whole rebind flow), screens in `Screens.cs` (Loading with progress and fade, Title as a left menu column over the live flyover, Pause,
      confirmation and rebind dialogs, toasts) and `OptionsScreen.cs` (Graphics, Audio, Gameplay, Controls; changes apply live and are saved
      600 ms after the last one; per-tab Reset, Reset All on Controls). Core (`Core/Ui`): `ScreenStack`, `OptionChoices`, `GridFocus`,
      `NoticeQueue`. Quit and Return to Title ask first; Resume and Options never do. Player `--shots DIR` walks the menus and writes 14
      screenshots (`tools/unity-check.ps1 -Step Shots`). `tools/player-keys.ps1` drives the built player with real OS key presses (arrows, Enter,
      Esc, PageDown, a real rebind, pause/resume/confirm) and checks the player log: all 11 steps pass. The player logs `[Cypher] ui screen X`.
      702 in the fast loop, 753 EditMode + 232 PlayMode pass; benchmark still passes (133 fps avg, p99 11.6 ms).
- [x] 6. Whole 7x7 city (2026-10-21). Core: `CityChunkJob` (the chunker's work in pieces), `WorldEdge` (play area = the city plus a 30 m
      apron, four invisible wall boxes just outside it), `SkylineRing` (135 windowed towers in two rows beyond the wall, built with the real
      `BuildingMesher`, 1,350 triangles in about two dozen segments, no colliders), `ProbeLayout` (a 3x3 grid of reflection probes, each
      captured at a road junction near its district's middle and projected onto a box that overlaps its neighbours by 72 m),
      `BenchmarkRoute` + `--benchmark-route centre|city` (the city tour runs every road at the edge, 2.2 km a lap, 240 s by default).
      Unity: `CityBuilder` defaults to the whole city and builds over frames (`BuildOverFrames`, 24 ms a frame, progress and a status line from
      a `LoadTracker` weighted by each stage's measured time; `Build()` still builds at once for tests); the world edge and ring only go with the
      whole grid; `SurfaceMaterials.Steps` makes the textures one at a time; `StreetReflectionProbe` manages a set of probes (the old single-box
      `Configure` still works); `GameFlow` follows the city's progress and asks for the probe capture once the city is all there; the camera's far
      plane is 520 m (the fog is opaque long before). New capture poses: the edge looking out, a corner, the outer road, a long avenue, a far wet
      street, and a view of everything. `unity-check.ps1` takes `-Route centre|city`. The player logs `[Cypher] city built` with per-stage times
      and `[Cypher] load:` lines. The unused `FlowHotkeys` and `FlowHintLabel` are deleted and `ZzScrimDiagnostic` is now `UiTitleFadeTests`.
      Verified 2026-10-21: 740 in the fast loop, 791 EditMode, 252 PlayMode (in three runs, after the last edits), `tools/player-keys.ps1` all 11 steps
      against the whole city. Measured: 71,492 triangles, 1.1 to 1.2 s of build CPU, title up 6.9 s after launch; the old loop round the plaza
      102 FPS, p99 16.9 ms; the city tour 87 FPS, p99 24.5 ms over its first 100 s (see `performance_results.md`, including why the long
      runs are not trusted). No streaming needed. **Known look item for Gate A:** at the edge the wet ground reflects the moon as a bright white
      sheen at grazing angles (open space lets you see it to the horizon); it is the same wet-road shading as everywhere, just more visible.
      Hitches: the tour has scattered 35 to 70 ms frames (30 of 8,700), not tied to one place; Gate B will tell if they matter with actors.
- [x] 7. Props (2026-10-21). You chose: cables and steam in, pooled lights and kickable junk out, props merged per block and hidden by distance.
      Core: `PropMeshBuilder` (boxes, cylinders, spheres, tubes and quads with a colour and a per-vertex surface: emission, roughness, metalness),
      `PropKits` (31 kits ported from Props.js: street furniture, decay, cyberpunk fixtures, rooftop gear; footprints and colliders as the prototype),
      `Canvas2D` (a small antialiased software canvas) and `PropTextures` (the grime map, the graffiti and ground-decal atlases stacked in one
      1024x2048 texture, the steam puff; same seeds and random order as the prototype), `PropPlacer` (the placement: wall-run clutter, alley junk, kerb
      furniture, kerbside deliveries and roadworks, wall fixtures, ATMs, rooftop gear, the plaza, ground and wall decals, cables and conduit, steam;
      the spawn pocket is kept clear), `PropCellMesher` (one props mesh and one decals mesh per block, colliders per block), `SteamMesh`, `PropBake`
      (all of it in one call, so it can run on a worker). Unity: `CityLit` gained a per-vertex surface mode (`_PropSurface` 1 props, 2 decals with alpha
      blending), new `Cypher/Steam` shader (puffs animated entirely in the vertex shader), `CityMaterials` gained optional Props/Decals/Steam,
      `CityBuilder` bakes the props on a worker thread right after the plan and, once the blocks are up, makes the meshes, textures, one oriented box
      collider per solid prop and the steam (stage `props`, `[Cypher] props:` log line), `PropDressing` hides a block's props beyond the draw distance (with
      a margin so nothing flickers) and owns the steam, `QualityTier.PropDistance` (70/95/125/160/210 m) and `Steam` (off on Performance) are tier levers
      applied through `QualityDirector.TierApplied` and `GameFlow`. Capture tool: refreshes the prop distance after each camera move and photographs a puff.
      Verified: 832 fast-loop, 905 EditMode, 259 PlayMode (a real player is stopped by a barrier; every collider is in the physics world, turned like
      its prop). Measured in the Development player: 8,928 props (698k triangles with the cables), 8,641 decals, 1,311 colliders, 96 puffs; the worker
      takes about 1.1 s in the Mono player (layout 258 ms, meshes 460, textures 376) and the main thread never waited for it; the main thread spends
      263 ms making the objects; the title is up 7.0 to 7.1 s after launch. Frame: the old loop round the plaza 92.5 FPS, p99 18.4 ms (triangles 365k average,
      draw calls 279); the city tour (first 100 s) 116.9 FPS, p99 14.4 ms, 3 frames over budget. Not run this time: `tools/player-keys.ps1` (it could not
      get the foreground; nothing in step 7 touches input).
      Not ported: facade anchors (the pass over every facade places the same fixtures and the ATMs), the pooled lights, the kickable props.
- [x] 8. Signage upgrade and **Gate A** (2026-10-22). Gate A passed (see below). Decided without asking (nothing here was a real fork): the ad art is four 512 px panels in a
      1024 px atlas; the lettering atlas is one channel at 2048 px; the pooled real lights stay out; each hoarding and hologram keeps the prototype's glow wash (a soft
      radial quad), which the A/B runs showed costs nothing measurable.
      Core: `StrokeFont` (Latin A to Z, 0 to 9, `. , : / - + %` and the yen sign, the prototype's 29 kana, 12 kanji-like blocks and 8 icons, written as a small string
      DSL of strokes, no OS font), `GlyphPainter` (strokes as thick round-ended lines, three weights) and `AlphaBlur` (three box passes), `Canvas2D` gained polygons,
      linear gradients and ellipse strokes, `NeonAtlas` (the 2048 lettering atlas: 24 words in three styles, 4 kana bands, 14 kanji plaques, 8 vertical words, 8 kana columns,
      8 icons, each with a wide halo, a mid glow and a hard core; a cheap `Layout` for the planner and a slower `Render` for the worker), `BoardArt` (the four ad panels,
      ticker repeated a whole number of times so it wraps without a seam), `SignBake` (both atlases in one call for a worker), `SignVantage` (which signs to photograph).
      `NeonPlanner` now draws from three random streams (shapes, lettering, boards and holograms), so changing the lettering never moves a tube; `Quad.Cell` names the
      lettering, `NeonLayout.Boards` (99 hoardings: street walls over 16 m tall, a coin toss each, in a dark frame with four rim tubes and a glow wash) and
      `Holograms` (11 of 16 tries over buildings taller than 40 m, never inside a lot). `NeonMesher` gives panel quads their atlas coordinates (U is flipped: Unity's axes
      mirror the prototype's, so lettering is read from the quad's right-hand edge, which is tested) and builds a Boards and a Holos mesh per block.
      Unity: `NeonAdd` panels read the lettering atlas (the procedural bars are gone), new `Cypher/NeonBoard` shader (one shader, `_Holo` 0 an opaque hoarding with fog
      blending, 1 an additive projection; glitch tearing, ticker scroll, scanlines that fade out when they cannot be resolved at 540p, colour split, bezel; the panel is found
      from the atlas coordinates and `_PanelInset`), `CityMaterials` gained optional `Boards` and `Holos` (assets `CityBoards`, `CityHolos`), `SurfaceMaterials.MakeMask` and
      `CreateSigns` (instances, so the project's materials stay untouched), `CityBuilder` bakes the signs on a worker next to the props and adds a Boards and a Holos renderer to
      each block that has them. The capture tool now photographs the nearest hoarding (far and close), hologram, shop sign, plaque and blade.
      Verified 2026-10-22 on the final code: `unity-check.ps1 -Step All` green (Build, 1,026 EditMode, 259 PlayMode), 942 in the fast loop (about 110 new: the font, painter, blur,
      atlases, board art, planner streams, hoardings, holograms, mesher, bake, vantage, the shader compiles and the builder's renderers and materials). Looked at: every sign kind reads the
      right way round from the street, the hoarding art, ticker and tearing play, the hologram floats; at glancing angles heavy lettering merges into its glow (expected).
      **Gate A: passed.** City tour (first 100 s): p99 14.9 and 18.7 ms in two runs (limit 33.3), 102.8 and 88.1 FPS, 2 frames over budget in one run (a 50 ms hitch); loop round the plaza
      74 to 92 FPS, p99 15.9 to 19.6 ms. Title up about 7.9 s after launch (limit 10 s). The machine was busy all session (CPU 30 to 45%), so absolute numbers read lower than
      step 7's; A/B builds without the glow washes and without the screens at all were within that noise (details in `performance_results.md`).
      Not run: `tools/player-keys.ps1` (it needs the PC free; nothing in step 8 touches input). Not ported: the pooled real lights and the camera-facing glow cards on small
      signs (light spill is the glow pools).

Milestone 3: living streets (steps 9 to 13 of `implementation_plan_m2.md`; Gate B at the end).

- [x] 9. Navigation (2026-10-22). Port of `CityNav.js` as `Core/Nav/CityNav.cs` (`Crossing`, `NavBlock`, `NavGeometry`, `CityNav`): a ring of eight nodes round every block (four
      corners and four kerb-edge midpoints, in cyclic order) with a crosswalk edge between the facing midpoints of neighbouring blocks (`Crossing.X` where the pedestrian walks
      along Z over a road carrying X traffic, `Crossing.Z` the other way), the road centrelines `XLines` and `ZLines` (the intersection lattice), `KerbDistance`, `GroundY` and
      `NodeInBand`. Decided without asking: the prototype probed the kerb with physics raycasts to learn where the pavement ends; the Core knows the geometry, so it is given
      (`NavGeometry.City`: 62 m cells, 46 m blocks, pavement out to 25.5 m, kerb 0.185 m) and navigation stays pure, with no engine, no physics and no load-time cost. The
      prototype's probe steps by 0.3 m and so reads the kerb as 25.4 m; the golden test feeds the generic build that same figure and gets the prototype's numbers exactly.
      `KerbDistance` is found one axis at a time (the blocks are a full grid, which the build checks and refuses otherwise) instead of scanning all 49 blocks.
      The ring radius is 24.25 m, the walkable band 0.8 m either side, the asphalt 5.5 m either side of a road centreline.
      Verified: `tools/gen-golden.mjs` now also runs the prototype's `buildNav` on the golden city (with a flat world standing in for the physics probe) and writes
      `nav_golden_20771.txt`; 28 new tests (23 property tests plus 5 golden checks of all 392 nodes, their neighbours and crossings, the centrelines, ground and kerb distance at
      ten points and the band search from the same draws), 970 in the fast loop. Nothing in the Unity layer yet: step 10 will hang a `CityNav` on the builder when pedestrians need it.
- [x] 10. Pedestrians (2026-10-22). Plan: `implementation_plan_step10.md`. You chose: cheaper bodies by distance on every tier, no people behind the title screen (the crowd is made round the player
      when a game starts and removed at the title), the prototype's counts (36 / 60 / 110 / 320 / 400 on Performance to Ultra; Gate B will trim them).
      Core, new folder `Crowd/`: `Pedestrian` + `PedestrianFactory` (the appearance table drawn in the prototype's random order), `CrowdSim` (the prototype's whole crowd logic: walk, hurry and idle states, target choice
      along the kerb-node graph, the 4.2 m neighbour grid with ahead-only avoidance, player avoidance and a 0.62 m keep-out, the 112 to 144 m spawn band and 148 m recycle radius, pool resizing that never pops anyone into or out of
      view, the eight people nearest the player), `ITrafficQuery` (the seam for step 11; nobody waits at a kerb yet), `Mat4` + `PedestrianPose` (the rig's joint maths, the reference the shader is checked against),
      `PedestrianRig` (every part in its joint's frame: 706 triangles and 917 vertices for the full body with all accessories, 56 for the rigid body, 12 for the box, the contact-shadow disc), `PedestrianGpuData` (80 bytes),
      `Frustum` and `CrowdFrame` (cull, interpolate, level of detail with 6 m hysteresis, sort into three runs, the head's glance at the camera), `CrowdVantage` (capture shots). `QualityTier` gained `Pedestrians`,
      `ActorNear` and `ActorFar`; `--no-crowd` leaves the streets empty (a control for measuring).
      Unity, new assembly `Cypher.Actors`: `Cypher/Pedestrian` shader (a vertex shader that poses every vertex from its part id and the person's gait, read from a structured buffer; URP PBR with the city's night fills,
      shared with CityLit through the new `CityNightFill.hlsl`; faceted normals from screen-space derivatives; glowing unlit trim), `PedestrianPose.hlsl` (the pose maths, shared with a compute probe), `Cypher/PedestrianBlob`
      (the soft shadow under each person), `CrowdRenderer` (one instanced draw per level of detail plus one for the shadows through `Graphics.RenderMeshPrimitives`: three or four draw calls for the crowd), `CrowdProxies`
      (eight kinematic capsules leased to the nearest people), `CrowdController` (starts and stops with the game flow, steps at 60 Hz with interpolation, follows the tier). The capture tool photographs the nearest
      person and one of each kind. A hand-rendered camera must call `CrowdController.DrawNow()` just before it renders, because the draws are queued per frame in LateUpdate.
      **Checked against the prototype, not just against ourselves:** `tools/gen-golden-crowd.mjs` runs the prototype's real `Crowd.js` headless; the C# crowd matches its appearance table for all 400, and every
      active person's position, heading and state after 0 to 5,400 steps (90 s) seeded, and after 0 to 900 steps standing at kerbs, with and without a stub traffic query that holds people back; the C# pose
      matches all 555 joint matrices of 60 synthetic states to 5e-7; and the shader matches the C# pose on the GPU for 39,822 vertices (worst difference 0.00002 m).
      **Finding, kept as the prototype has it:** people almost never choose to cross a road. A crosswalk is at right angles to the way someone is walking, so it scores at most 0.196 against at least 0.55 for carrying on
      round the block, and only a spawn or recycle that happens to face the road starts one. Crowds therefore circle their own block. Worth a decision when traffic arrives (step 11).
      Verified 2026-10-22 on the final code: `unity-check.ps1 -Step All` green (1,204 EditMode, 270 PlayMode), 1,102 in the fast loop (about 270 new). Cost (details in `performance_results.md`): on a quiet machine the
      crowd costs about 0.5 to 0.9 ms at Medium on the plaza loop and nothing measurable at the other tiers or on the whole-city tour (127 FPS with and without 110 people); three or four draw calls; launch to play unchanged
      at about 7 s. Looked at: people walk with a proper stride, swing their arms, carry umbrellas, wear long coats, hoods and packs, glow where they have trim, and stand on soft shadows.
      Not done: real traffic to yield to (step 11), footsteps (step 12), people on the title screen (your choice), `tools/player-keys.ps1` (needs the PC free).
- [x] 11. Traffic, aircraft and people who cross the road (2026-10-23). Plan: `implementation_plan_step11.md`. You chose: a light crossing bias (about 1 in 10 arrivals at a kerb midpoint cross, weight 2.0), people wait for a
      gap and cars stop only for someone already on the road. Also as before: no traffic behind the title screen (made round the player when a game starts), the prototype's budgets (18 / 30 / 52 / 74 / 96 vehicles and
      5 / 12 / 20 / 28 / 36 aircraft on Performance to Ultra; Gate B will trim them).
      Core, new folder `Traffic/`: `VehicleCatalog` (the four ground and two aerial types, palettes) and `FleetBudget`, `Vehicle`, `Aircraft`, `SignalClock`, `SignalLamps`, `TrafficSim` (the prototype's whole logic: lane-following
      pursuit, car-following, braking to red and amber, turn choice, left turns giving way, the 120 to 215 m spawn band and 250 m recycle, resizing to a tier's budget without popping anything into view, aircraft in six altitude
      bands, the four nearest vehicles for colliders), `TrafficRules`, `VehicleMeshes` (hulls extruded from profiles, wheels, the rider, wings, the glow rigs and cones, the shadow, the 256 signal heads and masts),
      `VehiclePlacement` (the per-frame part transforms; the shader's reference), `TrafficFrame` and `VehicleGpuData` (cull, interpolate, sort by type, pick the vehicles for the point lights), `TrafficVantage` (capture shots).
      In `Crowd/`: `CrowdRules` (`Prototype` and `City`), `IPedestrianQuery`, and `ITrafficQuery.RoadBusy` now takes the whole crossing and the walker's speed. `QualityTier` gained `GroundVehicles`, `AircraftCount` and
      `VehicleLights`; `--no-traffic` and `--no-vehicle-lights` are measuring controls.
      Unity, in `Cypher.Actors`: `Cypher/Vehicle` (URP PBR with the night fills; bodies, wheels, aircraft and the masts), `Cypher/VehicleGlow` (added light: lamps, under-glow, cones, thrust), `Cypher/VehicleShadow`,
      `Cypher/Signal` (the lit lamp comes from one global), `VehiclePose.hlsl` (the placement maths, shared with the compute probe), `TrafficRenderer` (at most 12 draw calls), `TrafficController` (starts and stops with the
      game flow, 60 Hz with interpolation, follows the tier, steps after the crowd), `TrafficProxies` (four solid boxes the size of the vehicles), `TrafficLights` (six pooled real point lights, two on Performance).
      **People cross roads now.** `CrowdRules.City` weights a crosswalk 2.0 against the prototype's 0.45, which could never win; people also look before stepping off the pavement, never idle on the road, and wait for a gap
      (`TrafficSim.RoadBusy` counts vehicles speeding up as hard as they can, ones about to turn onto the road, and ones waiting at a red that will turn green); vehicles stop 3 m short of anyone on the road, and of anyone
      walking into their lane. **Checked against the prototype, not just against ourselves:** `tools/gen-golden-traffic.mjs` runs the prototype's real `Traffic.js` headless; the C# traffic matches its fleet table (type, speed,
      paint and under-glow for all 96, the aircraft), and every vehicle's and aircraft's position, speed, junction, turn and signal phase after 0 to 3,600 steps (60 s; 2,700 for the full fleet, see below), including with
      the player standing in a lane; `tools/gen-golden-vehicles.mjs` checks every mesh's triangles and the matrix of every part of every vehicle and aircraft to 1e-5; and the shader places 14,000 vertices where the C#
      reference does (worst difference logged by the PlayMode test).
      **Faults in the prototype's traffic, kept as it has them under `TrafficRules.Prototype` and mended in the game's `TrafficRules.City`:** a vehicle waiting at a signal swings round to face a waypoint a few centimetres
      away; a vehicle that overshoots a waypoint then skips through the junctions of half the grid and drives across the blocks (4 of 96 in 3 minutes, in the prototype itself); and the player in the road is only stopped for
      from 14 m, too late for anything over about 13 m/s. One more oddity, left alone: a vehicle waiting to turn across traffic does not wait at the line, it coasts 2.5 m into the junction and goes (so the golden check
      of the full fleet stops at 45 s, where such a vehicle steers for a point exactly behind it and the direction of its turn depends on the last bit of atan2).
      **How safe:** ten simulated minutes at Medium and the top tier's full crowd and fleet: nobody ever inside a vehicle at Medium (closest 0.7 m), and nothing moving over 2 m/s ever runs into anyone at any tier; the
      crowd does not steer round vehicles (neither does the prototype's), so in a crush someone is now and then pushed against a stopped or creeping one.
      Verified 2026-10-23 on the final code: `unity-check.ps1 -Step All` green (1,421 EditMode, 289 PlayMode), 1,286 in the fast loop (about 190 new). Cost (details in `performance_results.md`): about 0.3 to 1 ms of GPU
      and 3 to 10% of the frame rate at every tier, twelve draw calls at most; launch to play unchanged at about 7 s. Looked at: vehicles with wheels, lamps, under-glow, headlight cones on the wet road and soft shadows,
      aircraft with thrust streaks and navigation lamps, signals changing at the junctions.
      Not done: ambient audio (step 12), `tools/player-keys.ps1` (needs the PC free).
- [x] 12. Ambient audio (2026-10-23; **built and checked, not yet listened to**). Plan: `implementation_plan_step12.md`. You chose: the bed synthesised live, plus the player's foley, sounds from the real traffic and
      people, and the music score; nothing on the title screen but the menu sounds.
      Core, new folder `Audio/` (with `Synth/` and `Recipes/`): a Web-Audio-shaped synthesis engine (`SynthGraph`, `AudioParam` automation, oscillators on band-limited wave tables, noise loops, biquads, gains, delays, a
      panner, queued players, Chromium's dynamics compressor), `SoundBank` (38 recipes: 10 menu sounds, 6 ambient one-shots, footsteps on 7 surfaces, jump, land, cloth, breath, 11 music instruments), `Bed` + `AmbientEvents`
      (seven continuous layers and six random-event clocks), `Score` (the prototype's generative synthwave) + `ScoreNotes` + `ScoreRender`, `SpaceProbe` + `ReverbSpaces` (alley, street, plaza, indoors, open),
      `Footing`/`StrideClock`/`BreathClock`, `Occlusion`, `VoicePool`, `BusLevels`, `MasterStage` (glue + limiter + master, in code), `ClipPlan`, `ActorSounds`, `LiveGraphs`, `AudioAnalysis`, `AudioNoise`, `PointGrid`, `WavFile`,
      `AudioExport`, `AudioCost`.
      Unity, new assembly `Cypher.Audio`: `AudioEngine` (owns it all, follows the Options and the game flow), `AudioClipBank` (317 clips rendered on two worker threads at load), `SynthSource` (a graph on the audio thread through
      `OnAudioFilterRead`), `BedPlayer` (bed, neon buzz and air conditioning standing at the nearest real tube and vent), `ScorePlayer`, `ActorVoices` (engine and tyres of the five nearest cars, footsteps of the nearest walkers),
      `PlayerFoley`, `OneShotVoices` (24 placed and 16 flat voices, priority stealing, occlusion low-pass, Web Audio's rolloff as a curve), `AudioEnvironment`, `MasterFilter`, `MixerLevels`; `Cypher.mixer` (written as text by
      `AudioMixerWriter`: seven groups per bus by reverb-send tier, a reverb return with exposed settings); `UiSound` raised from every menu interaction. `--no-audio` (`-NoAudio`) switches it all off.
      **Checked against the prototype in a real Web Audio engine, not just against ourselves:** `tools/audio-ref/` runs the prototype's own `Bank.js`, `Ambience.js`, `Music.js` and `Dsp.js` in headless Chromium
      (Playwright, from the prototype's node_modules, read-only) and writes the fixtures: the noises match sample for sample, the synthesis engine matches Chromium's oscillators, filters, delays and compressor, every
      recipe matches its Chromium render, the bed matches at several weather settings and the score plays the same notes at the same times. Chromium details the port had to reproduce are listed in the memory file.
      **Found along the way:** Unity's mixer compressor is a hard limiter, nothing like the prototype's 3:1 glue stage, so the master dynamics are code on the listener; a mixer group has one send level and the prototype gives every
      sound its own, hence seven groups per bus; a mixer that may suspend idle groups swallows the first 20 ms of a click, so suspend is off; the asset keeps whatever the last test set through it (tests clear it first); and
      `float * int` is evaluated wider by Unity's runtime than by .NET, which moved a 480-frame delay by a hair (now pinned with a cast).
      **Decided without asking:** the prototype's convolution reverb has no equal in Unity, so the five spaces are five settings of Unity's SFX Reverb, blended over 1.3 s; 24 + 16 voices where the prototype had 80;
      the prototype's random car passes and voices remain only as the fallback when there is no traffic or crowd.
      Verified 2026-10-23 on the final code: 1,475 in the fast loop (about 190 new), 1,616 EditMode, 326 PlayMode (about 40 new, which wait out real time, so the test step now allows 30 minutes). Cost, details in
      `performance_results.md`: **the audio thread takes about 27% of one core (5.7 ms of every 21.3 ms block), the main thread 0.17 ms a frame, 27 MB of clips, up to half a second more to load; the frame rate is 1 to 10% lower** with sound
      (the tour at Medium: 100 FPS against 110). Tools: `Cypher > Export Audio Samples` writes every sound, three beds and two scores as WAV files at the level the game mixes them, with a `levels.md` (the quietest menu sound
      is 6 dB over the rain bed ducked for the pause menu, 10 dB under it at full level); `Cypher > Profile Audio` times each kind of node; `unity-check.ps1 -Step Player -Release` builds without the Development flag.
      Not done: **nobody has listened** (I cannot), so the levels, the reverb's size and the balance of the score are as the prototype's numbers give them and untuned by ear; IL2CPP; crackle detection (nothing counts a missed
      audio block); `tools/player-keys.ps1` (needs the PC free).
- [x] 13. Integration and **Gate B** (2026-10-23; **closed on your word that the measurement is acceptable for now, with Medium and Ultra measured once each**). Plan: `implementation_plan_step13.md` (approved: default tier =
      highest with 20% headroom, no IL2CPP, ask before trimming).
      **Built.** (A) `StreetRoutes` (the plaza ring, 690 m over eight crosswalks; the junction loop, 250 m) made from `CityNav`; `--benchmark-route street|junction`; a pilot that notices it is held up, steps aside
      (alternating sides, a jump every other time) and counts `stalled_s` and `detours`, and a run held up for over 15% is invalid. A free walker takes 158 s a lap, so the street recording is 160 s. (B) The summary has
      allocation a frame, frames with a collection, the people and vehicles drawn and the sound's cost (`IBenchmarkCounters`, offered by the crowd, the traffic and the sound), plus `hitches.csv` and `warmup_hitches.csv`;
      `tools/gate-b.ps1` waits for a quiet machine, refuses a stale player, alternates the tiers and prints the table and the highest tier with 20% headroom (`-ReportFrom` prints one for runs already made);
      `unity-check.ps1 -Step Player -Release` builds without the Development flag. (C) `IntegrationPlayTests` (5): three games in a row leave an identical census of objects and a flat heap; a tier sweep through the
      quality director resizes the people, vehicles, aircraft and lights; a slow machine makes the governor step the tier down and it holds there; pause and resume freeze the world and duck the sound; and 90 s of the
      street route at Ultra with everything on runs clean. **They found one leak, mended:** every game left nine silent audio clips behind (the clips that keep each synthesised source pulling).
      **Measured** (details and the table in `performance_results.md`; raw data in `benchmarks/`): Street route, Development player, everything on. **Medium (the default): 88.9 FPS, p99 15.6 ms, worst 23 ms, no frame over
      33.3 ms: Gate B passes with 53% headroom. Ultra (400 people, 96 vehicles, 36 aircraft): 73.3 FPS, p99 17.9 ms**, one 296 ms frame mid-run that was outside the game's own work. Launch to the first playing frame 8.1 s.
      **Not done:** Performance, Low and High were not measured, nothing was repeated (so no median of three), and the junction stress route was not run; **the default tier stays Medium**, although by the rule you chose
      Ultra would win on these numbers once three runs of each tier are in. `tools/player-keys.ps1` (needs the PC free) was not re-run. IL2CPP was skipped on your answer.
      **Known and left:** about half a second of stutter after New Game (a 225 ms first frame that is there with everything off, the actors starting in the same frame, and a 280 to 310 ms stall when the crowd and the
      traffic first draw: probably shader compilation in the driver); ways to remove it are in `performance_results.md` and the choice is yours. Garbage: about 4 KB a frame on the main thread, a collection every 18 to 20 s,
      none coinciding with a hitch. The sound's levels are as the prototype's numbers give them, untuned by ear.
      **Milestone 3 as a whole:** people (110 to 400) and traffic (52 to 96 vehicles, 20 to 36 aircraft) that really cross roads and stop for each other, the whole city's sound, and the gate passed with all of it on. Verified
      2026-10-23 on the final code: 1,511 in the fast loop, `unity-check.ps1 -Step All` green (1,652 EditMode, 333 PlayMode; about 23 minutes with the fast loop).
---

# Milestone 1 — task checklist (complete)

- [x] 1. git init, Unity .gitignore, create URP project (opens and compiles headless on 6000.6.6f1, 2026-10-20)
- [x] 2. Assembly definitions, folder skeleton, Input System + Test Framework wired (54/54 EditMode tests pass in real Unity)
- [x] 3. CityLayout + Rng + tests (verified outside Unity against prototype golden fixtures)
- [x] 4. MeshBuilder + BuildingMesher + CityPlan + tests (same)
- [x] 5. CityBuilder (runtime from seed, 3x3 window of 62 m cell chunks, roadbed, sidewalks, markings, box colliders,
      procedural window atlas, `Cypher/CityLit` vertex-colour shader). Rendered on the Vega 8 and inspected.
- [x] 6. FirstPersonController (new Input System, first-person only). `PlayerMotor` + `LookState` in Core (prototype
      constants), `FirstPersonController` (CharacterController, autostep 0.42) and `InputSystemCommandSource` in
      `Cypher.Player`, player placed in `Slice.unity`. Kerb climb, wall blocking, jump arc, crouch/headroom and
      key/mouse/gamepad bindings are tested in real Unity. Feel with a real mouse and display has NOT been checked.
- [x] 7. URP low-res (renderScale + point upscale), bloom, tone map, fog, night lighting. `LowResController` (runtime
      clone of the URP asset, 540p default, project asset untouched: verified by hash), `NightLighting` + Core
      `NightRig` (prototype's moon/hemisphere/fog/fills ported, tested against Node goldens), shader-side fills in
      `CityLit`, Volume with Bloom + Neutral tonemap + Vignette, gradient sky, prototype pane proportions.
- [x] 8. Neon, wet streets, rain (2026-10-20). 281 EditMode + 55 PlayMode tests pass; slice is 13,498 triangles in 43
      renderers, city build 745 ms in the Editor (textures, materials, meshes). Pieces:
      - Wet streets: Core `Noise` (prototype Perlin, goldens match to 1e-12) and `SurfaceTextures` (asphalt, paving,
        concrete and roughness fields; roughness goldens byte-exact); `SurfaceMaterials` makes textured instances;
        `CityLit` gained a smoothness map and planar world UVs (so concrete texture works on atlas-UV walls).
      - Reflections: `StreetReflectionProbe`, one box-projected realtime probe rendered once after the city is built.
      - Neon: Core `NeonDistricts`, `NeonWalls`, `NeonPlanner`, `NeonMesher` + `Cypher/NeonAdd` shader (additive
        tubes, lettered panels, bulbs, glow pools; the prototype's flicker and chase) and dark metal housings.
      - Rain: Core `RainModel` + `RainController` (one camera-following stretched-billboard particle system,
        3,000-particle cap at full intensity, 0.45 by default) + `Cypher/Rain`.
- [x] 9. Perf overlay and `--benchmark` (2026-10-20). Core: `PerfStats` (rolling window, nearest-rank percentiles),
      `PerfOverlayText`, `BenchmarkOptions`, `BenchmarkPilot` (scripted walk of a 372 m loop), `BenchmarkRecorder` (CSV,
      summary, p99 gate). Unity (`Cypher.Perf`): `PerfOverlay` (UI Toolkit, native resolution, on by default, F3 toggles),
      `RenderCounters` (draw calls, triangles), `BenchmarkRunner`, `PilotCommandSource`. 321 EditMode + 71 PlayMode tests
      pass; 254 in the fast loop.
- [x] 10. Development player built and measured on the Vega 8 (2026-10-20): **passed**, p99 10.2 to 11.3 ms (gate 33.3),
      about 130 FPS average at 1080p window / 540p internal. Details and caveats in `performance_results.md`.

Also done ahead of schedule: `ResolutionScaler` (step 7 maths).

## Verifying

Editor: `D:\source\Unity\6000.6.6f1\Editor\Unity.exe` (reinstalled with Windows standalone support; the project
moved from 6000.6.5f1 to 6000.6.6f1 on first open). Close any open Editor on the project first.

One command for everything (logs in `%TEMP%\cypher-unity`, a hung Unity is killed after `-TimeoutSeconds`, default 420):

    powershell -File tools/unity-check.ps1 -Step All                    # Build scene, EditMode, PlayMode
    powershell -File tools/unity-check.ps1 -Step Build|Edit|Play
    powershell -File tools/unity-check.ps1 -Step Capture -CaptureDir D:\shots -ScreenHeight 1080
    powershell -File tools/unity-check.ps1 -Step Player       # Development player into Builds\Dev (about 7 min the first time)
    powershell -File tools/unity-check.ps1 -Step Benchmark    # runs it with --benchmark at 1920x1080, prints the summary
    powershell -File tools/unity-check.ps1 -Step Perf         # Player, then Benchmark

PlayMode and Capture run with a graphics device (no `-nographics`): the reflection-probe tests skip without a GPU.
The PlayMode tests need the scene built first (`-Step Build`, or `All`).

Fast loop without Unity: `powershell -File tools/core-tests/run.ps1 [fixture-filter]` builds `Core` and
`Assets/Tests/EditMode` (not the `UnityLayer` folder) with the SDK bundled in the Unity install. About 5 s. It runs
[Test], [TestCase], [SetUp], [TearDown] and refuses (fails) any other test attribute rather than skipping it. It does
not prove asmdefs or Unity-side code compile; only the editor can.

Golden fixtures come from the prototype: `node tools/gen-golden.mjs D:/source/cyberpunkish [seed]` (RNG, city,
Perlin/fbm noise, roughness fields).

## Capturing (visual check without playing)

`SliceCapture` enters Play mode and `SliceCaptureDriver` (a runtime assembly that only compiles in the Editor)
renders each pose in `CapturePoses` to a PNG through the real pipeline, so images include low-res scaling, the
reflection probe, lighting and rain. `-ScreenHeight 1080` emulates a 1080p window (540p internal, point-upscaled);
without it the output is the 960x540 internal image. Scene and materials come from
`Cypher/Build Slice Scene` (or `-Step Build`).

## Project settings changed

- `ProjectSettings/ProjectSettings.asset`: `enableFrameTimingStats` on (set by `PlayerBuilder` so the benchmark can read
  CPU and GPU frame times).
- `Assets/Settings/PC_RPAsset.asset`: **a player build rewrites its shader-stripping `m_Prefilter*` fields** (URP
  derives them from what the project uses). The settings that matter (probe blending, box projection, Forward+) are
  unchanged. Expect that diff after every player build.
- `ProjectSettings/QualitySettings.asset`: **Realtime Reflection Probes turned on** for both quality levels (the URP
  template ships it off, which silently stops realtime probes rendering). `StreetReflectionProbe.Capture()` throws if
  it is off. The URP asset already had probe blending, box projection and the probe atlas on, in Forward+; the game
  and the capture tool never modify it (the runtime clone in `LowResController` is a copy).

## Menus

`Cypher.UI` (UI Toolkit, no EventSystem; input arrives through Unity's built-in `InputForUI` provider). Theme and styles are
`Assets/_Cypher/UI/Resources/CypherUI/CypherTheme.tss` and `Cypher.uss`; fonts are the OS's Bahnschrift and Consolas, falling back
(logged) to Unity's default. Everything is reachable by keyboard (arrows, Enter, Esc, PageUp/PageDown for tabs), mouse and gamepad
(d-pad or stick, A, B or Start to go back, LB/RB for tabs). Options apply as they change and save 600 ms after the last change.
Screenshots of every menu: `tools/unity-check.ps1 -Step Shots` (player, 1920x1080, into `%TEMP%\cypher-unity\shots`). Real key
presses against a built player: `tools/player-keys.ps1` (foreground window only; it sends nothing if it cannot get it).

## Perf overlay and benchmark

In the player (and the Editor) the overlay shows FPS with an OK / BELOW 30 verdict, frame time (average, worst, p99 of
the last 5 s), draw calls and triangles; F3 hides it. Draw calls and triangles read 0 in the Editor and are real in a
Development player; a release player reports n/a.

`Cypher.exe --benchmark [--benchmark-out DIR] [--benchmark-seconds N] [--benchmark-warmup N] [--benchmark-quality PRESET]` (defaults 60 s after a
10 s warm-up, results beside the exe in `benchmark\`) writes `benchmark.csv`, `benchmark_summary.txt`, `first_frame.png`
and `last_frame.png`, and exits with 0 pass / 1 gate failed / 2 exception / 3 invalid run. Add Unity's own
`-screen-width 1920 -screen-height 1080 -popupwindow` for a fixed window. The benchmark turns vsync off.

## Flow and settings

Launching the player: Boot, Loading (city, reflection probe, a few settle frames), then the Title over the live city (camera
gliding down the avenues). The title menu is New Game, Continue (greyed until there are saves), Options and Quit; **Esc**
(or the gamepad's Start) pauses the game and, in a menu, goes back; the pause menu is Resume, Options, Return to Title, Quit.
Quit and Return to Title ask first. Losing window focus while playing pauses. Settings live in `%USERPROFILE%\AppData\LocalLow\Cypher\CYPHER\settings.json` (the Editor uses
`settings.editor.json`); the file appears only when something is saved, and an unusable one is set aside as `.bad`.
Launch switches: `--skip-title`, `--default-settings` (never reads or writes the settings file), `--no-auto-pause`.

Controls: Move forward/back/left/right (W/S/A/D and the arrow keys), Jump (Space, gamepad A), Sprint (Left/Right Shift, gamepad L3) and
Crouch (Left Ctrl or C, gamepad B) can be rebound; mouse look, the sticks, Esc (pause), Enter, F3 (overlay) and the gamepad's Start/Select
cannot. Rebinding goes through `SettingsService.Bindings` (`Assign`, then `SwapWith` if it reports a Conflict, `Unbind`, `Reset`,
`ResetAll`) and `BindingCapture`; the settings file stores only the differences, as `"jump:0": "<Keyboard>/j"` (slot 0 and 1 are keyboard/mouse,
2 is the gamepad, `"none"` means empty).

## Known limitations

Rendering and look:
- The first frame after `CityBuilder.Build()` rendered every wall as plain dark glass in the old edit-mode capture.
  Checked in the Development player: the first frame (`first_frame.png`) is already correct, so this does not occur
  there.
- Neon lettering is the stroke font (no OS font), a limited vocabulary: 24 shop words, kana and kanji-like blocks, 8 icons; every sign says one of those. The
  hoardings and holograms play four ad panels (the ticker text is fitted to the panel width, so it is small); heavy lettering seen at a glancing angle merges into its
  glow. Not ported: the pooled real point lights (light spill is additive glow pools plus bloom) and the camera-facing glow cards on small signs.
- Reflections come from one probe captured once from a fixed point: blurry, static, they do not flicker with the signs,
  and they never include the rain. Road roughness is scaled to 0.5 of the prototype's map so the probe's blur still
  shows neon (the prototype's screen-space reflections stay sharp at 1.0).
- Rain has no splashes, mist, haze or lens droplets. The roadbed is a 1 km square and its far edge is visible from
  a high overview pose.
- Control names assume a US keyboard: `<Keyboard>/w` is the key where W is on a US layout (so WASD works on any layout) but is shown as
  "W". The Controls screen should prefer the connected keyboard's own labels. The menus and key capture have been driven by real OS key
  presses (`tools/player-keys.ps1`) but never by a real gamepad or mouse.
- The Audio tab says there is no sound yet: the levels are saved and will apply when audio arrives (step 12).
- The Editor tests send UI Toolkit navigation events straight to elements, because UI Toolkit ignores input while the Editor is
  unfocused; the real input path is covered only by `tools/player-keys.ps1`, which needs an interactive desktop and a built player.
- Walls: no recessed bays, piers or balconies; the plaza block (3,3) is empty so it has no signage.
- Props: they are hidden by block, not by prop, so a block's streetlights vanish with its bins when the block passes the draw distance (the fog
  hides most of it); the steam is faint by design (a puff peaks at 16% opacity); oil slicks can read as dark holes seen at a low angle; the
  prototype's decals were double-sided, ours face outward only.
- Sky is a plain gradient: no horizon glow bands, stars, moon or clouds. Static night only.
- Bloom (threshold 0.8, intensity 0.9, scatter 0.65) and vignette are tuned by eye, not matched.
- Shadows are off; the shader's shadow and depth-normals passes are untested in a render.

Not ported from the web prototype: spires/masts, viaduct, tram rails, manholes, balconies, pooled prop lights, kickable junk, facade anchors.

Startup: textures and meshes are generated on the CPU at start. In the player the city build is 0.76 to 0.83 s and the
first frame appears about 4.4 s after the process starts.

Not verified: how mouse-look feels with a real mouse and display (the benchmark supplies look input directly), a
release player, IL2CPP, other hardware, sessions beyond 160 s. See `performance_results.md`.

## Resolved blocker (2026-10-19 → 2026-10-20)

6000.6.5f1 could not compile scripts (missing reference assemblies, analyzers, Windows standalone module).
Fixed by the user reinstalling as 6000.6.6f1 under `D:\source\Unity`.
