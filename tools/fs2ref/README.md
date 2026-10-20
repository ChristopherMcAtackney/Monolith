# fs2ref: the FS2 flight measuring stick

Reproduces FreeSpace 2's player-ship flight from FS2's own source rules and `ships.tbl`, turns it into
reference traces and metrics, and compares our game's traces against them (design doc section 4, "Measured
feel"). It is a measuring stick, not the game's flight model. Python 3, standard library only; run with `-I`.

| File | Job |
|---|---|
| `ships.py` | Reads `data/tables/ships.tbl` in place from `Root_fs2.vp` (override with `FS2_VP` or `--vp`) and returns a class's flight fields. |
| `fs2_flight.py` | The per-tick simulation. Every rule is commented with the FS2 file and function it came from. |
| `metrics.py` | CSV I/O, metric extraction, tolerances. The same code measures the reference and our traces. |
| `make_reference.py` | Runs all scenarios for ship classes; writes `gauntlet/reference/fs2_flight/<Class_Name>/`. |
| `compare.py` | Our trace folder vs a reference folder: table of per-metric error and PASS/FAIL, exit 1 on any FAIL. |
| `test_fs2ref.py` | 18 self-checks (rules, sign conventions, tick-rate independence, compare self-pass / wrong-ship fail). |

## What FS2 does (source: github.com/ptitSeb/freespace2)

`physics/physics.cpp` (`apply_physics`, `velocity_ramp`, `physics_read_flying_controls`, `physics_sim_vel`,
`physics_sim_rot`), `ship/ship.cpp` (table parse, `physics_ship_init`), `playerman/playercontrol.cpp`
(`read_keyboard_controls`, `read_player_controls`), `ship/afterburner.cpp`, `hud/hudets.cpp`, `io/joy.cpp`.
Per tick, in FS2's order: read controls, make goals, move (velocity, then rotation), act on afterburner key
edges, burn or recharge fuel. Everything is a first-order exponential chase.

- **Turn rate:** goal = stick x (2 pi / `$Rotation time`) per axis; rate chases it with `$Rotdamp`. The stick is
  not otherwise smoothed. Rotation per tick uses the new rate x dt.
- **Auto-bank:** roll goal += -(yaw stick x max *yaw* rate) / 2. Half the yaw rate, taken from the yaw axis.
- **Speed:** goal = (throttle, or 1 with the afterburner key) x top speed. The speed along the nose ramps to the
  goal with `$Forward accel` going up, `$Forward decel` going down, `+Aburn For accel` while the afterburner is
  on. No reverse (`$Rear Velocity` is 0).
- **Velocity ramp shortcut:** when the remaining gap (m/s) is under time-constant/3, the time constant becomes
  gap/3, so the last sliver closes in a few ticks and the speed lands exactly on target. Only touches the last
  ~1% of a change; the 63% / 95% times are unaffected.
- **Velocity follows the nose:** forward velocity is set straight to the ramp speed (zero damping); sideways and
  vertical velocity chase 0 with `$Damp`. The velocity is rotated into the *previous* orientation before the nose
  turns, so a turning ship always carries some sideways slip and speed is not conserved in a turn.
- **Afterburner:** physics reacts one tick after the key edge. Start needs fuel >= 10 and 1.3 s since the last
  start. Fuel burns at `+Aburn Burn Rate`; at zero the afterburner cuts out even with the key held and speed
  falls back to normal top speed on the decel constant. Fuel refills whenever it is off, even with the key held,
  at the table rate x 0.333 x 2 x skill scale (3 at the default skill, Easy; `--skill` changes it).

Not modelled: hit/collision whacks and shockwaves, dead engines, warp, engine wash, the ETS engine slider,
sideways slide (fighters and bombers never slide; ships that do raise `NotImplementedError`).
Input-device effects that live outside the model and are not applied here: FS2 samples the joystick at 10 Hz
and holds it, after a 10% dead zone and a sensitivity curve; keyboard yaw/pitch deflection grows 1/8 per second
held. Our scenarios give the stick value after all of that. FS2 used 32-bit floats; this uses 64-bit.

## Scenario format: `gauntlet/shots/flight_scenarios.json`

Format tag `monolith-flight-scenarios/1`. Each scenario: `name`, `description`, `duration` (s), `initial`
(`speed_fraction` of normal top speed, `throttle`), `keys`, `measure` (metric groups; the game ignores it).
`keys` is a list of `{"t": seconds, ...inputs}`. An input holds from its key time until a later key changes it
(step, no interpolation); unmentioned inputs keep their value. Inputs: `pitch`, `yaw`, `roll` in -1..1,
`throttle` 0..1, `afterburner` true/false. Signs match Unity and FS2: **pitch +1 = nose down, yaw +1 = nose
right, roll +1 = roll left** (counter-clockwise seen from behind). Start: origin, nose +z, up +y, not rotating,
full afterburner tank.

Scenarios: `throttle_up`, `throttle_cut`, `pitch_pulse`, `yaw_pulse` (shows auto-bank), `roll_pulse`,
`afterburner_burst`, `afterburner_to_empty`, `turn90_cruise` (full yaw for 0.65 s at cruise: a 90 degree
path for a Ulysses, about 87 degrees between start and end headings; less for slower-turning classes).

## Traces

