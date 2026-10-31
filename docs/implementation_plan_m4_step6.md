# M4 step 6: guns

Part of `implementation_plan_m4.md` (approved 2026-10-27; the user chose "guns mark hits only, enemies later"). **Answered 2026-10-28: A, the prototype's three guns.** This note covers what reading the prototype's gun
code turned up, how the port is built, and one decision.

## What the prototype has (read 2026-10-28)

- `player/Weapons.js` (1,314 lines): **three fixed guns**, a table of its own: *Wasp* (SMG, slot 1, full auto 880 rpm, 32-round mag), *Magnat* (revolver,
  slot 2, semi 172 rpm, 6 rounds, a chamber-check "inspect" on R when full), *Voltline* (rail projector, slot 3, hold to charge 0.95 s, release fires,
  5 cells). Per gun: damage, range, spread (base, per shot, max, decay, move, air, ADS), recoil (camera kick + viewmodel springs), flash, tracer, shells,
  hip/ADS/sprint poses, bob. A state machine (draw, idle, holster, reload, inspect) with scripted reload animations per gun (magazine drop and seat,
  cylinder swing-out and ejector, cell swap). Hitscan from the camera with spread; surfaces: flesh, metal (props), asphalt, concrete, energy.
  Viewmodel squashed into the nearest sliver of depth so it never clips into walls; narrower viewmodel FOV.
- `player/WeaponModels.js` (605): the three guns built from primitives with per-vertex colour, metal/rough and four glow channels (always, ammo, charge, heat).
- `combat/Impacts.js` (775): pooled decals (128), sparks (384), smoke (128), debris (96), tracers (16), shells (16, physics bodies) and 4 muzzle lights.
- `audio/Bank.js`: gun sounds (a four-part gunshot recipe per profile, energy shot, crack, dry fire, switch, reload, shell). **Not ported in M3**.
- HUD: weapon panel and crosshair kick/hit marker (`ui/hud/WeaponPanel.js`, `Crosshair.js`; I left the weapon panel for this step).
- **Not connected to the RPG at all**: the RPG's weapon items (w_pistol, w_smg, w_rifle...) have stats, but the guns ignore them and nothing in the RPG
  reads a hit. The RPG has no ammo items.

## How it is built

1. **`Core/Combat`** (pure, fast-loop tested): the weapon table, the trigger and fire timing (auto, semi, charge), magazine and reserve, reload with the seat
   moment, the switch state machine (holster, draw, inspect), spread (rest, per shot, decay, movement, air, ADS), recoil impulses, the viewmodel springs, sway
   and bob, the reload/draw/inspect pose curves, and the shot direction from a seeded random stream. Checked against the prototype's code run headless in
   Node (golden sessions: hold fire, tap, charge and release, reload mid-mag, switch mid-reload), as the earlier steps were.
2. **Models**: `WeaponModels` ported as mesh builders in Core (vertex colour + MRE + glow channel), one mesh per moving part, golden-checked on vertex counts and
   bounds; a `Cypher/Viewmodel` shader (lit like the city, the glow channels, the depth squash so it never clips).
3. **Unity `WeaponSystem`**: input (Fire, Aim, Reload, 1-3), the viewmodel under the camera, hitscan with `Physics.Raycast` against the city, props and the
   people's and vehicles' collision proxies, camera recoil and shake through the player's look, ADS FOV.
4. **Impacts**: pooled decals, sparks, smoke, debris, tracers, shells, muzzle lights, drawn in a handful of draws, at the prototype's budgets.
5. **Sounds**: the gun recipes ported into the audio engine with Chromium goldens, as in M3 step 12; played on fire, hit, reload, switch, dry fire, shells.
6. **HUD**: weapon panel (name, mode, ammo / reserve, reload bar), crosshair that opens with spread, hit marker.
7. Off while a game screen is open, holstered on the title.

## Decision

**Which guns?**
- **A. The prototype's three** (*recommended*): Wasp, Magnat and Voltline on 1-3, their table as is, unrelated to the inventory, as the prototype. With no
  enemies yet, a gun's damage only labels its hits; tying guns to items is best decided with the enemy milestone, when damage starts to matter.
- **B. Guns follow the RPG**: the equipped primary and secondary weapons are what you hold; each item type maps to one of the three models (SMG/rifle to the Wasp,
  pistol to the Magnat, sniper/shotgun to the Voltline) and the item's rolled damage and RPM replace the table's. Needs ammo rules the prototype never had.
- **C. A, plus the RPG's numbers**: the three guns, with the character's ranged damage and crit stats applied to the hit numbers shown, and a hit counted for
  the RPG's combat XP.

## Decisions I will make without asking (say if you disagree)

- Shell casings fly on a simple arc and bounce once on the ground instead of being physics bodies (cheaper; looks the same at that size).
- Only the people and vehicles near the player have collision proxies (8 and 4, from M3), so shots pass through people further away. Hits on them are marked
  and do nothing else (your M4 choice).
- The gun is out whenever you are playing, as in the prototype (no holster key).
- Random spread and recoil come from a seeded stream (repeatable in tests); the prototype used `Math.random`.
