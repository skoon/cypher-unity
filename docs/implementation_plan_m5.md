# Milestone 5: enemies and combat

Status: **approved 2026-10-29.** Branch: `m5`, cut from `m4` once you have committed M4's steps 4 to 7. Answers: (1) set encounters **plus street patrols**; (2) **two** kinds, the SMG gunner
and the heavy; (3) two new gigs plus guards at the Data Core; (4) Trauma Team; (5) people scatter, cars carry on; (6) the procedural rig with combat poses.

M4 ported the game the prototype had. The prototype has **no enemies**, so the RPG is waiting for a fight that was never built:
- the core has health, armour, `Damage()` and `rpg:playerDied`, and a HUD toast for being flatlined;
- `Systems.js` documents two inputs nothing ever sent: `combat:kill { xp, cred, tag, tier, position }` (XP, cred, a loot drop, kill objectives) and `player:damage { amount, source }`. Our `RpgService` handles neither yet;
- the stats include crit chance, crit damage and ranged damage, and the player owns quickhacks (Ping, Reboot Optics, Overheat, Short Circuit, Contagion, Cyberpsychosis) with no one to use them on;
- the job system has a `kill` trigger that no job uses.

So, unlike M2 to M4, **this milestone is design, not a port.** There is no prototype to check against. The rules go in `Cypher.Core` as pure, seeded, fixed-step code with ordinary unit tests and scripted fights replayed headless (the golden idea, against our own recorded runs instead of the prototype's).

## What already exists to build on

| Part | Where | Use in M5 |
|---|---|---|
| Hitscan guns, impacts, kick, hit marker | `Core/Combat`, `Cypher.Combat` | The player's side. Shots already report what they hit (`ShotTarget`). |
| People: sim, GPU-posed rig (706/56/12 tris), proxies near the player | `Core/Crowd`, `Cypher.Actors` | The body, walk and LODs for enemies; the rig needs combat poses. |
| Pavement graph, crosswalks | `CityNav` | Where enemies walk. |
| RPG: health, armour, crit, ranged damage, loot, XP, cred, kill objectives, quickhack deck and RAM | `Core/Rpg` | Damage both ways, rewards, hacks on enemies. |
| Scanner overlay (F) | `UI/ScannerOverlay` | Picking an enemy to hack. |
| Sound: gun recipes, impacts, positional one-shots | `Cypher.Audio` | Enemy gunfire, hits. |
| Benchmark pilot, gate script, census | `Perf`, `tools/gate-b.ps1` | Gate D. |

## Work, in order

1. **Combat rules (Core).** `EnemyTable` (two archetypes: the SMG gunner, who holds cover and lays down fire, and the heavy, armoured, with a revolver, who pushes forward and has a weak head: health, armour, weapon, accuracy, reaction time, ranges, XP/cred/loot tier, tag); `DamageModel` both ways: the player's shot = gun damage × RPG `rangedDamage` × crit roll (`critChance`, `critDamage`) × head multiplier, less the enemy's armour; an enemy's shot goes through `RpgCore.Damage` (the player's armour already applies). Seeded rolls. `RpgService` gains the two inputs the prototype documented (kill and damage). Tests.
2. **Enemy brain (Core).** A fixed-step state machine per enemy: idle or patrol → suspicious (heard a shot, saw the player at the edge of sight) → engage (keep distance, take cover, peek and shoot in bursts, reload) → flee at low health (some archetypes) → down. Sight is a cone plus a line-of-sight query the Unity side answers; hearing is gunshots within a radius. A squad layer so a group does not all peek at once. Fights are scripted in tests and replayed: same seed, same fight.
3. **Where they stand and hide (Core).** Cover points made from the city plan (props, building corners, parked cars), movement on the pavement graph plus local steering to cover; encounter spawn sets per
   place, cleared ones kept in the save; **street patrols**: small groups (2 or 3) walking the pavement graph, neutral until the player shoots near them, aims at them or comes too close, a set number alive at a
   time per quality tier, spawned out of sight like the crowd.
4. **Bodies (Core + Actors).** Combat poses on the pedestrian rig: aim, fire, take a hit, die and lie down (posed in the shader, checked against the C# oracle like the walk), a gang look (colours, a held gun from `WeaponModels` primitives), a head volume for headshots on the proxies. Enemies drawn by the existing crowd renderer path, kept within its draw-call count.
5. **The fight in Unity.** `EnemyController` (spawns encounters, 60 Hz steps, physics line of sight, proxies for every live enemy not just the nearest eight), enemy shots (tracers, flashes and impacts through the existing pools, positional gunfire from the gun recipes), the player's hits reaching enemies (`ShotTarget.Enemy`), XP/cred/loot and kill objectives through the RPG, HUD: hit direction arcs and the damage vignette (the prototype's), an enemy health bar over the one you aim at. Player death per question 4.
6. **Quickhacks on enemies.** The scanner (F) targets an enemy; the six hacks do what their tables say: reveal (Ping), blind, burn over time, shock, contagion spreading to nearby enemies, berserk (attacks its own side). RAM costs as now.
7. **The city reacts.** People near a gunfight scatter and run (the crowd sim gets a flee state); cars do what question 5 says.
8. **Content.** Encounters and jobs that use them (question 3), written as data like the existing jobs, with `kill` objectives.
9. **Integration and Gate D.** A scripted pilot plays an encounter to the end in a PlayMode test (clears it with the gun and a hack); three fights in a row leave nothing behind (the census); allocation budget kept; shots; the Development player measured on a combat route (the street route plus an encounter of the largest size firing back): same p99 33.3 ms rule.

