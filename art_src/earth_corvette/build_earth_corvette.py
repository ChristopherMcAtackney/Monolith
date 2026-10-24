"""Earth's corvette (working name: Warden class), about 386 m long with its engines: a long, layered silhouette of plated segments in matte
black and chrome, joined by recessed necks and chrome collars; lit window rows and small running lights give the scale away; spines, fins,
an asymmetric sensor boom and radiator stacks; five recessed, glowing engine bells; a prow of three lances and a halo. Flak batteries and
anti-fighter guns, launchers, the bridge, sensors, comms and the reactor are named subsystems, each with a wrecked variant and debris; the
hull has damage sites, FX hooks and sampled points for scorch decals. Nothing here is hand-made.

    tools\\blender.ps1 art_src\\earth_hull\\build_earth_hull_textures.py       (once, or when the textures change)
    tools\\blender.ps1 art_src\\earth_corvette\\build_earth_corvette.py        (writes the FBX and manifest; then tools\\unity.ps1 ...ShipArtSetup.Run)
    tools\\blender.ps1 art_src\\earth_corvette\\build_earth_corvette.py -- --preview        (also renders Workbench views, into $MN_PREVIEW_DIR)

Axes while building: x right (starboard), y forward (the bow), z up. Nose at +y.
"""
import json
import math
import os
import sys

import numpy as np

from blender_lib import mnmesh, mnship, mnkit, mnparts, mnsystems, mnpreview
from blender_lib.mnkit import Kit, station, complement, PLATE, TRIM, LIGHTS, ENGINE
from blender_lib.mnkit import LIGHT_WHITE, LIGHT_COLD, LIGHT_RED, LIGHT_GREEN, LIGHT_AMBER, LIGHT_BLUE
from blender_lib.mnmesh import Frame, norm, cross, chamfered_rect, regular_poly

ROOT = os.environ.get("MN_ROOT", os.getcwd())
ART = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "EarthCorvette")
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "ShipArt")
TRIMS = json.load(open(os.path.join(ROOT, "art_src", "earth_hull", "trims.json")))
MATERIAL_SET = "Assets/Monolith/Art/EarthHull/earth_hull.materials.json"
SHIP_ID = "earth_corvette"
SEED = 1141
ZHAT = np.array([0.0, 0.0, 1.0])
XHAT = np.array([1.0, 0.0, 0.0])
YHAT = np.array([0.0, 1.0, 0.0])

# ------------------------------------------------------------------------------------------------ the hull, aft to bow
st = station
SEG = {
    "engine": [st(-186, 22, 15, 5), st(-150, 22, 15, 5)],
    "reactor": [st(-150, 19, 13, 4.5), st(-104, 19.5, 13.5, 4.5)],
    "neck1": [st(-104, 16.5, 11, 4), st(-96, 16.5, 11, 4)],
    "main": [st(-96, 25, 17, 6), st(-62, 28, 19, 6.5), st(-12, 28, 19, 6.5), st(14, 25, 17.5, 6)],
    "neck2": [st(14, 14, 10.5, 3.5), st(34, 14, 10.5, 3.5)],
    "fwd": [st(34, 21, 15, 4.5), st(80, 20, 14.5, 4.5), st(124, 16.5, 12, 4)],
    "neck3": [st(124, 12, 8.5, 3), st(130, 12, 8.5, 3)],
    "prow": [st(130, 11, 7.5, 3), st(150, 11, 7.5, 3)],
}
# lit windows on the flanks: segment -> [(z of the strip's centre, trim piece)]
WINDOWS = {
    "reactor": [(-5.0, "windows_a")],
    "main": [(9.0, "windows_a"), (3.5, "windows_b"), (-4.5, "windows_a")],
    "fwd": [(5.0, "windows_a"), (-1.5, "windows_b")],
}
PLATING = {
    "t": dict(rows=(6, 15), cols=(4, 10), height=(0.2, 0.45), sub_prob=0.4),
    "b": dict(rows=(6, 15), cols=(4, 10), height=(0.2, 0.45)),
    "r": dict(rows=(5, 12), cols=(3.5, 8), height=(0.14, 0.4), sub_prob=0.25),
    "l": dict(rows=(5, 12), cols=(3.5, 8), height=(0.14, 0.4), sub_prob=0.25),
    "tr": dict(rows=(6, 13), cols=(3, 7), height=(0.18, 0.36), sub_prob=0.2),
    "tl": dict(rows=(6, 13), cols=(3, 7), height=(0.18, 0.36), sub_prob=0.2),
    "br": dict(rows=(6, 13), cols=(3, 7), height=(0.18, 0.36)),
    "bl": dict(rows=(6, 13), cols=(3, 7), height=(0.18, 0.36)),
}


