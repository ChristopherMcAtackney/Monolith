"""Earth's destroyer, the Archon (class earth_destroyer, GTD Hecate numbers): over a kilometre of black armour and chrome, a wasp in plan: a narrow engine block, an aft citadel that
flares to 200 m across under a stepped dorsal mesa and the bridge tower, a waist, a long fore lance tapering to a forked prow, with a heavy shoulder of armour belts down both flanks.
Built from the corvette's language (plated lofts, chrome collars, armour ribs, belts, lit window runs, lattice sails, radiator stacks) at the destroyer's own scale: plates of 10 to
25 m, windows twice the corvette's. Two heavy beam cannons are the first thing the eye finds (a dorsal one on the lance, its barrel raised over the prow; a ventral one aft under
the citadel: each a turret on a drum with a caged barrel of coil rings and a red lens), a chin hangar hangs under the lance with its doors open, three big recessed engines
burn aft, flak batteries line the shoulders and anti-fighter guns the decks and the belly. Every part is a named subsystem with a wreck and debris; the hull has damage sites,
FX hooks and scorch points. Nothing here is hand-made.

    tools\\blender.ps1 art_src\\oort_art\\build_earth_destroyer.py        (writes the FBX and manifest; then tools\\unity.ps1 ...ShipArtSetup.Run)
    tools\\blender.ps1 art_src\\oort_art\\build_earth_destroyer.py -- --preview

Axes while building: x right (starboard), y forward (the bow), z up. Nose at +y.
"""
import json
import math
import os
import sys
import time

import numpy as np

from blender_lib import mnmesh, mnship, mnkit, mnparts, mnsystems, mnpreview, mnearth, mnheavy
from blender_lib.mnkit import Kit, station, complement
from blender_lib.mnkit import PLATE, TRIM, LIGHTS, ENGINE, CHROME, PLATE_B, PLATE_C, PLATE_D, PLATE_E, WEAR, PAINT, PLUME
from blender_lib.mnkit import LIGHT_WHITE, LIGHT_COLD, LIGHT_RED, LIGHT_GREEN, LIGHT_AMBER, LIGHT_BLUE, LIGHT_EMBER
from blender_lib.mnmesh import Frame, Patch, MeshBuilder, norm, cross, chamfered_rect, regular_poly

ROOT = os.environ.get("MN_ROOT", os.getcwd())
ART = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "EarthDestroyer")
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "ShipArt")
TRIMS = json.load(open(os.path.join(ROOT, "art_src", "earth_hull", "trims.json")))
MATERIAL_SET = "Assets/Monolith/Art/EarthHull/earth_hull.materials.json"
SHIP_ID = "earth_destroyer"
SEED = 3319
T0 = time.time()
MATERIALS = ["mn_earth_plate", "mn_earth_trim", "mn_earth_lights", "mn_earth_engine", "mn_earth_chrome", "mn_earth_plate_b", "mn_earth_plate_c", "mn_earth_plume", "mn_earth_plate_d",
             "mn_earth_plate_e", "mn_earth_wear", "mn_earth_paint"]
X, Y, Z = mnearth.X, mnearth.Y, mnearth.Z
IDENT = Frame((0, 0, 0), Y, Z)
st = station
mnkit.TONE_DOMINANT = (0.16, 0.40, 0.30, 0.10, 0.04)      # black and graphite armour, a little grey: the paint tones, deep to pale
WIN = 2.2                                                  # the window pieces are drawn this much bigger than the corvette's


def log(msg):
    print(f"[MN] destroyer {time.time() - T0:5.1f}s  {msg}", flush=True)