Size: about M4's. Steps 1 to 3 are the risk (they decide whether the fights are any good) and the cheapest to change; 4 and 5 are most of the hours.

## Decisions I will make without asking (say if you disagree)

- Enemies use the player's three guns' numbers as their weapons (Wasp-like SMG, Magnat-like revolver), with lower accuracy; no new gun models.
- Hitscan both ways, no projectile flight, as the player's guns are.
- Enemies cannot hurt civilians; civilians cannot die (the player's shots on a person stay "mark only", as in M4).
- No new binary assets: bodies, guns and sounds stay procedural (unless question 6 brings in the Kenney models).
- Balance numbers are first guesses in one table, tuned by playing; I will say which ones I guessed.

## Questions (answered 2026-10-29; kept for the record)

1. **Where do enemies appear?**
   - *Set encounters (Recommended):* gangs hold a few places (a block's back alley, the Data Core's guards, the garage), spawned when you come near and remembered in the save once cleared; the rest of the city stays peaceful.
   - *Set encounters plus street patrols:* also small gang patrols walking the pavements that turn hostile if you shoot near them or come too close. More life, more work, more performance cost.
   - *Waves / arena:* enemies come to you in rounds at a chosen spot. Simplest, least like the city.
2. **How many kinds of enemy?**
   - *Three (Recommended):* a pistol punk (light, flanks), an SMG gunner (holds cover, suppresses), a heavy (armoured, revolver, pushes forward, weak head).
   - *Two:* gunner and heavy only.
   - *Four:* the three plus a netrunner who quickhacks you (drains RAM, blinds the HUD) and must be found with the scanner.
3. **Which jobs use the fights?**
   - *Two new gigs plus guards on the main job (Recommended):* two short gigs with `kill` objectives ("Clear the Rust Jackals out of the garage", "Find who's shaking down Marlene"), and guards at the Data Core before the breach in *Ghost in the Rain*. Changes the main job's feel; its stages and endings stay.
   - *New gigs only:* the five existing jobs stay as the prototype wrote them.
   - *Encounters only, no jobs:* fights pay XP, cred and loot; no new writing.
4. **When the player is flatlined:**
   - *Trauma Team (Recommended):* black screen, "FLATLINED / TRAUMA TEAM DISPATCHED", you wake at Doc Vey's clinic at full health, lose 10% of your eddies, jobs as they were; the encounter resets.
   - *Load the last save:* the autosave or your Save, as most shooters do.
   - *Respawn where you fell* after a short wait, nothing lost (most forgiving).
5. **The city around a fight:**
   - *People scatter, cars carry on (Recommended):* the crowd flees from shots; traffic is unchanged.
   - *People scatter and cars stop:* drivers stop and wait near a fight (traffic has a stop rule already; it would need a "danger" reason).
   - *Nobody reacts:* as M4.
6. **Bodies:**
   - *The procedural rig with combat poses (Recommended):* consistent with everything on `m4`, no binary assets, the GPU path keeps the cost down.
   - *The Kenney character from `3dexperiment`:* has its own animations, but that branch is not merged into `m4`; it would come in with its asset pipeline and a skinned-mesh cost per enemy.
7. **Approve the plan?**

## Risks

- **No reference to match.** Fights can be correct by the tests and still dull or unfair. I will provide a debug overlay (each enemy's state, sight cone, cover) and a playable test spot, and you will need to play it; tuning will take rounds.
- **The pose shader.** Combat poses are new joint curves in `PedestrianPose.hlsl` and its C# oracle; a death fall that lies flat on uneven pavements may need a simple ground probe.
- **Hit volumes.** Enemies need proxies at any distance they can shoot from (the crowd keeps 8 near the player); I will give enemies their own pool of capsules plus head spheres.
- **Performance.** AI line-of-sight raycasts, more tracers and lights, more proxies. Encounters are capped (question 1/2 set the largest), and Gate D measures the worst case.
- **The main job** (if question 3 adds guards): the Data Core's breach could become unreachable again if the guards make the place unsafe; the pilot test from M4 will be extended to clear them first.