def seg_at(name, y):
    sts = SEG[name]
    for a, b in zip(sts[:-1], sts[1:]):
        if a["y"] <= y <= b["y"]:
            return mnkit.lerp_station(a, b, (y - a["y"]) / (b["y"] - a["y"]))
    raise ValueError((name, y))


def top_pt(name, y, x=0.0, lift=0.0):
    s = seg_at(name, y)
    return np.array([x, y, s["cz"] + s["hh"] + lift])


def flank_pt(name, y, z, side, lift=0.0):
    s = seg_at(name, y)
    return np.array([side * (s["hw"] + lift) + s["cx"], y, z])


def pt_at(patch, z=None, y=None):
    u = 0.0 if z is None else (patch.p00[2] - z) / (patch.p00[2] - patch.p10[2])
    v = 0.0 if y is None else (y - patch.p00[1]) / (patch.p01[1] - patch.p00[1])
    return patch.pt(u, v)


def flank_strips(K, patch, rows, margin=3.0):
    """Window rows on a flank flat; returns the u-intervals (metres from the top edge) the strips occupy, for the plating to leave free."""
    gaps = []
    n = patch.normal()
    run = norm(cross(ZHAT, n))
    top = patch.p00[2]
    for zc, name in rows:
        ph = K.piece_m(name)[1]
        zb = zc - ph / 2
        if zb < patch.p10[2] + 0.5 or zb + ph > top - 0.5:
            continue
        ya, yb = patch.p00[1] + margin, patch.p01[1] - margin
        if yb - ya < 6:
            continue
        pa, pb = pt_at(patch, zb, ya), pt_at(patch, zb, yb)
        if np.dot(pb - pa, run) < 0:
            pa, pb = pb, pa
        K.strip(pa, run, n, float(np.linalg.norm(pb - pa)), name, lift=0.03)
        gaps.append((top - (zb + ph) - 0.45, top - zb + 0.45))
    return gaps


def plate_bands(K, bands, key, windows=None, styles=None, skip=(), detail=True):
    for bi, band in enumerate(bands):
        for en, patch in band.items():
            if en in skip:
                continue
            gaps = []
            if windows and en in ("r", "l"):
                gaps = flank_strips(K, patch, windows)
            style = (styles or PLATING).get(en, {})
            for gi, (a, b) in enumerate(complement(0.0, patch.lu, gaps, 1.5)):
                K.plate_patch(patch, f"{key}-{bi}-{en}-{gi}", u_range=(a, b), **style)
                if detail and K.lod == 0:
                    K.scatter_details(patch, K.last_cells, f"{key}-{bi}-{en}-{gi}")


def rails(K, stations, radius=0.3):
    """Chrome ridge lines along every corner of a hull segment: the black-and-chrome look in the hard sunlight."""
    for a, b in zip(stations[:-1], stations[1:]):
        pa = chamfered_rect(a["hw"], a["hh"], a["c"])
        pb = chamfered_rect(b["hw"], b["hh"], b["c"])
        for e in range(8):
            p0 = np.array([pa[e][0] + a["cx"], a["y"], pa[e][1] + a["cz"]])
            p1 = np.array([pb[e][0] + b["cx"], b["y"], pb[e][1] + b["cz"]])
            K.pipe(p0, p1, radius, sides=4, caps=False)


