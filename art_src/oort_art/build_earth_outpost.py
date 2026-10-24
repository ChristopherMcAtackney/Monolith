"""Earth's comet outpost, the Oculus (class earth_outpost, GTI Arcadia numbers): the early-warning station of the Oort Cloud, bored into a comet nucleus and clamped to its
ice. The nucleus is a dark, dusty, two-lobed rock (a smooth union of superellipsoids, bumped by lumps, ridges, strata and craters) with frost in its craters and outgassing
jets from its vents; the station is Earth's cold black and chrome: a long armoured spine in a shelf cut across the top of the big lobe, the Eye (a gimbal ring with a red-pupilled
lens: the sensor), a tall comms tower with its dish (the target the mission's first decision turns on), docking arms clamped to the ice, radiator vanes, flak on the spine, gun pits
dug into the ice, and the hangar cut into a cliff on the face the approach looks at: an armoured mouth with a lit bay, from which the interceptors launch.

    tools\\blender.ps1 art_src\\oort_art\\build_oort_textures.py        (once, or when the ice textures change)
    tools\\blender.ps1 art_src\\oort_art\\build_earth_outpost.py        (writes the FBX and manifest; then tools\\unity.ps1 ...ShipArtSetup.Run)
    tools\\blender.ps1 art_src\\oort_art\\build_earth_outpost.py -- --preview        (also renders Workbench views, into $MN_PREVIEW_DIR)

Axes while building: x right (starboard), y forward (toward the approach: the hangar face), z up (the station's side). The mission turns the model so +y faces the player.
Scale is real: the nucleus is about 1.1 km by 0.9 by 0.6 and the station about 420 m long.
"""
import json
import math
import os
import sys
import time

import numpy as np

from blender_lib import mnmesh, mnship, mnkit, mnparts, mnsystems, mnpreview, mnrock, mnearth
from blender_lib.mnkit import Kit, station
from blender_lib.mnkit import PLATE, TRIM, LIGHTS, ENGINE, CHROME, PLATE_B, PLATE_C, PLATE_D, PLATE_E, WEAR, PAINT, PLUME
from blender_lib.mnkit import LIGHT_WHITE, LIGHT_COLD, LIGHT_RED, LIGHT_GREEN, LIGHT_AMBER, LIGHT_BLUE, LIGHT_EMBER
from blender_lib.mnmesh import Frame, Patch, MeshBuilder, norm, cross, chamfered_rect, regular_poly
from blender_lib.mnrock import smoothstep

ROOT = os.environ.get("MN_ROOT", os.getcwd())
ART = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "EarthOutpost")
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "ShipArt")
TRIMS = json.load(open(os.path.join(ROOT, "art_src", "earth_hull", "trims.json")))
MATERIAL_SET = "Assets/Monolith/Art/EarthHull/earth_oort.materials.json"
SHIP_ID = "earth_outpost"
SEED = 2177
T0 = time.time()
ROCK, ICE, JET = 12, 13, 14
MATERIALS = ["mn_earth_plate", "mn_earth_trim", "mn_earth_lights", "mn_earth_engine", "mn_earth_chrome", "mn_earth_plate_b", "mn_earth_plate_c", "mn_earth_plume", "mn_earth_plate_d",
             "mn_earth_plate_e", "mn_earth_wear", "mn_earth_paint", "mn_oort_rock", "mn_oort_ice", "mn_oort_jet"]
X, Y, Z = mnearth.X, mnearth.Y, mnearth.Z
mnkit.TONE_DOMINANT = (0.22, 0.42, 0.24, 0.09, 0.03)      # the station is black and graphite with a little grey: the paint tones, deep to pale


def log(msg):
    print(f"[MN] outpost {time.time() - T0:5.1f}s  {msg}", flush=True)


# ------------------------------------------------------------------------------------------------ layout (ship space, metres)
ZS = 282.0                    # the shelf the station stands on
SY = -20.0                    # the spine's centre line (world y)
YW = 315.0                    # the hangar cliff: a plane facing +y
MOUTH = dict(x=-235.0, z=12.0, hw=50.0, hh=26.0, depth=80.0)
SHELF = dict(cx=-135.0, hx=232.0, hy=118.0, feather=52.0)

NUCLEUS = dict(
    lobes=[((-190.0, -20.0, 0.0), (375.0, 365.0, 300.0), 2.5), ((315.0, 45.0, -10.0), (250.0, 245.0, 215.0), 2.4)],
    blend=70.0, seed=17,
    amp=dict(big=56.0, mid=13.0, fine=3.0, ridge=15.0, strata=2.4),
    strata_axis=(0.22, 0.12, 1.0),
    craters=[((-0.90, 0.10, 0.35), 110.0, 28.0), ((-0.45, 0.85, -0.30), 70.0, 18.0), ((-0.55, -0.75, 0.35), 95.0, 26.0), ((-0.20, 0.10, -0.97), 90.0, 24.0),
             ((-0.60, -0.80, -0.10), 60.0, 15.0), ((0.55, 0.15, 0.82), 90.0, 24.0), ((0.60, 0.78, 0.05), 75.0, 20.0), ((0.45, -0.85, 0.20), 70.0, 19.0),
             ((0.97, -0.10, -0.20), 60.0, 16.0), ((0.10, 0.30, 0.95), 40.0, 10.0), ((-0.35, 0.55, 0.75), 44.0, 12.0), ((0.28, 0.35, 0.90), 38.0, 10.0),
             ((0.82, 0.45, 0.30), 36.0, 10.0), ((-0.70, 0.60, 0.30), 34.0, 9.0), ((0.05, -0.30, 0.95), 46.0, 12.0), ((-0.95, -0.2, 0.15), 40.0, 11.0),
             ((0.35, -0.5, -0.8), 66.0, 16.0), ((-0.4, -0.3, -0.9), 54.0, 14.0)],
)
# gun pits dug into the ice: (direction from the nucleus centre, radius, depth, name)
PITS = [((-0.66, 0.46, 0.50), 26.0, 8.0, "fore_p"), ((0.66, 0.62, 0.36), 26.0, 8.0, "fore_s"), ((-0.97, 0.10, 0.12), 26.0, 8.0, "west"),
        ((0.96, 0.08, 0.14), 26.0, 8.0, "east"), ((-0.30, -0.88, 0.33), 26.0, 8.0, "aft"), ((0.0, 0.36, -0.93), 26.0, 8.0, "belly")]
# vents: (direction, radius, depth, jet length, name)
VENTS = [((0.70, -0.25, 0.66), 22.0, 11.0, 520.0, "neck_top"), ((-0.90, 0.27, 0.34), 20.0, 10.0, 560.0, "west_rim"), ((0.76, 0.60, 0.12), 17.0, 8.0, 330.0, "east_front"),
         ((0.20, 0.95, -0.22), 16.0, 8.0, 280.0, "neck_front"), ((-0.25, -0.70, 0.67), 19.0, 9.0, 420.0, "back_top"), ((0.05, 0.30, -0.95), 15.0, 8.0, 200.0, "belly_vent")]


def _punch(V, d, R, depth, berm=0.22):
    """Push the surface along the line from the nucleus centre through direction d inward by ``depth`` within radius R, with a raised rim."""
    d = norm(np.asarray(d, float))
    t = V @ d
    perp = np.linalg.norm(V - t[:, None] * d, axis=1)
    sel = t > 0
    prof = 1.0 - smoothstep(0.45 * R, R, perp)
    rim = np.exp(-((perp - 1.22 * R) / (0.28 * R)) ** 2) * berm
    k = np.where(sel, -prof * depth + rim * depth, 0.0)
    return V + d[None, :] * k[:, None]


def edit_shelf(V):
    S = SHELF
    f = (1 - smoothstep(S["hx"], S["hx"] + S["feather"], np.abs(V[:, 0] - S["cx"]))) * (1 - smoothstep(S["hy"], S["hy"] + S["feather"], np.abs(V[:, 1] - SY)))
    above = V[:, 2] > ZS
    rough = mnrock.fbm3(V / 14.0, 41, 2) * 0.7
    V = V.copy()
    V[:, 2] = np.where(above, V[:, 2] - f * (V[:, 2] - ZS) + f * rough, V[:, 2])
    return V


def edit_wall(V):
    """A cliff facing +y: the rock in front of the plane YW is shaved off over an organic footprint, flat and clean round the mouth and rough toward the edges; the hangar's
    pocket is pushed well back inside it."""
    xc, zc = MOUTH["x"], MOUTH["z"] + 4.0
    warp = 22.0 * mnrock.fbm3(V / 120.0, 61, 2)
    f = (1 - smoothstep(110.0, 165.0, np.abs(V[:, 0] - xc) + warp)) * (1 - smoothstep(100.0, 150.0, np.abs(V[:, 2] - zc) + warp))
    pad = (1 - smoothstep(92.0, 112.0, np.abs(V[:, 0] - xc))) * (1 - smoothstep(72.0, 92.0, np.abs(V[:, 2] - zc)))
    plane = YW + (1.0 - pad) * (5.0 + 7.0 * mnrock.fbm3(V / 17.0, 53, 2))
    front = V[:, 1] > plane
    V = V.copy()
    V[:, 1] = np.where(front, V[:, 1] - f * (V[:, 1] - plane), V[:, 1])
    m = MOUTH
    inside = (np.abs(V[:, 0] - m["x"]) < m["hw"] + 8.0) & (np.abs(V[:, 2] - m["z"]) < m["hh"] + 8.0) & (V[:, 1] > YW - 20.0)
    V[:, 1] = np.where(inside, YW - m["depth"] - 12.0, V[:, 1])
    return V


def edit_pits(V):
    for d, R, depth, _ in PITS:
        V = _punch(V, d, R, depth)
    for d, R, depth, _, _ in VENTS:
        V = _punch(V, d, R, depth, berm=0.35)
    return V


def ice_fn(cen, nrm, V, F):
    """Ice where the rock has been broken: pit and vent floors, the inside of the big craters, and thin patches of frost on the sheltered, up-facing hollows."""
    ice = np.zeros(len(cen), bool)
    for d, R, depth, *_ in list(VENTS):
        d = norm(np.asarray(d, float))
        t = cen @ d
        perp = np.linalg.norm(cen - t[:, None] * d, axis=1)
        ice |= (t > 0) & (perp < 0.8 * R)
    cn = cen / (np.linalg.norm(cen, axis=1, keepdims=True) + 1e-9)
    for cdir, rad, depth in NUCLEUS["craters"]:                                  # frost on the floors of the big craters
        if rad < 70.0:
            continue
        d = norm(np.asarray(cdir, float))
        ice |= (cn @ d) > math.cos(0.5 * rad / 440.0)
    frost = mnrock.fbm3(cen / 150.0, 91, 3)
    ice |= (frost > 0.80) & (nrm[:, 2] > 0.3)
    return ice


def make_rock(level):
    nuc = mnrock.Nucleus(NUCLEUS)
    t = time.time()
    rm = nuc.mesh(level, edits=(edit_shelf, edit_wall, edit_pits), ice_fn=ice_fn)
    lo, hi = rm.bounds()
    log(f"rock level {level}: {len(rm.F)} faces, {rm.ice.sum()} ice, bounds {np.round(lo)} .. {np.round(hi)} ({time.time() - t:.1f}s)")
    return rm


# ------------------------------------------------------------------------------------------------ the spine
BURY = 14.0                    # how far the spine's underside sits below the shelf
SF = Frame((0.0, SY, ZS), X, Z)         # local y = world x (east), local x = world -y (so the "l" flank faces the approach), z up


