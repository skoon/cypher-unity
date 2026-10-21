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
- [~] 9. PerfStats + ResolutionScaler done and tested; PerfOverlay + --benchmark path not started
- [ ] 10. Build player, measure, record in performance_results.md

Also done ahead of schedule: `ResolutionScaler` (step 7 maths).

## Verifying

Editor: `D:\source\Unity\6000.6.6f1\Editor\Unity.exe` (reinstalled with Windows standalone support; the project
moved from 6000.6.5f1 to 6000.6.6f1 on first open). Close any open Editor on the project first.

One command for everything (logs in `%TEMP%\cypher-unity`, a hung Unity is killed after `-TimeoutSeconds`, default 420):

    powershell -File tools/unity-check.ps1 -Step All                    # Build scene, EditMode, PlayMode
    powershell -File tools/unity-check.ps1 -Step Build|Edit|Play
    powershell -File tools/unity-check.ps1 -Step Capture -CaptureDir D:\shots -ScreenHeight 1080

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

- `ProjectSettings/QualitySettings.asset`: **Realtime Reflection Probes turned on** for both quality levels (the URP
  template ships it off, which silently stops realtime probes rendering). `StreetReflectionProbe.Capture()` throws if
  it is off. The URP asset itself is untouched (checked by hash): it already had probe blending, box projection and
  the probe atlas on, in Forward+.

## Known limitations

Rendering and look:
- The first frame drawn right after `CityBuilder.Build()` rendered every wall as plain dark glass in the old
  edit-mode capture until a second frame was drawn (atlas not uploaded yet, as far as I could tell). The new
  Play-mode capture waits several frames so it cannot show this. Not yet checked in a player.
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

Startup: textures are generated on the CPU at start (about 0.75 s in the Editor for everything). Step 10 should
measure it in a player.

## Resolved blocker (2026-10-19 → 2026-10-20)

6000.6.5f1 could not compile scripts (missing reference assemblies, analyzers, Windows standalone module).
Fixed by the user reinstalling as 6000.6.6f1 under `D:\source\Unity`.
