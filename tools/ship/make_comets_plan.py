"""Capture plans for the Oort ice field's comets (gauntlet/pieces/comets): the same framings before and after the field's rocks were swapped for the Blender nuclei.

    python tools/ship/make_comets_plan.py stills   -> gauntlet/shots/comets_r1.json        (the four framings, plus alternates, HUD off, 1920x1080, the real Oort mission)
    python tools/ship/make_comets_plan.py perf     -> gauntlet/shots/comets_r1_perf.json   (the start view, the field from the Mercy's side, the Archon's fight: -mnPerfSync)
    python tools/ship/make_comets_tour.py <player.log>  -> gauntlet/shots/comets_r1_tour.json   (a portrait of each model, from the log of a -mnLogScenery run)

The rocks' placement is the mission's own (`scenery` in oort.mission.json; the player logs it with `-mnLogScenery`); the numbers below were read from the baseline's log and the
after build's log is diffed against them by `tools/ship/check_comets_layout.py` (positions, radii and stretches must not move). A shot is placed from a rock: ``cam_for`` puts the camera
at a distance on the circle of directions that make a chosen phase angle with the Sun (0 = lit face on, 90 = a half-lit disc, relief at its strongest), looking at the rock's centre.
The Sun's direction in this mission is fixed: toward (-0.468, 0.087, -0.880), behind the player's start (so from the start the field is seen full on, and flat).
"""
import json
import math
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "gauntlet", "shots")
SUN = (-0.468, 0.087, -0.880)
OUTPOST = (759.8, 630.0, 7194.8)

# index in the mission's scenery list -> (centre, radius, stretch). Singles are the mission's "rocks"; fields' rocks are numbered in placement order ("Ice n").
ROCKS = {
    1: ((-5200, 1300, 6200), 1400, (1.3, 0.8, 1.1)), 2: ((6800, -900, 9400), 950, (0.9, 0.7, 1.4)), 3: ((-2600, -1900, 14500), 760, (1.2, 1.0, 0.8)),
    4: ((2400, 2200, -3800), 520, (1.0, 0.7, 1.3)), 5: ((-1500, -300, 2600), 140, (1.4, 0.8, 1.0)), 6: ((1300, 260, 1400), 90, (0.9, 0.8, 1.3)),
}
FIELD = {   # read from the baseline's log (-mnLogScenery); the layout never changes
    116: ((-402, 344, 1887), 27.6, (1.16, 0.91, 1.25)), 161: ((-664, -181, 750), 43.3, (0.89, 0.78, 1.13)), 54: ((50, -748, 522), 45.8, (0.82, 1.03, 0.85)),
    137: ((600.5, 247.9, 1434.1), 158.2, (1.15, 0.95, 1.19)), 98: ((-530, -1128.1, 129.9), 135.4, (1.09, 0.98, 0.8)), 59: ((-275, -882, -35), 55.5, (1.01, 0.75, 0.78)),
    121: ((-467, -1174, 2165), 63.9, (0.82, 0.8, 1.35)), 13: ((1196, 706, 4965), 21.7, (0.82, 0.83, 0.85)), 70: ((-302, -903, 3563), 25.1, (1.05, 0.99, 0.84)),
}


def norm(v):
    l = math.sqrt(sum(c * c for c in v))
    return tuple(c / l for c in v)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def cam_for(centre, dist, phase, elev=15.0, side=1.0):
    """A camera ``dist`` metres from ``centre`` such that the angle Sun-rock-camera is ``phase`` degrees (the camera swung to ``side`` (+1 or -1) of the Sun, tilted ``elev`` degrees up)."""
    s = norm(SUN)
    t = norm(cross(s, (0, 1, 0)))
    t = tuple(side * c for c in t)
    p = math.radians(phase)
    c = tuple(math.cos(p) * s[i] + math.sin(p) * t[i] for i in range(3))
    e = math.radians(elev)
    c = norm((c[0], c[1] + math.sin(e), c[2]))
    return [round(centre[i] + c[i] * dist, 1) for i in range(3)]


def still(name, pos=None, look=None, fov=45, warm=0.5, **kw):
    d = {"name": name}
    if pos is not None:
        d["pos"] = [round(v, 1) for v in pos]
        d["lookAt"] = [round(v, 1) for v in look]
        d["fov"] = fov
    d["warmup"] = warm
    d.update(kw)
    return d


