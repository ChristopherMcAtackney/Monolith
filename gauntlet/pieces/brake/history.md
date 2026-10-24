# Piece: the brake (R3), the guns low in the view, and the whirr on death

Round 1, juice builder, 2026-10-24. From the user's playtest: "there's a really weird sound when you explode, like a high pitched whirring - get rid of that";
a brake on R3 mirroring L3's boost (design section 4); "the lasers should originate from somewhere bottom left / right of the main view, so you get a much fuller view of them as they shoot" (section 5).

## 1. The whirr on death

**The culprit was `death_scream`**, one of the five sounds the death sequence synthesises (`tools/audio/Sfx/SfxDeath.cs`). It was built as "the hull screaming": frequency-modulated tones and saws, a rising stack
of harmonics with a 7 Hz and a 23 Hz warble, 3 seconds long at the loudest point of the sequence. Found by rendering the death clip's audio in step with a capture run (`-mnAudioCapture`) and
looking at its spectrogram (`gauntlet/captures/brake/r1/audio/death_seated.png`): from the canopy crack on, a ladder of wavy parallel lines rising through 1 to 5 kHz, which the ear hears as a whir. The
isolated sound (`death_scream.png`) shows the same ladder; the other four death sounds, the cabin's power-down and the music have no such lines.

Removed, not turned down: `death_scream` is replaced by `death_tear` (`SfxDeath.Tear()`), a ripping, grinding roar built from band-passed noise whose centre wanders on two slow random walks, with irregular
plate pops and crackle, a low rumble and a reverb tail; it holds no pitch and repeats nothing at a steady rate (`death_tear.png`: no lines). The cue is `JuiceCue.DeathTear` (renamed from `DeathScream`), the
event id `death_hull_scream` is unchanged (it names the moment, not the sound). The re-rendered death clip (`audio/death_seated_new.png`) shows the ladder gone. Not listened to by a person, as before.

## 2. The brake

### How it behaves
- **R3** (the aim stick's click; **Left Ctrl** on the keyboard) latches the brake: the ship stops hard on its retro thrusters, the **throttle setting goes to 0** and the **previous setting is remembered**
  (if it was already near 0, the last real one; the HUD shows it as `RESUME  n%` and a ghost fill with a bracket on the throttle band).
- To move again: **push the throttle up** as now (it carries on from 0), or **click R3 again** (the remembered throttle comes back), or **boost with L3**. Match speed (X) also lets go and does its own thing.
  **A boost from a standstill** (throttle 0, braked or not) sets the throttle to cruise (`brake.boostFromRestThrottle`, 1.0 in controls.json), so when the burner ends you are cruising, not stopped. If the
  boost is refused (no fuel, the restart lockout) the brake and throttle come back as they were.
- The brake shuts the burner off and overrules a held afterburner key and match speed, in the flight model (`ControlButtons.Brake`, held; the pilot sends the remembered throttle in the throttle field while it is held, which the ship ignores).
- Only the player's fighter has retros (`brakeDecel` in `Resources/Ships/vanguard_fighter.json`); the four FS2 classes have none and a brake on them is the ordinary slow-down, so the FS2 traces are untouched
  (35 of 35 metrics on each class).

### The time constant (the proposal, justified)
**0.35 s** (the exponential approach `v(t) = v0 e^(-t/tau)`), against 1.5 s for the ordinary slow-down. From cruise (70 m/s): under 2 m/s in 1.3 s, a dead stop (the ramp snaps to 0 under 0.4 m/s) in about 1.8 s;
from burner speed (235 m/s) a stop in about 2.2 s; a peak deceleration of 200 m/s^2. It is four times quicker than the slow-down, so it is unmistakably a brake and not the throttle, and it is still a
stop you watch happen (the lurch, the flare and the HUD all have time to read) and not a teleport. The number lives in data with its reasoning and is the first thing to tune after a playtest.

### The aim stick under the click
R3 is the aim stick and a thumbstick click pushes it off centre. For `brake.aimHoldSeconds` (0.15 s) after the click, and while it is down (at most 0.5 s), the aim reads what it read
`brake.aimLookBackSeconds` (0.1 s) **before** the click, so the nose carries straight on through it. The pad drive clicks R3 with the stick thrown to (0.35, -0.30) and checks the largest aim output is 0.000.
Both numbers are in controls.json (`brake` block); 0 turns the hold off.

### R3 was already mapped
R3 was **next missile type**. It moved to **D-pad right** (outside the quick menu; inside it D-pad right is still "favour engines"). The D-pad's **two-thirds throttle preset gave way** (the pad keeps up = full,
down = 0, left = one third; the keyboard keeps 1 2 3 4). Nothing else on the pad clashed. The pause screens (mission, dogfight, range) list R3 as the brake and D-pad right as next missile.