def S(y, hw, hh, c):
    return station(y, hw, hh, c, 0.0, hh - BURY)


SPINE = {
    "W": [S(-350, 32, 24, 7), S(-300, 38, 27, 7), S(-272, 38, 27, 7)],
    "n1": [S(-272, 29, 19, 5), S(-258, 29, 19, 5)],
    "M1": [S(-258, 35, 24, 7), S(-215, 37, 26, 7), S(-170, 35, 24, 7)],
    "n2": [S(-170, 28, 18, 5), S(-158, 28, 18, 5)],
    "E": [S(-158, 46, 30, 9), S(-100, 50, 32, 9), S(-40, 46, 30, 9)],
    "n3": [S(-40, 29, 19, 5), S(-28, 29, 19, 5)],
    "M2": [S(-28, 33, 23, 7), S(10, 35, 25, 7), S(50, 31, 22, 7)],
    "n4": [S(50, 24, 17, 5), S(60, 24, 17, 5)],
    "T": [S(60, 28, 18, 6), S(100, 26, 17, 6)],
}
# window rows (height above the shelf, trim piece) on the front flank of each segment: a few bands, not a facade
WINDOWS = {
    "W": [(6.0, "windows_a"), (11.0, "windows_b")],
    "M1": [(5.0, "windows_b"), (10.0, "windows_a"), (15.5, "windows_a"), (27.0, "windows_b")],
    "E": [(7.0, "windows_a"), (12.5, "windows_b"), (33.0, "windows_a"), (38.0, "windows_a")],
    "M2": [(6.0, "windows_b"), (11.0, "windows_a"), (25.0, "windows_a")],
    "T": [(6.0, "windows_a")],
}


def seg_station(name, x):
    """The interpolated station of a spine segment at world x."""
    sts = SPINE[name]
    for a, b in zip(sts[:-1], sts[1:]):
        if a["y"] <= x <= b["y"]:
            return mnkit.lerp_station(a, b, (x - a["y"]) / (b["y"] - a["y"]))
    raise ValueError((name, x))


def seg_of(x):
    for k, v in SPINE.items():
        if v[0]["y"] <= x <= v[-1]["y"] and not k.startswith("n"):
            return k
    return None


def spine_top(x):
    """World z of the spine's top at world x (the highest of the segments there)."""
    for k, v in SPINE.items():
        if v[0]["y"] <= x <= v[-1]["y"]:
            s = seg_station(k, x)
            return ZS + s["cz"] + s["hh"]
    return ZS


def spine_front(x):
    for k, v in SPINE.items():
        if v[0]["y"] <= x <= v[-1]["y"]:
            return SY + seg_station(k, x)["hw"]
    return SY + 20.0


def deck_stations(y0, y1, hw, hh, c, bottom, taper=7.0):
    cz = bottom + hh
    return [station(y0, hw * 0.55, hh, c, 0.0, cz), station(y0 + taper, hw, hh, c, 0.0, cz), station(y1 - taper, hw, hh, c, 0.0, cz), station(y1, hw * 0.55, hh, c, 0.0, cz)]


DECKS = {
    "deckW": deck_stations(-342, -286, 26, 4.5, 2.0, 38.5),
    "deckM1": deck_stations(-252, -178, 27, 5.5, 2.5, 37.0),
    "deckE": deck_stations(-150, -48, 34, 4.5, 2.0, 48.5),
    "deckM2": deck_stations(-24, 44, 25, 5.0, 2.2, 34.5),
    "opsTower": [station(-236, 10, 17, 4.0, 0.0, 47.0 + 17), station(-228, 15, 17, 4.0, 0.0, 47.0 + 17), station(-210, 15, 17, 4.0, 0.0, 47.0 + 17), station(-202, 10, 17, 4.0, 0.0, 47.0 + 17)],
}
DECK_WINDOWS = {"deckM1": [(42.0, "windows_slit")], "deckM2": [(39.0, "windows_slit")], "opsTower": [(55.0, "windows_big"), (63.0, "windows_a"), (69.0, "windows_b"), (74.0, "windows_a")]}
FLAK_KEEP = []


def spine(K, ctx):
    lod = ctx.lod
    for name, sts in SPINE.items():
        bands = mnearth.loft_placed(K, SF, sts)
        if name.startswith("n"):
            if lod == 0:
                for band in bands:
                    for en in ("t", "l", "r"):
                        patch = band[en]
                        run = norm(patch.ev())
                        n = patch.normal()
                        for k in range(2):
                            K.strip(patch.pt(0.25 + 0.5 * k, 0.0), run, n, patch.lv, "conduit", lift=0.12)
            continue
        mnearth.plate_bands(K, bands, name, [(ZS + h, nm) for h, nm in WINDOWS.get(name, [])], mnearth.PLATING, window_edges=("l",))
        if lod <= 1:
            mnearth.rails(K, SF, sts, radius=0.32)
    # chrome collars over the necks
    names = list(SPINE)
    for i, name in enumerate(names):
        if not name.startswith("n"):
            continue
        a, b = SPINE[name][0], SPINE[name][-1]
        prev, nxt = SPINE[names[i - 1]][-1], SPINE[names[i + 1]][0]
        hh2 = max(prev["hh"], nxt["hh"]) + 2.2
        scratch = MeshBuilder("collar")
        K.ring(a["y"], b["y"], (max(prev["hw"], nxt["hw"]) + 2.4, hh2, 5.0), inner=(a["hw"], a["hh"], a["c"]), centre=(0.0, hh2 - BURY), mb=scratch)
        K.hull.merge(scratch.transformed_copy(SF))
    # the decks stepping up, each with its own windows and rails
    for name, sts in DECKS.items():
        bands = mnearth.loft_placed(K, SF, sts)
        mnearth.plate_bands(K, bands, name, [(ZS + h, nm) for h, nm in DECK_WINDOWS.get(name, [])], mnearth.PLATING, window_edges=("l",))
        if lod <= 1:
            mnearth.rails(K, SF, sts, radius=0.24)
    # armour ribs standing proud round the segments: the long flanks get a rhythm and real shadows
    if lod <= 1:
        for name, y0, y1, step in (("M1", -245.0, -180.0, 13.0), ("M2", -20.0, 42.0, 14.0), ("W", -338.0, -280.0, 12.0)):
            for y in np.arange(y0, y1, step):
                s0 = seg_station(name, y)
                scratch = MeshBuilder("rib")
                K.ring(y, y + 1.9, (s0["hw"] + 1.7, s0["hh"] + 1.7, s0["c"] + 0.9), inner=(s0["hw"], s0["hh"], s0["c"]), centre=(0.0, s0["cz"]), mb=scratch, plate=True)
                K.hull.merge(scratch.transformed_copy(SF))
    # anchors where the spine meets the shelf: blocks driven into the rock along the front and back, with shoring pipes slanting into the ice
    rng = K.rng("anchors")
    if lod <= 1:
        for x in np.arange(-346.0, 98.0, 17.0):
            x += rng.uniform(-4.0, 4.0)
            if seg_of(x) is None:
                continue
            for side in (1, -1):
                y = SY + side * (spine_front(x) - SY)
                w = rng.uniform(2.6, 6.0)
                h = rng.uniform(2.0, 5.0)
                fr = Frame((x, y + side * 2.2, ZS + 0.4), Y * side, Z)
                K.hull.box(fr, (w, 5.5, h), PLATE_B if rng.random() < 0.7 else PLATE_C, centre=(0, 0, h / 2 - 0.3), chamfer=0.5, tile=6.0)
                if lod == 0 and rng.random() < 0.6:
                    K.pipe((x, y + side * 4.5, ZS + h * 0.5), (x, y + side * 12.0, ZS - 3.0), 0.45, sides=5)


def deck_clutter(K, ctx):
    """Machinery on the decks and the spine's tops: ducts, vent housings, sensor blisters, antenna whips: groups of different sizes at different angles, so no row of
    identical blocks. Keep-outs are boxes (x0, x1, y0, y1) where something else stands."""
    lod = ctx.lod
    if lod >= 2:
        return
    rng = K.rng("clutter")
    keep = [(-205.0, 5.0, SY - 56.0, SY + 56.0), (50.0, 112.0, SY - 34.0, SY + 34.0), (-244.0, -196.0, SY - 22.0, SY + 22.0)] + FLAK_KEEP
    zone = [("W", -345.0, -290.0, 40.0), ("M1", -250.0, -242.0, 38.0), ("M1", -190.0, -178.0, 38.0), ("E", -156.0, -126.0, 50.0), ("E", -70.0, -42.0, 50.0), ("M2", -26.0, 46.0, 36.0)]
    for name, x0, x1, zt in zone:
        count = int((x1 - x0) / 5.5) if lod == 0 else int((x1 - x0) / 16.0)
        for i in range(count):
            x = rng.uniform(x0 + 3, x1 - 3)
            hw = seg_station(name, x)["hw"]
            y = SY + rng.uniform(-hw * 0.7, hw * 0.7)
            if any(a <= x <= b and c <= y <= d for a, b, c, d in keep):
                continue
            top = ZS + zt
            r = rng.random()
            ang = rng.uniform(0, math.pi)
            fr = Frame((x, y, top), (math.cos(ang), math.sin(ang), 0.0), Z)
            if r < 0.34:
                w, l, h = rng.uniform(2.0, 7.0), rng.uniform(3.0, 11.0), rng.uniform(1.4, 5.0)
                K.hull.box(fr, (w, l, h), [PLATE, PLATE_B, PLATE_C][int(rng.integers(3))], centre=(0, 0, h / 2), chamfer=min(0.5, h * 0.2), tile=6.0)
                if lod == 0 and w > 3.0 and l > 4.0:
                    K.decal(fr.p(0, 0, h + 0.02), fr.f, Z, str(rng.choice(["hatch_a", "hatch_b", "vent"])), scale=min(1.0, w / 3.5), lift=0.0)
            elif r < 0.52:
                rr = rng.uniform(0.9, 2.8)
                h = rng.uniform(2.0, 6.0)
                K.hull.lathe([(rr * 1.2, 0.0), (rr * 1.2, 0.5), (rr, 0.8), (rr, h * 0.85), (rr * 1.25, h * 0.9), (rr * 1.25, h)], Frame((x, y, top), Z, -Y), CHROME, segs=14 if lod == 0 else 8, tile=3.0)
            elif r < 0.66 and lod == 0:
                K.dome(Frame((x, y, top), Z, Y), rng.uniform(1.2, 3.2))
            elif r < 0.82:
                h = rng.uniform(6.0, 22.0)
                K.pipe((x, y, top), (x + rng.uniform(-1.2, 1.2), y + rng.uniform(-1.2, 1.2), top + h), 0.16, sides=4)
                if lod == 0 and rng.random() < 0.4:
                    K.light(np.array([x, y, top + h]), Z, LIGHT_RED if rng.random() < 0.5 else LIGHT_WHITE, 0.6, "nav")
            else:
                ln = rng.uniform(8.0, 26.0)
                p1 = np.array([x, y, top]) + np.array([math.cos(ang), math.sin(ang), 0.0]) * ln
                K.duct(np.array([x, y, top]), p1, Z, width=rng.uniform(0.8, 1.6), height=rng.uniform(0.4, 0.9))


