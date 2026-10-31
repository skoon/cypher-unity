# M5 step 9: integration and Gate D

Status: **approved 2026-10-31: the squad follows the pilot; enemy volumes stop blocking the player's movement.** From `implementation_plan_m5.md` step 9: a scripted pilot clears an encounter with the gun and a hack, three fights in a
row leave nothing behind (the census), the allocation budget kept, shots, and the Development player measured on a combat route (the street route plus an encounter of the
largest size, the 5 Jackals, firing back): the same p99 33.3 ms rule.

## What exists to build on

- `MainJobPlayTests` (M4, extended in step 8): the main job through the real screens; the Data Core's guards are put down through the sim, not fought.
- `HackPlayTests` and `FightPlayTests` (steps 5 and 6): single hacks and single shots on placed enemies. Nothing plays a whole fight to the end.
- `EncounterPlayTests` (step 8): the Jackals appear with their gig, come back with their survivors, and clearing them moves the gig on. Again the kills go through the sim.
- `IntegrationPlayTests`: the three-games census (objects, clips, voices, UI elements, heap), and the 90 s street walk. Neither knows about the fight; patrols are on in both.
- `AllocationPlayTests`: walking and walking-and-firing, 10 KB a frame. No enemies.
- `BenchmarkRunner` with `--benchmark-fire` and `tools/gate-b.ps1 -Fire` (Gate C). No enemies on purpose: patrols spawn 80–130 m out and seldom meet the pilot.
- `UiShots` (the player's `--shots` mode): menus, HUD, screens and guns. No fight.

## Work

1. **The fight pilot (PlayMode, new `FightPilotPlayTests`).** New Game, *Jackal Season* moved to its "clear" stage, the player at the garage. The pilot aims at the enemy
   it can see (turning the real look towards its chest or head, as a player would), fires the real gun in bursts and reloads, and once uses the scanner: holds F, picks
   the heavy, Pings, then Short Circuits. It goes on until all five are down. Checks along the way: hits land and are marked, the health bar follows the aimed enemy, the hack
   costs RAM and does damage, enemies fire back (hit arcs, the player's health falls). At the end: the gig is at "back to Kaz", XP, cred and loot paid for five kills, the
   encounter is cleared in the save, and nothing threw.
2. **The census with fights.** The three-games test also, in each game, starts the gig, fights at the garage for a few seconds (shots both ways, two kills, one hack), and
   lets a patrol spawn. At the title: no live squads, every hit volume parked, no bodies drawn, no tracers or flashes, no enemy voices. It counts the fight's objects too
   (proxies, pooled tracers) and asserts game 3 equals game 2, as now.
3. **Allocation.** `AllocationPlayTests` gains a third case: walking and firing with the five Jackals engaged and firing back. The budget stays at 10 KB a frame. If it
   is over, I will find and fix the source, as at Gate C. The flaky audio-clip spikes from step 5 are left as you chose.
4. **The flaky 90 s walk.** First confirm the cause: log what the pilot ran into each time it was held up (the collider's name). Then the fix you choose in question 2.
5. **Shots.** `UiShots` gains a fight section: enemies in cover at range, a burst coming in (tracers, hit arcs), the aimed enemy's health bar, the hack deck with a target,
   a Ping through walls, a Short Circuit landing, a body down, the flatline screen, waking at Doc Vey's. Shot at 1080p into `docs/shots/shots_m5_gate_d/`.
6. **Gate D.** `--benchmark-fight` makes the benchmark bring the fight (question 1). `tools/gate-b.ps1` gets a `-Fight` switch, which also turns on `-Fire`. Run Medium and
   Ultra, one run each, as at Gate C. Compare against Gate C, and write `docs/performance_results.md` and `benchmarks/gate-d-<date>/`.
7. task.md, memory, report.

## Decisions I will make without asking (say if you disagree)

- The pilot test keeps the player alive (health topped up when it falls below half), so a bad run fails on what it checks rather than flatlining. The Trauma Team is
  already covered by `EncounterPlayTests`.
- The hack in the pilot is Ping, then Short Circuit on the heavy: one quiet hack, one loud one. The other four hacks stay covered by `HackPlayTests`.
- Gate D uses Gate C's route (street), length (160 s) and 20% headroom reading. A fail is reported, not trimmed, without your yes.
- No balance changes. Bugs the pilot finds are fixed in this step and listed.

## Questions

1. **What does the fight look like during the benchmark?**
   - *A squad that follows the pilot (Recommended):* five Jackals (a heavy and four gunners) spawn 25–35 m ahead of the pilot, out of the crowd's way, and engage.
     When the pilot leaves them behind they are taken away and a new five come ahead. Neither side can die during the run (the pilot's shots still hit and mark them).
     So the worst case, five firing back, is there for the whole recording.
   - *The Jackals at the garage, the route passing by:* the real encounter where the job puts it. Realistic, but the fight is in only part of each lap, so the p99 mixes
     fight frames with quiet ones.
   - *Standing at the garage:* the pilot stays at the garage for the whole run, fighting the five. A fight all the time, but the city round it (the crowd and traffic the
     street route walks through) is less of the measurement.
2. **If the 90 s walk is being held up by patrols' hit volumes:**
   - *The enemies' volumes stop blocking the player's movement; shots still hit them (Recommended):* enemies already walk through the player (the sim moves them, not
     physics), so this makes it even both ways. It also keeps the benchmark pilot from stalling on them in Gate D.
   - *Patrols off in that one test:* the test is about the street. The game is unchanged, and Gate D still measures the fight.
   - *Leave it as is:* raise the test's held-up limit.