def lights_row(K, pts, normal, colour=LIGHT_COLD, size=0.5, kind="running", blink=None):
    for p in pts:
        K.light(p, normal, colour, size, kind, blink)


# ------------------------------------------------------------------------------------------------ the ship at one LOD
def build(ctx):
    K = Kit(ctx, TRIMS, SEED)
    lod = ctx.lod
    ship = ctx.ship
    all_bands = {}

    # --- the main segments, plated, windowed, railed ---
    for name, sts in SEG.items():
        bands = K.loft(sts)
        all_bands[name] = bands
        if name.startswith("neck"):
            for band in bands:
                for en in ("t", "b", "r", "l"):
                    patch = band[en]
                    run = norm(patch.ev())
                    n = patch.normal()
                    for k in range(2):
                        K.strip(patch.pt(0.25 + 0.5 * k, 0.0), run, n, patch.lv, "conduit", lift=0.12)
            continue
        plate_bands(K, bands, name, WINDOWS.get(name), PLATING)
        if lod <= 1:
            rails(K, sts)

    # --- chrome collars where the segments join ---
    K.ring(-104, -96, (22.0, 15.2, 5.2), inner=(16.5, 11, 4))
    K.ring(15, 21, (17.5, 12.8, 4.2), inner=(14, 10.5, 3.5))
    K.ring(27, 33, (17.5, 12.8, 4.2), inner=(14, 10.5, 3.5))
    K.ring(124, 130, (14.5, 10.3, 3.5), inner=(12, 8.5, 3))
    K.ring(-152, -148, (23.0, 16.0, 5.5), inner=(22, 15, 5))

    # --- shoulder belts of armour along the widest hull, port and starboard, not the same length ---
    def belt(side, y0, y1, cz):
        sts = [st(y0, 1.6, 5.0, 1.2, side * 29.0, cz), st(y0 + 7, 3.2, 8.0, 2.0, side * 30.8, cz), st(y1 - 7, 3.2, 8.0, 2.0, side * 30.8, cz), st(y1, 1.6, 5.0, 1.2, side * 29.0, cz)]
        bands = K.loft(sts)
        plate_bands(K, bands, f"belt{side}", None, PLATING)
        if lod <= 1:
            rails(K, sts, 0.26)
        return bands
    belt_s = belt(1, -86, -6, -1.5)
    belt_p = belt(-1, -92, -22, 0.5)

    # --- the dorsal decks: three layers stepping up and back, the top one carries the bridge ---
    deck1 = [st(-90, 15, 2.9, 1.0, 0, 20.4), st(-83, 21.5, 2.9, 1.3, 0, 20.4), st(3, 21.5, 2.9, 1.3, 0, 20.4), st(11, 15, 2.9, 1.0, 0, 20.4)]
    deck2 = [st(-74, 11, 1.8, 0.9, 0, 25.3), st(-67, 14, 1.8, 1.0, 0, 25.3), st(-8, 14, 1.8, 1.0, 0, 25.3), st(-1, 10, 1.8, 0.9, 0, 25.3)]
    deck3 = [st(-58, 8, 2.2, 1.0, 0, 29.4), st(-52, 10, 2.2, 1.1, 0, 29.4), st(-16, 10, 2.2, 1.1, 0, 29.4), st(-10, 7, 2.2, 1.0, 0, 29.4)]
    for nm, d, win in (("deck1", deck1, [(20.9, "windows_slit")]), ("deck2", deck2, None), ("deck3", deck3, None)):
        bands = K.loft(d)
        plate_bands(K, bands, nm, win, PLATING)
        if lod <= 1:
            rails(K, d, 0.22)

    # --- fins, spines and keels (asymmetric: a tall swept fin to port, a low one to starboard, a ventral keel and a forward keel) ---
    K.fin([(-70, 27.0), (-62, 49.0), (-34, 49.0), (-20, 33.0), (-12, 27.0)], -11.5, 1.3)
    K.fin([(-96, 27.0), (-92, 40.0), (-74, 40.0), (-66, 27.0)], 10.0, 1.1)
    K.fin([(-84, -19.0), (-78, -33.0), (-34, -36.0), (-14, -28.0), (-4, -19.0)], 0.0, 1.8)
    K.fin([(36, -15.0), (42, -24.0), (100, -26.0), (114, -15.0)], 0.0, 1.5)
    K.fin([(-184, 15.0), (-176, 29.0), (-162, 29.0), (-152, 15.0)], 13.5, 1.0)
    K.fin([(-184, 15.0), (-178, 23.0), (-164, 23.0), (-156, 15.0)], -13.5, 1.0)
    if lod == 0:
        # window slits and a light bar on the tall fin's faces
        for sx in (-1, 1):
            n = XHAT * sx
            run = cross(ZHAT, n)
            lo, hi = (-58.0, -36.0) if run[1] > 0 else (-36.0, -58.0)
            K.strip(np.array([-11.5 + sx * 0.68, lo, 45.0]), run, n, 22.0, "windows_slit", lift=0.0)
            lo, hi = (-60.0, -32.0) if run[1] > 0 else (-32.0, -60.0)
            K.strip(np.array([-11.5 + sx * 0.68, lo, 41.5]), run, n, 28.0, "light_bar", lift=0.0)

    # --- radiator stacks on the reactor's flanks: three vanes to port, two to starboard ---
    def vane(x, y0, y1, z0, z1):
        K.fin([(y0, z0), (y1, z0), (y1, z1), (y0, z1)], x, 0.6)
        if lod == 0:
            for side in (-1, 1):
                if side > 0:
                    K.tile_piece("radiator", (x + 0.32, y0, z0), YHAT, ZHAT, y1 - y0, z1 - z0)
                else:
                    K.tile_piece("radiator", (x - 0.32, y1, z0), -YHAT, ZHAT, y1 - y0, z1 - z0)
    for x in (-25.5, -30.5, -35.5):
        vane(x, -146, -104, -9.0, 15.0)
    for x in (25.5, 30.5):
        vane(x, -146, -108, -10.0, 13.0)
    for y in (-136, -118):
        for z in (-3.0, 7.0):
            K.pipe((-19.0, y, z), (-36.0, y, z), 0.45, sides=6 if lod == 0 else 4)
            K.pipe((19.0, y, z), (31.0, y, z), 0.45, sides=6 if lod == 0 else 4)

    # --- the sensor boom to starboard: a truss to a phased-array dish; a pod on the way ---
    boom_a, boom_b = np.array([34.0, -50.0, -1.5]), np.array([38.0, 54.0, -1.5])
    K.truss(boom_a, boom_b, 1.7, 6.0)
    K.slab_box(Frame((36.0, -4.0, -1.5), YHAT, ZHAT), (4.0, 12.0, 3.2), chamfer=0.7)
    # --- the prow: a beak block, three lances and a halo ---
    pf = Frame((0, 0, 0), YHAT, ZHAT)
    chrome = K.rect("chrome_brushed")
    ctx.hull.extrude(regular_poly(8, 4.2), pf, 150, 186, TRIM, s1=(0.22, 0.22), rect=chrome, caps=(False, True), cap_mat=TRIM, cap_rect=chrome)
    for sx in (-1, 1):
        ctx.hull.extrude(regular_poly(8, 2.2), Frame((sx * 8.5, 0, -0.5), YHAT, ZHAT), 146, 176 - 2 * (sx < 0), TRIM, s1=(0.28, 0.28), rect=chrome, caps=(False, True), cap_mat=TRIM, cap_rect=chrome)
    K.ring(150, 154, (11.6, 8.1, 3.4), inner=(11, 7.5, 3))
    if lod <= 1:
        ctx.hull.lathe([(9.4, 150.2), (10.2, 150.5), (10.2, 152.5), (9.4, 152.8)], pf, TRIM, segs=40 if lod == 0 else 16, rect=chrome)
        for k in range(4):
            a = k * math.pi / 2 + math.pi / 4
            K.pipe((math.sin(a) * 3.5, 151.5, math.cos(a) * 3.5), (math.sin(a) * 9.6, 151.5, math.cos(a) * 9.6), 0.3, sides=6 if lod == 0 else 4)
    if lod == 0:
        K.tile_piece("heat_tiles", (8.0, 150.04, -5.0), -XHAT, ZHAT, 16.0, 10.0)

    # --- the engine block's aft face: a heat-tiled plate with the five bells set into it ---
    if lod == 0:
        K.tile_piece("heat_tiles", (21.5, -186.05, -14.0), -XHAT, ZHAT, 43.0, 28.0)

    # ============================================================================================ subsystems
    engines = [("C", 0.0, 0.0, 3.4, 6.6, 15.0)] + [(f"{i + 1:02d}", x, z, 2.4, 4.6, 11.0) for i, (x, z) in enumerate([(-14.5, 8.5), (14.5, 8.5), (-14.5, -8.5), (14.5, -8.5)])]
    for nm, x, z, th, ex, ln in engines:
        fr = Frame((x, -186.0, z), (0, -1, 0), ZHAT)
        with ctx.subsystem(nm, "engine", fr, radius=ex * 1.3, hp=2.0 if nm == "C" else 1.2, tags=["propulsion"]) as sb:
            mnparts.engine_nozzle(K, sb, fr, f"eng{nm}", throat=th, exit_r=ex, length=ln)

    # flak batteries along the reactor flanks; anti-fighter guns on the decks, the keel and the forward hull
    for side, tag in ((-1, "P"), (1, "S")):
        for i, y in enumerate((-140.0, -128.0, -116.0, -108.0)):
            p = flank_pt("reactor", y, 3.2, side, lift=0.05)
            fr = K.frame_on(p, XHAT * side, (0, 1, 0.12))
            with ctx.subsystem(f"{tag}{i + 1:02d}", "flak", fr, radius=3.4, tags=["anti-fighter", "flak"]) as sb:
                mnparts.gun_turret(K, sb, fr, f"flak{tag}{i}", s=1.0, barrels=2, barrel_len=4.2)
    aaf_sites = []
    for side in (-1, 1):
        aaf_sites += [("D", f"D{(0 if side < 0 else 2) + 1:02d}", np.array([side * 14.0, -72.0, 23.35]), ZHAT, (0, 1, 0)),
                      ("D", f"D{(0 if side < 0 else 2) + 2:02d}", np.array([side * 16.0, -14.0, 23.35]), ZHAT, (0, 1, 0)),
                      ("V", f"V{(0 if side < 0 else 2) + 1:02d}", np.array([side * 14.0, -64.0, -19.0 - 0.05]), -ZHAT, (0, 1, 0)),
                      ("V", f"V{(0 if side < 0 else 2) + 2:02d}", np.array([side * 16.0, -8.0, -17.5 - 0.05]), -ZHAT, (0, 1, 0)),
                      ("F", f"F{(0 if side < 0 else 2) + 1:02d}", top_pt("fwd", 56.0, side * 10.0, 0.05), ZHAT, (0, 1, 0)),
                      ("F", f"F{(0 if side < 0 else 2) + 2:02d}", top_pt("fwd", 102.0, side * 8.5, 0.05), ZHAT, (0, 1, 0))]
    for grp, nm, p, n, f in aaf_sites:
        fr = K.frame_on(p, n, f)
        with ctx.subsystem(nm, "aaf", fr, radius=4.6, hp=1.6, tags=["anti-fighter", "beam"]) as sb:
            mnparts.gun_turret(K, sb, fr, f"aaf{nm}", s=1.45, barrels=3 if grp == "F" else 2, barrel_len=4.6)

    # launchers
    for side, nm in ((-1, "P01"), (1, "S01")):
        fr = K.frame_on(top_pt("fwd", 78.0, side * 11.0, 0.05), ZHAT, (0, 1, 0))
        with ctx.subsystem(nm, "launcher", fr, radius=5.0, tags=["missile"]) as sb:
            mnsystems.launcher_bank(K, sb, fr, f"launch{nm}")

    # bridge on the top deck, sensor array on the fin, sensor dish on the boom, comms mast, reactor stacks
    bfr = Frame((0, -36.0, 31.6), YHAT, ZHAT)
    with ctx.subsystem("main", "bridge", bfr, radius=12.0, hp=3.0, tags=["command"]) as sb:
        mnsystems.bridge_block(K, sb, bfr, "bridge")
    ffr = Frame((-11.5 - 0.66, -48.0, 40.0), ZHAT, -XHAT)         # local y = up the fin, local z = out of its port face
    with ctx.subsystem("fin", "sensors", ffr, radius=11.0, tags=["radar"]) as sb:
        mnsystems.sensor_panel(K, sb, ffr, "finpanel", w=22.0, h=8.0)
    dfr = Frame((38.0, 56.0, -1.5), (0.25, 1, 0.12), ZHAT)
    with ctx.subsystem("boom", "sensors", dfr, radius=7.5, tags=["radar", "long-range"]) as sb:
        mnsystems.sensor_dish(K, sb, dfr, "dish", r=6.5)
    mfr = K.frame_on(np.array([-20.0, -70.0, 21.0]), ZHAT, (0, 1, 0))
    with ctx.subsystem("main", "comms", mfr, radius=6.0, tags=["comms"]) as sb:
        mnsystems.comms_mast(K, sb, mfr, "mast", height=30.0)
    rfr = K.frame_on(top_pt("reactor", -124.0, 0.0, 0.05), ZHAT, (0, 1, 0))
    with ctx.subsystem("main", "reactor", rfr, radius=9.0, hp=3.0, tags=["power"]) as sb:
        mnsystems.reactor_stacks(K, sb, rfr, "reactor")

    # ============================================================================================ details that make the scale
    if lod <= 1:
        # running lights: red to port, green to starboard at the extremes, cold white elsewhere
        K.light(np.array([-8.5, 172.0, -0.5]) + np.array([-1.9, 0, 0]), (-1, 0.2, 0), LIGHT_RED, 0.9, "nav")
        K.light(np.array([8.5, 174.0, -0.5]) + np.array([1.9, 0, 0]), (1, 0.2, 0), LIGHT_GREEN, 0.9, "nav")
        K.light(np.array([-11.5, -30.0, 49.2]), ZHAT, LIGHT_RED, 1.1, "nav")
        K.light(np.array([10.0, -80.0, 40.2]), ZHAT, LIGHT_GREEN, 1.1, "nav")
        K.light(np.array([0.0, -34.0, -36.2]), -ZHAT, LIGHT_WHITE, 1.2, "strobe", blink="strobe")
        K.light(np.array([38.0, 62.0, 4.8]), ZHAT, LIGHT_GREEN, 0.9, "nav")
        K.light(np.array([-36.0, -144.0, 15.2]), ZHAT, LIGHT_RED, 1.0, "nav")
        K.light(np.array([31.5, -144.0, 13.2]), ZHAT, LIGHT_GREEN, 1.0, "nav")
        K.light(np.array([0.0, 185.0, 0.0]), YHAT, LIGHT_WHITE, 1.0, "strobe", blink="strobe")
        K.light(np.array([-20.0, -70.0, 54.0]), ZHAT, LIGHT_WHITE, 0.9, "strobe", blink="strobe")
        # a line of small cold lights along the belts' tops and the decks' edges
        for y in np.arange(-80.0, -10.0, 14.0):
            K.light(np.array([30.8, y, 6.6]), ZHAT, LIGHT_COLD, 0.45, "running")
        for y in np.arange(-86.0, -26.0, 14.0):
            K.light(np.array([-30.8, y, 8.6]), ZHAT, LIGHT_COLD, 0.45, "running")
        for y in (-100.0, 18.0, 30.0, 127.0):
            ring = {-100.0: (22.0, 15.2), 18.0: (17.5, 12.8), 30.0: (17.5, 12.8), 127.0: (14.5, 10.3)}[y]
            for sx in (-1, 1):
                K.light(np.array([sx * (ring[0] - 2.0), y, ring[1] + 0.1]), ZHAT, LIGHT_COLD, 0.5, "running")
        for x in (-19.0, -9.5, 9.5, 19.0):
            K.light(np.array([x, -187.0, 13.6]), -YHAT, LIGHT_COLD, 0.7, "running")
            K.light(np.array([x, -187.0, -13.6]), -YHAT, LIGHT_COLD, 0.7, "running")
    if lod == 0:
        # markings: the hull code on a pad on the starboard belt, Earth's mark on the top deck
        K.slab_box(Frame((34.2, -46.0, 2.0), YHAT, ZHAT), (0.5, 11.0, 3.9), mat=PLATE, chamfer=0.1)
        K.decal((34.5, -46.0, 2.0), YHAT, XHAT, "label_code", size=(10.0, 3.3), lift=0.0)
        K.slab_box(Frame((0, -42.0, 23.6), YHAT, ZHAT), (8.8, 8.8, 0.4), mat=PLATE, chamfer=0.1)
        K.decal((0, -42.0, 23.82), -YHAT, ZHAT, "label_emblem", size=(8.4, 8.4), lift=0.0)

    # --- the collision proxy: the segments and the big appendages as coarse boxes ---
    if lod == 0:
        for name, sts in SEG.items():
            K.loft(sts, mb=ship.collision)
        K.loft(deck1, mb=ship.collision)
        K.loft([st(-86, 3.2, 8.0, 2.0, 30.8, -1.5), st(-6, 3.2, 8.0, 2.0, 30.8, -1.5)], mb=ship.collision)
        K.loft([st(-92, 3.2, 8.0, 2.0, -30.8, 0.5), st(-22, 3.2, 8.0, 2.0, -30.8, 0.5)], mb=ship.collision)
        K.loft([st(-70, 0.7, 11.0, 0.5, -11.5, 38.0), st(-12, 0.7, 11.0, 0.5, -11.5, 38.0)], mb=ship.collision)
        K.loft([st(-84, 0.9, 8.0, 0.5, 0, -27.0), st(-4, 0.9, 8.0, 0.5, 0, -27.0)], mb=ship.collision)
    return K


