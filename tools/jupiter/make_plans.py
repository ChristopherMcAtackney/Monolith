"""Write the capture plans for the Jupiter backdrop (gauntlet/shots/jupiter_r1_*.json).

Run:  python -I tools/jupiter/make_plans.py

The planet-alone plans use the sky test scene ("scene": "sky": a camera, a grey hull at the origin, the sky) with the backdrop given in
"sceneOptions" (the same fields as a mission's backdrop block, by SkyOptions.Backdrop's names). The shots aim at places on the planet, worked out here
from the same frame the shader uses (the pole from Jupiter's real right ascension and declination, the central meridian, the Sun), so a number in a
plan is a direction in the world and can be rechecked.
"""
import json
import math
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SHOTS = os.path.join(ROOT, "gauntlet", "shots")

# The backdrop the Jupiter Skirmish mission uses (Resources/Missions/jupiter.mission.json).
BACKDROP = {"Body": "jupiter", "DistanceKm": 200000, "Azimuth": 24, "Elevation": -9, "HasSun": True, "SunAzimuth": -100, "SunElevation": 6, "SunDistanceAu": 5.2, "GrsLongitude": -22, "Planetshine": 3, "AmbientScale": 0.3,
            "Moons": [{"Body": "io", "OrbitDeg": 300}, {"Body": "europa", "OrbitDeg": 205}]}
RADIUS_KM = 71492.0
POLE_RA, POLE_DEC = 268.0566, 64.4953
OBLIQUITY = 23.4392911


def azel(az, el):
    a, e = math.radians(az), math.radians(el)
    return (math.sin(a) * math.cos(e), math.sin(e), math.cos(a) * math.cos(e))


def to_azel(v):
    n = math.sqrt(sum(c * c for c in v))
    v = [c / n for c in v]
    return math.degrees(math.atan2(v[0], v[2])), math.degrees(math.asin(max(-1, min(1, v[1]))))


def dot(a, b): return sum(x * y for x, y in zip(a, b))
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def add(a, b, k=1.0): return tuple(x + k * y for x, y in zip(a, b))
def scale(a, k): return tuple(x * k for x in a)
def norm(a):
    n = math.sqrt(dot(a, a))
    return tuple(x / n for x in a)


def equatorial_to_world(ra_deg, dec_deg):
    ra, dec = math.radians(ra_deg), math.radians(dec_deg)
    x, y, z = math.cos(dec) * math.cos(ra), math.cos(dec) * math.sin(ra), math.sin(dec)
    c, s = math.cos(math.radians(OBLIQUITY)), math.sin(math.radians(OBLIQUITY))
    return norm((-(y * c + z * s), -y * s + z * c, x))


def frame(b):
    J = azel(b["Azimuth"], b["Elevation"])
    S = azel(b["SunAzimuth"], b["SunElevation"])
    P = equatorial_to_world(POLE_RA, POLE_DEC)
    D = b["DistanceKm"] / RADIUS_KM
    fc = scale(J, -1)
    E1 = norm(add(fc, P, -dot(fc, P)))
    E2 = norm(cross(E1, P))
    return J, S, P, D, E1, E2


def direction_to_point(J, D, n):
    """The direction from the viewer to the point n (a unit vector from the centre) on the sphere."""
    return norm(add(scale(J, D), n))


MOONS = {"io": (421700.0, 1.769), "europa": (671100.0, 3.551)}      # orbit radius km, period days


def moon_direction(b, body, orbit_deg, seconds=0.0):
    """Where a moon lies in the sky (unit vector from the viewer) `seconds` into the mission, by the same orbit rule as BackdropState (OrbitDeg from the sunward point, 90 toward the
    planet's east, going round MoonSpeed times faster than real)."""
    J, S, P, D, E1, E2 = frame(b)
    orbit_km, days = MOONS[body]
    a = orbit_km / RADIUS_KM
    sun_eq = norm(add(S, P, -dot(S, P)))
    east = norm(cross(sun_eq, P))
    t = math.radians(orbit_deg + 360.0 / (days * 86400.0) * b.get("MoonSpeed", 100.0) * seconds)
    c = add(scale(sun_eq, a * math.cos(t)), east, a * math.sin(t))
    return norm(add(c, J, D))


def rot_for(az, el):
    return [round(-el, 3), round(az, 3), 0]


def still(name, aim_dir, fov, pos=(-20000, 0, 0), **extra):
    az, el = to_azel(aim_dir)
    d = {"name": name, "pos": list(pos), "rot": rot_for(az, el), "fov": fov}
    d.update(extra)
    return d


