# Milestone 1 performance gate

**Gate:** at least 30 FPS sustained in a Development build on the target machine, meaning p99 frame time
at or under 33.3 ms. **Result: passed, with roughly 3x headroom on p99** (2026-10-20).

## Setup

| | |
|---|---|
| Machine | AMD Ryzen 5 3550H (4 cores), Radeon Vega 8 integrated graphics (driver 31.0.21923.11000), 30 GB RAM |
| Display / window | 1920x1080 at 59 Hz; borderless window 1920x1080 |
| Build | Windows Development player, Mono, Direct3D 11, Unity 6000.6.6f1, URP 17.6, quality level "PC" |
| Scene | the whole Slice: 3x3 city window, wet streets and reflection probe, neon, rain (3,000-streak cap at 0.45 intensity), bloom, tonemap, vignette |
| Rendering | 540p internal, point-upscaled to 1080p; shadows off |
| Frame cap | vsync off, no target frame rate, runs in background (so the number is what the machine can do, not the display's refresh) |
| Overlay | on (F3 toggles); UI Toolkit, native resolution |

## Method

`Cypher.exe --benchmark` hands the character to a scripted pilot that walks a fixed 372 m loop through the
streets of the slice (two long legs sprinting, three walking, view sweeping up and down), driving the real
`FirstPersonController`, so physics and kerbs are included. 10 s of warm-up are not recorded, then 60 s
(or the length given) of every frame is. p99 is nearest-rank over all recorded frames. The player writes
`benchmark.csv` (one row per frame), `benchmark_summary.txt`, and two screenshots, then exits with 0 for pass,
1 for a failed gate, 2 for an exception, 3 for an invalid run (the character did not walk).

Reproduce: `powershell -File tools/unity-check.ps1 -Step Perf` (about 8 minutes for a first build, then 80 s).
Close the Unity Editor first; the benchmark window takes over the screen while it runs.

## Results

| Run | Length | Avg FPS | Avg ms | p50 | p95 | **p99** | Worst | Frames over 33.3 ms | GPU avg / p99 | Draw calls avg / max | Triangles avg / max |
|---|---|---|---|---|---|---|---|---|---|---|---|
| A (long, 2 laps) | 160 s, 20,690 frames | 129.3 | 7.73 | 7.64 | 9.85 | **11.28** | 13.98 | 0 | 6.70 / 9.97 | 67 / 102 | 17,346 / 25,667 |
| B | 60 s, 8,059 frames | 134.3 | 7.45 | 7.34 | 9.26 | **10.17** | 11.73 | 0 | 6.45 / 8.97 | 62 / 79 | 15,831 / 20,741 |
| C | 60 s, 7,851 frames | 130.8 | 7.64 | 7.53 | 9.44 | **10.54** | 12.37 | 0 | 6.63 / 9.33 | 62 / 79 | 15,856 / 20,739 |

All times in ms. The second lap of run A is as smooth as the first (p99 11.6 vs 10.8 ms), so there are no
first-visit hitches from shader or texture warm-up once the 10 s warm-up has passed.

Startup, from the summaries: the first frame is drawn about 4.4 s after the process starts (engine start,
scene load, 0.76 to 0.83 s of that is the city build including all procedural textures).

## What this does and does not say

- The frame is GPU-bound: GPU time averages 6.5 ms of a 7.5 ms frame. At 540p internal the Vega 8 has about
  four times the headroom the gate asks for.
- **A first run did not look like this** and is not in the table: it came straight after the player was built and had
  10 frames of 34 to 72 ms (p99 18.2 ms, still a pass). In those frames CPU time was 7 to 27 ms and GPU time
  about 7 ms, so neither the CPU nor the GPU was busy; the cause is outside the game (my guess is first launch of a
  freshly written exe: antivirus and disk cache, but I did not confirm it). It did not recur in three further runs.
  One more frame of 169 ms in that run was the benchmark's own mid-run screenshot, which is why screenshots are
  now taken after recording stops.
- Vsync was off. With vsync on at this display's 59 Hz the game would be capped near 59 FPS.
- CPU frame time from the engine equals the frame time here (it includes waiting), so it says nothing about how
  much headroom the main thread has; only the GPU number is informative.
- Not measured: a release player, IL2CPP, other hardware, sessions longer than 160 s, memory use, real mouse
  input (the pilot supplies look input directly), other window sizes.
- Frame-timing values from the engine are sometimes garbage (one frame read 1.8e13 ms), so the recorder ignores
  CPU and GPU times above 1000 ms.

## Quality tiers (Milestone 2, step 2, 2026-10-20)

Each preset measured once with the same method as above, adaptive quality off: 1080p borderless window, vsync off,
8 s warm-up then 40 s recorded, Development player, the Slice scene (3x3 window, as in Milestone 1).

| Preset | Internal height | MSAA | Avg FPS | Avg ms | p99 ms | Worst ms | GPU avg ms | Draw calls | Triangles |
|---|---|---|---|---|---|---|---|---|---|
| Performance | 360 | off | 185.0 | 5.41 | 6.91 | 9.67 | 4.27 | 60 | 15,046 |
| Low | 450 | off | 167.1 | 5.98 | 7.99 | 9.08 | 5.05 | 66 | 15,942 |
| Medium | 540 | off | 143.8 | 6.95 | 8.69 | 10.03 | 6.07 | 66 | 16,811 |
| High | 540 | 2x | 125.7 | 7.96 | 10.33 | 13.28 | 7.09 | 66 | 16,820 |
| Ultra | 540 | 4x | 126.2 | 7.92 | 9.89 | 11.03 | 7.07 | 66 | 18,194 |

All five pass the gate by a wide margin. What this says:

- The whole range costs only 5.4 to 8 ms. At this scene size the presets barely matter for the gate; they will matter
  once the whole city and its people exist (the table is meant to be re-measured then, in Milestone 3).
- Single 40 s runs: Medium read 6.95 ms avg here against 7.45 to 7.73 ms in the earlier 60 s and 160 s runs, so differences
  under about 0.8 ms are noise. In particular High and Ultra are indistinguishable (4x MSAA costs no more than 2x here).
- The visible differences are as designed: Performance is chunkier with softer, wider bloom and thin rain; Ultra has
  smooth edges, crisp bloom and denser rain.

## The whole city (Milestone 2, step 6, 2026-10-21)

The 7x7 city (49 chunks) with its skyline ring, an invisible wall at the edge, and nine reflection probes, built over frames behind the
loading bar. Same machine, Development player, 1080p borderless, Medium, adaptive off, vsync off.

| What | Value |
|---|---|
| Geometry | 71,492 triangles in 49 chunks plus 1,350 in the skyline ring (the 3x3 slice was about 13,500); 143 k vertices; 454 colliders |
| City build | 1.0 to 1.2 s of CPU: textures 754 ms, signage 172, blocks 135, wall and ring 100, plan 27, roads 17 (a few frames each at an 8 ms budget) |
| Launch | engine up (Boot ends) at 3.8 to 4.0 s; loading (city, nine probe captures, settle frames) ends at 6.9 s with the title showing (7.2 s in the benchmark, which skips it). Target: at most 10 s to the title. With the machine busy the same load took 6.4 s at an 8 ms-a-frame build budget, which is why the budget is now 24 ms |
| Centre loop, 60 s (the loop every earlier row used) | **96.7 FPS, p99 14.1 ms**, worst 17.1 ms, 0 frames over 33.3 ms; GPU 9.3 ms avg / 12.8 p99; 244 draw calls avg (368 max), 58.8 k triangles avg (85 k max). M1 was 133 FPS, p99 11.6 ms, 63 draw calls |
| Centre loop again after the last edits (24 ms build budget, 520 m far plane), 60 s | **102.0 FPS, p99 16.9 ms**, worst 32.6 ms, 0 frames over; GPU 8.8 avg / 14.5 p99; 243 draw calls avg (368 max) |
| City tour, first 100 s (after the last edits) | **87.4 FPS, p99 24.5 ms**, worst 69 ms, 30 of 8,736 frames over 33.3 ms, scattered along the route (a 49 ms CPU hitch with no GPU cause; the "634 draw calls" max is two frames' counters added together); 177 draw calls avg, 44 k triangles avg; reaches the NE corner and the first stretch of the east edge |
| City tour, first 170 s (earlier run) | **93.8 FPS, p99 16.6 ms**, worst 22.8 ms, 0 frames over 33.3 ms; 176 draw calls avg. Covers the north edge, the east edge, the south edge as far as x = -157 |

So the whole city costs about 2.5 ms and 180 draw calls more than the slice, and the gate still has a factor of two on p99. **No streaming or
chunk distance culling is needed at this size.** The frame is no longer clearly GPU-bound (CPU and GPU both about 9 to 10 ms), so draw calls
(five renderers per chunk) are the first thing to cut if Milestone 3's actors need the room.

**What not to trust.** The last 65 s of the 240 s city tour read 21 ms average, p99 42 ms, with CPU time doubling while GPU time halved for similar
content. A second 240 s run of the old centre loop, started after the machine had rebooted and while other programs were starting up,
read 59.8 FPS with spikes to 250 ms and failed the gate. Both are consistent with the machine being throttled or disturbed, not with the game:
the 60 s runs and the first 170 s of the tour are steady. Windows logged two **unexpected resets** that day (event 41, both
while a long Unity job was running; no battery is reported and the clock showed 2.1 GHz at 100% load). Until that is understood, long benchmarks here are suspect: run
them on a quiet machine (check CPU is idle first), keep them to a minute or two, and repeat anything that looks bad.

## Street props (Milestone 2, step 7, 2026-10-21)

The whole city again, now dressed: 8,928 props (31 kits, 698 k triangles together with the cables), 8,641 wall and ground decals, 1,311 box colliders and
96 steam puffs, baked into one props mesh and one decals mesh per block and hidden beyond the quality tier's distance (Medium: 125 m). Same machine, Development
player, 1080p borderless, Medium, adaptive off, vsync off, the machine otherwise quiet (CPU 5 to 18% before each run).

| Run | Avg FPS | p99 ms | Worst ms | Frames over 33.3 ms | GPU avg / p99 ms | Draw calls avg / max | Triangles avg / max |
|---|---|---|---|---|---|---|---|
| Loop round the plaza, 60 s | 92.5 | 18.4 | 20.7 | 0 | 9.9 / 16.9 | 279 / 401 | 365 k / 493 k |
| City tour, first 100 s | 116.9 | 14.4 | 57.0 | 3 | 7.5 / 11.2 | 188 / 672 | 210 k / 537 k |

Loading: Boot ends at 3.6 to 3.8 s and the title shows at 7.0 to 7.1 s. The props are computed on a worker thread (layout 258 ms, meshes 460, textures 376 in the Mono
player) while the main thread builds the blocks, so the main thread waited 0 ms for them; making the objects takes it 263 ms.

What this says: the props multiply the triangles in view by about six (58 k to 365 k on the loop) and cost about 1 ms of GPU and 35 draw calls on it, because the
distance switch keeps most of the city's 700 k prop triangles out of the frame and what is drawn is cheap geometry. The 672 draw-call maximum is two frames'
counters added together around a hitch (the same artefact as before), not a frame that drew that many. The comparison with the earlier no-props numbers is rough: those
runs were made with other programs busy (the loop read 102 FPS then, p99 16.9 ms; the tour 87 FPS, p99 24.5 ms), this one on a quiet machine. Draw calls and the
per-block switching are the first things to look at if Milestone 3's actors need room.

## Signage and Gate A (Milestone 2, step 8, 2026-10-22)

The whole city again, now with every sign lettered from the stroke font (3,652 lettered panels: word banners, kana bands, kanji plaques, vertical blades,
icons), 99 LED hoardings on tall street facades and 11 floating holograms, each playing one of four ad panels with a scrolling ticker, tearing and a colour
split. The signage meshes come to 62 k triangles, 220 of them screens. The lettering atlas (2048 squared, one channel) and the ad atlas (1024 squared, RGBA)
are drawn on a worker thread while the main thread builds the blocks: 1.3 s and 0.4 s in the Mono player, 0.6 s and 0.2 s outside Unity; the main thread
never waited for them. Same machine, Development player, 1080p borderless (540p internal), Medium, adaptive off, vsync off.

**This session's machine was not quiet** (CPU 30 to 45% before most runs, and 67% before one; Explorer alone took five cores for a minute earlier), which
the numbers show: the same build read anywhere from 74 to 92 FPS on the same loop. The gate has a wide margin either way.

| Run | Avg FPS | p99 ms | Worst ms | Frames over 33.3 ms | GPU avg / p99 ms | Draw calls avg / max | Triangles avg |
|---|---|---|---|---|---|---|---|
| City tour, first 100 s, run 1 (CPU 32% before) | 102.8 | 14.9 | 16.4 | 0 | 8.6 / 13.6 | 194 / 422 | 204 k |
| City tour, first 100 s, run 2 (CPU 43% before) | 88.1 | 18.7 | 50.0 | 2 | 9.9 / 15.4 | 198 / 560 | 208 k |
| Loop round the plaza, 60 s, four runs | 74.3 to 91.6 | 15.9 to 19.6 | 22.4 to 25.1 | 0 | 9.9 to 12.3 / 14.0 to 16.8 | 289 to 292 / 425 | 366 k |

**Gate A: passed.** The city tour's p99 is 14.9 and 18.7 ms against the 33.3 ms limit; the plaza loop's worst p99 is 19.6 ms. The two frames over budget in run 2 are
single hitches (the worst is 50 ms) in a tour that drew 8,807 frames; the gate is about p99. The 560 draw-call maximum is two frames' counters added together around
that hitch, as before.

Loading (the title shows at about 7.9 s; `launch_to_play_s` 7.7 to 8.0 s on a normal launch, target at most 10 s): the engine is up (Boot ends) at 4.0 s,
the city is ready at 7.6 s and the nine reflection probes finish at 7.9 s. The build's main-thread CPU is 1.4 to 1.6 s (textures 780 to 810 ms, signage 155 to 160,
props 330 to 410, blocks 83, plan 57 to 65), about 0.3 s more than before the signage (the two new textures and the two extra meshes per block). Three launches read 10.7,
11.8 and 16.0 s with the machine busy (CPU 67% before the 16 s one); that is the machine, not the loading code, but it is the reason the 10 s target needs a quiet machine to be met.

**What the signage costs.** I could not see it in the frame. Two experiment builds were run against the real one, alternating back to back on the plaza loop:
without the glow washes round the hoardings and holograms 70.9 FPS (the real build 74.3 in the run before); without the hoardings and holograms at all 81.8 and
85.6 FPS against the real build's 91.6 and 82.9 in the runs on either side of them. The differences are smaller than the run to run drift. The lettering replaces a procedural pattern
with one texture read per sign pixel, which is no dearer. Each block gains at most two more renderers (hoardings, holograms), so up to 60 more draw calls over the whole city and
a dozen in view.

## Startup and the flow (Milestone 2, step 3, 2026-10-20)

A normal launch of the Development player on this machine (1280x720 window): the engine is up and Boot ends at 4.4 s, loading
(city already built, reflection probe, a few settle frames) ends at 6.2 s with the title screen showing. The target in the
plan is at most 10 s from launch to title, so there is room for the whole 7x7 city (step 6) to add to it. The standard
benchmark, which skips the title, reached the first playing frame at 8.0 s (`launch_to_play_s`; it also takes a screenshot there)
and measured 140.7 FPS average with p99 9.83 ms through the new flow, so the flow costs nothing in the frame.