def belts(K, ctx):
    """Armour belts along the spine's front flank, raised bands of plate at different heights and lengths: relief that rakes in low sun and breaks the flank's face."""
    lod = ctx.lod
    if lod >= 2:
        return
    specs = [("M1", -250.0, -178.0, 17.0, 2.6), ("E", -150.0, -48.0, 22.0, 3.0), ("E", -146.0, -62.0, 36.0, 2.2), ("M2", -20.0, 44.0, 14.0, 2.4), ("W", -338.0, -282.0, 22.0, 2.4)]
    for k, (name, y0, y1, z, hh) in enumerate(specs):
        ys = np.linspace(y0, y1, max(3, int((y1 - y0) / 14.0)))
        sts = []
        for i, y in enumerate(ys):
            s0 = seg_station(name, float(y))
            hwb = 1.5 if i in (0, len(ys) - 1) else 2.4
            sts.append(station(float(y), hwb, hh, 0.8, -(s0["hw"] + hwb * 0.55), z))
        bands = mnearth.loft_placed(K, SF, sts)
        mnearth.plate_bands(K, bands, f"belt{k}-0", None, mnearth.PLATING)
        if lod <= 1:
            mnearth.rails(K, SF, sts, 0.22)


# ------------------------------------------------------------------------------------------------ small helpers
def lamp(K, mb, pos, normal, colour=LIGHT_COLD, size=0.8):
    """A lit lamp (a small box with its lit face out) in the builder ``mb``: part of whatever it is drawn into, so it goes with the part when the part is destroyed."""
    n = norm(normal)
    up = Z if abs(n[2]) < 0.9 else Y
    fr = Frame(pos, n, up)
    if K.lod <= 1:
        mb.box(fr, (size, size * 0.55, size), LIGHTS, centre=(0, size * 0.25, 0), rect=K.light_rect(colour), caps=True)


def radial_frame(origin_frame, r, a, y=0.0, up=Y):
    """A frame on a ring about origin_frame's y axis: at radius r and angle a (from local +x toward local +z), local y pointing radially outward."""
    p = origin_frame.p(r * math.cos(a), y, r * math.sin(a))
    out = origin_frame.d(math.cos(a), 0.0, math.sin(a))
    return Frame(p, out, origin_frame.f if up is None else up)


# ------------------------------------------------------------------------------------------------ the Eye
U = 1.25                                      # the Eye and the tower are drawn at this scale of their first design
EYE_C = np.array([-100.0, SY + 42.0, ZS + 150.0])
RF = Frame(EYE_C, Y, Z)                       # local x = world x, local y = world y (toward the approach), local z = up
RING = dict(ro=78.0 * U, ri=56.0 * U, w=10.0 * U)
BALL = dict(R=52.0 * U, cy=-6.0 * U, front=24.0 * U)


def eye_housing(K, ctx):
    """The gimbal ring, its two pylons and the braces to the eyeball: hull (it stays when the lens dies)."""
    lod = ctx.lod
    ro, ri, w = RING["ro"], RING["ri"], RING["w"]
    segs = {0: 80, 1: 40, 2: 12}[lod]
    prof = [(ro, -w), (ro, w - 3.0), (ro - 3.0, w), (ri + 4.0, w), (ri, w - 4.0), (ri, -w + 3.0), (ri + 3.0, -w), (ro - 3.0, -w), (ro, -w)]
    ctx.hull.lathe(prof, RF, PLATE, segs=segs, tile=8.0)
    z0 = spine_top(EYE_C[0])
    if lod >= 2:
        for sx in (1, -1):
            ctx.hull.box(Frame((EYE_C[0] + sx * ro * 0.8, EYE_C[1], (z0 + EYE_C[2]) / 2), Y, Z), (24.0, 26.0, EYE_C[2] - z0), PLATE, chamfer=3.0)
        return
    rng = K.rng("eye")
    # chrome lips: the inner edge and the outer edge stand proud as polished bands
    ctx.hull.lathe([(ri - 0.1, -w + 2.0), (ri - 0.1, w + 1.2), (ri + 2.4, w + 1.6), (ri + 2.4, w - 2.0)], RF, CHROME, segs=segs, tile=3.0)
    ctx.hull.lathe([(ro + 0.1, -w + 3.0), (ro + 0.1, w - 2.0), (ro - 2.0, w + 0.8), (ro - 2.0, w - 3.0)], RF, CHROME, segs=segs, tile=3.0)
    nsec = 28
    if lod == 0:
        for k in range(nsec):
            if k % 9 == 5:
                continue                                     # a few sectors left bare: the dark underlayer showing, as if a panel were off
            a0, a1 = 2 * math.pi * k / nsec, 2 * math.pi * (k + 1) / nsec
            r0, r1 = ri + 5.0, ro - 4.0

            def pa(r, a):
                return RF.p(r * math.cos(a), w, r * math.sin(a))
            patch = Patch(pa(r0, a0), pa(r0, a1), pa(r1, a1), pa(r1, a0))
            K.plate_patch(patch, f"eyeface{k // 5}-{k}", rows=(3.5, 9.5), cols=(2.4, 6.0), height=(0.15, 0.42), margin=0.35, sub_prob=0.25)
            K.scatter_details(patch, K.last_cells, f"eyeface{k // 5}-{k}")
            K.edge_wear(patch, K.last_cells, K.last_tones, f"eyeface{k // 5}-{k}")
        for k in range(14):                                  # chrome radial ribs on the face
            a = (k + 0.5) * 2 * math.pi / 14
            fr = radial_frame(RF, (ri + ro) / 2, a, y=w + 0.25)
            ctx.hull.box(fr, (1.2, ro - ri - 9.0, 0.6), CHROME, tile=3.0, chamfer=0.15)
        for k in range(34):                                  # outer clamps: blocks of different sizes round the rim
            a = rng.uniform(0, 2 * math.pi)
            fr = radial_frame(RF, ro + 1.0, a, y=rng.uniform(-3.0, 3.0))
            ctx.hull.box(fr, (rng.uniform(3.0, 9.0), rng.uniform(3.0, 7.0), rng.uniform(12.0, 24.0)), [PLATE, PLATE_B, PLATE_C][int(rng.integers(3))], centre=(0, 1.0, 0), chamfer=0.5, tile=6.0)
        # a ladder of conduit and a cable run on the back face of the ring
        for k in range(10):
            a = rng.uniform(0, 2 * math.pi)
            fr = radial_frame(RF, (ri + ro) / 2, a, y=-w - 0.3)
            ctx.hull.box(fr, (rng.uniform(1.5, 4.5), rng.uniform(8.0, 20.0), 0.7), PLATE_B, tile=4.0, chamfer=0.2)
    # the lit rim: a ring of lamps round the inner lip, cold white, bright enough to read as an eyelid of light from kilometres off
    nl = 56 if lod == 0 else 28
    for k in range(nl):
        a = (k + 0.25 * (k % 2)) * 2 * math.pi / nl
        lamp(K, ctx.hull, RF.p((ri + 6.8) * math.cos(a), w + 0.1, (ri + 6.8) * math.sin(a)), Y, LIGHT_COLD if k % 7 else LIGHT_WHITE, 3.6)
    # the two pylons: a gimbal drum at each side of the ring, a plated pivot housing, two square-section legs down to a plinth on the spine, braced with a lattice
    for sx in (1, -1):
        px = EYE_C[0] + sx * (ro + 3.0)
        piv = Frame((px, EYE_C[1], EYE_C[2]), X * sx, Z)
        sg = 32 if lod == 0 else 12
        ctx.hull.lathe([(16.0, -8.0), (16.0, 0.0), (13.5, 4.0), (13.5, 19.0), (10.0, 19.0)], piv, CHROME, segs=sg, tile=3.0)
        ctx.hull.lathe([(19.0, -13.0), (19.0, -8.0), (16.0, -8.0)], piv, PLATE_C, segs=sg, tile=3.0)
        hx = EYE_C[0] + sx * (ro + 14.0)
        ctx.hull.box(Frame((hx, EYE_C[1], EYE_C[2] - 6.0), Y, Z), (30.0, 26.0, 52.0), PLATE_B, chamfer=3.0, tile=6.0)
        leg_o0 = np.array([EYE_C[0] + sx * (ro + 18.0), EYE_C[1], z0 - 1.0])
        leg_o1 = np.array([hx + sx * 2.0, EYE_C[1], EYE_C[2] - 28.0])
        leg_i0 = np.array([EYE_C[0] + sx * (ro * 0.5), EYE_C[1], z0 - 1.0])
        leg_i1 = np.array([hx - sx * 9.0, EYE_C[1], EYE_C[2] - 26.0])
        K.pipe(leg_o0, leg_o1, 8.5, mat=PLATE, sides=4)
        K.pipe(leg_i0, leg_i1, 7.5, mat=PLATE, sides=4)
        # chrome edge rails along the legs and X-bracing between them
        for dy_ in (-8.0, 8.0):
            K.pipe(leg_o0 + np.array([0, dy_, 0]), leg_o1 + np.array([0, dy_, 0]), 0.5, sides=4, caps=False)
            K.pipe(leg_i0 + np.array([0, dy_, 0]), leg_i1 + np.array([0, dy_, 0]), 0.5, sides=4, caps=False)
        nb = 4
        for j in range(nb):
            t0, t1 = j / nb, (j + 1) / nb
            a0_, a1_ = leg_i0 + (leg_i1 - leg_i0) * t0, leg_i0 + (leg_i1 - leg_i0) * t1
            b0_, b1_ = leg_o0 + (leg_o1 - leg_o0) * t0, leg_o0 + (leg_o1 - leg_o0) * t1
            K.pipe(a0_, b1_ if j % 2 == 0 else b0_, 1.2, sides=4, caps=False)
            K.pipe(b0_ if j % 2 == 0 else a1_, a1_ if j % 2 == 0 else b1_, 1.2, sides=4, caps=False)
            K.pipe(a1_, b1_, 1.5, sides=4, caps=False)
        ctx.hull.box(Frame(((leg_o0[0] + leg_i0[0]) / 2, EYE_C[1], z0 + 3.0), Y, Z), (abs(leg_o0[0] - leg_i0[0]) + 22.0, 34.0, 8.0), PLATE_C, chamfer=1.6, tile=6.0)
        if lod == 0:
            patch = Patch(np.array([hx - sx * 14.0, EYE_C[1] + 13.1, EYE_C[2] - 28.0]), np.array([hx + sx * 14.0, EYE_C[1] + 13.1, EYE_C[2] - 28.0]),
                          np.array([hx + sx * 14.0, EYE_C[1] + 13.1, EYE_C[2] + 18.0]), np.array([hx - sx * 14.0, EYE_C[1] + 13.1, EYE_C[2] + 18.0]))
            if np.dot(patch.normal(), Y) < 0:
                patch = Patch(patch.p10, patch.p00, patch.p01, patch.p11)
            K.plate_patch(patch, f"pivot{sx}-0", rows=(3, 7), cols=(2.5, 6), height=(0.12, 0.3), margin=0.3)
            K.scatter_details(patch, K.last_cells, f"pivot{sx}-0")
            K.strip(np.array([hx - sx * 12.0, EYE_C[1] + 13.15, EYE_C[2] + 8.0]) if sx > 0 else np.array([hx - 12.0, EYE_C[1] + 13.15, EYE_C[2] + 8.0]), X, Y, 24.0, "light_bar", lift=0.0)
    # braces from the ring's inner wall to the ball
    for k in range(6):
        a = (k + 0.5) * 2 * math.pi / 6 + 0.2
        p0 = RF.p((ri - 0.5) * math.cos(a), BALL["cy"], (ri - 0.5) * math.sin(a))
        p1 = RF.p((BALL["R"] - 3.0) * math.cos(a), BALL["cy"], (BALL["R"] - 3.0) * math.sin(a))
        K.pipe(p0, p1, 1.9, sides=6 if lod == 0 else 4)


