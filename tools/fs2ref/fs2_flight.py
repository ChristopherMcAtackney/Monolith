"""Per-tick re-implementation of FreeSpace 2's player-ship flight model.

This is a measuring stick, not the game's flight model: it reproduces what FS2 does, rule by
rule, so traces from our game can be compared against it. The maths is re-derived from reading
the FS2 source (https://github.com/ptitSeb/freespace2, paths below are relative to that repo);
no FS2 code is copied. Run with `python -I`; standard library only.

ONE TICK, IN THE ORDER FS2 DOES IT (freespace.cpp game_frame, then object.cpp obj_move_all):
  1. read the player's controls                 playercontrol.cpp  read_keyboard_controls
  2. turn controls into goal velocities         physics.cpp        physics_read_flying_controls
  3. move: velocity, position, then rotation    physics.cpp        physics_sim -> physics_sim_vel, physics_sim_rot
  4. act on afterburner key edges               object.cpp         obj_player_fire_stuff
  5. burn / recharge afterburner fuel           afterburner.cpp    afterburners_update (via ship_process_post)
Consequence of the order: an afterburner key press (or release) changes the physics one tick
after the key edge, because step 4 runs after step 2. Everything else responds in the same tick.

RULES (each is a first-order exponential chase, v' = goal + (v - goal) * exp(-dt / tau)):

 R1  Turn rate.  physics_read_flying_controls: goal turn rate per axis = stick * max_rotvel,
     max_rotvel = 2*pi / "$Rotation time" (ship.cpp, table parse). physics_sim_rot chases the goal
     with time constant "$Rotdamp" using the closed-form step (apply_physics), then rotates by the
     NEW rate times dt (not the average of old and new). There is no other smoothing of the stick:
     deflection goes straight to the goal rate.
 R2  Auto-bank.  physics_read_flying_controls: bank goal = bank_stick * max_rotvel.z
     - (yaw_stick * max_rotvel.y) / 2. Note it is half the YAW rate (axis y), not the bank rate.
 R3  Forward speed.  Goal speed = clamp(throttle + (afterburner key ? 1 : 0), -1, 1) * top speed,
     floored at minus "$Rear Velocity" (0 for every fighter and bomber, so never reverse).
     The speed chases the goal through velocity_ramp(): time constant "$Forward accel" when the goal
     is at or above the current ramp speed, "$Forward decel" when below. This ramp speed
     (prev_ramp_vel.z) is itself the velocity along the nose.
 R4  Velocity-ramp shortcut.  velocity_ramp() has a hack: when the remaining gap (m/s) is below
     time_const/3 (seconds, units mixed on purpose), the time constant is replaced by gap/3, so
     the last sliver closes in a few ticks, and under 0.0003 m/s it snaps to the goal. This makes
     speed reach its goal exactly in finite time instead of creeping forever. It only bites in the
     last ~1% of a change, so it does not move the 63% / 95% times.
 R5  Velocity follows the nose.  physics_sim_vel works in the ship's local frame, using the
     orientation from BEFORE this tick's rotation. Local z (forward) is set straight to the ramp
     speed (its damping constant is 0 for the player). Local x and y (sideways, up) chase 0 with
     time constant "$Damp" (side_slip_time_const), 0.1 to 0.35 s for fighters and bombers. Because
     the nose rotates every tick, the old velocity acquires a sideways component in the new frame
     and "$Damp" bleeds it off. Speed magnitude is therefore NOT conserved in a turn, and there is
     no drift or strafe (fighters have $Max Velocity x = y = 0, so slide is disabled).
 R6  Afterburner.  While the afterburner flag is on, the goal speed uses "+Aburn Max Vel" and
     the speed-up constant is "+Aburn For accel" (ci->forward is forced to 1). The slow-down
     after it ends uses the normal "$Forward decel". Starting needs fuel >= 10 and, for the
     player, 1.3 s since the previous start (afterburner.cpp afterburners_start); the flag is
     only set on the key press edge, so a refused start needs a fresh press. Fuel burns at "+Aburn
     Burn Rate" per second while on; at zero the flag drops even if the key is held (the key still
     forces forward=1, so speed falls back to normal top speed with the decel constant). Fuel
     recharges when off at "+Aburn Rec Rate" * engine_ets_fraction * 2 * skill_scale per second
     (afterburner.cpp afterburners_update; defaults: ETS index 4 = 0.333, skill Easy = 3.0).
 R7  Top speed.  physics.max_vel.z is overwritten every frame with ship.current_max_speed
     (playercontrol.cpp read_player_controls), which at the default ETS setting is the length of
     "$Max Velocity" (hudets.cpp update_ets). Engine-power sliders and engine damage are not modelled.

NOT MODELLED (none are part of normal unimpeded flight): weapon/collision whacks and shockwaves
(reduced damping, shake), dead-engine damping, warp in/out, engine wash, sideways/vertical slide
for ships with $Max Velocity x or y > 0 (raises NotImplementedError), ETS engine slider.

INPUT DEVICE NOTES (not part of the model, but they shape how FS2 feels): the joystick is sampled
at 10 Hz and held between samples (playercontrol.cpp playercontrol_read_stick), after a 10% dead
zone and a sensitivity curve (joy.cpp joy_get_scaled_reading). Keyboard yaw/pitch ramp the
deflection by 1/8 per second held (read_keyboard_controls). This module takes the stick value
AFTER all of that, as a number in [-1, 1].

AXES AND SIGNS match Unity (left-handed, x right, y up, z forward) and FS2's own convention, so
no conversion is needed: pitch +1 = nose down, yaw +1 = nose right, roll +1 = roll left
(counter-clockwise seen from behind). Angular velocity is reported in local axes (x pitch, y yaw,
z roll) in the same sense as Unity's local Euler angles. Arithmetic is 64-bit float; FS2 used 32-bit.
"""
from __future__ import annotations

