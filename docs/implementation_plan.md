# CYPHER — Unity implementation plan, Milestone 1 (walkable slice)

**Status:** awaiting review. No Unity project files exist yet; only this document.

## Decisions already made

| Topic | Choice |
|---|---|
| Location | `D:\source\cypher-unity`, its own git repo. Web prototype (`D:\source\cyberpunkish`) is read-only reference. |
| Editor | Unity `6000.6.5f1` (installed) |
| Pipeline | URP |
| Lowres | Low-res render target: fixed vertical resolution (default 540p, tunable), point-filtered upscale |
| Milestone 1 | Walkable slice: player + one rainy neon street block + low-res URP + FPS counter |

Note: the prototype is plain JavaScript (three.js), not TypeScript. It is the design reference either way.

## Goal and exit criteria

Prove the look and the 30 FPS floor before building anything else.

1. Walk a first-person character around one block of the city at night, in rain-slick streets with neon.
2. Scene renders at a fixed internal height (default 540p) with point-filtered upscale; HUD is native resolution.
3. On-screen overlay: FPS, frame ms (avg + worst of last 5 s), draw calls, triangles.
4. **Gate:** ≥ 30 FPS sustained (p99 frame ≤ 33.3 ms) in a Development build on the user's actual machine. If it fails, we fix it before Milestone 2.
5. EditMode unit tests green for all pure logic (city layout math, perf stats, resolution scaling).

## What gets ported from the web version

Layout contract from `src/world/City.js`, kept numerically identical so later systems line up with the prototype:

- `BLOCK = 46`, `ROAD = 16`, `CELL = 62`, `GRID = 7`, `FLOOR_H = 3.55`, `SIDEWALK_H = 0.185`, `SIDEWALK_PAD = 2.5`.
- Plaza block at grid (3,3) centred on origin; player spawns there.
- Slice builds the plaza block plus its 8 neighbours' near faces (a 3×3 window of the 7×7 grid) so there is a street to look down and a skyline to read.

Not ported in M1: crowd, traffic, aircraft, weapons, RPG systems, audio, full weather, hacking UI.

## Architecture

Pure C# logic is kept out of `MonoBehaviour`s so it can be unit-tested in EditMode (your rule: tests for all new business logic).

```
Assets/_Cypher/
  Scripts/
    Core/        Cypher.Core.asmdef      CityLayout (grid math), Rng (seeded), PerfStats (rolling window)
    City/        Cypher.City.asmdef      MeshBuilder (port of the JS one), BuildingGenerator, CityBuilder (MonoBehaviour)
    Player/      Cypher.Player.asmdef    FirstPersonController (CharacterController + Input System)
    Rendering/   Cypher.Rendering.asmdef LowResController (sets URP renderScale from target height), RainController
    Perf/        Cypher.Perf.asmdef      PerfOverlay (UI Toolkit)
  Settings/      URP asset, renderer data, volume profile
  Materials/     shared materials, SRP-Batcher-friendly
  Scenes/        Slice.unity
Assets/Tests/EditMode/  Cypher.Tests.EditMode.asmdef
```

## Approach, per concern

**City geometry.** A C# `MeshBuilder` ported from the JS one (quads, boxes, vertex colours), one mesh per 62 m cell chunk, marked static. Buildings use vertex colour plus one shared lit material and a window atlas generated once at startup (the prototype has zero binary art assets; I'd keep that for M1). Cell chunks give Unity frustum culling per cell, which is the lever the web version found most important (geometry-bound, not fill-bound).

**Low-res.** `LowResController` sets URP `renderScale = targetHeight / Screen.height` on screen change, and the URP asset uses the Point upscaling filter. Screen-space overlay UI renders after the upscale, so HUD stays sharp.

**Neon and wet look.** Emissive materials with HDR values + URP Bloom. Wet streets: high smoothness material plus a baked reflection probe. No screen-space reflections (URP has none built in); neon smear is faked by the probe and emissive floor decals. Few or no real-time point lights.

**Shadows.** Off for M1 (the web Performance tier also removes them and measured shadows at ~0 ms on a rainy night).

**Rain.** One GPU-cheap particle system following the camera, capped particle count.

**Player.** `CharacterController`, Unity Input System, first person, walk/sprint/jump/crouch, autostep 0.42 m to climb kerbs as in the prototype.

**Perf overlay.** `PerfStats` (pure, rolling 5 s window, p99) fed by the MonoBehaviour; draw calls/tris read from `UnityEngine.Rendering.FrameTimingManager` / profiler recorders in Development builds.

## Verification

- EditMode tests via `Unity.exe -batchmode -nographics -runTests -testPlatform EditMode` (I will confirm this runs headless on your machine early; if the Editor is already open on the project it will refuse, so close it first).
- Run all tests before declaring any step done.
- Frame-rate gate measured in a **built player**, not the Editor (Editor timings are not representative). I can build from CLI but cannot read the screen; you press play and read the overlay, or I add a `--benchmark` flag that walks a fixed path and writes a CSV I can read.

## Steps (will become `task.md` once approved)

1. `git init`, Unity `.gitignore`, create project with URP template (batchmode), verify it opens and compiles.
2. Assembly definitions, folder skeleton, Input System + Unity Test Framework wired; first passing test.
3. `CityLayout` + `Rng` + tests (port constants and cell/plaza math).
4. `MeshBuilder` + `BuildingGenerator` + tests (vertex/triangle counts, determinism by seed).
5. `CityBuilder`: ground, roads, sidewalks, 3×3 block window, chunked static meshes.
6. `FirstPersonController`.
7. URP setup: low-res scale + point upscale, bloom, tone mapping, fog, night lighting. `LowResController` + tests for the scale math.
8. Neon, wet streets, rain.
9. `PerfStats` + `PerfOverlay` + `--benchmark` path.
10. Build player, measure, record results in `performance_results.md`.

## Open decisions (I'll ask one at a time as each step needs it; defaults are my recommendation)

1. **Input:** new Input System package (default) vs legacy Input Manager.
2. **Internal resolution default:** 540p (default, matches web Performance tier) vs 360p (chunkier, cheaper) vs 720p.
3. **City generation timing:** at runtime from a seed (default, matches prototype, easy to tweak) vs baked in the Editor into scene assets (faster load, harder to iterate).
4. **Min-spec target for the 30 FPS gate:** Vega 8-class integrated GPU (default, matches what the prototype was measured on) vs a mid-range discrete GPU.
5. **Camera:** first-person only (default) vs first/third toggle like Skyrim.

## Risks

- Unity 6000.6 is a recent Unity 6 stream; some URP APIs differ from what I know best. I'll verify against the installed package source rather than assume.
- Headless Editor runs can't screenshot the scene, so visual correctness needs your eyes; I'll say so rather than claim it verified.
- The prototype's measured geometry cost (≈1M triangles worth aiming at, ~2.5M too slow on Vega 8) means the slice should budget triangles from day one.
