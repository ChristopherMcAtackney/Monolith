# Pipeline: edit, compile, build, capture

Everything runs headless from the repo root `C:\Users\chris\Monolith`. Every command works the same from PowerShell
and bash. Never start Unity any other way: `tools\unity.ps1` holds the project lock, and nobody opens the editor.

| Step | Command | Typical time |
|---|---|---|
| Compile check | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 -CompileOnly` | 6 s warm, ~50 s after a package change |
| Project set-up (idempotent) | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 Monolith.EditorTools.Setup.Project` | 7 s |
| Build player | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 Monolith.EditorTools.Build.Player` | 12 s after a script edit, ~90 s cold (HDRP shaders) |
| Capture a plan | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture.ps1 -Plan gauntlet\shots\smoke.json -Out gauntlet\captures\smoke\r1` | 4 s boot + 0.3 s per still + the perf seconds |
| Flight traces + FS2 compare | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\flight.ps1 -Out gauntlet\captures\flight-model\rN\traces` | ~10 s for five classes |
| Blender script | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\blender.ps1 art_src\Viper\build_viper.py` | 2-5 s + your work |

The whole loop is **edit, Build.Player, capture** (~30 s); the build compiles too. Use `-CompileOnly` straight after
writing files. Fresh clone: run `-CompileOnly` (imports packages), then `Setup.Project`, then `Build.Player`.

## unity.ps1

