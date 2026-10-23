"""Write the sky capture plans (gauntlet/shots/sky.json, sky_baseline.json) with camera directions worked out from real sky positions.

Run:  python -I tools/sky/make_sky_plan.py
The maths mirrors Game/Assets/Monolith/Scripts/Render/Sky/SkyMath.cs: equatorial (ICRS) -> ecliptic -> Unity world
(world = (-ecl.y, ecl.z, ecl.x)), and the Sun default place (ecliptic longitude 152, latitude 5) from SkyOptions.
Standard library only.
"""
import json
import math
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OBLIQUITY = math.radians(23.4392911)

SUN_ECL = (152.0, 5.0)      # SkyOptions.SunEclipticLongitude / Latitude


def norm(v):
    n = math.sqrt(sum(c * c for c in v))
    return tuple(c / n for c in v)


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def scale(v, s):
    return tuple(c * s for c in v)


def add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def eq_to_world(eq):
    x, y, z = eq
    c, s = math.cos(OBLIQUITY), math.sin(OBLIQUITY)
    return (-(y * c + z * s), -y * s + z * c, x)


def equatorial(ra_h, dec_d):
    ra, dec = math.radians(ra_h * 15.0), math.radians(dec_d)
    return (math.cos(dec) * math.cos(ra), math.cos(dec) * math.sin(ra), math.sin(dec))


# ICRS -> galactic rotation (Hipparcos documentation); the inverse is the transpose.
GAL = ((-0.0548755604, -0.8734370902, -0.4838350155),
       (0.4941094279, -0.4448296300, 0.7469822445),
       (-0.8676661490, -0.1980763734, 0.4559837762))


def galactic_to_world(l_deg, b_deg):
    l, b = math.radians(l_deg), math.radians(b_deg)
    g = (math.cos(b) * math.cos(l), math.cos(b) * math.sin(l), math.sin(b))
    eq = tuple(sum(GAL[r][c] * g[r] for r in range(3)) for c in range(3))
    return eq_to_world(eq)


def ecliptic_to_world(lon_d, lat_d):
    lon, lat = math.radians(lon_d), math.radians(lat_d)
    ecl = (math.cos(lat) * math.cos(lon), math.cos(lat) * math.sin(lon), math.sin(lat))
    return (-ecl[1], ecl[2], ecl[0])


def angle_deg(a, b):
    return math.degrees(math.acos(max(-1.0, min(1.0, dot(a, b)))))


def euler_for(f):
    """Unity Euler (pitch down positive, yaw) for a forward direction, no roll."""
    yaw = math.degrees(math.atan2(f[0], f[2]))
    pitch = -math.degrees(math.asin(max(-1.0, min(1.0, f[1]))))
    return [round(pitch, 4), round(yaw, 4), 0.0]


def rotate_yaw(f, deg):
    r = math.radians(deg)
    c, s = math.cos(r), math.sin(r)
    return (f[0] * c + f[2] * s, f[1], -f[0] * s + f[2] * c)


def r3(v):
    return [round(c, 3) for c in v]


SUN = norm(ecliptic_to_world(*SUN_ECL))
FAR = 30000.0   # sky shots: the camera sits this far out along its own view direction, so the test hull at the origin is behind it


def sky_shot(name, f, fov=40.0, **extra):
    f = norm(f)
    s = {"name": name, "pos": r3(scale(f, FAR)), "lookAt": r3(scale(f, 2 * FAR)), "fov": fov}
    s.update(extra)
    return s


