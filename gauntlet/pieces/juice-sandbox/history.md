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
  `juice_shake_clip.json` (generated, 394 and 100 stills, so not committed) and `juice_perf.json` come from `gauntlet\shots\specs\*.json` with
  `python tools\juice\make_plan.py spec plan`.
- Checks (player flags): `-mnJuiceSelfTest`, `-mnPanelDrive`, `-mnRumbleTest`, `-mnDumpSounds dir`, `-mnPanelShot file.png`,
  `-mnShakeLog file.csv`, `-mnJuiceHitchLog`, `-mnAudioMute` (run the sound paths silently).

## Round 1 verdicts (lead, 2026-10-24)
Two blind critics, each with fresh context.

**Realism vs The Expanse: lost 3/3, all certain.** Pairs: capital_death_final vs explosion_capital/expanse/7wFt319UB2o_0035; bomber_kill_fireball vs explosion_small/expanse/7_ATYOm3nNc_0191 (2:1); laser_exchange vs gunfire_and_tracers/expanse/XuqEX1PnG9I_0002 (2:1).
- Giveaways: fireballs are smooth, airbrushed sprites with no turbulent volumetric structure or motion blur; blasts and bolts put no hot light on nearby hulls and debris (no spill); sparks are evenly scattered dashes of the same length and brightness, with no depth or blur; bolts are constant-width capsules where the show's tracers are thin, tapered and blurred.
- Lead's fault: the captures had the HUD on, and the critic called the HUD "an instant giveaway before any of the VFX". Look captures must be HUD-off from now on.

**Meatiness vs FS2's own beam battles: lost 3/3 (certain, likely, certain).** Pairs: beam_strike vs heavy_beam_fire/fs2/big_battle_comparison_0187; laser_exchange vs gunfire_and_tracers/fs2/big_battle_comparison_0110; capital_death_final vs explosion_capital/fs2/big_battle_comparison_0132 (all cropped to 2:1).
- Giveaways: our beam is a dim green smear, where FS2's is fat and saturated with a white-hot, hard-edged core, flaring edges and a big hit flare; our bolts are short glow-stick dashes with no sustained hammering on a hull; our capital death is a decent burst but small, and the ship stays crisp and readable "like a cutout with a sticker on it", where FS2's fireball swallows the whole hull.

**Both critics point the same way.** Explosions several times bigger and brighter, with a blown-out core, turbulent volumetric structure and real light spill onto nearby surfaces, so the ship disappears into the blast. Beams fat and saturated with a white-hot core, a big flare where they hit, and visible sustained hammering. Sparks and debris with depth and motion blur. These are pointers, not a recipe.

## Round 2 (builder, 2026-10-24)

Both critics' notes pointed the same way (bigger, brighter, volumetric explosions that light what is near; fat saturated beams that hammer; bolts and
sparks with depth; no HUD in look captures; use the real corvette), so one pass answered both. The notes were pointers; the fixes below came from
the reference frames (Expanse `7wFt319UB2o_0035`, `7_ATYOm3nNc_0191`, `XuqEX1PnG9I_0002`; FS2 `big_battle_comparison_0132`, `_0187`, `_0110`).

### What changed
- **Fireballs are raymarched turbulent volumes** (`ShadeFire` in `MN_Fx.shader`, 16 steps, white-noise jitter so the edges grain instead of
  hatching). A 64 cubed tiling `Texture3D` of Perlin-Worley, Worley and Perlin noise is built in code at start-up (`Fx/FxNoise.cs`, about 60 ms),
  warped once and read once per step, so lobes billow out of the ball (cauliflower edges, hard-edged dark pockets) instead of a smooth sprite.
  Temperature drives a blackbody ramp: a blown-out white core, yellow, orange, red at the edges; the ball is opaque where dense so it swallows what
  is behind it, and as it cools it erodes into hot lumps and goes (gas that has cooled thins out; it does not hang as a smooth red cloud). Fireballs
  are drawn far to near; a ball's quad is grown so that close to the lens it still covers its sphere.
- **Explosions several times bigger**: a fighter's blast radius 30 to 52 m, a bomber's 56 to 96 m (two blasts), a part blasting off a corvette 16 to 80 m,
  a capital's final blast as big as the ship is long (220 to 250 m radius for the 374 m corvette, up to 800 m for a big hull) with fireballs spread
  through the space the hull filled (not a sphere round its middle), a core flash far above the bloom threshold and an anamorphic flare.
- **Real light (spill)** on nearby hulls and debris: blasts carry a point light of `4e6 x R^2` candela (the final blast `2e6 x R^2`, range 5.2 km) and beams
  light the hull at the impact (`1.6e10` candela) and along their length. The Round 1 spill was there but invisible because the camera sat with the
  Sun behind it, washing the hull out; a lights-on, lights-off test with the camera looking toward the Sun showed the hull taking the orange. The capture
  plans now put the camera where the hull is backlit or in shade for the death and the beam. No lighting, exposure or post setting was touched.
- **Beams**: one fat ribbon (`Beams.UpdateFire`, shader kind 7): a white-hot hard-edged core (0.4 of the body radius), a saturated body with a ragged
  flaring edge, a soft glow, edge licks (short bright streaks peeling off the sides), round ends, noise that flows along it (half width 16 m, swelling with
  distance). At the hit: a blinding white spot, a coloured halo and an orange halo, a wide flare with a tall spike, and **sustained hammering**: about
  520 sparks a second thrown in every direction at every depth, a fireball burst six to nine times a second, a bigger blast now and then, molten dots
  left behind the beam. Halos are held to a fraction of the view so one beside the lens cannot cover it.
- **Bolts**: long (85 to 90 m), thin (0.9 m half width), tapered to a point behind a hot white head with a saturated body; capital turret bolts 230 m long.
  **Hit flares**: a hit now flashes with an apparent-size floor (never smaller than 1.4 to 6 per cent of the view at its distance), so a hit a kilometre away is
  still a clear flare, on shields and hulls.
- **Sparks** vary in depth (a sixth fly at the camera, long and soft; the rest away, small and sharp), size (log-normal), shutter (14 to 55 ms, so streak
  length varies), brightness (e^-1.1 to e^0.35) and blur (`FxParticles.Particle.Blur`: hard saturated to soft smear). Tapered to a point, like the show's.