def eye_lens(K, ctx):
    """The Eye proper (kind sensors): the ball in the ring, its chrome-rimmed front with the phased-array iris, the recessed pupil with its red lamp, and the wreck."""
    lod = ctx.lod
    R, cy, fy = BALL["R"], BALL["cy"], BALL["front"]
    fr = Frame(RF.p(0.0, fy, 0.0), Y, Z)
    with ctx.subsystem("eye", "sensors", fr, radius=R * 1.25, hp=1.0, tags=["radar", "long-range"]) as sb:
        rf = math.sqrt(R * R - (fy - cy) ** 2)
        if lod == 2:
            sb.part().box(RF, (2 * R, 2 * (fy + 30.0), 2 * R), PLATE_B, centre=(0, 0, 0), chamfer=8.0)
            return
        if lod == 1:
            return
        ball, iris = sb.part("ball"), sb.part("iris")
        ys = np.linspace(-R + cy + 16.0, fy, 16)
        prof = [(math.sqrt(max(R * R - (y - cy) ** 2, 1.0)), y) for y in ys]
        ball.lathe(prof, RF, PLATE_B, segs=56, tile=6.0)
        for yy in (-22.0, 8.0):                                   # chrome seam bands round the sphere
            r = math.sqrt(R * R - (yy - cy) ** 2)
            ball.lathe([(r + 0.2, yy - 1.2), (r + 1.1, yy), (r + 0.2, yy + 1.2)], RF, CHROME, segs=56, tile=3.0)
        for k in range(18):                                       # plated panels on the sphere
            a0, a1 = 2 * math.pi * k / 18 + 0.05, 2 * math.pi * (k + 1) / 18 - 0.05
            ya, yb = -17.0, 3.0
            ra, rb = math.sqrt(R * R - (ya - cy) ** 2) + 0.15, math.sqrt(R * R - (yb - cy) ** 2) + 0.15
            P = [RF.p(ra * math.cos(a0), ya, ra * math.sin(a0)), RF.p(ra * math.cos(a1), ya, ra * math.sin(a1)), RF.p(rb * math.cos(a1), yb, rb * math.sin(a1)), RF.p(rb * math.cos(a0), yb, rb * math.sin(a0))]
            patch = Patch(*P)
            if np.dot(patch.normal(), RF.d(math.cos((a0 + a1) / 2), 0, math.sin((a0 + a1) / 2))) < 0:
                patch = Patch(P[1], P[0], P[3], P[2])
            K.plate_patch(patch, f"ball{k // 6}-{k}", rows=(2.5, 6.0), cols=(2.0, 4.5), height=(0.1, 0.25), margin=0.2, mb=ball)
        # the front: a chrome bezel, the iris (the phased-array grid), a funnel into the pupil
        iris.lathe([(rf, fy), (rf, fy + 3.2), (rf - 6.5, fy + 3.2), (rf - 6.5, fy)], RF, CHROME, segs=64, tile=3.0)
        iris.disc(RF, rf - 6.5, fy, TRIM, segs=64, facing=1.0, rect=K.rect("sensor_grid"), inner=26.0)
        iris.lathe([(19.0, fy - 15.0), (26.0, fy)], RF, TRIM, segs=48, rect=K.rect("chrome_brushed"), flip=True)
        iris.disc(RF, 19.0, fy - 15.0, LIGHTS, segs=48, facing=1.0, rect=K.light_rect(LIGHT_RED))
        iris.disc(RF, 11.0, fy - 14.8, LIGHTS, segs=40, facing=1.0, rect=K.light_rect(LIGHT_AMBER))
        iris.disc(RF, 4.6, fy - 14.6, LIGHTS, segs=24, facing=1.0, rect=K.light_rect(LIGHT_WHITE))
        for k in range(20):
            a = 2 * math.pi * k / 20
            lamp(K, iris, RF.p((rf - 3.3) * math.cos(a), fy + 3.3, (rf - 3.3) * math.sin(a)), Y, LIGHT_COLD, 2.0)
        for k in range(3):                                         # iris blades across the grid, off-centre, breaking the circle
            a = k * 2 * math.pi / 3 + 0.6
            K.pipe(RF.p(25.0 * math.cos(a), fy + 0.6, 25.0 * math.sin(a)), RF.p(42.0 * math.cos(a + 0.35), fy + 0.6, 42.0 * math.sin(a + 0.35)), 0.7, mb=iris, sides=4)
        # ---- wreck: the dome cracked open on one side, the iris torn off, embers where the lens was
        d = sb.variant("destroyed")
        burnt = K.rect("burnt")
        a0 = K.rng("eyewreck").uniform(0, 360)
        d.lathe(prof, RF, TRIM, segs=40, rect=burnt, arc=(a0, a0 + 235))
        d.lathe([(p[0] - 1.8, p[1]) for p in prof], RF, TRIM, segs=40, rect=burnt, arc=(a0, a0 + 235), flip=True)
        d.lathe([(rf, fy), (rf, fy + 2.2), (rf - 5.0, fy + 2.2)], RF, TRIM, segs=24, rect=burnt, arc=(a0 + 20, a0 + 150))
        mnparts.wreck_stump(K, d, Frame(RF.p(0, fy - 10.0, 0), Z, Y), 28.0, 11.0, "eye", plates=8, embers=8)
        mnparts.debris_set(K, sb, fr, "eye", [
            {"kind": "plate", "size": (26.0, 26.0, 0.5), "at": (17.0, 2.0, 7.0), "speed": 8}, {"kind": "chunk", "size": (18.0, 13.0, 11.0), "at": (-15.0, -8.0, -10.0), "speed": 6},
            {"kind": "plate", "size": (20.0, 20.0, 0.5), "at": (-12.0, -3.0, 15.0), "speed": 10}, {"kind": "chunk", "size": (10.0, 8.0, 8.0), "at": (8.0, -15.0, -17.0), "speed": 12},
            {"kind": "tube", "size": (4.0, 30.0, 0), "at": (27.0, -10.0, 5.0), "speed": 7}])


# ------------------------------------------------------------------------------------------------ the comms tower
TOWER_X = 82.0
TOWER_H = 215.0                               # to the top of the mast; the whips and the beacon go 30 m higher
DISH_UP = 168.0                               # the big dish's centre above the tower's foot
ARRAY_FROM = 118.0                            # the array (the destructible part) starts this high on the mast


def tower_base_z():
    return spine_top(TOWER_X) - 0.5


def comms_dish(K, mb, fr, R, key, depth=None):
    """A parabolic dish: ``fr`` origin at the rim's centre plane, local y = the way it faces. A polished chrome face, a ribbed black back, a tripod holding the feed horn."""
    depth = depth if depth is not None else R * 0.28
    n = 10
    lod = K.lod
    sg = 44 if lod == 0 else 16
    front = [((i / n) * R, -depth * (1.0 - (i / n) ** 2)) for i in range(n + 1)]
    mb.lathe(front, fr, CHROME, segs=sg, tile=3.0, flip=True)
    back = [(r + 0.0, y - 0.9) for r, y in front]
    mb.lathe(back, fr, PLATE_B, segs=sg, tile=6.0)
    mb.lathe([(R, 0.0), (R + 0.25, -0.45), (R, -0.9)], fr, CHROME, segs=sg, tile=3.0)
    if lod == 0:
        for rr in (0.4, 0.7):
            y = -depth * (1.0 - rr * rr) - 1.0
            mb.lathe([(R * rr, y), (R * rr + 0.5, y - 0.4), (R * rr + 0.5, y - 0.8), (R * rr, y - 1.2)], fr, CHROME, segs=sg, tile=3.0)
        foc = R * R / (4.0 * depth) - depth
        for k in range(3):
            a = k * 2 * math.pi / 3 + 0.5
            K.pipe(fr.p(R * 0.98 * math.cos(a), -0.3, R * 0.98 * math.sin(a)), fr.p(0, foc, 0), max(0.16, R * 0.012), mb=mb, sides=4)
        mb.lathe([(0.3, foc - 1.2), (R * 0.07, foc - 0.2), (R * 0.1, foc + R * 0.08), (R * 0.05, foc + R * 0.08)], fr, CHROME, segs=14, tile=3.0)


