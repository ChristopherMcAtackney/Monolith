"""Earth's interceptor (the Seraph class), 11 m long: a lance. A slender, layered fuselage of plated segments joined by recessed necks and chrome collars, a
long chrome probe for a nose, three pairs of thin plated blades (forward-swept main wings that carry the needle guns, swept lower stabilisers, short upper
canards: the seraph's six wings), a tall leaning dorsal blade and a short ventral one on the other side (asymmetric), twin engine nacelles with recessed hot bells
at the tail, a sensor eye on the spine, a chin cannon on a pylon and a four-missile rack on the belly. Antennas, a sensor boom and a vane break the outline.
Engines, the two needle mounts, the chin cannon, the sensor and the rack are named subsystems with wrecks and debris.

    tools\\blender.ps1 art_src\\earth_fighters\\build_earth_interceptor.py              (writes the FBX and manifest; then tools\\unity.ps1 ...Setup.All)
    tools\\blender.ps1 art_src\\earth_fighters\\build_earth_interceptor.py -- --preview  (also renders Workbench views)

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
SHIP_ID = "earth_interceptor"
SEED = 2207

SEG = {
    "tail": [st(-4.95, 0.44, 0.38, 0.14), st(-3.3, 0.60, 0.50, 0.19)],
    "mid": [st(-3.0, 0.66, 0.54, 0.20), st(-0.4, 0.80, 0.60, 0.23), st(2.1, 0.62, 0.46, 0.20)],
    "fore": [st(2.4, 0.50, 0.38, 0.16), st(3.2, 0.42, 0.32, 0.14), st(4.15, 0.20, 0.17, 0.07)],
}
NECKS = [[st(-3.3, 0.46, 0.40, 0.14), st(-3.0, 0.46, 0.40, 0.14)], [st(2.1, 0.40, 0.32, 0.12), st(2.4, 0.40, 0.32, 0.12)]]


def mirror(q):
    """The same quad on the other side of the ship, in the order that keeps its normal."""
    m = np.asarray(q, float) * np.array([-1.0, 1.0, 1.0])
    return m[::-1]


def P(x, y, z):
    return np.array([x, y, z], float)


# the wings, starboard side, corners counter-clockwise seen from the face whose normal points out (up, or outward)
MAIN = [P(0.65, -0.5, 0.0), P(4.2, 3.35, -0.26), P(4.2, 4.15, -0.26), P(0.65, 1.9, 0.0)]
STAB = [P(0.50, -2.6, -0.15), P(0.50, -4.4, -0.15), P(3.1, -5.15, -1.35), P(3.1, -4.0, -1.35)]
CANARD = [P(0.30, 2.5, 0.10), P(1.55, 3.05, 0.62), P(1.55, 3.65, 0.62), P(0.30, 3.1, 0.10)]
VANE = [P(0.55, 0.2, 0.1), P(0.9, 0.15, 0.55), P(0.9, 1.1, 0.5), P(0.55, 1.05, 0.1)]


def build(ctx):
    F.dark_paint()
    K = Kit(ctx, TRIMS, SEED)
    K.surfaces = []
    lod = ctx.lod
    hull = ctx.hull
    ship = ctx.ship

    # --- the fuselage: three plated segments, recessed necks, chrome collars ---
    for name, sts in SEG.items():
        F.hull(K, hull, sts, name)
    for sts in NECKS:
        K.loft(sts, mb=hull)
    F.collar(K, -3.3, -3.0, (0.62, 0.52, 0.16), inner=(0.46, 0.40, 0.14), bevel=0.02)
    F.collar(K, 2.1, 2.4, (0.52, 0.40, 0.14), inner=(0.40, 0.32, 0.12), bevel=0.02)
    F.collar(K, -5.0, -4.9, (0.50, 0.44, 0.15), inner=None, bevel=0.02)
    # the nose: a long chrome probe, a collar and a thin sensor ring on it
    F.taper(K, hull, P(0, 4.1, 0), P(0, 5.46, 0), 0.13, 0.012, sides=6, mat=CHROME)
    F.band(K, hull, P(0, 4.45, 0), Y, 0.17, 0.05, sides=8)
    F.band(K, hull, P(0, 4.85, 0), Y, 0.11, 0.035, sides=8)

    # --- the dorsal spine: a raised armoured ridge along the back, a tall leaning blade at the tail and a short ventral one on the other side ---
    F.solid(K, hull, [P(-0.17, -3.1, 0.52), P(0.17, -3.1, 0.52), P(0.17, 1.9, 0.5), P(-0.17, 1.9, 0.5)], 0.07, "spine", bevel=0.02, faces=(True, False), chrome_edges=(1, 3))
    F.solid(K, hull, [P(-0.05, -1.0, 0.52), P(-0.05, -4.0, 0.40), P(-0.60, -4.5, 2.05), P(-0.60, -2.95, 2.05)], 0.07, "fin", bevel=0.02, chrome_edges=(2,))
    F.solid(K, hull, [P(0.12, -1.3, -0.48), P(0.12, -3.6, -0.4), P(0.62, -3.4, -1.25), P(0.62, -2.0, -1.3)], 0.06, "keel", bevel=0.018)

    # --- the six blades ---
    for side in (1, -1):
        sg = (lambda q: q) if side > 0 else mirror
        tag = "S" if side > 0 else "P"
        F.solid(K, hull, sg(MAIN), 0.15, "main" + tag, bevel=0.032, chrome_edges=(2,))                       # the leading edge is the (tip lead, root lead) edge
        F.solid(K, hull, sg(STAB), 0.10, "stab" + tag, bevel=0.025, chrome_edges=(3,))
        F.solid(K, hull, sg(CANARD), 0.06, "can" + tag, bevel=0.018, plating=None, chrome_edges=(2,))
    F.solid(K, hull, VANE, 0.05, "vane", bevel=0.015, plating=None, chrome_edges=(1,))                           # a sensor vane on the starboard flank only

    # --- the engine nacelles, on struts: hard octagonal bodies with chrome collars and slotted intakes ---
    for side in (1, -1):
        x = side * 1.55
        F.taper(K, hull, P(x, -1.55, 0.04), P(x, -4.55, 0.04), 0.50, 0.41, sides=8, mat=PLATE, phase=math.pi / 8)
        F.taper(K, hull, P(x, -1.40, 0.04), P(x, -1.55, 0.04), 0.40, 0.50, sides=8, mat=CHROME, phase=math.pi / 8)
        F.band(K, hull, P(x, -2.6, 0.04), Y, 0.54, 0.07, sides=8)
        F.band(K, hull, P(x, -3.6, 0.04), Y, 0.50, 0.05, sides=8)
        for z in (0.34, -0.26):
            q = [P(side * 0.55, -2.2, z * 0.3), P(side * 1.15, -2.2, z), P(side * 1.15, -4.2, z), P(side * 0.55, -4.2, z * 0.3)]
            F.solid(K, hull, q, 0.05, f"strut{side}{z}", bevel=0.012, plating=None)

    # --- hardware that breaks the outline: antennas, a sensor boom with a lens, a small dish on the port flank ---
    F.whip(K, hull, P(0.28, -1.2, 0.58), P(0.36, -1.55, 2.35), 0.014)
    F.whip(K, hull, P(-0.30, 0.2, 0.58), P(-0.34, -0.1, 1.55), 0.011)
    F.whip(K, hull, P(4.2, 3.75, -0.26), P(4.3, 5.1, -0.24), 0.013)
    F.whip(K, hull, P(-4.2, 3.75, -0.26), P(-4.25, 4.5, -0.25), 0.012)
    F.taper(K, hull, P(0.72, 0.9, 0.05), P(1.55, 2.0, 0.08), 0.045, 0.03, sides=6, mat=CHROME)
    if lod <= 1:
        hull.box(Frame(P(1.6, 2.1, 0.08), Y, Z), (0.13, 0.2, 0.13), CHROME, tile=2.0, chamfer=0.03)
        hull.lathe([(0.0, 0.0), (0.2, 0.05), (0.27, 0.12)], Frame(P(-0.82, 0.7, 0.12), -X, Z), CHROME, segs=10, tile=2.0)

    # --- small trim: hatches and vents on the spine and the flanks, a light bar ---
    if lod == 0:
        for (x, y, z, sc, nm) in ((0.0, -1.2, 0.665, 0.17, "hatch_a"), (0.0, 0.9, 0.62, 0.15, "hatch_b"), (0.0, -2.3, 0.545, 0.13, "vent")):
            F.hatch(K, P(x, y, z), Y, Z, scale=sc, name=nm, lift=0.045)
        K.strip(P(0.805, -0.6, -0.04), Y, X, 2.4, "light_bar", lift=0.0, scale=0.28)
        K.strip(P(-0.805, -0.6, -0.04), -Y, -X, 2.4, "light_bar", lift=0.0, scale=0.28)

    # --- markings and wear: a code on the starboard flank, chevrons on the wing blades, a scorch behind each gun, heat on the nacelles ---
    if lod == 0:
        K.paint_quad("code_3", P(0.815, -2.05, 0.18), Y, X, size=(0.95, 0.33), lift=0.0)
        K.paint_quad("chevron_0", P(3.4, 2.65, -0.095), -Y * 0.0 + X, Z, size=(1.7, 0.36), lift=0.0)
        K.paint_quad("chevron_1", P(-3.4, 2.65, -0.095), X, Z, size=(1.7, 0.36), lift=0.0)
        K.paint_quad("emblem", P(0.0, -3.6, 0.51), Y, Z, size=(0.7, 0.7), lift=0.0)
        for side in (1, -1):
            K.wear_quad("scorch_1" if side > 0 else "scorch_2", P(side * 2.9, 2.9, -0.2), Y, Z, size=(0.8, 1.6), lift=0.0)
        K.wear_quad("streak_2", P(0.0, -0.6, 0.545), Y, Z, size=(3.2, 0.5), lift=0.0)

    # ============================================================================================ subsystems
    # engines: the bell sits in the nacelle's rear face, the exhaust goes out of the back
    for side, nm in ((-1, "P"), (1, "S")):
        fr = Frame(P(side * 1.55, -4.45, 0.04), -Y, Z)
        with ctx.subsystem(nm, "engine", fr, radius=1.6, hp=1.0, tags=["propulsion"]) as sb:
            F.fighter_engine(K, sb, fr, f"eng{nm}", throat=0.2, exit_r=0.40, length=0.9, s=0.2)
    # the two needle guns, under the leading edges of the main wings; the chin cannon on a pylon
    for side, nm in ((-1, "P"), (1, "S")):
        fr = Frame(P(side * 2.9, 3.38 - 1.38, -0.35), Y, Z)
        with ctx.subsystem(nm, "mount", fr, radius=1.0, hp=0.5, tags=["gun:primary", "weapon"]) as sb:
            F.needle_gun(K, sb, fr, f"needle{nm}", length=1.42, r=0.062)
    F.solid(K, hull, [P(-0.07, 2.6, -0.28), P(0.07, 2.6, -0.28), P(0.07, 3.7, -0.26), P(-0.07, 3.7, -0.26)][::1], 0.05, "chinfin", bevel=0.01, plating=None)
    hull.box(Frame(P(0, 3.15, -0.6), Y, Z), (0.1, 1.15, 0.64), PLATE, chamfer=0.02, tile=3.0)
    fr = Frame(P(0, 4.03 - 1.45, -0.9), Y, Z)
    with ctx.subsystem("H", "mount", fr, radius=1.2, hp=1.0, tags=["gun:heavy", "weapon"]) as sb:
        F.needle_gun(K, sb, fr, "hammer", length=1.5, r=0.075, heavy=True)
    # the sensor eye on the spine and the missile rack on the belly
    fr = Frame(P(0, 1.56, 0.5), Y, Z)
    with ctx.subsystem("eye", "sensors", fr, radius=1.3, hp=1.0, tags=["radar"]) as sb:
        F.sensor_eye(K, sb, fr, "eye", r=0.25, tall=0.32)
    fr = Frame(P(0, 0.3, -0.58), Y, -Z)
    with ctx.subsystem("rack", "launcher", fr, radius=1.0, hp=1.0, tags=["missile"]) as sb:
        F.missile_rack(K, sb, fr, "rack", cols=2, rows=2, r=0.062, length=1.5, spacing=0.25)

    # ============================================================================================ lights: harsh white, a few
    if lod <= 1:
        for sx in (-1, 1):
            F.light(K, P(sx * 4.18, 3.65, -0.14), (sx, 0.3, 0.3), LIGHT_WHITE, 0.13, "strobe", blink="strobe")
            F.light(K, P(sx * 3.05, -5.1, -1.25), (sx, 0, -0.2), LIGHT_WHITE, 0.09, "running")
            F.light(K, P(sx * 1.55, -1.38, 0.55), (0, 0.5, 1), LIGHT_WHITE, 0.08, "running")
        F.light(K, P(-0.6, -4.5, 2.07), Z, LIGHT_WHITE, 0.11, "strobe", blink="strobe")
        F.light(K, P(0.0, 4.46, 0.0), Y, LIGHT_WHITE, 0.07, "running")
        F.light(K, P(0.0, -0.4, -0.62), -Z, LIGHT_RED, 0.1, "strobe", blink="strobe")

    # --- the collision proxy ---
    if lod == 0:
        for name, sts in SEG.items():
            K.loft(sts, mb=ship.collision)
        for side in (1, -1):
            q = MAIN if side > 0 else mirror(MAIN)
            F.solid(K, ship.collision, q, 0.16, "colw", bevel=0.01, plating=None)
        ship.collision.box(Frame(P(0, -2.6, 1.0), Y, Z), (0.1, 1.8, 2.0), PLATE)
    return K


def main():
    preview = "--preview" in sys.argv
    ship = mnship.Ship(SHIP_ID, "Seraph interceptor", ART, ["mn_earth_plate", "mn_earth_trim", "mn_earth_lights", "mn_earth_engine", "mn_earth_chrome", "mn_earth_plate_b", "mn_earth_plate_c", "mn_earth_plume", "mn_earth_plate_d", "mn_earth_plate_e", "mn_earth_wear", "mn_earth_paint"], seed=SEED,
                       manifest_dir=RES, material_set=MATERIAL_SET)
    kits = {}
    for lod in (0, 1, 2):
        ship.build_lod(lod, lambda ctx: kits.__setitem__(ctx.lod, build(ctx)))
    K0 = kits[0]
    # hull damage sites, hooks and the sampled points for scorch decals
    sites = [("spine-aft", P(0.0, -2.2, 0.56), Z, 0.42), ("flank-stbd", P(0.82, -0.9, 0.0), X, 0.4), ("main-stbd", P(2.6, 1.9, 0.11), Z, 0.45),
             ("main-port", P(-2.2, 1.0, 0.1), Z, 0.4), ("belly", P(0.0, -1.8, -0.53), -Z, 0.4)]
    for name, p, n, r in sites:
        F.add_site(K0, ship, name, p, n, r)
    for side in (1, -1):
        ship.add_hook("fire", Frame(P(side * 1.55, -4.5, 0.04), -Y, Z), 0.8)
    ship.add_hook("vent", Frame(P(0.0, -1.2, 0.56), Z, Y), 0.6)
    ship.extra["damagePoints"] = F.damage_points(K0.surfaces, 28, SEED)
    ship.extra["lodScreenHeights"] = [0.10, 0.03, 0.006]
    ship.extra["scorchScale"] = 0.11          # hit radii are set for capital hulls; a fighter's scorch is this fraction of that
    ship.extra["engineFollowsShip"] = True    # the glow follows the ship's throttle and afterburner in a fight
    ship.extra["engineLights"] = {"SUB_engine_P": {"z": 0.55, "candela": 6000.0, "range": 14.0, "fade": 140.0}, "SUB_engine_S": {"z": 0.55, "candela": 6000.0, "range": 14.0, "fade": 140.0}}
    stats = ship.export()
    print(f"[MN] bounds {stats['bounds']}")
    if preview:
        out = os.environ.get("MN_PREVIEW_DIR", os.path.join(ROOT, "art_src", "earth_fighters", "_preview", SHIP_ID))
        mnpreview.render_views(out, [
            ("three_quarter", (11, -13, 6.5), (0, 0, 0), 40),
            ("side", (22, 0, 0.4), (0, 0, 0), 40),
            ("top", (0, 0, 22), (0, 0, 0), 40),
            ("front", (0, 20, 3), (0, 0, 0), 40),
            ("aft", (7, -13, 3), (0, -3, 0), 40),
            ("close", (5.5, 5.5, 2.5), (1, 2, -0.2), 40),
        ], size=(1280, 720))
        mnpreview.render_views(out + "_wreck", [("wreck_aft", (5, -9, 3), (0, -2, 0), 40), ("wreck_front", (6, 8, 2), (1.5, 2.5, -0.2), 40)], size=(1280, 720),
                               show=("__destroyed", "__debris", "__breach"))


main()
