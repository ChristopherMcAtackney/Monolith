"""Earth's bomber (the Censer class), 20 m long: a catamaran with a swinging load. Two long plated booms (layered segments, necks and chrome collars) with an engine at
the tail of each, a slim central gondola that runs on ahead of them to a sensor head, joined by plated bridges; broad forward wings that carry the needle guns, a
tall dorsal fin on the gondola and a wide tail brace; and slung beneath the gondola on pylons, the load: a cradle of four heavy torpedoes. The starboard boom
carries a dish and the port boom is shorter (asymmetric); a comms mast stands on the gondola's back. Engines, the guns, the sensor eye, the comms array and the
torpedo cradle are subsystems with wrecks and debris.

    tools\\blender.ps1 art_src\\earth_fighters\\build_earth_bomber.py [-- --preview]

Axes while building: x right (starboard), y forward (the nose), z up.
"""
import json
import math
import os
import sys

import numpy as np

from blender_lib import mnmesh, mnship, mnkit, mnparts, mnpreview, mnfighter as F
from blender_lib.mnkit import Kit, station as st, PLATE, TRIM, LIGHTS, ENGINE, CHROME, PLATE_B, PLATE_C, PLUME, PLATE_D, PLATE_E, WEAR, PAINT
from blender_lib.mnkit import LIGHT_WHITE, LIGHT_COLD, LIGHT_RED, LIGHT_GREEN, LIGHT_AMBER
from blender_lib.mnmesh import Frame, norm, cross
from blender_lib.mnfighter import X, Y, Z

ROOT = os.environ.get("MN_ROOT", os.getcwd())
ART = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "EarthFighters")
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "ShipArt")
TRIMS = json.load(open(os.path.join(ROOT, "art_src", "earth_hull", "trims.json")))
MATERIAL_SET = "Assets/Monolith/Art/EarthHull/earth_hull.materials.json"
SHIP_ID = "earth_bomber"
SEED = 4421
BX = 3.1                                  # the booms' centre line
PLATES = dict(F.PLATING, rows=(0.7, 2.4), cols=(0.5, 1.7))


def P(x, y, z):
    return np.array([x, y, z], float)


def boom_segments(side):
    cx = side * BX
    nose = 7.3 if side > 0 else 6.7                       # the port boom is shorter
    return {
        "eng": [st(-8.4, 0.92, 0.84, 0.30, cx, 0), st(-5.7, 1.24, 1.10, 0.36, cx, 0)],
        "main": [st(-5.4, 1.30, 1.15, 0.40, cx, 0), st(0.4, 1.38, 1.20, 0.42, cx, 0), st(3.6, 1.30, 1.12, 0.40, cx, 0)],
        "fore": [st(3.9, 1.14, 0.98, 0.36, cx, 0), st(nose - 1.6, 0.96, 0.84, 0.32, cx, 0), st(nose, 0.5, 0.44, 0.18, cx, 0)],
    }, [[st(-5.7, 0.96, 0.86, 0.3, cx, 0), st(-5.4, 0.96, 0.86, 0.3, cx, 0)], [st(3.6, 0.95, 0.84, 0.3, cx, 0), st(3.9, 0.95, 0.84, 0.3, cx, 0)]], nose


GONDOLA = {
    "aft": [st(-6.2, 0.62, 0.56, 0.2), st(-3.2, 0.95, 0.9, 0.3)],
    "mid": [st(-2.9, 1.02, 1.0, 0.34), st(2.2, 1.12, 1.18, 0.4), st(5.0, 0.92, 0.9, 0.32)],
    "head": [st(5.3, 0.78, 0.72, 0.28), st(7.8, 0.6, 0.54, 0.22), st(9.4, 0.26, 0.24, 0.1)],
}
G_NECKS = [[st(-3.2, 0.7, 0.66, 0.24), st(-2.9, 0.7, 0.66, 0.24)], [st(5.0, 0.62, 0.58, 0.22), st(5.3, 0.62, 0.58, 0.22)]]


