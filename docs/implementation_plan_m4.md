# Milestone 4: the game (RPG, jobs, hacking, guns, HUD)

Status: **approved 2026-10-27** (branch `m4`, cut from `main` at d54add7). Answers: (1) parity, guns mark hits and nothing else, enemies are a later milestone; (2) a marker in the world at each place as well as the HUD; (3) one slot,
autosave every 120 s plus Save in the pause menu, Continue loads it; (4) M4 starts from `main` once the 3D experiment is committed or shelved on its branch.

M2 and M3 ported the city, its people, its traffic and its sound. M4 ports what the prototype does *with* them: everything M2's plan set aside as "RPG layer,
weapons and impacts, HUD panels". At the end of M4 a new game starts with a job from Mama Kaz, and you can play the prototype's five jobs to any of their endings, level up,
buy perks and chrome, trade with vendors, breach devices, shoot, save, quit, and Continue.

## What the prototype has (read 2026-10-27)

| Part | Prototype source | Lines | Notes |
|---|---|---|---|
| RPG core: one pure state machine | `rpg/Core.js` + `Stats`, `Perks`, `Cyberware`, `Items`, `Inventory`, `Economy`, `Conditions`, `Effects`, `Save`, `Systems` (the contract) | ~4,500 | No THREE, no DOM. **Seeded** (run seed 20771): loot, vendor stock and breach grids are deterministic. 93 Node tests in `tests/rpg.test.mjs`. |
| Content | `Quests.js` (5 jobs: the main job *Ghost in the Shard* with three endings, two side jobs, two gigs), `Dialogue.js` (4 graphs: Kaz, Vey, Marlene, Wire, with stat-gated options), `Economy.js` (5 vendors), `Items.js` (48 templates), `Perks.js` (25), `Cyberware.js` (17), `Hacking.js` (3 breach targets + quickhacks), `Locations.js` (12 places, put on city blocks by the seed) | (in the above) | Data tables; they port as data. |
| Hacking | `rpg/Hacking.js` (breach grids, sequences, the timer, quickhack deck and RAM), `ui/Breach.js`, `ui/Scanner.js` | ~1,000 | The breach is the prototype's minigame: pick codes from a grid along alternating rows and columns to complete sequences before the timer ends. |
| Guns | `player/Weapons.js`, `player/WeaponModels.js`, `combat/Impacts.js` | ~2,700 | Three procedural guns (Wasp SMG, Magnat revolver, Voltline rail projector), viewmodel with sway, bob, recoil, reload, aim down sights, the rail's charge; hitscan; pooled bullet holes, sparks, smoke, debris, tracers, shells and muzzle lights. |
| HUD and menus | `ui/HUD.js`, `ui/hud/*` (vitals, weapon panel, compass, minimap, waypoint, quest tracker, crosshair, damage, toasts), `ui/Menus.js` (inventory, character and perks, journal), `ui/Dialogue.js` | ~3,300 | DOM in the prototype; ours is UI Toolkit, like the M2 menus. |
| Sounds | `audio/Bank.js`: `weapon.crack/dryfire/energy/reload/shell/switch`, `ui.levelup/notify/scan/quickhack/alert/…` | | The step 12 engine and the golden method carry over; the menu sounds already exist. |

**Two facts that shape the plan.**
1. **Nothing in the prototype fights back.** No code emits `combat:kill` or `player:damage`: there are no enemies. Shooting a pedestrian only flashes the hit marker. The RPG has health, damage, kill-count objectives and combat XP waiting for a fight
   that was never built. (Question 1.)
2. **The places are invisible.** A location is a point on a sidewalk; the prototype shows it only on the compass, minimap and waypoint. Talking to Kaz is pressing E inside a 7 m circle at the noodle bar. (Question 2.)

## How it is built

Same method as M3. **Pure logic in `Cypher.Core` (new `Rpg/` folder), checked against the prototype's own code.** A new `tools/gen-golden-rpg.mjs` runs the prototype's `RpgCore` in Node through scripted sessions (a whole main-job run to each ending, buying,
selling, perk and chrome rules, a breach played to success and to failure, a save, a migration from version 1) and writes a snapshot after every step; the C# core replays the same script and must match field for field. The 93 Node tests are ported as C# tests too.
Unity is a thin layer over it, as `Systems.js` is over `Core.js`.

**Content as data.** The quest, dialogue, vendor, item, perk, chrome and breach tables are ported as C# data (generated once from the prototype's tables by the golden tool, then owned by us), so writing a new job later is editing a table.

**Saves.** `Core/Rpg/Save` with the version field and migration chain, stored next to the settings file (`LocalLow\Cypher\CYPHER\save_auto.json`), written atomically like the settings. The run seed is in the save, so the city, the places and the loot come back the same. **Continue** on the title screen loads it (today it is greyed out).

## Work, in order

1. **RPG core, part 1: character.** `Stats` (5 attributes, derived stats, XP, levels to the cap, street cred), `Perks` (trees, ranks, requirements, respec), `Cyberware` (slots, capacity, requirements), `Items` (templates, rarities, the seeded loot roller), `Inventory` (weight, stacking, equip slots, the quickhack deck),
   `Economy` (vendors, prices, restock), `Conditions`, `Effects`, `Save` with migrations. Golden tool and fixtures. Fast-loop tests.
