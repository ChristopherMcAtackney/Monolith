# Piece: flight model and the first flight playtest

Measured piece (design section 4, "Measured feel"): our traces against FS2's, from `tools/fs2ref`. The user's
playtest overrules the traces. Code: `Game/Assets/Monolith/Scripts/Flight` (rules in its `README.md`), `Input`,
`Targeting`, `View`, `Hud`, and `Game/FlightPlaytest.cs` + `Game/PlaceholderShips.cs`.

## Round 1 (builder, 2026-10-21 to 2026-10-23)

### What was built
- **Flight** (`Monolith.Flight`): `ShipFlight` per ship, `FlightSystem` on the 60 Hz sim, driven only by `ControlInput`
  (player, `ScriptedPilot`, the scenario runner; AI and remote later). Written from FS2's rules as summarised in
  `tools/fs2ref/README.md` and read in FS2's `physics.cpp` (no code copied, nothing fitted to the traces): exponential
  turn-rate chase, auto-bank from the yaw axis at half the yaw rate, speed ramp with FS2's late shortcut, velocity worked
  in the start-of-tick frame (forward = ramp speed, sideways/vertical decay with `$Damp`, so turns slip), afterburner
  key edges acted on after the move, 10-fuel minimum and 1.3 s restart lockout, burn and recharge, match target speed,
  and knocks (`GameWorld.Knocks`) that loosen the damping for up to 2 s, fading.
- **Data**: `Resources/Ships/{vanguard_fighter, gtf_ulysses, gtf_myrmidon, gtf_hercules, gtb_artemis}.json`, every value
  `{ value, unit, source }` (ships.tbl fields, verified against `tools/fs2ref/ships.py`; mass and size are marked as
  estimates). Our fighter starts on the Ulysses' numbers. Shared constants with sources in `Resources/Flight/flight_rules.json`.
- **Scenario runner**: `-mnFlightScenarios <file> -mnShip <class> -mnOut <dir>` replays every scenario through the
  game's own `Sim` + `Pilots` + `FlightSystem` and writes `<scenario>/trace.csv`. `tools/flight.ps1` runs it for the four
  reference classes and our fighter, calls `compare.py`, and also runs `FlightSelfTest` (17 checks: input signs, knocks,
  match speed, afterburner refusals) in the same player.
- **Playtest scene** (`-mnScene flight`): first person only, our fighter, four target drones on looping `ScriptedPilot`s
  through the same flight model (a vertical loop, a flat circle, a weave, a tumbling loop), three capital hulls, the Sky
  system (`SkyRig.Enable`: real stars, Milky Way, Sun, fill, exposure), FS2-style motion dust streaking along your
  velocity, and a minimal line HUD (reticle, speed + throttle setting, afterburner fuel, target bracket with range and
  speed, pause/help overlay). Render interpolation: every ship and the cockpit camera draw Lerp/Slerp(previous tick,
  current tick, `GameWorld.Alpha`); `-mnFps n` caps the frame rate to try it.
- **Capture**: shots can `"follow": true` the game's camera; `gauntlet/shots/flight_playtest.json` flies a scripted demo
  route (`"sceneOptions": { "pilot": "demo" }`) and shoots four stills and a 20 s perf run from the cockpit.

### Compare results (built player, 60 Hz, `gauntlet/captures/flight-model/r1/compare/`)

| Class | Result | Worst metric (share of its tolerance) |
|---|---|---|
| GTF Ulysses | 35 of 35 pass | `afterburner_to_empty.fuelout_t63/t95` +0.0167 s, one tick (22%) |
| GTF Myrmidon | 35 of 35 pass | everything within 0.0001 of the reference |
| GTF Hercules | 35 of 35 pass | everything within 0.0001 of the reference |
| GTB Artemis | 35 of 35 pass | `afterburner_to_empty.fuelout_t63/t95` +0.0167 s, one tick (17%) |
| Vanguard fighter (ours, vs the Ulysses reference, information only) | 35 of 35 pass | as the Ulysses |

All other metrics (speed t63/t95, turn plateaus, rate t63/t95, settle, auto-bank ratio -0.5000, slide peak and decay,
turn angle, afterburner peak/ramp/release, fuel-empty time) match to 4 decimals. The model passed on its first run.