# ------------------------------------------------------------------------------------------------ the hull, aft to bow: one continuous loft, collars at the joints
HULL = [
    st(-500, 52, 38, 9), st(-470, 58, 42, 10), st(-408, 60, 44, 10),
    st(-398, 66, 46, 11), st(-300, 92, 54, 13), st(-200, 100, 60, 14), st(-110, 92, 60, 14),
    st(-40, 78, 56, 13), st(30, 70, 54, 12),
    st(90, 66, 50, 11), st(200, 58, 44, 10), st(330, 46, 36, 9),
    st(342, 46, 34, 9), st(430, 32, 26, 8), st(498, 20, 16, 6),
]
COLLARS = [(-408.0, -398.0), (-76.0, -64.0), (330.0, 342.0)]
WINDOWS = [(36.0, "windows_a"), (26.0, "windows_b"), (16.0, "windows_a"), (6.0, "windows_a"), (-4.0, "windows_b"), (-14.0, "windows_a"), (-24.0, "windows_a")]
PLATING = {
    "t": dict(rows=(12, 28), cols=(8, 20), height=(0.35, 0.8), sub_prob=0.45, sub=(3.0, 8.0), sub_height=(0.1, 0.25), gap=0.24, chamfer=0.2, margin=0.4, min_size=0.8),
    "b": dict(rows=(12, 28), cols=(8, 20), height=(0.35, 0.8), gap=0.24, chamfer=0.2, margin=0.4, min_size=0.8),
    "r": dict(rows=(10, 24), cols=(7, 17), height=(0.3, 0.7), sub_prob=0.3, sub=(3.0, 8.0), sub_height=(0.1, 0.25), gap=0.24, chamfer=0.2, margin=0.4, min_size=0.8),
    "l": dict(rows=(10, 24), cols=(7, 17), height=(0.3, 0.7), sub_prob=0.3, sub=(3.0, 8.0), sub_height=(0.1, 0.25), gap=0.24, chamfer=0.2, margin=0.4, min_size=0.8),
    "tr": dict(rows=(12, 26), cols=(6, 14), height=(0.3, 0.65), sub_prob=0.25, sub=(3.0, 7.0), sub_height=(0.1, 0.22), gap=0.24, chamfer=0.2, margin=0.4, min_size=0.8),
    "tl": dict(rows=(12, 26), cols=(6, 14), height=(0.3, 0.65), sub_prob=0.25, sub=(3.0, 7.0), sub_height=(0.1, 0.22), gap=0.24, chamfer=0.2, margin=0.4, min_size=0.8),
    "br": dict(rows=(12, 26), cols=(6, 14), height=(0.3, 0.65), gap=0.24, chamfer=0.2, margin=0.4, min_size=0.8),
    "bl": dict(rows=(12, 26), cols=(6, 14), height=(0.3, 0.65), gap=0.24, chamfer=0.2, margin=0.4, min_size=0.8),
}
SMALL_PLATING = {k: dict(v, rows=(6, 14), cols=(4, 10), height=(0.2, 0.5), sub_prob=0.2, min_size=0.5) for k, v in PLATING.items()}


def hull_at(y):
    for a, b in zip(HULL[:-1], HULL[1:]):
        if a["y"] <= y <= b["y"]:
            return mnkit.lerp_station(a, b, (y - a["y"]) / (b["y"] - a["y"]) if b["y"] > a["y"] else 0.0)
    raise ValueError(y)


def top_z(y):
    s = hull_at(y)
    return s["cz"] + s["hh"]


def bottom_z(y):
    s = hull_at(y)
    return s["cz"] - s["hh"]


def top_pt(y, x=0.0, lift=0.0):
    return np.array([x, y, top_z(y) + lift])


def bottom_pt(y, x=0.0, lift=0.0):
    return np.array([x, y, bottom_z(y) - lift])


def flank_pt(y, z, side, lift=0.0):
    s = hull_at(y)
    return np.array([side * (s["hw"] + lift) + s["cx"], y, z])


def K_frame_on(point, normal, forward=(0, 1, 0)):
    n = norm(normal)
    f = np.asarray(forward, float)
    f = f - n * np.dot(f, n)
    return Frame(point, f, n)


# the beam cannons: mount points on the hull and where they point
CANNONS = {"fore": dict(y=232.0, x=8.0, side=1, pitch=22.0), "aft": dict(y=-250.0, x=-22.0, side=-1, pitch=22.0)}
BEAM_LEN, BEAM_R = 126.0, 13.0
MESA_Y0, MESA_Y1, MESA_HW, MESA_HH = -330.0, -30.0, 50.0, 12.0
TOWER_Y0, TOWER_Y1 = -262.0, -170.0
TOWER_Z0 = 78.0                                    # the tower stands on the mesa (the mesa top there is about 80, the tower sinks 4 m into it)


def cannon_frame(c):
    if c["side"] > 0:
        return K_frame_on(top_pt(c["y"], c["x"], 0.05), Z, (0, 1, 0))
    return K_frame_on(bottom_pt(c["y"], c["x"], 0.05), -Z, (0, 1, 0))


