"""Writes a capture plan for a ship showcase scene from shots given in the SHIP's own frame (x starboard, y up, z bow), so framing does not
depend on which way the ship is turned to catch the Sun.

    python tools/ship/make_ship_plan.py corvette            -> gauntlet/shots/corvette.json and corvette_damage.json
    python tools/ship/make_ship_plan.py corvette --check    -> also prints whether the placeholder fighter is in each frame

The Sun is fixed in the sky (Sky system default: ecliptic longitude 152, latitude 5): the ship's yaw is what puts it astern, abeam or ahead.
"""
import json
import math
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SUN = (-0.4677, 0.0872, -0.8796)          # unit vector from the ship toward the Sun (logged by SkyRig: "SKY on: sun ...")


def rot(pitch, yaw, roll):
    """Unity's Quaternion.Euler(pitch, yaw, roll) as a 3x3 (rows): world = Ry(yaw) * Rx(pitch) * Rz(roll) * v, left-handed."""
    cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
    cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    cr, sr = math.cos(math.radians(roll)), math.sin(math.radians(roll))
    Rz = [[cr, -sr, 0], [sr, cr, 0], [0, 0, 1]]
    Rx = [[1, 0, 0], [0, cp, sp], [0, -sp, cp]]
    Ry = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]
    def mm(A, B):
        return [[sum(A[i][k] * B[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mm(Ry, mm(Rx, Rz))


def apply(R, v):
    return [sum(R[i][k] * v[k] for k in range(3)) for i in range(3)]


def world(p, orient):
    return [round(x, 3) for x in apply(rot(*orient), p)]


def sun_in_ship(orient):
    R = rot(*orient)
    return [sum(R[k][i] * SUN[k] for k in range(3)) for i in range(3)]       # R^T * SUN


def solve_orientation(target, prefer_level=True):
    """Euler (pitch, yaw, roll) that put the Sun at ``target`` (a unit vector in the ship's frame: x starboard, y up, z bow), keeping the ship
    near level when it can."""
    t = [x / math.sqrt(sum(y * y for y in target)) for x in target]
    best = None
    for yaw in range(0, 360, 2):
        for pitch in range(-40, 41, 4):
            for roll in range(-100, 101, 4):
                s = sun_in_ship((pitch, yaw, roll))
                err = math.degrees(math.acos(max(-1, min(1, sum(s[i] * t[i] for i in range(3))))))
                cost = err + 0.02 * abs(pitch) + 0.01 * abs(roll)
                if best is None or cost < best[0]:
                    best = (cost, (pitch, yaw, roll), err)
    # refine to one degree
    p0, y0, r0 = best[1]
    for yaw in range(y0 - 2, y0 + 3):
        for pitch in range(p0 - 4, p0 + 5):
            for roll in range(r0 - 4, r0 + 5):
                s = sun_in_ship((pitch, yaw, roll))
                err = math.degrees(math.acos(max(-1, min(1, sum(s[i] * t[i] for i in range(3))))))
                cost = err + 0.02 * abs(pitch) + 0.01 * abs(roll)
                if cost < best[0]:
                    best = (cost, (pitch, yaw, roll), err)
    return best[1], best[2]


def project(p_world, cam, look, fov, aspect=16 / 9):
    f = [look[i] - cam[i] for i in range(3)]
    n = math.sqrt(sum(x * x for x in f))
    f = [x / n for x in f]
    up = (0, 1, 0)
    r = (f[1] * up[2] - f[2] * up[1], f[2] * up[0] - f[0] * up[2], f[0] * up[1] - f[1] * up[0])
    rn = math.sqrt(sum(x * x for x in r))
    r = [x / rn for x in r]
    u = (r[1] * f[2] - r[2] * f[1], r[2] * f[0] - r[0] * f[2], r[0] * f[1] - r[1] * f[0])
    d = [p_world[i] - cam[i] for i in range(3)]
    z = sum(d[i] * f[i] for i in range(3))
    if z <= 0:
        return None
    x = sum(d[i] * r[i] for i in range(3)) / z / (math.tan(math.radians(fov / 2)) * aspect)
    y = sum(d[i] * u[i] for i in range(3)) / z / math.tan(math.radians(fov / 2))
    return round(x, 2), round(y, 2)


def corvette():
    # The Sun in the ship's frame (x starboard, y up, z bow): from ahead, high and a little to starboard, so it rakes the starboard flank and the top deck.
    orient, err = solve_orientation((0.30, 0.50, 0.81))
    fighter = {"pos_ship": (52.0, 26.0, 250.0), "rot": [0.0, 0.0, 0.0]}
    shots = [
        ("close_hull", (44.0, 6.0, -78.0), (14.0, 6.0, 40.0), 50),
        ("medium_three_quarter", (150.0, 70.0, 250.0), (0.0, 10.0, -10.0), 34),
        ("wide_sun_rake", (520.0, 55.0, 40.0), (0.0, 8.0, 0.0), 30),
        ("shadow_side", (-170.0, 38.0, -250.0), (-5.0, 8.0, -30.0), 38),
        ("scale_fighter", (112.0, 24.0, 150.0), (22.0, 14.0, 228.0), 40),
    ]
    perf_cam = ((52.0, 10.0, -130.0), (4.0, 6.0, 60.0), 62)
    out = []
    for name, cam, look, fov in shots:
        out.append({"name": name, "pos": world(cam, orient), "lookAt": world(look, orient), "fov": fov})
    # the fighter flies the ship's heading, wings level, in the ship's own frame
    frot = fighter["rot"]
    plan = {
        "scene": "corvette",
        "sceneOptions": {"ship": "earth_corvette", "pitch": orient[0], "yaw": orient[1], "roll": orient[2],
                         "fighter": {"pos": world(fighter["pos_ship"], orient), "rot": [orient[0], orient[1], orient[2]]}},
        "defaults": {"width": 1920, "height": 1080, "warmup": 1.5, "perfWarmup": 2.0},
        "stills": out,
        "perf": [{"name": "corvette_fills_frame", "seconds": 12, "pos": world(perf_cam[0], orient), "lookAt": world(perf_cam[1], orient), "fov": perf_cam[2]}],
    }
    dmg = json.loads(json.dumps(plan))
    dmg["sceneOptions"].pop("fighter")
    dmg["sceneOptions"]["damage"] = {
        "destroy": ["SUB_flak_S02", "SUB_flak_S04", "SUB_aaf_D04", "SUB_aaf_F03", "SUB_launcher_S01", "SUB_engine_02", "SUB_sensors_boom", "SUB_comms_main"],
        "health": {"SUB_flak_S01": 0.0, "SUB_aaf_D03": 0.0},
        "debrisAge": 1.3,
        "hull": 0.75,
    }
    dmg["stills"] = [
        {"name": "damage_three_quarter", "pos": world((170.0, 80.0, 230.0), orient), "lookAt": world((0.0, 8.0, -20.0), orient), "fov": 36},
        {"name": "damage_aft", "pos": world((120.0, 38.0, -330.0), orient), "lookAt": world((15.0, 6.0, -145.0), orient), "fov": 40},
        {"name": "damage_close", "pos": world((52.0, 20.0, -40.0), orient), "lookAt": world((20.0, 10.0, -125.0), orient), "fov": 50},
    ]
    dmg["perf"] = []
    return plan, dmg, fighter, shots, orient, err


def main():
    which = sys.argv[1] if len(sys.argv) > 1 else "corvette"
    plan, dmg, fighter, shots, orient, err = corvette()
    d = os.path.join(ROOT, "gauntlet", "shots")
    for name, p in ((which, plan), (which + "_damage", dmg)):
        with open(os.path.join(d, name + ".json"), "w") as f:
            json.dump(p, f, indent=1)
        print("wrote", os.path.join(d, name + ".json"))
    s = sun_in_ship(orient)
    print(f"ship orientation pitch/yaw/roll {orient}, solver error {err:.1f} deg; Sun in the ship's frame (x starboard, y up, z bow): ({s[0]:.2f}, {s[1]:.2f}, {s[2]:.2f})")
    if "--check" in sys.argv:
        fw = world(fighter["pos_ship"], orient)
        for st in plan["stills"]:
            pr = project(fw, st["pos"], st["lookAt"], st["fov"])
            vis = pr is not None and abs(pr[0]) < 1.05 and abs(pr[1]) < 1.05
            print(f"  {st['name']:<22} fighter at {pr}  {'IN FRAME' if vis else 'out'}")


main()
