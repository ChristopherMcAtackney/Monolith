# Piece: the loading screen (the user's splash, a bar in the HUD's style, and a start that does not stutter)

The user's note (2026-10-24): "It's a bit jarring when loading the playtest builds to sit waiting for the resources etc. to load. Nothing too fancy, just a splash graphic
(I will supply), and a loading bar that is in the same style as the HUD." Their splash is `art_src/ui/Oort_Splash.png` (1672x941, theirs; shipped as bytes in
`Resources/Loading/oort_splash.png.bytes`). The user judges by eye, so this piece has no critic rounds. Code: `Scripts/Loading/` (README there), `Scripts/Game/Loading/`,
`Editor/LoadingManifest.cs`, the Hud kit's image layer and pass 5 of `Resources/Shaders/MN_Lines.shader`. Tools: `tools/loading/`.

## Round 1 (builder, 2026-10-24)

### What a played start did before (measured, `-mnNoLoadingScreen -mnStartupProbe 10 -mnStartupDrive`, windowed 1920x1080, RTX 4070 Ti, 144 Hz vsync, three runs)
- A white flash, Unity's logo (forced by the licence), then a plain dark window for about 3 s, then nothing drawn at all until the world was built and its first frame drawn: **the first picture at 6.8 to 6.9 s**.
  That first frame took 2.5 s to draw (the sky's first view, HDRP's first frame).
- The sound (343 files, 21 MB of Ogg, decoded one at a time inside Unity: about 5 s however many are asked for at once) was not ready until **9.4 to 9.5 s**: the first seconds of flight were silent.
- **The first 10 s of flight: worst frame 770 to 790 ms** (two stalls, at +0.78 s and +1.55 s: the sky's Milky Way and star catalogue built on first sight, and the effects' first use), 6 to 9 frames over 100 ms, 16 to 25 over 50 ms,
  31 to 39 over 33 ms, then a run of 15 to 33 ms frames at +0.3 to 0.5 s (the pilot's cabin being cut into pieces for the death, a part a frame, which started itself 90 frames into the mission) and about 190 ms
  the first time the guns, a missile and the pause screen were used.

### What was built
- **The screen** (`LoadingScreen`): the splash full screen from the first frame the game's code can draw (**3.5 to 3.6 s after launch**; before that the window is Unity's own), faded in over 0.6 s and settling
  very slowly (4.5% larger at the start, at rest after 10 s), then the bar and a one-line status in the HUD's current style and type, and a short fade (0.55 s) into the mission, which has been drawing behind it. Never stretched:
  scaled the same both ways to fill the screen but by no more than 14% short of filling it on the tighter axis (`LoadingSplash.CropLimit`), so 21:9, 4:3 and 5:4 get a little black at the sides with the picture's edge
  melting into it. A `Loading/splash.png` beside the game replaces the picture.
- **The bar**, in the style the player has (the `hud.json` choice or `-mnHudStyle`): **Halo** cells like the gauges, **Meridian** a thin line with a scale and a pointer, **Cipher** a plate with the fill set in it and a glyph row; the status
  in the label face, the percentage in the value face, over a faint plate with corner ticks so it reads over any part of the picture.
- **Honest progress** (`LoadMeter`): the steps are the real work, each reports a count (assets done of assets to do, sound files decoded of files to decode, parts cut of parts to cut, frames drawn of frames to draw), never a
  timer; the bar is the weight done over the weight of all of it, with each step's weight the milliseconds it took last time on this machine (`loading_stats.json` beside the game) or the shipped figures the first time. The shown
  value eases toward the real one and is never ahead of it; the screen waits a beat with the bar full.
- **The start** (`Game/Loading/LoadingFlow`): `GameRoot.Build` was split (`BuildPlay` is the old tail) so the screen is up before the world is built. The steps: the screen on the display; **the sound's decode started first**
  (`AudioRig.BeginPreload`: the audio system made later takes the same library, `AudioLibrary.Started`); the **assets** of `Resources/Loading/preload.json` read asynchronously (ships, cabin, effects, sky: 25 assets; the file is
  written before every build by `Editor/LoadingManifest`, so a new ship is in it without anyone remembering); the **world** built with the clock held (`Sim.Held`) and the pad and keyboard switched off; **the cabin cut** done now
  (`DeathFx.PrepareSoon`, one line in the death effect; it waited 90 frames before) and the sky's wide first look with it (`SkyWarmUp`); the cabin shown for four frames; twelve calm frames in a row; the bar filled; the clock and
  the pad given back as the fade starts. If the world cannot be built the screen comes down and the pad comes back (a broken mission is not hidden behind it).
- **Everywhere the player enters** goes through it: Play.bat missions (escort, cabin, dogfight), `-mnScene dogfight|range|flight|juice` (Sandbox.bat). Skipped for captures, every headless check and the check drivers
  (`-mnJuiceSelfTest`, `-mnPanelShot`, `-mnRumbleTest`, `-mnPanelDrive`, `-mnPadDrive`, `-mnDeathTest`), and with `-mnNoLoadingScreen`.
