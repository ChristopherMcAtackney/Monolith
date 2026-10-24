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
| `Fx/FxNoise` | The 64 cubed tiling `Texture3D` (Perlin-Worley, Worley, fine Worley, Perlin) the fireball and beam shaders read; built in code at start-up (about 60 ms). |
| `Fx/Projectiles` | Bolts (analytic from where and when they were fired, so smooth at any frame rate), flak shells, missiles with homing and a glowing trail. The weapon table is `WeaponLooks`. |
| `Fx/Beams` | Charge, then one fat ribbon (white-hot hard core, saturated body with flaring edges, soft glow; segmented so it never turns edge-on) sweeping the target hull (or following `beam_aim` events), with a big hit flare, hammering (sparks, fireball bursts, molten dots), and on a modelled ship scorch marks along its track and the parts it crosses blown off. `HullGeometry.Raycast` finds where it meets the hull (the model collision proxies when it has them). |
| `Fx/HitEffects` | Shield ripples, hull hits, near misses, flak bursts, the edge-of-screen flash when the player is hit. |
| `Fx/Explosions` | Fighter, bomber and the staged capital death (internal blasts; on a modelled ship its parts go one after another; the hull breaks into lit sections; a final blast as big as the hull is long, with real light; shockwave). `DestroyPart` is what takes a part off a modelled ship, for beams, hits and the death. |
| `Fx/Debris` | Cuts a visual into pieces by Voronoi cells (only meshes that are readable), takes whole chunks of a real ship (`AdoptChunk`), makes `Shards` (torn plates, wedges, struts and chunks cut from the dying ship's own plating, glowing at the edges with a small halo), then tumbles, heats and burns them. |
| `Fx/ShipDamageHook`, `Fx/HullSections` | `IJuiceShipDamage`: what the juice may ask of a ship model with real parts (parts, hull hits, hull damage, destroy a part and get its chunks, collision proxies); the Game assembly adapts the ship art `ShipDamage` to it (`ShipArtJuiceDamage`), so the juice never references the art. `HullSections` finds the hull cuts baked by `Monolith.EditorTools.JuiceSetup.BakeSections` (ship models import unreadable, so a built game cannot cut them; run `tools/unity.ps1 Monolith.EditorTools.JuiceSetup.BakeSections` after a model changes). |
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

`-mnJuiceSelfTest` settings round trip, presets, trauma, hit-stop. `-mnPanelDrive` drives the panel, the pad shortcuts and the mouse
with virtual devices. `-mnRumbleTest` pulses both pad motors. `-mnDumpSounds dir` writes every placeholder sound as a WAV and logs its levels.
`-mnPanelShot file.png [-mnPanelPage n]` saves a screenshot with the panel open. Captures never write `juice.json` and make no sound (`-mnAudio` to hear them).

## Trigger rumble

Unity's Input System can drive the Xbox impulse triggers only through `IXboxOneRumble`, which no Windows device implements: XInput exposes
two motors. `RumbleOut.TriggerRumbleSupported` reports it (false here); gun fire is carried by the high-frequency motor instead.