def comms_tower(K, ctx):
    lod = ctx.lod
    zb = tower_base_z()
    base = np.array([TOWER_X, SY, zb])
    rng = K.rng("tower")
    w0, w1 = 14.0, 8.5
    wmid = w0 + (w1 - w0) * ARRAY_FROM / TOWER_H
    # the foot: an armoured pad with four buttress pads and a collar
    if lod <= 1:
        ctx.hull.box(Frame(base, Y, Z), (40.0, 40.0, 3.5), PLATE_B, centre=(0, 0, 1.75), chamfer=1.4, tile=6.0)
        for k in range(4):
            a = k * math.pi / 2 + math.pi / 4
            ctx.hull.box(Frame(base + np.array([math.cos(a) * 15.0, math.sin(a) * 15.0, 5.5]), (math.cos(a), math.sin(a), 0), Z), (6.0, 8.0, 7.0), PLATE, chamfer=0.7, tile=6.0)
    # the lower mast: hull (what is left standing when the array goes)
    if lod == 2:
        ctx.hull.box(Frame(base, Y, Z), (20.0, 20.0, TOWER_H + 5.0), PLATE, centre=(0, 0, TOWER_H / 2), chamfer=2.0)
    else:
        mnearth.lattice_tower(K, base + np.array([0, 0, 3.5]), ARRAY_FROM, w0, wmid, 9, rod=0.7, platforms=(0.3, 0.62))
    # the array: kind comms, pivoted at the dish so a shot at the dish counts
    c0 = base + np.array([0.0, 0.0, ARRAY_FROM + 3.5])
    dish_c = base + np.array([0.0, 9.0, DISH_UP])
    face = norm(np.array([0.0, 0.62, 0.78]))
    fr = Frame(dish_c, face, Z)
    with ctx.subsystem("array", "comms", fr, radius=52.0, hp=1.0, tags=["comms"]) as sb:
        if lod == 2:
            sb.part().box(Frame(base, Y, Z), (10.0, 10.0, TOWER_H - ARRAY_FROM), PLATE, centre=(0, 0, (TOWER_H + ARRAY_FROM) / 2))
            sb.part().box(fr, (82.0, 7.0, 82.0), PLATE_B, chamfer=12.0)
            return
        if lod == 1:
            return
        mast, dish, arms = sb.part("mast"), sb.part("dish"), sb.part("arms")
        mnearth.lattice_tower(K, c0, TOWER_H - ARRAY_FROM - 3.5, wmid, 4.2, 7, rod=0.55, mb=mast, platforms=(0.5,))
        # the big dish on a yoke, tilted up toward the approach
        comms_dish(K, dish, fr, 40.0, "bigdish", depth=11.0)
        yoke_a, yoke_b = fr.p(-43.0, -7.0, 0.0), fr.p(43.0, -7.0, 0.0)
        K.pipe(yoke_a, yoke_b, 1.3, mb=dish, sides=6)
        for p in (yoke_a, yoke_b):
            K.pipe(p, np.array([TOWER_X, SY, zb + DISH_UP - 18.0]) + (p - dish_c) * 0.12, 1.1, mb=dish, sides=6)
        dish.box(Frame(np.array([TOWER_X, SY + 3.0, zb + DISH_UP - 16.0]), Y, Z), (13.0, 13.0, 8.0), PLATE, chamfer=1.4, tile=6.0)
        # a crossbar with four small dishes at different angles, and the whip antennas above
        zc = zb + ARRAY_FROM + 18.0
        K.pipe(np.array([TOWER_X - 27.0, SY, zc]), np.array([TOWER_X + 27.0, SY, zc]), 0.6, mb=arms, sides=5)
        for k, (dx, dz, ang) in enumerate(((-27.0, 6.0, 0.5), (27.0, 5.0, -0.4), (-15.0, -8.0, 0.9), (16.0, -6.0, -0.8))):
            f2 = Frame(np.array([TOWER_X + dx, SY - 3.0, zc + dz]), norm(np.array([math.sin(ang), 0.5, 0.55])), Z)
            comms_dish(K, arms, f2, 8.0, f"small{k}", depth=2.4)
            K.pipe(f2.p(0, -2.4, 0), np.array([TOWER_X + dx * 0.4, SY, zc]), 0.34, mb=arms, sides=4)
        top = zb + TOWER_H
        K.pipe(np.array([TOWER_X, SY, top - 4.0]), np.array([TOWER_X, SY, top + 32.0]), 0.36, mb=mast, sides=5)
        for dx, dy_, hh in ((-7.0, 2.0, 20.0), (7.0, -2.0, 26.0), (2.0, 6.0, 15.0)):
            K.pipe(np.array([TOWER_X + dx, SY + dy_, top - 9.0]), np.array([TOWER_X + dx * 1.2, SY + dy_, top - 9.0 + hh]), 0.22, mb=mast, sides=4)
        lamp(K, mast, np.array([TOWER_X, SY, top + 33.5]), Z, LIGHT_RED, 6.0)
        lamp(K, mast, np.array([TOWER_X + 7.0 * 1.2, SY - 2.0, top - 9.0 + 26.0]), Z, LIGHT_WHITE, 2.0)
        lamp(K, mast, np.array([TOWER_X - 7.0 * 1.2, SY + 2.0, top - 9.0 + 20.0]), Z, LIGHT_RED, 2.0)
        for k in range(4):
            a = k * math.pi / 2 + 0.7
            lamp(K, mast, c0 + np.array([math.cos(a) * 10.0, math.sin(a) * 10.0, 1.0]), (math.cos(a), math.sin(a), 0.3), LIGHT_RED, 1.8)
        # ---- wreck: the mast snapped a third of the way up, the stump leaning and burning, the big dish hanging off a yoke arm
        d = sb.variant("destroyed")
        cut = 32.0
        K.pipe(c0, c0 + np.array([1.8, 1.2, cut]), 4.4, mb=d, rect_name="burnt", sides=6)
        for k in range(4):
            a = k * math.pi / 2 + math.pi / 4
            K.pipe(c0 + np.array([math.cos(a) * wmid, math.sin(a) * wmid, 0]), c0 + np.array([math.cos(a) * 7.0 + 2.0, math.sin(a) * 7.0 + 1.0, cut * rng.uniform(0.5, 1.0)]), 0.7, mb=d, rect_name="burnt", sides=4)
        mnparts.wreck_stump(K, d, Frame(c0 + np.array([1.8, 1.2, cut]), Y, Z), 7.0, 3.5, "array", plates=5, embers=4)
        hang = Frame(c0 + np.array([-11.0, 5.0, 19.0]), norm(np.array([0.2, 0.9, -0.4])), Z)
        comms_dish(K, d, hang, 24.0, "hangdish", depth=6.5)
        K.pipe(c0 + np.array([1.8, 1.2, cut - 2.0]), hang.p(0, -3, 0), 1.0, mb=d, rect_name="burnt", sides=5)

        def loc(wp):
            q = np.asarray(wp, float) - fr.o
            return (float(q @ fr.r), float(q @ fr.f), float(q @ fr.u))
        mnparts.debris_set(K, sb, fr, "array", [
            {"kind": "plate", "size": (36.0, 36.0, 0.6), "at": loc(dish_c + np.array([0, 3.0, 0])), "speed": 7},
            {"kind": "tube", "size": (1.7, 48.0, 0), "at": loc(base + np.array([-3.0, 0.0, 140.0])), "speed": 5},
            {"kind": "tube", "size": (1.1, 26.0, 0), "at": loc(base + np.array([4.0, 3.0, 195.0])), "speed": 9},
            {"kind": "chunk", "size": (8.0, 8.0, 7.0), "at": loc(base + np.array([0.0, 0.0, 150.0])), "speed": 6},
            {"kind": "plate", "size": (14.0, 14.0, 0.5), "at": loc(base + np.array([-22.0, -3.0, 136.0])), "speed": 10},
            {"kind": "plate", "size": (13.0, 13.0, 0.5), "at": loc(base + np.array([23.0, -3.0, 139.0])), "speed": 11}])


# ------------------------------------------------------------------------------------------------ more helpers
def light_panel(mb, centre, normal, w, h, up=Z, colour=LIGHT_WHITE, K=None):
    """A flat lit panel (the LIGHTS material, one swatch) facing ``normal``: w across (the frame's x), h up."""
    n = norm(normal)
    u = norm(np.cross(up, n)) if abs(n[2]) < 0.99 else X
    v = np.cross(n, u)
    c = np.asarray(centre, float)
    p = [c - u * w / 2 - v * h / 2, c + u * w / 2 - v * h / 2, c + u * w / 2 + v * h / 2, c - u * w / 2 + v * h / 2]
    mb.quad_rect(p[0], p[1], p[2], p[3], K.light_rect(colour), LIGHTS)


class on_hull:
    """Point the kit's default builder (what scatter_details and edge_wear draw into) at a part's own builder for a moment, so a part's details go with the part."""

    def __init__(self, K, mb):
        self.K, self.mb = K, mb

    def __enter__(self):
        self.saved = self.K.hull
        self.K.hull = self.mb

    def __exit__(self, *a):
        self.K.hull = self.saved


def front_patch(x0, x1, z0, z1, y):
    """A patch on the plane y facing +y: u along z, v along x."""
    return Patch(np.array([x0, y, z0]), np.array([x0, y, z1]), np.array([x1, y, z1]), np.array([x1, y, z0]))


def loc_of(fr, wp):
    q = np.asarray(wp, float) - fr.o
    return (float(q @ fr.r), float(q @ fr.f), float(q @ fr.u))


