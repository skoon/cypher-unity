# M4 step 7: integration and Gate C

Status: **answered 2026-10-29: teleport, the pilot fires bursts, one ending (corpo) with Save/Continue in the middle.** From `implementation_plan_m4.md` step 7: the whole main job played by a scripted pilot, three games with save and
Continue leave nothing behind, shots of every screen, and the Development player measured on the street route with the HUD and a gun (p99 33.3 ms).

## What exists to build on

- `IntegrationPlayTests` (M3): three New Game / Return to Title rounds with a census of objects, clips and voices; tiers in turn; pause; 90 s on the street route.
  It knows nothing about the RPG, the HUD or the guns.
- `BenchmarkRunner` + `tools/gate-b.ps1`: the street route (160 s lap) with tiers, repeats and a quiet-machine wait. The pilot walks; it never fires.
- RPG goldens already play the main job to all three endings *in the core* (no Unity, no UI). Step 7's pilot is about the game around it: the triggers firing from
  the real player's position, E opening the real dialogue screen, the breach screen, saves on disk.
- UiShots: menus 11e-11k, guns 11l-11t, HUD 11a-11d.

## Work

1. **Main-job pilot (PlayMode, `MainJobPlayTests`).** New Game, then for each objective: put the player at the place, press E through the real interaction path,
   pick dialogue options and breach codes through the screens (the same UiEvents the step 5 tests use), until the job ends. Checks along the way: the tracker
   text, the waypoint target, toasts, XP/cred moved, the quest log's outcome. Shoot a few rounds on the way (ammo, hit marker, no errors).
2. **Save and Continue round trip.** Mid-job: Save from the pause menu, Return to Title, Continue: same stage, inventory, position, ammo is not saved (prototype
   parity: guns are not in the save; say if you want them saved). Then finish the job from the loaded game.
3. **Census extended.** The M3 three-games test also counts RPG/HUD/gun objects (markers, impact pools, viewmodel meshes, gun voices, UI elements), plays a little
   (fires, opens and closes the menu) in each game, and asserts game 3 equals game 1.
4. **Shots of every screen.** One UiShots walk at 1080p: title, options, pause, HUD, dialogue, breach (start, mid, success), inventory, character, journal, vendor,
   scanner, guns. Mostly exists; fill the gaps and re-shoot together into `shots_m4_gate_c/`.
5. **Gate C.** The benchmark gains the RPG on (it is in the scene already) and a gun: `--benchmark-fire` makes the pilot shoot (see question 2). `tools/gate-b.ps1`
   gets a `-Fire` switch (or a copy, `gate-c.ps1`, if you prefer the Gate B script left as it was). Run Medium and Ultra (Gate B's two), compare against Gate B's
   numbers, write `performance_results.md` and `benchmarks/gate-c-<date>/`.
6. task.md, memory, report.

## Decisions I will make without asking (say if you disagree)

- The ending the pilot plays is chosen by question 1; the other two stay covered by the core goldens.
- Gate C uses Gate B's route (street), length (160 s) and 20% headroom reading; a fail is reported, not trimmed, without your yes (as at Gate B).
- No new art, no balance changes; bugs the pilot finds are fixed in this step and listed.

## Questions

1. **How does the pilot move between places?**
   - *Teleport to each place (Recommended):* fast (the whole job in about a minute of test time), checks every trigger, dialogue and breach through the real game.
     Walking is already covered by the street-route tests.
   - *Walk there through CityNav:* the real controller on real streets; places are hundreds of metres apart, so 10-15 minutes per run and fragile to traffic.
2. **What does "a gun out" mean for Gate C?**
   - *The pilot fires bursts (Recommended):* a three-second burst of the Wasp every ten seconds, so tracers, impacts, lights and gun audio are in the measurement. The worst case
     the player can make.
   - *Held only:* the viewmodel drawn and the HUD up, no shots. The cheapest reading.
3. **Which endings does the pilot play?**
   - *One ending (corpo), plus Save/Continue in the middle (Recommended).*
   - *All three:* three runs through the screens; about three times the test time (PlayMode is already ~25 min).
