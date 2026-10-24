"""Earth's medium fighter (the Dominion class), 15 m long: a hammer. A long layered spindle that ends in a wide, thick crossbar (the anvil) set square across the nose,
with the needle guns at its leading edge and horns at its tips, a chrome probe in front of it, a crown of three blades at the tail (a tall one and two canted out),
two engine nacelles on short struts, a belly missile rack, a chin cannon on a pylon and a sensor eye on the spine. Asymmetric: a dish on a boom to port, a
long mast and a vane to starboard. Engines, the two needle mounts, the chin cannon, the sensor eye and the rack are subsystems with wrecks and debris.

    tools\\blender.ps1 art_src\\earth_fighters\\build_earth_fighter.py [-- --preview]

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
SHIP_ID = "earth_fighter"
SEED = 5533

SEG = {
    "tail": [st(-7.0, 0.62, 0.55, 0.20), st(-4.6, 0.90, 0.78, 0.28)],
    "mid": [st(-4.3, 0.98, 0.86, 0.32), st(-0.5, 1.22, 1.02, 0.40), st(2.6, 1.0, 0.82, 0.32)],
    "fore": [st(2.9, 0.80, 0.66, 0.26), st(4.3, 0.62, 0.52, 0.22), st(5.9, 0.26, 0.22, 0.10)],
}
NECKS = [[st(-4.6, 0.7, 0.62, 0.22), st(-4.3, 0.7, 0.62, 0.22)], [st(2.6, 0.7, 0.58, 0.22), st(2.9, 0.7, 0.58, 0.22)]]
PLATES = dict(F.PLATING, rows=(0.55, 1.9), cols=(0.4, 1.35))


def P(x, y, z):
    return np.array([x, y, z], float)


def build(ctx):
    F.dark_paint()
    K = Kit(ctx, TRIMS, SEED)
    K.surfaces = []
    lod = ctx.lod
    hull = ctx.hull
    ship = ctx.ship

    # --- the spindle, its necks and collars, and the probe in front of the anvil ---
    for name, sts in SEG.items():
        F.hull(K, hull, sts, name, plating=PLATES, rail_r=0.022)
    for sts in NECKS:
        K.loft(sts, mb=hull)
    F.collar(K, -4.6, -4.3, (1.06, 0.92, 0.3), inner=(0.7, 0.62, 0.22), bevel=0.022)
    F.collar(K, 2.6, 2.9, (1.06, 0.88, 0.3), inner=(0.7, 0.58, 0.22), bevel=0.022)
    F.collar(K, -7.1, -7.0, (0.68, 0.6, 0.22), inner=None, bevel=0.02)
    F.taper(K, hull, P(0, 5.8, 0), P(0, 7.5, 0), 0.17, 0.013, sides=6, mat=CHROME)
    F.band(K, hull, P(0, 6.3, 0), Y, 0.22, 0.06, sides=8)
    F.band(K, hull, P(0, 6.9, 0), Y, 0.15, 0.04, sides=8)

    # --- the anvil: a wide, thick crossbar square across the nose, plated in layers, with a horn at each tip ---
    F.solid(K, hull, [P(-5.9, 3.5, 0.0), P(5.9, 3.5, 0.0), P(5.9, 5.1, 0.0), P(-5.9, 5.1, 0.0)], 0.44, "anvil", bevel=0.05, normal=Z, plating=PLATES)
    F.solid(K, hull, [P(-4.6, 3.9, 0.24), P(4.6, 3.9, 0.24), P(4.6, 4.75, 0.24), P(-4.6, 4.75, 0.24)], 0.12, "anvilcap", bevel=0.02, normal=Z, plating=dict(PLATES, rows=(0.35, 0.9), cols=(0.35, 1.0)))
    for side in (1, -1):
        F.solid(K, hull, [P(side * 5.9, 3.6, -0.35), P(side * 5.9, 5.0, -0.35), P(side * 6.15, 5.6, 1.5), P(side * 6.15, 3.5, 1.1)], 0.12, f"horn{side}", bevel=0.02, normal=X * side, chrome_edges=(2,))
        # a short strut from the anvil to the fuselage's flank, so it does not float
        F.solid(K, hull, [P(side * 1.0, 3.0, -0.2), P(side * 1.5, 3.0, -0.2), P(side * 1.5, 3.5, -0.2), P(side * 1.0, 3.5, -0.2)], 0.2, f"bridge{side}", bevel=0.02, normal=Z, plating=None, lod2=False)

    # --- the crown: a tall blade at the tail and two canted outward, a keel on the other side ---
    F.solid(K, hull, [P(0.0, -1.5, 1.0), P(0.0, -5.2, 0.7), P(-0.2, -6.0, 3.0), P(-0.2, -3.6, 2.7)], 0.1, "crownC", bevel=0.02, chrome_edges=(2,))
    for side, hgt in ((1, 2.0), (-1, 1.7)):
        F.solid(K, hull, [P(side * 0.55, -2.8, 0.85), P(side * 0.55, -5.4, 0.7), P(side * 1.7, -5.8, hgt), P(side * 1.7, -3.9, hgt - 0.1)], 0.08, f"crown{side}", bevel=0.018, normal=Z + X * side * 0.8)
    F.solid(K, hull, [P(0.1, -1.6, -0.95), P(0.1, -4.5, -0.7), P(0.7, -4.4, -1.8), P(0.7, -2.6, -1.9)], 0.07, "keel", bevel=0.018)
    F.solid(K, hull, [P(-0.2, -3.6, 1.02), P(0.2, -3.6, 1.02), P(0.2, 2.2, 1.04), P(-0.2, 2.2, 1.04)], 0.07, "spine", bevel=0.02, faces=(True, False), chrome_edges=(1, 3), normal=Z)

    # --- the engine nacelles on struts ---
    for side in (1, -1):
        x = side * 2.38
        F.taper(K, hull, P(x, -1.9, 0.0), P(x, -5.8, 0.0), 0.62, 0.52, sides=8, mat=PLATE, phase=math.pi / 8)
        F.taper(K, hull, P(x, -1.75, 0.0), P(x, -1.9, 0.0), 0.5, 0.62, sides=8, mat=CHROME, phase=math.pi / 8)
        F.band(K, hull, P(x, -3.2, 0.0), Y, 0.66, 0.07, sides=8)
        F.band(K, hull, P(x, -4.6, 0.0), Y, 0.6, 0.05, sides=8)
        for z in (0.3, -0.25):
            q = [P(side * 0.9, -2.4, z * 0.4), P(side * 1.8, -2.4, z), P(side * 1.8, -5.0, z), P(side * 0.9, -5.0, z * 0.4)]
            F.solid(K, hull, q, 0.06, f"strut{side}{z}", bevel=0.014, normal=Z * (1 if z > 0 else -1), plating=None, lod2=False)

    # --- hardware: a dish on a boom to port, a mast and a vane to starboard, antennas ---
    F.taper(K, hull, P(-1.0, 1.0, 0.1), P(-2.3, 2.3, 0.45), 0.06, 0.045, sides=6, mat=CHROME)
    if lod <= 1:
        hull.lathe([(0.0, 0.0), (0.25, 0.06), (0.4, 0.16)], Frame(P(-2.3, 2.3, 0.45), norm(P(-0.4, 0.5, 0.8)), Z), CHROME, segs=12, tile=2.0)
    F.whip(K, hull, P(0.6, -1.0, 0.85), P(0.7, -1.4, 3.1), 0.015)
    F.whip(K, hull, P(-0.45, 0.0, 0.88), P(-0.5, -0.3, 1.9), 0.012)
    F.whip(K, hull, P(5.95, 5.0, 0.0), P(6.0, 6.3, 0.0), 0.014)
    F.whip(K, hull, P(-5.95, 5.0, 0.0), P(-6.0, 5.8, 0.0), 0.012)
    F.solid(K, hull, [P(1.0, 0.6, 0.15), P(1.35, 0.6, 0.8), P(1.35, 1.6, 0.8), P(1.0, 1.6, 0.15)], 0.05, "vane", bevel=0.012, normal=X, plating=None, lod2=False)

    if lod == 0:
        for (x, y, z, sc, nm) in ((0.0, -2.0, 1.065, 0.18, "hatch_a"), (0.0, 0.9, 1.04, 0.15, "hatch_b"), (0.0, -0.4, 1.02, 0.13, "vent")):
            F.hatch(K, P(x, y, z), Y, Z, scale=sc, name=nm, lift=0.045)
        K.strip(P(1.225, -2.8, -0.04), Y, X, 3.0, "light_bar", lift=0.0, scale=0.3)
        K.paint_quad("code_1", P(1.225, -2.1, 0.25), Y, X, size=(1.0, 0.34), lift=0.0)
        K.paint_quad("chevron_0", P(3.4, 4.3, 0.225), X, Z, size=(2.4, 0.5), lift=0.0)
        K.paint_quad("chevron_1", P(-3.4, 4.3, 0.225), -X, Z, size=(2.4, 0.5), lift=0.0)
        K.paint_quad("emblem", P(0.0, -3.0, 1.03), Y, Z, size=(0.75, 0.75), lift=0.0)
        for side in (1, -1):
            K.wear_quad("scorch_1" if side > 0 else "scorch_3", P(side * 4.06, 4.7, -0.25), Y, Z, size=(0.9, 1.8), lift=0.0)
        K.wear_quad("streak_4", P(0.0, -1.5, 1.045), Y, Z, size=(3.6, 0.5), lift=0.0)

    # ============================================================================================ subsystems
    for side, nm in ((-1, "P"), (1, "S")):
        fr = Frame(P(side * 2.38, -6.0, 0.0), -Y, Z)
        with ctx.subsystem(nm, "engine", fr, radius=2.12, hp=1.0, tags=["propulsion"]) as sb:
            F.fighter_engine(K, sb, fr, f"eng{nm}", throat=0.27, exit_r=0.55, length=1.1, s=0.27)
    for side, nm in ((-1, "P"), (1, "S")):
        fr = Frame(P(side * 4.06, 4.68 - 1.43, -0.35), Y, Z)
        with ctx.subsystem(nm, "mount", fr, radius=1.8, hp=0.5, tags=["gun:primary", "weapon"]) as sb:
            F.needle_gun(K, sb, fr, f"needle{nm}", length=1.46, r=0.066)
    F.solid(K, hull, [P(-0.08, 3.0, -0.5), P(0.08, 3.0, -0.5), P(0.08, 4.3, -0.5), P(-0.08, 4.3, -0.5)], 0.06, "chinfin", bevel=0.01, plating=None, normal=Z, lod2=False)
    hull.box(Frame(P(0, 3.8, -0.7), Y, Z), (0.11, 1.1, 0.4), PLATE, chamfer=0.02, tile=3.0)
    fr = Frame(P(0, 5.58 - 1.5, -0.9), Y, Z)
    with ctx.subsystem("H", "mount", fr, radius=1.68, hp=1.0, tags=["gun:heavy", "weapon"]) as sb:
        F.needle_gun(K, sb, fr, "hammer", length=1.55, r=0.08, heavy=True)
    fr = Frame(P(0, 2.16, 0.97), Y, Z)
    with ctx.subsystem("eye", "sensors", fr, radius=1.82, hp=1.0, tags=["radar"]) as sb:
        F.sensor_eye(K, sb, fr, "eye", r=0.3, tall=0.36)
    fr = Frame(P(0, -0.8, -1.05), Y, -Z)
    with ctx.subsystem("rack", "launcher", fr, radius=1.2, hp=1.0, tags=["missile"]) as sb:
        F.missile_rack(K, sb, fr, "rack", cols=3, rows=2, r=0.068, length=1.6, spacing=0.27)

    if lod <= 1:
        for sx in (-1, 1):
            F.light(K, P(sx * 6.1, 5.4, 1.45), (sx, 0.3, 0.3), LIGHT_WHITE, 0.14, "strobe", blink="strobe")
            F.light(K, P(sx * 5.95, 3.55, -0.2), (sx, -0.2, 0), LIGHT_WHITE, 0.1, "running")
            F.light(K, P(sx * 2.38, -1.7, 0.66), (0, 0.5, 1), LIGHT_WHITE, 0.09, "running")
            F.light(K, P(sx * 1.7, -5.8, 1.9), Z, LIGHT_WHITE, 0.09, "running")
        F.light(K, P(-0.2, -6.0, 3.02), Z, LIGHT_WHITE, 0.12, "strobe", blink="strobe")
        F.light(K, P(0.0, 6.4, 0.0), Y, LIGHT_WHITE, 0.08, "running")
        F.light(K, P(0.7, -3.3, -1.88), -Z, LIGHT_RED, 0.1, "strobe", blink="strobe")

    if lod == 0:
        for name, sts in SEG.items():
            K.loft(sts, mb=ship.collision)
        F.solid(K, ship.collision, [P(-5.9, 3.5, 0.0), P(5.9, 3.5, 0.0), P(5.9, 5.1, 0.0), P(-5.9, 5.1, 0.0)], 0.44, "colanvil", plating=None, normal=Z)
    return K


def main():
    preview = "--preview" in sys.argv
    ship = mnship.Ship(SHIP_ID, "Dominion fighter", ART, ["mn_earth_plate", "mn_earth_trim", "mn_earth_lights", "mn_earth_engine", "mn_earth_chrome", "mn_earth_plate_b", "mn_earth_plate_c", "mn_earth_plume", "mn_earth_plate_d", "mn_earth_plate_e", "mn_earth_wear", "mn_earth_paint"], seed=SEED,
                       manifest_dir=RES, material_set=MATERIAL_SET)
    kits = {}
    for lod in (0, 1, 2):
        ship.build_lod(lod, lambda ctx: kits.__setitem__(ctx.lod, build(ctx)))
    K0 = kits[0]
    sites = [("spine-aft", P(0.0, -2.4, 1.05), Z, 0.5), ("flank-stbd", P(1.24, -0.6, 0.0), X, 0.45), ("anvil-stbd", P(3.6, 4.3, 0.24), Z, 0.55),
             ("anvil-port", P(-3.2, 4.3, 0.24), Z, 0.5), ("belly", P(0.0, -2.6, -0.82), -Z, 0.45)]
    for name, p, n, r in sites:
        F.add_site(K0, ship, name, p, n, r)
    for side in (1, -1):
        ship.add_hook("fire", Frame(P(side * 2.38, -6.1, 0.0), -Y, Z), 0.9)
    ship.add_hook("vent", Frame(P(0.0, -1.6, 1.04), Z, Y), 0.7)
    ship.extra["damagePoints"] = F.damage_points(K0.surfaces, 32, SEED)
    ship.extra["lodScreenHeights"] = [0.10, 0.03, 0.006]
    ship.extra["scorchScale"] = 0.13
    ship.extra["engineLights"] = {"SUB_engine_P": {"z": 0.7, "candela": 10000.0, "range": 18.0, "fade": 150.0}, "SUB_engine_S": {"z": 0.7, "candela": 10000.0, "range": 18.0, "fade": 150.0}}
    stats = ship.export()
    print(f"[MN] bounds {stats['bounds']}")
    if preview:
        out = os.environ.get("MN_PREVIEW_DIR", os.path.join(ROOT, "art_src", "earth_fighters", "_preview", SHIP_ID))
        mnpreview.render_views(out, [
            ("three_quarter", (13, -16, 8), (0, 0, 0), 40),
            ("side", (28, 0, 0.5), (0, 0, 0), 40),
            ("top", (0, 0, 28), (0, 0, 0), 40),
            ("front", (0, 26, 4), (0, 0, 0), 40),
            ("aft", (8, -17, 4), (0, -3, 0), 40),
            ("close", (7, 8, 3.5), (0, 3, 0), 40),
        ], size=(1280, 720))


main()
