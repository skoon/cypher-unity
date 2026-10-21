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
