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
