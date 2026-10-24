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
    Rz = [[cr, -sr, 0], [sr, cr, 0], [0, 0, 1]]        # Unity applies the textbook rotation matrices to its (left-handed) axes:
    Rx = [[1, 0, 0], [0, cp, -sp], [0, sp, cp]]       # +pitch points the nose down, +yaw turns right; verified against the log line "SHOWCASE sun in the ship's frame"
    Ry = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]
    def mm(A, B):
        return [[sum(A[i][k] * B[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mm(Ry, mm(Rx, Rz))


def apply(R, v):
    return [sum(R[i][k] * v[k] for k in range(3)) for i in range(3)]


def world(p, orient):
    return [round(x, 3) for x in apply(rot(*orient), p)]


def look_rot(pos, target, up):
    """Unity Euler (pitch, yaw, roll) for a camera at ``pos`` looking at ``target`` with its up as close to ``up`` as it can be."""
    f = [target[i] - pos[i] for i in range(3)]
    n = math.sqrt(sum(x * x for x in f))
    f = [x / n for x in f]
    r = [up[1] * f[2] - up[2] * f[1], up[2] * f[0] - up[0] * f[2], up[0] * f[1] - up[1] * f[0]]      # up x forward (Unity is left-handed)
    rn = math.sqrt(sum(x * x for x in r))
    r = [x / rn for x in r]
    u = [f[1] * r[2] - f[2] * r[1], f[2] * r[0] - f[0] * r[2], f[0] * r[1] - f[1] * r[0]]
    pitch = -math.degrees(math.asin(max(-1, min(1, f[1]))))
    yaw = math.degrees(math.atan2(f[0], f[2]))
    roll = math.degrees(math.atan2(r[1], u[1]))
    return [round(pitch, 3), round(yaw, 3), round(roll, 3)]


def shot(name, cam, look, fov, orient):
    """A still in the ship's frame: world position, and a rotation that keeps the ship's own up upward in the picture."""
    R = rot(*orient)
    up = apply(R, (0, 1, 0))
    p, t = world(cam, orient), world(look, orient)
    return {"name": name, "pos": p, "rot": look_rot(p, t, up), "fov": fov}


def hit(pos, normal, radius, severity, orient):
    """A hull hit given in the ship's frame, as the scene options want it (world position and normal)."""
    return {"pos": world(pos, orient), "normal": world(normal, orient), "radius": radius, "severity": severity}


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


def fwd_of(rot_euler):
    p, y = math.radians(rot_euler[0]), math.radians(rot_euler[1])
    return (math.cos(p) * math.sin(y), -math.sin(p), math.cos(p) * math.cos(y))


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


SUN_IN_SHIP = (0.50, 0.25, 0.83)      # round 2: from ahead, low and to starboard, so it rakes the starboard flank and the decks and leaves the port side dark


def corvette(sun=SUN_IN_SHIP, sky=None):
    # The Sun in the ship's frame (x starboard, y up, z bow); the ship is turned to put it there (the Sun is fixed in the sky).
    orient, err = solve_orientation(sun)
    fighter = {"pos_ship": (46.0, 12.0, 122.0), "rot": [0.0, 0.0, 0.0]}
    shots = [
        ("close_hull", (44.0, 6.0, -78.0), (14.0, 6.0, 40.0), 50),
        ("medium_three_quarter", (215.0, 78.0, -310.0), (0.0, 8.0, -20.0), 40),
        ("wide_sun_rake", (470.0, 120.0, -250.0), (0.0, 6.0, 5.0), 32),
        ("shadow_side", (-150.0, 24.0, -300.0), (-6.0, 4.0, -85.0), 36),
        ("scale_fighter", (135.0, 28.0, 30.0), (18.0, 8.0, 125.0), 44),
        ("raking_close", (47.0, 33.0, -30.0), (12.0, 22.0, 14.0), 52),
    ]
    perf_cam = ((52.0, 10.0, -130.0), (4.0, 6.0, 60.0), 62)
    out = [shot(name, cam, look, fov, orient) for name, cam, look, fov in shots if name != "scale_fighter"]
    scale = [shot(name, cam, look, fov, orient) for name, cam, look, fov in shots if name == "scale_fighter"]
    # the fighter flies the ship's heading, wings level, in the ship's own frame
    frot = fighter["rot"]
    plan = {
        "scene": "corvette",
        "sceneOptions": {"ship": "earth_corvette", "pitch": orient[0], "yaw": orient[1], "roll": orient[2],
                         "fighter": {"pos": world(fighter["pos_ship"], orient), "rot": [orient[0], orient[1], orient[2]]}},
        "defaults": {"width": 1920, "height": 1080, "warmup": 1.5, "perfWarmup": 2.0},
        "stills": out,
        "perf": [dict(shot("corvette_fills_frame", perf_cam[0], perf_cam[1], perf_cam[2], orient), seconds=12)],
    }
    # the placeholder fighter appears only in the scale shot (its own plan), so the other frames hold nothing but the ship
    scale_plan = json.loads(json.dumps(plan))
    scale_plan["stills"], scale_plan["perf"] = scale, []
    plan["sceneOptions"].pop("fighter")
    if sky:
        plan["sceneOptions"]["sky"] = sky
        scale_plan["sceneOptions"]["sky"] = sky
    dmg = json.loads(json.dumps(plan))
    dmg["sceneOptions"]["damage"] = {
        "destroy": ["SUB_flak_S02", "SUB_flak_S04", "SUB_aaf_D04", "SUB_aaf_F03", "SUB_launcher_S01", "SUB_engine_02", "SUB_sensors_boom", "SUB_comms_main"],
        "health": {"SUB_flak_S01": 0.0, "SUB_aaf_D03": 0.0},
        "debrisAge": 1.3,
        "hull": 0.75,
        "hits": [hit((19.4, 6.0, -112.0), (1, 0, 0), 4.0, 0.9, orient), hit((19.3, 9.0, -127.0), (1, 0, 0), 3.0, 0.7, orient),
                 hit((19.2, 0.0, -140.0), (1, 0, 0), 5.0, 0.95, orient), hit((10.0, 13.4, -118.0), (0, 1, 0), 3.5, 0.8, orient)],
    }
    dmg["stills"] = [
        shot("damage_three_quarter", (170.0, 80.0, 230.0), (0.0, 8.0, -20.0), 36, orient),
        shot("damage_aft", (120.0, 38.0, -330.0), (15.0, 6.0, -145.0), 40, orient),
        shot("damage_close", (52.0, 20.0, -40.0), (20.0, 10.0, -125.0), 50, orient),
        shot("damage_flank", (62.0, 8.0, -78.0), (19.0, 4.0, -126.0), 46, orient),
    ]
    dmg["perf"] = [dict(shot("corvette_damaged_fills_frame", perf_cam[0], perf_cam[1], perf_cam[2], orient), seconds=12)]
    return plan, dmg, scale_plan, fighter, shots, orient, err


def arg(name, default=None):
    if name in sys.argv:
        return sys.argv[sys.argv.index(name) + 1]
    return default


def main():
    which = sys.argv[1] if len(sys.argv) > 1 else "corvette"
    sun = tuple(float(x) for x in arg("--sun").split(",")) if arg("--sun") else SUN_IN_SHIP
    sky = json.loads(arg("--sky")) if arg("--sky") else None
    plan, dmg, scale_plan, fighter, shots, orient, err = corvette(sun, sky)
    only = arg("--only")
    if only:
        names = only.split(",")
        plan["stills"] = [s_ for s_ in plan["stills"] if s_["name"] in names]
        plan["perf"] = []
        dmg["stills"] = [s_ for s_ in dmg["stills"] if s_["name"] in names]
        dmg["perf"] = []
        scale_plan["stills"] = [s_ for s_ in scale_plan["stills"] if s_["name"] in names]
    d = os.path.join(ROOT, "gauntlet", "shots")
    for name, p in ((which, plan), (which + "_damage", dmg), (which + "_scale", scale_plan)):
        with open(os.path.join(d, name + ".json"), "w") as f:
            json.dump(p, f, indent=1)
        print("wrote", os.path.join(d, name + ".json"))
    s = sun_in_ship(orient)
    print(f"ship orientation pitch/yaw/roll {orient}, solver error {err:.1f} deg; Sun in the ship's frame (x starboard, y up, z bow): ({s[0]:.2f}, {s[1]:.2f}, {s[2]:.2f})")
    if "--check" in sys.argv:
        fw = world(fighter["pos_ship"], orient)
        for st in scale_plan["stills"]:
            look = [st["pos"][i] + 100 * v for i, v in enumerate(fwd_of(st["rot"]))]
            pr = project(fw, st["pos"], look, st["fov"])
            vis = pr is not None and abs(pr[0]) < 1.05 and abs(pr[1]) < 1.05
            print(f"  {st['name']:<22} fighter at {pr}  {'IN FRAME' if vis else 'out'}")


if __name__ == "__main__":
    main()