# ------------------------------------------------------------------------------------------------ the hangar
def hangar(K, ctx):
    """The hangar (kind hangar): an armoured bezel standing 14 m proud of the cliff round a mouth 100 m wide and 52 m high, a tunnel 80 m into the ice with a lit deck,
    walls and back wall, a half-lowered shutter on rams, hazard marks and amber beacons; the wreck is a torn bezel and a collapsed, burnt bay."""
    lod = ctx.lod
    m = MOUTH
    cx, cz, hw, hh, dep = m["x"], m["z"], m["hw"], m["hh"], m["depth"]
    fr = Frame((cx, YW, cz), Y, Z)
    with ctx.subsystem("main", "hangar", fr, radius=64.0, hp=1.0, tags=["hangar"]) as sb:
        if lod == 2:
            sb.part().box(fr, (2 * (hw + 24), 20.0, 2 * (hh + 24)), PLATE_B, centre=(0, 4.0, 0), chamfer=6.0)
            return
        if lod == 1:
            return
        rng = K.rng("hangar")
        frame_p, inner, door = sb.part("frame"), sb.part("interior"), sb.part("door")
        T = 24.0                                    # bezel thickness (across the opening's edge)
        yb, yf = YW - 6.0, YW + 14.0                # the bezel's back and front faces
        # --- the bezel: four slabs, plated on their faces, chrome lips on the inner edge, hazard marks on the top and bottom slabs
        slabs = [("top", cx - hw - T, cx + hw + T, cz + hh, cz + hh + T), ("bot", cx - hw - T, cx + hw + T, cz - hh - T, cz - hh),
                 ("left", cx - hw - T, cx - hw, cz - hh, cz + hh), ("right", cx + hw, cx + hw + T, cz - hh, cz + hh)]
        for nm, x0, x1, z0, z1 in slabs:
            frame_p.box(Frame(((x0 + x1) / 2, (yb + yf) / 2, (z0 + z1) / 2), Y, Z), (x1 - x0, yf - yb, z1 - z0), PLATE_B, chamfer=1.6, tile=8.0)
            patch = front_patch(x0 + 0.6, x1 - 0.6, z0 + 0.6, z1 - 0.6, yf)
            if patch.lu * patch.lv > 4.0:
                K.plate_patch(patch, f"hbez{nm}-0", rows=(3.0, 8.0), cols=(2.5, 6.5), height=(0.15, 0.4), margin=0.3, sub_prob=0.2, mb=frame_p)
                with on_hull(K, frame_p):
                    K.scatter_details(patch, K.last_cells, f"hbez{nm}-0")
                    K.edge_wear(patch, K.last_cells, K.last_tones, f"hbez{nm}-0")
        # chrome lips on the opening's edge and a ring of brackets at the corners
        lip = 3.2
        for nm, (x0, x1, z0, z1) in {"t": (cx - hw - 1.5, cx + hw + 1.5, cz + hh - 0.5, cz + hh + lip), "b": (cx - hw - 1.5, cx + hw + 1.5, cz - hh - lip, cz - hh + 0.5),
                                    "l": (cx - hw - lip, cx - hw + 0.5, cz - hh, cz + hh), "r": (cx + hw - 0.5, cx + hw + lip, cz - hh, cz + hh)}.items():
            frame_p.box(Frame(((x0 + x1) / 2, yf + 1.0, (z0 + z1) / 2), Y, Z), (x1 - x0, 2.4, z1 - z0), CHROME, tile=3.0, chamfer=0.4)
        for sx in (-1, 1):
            for sz in (-1, 1):
                p = np.array([cx + sx * (hw + 12.0), yf + 1.5, cz + sz * (hh + 12.0)])
                frame_p.box(Frame(p, Y, Z), (11.0, 5.0, 11.0), PLATE_C, chamfer=2.4, tile=6.0)
                lamp(K, frame_p, p + np.array([0, 2.6, 0]), Y, LIGHT_AMBER, 4.0)
                tip = p + np.array([sx * 10.0, 40.0, sz * 8.0])                     # a boom out from each corner, lit at its tip: a frame in the dark for the launch lane
                K.pipe(p, tip, 0.8, mb=frame_p, sides=5)
                K.pipe(p + np.array([sx * 3.0, 0, sz * 3.0]), tip - np.array([0, 10.0, 0]), 0.4, mb=frame_p, sides=4)
                lamp(K, frame_p, tip + np.array([0, 1.0, 0]), (sx * 0.3, 1.0, sz * 0.2), LIGHT_AMBER, 4.6)
        # hazard chevrons on the top and bottom slabs
        for zc, nm in ((cz + hh + T / 2, "chev_t"), (cz - hh - T / 2, "chev_b")):
            for side in (-1, 1):
                K.paint_quad(f"chevron_{int(rng.integers(2))}", (cx + side * (hw * 0.5 + 6.0), yf + 0.05, zc), X, Y, size=(hw * 0.9, 10.0), lift=0.0, mb=frame_p)
        # --- the tunnel: floor, ceiling and two walls 2 m thick, ribbed, with lit strips, and a back wall that glows
        t = 2.0
        y0 = YW - dep
        for nm, size, c in (("floor", (2 * hw + 2 * t, dep, t), (cx, YW - dep / 2, cz - hh - t / 2)), ("ceil", (2 * hw + 2 * t, dep, t), (cx, YW - dep / 2, cz + hh + t / 2)),
                            ("lw", (t, dep, 2 * hh), (cx - hw - t / 2, YW - dep / 2, cz)), ("rw", (t, dep, 2 * hh), (cx + hw + t / 2, YW - dep / 2, cz))):
            inner.box(Frame(c, Y, Z), size, PLATE_B, tile=8.0, chamfer=0.2)
        inner.box(Frame((cx, y0 - 1.0, cz), Y, Z), (2 * hw + 2 * t, 2.0, 2 * hh + 2 * t), PLATE_B, tile=8.0)
        light_panel(inner, (cx, y0 + 0.2, cz + 2.0), Y, 2 * hw - 30.0, 2 * hh - 24.0, colour=LIGHT_AMBER, K=K)
        for k in range(6):                                          # ribs: frames of plate every 12 m, each a slightly different depth
            y = YW - 6.0 - k * 12.5 - rng.uniform(0, 2)
            inner.box(Frame((cx, y, cz + hh - 1.0), Y, Z), (2 * hw, 1.6, 2.0), PLATE_C, chamfer=0.3, tile=6.0)
            for sx in (-1, 1):
                inner.box(Frame((cx + sx * (hw - 1.0), y, cz), Y, Z), (2.0, 1.6, 2 * hh), PLATE_C, chamfer=0.3, tile=6.0)
        for xo in (-hw * 0.55, 0.0, hw * 0.55):                      # light bars down the ceiling
            K.strip(np.array([cx + xo - 0.4, YW - 2.0, cz + hh - 0.05]), -Y, -Z, dep - 6.0, "light_bar", lift=0.0, scale=1.6, mb=inner)
        for sx in (-1, 1):                                          # window strips and conduit on the walls
            n = -X * sx
            K.strip(np.array([cx + sx * (hw - 0.05), YW - 3.0, cz + 6.0]), -Y, n, dep - 8.0, "windows_big", lift=0.0, mb=inner)
            K.strip(np.array([cx + sx * (hw - 0.05), YW - 3.0, cz - 14.0]), -Y, n, dep - 8.0, "conduit", lift=0.0, scale=1.4, mb=inner)
        for k in range(14):                                         # guide lamps on the deck
            for sx in (-1, 1):
                lamp(K, inner, np.array([cx + sx * hw * 0.62, YW - 4.0 - k * 5.4, cz - hh + 0.1]), Z, LIGHT_AMBER if k % 4 == 0 else LIGHT_COLD, 1.2)
        K.paint_quad("chevron_0", (cx, YW - 12.0, cz - hh + 0.06), Y, Z, size=(hw * 1.6, 12.0), lift=0.0, mb=inner)
        # --- the shutter: a slab hung half-down on four rams, behind the top slab
        sh_y = YW - 14.0
        door.box(Frame((cx, sh_y, cz + hh - 9.0), Y, Z), (2 * hw - 6.0, 3.0, 17.0), PLATE_C, chamfer=0.6, tile=6.0)
        K.strip(np.array([cx - hw + 4.0, sh_y + 1.6, cz + hh - 14.0]), X, Y, 2 * hw - 8.0, "light_bar", lift=0.0, scale=1.0, mb=door)
        for sx in (-0.8, -0.3, 0.3, 0.8):
            K.pipe(np.array([cx + sx * hw, sh_y, cz + hh - 1.0]), np.array([cx + sx * hw, sh_y, cz + hh + 8.0]), 1.0, mb=door, sides=6)
            K.pipe(np.array([cx + sx * hw, sh_y, cz + hh - 14.0]), np.array([cx + sx * hw, sh_y, cz + hh - 2.0]), 1.8, mb=door, mat=PLATE, sides=6)
        # --- wreck: a torn bezel (the right slab and part of the top gone), a bay filled with rubble, burnt walls, embers
        d = sb.variant("destroyed")
        burnt = K.rect("burnt")
        for nm, x0, x1, z0, z1 in slabs:
            if nm == "right":
                continue
            if nm == "top":
                x1 = x0 + (x1 - x0) * 0.62
            d.box(Frame(((x0 + x1) / 2, (yb + yf) / 2, (z0 + z1) / 2), Y, Z), (x1 - x0, yf - yb, z1 - z0), PLATE_B, chamfer=1.6, tile=8.0)
        torn = K.rng("hangar-wreck")
        for k in range(14):
            p = np.array([cx + torn.uniform(-hw * 0.9, hw * 0.9), YW + torn.uniform(-5.0, 12.0), cz - hh + torn.uniform(0.5, 7.0)])
            mnparts.chunk(K, d, p, (torn.uniform(2.0, 8.0), torn.uniform(2.0, 8.0), torn.uniform(1.5, 5.0)), torn)
        for nm, size, c in (("floor", (2 * hw + 2 * t, dep, t), (cx, YW - dep / 2, cz - hh - t / 2)), ("lw", (t, dep, 2 * hh), (cx - hw - t / 2, YW - dep / 2, cz))):
            d.box(Frame(c, Y, Z), size, TRIM, tile=8.0, rect=burnt)
        d.box(Frame((cx, y0 - 1.0, cz), Y, Z), (2 * hw + 2 * t, 2.0, 2 * hh + 2 * t), TRIM, rect=burnt)
        mnparts.wreck_stump(K, d, Frame((cx, YW - 6.0, cz - hh + 2.0), Z, Y), 34.0, 13.0, "hangar", plates=9, embers=10)
        for k in range(4):
            mnparts.torn_plate(K, d, np.array([cx + torn.uniform(-hw, hw), YW + 2.0, cz + hh - torn.uniform(0.0, 6.0)]), (torn.normal(0, 0.3), 1.0, torn.normal(0, 0.4)), 12.0, torn, thickness=0.5)
        mnparts.debris_set(K, sb, fr, "hangar", [
            {"kind": "plate", "size": (30.0, 20.0, 0.8), "at": (-40.0, 12.0, hh + 8.0), "speed": 8}, {"kind": "chunk", "size": (22.0, 14.0, 12.0), "at": (hw + 10.0, 10.0, 6.0), "speed": 6},
            {"kind": "plate", "size": (24.0, 24.0, 0.8), "at": (hw + 12.0, 12.0, -hh + 6.0), "speed": 10}, {"kind": "chunk", "size": (12.0, 10.0, 8.0), "at": (-20.0, 6.0, -hh + 3.0), "speed": 12},
            {"kind": "tube", "size": (3.0, 36.0, 0), "at": (0.0, 8.0, hh + 4.0), "speed": 7}, {"kind": "plate", "size": (18.0, 18.0, 0.6), "at": (20.0, 4.0, 0.0), "speed": 13}])


def approach_beacons(K, ctx):
    """A row of lit masts standing out from the cliff along the hangar's approach lane: amber lamps, getting taller away from the mouth, so the eye follows them to the door."""
    lod = ctx.lod
    if lod >= 2:
        return
    rm = ROCKS[6]
    m = MOUTH
    for i in range(6):
        for side in (-1, 1):
            x = m["x"] + side * (94.0 + 14.0 * (i % 2))
            z = m["z"] - 8.0 + 4.0 * i
            hit = rm.ray(np.array([x, 1200.0, z]), -Y)
            if hit is None:
                continue
            p, n = hit
            if p[1] < YW - 25.0:
                continue
            h = 14.0 + 3.0 * i
            K.pipe(p - Y * 1.0, p + Y * h * 0.0 + n * h, 0.35, sides=5 if lod == 0 else 4)
            lamp(K, ctx.hull, p + n * (h + 0.4), n, LIGHT_AMBER if i % 2 == 0 else LIGHT_WHITE, 3.4 + 0.4 * i)


# ------------------------------------------------------------------------------------------------ reading at range (oort-mission round 2)
# From the player's start, 7.2 km out, a pixel is about 6.5 m: the lamps (1 to 6 m) are dots at best and the rock is a dark lump, so the outpost said "comet with a
# thing on it". These are lit shapes big enough to cover a pixel and bright enough (the lights material, 150,000 nits) for the bloom to catch: the Eye's two rims as
# continuous rings of light (a circle of light reads as built, and as an eye), lit bands along the decks' fronts (the part of the station that shows over the rock's
# brow from the approach: a horizontal line of light under the Eye), a lit strip along the spine's foot where it meets the ice (hidden by the brow from the start;
# it shows from above and from the flanks), and beacon globes on masts at the spine's ends. They are hull (they stay when parts die) and every LOD carries the rings,
# so the outpost keeps its eye however far off it is.
def beacon_rings(K, ctx):
    """The Eye's rims as two continuous bands of cold light on the ring's face (toward the approach): 6 m from the inner chamfer out, 3 m inside the outer lip.
    They stand 0.7 m off the flat face, above the plating and the radial ribs; the rim lamps poke through the inner band."""
    lod = ctx.lod
    ro, ri, w = RING["ro"], RING["ri"], RING["w"]
    segs = {0: 112, 1: 56, 2: 24}[lod]
    ctx.hull.disc(RF, ri + 10.0, w + 0.7, LIGHTS, segs=segs, facing=1.0, rect=K.light_rect(LIGHT_COLD), inner=ri + 4.0)
    ctx.hull.disc(RF, ro - 3.6, w + 0.7, LIGHTS, segs=segs, facing=1.0, rect=K.light_rect(LIGHT_WHITE), inner=ro - 6.6)


def foundation_strip(K, ctx):
    """A lit strip 3.4 m tall along the spine's front flank just above the shelf, segment by segment (the necks stay dark, so it reads as a run of lit bays), over the
    plating; the anchor blocks in front of it break it into dashes. From the approach it marks where the station meets the ice."""
    lod = ctx.lod
    if lod >= 2:
        return
    z0, z1 = ZS + 1.2, ZS + 4.6
    for name, sts in SPINE.items():
        if name.startswith("n"):
            continue
        xs = np.arange(sts[0]["y"] + 2.0, sts[-1]["y"] - 2.0 + 1e-6, 6.0)
        for a, b in zip(xs[:-1], xs[1:]):
            m = 0.5 * (a + b)
            y = SY + seg_station(name, float(m))["hw"] + 0.55
            ctx.hull.quad_rect(np.array([b, y, z0]), np.array([a, y, z0]), np.array([a, y, z1]), np.array([b, y, z1]), K.light_rect(LIGHT_COLD), LIGHTS)


# the decks' lit bands: (deck, foot above the shelf, height), on each deck's vertical front between its chamfers and clear of its window slit
DECK_BANDS = [("deckW", 41.6, 2.4), ("deckM1", 42.8, 2.2), ("deckE", 51.6, 2.4), ("deckM2", 39.6, 2.2)]


def deck_bands(K, ctx):
    """A lit band (2.2 to 2.4 m tall) along each deck's front face, over the plating, the length of the deck's straight part."""
    lod = ctx.lod
    for name, h, bh in DECK_BANDS:
        sts = DECKS[name]
        y0, y1 = sts[1]["y"] + 1.0, sts[2]["y"] - 1.0
        y = SY + sts[1]["hw"] + 0.5
        z0, z1 = ZS + h, ZS + h + bh
        step = 8.0 if lod == 0 else (y1 - y0)
        xs = np.arange(y0, y1 + 1e-6, step) if lod == 0 else np.array([y0, y1])
        if xs[-1] < y1 - 0.5:
            xs = np.append(xs, y1)
        for a, b in zip(xs[:-1], xs[1:]):
            ctx.hull.quad_rect(np.array([b, y, z0]), np.array([a, y, z0]), np.array([a, y, z1]), np.array([b, y, z1]), K.light_rect(LIGHT_COLD), LIGHTS)


