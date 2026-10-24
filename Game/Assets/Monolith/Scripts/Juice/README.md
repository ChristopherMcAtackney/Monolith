# Juice (`Monolith.Juice`)

Explosions, lasers, rumble, oomph and shake, driven by combat events. Systems that raise events (weapons, AI, missions) never
reference this assembly; they call `GameEvents.RaiseAt(CombatEvents.X, position, direction, subject, value, entity, other)`.
The event table (who is `Entity`, who is `Other`, what `Value` means) is in `Scripts/Core/CombatEvents.cs`.

## Using it

```csharp
var juice = JuiceDirector.Create(world, camera, parent);          // once per scene
juice.RegisterShip(ship.Id, visualTransform);                     // so the ship can break up, be hit by beams, have a shield bubble
GameEvents.RaiseAt(CombatEvents.HitHull, point, normal, "laser_player", 40f, victim.Id, shooter.Id);
juice.OwnsTimeScale = true; juice.BaseTimeScale = paused ? 0f : 1f;   // optional: lets the hit-stop and slow-motion own Sim.TimeScale
```

## What is where

| File | What it does |
|---|---|
| `JuiceDirector` | Builds everything, subscribes to the events, runs the frame (`LateUpdate`, order 70: after the view and the shake). |
| `JuiceSettings` | Presets (Low, Medium, High, Extreme), switches, levels. `juice.json` next to the game, else `Resources/Juice/juice_defaults.json`. |
| `Fx/FxPass`, `Fx/FxBatch`, `Resources/Juice/MN_Fx.shader` | One HDRP custom pass (before post-processing) draws every glow, streak, beam, ring, flare, scorch and the shield bubble. Colours are display units at the sky's fixed exposure, times `nitsPerUnit * exposure`, so bloom does the glow. |
| `Fx/FxParticles`, `Fx/Emitters` | The particle pool and the building blocks: flash, flare, fireball, sparks, embers, gas, ring, scorch, vent, and `Blast` (a whole layered explosion). |
| `Fx/Projectiles` | Bolts (analytic from where and when they were fired, so smooth at any frame rate), flak shells, missiles with homing and a glowing trail. The weapon table is `WeaponLooks`. |
| `Fx/Beams` | Charge, then a segmented ribbon with animated noise, sweeping the target hull (or following `beam_aim` events), impact sparks, scorch and small blasts. |
| `Fx/HitEffects` | Shield ripples, hull hits, near misses, flak bursts, the edge-of-screen flash when the player is hit. |
| `Fx/Explosions` | Fighter, bomber and the staged capital death (internal blasts, break-up into lit sections, final flash, shockwave). |
| `Fx/Debris` | Cuts a visual into pieces by Voronoi cells, then tumbles, heats and burns them. |
| `Feel/*` | `Trauma` + `CameraShake` (Eiserloh: shake = trauma squared, Perlin noise, mostly rotation), `RumbleOut` (both motors), `HitStop`, `Shockwaves` (knocks), `Feel` (distance and success scaling). |
| `Audio/*` | `IJuiceAudio` (what the real audio system implements) and `ProceduralAudio`, placeholder sounds built from `Synth`. |
| `Panel/JuicePanel` | The sandbox's panel: pages of buttons, switches and sliders, pad and mouse. |

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
