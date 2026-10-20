"""Trace I/O and the feel metrics, shared by make_reference.py and compare.py.

A trace is a dict of column name -> list of floats (see fs2_flight.TRACE_COLUMNS). Metrics are
extracted from a trace alone, so the same code measures the FS2 reference and our game.

Definitions (all times are seconds measured from the event named, interpolated linearly between ticks,
so they do not depend on the tick rate):
  *_t63, *_t95   time for a signal to cover 63% / 95% of the way from its value at the event to its
                 settled target. Event = the tick at which the input changes.
  *_settle       time after stick release until |rate| stays under 2% of the plateau rate.
  slide_decay    time from the peak of sideways speed (hypot(vx, vy)) until it stays under 5% of that peak.
  fuel_empty_t   time from afterburner engage until the fuel reading reaches 0.1% of its starting value.
Level metrics (plateau, peak, start, end, ratio, angle) are plain values read from the trace.
"""
from __future__ import annotations

import csv
import math
import re

from fs2_flight import TRACE_COLUMNS

FRACTIONS = (0.63, 0.95)        # "time to 63% / 95%"
SETTLE_FRACTION = 0.02          # rate under 2% of max counts as settled
SLIDE_FRACTION = 0.05           # sideways speed under 5% of its peak counts as decayed
FUEL_EMPTY_FRACTION = 0.001
_EPS = 1e-9


class TraceError(Exception):
    """A trace lacks a column or never shows the event a metric needs."""


# ---------------------------------------------------------------------------------------
# CSV
# ---------------------------------------------------------------------------------------

def write_trace(path: str, rows: list[dict]) -> None:
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(TRACE_COLUMNS)
        for r in rows:
            out = []
            for c in TRACE_COLUMNS:
                v = r[c]
                if c == "afterburner":
                    out.append(str(int(v)))
                elif c.startswith("pos_"):
                    out.append(f"{v:.3f}")
                else:
                    out.append(f"{v:.5f}")
            w.writerow(out)


def read_trace(path: str) -> dict[str, list[float]]:
    """Read a trace CSV (header names are case-insensitive; booleans may be true/false)."""
    cols: dict[str, list[float]] = {}
    with open(path, newline="", encoding="utf-8-sig") as f:
        reader = csv.reader(f)
        header = [h.strip().lower() for h in next(reader)]
        for h in header:
            cols[h] = []
        for line_no, row in enumerate(reader, start=2):
            if not row:
                continue
            if len(row) != len(header):
                raise TraceError(f"{path}: line {line_no} has {len(row)} fields, header has {len(header)}")
            for h, cell in zip(header, row):
                cell = cell.strip()
                low = cell.lower()
                if low in ("true", "false"):
                    cols[h].append(1.0 if low == "true" else 0.0)
                else:
                    cols[h].append(float(cell))
    return cols


def _col(trace, name):
    if name not in trace:
        raise TraceError(f"trace has no '{name}' column")
    return trace[name]


# ---------------------------------------------------------------------------------------
# Signal helpers
# ---------------------------------------------------------------------------------------

def _first_index(pred, n, start=0):
    for i in range(start, n):
        if pred(i):
            return i
    return None


def _crossing(t, y, level, rising, start):
    """First time at/after index `start` where y reaches `level` (rising: y >= level, else y <= level), interpolated."""
    for i in range(start, len(y)):
        hit = y[i] >= level if rising else y[i] <= level
        if hit:
            if i == start or y[i] == y[i - 1]:
                return t[i]
            a = (level - y[i - 1]) / (y[i] - y[i - 1])
            return t[i - 1] + a * (t[i] - t[i - 1])
    return None


def step_response(t, y, k0, target, prefix, out):
    """Write {prefix}_t63 and {prefix}_t95: time from index k0 to cover that fraction of the way to `target`."""
    y0 = y[k0]
    span = target - y0
    for frac in FRACTIONS:
        key = f"{prefix}_t{int(round(frac * 100))}"
        if abs(span) < 1e-6:
            out[key] = None
            continue
        tc = _crossing(t, y, y0 + frac * span, span > 0, k0)
        out[key] = None if tc is None else tc - t[k0]


def time_to_stay_below(t, y, threshold, start):
    """Time at which |y| drops under `threshold` for good (last downward crossing), or None if it never stays under."""
    n = len(y)
    last_above = None
    for i in range(start, n):
        if abs(y[i]) >= threshold:
            last_above = i
    if last_above is None:
        return t[start]
    if last_above == n - 1:
        return None
    a, b = abs(y[last_above]), abs(y[last_above + 1])
    frac = (a - threshold) / (a - b) if a != b else 1.0
    return t[last_above] + frac * (t[last_above + 1] - t[last_above])


# ---------------------------------------------------------------------------------------
# Metric groups (named in a scenario's "measure" list)
# ---------------------------------------------------------------------------------------