- **HUD-free looks**: `"sceneOptions": { "hud": false }` (or `-mnNoHud`) leaves the HUD out; every look plan uses it. `"layoutYaw"` turns the whole
  sandbox layout (ships, player start, scripted places and faces) so the Sun can be behind or in front.
- **The real Earth corvette** replaces the placeholder hulls as the two capital ships (`Model = "earth_corvette"`, class `juice_corvette`: FS2 corvette numbers):
  - `IJuiceShipDamage` (`Fx/ShipDamageHook.cs`) is what the juice may ask of a model with real parts; `Game/Sandbox/ShipArtJuiceDamage.cs` adapts the ship
    art's `ShipDamage` to it, so the juice never references the art. `JuiceDirector.RegisterShip(id, visual, shield, damage)`.
  - **Hits on the hull** paint the model's own scorch, crater and streak decals and open its damage sites (`AddHullHit`) instead of a flat quad; a beam
    does it along its track every 0.1 s. **Beams blow parts off**: every part within the beam's width is destroyed (its wreck shows, a blast sized to
    it, its debris chunks fly at 9 times the model's slow-reveal speed with a fire trail; `Explosions.DestroyPart`). `subsystem_destroyed` does the same
    to the part the event names (else the nearest weapon). Bolts and beams hit the model's collision proxies (`HullGeometry.Raycast`), not a box.
  - **The capital death uses the real parts**: internal blasts erupt at parts, and parts go one after another (weapons and sensors first, engines,
    reactor and bridge last) with the hull scorched to 0.6 at the start; at the break the **hull splits along cuts baked for the model**. Ship models
    import unreadable (`ShipArtSetup.OnPreprocessModel`), so a built game cannot cut them; `Monolith.EditorTools.JuiceSetup.BakeSections`
    (`tools\unity.ps1 Monolith.EditorTools.JuiceSetup.BakeSections`, 0.5 s) reads them in the editor and cuts every hull mesh into 8 ragged sections along
    the ship's length, saved as one 3.7 MB asset per ship (`Resources/Juice/Sections/<ship>.sections.asset`; a mesh name carries the source's name and vertex
    count so a stale bake is never used). At the break the juice swaps the intact hull for the sections (with the model's materials and an inner skin,
    made once at spawn), moves each real part and each scorch decal to the nearest section, and the final blast throws the sections whole (cutting
    them again cost 20 to 75 ms in a frame) and flings the parts still on them through the fireball. Without a bake the parts still fly and the hull
    vanishes into the blast.
  - No art file was edited; ship models stay unreadable.
- **Smaller fixes found on the way**: scorch marks now travel with their ship and last one second on a small ship (they used to hang in space as black dots where a
  fighter had been); cooled gas is a lit pale puff, not a black blob; a shockwave reaching you is a faint warm edge glow, not a grey haze; the shockwave ring
  and the blast ring are much fainter; the first break-up no longer has a 31 ms frame (the baked cuts are loaded and doubled at spawn).

### Captures (`gauntlet/captures/juice-sandbox/r2/`, from a fresh build, **no HUD**)
- `stills/`: the same names as r1: `laser_exchange`, `fighter_kill`, `fighter_kill_fireball`, `shield_hit`, `hull_hit_capital`, `bomber_kill`,
  `bomber_kill_fireball`, `flak_barrage`, `beam_charge`, `beam_strike`, `beam_strike_sweep`, `beam_impact_close`, and **`capital_death_internal`,
  `capital_death_breakup`, `capital_death_final` with the corvette**. Contact sheets `overview_a/b/c.png`.
- `capital_death.mp4` (10.6 s, 30 fps), `capital_death_strip.png`, `capital_death_key_frames/`: the corvette losing its parts, breaking into its real hull
  sections and dying in a blast that fills the frame. `shake.mp4`, `shake_strip.png`, `shake_log.csv` as in r1. `checks/` as in r1.
- **Best three**: `stills/capital_death_final.png` (the corvette's death: a blown-out volumetric fireball filling the frame, the hull's own parts flung through
  it and a fine spray of sparks at every depth), `stills/beam_strike.png` (the fat saturated beam with its white core and flaring edges hammering the corvette:
  sparks, fireballs, the hull lit by the blast), `stills/fighter_kill_fireball.png` (a 52 m blast 0.26 s in: billowing lobes, a white core, sparks varying in
  depth and blur).

### Performance (`perf/`, `perf_repeats/`, 1920x1080, vsync off, RTX 4070 Ti, the same 18 s run as r1: ambient battle, firing, a capital death at 3 s)
477.9 fps average, 1% low 132.6 fps, worst frame 16.4 ms, p99 6.3 ms, GPU 1.7 ms, CPU main thread 2.1 ms. Two repeats while other builders were running Unity on the
machine: 456.4 and 345.3 fps, 1% lows 116.7 and 103.1, worst 49.5 and 14.8 ms. Peak load about 12,000 particles and 11,000 quads (the final blast), 12 lights, 85
debris pieces. Headroom is about 8x at 60 fps. A first version of the final blast shattered the baked sections with the Voronoi cutter: 20 to 75 ms frames with up to 16 gen0
collections; throwing them whole fixed it (worst frame 75 ms to 14 ms).

### Honest read against the reference frames
- **Explosions (Expanse and FS2 capital frames)**: closer than r1 by a long way. The final blast has the turbulent, hard-edged, billowing, blown-out look with dark pockets
  and a spark spray with depth; it envelops the hull and lights it. Still short of the references: the interior is mostly uniform pale yellow-white where FS2's has more orange and red
  structure inside, and the ball is a little too clean at its edge next to the Expanse's ragged flat-lit cloud; the Expanse's blue-white grade is not used (orange fire).
- **Beams (FS2 `heavy_beam_fire`)**: fat, saturated, hard white core, flaring edges, big hit flare and constant hammering. The flaring edge is a repeating comb of tall teeth
  (noise with one dominant wavelength), where FS2's is rougher; the body is smoother than FS2's particle-noise texture. Beams are 16 m half width, fat against a 74 m corvette; against
  a kilometre-wide ship they would want to be fatter, which the beam width slider does.
