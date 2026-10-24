# Juice (`Monolith.Juice`)

Explosions, lasers, rumble, oomph and shake, driven by combat events. Systems that raise events (weapons, AI, missions) never
reference this assembly; they call `GameEvents.RaiseAt(CombatEvents.X, position, direction, subject, value, entity, other)`.
The event table (who is `Entity`, who is `Other`, what `Value` means) is in `Scripts/Core/CombatEvents.cs`.

## Using it

```csharp
var juice = JuiceDirector.Create(world, camera, parent);          // once per scene
juice.RegisterShip(ship.Id, visualTransform);                     // so the ship can break up, be hit by beams, have a shield bubble
juice.RegisterShip(ship.Id, visual, null, damage);                // a model with real parts and damage looks: damage is an IJuiceShipDamage (see below)
GameEvents.RaiseAt(CombatEvents.HitHull, point, normal, "laser_player", 40f, victim.Id, shooter.Id);
juice.OwnsTimeScale = true; juice.BaseTimeScale = paused ? 0f : 1f;   // optional: lets the hit-stop and slow-motion own Sim.TimeScale
```

## What is where

| File | What it does |
|---|---|
| `JuiceDirector` | Builds everything, subscribes to the events, runs the frame (`LateUpdate`, order 70: after the view and the shake). |
| `JuiceSettings` | Presets (Low, Medium, High, Extreme), switches, levels. `juice.json` next to the game, else `Resources/Juice/juice_defaults.json`. |
| `Fx/FxPass`, `Fx/FxBatch`, `Resources/Juice/MN_Fx.shader` | One HDRP custom pass (before post-processing) draws every glow, streak, beam, ring, flare, scorch and the shield bubble. Colours are display units at the sky's fixed exposure, times `nitsPerUnit * exposure`, so bloom does the glow. |
| `Fx/FxParticles`, `Fx/Emitters` | The particle pool and the building blocks: flash, flare, fireball, sparks, embers, gas, ring, scorch, vent, and `Blast` (a whole layered explosion). Fireballs are drawn far to near and are soft, translucent and smeared radially (no hard edge); `SparkBurst` throws sparks in a few jets of their own cone, speed and brightness, not an even spray; sparks vary in depth, size, brightness and motion blur. |
| `Fx/Flipbooks`, `Resources/Juice/Flip`, `art_src/juice_fx` | The library of **simulated explosions**: fire-and-smoke fluid simulations (Blender 5.2 Mantaflow, scripted and headless: `tools/blender.ps1 art_src/juice_fx/build_flip.py -- --variant fire_a --res 192`), rendered with Cycles on the GPU into flipbook atlases (a 6 x 6 grid of 512 px frames in one RGBA8 PNG per variant: R the burning gas, G the smoke, B and A the smoke surface slope) plus a json (frame times, scales). `FlipLibrary` copies them into one texture array; `Emitters.Flipbook` plays one (a sprite that faces the ball, turned and flipped at random); the shader (kind 8) turns R into fire through the temperature ramp and lights the smoke with the Sun, the sky fill and the fire's own glow. A blast is several of them (`BlastFlips`); a capital's final blast is a dozen and a half through the hull's volume (`FinalFlips`). Without the library everything falls back to the raymarched fireballs. |
| `Fx/FxNoise` | The 64 cubed tiling `Texture3D` (Perlin-Worley, Worley, fine Worley, Perlin) the fireball and beam shaders read; built in code at start-up (about 60 ms). |
| `Fx/Projectiles` | Bolts (analytic from where and when they were fired, so smooth at any frame rate), flak shells, missiles with homing and a glowing trail. The weapon table is `WeaponLooks`. |
| `Fx/Beams` | Charge, then one fat ribbon (white-hot hard core, saturated body with flaring edges, soft glow; segmented so it never turns edge-on) sweeping the target hull (or following `beam_aim` events), with a big hit flare, hammering (sparks, fireball bursts, molten dots), and on a modelled ship scorch marks along its track and the parts it crosses blown off. `HullGeometry.Raycast` finds where it meets the hull (the model collision proxies when it has them). |
| `Fx/HitEffects` | Shield ripples, hull hits, near misses, flak bursts, the edge-of-screen flash when the player is hit. |
| `Fx/Explosions` | Fighter, bomber and the staged capital death (internal blasts; on a modelled ship its parts go one after another; the hull breaks into lit sections; a final blast as big as the hull is long, with real light; shockwave). `DestroyPart` is what takes a part off a modelled ship, for beams, hits and the death. |
| `Fx/Debris` | Cuts a visual into pieces by Voronoi cells (only meshes that are readable), takes whole chunks of a real ship (`AdoptChunk`), makes `Shards` (torn plates, wedges, struts and chunks cut from the dying ship's own plating, glowing at the edges with a small halo), then tumbles, heats and burns them. |
| `Fx/ShipDamageHook`, `Fx/HullSections` | `IJuiceShipDamage`: what the juice may ask of a ship model with real parts (parts, hull hits, hull damage, destroy a part and get its chunks, collision proxies); the Game assembly adapts the ship art `ShipDamage` to it (`ShipArtJuiceDamage`), so the juice never references the art. `HullSections` finds the hull cuts baked by `Monolith.EditorTools.JuiceSetup.BakeSections` (ship models import unreadable, so a built game cannot cut them; run `tools/unity.ps1 Monolith.EditorTools.JuiceSetup.BakeSections` after a model changes). |
| `Fx/BoostFx` | **The boost (afterburner).** Watches the player's burner and gives it a feel: the view widens (`GameWorld.FovKick`, applied by the cockpit view: a quick kick on ignition, held while it burns, eased back on release; the wide look is the cabin's standing view and the sky warm-up's wide frames, borrowed on purpose), a shake and pad jolt on ignition then a low steady rumble, the dust drawn longer and brighter (`GameWorld.DustStretch`, `DustBright`), a radial blur and colour fringe at the rim with a faint engine glow (pass 4 of `MN_Fx`, read from a copy of the picture; clear in the middle and in a disc round the player's target), flames on every other ship that burns, an ignition whoomph and an empty-tank sputter through `IJuiceAudio` (the audio system's roar loop and on/off swells follow the same burner flag), and a flutter as the last of the fuel goes. Every piece has a level in `JuiceSettings.boost` (the sandbox's BOOST page); `-mnBoostLog file.csv` writes the numbers each frame. |
| `Fx/DeathFx` | **The player's death (first person throughout).** Takes the player's own `ship_destroyed` (the ordinary blast at the eye is not made) and plays a ~5.9 s sequence: the canopy cracks (a web of lines in pass 5 of `MN_Fx`), the cabin model - cut in advance into ~60 pieces with `Debris.Slice` - tears apart around you, the canopy blows out, you are flung through the hole at ~30 m/s tumbling (the camera is `GameWorld.Death`'s, read by `CockpitView`), your ship breaks up and burns behind you in staged blasts, then the view closes in a red-edged iris, the sound collapses, a heartbeat stops, black. Cause (gunfire, blast, beam, collision) sets the timing and force. A or Start skips it after 1.4 s. Named events for the audio are in `DeathEvents` (`Core/DeathBoard.cs`); the juice plays five sounds through `IJuiceAudio`. The scene stands the HUD down while `Death.Active` and shows its failure flow when `Death.Finished`. Settings: `death` (enabled, intensity, tumble). |
| `Feel/*` | `Trauma` + `CameraShake` (Eiserloh: shake = trauma squared, Perlin noise, mostly rotation), `RumbleOut` (both motors), `HitStop`, `Shockwaves` (knocks), `Feel` (distance and success scaling). |
| `Audio/*` | `IJuiceAudio` (what the real audio system implements) and `ProceduralAudio`, placeholder sounds built from `Synth`. |
| `Panel/JuicePanel` | The sandbox's panel: pages of buttons, switches and sliders, pad and mouse. |

## Look captures

Take looks without the HUD: `"sceneOptions": { "hud": false }` in the plan (or `-mnNoHud`). The sandbox plan can also turn the whole layout
with `"layoutYaw"` (degrees) to put the Sun behind or in front of the action; hulls lit from behind show the spill of blasts and beams best.

## Rules the juice follows (design section 9)

- Success gets the loudest reaction: a kill the player makes shakes, rumbles and (if switched on) freezes more than the same blast elsewhere.
- Nothing hides the target or the HUD: flashes are capped to a fraction of the view near the lens, the edge flash leaves the middle clear,
  and the HUD draws after post-processing, on top of everything.
- Medium is the default. Shake and flashes have a level and an off switch.
- Vacuum: a fast flash, a fireball that dims quickly, glowing debris, venting gas, no lingering smoke.

## Checks (player command line)

`-mnScene juice -mnJuiceSelfTest` settings round trip (the boost section too), presets, trauma, hit-stop, then the boost on the running sandbox (ignition, view, dust, lens, release, the off switch, running the tank dry; about 15 s). `-mnPanelDrive` drives the panel, the pad shortcuts and the mouse
with virtual devices. `-mnRumbleTest` pulses both pad motors. `-mnDumpSounds dir` writes every placeholder sound as a WAV and logs its levels.
`-mnPanelShot file.png [-mnPanelPage n]` saves a screenshot with the panel open. Captures never write `juice.json` and make no sound (`-mnAudio` to hear them).

## Trigger rumble

Unity's Input System can drive the Xbox impulse triggers only through `IXboxOneRumble`, which no Windows device implements: XInput exposes
two motors. `RumbleOut.TriggerRumbleSupported` reports it (false here); gun fire is carried by the high-frequency motor instead.