def end_beacons(K, ctx):
    """Masts at the spine's two ends, each with a beacon globe (9 m across, cold white) at its head, so the station's length is marked by two bright points either
    side of the Eye."""
    lod = ctx.lod
    if lod >= 2:
        return
    for x, h in ((-344.0, 46.0), (94.0, 38.0)):
        top = spine_top(x)
        p0 = np.array([x, SY, top - 1.0])
        p1 = np.array([x, SY, top + h])
        K.pipe(p0, p1, 1.1, mat=PLATE, sides=6 if lod == 0 else 4)
        if lod == 0:
            for k in range(3):                                               # guy braces
                a = k * 2 * math.pi / 3 + 0.4
                K.pipe(np.array([x + 7.0 * math.cos(a), SY + 7.0 * math.sin(a), top - 0.5]), p1 - Z * (h * 0.45), 0.25, sides=4, caps=False)
        ctx.hull.box(Frame(p1, Y, Z), (5.0, 5.0, 3.0), PLATE_C, centre=(0, 0, 1.5), chamfer=0.6, tile=4.0)
        R = 4.5
        globe = [(max(R * math.sin(math.pi * k / 10), 0.05), -R * math.cos(math.pi * k / 10)) for k in range(11)]
        ctx.hull.lathe(globe, Frame(p1 + Z * (3.0 + R), Z, Y), LIGHTS, segs=16 if lod == 0 else 8, rect=K.light_rect(LIGHT_WHITE))


# ------------------------------------------------------------------------------------------------ guns, flak, arms
FLAK_SITES = [(-322.0, SY + 16.0, "deckW"), (-300.0, SY - 22.0, "W"), (-188.0, SY + 12.0, "deckM1"), (30.0, SY - 14.0, "deckM2"), (-150.0, SY - 38.0, "E"), (-62.0, SY - 36.0, "E")]
FLAK_KEEP.extend((x - 9.0, x + 9.0, y - 9.0, y + 9.0) for x, y, _ in FLAK_SITES)


def deck_top(x, which):
    if which.startswith("deck"):
        sts = DECKS[which]
        return ZS + sts[1]["cz"] + sts[1]["hh"]
    return spine_top(x)


def flak(K, ctx):
    for i, (x, y, which) in enumerate(FLAK_SITES):
        fr = K.frame_on(np.array([x, y, deck_top(x, which) + 0.05]), Z, (0, 1, 0))
        with ctx.subsystem(f"F{i + 1:02d}", "flak", fr, radius=6.0, tags=["anti-fighter", "flak"]) as sb:
            mnparts.gun_turret(K, sb, fr, f"flak{i}", s=1.7, barrels=2, barrel_len=4.4)
        if ctx.lod <= 1:
            ctx.hull.lathe([(7.0, 0.0), (7.0, 0.6), (5.6, 1.2), (4.4, 1.2)], Frame(fr.o, Z, Y), CHROME, segs=20 if ctx.lod == 0 else 8, tile=3.0)


def rock_site(rm, d):
    d = norm(np.asarray(d, float))
    hit = rm.ray(d * 1700.0, -d)
    return hit


def pit_guns(K, ctx):
    rm = ROCKS[6]
    for i, (d, R, depth, name) in enumerate(PITS):
        hit = rock_site(rm, d)
        if hit is None:
            continue
        p, n = hit
        fwd = np.array([0.0, 1.0, 0.3])
        fr = K.frame_on(p + n * 0.05, n, fwd)
        with ctx.subsystem(f"G{i + 1:02d}", "aaf", fr, radius=10.0, hp=1.0, tags=["anti-fighter", "laser"]) as sb:
            mnparts.gun_turret(K, sb, fr, f"pit{name}", s=2.3, barrels=2, barrel_len=4.8)
        # the collar: a plated ring round the pit with lamps, armoured shields, a cable trench
        if ctx.lod <= 1:
            ax = Frame(p, n, fwd)
            ctx.hull.lathe([(17.5, -1.2), (17.5, 2.4), (14.5, 4.0), (10.5, 4.0)], ax, PLATE_B, segs=28 if ctx.lod == 0 else 10, tile=5.0)
            ctx.hull.lathe([(17.7, 1.9), (17.7, 2.7), (16.4, 3.2)], ax, CHROME, segs=28 if ctx.lod == 0 else 10, tile=3.0)
            if ctx.lod == 0:
                rng = K.rng("pit", name)
                for k in range(5):
                    a = 2 * math.pi * k / 5 + rng.uniform(-0.2, 0.2)
                    q = p + ax.d(math.cos(a), math.sin(a), 0.0) * 19.5
                    ctx.hull.box(K.frame_on(q, n, (math.cos(a), math.sin(a), 0)), (rng.uniform(3, 6), rng.uniform(3, 6), rng.uniform(2, 4.5)), PLATE if k % 2 else PLATE_C, centre=(0, 0, 1.0), chamfer=0.5, tile=5.0)
                for k in range(4):
                    a = 2 * math.pi * (k + 0.5) / 4
                    lamp(K, ctx.hull, p + ax.d(math.cos(a), math.sin(a), 0.0) * 15.5 + n * 4.2, n, LIGHT_COLD, 1.4)


def arm(K, ctx, p0, p1, n_end, name, along=(0, 1, 0), r=6.0):
    """A docking arm: a plated sleeve on a triangular truss from the spine down to a clamp gripping the ice at p1."""
    lod = ctx.lod
    if lod >= 2:
        K.pipe(p0, p1, r * 0.9, mat=PLATE, sides=4)
        return
    d = p1 - p0
    L = np.linalg.norm(d)
    ax = d / L
    K.pipe(p0, p0 + ax * (L - 18.0), r, mat=PLATE, sides=6 if lod == 0 else 4)
    K.truss(p0 + ax * 6.0, p0 + ax * (L - 24.0), r * 1.9, bay=12.0, rod=0.34)
    # a joint with a chrome knuckle in the middle, hydraulic rams across it
    mid = p0 + ax * L * 0.45
    if lod == 0:
        ctx.hull.lathe([(r * 1.4, -r * 1.6), (r * 1.7, -r * 0.6), (r * 1.7, r * 0.6), (r * 1.4, r * 1.6)], Frame(mid, ax, Z), CHROME, segs=20, tile=3.0)
        for sgn in (-1, 1):
            side = norm(np.cross(ax, Z)) * sgn
            K.pipe(mid - ax * r * 5.0 + side * r * 2.4, mid + ax * r * 6.0 + side * r * 1.2, 0.75, sides=6)
            K.pipe(mid - ax * r * 5.0 + side * r * 2.4, mid - ax * r * 0.5 + side * r * 1.9, 1.3, mat=PLATE, sides=6)
    # the clamp at the end: a body and three curved fingers driven into the ice, anchor pipes round it
    mnearth.clamp_claw(K, ctx.hull, p1, n_end, along, r=r * 1.7, length=r * 4.4, key=name)
    if lod == 0:
        rng = K.rng("arm", name)
        for k in range(5):
            a = 2 * math.pi * k / 5 + 0.3
            side = norm(np.cross(n_end, np.array(along) + 1e-3 * np.array([1, 0, 0])))
            q = p1 + (side * math.cos(a) + np.cross(n_end, side) * math.sin(a)) * r * 3.4
            K.pipe(q + n_end * 2.0, q - n_end * 7.0, 0.6, sides=5)
        # a docking ring near the spine end: a chrome ring with lamps, where a ship could berth
        ring = Frame(p0 + ax * 22.0, ax, Z)
        ctx.hull.lathe([(r * 2.4, -1.4), (r * 2.4, 1.4), (r * 1.7, 1.4), (r * 1.7, -1.4), (r * 2.4, -1.4)], ring, CHROME, segs=24, tile=3.0)
        for k in range(8):
            a = 2 * math.pi * k / 8
            lamp(K, ctx.hull, ring.p(math.cos(a) * r * 2.45, 0.0, math.sin(a) * r * 2.45), ring.d(math.cos(a), 0, math.sin(a)), LIGHT_COLD, 1.1)
        lamp(K, ctx.hull, p0 + ax * 14.0 + Z * (r + 1.0), Z, LIGHT_RED if name.endswith("w") else LIGHT_GREEN, 1.4)


def arms(K, ctx):
    rm = ROCKS[6]
    specs = [("a1w", np.array([-296.0, SY + 38.0, ZS + 26.0]), (-300.0, 150.0), 6.5), ("a2", np.array([-60.0, SY + 52.0, ZS + 24.0]), (-40.0, 176.0), 6.0),
             ("a3w", np.array([-350.0, SY, ZS + 14.0]), (-478.0, -16.0), 6.0), ("a4e", np.array([100.0, SY, ZS + 12.0]), (222.0, 30.0), 6.5)]
    for name, p0, (x1, y1), r in specs:
        hit = rm.drop(x1, y1)
        if hit is None:
            continue
        p1, n = hit
        arm(K, ctx, p0, p1 + n * 1.0, n, name, along=(0, 1, 0), r=r)


# ------------------------------------------------------------------------------------------------ radiator vanes, a sail, the sensor boom
def vanes(K, ctx):
    lod = ctx.lod
    top = ZS + 40.0
    spec = [(-318.0, [(SY - 30, top), (SY - 18, top + 66), (SY + 22, top + 66), (SY + 34, top)]), (-298.0, [(SY - 26, top), (SY - 14, top + 44), (SY + 18, top + 44), (SY + 28, top)]),
            (-340.0, [(SY - 22, top + 8), (SY - 12, top + 92), (SY + 14, top + 92), (SY + 24, top + 8)])]
    for x, poly in spec:
        K.fin(poly, x, 1.6)
        if lod == 0:
            ya, yb = max(poly[0][0], poly[1][0]) + 4.0, min(poly[3][0], poly[2][0]) - 4.0
            za, zb = poly[0][1] + 4.0, poly[1][1] - 4.0
            for sgn, xd, yd in ((1, Y, Z), (-1, Z, Y)):
                if sgn > 0:
                    K.tile_piece("radiator", (x + 0.9, ya, za), Y, Z, yb - ya, zb - za)
                else:
                    K.tile_piece("radiator", (x - 0.9, ya, za), Z, Y, zb - za, yb - ya)
        if lod <= 1:
            (y0, z0), (y1, z1), (y2, z2), (y3, z3) = poly
            K.lattice([(y1 + 2.0, z1), (y1 + 8.0, z1 + 38.0), (y2 - 8.0, z2 + 38.0), (y2 - 2.0, z2)], x, beam=0.5, braces=3)
    # a swept lattice sail east of the Eye
    xs = 12.0
    t = spine_top(xs) + 0.0
    K.fin([(SY - 24, t - 2), (SY - 10, t + 60), (SY + 22, t + 60), (SY + 32, t - 2)], xs, 1.8)
    if lod <= 1:
        K.lattice([(SY - 10, t + 60), (SY - 2, t + 100), (SY + 16, t + 100), (SY + 22, t + 60)], xs, beam=0.6, braces=3)
        K.lattice([(SY - 2, t + 100), (SY + 4, t + 128), (SY + 12, t + 128), (SY + 16, t + 100)], xs, beam=0.45, braces=2)
    if lod == 0:
        K.fin_plates((SY - 20.0, SY + 28.0, t + 4.0, t + 56.0), xs, 1.8, "sail", rows=(5, 9), cols=(3, 6))


