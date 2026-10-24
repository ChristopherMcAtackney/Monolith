# Loading (`Monolith.Loading`, and `Game/Loading/` in the composition root)

What a played start shows while the game gets ready: the user's splash picture, and a loading bar in the HUD's own style with a status line. Everything on the bar is real work.

## What the player sees
1. The window is Unity's own (its logo, forced by the licence) until the game's code can draw. From the first frame it can, **the splash** is up (about 3.5 s after launch on the development machine), faded in over 0.6 s and settling very slowly (4.5% larger at the start, at rest after 10 s), then **the bar** at the bottom in the current HUD style (Halo: cells like the gauges; Meridian: a thin line with a scale and a pointer; Cipher: a plate with the fill set in it) with a one-line status in the HUD's type (`LOADING SHIPS`, `LOADING EFFECTS`, `BUILDING THE WORLD`, `WARMING UP THE SKY`...) and the percentage.
2. When everything is done the bar is seen full for a beat and the screen fades over 0.55 s into the mission, which is already drawing behind it.
3. Nothing happens behind the screen: the world's clock is held (`Sim.Held`) and the pad and keyboard are switched off until the fade starts.

The picture is never stretched. It is scaled the same both ways to fill the screen, but by no more than 14% short of filling it on the tighter axis (`LoadingSplash.CropLimit`), so a wide or square screen gets a little black at the sides (the picture's edge melts into it) instead of losing a third of the art. To use another picture: `Loading/splash.png` beside the game replaces the shipped one.

## How a start goes (`Game/Loading/LoadingFlow`)
`GameRoot.Build` is split: the checks and captures run as before; a played start (not batch, not `-mnCapture`, not `-mnNoLoadingScreen`) runs `LoadingFlow.Run` as a coroutine, which calls `GameRoot.BuildPlay` (the old tail of `Build`: the world, the systems, the scene) between its own steps.

| step | what it is | how its fraction is counted |
|---|---|---|
| start | the screen itself, on the display (two frames) | done |
| sound | `AudioRig.BeginPreload` loads the whole library at once. Unity decodes an Ogg or MP3 inside `GetContent`, on the main thread (2.3 s for the 349 files, 5.7 s before the audio builder's loader moved the 16-bit conversion off it), so a file that has been decoded once is kept as plain PCM (`Audio\.pcm`, `PcmCache`) and read back by worker threads: **0.13 to 0.18 s** for the whole library on every start after the first (the playtest scripts of the audio builder make the cache before you play; any other start makes it itself, once, 1.5 s slower). The audio system made later takes the same library over | files loaded of files to load (the step ends when the library is ready, not when the load gets round to asking) |
| ships, cabin, effects, sky | the assets named by `Resources/Loading/preload.json` (the ship art, the cockpit and cabin model, the flipbooks and hull sections, the Milky Way and the star catalogue and the shaders), each read with `Resources.LoadAsync` while the screen draws | assets done of assets to do, with the one in flight's own progress |
| world | `BuildPlay`, in one go (it cannot be cut up without touching every scene) | done when it returns |
| deathcut | the pilot's cabin is cut into pieces ahead of time for the death (`DeathFx`, a part of the model a frame; it used to start itself about 90 frames into the mission, a stutter of 15 to 33 ms a frame at 0.3 to 0.5 s); the screen asks it to start now (`DeathFx.PrepareSoon`) and waits for it. The sky's first wide look (`SkyWarmUp`, which builds the Milky Way and the stars: about 0.6 s the first time) happens in these frames | parts cut of parts to cut |
| skywarm | whatever is left of the sky's wide look | frames drawn of frames to draw |
| cabinwarm | the cabin shown for four frames (`CabinRig.WarmShow`) where the mission has one | frames |
| settle | wait for 12 quick frames in a row, so the first draws of everything are over | calm frames of 12 |

**Honest progress**: the bar is the weight of the work done over the weight of all of it (`LoadMeter`); a step reports a count, never a timer, and a meter left alone does not move. A step's weight is how long it took last time on this machine (`loading_stats.json` beside the game, written at the end of each load), or the shipped figures (`Resources/Loading/loading.json`) the first time. Steps overlap (the sound decodes while the ships load), so the weights add up to more than the whole. The shown value eases toward the real one and is never ahead of it.

`Resources/Loading/preload.json` is written by `Editor/LoadingManifest` before every player build (and from the menu Monolith / Loading), so a new ship or flipbook is in it without anyone remembering; `-mnLoadingSelfTest` checks every entry loads.

## Measuring a start
`-mnStartupProbe [seconds]` logs `STARTUP +ms: label` milestones (process start to `boot scene loaded`, `loading screen on display`, `world built`, `sound ready`, `flyable`) and, once flyable, every frame's time for the next seconds (mean, p99, worst, counts over 33/50/100 ms, the worst five and when). `-mnOut dir` also writes `startup.json`; `-mnStartupQuit` ends the run after the window; `-mnStartupDrive` presses the first-use keys (afterburner, guns, missile, countermeasure, brake, stand up, pause) with a virtual keyboard so the hitches that only come the first time something is used are in the numbers; `-mnNoLoadingScreen` is the old start, for comparing. `-mnStartupDrive` also marks `sound decoded` in the old start.

## Where things are
- `LoadingScreen` (the screen: own black camera until the mission's camera exists, then the same pass on the mission's camera so the fade reveals the real frame; `DrawFrame` is a pure function of its arguments, used by the capture scene and the checks), `LoadingSplash` (loading and fitting the picture), `LoadMeter`, `StartupProbe`.
- `Hud` kit additions: `HudLines.Image` and `HudLines.Backdrop` (a picture layer under every shape and word) and pass 5 of `Resources/Shaders/MN_Lines.shader`.
- Hooks in shared code, all small: `Sim.Held`, `SkyWarmUp.Pending` and `.Frames`, `CabinRig.Current` / `.WarmShow`, `AudioLibrary.Started`, `AudioRig.BeginPreload` / `.Preloaded` (and `AudioSystem` not starting a library that has started).
- `Game/Loading/LoadingTestWorld` (capture scene `"scene": "loading"` with `sceneOptions` `style` and `keys` `[[t, progress, "status", settle], ...]`; a real start cannot be photographed, the window is not shown while a capture runs), `LoadingSelfTest` (`-mnLoadingSelfTest`, 55 checks), `StartupDrive`.
- Tools: `tools/loading/make_plans.py` (the capture plans), `tools/loading/make_playtest.ps1` (the playtest folder).
