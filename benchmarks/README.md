# Benchmark data kept for later comparison

Raw output of the player's benchmark (`Cypher.exe --benchmark`, see `tools/unity-check.ps1 -Step Benchmark` and `tools/gate-b.ps1`), kept so that later runs can be compared with these. The write-ups and what the
numbers mean are in `performance_results.md`. Screenshots (`first_frame.png`, `last_frame.png`) were left out to keep the folder small.

Every run folder has `benchmark_summary.txt` (the numbers and the environment: build, GPU, tier, route), usually `benchmark.csv` (every frame: time, frame, CPU and GPU milliseconds, draw calls, triangles,
position, bytes allocated, whether a collection ran), `hitches.csv` (the frames over 33.3 ms), `warmup_hitches.csv` (frames over 50 ms before recording) and `player.log` (the game's own log, including the
sound's status lines every 15 s).

| Folder | What it is |
|---|---|
| `gate-b-2026-10-23/` | Gate B, partial: the Street route (160 s, one lap), Development player, Medium and Ultra, one run each, everything on. `gate-b-table.txt` is the table `tools/gate-b.ps1` printed. |
| `startup-hitches-2026-10-23/` | Four 4 s runs at Medium (centre route) with systems switched off in turn (`all-on`, `no-audio`, `audio-only`, `all-off`): where the stutter after New Game comes from. Read `warmup_hitches.csv` and the "started in" lines of `player.log`. |
| `step12-audio-2026-10-23/` | The sound's cost, alternating runs with and without `--no-audio` (`*-audio`, `*-noaudio`), on the plaza loop (`centre-*`) and the city tour (`city-*`), Medium. `set-B-...` was before the compressor and oscillator loops were made cheaper, `set-C-...` after, with `probe1`/`probe2` (the main-thread breakdown) and `release1` (a non-Development player). The first set, from before the nearest-sign search had a grid, was not kept; its numbers are in `performance_results.md`. The per-frame csv files are left out. |

Runs are only comparable on the same machine (Ryzen 5 3550H, Vega 8), with the same build kind and tier, and a quiet CPU before the run (`gate-b.ps1` waits for one). To repeat Gate B:

```
powershell -File tools/unity-check.ps1 -Step Player
powershell -File tools/gate-b.ps1                      # Street route, five tiers, three runs each, about an hour; takes over the screen
powershell -File tools/gate-b.ps1 -ReportFrom <folder> # print the table for a folder of runs already made, e.g. benchmarks\gate-b-2026-10-23
```
