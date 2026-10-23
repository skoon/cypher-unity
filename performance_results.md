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

## Pedestrians, a checkpoint (Milestone 3, step 10, 2026-10-22)

The people: up to 400 in the pool, 36 / 60 / 110 / 320 / 400 walking on the five tiers (the prototype's budgets), simulated at 60 Hz on the CPU and posed entirely in the vertex shader.
Each frame the people in view are written into one 80-byte-a-person buffer and drawn in one instanced draw per level of detail (the full rig out to 42 m, a rigid four-part body to 92 m, a box beyond; Performance 25 and 55 m) plus one
for the contact shadows: **three or four draw calls for the whole crowd**, whatever its size. The full rig is 706 triangles (917 vertices); the cheap bodies 56 and 12.

Measured the way the tiers were: the plaza loop, 60 s, Development player, 1080p borderless, each tier's own preset, **the machine quiet** (CPU 5 to 24% before each run), the same build with and without
people (`--no-crowd`), alternating back to back. The loop stays near the plaza, so it never has more than about a hundred people in view.

| Tier (people) | With people: FPS / p99 ms / GPU ms | Without: FPS / p99 ms / GPU ms | In view with people (full / simple / boxes) |
|---|---|---|---|
| Performance (36) | 152.2 / 8.7 / 5.7 | 149.4 / 9.3 / 5.8 | 0 / 2 / 11 |
| Medium (110), first pair | 100.4 / 14.3 / 9.0 | 110.1 / 12.5 / 8.4 | 5 / 6 / 25 |
| Medium (110), second pair | 106.0 / 13.0 / 8.6 | 111.2 / 11.9 / 8.3 | 4 / 4 / 27 |
| High (320) | 87.2 / 15.1 / 10.5 | 88.1 / 15.6 / 10.5 | 11 / 28 / 56 |
| Ultra (400) | 80.9 / 16.6 / 11.4 | 80.9 / 17.1 / 11.4 | 13 / 30 / 68 |

The whole-city tour (first 100 s plus warm-up, Medium, the same alternation, twice each) says the same on a longer and more varied route: **127.1 and 127.1 FPS with the 110 people (p99 12.5 and 11.8 ms, GPU
7.0 ms) against 127.6 and 128.1 without (p99 12.0 and 11.3 ms, GPU 7.0 ms)**, 200 draw calls against 197, 211 k triangles against 209 k. Over the tour 10 to 50 people were in view at a time (2 to 8 of them in the full rig), one
person was solid for a moment, and there was one 46 ms frame in the first run (and an 82 ms one without people in the first control run, so those hitches are not the crowd's).

**The crowd costs next to nothing here.** At Performance, High and Ultra the runs with and without people are within the noise (the people even read slightly faster at Performance); at Medium the people cost about
0.5 to 0.9 ms a frame (4 to 10 FPS at 100 to 110 FPS). Draw calls go up by three or four, triangles by 1 to 2.5% (Ultra: +11 k on 587 k). Launch to the first playing frame is 7.0 to 7.3 s, unchanged. The CPU
side (the simulation, the culling and sort, 80 bytes a visible person) was not timed on its own: it is inside the frame times above, and the Medium difference of under a millisecond is the most it can be there.
It is written not to allocate per step (pooled neighbour lists, reused buffers), which has not been measured either.

What this does not say: the loop shows few people at a time. Gate B's route (step 13) goes through the streets, where a hundred people can be in view, and adds traffic; that is the measurement that counts. The cheap
bodies and boxes are what keep it cheap: of the 400 on Ultra only about 110 are in view, and only 13 of those get the full rig.

## Traffic and aircraft, a checkpoint (Milestone 3, step 11, 2026-10-23)

The vehicles: a fleet of 96 and 36 aircraft, of which 18 / 30 / 52 / 74 / 96 vehicles and 5 / 12 / 20 / 28 / 36 aircraft are in use on Performance to Ultra (the prototype's budgets), simulated at 60 Hz on the CPU
and placed entirely in the vertex shaders. Each frame the ones in view are written into two buffers of 48 bytes a vehicle and drawn in **at most twelve draw calls whatever their number**: a hull draw for each of
the four vehicle types in view (and two for aircraft), one for all the wheels, one for the glow (lamps, under-glow and headlight cones), one for the shadows, one for the aircraft's glow, one for the 768 signal
lenses and one for the 256 signal masts. The nearest vehicles in view (six; two on Performance) carry real point lights. Hulls are 24 to 32 triangles, a wheel 36, a bike with its rider 148;
the masts and lenses are 18,432 triangles that are always there.

Measured as the pedestrians were: the plaza loop (60 s) and the whole-city tour (100 s), Development player, 1080p borderless, each tier's own preset, with and without the traffic (`--no-traffic`; the people are in both),
alternating back to back; the machine quiet (CPU 1 to 13% before each run unless said). **One trap, found the hard way:** `unity-check.ps1 -Step Benchmark` runs the player already built, so a benchmark after a code
change needs `-Step Player` first; my first set of numbers was of the old player with the people only, and showed no difference at all.

| Run | With traffic: FPS / p99 ms / GPU ms / draws | Without: FPS / p99 ms / GPU ms / draws | In view (vehicles and aircraft) |
|---|---|---|---|
| Performance, plaza | 153.7 / 8.2 / 5.6 / 281 (CPU was 54% before) | 136.4 / 11.2 / 6.3 / 271 | 4 and 0 |
| Medium, plaza | 85.2 / 16.2 / 10.7 / 306 | 91.0 / 15.6 / 10.0 / 295 | 13 and 3 |
| High, plaza | 70.7 / 18.2 / 13.1 / 316 | 71.7 / 18.6 / 12.2 / 301 (one 2.6 s hitch) | 14 and 3 |
| Ultra, plaza | 64.9 / 22.0 / 14.3 / 328 | 68.3 / 20.3 / 13.5 / 316 | 20 and 4 |
| Medium, city tour (twice) | 98.3 and 98.8 / 16.0 and 14.7 / 9.2 and 9.0 / 248 and 214 | 108.4 and 107.7 / 13.4 and 13.3 / 8.2 and 8.2 / 202 and 203 | 23 and 2, 9 and 3 |

A second set, cleaner, to see what the point lights cost (traffic with the lights, with the traffic but no lights `--no-vehicle-lights`, and no traffic; two pairs each):

| Run | Traffic with its lights | Traffic, no lights | No traffic |
|---|---|---|---|
| Medium, city tour | 106.6 and 104.5 FPS; GPU 8.30 and 8.54 ms | 109.3 and 105.9; 8.12 and 8.39 | 109.7 and 108.4; 8.07 and 8.17 |
| Ultra, plaza | 67.8 and 68.4 FPS; GPU 13.68 and 13.58 ms | 69.2 and 69.7; 13.36 and 13.28 | 70.2 and 70.9; 13.18 and 13.07 |

**What it costs:** between about 0.3 and 1 ms of GPU a frame, 3 to 10% of the frame rate, at every tier (the cleaner set says the low end: 3% and 0.3 ms at Medium on the tour, 3.5% and 0.5 ms at Ultra on the
plaza). The point lights are about half to 60% of it; the rest is the 10 to 12 extra draws, 20 to 40 thousand extra triangles (half of them the signals), and the blending of the cones, the under-glow and the shadows.
Launch to the first playing frame is unchanged at 6.7 to 7.3 s. The CPU side was not measured apart: the whole of the traffic's simulation is under a millisecond for 96 vehicles (the fast loop steps ten simulated
minutes of 400 people and 96 vehicles in a few seconds).

What this does not say: the plaza loop shows only 4 to 20 vehicles at a time, and the tour 9 to 23. Gate B's route (step 13) goes through the streets with the crowd and the traffic together; that is the measurement
that counts, and where the tiers' budgets are trimmed. The knobs are the lights (two on Performance already), and the number of vehicles.

## Ambient audio, a checkpoint (Milestone 3, step 12, 2026-10-23)

The sound is mostly **live synthesis on the audio thread**: the bed (about 70 nodes: noise loops, oscillators for the slow modulators, filters and gains) with a neon buzz and an air-conditioning unit on their own sources, the
score's pad, delay and sidechain, an engine and tyres for each of the five nearest cars, and the master glue and limiter. What can be made ahead is made at load: 317 one-shot clips (menu sounds, footsteps on six surfaces at
three paces and wetnesses, jumps, landings, breaths, the ambient one-shots) rendered on two worker threads while the city builds, then turned into `AudioClip`s a few a frame. Notes of the score are rendered a second ahead and
queued by sample. Nothing synthesises on the main thread.

Measured the way the actors were: the Development player (Mono), 1080p borderless, Medium, the plaza loop (60 s) and the whole-city tour (100 s), with and without sound (`--no-audio`), alternating back to back, the
machine quiet (CPU 1 to 28% before each run; 53% before the first of set A and 87% before one run of set C, which had no sound). The first set (A) was before the nearest-sign search was given a spatial grid; B and C are after, C after the cheaper compressor and oscillator loops.

| Run | With sound: FPS / p99 ms / GPU ms | Without: FPS / p99 ms / GPU ms |
|---|---|---|
| Plaza loop, set A (twice) | 77.5 and 75.7 / 19.6 and 22.4 / 11.5 and 11.6 | 80.9 and 78.7 / 19.3 and 20.8 / 11.0 and 11.3 |
| Plaza loop, set B (twice) | 82.6 and 86.5 / 17.0 and 17.3 / 11.0 and 10.5 | 90.8 and 89.0 / 15.7 and 16.2 / 10.0 and 10.2 |
| Plaza loop, set C (twice) | 83.1 and 88.7 / 19.0 and 15.6 / 10.8 and 10.2 | 84.3 and 90.0 / 18.4 and 15.8 / 10.7 and 10.1 |
| City tour, set B | 103.2 / 13.5 / 8.7 | 114.5 / 13.2 / 7.9 |
| City tour, set C | 100.4 / 15.5 / 8.9 | 110.1 / 13.2 / 8.2 |

**What it costs:** between 1 and 10% of the frame rate on this machine, no more than about a millisecond of frame time (the tour's CPU frame time is 9.96 ms with sound against 9.08 without), and nothing near the gate
(30 FPS, 33.3 ms). Hitches: over the six plaza-loop runs of each kind, 22 frames went over the 33.3 ms budget with sound and 11 without (of about 27,600 each; the worst 96 ms with, 69 ms without), and none on the
tour either way, so the loop is noisy and the sound may add a few.

Where it goes, from the engine's own counters (a status line every 15 s in the log; `AudioEngine.Cost`):

| Where | Cost | Notes |
|---|---|---|
| Audio thread, total | **5.6 to 5.8 ms of every 21.3 ms block (26 to 27%)** | was 6.3 ms (30%) before the cheaper compressor and oscillator loops; a release (non-development) player measured 6.05 ms against the development player's 6.3 at that point, so it is the loops and not debug code |
| the bed and its two emitters | 3.0 ms | 25 oscillators, 47 gains, 17 filters, 9 noise loops: a few hundred small node runs a block |
| the score | 0.85 ms | |
| five car voices | 1.15 ms | a car's engine and tyres, 0.23 ms each |
| master glue and limiter | 0.63 ms | was 0.87 ms |
| worst single piece in a block | 7 to 12 ms | one piece's worst callback, not the sum; nothing here detects a missed block, and nobody has listened for crackle yet |
| Main thread | **0.16 to 0.18 ms a frame** (worst 17 to 27 ms, once, at the start of a game) | was 0.96 ms: the nearest-sign search scanned every tube and vent in the city each frame (0.62 ms); it is a grid now |
| Clips in memory | 27.1 MB | 317 clips |
| Load | 4.5 to 5.5 s of worker time, 47 to 58 ms of main thread to make the clips | launch to the first playing frame 7.3 to 8.3 s with sound against 6.7 to 8.2 without, about half a second more on average, as the workers share the cores with the city build |

The engine's own work on the main thread explains only a part of the frame-rate cost: the frame loses 0.1 to 1 ms more than the 0.17 ms, and the GPU time reads 0.1 to 0.8 ms higher with sound on although nothing on
the GPU changes. My reading (not proved) is that this laptop's processor and graphics share one power and cache budget, and the audio thread's quarter of a core is paid for there. If so, the audio thread's cost is worth
keeping down as well as the main thread's.

**Levers, if Gate B needs them** (none pulled): fewer car voices (each is 0.23 ms), the neon and air-conditioning sources off on Performance, the bed's filter coefficients refreshed every 128 frames instead of 32,
slow modulators computed once per 32 frames and interpolated (they are about a fifth of the bed), and an IL2CPP player, which this project has not built and which should run the synthesis loops several times faster than Mono.

What this does not say: IL2CPP, other hardware, and the populated route of Gate B (step 13), where the crowd, traffic and sound all run together. The audio cost does not grow with the crowd or traffic (it is five cars and the
nearest walkers' footsteps whatever the population), so what step 13 adds is the other costs' sum.

## Startup and the flow (Milestone 2, step 3, 2026-10-20)

A normal launch of the Development player on this machine (1280x720 window): the engine is up and Boot ends at 4.4 s, loading
(city already built, reflection probe, a few settle frames) ends at 6.2 s with the title screen showing. The target in the
plan is at most 10 s from launch to title, so there is room for the whole 7x7 city (step 6) to add to it. The standard
benchmark, which skips the title, reached the first playing frame at 8.0 s (`launch_to_play_s`; it also takes a screenshot there)
and measured 140.7 FPS average with p99 9.83 ms through the new flow, so the flow costs nothing in the frame.
