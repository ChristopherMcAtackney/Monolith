"""Writes the capture plans for the diaspora's ships (art_src/diaspora) in the ships' own frame (x starboard, y up, z bow): the Vanguard fighter and the Small Mercy, one
plan per lighting set (the Sun is fixed in the sky, so each set turns the ship to put the light where the shot wants it), the fighter in formation with its wingmen, the
Mercy next to Earth's corvette for contrast, and the damaged sets.

    python tools/ship/make_diaspora_plan.py            -> gauntlet/shots/diaspora_<ship>_<set>.json, diaspora_formation.json, diaspora_contrast.json

Sets: ``hero`` (a three-quarter from the aft starboard quarter, Sun ahead and above to starboard), ``side`` (the side silhouette, lit from the camera's side), ``rake``
(raking light across the hero face with the adjacent faces dark), ``damage`` (parts gone, scorch, breaches).
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_ship_plan as M      # noqa: E402

ROOT = M.ROOT
SHIPS = {
    "vanguard_fighter": dict(L=13.1, span=10.3, h=4.7, short="vanguard"),
    "diaspora_cruiser": dict(L=350.0, span=98.0, h=93.0, short="mercy"),
}
SUNS = {"hero": (0.60, 0.52, 0.45), "side": (0.86, 0.34, 0.25), "rake": (0.93, 0.22, -0.22)}


def fighter_shots(which):
    L = SHIPS["vanguard_fighter"]["L"]
    if which == "hero":
        return [("hero_three_quarter", (0.78 * L, 0.34 * L, -0.80 * L), (0.0, 0.0, 0.03 * L), 34), ("hero_front_quarter", (0.7 * L, 0.28 * L, 0.85 * L), (0.0, 0.0, -0.02 * L), 34),
                ("mid_three_quarter", (3.0 * L, 1.1 * L, -3.4 * L), (0.0, 0.0, 0.0), 22)]
    if which == "side":
        return [("side_silhouette", (1.9 * L, 0.04 * L, 0.0), (0.0, 0.0, 0.0), 30), ("top_silhouette", (0.0, 1.9 * L, 0.0), (0.0, 0.0, 0.0), 30)]
    return [("raking", (0.55 * L, 0.42 * L, 0.65 * L), (0.0, 0.0, -0.1 * L), 34), ("raking_close", (0.38 * L, 0.2 * L, 0.3 * L), (0.0, 0.0, -0.18 * L), 40)]


def cruiser_shots(which):
    if which == "hero":
        return [("hero_three_quarter", (250.0, 95.0, -330.0), (0.0, 6.0, -10.0), 40), ("hero_bow_quarter", (190.0, 80.0, 360.0), (0.0, 4.0, 60.0), 42),
                ("far_three_quarter", (620.0, 230.0, -760.0), (0.0, 6.0, 0.0), 26)]
    if which == "side":
        return [("side_silhouette", (560.0, 6.0, 0.0), (0.0, 4.0, 0.0), 34), ("top_silhouette", (0.0, 560.0, 0.0), (0.0, 0.0, 0.0), 34)]
    return [("raking", (170.0, 52.0, -110.0), (20.0, 6.0, -20.0), 46), ("raking_close", (96.0, 34.0, -70.0), (26.0, 4.0, -20.0), 50),
            ("hangars_close", (125.0, 10.0, 12.0), (44.0, 0.0, 6.0), 46), ("engines_close", (96.0, 30.0, -250.0), (30.0, 0.0, -160.0), 48),
            ("bridge_close", (40.0, 66.0, 20.0), (-7.0, 34.0, 82.0), 50), ("crest_close", (62.0, 52.0, 200.0), (0.0, 22.0, 100.0), 46)]


def write(name, plan):
    d = os.path.join(ROOT, "gauntlet", "shots")
    with open(os.path.join(d, name + ".json"), "w") as f:
        json.dump(plan, f, indent=1)
    print("wrote", os.path.join(d, name + ".json"))


def single(cid, which):
    orient, err = M.solve_orientation(SUNS[which])
    shots = fighter_shots(which) if cid == "vanguard_fighter" else cruiser_shots(which)
    stills = [M.shot(n, cam, look, fov, orient) for n, cam, look, fov in shots]
    plan = {"scene": "corvette", "sceneOptions": {"ship": cid, "pitch": orient[0], "yaw": orient[1], "roll": orient[2]},
            "defaults": {"width": 1920, "height": 1080, "warmup": 1.5, "perfWarmup": 2.0}, "stills": stills, "perf": []}
    write(f"diaspora_{SHIPS[cid]['short']}_{which}", plan)
    return orient


DAMAGE = {
    "vanguard_fighter": dict(destroy=["SUB_engine_S", "SUB_mount_P", "SUB_sensors_eye"], health={"SUB_engine_P": 0.3, "SUB_mount_H": 0.0},
                             hits=[((1.4, 0.1, -1.8), (1, 0, 0), 0.9, 0.9), ((3.2, 0.16, -0.3), (0, 1, 0), 0.9, 0.8), ((-1.0, 1.1, -3.4), (0, 1, 0), 0.8, 0.7)]),
    "diaspora_cruiser": dict(destroy=["SUB_hangar_S1", "SUB_flak_P04", "SUB_aaf_D02", "SUB_aaf_V01", "SUB_engine_S", "SUB_sensors_crest", "SUB_turret_fore"],
                             health={"SUB_flak_S01": 0.0, "SUB_aaf_D03": 0.0, "SUB_hangar_P2": 0.3}, hits=[
                                 ((36.4, 12.0, -30.0), (1, 0, 0), 5.0, 0.9), ((20.0, 28.5, 5.0), (0, 1, 0), 4.0, 0.8), ((-36.2, -6.0, 20.0), (-1, 0, 0), 5.0, 0.9),
                                 ((12.0, 17.0, 90.0), (0, 1, 0), 3.5, 0.8), ((26.0, 3.0, -100.0), (1, 0, 0), 4.0, 0.7)]),
}


def damaged(cid):
    orient, err = M.solve_orientation(SUNS["hero"])
    d = DAMAGE[cid]
    if cid == "vanguard_fighter":
        shots = [("damage_three_quarter", (0.78 * 13.1, 0.34 * 13.1, -0.80 * 13.1), (0.0, 0.0, 0.4), 34), ("damage_rake", (0.55 * 13.1, 0.42 * 13.1, 0.65 * 13.1), (0.0, 0.0, -1.3), 34)]
    else:
        shots = [("damage_three_quarter", (250.0, 95.0, -330.0), (0.0, 6.0, -10.0), 40), ("damage_close", (110.0, 40.0, -60.0), (34.0, 10.0, -30.0), 50)]
    stills = [M.shot(n, cam, look, fov, orient) for n, cam, look, fov in shots]
    opts = {"ship": cid, "pitch": orient[0], "yaw": orient[1], "roll": orient[2],
            "damage": {"destroy": d["destroy"], "health": d["health"], "debrisAge": 0.8, "hull": 0.7, "hits": [M.hit(p, n, r, sev, orient) for p, n, r, sev in d["hits"]]}}
    write(f"diaspora_{SHIPS[cid]['short']}_damage", {"scene": "corvette", "sceneOptions": opts, "defaults": {"width": 1920, "height": 1080, "warmup": 1.5, "perfWarmup": 2.0},
                                                      "stills": stills, "perf": []})


def formation():
    """Four Vanguards in a finger-four, the Sun ahead and to starboard so their fronts and tops are lit: the wingmen as the player sees them."""
    sun = (0.50, 0.46, 0.62)
    orient, err = M.solve_orientation(sun)
    R = lambda p: M.world(p, orient)
    ships = [{"ship": "vanguard_fighter", "pos": R((-15.0, -0.8, -12.0)), "rot": list(orient)},
             {"ship": "vanguard_fighter", "pos": R((17.0, 1.4, -17.0)), "rot": list(orient)},
             {"ship": "vanguard_fighter", "pos": R((-30.0, -2.0, -26.0)), "rot": list(orient)},
             {"ship": "diaspora_cruiser", "pos": R((60.0, -140.0, -760.0)), "rot": list(orient)}]
    opts = {"ship": "vanguard_fighter", "pitch": orient[0], "yaw": orient[1], "roll": orient[2], "ships": ships}
    stills = [M.shot("formation_front", (30.0, 14.0, 34.0), (-4.0, -1.0, -14.0), 44, orient), M.shot("formation_side", (60.0, 8.0, -14.0), (-6.0, 0.0, -14.0), 40, orient),
              M.shot("formation_lead", (17.0, 6.0, 20.0), (-2.0, 0.0, -8.0), 42, orient), M.shot("formation_aft", (-22.0, 14.0, -52.0), (-6.0, -1.0, -14.0), 42, orient)]
    write("diaspora_formation", {"scene": "corvette", "sceneOptions": opts, "defaults": {"width": 1920, "height": 1080, "warmup": 1.5, "perfWarmup": 2.0}, "stills": stills, "perf": []})


def contrast():
    """The Small Mercy and Earth's corvette side by side, flying the same way: the same length, the opposite look."""
    sun = (0.55, 0.50, 0.30)
    orient, err = M.solve_orientation(sun)
    R = lambda p: M.world(p, orient)
    ships = [{"ship": "earth_corvette", "pos": R((-200.0, 14.0, 90.0)), "rot": list(orient)}]
    opts = {"ship": "diaspora_cruiser", "pitch": orient[0], "yaw": orient[1], "roll": orient[2], "ships": ships}
    stills = [M.shot("contrast_wide", (360.0, 95.0, 330.0), (-100.0, 6.0, 40.0), 52, orient), M.shot("contrast_pair", (300.0, 60.0, -420.0), (-100.0, 6.0, 20.0), 46, orient),
              M.shot("contrast_split", (120.0, 200.0, 150.0), (-100.0, 0.0, 40.0), 56, orient)]
    write("diaspora_contrast", {"scene": "corvette", "sceneOptions": opts, "defaults": {"width": 1920, "height": 1080, "warmup": 1.5, "perfWarmup": 2.0}, "stills": stills, "perf": []})


def main():
    for cid in SHIPS:
        for which in SUNS:
            print(f"  {cid} {which}: orientation {single(cid, which)}")
        damaged(cid)
    formation()
    contrast()


if __name__ == "__main__":
    main()
