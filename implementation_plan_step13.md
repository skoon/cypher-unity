# Step 13: integration and Gate B (Milestone 3)

Status: **closed 2026-10-23**, with the answers recorded under "Questions". Sections A, B and C were built and are green. Section D was cut short on your word that the numbers are acceptable for now: Medium and Ultra were measured once on the Street route and passed, Performance, Low and High were not, nothing was repeated and the Junction route and the references were not run; so E found nothing to trim, the default tier stays Medium, and F is done except `player-keys.ps1`. The data and the tools are kept (`benchmarks/`, `tools/gate-b.ps1`). Part of `implementation_plan_m2.md` (approved 2026-10-20), whose line for this step is: "Extend the benchmark pilot through the populated city, per-tier runs, results in
`performance_results.md`, update `task.md`." Steps 9 to 12 each measured what they added on its own. This step measures everything at once, on a route through the streets, fixes what that shows, decides the default
tier and closes the milestone. It adds no features.

## What Gate B is

From the M2 plan: **the default tier, fully populated: p99 frame time at most 33.3 ms** (30 FPS). Every tier's p99 is recorded; the tiers above the default may exceed the gate on this machine (the prototype said the same).
Same terms as M1 and Gate A: the Development player on this machine (Ryzen 5 3550H, Vega 8), a 1080p borderless window with the 540p internal target of the tier, adaptive quality off, vsync off.

"Fully populated" means all of it running, with the tier's own budgets:

| Tier | People | Vehicles | Aircraft | Vehicle lights | Props out to | MSAA |
|---|---|---|---|---|---|---|
| Performance | 36 | 18 | 5 | 2 | 70 m | off |
| Low | 60 | 30 | 12 | 6 | 95 m | off |
| Medium | 110 | 52 | 20 | 6 | 125 m | off |
| High | 320 | 74 | 28 | 6 | 160 m | 2x |
| Ultra | 400 | 96 | 36 | 6 | 210 m | 4x |

plus the signals, the whole signage, the rain, the sound (the bed, the score, five cars, the nearest walkers, the player's own steps), and the street props. The title screen is not part of it (nothing is populated there).

## What the earlier numbers do not say

- **They were not measured on a route through the streets.** The plaza loop (315 m) keeps to the plaza and shows 4 to 20 vehicles and about a hundred people at most. The city tour (2.2 km) runs round the outermost roads,
  where the crowd and the traffic, which are made round the player, have fewer streets within reach on the outward side.
- **The pilot does not get the same route every time once the streets are populated.** The 100 s city tour covered **448 m with sound, 687 m without it, and 460 m in another run**, while the plaza loop is 315 m every
  time. I think the pilot was held up by a vehicle or a person in two runs out of three (it walks the road centreline, where cars stop for it and could then block it), but nothing recorded it, so that is a reading and not a
  fact. My A/B comparisons in steps 10 to 12 were therefore not always over the same ground. Whatever the route is, it has to be one a person could walk, and it has to report when it was held up.
- **Nothing has run everything at every tier.** The crowd was measured at all five tiers, the traffic at all five, the sound at Medium only, and each alone or with one other.
- **Nothing has been run across a whole session.** Each system starts and stops correctly on its own (tested); what is not tested is New Game, Return to Title and New Game again with all of them, a tier change
  with all of them live, or what the heap and the object counts do over a few cycles.
- **Hitches have not been looked for.** The gate is about p99, but the runs show single frames of 40 to 96 ms that nobody has explained, and the sound engine's own update has a worst frame of 17 to 27 ms (probably when a
  game starts, not yet located).

## The work

### A. A pilot that walks like a person (Core, tested)

- **The Street route.** A closed loop of 690 m round the plaza through the eight blocks that touch it diagonally (the middle of the city, not the outer roads), generated from `CityNav` so that it is made of what the
  people walk: half the pavement ring of a block, then a crosswalk to the next block, and on. Eight road crossings, walking for two thirds of the pavement and sprinting for a third (the tours sprint nearly all the way,
  which is not how anyone plays). A lap takes a free walker 158 s, so a recording is **160 s, one lap**, over the same ground every time. `--benchmark-route street`.