def main():
    b = BACKDROP
    J, S, P, D, E1, E2 = frame(b)
    alpha = math.degrees(math.asin(1 / D))
    print(f"disc: {2 * alpha:.1f} degrees across, viewer {D:.3f} radii from the centre; Sun {math.degrees(math.acos(dot(J, S))):.1f} degrees from the planet; phase angle {180 - math.degrees(math.acos(dot(J, S))):.1f}")

    stills = []
    stills.append(still("jupiter_full", J, 60, warmup=1.0))
    stills.append(still("jupiter_full_wide", J, 85, warmup=1.0))

    # The two places where the limb meets the terminator, and the lit limb
    t_pt = norm(cross(J, S))
    tips = [t_pt, scale(t_pt, -1)]
    tips.sort(key=lambda n: -direction_to_point(J, D, n)[1])
    stills.append(still("limb_terminator_north", direction_to_point(J, D, tips[0]), 16))
    stills.append(still("limb_terminator_south", direction_to_point(J, D, tips[1]), 16))
    lit_limb = norm(add(S, J, -dot(S, J)))
    stills.append(still("limb_lit", direction_to_point(J, D, lit_limb), 14))
    # the terminator where it crosses the middle of the disc
    sub = scale(J, -1)
    mid_t = norm(add(sub, S, -dot(sub, S)))
    stills.append(still("terminator_middle", direction_to_point(J, D, mid_t), 22))
    stills.append(still("terminator_wide", direction_to_point(J, D, mid_t), 45))

    # the Great Red Spot
    lat = math.radians(-20.1)
    lon = math.radians(b["GrsLongitude"])
    n = add(add(scale(E1, math.cos(lat) * math.cos(lon)), E2, math.cos(lat) * math.sin(lon)), P, math.sin(lat))
    stills.append(still("grs", direction_to_point(J, D, n), 18))
    stills.append(still("grs_close", direction_to_point(J, D, n), 7))

    # the moons (they go round, so these are the first shots of the plan and each is aimed where its moon is at that second)
    moon_stills = []
    t_now = 0.0
    for name, body, orbit, fov, beside in (("io_close", "io", 300, 2.2, False), ("europa_close", "europa", 205, 1.6, False),
                                           ("io_beside_jupiter", "io", 300, 38, True), ("europa_beside_jupiter", "europa", 205, 60, True)):
        t_now += 0.5
        d = moon_direction(b, body, orbit, t_now)
        moon_stills.append(still(name, norm(add(d, J, 1.0)) if beside else d, fov, warmup=0.5))
    stills = moon_stills + stills

    # a hull in front of the planet (planetshine): the camera 90 m from the hull, on the line from the hull away from the planet
    cam = scale(J, -90.0)
    stills.append({"name": "hull_before_jupiter", "pos": [round(c, 2) for c in cam], "lookAt": [0, 0, 0], "fov": 40, "warmup": 1.0})
    stills.append({"name": "hull_before_jupiter_wide", "pos": [round(c, 2) for c in cam], "lookAt": [0, 0, 0], "fov": 70, "warmup": 1.0})

    # the hull's planet side: the camera on the planet's side of the hull, so it sees the faces that face Jupiter (the Sun is 105 degrees from Jupiter: those faces are in its shadow,
    # so what lights them is the planet's own light and the sky's fill)
    cam2 = scale(J, 90.0)
    stills.append({"name": "hull_planetshine", "pos": [round(c, 2) for c in add(cam2, (0, 12, 0))], "lookAt": [0, 0, 0], "fov": 40, "warmup": 1.0})
    stills.append({"name": "hull_planetshine_low", "pos": [round(c, 2) for c in add(cam2, (14, -4, 0))], "lookAt": [0, 0, 0], "fov": 40, "warmup": 1.0})

    # Io's shadow crossing the clouds: Io swings sunward and its shadow is on the lit disc from about 4.4 to 4.8 minutes (a plan of its own, so the times are the planet's clock)
    shadow = []
    prev = 0.0
    for i, t in enumerate((235.0, 250.0, 265.0, 280.0, 295.0, 315.0)):
        shadow.append(still(f"io_shadow_t{int(t)}", J, 34, warmup=round(t - prev, 2)))
        prev = t
    with open(os.path.join(SHOTS, "jupiter_r1_planet_shadow.json"), "w") as f:
        json.dump({"scene": "sky", "sceneOptions": {"Backdrop": b}, "defaults": {"width": 1920, "height": 1080, "warmup": 0.0, "perfWarmup": 2.0}, "stills": shadow}, f, indent=1)

    # a turn: the planet slowly turning (game time between the stills)
    for i, w in enumerate((0.5, 40.0, 80.0)):
        stills.append(still(f"drift_{i}", J, 40, warmup=w))

    plan = {"scene": "sky", "sceneOptions": {"Backdrop": b}, "defaults": {"width": 1920, "height": 1080, "warmup": 1.0, "perfWarmup": 2.0}, "stills": stills}
    with open(os.path.join(SHOTS, "jupiter_r1_planet.json"), "w") as f:
        json.dump(plan, f, indent=1)

    # the planet alone at other phases (the Sun elsewhere; the same planet, the same view): half lit with the terminator through the middle, and a thin crescent (the Sun almost behind the planet)
    for tag, az, el in (("half", -68, 22), ("crescent", 55, -4)):
        bb = dict(b, SunAzimuth=az, SunElevation=el)
        Jb, Sb, Pb, Db, E1b, E2b = frame(bb)
        phase = 180 - math.degrees(math.acos(dot(Jb, Sb)))
        print(f"{tag}: Sun {math.degrees(math.acos(dot(Jb, Sb))):.0f} degrees from the planet, phase angle {phase:.0f}, {(1 + math.cos(math.radians(phase))) * 50:.0f} percent lit")
        sub = scale(Jb, -1)
        mid_b = norm(add(sub, Sb, -dot(sub, Sb)))
        tb = norm(cross(Jb, Sb))
        tipb = max((tb, scale(tb, -1)), key=lambda n: direction_to_point(Jb, Db, n)[1])
        lit_b = norm(add(Sb, Jb, -dot(Sb, Jb)))
        st = [still(f"{tag}_full", Jb, 60, warmup=1.0),
              still(f"{tag}_terminator", direction_to_point(Jb, Db, mid_b), 24, warmup=1.0),
              still(f"{tag}_limb_north", direction_to_point(Jb, Db, tipb), 18, warmup=1.0),
              still(f"{tag}_limb_lit", direction_to_point(Jb, Db, lit_b), 18, warmup=1.0)]
        pp = {"scene": "sky", "sceneOptions": {"Backdrop": bb}, "defaults": plan["defaults"], "stills": st}
        with open(os.path.join(SHOTS, f"jupiter_r1_planet_{tag}.json"), "w") as f:
            json.dump(pp, f, indent=1)

    # the same two hull shots with the planet's light turned off, to show what it does to the hull
    off = dict(b, Planetshine=0.0)
    p2 = {"scene": "sky", "sceneOptions": {"Backdrop": off}, "defaults": plan["defaults"], "stills": [x for x in stills if x["name"].startswith("hull_planetshine")]}
    for x in p2["stills"]:
        x["name"] += "_off"
    with open(os.path.join(SHOTS, "jupiter_r1_planetshine_off.json"), "w") as f:
        json.dump(p2, f, indent=1)

    # how far: the same view at several distances, in the cockpit's own 55 degree field (the choice of 200,000 km is justified by these; each is its own plan because the options are per plan)
    for km in (110000, 150000, 200000, 250000, 400000):
        bd = dict(b, DistanceKm=km)
        half = math.degrees(math.asin(RADIUS_KM / km))
        with open(os.path.join(SHOTS, f"jupiter_r1_dist_{km}.json"), "w") as f:
            json.dump({"scene": "sky", "sceneOptions": {"Backdrop": bd}, "defaults": plan["defaults"],
                       "stills": [still(f"dist_{km}", J, 55, warmup=1.0)]}, f, indent=1)
        print(f"{km} km: the disc is {2 * half:.1f} degrees across, {2 * half / 55 * 100:.0f} percent of the cockpit's frame height")

    # cost of the planet: the same view and hull with and without it (GPU ms from the capture harness)
    cam_view = {"name": "full", "seconds": 10, "pos": [-20000, 0, 0], "rot": rot_for(*to_azel(J)), "fov": 60}
    for tag, bd in (("planet", b), ("noplanet", dict(b, Body=""))):
        p = {"scene": "sky", "sceneOptions": {"Backdrop": bd}, "defaults": {"width": 1920, "height": 1080, "warmup": 1.0, "perfWarmup": 2.0}, "perf": [dict(cam_view, name="jupiter_" + tag)]}
        with open(os.path.join(SHOTS, f"jupiter_r1_perf_{tag}.json"), "w") as f:
            json.dump(p, f, indent=1)
    print("wrote plans in", SHOTS)


if __name__ == "__main__":
    main()
