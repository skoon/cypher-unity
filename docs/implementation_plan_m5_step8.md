# M5 step 8: content (two gigs, Data Core guards, encounters)

Status: **approved and built 2026-10-30** (see task.md). (1) The guards are a **required** objective; (2) a part-killed encounter comes back with **survivors only**, on a despawn and on a flatline; (3) the gigs **as drafted**.

## What is there now

- `EncounterDirector` spawns every `EncounterDef` within 75 m, and despawns it beyond 120 m when it is not fighting. A cleared encounter is kept in the save's flags. `EnemyController.Encounters` is empty: nothing sets it yet.
- The kill objective (`trigger: "kill", tag, count`) works in `RpgQuests`, and a kill counts the squad's tag. No job uses it yet.
- Jobs and conversations are JSON in `RpgContent.g.cs`. Gigs start from a giver's conversation through a `startQuest` effect (Vey starts *Cardiac Arrest*, Marlene starts *Tin Man's Ledger*).
- The breach and conversation screens do not stop time. The fight keeps running behind them.

## Work

1. **Encounters as data (Core).**
   - Add `RpgContent.EncountersJson`: each encounter has an id, a tag, a place (an RPG location key), a reach in metres, its members by kind, and a `when` condition in the existing grammar, for example "the gig is at this stage".
   - Add `RpgEncounters.Build(json, locations, nav)`, which turns these into `EncounterDef`s.
     - Members stand on pavement points round the place, at least 3 m apart, facing out. They are picked from the nav and are the same every time for the same city.
     - I am choosing this over hand-written offsets, which would land inside walls if the city plan changes.
     - Unknown places, kinds or condition keys throw.
   - `EncounterDirector` takes a predicate for whether an encounter is wanted now. It spawns only those, and despawns an unwanted one once it is no longer fighting.
   - It also remembers who is down. A part-killed encounter comes back with only the survivors, after a despawn and after a flatline's `Reset`. The dead are kept in the save beside the cleared flag, so a reload agrees too.
2. **The two gigs and the guards (content).**
   - Add the two jobs, the conversation options that start them, and the encounters (draft below).
   - Add the guards to *Ghost in the Rain*'s Data Core stage, with a required objective, "Deal with the guards" (kill, tag `corpsec`, count 4). Its stages and endings do not change.
3. **Wiring.** `EnemyController.Begin` builds the encounters from the content, and its step evaluates `when` through the RPG core.
4. **Tests.**
   - EditMode:
     - The content loads and is valid.
     - Every member stands on walkable pavement, clear of building colliders, in the real city.
     - `when` gates the encounters.
     - A gig plays through to its outcome headless: kills by tag, then the stage advances.
   - PlayMode:
     - Each gig's encounter appears at its place when the stage is reached, and clearing it completes the objective.
     - `MainJobPlayTests` still plays to the Spire, now clearing the guards first (by the sim, not by aiming; the gun-and-hack pilot is step 9).
   - Full suites.

## Draft content (first guesses: text, numbers and places are yours to change)

**Gig 1: "Jackal Season"** (giver Mama Kaz; needs level 2 and the main job past "Find Mama Kaz").
- Story: the Rust Jackals, a scav crew, have pushed Sixth Street out of the Chrome Cathedral Garage and are stripping cars on Kaz's delivery route. Kaz wants them gone.
- Stages: hear Kaz → reach the garage → clear the Jackals (kill, tag `jackals`, count 5) → back to Kaz.
- Encounter: 4 gunners and 1 heavy, at the garage, only while the gig is at its "clear" stage.
- Reward: 600 XP, 2 street cred, 1,800 €$.

**Gig 2: "Protection"** ("find who's shaking down Marlene"; giver Tin Marlene, through a new "You look worried." option; needs level 3).
- Story: three heavies collect "rent" from her every Thursday. She wants to know whose they are and wants them not to come back.
- Stages: hear Marlene → wait for the collectors outside the pawn shop (they appear when you arrive) → kill them (tag `collectors`, count 3) → take what one carried (a loot item, a datashard naming who sent them) → back to Marlene.
- Encounter: 2 gunners and 1 heavy.
- Reward: 500 XP, 1 cred, 1,400 €$, and Marlene's counter prices.

**Data Core guards** (*Ghost in the Rain*, stage "Breach the Sector Data Core").
- 3 gunners and 1 heavy, tag `corpsec`, at the Data Core while the job is at that stage.
- Whether they block the breach is question 1.

Largest encounter: 5. Gate D (step 9) measures the 5 firing back, plus the street.

## Decisions I will make without asking (say if you disagree)

- Gig encounters exist only while their stage is active. The Data Core guards exist only during the Data Core stage. So the city stays as it was before you take a job.
- Each encounter's tag is its own, so patrol kills (`street`) never count for a gig.
- Rewards and levels are first guesses, listed above.
- The guards use the same gang look (there is one look).

## Questions

1. **The Data Core guards:**
   - *A required objective (Recommended):* "Deal with the guards" (kill, count 4) joins the breach in that stage. The job cannot finish with guards standing, and the breach screen is not interrupted by gunfire.
   - *There but optional:* you can breach with them alive, under fire (the breach screen does not pause the fight).
   - *Optional with a bonus:* as above, plus cred for clearing them.
2. **A part-killed encounter that leaves or resets (despawn, or a flatline):**
   - *Comes back with only those still standing (Recommended):* kill counts and clearing always agree, and you cannot farm XP by walking away and back. This changes my step 3 call and the "the encounter resets" part of the Trauma Team answer.
   - *Comes back fresh, as now:* then a gig counts the encounter as cleared (the save flag) rather than counting kills, and walking away and back can farm kills.
3. **The draft gigs:**
   - *As drafted:* I write the full conversations in the prototype's voice.
   - *Change something:* tell me what (givers, places, numbers, story).
