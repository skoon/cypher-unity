# Milestone 1 — task checklist

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
`Assets/Tests/EditMode` (not the `UnityLayer` folder) with the SDK bundled in the Unity install. About 3 s. It does
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

## Perf overlay and benchmark

In the player (and the Editor) the overlay shows FPS with an OK / BELOW 30 verdict, frame time (average, worst, p99 of
the last 5 s), draw calls and triangles; F3 hides it. Draw calls and triangles read 0 in the Editor and are real in a
Development player; a release player reports n/a.

`Cypher.exe --benchmark [--benchmark-out DIR] [--benchmark-seconds N] [--benchmark-warmup N]` (defaults 60 s after a
10 s warm-up, results beside the exe in `benchmark\`) writes `benchmark.csv`, `benchmark_summary.txt`, `first_frame.png`
and `last_frame.png`, and exits with 0 pass / 1 gate failed / 2 exception / 3 invalid run. Add Unity's own
`-screen-width 1920 -screen-height 1080 -popupwindow` for a fixed window. The benchmark turns vsync off.

## Known limitations

Rendering and look:
- The first frame after `CityBuilder.Build()` rendered every wall as plain dark glass in the old edit-mode capture.
  Checked in the Development player: the first frame (`first_frame.png`) is already correct, so this does not occur
  there.
- Neon "lettering" is procedural bars, not glyphs. Not ported: LED billboards, floating holograms, the pooled real
  point lights (light spill is additive glow pools plus bloom), glow cards.
- Reflections come from one probe captured once from a fixed point: blurry, static, they do not flicker with the signs,
  and they never include the rain. Road roughness is scaled to 0.5 of the prototype's map so the probe's blur still
  shows neon (the prototype's screen-space reflections stay sharp at 1.0).
- Rain has no splashes, mist, haze or lens droplets. The roadbed is a 1 km square and its far edge is visible from
  a high overview pose.
- Walls: no recessed bays, piers or balconies; the plaza block (3,3) is empty so it has no signage.
- Sky is a plain gradient: no horizon glow bands, stars, moon or clouds. Static night only.
- Bloom (threshold 0.8, intensity 0.9, scatter 0.65) and vignette are tuned by eye, not matched.
- Shadows are off; the shader's shadow and depth-normals passes are untested in a render.

Not ported from the web prototype: spires/masts, viaduct, tram rails, manholes, plaza props, balconies.

Startup: textures and meshes are generated on the CPU at start. In the player the city build is 0.76 to 0.83 s and the
first frame appears about 4.4 s after the process starts.

Not verified: how mouse-look feels with a real mouse and display (the benchmark supplies look input directly), a
release player, IL2CPP, other hardware, sessions beyond 160 s. See `performance_results.md`.

## Resolved blocker (2026-10-19 → 2026-10-20)

6000.6.5f1 could not compile scripts (missing reference assemblies, analyzers, Windows standalone module).
Fixed by the user reinstalling as 6000.6.6f1 under `D:\source\Unity`.
