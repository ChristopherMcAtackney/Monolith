# Cabin (`Monolith.Cabin`)

The pilot as a person in the ship. Seated, the game is the flight view it always was. Hold View (keyboard: hold Z) and the eye rises out of the
seat, the HUD folds into the cabin's own displays, the left stick walks, the right stick looks and **one** context button does the physical thing
you are looking at. Nothing pauses, there is no inventory and no puzzle: a pull, a hold, a turn of a page. While the pilot is up the ship holds
its last heading and speed (`CabinPilot`), nobody fires, and the fight goes on outside.

The cabin is on in every played mission (a pad, or a scripted cabin in a capture plan); the AI-flown runs and `-mnNoCabin` leave it out and the
mission gets `CabinStub` instead (`Scripts/Mission/MissionHost.cs`), so headless checks and the auto-plays do not change.

## What you can do

| Where | What | Feel |
|---|---|---|
| The seat | hold View to stand (half a second, so a stray press does nothing), hold it again at the seat to sit | the view rises 1.7 s, the HUD collapses toward the bottom of the screen into the displays; the eye opens slowly to the dim cabin |
| The main breaker (starboard, chest high) | one press: the lever slides down in 1.5 s, the HUD dies, the cabin goes black but for the red emergency strips; again to restore (2.4 s, the displays come back one by one) | `powered` is false while it is off; the ship drifts on its last heading and speed |
| Dark, a scan crossing | the sweep's light crawls through the canopy for about 3.4 s, swinging 75 degrees, with a hull creak four seconds before and breathing | a **powered** ship in range is found; a dark one is missed, unless it went dark less than 5 s ago (still warm) |
| A repair panel (aft starboard "engine", port "arms") | hold the button: the hatch opens, the module lies crooked in its socket, the module presses itself home in 5 s while it glows; a hit makes the repair slip; let go and the progress decays slowly | any destroyed player part needs a hand repair; it comes back at 25 percent health and the living tech mends the rest |
| The weapon rack (port wall) | press at a bay: two gun bays and two missile bays, the module is pulled out and the next type is seated (1.1 s) | feeds the loadout (`Loadout`): the ship's fast and heavy gun slots and its two missile types; locked after launch |
| The plan table | press to turn pages, B to step away | briefing and debrief pages, and the retro blip map of the same battle the seat's map shows (double-tap View seated) |

**There are no hands.** The pilot is a camera: nothing of the body is drawn (user, 2026-10-24: "unnecessary and awful looking"). Every interaction shows on the cabin itself, which animates on its own: the
lever swings down, the hatch opens and the module seats itself, the rack bay slides out, the table turns its page. No arms, no reach poses, no IK, and the pilot no longer ducks to a low panel.

**Looking round is inverted by default** (user, 2026-10-24): push the right stick up (or press the up arrow) and the view goes down; pull back and it looks up. `controls.json` `invert.cabinLook` (and `invert.flight` for flying)
turns it round; the pause screen flips both (LB flight, RB cabin look, or I and O) and saves them back to the controls file.

## Parts

