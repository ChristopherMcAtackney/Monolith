"""Generate FS2 flight reference traces and metrics.

    python -I tools/fs2ref/make_reference.py                       # the four reference classes
    python -I tools/fs2ref/make_reference.py "GTF Ulysses" --hz 120

Writes, per ship class, into <out>/<Class_Name>/ :
    ship.json                  flight fields read from ships.tbl, plus where they came from
    summary.json               every scenario's metrics in one file
    <scenario>/trace.csv       one row per tick
    <scenario>/metrics.json    metrics, units, tolerances, and the closed-form expectation they were checked against
Output is deterministic (no timestamps), so regenerating gives clean git diffs.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python -I does not add the script folder

import ships  # noqa: E402
from fs2_flight import DEFAULT_SKILL_LEVEL, simulate  # noqa: E402
from metrics import (  # noqa: E402
    allowed_error, compute_metrics, expected_metrics, tolerance_of, unit_of, write_trace,
)
from scenarios import load_scenarios  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DEFAULT_SCENARIOS = os.path.join(REPO, "gauntlet", "shots", "flight_scenarios.json")
DEFAULT_OUT = os.path.join(REPO, "gauntlet", "reference", "fs2_flight")


def folder_name(ship_name: str) -> str:
    return ship_name.replace(" ", "_")


def _round(x, nd=5):
    return None if x is None else round(x, nd)


def check_against_expectation(measured: dict, expected: dict):
    """Return [(key, measured, expected, ok)] for keys present in both; ok = within 3% or one-tick-ish 0.05."""
    rows = []
    for k, e in expected.items():
        m = measured.get(k)
        if m is None:
            rows.append((k, m, e, False))
            continue
        tol = max(0.03 * abs(e), 0.05 if unit_of(k) == "s" else 0.02)
        rows.append((k, m, e, abs(m - e) <= tol))
    return rows


def build(ship_names, scenario_path, out_root, hz, skill, vp, quiet=False):
    scenarios = load_scenarios(scenario_path)
    dt = 1.0 / hz
    sha = ships.ships_tbl_sha256(vp)
    problems = 0
    for name in ship_names:
        ship = ships.get_ship(name, vp)
        out_dir = os.path.join(out_root, folder_name(ship.name))
        os.makedirs(out_dir, exist_ok=True)
        info = {
            "ship": ship.to_dict(),
            "source": {
                "table": "data/tables/ships.tbl inside Root_fs2.vp (Freespace 2 retail, Steam)",
                "ships_tbl_sha256": sha,
                "model": "tools/fs2ref/fs2_flight.py (rules documented there and in tools/fs2ref/README.md)",
            },
            "sim": {"tick_hz": hz, "skill_level": skill, "ets_engine_index": 4},
        }
        _dump(os.path.join(out_dir, "ship.json"), info)
        summary = {"ship": ship.name, "tick_hz": hz, "scenarios": {}}
        if not quiet:
            print(f"\n=== {ship.name}  (top speed {ship.max_speed:g} m/s, turn {ship.rotation_time} s, "
                  f"rotdamp {ship.rotdamp}, damp {ship.damp}, accel/decel {ship.forward_accel}/{ship.forward_decel})")
        for sc in scenarios:
            rows = simulate(ship, sc, dt=dt, skill_level=skill)
            sdir = os.path.join(out_dir, sc.name)
            os.makedirs(sdir, exist_ok=True)
            write_trace(os.path.join(sdir, "trace.csv"), rows)
            trace = {k: [r[k] for r in rows] for k in rows[0]}
            m = compute_metrics(trace, sc.measure)
            exp = expected_metrics(ship, sc.measure, trace, dt)
            checks = check_against_expectation(m, exp)
            doc = {
                "scenario": sc.name,
                "ship": ship.name,
                "tick_hz": hz,
                "measure": sc.measure,
                "metrics": {k: _round(v) for k, v in m.items()},
                "units": {k: unit_of(k) for k in m},
                "tolerance": {k: {"relative": tolerance_of(k)[0], "floor": tolerance_of(k)[1]} for k in m},
                "closed_form_expectation": {k: _round(v) for k, v in exp.items()},
            }
            _dump(os.path.join(sdir, "metrics.json"), doc)
            summary["scenarios"][sc.name] = doc["metrics"]
            if not quiet:
                print(f"  {sc.name}")
                for k, v in m.items():
                    e = exp.get(k)
                    flag = ""
                    if e is not None:
                        ok = next(c[3] for c in checks if c[0] == k)
                        flag = f"   expected {e:.4f}" + ("" if ok else "   <-- DIFFERS")
                        problems += 0 if ok else 1
                    vs = "None" if v is None else f"{v:.4f}"
                    print(f"      {k:<18} {vs:>10} {unit_of(k):<5}{flag}")
            else:
                problems += sum(0 if c[3] else 1 for c in checks)
        _dump(os.path.join(out_dir, "summary.json"), summary)
    return problems


def _dump(path, doc):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2)
        f.write("\n")


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("ships", nargs="*", help="ship class names (default: the four reference classes)")
    ap.add_argument("--scenarios", default=DEFAULT_SCENARIOS)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--hz", type=float, default=60.0, help="simulation tick rate (default 60)")
    ap.add_argument("--skill", type=int, default=DEFAULT_SKILL_LEVEL,
                    help="FS2 skill level 0-4 (only changes afterburner fuel recharge speed; default 1 = Easy)")
    ap.add_argument("--vp", default=None, help="path to Root_fs2.vp (default: FS2_VP env var or the Steam install)")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)
    names = args.ships or list(ships.REFERENCE_CLASSES)
    problems = build(names, args.scenarios, args.out, args.hz, args.skill, args.vp, args.quiet)
    print(f"\nwrote references for {len(names)} class(es) to {args.out}")
    if problems:
        print(f"WARNING: {problems} metric(s) differ from the closed-form expectation")
        return 1
    print("every metric matches its closed-form expectation")
    return 0


if __name__ == "__main__":
    sys.exit(main())