def hull_extras(ship):
    """Things computed once from the geometry: damage sites, FX hooks and sampled hull points for scorch decals."""
    sites = [
        ("main-stbd-upper", flank_pt("main", -30.0, 8.0, 1, 0.05), XHAT, 5.5),
        ("main-port-lower", flank_pt("main", -48.0, -6.0, -1, 0.05), -XHAT, 5.0),
        ("deck-stbd", np.array([13.0, -40.0, 23.5]), ZHAT, 5.5),
        ("deck-aft", np.array([-14.0, -82.0, 23.5]), ZHAT, 4.5),
        ("fwd-top", top_pt("fwd", 70.0, 1.0, 0.05), ZHAT, 5.0),
        ("fwd-stbd", flank_pt("fwd", 90.0, 2.0, 1, 0.05), XHAT, 4.5),
        ("reactor-top", top_pt("reactor", -112.0, -7.0, 0.05), ZHAT, 4.5),
        ("belt-stbd-top", np.array([30.8, -52.0, 6.55]), ZHAT, 4.0),
    ]
    for name, p, n, r in sites:
        site_fr = Frame(p, n, _perp(n))
        build_fr = Frame(p, _perp(n), n)
        ship.add_site(site_fr, r, mnparts.breach(Kit(ship.lods[0], TRIMS, SEED), build_fr, r, name))
        ship.add_hook("spark", Frame(p + norm(n) * 0.8, n, _perp(n)), 1.0)
        ship.add_hook("vent", Frame(p + norm(n) * 0.5 + _perp(n) * r * 0.5, n, _perp(n)), 2.0)
    for x, z in ((0, 0), (-14.5, 8.5), (14.5, 8.5), (-14.5, -8.5), (14.5, -8.5)):
        ship.add_hook("fire", Frame((x, -187.0, z), -YHAT, ZHAT), 3.0)
    for i in range(3):
        x = (i - 1) * 6.0
        ship.add_hook("vent", Frame(top_pt("reactor", -124.0, x, 8.0), ZHAT, YHAT), 2.5)
    return sites


