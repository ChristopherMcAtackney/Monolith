# Piece: juice sandbox (explosions, lasers, rumble, oomph and shake)

The user's brief: "really quite a bit of effort" on explosions, lasers, rumble, oomph and screen shake, and the "big powerful meaty
lasers everywhere" of FS2. The bar: The Expanse's space battles with FS2's lasers. The user judges by hand in the sandbox; the blind
critic compares our stills with real frames. Code: `Game/Assets/Monolith/Scripts/Juice` (own assembly `Monolith.Juice`, its
`README.md` explains the files), the sandbox in `Scripts/Game/Sandbox`, the effect shader `Resources/Juice/MN_Fx.shader`, the
event vocabulary in `Scripts/Core/CombatEvents.cs`.

## Round 1 (builder, 2026-10-24)

### What was built
- **A Juice system driven by named events.** Weapons, AI and missions raise `GameEvents.RaiseAt(CombatEvents.X, position, direction,
  subject, value, entity, other)` and know nothing else. `GameEvent` gained `Position`, `Direction`, `Other` and `HasPosition`
  (additive; old calls are unchanged). Events: `weapon_fired`, `hit_shield`, `hit_hull`, `near_miss`, `ship_destroyed`,
  `subsystem_destroyed`, `explosion`, `flak_burst`, `beam_charge`, `beam_fire`, `beam_aim`, `beam_end` (the table of who is Entity and
  Other and what Value means is in `CombatEvents.cs`). `ShipState.Mass` was added (Flight fills it) so a shockwave shoves by mass.
