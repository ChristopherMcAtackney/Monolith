"""Summarise a tools/mission_sweep.ps1 output directory (one folder per <Level>_s<seed>, each with autoplay.json).

    python -I tools/mission_sweep_summary.py DIR [--events "Lantern here,Wick here"] [--ship Tamsin]

Per level: passes out of runs, the length of the run (mean, min-max), the longest quiet, the most hostiles on the player, the most threats,
the hull the watched ship (--ship, default the first of the mission's pacing.hulls) ended at and whether it survived, how many runs fired each
named event, and every failed check with how many runs failed it (and the seeds).
"""
import json
import os
import re
import sys
from collections import Counter, defaultdict

LEVELS = ["Veryeasy", "VeryEasy", "Easy", "Medium", "Hard", "Insane"]      # the folder name drops the space of "Very easy"


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    root = argv[0]
    events = []
    ship = None
    i = 1
    while i < len(argv):
        if argv[i] == "--events":
            events = [e.strip() for e in argv[i + 1].split(",") if e.strip()]
            i += 2
        elif argv[i] == "--ship":
            ship = argv[i + 1]
            i += 2
        else:
            i += 1
    runs = defaultdict(list)
    for name in sorted(os.listdir(root)):
        m = re.match(r"^(.*)_s(\d+)$", name)
        path = os.path.join(root, name, "autoplay.json")
        if not m:
            continue
        if not os.path.exists(path):
            runs[m.group(1)].append({"seed": int(m.group(2)), "missing": True})
            continue
        with open(path, encoding="utf-8") as f:
            j = json.load(f)
        j["seed"] = int(m.group(2))
        runs[m.group(1)].append(j)
    order = [l for l in LEVELS if l in runs] + [l for l in sorted(runs) if l not in LEVELS]
    for lvl in order:
        rs = runs[lvl]
        ok = [r for r in rs if not r.get("missing")]
        missing = [r["seed"] for r in rs if r.get("missing")]
        passes = [r for r in ok if r.get("pass")]
        print(f"== {lvl}: {len(passes)} of {len(rs)} pass" + (f" (no report for seeds {missing})" if missing else ""))
        if not ok:
            continue
        ends = [r["endedAt"] for r in ok if r.get("endedAt") is not None]
        if ends:
            print(f"   duration  mean {sum(ends)/len(ends):6.1f} s   min {min(ends):6.1f}   max {max(ends):6.1f}   ({len(ends)} ended)")
        results = Counter(r.get("result") for r in ok)
        print("   results   " + ", ".join(f"{k} {v}" for k, v in sorted(results.items(), key=lambda kv: str(kv[0]))))
        q = [r["pacing"]["longestQuiet"] for r in ok]
        print(f"   quiet     mean {sum(q)/len(q):5.1f} s   max {max(q):5.1f}")
        e = [r["pacing"]["maxEngaging"] for r in ok]
        print(f"   on player mean {sum(e)/len(e):5.1f}     max {max(e):3d}   (per run: {' '.join(str(x) for x in sorted(e))})")
        t = [r["pacing"]["maxThreats"] for r in ok]
        a = [r["pacing"]["avgThreats"] for r in ok]
        print(f"   threats   max mean {sum(t)/len(t):5.1f} (max {max(t)})   avg mean {sum(a)/len(a):4.1f} (min {min(a):.1f})")
        k = [r.get("kills", 0) for r in ok]
        print(f"   kills by the player mean {sum(k)/len(k):4.1f}  (min {min(k)}, max {max(k)})")
        name = ship
        if name is None:
            for r in ok:
                if r.get("capitals"):
                    name = next(iter(r["capitals"]))
                    break
        if name:
            lows, gone, dead, away = [], 0, 0, 0
            for r in ok:
                c = (r.get("capitals") or {}).get(name)
                if not c:
                    continue
                if c.get("lowestHull") is not None:
                    lows.append(c["lowestHull"])
                if c.get("destroyed") is not None:
                    dead += 1
                if c.get("departed") is not None:
                    away += 1
            if lows:
                print(f"   {name}: survives in {len(ok) - dead} of {len(ok)} (destroyed {dead}, jumped out {away}); lowest hull mean {sum(lows)/len(lows):.0f}% min {min(lows):.0f}%")
        if events:
            row = []
            for en in events:
                n = sum(1 for r in ok if any(ev["name"] == en for ev in r.get("events", [])))
                row.append(f"{en} {n}/{len(ok)}")
            print("   events    " + "; ".join(row))
        fails = defaultdict(list)
        for r in ok:
            for c in r.get("checks", []):
                if not c["pass"]:
                    fails[re.sub(r"\d+(\.\d+)?", "#", c["what"])].append((r["seed"], c["what"]))
        for key, lst in sorted(fails.items(), key=lambda kv: -len(kv[1])):
            seeds = ",".join(str(s) for s, _ in lst)
            print(f"   FAIL x{len(lst)}: {lst[0][1]}  [seeds {seeds}]")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
