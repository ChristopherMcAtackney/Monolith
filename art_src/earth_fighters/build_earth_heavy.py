"""Earth's heavy fighter (the Throne class), 14 m long: a wheel. A thick, layered spindle of a hull with a faceted armoured ring standing round it ahead of the
middle (wheels within wheels), a wide plated yoke behind the ring that carries tip fins, twin engine nacelles on its trailing edge and a pair of long gun booms
reaching forward under the leading edge, a chin cannon, a belly missile rack, and a sensor eye on the crown of the ring. A mandible pair at the nose, antennas and
a sensor boom break the outline; the starboard side carries a boom with a dish and the port side a radiator stack (asymmetric). Engines, the two needle mounts, the
chin cannon, the sensor eye and the rack are subsystems with wrecks and debris.

    tools\\blender.ps1 art_src\\earth_fighters\\build_earth_heavy.py [-- --preview]

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
SHIP_ID = "earth_heavy"
SEED = 3317

SEG = {
    "tail": [st(-6.4, 0.95, 0.80, 0.28), st(-4.4, 1.30, 1.05, 0.36)],
    "mid": [st(-4.1, 1.42, 1.14, 0.40), st(0.8, 1.58, 1.22, 0.44), st(3.0, 1.25, 0.98, 0.36)],
    "fore": [st(3.3, 0.98, 0.78, 0.30), st(4.5, 0.80, 0.62, 0.26), st(5.9, 0.34, 0.28, 0.12)],
}
NECKS = [[st(-4.4, 0.98, 0.84, 0.3), st(-4.1, 0.98, 0.84, 0.3)], [st(3.0, 0.9, 0.7, 0.26), st(3.3, 0.9, 0.7, 0.26)]]
PLATES = dict(F.PLATING, rows=(0.6, 2.0), cols=(0.45, 1.5))
RING_Y, RING_R, RING_IN, RING_D = 1.7, 2.55, 2.02, 0.62


def P(x, y, z):
    return np.array([x, y, z], float)


def build(ctx):
    F.dark_paint()
    K = Kit(ctx, TRIMS, SEED)
    K.surfaces = []
    lod = ctx.lod
    hull = ctx.hull
    ship = ctx.ship

    # --- the spindle: three plated segments, recessed necks, chrome collars; a chrome probe and mandibles at the nose ---
    for name, sts in SEG.items():
        F.hull(K, hull, sts, name, plating=PLATES, rail_r=0.024)
    for sts in NECKS:
        K.loft(sts, mb=hull)
    F.collar(K, -4.4, -4.1, (1.5, 1.2, 0.4), inner=(0.98, 0.84, 0.3), bevel=0.025)
    F.collar(K, 3.0, 3.3, (1.3, 1.02, 0.36), inner=(0.9, 0.7, 0.26), bevel=0.025)
    F.collar(K, -6.5, -6.4, (1.0, 0.85, 0.3), inner=None, bevel=0.025)
    F.taper(K, hull, P(0, 5.8, 0), P(0, 7.1, 0), 0.2, 0.014, sides=6, mat=CHROME)
    F.band(K, hull, P(0, 6.2, 0), Y, 0.26, 0.06, sides=8)
    for sx, ln in ((-1, 1.3), (1, 0.95)):                                # the mandibles: a pair of chrome tines either side of the nose, not the same length
        F.taper(K, hull, P(sx * 0.34, 5.2, -0.1), P(sx * 0.78, 5.2 + ln, -0.18), 0.075, 0.016, sides=5, mat=CHROME)

    # --- the wheel: a faceted ring standing round the hull ahead of its middle, on four spokes; sensors sit on its crown ---
    segs = 16 if lod < 2 else 8
    F.hoop(K, hull, P(0, RING_Y, 0), Y, RING_R, RING_IN, RING_D, segs, "ring", plating=dict(PLATES, rows=(0.4, 0.9), cols=(0.3, 0.7), sub_prob=0.0), bevel=0.03)
    for sx in (-1, 1):
        F.solid(K, hull, [P(sx * 1.45, RING_Y, -0.3), P(sx * 1.45, RING_Y, 0.3), P(sx * (RING_IN + 0.05), RING_Y, 0.3), P(sx * (RING_IN + 0.05), RING_Y, -0.3)], 0.2, f"spokeX{sx}", bevel=0.03, plating=None, normal=Y)
    for sz in (-1, 1):
        F.solid(K, hull, [P(-0.34, RING_Y, sz * 1.1), P(0.34, RING_Y, sz * 1.1), P(0.34, RING_Y, sz * (RING_IN + 0.05)), P(-0.34, RING_Y, sz * (RING_IN + 0.05))], 0.2, f"spokeZ{sz}", bevel=0.03, plating=None, normal=Y)
    F.band(K, hull, P(0, RING_Y, 0), Y, RING_IN - 0.02, 0.07, sides=16)           # a chrome lip inside the ring

    # --- the yoke: a wide plated blade behind the ring, tip fins, a second plated layer on top ---
    for side in (1, -1):
        sg = side
        q = [P(sg * 1.4, -2.0, 0.0), P(sg * 6.05, -1.2, -0.1), P(sg * 6.05, 1.7, -0.1), P(sg * 1.4, 0.9, 0.0)]
        F.solid(K, hull, q, 0.30, "yoke" + str(side), bevel=0.04, normal=Z, plating=PLATES)
        F.solid(K, hull, [P(sg * 2.0, -1.6, 0.17), P(sg * 5.2, -1.05, 0.12), P(sg * 5.2, 0.95, 0.12), P(sg * 2.0, 0.55, 0.17)], 0.12, "yokecap" + str(side), bevel=0.02, normal=Z, plating=dict(PLATES, rows=(0.4, 1.0), cols=(0.35, 0.9)))
        F.solid(K, hull, [P(sg * 6.05, -1.3, -0.9), P(sg * 6.05, 1.2, -0.9), P(sg * 6.05, 0.7, 1.2), P(sg * 6.05, -1.0, 1.2)], 0.13, "tipfin" + str(side), bevel=0.025, normal=X * sg, chrome_edges=(2,))
        # a gun boom under the leading edge, tapering forward to the gun
        F.taper(K, hull, P(sg * 4.35, 1.2, -0.3), P(sg * 4.35, 3.1, -0.3), 0.22, 0.17, sides=6, mat=PLATE)
        F.band(K, hull, P(sg * 4.35, 2.3, -0.3), Y, 0.25, 0.05, sides=6)

    # --- the engine nacelles on the yoke's trailing edge: hard hexagonal bodies, chrome collars ---
    for side in (1, -1):
        x = side * 2.55
        F.taper(K, hull, P(x, -1.7, 0.0), P(x, -5.1, 0.0), 0.82, 0.72, sides=6, mat=PLATE, phase=math.pi / 6)
        F.taper(K, hull, P(x, -1.55, 0.0), P(x, -1.7, 0.0), 0.72, 0.82, sides=6, mat=CHROME, phase=math.pi / 6)
        F.band(K, hull, P(x, -2.9, 0.0), Y, 0.86, 0.08, sides=6)
        F.band(K, hull, P(x, -4.2, 0.0), Y, 0.8, 0.06, sides=6)
        F.solid(K, hull, [P(side * 1.55, -3.4, -0.3), P(side * 1.85, -3.4, -0.3), P(side * 1.85, -5.0, -0.3), P(side * 1.55, -5.0, -0.3)], 0.12, f"nstrut{side}", bevel=0.02, normal=Z, plating=None)

    # --- armour skirts on the hull flanks (stepped plates), a dorsal spine, a keel, a port radiator stack, a starboard dish boom, antennas ---
    for side in (1, -1):
        F.solid(K, hull, [P(side * 1.6, -3.6, -0.7), P(side * 1.6, -2.1, -0.7), P(side * 1.6, -2.1, 0.7), P(side * 1.6, -3.6, 0.7)], 0.12, f"skirtA{side}", bevel=0.02, normal=X * side, plating=PLATES)
        F.solid(K, hull, [P(side * 1.64, -0.4, -0.5), P(side * 1.64, 1.2, -0.5), P(side * 1.64, 1.2, 0.8), P(side * 1.64, -0.4, 0.8)], 0.1, f"skirtB{side}", bevel=0.02, normal=X * side, plating=PLATES)
    F.solid(K, hull, [P(-0.2, -4.0, 1.07), P(0.2, -4.0, 1.07), P(0.2, 0.6, 1.26), P(-0.2, 0.6, 1.26)], 0.08, "spine", bevel=0.02, faces=(True, False), chrome_edges=(1, 3), normal=Z)
    F.solid(K, hull, [P(0.0, -3.9, 1.0), P(0.0, -5.6, 0.85), P(-0.4, -5.9, 2.6), P(-0.4, -3.8, 2.4)], 0.09, "tailfin", bevel=0.02, chrome_edges=(2,))
    F.solid(K, hull, [P(0.0, -3.0, -1.15), P(0.0, 0.0, -1.25), P(0.3, -0.3, -1.95), P(0.3, -2.6, -1.9)], 0.08, "keel", bevel=0.02)
    for k in range(3):                                                       # the port radiator stack: three louvres standing on the nacelle's back
        F.solid(K, hull, [P(-2.55 + (k - 1) * 0.28, -4.2, 0.78), P(-2.55 + (k - 1) * 0.28, -2.3, 0.78), P(-2.55 + (k - 1) * 0.28, -2.3, 1.6), P(-2.55 + (k - 1) * 0.28, -4.2, 1.5)], 0.04, f"rad{k}", bevel=0.01, plating=None, normal=X, lod2=False)
    F.taper(K, hull, P(1.6, 0.5, 0.2), P(3.5, 1.1, 0.9), 0.055, 0.04, sides=6, mat=CHROME)
    if lod <= 1:
        hull.lathe([(0.0, 0.0), (0.22, 0.05), (0.36, 0.14)], Frame(P(3.5, 1.1, 0.9), norm(P(0.5, 0.3, 0.8)), Z), CHROME, segs=10, tile=2.0)
    F.whip(K, hull, P(0.3, RING_Y, 2.5), P(0.5, RING_Y - 0.3, 3.5), 0.016)
    F.whip(K, hull, P(-0.55, -3.4, 1.1), P(-0.7, -3.7, 2.1), 0.012)
    F.whip(K, hull, P(6.05, 1.5, 0.9), P(6.12, 2.9, 0.92), 0.013)
    F.whip(K, hull, P(-6.05, 1.2, 0.9), P(-6.1, 2.2, 0.9), 0.012)

    if lod == 0:
        for (x, y, z, sc, nm) in ((0.0, -2.6, 1.185, 0.2, "hatch_a"), (0.9, 0.2, 1.215, 0.16, "hatch_b"), (-0.8, 2.2, 1.0, 0.15, "vent"), (0.0, 4.3, 0.64, 0.12, "hatch_a")):
            F.hatch(K, P(x, y, z), Y, Z, scale=sc, name=nm, lift=0.045)
        K.strip(P(1.595, -3.0, -0.05), Y, X, 3.2, "light_bar", lift=0.0, scale=0.3)
        K.paint_quad("code_5", P(1.585, -1.2, 0.18), Y, X, size=(1.05, 0.36), lift=0.0)
        K.paint_quad("chevron_0", P(4.0, -0.3, 0.19), -X, Z, size=(2.4, 0.5), lift=0.0)
        K.paint_quad("chevron_1", P(-4.0, -0.3, 0.19), X, Z, size=(2.4, 0.5), lift=0.0)
        K.paint_quad("emblem", P(0.0, -3.0, 1.07 + 0.02), Y, Z, size=(0.8, 0.8), lift=0.0)
        for side in (1, -1):
            K.wear_quad("scorch_1" if side > 0 else "scorch_3", P(side * 4.35, 2.7, -0.2), Y, Z, size=(0.9, 1.8), lift=0.0)
        K.wear_quad("streak_3", P(0.0, -1.0, 1.215), Y, Z, size=(4.2, 0.6), lift=0.0)

    # ============================================================================================ subsystems
    for side, nm in ((-1, "P"), (1, "S")):
        fr = Frame(P(side * 2.55, -5.2, 0.0), -Y, Z)
        with ctx.subsystem(nm, "engine", fr, radius=2.25, hp=1.0, tags=["propulsion"]) as sb:
            F.fighter_engine(K, sb, fr, f"eng{nm}", throat=0.3, exit_r=0.6, length=1.2, s=0.3)
    for side, nm in ((-1, "P"), (1, "S")):
        fr = Frame(P(side * 4.35, 4.42 - 1.42, -0.35), Y, Z)
        with ctx.subsystem(nm, "mount", fr, radius=2.0, hp=0.5, tags=["gun:primary", "weapon"]) as sb:
            F.needle_gun(K, sb, fr, f"needle{nm}", length=1.45, r=0.07)
    F.solid(K, hull, [P(-0.09, 3.2, -0.7), P(0.09, 3.2, -0.7), P(0.09, 4.5, -0.66), P(-0.09, 4.5, -0.66)], 0.06, "chinfin", bevel=0.01, plating=None, normal=Z, lod2=False)
    hull.box(Frame(P(0, 3.85, -0.8), Y, Z), (0.12, 1.2, 0.34), PLATE, chamfer=0.02, tile=3.0)
    fr = Frame(P(0, 5.27 - 1.55, -0.9), Y, Z)
    with ctx.subsystem("H", "mount", fr, radius=1.8, hp=1.0, tags=["gun:heavy", "weapon"]) as sb:
        F.needle_gun(K, sb, fr, "hammer", length=1.6, r=0.085, heavy=True)
    fr = Frame(P(0, RING_Y, RING_R + 0.0), Y, Z)
    with ctx.subsystem("eye", "sensors", fr, radius=1.95, hp=1.0, tags=["radar"]) as sb:
        F.sensor_eye(K, sb, fr, "eye", r=0.34, tall=0.42)
    fr = Frame(P(0, -0.2, -1.25), Y, -Z)
    with ctx.subsystem("rack", "launcher", fr, radius=1.4, hp=1.0, tags=["missile"]) as sb:
        F.missile_rack(K, sb, fr, "rack", cols=3, rows=2, r=0.075, length=1.8, spacing=0.3)

    # ============================================================================================ lights: harsh white
    if lod <= 1:
        for sx in (-1, 1):
            F.light(K, P(sx * 6.1, 1.5, 1.05), (sx, 0.3, 0.2), LIGHT_WHITE, 0.15, "strobe", blink="strobe")
            F.light(K, P(sx * 6.1, -1.25, -0.8), (sx, -0.3, 0), LIGHT_WHITE, 0.11, "running")
            F.light(K, P(sx * 2.55, -1.5, 0.84), (0, 0.5, 1), LIGHT_WHITE, 0.1, "running")
            F.light(K, P(sx * (RING_R + 0.03), RING_Y, 0.0), (sx, 0, 0), LIGHT_WHITE, 0.12, "running")
        F.light(K, P(0.0, RING_Y, -RING_R - 0.03), -Z, LIGHT_WHITE, 0.12, "strobe", blink="strobe")
        F.light(K, P(-0.4, -5.9, 2.6), Z, LIGHT_WHITE, 0.12, "strobe", blink="strobe")
        F.light(K, P(0.0, 6.2, 0.0), Y, LIGHT_WHITE, 0.08, "running")
        F.light(K, P(0.0, 0.0, -1.97), -Z, LIGHT_RED, 0.12, "strobe", blink="strobe")

    if lod == 0:
        for name, sts in SEG.items():
            K.loft(sts, mb=ship.collision)
        F.hoop(K, ship.collision, P(0, RING_Y, 0), Y, RING_R, RING_IN, RING_D, 8, "colring", plating=None)
        for side in (1, -1):
            F.solid(K, ship.collision, [P(side * 1.4, -2.0, 0.0), P(side * 6.05, -1.2, -0.1), P(side * 6.05, 1.7, -0.1), P(side * 1.4, 0.9, 0.0)], 0.3, "colyoke", plating=None, normal=Z)
    return K


def main():
    preview = "--preview" in sys.argv
    ship = mnship.Ship(SHIP_ID, "Throne heavy fighter", ART, ["mn_earth_plate", "mn_earth_trim", "mn_earth_lights", "mn_earth_engine", "mn_earth_chrome", "mn_earth_plate_b", "mn_earth_plate_c", "mn_earth_plume", "mn_earth_plate_d", "mn_earth_plate_e", "mn_earth_wear", "mn_earth_paint"], seed=SEED,
                       manifest_dir=RES, material_set=MATERIAL_SET)
    kits = {}
    for lod in (0, 1, 2):
        ship.build_lod(lod, lambda ctx: kits.__setitem__(ctx.lod, build(ctx)))
    K0 = kits[0]
    sites = [("spine-aft", P(0.0, -2.0, 1.2), Z, 0.55), ("flank-stbd", P(1.62, 0.2, 0.0), X, 0.5), ("yoke-stbd", P(3.6, -0.3, 0.17), Z, 0.6),
             ("yoke-port", P(-3.4, -0.4, 0.17), Z, 0.55), ("belly", P(0.0, -2.2, -1.1), -Z, 0.5), ("ring-top", P(-0.9, RING_Y, 2.5), Z, 0.4)]
    for name, p, n, r in sites:
        F.add_site(K0, ship, name, p, n, r)
    for side in (1, -1):
        ship.add_hook("fire", Frame(P(side * 2.55, -5.3, 0.0), -Y, Z), 1.0)
    ship.add_hook("vent", Frame(P(0.0, -2.6, 1.2), Z, Y), 0.8)
    ship.extra["damagePoints"] = F.damage_points(K0.surfaces, 36, SEED)
    ship.extra["lodScreenHeights"] = [0.10, 0.03, 0.006]
    ship.extra["scorchScale"] = 0.14          # hit radii are set for capital hulls; a fighter's scorch is this fraction of that
    ship.extra["engineLights"] = {"SUB_engine_P": {"z": 0.8, "candela": 14000.0, "range": 20.0, "fade": 160.0}, "SUB_engine_S": {"z": 0.8, "candela": 14000.0, "range": 20.0, "fade": 160.0}}
    stats = ship.export()
    print(f"[MN] bounds {stats['bounds']}")
    if preview:
        out = os.environ.get("MN_PREVIEW_DIR", os.path.join(ROOT, "art_src", "earth_fighters", "_preview", SHIP_ID))
        mnpreview.render_views(out, [
            ("three_quarter", (14, -17, 9), (0, 0, 0), 40),
            ("side", (30, 0, 0.5), (0, 0, 0), 40),
            ("top", (0, 0, 30), (0, 0, 0), 40),
            ("front", (0, 28, 4), (0, 0, 0), 40),
            ("aft", (9, -18, 4), (0, -3, 0), 40),
            ("close", (7, 7, 3.5), (0, 1.5, 0), 40),
        ], size=(1280, 720))
        mnpreview.render_views(out + "_wreck", [("wreck_aft", (7, -12, 4), (0, -3, 0), 40), ("wreck_front", (9, 10, 3), (1.5, 3, -0.2), 40)], size=(1280, 720),
                               show=("__destroyed", "__debris", "__breach"))


main()