```
tools\unity.ps1 -CompileOnly
tools\unity.ps1 <Namespace.Class.Method> [-TimeoutMin 45] [-NoGraphics] [-Full] [-- -mnYourArg value ...]
```
- Waits for the `unity` lock (`tools\.locks\unity.lock` holds the holder's PID). Stale locks and orphaned batch Unity
  processes are cleared automatically; a Unity Editor GUI open on `Game\` blocks it with a message.
- Summary: exit code, each `error CS` with file(line,col), exceptions, `[MN]` lines, the build result. The full log path
  is printed (`Game\Logs\unity_*.log`, last 40 kept).
- Exit codes: Unity's own (1 on compile errors or a thrown editor method), 2 = compile errors with Unity exiting 0,
  3 = lock timeout or an editor GUI is open, 4 = timed out (killed).
- Editor entry points live in `Game\Assets\Monolith\Editor` (namespace `Monolith.EditorTools`). Wrap the body in
  `Batch.Run("Name", () => {...})`: it logs `[MN] Name OK`, saves assets, and exits 1 on any exception. Read extra
  arguments with `Monolith.Core.CommandLine.Get("-mnYourArg")`.
- Built in: `Setup.Project` (HDRP asset and global settings, linear colour, Input System only, player settings, smoke
  materials, the Boot scene as the only scene) | `Setup.All` (Setup.Project + Build.Player in one session) |
  `Build.Player` (`-- -mnDev` for a development build) writes `Game\Builds\Win\Monolith.exe`.

## Capture plans (`gauntlet\shots\*.json`)

```json
{ "defaults": { "width": 1920, "height": 1080, "warmup": 1.0, "perfWarmup": 2.0 },
  "stills": [ { "name": "front", "pos": [0, 1.6, -9], "rot": [9, 0, 0], "fov": 40 },
              { "name": "side",  "pos": [-7, 1.2, -5.5], "lookAt": [0, 0, 0], "warmup": 2.0, "width": 1280, "height": 720 } ],
  "perf":   [ { "name": "smoke", "seconds": 10, "pos": [0, 1.6, -9], "rot": [9, 0, 0] } ] }
```
- A still: camera `pos` and `rot` (Euler degrees) or `lookAt`, `fov` (vertical degrees), `warmup` (game seconds run before
  the shot), `width`/`height`, `seed`. A missing camera field keeps the scene camera's own value. Names become file names.
  A misspelt field aborts the run.
- A perf run: the same camera fields plus `seconds` (real time, after `warmup`/`perfWarmup`). It plays in real time on a
  1920x1080 window with vsync off.
- Stills render offscreen at the exact resolution with game time fixed at one 60 Hz tick per frame from the first frame, so
  a rerun takes each shot on the same tick and matches to within 1 LSB in a handful of pixels (HDRP dithering). `-Only`
  changes how many ticks pass before a later shot, so a moving scene differs. The window shows black while capturing.
- Output in `-Out` (default `gauntlet\captures\<plan>_<stamp>\`, git-ignored): `<still>.png`, `perf_<run>.json`,
  `capture_manifest.json`, `player.log`. A perf JSON holds `avgFps`, `low1PctFps`, `worstFrameMs`, p50/p95/p99 ms,
  GPU and CPU ms, `simTicks` and `simHzMeasured` (should read ~60), and the GPU name.
- Every capture starts with `CoreSelfTest` (clock rate and order, events, pilots); its result is in the log and manifest.
- `capture.ps1` options: `-Only a,b` (some shots), `-Kind stills|perf`, `-Out`, `-Width/-Height` (window), `-TimeoutMin 15`,
  `-Exe`. Exit codes: 0 ok, 1 a shot or the self-test failed, 2 bad plan, 3 lock timeout, 4 timed out, 5 no build.
  A plan with perf runs also waits for the `unity` lock so a build cannot skew the numbers; builds hold the `player` lock.
- Smoke-plan baseline (RTX 4070 Ti, 1920x1080): about 1,080 fps average, 1% low about 540 fps, GPU 0.7 ms.

- `"follow": true` on a still or perf run shoots through the game's own camera wherever the game moves it (the cockpit
  flying a demo route); it cannot also give `pos`, `rot` or `lookAt`. `gauntlet\shots\flight_playtest.json` is the example:
  `"scene": "flight"` with `"sceneOptions": { "pilot": "demo" }` flies the player's ship on the demo route in
  `Resources/Flight/playtest.json`, and the shots are taken at its game times.

Player flags: `-mnCapture plan.json -mnOut dir [-mnOnly a,b] [-mnKind stills|perf] [-mnLogEvents]`.
Flight: `-mnScene flight` (the playtest; `Play.bat` in a playtest folder passes it), `-mnShip <class>`, `-mnPilot demo`,
`-mnControls file`, `-mnFps n` (cap the frame rate to check smoothness), and
`-batchmode -nographics -mnFlightScenarios gauntlet\shots\flight_scenarios.json -mnShip "GTF Ulysses" -mnOut dir`
(traces only; `tools\flight.ps1` runs it for every class, then `tools\fs2ref\compare.py`, and also runs the flight self-test).

Juice sandbox: `-mnScene juice` (the panel is Start; settings save to `juice.json` next to the exe, or `-mnJuice file`). A plan with
`"scene": "juice"` and `"sceneOptions": { "script": [ {"t": 3.0, "do": "event", "name": "capital_death"} ] }` drives it (steps are
documented in `Scripts/Game/Sandbox/SandboxScript.cs`); `python tools\juice\make_plan.py spec.json plan.json` turns absolute shot
times into the plan's warm-ups, `tools\juice\sheet.py` and `clip.py` make contact sheets and clips from the stills. Check flags:
`-mnJuiceSelfTest`, `-mnPanelDrive`, `-mnRumbleTest`, `-mnDumpSounds dir`, `-mnPanelShot file.png`, `-mnShakeLog file.csv`,
`-mnJuiceHitchLog`, `-mnAudioMute`. Captures make no sound and never write `juice.json`.

Combat range: `-mnScene range` (Start pauses and lists the controls; `-mnControls file`). A plan with `"scene": "range"` and
`"sceneOptions": { "pilot": "scripted", "script": [...] }` plays the player's side through the same `ControlInput` as the pad
(steps: place, face, facePart, input, fire, heavy, missile, decoy, cycle, select, target [part], aim lead|target with autoFire and holdRange,
power, menu, damage, log, expect, summary; documented in `Scripts/Game/Range/RangeScript.cs`; `trace` logs the key events). Other sceneOptions:
`noEmplacements`, `emplacements: ["Rack 1"]`, `noTurrets`, `noRespawn`, `hullScale`, `hud: false`, `juice: {...}`. `python tools\juice\make_plan.py spec plan`
turns absolute times into warm-ups for this scene too (specs in `gauntlet\shots\specs\combat_r1_*.json`). Checks:
`Monolith.exe -batchmode -nographics -mnCombatSelfTest` (the combat systems on the real sim, 90 checks), `-mnScene range -mnPadDrive` (a virtual pad
presses every combat control, 18 checks), `python -I tools\combat\check_weapons.py [--out file]` (every number copied from FS2 against FS2's own tables).
The weapons, damage and AI contract are in `Scripts/Weapons/README.md`.

Dogfight: `-mnScene dogfight` (`Play.bat` in a playtest folder passes it; `-mnDifficulty "Very easy"|Easy|Medium|Hard|Insane` or 0..4, `-mnAutopilot`
the AI flies your ship, `-mnInvulnerable`, `-mnPacingLog file.csv` writes the pacing when a run ends). A plan with `"scene": "dogfight"` takes
`sceneOptions` `pilot: "ai"`, `difficulty`, `invulnerable`, `hud`, `dust` (false: no cockpit motion dust, for look shots), `stress` (the first three
waves and the bomber wing at once, uncapped: the perf run), `lod0`, `juice`, and `spectate: { "ship": callsign, "offset": [x,y,z], "world": false, "look":
"target"|"ship"|callsign }` (a capture-only camera riding another ship, used with `"follow": true` shots; never the player's ship). Plans:
`gauntlet/shots/ai_r1_*.json`. AI checks: `Monolith.exe -batchmode -nographics -mnAiSelfTest` (37 checks), `-mnAiArena gauntlet/shots/ai_arena.json
-mnOut dir [-mnArenaTrace dir]` (AI against AI: wins, kills, time on tail, hit rates by skill, knob sweeps; arena.txt and arena.json),
`-mnDogfightSoak seconds -mnOut dir [-mnDifficulty x] [-mnSoakMortal] [-mnSoakTrace]` (the dogfight bare, the AI flying an invulnerable player
ship, loops back to back: stuck ships, hull collisions, errors, pacing per loop, what hurt the Mercy and the wingmen; exit 0 on pass),
`-mnDogfightSoak 0 -mnWardenRun n -mnOut dir [-mnDifficulty x] [-mnNoFighters] [-mnWingOrder cover|attack|none] [-mnRunSeconds 300]` (the
capital-ship balance check: a mortal AI-flown player strips the Warden's turrets; time alive, time within 1 km, turrets destroyed, turrets and
shots on it as the turrets go; warden_runs.txt and .json). Capture stills with `pos`/`rot`/`lookAt` are now
pinned after every LateUpdate as well as before rendering (HDRP takes the camera's matrices before `beginCameraRendering`, so in scenes with the
cockpit camera those shots used to come out from the cockpit).

Missions: `-mnMission <name or path>` plays any mission file (`Resources/Missions/<name>.mission.json`, or `Missions/<name>.mission.json` beside
the exe, read first; `Play.bat <name>` in a playtest folder passes it), with `-mnDifficulty`, `-mnAutopilot`, `-mnInvulnerable`, `-mnSkipBriefing`,
`-mnPacingLog dir` (events.csv and pacing.csv when a run ends). A plan with `"scene": "mission"` takes `sceneOptions` `mission`, `pilot: "ai"`,
`difficulty`, `invulnerable`, `hud`, `dust`, `juice`, `lod0` and `spectate` (as the dogfight's); plans `gauntlet/shots/mission_r1_*.json`. Checks:
`Monolith.exe -batchmode -nographics -mnMissionSelfTest` (the format, every condition and action, the dogfight's port), `-mnMissionAutoPlay <name>
-mnOut dir [-mnSeconds n] [-mnDifficulty x] [-mnMortal]` (the AI flies the player; the mission's `autoplay.expect` is asserted against its event
log and pacing; exit 0 on pass), `-mnDogfightPortCheck seconds -mnOut dir` (the hand-written dogfight against `dogfight.mission.json`, every
ship's state compared to the bit once a second). `tools\mission.ps1 -Out dir` runs the self-test and every mission's auto-play. The format,
every condition and action: `Scripts/Mission/README.md`.

Cabin: the pilot as a person in the ship (`Scripts/Cabin/README.md`), on in every played mission. `-mnCabinSelfTest` (73 checks: the plan and prefab, the hands, the pad and keyboard through virtual devices, standing and
sitting, walking, the breaker, scans, the hand repair, the rack, the plan table, and `cabin.mission.json` played with the real cabin and a scripted pilot) from `Monolith.exe -batchmode -nographics`.
Flags: `-mnNoCabin` (the mission gets the stub), `-mnCabin <file>` (a `cabin.json` to read instead of the built-in: a playtest build reads the one next to `Play.bat`),
`-mnHandsShowcase` (the hands held up before the eye, for looking at them), `-mnCabinNoDisplays` and `-mnCabinNoShadows` (leave those out, to see what a frame loses).
A mission plan takes `sceneOptions.cabin`: `{ "prelaunch": true, "script": [ ... ] }` where `prelaunch` starts at the plan table with the sim held, and the script is a list of steps
(`stand`, `sit`, `walk` with `to` (an interactable id or `[x, z]`) and optional `via` points, `face` with `at` or `yaw` / `pitch`, `press`, `use` with `seconds`, `back`, `map`, `launch`,
`wait` with `seconds`), each with an optional `t` (do not start before that many seconds); `"cabin": false` leaves the cabin out. A `follow` still then shows the cabin's own camera.
Plans: `gauntlet/shots/cabin_r1_*.json` (the stand and sit, the whole mission walk-through, the seated HUD, the perf runs), `cabin_hands.json`, `cabin_bench_table.json`.

Perf on a machine whose desktop session is disconnected (remote, locked, or a console session closed): the window's frames are never rendered or presented, so the plain frame
time is only the CPU's submit time and the GPU timer reads nonsense (a hundredth of a millisecond). `-mnPerfSync` (a player flag; `-PlayerArgs "-mnPerfSync"` on `capture.ps1`)
renders into a target of its own and waits for the GPU to finish every frame (a one-pixel read-back queued behind it), so each frame costs what it really costs: the CPU's and the GPU's
work together, no overlap, no present (so it reads a little worse than a presented run, never better). `perf_*.json` then has `"gpuSynced": true`, and the first frame (setting the target
up) is left out of the statistics. A run also logs `PERF hitch:` for any frame over 120 ms with its time in the run.

Audio: `Monolith.exe -batchmode -nographics -mnAudioSelfTest [-mnOut dir]` (83 checks on the real decoded files through the real audio system rendered offline: files, loops and seams, every state and sting, every state-to-state change on its beat or bar, every radio line has a clip or a logged squelch,
the player's own files override and give back, every effect plays, a scripted battle never clips; writes `audio_selftest.txt` and the mixes). Flags: `-mnAudioMute` (every path runs, nothing is heard), `-mnNoAudio`, `-mnAudio` (sound in a capture run),
`-mnAudioCapture file.wav` (render in step with the sim, no sound card, and record: one tick of samples per tick, so a capture run's stills and sound line up; `python tools/audio/make_clip.py` joins them into a clip with sound),
`-mnAudioUser dir`, `-mnAudioLog` (a status line every 5 s: voices, audio-thread load, loudness, ducking). The offline tools (music from MIDI, effects, radio voices, the audio sheet): `tools/audio/README.md`;
the build `tools/audio/make_playtest.ps1` makes the audio playtest folder. Volumes: F5/F6 music, F7/F8 effects, F3/F4 voice, - and = master, F2 mute (saved to `audio.json` beside the game).

## Blender

`tools\blender.ps1 <script.py> [-Blend file.blend] [-TimeoutMin 20] [-- script args]` runs
`blender -b --factory-startup` (Blender 5.2). `tools\` is put on `sys.path`, `MN_ROOT` is set, and a Python exception gives
a non-zero exit plus the traceback in the summary. Logs go to `Game\Logs\blender_*.log`. Keep scripts and `.blend` files in
`art_src\<model>\`; never put `.blend` files under `Game\Assets` (Unity would try to import them with Blender).
Shared Blender helpers live in `tools\blender_lib\` (the ship kit: textures, meshes, parts, export, previews); the art pipeline, conventions and how to add a ship are in `art_src\PIPELINE.md`. `tools\ship\make_ship_plan.py` writes capture plans for ship models (`"scene": "corvette"`, `sceneOptions` per `ShipShowcase`: ship id, orientation, a placeholder fighter, damage) in the ship's own frame.

## Gotchas

- **HDRP creates a global settings asset itself** at `Game\Assets\HDRPDefaultResources\` the first time the package loads;
  `Setup.Project` finds and registers it. Do not delete it.
- **Materials the player needs** must be assets (made by `Setup.Project`, kept under a `Resources` folder), not
  `Shader.Find` at runtime, or the build strips their shader variants. See `Hdrp.EnsureLit`.
- **Long runs and tool timeouts:** a foreground tool call dies at 10 min. Run cold builds or big captures in the
  background. If your shell is killed, the next `unity.ps1` waits for the orphaned Unity (up to 20 min) before it starts.
- **Shared compile:** every builder's scripts compile together. Errors in someone else's files usually mean they are
  mid-edit: wait and retry. Never leave your own files broken.
- **Determinism:** gameplay uses the sim tick (`SimFrame.Time`), not `UnityEngine.Time`. `UnityEngine.Random` is seeded per
  shot; `System.Random` is not.
- **Resolution:** in a capture, `Screen.width/height` is the window, not the shot. Use `camera.pixelWidth`.
- **Unity Personal** shows a splash for about 2 s at player start. It does not affect captures.
- **Keep the repo path short.** HDRP's package paths are long, and a checkout deeper than ~100 characters hits Windows'
  260-character limit during import (`DirectoryNotFoundException` under `Library\PackageCache`).
- **Disk:** `Game\Library` is ~2 GB and each cold shader build adds more. Clean old `gauntlet\captures\` runs.