def mission_plan(stills, perf=None):
    p = {"scene": "mission", "sceneOptions": {"mission": "oort", "pilot": "ai", "invulnerable": True, "hud": False, "dust": False, "cabin": False},
         "defaults": {"width": 1920, "height": 1080, "warmup": 1.0, "perfWarmup": 3.0}, "stills": stills}
    if perf:
        p["perf"] = perf
    return p


def rock_shot(name, rock, dist_from_surface, phase, fov, elev=15.0, side=1.0, warm=0.5, aim_off=(0, 0, 0)):
    """Aimed at a rock; ``dist_from_surface`` is counted from where the rock's far reach is (radius x its largest stretch x the fit): the same camera before and after."""
    centre, radius, stretch = rock
    fit = 1.5 if radius >= 420 else 1.25                      # how far a comet of the mission's radius reaches (the bound, in radii); the same camera serves the old rocks and the new
    d = radius * max(stretch) * fit + dist_from_surface
    pos = cam_for(centre, d, phase, elev, side)
    look = [centre[i] + aim_off[i] for i in range(3)]
    return still(name, pos, look, fov, warm)


def stills():
    s = []
    # 1. the start view across the field: the seat camera as the mission opens (what the player sees), and a fixed 55 degree view toward the outpost (round 2's framing)
    s.append(still("start_cockpit", follow=True, warm=0.5))
    s.append(still("start_view", (0, 0.4, 1.5), OUTPOST, 55, 3.0))
    s.append(still("start_wide_left", (0, 0.4, 1.5), (-5200, 1300, 6200), 70, 0.5))
    # 2. a big comet at about 2 km (the surface 2 km off): two of the mission's big single rocks
    s.append(rock_shot("big_2km_a", ROCKS[4], 2000, 80, 42, 18, 1))
    s.append(rock_shot("big_2km_b", ROCKS[1], 2000, 72, 55, 12, -1))
    # 3. a small one at about 200 m: a field rock of 28 m and one of 22 m
    s.append(rock_shot("small_200m_a", FIELD[116], 200, 78, 40, 15, 1))
    s.append(rock_shot("small_200m_b", FIELD[13], 200, 62, 40, 10, -1))
    # 4. a close pass at about 50 m
    s.append(rock_shot("pass_50m_a", FIELD[161], 50, 70, 60, 12, 1))
    s.append(rock_shot("pass_50m_b", FIELD[54], 50, 55, 60, 25, -1))
    # alternates: a mid one, 600 m (the biggest in the field) and the field's rocks from the side, to see them together
    s.append(rock_shot("mid_600m", FIELD[137], 600, 75, 45, 15, 1))
    s.append(rock_shot("mid_150m", FIELD[98], 150, 80, 55, 10, -1))
    s.append(still("field_side_1km", (900, 200, 1500), (-400, -300, 1700), 50, 0.5))
    s.append(still("field_side_wide", (1700, 300, 600), (-300, -200, 2200), 62, 0.5))
    return mission_plan(s)


def perf():
    """The start view (the seat camera as the mission opens: the whole field in the 140 degree view), the drift through the field, the strike (the outpost's guns and the waves out of its hangar) and
    the Archon's fight (both big ships, the Mercy, the bombers and their escort up) as the player sees them, the AI flying: the busiest in the mission. Warm-ups add up from one run to the next
    (the runs start at about 0:03, 0:46, 2:19 and 5:32 of mission time)."""
    runs = [{"name": "start_view_follow", "follow": True, "warmup": 0.5, "seconds": 20},
            {"name": "field_drift_follow", "follow": True, "warmup": 20.0, "seconds": 20},
            {"name": "strike_follow", "follow": True, "warmup": 70.0, "seconds": 25},
            {"name": "archon_fight_follow", "follow": True, "warmup": 178.0, "seconds": 30}]
    return mission_plan([], runs)


def main():
    what = sys.argv[1] if len(sys.argv) > 1 else "stills"
    plans = {"stills": ("comets_r1.json", stills), "perf": ("comets_r1_perf.json", perf)}
    if what not in plans:
        raise SystemExit("unknown plan " + what)
    name, fn = plans[what]
    path = os.path.join(OUT, name)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(fn(), f, indent=1)
    print("wrote", path)


if __name__ == "__main__":
    main()