def build(ctx):
    F.dark_paint()
    K = Kit(ctx, TRIMS, SEED)
    K.surfaces = []
    lod = ctx.lod
    hull = ctx.hull
    ship = ctx.ship

    # --- the booms: plated segments, necks, collars ---
    for side in (1, -1):
        segs, necks, nose = boom_segments(side)
        tag = "S" if side > 0 else "P"
        for name, sts in segs.items():
            F.hull(K, hull, sts, name + tag, plating=PLATES, rail_r=0.026)
        for sts in necks:
            K.loft(sts, mb=hull)
        cx = side * BX
        F.collar(K, -5.7, -5.4, (1.34, 1.18, 0.38), inner=(0.96, 0.86, 0.3), bevel=0.025, centre=(cx, 0))
        F.collar(K, 3.6, 3.9, (1.26, 1.1, 0.36), inner=(0.95, 0.84, 0.3), bevel=0.025, centre=(cx, 0))
        F.collar(K, -8.5, -8.4, (0.98, 0.9, 0.3), inner=None, bevel=0.02, centre=(cx, 0))
        F.taper(K, hull, P(cx, nose, 0), P(cx, nose + 1.1, 0), 0.15, 0.015, sides=6, mat=CHROME)
    # --- the gondola and its sensor head ---
    for name, sts in GONDOLA.items():
        F.hull(K, hull, sts, "g" + name, plating=PLATES, rail_r=0.024)
    for sts in G_NECKS:
        K.loft(sts, mb=hull)
    F.collar(K, -3.2, -2.9, (1.08, 1.04, 0.36), inner=(0.7, 0.66, 0.24), bevel=0.025)
    F.collar(K, 5.0, 5.3, (0.96, 0.94, 0.34), inner=(0.62, 0.58, 0.22), bevel=0.025)
    F.taper(K, hull, P(0, 9.3, 0), P(0, 10.1, 0), 0.09, 0.012, sides=6, mat=CHROME)
    F.band(K, hull, P(0, 8.0, 0), Y, 0.66, 0.07, sides=8)
    F.band(K, hull, P(0, 8.9, 0), Y, 0.4, 0.05, sides=8)

    # --- the bridges between the gondola and the booms, the tail brace, and the broad forward wings ---
    for y0, y1, z, th in ((2.4, 4.9, 0.0, 0.32), (-2.4, -0.2, 0.0, 0.28), (-7.4, -6.2, 0.1, 0.24)):
        for side in (1, -1):
            F.solid(K, hull, [P(side * 0.9, y0, z), P(side * 2.1, y0, z), P(side * 2.1, y1, z), P(side * 0.9, y1, z)], th, f"bridge{y0}{side}", bevel=0.03, normal=Z, plating=PLATES, lod2=False)
    for side in (1, -1):
        sg = side
        F.solid(K, hull, [P(sg * 4.15, 1.0, 0.0), P(sg * 7.55, 2.1, -0.2), P(sg * 7.55, 5.1, -0.2), P(sg * 4.15, 5.7, 0.0)], 0.34, "wing" + str(side), bevel=0.045, normal=Z, plating=PLATES)
        F.solid(K, hull, [P(sg * 4.8, 1.8, 0.2), P(sg * 6.9, 2.6, 0.12), P(sg * 6.9, 4.5, 0.12), P(sg * 4.8, 4.9, 0.2)], 0.12, "wingcap" + str(side), bevel=0.02, normal=Z, plating=dict(PLATES, rows=(0.4, 1.0), cols=(0.35, 0.9)))
    F.solid(K, hull, [P(7.55, 2.3, -0.9), P(7.55, 4.8, -0.9), P(7.55, 4.3, 1.5), P(7.55, 2.7, 1.5)], 0.14, "tipfin", bevel=0.025, normal=X, chrome_edges=(2,))          # a tall fin on the starboard wing only
    F.hoop(K, hull, P(-7.2, 3.4, 0.2), Y, 0.78, 0.54, 0.16, 10, "porthoop", plating=None, bevel=0.02, chrome_rim=True) if lod <= 1 else None          # a sensor ring on the port wingtip
    # tail brace and fins
    F.solid(K, hull, [P(-4.7, -7.6, 0.5), P(4.7, -7.6, 0.5), P(4.7, -6.5, 0.5), P(-4.7, -6.5, 0.5)], 0.14, "tailbrace", bevel=0.025, normal=Z, plating=PLATES, lod2=False)
    F.solid(K, hull, [P(0.0, -3.5, 0.9), P(0.0, -6.0, 0.55), P(-0.45, -6.6, 2.9), P(-0.45, -4.5, 2.6)], 0.1, "dorsalfin", bevel=0.02, chrome_edges=(2,))
    F.solid(K, hull, [P(0.0, -3.4, -0.9), P(0.0, -5.6, -0.5), P(0.35, -5.8, -1.8), P(0.35, -4.0, -1.7)], 0.08, "ventralfin", bevel=0.02)
    for side in (1, -1):                                              # small fins on the booms' backs
        F.solid(K, hull, [P(side * BX, -4.9, 1.1), P(side * BX, -7.3, 0.9), P(side * BX, -7.3, 1.8), P(side * BX, -5.3, 2.0)], 0.07, f"boomfin{side}", bevel=0.015, normal=X * side, plating=None, lod2=False)

    # --- the load: pylons from the gondola's belly and the torpedo cradle's collars (the torpedoes are the rack subsystem) ---
    for y in (-0.6, 2.2):
        hull.box(Frame(P(0, y, -1.3), Y, Z), (0.26, 0.5, 0.7), PLATE, chamfer=0.04, tile=3.0)
    for side in (1, -1):
        F.taper(K, hull, P(side * 0.7, -0.4, -1.1), P(side * 0.7, 2.0, -1.1), 0.06, 0.06, sides=4, mat=CHROME)

    # --- hardware: a dish boom on the starboard boom, antennas, a long probe from the wing ---
    F.taper(K, hull, P(BX + 1.1, 5.0, 0.3), P(BX + 2.5, 6.2, 0.9), 0.07, 0.05, sides=6, mat=CHROME)
    if lod <= 1:
        hull.lathe([(0.0, 0.0), (0.28, 0.06), (0.46, 0.18)], Frame(P(BX + 2.5, 6.2, 0.9), norm(P(0.5, 0.35, 0.8)), Z), CHROME, segs=12, tile=2.0)
    F.whip(K, hull, P(-0.3, -2.0, 1.15), P(-0.4, -2.3, 3.4), 0.016)
    F.whip(K, hull, P(BX, -7.0, 1.4), P(BX + 0.2, -7.6, 3.0), 0.013)
    F.whip(K, hull, P(-BX, -6.4, 1.3), P(-BX - 0.2, -6.7, 2.2), 0.012)
    F.whip(K, hull, P(7.55, 4.9, -0.2), P(7.6, 6.3, -0.2), 0.014)
    F.whip(K, hull, P(-7.55, 4.9, -0.2), P(-7.6, 5.6, -0.2), 0.012)

    if lod == 0:
        for (x, y, z, sc, nm) in ((0.0, 0.2, 1.185, 0.22, "hatch_a"), (0.0, -1.5, 1.0, 0.18, "vent"), (BX, -2.0, 1.22, 0.2, "hatch_b"), (-BX, 1.0, 1.22, 0.2, "hatch_a"), (BX, 2.0, 1.22, 0.18, "vent")):
            F.hatch(K, P(x, y, z), Y, Z, scale=sc, name=nm, lift=0.045)
        for side in (1, -1):
            K.strip(P(side * (BX + 1.405), -4.0, -0.05), Y, X * side, 4.0, "light_bar", lift=0.0, scale=0.32)
        K.paint_quad("code_2", P(BX + 1.42, -1.8, 0.25), Y, X, size=(1.2, 0.41), lift=0.0)
        K.paint_quad("chevron_0", P(5.6, 3.5, 0.19), -X, Z, size=(2.6, 0.52), lift=0.0)
        K.paint_quad("chevron_1", P(-5.6, 3.5, 0.19), X, Z, size=(2.6, 0.52), lift=0.0)
        K.paint_quad("emblem", P(0.0, 0.2, 1.185 + 0.02), Y, Z, size=(0.9, 0.9), lift=0.0)
        for side in (1, -1):
            K.wear_quad("scorch_2" if side > 0 else "scorch_0", P(side * 5.22, 5.0, -0.4), Y, Z, size=(1.0, 2.0), lift=0.0)
        K.wear_quad("streak_1", P(BX, 0.4, 1.205), Y, Z, size=(4.6, 0.6), lift=0.0)

    # ============================================================================================ subsystems
    for side, nm in ((-1, "P"), (1, "S")):
        fr = Frame(P(side * BX, -8.2, 0.0), -Y, Z)
        with ctx.subsystem(nm, "engine", fr, radius=2.64, hp=1.0, tags=["propulsion"]) as sb:
            F.fighter_engine(K, sb, fr, f"eng{nm}", throat=0.38, exit_r=0.8, length=1.5, s=0.4)
    for side, nm in ((-1, "P"), (1, "S")):
        fr = Frame(P(side * 5.22, 6.24 - 1.46, -0.35), Y, Z)
        with ctx.subsystem(nm, "mount", fr, radius=2.2, hp=0.5, tags=["gun:primary", "weapon"]) as sb:
            F.needle_gun(K, sb, fr, f"needle{nm}", length=1.5, r=0.075)
    fr = Frame(P(0, 2.9, 1.22), Y, Z)
    with ctx.subsystem("eye", "sensors", fr, radius=2.34, hp=1.0, tags=["radar"]) as sb:
        F.sensor_eye(K, sb, fr, "eye", r=0.4, tall=0.46)
    fr = Frame(P(0, -4.8, 0.95), Y, Z)
    with ctx.subsystem("mast", "comms", fr, radius=1.8, hp=1.0, tags=["comms"]) as sb:
        F.comms_array(K, sb, fr, "mast", height=1.9, r=0.36)
    fr = Frame(P(0, 0.4, -2.0), Y, -Z)
    with ctx.subsystem("cradle", "launcher", fr, radius=2.4, hp=1.0, tags=["missile", "torpedo"]) as sb:
        F.missile_rack(K, sb, fr, "cradle", cols=2, rows=2, r=0.3, length=5.4, spacing=0.74, torpedo=True)

    # ============================================================================================ lights: harsh white
    if lod <= 1:
        F.light(K, P(7.62, 4.6, 1.45), X, LIGHT_WHITE, 0.16, "strobe", blink="strobe")
        F.light(K, P(-7.0, 3.4, 0.7), -X, LIGHT_WHITE, 0.14, "strobe", blink="strobe")
        for sx in (-1, 1):
            F.light(K, P(sx * 7.6, 2.3, -0.25), (sx, 0, 0), LIGHT_WHITE, 0.12, "running")
            F.light(K, P(sx * BX, 7.0 if sx > 0 else 6.4, 0.0), Y, LIGHT_WHITE, 0.09, "running")
            F.light(K, P(sx * 4.6, -7.55, 0.6), (sx, -0.2, 0.5), LIGHT_WHITE, 0.12, "running")
            F.light(K, P(sx * BX, -4.8, 1.24), Z, LIGHT_WHITE, 0.09, "running")
        F.light(K, P(-0.45, -6.5, 2.92), Z, LIGHT_WHITE, 0.14, "strobe", blink="strobe")
        F.light(K, P(0.0, 9.0, 0.0), Y, LIGHT_WHITE, 0.08, "running")
        F.light(K, P(0.35, -5.5, -1.82), -Z, LIGHT_RED, 0.12, "strobe", blink="strobe")
        F.light(K, P(0.0, 0.4, -2.85), -Z, LIGHT_WHITE, 0.1, "running")

    if lod == 0:
        for side in (1, -1):
            segs, _, _ = boom_segments(side)
            for sts in segs.values():
                K.loft(sts, mb=ship.collision)
        for name, sts in GONDOLA.items():
            K.loft(sts, mb=ship.collision)
        for side in (1, -1):
            F.solid(K, ship.collision, [P(side * 4.15, 1.0, 0.0), P(side * 7.55, 2.1, -0.2), P(side * 7.55, 5.1, -0.2), P(side * 4.15, 5.7, 0.0)], 0.34, "colwing", plating=None, normal=Z)
    return K


