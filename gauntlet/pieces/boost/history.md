# Piece: the boost (afterburner feel and speed)

The user's brief (2026-10-24): "Boosting feels weak at the moment so it needs a round of juicing, and needs to provide more speed for a
really satisfying feeling when applying it." They also saw a high field-of-view effect in a test build and want it for the boost.
Design section 4 (the afterburner bullet) carries the user override. The bar: lighting the burner is the best moment of flying. Code:
`Game/Assets/Monolith/Scripts/Juice/Fx/BoostFx.cs` (the feel), the ship's own data `Resources/Ships/vanguard_fighter.json` (the speed),
the lens in `Resources/Juice/MN_Fx.shader` pass 4, two sounds in `tools/audio/Sfx/SfxShip.cs`.

## Round 1 (builder, 2026-10-24)

### More speed (the player's side only)
| | before | now | why |
|---|---|---|---|
| burner speed | 150 m/s (FS2 Ulysses) | **235 m/s** | 3.4 x the 70 m/s cruise; FS2's ratio is 2.1 x. A full tank (6 s) covers about 1.3 km. |
| ramp time constant | 0.7 s | **0.4 s** | 63 percent of the extra speed in 0.4 s (an initial push of about 410 m/s^2), 95 percent in 1.2 s (was 0.71 s and 2.07 s). |
| cruise, fuel, burn, recharge, release | 70, 300, 50/s, 25/s, 1.5 s | unchanged | the release still eases back over about 4.5 s. |

Both fields carry a `source` note that says USER OVERRIDE and what they replace. The four FS2 reference classes (Ulysses, Myrmidon, Hercules,
Artemis) are untouched: `tools/flight.ps1` passes **35 of 35 on each**. `vanguard_fighter` is an info-only comparison against the Ulysses and
now differs on exactly the four afterburner metrics it is meant to (`ab_peak` twice, `ab_t63`, `ab_t95`; 31 of 35).

