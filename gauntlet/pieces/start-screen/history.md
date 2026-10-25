# Piece: the start screen (choose a scenario and a difficulty, in the HUD's own look; and a scenario that goes away cleanly)

The user's note (2026-10-25), from a playtest: "I'd like the game to load straight into a screen where you select the options: Scenario / Difficulty - no point making a full title screen etc. at this point, we'll do that later,
but do make it use the HUD style and all." The user judges by eye, so this piece has no critic rounds. Code: `Scripts/StartScreen/` (README there), `Scripts/HudUi/HudMenu.cs` and `HudRenderer.Menu.cs` (what is drawn),
`Scripts/Game/Start/` (the flow, what is listed, the checks), `GameRoot.TearDownPlay`, `LoadingFlow.RunBoot` and `RunLaunch`. Tools: `tools/start/make_plans.py`.

## Round 1 (builder, 2026-10-25)

### What was built
- **A plain start** (no mission, scene, capture or test flag on the command line, not headless) goes from the loading screen to the start screen. A start with a mission or scene on the command line, every capture, every check and every
  auto-play skips it and does what it always did (a pause screen's B then closes the game, as before). `-mnNoStartScreen` turns it off; `-mnDifficulty Hard` opens it on Hard.
- **The screen**: Scenario on the left (the missions by the name written in each file: The Dogfight, The Oculus, The Crossing, Dead Water, then any in the `Missions` folder beside the game, which replace a built-in of the same
  name in its place; then Free flight, Combat range and the Explosions sandbox; then Quit), Difficulty on the right (Very easy, Easy, Medium, Hard, Insane: the AI's own table; Medium to begin with), and between them the
  HUD's two main gauges showing the chosen level and the damage to you it means (25% to 100%, from `ai.json`). Two sentences of the highlighted mission's own briefing under them, the keys under that.
  Xbox pad first (D-pad or left stick to move, A to choose, B back), the keyboard too (arrows, Enter, Esc, F10 quits). A on a mission moves to Difficulty, A there launches; A on a sandbox launches it (it has no difficulty, so the
  right list stands down with "MISSIONS ONLY"); B jumps to the Quit row and B or A there closes the game. The last scenario and difficulty are kept in `start.json` beside the game.
- **It is the HUD**, not a menu toolkit: drawn by `HudRenderer` (`DrawMenu`) from plain data (`HudMenu`, in `HudFrame.Menu`): the plates with corner ticks, the headings with their rule and glyphs, the HUD's cell bars, its gauge arcs,
  the target box for the highlighted row, its fonts and colours, in whichever of halo, meridian and cipher the player has (F9 cycles it here too and keeps it in `hud.json`). It sits on a black camera of its own.
- **Back to it**: a mission's debrief (B), the pause screen's quit (B), a mission that cannot load, the Explosions sandbox's F10 (and a button on its panel). In a start-screen run the screens say MENU where they said QUIT. Fly again is unchanged.
- **The playtest launcher**: `Play.bat` with no scenario word starts the screen (`Play.bat hard` opens it on Hard); every other word is kept and applies to whatever is launched (`god`, `skip`, `ai`...). `tools/audio/OPTIONS.txt` says so.

### Switching scenarios in one process: it is safe, and this is how it is made so
A scenario is launched by adding its flags to the command line for the run (`CommandLine.SetOverlay`: `-mnMission oort -mnDifficulty Hard`), so every scene builds exactly as it does from a command line. Going back, `GameRoot.TearDownPlay`
(behind the start screen, already up on its own camera so no frame is drawn without one): stops the clock's driver, keeps the decoded sound library for the next audio system, destroys everything under the root and every top-level
object made since the world began (found by comparing the roots with a list taken before the build), then drops the event listeners, the world and the clock, puts back the engine state a scene sets (clock speed, vsync, frame rate;
the cursor is the start screen's), and unloads what nothing uses, twice with frames between (the render pipeline lets go of a destroyed camera's history and volume profiles a frame or two later).
The one thing a scenario left behind was **a mesh for every canvas** (`HudLines.Mesh`, hidden and never destroyed: 13 a mission, one for each of the cabin's displays; the HUD, the overlay, the loading and start screens too). Fixed (`HudLines.Dispose`).
Everything else is destroyed with its object. What stays on purpose: the assets the loading screen preloaded, and the game's own caches (the flipbooks, the hull sections, the cabin's cut pieces, the fonts).

Proof, `-mnStartSelfTest`, windowed at 1920x1080 through the real loading screens and the audio system (`-mnAudioMute`), three round trips: The Oculus (Hard), The Dogfight (Medium; ended through its debrief), The Oculus again (`windowed_3trips`):

| at the start screen | boot, before any scenario | back from trip 1 | back from trip 2 | back from trip 3 |
|---|---|---|---|---|
| game objects / roots / event listeners | 3 / 4 / 0 | 3 / 4 / 0 | 3 / 4 / 0 | 3 / 4 / 0 |
| audio system / world / clock | none | none | none | none |
| Unity objects | 8,580 | 8,784 (+204) | 8,840 (+56) | 8,840 (+0) |
| managed memory in use | 249.6 MB | 264.6 | 265.2 | 265.7 |
| allocated memory | 178.1 MB | 315.2 | 349.1 | 349.2 |

and during each trip: the Oculus 14 sim systems, 69 event listeners, 5 audio voices at most; the Dogfight 14, 69, 4; the Oculus again **14, 69, 5** (the same world built twice). At the start screen no voices at all.
Read it honestly: the game objects, roots, listeners, audio system, world and clock are back to the baseline exactly, every time. Memory is not back to the figure at the first start screen, and cannot be: the first world a run builds
leaves the caches the game keeps on purpose (+204 objects: 117 meshes and 79 materials, the cabin's cut pieces and effects) and the render pipeline's buffers (+137 MB allocated, +15 MB managed), and a second kind of scenario adds
its own once (the Dogfight: +56 objects, +34 MB). What matters is that it does not grow: the third trip leaves exactly what the second did (+0 objects, +0.1 MB allocated, +0.5 MB managed). Six identical round trips (`windowed_oort6`): objects +0 on every return after the first;
managed memory 264.5, 265.3, 265.5, 265.6, 265.6, 265.7 MB (+1.2 MB over the five that follow the first, flattening); allocated 315.5, 315.8, 316.0, 316.0, 316.0, 316.0 MB (+0.5 MB in all). The managed figure moves a few MB from one process to the next (the first start screen read 249.6 MB in one run and 249.7 in another, the first return 264.5 and 272.5), so the figures to compare are the ones within one run. Headless, eight scenarios in a row (`headless`: the Oculus, Free flight, the Dogfight, the Combat range, the Explosions sandbox,
Dead Water, the Crossing, the Oculus again): 3 game objects, 2 roots and 0 listeners after every return; the Oculus built again is 13 sim systems and 37 listeners both times; objects flat after the first of each kind (+0 on the last two).
Not measured: the graphics driver's own memory (the counter reads 0 on this machine), and audio voices headless (a headless run makes no sound, so it has no audio system).

### Numbers from the loading screen's two halves (windowed, 1920x1080, RTX 4070 Ti)
Boot to the start screen: **2.4 s** after the game's code could draw (start 0, ships 439 ms, cabin 44, effects 199, sky 35, sound 130; the sound cache from earlier runs is warm). A scenario behind the loading screen: the Oculus first time
**3.4 s** (world 1.04 s, the cabin's cut 1.48 s, then 0.14 s of warm-up), later ones **1.9 s** (world 0.73 s, cut 0.30 s).

### Checks (this build, `gauntlet/captures/start_checks/`, `gauntlet/captures/start_screen/selftests/`)
- `-mnStartSelfTest`: **109 of 109** headless (the logic with no devices: the listing by display name with a `Missions` folder that adds, replaces and has a broken file; the levels; every key; `start.json` good, broken and stale; the flags a choice adds; which starts are plain; then eight scenarios in a
  row through a virtual pad (D-pad, left stick and arrow keys in turn), each launched, checked to be what was chosen, left by the pause screen, a debrief or F10, and checked to be gone; then B to the Quit row and A to quit), **65 of 65** windowed for the three round trips, **102 of 102** windowed for the six identical ones, 58 of 58 windowed with no loading screens at 1280x720.
- `-mnHudSelfTest` **125 of 125** (the start screen drawn at 8 screen shapes x 3 styles x 3 lists x 5 cursors: nothing off screen, no two panels or words overlapping, nothing under 8.5 px, a gap between the level's name and the line under it).
- `-mnLoadingSelfTest` 55/55, `-mnMissionSelfTest` 81/81, `tools\mission.ps1` all four missions pass on Medium with events.csv and pacing.csv byte-identical to the build at main 0e9ad06, `-mnAiSelfTest` 39/39, `-mnCombatSelfTest` 98/98,
  `-mnCabinSelfTest` 75/75, `-mnAudioSelfTest` 150 checks pass, `-mnScene range -mnPadDrive` 42/42.
- Captures of the screen in three styles and four states (the first mission, the level list, a sandbox, quit) and at 21:9, 4:3 and 5:4: `gauntlet/captures/start_screen/r1/<style>/`, side by side in `.../r1/sheets/`. Real windows grabbed while a windowed run went through the whole flow: `gauntlet/captures/start_screen/window/`.

### Found on the way
- The type's small-screen floor and the new HUD scale disagreed (see the HUD history, round 2): the same layout check that guards the HUD found it.
- A plain start used to reach the smoke world (a sphere and a box); nothing relied on that, and `-mnNoStartScreen` keeps it for anyone who does.
- `loading_stats.json` was written whole by each load; with two halves each would have erased the other's steps, so a load now merges its steps into the file.

### Known gaps
- The screen is black with the HUD on it: no picture behind it, no title art, no sound (there is no audio system until a scenario is launched). Keys have no click. Deliberate: "no point making a full title screen".
- The mouse does nothing on it.
- Coming back, the screen cuts in from the scenario (its contents fade in over 0.35 s on black) instead of fading from the last frame.
- The Explosions sandbox has no pad shortcut back: its panel (Start) has a button, F10 does it on the keyboard.
- The mission blurb is the first two sentences of the briefing's first stage; a mission file with no briefing shows nothing there.
- Teardown finds what a scenario made at the top level by comparing roots, so anything a future system parks in `DontDestroyOnLoad` with a static pointing at it would outlive a scenario; the check counts game objects, roots and listeners after every return and would say so.
- The memory figures above are one machine and one session. A longer run (many round trips of every scenario) was not soaked.
