"""Capture plans for the Oort models (the comet outpost and the destroyer), in the real Oort mission or the showcase scene.

    python tools/ship/make_oortart_plan.py outpost            -> gauntlet/shots/oortart_outpost.json   (stills of the outpost in the mission, from the quiet drift to the guns)
    python tools/ship/make_oortart_plan.py outpost_details    -> gauntlet/shots/oortart_outpost_details.json
    python tools/ship/make_oortart_plan.py archon             -> gauntlet/shots/oortart_archon.json     (the destroyer in the compressed test mission)

The mission places the outpost at (700, 300, 7200) turned 185 degrees about up (its model +z, the hangar face, toward the player at the origin); ``outpost_pt`` maps a point in the
model's Unity axes (x right, y up, z forward) to the world, so a shot can be aimed at the Eye or the hangar mouth. Shots are HUD off, 1920x1080, from a free camera.
"""
import json
import math
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "gauntlet", "shots")
CENTRE = (700.0, 300.0, 7200.0)
YAW = math.radians(185.0)


def outpost_pt(m):
    """A point of the outpost's model (Unity axes, from its build axes (x, y, z) -> (x, z, y)) in the world."""
    mx, my, mz = m
    c, s = math.cos(YAW), math.sin(YAW)
    return [round(CENTRE[0] + mx * c + mz * s, 1), round(CENTRE[1] + my, 1), round(CENTRE[2] - mx * s + mz * c, 1)]


def build_pt(x, y, z):
    """The same from the build script's axes (x right, y forward, z up)."""
    return outpost_pt((x, z, y))


def still(name, pos, look, fov=45, warm=1.0, **kw):
    d = {"name": name, "pos": [round(v, 1) for v in pos], "lookAt": [round(v, 1) for v in look], "fov": fov, "warmup": warm}
    d.update(kw)
    return d


def mission_plan(stills, mission="oort"):
    return {"scene": "mission", "sceneOptions": {"mission": mission, "pilot": "ai", "invulnerable": True, "hud": False, "dust": False},
            "defaults": {"width": 1920, "height": 1080, "warmup": 1.0}, "stills": stills}


def outpost():
    eye = build_pt(-100, 42, 432)
    hang = build_pt(-235, 315, 12)
    tower = build_pt(82, -20, 480)
    st = build_pt(-120, -20, 380)
    s = [
        still("money_7km", (0, 40, -120), build_pt(-60, 0, 330), 34, 3.0),
        still("money_7km_wide", (0, 40, -120), build_pt(-60, 0, 330), 62, 1.0),
        still("drift_4km", (300, 120, 3000), build_pt(-60, 0, 330), 40, 1.0),
        still("approach_2km", (800, 420, 5100), build_pt(-90, 0, 340), 40, 1.0),
        still("approach_1km", (900, 600, 6100), build_pt(-110, 0, 360), 42, 1.0),
        still("eye_close", build_pt(-60, 700, 520), eye, 40, 1.0),
        still("hangar_close", build_pt(-200, 640, 60), hang, 42, 1.0),
        still("tower_close", build_pt(260, 380, 520), tower, 45, 1.0),
        still("station_three_quarter", build_pt(520, 760, 600), st, 42, 1.0),
        still("from_above", build_pt(-120, 160, 1300), build_pt(-120, 0, 300), 40, 1.0),
        still("flank_west", build_pt(-900, 200, 460), build_pt(-250, 0, 330), 42, 1.0),
        still("flank_east", build_pt(900, 200, 460), build_pt(0, 0, 320), 42, 1.0),
        still("jets", build_pt(300, 900, 520), build_pt(100, 0, 250), 52, 1.0),
        still("rock_belly", build_pt(-300, 560, -700), build_pt(-150, 100, -100), 50, 1.0),
        still("lod1_25km", (0, 40, -17800), build_pt(-60, 0, 330), 40, 1.0),
        still("lod1_25km_zoom", (0, 40, -17800), build_pt(-60, 0, 330), 6, 1.0),
    ]
    return mission_plan(s)


ARCHON = (0.0, 0.0, 2400.0)          # the lab mission (gauntlet/missions/archon_test.mission.json): the Archon's centre, its bow along +x (port is +z, starboard -z)


def archon_pt(m):
    """A point of the Archon's model (Unity axes: x right, y up, z forward) in the lab: its yaw is 90 degrees, so the bow points along +x."""
    mx, my, mz = m
    return [round(ARCHON[0] + mz, 1), round(ARCHON[1] + my, 1), round(ARCHON[2] - mx, 1)]


def archon_build(x, y, z):
    return archon_pt((x, z, y))