# ------------------------------------------------------------------------------------------------ the ship at one LOD
def build(ctx):
    K = Kit(ctx, TRIMS, SEED)
    lod = ctx.lod
    ship = ctx.ship

    # --- the hull: one loft, plated in bands (each band its own paint region), windowed in broken runs, chrome rails on every corner ---
    bands = mnearth.loft_placed(K, IDENT, HULL)
    mnearth.plate_bands(K, bands, "hull", WINDOWS, PLATING, win_scale=WIN)
    if lod <= 1:
        mnearth.rails(K, IDENT, HULL, radius=0.5)
        for y0, y1 in COLLARS:
            s0 = hull_at(0.5 * (y0 + y1))
            K.ring(y0, y1, (s0["hw"] + 5.0, s0["hh"] + 4.0, s0["c"] + 2.0), inner=(s0["hw"] - 2.0, s0["hh"] - 2.0, s0["c"]))
        # armour ribs: matte frames standing proud round the citadel, the waist and the lance: a rhythm down the long flanks and real shadows
        for y in list(np.arange(-382.0, -92.0, 30.0)) + list(np.arange(-20.0, 80.0, 24.0)) + list(np.arange(104.0, 322.0, 26.0)):
            s0 = hull_at(float(y))
            K.ring(float(y), float(y) + 3.2, (s0["hw"] + 2.8, s0["hh"] + 2.8, s0["c"] + 1.4), inner=(s0["hw"], s0["hh"], s0["c"]), plate=True)

    # --- shoulder belts of armour down both flanks, not the same length or height ---
    def belt(side, y0, y1, zc, hh=11.0):
        pts = []
        for y in np.linspace(y0, y1, max(4, int((y1 - y0) / 40.0))):
            s0 = hull_at(float(y))
            k = 0.35 if y in (y0, y1) else 1.0
            pts.append(st(float(y), 3.2 + 2.6 * k, hh * (0.6 + 0.4 * k), 2.4, side * (s0["hw"] + 1.6 * k), zc))
        bb = mnearth.loft_placed(K, IDENT, pts)
        mnearth.plate_bands(K, bb, f"belt{side}{int(y0)}-0", None, PLATING)
        if lod <= 1:
            mnearth.rails(K, IDENT, pts, 0.36)
    belt(1, -380.0, -60.0, 8.0, 12.0)
    belt(-1, -350.0, -110.0, 4.0, 12.0)
    belt(-1, 0.0, 120.0, 4.0, 9.0)
    belt(1, 40.0, 190.0, 6.0, 8.0)

    # --- the dorsal mesa stepping up from the citadel, the bridge tower on it, and the keel under the waist ---
    def keel(name, y0, y1, hw, hh, z_fn, taper=22.0):
        sts = [st(y0, hw * 0.5, hh, 3.0, 0, z_fn(y0)), st(y0 + taper, hw, hh, 4.0, 0, z_fn(y0 + taper)), st(y1 - taper, hw, hh, 4.0, 0, z_fn(y1 - taper)), st(y1, hw * 0.5, hh, 3.0, 0, z_fn(y1))]
        bb = mnearth.loft_placed(K, IDENT, sts)
        mnearth.plate_bands(K, bb, name, None, SMALL_PLATING)
        if lod <= 1:
            mnearth.rails(K, IDENT, sts, 0.34)
        return sts
    keel("mesa", MESA_Y0, MESA_Y1, MESA_HW, MESA_HH, lambda y: top_z(y) + MESA_HH - 3.0, taper=30.0)
    keel("lanceKeel", 120.0, 300.0, 11.0, 5.0, lambda y: top_z(y) + 2.0, taper=20.0)
    keel("belly", 30.0, 90.0, 16.0, 9.0, lambda y: bottom_z(y) - 6.0, taper=14.0)
    tower = [st(TOWER_Y0, 24, 26, 7, 0, TOWER_Z0 + 24), st(TOWER_Y0 + 16, 36, 28, 8, 0, TOWER_Z0 + 25), st(TOWER_Y1 - 16, 36, 28, 8, 0, TOWER_Z0 + 25), st(TOWER_Y1, 26, 26, 7, 0, TOWER_Z0 + 24)]
    tb = mnearth.loft_placed(K, IDENT, tower)
    mnearth.plate_bands(K, tb, "tower", [(TOWER_Z0 + 40, "windows_big"), (TOWER_Z0 + 30, "windows_a"), (TOWER_Z0 + 20, "windows_b"), (TOWER_Z0 + 10, "windows_a")], SMALL_PLATING, win_scale=1.6)
    if lod <= 1:
        mnearth.rails(K, IDENT, tower, 0.36)

    # --- fins and lattice sails (asymmetric: a tall sail to starboard on the citadel, a low armoured fin to port aft, a ventral fin to starboard, a sensor boom forward to port) ---
    ht = top_z(-120.0)
    K.fin([(-150, ht - 2), (-136, ht + 62), (-84, ht + 62), (-66, ht - 2)], 70.0, 2.4)
    K.lattice([(-136, ht + 62), (-126, ht + 118), (-98, ht + 118), (-86, ht + 62)], 70.0, beam=0.9, braces=4, mat=PLATE)
    K.fin_plates((-132.0, -82.0, ht + 4.0, ht + 58.0), 70.0, 2.4, "finS", rows=(7, 13), cols=(4, 8))
    ha = top_z(-340.0)
    K.fin([(-388, ha - 2), (-374, ha + 44), (-336, ha + 44), (-318, ha - 2)], -62.0, 2.0)
    K.fin_plates((-376.0, -330.0, ha + 3.0, ha + 40.0), -62.0, 2.0, "finP", rows=(6, 11), cols=(3.5, 7))
    hb = bottom_z(-220.0)
    K.fin([(-290, hb + 2), (-278, hb - 40), (-196, hb - 40), (-170, hb + 2)], 60.0, 2.4)
    K.fin_plates((-280.0, -190.0, hb - 36.0, hb - 4.0), 60.0, 2.4, "finV", rows=(6, 12), cols=(4, 8))
    if lod <= 1:                                                       # a comb of low spines down the lance's keel
        for i, y in enumerate(np.arange(126.0, 296.0, 15.0)):
            z0 = top_z(y) + 5.0
            h = 7.0 + 6.0 * ((i * 5) % 3) / 2.0
            K.fin([(y, z0), (y + 1.0, z0 + h), (y + 4.0, z0 + h * 0.78), (y + 5.5, z0)], 0.0, 1.0)
    if lod == 0:                                                       # sensor blisters along the shoulders
        for y in np.arange(-380.0, 300.0, 26.0):
            s0 = hull_at(float(y))
            for sx in (1, -1):
                if abs(y - CANNONS["fore"]["y"]) < 36.0 and sx > 0:
                    continue
                K.dome(K_frame_on(np.array([sx * (s0["hw"] - 16.0), float(y) + (5.0 if sx < 0 else 0.0), s0["hh"] + s0["cz"]]), Z, (0, 1, 0)), 2.4)
    # sponsons: stubby armoured blocks jutting from the flanks under the flak batteries, of different lengths and heights, so the long sides have a profile and the guns a seat
    rng_s = K.rng("sponsons")
    for side, ys in ((-1, (-338.0, -150.0, 118.0)), (1, (-322.0, -170.0, 136.0))):
        for i, y in enumerate(ys):
            s0 = hull_at(y)
            ln, w, h = float(rng_s.uniform(30.0, 54.0)), float(rng_s.uniform(14.0, 24.0)), float(rng_s.uniform(14.0, 22.0))
            zc = (30.0 if i % 2 == 0 else 22.0) - h / 2 - 1.0
            fr = Frame((side * (s0["hw"] + w / 2 - 1.5), y, zc), Y, Z)
            ctx.hull.box(fr, (w, ln, h), [PLATE, PLATE_B, PLATE_C][int(rng_s.integers(3))], chamfer=1.6, tile=8.0)
            if lod <= 1:
                ctx.hull.box(Frame((side * (s0["hw"] + w - 1.0), y, zc), Y, Z), (1.2, ln * 0.8, h * 0.8), CHROME, chamfer=0.3, tile=3.0)
            if lod == 0:
                K.strip(np.array([side * (s0["hw"] + w - 0.35), y - ln * 0.4 * (1 if side > 0 else -1), zc - 2.0]), Y * side, X * side, ln * 0.8, "windows_a", lift=0.0, scale=1.6)
    # mast cluster on the lance's keel: tall thin lattices at different heights and a dish on one, the outline-breaking gear of a ship that listens
    if lod <= 1:
        for i, (y, dx, hgt) in enumerate(((176.0, -9.0, 58.0), (204.0, 7.0, 84.0), (236.0, -5.0, 46.0), (268.0, 9.0, 66.0))):
            z0 = top_z(y) + 6.0
            K.lattice([(y - 2.2, z0), (y - 0.9, z0 + hgt), (y + 0.9, z0 + hgt), (y + 2.2, z0)], dx, beam=0.4, braces=3 if hgt > 60 else 2)
            if lod == 0:
                K.pipe(np.array([dx, y, z0 + hgt]), np.array([dx, y, z0 + hgt + 14.0]), 0.2, sides=4)
                K.light(np.array([dx, y, z0 + hgt + 14.5]), Z, LIGHT_RED if i % 2 == 0 else LIGHT_WHITE, 1.8, "nav")
    # a comb of ventral spines down the belly aft of the hangar and a low port sail forward
    if lod <= 1:
        for i, y in enumerate(np.arange(-150.0, 20.0, 17.0)):
            z0 = bottom_z(y) - 4.0
            h = 8.0 + 8.0 * ((i * 7) % 3) / 2.0
            K.fin([(y, z0), (y + 1.2, z0 - h), (y + 5.0, z0 - h * 0.7), (y + 7.0, z0)], 20.0 * (-1 if i % 2 else 1), 1.1)
    hp_ = top_z(60.0)
    K.fin([(30, hp_ - 2), (40, hp_ + 30), (78, hp_ + 30), (92, hp_ - 2)], -44.0, 1.6)
    K.fin_plates((40.0, 80.0, hp_ + 3.0, hp_ + 27.0), -44.0, 1.6, "finP2", rows=(5, 9), cols=(3, 6))
    # the sensor boom to port, forward: a truss with a pod, out past the shoulder
    ba, bb_ = np.array([-60.0, 150.0, 18.0]), np.array([-132.0, 262.0, 26.0])
    K.truss(ba, bb_, 4.0, bay=14.0, rod=0.4)
    ctx.hull.box(Frame((-96.0, 206.0, 22.0), norm(bb_ - ba), Z), (9.0, 22.0, 8.0), PLATE, chamfer=1.4, tile=6.0)

    # --- radiator stacks on the engine block's flanks: louvred plates reaching out sideways, four to port and three to starboard ---
    def radiator_stack(side, zs, y0, y1, reach):
        x0, x1 = side * 56.0, side * (56.0 + reach)
        cx, ln = (x0 + x1) / 2, y1 - y0
        for z in zs:
            ctx.hull.box(Frame((cx, (y0 + y1) / 2, z), Y, Z), (reach, ln, 0.9), PLATE, chamfer=0.25, tile=8.0)
            if lod == 0:
                xa, xb = min(x0, x1), max(x0, x1)
                K.tile_piece("radiator", (xa, y0, z + 0.5), X, Y, xb - xa, ln)
                K.tile_piece("radiator", (xa, y0, z - 0.5), Y, X, ln, xb - xa)
        K.pipe((x1, y0 + 1.5, min(zs) - 1.0), (x1, y0 + 1.5, max(zs) + 1.0), 0.7, sides=6 if lod == 0 else 4)
        K.pipe((x1, y1 - 1.5, min(zs) - 1.0), (x1, y1 - 1.5, max(zs) + 1.0), 0.7, sides=6 if lod == 0 else 4)
        for y in (y0 + 8.0, (y0 + y1) / 2, y1 - 8.0):
            K.pipe((x0, y, zs[0]), (x1, y, zs[0]), 0.5, sides=4)
            K.pipe((x0, y, zs[-1]), (x1, y, zs[-1]), 0.5, sides=4)
    radiator_stack(-1, [-26.0, -9.0, 9.0, 26.0], -494.0, -430.0, 38.0)
    radiator_stack(1, [-18.0, 0.0, 18.0], -490.0, -436.0, 30.0)

    # --- the prow: a fork of two long armoured blades, a central sensor spike between them and a lit halo behind ---
    if lod <= 1:
        for sx in (-1, 1):
            d = norm(np.array([sx * 0.30, 1.0, 0.03]))
            base = np.array([sx * 24.0, 440.0, 2.0])
            bf = Frame(base, d, Z)
            ctx.hull.extrude(chamfered_rect(14.0, 6.5, 2.4), bf, 0.0, 128.0, PLATE_B, s1=(0.22, 0.34), tile=6.0, caps=(False, True), cap_mat=CHROME)
            for dz in (-1, 1):
                K.pipe(base + Z * dz * 6.5, base + d * 128.0 + Z * dz * 2.2, 0.8, sides=4, caps=False)
            for t in (0.25, 0.5, 0.75):
                q = base + d * 128.0 * t
                ctx.hull.box(Frame(q + np.array([sx * 3.0, 0, 0]), d, Z), (2.2, 10.0, 11.0 * (1 - 0.7 * t)), CHROME, chamfer=0.4, tile=3.0)
        ctx.hull.extrude(regular_poly(8, 6.5), IDENT, 494.0, 590.0, CHROME, s1=(0.12, 0.12), tile=4.0, caps=(False, True), cap_mat=CHROME)
        K.ring(498, 506, (34.0, 28.0, 8.0), inner=(21.0, 17.0, 6.0))
    if lod == 0:
        K.tile_piece("heat_tiles", (-52.0, -500.05, -37.0), X, Z, 104.0, 74.0)

    # ============================================================================================ subsystems
    # three engines: port, starboard and one above, set in the engine block's aft face
    for nm, x, z in (("P", -33.0, -4.0), ("S", 33.0, -4.0), ("D", 0.0, 20.0)):
        fr = Frame((x, -500.0, z), (0, -1, 0), Z)
        with ctx.subsystem(nm, "engine", fr, radius=24.0, hp=1.0, tags=["propulsion"]) as sb:
            mnparts.engine_nozzle(K, sb, fr, f"eng{nm}", throat=5.4, exit_r=12.5, length=27.0, s=3.3, bell_segs=56 if lod == 0 else 24, bell_n=12, plume_shells=8, plume_segs=22)
    if lod <= 1:
        ctx.hull.box(Frame((0, -501.4, 0), Y, Z), (112.0, 3.0, 3.2), CHROME, tile=3.0, chamfer=0.5)
        ctx.hull.box(Frame((0, -501.4, 0), Y, Z), (3.2, 3.0, 84.0), CHROME, tile=3.0, chamfer=0.5)

    # flak batteries along the shoulders: three a side, staggered
    for side, tag in ((-1, "P"), (1, "S")):
        for i, y in enumerate((-338.0 if side < 0 else -322.0, -150.0 if side < 0 else -170.0, 118.0 if side < 0 else 136.0)):
            p = flank_pt(y, 30.0 if i % 2 == 0 else 22.0, side, lift=0.05)
            fr = K.frame_on(p, X * side, (0, 1, 0.12))
            with ctx.subsystem(f"{tag}{i + 1:02d}", "flak", fr, radius=7.0, tags=["anti-fighter", "flak"]) as sb:
                mnparts.gun_turret(K, sb, fr, f"flak{tag}{i}", s=2.3, barrels=2, barrel_len=4.4)
    # anti-fighter guns: three on top (staggered), three on the belly
    aaf = [("D01", top_pt(-346.0, -40.0, 0.05), Z), ("D02", top_pt(-18.0, 40.0, 0.05), Z), ("D03", top_pt(150.0, -26.0, 0.05), Z),
           ("V01", bottom_pt(-340.0, 44.0, 0.05), -Z), ("V02", bottom_pt(-70.0, -46.0, 0.05), -Z), ("V03", bottom_pt(288.0, 20.0, 0.05), -Z)]
    for nm, p, n in aaf:
        fr = K.frame_on(p, n, (0, 1, 0))
        with ctx.subsystem(nm, "aaf", fr, radius=8.0, hp=1.0, tags=["anti-fighter", "laser"]) as sb:
            mnparts.gun_turret(K, sb, fr, f"aaf{nm}", s=2.7, barrels=3, barrel_len=4.8)

    # the two heavy beam cannons
    for nm, c in CANNONS.items():
        fr = cannon_frame(c)
        mz = mnheavy.muzzle_frame(fr, BEAM_LEN, BEAM_R, c["pitch"])
        with ctx.subsystem(nm, "turret", mz, radius=40.0, hp=1.0, tags=["beam", "anti-capital"]) as sb:
            mnheavy.beam_cannon(K, sb, fr, f"beam{nm}", length=BEAM_LEN, r=BEAM_R, pitch=c["pitch"])

    # the chin hangar, hung under the lance; its part sits in the open face and points out of it (down), where the bombers come out
    hy = 152.0
    hfr = K.frame_on(bottom_pt(hy, 0.0, 0.0), -Z, (0, 1, 0))
    ph = 24.0
    opening = Frame(hfr.p(0, 0, ph), hfr.u, hfr.f)
    with ctx.subsystem("main", "hangar", opening, radius=52.0, hp=1.0, tags=["hangar"]) as sb:
        mnheavy.chin_hangar(K, sb, hfr, "chin", w=62.0, l=118.0, h=ph)

    # the bridge on the tower's top, the sensor dish on the prow, the sensor panel on the tower, the comms mast and the reactor stacks aft
    btop = TOWER_Z0 + 24 + 28
    bfr = Frame(((TOWER_Y0 + TOWER_Y1) / 2 * 0.0, (TOWER_Y0 + TOWER_Y1) / 2, btop), Y, Z)
    with ctx.subsystem("main", "bridge", bfr, radius=26.0, hp=1.0, tags=["command"]) as sb:
        mnsystems.bridge_block(K, sb, bfr, "bridge", w=52.0, l=72.0, h=17.0)
    pfr = Frame((36.6, (TOWER_Y0 + TOWER_Y1) / 2, TOWER_Z0 + 22), Z, X)          # local y = up the panel, local z = out of its starboard face
    with ctx.subsystem("tower", "sensors", pfr, radius=19.0, tags=["radar"]) as sb:
        mnsystems.sensor_panel(K, sb, pfr, "towerpanel", w=50.0, h=24.0)
    dfr = Frame(top_pt(404.0, -18.0, 0.0) + np.array([0, 0, 4.0]), (0.0, 1.0, 0.35), Z)
    with ctx.subsystem("prow", "sensors", dfr, radius=16.0, tags=["radar", "long-range"]) as sb:
        mnsystems.sensor_dish(K, sb, dfr, "dish", r=14.0)
    mfr = K.frame_on(top_pt(-372.0, 30.0, 0.05), Z, (0, 1, 0))
    with ctx.subsystem("main", "comms", mfr, radius=9.0, tags=["comms"]) as sb:
        mnsystems.comms_mast(K, sb, mfr, "mast", height=70.0)
    rfr = K.frame_on(top_pt(-446.0, 0.0, 0.05), Z, (0, 1, 0))
    with ctx.subsystem("main", "reactor", rfr, radius=19.0, hp=1.0, tags=["power"]) as sb:
        mnsystems.reactor_stacks(K, sb, rfr, "reactor", n=4, spacing=11.0, radius=3.4, height=14.0)

    # ============================================================================================ details that make the scale
    if lod <= 1:
        K.light(np.array([-20.0, 592.0, 2.0]), (-1, 0.2, 0), LIGHT_RED, 2.4, "nav")
        K.light(np.array([20.0, 592.0, 2.0]), (1, 0.2, 0), LIGHT_GREEN, 2.4, "nav")
        K.light(np.array([0.0, (TOWER_Y0 + TOWER_Y1) / 2, btop + 18.0]), Z, LIGHT_RED, 3.0, "nav")
        K.light(np.array([0.0, 600.0, 6.0]), Y, LIGHT_WHITE, 2.8, "strobe", blink="strobe")
        K.light(np.array([70.0, -112.0, ht + 118.5]), Z, LIGHT_WHITE, 2.4, "strobe", blink="strobe")
        K.light(np.array([-62.0, -350.0, ha + 44.0]), Z, LIGHT_RED, 2.4, "nav")
        K.light(np.array([60.0, -240.0, hb - 41.0]), -Z, LIGHT_WHITE, 2.8, "strobe", blink="strobe")
        for y in np.arange(-380.0, 330.0, 40.0):
            for sx in (-1, 1):
                s0 = hull_at(float(y))
                K.light(np.array([sx * (s0["hw"] - 10.0), float(y), s0["hh"] + s0["cz"] + 0.1]), Z, LIGHT_COLD, 1.5, "running")
        for x in (-48.0, -24.0, 24.0, 48.0):
            K.light(np.array([x, -501.5, 34.0]), -Y, LIGHT_COLD, 1.8, "running")
            K.light(np.array([x, -501.5, -34.0]), -Y, LIGHT_COLD, 1.8, "running")
    if lod == 0:
        # markings: the hull code on a pad on the starboard belt, Earth's mark on the mesa
        pad_y = -230.0
        s0 = hull_at(pad_y)
        K.slab_box(Frame((s0["hw"] + 3.6, pad_y, 8.0), Y, Z), (0.8, 30.0, 10.0), mat=PLATE_C, chamfer=0.15, tile=8.0)
        K.paint_quad("hull_code", (s0["hw"] + 4.05, pad_y, 8.0), Y, X, lift=0.02, scale=2.6)
        K.slab_box(Frame((0, -70.0, top_z(-70.0) + 2 * MESA_HH - 2.2), Y, Z), (28.0, 28.0, 0.6), mat=PLATE_C, chamfer=0.15, tile=8.0)
        K.paint_quad("emblem", (0, -70.0, top_z(-70.0) + 2 * MESA_HH - 1.8), -Y, Z, lift=0.02, scale=2.6)

    # --- the collision proxy: the hull and the big appendages as coarse boxes ---
    if lod == 0:
        K.loft(HULL, mb=ship.collision)
        K.loft(tower, mb=ship.collision)
        K.loft([st(MESA_Y0, MESA_HW * 0.5, MESA_HH, 3.0, 0, top_z(MESA_Y0) + MESA_HH - 3.0), st(MESA_Y1, MESA_HW * 0.5, MESA_HH, 3.0, 0, top_z(MESA_Y1) + MESA_HH - 3.0)], mb=ship.collision)
    return K