import math
from dataclasses import dataclass

from ships import ShipFlight

# --- constants quoted from the FS2 source -----------------------------------------------------
MIN_FUEL_TO_ENGAGE = 10.0            # afterburner.cpp MIN_AFTERBURNER_FUEL_TO_ENGAGE
RESTART_LOCKOUT_S = 1.3              # afterburner.cpp afterburners_start: 1300 ms since last start (player only)
ENERGY_LEVELS = (0.0, 0.0833, 0.167, 0.25, 0.333, 0.417, 0.5, 0.583, 0.667, 0.75, 0.833, 0.9167, 1.0)  # hudets.cpp
DEFAULT_ENGINE_ETS_INDEX = 4         # hudets.cpp INTIAL_ENGINE_RECHARGE_INDEX
SKILL_RECHARGE_SCALE = (5.0, 3.0, 2.0, 1.5, 1.0)  # afterburner.cpp, very easy .. insane
DEFAULT_SKILL_LEVEL = 1              # freespace.cpp DEFAULT_SKILL_LEVEL (Easy)
SNAP_TIME_CONSTANT = 0.0001          # physics.cpp velocity_ramp: below this the speed snaps to its goal
DAMPING_EPSILON = 0.0001              # physics.cpp apply_physics: below this damping the value snaps to its goal


@dataclass
class Controls:
    """What the player is doing during one tick."""

    pitch: float = 0.0       # -1..1, +1 nose down
    yaw: float = 0.0         # -1..1, +1 nose right (FS2 calls this heading)
    roll: float = 0.0        # -1..1, +1 roll left (FS2 calls this bank)
    throttle: float = 0.0    # 0..1, FS2 forward_cruise_percent / 100
    afterburner: bool = False  # afterburner key held


# --- the two exponential primitives -----------------------------------------------------------

def apply_physics(damping: float, goal: float, start: float, dt: float) -> tuple[float, float]:
    """Closed-form exponential approach (physics.cpp apply_physics).

    Returns (value after dt, distance travelled during dt if `value` is a velocity).
    damping is the time constant in seconds; near zero the value jumps straight to the goal.
    """
    if damping < DAMPING_EPSILON:
        return goal, goal * dt
    gap = start - goal
    decay = math.exp(-dt / damping)
    return gap * decay + goal, (1.0 - decay) * gap * damping + goal * dt


def velocity_ramp(v_in: float, v_goal: float, time_const: float, dt: float) -> float:
    """Speed ramp with FS2's late-approach shortcut (physics.cpp velocity_ramp; rule R4)."""
    if dt == 0.0:
        return v_in
    delta = v_goal - v_in
    gap = abs(delta)
    if gap < time_const / 3.0:          # gap in m/s compared with a time in s: quirk kept on purpose
        time_const = gap / 3.0
    if time_const < SNAP_TIME_CONSTANT:
        return v_goal
    return v_in + delta * (1.0 - math.exp(-dt / time_const))


# --- tiny vector / matrix kit. A matrix is three rows [right, up, forward], each a world-space axis ---

def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def _norm(a):
    m = math.sqrt(_dot(a, a))
    return [a[0] / m, a[1] / m, a[2] / m]


def identity_orientation():
    return [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]


def to_local(v, m):
    """World vector -> ship-local coordinates (right, up, forward)."""
    return [_dot(v, m[0]), _dot(v, m[1]), _dot(v, m[2])]


