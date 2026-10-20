"""Compare a folder of our flight traces against an FS2 reference folder.

    python -I tools/fs2ref/compare.py <our trace folder> <reference folder>
    python -I tools/fs2ref/compare.py out/traces gauntlet/reference/fs2_flight/GTF_Ulysses

Our folder holds one trace per scenario, either <scenario>/trace.csv or <scenario>.csv (same columns
as the reference; see README.md). The reference folder is one class folder written by make_reference.py.
Metrics are recomputed from OUR trace with the same code that measured the reference, then each is
checked against the tolerance for its unit (metrics.py, justified in README.md).

Exit status 0 only if every metric of every scenario passes.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python -I does not add the script folder

from metrics import (  # noqa: E402
    TraceError, allowed_error, compute_metrics, read_trace, unit_of,
)


def find_trace(folder: str, scenario: str):
    for candidate in (os.path.join(folder, scenario, "trace.csv"), os.path.join(folder, scenario + ".csv")):
        if os.path.isfile(candidate):
            return candidate
    return None


def tick_rate(trace) -> float:
    t = trace["time"]
    return (len(t) - 1) / (t[-1] - t[0]) if len(t) > 1 and t[-1] > t[0] else float("nan")


def compare(ours_dir: str, ref_dir: str, only=None):
    """Return (rows, notes). Each row: dict(scenario, metric, unit, ref, ours, err, allowed, status, why)."""
    rows, notes = [], []
    scenarios = sorted(
        d for d in os.listdir(ref_dir)
        if os.path.isfile(os.path.join(ref_dir, d, "metrics.json")) and (not only or d in only)
    )
    if not scenarios:
        raise SystemExit(f"no scenario folders with metrics.json in {ref_dir}")
    for sc in scenarios:
        with open(os.path.join(ref_dir, sc, "metrics.json"), encoding="utf-8") as f:
            ref = json.load(f)
        trace_path = find_trace(ours_dir, sc)
        if trace_path is None:
            for k, v in ref["metrics"].items():
                rows.append(dict(scenario=sc, metric=k, unit=unit_of(k), ref=v, ours=None, err=None,
                                 allowed=allowed_error(k, v or 0.0), status="FAIL", why="no trace for this scenario"))
            continue
        try:
            trace = read_trace(trace_path)
            ours = compute_metrics(trace, ref["measure"])
            notes.append(f"{sc}: ours {tick_rate(trace):.1f} Hz, reference {ref['tick_hz']:g} Hz")
        except TraceError as e:
            for k, v in ref["metrics"].items():
                rows.append(dict(scenario=sc, metric=k, unit=unit_of(k), ref=v, ours=None, err=None,
                                 allowed=allowed_error(k, v or 0.0), status="FAIL", why=str(e)))
            continue
        for k, rv in ref["metrics"].items():
            ov = ours.get(k)
            allowed = allowed_error(k, rv if rv is not None else 0.0)
            if rv is None:
                status, err, why = ("PASS", None, "reference could not measure it") if ov is None else ("FAIL", None, "reference has no value but ours does")
            elif ov is None:
                status, err, why = "FAIL", None, "could not measure (signal never reaches the level or settle threshold)"
            else:
                err = ov - rv
                status, why = ("PASS" if abs(err) <= allowed else "FAIL"), ""
            rows.append(dict(scenario=sc, metric=k, unit=unit_of(k), ref=rv, ours=ov, err=err,
                             allowed=allowed, status=status, why=why))
    return rows, notes


def format_table(rows) -> str:
    head = ("scenario", "metric", "unit", "reference", "ours", "error", "allowed", "result")
    body = []
    for r in rows:
        def f(x):
            return "-" if x is None else f"{x:.4f}"
        body.append((r["scenario"], r["metric"], r["unit"], f(r["ref"]), f(r["ours"]),
                     "-" if r["err"] is None else f"{r['err']:+.4f}", f(r["allowed"]),
                     r["status"] + (f"  ({r['why']})" if r["why"] else "")))
    widths = [max(len(head[i]), *(len(b[i]) for b in body)) for i in range(len(head) - 1)]
    lines = []
    fmt = lambda cells: "  ".join(c.ljust(w) for c, w in zip(cells[:-1], widths)) + "  " + cells[-1]
    lines.append(fmt(head))
    lines.append("  ".join("-" * w for w in widths) + "  " + "-" * 6)
    last = None
    for b in body:
        if last is not None and b[0] != last:
            lines.append("")
        last = b[0]
        lines.append(fmt(b))
    return "\n".join(lines)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("ours", help="folder of our traces")
    ap.add_argument("reference", help="reference class folder, e.g. gauntlet/reference/fs2_flight/GTF_Ulysses")
    ap.add_argument("--scenario", action="append", help="only this scenario (repeatable)")
    ap.add_argument("--json", help="also write the result rows to this file")
    args = ap.parse_args(argv)

    rows, notes = compare(args.ours, args.reference, set(args.scenario) if args.scenario else None)
    print(format_table(rows))
    print()
    for n in notes:
        print(n)
    failed = [r for r in rows if r["status"] != "PASS"]
    print(f"\n{len(rows) - len(failed)} of {len(rows)} metrics pass"
          + ("" if not failed else f"; FAILED: {', '.join(sorted({r['scenario'] + '.' + r['metric'] for r in failed}))}"))
    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump({"pass": not failed, "rows": rows}, f, indent=2)
    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main())