def _perp(n):
    n = norm(n)
    a = ZHAT if abs(n[2]) < 0.9 else XHAT
    return norm(cross(a, n))


def damage_points(count=72):
    """Points on the hull flats, spread out, for the progressive scorch plan: (position, outward normal) in ship space."""
    rng = np.random.default_rng(SEED + 7)
    pts = []
    for name, sts in SEG.items():
        if name.startswith("neck") or name == "prow":
            continue
        for a, b in zip(sts[:-1], sts[1:]):
            pa = chamfered_rect(a["hw"], a["hh"], a["c"])
            pb = chamfered_rect(b["hw"], b["hh"], b["c"])
            for e in (0, 1, 2, 5, 6, 7, 3, 4):
                p00 = np.array([pa[e][0], a["y"], pa[e][1]])
                p10 = np.array([pa[(e + 1) % 8][0], a["y"], pa[(e + 1) % 8][1]])
                p11 = np.array([pb[(e + 1) % 8][0], b["y"], pb[(e + 1) % 8][1]])
                p01 = np.array([pb[e][0], b["y"], pb[e][1]])
                patch = mnmesh.Patch(p00, p10, p11, p01)
                area = patch.lu * patch.lv
                for _ in range(max(1, int(area / 260))):
                    u, v = rng.uniform(0.15, 0.85, 2)
                    pts.append((patch.pt(u, v), patch.normal(), area))
    idx = rng.permutation(len(pts))
    chosen = []
    for i in idx:                                   # farthest-point-ish thinning so scorches spread over the whole hull
        p = pts[i][0]
        if all(np.linalg.norm(p - c[0]) > 14.0 for c in chosen):
            chosen.append(pts[i])
        if len(chosen) >= count:
            break
    return [{"p": mnship.to_unity(p), "n": mnship.to_unity(n)} for p, n, _ in chosen]


