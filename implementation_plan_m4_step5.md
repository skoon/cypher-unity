# M4 step 5: menus and modal screens

Part of `implementation_plan_m4.md` (approved 2026-10-27). **Answered 2026-10-28: A (working screens on the real core); K opens the character screen.** This note covers what reading the prototype's screens turned up, and one decision that needs you.

## What the prototype actually has (read 2026-10-28)

| Screen | Prototype | Wired to the RPG? |
|---|---|---|
| Dialogue (`ui/Dialogue.js`, 185 lines) | speaker plate, portrait, typewriter text, choices, greyed gated options | **Yes**: choices go to `chooseDialogueOption` |
| Breach (`ui/Breach.js`, 356) | 6x6 code matrix, buffer, daemons, timer, upload | **Yes** when the core starts it (`rpg:hackStart`, `breachSelect`) |
| Inventory (`ui/Menus.js`) | categories, grid, detail pane, carry weight | **List only**: EQUIP / DISASSEMBLE / DROP buttons do nothing |
| Character | level ring, XP, attributes with a "+", perk tree, perk detail | **No**: the tree is placeholder data (`ui/data.js` PERKS), not the core's trees; "+" and UPGRADE do nothing |
| Journal | job list, detail, objectives, TRACK | **Half**: list and TRACK work on the UI's copy; the rewards shown are made up (`level * 240` XP) |
| Vendor | **none** | the core opens a vendor on E (`openVendor`), but nothing is shown |
| Scanner (`ui/Scanner.js`, 250) | hold F: dim, brackets on nearby things, info cards | **No**: cards are invented archetypes ("STREET VENDOR", fake HP) |

So the dialogue and the breach are the only screens the main job needs (talk to Kaz, breach the data core / locker / breaker). The others are a look over little or no function.

## Decision: how far to take the menus?

- **A. Working screens on the real core** (**recommended**). Same look and layout as the prototype, but every control does what it says through the core's existing API, which steps 1-2 already ported and tested: equip / unequip / use / drop, attribute points, the core's real perk trees (buy, respec), chrome install, journal from the quest log with real rewards and TRACK, and a **vendor screen** (buy / sell with the core's prices). Scanner tags the people, vehicles and places that exist, with what we actually know about them. More work (my estimate: the bulk of M4's remaining hours), but nothing on screen lies.
- **B. Parity**: port the screens as they are, decorative buttons and placeholder perk tree included; no vendor screen. Smallest, but E at a vendor does nothing visible and levelling up has no use.
- **C. Parity plus the two holes**: B, plus a working vendor screen and working attribute/perk buying, and nothing else.

## How it is built (any choice)

- Each screen is a `MenuScreen` on the M2 `ScreenStack` (new `UiScreen` values), so Esc / B close it, focus and gamepad navigation work as in the options, and the game pauses behind it (time scale 0) as the pause screen does. The HUD hides under them.
- Keys from step 3: Tab inventory, C character (needs a new binding), J journal; E opens dialogue / vendor / breach via the core; F holds the scanner (an overlay, not a pause).
- Pure view models in `Core/Ui` (what each list shows, filters, sort, compare deltas, button states), fast-loop tested; PlayMode tests drive each screen through its controls in the Slice scene; shots for your look review.
- Dialogue and breach first (the main job needs them), then journal, inventory, character, vendor, scanner.

## Questions

1. **A, B or C?**
2. **Character key:** the prototype uses C, but C is Crouch's alternate key here (Left Ctrl is its main one). Options: *K for Character* (free; **recommended**), or *C for Character and drop Crouch's alternate*. The gamepad has no free button left from step 3's defaults, so on a pad the character screen is reached as a tab inside the menu (Tab / D-pad up opens it on inventory; LB / RB switch tabs, as the options screen does).
