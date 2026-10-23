#!/usr/bin/env python
"""Build a juice-sandbox capture plan whose stills are taken at absolute game times.

A capture plan gives each still a `warmup` (game seconds run since the previous still), which makes it awkward to say
"take a picture 1.5 s after the capital ship starts to die". This takes a spec with absolute times and writes the plan.

  python tools/juice/make_plan.py spec.json plan.json

spec.json:
  { "scene": "juice", "width": 1280, "height": 720,
    "script": [ { "t": 1.0, "do": "event", "name": "capital_death" }, ... ],     (the sandbox script, see Game/Sandbox/SandboxScript.cs)
    "options": { "preset": "Medium" },                                          (extra sceneOptions, optional)
    "at":     [ { "name": "internal", "t": 3.0 }, ... ],                         (single stills)
    "series": [ { "name": "cap", "from": 1.0, "to": 12.0, "step": 0.0333 } ] }  (numbered frames cap_000, cap_001, ...)
Stills are sorted by time. The first still is taken at about 2 ticks after its time (the runner needs two frames to start).
"""
import json
import sys

TICK = 1.0 / 60.0


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    spec = json.load(open(sys.argv[1], encoding="utf-8"))
    shots = []
    for a in spec.get("at", []):
        shots.append((float(a["t"]), a["name"]))
    for s in spec.get("series", []):
        t, i = float(s["from"]), 0
        while t <= float(s["to"]) + 1e-6:
            shots.append((t, f"{s['name']}_{i:03d}"))
            t += float(s["step"])
            i += 1
    shots.sort()
    stills, prev = [], 2 * TICK
    for t, name in shots:
        ticks = max(1, round((t - prev) / TICK))
        stills.append({"name": name, "follow": True, "warmup": round(ticks * TICK + 1e-4, 4)})
        prev += ticks * TICK
    opts = {"script": spec["script"]}
    opts.update(spec.get("options", {}))
    plan = {
        "scene": spec.get("scene", "juice"),
        "sceneOptions": opts,
        "defaults": {"width": spec.get("width", 1280), "height": spec.get("height", 720), "warmup": 1.0, "perfWarmup": 2.0},
        "stills": stills,
    }
    if "perf" in spec:
        plan["perf"] = spec["perf"]
    json.dump(plan, open(sys.argv[2], "w", encoding="utf-8"), indent=1)
    print(f"{sys.argv[2]}: {len(stills)} stills, last at about {prev:.2f} s")


if __name__ == "__main__":
    main()