- **One HDRP custom pass draws all of it** (before post-processing, so the sky builder's bloom does the glow; I touched no exposure,
  tonemapping, bloom or sun settings). Every glow, streak, beam ribbon, ring, flare, fireball, scorch mark, the shield bubble and the
  screen-edge flash is a quad or mesh in `MN_Fx.shader`, depth-tested with soft edges where a fireball meets a hull. Colours are
  written in "display units at the reference exposure" (like the sky's) and multiplied by `nitsPerUnit * exposure`, so they follow
  the camera's exposure like real light. 16,000 particles and 15,000 quads of capacity; one draw call per blend mode.
- **Laser bolts**: capsule streaks with a white-hot core, saturated body and wide halo, 0.9 to 1.3 m half width, placed analytically
  from where and when they were fired (smooth at any frame rate), at FS2 speeds: player 620 m/s, enemy 520, flak 720, capital
  turret lasers 340. Muzzle flash (glow, anamorphic flare, a hot lance). An impact event near a bolt consumes it.
- **Heavy beams**: a 2.3 s charge (sparks pulled in, a swelling glow and flare, a light, rising rumble and sound), then a 4.8 s beam as
  three noise-animated ribbon layers (sheath, body, white core), drawn in segments so it never turns edge-on, sweeping a line down
  the target hull with a flare and blinding spot at the impact, a fan of sparks, a molten scorch trail and small blasts, a light that
  follows the impact, and a roar you feel in the pad when it passes near. Beams swell a little with distance so they stay fat at 3 km.
- **Flak** (shells fly, then burst: flash, two fireballs, a shell of shrapnel streaks, a ring, a light) and **missiles** (homing, with a
  glowing exhaust plume laid along the path).
- **Hits**: on shields a hex-patterned bubble (ellipsoid, 8 simultaneous ripples, a rim that rises and fades) plus a blue flash, sparks
  and a ring; on hulls a flash, fireball, sparks, embers, a dark scorch with a hot rim that cools, and every few hits a vent that keeps
  smouldering on the hull (design: visible, progressive damage; if a model marks `FX_<kind>_NN` empties the vents use them).
  Hits on the player add shake, rumble, an edge-of-screen flash that leaves the middle clear, and specks flying past the cockpit edges.
- **Explosions, layered and scaled**: flash, anamorphic flare, billowing fireballs that swell and dim (noise-eroded, blackbody ramp), a
  streak spray plus a fine spray of tiny dots (FS2's look), embers, a shock ring and a light that hits the hulls nearby. A fighter is
  one blast and lit pieces; a bomber goes off twice; a **capital ship dies in stages** (3 s of blasts building along the hull with
  venting gas and a groaning shake, then the hull cut by Voronoi cells into 7 lit sections that drift apart burning, then a final
  flash, a fireball field, 900 fine sparks, a ring, the sections shattering and a shockwave out to 3.6 km).
- **The shockwave shoves ships through `GameWorld.Knocks`** (up to 26 m/s, scaled by distance, mass and the "shockwave shove" level, with
  `Shock` set so Flight loosens the damping for about 2 s). Measured in the capital-death clip: 1,191 m away, strength 0.59, the
  player's velocity changed by 17.8 m/s and `Knock` read 0.58.
- **Feel**: `Trauma` (Eiserloh: hits add, 0.62/s decay, shake = trauma squared) applied by `CameraShake` after the view puts the camera
  on the ship: Perlin noise at 21 Hz, mostly rotation (max 4.4 deg pitch and yaw, 7 deg roll, 0.2 m shift at trauma 1, times the level).
  Measured in the shake clip (four hits on the player then two more): peak 2.3 deg pitch, 2.0 yaw, 3.4 roll. `RumbleOut` drives both
  motors from the same events (pulses that fade, plus steady levels for gun fire, the afterburner, a charging or passing beam).
  **Hit-stop** is a switch, off by default (default 70 ms at 5% speed), only when the player made the kill. Success gets the loudest
  reaction (trauma x1.55, rumble x1.3 on a kill the player makes). Medium is the default.
- **Sounds** are synthesised in code at start-up (`Synth`, `ProceduralAudio`, 21 cues, near and far versions: the far one is dull,
  smeared and late) behind `IJuiceAudio`, which the real audio system will implement. Layers per cue: a crack, a body, a low thump or a
  tail; the biggest moments duck the rest; slow motion drops the pitch. **Nobody has heard them** (see gaps).
- **The sandbox** (`-mnScene juice`): the player's fighter on the real flight model and pad, three enemy fighters and a bomber, two
  wingmen, an enemy cruiser and our destroyer (two new ship classes, `juice_cruiser` and `juice_destroyer`, with FS2's capital numbers and
  sources). Stand-in weapons (`SandboxCombat`) fire real `weapon_fired`, `hit_*` and `ship_destroyed` events so the juice is tuned
  against something that behaves like a fight; targets come back after they die. The panel (Start): EVENTS (fighter kill, bomber kill,
  capital death, beam strike, shield hit, hull hit, near miss, flak, missile, laser volley, subsystem destroyed, hits on you, repeat
  last, ambient battle, slow motion), EFFECTS (a switch per effect plus hit-stop) and TUNING (Low, Medium, High, Extreme; sliders for
  shake, rumble, flash, size, density, shove, bolt and beam thickness, volume, hit-stop and slow-motion speed). Pad and mouse, tested
  with virtual devices (`-mnPanelDrive`). LB or RB with A B X Y fire events in flight. Settings save as you tune to `juice.json` next
  to the exe (not in captures), load from there, fall back to `Resources/Juice/juice_defaults.json`.
- **Capture scripting**: `"sceneOptions": { "script": [...] }` drives the sandbox from a plan (place, face, fire, event, battle, target,
  raise), `tools/juice/make_plan.py` turns absolute times into the plan's warm-ups, `tools/juice/sheet.py` and `clip.py` make contact
  sheets, strips and clips. Specs are in `gauntlet/shots/specs`.

### Defaults (Medium; `Resources/Juice/juice_defaults.json`)
shake 1.0, rumble 1.0, flash 1.0, size 1.0, density 1.0, shove 1.0, bolt and beam width 1.0, volume 0.8, hit-stop off, slow motion 0.25.
Low is 0.45/0.45/0.6/0.8/0.6 (shake, rumble, flash, size, density); High 1.5/1.4/1.3/1.2/1.4; Extreme 2.2/1.9/1.7/1.5/2.0.

### Captures (`gauntlet/captures/juice-sandbox/r1/`, from a fresh build, all as the player sees them with the HUD)
- `stills/`: `laser_exchange` (several bolts in flight and hits), `fighter_kill` and `fighter_kill_fireball` (the flash and then the
  fireball 0.26 s later), `shield_hit`, `hull_hit_capital`, `bomber_kill` and `bomber_kill_fireball`, `flak_barrage`, `beam_charge`,
  `beam_strike`, `beam_strike_sweep`, `beam_impact_close`, `capital_death_internal`, `capital_death_breakup` (mid break-up),
  `capital_death_final`. Contact sheets: `overview_a/b/c.png`.
- Clips and strips: `capital_death.mp4` (13.1 s, 30 fps) and `capital_death_strip.png`; `shake.mp4` and `shake_strip.png`
  (hits on the player), `shake_log.csv` (the angles applied each frame), key frames in `*_key_frames/`.
- `checks/`: panel screenshots (`panel_page0-2.png`), the self-test and panel-drive logs, the rumble test, every placeholder sound as a
  WAV with its level (`sounds/`, `sounds.log`) and spectrograms (`sounds/specs.png`).
- **Best three**: `stills/capital_death_final.png` (the staged death at its peak: lit sections breaking, the fireball field, the
  shockwave ring), `stills/beam_strike.png` (a 2.4 km beam from the destroyer's flank to the cruiser, with impact glow and sparks;
  `beam_impact_close.png` is the close view), `stills/bomber_kill_fireball.png` (the layered blast with the fine spark spray).
  The capital death is better judged in `capital_death_strip.png` or the clip than in any one frame.

### Performance (`perf/`, 1920x1080, vsync off, RTX 4070 Ti, an 18 s run with the ambient battle, the player firing and a capital death at 3 s)
Quiet machine: 467.6 and 457.1 fps average, 1% low 162.6 and 162.0 fps, worst frame 16.4 and 15.1 ms, p99 5.2 ms, GPU 1.5 ms,
simulation 60.0 Hz (`perf_repeats/run1`, `run2`). Peak load about 8,500 particles and quads, 4 lights, 30 debris pieces. The worst game
frames are the final blast (spawning about 3,500 particles, 15 ms) and the hull cut (7 ms, 2,388 triangles); the hull is cut once and the
last shatters are spread one section per frame. While another builder was running Blender and Unity on the machine the same build gave
429 to 426 fps average, 1% lows of 85 and 52 fps and worst frames of 67 to 124 ms, with stalls in frames where nothing was
happening (`perf/` and `perf_repeats/run3`, whose hitch log shows them), so those are the machine, not the juice. The bar (60 fps with
large headroom) holds on a quiet machine with about 7x average headroom; on a busy one the 1% low can dip near 50.

### Rumble
Both motors work on this Windows machine (the pad is an `XInputControllerWindows`; `-mnRumbleTest` commanded both without error and the
log says so). **Trigger rumble is not possible**: the Input System only reaches the Xbox impulse triggers through `IXboxOneRumble`, which
no Windows device implements (XInput has two motors; triggers need GameInput or Windows.Gaming.Input). The game says so
(`RumbleOut.TriggerRumbleSupported` is false) and carries gun fire on the light motor instead. The builder cannot hold the pad, so how
the rumble feels is untested by hand.

### Honest read against the reference frames
- **Bolts and beams (FS2 `heavy_beam_fire`, `gunfire_and_tracers`)**: the same family. FS2's modern-mod beams are a very hot white core
  inside a saturated sheath, straight, thick near the camera and a bright thin line far away; ours match that and add a noise-driven
  sheath. Ours are not yet as dense: FS2's battle frames have eight or more beams and dozens of bolts on screen, and our ambient battle
  runs a fraction of that. Bolts are slightly "soft sausage" next to FS2's crisp ones; the thickness slider helps and the user should
  judge.
- **Explosions (FS2 `explosion_capital`, `explosion_small`)**: FS2's capital explosion is a huge, opaque, billowing yellow-white
  fireball that fills the frame, with a mass of tiny sparks. Ours has the sparks right (the fine spray was added after looking at the frame)
  and the colour ramp, but the fireball is smaller on screen at the default 1 km viewing distance, more translucent, and built from
  flat noise sprites with no internal shadowing or depth, so the biggest blasts read as stuck-on stickers more than as volume. It
  also dims faster than FS2's (that is deliberate: The Expanse's vacuum blasts do).
- **Explosions (The Expanse `explosion_capital`, `explosion_small`)**: the Expanse's are blue-white flashes with a long horizontal
  streak and a big bloom, a fast dim and debris with hot spots, no smoke. We have the flash, the anamorphic streak, the fast dim and
  hot debris; ours default to orange fire and a white core with blue shock rings, not the Expanse's blue grade, and the haze and bloom
  belong to the camera look, which I did not touch.
- **What the stills cannot show**: the ships are the flight playtest's placeholder hulls, which look like grey boxes next to real
  ships and dominate every frame. The capital break-up cuts them along Voronoi cells at triangle level, so the sections have spiky
  triangular edges rather than torn plating and are hollow shells (each triangle gets an inner skin so they are not paper-thin).
  Real Blender models with sub-object debris will break better.

### Biggest weakness
The fireball itself: flat 2D noise sprites that are fine for the small and medium blasts but not yet the volumetric, billowing mass
that FS2 and the Expanse give a capital death. Second: the hull break-up shapes (spiky Voronoi shards on placeholder boxes). Third:
nobody has heard the sounds, and the pad rumble has not been felt.

### Known gaps and what to do next
- Hear the sounds and feel the rumble (the user's playtest decides); the synth cues are `Audio/ProceduralAudio.cs`.
- Fireballs: try several noise slices blended over time (a flipbook of volumetric slices), soft-lit with the hull light, and larger
  default size for capital deaths. The size slider already scales them.
- Break-up: when real ship models exist, break along their own sub-objects (the ship art builder's `ShipSubsystem` and `ShipDamage`
  already name the parts and `ShipFxHook` the vents); the juice already reads `FX_<kind>_NN` empties, and `JuiceDirector.RegisterShip`
  takes the visual.
- Shield bubble sizes come from a fixed ratio of the visual's bounds; real ships should give their own radii.
- Wiring to real weapons: they raise the events above; the juice keeps its own cosmetic bolts, and an impact event near a bolt ends it.
- Scorch marks are quads at the hit point (they do not wrap curved hulls and do not travel with a moving hull); a decal projector would.
- Muzzle positions and beam charge for real capital ships should come from their models; the sandbox picks them from the hull bounds.

### Gotchas found
- **A `MaterialPropertyBlock` on an HDRP Lit renderer broke its rendering** (heat via `_EmissiveColor` made whole hull pieces blow out to
  white-orange at a tiny emissive value). Heat now goes on a per-piece copy of the material (`new Material(source)`, which keeps the
  keywords the build already has), and the copies are destroyed with the piece, never shared with the pieces cut from it (that gave
  magenta error materials).
- No new HDRP/Lit material assets were needed (pieces reuse the flight playtest's), and the effect shader is loaded with
  `Resources.Load`, so `Setup.Project` needed no change.
- The shared project could not compile for a while because another builder's files were mid-edit; I built and captured in a private git
  worktree with the same files until it was clean again, and the final commit is from the main tree.
- The machine is shared: perf numbers swing when another builder runs Blender or Unity (see above); run them on a quiet machine.

### How to run
- Sandbox: `Playtest\Monolith_2026-10-24_juice\Play.bat` (`JUICE.txt` there explains the panel), or `-mnScene juice`.
- Captures: `tools\capture.ps1 -Plan gauntlet\shots\juice_sandbox.json -Out ...`; the other plans `juice_capital_clip.json`,
  `juice_shake_clip.json`, `juice_perf.json` come from `gauntlet\shots\specs\*.json` with `python tools\juice\make_plan.py spec plan`.
- Checks (player flags): `-mnJuiceSelfTest`, `-mnPanelDrive`, `-mnRumbleTest`, `-mnDumpSounds dir`, `-mnPanelShot file.png`,
  `-mnShakeLog file.csv`, `-mnJuiceHitchLog`, `-mnAudioMute` (run the sound paths silently).
