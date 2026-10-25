# The start screen (`Monolith.StartScreen`, namespace `Monolith.Start`; the flow is `Game/Start/`)

A plain start (no mission, scene, capture or test flag on the command line) goes from the loading screen to a screen where the player chooses **Scenario** and **Difficulty**, then launches. No title screen, no
settings: the choice, and a way out. Drawn with the HUD's own renderer in whatever HUD style the player has (`HudRenderer.Menu.cs`: the plates with corner ticks, the headings with their rule and glyphs, the cell
bars, the two main gauges between the lists showing the chosen level and how hard Earth's guns hit you at it, the highlighted row boxed as the target is). F9 changes the style here too.

## What the player does
- Xbox pad: D-pad or left stick to move, **A** to choose, **B** to go back. Keyboard: arrows, **Enter**, **Esc**. F10 quits.
- **Scenario** (left list): the missions (every `*.mission.json` the game carries, and any in the `Missions` folder beside the game, read first, listed by the `name` written in the file: The Dogfight, The Oculus, The Crossing,
  Dead Water; others after them) and the sandboxes (Free flight, Combat range, Explosions sandbox), then Quit. A on a mission moves to Difficulty; A on a sandbox launches it (it has no difficulty).
- **Difficulty** (right list): Very easy, Easy, Medium, Hard, Insane (the AI's own table, `ai.json`), Medium to begin with. A launches.
- B goes back a list; from the scenarios it jumps to the Quit row, where B or A closes the game.
- The last scenario and difficulty are remembered in `start.json` beside the game (`{"scenario": "oort", "difficulty": "Hard"}`), the way `hud.json` remembers the HUD style. `-mnDifficulty Hard` on a plain start opens on that level.
- Under the gauges, two sentences of the highlighted mission's own briefing.

## How a start goes (`Game/Start/StartFlow`)
1. **Boot** (`LoadingFlow.RunBoot`): the splash and the bar while the sound decodes and the assets are read; no world (which scenario is not known yet). The start screen is built behind the loading screen, which fades off it.
2. **Launch** (`LoadingFlow.RunLaunch`): the loading screen comes up over the start screen, `GameRoot.BuildPlay` builds the chosen scenario with the clock held, the cabin cut, the sky and the cabin are warmed behind
   the screen, and it fades into the scenario. The choice is added to the command line for the run (`CommandLine.SetOverlay`: `-mnMission oort -mnDifficulty Hard`, or `-mnScene flight`), so every scene starts
   exactly as it does from a command line, and the real flags (`-mnSkipBriefing`, `-mnInvulnerable`, `-mnNoHud`...) still apply to whatever is launched.
3. **Back**: a mission's debrief (B), the pause screen's quit (B), a mission that fails to load, F10 in the explosions sandbox (which also has a button on its panel) all call `StartFlow.QuitOrMenu()`. In a run that
   came by the start screen that goes back to it; in a run that did not (a mission or scene on the command line) it closes the game as it always did. The pause, briefing and debrief screens say **MENU** instead of
   **QUIT** in a start-screen run. "Fly again" is unchanged.
4. **Taking a scenario down** (`GameRoot.TearDownPlay`): the start screen goes up first (its black camera covers the scenario), then the scenario's world is destroyed behind it: the sim driver, every child of the root,
   every top-level object made since the world began, the event listeners, the world and the clock, the audio system (its decoded library kept for the next one), the command line overlay, the clock's speed, the
   frame-rate settings; then the assets nothing uses are unloaded, twice, a few frames apart (the render pipeline lets go of a destroyed camera's history and the volume profiles it read a frame or two later). Meshes a
   canvas made (`HudLines.Mesh`) are now destroyed with their owners (the HUD, the cabin's displays and overlay, the loading and start screens): they were the one thing a scenario left behind (13 meshes a mission).

## Checks
`Monolith.exe [-batchmode -nographics] -mnStartSelfTest [-mnOut dir] [-mnStartTour oort:Hard:pause,flight::pause,dogfight:Easy:end,...] [-mnNoLoadingScreen]`:
- the logic with no devices (the listing by display name, a `Missions` folder beside the game that adds, replaces and has a broken file, the levels, every key, `start.json`, the flags a choice adds, which starts are plain);
- the real thing through a virtual Xbox pad and keyboard (D-pad, left stick and arrow keys in turn): choose, launch, check what is running is what was chosen, play a few seconds, return (the pause screen's quit, a mission's
  debrief, F10), and after every return take the numbers: no world, no audio system, the command line and the clock as they were; the same number of game objects, roots and event listeners as at the start; the same
  scenario twice builds the same world (sim systems, listeners); a scenario that has run before leaves nothing more behind than the trip before (objects, managed memory, allocated memory). At the end B goes to the quit row and A quits.
  Windowed it shows the loading screens and counts the audio voices (`-mnAudioMute` keeps it quiet). `-mnStartTour` sets the trips (`id:level:how`, how = `pause`, `end` or `f10`).
- `-mnHudSelfTest` draws the start screen at 8 screen shapes in 3 styles with 3 lists and 5 cursor positions (nothing off screen, no panel or word overlapping).
Capture scene `"scene": "start"` (`sceneOptions`: style, scenario, level, focus, fade); plans `gauntlet/shots/start_r1_*.json`, written by `python -I tools/start/make_plans.py`.

## Where things are
`StartMenu` (the choice: two lists, one cursor, no drawing), `StartScreen` (the component: its own black camera, the pad and keyboard, the HUD's pass), `StartSettings` (`start.json`); `HudUi/HudMenu.cs` and
`HudUi/HudRenderer.Menu.cs` (what is drawn); `Game/Start/StartCatalog` (what is listed, from the mission library and the AI table), `StartFlow`, `StartScreenSelfTest`, `StartTestWorld`; `Game/GameRoot.TearDownPlay`;
`Game/Loading/LoadingFlow` (the two halves).