2. **RPG core, part 2: the world's state.** `Locations` (placed on *our* `CityPlan` by the same seeded rule, so they land on the same blocks as in the prototype), `Quests` (stages, objectives by trigger: reach, talk, hack, item, kill, manual; timers; outcomes; markers), `Dialogue` (graphs, stat and flag gates, locked options shown greyed), `Hacking` (breach generation and
   session, quickhacks and RAM), and `RpgCore` (the facade: the read and write API and the event list of `Systems.js`, `{ok, reason}` results for rule failures, exceptions for programmer errors, autosave every 120 s).
3. **Unity service.** `RpgService` (new assembly `Cypher.Rpg`): owns the core, ticks it at 60 Hz with the player's position, forwards its events as C# events, saves and loads, starts a fresh core on New Game and loads on Continue, pauses with the game. Input: interact (E), scanner (F), inventory (Tab),
   character (C), journal (J), weapon slots (1 to 3), fire, aim, reload (R), fire mode; all rebindable through the existing Controls screen.
4. **HUD.** UI Toolkit, native resolution, like the perf overlay: vitals (health, stamina, RAM), XP and cred bars with level-up, compass and minimap with location and objective markers, the waypoint to the tracked objective, the quest tracker, toasts, the interact prompt ("[E] Talk to Mama Kaz"), crosshair and hit marker, damage
   vignette, the weapon panel. Toggled with the rest of the UI; hidden on the title.
5. **Menus and modal screens.** Inventory (categories, compare, equip, use, drop), character (attributes, perk trees, chrome), journal (active, available, finished, track); dialogue; vendor (buy and sell); the breach minigame; the scanner overlay. They use the M2 screen stack, focus and gamepad navigation, and the game pauses
   behind them as the prototype does.
6. **Guns.** `Core/Combat`: weapon configs, fire timing, spread, recoil curves, magazine and reserve, reload and charge states (pure, tested). Unity: the three viewmodels ported as procedural meshes (`WeaponModels` geometry), a viewmodel camera layer so the gun never clips into walls, hitscan through the physics scene
   (walls, props, the people's and vehicles' solid proxies), hit events to the RPG; impact pools (decals, sparks, smoke, debris, tracers, shells, muzzle lights) under fixed budgets; the weapon sounds ported into the audio engine with goldens like step 12's.
7. **Integration and Gate C.** The whole main job played by a scripted pilot in a PlayMode test (walk to each place, talk, choose, breach, reach an ending); three games in a row with save and Continue leave nothing behind (the M3 census); capture shots of every screen; the Development player measured on the street route with the HUD and
   a gun out (Gate C: the same p99 33.3 ms rule as Gates A and B).

Size: large, about the size of M3. Steps 1 and 2 are the bulk of the risk (they are where the rules are) and the cheapest to check; 4 and 5 are the bulk of the hours.

## Decisions I will make without asking (say if you disagree)

- One run seed per game (20771, the prototype's, as now); New Game always starts the same city and the same jobs.
- Prototype numbers are kept: XP curve, prices, perk costs, breach timers, weapon damage and recoil. Balance changes are later, and deliberate.
- The prototype's text (dialogue, item and job descriptions) is ported as is.
- The menus pause the game (time scale 0), as the pause screen already does; the HUD does not.
- No placeholder art: icons are drawn with the existing stroke font and vector shapes, as the M2 menus are.

## Questions

1. **What do the guns shoot?** The prototype has no enemies.
   - *Parity:* targets are the world; a hit on a person or a car is marked and does nothing else. Combat XP, kill objectives and health loss stay unused, as in the prototype. Smallest. **Recommended for M4**, with enemies as a milestone of their own (they need AI, cover, death and damage design that the prototype never had).
   - *Parity plus panic:* as above, and people near a shot scatter and run (the crowd simulation already has walking states to build on); cars do not stop. A week's work more, and the city reacts.
   - *Enemies now:* a hostile gang type in the crowd that shoots back; designs the missing combat in this milestone.
2. **How do you find the places?** In the prototype only the HUD shows them.
   - *Parity:* compass, minimap and waypoint only.
   - *Plus a marker in the world:* a small neon sign or holo beacon at each place (made with the existing neon and board systems), so the noodle bar looks like a noodle bar. **Recommended.**
   - *Plus people at them:* also stand a character (from the 3D experiment's model) at each vendor and contact. Depends on what you decide about that branch.
3. **Saves:** *one autosave that Continue loads, plus a Save in the pause menu writing the same slot.* **Recommended.** Or *three manual slots and a load screen*, or *autosave only*.
4. **Where does M4 start from?** The `3dexperiment` branch is uncommitted. *From `main`, after you commit or shelve the experiment on its branch* (**recommended**: M4 does not depend on it), or *on top of `3dexperiment`*.
5. **Approve the plan?**

## Risks

- **The core is big and branchy** (about 4,500 lines of rules); a quiet mismatch would show up hours into a playthrough. The golden sessions are the answer, so they must cover every trigger type and every ending, and they are written first.
- **UI Toolkit volume.** The prototype's menus are 3,300 lines of DOM and CSS. UI Toolkit has no CSS animation parity; transitions are done in code as in M2.
- **Hitscan against proxies only.** The people and cars have solid proxies only near the player (8 capsules, 4 boxes); a shot at a distant person passes through. Fine for parity (it only marks a hit); a problem the day there are enemies.
- **Viewmodel at 540p.** A gun drawn at the chunky internal resolution may read badly; it may need its own native-resolution overlay camera (decided after the first capture).
- **Performance:** the HUD and a gun add draws and UI cost; Gate C measures it.