### The juice
- **The view widens** (the effect the user saw): looked for it in the builders' recent work and found two, the cabin's standing view (68 degrees against the
  cockpit's 55, `cabin.json` `fovStanding`) and the sky warm-up's 4 frames at 140 degrees (`SkyWarmUp`, a start-up trick, not a look). The boost borrows the
  cabin's wide look: a kick out on ignition (peak +17.4 degrees at 0.18 s, so 72 from 55), held at +12 (67) while it burns, eased back over about a second on
  release (a smoothstep on a state that rises in 0.09 s and falls in 0.32 s). `BoostFx` writes `GameWorld.FovKick`; `CockpitView` applies it (only while
  there is a kick, and once more to put it back, so the cabin's and the warm-up's FOV are not disturbed); `CaptureCamera` adds it in follow shots.
- **The body:** a trauma of 0.62 and a pad pulse (low 0.8, high 0.46, 0.3 s) on ignition, then a steady low rumble (0.14 low, 0.06 high) and a faint
  sustained tremble while it burns. The old one-line hum in `JuiceDirector` moved into `BoostFx` (and stays as the fallback when the boost juice is off).
- **The speed:** the motion dust drawn up to 2x longer and 1.5x brighter (`GameWorld.DustStretch`, `DustBright`; `SpaceDust.Update` takes them); a **lens**
  (pass 4 of `MN_Fx`: copy the picture, redraw the rim with a 7-tap radial zoom blur and a red/blue split, plus a faint blue glow) that is zero inside 34 percent
  of the half diagonal and in a disc round the player's target (so the target and reticle are never smeared; the HUD draws after post anyway).
- **Flames:** every other ship that burns grows a blue-white plume (streak, hot glow, flare) over half a second, from the rear of its look bounds.
- **Sound** through `IJuiceAudio`: `JuiceCue.BoostWhoomph` (a thump under 100 Hz, a rush that climbs to 2.5 kHz, a crack, a low tail: layered under the audio
  system's existing `afterburner_on` and roar loop, which already follow the burner flag) and `BoostSputter` (four coughs at 0, 0.17, 0.38, 0.66 s that thin out, a
  falling whine, a flameout). The roar loop's level and pitch now follow the speed gained over cruise (a 3-line change in `AudioSystem`), so it swells with the surge.
- **Empty tank:** at fuel 0 with the key still down, the sputter plays and the view, the shake and the pad cough on the same four beats; a key pressed with under
  10 fuel does a smaller cough. The last 18 percent of the tank flutters the view and the pad as a warning.
- **Success is loudest:** the player's own ignition gets the whole treatment; other ships get only the flame. **Nothing hides the target or the HUD** (above).
  **Intensity:** `juice.json` `boost` has `enabled`, `intensity` (0 to 2) and a level for each piece (`fovHold` 12, `fovPunch` 8, `shake`, `rumble`, `lens`,
  `streaks`, `flare`, `sound`); 0 turns a piece off, so the view widening and the edge blur (the parts a sensitive player may want gone) can go alone. The shake
  and rumble switches in the main settings still apply, and the flash switch turns the rim glow off. The user's `juice.json` defaults are untouched; the new section
  has its own defaults and a file without it loads fine.
- **The sandbox:** a BOOST page (Boost 4 s, Run the tank dry, Wingmen burn 5 s, the switch and nine sliders, a live speed, fuel and view readout). `-mnBoostLog file.csv`
  writes the numbers each frame.

### Shared-file edits (kept small)
`GameWorld` (three fields), `CockpitView` (the kick, the dust arguments), `SpaceDust.Update` (two optional arguments), `CaptureCamera` (the kick in follow shots),
`AudioSystem` (the roar follows the gained speed), `AudioRig` and `JuiceAudio` (two cues), `CabinSelfTest` (read the mission run only once the scripted pilot is back
in the seat: how long the dogfight lasts depends on how the ships fly, and the balance round and a faster player ship both move it), `SandboxScript` (`face` takes
`behind`, `speed`, `throttle`).

### Checks (build from the committed tree plus this piece; `gauntlet/captures/boost/r1/`)
- Flight `tools/flight.ps1`: Ulysses, Myrmidon, Hercules, Artemis 35/35 each; vanguard (info) 31/35 as described.
- Combat self-test 90/90. AI self-test 37/37. Mission self-test 61/61 and the autoplay of all three missions (cabin, dogfight, escort) pass. Cabin self-test 75/75.
  Pad drive 20/20. Juice self-test OK, plus the new **boost check on the running sandbox** (ignition lights the burner once, the view widens past 80 percent of the
  setting, the dust stretches, the lens is on, the speed rises, the burner is out after the 4 s, the view is still easing back 0.3 s later and all the way back after
  2 s, the off switch leaves the speed and removes everything else, running the tank dry makes the sputter).
  Audio self-test 96/97: the one failure is pre-existing and not the boost (146 of 163 radio lines have a clip: the cabin mission's lines are not voiced yet); the
  new sounds are in (75 effects each make sound, 0 silent).
- Numbers from `boost_log.csv` (the stills run): ignition at 1.0 s, FOV +12.8 degrees by 0.08 s, peak +17.4 at 0.18 s, +12.0 by 1.0 s, back under 0.1 degree 1.1 s
  after release; speed 69 to 235 m/s in 2 s; trauma 0.59 at ignition, 0.045 while it burns.
- Perf (1920x1080, `-mnPerfSync`, the GPU field is real, `gpuSynced: true`): GPU 2.17 ms while boosting against 2.05 ms cruising (+0.13 ms) in the first run, 2.55 against
  2.15 (+0.40 ms) in a second run with another agent loading the machine; frame time p99 5.1 to 5.4 ms, about 320 to 360 fps either way. The lens is skipped when idle.
- Captures: `stills/` (before, ignition, punch, surge, hold, hold late, release, after), `stills_boost_off/` (the same shots with the treatment off: the speed
  is the same, the look is not), `clip/` and `boost_clip.mp4` (4 s, 30 fps, with the audio system's mix), `dry/` and `boost_dry_clip.mp4` (the sputter, with sound),
  `flame/` (wingmen burning), `perf_on/`, `perf_off/`, `flight/`, `selftests/`, `mission/`.

### Weaknesses, said plainly
- The sounds are not listened to by ear here; the loudness numbers and the timing of the coughs against the camera beats were measured, not judged. In the juice sandbox
  the cockpit loops (the engine, the roar) are silent because the sandbox's player has no combat record (the audio system plays them only for a living ship): the roar is
  heard in the missions and the dogfight, the whoomph and sputter in both.
- The flame is a thin streak: it reads at 100 m behind a fighter and is lost at a kilometre; it has no light on the hull and no heat shimmer.
- The lens fringe is small by design (1.25 percent of the radius at the rim); at 1280 wide it shows as a smear more than a split of colour.
- The kick to 72 degrees is a lot on a small monitor; it is one slider. The 140-degree warm-up frames are untouched.
- The pause screens of the dogfight, flight and range scenes do not have the invert switches (the missions' does; the file works everywhere).
- The AI's afterburner use is unchanged (it does not know the burner is stronger): worth a look in the balance round, since a player who boosts now outruns every drone.