def boom(K, ctx):
    lod = ctx.lod
    a = np.array([-350.0, SY + 8.0, ZS + 22.0])
    b = np.array([-468.0, SY + 8.0, ZS + 14.0])
    if lod == 2:
        K.pipe(a, b, 3.0, mat=PLATE, sides=4)
    else:
        K.truss(a, b, 4.2, bay=10.0, rod=0.3)
        ctx.hull.box(Frame((-398.0, SY + 8.0, ZS + 19.0), X, Z), (14.0, 12.0, 9.0), PLATE, chamfer=1.4, tile=6.0)
    fr = Frame(b + np.array([-4.0, 0.0, 0.0]), norm(np.array([-0.15, 0.85, 0.45])), Z)
    with ctx.subsystem("boom", "sensors", fr, radius=15.0, tags=["radar"]) as sb:
        mnsystems.sensor_dish(K, sb, fr, "boomdish", r=11.0)


# ------------------------------------------------------------------------------------------------ jets
def jets(K, ctx):
    rm = ROCKS[6]
    lod = ctx.lod
    if lod >= 2:
        return
    for i, (d, R, depth, L, name) in enumerate(VENTS):
        hit = rock_site(rm, d)
        if hit is None:
            continue
        p, n = hit
        axis = norm(n * 0.85 + norm(np.asarray(d, float)) * 0.15)
        fr = Frame(p + axis * 1.0, axis, Y if abs(axis[1]) < 0.9 else Z)
        with ctx.subsystem(f"vent_{name}", "vent", fr, radius=R * 1.6, hp=0.1, tags=["decor"]) as sb:
            if lod == 1:
                return
            jet = sb.part("jet")
            r0, r1 = R * 0.6, L * 0.22
            for k, sc in enumerate((1.0, 0.78, 0.56, 0.34)):
                pts = []
                for j in range(10):
                    t = j / 9.0
                    pts.append((max((r0 + (r1 - r0) * t ** 0.78) * sc * (1.0 + 0.06 * math.sin(t * 7.0 + i)), 0.05), L * t * (1.0 + 0.1 * (3 - k) / 3.0 * 0.0)))
                rect = (k / 4.0 + 0.004, 0.992, (k + 1) / 4.0 - 0.004, 0.008)
                jet.lathe(pts, fr, JET, segs=16, rect=rect)
                jet.lathe(pts, fr, JET, segs=16, rect=rect, flip=True)
            sb.variant("destroyed").box(fr, (0.1, 0.1, 0.1), PLATE)


# ------------------------------------------------------------------------------------------------ loose rock
def boulders(K, ctx):
    lod = ctx.lod
    if lod >= 2:
        return
    rm = ROCKS[6]
    rng = K.rng("boulders")
    placed = 0
    want = 150 if lod == 0 else 14
    tries = 0
    while placed < want and tries < want * 12:
        tries += 1
        x, y = rng.uniform(-460.0, 360.0), rng.uniform(-260.0, 300.0)
        in_spine = (-372.0 < x < 118.0) and (abs(y - SY) < 70.0)
        if in_spine:
            continue
        hit = rm.drop(x, y)
        if hit is None:
            continue
        p, n = hit
        if n[2] < 0.35 and rng.random() < 0.7:
            continue
        big = rng.random() < 0.08
        rad = rng.uniform(11.0, 24.0) if big else float(np.clip(rng.lognormal(1.4, 0.55), 2.0, 11.0))
        if lod == 1 and rad < 10.0:
            continue
        mnrock.boulder(ctx.hull, ROCK, p + n * (rad * 0.1), n, rad, rng, tile=160.0, level=2 if rad > 5 else 1)
        placed += 1


# ------------------------------------------------------------------------------------------------ the model at one LOD
ROCKS = {}


def build(ctx):
    K = Kit(ctx, TRIMS, SEED)
    lod = ctx.lod
    ship = ctx.ship
    level = {0: 6, 1: 5, 2: 3}[lod]
    if level not in ROCKS:
        ROCKS[level] = make_rock(level)
    rm = ROCKS[level]
    rm.emit(ctx.hull, ROCK, ICE, tile=160.0)
    spine(K, ctx)
    belts(K, ctx)
    deck_clutter(K, ctx)
    eye_housing(K, ctx)
    eye_lens(K, ctx)
    comms_tower(K, ctx)
    hangar(K, ctx)
    flak(K, ctx)
    pit_guns(K, ctx)
    approach_beacons(K, ctx)
    beacon_rings(K, ctx)
    deck_bands(K, ctx)
    foundation_strip(K, ctx)
    end_beacons(K, ctx)
    arms(K, ctx)
    vanes(K, ctx)
    boom(K, ctx)
    jets(K, ctx)
    boulders(K, ctx)
    return K


# ------------------------------------------------------------------------------------------------ hull damage: sites, hooks, scorch points, collision
def _perp(n):
    n = norm(n)
    a = Z if abs(n[2]) < 0.9 else X
    return norm(cross(a, n))


def hull_extras(ship):
    """Damage sites (a torn-open look shown when the hull is hit hard there), FX hooks (sparks, vents and fires spawn on them), all on the real surface."""
    rm = ROCKS[6]
    f = lambda x, h: np.array([x, spine_front(x) + 0.05, ZS + h])
    sites = [
        ("m1-front", f(-215.0, 14.0), Y, 8.0), ("w-front", f(-310.0, 20.0), Y, 7.0), ("e-front", f(-70.0, 22.0), Y, 8.0), ("m2-front", f(10.0, 16.0), Y, 7.0),
        ("deck-e", np.array([-100.0, SY, ZS + 57.6]), Z, 9.0), ("deck-m1", np.array([-215.0, SY - 8.0, ZS + 48.1]), Z, 7.0), ("t-top", np.array([88.0, SY + 8.0, spine_top(88.0) + 0.05]), Z, 7.0),
        ("ops-front", np.array([-219.0, SY + 15.1, ZS + 70.0]), Y, 6.0),
    ]
    for nm, d, r in (("cliff", (-0.45, 0.88, 0.10), 12.0), ("neck", (0.10, 0.12, 0.98), 11.0)):
        hit = rock_site(rm, d)
        if hit is not None:
            sites.append((nm, hit[0] + hit[1] * 0.2, hit[1], r))
    for name, p, n, r in sites:
        site_fr = Frame(p, n, _perp(n))
        build_fr = Frame(p, _perp(n), n)
        ship.add_site(site_fr, r, mnparts.breach(Kit(ship.lods[0], TRIMS, SEED), build_fr, r, name))
        ship.add_hook("spark", Frame(p + norm(n) * 0.8, n, _perp(n)), 1.5)
        ship.add_hook("vent", Frame(p + norm(n) * 0.5 + _perp(n) * r * 0.5, n, _perp(n)), 3.0)
    # the Eye, the tower's foot, the hangar mouth: fires and vents when they go
    ship.add_hook("fire", Frame(RF.p(0.0, BALL["front"], 0.0), Y, Z), 6.0)
    ship.add_hook("vent", Frame(RF.p(0.0, BALL["front"], 0.0), Y, Z), 5.0)
    ship.add_hook("fire", Frame(np.array([TOWER_X, SY, tower_base_z() + ARRAY_FROM]), Z, Y), 5.0)
    ship.add_hook("vent", Frame(np.array([TOWER_X, SY, tower_base_z() + ARRAY_FROM]), Z, Y), 4.0)
    ship.add_hook("fire", Frame(np.array([MOUTH["x"], YW + 2.0, MOUTH["z"]]), Y, Z), 8.0)
    ship.add_hook("vent", Frame(np.array([MOUTH["x"] - 20.0, YW + 2.0, MOUTH["z"]]), Y, Z), 6.0)
    return sites


def damage_points(count=84):
    """Points on the spine's flats and the Eye's housing, spread out, for the progressive scorch plan: (position, outward normal) in ship space."""
    rng = np.random.default_rng(SEED + 7)
    pts = []
    for _ in range(900):
        x = rng.uniform(-346.0, 96.0)
        seg = seg_of(x)
        if seg is None:
            continue
        r = rng.random()
        if r < 0.55:
            pts.append((np.array([x, spine_front(x) + 0.05, ZS + rng.uniform(2.0, spine_top(x) - ZS - 4.0)]), Y))
        elif r < 0.85:
            y = SY + rng.uniform(-25.0, 25.0)
            pts.append((np.array([x, y, spine_top(x) + 0.05]), Z))
        else:
            pts.append((np.array([x, SY - (spine_front(x) - SY) - 0.05, ZS + rng.uniform(2.0, spine_top(x) - ZS - 4.0)]), -Y))
    chosen = []
    for p, n in pts:
        if all(np.linalg.norm(p - c[0]) > 15.0 for c in chosen):
            chosen.append((p, n))
        if len(chosen) >= count:
            break
    return [{"p": mnship.to_unity(p), "n": mnship.to_unity(n)} for p, n in chosen]


def collision(ship):
    """The collision proxy: the nucleus as a coarse mesh, the spine, the ring and the tower as boxes."""
    col = ship.collision
    ROCKS[3].emit(col, ROCK, ROCK)
    col.box(Frame(((-350.0 + 100.0) / 2, SY, ZS + 16.0), X, Z), (450.0, 98.0, 62.0), PLATE)
    col.box(Frame((EYE_C[0], EYE_C[1], EYE_C[2] - 4.0), Y, Z), (2 * RING["ro"] + 40.0, 30.0, 2 * RING["ro"] + 10.0), PLATE)
    col.box(Frame((TOWER_X, SY, ZS + 120.0), Y, Z), (24.0, 24.0, 210.0), PLATE)


def main():
    preview = "--preview" in sys.argv
    ship = mnship.Ship(SHIP_ID, "Earth comet outpost", ART, MATERIALS, seed=SEED, manifest_dir=RES, material_set=MATERIAL_SET)
    for lod in (0, 1, 2):
        ship.build_lod(lod, build)
    hull_extras(ship)
    ship.extra["damagePoints"] = damage_points()
    collision(ship)
    ship.extra["lodScreenHeights"] = [0.07, 0.02, 0.006]
    stats = ship.export()
    print(f"[MN] bounds {stats['bounds']}")
    if preview:
        out = os.environ.get("MN_PREVIEW_DIR", os.path.join(ROOT, "art_src", "oort_art", "_preview"))
        cols = dict(mnpreview.DEFAULT_COLOURS)
        cols.update({"mn_oort_rock": (0.17, 0.155, 0.14, 1), "mn_oort_ice": (0.62, 0.70, 0.80, 1), "mn_oort_jet": (0.5, 0.6, 0.8, 1)})
        mnpreview.render_views(out, [
            ("far7km", (0, 7000, 250), (0, 0, 100), 50),
            ("front", (0, 1900, 300), (-30, 0, 120), 30),
            ("station_front", (-120, 900, 470), (-120, -20, 390), 38),
            ("station_3q", (520, 760, 560), (-120, -20, 380), 40),
            ("station_close", (-200, 330, 420), (-120, -20, 360), 50),
            ("hangar", (-235, 520, 60), (-235, 315, 12), 45),
            ("hangar_close", (-235, 400, 40), (-235, 315, 12), 55),
            ("west", (-760, 120, 420), (-330, -20, 330), 40),
            ("east", (620, 160, 420), (60, -20, 330), 40),
            ("top", (0, 0, 1800), (0, 0, 0), 38),
            ("station_top", (-130, -20, 1100), (-130, -20, 300), 38),
            ("side", (1900, 0, 150), (0, 0, 100), 32),
        ], colours=cols, sun=(0.4, 0.55, -0.75), clip=(1.0, 20000.0))


main()
