r"""A portrait of each comet model, in the real mission: reads a player log written with ``-mnLogScenery`` (which rock is which model, where, how big) and writes a capture plan with, for each model,
its biggest rock in the field seen at two phases of the Sun (a half-lit disc, a crescent) and from far (the model among its neighbours).

    python -I tools/ship/make_comets_tour.py <player.log> [out plan = gauntlet/shots/comets_r1_tour.json]
    tools\capture.ps1 -Plan gauntlet\shots\comets_r1_tour.json -Out gauntlet\captures\comets\r1\tour -Kind stills

Fits are the manifest's (Resources/CometArt/comets.json), so a portrait frames the model whatever the rock's size.
"""
import json
import math
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
from make_comets_plan import cam_for, mission_plan, still, OUT   # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def main():
    log = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(OUT, "comets_r1_tour.json")
    manifest = json.load(open(os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "CometArt", "comets.json")))
    fit = {m["id"]: m["fit"] for m in manifest["models"]}
    scen = {}
    comet = {}
    for line in open(log, encoding="utf-8", errors="replace"):
        m = re.search(r"SCENERY (\w+) (\d+) at (\S+) (\S+) (\S+) radius (\S+) stretch (\S+) (\S+) (\S+)", line)
        if m:
            scen[int(m.group(2))] = ((float(m.group(3)), float(m.group(4)), float(m.group(5))), float(m.group(6)), (float(m.group(7)), float(m.group(8)), float(m.group(9))))
        m = re.search(r"COMET Ice (\d+) model (\w+) radius (\S+) scale (\S+) mirror (\d) tint (\d+) stretch (\S+) (\S+) (\S+)", line)
        if m:
            comet[int(m.group(1))] = (m.group(2), tuple(float(m.group(i)) for i in (7, 8, 9)))
    stills = []
    for mid in sorted(fit):
        mine = [(scen[n][1], n) for n, (id_, _) in comet.items() if id_ == mid and n in scen and scen[n][1] <= 200.0]
        if not mine:
            mine = [(scen[n][1], n) for n, (id_, _) in comet.items() if id_ == mid and n in scen]
        if not mine:
            continue
        r, n = max(mine)
        centre, radius, stretch = scen[n]
        extent = radius * fit[mid] * max(comet[n][1]) * 1.12
        d = extent * 3.0
        short = mid.replace("comet_", "")
        for tag, phase, el, side, dist in (("half", 82, 16, 1, d), ("crescent", 125, 10, -1, d), ("far", 70, 14, 1, d * 3.2)):
            pos = cam_for(centre, dist, phase, el, side)
            stills.append(still(f"tour_{short}_{tag}", pos, centre, 36 if tag != "far" else 40, 0.5))
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(mission_plan(stills), f, indent=1)
    print("wrote", out, len(stills), "stills")


main()
