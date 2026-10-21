# Milestone 2: front end, whole city, living streets

Status: **draft for review, nothing started.** Decisions already made by you: whole 7x7 city built at load; population =
pedestrians, traffic and aircraft, street props and signage, ambient audio; title is CYPHER with NIGHT SECTOR as the
district; Options go all the way (graphics, audio, gameplay, controls with rebinding for keyboard/mouse and gamepad).

## Goal and exit criteria

1. Launching the player shows a loading bar, then a title screen over the live night city (New Game, Continue, Options,
   Quit). New Game drops you on the plaza. Esc pauses (Resume, Options, Return to Title, Quit), which also fixes the
   "no way to quit" gap.
2. Options persist between runs and take effect immediately: quality preset + adaptive quality, internal height, window
   mode, vsync, FOV, per-bus volume, mouse sensitivity and invert-Y, full rebinding with conflict handling, reset to
   defaults. Everything works with mouse, keyboard and gamepad.
3. The whole 7x7 city is walkable, dressed with props and real signage, populated by pedestrians, ground traffic and
   aircraft, with a rain-and-city soundscape.
4. **Gates, measured in a Development player on this Vega 8 at 1080p window / 540p internal with the benchmark route:**
   - **Gate A (after step 8):** whole city with props and signage, no population: p99 <= 33.3 ms.
   - **Gate B (end):** the default tier, fully populated: p99 <= 33.3 ms. Every tier's p99 is recorded in
     `performance_results.md`; tiers above the default may exceed the gate on this machine (the prototype said the same).
5. Unit tests for all new logic; `unity-check.ps1 -Step All` green; results recorded.

## What carries over from the prototype, and what is new

