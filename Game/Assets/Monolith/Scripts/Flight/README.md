# Flight

FS2's flight behaviour, our own code (design section 4). One `ShipFlight` per ship, stepped by `FlightSystem` on the
60 Hz sim from that ship's `ControlInput`; whoever produces the input (pad, AI, script, remote) does not matter.

## Rules, per tick, in FS2's order

1. **Controls.** Afterburner key edges become start/stop requests, acted on after the move (so the physics reacts one
   tick late, as in FS2). Match speed toggles on its button with a target; moving the throttle cancels it.
2. **Goals.** Turn-rate goal = stick x 2 pi / rotation time per axis; yaw also asks for roll at half the yaw rate the
   other way (auto-bank). Speed goal = throttle (1 while the afterburner key is held) x top speed, or the afterburner
   speed while it burns. Speed ramps with the speed-up or slow-down constant and FS2's late shortcut.
3. **Move.** In the frame from the start of the tick: forward velocity = ramp speed, sideways and vertical velocity decay
   with `sideDamp` (so a turning ship slips). Then turn rates chase their goals with `rotDamp` and the ship turns by the
   new rates (Unity's Euler order is FS2's heading-pitch-bank order).
4. **Afterburner** starts need 10 fuel and 1.3 s since the last start; it cuts out at empty even with the key held (counted in `ShipState.FuelCutOuts` in the tick it happens, which the juice's empty-tank sputter reads: a frame that runs several ticks has already refilled a little by the time it is drawn).
5. **Fuel** burns while on and refills whenever off (table rate x `afterburnerRechargeScale`).

Knocks (`GameWorld.Knocks`, from weapons, collisions, shockwaves later; the playtest's View button for now) change
velocity by impulse / mass and spin from an off-centre hit, then loosen the damping for up to 2 s, fading: sideways
x (1 + 9f), forward x 10f (the push decays instead of snapping back), turn damping x (1 + 4f) and a smooth shake for
shockwaves.

## Data

`Resources/Ships/<class>.json` (format `monolith-ship-class/1`): every value is `{ value, unit, source }`.
`Resources/Flight/flight_rules.json`: the shared constants, each with its source. Unknown or unsourced entries are errors.

## Measuring

`tools\flight.ps1` runs the built player with `-mnFlightScenarios` for the four FS2 reference classes and our fighter,
then `tools\fs2ref\compare.py`; it also runs `FlightSelfTest` (signs, knocks, match speed, afterburner refusals).
History and results: `gauntlet/pieces/flight-model/history.md`.