def hull_extras(ship):
    """Damage sites, FX hooks and sampled hull points for scorch decals, all on the real hull."""
    K = Kit(ship.lods[0], TRIMS, SEED)
    sites = [
        ("citadel-top-s", top_pt(-250.0, 52.0, 0.06), Z, 11.0), ("citadel-flank-s", flank_pt(-220.0, 20.0, 1, 0.06), X, 12.0), ("citadel-flank-p", flank_pt(-180.0, 12.0, -1, 0.06), -X, 11.0),
        ("aft-top", top_pt(-360.0, -22.0, 0.06), Z, 11.0), ("aft-flank", flank_pt(-320.0, 18.0, 1, 0.06), X, 10.0), ("lance-top", top_pt(110.0, 8.0, 0.06), Z, 11.0),
        ("lance-flank", flank_pt(210.0, 16.0, -1, 0.06), -X, 10.0), ("belly", bottom_pt(-150.0, -30.0, 0.06), -Z, 11.0), ("prow-top", top_pt(380.0, -12.0, 0.06), Z, 8.0),
        ("tower", np.array([36.1, -216.0, TOWER_Z0 + 22.0]), X, 8.0),
    ]
    for name, p, n, r in sites:
        site_fr = Frame(p, n, _perp(n))
        build_fr = Frame(p, _perp(n), n)
        ship.add_site(site_fr, r, mnparts.breach(K, build_fr, r, name))
        ship.add_hook("spark", Frame(p + norm(n) * 0.9, n, _perp(n)), 1.8)
        ship.add_hook("vent", Frame(p + norm(n) * 0.6 + _perp(n) * r * 0.5, n, _perp(n)), 3.4)
    for x, z in ((-33.0, -4.0), (33.0, -4.0), (0.0, 20.0)):
        ship.add_hook("fire", Frame((x, -502.0, z), -Y, Z), 6.0)
    for i in range(4):
        ship.add_hook("vent", Frame(top_pt(-446.0, (i - 1.5) * 11.0, 14.0), Z, Y), 4.0)
    return sites


