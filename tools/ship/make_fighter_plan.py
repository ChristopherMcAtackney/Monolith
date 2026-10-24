"""Writes the capture plans for Earth's fighters (art_src/earth_fighters) in the ships' own frame (x starboard, y up, z bow), one plan per lighting set: the Sun is
fixed in the sky, so each set turns the ship (or ships) to put the light where the shot wants it.

    python tools/ship/make_fighter_plan.py            -> gauntlet/shots/fighters_<class>_<set>.json and fighters_together.json

Sets per class: ``hero`` (a three-quarter from the aft starboard quarter, Sun ahead and above to starboard, so the top and starboard faces carry the frame and the
rest falls dark), ``side`` (the side silhouette, lit from the camera's side so the shape reads), ``rake`` (raking light across the hero face with the adjacent faces
dark), and ``together`` (all three next to the corvette for scale).
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_ship_plan as M      # noqa: E402

ROOT = M.ROOT
CLASSES = {
    # id: (length, span, height, centre z offset (ship frame y), dolly scale)
    "earth_interceptor": dict(L=10.6, span=8.6, h=3.8),
    "earth_fighter": dict(L=15.0, span=12.2, h=4.4),
    "earth_heavy": dict(L=14.4, span=12.6, h=5.4),
    "earth_bomber": dict(L=20.2, span=15.2, h=5.4),
}
SUNS = {"hero": (0.60, 0.52, 0.45), "side": (0.86, 0.34, 0.25), "rake": (0.93, 0.22, -0.22)}


def class_shots(cid, which, orient):
    c = CLASSES[cid]
    L, S = c["L"], c["span"]
    if which == "hero":
        return [("hero_three_quarter", (0.78 * L, 0.34 * L, -0.80 * L), (0.0, 0.0, 0.03 * L), 34),
                ("hero_front_quarter", (0.7 * L, 0.28 * L, 0.85 * L), (0.0, 0.0, -0.02 * L), 34)]
    if which == "side":
        return [("side_silhouette", (1.9 * L, 0.04 * L, 0.0), (0.0, 0.0, 0.0), 30), ("top_silhouette", (0.0, 1.9 * L, 0.0), (0.0, 0.0, 0.0), 30)]
    if which == "rake":
        return [("raking", (0.55 * L, 0.42 * L, 0.65 * L), (0.0, 0.0, -0.1 * L), 34), ("raking_close", (0.38 * L, 0.2 * L, 0.3 * L), (0.0, 0.0, -0.18 * L), 40)]
    raise ValueError(which)


def write(name, plan):
    d = os.path.join(ROOT, "gauntlet", "shots")
    with open(os.path.join(d, name + ".json"), "w") as f:
        json.dump(plan, f, indent=1)
    print("wrote", os.path.join(d, name + ".json"))


def single(cid, which):
    sun = SUNS[which]
    orient, err = M.solve_orientation(sun)
    stills = [M.shot(n, cam, look, fov, orient) for n, cam, look, fov in class_shots(cid, which, orient)]
    plan = {
        "scene": "corvette",
        "sceneOptions": {"ship": cid, "pitch": orient[0], "yaw": orient[1], "roll": orient[2]},
        "defaults": {"width": 1920, "height": 1080, "warmup": 1.5, "perfWarmup": 2.0},
        "stills": stills, "perf": [],
    }
    write(f"fighters_{cid.replace('earth_', '')}_{which}", plan)
    return orient


DAMAGE = {
    # ship-frame (x starboard, y up, z bow) hits: position, outward normal
    "earth_interceptor": dict(destroy=["SUB_engine_S", "SUB_mount_P", "SUB_sensors_eye", "SUB_launcher_rack"], health={"SUB_engine_P": 0.3, "SUB_mount_H": 0.0},
                              hits=[((0.82, 0.0, -0.9), (1, 0, 0), 0.9, 0.9), ((2.6, 0.12, 1.9), (0, 1, 0), 0.8, 0.8), ((0.0, 0.58, -2.2), (0, 1, 0), 0.7, 0.7)]),
    "earth_fighter": dict(destroy=["SUB_engine_S", "SUB_mount_P", "SUB_sensors_eye", "SUB_launcher_rack"], health={"SUB_engine_P": 0.3, "SUB_mount_H": 0.0},
                          hits=[((1.24, 0.0, -0.6), (1, 0, 0), 0.9, 0.9), ((3.6, 0.24, 4.3), (0, 1, 0), 0.9, 0.8), ((-3.2, 0.24, 4.3), (0, 1, 0), 0.8, 0.7)]),
    "earth_heavy": dict(destroy=["SUB_engine_S", "SUB_mount_S", "SUB_sensors_eye", "SUB_launcher_rack"], health={"SUB_engine_P": 0.3, "SUB_mount_H": 0.0},
                        hits=[((1.62, 0.0, 0.2), (1, 0, 0), 1.0, 0.9), ((3.6, 0.17, -0.3), (0, 1, 0), 0.9, 0.8), ((-3.4, 0.17, -0.4), (0, 1, 0), 0.8, 0.7)]),
    "earth_bomber": dict(destroy=["SUB_engine_P", "SUB_mount_S", "SUB_sensors_eye", "SUB_comms_mast", "SUB_launcher_cradle"], health={"SUB_engine_S": 0.3},
                         hits=[((4.5, 1.22, -1.5), (0, 1, 0), 1.0, 0.9), ((5.6, 0.18, 3.4), (0, 1, 0), 0.9, 0.8), ((-4.5, 1.22, 0.8), (0, 1, 0), 0.9, 0.8)]),
}


def damaged(cid):
    sun = SUNS["hero"]
    orient, err = M.solve_orientation(sun)
    c = CLASSES[cid]
    L = c["L"]
    d = DAMAGE[cid]
    stills = [M.shot("damage_three_quarter", (0.78 * L, 0.34 * L, -0.80 * L), (0.0, 0.0, 0.03 * L), 34, orient),
              M.shot("damage_rake", (0.55 * L, 0.42 * L, 0.65 * L), (0.0, 0.0, -0.1 * L), 34, orient)]
    opts = {"ship": cid, "pitch": orient[0], "yaw": orient[1], "roll": orient[2],
            "damage": {"destroy": d["destroy"], "health": d["health"], "debrisAge": 0.6, "hull": 0.7,
                       "hits": [M.hit(p, n, r, sev, orient) for p, n, r, sev in d["hits"]]}}
    plan = {"scene": "corvette", "sceneOptions": opts, "defaults": {"width": 1920, "height": 1080, "warmup": 1.5, "perfWarmup": 2.0}, "stills": stills, "perf": []}
    write(f"fighters_{cid.replace('earth_', '')}_damage", plan)


def together():
    """The three fighters in a loose formation in front of the corvette's bow, a hull length or two away, for scale: the corvette is 374 m, the bomber 20."""
    sun = (0.70, 0.45, 0.35)
    orient, err = M.solve_orientation(sun)
    R = M.rot(*orient)
    # corvette at the origin, bow at +z; the fighters hang off its starboard quarter, 60 m out and 120 m forward of the main hull, in one frame
    ships = [
        {"ship": "earth_corvette", "pos": (0.0, 0.0, 0.0)},
        {"ship": "earth_interceptor", "pos": (70.0, 12.0, 20.0)},
        {"ship": "earth_heavy", "pos": (100.0, 4.0, 4.0)},
        {"ship": "earth_bomber", "pos": (132.0, 14.0, 22.0)},
    ]
    opts = {"ship": "earth_corvette", "pitch": orient[0], "yaw": orient[1], "roll": orient[2],
            "ships": [{"ship": s["ship"], "pos": M.world(s["pos"], orient), "rot": list(orient)} for s in ships[1:]]}
    stills = [M.shot("scale_formation", (150.0, 36.0, -110.0), (95.0, 8.0, 14.0), 36, orient),
              M.shot("scale_with_corvette", (210.0, 70.0, -260.0), (30.0, 0.0, 0.0), 40, orient),
              M.shot("scale_close", (118.0, 16.0, -32.0), (100.0, 8.0, 14.0), 40, orient)]
    plan = {"scene": "corvette", "sceneOptions": opts, "defaults": {"width": 1920, "height": 1080, "warmup": 1.5, "perfWarmup": 2.0}, "stills": stills, "perf": []}
    write("fighters_together", plan)


def main():
    only = sys.argv[1:] or list(CLASSES)
    for cid in CLASSES:
        if cid in only or cid.replace("earth_", "") in only:
            for which in SUNS:
                o = single(cid, which)
                print(f"  {cid} {which}: orientation {o}")
            damaged(cid)
    if not sys.argv[1:] or "together" in only:
        together()


if __name__ == "__main__":
    main()