### The juice (`Juice/Fx/BrakeFx.cs`, settings `brake`, the sandbox's BRAKE page)
- **Forward lurch**: a spring thrown by the click (a push along the view and a nod), overshooting once and settling into a lean while the retros fire: about 17 cm and 1.5 degrees at cruise, applied by `CameraShake` after the cockpit view.
- **The view narrowing**: the boost's kick in reverse. 5 degrees of kick on the click (the boost's envelope), 3 held while the retros fire, easing back as the ship stops (`GameWorld.FovKick` below zero).
- **Retro flare**: two jets at the sides of the nose (ship space +-1.45, -0.35, 5.2), seen from the cockpit at the lower left and right, firing forward, a flash on the click, shrinking as the speed falls.
- **Thump and hiss** through `IJuiceAudio` (`brake_thump`, `brake_hiss`; `tools/audio/Sfx/SfxBrake.cs`): noise and low body only, no tone (spectrograms in `audio/`). **Rumble**: a heavy pad thump and a hard shake on the click, a steady rumble while the retros fire, a soft pulse when it lets go.
- Only a click at speed (over 8 m/s) gets it all; braking at a crawl is a latch with a small pulse. Every piece has a level; the off switch leaves the stop with nothing added.

### HUD
While the brake is on the speed gauge's label reads `BRAKING` (pulsing, with a row of ticks running down the speed band) while the retros fire and `BRAKE ON` once stopped, with `RESUME  n%` under the throttle line and the
remembered throttle drawn on the thin band as a ghost fill and a bracket. Match speed's `MATCHING` gives way to it. The HUD self-test still passes (90 checks).

## 3. The guns low in the view

The two Needle muzzles moved from (+-3.19, -0.35, 3.64) (off the side of the picture: 1.6 of the half width) to **(+-2.0, -0.85, 4.6)**: 3.1 m ahead of the cockpit eye (0, 0.4, 1.5), 2.0 m out, 1.25 m below it; on screen
(+-0.70, -0.78) of the half width and half height, the bottom left and bottom right. The heavy gun's muzzle stays at (0, -0.9, 4.34), already low in the middle (0, -0.88). The guns still converge on the nose axis at the target's range
(600 m with none), so the lead pip's aim is unchanged. The combat self-test fires both guns at a stationary target dead ahead at 150, 400 and 900 m, targeted and not, and both shots land every time; the lead-hit test (five crossing
targets) still passes. The sandbox's own guns and volley now read the same hardpoints.

What the player sees: bolts that start at the corners and streak to the aim point. At 70 m/s the ship flies past the muzzle's point in 45 ms, so the muzzle flash now rides with the ship (it used to be left behind) and the
bolt's tail trails off the edge of the picture; the bolt is drawn from the muzzle, shows from a metre out, is held to 0.6 degrees of the picture near the eye (a bolt 0.9 m wide at 4 m would fill a quarter of the screen) and tapers to a point at its tail.

## Results (`gauntlet/captures/brake/r1/`; the merged build, `merged/`)
- Self-tests: flight 35/35 on each FS2 class (vanguard info 31/35 as since the boost) with brake checks added; combat 98/98 (8 new); AI 37/37; cabin 75/75; HUD 90/90; mission 62/62; audio 104 checks; juice self-test OK
  (settings, boost check, **brake check**, death check); pad drive 42/42 (12 new: R3 latches, the aim stays put, the stop, the throttle comes back, a push lets go, a boost from a standstill ends at cruise).
- Mission autoplay: cabin and escort pass. **The dogfight autoplay fails its pacing gate** on the merged tree (longest quiet stretch 94 s against 30 s, victory at 639 s, only the Warden left): it passed (12 kills, quiet 3 s) on my branch before the
  merge. Moving the *heavy* gun to (0, -1.0, 4.9) failed it at 33 s even before the merge, so this scene is sensitive to where the guns are, and I put the heavy muzzle back. The side branch reported the same kind of stretch (75 s) from its own part layout.
  It goes to the AI builder.
- Perf (1920x1080, `-mnPerfSync`): braking and firing: avg 240 fps, 1% low 153 fps, worst frame 13 ms, GPU 3.14 ms; the same run not braking 216 fps, GPU 3.16 ms.
- Captures: `guns_still3/` (stationary: the flash at the corner, the bolt to the aim point), `guns_c/` and `guns_fine/` (at cruise), `game2/` (the real mission scene, the AI flying), `brake_b/` (ten stills of a brake from cruise to a stop and away), `audio/` (spectrograms).

## Shared files touched (small)
`ControlInput` (one flag), `Ships` (ShipState: three fields), `FlightSystem` (one line), `ShipFlight`/`ShipClass` (the brake), `PlayerPilot`/`ControlsConfig`/controls.json, `HudFrame`/`HudReader`/`HudRenderer`, `JuiceDirector`/`JuiceContext`/`JuiceSettings`
(+defaults), `CameraShake` (the lurch), `Projectiles` (the player's bolts and flash), `AudioRig`/`JuiceAudio` (cues), `SfxLib`/`sfx.json`/`AUDIO_LIST.txt`, the pause texts in `MissionScene`, `DogfightScene`, `CombatRange`, the sandbox, the vanguard's two json files.

## Weaknesses, said plainly
- None of the sounds, nor the stop's feel, has been tried with a pad in the hand; the numbers (0.35 s, the lurch size, the 3 and 5 degrees) are a first guess from the flight and juice maths.
- The retro jets are two glows with a streak each; there is no nozzle model (the player's ship is never drawn from the cockpit).
- Clicking R3 while steering is held by a 0.15 s hold on the aim; a very fast flick through the click would be smoothed.
- The two-thirds throttle preset is gone from the pad; missile cycling is on the D-pad where a thumb comes off the left stick to reach it.
- In the stationary capture the bolt looks like a clean line from the corner; at cruise most of its length is off the edge of the picture because the camera passes the muzzle in 45 ms.