- **A way past what stands in the way.** A person or a vehicle in the pilot's path is a normal event now. The pilot notices that it has gone less than half a metre in a second while walking forward and steps aside
  (strafes to the open side, alternating, and jumps if that does not clear it), then rejoins the leg. It counts the seconds it was held up and the detours.
- **The Junction route**, a stress case rather than a gate: a walked loop of 250 m round the four blocks and over the four crosswalks of the junction at the plaza's corner, where cars stop for the pilot and queue, so the
  most vehicles and people that can be in view are in view.
- The run is **invalid** if the pilot was held up for more than 15% of it (as well as the existing rule: less than half the walking distance it should have covered). The summary gets `stalled_s` and `detours`.

### B. A harness that can be trusted

- **`tools/gate-b.ps1`**: waits until the machine is quiet (CPU under 15% for ten seconds) before each run, alternates the tiers round the repeats (Performance, Low, Medium, High, Ultra, Performance, ...) so drift spreads
  over them, copies each run's summary and log, and prints one table. It refuses to start if the player was built before the last source change (the trap from step 11).
- **The summary gains** what is needed to read a run: the people and vehicles drawn (average and most), main-thread allocation per frame (`GC.GetAllocatedBytesForCurrentThread`) and the number of collections,
  the audio cost line, and a **`hitches.csv`**: every frame over 33.3 ms with its time, place, CPU and GPU milliseconds and whether a collection ran. Frames over 50 ms during the warm-up are listed too (first sight of a
  shader, the start of a game).
- Tests for the new recorder fields and the validity rule, in the fast loop.

### C. Integration checks (PlayMode, in the real scene with everything on)

1. **A session, three times over.** New Game, ten seconds of the pilot, Pause, Return to Title, again. After each return the crowd, the traffic and the world's sound are gone (no actors, no voices, `WorldRunning`
   false). After the third, the counts of GameObjects, AudioSources, meshes, materials and textures equal the first game's, and the managed heap has not grown by more than a few megabytes.
2. **A tier sweep while playing.** Performance, Low, Medium, High, Ultra and back: within a few seconds the people, vehicles, aircraft and vehicle lights are the tier's numbers, nothing throws, the sound carries on.
3. **A slow machine.** With adaptive quality on and 40 ms of artificial load a frame, the governor steps the tier down, the populations shrink and a notice is shown; with the load removed it holds where it is.
4. **Pause and resume.** Everything freezes (positions do not move, the sound is ducked) and carries on from the same place.
5. **The whole thing for 90 seconds**: the Street route at Ultra in the Editor: no error in the log, no NaN, people and vehicles within the tier's budgets, voices within their caps, and the pilot gets round at least 85%
   of the distance.

Then, outside the tests: launch to the title and to the first playing frame (limit 10 s), memory, and the list of hitches, the biggest of which I will explain and fix if the cause is ours (the frame in which a game
starts is the first suspect; its work can be spread over frames).

### D. Measuring

All in the Development player, rebuilt first, in windows of ten to fifteen minutes in which **I need the machine to myself** (the window takes over the screen; I will tell you before each one, and stop if Windows resets as it
did twice in step 6).

- **Gate B proper:** the Street route, 10 s warm-up and 160 s recorded (one lap), **three runs of each of the five tiers**, alternated. A tier passes if all three runs have p99 at most 33.3 ms. I report each run and the median.
- **Stress:** the Junction route, one run a tier, recorded only.
- **Reference, not gating:** the default tier once in a release (non-development) Mono player, and once in an IL2CPP player if it will build (see Questions).
- Machine time is about an hour: five tiers by three runs at about 3.3 minutes each, the stress runs and the references.

### E. After the numbers

- **If the default tier passes with room**, nothing is cut. The tiers above it are recorded as they come.
- **If a tier that has to pass does not**, populations are trimmed in this order, the cheapest visible loss first, each step re-measured: the vehicle point lights; the aircraft; the full-rig distance of the people
  (`ActorNear`); the vehicles; the people; the props' draw distance; and, if the sound is the margin that is missing, the neon and air-conditioning sources and some car voices on the lower tiers (new tier fields). The
  pinned tier table in `QualityTiersTests` moves with them.