def to_world(v, m):
    """Ship-local vector -> world."""
    return [v[0] * m[0][i] + v[1] * m[1][i] + v[2] * m[2][i] for i in range(3)]


def small_rotation(pitch: float, heading: float, bank: float):
    """Rotation matrix for one tick's angles (vecmat.cpp vm_angles_2_matrix; same composition order)."""
    sp, cp = math.sin(pitch), math.cos(pitch)
    sb, cb = math.sin(bank), math.cos(bank)
    sh, ch = math.sin(heading), math.cos(heading)
    return [
        [cb * ch + sp * sb * sh, sb * cp, sp * sb * ch - cb * sh],       # right row
        [sp * cb * sh - sb * ch, cb * cp, sb * sh + sp * cb * ch],       # up row
        [sh * cp, -sp, ch * cp],                                         # forward row
    ]


def rotate_orientation(orient, rot):
    """New orientation = rot applied in the ship's own frame (vm_matrix_x_matrix(orient, rot)), then re-orthogonalised."""
    new = [[sum(rot[i][k] * orient[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    f = _norm(new[2])
    u = _norm(new[1])
    r = _norm(_cross(u, f))
    u = _cross(f, r)
    return [r, u, f]


# --- the simulated ship -----------------------------------------------------------------------

class FlightSim:
    """One FS2 player ship. Call step(controls) once per tick; read the state attributes after."""

    def __init__(
        self,
        ship: ShipFlight,
        dt: float = 1.0 / 60.0,
        start_speed: float = 0.0,
        start_throttle: float = 0.0,
        skill_level: int = DEFAULT_SKILL_LEVEL,
        ets_engine_index: int = DEFAULT_ENGINE_ETS_INDEX,
    ):
        if ship.slide_enabled:
            raise NotImplementedError(f"{ship.name}: sideways/vertical slide is not modelled (fighters never slide)")
        if ets_engine_index != DEFAULT_ENGINE_ETS_INDEX:
            raise NotImplementedError("only the default ETS engine setting is modelled")
        self.ship = ship
        self.dt = dt
        self.time = 0.0
        self.max_rotvel = ship.max_rotvel
        self.max_speed = ship.max_speed  # R7: current_max_speed at default ETS
        self.recharge_scale = ENERGY_LEVELS[ets_engine_index] * 2.0 * SKILL_RECHARGE_SCALE[skill_level]

        self.pos = [0.0, 0.0, 0.0]
        self.orient = identity_orientation()
        self.ramp_speed = float(start_speed)               # prev_ramp_vel.z
        self.vel = [0.0, 0.0, float(start_speed)]          # world velocity (identity orientation at start)
        self.rotvel = [0.0, 0.0, 0.0]                      # local, rad/s (x pitch, y yaw, z roll)
        self.fuel = ship.aburn_fuel
        self.aburn_on = False
        self._aburn_wait = False                           # PF_AFTERBURNER_WAIT: set on start, cleared on key release
        self._aburn_key_last = False
        self._aburn_last_start = -1.0e9                    # sim time of the previous successful start
        self.last_throttle = start_throttle
        self.last_controls = Controls(throttle=start_throttle)

    # ---- derived readouts ----
    def local_velocity(self):
        return to_local(self.vel, self.orient)

    def speed(self) -> float:
        return math.sqrt(_dot(self.vel, self.vel))

    # ---- one tick ----
    def step(self, ctl: Controls) -> None:
        ship, dt = self.ship, self.dt
        self.last_controls = ctl

        # 1. controls (read_keyboard_controls): afterburner key edges become one-tick start/stop requests.
        key = bool(ctl.afterburner)
        start_request = key and not self._aburn_key_last
        stop_request = (not key) and self._aburn_key_last
        self._aburn_key_last = key
        if start_request and not ship.has_afterburner:
            start_request = False
        throttle = min(1.0, max(0.0, ctl.throttle))
        self.last_throttle = throttle
        pitch = min(1.0, max(-1.0, ctl.pitch))
        heading = min(1.0, max(-1.0, ctl.yaw))
        bank = min(1.0, max(-1.0, ctl.roll))

        # 2. goals (physics_read_flying_controls)
        forward = (1.0 if key else 0.0) + throttle          # cruise throttle is added to the key thrust
        if self.aburn_on:
            forward = 1.0
        forward = min(1.0, max(-1.0, forward))

        desired_rot = [
            pitch * self.max_rotvel[0],
            heading * self.max_rotvel[1],
            bank * self.max_rotvel[2] - (heading * self.max_rotvel[1]) / 2.0,   # R2 auto-bank
        ]
        if self.aburn_on:
            goal_speed = forward * ship.aburn_max_vel[2]
            accel_const = ship.aburn_forward_accel
        else:
            goal_speed = forward * self.max_speed
            accel_const = ship.forward_accel
        goal_speed = max(goal_speed, -ship.max_rear_vel)
        tau = accel_const if goal_speed >= self.ramp_speed else ship.forward_decel   # R3 / R6
        self.ramp_speed = velocity_ramp(self.ramp_speed, goal_speed, tau, dt)

        # 3a. velocity (physics_sim_vel): local frame of the pre-rotation orientation.
        v_local = to_local(self.vel, self.orient)
        out = [0.0, 0.0, 0.0]
        disp = [0.0, 0.0, 0.0]
        out[0], disp[0] = apply_physics(ship.damp, 0.0, v_local[0], dt)             # sideways -> 0
        out[1], disp[1] = apply_physics(ship.damp, 0.0, v_local[1], dt)             # up/down  -> 0
        out[2], disp[2] = apply_physics(0.0, self.ramp_speed, v_local[2], dt)       # forward: no damping, = ramp speed
        step_world = to_world(disp, self.orient)
        self.pos = [self.pos[i] + step_world[i] for i in range(3)]
        self.vel = to_world(out, self.orient)

        # 3b. rotation (physics_sim_rot): chase goal rate, then rotate by the new rate * dt.
        for i in range(3):
            self.rotvel[i], _ = apply_physics(ship.rotdamp, desired_rot[i], self.rotvel[i], dt)
        rot = small_rotation(self.rotvel[0] * dt, self.rotvel[1] * dt, self.rotvel[2] * dt)
        self.orient = rotate_orientation(self.orient, rot)

        self.time += dt

        # 4. afterburner edges (obj_player_fire_stuff -> afterburners_start / afterburners_stop)
        if start_request:
            self._afterburner_start()
        if stop_request:
            self._aburn_wait = False                         # key released
            self.aburn_on = False

        # 5. fuel (afterburners_update)
        if ship.has_afterburner:
            if not self.aburn_on:
                if self.fuel < ship.aburn_fuel:
                    self.fuel = min(ship.aburn_fuel, self.fuel + ship.aburn_recover_rate * dt * self.recharge_scale)
            elif self.fuel <= 0.0:
                self.fuel = 0.0
                self.aburn_on = False                        # ran dry: flag drops, key stays down, WAIT stays set
            else:
                self.fuel = max(0.0, self.fuel - ship.aburn_burn_rate * dt)

    def _afterburner_start(self) -> None:
        if self.time - self._aburn_last_start < RESTART_LOCKOUT_S - 1e-9:
            return
        if self._aburn_wait or self.aburn_on:
            return
        if self.fuel < MIN_FUEL_TO_ENGAGE:
            return
        self.aburn_on = True
        self._aburn_wait = True
        self._aburn_last_start = self.time

    # ---- trace row: state at the START of a tick, plus the controls about to be applied ----
    def row(self, ctl: Controls) -> dict:
        v = self.local_velocity()
        f, u = self.orient[2], self.orient[1]
        return {
            "time": self.time,
            "pitch": ctl.pitch, "yaw": ctl.yaw, "roll": ctl.roll,
            "throttle": ctl.throttle, "afterburner": 1 if ctl.afterburner else 0,
            "vx": v[0], "vy": v[1], "vz": v[2], "speed": self.speed(),
            "wx": self.rotvel[0], "wy": self.rotvel[1], "wz": self.rotvel[2],
            "fwd_x": f[0], "fwd_y": f[1], "fwd_z": f[2],
            "up_x": u[0], "up_y": u[1], "up_z": u[2],
            "pos_x": self.pos[0], "pos_y": self.pos[1], "pos_z": self.pos[2],
            "fuel": self.fuel,
        }


TRACE_COLUMNS = (
    "time", "pitch", "yaw", "roll", "throttle", "afterburner",
    "vx", "vy", "vz", "speed", "wx", "wy", "wz",
    "fwd_x", "fwd_y", "fwd_z", "up_x", "up_y", "up_z",
    "pos_x", "pos_y", "pos_z", "fuel",
)


def simulate(ship: ShipFlight, scenario, dt: float = 1.0 / 60.0, **sim_kwargs) -> list[dict]:
    """Run a scenarios.Scenario for its full duration; returns one row per tick boundary (n+1 rows)."""
    sim = FlightSim(
        ship,
        dt=dt,
        start_speed=scenario.start_speed_fraction * ship.max_speed,
        start_throttle=scenario.start_throttle,
        **sim_kwargs,
    )
    n = int(round(scenario.duration / dt))
    rows = []
    for i in range(n + 1):
        ctl = scenario.controls_at(i * dt)
        rows.append(sim.row(ctl))
        if i < n:
            sim.step(ctl)
    return rows