**What does not match exactly, and why:** fuel is a 32-bit float, as it was in FS2. 300 fuel at 50/s in 1/60 s steps
does not land exactly on 0 in float32 (it does in the reference's float64), so the tank reads empty one tick later and the
afterburner cuts out one tick later (Ulysses and Artemis; the Myrmidon's and Hercules' rates divide exactly). 17-22% of
the tolerance; left as is because FS2 itself had float32 fuel.

### Different from FS2 on purpose
- **No 10 Hz stick sampling** (FS2 sampled the joystick at 10 Hz and held it): we read the pad every 60 Hz tick.
- **Keyboard** gives full deflection at once (FS2 ramped keyboard yaw/pitch); the 0.35 s turn lag smooths it. Keyboard is a fallback.
- **Knock timer** saturates at 2 s when knocks stack; FS2 left the timer unchanged when the sum overflowed (a quirk).
- **Shockwave shake** is a smooth noise added to the turn rate, not FS2's random jolt to the orientation, so render
  interpolation stays smooth. Strength 0.6 rad/s per unit shock is a placeholder for the juice sandbox.
- **Afterburner recharge** uses FS2's default-skill (Easy) scale, 1.998 x table rate (50/s on a Ulysses, full in 6 s), as
  the reference does. Medium would be 1.332. It is one number in `flight_rules.json`.
- **Afterburner key without an afterburner** does nothing (FS2 still forced full throttle). Only matters for capital ships.
- **Match speed** cancels when the pilot moves the throttle (FS2: any throttle key). Mass and size, used only by knocks,
  are estimates (FS2 takes mass from the 3D model).

### Pad layout and stick shaping (`Resources/Input/controls.json`, copied next to `Play.bat` so it can be tuned)
Starts from Star Wars: Squadrons' default Xbox scheme: left stick throttle (y) and roll (x), right stick pitch and yaw,
L3 boost (our afterburner), RT fire, LT target ahead, A cycle targets, B countermeasures, LB/RB auxiliaries. Our
additions: X match target speed (Squadrons' X is power, which the design moves to a hold menu), D-pad throttle presets
100/0/33/66% (Squadrons' D-pad is power too), Start pause (Y respawn, B quit while paused), View a test knock.
- **Right stick (pitch + yaw)**: radial dead zone 0.10 (Xbox sticks rest up to ~0.1 off centre), rescaled so output starts
  at 0 at its edge (no jump), outer dead zone 0.04 so full rate is reachable before the gate, then deflection^1.6. A quarter
  stick gives 6% of the turn rate (8 deg/s of yaw on a Ulysses) for fine aim, half stick 29%, full stick the table rate.
  Radial, not per-axis, so diagonals keep their direction. No extra smoothing: FS2's 0.35 s turn lag is the smoothing.
  Push forward = nose down (aircraft; FS2 is a joystick game), `pushForwardIsNoseDown` flips it.
- **Left stick x (roll)**: axial dead zone 0.18, exponent 1.5: larger than the aim stick's so throttling does not roll.
- **Left stick y (throttle)**: moves the throttle setting, which stays where you leave it (Squadrons' pad throttle; FS2's
  throttle is a setting), 0.8 per second at full stick (0 to 100% in 1.25 s), dead zone 0.30 so rolling does not nudge it.
- **Afterburner**: L3 click toggles it on and off; it lets go by itself when the tank runs dry or the start is refused
  (minimum fuel, 1.3 s lockout). Holding a stick button while rolling with that stick is tiring. `afterburnerToggle: false`
  makes it hold-to-burn, as FS2's Tab (the keyboard's Tab is hold).
The pad was detected ("Xbox Controller") but the builder cannot hold it: the shaping is untested by hand.

### Placeholder ships (changed after the user's note of 2026-10-23)
The first hulls (grey boxes under noise greebles, a tower, a wedge prow, flat white engine discs) read as TIE Fighter.
Replaced, cheaply, with FS2's design language: long hulls of distinct chamfered plated segments joined by darker recessed
necks, a blunt nose, layered upper decks and armour plates, a dorsal spine with fins, a keel fin, an asymmetric pod on
struts and an antenna boom, rows of lit windows in blocks, light strips, red/green/white running lights, and recessed
exhaust tubes with a hot white-blue-violet gradient. Materials are plated PBR generated at boot: a panel layout with
recessed panel lines and rivet rows (normal map), per-plate shade and a few blue-grey/olive plates, the odd bare-metal
or hazard-striped plate, grime streaks and chipped edges, and a mask map so shine varies with paint and dirt. Setup
makes the base assets with those maps switched on (`Editor/FlightAssets.cs`). The scene's sun, fill and exposure now come
from the Sky system (`SkyRig.Enable`), and shadow sides keep their form (not crushed to black) in the stills.

### Captures (`gauntlet/captures/flight-model/r1/`)
- `playtest/hull_run.png`: on afterburner at 150 m/s along the carrier's flank, the destroyer and cruiser ahead, dust
  streaking. The best single picture of the feel.
- `playtest/target.png`: a target bracketed at 363 m, 52 m/s, between two hulls. `playtest/bank.png`: banking over the
  carrier's ribbed segment with a target bracketed against it and the Milky Way. `playtest/turn.png`: a climbing left turn
  out of the hull run, mostly sky.
- Perf, 1920x1080, vsync off, cockpit flying the demo route: 971 fps average, 1% low 714 fps, worst frame 1.8 ms,
  GPU 0.97 ms, sim 60.0 Hz (`perf_from_start`); 1,096 / 788 fps on the later part of the route (`playtest`).
- Traces and compare tables: `traces/<Class>/`, `compare/<Class>.txt`, `traces/summary.txt`.

### Known gaps
- No collisions yet: you fly through hulls and other ships.
- Targeting is minimal (any other ship, nearest the nose or nearest first). No weapons, so RT/LB/RB/B/Y do nothing.
- The playtest's placeholder starfield (View) is off now that the Sky system draws the real one.
- The demo-route stills are composed by hand for these hull positions; moving hulls means re-timing the demo keys.