- **Bolts (FS2 `gunfire`, Expanse tracers)**: long, thin, saturated with a hot head; the Expanse's blurred, pink-white tracers are not copied (ours are crisp, FS2's way).
- **Spill**: visible on the corvette (orange on the hull at a hit, the beam's white-green on the plating, blue-grey sections lit by the Sun and orange by their fires); it is
  strongest when the hull is backlit or in shade, and weak when the Sun is behind the camera and already lights the hull.
- **The shield hit still is weak** (a pale bubble around a fighter with a short flash; the flash is 0.14 s and the shot lands near its end). Unchanged in spirit from r1.

### Gaps and what to do next
- The break-up sections are hollow shells (the second skin helps, but a torn edge shows no interior structure), and their cut edges are flat, ragged planes, not torn plating.
- Part wrecks and debris fly well but have no heat glow (they are the model's own materials); the heat glow only reaches Voronoi pieces.
- The edge-of-screen flash (when the shockwave reaches you) is still a faint warm haze at the frame's corners in the `capital_death_final` still.
- Beams: break the tooth pattern with a second incommensurate noise; give the hit a shower of molten dripping. Flipbooks (precomputed volume slices) would let the fireball cost less.
- Only the Earth corvette has baked cuts; run `BakeSections` after any model change, and for each new ship model.

### Gotchas found
- Shared files (`Projectiles.cs`, `JuiceDirector.cs`) also carry the weapons builder's missile-tracking work (`OnTrack`, `missile_track`) and `hammer` weapon looks; the Round 2 commit
  stages only the hunks that are mine, so it builds against the committed `CombatEvents.cs`.
- Ship models import with `isReadable = false`: `Mesh.vertices` works in the editor (so the bake works) and logs nothing, but the same call fails in a built player.
- A `LODGroup` that is disabled lets every LOD show at once; hiding a model means disabling its renderers explicitly.
- Reparenting a model's parts out of its root before the root is destroyed keeps them; `ShipDamage` lives on the root and is gone a frame after `ship_destroyed`, so everything that
  needs it (the hull-damage ramp) must be done in that frame.
- Three captures ran at once by mistake (a backgrounded script that kept running after its tool call returned) and corrupted each other's output folder; run each capture script once, in the foreground.

## Round 2 verdicts (lead, 2026-10-24)
**Meatiness vs FS2: WON 3/3** (all likely). The critic picked ours as meatier in every pair: beam_strike vs heavy_beam_fire/fs2/big_battle_comparison_0220; laser_exchange vs gunfire_and_tracers/fs2/clash_of_titans_ii_0096; capital_death_final vs explosion_capital/fs2/big_battle_comparison_0099. What FS2 lacked, in its words: beams that never hit anything, bolts with no impact flash, and a fireball with no hot core or structure. One more win against fresh FS2 frames completes the meatiness bar.

**Realism vs The Expanse: lost 3/3** (all certain). Pairs: capital_death_final vs explosion_capital/expanse/7_ATYOm3nNc_0147 (2:1); fighter_kill_fireball vs explosion_small/expanse/7_ATYOm3nNc_0141 (2:1); laser_exchange vs gunfire_and_tracers/expanse/7_ATYOm3nNc_0112.
- Giveaways: fireballs are opaque, hard-edged "cauliflower" silhouettes sitting on dead black, where the show's fire is translucent, motion-blurred and edgeless; still almost no warm spill on nearby hulls from a blast that big; sparks sprayed evenly over the whole frame; debris reads as small round grey blobs.
- **Lead's call on the conflict:** the realism critic also marked our bolts down as "fat green capsules" against the show's small, dotted tracers. The user's bar is "The Expanse with FS2's big, meaty lasers everywhere", and the meatiness critic preferred our thick bolts. So bolts stay thick and bright, and realism is judged on everything else.
- Gap for round 3: fireball edges that are translucent, soft and motion-blurred, with no hard silhouette; blast spill light that visibly warms nearby hulls (rim and cast); spark distribution concentrated and varied, not an even spray; debris with real shapes; check CameraLook (graded blacks, haze) is on in the juice scene.

## Round 3 (builder, 2026-10-24)

The brief: keep what won the meatiness bar (fat bright bolts, big beams that hit and hammer, a fireball with a hot core and structure) and close the realism gaps the critic
named: fireball edges, spill light, sparks, debris, and the camera look. Reference frames: Expanse `7_ATYOm3nNc_0147` (a capital blast: translucent amber gas streaked by
motion, objects showing through it, no edge), `_0141` (a small blast: a hot core in a huge soft haze, thin sparks, a dark chunk silhouetted), `_0112` (dotted tracers, glowing
debris chunks with their own small halos, a warm dusty haze over everything).

### What changed
- **Fireball edges are gone.** `ShadeFire` now feathers the density over a wide range (`pow(saturate(mask / 0.85), 1.6)` and a fade toward the sphere's rim, extinction 7.5 to 5.2), so the fire thins
  out into the dark and stars show through its edge; the rim of the sphere never shows. **Motion blur**: the noise is blended with a second read taken along the direction from the
  centre (varies round the ball, hardly at all along a ray), mixed in more as the ball ages, so the detail is smeared radially the way a blast flings its fire.
  The ramp is cooler (`T` x 0.7) so the ball is amber with a white-hot core and visible lobes and dark pockets, not a yellow-white disc; the core stays blown out.
- **A warm veil round every blast** (`Emitters.Blast`: a soft glow 6.5 radii wide at a third of the core's strength): the show's small blast is mostly a hot core in a big warm haze.
- **Spill is visible now.** Blast lights are 9e6 x R^2 candela (they were 4e6; the Sky's fill dropped to 2,200 nits with no fill on the lit side, so a shaded hull is dark and shows any warm
  light), warmer (1, 0.66, 0.34), and last longer (0.6 + 0.008 R seconds, decay 1.25); the final blast is 6e6 x R^2. The `blast_near_hull` still (a 70 m blast beside the corvette, seen from its
  shaded side) shows the whole near hull warmed orange, brightest on the face toward the blast, falling off along the hull. The kill stills are now shot from the shadow side too.
- **Sparks are concentrated and varied** (`Emitters.SparkBurst`): three to six jets, each its own cone (6 to 30 degrees), share, speed and brightness, plus a thin scatter (12 per cent), used by every
  blast, the fighter, bomber and final blast; the camera-facing share applies to the all-round spray only. Beam hammering already used cones.
- **Debris has real shapes** (`Debris.Shards`): torn plates, thicker plates, struts, chunks and wedges, jagged and flat-shaded, each its own size (many small, a few big), cut from the dying ship's own
  plating (`ShipLook.HullMat`, found at registration), tumbling, glowing at the edges while hot (heat copy of the material) and throwing a small halo of light of their own. Fighters throw 16 shards of
  1.2 to 5 m, bombers 26 of 1.6 to 7 m, every part destroyed on a modelled ship 7, a big capital blast 8, the final blast 60 of up to 17 m, and a hit on a capital 2 to 4 flakes. The vent gas that read as
  round grey blobs is now a soft translucent puff with the same shader, and its colour is lit, not grey.
- **CameraLook is on in the juice scene** (the log says `LOOK on: intensity 1, glare on, grain 0.014`; `SkyRig.Enable` turns it on by default and the sandbox does not turn it off). The floor shows in the
  stills: the darkest 5 per cent of a quiet frame is about (1, 2, 4) of 255 and (5, 9, 15) beside a blast. Because the kill stills now look toward the Sun, the glare, dust discs and haze are in them.
  I did not change the look's own options (they belong to the sky builder).
- **Kept as they were**: bolt thickness and brightness (the lead's call), beams, the capital death and the real corvette parts and baked cuts.
- Smaller fixes: the beam ribbon's segments are longer than the ribbon is wide (a segment shorter than that painted a hard vertical bar at close range); the flak burst is a small flash and a soft puff, not
  a flat white disc ringed by a shock ring; scorch marks and the shockwave's edge glow as in round 2 but fainter.

### Captures (`gauntlet/captures/juice-sandbox/r3/`, from a fresh build, no HUD)
- `stills/`: the r2 names plus two new ones: `fighter_kill_debris` (about 1.3 s after the kill: the shards, with their halos) and `blast_near_hull` (the spill). The kill and hull-hit stills are now taken
  from the shadow side, toward the Sun. Contact sheets `overview_a/b/c.png`.
- `capital_death.mp4` (10.6 s), `capital_death_strip.png`, `capital_death_key_frames/`, `shake.mp4`, `shake_strip.png`, `shake_log.csv`, `checks/` (self-test, panel drive and rumble test pass).
- **Best three**: `stills/blast_near_hull.png` (spill: a soft-edged amber blast with concentrated spark jets warming the whole near hull), `stills/fighter_kill_fireball.png` (translucent, radially smeared
  amber fire with a hot core and stars showing through its edge; jets of sparks; plates and wedges inside it), `stills/capital_death_final.png` (the corvette's death with the sections thrown through it).

### Performance (`perf/`, `perf_repeats/`; 1920x1080, vsync off, RTX 4070 Ti; the same 18 s run)
445.2 fps average, 1% low 133.7, worst frame 14.8 ms; two repeats 427.4 and 455.3 fps, 1% lows 127.9 and 139.9, worst 15.4 and 13.9 ms. The fireball reads the 3D noise twice per step now (isotropic
and radial); the numbers are within the run-to-run noise of round 2 (478, 456 and 345 fps average then, the last while another builder had Unity open): about 7x headroom at 60 fps.

### Honest read against the reference frames
- Fireballs are now translucent and edgeless with radial smear, but the smear is a noise trick on a sphere; the show's gas has long streaks across the whole frame (real frame motion blur) that this does
  not produce, and its blasts are far larger in frame. The core is still blown out more than the show's.
- Spill is strong on a shaded hull; on a sunlit hull (the camera behind the Sun) it is still drowned, as light should be. The warm veil can look like a coloured fog when several blasts overlap.
- Shards read as shapes only when close or large; at 170 m the small ones are a few pixels. Their halos help. The vent puffs are still pale clouds.
- Sparks are concentrated, but every jet is the same kind of streak; the show's sparks include fat slow dots and some with a curve.
- The shield hit (a pale bubble) and the beam's regular comb of edge teeth are unchanged.

### Gotchas found
- The shared tree broke twice while the AI builder had files half edited (a build failed with a script-layout mismatch after `ShipState` changed; later `AiDirector.cs` referenced types that did not exist yet).
  I built and captured in a private git worktree (`git worktree add`, the Library copied with robocopy, my modified files copied over) and removed it afterwards; the commit is from the main tree with explicit paths.
- A heredoc with apostrophes (`it's`) fails in this shell tool's bash; write the file with the editor tool, then run it.

## Round 3 verdicts (lead, 2026-10-25): one critic per pair
**Meatiness vs FS2: WON 2/3. This completes the meatiness bar** (round 2 won 3/3; round 3 against different FS2 frames won 2/3).
- r3a beam_strike vs heavy_beam_fire/fs2/big_battle_comparison_0113: ours meatier (likely). FS2's hairline beams have "no mass or impact".
- r3b laser_exchange vs gunfire_and_tracers/fs2/clash_of_titans_ii_0140: ours meatier (likely). Fat, white-cored bolts with a spark and debris burst.
- r3c capital_death_final vs explosion_capital/fs2/clash_of_titans_ii_0179: **FS2 meatier** (likely). Ours is "a pretty firework": a soft, translucent cloud with no white-hot core and no dense mass, while FS2's blown-out core obliterates the target. **Round 3's translucency, done for realism, cost the capital death its weight.**

**Realism vs The Expanse: lost 3/3, but every call dropped from certain to likely.**
- r3a blast_near_hull vs explosion_small/expanse/7_ATYOm3nNc_0160: a saturated blob with a blown core ringed by evenly sized, evenly spaced dash sparks; lens-flare ring and streak overlaid; the ship shows no specular response to the blast.
- r3b fighter_kill_fireball vs explosion_small/expanse/7_ATYOm3nNc_0142: hundreds of near-identical razor-sharp orange dashes spread evenly over the frame; a saturated, crisp-edged ball against the show's pale, murky, graded bloom.
- r3c capital_death_final vs explosion_capital/expanse/7_ATYOm3nNc_0182: a pile of hot sprite bursts with spiky edges and uniform dashes "like a stock particle emitter"; no smoky mass or occlusion.

**Gap for round 4 (lead): resolve realism against meat through time.** Each blast opens dense, opaque and blown-out white for its first moments (the meat critic's obliterating core), then breaks into translucent, motion-blurred, cooling fringe (the realism critic's fire). Sparks: far fewer, varied in size, brightness, length and blur, some dark tumbling debris among them; never an even field of identical dashes. Fire colour: less saturated, paler and murkier as it cools. Specular response to blasts on nearby hulls. Bolts and beams stay as they are, since they won.

## Round 4 (builder, 2026-10-25)

The brief: keep the beams and bolts exactly as they are (they beat FS2 in two blind rounds), and resolve realism against meat through time: each blast opens dense, opaque and blown-out
white (the meat critic's obliterating core), then breaks up into translucent, smeared, cooling fire that goes paler and murkier (the realism critic's fire). Sparks: far fewer and varied.
The fire less saturated; no spiky star edges; a specular response on nearby hulls; no lens ring and streak on a blast. Reference frames: Expanse `7_ATYOm3nNc_0160` (a hit on a hull: the
fire is a pale pink streak-blurred fan, the hull is lit and shows a specular sheen), `_0142` (a pale, murky, graded bloom with about a dozen sparks, a few of them fat and slow, one dark tumbling
chunk), `_0182` (a capital: a blue mass with dark tumbling wreckage showing through); FS2 `clash_of_titans_ii_0179` (the opening: a solid blown-out white mass that fills the frame, a red-orange edge, a field of fine sparks).

### What changed (beams and bolts untouched)
- **A blast has a life in two parts** (`ShadeFire`). For its first fifth the ball is dense and opaque (extinction 9.5, a solid mask, hotter by half, 1.55 times brighter), a blown-out
  white mass with only a thin orange edge: it hides what it hits. After that the density feathers out (extinction down to 3.8), the radial smear takes over (0.12 to 0.55 of the noise read
  along the radius) and the ball drains toward a warm grey (up to 70 per cent) and dims to 0.62: paler and murkier as it cools. The rim is lumpy (a different radius in every direction)
  and dimmer than the middle, and the ball fades before its quad edge, so there is no circle, no straight cut and no bright hard line round it.
- **The capital death's final blast** is one central ball the whole size of the blast plus 11 lobes through the space the hull filled (it was 40 fireballs: "a pile of hot sprite bursts"),
  all opening white and dense, so the corvette is swallowed (`capital_death_final`), then it thins and cools over the next seconds (`capital_death_cooling`, new still). 100 shards of up to 22 m fly out of it.
- **Sparks, far fewer and all different** (`Emitters.SparkBurst` and `SparkVaried`): a blast throws 18 to 110 (it threw 80 to 800), a final blast about 76 (it threw 390), in two to four jets and a few lone
  sparks. Every spark differs: size from hair-thin to fat (a factor of 9), length from a dot to a long streak (a factor of 37 in shutter time), brightness from a barely-there ember to a brilliant one
  (a factor of 9), speed log-spread, blur (45 per cent are soft smears), and colour (14 per cent white-hot heads cooling to pale pink, 20 per cent dull embers, the rest orange). The fine specks are 380 (900).
- **Dark tumbling debris among the hot**: 45 per cent of shards are cold (no glow, no halo): they show only by the light that falls on them, as the dark chunks in the show's frames do. Fighters throw 22, bombers 32.
- **Specular response on hulls**: the blast's light now starts white-hot and goes amber (a white light draws a hard glint on a hull; amber warms it), and it has a size (a light with a radius gives a broad bright highlight
  on a plate, where a point gives a pinprick that a rough plate hides). `LightPool.Flash(..., endColour, shapeRadius)`.
- **No lens ring and streak over a blast**: the blast ring is gone (blasts, flak and the final blast), the anamorphic flare is a short, faint streak that does not outlast its flash (it was 7.5 radii wide for 0.2 s), and the
  flak flare is smaller.
- **The shared Look defaults changed under me** (corvette round 3: ghosts fade as the Sun leaves the frame, a quieter floor, less grain): nothing to do on my side; the stills are shot with them.

### Captures (`gauntlet/captures/juice-sandbox/r4/`, from a fresh build, no HUD)
- `stills/` as in r3 plus `capital_death_cooling` (the same blast 0.6 s later: thinning, murkier, wreckage showing through). `capital_death.mp4`, `shake.mp4`, `perf/`, `perf_repeats/`, `checks/` (all pass).
- **Best three**: `stills/capital_death_final.png` (the opening: a solid white mass swallowing the corvette), `stills/capital_death_cooling.png` (the same blast cooling, translucent and murky, wreckage through it),
  `stills/fighter_kill_fireball.png` (a small blast mid-life: pale amber lobes, a few varied sparks and dark and hot shards).

### Performance (`perf/`; 1920x1080, vsync off, RTX 4070 Ti; the same 18 s run)
486.1 fps average, 1% low 214.6, worst frame 10.8 ms; repeats 488.8 and 434.7 fps, 1% lows 217.3 and 139.9, worst 11.3 and 10.6 ms. Faster than round 3 (445 average, worst 14.8 ms): there are about a quarter
as many particles and far fewer fireballs in a death.

### Honest read against the reference frames
- The opening is now a solid blown-out mass and the afterglow a thin murky one; the join between them is a quick change (about 0.15 of the ball's life), and in one frame the ball can look like a
  glowing balloon at the moment the opaque mass has just formed. The show's blasts never show a full silhouette at all.
- The cooling fire is a pale amber, not the show's grey-pink or blue; the ramp is still a fire ramp. The radial smear is a noise trick: real long streaks across the frame are not there.
- Sparks are a few dozen and differ, but each is still a straight streak; the show's include slow fat dots with a curve and some that fade by cooling in colour along their length.
- Specular: the light is white-hot at the start, but I could not prove a glint on the corvette's plates in a still: the first 0.1 s is a white wash round the blast. The `blast_near_hull` still shows the warm side
  and the highlight, not a distinct specular line.

### Gotchas found
- The fire quad is a camera-facing square: a ball bigger than the screen section it covers is cut by the quad's edge as a straight line unless its density fades before the edge (`quadFade`).

## Round 4 verdicts (lead, 2026-10-25): one critic per pair
**Realism vs The Expanse: 1/3, the first realism pair won.** All three calls were "likely".
- r4a capital_death_final vs explosion_capital/expanse/7_ATYOm3nNc_0174: **WON.** The critic took ours for the real frame and the show's blue flare wash for "a single procedural radial glow".
- r4b capital_death_cooling vs explosion_capital/expanse/7_ATYOm3nNc_0176: lost. A clean orange-white blob with a smooth gradient and a soft smoky rim; "it isn't lighting anything" (no light on nearby hull fragments); tiny uniform orange dots across the starfield.
- r4c fighter_kill_fireball vs explosion_small/expanse/7_ATYOm3nNc_0172: lost. "The orange sparks": tiny, near-identical crisp dots and streaks scattered evenly over the whole frame, into the far corners, with no falloff from the blast. A puffy sprite-style fireball with a soft cutout silhouette.
- Lead's note: the spark count went down a lot, yet two critics still see an even field of tiny orange dots reaching the corners. Find out what those dots are. The likeliest suspects are the 380 fine specks flung far from the blast, or space dust or stars picking up the blast colour. Whatever they are, they must not reach the frame corners as an even field.

**Meatiness check, capital death vs FS2 big_battle_comparison_0083: lost** (likely). Ours is "one smooth, soft-edged beige blob" with a hard white core. FS2 has churning orange-red fire layers, dark smoky contrast and billowing lobes. Round 4 fixed the opaque opening, but its colour is beige.
- Meat bar status: still won (two winning rounds running already); this check guards against regression.

**Gap for round 5:** the dense opening phase should be churning orange-red layers with dark smoky pockets and lobes (not beige); pale and murky only in the cooling phase. Blast light that visibly lights nearby hull fragments in the cooling stills. Kill the far-flung even field of tiny orange dots. Fireball silhouettes that don't read as soft cutouts.

## Round 5 (builder, 2026-10-25)

Beams and bolts untouched. The brief's four gaps, and what I found. Reference frames: FS2 `big_battle_comparison_0083` (the opening of a capital blast: a white-hot core about
40 per cent of the width, surrounded by layers of churning orange and red fire with dark red-black gaps between billows, a ragged lobed edge, a field of fine sparks thickest near the blast).

### 1. The dots: what they were
Two things, both mine, and the spark counts were never the cause.
- **A minimum on-screen width.** The effect shader holds every streak to a minimum width and length in pixels (that is what keeps a far bolt a line). A spark, or one of the 260 to 900 fine
  specks, a kilometre away is thinner than a pixel, so it was widened to the minimum and drawn at full brightness: every distant spark became the same small bright rectangle, whatever its size.
- **Speeds that threw them kilometres.** Specks went up to 380 + 2.4 R m/s and sparks up to 140 + 8 R (over 1,000 m/s in a capital blast), with drag that let them travel 10 to 15 blast radii,
  so they were spread evenly over the whole frame by the time of the cooling still.
- **Fix.** Sparks and specks (`Particle.Dim`, `t4.y` in the shader) now put on a pixel only the light they really would: a streak thinner than a pixel is dimmed by its coverage (width ratio times length
  ratio, to the 0.85), so far ones fade out and near ones are bright, which also gives a natural falloff from the blast. Speeds are squared toward the slow end (`pow(F, 2.2)` for specks) and
  capped at a few radii (sparks 70 + 3.6 R, specks 40 + 2.4 R; the capital blast's specks 0.2 to 2.2 R, 260 of them, bigger, 0.5 m): a few reach far, most stay near the blast.
  The stills no longer show a field of dots at the corners.

### 2. The opening churns orange-red with dark pockets, not beige (`ShadeFire`)
The beige was over-exposure: heat x 0.7 x 1.15, a white flash over the whole ball and a 20 per cent grey blend kept every layer above the tonemapper's knee, so orange became cream.
- Temperature is now `heat * 0.45` times a layered value with large swings from billow to billow (`churn` from two noise channels), so the core is white, then yellow, orange and red layers round it,
  and dark where gas is thick and cold. The ramp is the same; the values are lower, so the hues survive the tonemapper.
- **Smoke**: pockets of thick cold gas (a noise channel, only where the temperature is low) dim the fire behind to dark red-brown and thicken as they go, so there is dark smoky contrast.
- The grey drain applies only while it cools (0.55 x the cooling phase), the opening's brightness boost is 1.12, and the ball's white flash is smaller (0.85 R at 42 units, the capital's 0.9 R at 50 warm white).
  The capital's central ball is heat 1.8 and its lobes 1.5 to 2.0 (they were 2.3 and 1.9 to 2.4).
- Extinction 4.6 in the opening (it was 8), so even the dense phase has a ragged edge; 3.4 after.

### 3. Silhouettes: no soft cutout, no square edges
- **Big lobes**: a low-frequency noise tap shapes the mask and pushes the rim in and out (`lobe`), so the outline is made of billows of different sizes, not a circle with a feathered edge. The density is forced to zero
  at the sphere's own boundary (a lobe pushed past it used to be cut flat: a hard circle).
- **Square edges close to the camera (the AI builder's note)**: the quad was aligned to the screen, but a ball off to the side of the view projects to an ellipse on that plane, wider than the quad's circle, so the quad's
  edge cut it. Fireball quads now face the ball (a basis across the line of sight), where the silhouette is exactly the circle the quad was sized for. The fade at the quad's edge now only applies when the ball is so near that its
  silhouette is wider than the quad's size cap; before, it started at 0.8 of every quad and carved a square into big balls (the boxy capital blast).
- Checked with a 90 m and a 70 m blast about 150 m and 125 m from the camera and off to the side: no straight edges.

### 4. Spill on wreckage
- The final blast's light was 6e6 x R^2 candela: 70 times the Sun at the wreckage, so it blew every fragment out to a flat orange. It is now 5.5e5 x R^2 (2.4 s) plus a slow ember glow (1.6e5 x R^2, 5 s, going to deep orange, sized 90 m)
  that outlasts it: fragments show gradients, a lit face toward the blast and a dark one away. Fighter and bomber blasts 9e6 to 3e6 x R^2, longer (0.8 + 0.01 R s).
- The cooling still is shot from where a section's blast-facing side is visible (off the ship's axis): the near fragment is lit orange on that face and the rest are silhouetted dark against the fire.

### Captures (`gauntlet/captures/juice-sandbox/r5/`, from a fresh build, no HUD)
- Same stills as r4. `capital_death_cooling` is now from farther back and off axis (wreckage in the frame, one lit side visible). `capital_death_internal`, `capital_death_breakup`, `fighter_kill_debris` and the rest as before.
  `capital_death.mp4`, `shake.mp4`, `perf/`, `perf_repeats/`, `checks/` (all pass).
- **Best three**: `stills/capital_death_cooling.png` (a churning orange-red fire with dark pockets, wreckage lit on its blast-facing side and silhouetted against the fire), `stills/fighter_kill_fireball.png` (billowing lobes
  and dark smoke through orange fire, a handful of varied sparks, nothing at the corners), `stills/capital_death_final.png` (the opening: the whole hull swallowed by churning orange-yellow fire).

### Performance (`perf/`; 1920x1080, vsync off, RTX 4070 Ti; the same 18 s run)
479 fps average, 1% low 205, worst frame 10.8 ms; repeats 476.3 (1% low 188, worst 11.5) and 397.2 (1% low 56, worst 35.7: another builder's Unity was open; the hitch log of round 1 showed the same on a busy machine).
One more noise tap per march step (the lobe) costs nothing measurable.

### Honest read against the reference frames
- The opening is orange-yellow with red-brown gaps, closer to FS2's, but the biggest balls are still smoother than FS2's cauliflower: the noise is the same size on every ball, so a 330 m ball has the detail of a 50 m one scaled up.
- The ball's silhouette is now lobed but has a soft edge everywhere; FS2's has some hard, bright edges where fire meets black, and the Expanse's none.
- The wreckage spill shows on one near section; most fragments are far enough that they read as dark silhouettes. The hot-glow heat is still not on the baked sections.
- The opening-to-cooling change is still a quick change of density and colour; I did not look at the clip frame by frame for popping.

### Gotchas found
- `tools/capture.ps1 -Only a,b` runs the plan from the start for those stills only, so timed stills (a death at 47 s) come out at the wrong game time; capture the whole plan.
- A shader edit that only changes numbers inside a branch can look like no change at all when the visible edge is made by a different term (the circle was the sphere boundary, not the lump terms); look for what makes the edge before tuning.

## Round 5 verdicts (lead, 2026-10-25): a regression, and a change of technique
**Realism vs The Expanse: 0/3** (certain, likely, likely), down from round 4's 1/3. Pairs: capital_death_cooling vs 7_ATYOm3nNc_0177 (2:1); fighter_kill_fireball vs 7wFt319UB2o_0033; capital_death_final vs 7_ATYOm3nNc_0171 (2:1).
**Meatiness check, capital death vs FS2 big_battle_comparison_0040: lost, certain.** "A single smooth, blurry, evenly lit orange ball... a soft fog-machine puff, not a ship dying."
- The giveaway is the same in all four: the fireball reads as **one round, evenly lit volume with a swirl texture painted across it**, a billboard or sphere. There are no ejecta streaming outward, no hull chunks tearing through, no shock front, no light thrown onto nearby ships, and no motion-blurred, compositing-style streaks. The orange-dot fix worked; nobody mentions an even field of dots any more.
- **Lead's read:** five rounds of tuning one raymarched noise sphere have hit its ceiling. Round 2's capital death won meatiness 3/3 because it was many blasts spread through the hull's space with sparks streaking out. Rounds 4 and 5 consolidated it into one ball and lost the meat check twice.
- **Round 6, change of technique:** explosions made the way photoreal games make them. Real fluid simulations in Blender 5.2 (Mantaflow fire and smoke, scripted and headless), rendered with Cycles into flipbook atlases with the lighting data needed to light them in-engine, then played in Unity as sequences, several per blast, at several scales. The capital death goes back to a staged, multi-blast composition through the hull's volume, with ejecta streaks and chunks tearing outward, plus a shock front. Keep round 5's shader fixes (coverage-correct sparks, ball-facing quads), its two-phase life and the cold-debris mix. Beams and bolts are untouched.

## Round 6 (builder, 2026-10-25): simulated explosions

The brief: five rounds of tuning one raymarched noise sphere hit its ceiling ("one round, evenly lit volume with a swirl painted across it, nothing tearing outward"). Change the
technique: simulate real fire and smoke in Blender (Mantaflow), render with Cycles into flipbook atlases with lighting data, play them in Unity as sequences, several per blast,
and bring back round 2's capital death (many blasts through the hull's volume, ejecta streaking out). Beams and bolts untouched.

### The library (`art_src/juice_fx/build_flip.py`, outputs in `Resources/Juice/Flip/`)
- **Simulation**: Blender 5.2 Mantaflow gas (fire and smoke, no gravity, vorticity, burning rate, flame smoke), headless through `tools\blender.ps1`. The source is a cluster of 9 to 15 lumpy spheres (jets for the tall variant)
  that swell and inject fuel and gas for about eight frames and then stop, with a turbulence field through the domain, so the burst is many billows pushing into each other instead of one ball.
  Grid 192 cubed, no noise upres (noise bake made the volume unreadable by the renderer in this build; the fine detail is added in the shader instead).
- **Render**: Cycles on the RTX 4070 Ti (OptiX, 64 samples), an emission-only volume material that writes the line integrals of `flame`, `density` and `heat` (no extinction: pure integrals), orthographic, 512 px frames,
  36 frames per variant with times denser early (`u^1.7`: a blast opens in the first tenth of a second). The integrals are floor-corrected, normalised, and lightly blurred (about a pixel) so a magnified frame is smooth gas, not film grain.
- **Atlas** (6 x 6 frames, 3072 px, one RGBA8 PNG per variant): **R** the burning gas (sqrt-encoded), **G** the smoke, **B and A** the slope of the smoke's surface (a screen-space normal from the smoothed smoke + fire), plus a json with the frame
  times, the playable span, scales and the radius that holds 90 per cent of the mass. Motion vectors: not made (frames are blended in time; the simulation advances 1/24 s a frame early on and the blend is invisible at 36 frames).
- **Variants**: `fire_a` round burst (0.96 s of simulation), `fire_b` broad and slow (0.62 s), `fire_c` a tall jet (0.71 s, thrown out along one axis), `fire_d` a big slow billow (0.71 s), `smoke_a` the cooling variant (1.21 s: a burst
  that burns out into grey smoke; played from the frame where the fire has gone, 0.39 s). Each is cut where its smoke reaches the edge of the simulation box.
- **Size**: 13 MB of PNG on disk (1.9, 2.4, 2.7, 3.0, 3.2 MB; none near the 50 MB limit), **45 MB of video memory** in all (BC7, one `Texture2DArray` of 5 x 3072 x 3072, no mipmaps). Each variant took 8 to 12 minutes to make at grid 192.

### In the game
- `FlipLibrary` (`Fx/Flipbooks.cs`) loads the atlases into one texture array at start (the director logs `JUICE 5 flipbooks, 3072x3072 BC7 each, 45.0 MB of video memory in all`); everything falls back to the raymarched fireballs if there are none.
- **Shader kind 8** (`ShadeFlip` in `MN_Fx.shader`): the sprite faces the ball (round 5's fix), is turned and flipped at random (no two alike), blends two frames, adds churn from the 3D noise on top (so a big sprite is not smooth), fades at its edge and in and out.
  The fire channel goes through the same temperature ramp as before (white core, yellow, orange, red) and greys and dims as it ages (the two-phase life: hot and dense first, murky as it cools). **The smoke is lit**: from its slope normal by the
  Sun (read from the scene's directional light), the sky's fill and the fire's own orange glow. Depth-softened against hulls.
- **Blasts are several simulations**: `Emitters.BlastFlips`: a main one, lobes of other variants thrown out round it at other times (the count grows with size), and a smoke that outlasts them for large ones. Small bursts (flak, hull hits,
  debris trails and burns) use `Emitters.Puff`, one small flipbook. Fighter 52 m, bomber 96 m, parts 16 to 80 m as before.
- **Capital death** (`CapitalDeath.FinalFlips`): internal blasts and the break-up are as in round 5 (parts blowing off one by one with their own flipbook blasts; the hull splitting along its baked cuts), then the final blast is a main simulation
  1.9 radii wide at the hull's middle, a second big one offset along the hull, then **15 more, each a different variant, placed through the whole volume the hull filled and ignited in a chain from one end to the other over three quarters of a second**,
  each with a spill light, three smokes that arrive late, 44 ejecta streaks (long hot streaks with white-hot heads, in jets and a thin scatter, thrown at up to about 850 m/s: `Emitters.Ejecta`), a faint shock front (`ShockFront`), 100 shards, the parts
  and sections still on the ship flung through it, and the long ember light. Round 5's coverage-correct sparks, the cold debris and the spill tuning are kept.
- A short fade-in on every sprite (no pop when one appears late), `Particle.Age < 0` as a delay (a sprite waits where it was put), a light with a size and a white-to-amber colour ramp.

### Captures (`gauntlet/captures/juice-sandbox/r6/`, fresh build, no HUD)
- The same 18 stills as r5 (`capital_death_cooling` is now from farther back at 2.1 s after the blast: the smoke has taken over); `capital_death.mp4`, `capital_death_strip.png` (the whole staged death), `shake.mp4`, `library/` (previews of each variant as simulated),
  `perf/`, `perf_dogfight/`, `checks/`.
- **Best three**: `stills/capital_death_final.png` (the opening: a hull inside a mass of billowing simulated fire, lobes along its length, ejecta streaking out), `stills/capital_death_cooling.png` (the same blast cooling: smoke turning grey with the
  wreckage silhouetted and lit orange on its blast-facing side), `stills/blast_near_hull.png` (a simulated blast beside the corvette, the near hull lit by it).
- **Library sheet**: `tools\juice\flip_sheet.py` makes a sheet of what is in the atlases (the preview folder has it).

### Performance
**Caveat: the numbers below were measured while the machine's desktop session was disconnected** (`quser` showed the builder's session as Disc with another user on the console): the player did not present frames, the frame-timing GPU figure read 0.03 ms
(it read 1.7 ms in round 5) and the panel screenshots and the panel-drive check, which read the screen, came back blank (the r5 playtest build does the same now). The frame-rate figures are therefore **optimistic** and not comparable to rounds 1 to 5.
- Capital death run (18 s, ambient battle): 1034 fps average, 1% low 187, worst frame 12.2 ms. Busiest dogfight (`ai_r1_perf_busy`, 30 s, the AI builder's plan): 360 fps average, 1% low 193, worst frame 15.0 ms (their baseline from before these
  effects: 274 average, 171 1% low, 10.7 ms worst, measured with a live display).
- **Estimate of the real cost** (to be checked with a live display): a simulated sprite costs four texture reads and two 3D-noise reads per pixel; round 5's raymarched ball cost 16 steps of one to three 3D reads per pixel, so a flipbook is
  about a quarter to a third of its cost per pixel, with a similar overdraw (the biggest blasts cover under half the frame, a dozen to twenty sprites at the death's peak). The worst frame in both runs is the break-up and final-blast frame (cutting
  and spawning, 5 and 3 ms). I expect well over 60 fps with headroom; I have not been able to prove the figure.

### Honest read against the reference frames
- The fire now has real billowing structure (the simulation's), shaded lobes, dark gaps between billows, and a core that is simulated rather than painted; the capital death is a staged, multi-source composition with ejecta and wreckage, which is what won
  meatiness in round 2 and what the realism critic found missing in rounds 4 and 5. Not checked by a blind critic.
- The sprites are still billboards: a hull passing through a blast is hidden or shown by depth, not by the gas wrapping it; close to the lens a sprite shows its flatness.
- The fire's palette is still the fire ramp (white-yellow-orange-red); the Expanse's blasts are blue-white and grey-pink, which this does not produce (the plasma variant of the old fireball is not used with flipbooks).
- The simulation frames are 36 per variant; a sprite slowed to a few seconds (the capital's) shows the blend between frames as a soft morph.
- fire_b's end was cut by hand (its smoke clipped the simulation's box as a triangle), so it is the shortest variant. The grain left by 64 Cycles samples is hidden by the blur and the detail noise, not removed.
- The smoke variant's grey is lit by the Sun but has no shadow cast onto it by the wreckage; the corvette's sections do not shade it.

### Gotchas found
- Mantaflow's noise upres bake produced a cache the renderer read as constant (`.vdb` noise files of 1.4 KB): the library is made without it. `bake_data` alone gives clean volumes.
- A Cycles volume integral has a constant floor (about 0.0495) inside the domain whatever the density: it is subtracted per frame, or every frame is a grey square.
- `numpy` is only in Blender's Python here; the repository's `python` has PIL only, so the contact-sheet tool uses PIL.
- A texture array built at start reads `Texture.width` of a texture that was unloaded by `Resources.UnloadAsset` as a null reference: read the size first.
- Another builder's tracked file (`Fx/Jumps.cs`, mission system) now lives in the Juice folder and needs types from Core; a worktree that is behind `main` does not compile with the copy of my folder: rebase the worktree first.
- A disconnected desktop session (the machine's other user logged in) blanks screen-reading checks and makes frame timing meaningless; check `quser` before trusting a perf run.