def main():
    preview = "--preview" in sys.argv
    ship = mnship.Ship(SHIP_ID, "Censer bomber", ART, ["mn_earth_plate", "mn_earth_trim", "mn_earth_lights", "mn_earth_engine", "mn_earth_chrome", "mn_earth_plate_b", "mn_earth_plate_c", "mn_earth_plume", "mn_earth_plate_d", "mn_earth_plate_e", "mn_earth_wear", "mn_earth_paint"], seed=SEED,
                       manifest_dir=RES, material_set=MATERIAL_SET)
    kits = {}
    for lod in (0, 1, 2):
        ship.build_lod(lod, lambda ctx: kits.__setitem__(ctx.lod, build(ctx)))
    K0 = kits[0]
    sites = [("boom-stbd", P(BX, -1.5, 1.22), Z, 0.6), ("boom-port", P(-BX, 0.8, 1.22), Z, 0.6), ("gondola", P(0.0, -1.2, 1.0), Z, 0.5),
             ("wing-stbd", P(5.6, 3.4, 0.18), Z, 0.6), ("wing-port", P(-5.8, 3.0, 0.18), Z, 0.55), ("flank", P(BX + 1.38, 1.5, 0.0), X, 0.5), ("belly", P(0.0, -1.6, -0.95), -Z, 0.5)]
    for name, p, n, r in sites:
        F.add_site(K0, ship, name, p, n, r)
    for side in (1, -1):
        ship.add_hook("fire", Frame(P(side * BX, -8.3, 0.0), -Y, Z), 1.2)
    ship.add_hook("vent", Frame(P(0.0, 1.0, 1.2), Z, Y), 0.9)
    ship.extra["damagePoints"] = F.damage_points(K0.surfaces, 44, SEED)
    ship.extra["lodScreenHeights"] = [0.10, 0.03, 0.006]
    ship.extra["scorchScale"] = 0.17          # hit radii are set for capital hulls; a fighter's scorch is this fraction of that
    ship.extra["engineFollowsShip"] = True    # the glow follows the ship's throttle and afterburner in a fight
    ship.extra["engineLights"] = {"SUB_engine_P": {"z": 1.0, "candela": 24000.0, "range": 26.0, "fade": 180.0}, "SUB_engine_S": {"z": 1.0, "candela": 24000.0, "range": 26.0, "fade": 180.0}}
    stats = ship.export()
    print(f"[MN] bounds {stats['bounds']}")
    if preview:
        out = os.environ.get("MN_PREVIEW_DIR", os.path.join(ROOT, "art_src", "earth_fighters", "_preview", SHIP_ID))
        mnpreview.render_views(out, [
            ("three_quarter", (19, -24, 11), (0, 0, 0), 40),
            ("side", (42, 0, 0.5), (0, 0, 0), 40),
            ("top", (0, 0, 42), (0, 0, 0), 40),
            ("front", (0, 38, 6), (0, 0, 0), 40),
            ("aft", (12, -26, 6), (0, -4, 0), 40),
            ("close", (11, 10, 5), (1, 3, -0.5), 40),
        ], size=(1280, 720))
        mnpreview.render_views(out + "_wreck", [("wreck_aft", (10, -18, 6), (0, -5, 0), 40), ("wreck_front", (12, 13, 4), (2, 4, -0.5), 40)], size=(1280, 720),
                               show=("__destroyed", "__debris", "__breach"))


main()