| File | Role |
|---|---|
| `CabinSystem` (+ `.Interact`, `.Mission`) | the logic, pure data and maths, steppable headless: `Advance(dt, now)`. Registered on the sim (`ISimSystem`), or stepped by the rig on its own clock before launch while the sim is held. Walking on the layout's floor levels, the stand/sit state machine, the interactions, the scans, the exposure and lights state. **Implements `ICabin`** for the mission runtime (`seated`, `powered`, `detected`, `needsRepair`; `scan`, `callOut`, `knockOut`, `cabinLights`) |
| `CabinBoard` (`Core/CabinBoard.cs`, `GameWorld.Cabin`) | what the rest of the game reads: phase, eye pose, FOV, exposure shift, ambient scale, HUD collapse, prompts, scan light. `CabinEvents` carries the sound hooks (`Step`, `Creak`, `Breath`, `Power`, `Repair`, `Lights`) for the audio builder |
| `CabinLayout` | `Resources/Cabin/cabin.layout.json` (written by the model script): floor levels, obstacles, interactables with their stand points, displays, lights |
| `CabinConfig` | `Resources/Cabin/cabin.json`: every number (walk speed, eye heights, timings, exposure per light state, light units, scan, rack, controls). A playtest build reads `cabin.json` next to `Play.bat` first (`-mnCabin <file>`) |
| `CabinPilot` | wraps the player's pilot source: standing or dark, it holds heading and the current speed |
| `CabinInput` | `PadCabinInput` (pad and keyboard, from `cabin.json`'s `controls`) and `ScriptedCabinInput` (a JSON script: `stand`, `sit`, `walk` (`to`, `via`), `face` (`at`, `yaw`, `pitch`), `press`, `use`, `back`, `map`, `launch`, `wait`, each with an optional `t` start time) for captures and checks |
| `CabinRig` | the MonoBehaviour that presents it: builds the prefab, the lights and the displays, follows the ship, drives the moving parts (lever, panel doors and modules, rack modules) and the emissives |
| `CabinLights`, `CabinDisplays`, `CabinOverlay`, `BlipMap`, `Loadout` | the lights, the display render textures (drawn with `HudLines` and `MN_Lines`), the prompts, the table's map, the rack's choice |

`Game/Cabin/CabinSelfTest.cs` is `-mnCabinSelfTest` (below). `Game/Mission/MissionScene.cs` builds the cabin (`MakeCabin`), wraps the pilot,
holds the sim while a mission starts at the plan table, and passes the cabin to `MissionRuntime` as its `ICabin`.

## How it looks and why

- **One rendering layer.** Every cabin renderer and light is on HDRP light layer 1 (`supportLightLayers` is on in `Monolith_HDRP`; `Hdrp.cs`
  sets it). The Sun is kept off that layer and replaced by a cabin-owned shadowed spot (the Sun through the canopy's tinted panes, cast through
  the canopy frame), so nothing outside lights the cabin and nothing inside lights the world: blasts and hits make the lights flicker instead.
- **Light in screen units.** Every light and glow is set as how bright it should read on screen (1.0 is white at the eye's exposure) and
  converted with `NitsPerUnit = 1.2 * 2^EV`, so the cabin reads the same as the eye opens (exposure 14 seated, 8 lit, 6.5 emergency, 7 dark, 5
  blackout) and only the world outside burns out or brightens. The sky's own fill is absolute light: `SkyRig` takes `ExposureShift` and
  `AmbientScale` from the board, and `CabinSystem.Mission` holds the fill at 4 percent while up and compensates for the eye's opening so a
  blackout is black (25 percent of that fill remains at the darkest).
- **Lit by:** dim instrument glow (amber lamps, teal displays, the glyph strips of the grafted tech), hard sun and starlight through the
  canopy, red emergency strips when dark, the scan beam, and a teal glow from a module being knitted.
- **Displays** are render textures drawn each frame on emissive instances of one editor-made material (`MN_CabScreen`); the seat's HUD folds into
  them (`HudCollapse`, `_MN_Dim` in `MN_Lines`).

Art: `art_src/cabin/` (see `art_src/PIPELINE.md`, "The cabin"). Soyuz, Apollo and ISS for the cramped, rough, utilitarian worn human hull; the dark,
ribbed, glowing biomechanical grafts of the Independence Day alien craft on top.

## Mission wiring

A mission file's optional `cabin` block (`MissionFile.CabinDef`): `"start": "table"` makes the mission begin at the plan table with the sim held
(briefing on the table, rack open, **Launch** from the seat), `"standAnytime": false` stops the pilot getting up unless the mission calls for it,
`"rack": { "guns": [...], "missiles": [...], "defaultGuns": [...], "defaultMissiles": [...] }` offers weapon ids from `Resources/Combat/weapons.json`.
`Resources/Missions/cabin.mission.json` ("Dead Water") is the test mission, made from data alone.

## Checks

- `Monolith.exe -batchmode -nographics -mnCabinSelfTest`: 73 checks. The plan and the prefab (every display and moving part the code binds
  is in the model, every renderer is on the cabin layer), no hand model in the build, the pad and keyboard mapping through virtual devices (the look inverted by default, and not when the switch is off), seated and standing control (the ship holds heading and speed, nobody fires,
  the HUD collapses, the eye opens), walking (every stand point reachable, never off the floor), the breaker, each scan outcome, the hand repair
  (hold, decay, slip, completion at 25 percent health), the rack feeding the loadout, the plan table, and the whole test mission played with the
  real cabin and a scripted pilot (dark when swept and missed, hit, repaired, back in the seat; if the drones break the engine again the pilot
  repairs it again).
- `tools\mission.ps1` auto-plays every mission with the stub (the cabin mission included), unchanged.
- Captures: `gauntlet\shots\cabin_r1_*.json` (`sceneOptions.cabin`: `script`, `prelaunch`).
  Perf: add `-mnPerfSync` (see `tools/PIPELINE.md`).

Flags: `-mnCabin <cabin.json>`, `-mnNoCabin`, `-mnCabinSelfTest`, `-mnCabinNoDisplays` and `-mnCabinNoShadows` (diagnostics for the frame cost).