Ported (logic is pure and tested in JS, so goldens/ports are mechanical): quality budgets
([GraphicsBudget.js](file:///D:/source/cyberpunkish/src/engine/GraphicsBudget.js)), adaptive governor
(`QualityGovernor.js`), save/settings versioning with migrations ([Save.js](file:///D:/source/cyberpunkish/src/rpg/Save.js)),
sidewalk/road navigation ([CityNav.js](file:///D:/source/cyberpunkish/src/ai/CityNav.js)), pedestrians
([Crowd.js](file:///D:/source/cyberpunkish/src/ai/Crowd.js)), traffic and aircraft
([Traffic.js](file:///D:/source/cyberpunkish/src/ai/Traffic.js)), the prop kit ([Props.js](file:///D:/source/cyberpunkish/src/world/Props.js),
about 35 kits), signage glyphs, LED billboards and holograms ([Neon.js](file:///D:/source/cyberpunkish/src/world/Neon.js)),
the audio synthesis ([audio/](file:///D:/source/cyberpunkish/src/audio)).

New design (the prototype has none of it): start screen, loading flow, options beyond graphics, key rebinding, gamepad
menus, audio mixer and sliders. The prototype's keys are hard-coded and sensitivity/invert-Y have no UI.

Deferred (not in M2): RPG layer (stats, perks, cyberware, inventory, quests, dialogue, vendors, hacking), weapons and
impacts, adaptive music, HUD panels beyond the perf overlay. **Continue** is a stub (disabled until saves exist); the
settings store is built so a save slot can reuse its versioning later.

## Architecture additions

Pure logic in `Cypher.Core` (no UnityEngine, doubles, tested in the 4 s fast loop): `Settings/` (model, ranges, store,
migrations), `Quality/` (tier table, governor), `Flow/` (game state machine), `Input/` (action map, binding conflicts),
`Nav/` (pedestrian graph, road lattice), `Crowd/`, `Traffic/`, `Audio/` (DSP and layer scheduling), `Signage/`
(stroke glyph rasteriser).

New Unity assemblies: `Cypher.UI` (UI Toolkit front end), `Cypher.Actors` (instanced pedestrians, vehicles, aircraft),
`Cypher.Props`, `Cypher.Audio` (clip generation, mixer, emitters). `Cypher.Perf` and the rest stay.

Conventions kept: zero binary art/audio assets (everything generated at start, seeded), prototype coordinates unchanged,
fail loudly, no commits from me.

## Steps

Sizes are relative (S/M/L). Each step ends with unit tests green and `-Step All` green; gates need a player build.

**Foundations**
1. **Settings core (S).** `GameSettings` (graphics, audio, gameplay, controls) with validated ranges, `SettingsStore`
   (versioned JSON in `Application.persistentDataPath`, atomic write, migrations, refuses a newer version, a corrupt file
   is renamed `.bad` and surfaced to the user, never silently reset). Storage is injected so tests use memory.
2. **Quality tiers and governor (M).** Tier table calibrated by measurement (internal height, rain cap, neon cap, crowd,
   vehicle and aircraft budgets, cull distances, post options) plus the downgrade-only adaptive governor fed by `PerfStats`
   p99. `QualityApplier` pushes a tier into `LowResController`, rain, neon, actors. Ports the prototype's governor tests.
3. **Game flow (S).** Pure state machine Boot -> Title -> Loading -> Playing <-> Paused, with cursor lock, time scale and
   Esc rules in one place; `GameFlow` MonoBehaviour. Single scene: the city builds once at launch behind the loading
   bar, the title runs over a slow camera flyover, New Game spawns the player, Return to Title does not reload.
4. **Input and rebinding (M).** Named actions (move, look, jump, sprint, crouch, interact, pause, overlay toggle) replace
   the hard-coded bindings in `InputSystemCommandSource`; interactive rebind for keyboard/mouse and gamepad, conflict
   detection and swap/cancel, overrides persisted through the settings store, reset per action and all.
5. **Front end in UI Toolkit (L).** Theme in USS lifted from the prototype's look (neon cyan/magenta on dark, monospace
   labels). Views: Loading, Title, Pause, Options (Graphics, Audio, Gameplay, Controls tabs), rebind dialog, notices.
   Focus navigation by mouse, keyboard and gamepad. Audio tab binds to the mixer in step 12. UI screenshots come from
   the player (UI Toolkit is not in the render-request capture), so the player gets a `--shots` mode.

**World**
6. **Whole 7x7 (M).** `CityWindow` over the full grid, built incrementally across frames with progress for the loading
   bar; reflection probe coverage for the larger area; world-edge treatment (the 1 km roadbed edge is visible today).
   Measure geometry, draw calls and load time first; add streaming only if the numbers force it.
7. **Props (M).** Port the kit (bollards, barriers, streetlights, signals, shelters, benches, kiosks, bins, hydrants,
   dumpsters, crates, vending machines, stalls, ATMs, cameras, rooftop units, decals), seeded placement from block data,
   one instanced draw per kit, colliders where the prototype has them.
8. **Signage upgrade (M).** Stroke-based glyph rasteriser (katakana, kanji and Latin letters as vector strokes, so no OS
   font is needed) replaces the placeholder lettering bars; LED billboards and holograms in the neon shader. **Gate A.**

**Life**
9. **Navigation (M).** Port `CityNav`: pedestrian ring graph with kerb crossings, road lattice and intersection grid.
10. **Pedestrians (L).** Seeded simulation with local avoidance on a uniform grid, yielding to traffic, a few kinematic
    capsules leased to whoever is nearest the player; instanced rendering with limb animation in the vertex shader;
    pooled to the tier budget and re-poolable when the tier changes.
11. **Traffic and aircraft (L).** Lane-following with car-following braking, signalised junctions with queues, turn
    selection; aircraft in altitude bands; headlights and rigs as emissive/additive geometry; `roadBusy` query for
    pedestrians; vehicle near the player leases a collider.
12. **Ambient audio (L).** Port the synthesis: the city bed (rain, traffic wash, hum, with the irrational-LFO trick),
    scheduled one-shots (sirens, drips, distant impacts, failing neon), UI sounds. Rendered to `AudioClip`s on worker
    threads at load, played through an `AudioMixer` with exposed bus volumes (master, music, sfx, ambience, ui, voice) that
    the Options sliders drive. Spatial emitters tied to world features.
13. **Integration and gate B (M).** Extend the benchmark pilot through the populated city, per-tier runs, results in
    `performance_results.md`, update `task.md`.

## Risks

- **CPU on the Vega 8 machine (Ryzen 5 3550H, Mono Development build).** M1 left 4x headroom but it was GPU-bound with no
  simulation; the prototype's crowd alone cost 7 ms of CPU. Mitigation: grid-based avoidance, budgets by tier, sim at a
  reduced rate for distant actors, measure after every step; IL2CPP/Burst only if numbers demand it.
- **Load time.** M1 builds 9 cells in 0.8 s plus about 3.5 s of engine start. 49 cells plus audio synthesis could reach
  10+ s; incremental build and worker-thread synthesis are in the plan, with a target of at most 10 s to the title screen.
- **UI Toolkit focus navigation with gamepad alongside Input System rebinding** is the least familiar combination; step 5
  starts with a thin vertical slice (title + one option) to prove it.
- **Procedural audio memory.** Long bed loops at 44.1 kHz stereo are about 5 MB per minute per layer; layer count and loop
  length are budgeted.
- **Reflection probe over 434 m.** One box-projected probe may be too coarse; may need one per 3x3 region.

## Open decisions (defaults are my recommendation; answer any you disagree with)

1. **One milestone with two gates (default) or split** into M2 = steps 1-8 and M3 = steps 9-13.
2. **Title backdrop:** single scene with the live city behind the menu (default) vs a separate light Title scene.
3. **Corrupt settings file:** rename to `.bad`, tell the user, continue with defaults (default) vs refuse to start.
4. **Pedestrian rendering:** limb animation in the vertex shader, 1-2 draws (default) vs one instanced mesh per body part
   like the prototype (14 draws, more CPU).
5. **Lettering:** own stroke font so no OS font is needed (default) vs OS fonts for Latin.
6. **UI typeface:** OS Consolas with logged fallback to the built-in font (default) vs the built-in font only.
7. **Music:** out of M2, bus exists (default) vs include the adaptive score.
8. **Player settings:** set product name to CYPHER (changes the window title and the settings folder from
   `DefaultCompany/cypher-unity`); company name needed from you if you want something other than DefaultCompany.
9. **Default tier on a fresh install:** decided by the Gate B measurements on this machine (default) vs the prototype's
   fixed "Performance".