- **A way to measure a start** (`StartupProbe`, `StartupDrive`): milestones in ms since the process began, and every frame for ten seconds after the game is flyable, with a virtual keyboard doing what a player does (afterburner,
  guns, a missile, a decoy, the brake, the pause screen), `startup.json` for the numbers.

### Result (same machine and settings, three runs each; `gauntlet/captures/loading/r1/`)
| | before | after |
|---|---|---|
| something on the screen | first picture at 6.8 to 6.9 s (a dark window before it) | the splash at 3.5 to 3.6 s |
| picture, sound, sky and cabin ready | 9.4 to 9.5 s (silent for the first 2.6 s of flight) | 10.0 to 10.35 s (all of it at once) |
| worst frame in the first 10 s of flight | 770 to 790 ms | 13.7 to 14.9 ms (two vsyncs) |
| frames over 100 / 50 / 33 ms | 6 to 9 / 16 to 25 / 31 to 39 | 0 / 0 / 0 |
| cabin mission, same measure | worst 761 ms, 68 frames over 33 ms | worst 12.5 ms, 0 over 33 ms |
Measured again on the tree rebased onto main 5f36cce (the fight theme's MP3 in the library): before, the first picture at 8.6 to 8.7 s, the sound at 11.3 s, a worst frame of **1.57 to 1.62 s** (a new stall at +2.3 s) and 760 ms at +0.75 s; after,
the splash at 3.6 to 3.9 s, everything ready at 11.8 to 12.0 s and a worst frame of **9.6 to 13.9 ms**.
So the game is ready to fly **0.5 to 0.8 s later than it was ready with sound**, behind a screen that appears 3.3 s sooner than the first picture did, and it does not stutter afterwards.
The time that is left is Unity's own start (3.5 s, with its logo), the sound's decode (about 5 s, serial inside Unity) and the world's construction (0.9 s, one synchronous call).
**The sound is the one big cost left, and it grows with the music**: with the fight theme (`fight.mp3`, 4.9 MB, an MP3, which Unity decodes slowest) merged from main the decode is 6.5 to 7.8 s and the start 12 to 13.7 s
(before the screen, the same sound was ready at 13.2 s). Converting the MP3 to Ogg, or decoding the music tracks lazily (the first one the mission needs first) or keeping them as raw sound, would take most of it out: the audio builder's call.

### Checks
- `-mnLoadingSelfTest`: **55 of 55** (the splash loads and is the picture; its fit at 15 screen shapes from 4:3 to 32:9 and a portrait one: never stretched, never off the screen, centred, always inside the picture, the
  settle gentle and over; the bar in every style at every shape and status: the plate on the screen, every word on the plate, the status never into the percentage; the three styles differ; faded out it draws nothing; the meter is
  monotone, weighs by share, names the step under way, does not move on a timer; a held clock does not tick and a released one does; every asset in the manifest loads and every ship is in it).
- The existing checks on this build: `-mnHudSelfTest` 90 of 90, `-mnMissionSelfTest` 62 of 62, `-mnCabinSelfTest` 75 of 75, `-mnAudioSelfTest` 104 passes, `-mnCombatSelfTest` 98 of 98, `-mnScene range -mnPadDrive` 42 of 42,
  `-mnScene juice -mnJuiceSelfTest` OK; an existing capture plan (`hud_r1_pause`) unchanged.
- Real windows, grabbed from the player while it ran (`PrintWindow`, so nothing in front can cover them): Unity's logo, the splash and each step of the bar, the fade into flight, in Halo at 16:9 and Meridian at 21:9;
  stills of the screen held still (`"scene": "loading"`, `tools/loading/make_plans.py`) in all three styles at 16:9 (early, middle, nearly done) and the middle moment at 21:9, 4:3 and 5:4.

### Found on the way, and fixed
- The black backdrop drawn as a shape covered the picture (shapes draw over the picture layer): the backdrop is a quad of the picture layer now (`HudLines.Backdrop`), under everything.
- Asking the sound to decode ten, twenty-four or sixty-four files at once makes no difference to the time (Unity decodes serially); starting it first, before the world is built, saves about 1 s.
- `Application.backgroundLoadingPriority = High` lets asset loads take 50 ms a frame, which is right while the assets and the sound load (about 20 frames a second behind a static picture) and wrong afterwards, so it is put back once the world is built.
- Frame-counted waits in other systems (the cabin cut's 90 frames) never finish behind a screen that draws 20 frames a second: they are asked to start now.

### Not done, and why
- The bar does not move during the world's construction (0.9 s, one synchronous call into every scene): the status says `BUILDING THE WORLD` and the sound's decode, which goes on in parallel, keeps the figure moving.
- Unity's own splash and the blank window before the game can draw (about 3.5 s) are the engine's and, with a Personal licence, cannot be replaced.
- The first-use hitches of things only a fight uses (a missile's smoke, a big explosion) are not warmed: none showed above 15 ms in the driven flight, which fires guns, a missile and a decoy.