def _perp(n):
    n = norm(n)
    a = Z if abs(n[2]) < 0.9 else X
    return norm(cross(a, n))


def damage_points(count=96):
    """Points on the hull flats, spread out, for the progressive scorch plan: (position, outward normal) in ship space."""
    rng = np.random.default_rng(SEED + 7)
    pts = []
    for a, b in zip(HULL[:-1], HULL[1:]):
        if b["y"] - a["y"] < 20:
            continue
        pa = chamfered_rect(a["hw"], a["hh"], a["c"])
        pb = chamfered_rect(b["hw"], b["hh"], b["c"])
        for e in (0, 1, 2, 5, 6, 7, 3, 4):
            p00 = np.array([pa[e][0], a["y"], pa[e][1]])
            p10 = np.array([pa[(e + 1) % 8][0], a["y"], pa[(e + 1) % 8][1]])
            p11 = np.array([pb[(e + 1) % 8][0], b["y"], pb[(e + 1) % 8][1]])
            p01 = np.array([pb[e][0], b["y"], pb[e][1]])
            patch = mnmesh.Patch(p00, p10, p11, p01)
            area = patch.lu * patch.lv
            for _ in range(max(1, int(area / 700))):
                u, v = rng.uniform(0.15, 0.85, 2)
                pts.append((patch.pt(u, v), patch.normal(), area))
    idx = rng.permutation(len(pts))
    chosen = []
    for i in idx:
        p = pts[i][0]
        if all(np.linalg.norm(p - c[0]) > 26.0 for c in chosen):
            chosen.append(pts[i])
        if len(chosen) >= count:
            break
    return [{"p": mnship.to_unity(p), "n": mnship.to_unity(n)} for p, n, _ in chosen]