def _throttle_step(trace, out):
    t, thr, vz = _col(trace, "time"), _col(trace, "throttle"), _col(trace, "vz")
    k0 = _first_index(lambda i: abs(thr[i] - thr[i - 1]) > _EPS, len(t), 1)
    if k0 is None:
        raise TraceError("throttle never changes in this trace")
    out["speed_start"] = vz[k0]
    out["speed_end"] = vz[-1]
    step_response(t, vz, k0, vz[-1], "speed", out)


_AXIS = {"pitch": ("pitch", "wx"), "yaw": ("yaw", "wy"), "roll": ("roll", "wz")}


def _turn_step(trace, out, axis):
    stick_col, rate_col = _AXIS[axis]
    t, stick, w = _col(trace, "time"), _col(trace, stick_col), _col(trace, rate_col)
    k0 = _first_index(lambda i: abs(stick[i]) > _EPS, len(t))
    if k0 is None:
        raise TraceError(f"{stick_col} input is never non-zero in this trace")
    kr = _first_index(lambda i: abs(stick[i]) <= _EPS, len(t), k0)
    if kr is None:
        raise TraceError(f"{stick_col} input is never released in this trace")
    kp = max(range(k0, kr + 1), key=lambda i: abs(w[i]))
    plateau = w[kp]
    out[f"{axis}_plateau"] = plateau
    step_response(t, w, k0, plateau, f"{axis}_rate", out)
    st = time_to_stay_below(t, w, SETTLE_FRACTION * abs(plateau), kr)
    out[f"{axis}_settle"] = None if st is None else st - t[kr]
    if axis == "yaw":
        wz = _col(trace, "wz")
        out["autobank_ratio"] = wz[kp] / plateau if abs(plateau) > _EPS else None


def _afterburner_edges(trace):
    t, ab = _col(trace, "time"), _col(trace, "afterburner")
    k_on = _first_index(lambda i: ab[i] > 0.5, len(t))
    if k_on is None:
        raise TraceError("afterburner is never engaged in this trace")
    k_off = _first_index(lambda i: ab[i] < 0.5, len(t), k_on)
    if k_off is None:
        raise TraceError("afterburner key is never released in this trace")
    return k_on, k_off


def _afterburner_burst(trace, out):
    t, vz = _col(trace, "time"), _col(trace, "vz")
    k_on, k_off = _afterburner_edges(trace)
    peak = max(vz[k_on : k_off + 1])
    out["ab_peak"] = peak
    step_response(t, vz, k_on, peak, "ab", out)
    step_response(t, vz, k_off, vz[-1], "ab_release", out)
    out["ab_end"] = vz[-1]


def _afterburner_empty(trace, out):
    t, vz, fuel = _col(trace, "time"), _col(trace, "vz"), _col(trace, "fuel")
    k_on, k_off = _afterburner_edges(trace)
    k_empty = _first_index(lambda i: fuel[i] <= FUEL_EMPTY_FRACTION * fuel[0], len(t), k_on)
    if k_empty is None:
        raise TraceError("fuel never runs out in this trace")
    out["fuel_empty_t"] = t[k_empty] - t[k_on]
    out["ab_peak"] = max(vz[k_on : k_empty + 1])
    out["ab_end"] = vz[-1]
    step_response(t, vz, k_empty, vz[-1], "fuelout", out)


def _sideslip(trace, out):
    t, vx, vy = _col(trace, "time"), _col(trace, "vx"), _col(trace, "vy")
    lateral = [math.hypot(a, b) for a, b in zip(vx, vy)]
    kp = max(range(len(lateral)), key=lambda i: lateral[i])
    peak = lateral[kp]
    out["slide_peak"] = peak
    st = time_to_stay_below(t, lateral, SLIDE_FRACTION * peak, kp) if peak > _EPS else None
    out["slide_decay"] = None if st is None else st - t[kp]
    fx, fy, fz = _col(trace, "fwd_x"), _col(trace, "fwd_y"), _col(trace, "fwd_z")
    d = fx[0] * fx[-1] + fy[0] * fy[-1] + fz[0] * fz[-1]
    out["turn_angle_deg"] = math.degrees(math.acos(max(-1.0, min(1.0, d))))


def compute_metrics(trace: dict, measure: list[str]) -> dict:
    """Run the named metric groups over a trace. Values that cannot be measured come back as None."""
    out: dict = {}
    for token in measure:
        if token == "throttle_step":
            _throttle_step(trace, out)
        elif token.startswith("turn_step:"):
            _turn_step(trace, out, token.split(":", 1)[1])
        elif token == "afterburner_burst":
            _afterburner_burst(trace, out)
        elif token == "afterburner_empty":
            _afterburner_empty(trace, out)
        elif token == "sideslip":
            _sideslip(trace, out)
        else:
            raise ValueError(f"unknown metric group '{token}'")
    return out