def main():
    stills = []

    # The Milky Way band: the Galactic centre region, and the Cygnus rift.
    stills.append(sky_shot("milkyway_core", galactic_to_world(0, 2), fov=60))
    stills.append(sky_shot("milkyway_rift", galactic_to_world(60, 0), fov=60))
    # A quiet field well off the plane.
    stills.append(sky_shot("starfield_quiet", galactic_to_world(150, 62), fov=40))
    # Orion, to prove the catalogue positions and orientation (the belt: Mintaka, Alnilam, Alnitak; Betelgeuse upper left, Rigel lower right).
    stills.append(sky_shot("starfield_orion", eq_to_world(equatorial(5.6, -1.0)), fov=30))

    # A field with no Milky Way in it: the south galactic pole, far from the Sun.
    stills.append(sky_shot("starfield_nomw", galactic_to_world(0, -85), fov=40))

    # Toward the Sun: off-centre (a third in from the left), dead centre, just off the edge of the frame, and wide.
    stills.append(sky_shot("sun_toward", rotate_yaw(SUN, 10.0), fov=40))
    stills.append(sky_shot("sun_centered", SUN, fov=40))
    stills.append(sky_shot("sun_edge", rotate_yaw(SUN, 36.0), fov=40))
    stills.append(sky_shot("sun_wide", rotate_yaw(SUN, 18.0), fov=75))

    # A lit hull in front of stars. The view direction l is on (or off) the Milky Way band and 100 to 150 degrees from the Sun, so the Sun
    # sits behind the camera shoulder and the hull shows a lit side and a terminator. Among the candidates take the one that shows the
    # most sunlit hull surface (the hull is a 30 x 6 x 8 m box along world x, SkyTestWorld).
    faces = [((1, 0, 0), 48.0), ((-1, 0, 0), 48.0), ((0, 1, 0), 240.0), ((0, -1, 0), 240.0), ((0, 0, 1), 180.0), ((0, 0, -1), 180.0)]

    def lit_score(l):
        u = scale(l, -1.0)      # hull -> camera
        return sum(area * max(0.0, dot(n, SUN)) * max(0.0, dot(n, u)) for n, area in faces)

    def best_view(lons, lats):
        best = None
        for lon in lons:
            for lat in lats:
                l = norm(galactic_to_world(lon, lat))
                ang = angle_deg(l, SUN)
                if not 100.0 <= ang <= 150.0:
                    continue
                sc = lit_score(l)
                if best is None or sc > best[0]:
                    best = (sc, l, lon, lat, ang)
        return best

    dist = 88.0
    _, l, lon, lat, ang = best_view(list(range(300, 360)) + list(range(0, 70)), (-6, -3, 0, 3, 6))
    stills.append({"name": "hull_lit", "pos": r3(scale(l, -dist)), "lookAt": [0.0, 1.0, 0.0], "fov": 40})
    # The same, against a plain starfield well off the band.
    _, l2, lon2, lat2, ang2 = best_view(range(0, 360, 5), (-60, -50, -40, 40, 50, 60))
    stills.append({"name": "hull_lit_field", "pos": r3(scale(l2, -dist)), "lookAt": [0.0, 1.0, 0.0], "fov": 40})

    # The Sun behind the hull and a little to one side: the camera looks at the hull from the shadow side (about 155 degrees from the Sun),
    # so what it sees is lit by the cold fill alone, with the Sun glare and a thin rim at the edge. One against the band, one against a field.
    def shadow_view(lons, lats, target=155.0, tol=12.0):
        best = None
        for lon in lons:
            for lat in lats:
                l = norm(galactic_to_world(lon, lat))
                u = scale(l, -1.0)
                ang = angle_deg(u, SUN)
                if abs(ang - target) > tol:
                    continue
                # prefer a camera that sees the large unlit faces: score by unlit area facing the camera
                sc = sum(area * max(0.0, dot(n, u)) for n, area in faces if dot(n, SUN) < 0.0)
                if best is None or sc > best[0]:
                    best = (sc, l, lon, lat, ang)
        return best

    _, l3, lon3, lat3, ang3 = shadow_view(list(range(0, 360)), (-6, -3, 0, 3, 6), target=135.0, tol=40.0)
    stills.append({"name": "hull_shadow", "pos": r3(scale(l3, -dist)), "lookAt": [0.0, 1.0, 0.0], "fov": 40})
    _, l4, lon4, lat4, ang4 = shadow_view(range(0, 360, 5), (-60, -50, -40, 40, 50, 60))
    stills.append({"name": "hull_shadow_field", "pos": r3(scale(l4, -dist)), "lookAt": [0.0, 1.0, 0.0], "fov": 40})

    # The hull between the camera and the Sun: a rim-lit silhouette against the glare. The camera sits behind the hull and a little to the side.
    side = norm(cross(SUN, (0, 1, 0)))
    cam = add(scale(SUN, -95.0), scale(side, -12.8))
    stills.append({"name": "hull_backlit", "pos": r3(cam), "lookAt": r3(add(scale(SUN, 400.0), scale(side, -14.3))), "fov": 40})

    # A ship-sized silhouette crossing the Sun: the camera sits behind the hull on the Sun line and the hull covers part of it. a: the Sun just
    # peeking past the dome (most of the glare); b: the hull fully in front of the Sun (only the scatter around its edge is left).
    for name, lateral, look_lat in (("hull_crossing_sun_a", -12.9, -14.4), ("hull_crossing_sun_b", -1.5, -1.5)):
        camx = add(scale(SUN, -95.0), scale(side, lateral))
        stills.append({"name": name, "pos": r3(camx), "lookAt": r3(add(scale(SUN, 400.0), scale(side, look_lat))), "fov": 40})

    # Turning: 24 frames at 0.005 degrees (about an eighth of a pixel at 40 degrees vertical) from a star-rich spot near the band,
    # then 12 frames at 1.5 degrees (a 90 degree/s turn at 60 Hz). Stills, so each frame is exact.
    f0 = norm(galactic_to_world(40, 3))
    e0 = euler_for(f0)
    for i in range(24):
        stills.append({"name": f"turn_slow_{i:02d}", "pos": r3(scale(f0, FAR)),
                       "rot": [e0[0], round(e0[1] + 0.005 * i, 4), 0.0], "fov": 40, "warmup": 0.1})
    for i in range(12):
        stills.append({"name": f"turn_fast_{i:02d}", "pos": r3(scale(f0, FAR)),
                       "rot": [e0[0], round(e0[1] + 1.5 * i, 4), 0.0], "fov": 40, "warmup": 0.1})

    sun_view = norm(rotate_yaw(SUN, 10.0))
    plan = {
        "scene": "sky",
        "defaults": {"width": 1920, "height": 1080, "warmup": 0.5, "perfWarmup": 2.0},
        "stills": stills,
        "perf": [{"name": "sky_on_toward_sun", "seconds": 10, "pos": r3(scale(sun_view, FAR)),
                  "lookAt": r3(scale(sun_view, 2 * FAR)), "fov": 40}],
    }
    out = os.path.join(ROOT, "gauntlet", "shots", "sky.json")
    with open(out, "w", newline="\n") as f:
        json.dump(plan, f, indent=1)
        f.write("\n")

    # Cost baselines for the same view: the bare scene (no sky drawn, no camera look), and the camera look alone (no sky drawn).
    for scene, name, fname in (("sky_bare", "sky_bare_toward_sun", "sky_baseline.json"), ("sky_off", "sky_off_toward_sun", "sky_lookonly.json")):
        base = {
            "scene": scene,
            "defaults": {"width": 1920, "height": 1080, "warmup": 0.5, "perfWarmup": 2.0},
            "perf": [{"name": name, "seconds": 10, "pos": plan["perf"][0]["pos"], "lookAt": plan["perf"][0]["lookAt"], "fov": 40}],
        }
        with open(os.path.join(ROOT, "gauntlet", "shots", fname), "w", newline="\n") as f:
            json.dump(base, f, indent=1)
            f.write("\n")

    # The glare with nothing else in the frame: no stars, no Milky Way. Its own plan because the scene options are per plan.
    only = {
        "scene": "sky",
        "sceneOptions": {"LimitMagnitude": -9.0, "MilkyWayGain": 0.0},
        "defaults": {"width": 1920, "height": 1080, "warmup": 0.5, "perfWarmup": 2.0},
        "stills": [sky_shot("sun_only", rotate_yaw(SUN, 8.0), fov=40), sky_shot("sun_only_edge", rotate_yaw(SUN, 30.0), fov=40)],
        "perf": [],
    }
    out3 = os.path.join(ROOT, "gauntlet", "shots", "sky_sunonly.json")
    with open(out3, "w", newline="\n") as f:
        json.dump(only, f, indent=1)
        f.write("\n")

    # The turn on its own, with the camera look off (no grain, no dust): measures the stars, not the grain.
    shimmer = {
        "scene": "sky",
        "sceneOptions": {"Look": {"GrainAmount": 0.0, "Dust": 0.0, "DustBokeh": 0.0}},
        "defaults": {"width": 1920, "height": 1080, "warmup": 0.5, "perfWarmup": 2.0},
        "stills": [st for st in stills if st["name"].startswith("turn_slow_")],
        "perf": [],
    }
    out4 = os.path.join(ROOT, "gauntlet", "shots", "sky_shimmer.json")
    with open(out4, "w", newline="\n") as f:
        json.dump(shimmer, f, indent=1)
        f.write("\n")

    print("sun world dir", r3(SUN), "hull_lit background galactic", lon, lat, "angle to sun", round(ang, 1), "| field", lon2, lat2, round(ang2, 1), "| shadow band", lon3, lat3, round(ang3, 1), "| shadow field", lon4, lat4, round(ang4, 1))
    print("galactic centre world dir", r3(norm(galactic_to_world(0, 0))), "NCP", r3(norm(eq_to_world(equatorial(0, 90)))))
    print("wrote", out, len(stills), "stills")


if __name__ == "__main__":
    main()
