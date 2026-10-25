# Piece: the player's death (first person throughout)

The user's brief (2026-10-24): "terrifying death sequences where the ship geometry rips apart and you (the player) are physically flung into space before dying". Design
section 6 ("The death sequence") and section 8 (first person only, never any external view; the user's hard rule). Before this a player death was a banner. The bar: a few
seconds you would not want to see twice, never comic. Code: `Game/Assets/Monolith/Scripts/Juice/Fx/DeathFx.cs` (the sequence), `Core/DeathBoard.cs` (the board the view and the
scenes read, and the event ids), pass 5 of `Resources/Juice/MN_Fx.shader` (cracks, flash, iris), five sounds in `tools/audio/Sfx/SfxDeath.cs`.

## Round 1 (builder, 2026-10-24)

### What happens (about 5.9 s from the hit; seated or standing; the camera is the player's own eye throughout)
| t | |
|---|---|
| 0.0 s | **The hit.** The canopy cracks: a web of white lines grows from the point of impact (screen-space, pass 5). A white flash. The **cabin model** (the real interior, which you never see from the seat) is suddenly around you, cut into ~87 pieces; those near the impact let go first and peel away at 2 to 9 m/s with sparks, air venting from the walls, red emergency light flickering and failing. Shake, pad jolt. |
| 0.55 s (gunfire) | **The breach.** The canopy blows out. The pieces ahead of you fly out faster than you do, those behind are left behind; fog, dust and streaks rush toward the hole; the pad peaks and then fades over 2.6 s. You are thrown through the hole at ~28 m/s, spinning (3 rad/s about an axis across your line of flight, wandering). Your ship (a fresh visual of its class) is cut into pieces that fly apart burning, three staged blasts, torn plates with fire trails. |
| 0.7 to 3 s | **The tumble.** Vacuum, stars wheeling, the battle going on, the wreck behind you coming into view as you turn. The field of view jolts out to about 80 degrees and settles. Breath from 1.4 s, heartbeat from 1.95 s (slowing). |
| 3.0 to 5.0 s | **The dark.** The view closes to an iris with a dark red edge, the spin dies away, the battle ducks away to a murmur, the last heartbeat at 5.0 s, black by 5.4 s, silence at 5.25 s. |
| 5.9 s | `DeathBoard.Finished`; the scene shows the ordinary failure flow (SHIP LOST, the debrief, A flies again, B quits) over the black screen. Nothing over the moment itself. |

By cause (read from the last hit and collision events): **gunfire** (the default), **blast** (missiles, torpedoes, flak: breach at 0.30 s, harder, more fire), **beam** (0.70 s, a longer
white-out, more fire), **collision** (0.12 s, thrown along the contact normal). A or Start skips it after 1.4 s (black within 0.55 s).

### How it is built
- **The player's own `ship_destroyed` is claimed** (`JuiceContext.PlayerDestroyed`, one line in `Explosions.OnShipDestroyed`): the ordinary blast at the eye is not made. The sequence
  writes `GameWorld.Death` every frame (pose, field of view, velocity); `CockpitView` puts the camera there (the dust follows its speed). The cabin rig and its prompts stand down while it plays;
  the cabin holds the seated eye's exposure (its flashes and fires are written for it); the mission is held from ending (`MissionRuntime.HoldPlayerDeath`); `MissionScene` hides the HUD
  (the PilotHud's whole object: switching the component off would leave its last frame on screen), takes A or Start for the skip and shows the failure flow when it ends.
- **The cabin is cut ahead of time**: `Debris.Slice` over the prefab's parts, one part a frame from a second or two after the scene starts (38,444 triangles in 37 parts, about 100 ms in all, the
  worst part 36 to 39 ms; cut whole it was a 150 ms hitch). The pieces wait switched off at the model's origin with their meshes uploaded; the death puts them in place by the ship's pose and
  releases them. The cabin FBX now imports readable (only under `Art/Cabin`, `ShipArtImport` version 6): ship models still do not.
- **Named events for the audio** (`DeathEvents`, documented in `Core/DeathBoard.cs`): `death_start`, `death_canopy_crack`, `death_hull_scream`, `death_decompression`, `death_tumble`,
  `death_breath`, `death_heartbeat` (many; value 1 strong to 0.35 faint), `death_heartbeat_stop`, `death_silence`, `death_skip`, `death_end`. The juice also plays five sounds itself through
  `IJuiceAudio`: `death_crack`, `death_tear` (first built as `death_scream`, a tonal howl that the user heard as a high-pitched whirring; replaced, see `gauntlet/pieces/brake/history.md`), `death_decompress` (the loudest sound in the game, bus "big"), `death_breath`, `death_heartbeat` (bus "ui": the duck at the breach and the collapse
  afterwards leave them alone). 104 checks in the audio self-test pass with them.
- **Settings** (`juice.json` `death`, the sandbox's DEATH page): `enabled` (off: the old banner), `intensity` (flashes, shake, pad), `tumble` (0 keeps the view steady, for anyone the spinning
  makes ill). The flash switch turns the white-out down to a quarter.

### Testing it
`-mnDeathTest [-mnDeathAt s] [-mnDeathCause gunfire|blast|beam|collision]`: in a mission the player is hit through the real damage path (and the cause is read from the real hit), in the sandbox the
sequence just plays; a plan's `sceneOptions.death: { "at", "cause" }` does the same. The sandbox has a DEATH page with a button for each kind. `-mnJuiceSelfTest` has a **death check** on the running
sandbox (the sequence has the eye from the hit, the cabin is in pieces, it cannot be skipped in its first moments, the eye is thrown 6+ m from the ship and the view turns 45+ degrees within a
second, the HUD stands down, it can be skipped after, the screen ends black, the events came in order, reset brings the flight view back, the destroyed player ship starts it and the off switch
stops that).

### Results (`gauntlet/captures/death/r1/`; merged with the real HUD, build from `main`)
- Self-tests: HUD 90/90, combat 90/90, AI 37/37, cabin 75/75, mission 62/62, pad drive 30/30, audio 104 checks pass, flight 35/35 on each of the four FS2 classes (vanguard info 31/35 as since the
  boost), the juice self-test with the boost check and the death check. The mission autoplay passes for `cabin` and `dogfight`; `escort` FAILS its event-order expectation ("Terce in" never fires:
  the AI-flown player does not destroy two thirds of Prime) **on afe0002 as well, before this piece**: it came with Earth's fighters, not with the death.
- Perf (1920x1080, `-mnPerfSync`, `gpuSynced: true`): a run with the death at 3 s: avg 229 fps, 1% low 69 fps, worst frame 28 ms (the first frame of the sequence), p99 12 ms, GPU 2.50 ms
  against 1.88 ms for the same run without a death (+0.6 ms). With `-mnJuiceHitchLog`: the hit frame 17 to 24 ms, the heavy second 10 to 14 ms a frame (70 to 100 fps), nothing worse.
- Captures: `seated_sheet.png`, `standing_sheet.png`, `collision_sheet.png` (22 stills each, HUD off), the stills in `seated/`, `standing/`, `collision/`; `hudflow_sheet.png` (the HUD before, gone
  during, the failure screen after); clips with the audio system's mix: `death_seated.mp4` (gunfire, from the seat, 6.6 s) and `death_standing.mp4` (a missile, standing at the plan table); `perf/`,
  `perf_off/`, `selftests/`, `flight/`, `mission/`.

### Shared files touched (small)
`GameWorld` (the `Death` board), `CockpitView` (the camera takes the board's pose), `CabinRig` and `CabinOverlay` (stand down), `CabinSystem.Mission` (the exposure holds the seated eye), `MissionRuntime`
(`HoldPlayerDeath`), `MissionScene` and `JuiceSandbox` (the HUD, the skip, the test), `Explosions` and `JuiceContext` (the claim on the player's destruction), `ShipArtSetup` (the cabin imports readable),
`AudioRig`/`JuiceAudio` (five cues), `FxPass`/`MN_Fx.shader` (pass 5), `JuiceSettings` and its defaults. The HUD's files are untouched; the merge with its branch (`hud-wt`) kept both sides.

### Weaknesses, said plainly
- The sounds are synthesised and have not been listened to by a person; only their levels and their timing against the events were measured. The breath is noise-shaped, not a recording.
- The ship you see break up is the placeholder fighter, cut into 11 pieces: it reads as a ship but is not a modelled one; the pieces near the eye are dropped (the nose you sat in is the cabin).
- The milky fog and the white flash around the breach can wash the picture out for a third of a second; standing at a wall it is brighter than from the seat.
- The iris edge is a flat dark red band, more "tunnel" than "dying"; there is no blood, no blur, no double vision (on purpose: serious, not gory).
- Only `MissionScene` and the sandbox run it; the older dogfight, range and flight-playtest scenes keep the banner. The skip uses A, Enter, Space, Start or Esc, and the pause screen cannot be opened while it plays.
- Cutting the cabin costs about 100 ms of work in all at each mission start (one part a frame); a scene that loads and kills the player within two seconds pays the rest on the hit frame.

## Round 2 (stutter builder, 2026-10-25): the half-second frame in the death, found and fixed

Worktree `stutter-wt` from main 23d98d3 (the fixes round and the start screen, built together for the first time: every check passed, see the combat history, Round 3). The brief: "in a
normal windowed game the first frame of the player's death takes 0.5 to 0.7 s" (the juice death check's "worst frame since the hit", about 540 ms in a windowed run of the fixes build).

### What the slow frame does (measured)
Reproduced in the juice sandbox, windowed 1920x1080 on the RTX 4070 Ti: `-mnScene juice -mnDeathTest -mnDeathAt 3 -mnJuiceHitchLog` logs one `HITCH` of **543 to 601 ms** in five runs (and 598, 617 and 625 ms in
three windowed runs of the whole juice self-test, whose death check reports 595, 610 and 622 ms "since the hit"), in a frame 1.2 s into the sequence, 0.6 s after the breach. Never in the frame that makes the burst.
- **Not the break-up.** Stopwatch laps round each part of the breach frame's script work: the wreck's slice 1.4 ms, the first blast 2.4 ms, the 36 hull shards 3.2 ms (3.9 in the dev build), the
  placeholder ship the wreck is cut from 4.2 ms (in a mission): under 10 ms in all. The hitch line splits the slow frame as "549.4 ms between the last LateUpdate and this Update" (the previous frame's
  rendering) and "the director's Update 0.1 ms".
- **A profiler capture** (a development player with `-profiler-enable`, read back by the new editor tool `ProfilerDump`, `tools/PIPELINE.md` "Profiling a hitch"): the 558.6 ms frame has the main
  thread inside `Inl_Monolith Lens` for **551.4 ms** (the HDRP render graph running the camera look's lens pass), the render thread idle in `Gfx.WaitForGfxCommandsFromMainThread` for 550.5 ms (the GPU is
  waiting for the CPU, not the other way round), and nothing else over 4 ms: script work inside that pass.
- **The work** is `DustTexture.Get()`: the lens pass draws the lens dirt (2048x1024, three channels: 1,400 specks, fibres and scratches, 55 smudges shaped by three octaves of Perlin noise on every
  pixel) on the main thread **the first time the Sun is in front of the camera** (the pass skips every frame the Sun is behind it). A log line now says it outright: `LOOK the lens dust texture was
  drawn in 534 ms at frame 505` and `HITCH 543.2 ms ... (frame 506)`. In the sandbox the first time the Sun is in front of the camera is the death's tumble swinging the view round to it.

### Why a played mission did not hitch (the premise was the sandbox)
A played start draws the dirt too, but earlier: `SkyWarmUp` swings the camera round the whole sky in its first four frames behind the loading screen, so the Sun is in front of it in one of them and
the lens pass pays the half second there (`HITCH 684 ms ... (frame 152)`, `LOOK the lens dust texture was drawn in 639 ms`, inside the loading's "deathcut" step: 0.9 to 1.2 s with it, 0.3 to 0.4 s
without). The sandbox, the juice self-test and every run that skips the loading screen have no warm-up, so they pay it at the death. **On the fixes build the death in a played mission never
stuttered**: dogfight, windowed 1920x1080, the four causes, the worst frame in the death's first second was 15.1 (gunfire), 15.2 (blast), 14.9 (beam) and 15.2 ms (collision). What a player paid was
0.6 s of the loading time. So the fix is as much about the loading as about the death.

### The fix
`Monolith.Look.DustTexture` draws its pixels on a worker thread (`Prepare`, started when the camera look is built, before the world has finished building), the camera look makes the texture from them in the
frame they are ready (`Poll`: 2 ms), and `Get` waits for the thread if the lens gets there first (a capture whose first frame has the Sun in it). The dirt is the same: the loading self-test (55 to 58
checks) draws it on a thread and on the main thread and compares them, **0 of 2,097,152 pixels differ**. `-mnDeathTest -mnMission x` now plays the mission behind the loading screen as a start does,
`-mnJuiceHitchLog` says where a slow frame's time went and how far into the death it fell, and the juice death check reports the worst frame in the first second after the hit as well as since it.

### Numbers (windowed 1920x1080 unless said; RTX 4070 Ti; another builder's perf captures shared the machine at times)
| | fixes build (23d98d3) | with the fix |
|---|---|---|
| the juice self-test windowed: worst frame in the death's first second / since the hit | 595, 610, 622 ms since the hit (the hitch log: 598, 617, 625) | **9 to 13 ms / 28 to 37 ms** (7 runs; the check now prints both) |
| the sandbox `-mnDeathTest` windowed, frames over 10 ms in the death | one of 543 to 601 ms | none over 17 ms |
| a played mission, the death's first second, worst frame (gunfire / blast / beam / collision, dogfight) | 15.1 / 15.2 / 14.9 / 15.2 ms | 14.4 / 15.3 / 15.0 / 14.5 ms |
| the same, gunfire, on the escort and the oort | 17.5 and 18.8 ms (20 frames over 10 ms on the oort) | 14.4 and 18.1 ms (2 frames) |
| a played mission with the real audio system (not muted), gunfire | | 14.8 ms; the first 16 s of flight: 2,295 frames, mean 6.97 ms, p99 7.6 ms, worst 35.9 ms (the fade into flight), none over 50 ms |
| launch to flyable (escort, 1280x720 window, six pairs run alternately; mean, range) | **7,075 ms** (6,847 to 7,251) | **6,553 ms** (6,406 to 6,731): 522 ms (7.4%) quicker |
| the loading screen's own time / its "deathcut" step | 4,768 ms / 920 ms | 4,251 ms / 383 ms |
| launch to flyable, dogfight, 1920x1080 (four runs each) | 7,061 to 7,348 ms | 6,672 to 6,769 ms |

The aim was no frame over 50 ms in the death's first second at 1080p: the worst is 13 ms in the sandbox and 19 ms in a played mission. Under 20 busy processes the death's first second shows 11 to 18 ms (12 runs),
under 40 busy processes 52 to 65 ms, which is the machine.

### Looks and sound: the same
- 22 stills of the seated death (`gauntlet/shots/death_r1_seated.json`, Round 1's plan) on the fixes build and on this one: 20 identical, two differ by 1 in one channel in 2 and 6 of 3,686,400 bytes (HDRP's
  dithering, the noise a rerun of one build shows). Two stills with the Sun in view (`sky_sunonly.json`, which draws the glare and the dust): 0 and 8 of 8,294,400 bytes differ, by 1.
- Nothing in the sequence, its events, its sounds or their timing was touched; the death check (events, eye, tumble, skip, black, off switch) passes as before.

### Known gaps
- The brief's "normal windowed game" does not hold on the fixes build: the sandbox, the juice self-test and any scene that skips the loading screen hitched; a played start never did, and paid the same half
  second as loading time instead. A standing death at the plan table was not driven in a played start (the cabin mission holds the clock at the table); the capture plan `death_r1_standing.json` covers its
  pictures and nothing in this fix touches it.
- `-mnFps 30` and `-mnFps 15` runs of the death check report 33 and 67 ms frames in the first second: that is the cap itself (33 and 66 ms a frame), not a hitch.
- The lens dust's blue (bokeh) channel has always been empty (`Mathf.SmoothStep(from, to, t)` is a blend, not GLSL's edge function, so the bokeh's falloff goes negative and clamps away). Drawing it would change
  every glare, so it is left as it was; noted for whoever next touches the lens.
- Other things built lazily on first sight have the same shape of fault (the sky's Milky Way, which `SkyWarmUp` was written for, is the other known one); `-mnJuiceHitchLog` plus `ProfilerDump` finds the next
  one in minutes. The sandbox still has no warm-up of its own: it is a lab, not a start.