# ---------------------------------------------------------------------------------------
# Units and tolerances (justification in README.md)
# ---------------------------------------------------------------------------------------

def unit_of(key: str) -> str:
    if re.search(r"_t(63|95)$|_settle$|_decay$|_empty_t$", key):
        return "s"
    if key.endswith("_plateau"):
        return "rad/s"
    if key in ("speed_start", "speed_end", "ab_peak", "ab_end", "slide_peak"):
        return "m/s"
    if key == "turn_angle_deg":
        return "deg"
    if key == "autobank_ratio":
        return "ratio"
    raise KeyError(f"no unit known for metric '{key}'")


# (relative tolerance, absolute floor) per unit; PASS when |ours - ref| <= max(rel * |ref|, floor).
TOLERANCE_BY_UNIT = {
    "s": (0.05, 0.04),
    "m/s": (0.02, 0.5),
    "rad/s": (0.02, 0.02),
    "deg": (0.03, 1.0),
    "ratio": (0.0, 0.03),
}
TOLERANCE_OVERRIDE = {
    # FS2's own sideways slip depends on tick rate (about +dt / (2 * $Damp) at full yaw rate), so a
    # 60 Hz reference is only good to ~10% for a game ticking anywhere from 30 to 240 Hz.
    "slide_peak": (0.10, 0.5),
}


def tolerance_of(key: str) -> tuple[float, float]:
    return TOLERANCE_OVERRIDE.get(key) or TOLERANCE_BY_UNIT[unit_of(key)]


def allowed_error(key: str, ref_value: float) -> float:
    rel, floor = tolerance_of(key)
    return max(rel * abs(ref_value), floor)


# ---------------------------------------------------------------------------------------
# Closed-form expectations, derived from the ship table, used only to sanity-check the reference
# ---------------------------------------------------------------------------------------

def expected_metrics(ship, measure: list[str], trace: dict, dt: float) -> dict:
    """What the exponential rules predict for the metrics of each group, from ship numbers alone."""
    ln_t63 = -math.log(1.0 - FRACTIONS[0])
    ln_t95 = -math.log(1.0 - FRACTIONS[1])
    exp: dict = {}
    t = trace["time"]
    for token in measure:
        if token == "throttle_step":
            thr = trace["throttle"]
            k0 = _first_index(lambda i: abs(thr[i] - thr[i - 1]) > _EPS, len(t), 1)
            rising = thr[k0] > thr[k0 - 1]
            tau = ship.forward_accel if rising else ship.forward_decel
            exp["speed_t63"], exp["speed_t95"] = tau * ln_t63, tau * ln_t95
            exp["speed_start"], exp["speed_end"] = thr[k0 - 1] * ship.max_speed, thr[k0] * ship.max_speed
        elif token.startswith("turn_step:"):
            axis = token.split(":", 1)[1]
            idx = {"pitch": 0, "yaw": 1, "roll": 2}[axis]
            stick = trace[_AXIS[axis][0]]
            k0 = _first_index(lambda i: abs(stick[i]) > _EPS, len(t))
            exp[f"{axis}_rate_t63"], exp[f"{axis}_rate_t95"] = ship.rotdamp * ln_t63, ship.rotdamp * ln_t95
            exp[f"{axis}_plateau"] = stick[k0] * ship.max_rotvel[idx]
            exp[f"{axis}_settle"] = ship.rotdamp * -math.log(SETTLE_FRACTION)
            if axis == "yaw":
                exp["autobank_ratio"] = -0.5
        elif token == "afterburner_burst":
            ab, thr = trace["afterburner"], trace["throttle"]
            k_on, k_off = _afterburner_edges(trace)
            cruise = thr[k_on] * ship.max_speed
            tau_up, tau_down = ship.aburn_forward_accel, ship.forward_decel
            # The physics reacts one tick after the key edge (flag is set after the physics step), so the
            # speed-up lasts hold - dt, and "peak" is the speed reached at release, a hair under the goal.
            reached = 1.0 - math.exp(-(t[k_off] - t[k_on] - dt) / tau_up)
            exp["ab_peak"] = cruise + (ship.aburn_max_vel[2] - cruise) * reached
            for f, key in zip(FRACTIONS, ("t63", "t95")):
                exp[f"ab_{key}"] = dt - tau_up * math.log(1.0 - f * reached)
                exp[f"ab_release_{key}"] = dt - tau_down * math.log(1.0 - f)
            exp["ab_end"] = cruise
        elif token == "afterburner_empty":
            exp["fuel_empty_t"] = ship.aburn_fuel / ship.aburn_burn_rate
            exp["fuelout_t63"] = dt + ship.forward_decel * ln_t63       # the flag drops in the tick the fuel reads 0
            exp["fuelout_t95"] = dt + ship.forward_decel * ln_t95
    return exp
