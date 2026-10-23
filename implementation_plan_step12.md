# Step 12: ambient audio (Milestone 3)

Status: **built and checked 2026-10-23** (steps 1 to 5, 7 and 8 of the build order); **step 6, listening, waits for you**: the WAV files and `levels.md` are written by `Cypher > Export Audio Samples`, and the Development player
(`Builds\Dev\Cypher.exe`) plays everything. What differs from this plan: the cost is higher than "small" (the audio thread takes about 27% of a core and the frame rate falls 1 to 10%, see `performance_results.md`, with
the levers listed there), so the bed stays live but Gate B may trim it; the score and the exporter share `ScoreNotes` so the game and the files make the same sounds; the sounds were also checked in Chromium sample for sample,
not only by level and spectrum as the plan said, which caught several Web Audio details (listed in `task.md`). Approved 2026-10-23, with the answers recorded under "Questions". Part of `implementation_plan_m2.md` (approved 2026-10-20), whose sketch was: port the synthesis, render to clips on worker threads at load, play through an
`AudioMixer` that the Options sliders drive. This plan fills in how, and changes one thing in the sketch (the continuous bed is synthesised live, see Questions). The behaviour is the prototype's
[audio/](file:///D:/source/cyberpunkish/src/audio): `Ambience.js` (the bed and its one-shot clocks), `Bank.js` (the sound recipes), `Graph.js` (mixer, voices, reverb), `Audio.js` (the system), `Dsp.js` (noise).

## What you hear

Rain, wind, a wet arterial two blocks away, a crowd murmur, the mains hum of the neon and an air-conditioning unit on a roof, under a bed that never repeats: every continuous layer is noise moved by slow
low-frequency oscillators at rates that do not divide each other (0.037, 0.061, 0.089 Hz and so on), so the combined contour has a period measured in hours. On top, one-shots come at random gaps: a siren four blocks
away, a distant impact, drips in the rain, a scrap of speech, a failing neon tube, a car hissing past on the wet. The bed follows the weather (rain on or off), muffles indoors, and dips when the game is paused or a
menu is open. The menus have their own small, consistent set of sounds (hover, tick, select, confirm, back, open, close, error, alert, notify). Sounds that come from somewhere are placed there, a siren high and far,
a neon buzz at the sign, and are muffled by a wall between you and them; the room around you (alley, street, plaza, indoors) changes how everything rings. The six Options sliders (master, music, effects, ambience,
interface, voices) work.

Your answers add three things. **The player's own sounds**: footsteps on six surfaces (puddles in the rain, metal grates, gravel), a jump, a landing, breathing that follows exertion, cloth. **The real traffic and people make
sound**: the nearest cars have an engine and tyre hiss that follow their speed (and the wet), the nearest walkers have footsteps, in place of the prototype's random car passes and voices. **The score**: the prototype's generative synthwave, a
pad under bass, arpeggio, drums and the odd stab and lead, at a fixed tempo, whose layers arm on the bar as its intensity rises. With nothing yet to excite it (no combat) it plays its calm side, with the
hooks in place; hard landings bump it, as in the prototype. **Nothing plays on the title screen but the menu sounds**; the bed and the music begin with New Game.

## What is ported, unchanged

The recipes and numbers of `Ambience.js` (seven continuous layers; the weather mapping of `advance`: how rain, wind, indoor and density set each layer's level and filter; the six one-shot clocks, their gaps and
where around the listener each lands), the six ambient one-shots, ten UI sounds, footsteps, jump, land, cloth and breath of `Bank.js`, `Music.js` and its twelve instrument recipes, the bus structure and levels of `Graph.js` (ambience 0.85, effects 0.95, interface 0.6, music 0.55, voices 1.0, master 0.9; ducking
0.15 paused, 0.45 in a menu), voice priorities and stealing, the occlusion rule (a ray from the listener: a wall costs the highs and 60% of the level), the geometry probe that picks the reverb space (twelve rays and one
up every half second, two agreeing probes before it changes), the five spaces' characters, and the seeds, so the same seed makes the same sounds. Not ported: weapons, impacts, the RPG sounds (level-up, scan, quick-hack),
which have nothing to trigger them yet. The random car passes and voices stay in the code (they are the prototype's, and the fallback when there is no traffic or crowd) but the game uses the real ones.

## Architecture

**Core (pure, no engine, in the fast loop), new folder `Core/Audio/`:**
- The prototype's vocabulary in plain C#: `SynthGraph` with noise loops (white, pink, brown, velvet, from `Dsp.js`'s generators and the same seeded `Rng`), oscillators (sine, triangle, and the band-limited
  `softsaw`, `hollow` and `bell` wave tables), biquad filters with the Web Audio specification's formulas (including that a low- or high-pass Q is in decibels), gains, delays, a wave shaper, a stereo panner, and parameters with
  scheduled automation (`setValueAtTime`, linear and exponential ramps, `setTargetAtTime`). It renders in blocks, offline or live, without allocating after it is built. The recipes are written against exactly this
  vocabulary, which is what makes porting them mechanical and checkable.
- `Recipes` (about forty sounds: UI, ambient, foley, the music's instruments), `Bed` (the seven layers and `Advance`), `Score` (the prototype's `Music`: chord progressions, the 16-step bar, which layers arm at which
  intensity, the pad and the sidechain pump, scheduled a bar at a time), `Surfaces` (which surface a step lands on, from position and rain), `Stride` (when a step falls), `EventClocks` (the six scheduled one-shots, with their gaps and the stall resync), `BusMixer` (bus levels, the slider mapping, ducking,
  the master fade-in), `VoicePool` (priority and stealing), `Occlusion`, `SpaceProbe` (the classification and its hysteresis, from ray distances), `ReverbSpaces` (the five spaces as reverb settings), `AudioAnalysis` (the
  prototype's `analyzeBuffer`: peak, RMS, centroid, band energies).

**Unity, new assembly `Cypher.Audio` (references Core, City, Game, Player, Rendering, UI):**
- `AudioEngine`: starts with the game, owns everything below, follows the options and the game flow (duck on pause and menus, fade the master up), and logs its state.
- `ClipBank`: renders the one-shots to `AudioClip`s on worker threads while the city builds (a few variants of each, chosen with the seeded stream), so nothing on the main thread synthesises anything.
- `BedSource`: the bed as a live graph pulled from `OnAudioFilterRead` on the audio thread, with rain, wind, indoor and density handed over as plain numbers; and a few `EmitterSource`s, the same graphs on 3D sources,
  standing at the neon signs and rooftop units nearest the player (the prototype had two fixed offsets from the listener; the city has real signs and vents), and on the nearest cars, whose engine pitch and tyre hiss follow their speed.
- `ScoreSource`: the music. Its pad (six detuned oscillators retuned on each chord, a filter on a slow LFO), its sidechain duck and its dotted-eighth delay are live, like the bed; its notes are not: a worker thread
  renders each note a little ahead of its beat (about a second) with the same offline renderer as the one-shots and queues it with a start time in samples, so the audio thread only mixes. Intensity changes are
  quantised to the bar anyway, so the lookahead costs nothing audible.
- `Foley` (the player's steps, jump, land, breath: stride timing from the motor, surface from where the feet are) and `ActorSounds` (footsteps from the nearest walkers, on their stride phase).
- `VoicePool` of pooled `AudioSource`s with the prototype's rolloff (a custom curve, WebAudio's inverse model with each sound's reference distance), occlusion lowpass, and the six mixer groups.
- The mixer itself (`Cypher.mixer`: seven groups per bus by reverb-send tier, a reverb return, exposed volumes and reverb settings), written by `AudioMixerWriter`; the master glue, limiter and volume are code on the listener (`MasterStage`); `SettingsService.Apply` sets the volumes; `UiRoot` and the controls raise a
  `UiSound` for each interaction; `GameFlow` says when to duck.

## Decisions I will make without asking (say if you disagree)

- The built-in spatialiser pans and attenuates but has no head model (the prototype's HRTF), and Unity has no convolution reverb: the five spaces become five settings of Unity's own reverb, blended as you move. It will not
  sound identical to the prototype; it will ring the same way (an alley rings, a plaza is long and wide, indoors is dead).
- Voices: 24 spatial and 16 flat (the prototype's 36 and 44 are more than Unity will really play), stolen by priority then age as the prototype does.
- The mixer is written as text (an editor menu item, `Cypher > Write Audio Mixer`, which the scene builder also runs), with no reflection into the editor's internals. **Spike done, it works** (see "What the spike found").
- Sample rate and buffer size are the project's (system rate, 1024); the bed runs at whatever rate the device gives.
- `--no-audio` turns it all off, as a measuring control, and tests that need a sound device skip themselves where there is none (batch mode has none).
- I cannot listen. What I can check is level, spectrum and envelope against the prototype's own renders; so there is a tool that writes every sound, and a minute of the bed at three weather settings, as WAV files you can play.

## What the spike found (2026-10-23)

The hand-written `Cypher.mixer` loads and behaves, so there is no fallback to buses in code. Measured at the listener, in batch mode (which has a real 48 kHz stereo device, so the audio tests run in the normal check):
- All 44 exposed parameters answer `SetFloat`/`GetFloat`. A group's volume is exact: -20 dB heard is 0.1005 of the level.
- A mixer group has one send level, but the prototype gives each sound its own (0.05 for a menu click, 0.9 for a siren). So **every bus is a family of seven groups**: dry, then six that send to the reverb at -24, -18, -13, -9, -5 and -1.5 dB. A sound plays through the tier nearest the send it asks for (every send in `Bank.js` is within 3 dB of a tier). The bus sliders drive all seven of a bus together.
- The reverb is Unity's "SFX Reverb" on a return group (Receive, reverb, fader, in that order so the fader governs the return); its decay, room, reflections and so on are exposed, so the five spaces are five sets of numbers. Heard: the tail lengthens with the decay time, rises with the tier by the stated decibels, and the return fader moves it by the stated decibels.
- **Unity's compressor is a hard limiter at its threshold** (a ratio not set by us), nothing like the prototype's 3:1 glue stage. So the master dynamics are written in code (the glue and the limiter, then the master volume, after the limiter as in the prototype) and run on the listener, which hears the finished mix after the mixer; the mixer has no compressor.
- A mixer allowed to suspend its idle groups lost the first 20 ms of the next sound (a click's attack); suspending is off.
- `AudioReverbZone` and `reverbZoneMix` add nothing to a mixer-routed source, so they are not used.
- Not yet known: how loud the reverb sits against the prototype's convolution (the Room and Reverb levels and the return gain are tuned against its renders in step 6).

## Questions (answered 2026-10-23)

Answers: **1** live synthesis for the bed; **2** all three extras: the player's foley, sounds from the real traffic and people, and the music score; **3** silent on the title screen but for the menu sounds.

1. **The bed: live or pre-rendered?** Live synthesis on the audio thread is a small, steady cost, reacts instantly to weather and indoors, never loops, and uses no memory. Pre-rendered loops are what the milestone plan
   sketched: simpler at run time, but each layer is a finite loop (so the bed does repeat, hidden by several loops of different lengths), weather only changes levels, not the filters, and it costs 35 to 90 MB and a few
   seconds of rendering at load.
2. **What beyond the city's own sounds?** (The baseline is the bed, the six one-shots, the ten UI sounds and the emitters at signs and vents.) The player's own foley is footsteps on six surfaces, jump, land, breathing and
   cloth, about ten recipes plus the surface choice and the stride timing. Sounds from the real traffic and people are new, not in the prototype: engine and tyre sound from the nearest cars, with pitch following speed, and
   footsteps from the nearest walkers, in place of the prototype's random car passes and voices. The music score is the prototype's generative synthwave (about 300 lines of its own, tempo fixed, intensity following events); it
   roughly doubles this step, and with no combat to drive it would only ever play its calm side.
3. **The title screen.** The bed under the title (rain, hum, traffic wash) is cheap and makes the live city feel live; the crowd and the traffic were kept off it by your choice.

## Build order and tests

1. **Spike: the mixer.** Write `Cypher.mixer` from the builder (groups, exposed volumes, a reverb return with exposed settings), load it, set and read the parameters, route a source through a group. Decide (above) before anything depends on it.
2. **Core primitives and goldens.** The four noises are checked sample for sample against `Dsp.js`'s own (run headless in node, `tools/gen-golden-audio.mjs`); the biquad against analytic responses and the specification's
   edge cases; automation against hand-worked ramps; the wave tables against their Fourier definitions; the analysis against the prototype's on the same buffers.
3. **Recipes.** Each sound (the UI, ambient, foley and the music's instruments) rendered offline, dry and flat, and compared with the prototype's own render of it in a headless browser (`OfflineAudioContext`, the harness `tools/audio-check.mjs` already boots; read-only
   against the prototype): the same seed gives the same frequencies and timings, and the comparison is of level, band energies, centroid and the 20 ms envelope, not of samples (the browser's oscillators and filters
   are not bit-for-bit anyone else's). Checks that nothing is silent, clipped, DC, or runs off its length.
4. **Bed, clocks, mixer logic.** The weather mapping for rain, wind, indoor and density at their extremes; the clocks' gaps and stall resync with a seeded stream; bus and slider mapping, ducking, voice stealing, occlusion,
   the space probe's hysteresis; an hour of simulated bed with no NaN, no clipping, and a contour that does not repeat.
5. **Unity layer.** The assembly, the engine, the clip bank on workers, the bed, the emitters at signs and vents and on cars, the score, foley and walkers' footsteps, the pool, the mixer wiring, the Options sliders, the UI hooks, the flow ducking, silence on the title, `--no-audio`. Tests: the clip bank renders and is
   complete; the sliders reach the mixer; pausing ducks it; every UI interaction raises its sound; a PlayMode run starts the bed at the title and a one-shot plays and ends (where there is a device).
6. **Listen.** The WAV export tool; levels checked against the prototype's relative levels (the bed sits under the one-shots, the UI is audible over the bed); you listen and I retune the gains you ask for.
7. **Cost.** Audio-thread time per callback, main-thread time, memory, and the extra load time (the rendering is on workers beside the city build), written into `performance_results.md`.
8. `task.md`, memory, `-Step All` green.

Size: very large, about the work of steps 10 and 11 together (the music is most of the difference from the first estimate), plus the mixer spike. Risks: the mixer authoring (first, with a fallback); the audio thread glitching under load in a Mono Development build (measured in step 7; if it does, the bed is
baked instead and I say so); and Web Audio semantics I get subtly wrong, which the prototype-render comparison is there to catch.