def archon():
    mission = os.path.join(ROOT, "gauntlet", "missions", "archon_test.mission.json").replace("\\", "/")
    muz = archon_pt((8.0, 126.0, 344.0))
    s = [
        still("three_quarter_raking", archon_pt((1250, 380, 760)), archon_pt((0, 10, -20)), 42, 2.0),
        still("three_quarter_aft", archon_pt((1100, 420, -900)), archon_pt((0, 10, -60)), 44, 1.0),
        still("scale_line", [-600.0, 650.0, 600.0], [150.0, 100.0, 2900.0], 50, 1.0),
        still("scale_wide", [-1100.0, 900.0, 300.0], [150.0, 100.0, 2900.0], 58, 1.0),
        still("beam_close", archon_pt((420, 230, 640)), muz, 40, 1.0),
        still("lance_dorsal", archon_pt((260, 260, 420)), archon_pt((0, 60, 250)), 46, 1.0),
        still("bridge_tower", archon_pt((380, 260, -80)), archon_pt((0, 110, -216)), 44, 1.0),
        still("hangar_below", archon_pt((-150, -420, 20)), archon_pt((0, -60, 152)), 50, 1.0),
        still("hangar_side", archon_pt((420, -60, 120)), archon_pt((0, -50, 152)), 44, 1.0),
        still("engines", archon_pt((210, 40, -950)), archon_pt((0, 5, -500)), 46, 1.0),
        still("prow", archon_pt((300, 90, 760)), archon_pt((0, 5, 470)), 44, 1.0),
        still("from_above", archon_pt((0, 1700, -30)), archon_pt((0, 0, 20)), 38, 1.0),
        still("ventral_cannon", archon_pt((260, -300, -440)), archon_pt((-22, -90, -150)), 46, 1.0),
        still("beam_charge_a", archon_pt((330, 210, 880)), muz, 36, 10.0),
        still("beam_charge_b", archon_pt((330, 210, 880)), muz, 36, 2.0),
        still("beam_charge_c", archon_pt((330, 210, 880)), muz, 36, 2.0),
        still("beam_firing", archon_pt((900, 430, 640)), archon_pt((0, 150, 300)), 46, 2.0),
    ]
    return mission_plan(s, mission)


def archon_scale():
    """The three capitals hung still (gauntlet/missions/archon_scale_test.mission.json: every beam held): the Archon with the Cantor and the Small Mercy, from the sunlit side."""
    mission = os.path.join(ROOT, "gauntlet", "missions", "archon_scale_test.mission.json").replace("\\", "/")
    s = [
        still("scale_three_quarter_raking", archon_pt((1250, 380, 760)), archon_pt((-80, 60, -20)), 42, 2.0),
        still("scale_line_up", [-1500.0, 520.0, 2150.0], [20.0, 120.0, 2600.0], 55, 1.0),
        still("scale_line_up_wide", [-2300.0, 700.0, 1900.0], [60.0, 150.0, 2650.0], 62, 1.0),
        still("scale_side_by_side", [-1200.0, 160.0, 2460.0], [-60.0, 90.0, 2200.0], 50, 1.0),
        still("scale_from_above", [300.0, 2100.0, 2400.0], [100.0, 100.0, 2500.0], 52, 1.0),
        still("scale_bow_on", archon_pt((120, 60, 1900)), archon_pt((0, 40, 100)), 46, 1.0),
        still("scale_corvette_beside", [-480.0, 120.0, 1900.0], [-60.0, 60.0, 2150.0], 52, 1.0),
    ]
    return mission_plan(s, mission)


def archon_charge():
    """The destroyer's beam charging, a frame every 0.5 s from 0.2 s into the lab (gauntlet/missions/archon_test.mission.json: its beams quick): the glow gathers at the lens with sparks pulled in."""
    mission = os.path.join(ROOT, "gauntlet", "missions", "archon_test.mission.json").replace("\\", "/")
    muz = archon_pt((8.0, 126.0, 344.0))
    s = [still(f"charge_{i:02d}", archon_pt((330, 210, 880)), muz, 34, 0.2 if i == 0 else 0.5) for i in range(10)]
    s += [still(f"charge_wide_{i:02d}", archon_pt((1250, 380, 760)), archon_pt((-80, 60, -20)), 42, 4.0 if i == 0 else 0.5) for i in range(6)]
    return mission_plan(s, mission)


def death_plan(mission_file, pos, look, fov, n=30, t0=3.0, step=0.5):
    mission = os.path.join(ROOT, "gauntlet", "missions", mission_file).replace("\\", "/")
    stills = [{"name": f"death_{i:02d}", "pos": pos, "lookAt": look, "fov": fov, "warmup": (t0 if i == 0 else step)} for i in range(n)]
    return mission_plan(stills, mission)


def perf_plan():
    """The Oort mission's busiest moments, measured with -mnPerfSync: the outpost's fight seen from the hangar face (the strike at 1:42, the waves out of the hangar, the guns; warmups
    add up, so the second run starts about 5:30), and the player's own view (the AI flying it) in the Archon's fight, when both big ships, the Mercy, the bombers and their escort are up."""
    cam = build_pt(-120, 560, 330)
    look = build_pt(-120, 0, 330)
    return {"scene": "mission", "sceneOptions": {"mission": "oort", "pilot": "ai", "invulnerable": True, "hud": False, "dust": False},
            "defaults": {"width": 1920, "height": 1080, "warmup": 1.0, "perfWarmup": 3.0}, "stills": [],
            "perf": [{"name": "outpost_fight_from_the_hangar_face", "pos": cam, "lookAt": look, "fov": 62, "warmup": 126.0, "seconds": 25},
                     {"name": "archon_fight_follow", "follow": True, "warmup": 178.0, "seconds": 30}]}


def main():
    what = sys.argv[1] if len(sys.argv) > 1 else "outpost"
    plans = {"outpost": ("oortart_outpost.json", outpost), "archon": ("oortart_archon.json", archon), "archon_scale": ("oortart_archon_scale.json", archon_scale), "archon_charge": ("oortart_archon_charge.json", archon_charge), "perf": ("oortart_perf.json", perf_plan),
             "outpost_death": ("oortart_outpost_death.json", lambda: death_plan("outpost_death_test.mission.json", [900.0, 420.0, 1750.0], [0.0, 120.0, 3000.0], 62)),
             "archon_death": ("oortart_archon_death.json", lambda: death_plan("archon_death_test.mission.json", [900.0, 360.0, 1250.0], [0.0, 0.0, 2400.0], 62))}
    if what not in plans:
        raise SystemExit("unknown plan " + what)
    name, fn = plans[what]
    path = os.path.join(OUT, name)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(fn(), f, indent=1)
    print("wrote", path)


if __name__ == "__main__":
    main()
