# Piece: the cabin (the pilot as a person in the ship)

The brief: the user's favourite idea (design section 6). Seated, the game is the flight view it always was. Hold a button and you stand up out of
the seat under artificial gravity, the HUD folds into the cabin's displays, and you walk a cramped, rough, utilitarian worn human hull with
dark, ribbed, glowing biomechanical grafts, lit by dim instrument glow, hard sun and starlight through the canopy, and red emergency light when
it is dark. Short physical actions, no puzzles, no inventory, no pausing: pull the main breaker and go dark (a scan finds a powered ship and misses
a dark one), knit a destroyed module back with your hands, choose two guns and two missile types at the weapon rack before launch, read the briefing
and a retro blip map at the plan table. Code: `Scripts/Cabin` (its own assembly; `Scripts/Cabin/README.md`), the game's side in
`Scripts/Game/Cabin` (the self-test) and `Scripts/Game/Mission/MissionScene.cs`. Art: `art_src/cabin`. Data: `Resources/Cabin`, `Resources/Missions/cabin.mission.json`.
The user judges by hand: `Playtest\Monolith_2026-10-24_cabin\` (`Play.bat`, CONTROLS.txt).

## Round 1 (builder, 2026-10-24)

### What was built
- **The cabin, by script in Blender** (`art_src/cabin/build_cabin.py`; textures `build_cabin_textures.py`): a hull 2.6 m wide and 3.8 m long on three floor levels
  (two steps up to the seat's dais), structural ribs every 0.44 m with panels between, a faceted canopy bubble of bio-ribbed frames over tinted glass, the pilot's
  seat, a dash ledge and two side consoles with screens, the plan table (a hexagonal pedestal with a ribbed collar round a flat display), the weapon rack on the port wall
  (two gun bays above two missile bays, each with a pull-out module and a label display), the main breaker on the starboard wall (a guarded slide switch, a lamp pair, a
  hazard band, the MAIN ON/OFF display), two hinged repair panels with a tissue-lined socket and a module that lies crooked until it is pressed home, hatch wheel, handrails,
  tied cable bundles, caged lamps, labels and placards. 38,000 triangles, 16 materials, textures at about a millimetre a pixel (42 MB of PNG). The pilot's hands (`build_pilot_hands.py`)
  are human but noticeably changed: fingers a sixth longer, ashen skin with darker banding, short dark horn nails, ridged tendons, in worn olive sleeves.
- **Standing, walking, looking** (`CabinSystem`): hold View (keyboard Z) for half a second and the eye rises 1.7 s out of the seat to 1.64 m (the HUD collapses toward the bottom
  of the screen into the displays, the eye opens slowly to the dim cabin); the left stick walks 1.25 m/s over the layout's floor levels (the plan is data: four levels, two
  obstacles, nine interactables with stand points), the right stick looks, bob and stride, one context button. The ship holds its last heading and speed (`CabinPilot`), nobody fires.
  Hold View at the seat to sit; double-tap View seated for the battle map on the seat's own display.
- **Interactions**, each a short physical action with the hands doing it: the **main breaker** (the lever slides down in 1.5 s; the HUD dies, the cabin goes black but for the
  emergency strips and the dark displays; restoring takes 2.4 s with the displays coming back one by one), a **scan** (a light crawls through the canopy for about 3.4 s,
  swinging 75 degrees, the hull creaks four seconds before, breathing quickens; detection is decided at the moment the sweep crosses: a powered ship in range is found, a
  dark one is missed unless it went dark less than 5 s ago), the **hand repair** (hold 5 s: the hatch opens, the module is pressed home in the glow of its own knitting, a hit
  makes the hands slip, letting go decays slowly; the part comes back at 25 percent health and the living tech mends the rest; any destroyed part of the player's ship needs one),
  the **weapon rack** (a bay's module is pulled out and the next type seated; feeds the ship's two gun slots and two missile types before launch, locked after), the **plan table**
  (briefing and debrief pages, and the same blip map the seat can switch to).
- **The mission hook:** `CabinSystem` implements `ICabin` (seated, powered, detected, needsRepair; scan, callOut, knockOut, cabinLights) for real. `CabinStub` stays for the AI-flown runs
  and the auto-plays, which are unchanged (the dogfight's event log is identical to the lead's verification run; the escort's differs only in a log word). A mission file's
  optional `cabin` block (`start: "table"`, `standAnytime`, the rack's weapon ids) is read by `MissionFile`.
- **The test mission** (`Resources/Missions/cabin.mission.json`, "Dead Water", from data alone): it starts at the plan table with the briefing on it and the rack open; launch; a quiet
  drift among the ice; a call to go dark (leave the seat, pull the breaker); Earth's picket corvette Lector arrives and sweeps for life signs: the sweep misses a dark ship and finds a
  powered one (being seen only changes what the radio says and how soon the drones come); then a hit takes the port engine, which only a hand repair brings back, while three drones come in.
  The mission ends when the drones are down and the engine is seated.
- **Rendering:** one HDRP light layer for the cabin (`supportLightLayers` on in the pipeline asset). The Sun is off that layer and replaced by a cabin-owned shadowed spot through the
  canopy, so nothing outside lights the cabin and the blasts outside only make the lights flicker. Every light and glow is set in screen units against the eye's exposure, so the cabin
  reads the same as the eye opens (EV 14 seated, 8 lit, 6.5 emergency, 7 dark, 5 blackout) and the world outside burns out or brightens. `SkyRig` takes the exposure shift and the
  ambient scale from `GameWorld.Cabin`: the sky's fill is cut to 4 percent while the pilot is up and compensated for the eye's opening (in a blackout it was eight times too bright
  before: the cabin read grey, not black).
- **Hands doing things:** two segmented arms with two-bone IK (`CabinHands`): rest low and apart, ride the breaker lever down, haul a rack module out, press a panel's module home with
  the other hand braced on the rim, tap the table. Low panels and low rack bays are `crouch` interactables: the pilot ducks when looking at them.

### Checks (the final build)
- `-mnCabinSelfTest`: **73 of 73** (the plan, the prefab, the hands, the pad and keyboard through virtual devices, seated control, standing, walking every stand point, the breaker, scans, repair, rack, table, and the whole test mission).
- `-mnMissionSelfTest` 61 of 61; auto-plays PASS: cabin (victory at 99.8 s), dogfight (defeat at 867.9 s, as always), escort (victory at 483.0 s).
- `tools/flight.ps1` 35 of 35 on all four classes (and the player's fighter); `-mnCombatSelfTest` 90 of 90; `-mnAiSelfTest` 31 of 31.
- Outputs in `gauntlet/captures/cabin/r1/checks/`.

### Performance (RTX 4070 Ti, 1920x1080, `-mnPerfSync`; `gauntlet/captures/cabin/r1/perf/`)
**This machine's desktop session is disconnected (remote/locked), so a presented frame is never rendered and the plain frame time and the GPU timer are nonsense** (the GPU
reading is hundredths of a millisecond). The numbers below are from `-mnPerfSync`, which renders into a target of its own and waits for the GPU to finish every frame: each frame
costs the CPU's and the GPU's work together with no overlap, so they read a little worse than a presented run, never better. The GPU field is sane (2.3 to 3.9 ms).

| Run | avg fps | median | p95 | p99 | 1% low | worst | GPU avg |
|---|---|---|---|---|---|---|---|
| Standing in the cabin (12 s, the plan table and rack, the displays live) | 182 | 4.7 ms | 11.3 ms | 12.2 ms | 66 fps | 42 ms | 3.9 ms |
| Seated in a mission with the cabin built (dogfight, AI flying, HUD) | 208 | 4.7 ms | 6.1 ms | 6.7 ms | 51 to 56 fps | 0.56 to 0.61 s once | 2.4 ms |
| The dogfight as a mission, no cabin | 194 to 200 | 4.9 ms | 7.2 ms | 11.5 ms | 41 to 46 fps | 0.56 to 0.61 s once | 2.4 ms |
| The dogfight scene's stress run, no cabin | 180 to 182 | 5.0 ms | 10.2 ms | 14.1 ms | 42 to 53 fps | 0.55 to 0.59 s once | 2.3 ms |

The cabin standing is well above 60 fps with headroom (p99 12 ms); building it costs nothing seated (the interior renderers and lights are off while the pilot sits). Both dogfight
runs, with or without a cabin, have one 0.55 to 0.6 s hitch about 20 to 23 s in (it logs right after the sky's star catalogue is built), which is all of their 1% low; it is not the cabin.
The first frame of a synced run (setting its target up) is dropped from the statistics.

### Captures (`gauntlet/captures/cabin/r1/`, HUD off unless named)
`seated.png` (the flight view is unchanged: stars and the canopy frame), `seated_hud.png` (with the HUD), `rising.png`, `standing.png` (fore, the dash and canopy), `standing_aft.png` (the
hatch and the breaker), `sitting.png`, `plan_table.png`, `weapon_rack.png` and `weapon_rack_swap.png`, `breaker_pull.png`, `dark_breaker_off.png`, `dark_scan_approach.png`,
`dark_scan_sweep.png` (the sweep's light crawling across the wall through the canopy frame), `mid_repair.png`. Plans: `gauntlet/shots/cabin_r1_*.json`.

### Known gaps and what is weakest
- **The art is a first look, not a judged one.** The hull's hard-edged surface detail (scratches, chips) reads as a repeating pattern at 30 cm from a wall; the hammered-metal normal
  on the rack's module caps and the bio ribs is too busy up close; the seat and the dash are simple boxes with chamfers; the hands' skin and sleeves are plain and their fingers stiff.
  No faces, no breath on glass, no particles. The cabin reads as a place from five metres and as a model from half a metre.
- **Reach is approximate.** The arm is 0.57 m: the stand points are chosen so grips are in reach, but the arms are posed by two-bone IK alone and are not bent by the geometry around them.
- **The first look at the rack and the breaker is close** (the cabin is 2.5 m wide); a pad player looks steeply down at the missile bays (the pilot crouches once looking at them).
- **No sound yet.** The events exist (`CabinEvents`: step, creak, breath, power, repair, lights) for the audio builder; nothing plays.
- **A hit while up** only flickers the lights and slips the repair; the hull has no damage state of its own (sparks, smoke, a cracked pane).
- **The plan table's blip map** is a first version (the player, wings, capitals, the next goal); no zoom.
- **Hardware the headless checks cannot show:** the real pad's feel (the dead zone, curves, the stand hold) is from the config and the keyboard, not from a hand on a pad.
- **`earth_corvette.prefab`, `QualitySettings.asset`** and other builders' files in the working tree were not touched by this piece and are not in its commit.

### Next
The user's playtest; the audio builder on the cabin's events; a second pass on the surface (the hull's detail at close range, the seat, the hands); the slice's Oort Cloud mission as a file
using the cabin's go-dark beat.