def main():
    preview = "--preview" in sys.argv
    ship = mnship.Ship(SHIP_ID, "Hecate-class destroyer", ART, MATERIALS, seed=SEED, manifest_dir=RES, material_set=MATERIAL_SET)
    for lod in (0, 1, 2):
        ship.build_lod(lod, build)
    hull_extras(ship)
    ship.extra["damagePoints"] = damage_points()
    ship.extra["lodScreenHeights"] = [0.12, 0.035, 0.01]
    ship.extra["engineLights"] = {f"SUB_engine_{nm}": {"z": 14.0, "candela": 4.0e6, "range": 220.0, "radius": 1.2} for nm in ("P", "S", "D")}
    stats = ship.export()
    print(f"[MN] bounds {stats['bounds']}")
    if preview:
        out = os.environ.get("MN_PREVIEW_DIR", os.path.join(ROOT, "art_src", "oort_art", "_preview", "destroyer"))
        mnpreview.render_views(out, [
            ("side", (1500, -40, 60), (0, 40, 30), 36),
            ("three_quarter", (900, -1100, 460), (0, 0, 10), 38),
            ("three_quarter_bow", (800, 1000, 380), (0, 40, 10), 38),
            ("top", (0, 20, 1500), (0, 20, 0), 38),
            ("aft", (160, -1000, 100), (0, -450, 10), 38),
            ("bow", (200, 1000, 80), (0, 400, 0), 40),
            ("below", (500, 100, -900), (0, 100, -40), 40),
            ("beam_close", (200, 120, 170), (10, 280, 80), 45),
            ("bridge_close", (170, -480, 190), (0, -215, 100), 45),
            ("hangar_close", (150, 90, -170), (0, 152, -70), 48),
            ("beam_aft", (180, -300, -170), (-24, -190, -80), 45),
        ], clip=(1.0, 8000.0))
        mnpreview.render_views(out + "_wreck", [("wreck_three", (900, -1100, 460), (0, 0, 10), 38), ("wreck_beam", (200, 120, 170), (10, 280, 80), 45)],
                               show=("__destroyed", "__debris", "__breach"), clip=(1.0, 8000.0))


main()