- **The default tier** is chosen by the rule you pick below and written into `GameSettings.QualityDefault`.

### F. Closing the milestone

`tools/player-keys.ps1` (real keys through every menu, not run since step 7 because it needs the PC free; I will ask for a window), `performance_results.md` (a Gate B section with the table, the hitches and what was
cut), `task.md` (step 13, the milestone's summary and the known limitations updated), the memory, and `unity-check.ps1 -Step All` green. The `shots11` folder left in the repo is yours to say yes or no to.

## Decisions I will make without asking (say if you disagree)

- The gate build stays the Development player (Mono), as for M1 and Gate A; the release and IL2CPP runs are references.
- Three runs a tier, each of which must pass; 160 s recorded; the median is what I quote.
- The route is on the pavement and the crosswalks, not the road centreline.
- Sprinting for a third of the route (the tours sprint nearly all of it); the Junction route is walked.
- The sound is on in every run (it is part of the game now); the controls (`--no-audio`, `--no-crowd`, `--no-traffic`) stay for explaining a number.
- I fix what the integration checks and the hitch list find, and nothing else: no new features, no retuning of the sounds (that waits for your listening and is done before the measuring, so the numbers are of the final code).

## Questions (answered 2026-10-23)

Answers: **1** the highest tier with at least 20% headroom; **2** skip IL2CPP; **3** no trimming without asking you first (so section E stops and shows you the numbers before any cut).

1. **Which tier should a fresh install start on?** The governor only steps down, never up, so a default that is too high costs a bad first minute on a weaker machine, and one that is too low wastes the machine. The M2 plan left
   this to the Gate B numbers (Medium is a placeholder). My expectation, from the plaza loop with the crowd and the traffic (Ultra: 65 FPS, p99 22 ms), is that every tier passes, so this is a real choice.
   - *The highest tier that passes with at least 20% headroom* (p99 at most 26.7 ms in all three runs). **Recommended.**
   - *Medium whatever the numbers say*: what has been tested most, and the lowest risk on a weaker machine.
   - *The highest tier that passes at all.*
2. **An IL2CPP player, as a reference?** The module is installed and Visual Studio's C++ tools are on the machine, but I have never built one here. It costs a build of perhaps 20 to 30 minutes and may fail on the Windows SDK;
   it would show what a shipped build does (the synthesis loops should be several times faster), which the 30 FPS gate in Mono does not. *Yes, try it once.* **Recommended.** / *Skip it.*
3. **If the default tier fails, may I trim?** *Yes, in the order above, a step at a time, reporting each.* **Recommended.** / *No: stop and ask you before cutting anything the player would see.*

## Build order and tests

1. **Pilot and route** (A). Tests: the Street route is made of nav nodes and edges only, stays on the pavement and the crosswalks, closes, and is the same every time; the stall detector fires on a blocked pilot (a fake motion
   model that holds it) and not on a free one; the sidestep gets it past a pillar and a person-sized block; the validity rule; `--benchmark-route` parsing.
2. **Harness** (B). Tests for the recorder fields; a dry run of `gate-b.ps1` against the plaza loop to prove the quiet-machine wait, the alternation and the table.
3. **Integration checks** (C), then fix what they find.
4. **Hitches**: read the list from a first populated run, explain the largest, fix ours.
5. **Measure** (D), in windows, with you away.
6. **Trim, choose the default, re-measure what changed** (E).
7. **Close** (F). `-Step All` green at the end, the fast loop green all along.

Size: medium to large, and most of it is measuring and waiting for a quiet machine. Risks: the machine itself (it reset twice during long jobs in step 6 and has read 20% slower under background load: hence the quiet wait, short
runs and repeats); a route that is blocked in ways I have not thought of (hence the stall count and the validity rule, so a bad run says so); a top tier that fails (trimming order above); the integration checks finding
a leak, which would be worth the whole step.
