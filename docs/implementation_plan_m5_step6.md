# M5 step 6: quickhacks on enemies

## What is there

- The core already has these. The game calls none of them yet.
  - `RpgCore.UploadQuickhack(slot, targetId)`: costs RAM (ICEPICK discount), scales the duration by `hackDuration`, gives 12 XP, and emits `rpg:quickhackApplied`.
  - `QuickhackDeck`: hacks running on a target; the same hack on the same target refreshes rather than stacks. `Tick` expires hacks and emits `rpg:quickhackExpired`.
  - The derived stats `hackStrength`, `hackRange` (12 m + 0.8 per INT) and `hackDuration`.
- The six hack tables in `RpgItems`:

| Hack | Effect | Cost | Duration | Power |
|---|---|---|---|---|
| Ping | reveal | 1 | 14 | 1 |
| Reboot Optics | blind | 3 | 8 | 1 |
| Overheat | burn | 4 | 10 | 6 |
| Short Circuit | shock | 5 | 1 | 40 |
| Contagion | contagion | 6 | 16 | 8 (spread 3) |
| Cyberpsychosis | berserk | 9 | 12 | 1 |

- `EnemySim.Harm(enemy, damage)` deals damage that is not a gunshot.
- The scanner (hold F) tags places, people and cars.
- The HUD shows RAM as chips.

## Plan

1. **Core: `HackEffects` (pure, fixed step).** Applies a running hack to the enemy it names, with target ids of the form `"enemy:<id>"`.
   - **reveal:** the target and its squad are marked revealed for the duration.
   - **blind:** the target cannot see the player. `CanSee` returns false, and awareness decays as if the player were out of sight.
   - **burn:** Power × (1 + hackStrength/100) damage a second through `Harm`.
   - **shock:** that scaled Power, once, on upload.
   - **contagion:** burns like Overheat at its Power. Every second it jumps to enemies within Spread (3 m) that don't already have it, each copy for the rest of the time left.
   - **berserk:** see question 4.
   - A kill by a hack pays through `RpgService.Kill`, the same as a gun kill.
   - Unit tests for each effect, stacking and refresh, expiry, a hack outliving its target, and contagion's spread.
2. **Core: `EnemySim` hooks.**
   - Each enemy gets `Blind`, `Revealed` and `Berserk` flags.
   - A berserk enemy picks the nearest squadmate as its target, through the same line-of-sight and burst code.
   - Tests for each.
3. **Scanner targeting.**
   - Enemies become scanner contacts, with name, squad and health, and "HOSTILE" or "NEUTRAL".
   - While scanning, the enemy nearest the crosshair within `hackRange` is the hack target. Its card lists the deck's hacks with their RAM costs, greyed out when you can't afford one.
   - How the player picks and uploads a hack is question 1.
4. **`EnemyController`.**
   - Handles `rpg:quickhackApplied` and `rpg:quickhackExpired` and ticks `HackEffects` in its fixed step.
   - Resets them on a flatline.
5. **HUD.**
   - Revealed enemies get a marker, drawn through walls (question 3).
   - The aimed enemy's health bar shows icons for the hacks running on it.
6. **Sound.** An upload blip, a burn crackle and a shock snap, from the existing synth recipes.
7. **Tests.**
   - Core unit tests.
   - PlayMode: scan an enemy and upload Short Circuit (RAM spent, damage, kill pays). Ping reveals the squad. Reboot Optics stops it seeing you. Contagion spreads. Berserk turns it on its squad.
   - Plus the allocation and fight suites.

## Decisions (2026-10-30)

1. Hold F to scan; the wheel picks a hack; Fire uploads it.
2. The damaging hacks and berserk provoke the squad. Ping and Reboot Optics are quiet.
3. Ping shows HUD markers through walls.
4. A berserk enemy shoots its nearest squadmate, and its squad shoots back.
5. The target must be within `hackRange` and in view. A pinged enemy can be hacked through walls. This is the default; it wasn't asked.

## Questions (as asked)

1. **Controls.**
   - *Recommended:* hold F and scroll the wheel to pick a hack; click to upload.
   - *Alternative:* a menu that slows time.
2. **What provokes a squad?**
   - *Recommended:* the damaging hacks and berserk do, like a hit. Ping and Reboot Optics are quiet.
3. **Ping's look.**
   - *Recommended:* HUD markers over the revealed enemies, seen through walls.
   - *Alternative:* an outline on the bodies themselves (shader work).
4. **Berserk.**
   - *Recommended:* the target shoots its nearest squadmate, and that squad turns on it.
   - *Alternative:* the target attacks its own side, but its squad ignores it.
5. **Range and line of sight.**
   - *Recommended:* a target must be within `hackRange` and in view, except a pinged enemy, which can be hacked through walls.