def main():
    preview = "--preview" in sys.argv
    ship = mnship.Ship(SHIP_ID, "Warden-class corvette", ART, ["mn_earth_plate", "mn_earth_trim", "mn_earth_lights", "mn_earth_engine"], seed=SEED,
                       manifest_dir=RES, material_set=MATERIAL_SET)
    for lod in (0, 1, 2):
        ship.build_lod(lod, build)
    hull_extras(ship)
    ship.extra["damagePoints"] = damage_points()
    ship.extra["lodScreenHeights"] = [0.16, 0.05, 0.012]
    stats = ship.export()
    print(f"[MN] bounds {stats['bounds']}")
    if preview:
        out = os.environ.get("MN_PREVIEW_DIR", os.path.join(ROOT, "art_src", "earth_corvette", "_preview"))
        mnpreview.render_views(out, [
            ("side", (330, -10, 20), (0, 0, 8), 38),
            ("three_quarter", (260, -330, 120), (0, -10, 8), 38),
            ("top", (0, -10, 520), (0, -10, 0), 38),
            ("aft", (50, -330, 40), (0, -170, 0), 40),
            ("bow", (120, 330, 60), (0, 140, 0), 40),
            ("close", (70, -100, 60), (10, -60, 10), 45),
        ])
        mnpreview.render_views(out + "_wreck", [("wreck_close", (60, -150, 50), (0, -125, 5), 45), ("wreck_three", (260, -330, 120), (0, -10, 8), 38)],
                               show=("__destroyed", "__debris", "__breach"))


main()