One CSV per scenario, `<folder>/<scenario>/trace.csv` (or `<folder>/<scenario>.csv`), one row per physics tick
(any fixed tick rate), **state at the start of the tick plus the inputs about to be applied**. Columns:
`time, pitch, yaw, roll, throttle, afterburner(0/1), vx, vy, vz, speed, wx, wy, wz, fwd_x, fwd_y, fwd_z,
up_x, up_y, up_z, pos_x, pos_y, pos_z, fuel`. Velocity is in ship-local axes (x right, y up, z forward, m/s);
angular velocity in ship-local axes (rad/s, same sense as Unity local Euler rates: +x pitch down, +y yaw right,
+z roll left); `fwd`/`up` are world vectors. `compare.py` needs `time`, the five inputs, `vx vy vz wx wy wz`;
`fwd_*` for `turn90_cruise`; `fuel` for `afterburner_to_empty`. Other columns are optional.

## Run

```
python -I tools/fs2ref/make_reference.py                 # four classes, 60 Hz; prints measured vs closed-form
python -I tools/fs2ref/make_reference.py "GTF Ulysses" --hz 50 --out some/folder
python -I tools/fs2ref/compare.py <our trace folder> gauntlet/reference/fs2_flight/GTF_Ulysses
python -I tools/fs2ref/test_fs2ref.py
```
Reference classes: GTF Ulysses, GTF Myrmidon, GTF Hercules, GTB Artemis. Each class folder has `ship.json`
(fields and the `ships.tbl` hash), `summary.json`, and per scenario `trace.csv` + `metrics.json`. Output is
deterministic, so regenerating gives no diff unless the rules or scenarios changed.

## Metrics and tolerances

Times are measured from the tick the input changes, interpolated, so they do not depend on tick rate. A metric
PASSes when `|ours - reference| <= max(relative x reference, floor)`.

| Metric | Meaning | Tolerance |
|---|---|---|
| `speed_t63/t95` | time to 63% / 95% of the change in forward speed after a throttle step | 5%, floor 0.04 s |
| `<axis>_rate_t63/t95`, `<axis>_settle` | time to 63% / 95% of the plateau turn rate after the stick moves; time after release until rate stays under 2% of plateau | 5%, floor 0.04 s |
| `ab_t63/t95`, `ab_release_t63/t95`, `fuelout_t63/t95` | afterburner speed-up, slow-down after release, slow-down after fuel runs out | 5%, floor 0.04 s |
| `fuel_empty_t`, `slide_decay` | engage to empty tank; sideways speed from its peak to under 5% of the peak | 5%, floor 0.04 s |
| `speed_start/end`, `ab_peak`, `ab_end` | speeds read off the trace | 2%, floor 0.5 m/s |
| `<axis>_plateau` | turn rate reached before release | 2%, floor 0.02 rad/s |
| `autobank_ratio` | roll rate / yaw rate during a yaw hold (FS2: -0.5) | absolute 0.03 |
| `turn_angle_deg` | angle between start and end nose directions | 3%, floor 1 degree |
| `slide_peak` | peak sideways speed in `turn90_cruise` | 10%, floor 0.5 m/s |

Why these numbers:
- **Times, 5%:** half of the roughly 10% just-noticeable difference for durations, so a pass is not
  perceptible. **Floor 0.04 s:** 2.4 ticks at 60 Hz, which covers one tick of logging or update-order difference
  (FS2 itself applies the afterburner one tick late) plus rounding; without it the 0.35 s turn-rate times
  could fail on bookkeeping.
- **Levels, 2-3%:** these are straight table constants (top speed, afterburner speed, 2 pi / turn time), so
  there is no legitimate reason to differ, and 2% is below what a player can see. The angle gets 3% because the
  scripted 0.65 s hold rounds to a whole number of ticks (up to +1.5% at 50 Hz).
- **Auto-bank, 0.03:** the rule is exactly -0.5.
- **`slide_peak`, 10%:** FS2's own sideways slip depends on tick rate (about +dt / (2 x `$Damp`) at full yaw
  rate: +8% at 60 Hz, +17% at 30 Hz, +2% at 240 Hz for a Ulysses), so a 60 Hz reference is good to ~10% for a
  game ticking anywhere from 30 to 240 Hz. For a tighter check, regenerate the reference at your tick rate with
  `--hz`. All other metrics were verified identical to tolerance from 30 to 240 Hz (`test_fs2ref.py`).

What it catches (tested on the Ulysses): any time constant 10% off fails (the settle times fail at 5%); a
`$Damp` of 1.5x fails slide metrics; the afterburner speed-up 10% off fails; top speed or max turn rate 3% off
fails. Thresholds are proposals: the user's playtests overrule the traces.

## Sanity against the design appendix (GTF Ulysses, 60 Hz)

Top speed 70 m/s; pitch time 3.0 s, so 2.094 rad/s (120 deg/s); yaw 2.417, roll 1.257 rad/s. Turn rate time
constant 0.35 s: 63% in 0.35 s, 95% in 1.05 s, under 2% by 1.37 s after release. Speed-up constant 2.0 s (63% in
1.99 s, 95% in 5.99 s), slow-down 1.5 s (1.49 s, 4.49 s). Afterburner 150 m/s, constant 0.7 s (63% in 0.71 s, 95%
in 2.07 s), burns 300 fuel at 50/s: empty after 6.0 s. All inside the appendix's "medium fighter" ranges. One
mismatch: the appendix says fighter and bomber `$Damp` is 0.1 to 0.25 s, but the GTB Artemis has 0.35.
