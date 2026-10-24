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
  `IJuiceAudio`: `death_crack`, `death_scream`, `death_decompress` (the loudest sound in the game, bus "big"), `death_breath`, `death_heartbeat` (bus "ui": the duck at the breach and the collapse
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
