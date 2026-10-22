# Milestone 2 — task checklist

Plan: `implementation_plan_m2.md` (approved 2026-10-20). Milestone 2 is complete: Gate A passed 2026-10-22. Milestone 3 (steps 9-13) is next.

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
