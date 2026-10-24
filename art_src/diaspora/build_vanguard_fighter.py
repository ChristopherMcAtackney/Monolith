"""The Vanguard fighter (vanguard_fighter): the diaspora's fighter, 13 m, flown by the player (first person only, so never seen from outside by them) and by the Sable
wingmen. A worn, patched human airframe with the alien allies' technology grafted on:

    - the pilot's cabin is the nose pod: its arch, its tall canopy bubble and the dark ribbed frames over the glass are the cabin's (art_src/cabin, same numbers, outside),
    - behind it a plated spine in bone, olive and rust paint with welded repair plates, rows of pipe and cable laid on the outside, a tall patched fin to port only,
    - broad forward-carrying wings with the needle guns on booms at the leading edge, hazard stripes, a stencilled code,
    - two engine nacelles hung from the wing roots: a plain human cowl in front and a dark, ribbed, grown shell behind it round a steel bell (the alien part),
    - a living vein of dark tube running from the sensor eye over the spine to the tail; teal glyph bands on the grafts.
Engines, the two needle guns, the chin cannon and the sensor eye are named subsystems with wrecks and debris (the contract in art_src/PIPELINE.md).

    tools\\blender.ps1 art_src\\diaspora_hull\\build_diaspora_hull_textures.py     (once, or when the textures change)
    tools\\blender.ps1 art_src\\diaspora\\build_vanguard_fighter.py                (writes the FBX and manifest; then tools\\unity.ps1 Monolith.EditorTools.Setup.All)
    tools\\blender.ps1 art_src\\diaspora\\build_vanguard_fighter.py -- --preview   (also renders Workbench views)

Axes while building: x right (starboard), y forward (the nose), z up. The origin is the ship's centre as the flight and combat data have it: the seated eye is at
(0, 1.5, 0.4) (Resources/View/view.json), which puts the cabin (built round its own origin) where it belongs inside the pod.
"""
import json
import math
import os
import sys

import numpy as np

from blender_lib import mnmesh, mnship, mnkit, mnparts, mnpreview, mnfighter as F, mndiaspora as D
from blender_lib.mnkit import Kit, station as st, PLATE, TRIM, LIGHTS, ENGINE, CHROME, PLATE_B, PLATE_C, PLUME, PLATE_D, PLATE_E, WEAR, PAINT
from blender_lib.mnkit import LIGHT_WHITE, LIGHT_COLD, LIGHT_RED, LIGHT_GREEN, LIGHT_AMBER, LIGHT_EMBER
from blender_lib.mnmesh import Frame, Patch, norm, cross
from blender_lib.mndiaspora import BIO, GLYPH, GLASS, STEEL
from blender_lib.mnfighter import X, Y, Z

ROOT = os.environ.get("MN_ROOT", os.getcwd())
ART = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "DiasporaShips")
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "ShipArt")
TRIMS = json.load(open(os.path.join(ROOT, "art_src", "diaspora_hull", "trims.json")))
MATERIAL_SET = "Assets/Monolith/Art/DiasporaHull/dia_hull.materials.json"
SHIP_ID = "vanguard_fighter"
SEED = 7101

# plates: a little bigger and more irregular than Earth's, with more chips: a used hull
PLATING = dict(rows=(0.55, 2.0), cols=(0.4, 1.4), gap=0.05, height=(0.02, 0.07), chamfer=0.03, margin=0.05, min_size=0.22, sub_prob=0.28,
               sub=(0.3, 0.75), sub_height=(0.01, 0.03))
F.WEAR_STYLE.update(chip_p=0.5, grime_p=0.35)

EYE_Y, FLOOR_Z = 1.5, -0.92            # the seated eye at (0, 1.5, 0.4); the cabin's floor is 1.32 below it
SKIN = 0.13                            # the pod's outer skin and frame over the cabin's inside


def P(x, y, z):
    return np.array([x, y, z], float)


def mirror(q):
    return (np.asarray(q, float) * np.array([-1.0, 1.0, 1.0]))[::-1]


# ------------------------------------------------------------------------------------------------ the cabin's arch, from art_src/cabin/build_cabin.py
def sstep(a, b, x):
    t = float(np.clip((x - a) / (b - a), 0.0, 1.0))
    return t * t * (3 - 2 * t)


def roof_z(y):
    return 2.36 + 0.26 * sstep(-1.45, -0.35, y) - 0.10 * sstep(0.7, 1.45, y)


def arch_half(y):
    k = 1.0 - 0.20 * sstep(0.30, 1.45, y)
    zr = roof_z(y)
    lift = zr - 2.36
    zs = float(np.clip(1.25 - 0.48 * y, 0.55, 1.25))
    return [(1.02 * k, 0.0), (1.14 * k, 0.12), (1.28 * k, 0.45), (1.28 * k, zs), (1.26 * k, 1.50 + 0.30 * lift), (1.12 * k, 1.85 + 0.60 * lift),
            (0.90 * k, 2.14 + 0.85 * lift), (0.46 * k, zr - 0.02), (0.0, zr)]


def arch(y):
    h = np.array(arch_half(y), np.float64)
    left = np.stack([-h[:, 0], h[:, 1]], axis=1)
    right = h[:-1][::-1]
    return np.concatenate([left, right], axis=0)


def inset_poly(poly, d):
    p = np.asarray(poly, np.float64)
    e = np.diff(p, axis=0)
    e /= np.linalg.norm(e, axis=1, keepdims=True)
    n = np.stack([e[:, 1], -e[:, 0]], axis=1)
    vn = np.zeros_like(p)
    vn[0], vn[-1] = n[0], n[-1]
    vn[1:-1] = n[:-1] + n[1:]
    vn /= np.linalg.norm(vn, axis=1, keepdims=True)
    cosv = np.empty(len(p))
    cosv[0], cosv[-1] = 1.0, 1.0
    cosv[1:-1] = np.maximum(0.35, np.einsum("ij,ij->i", vn[1:-1], n[:-1]))
    return p + vn * (d / cosv)[:, None]


def ring_at(yc, d=SKIN):
    """The pod's outer ring at cabin length ``yc`` (ship space), 17 points from the left floor edge over the roof to the right."""
    pts = inset_poly(arch(yc), -d)
    return np.array([(x, EYE_Y + yc, z + FLOOR_Z) for x, z in pts])


YS_AFT = [-2.35, -1.60, -0.80, 0.05]
YS_CAN = [0.05, 0.75, 1.45]            # the skin's rings (the canopy's ribs stand at the cabin's own frame positions, below)
SILL = 3                               # arch vertex indices 3 and 13 are the sill: glass above, plate below (front half)


def cap(mb, ring, normal, mat):
    """A flat cap over a ring of points, wound so its normal is ``normal``."""
    ring = np.asarray(ring, float)
    c = ring.mean(axis=0)
    k = len(ring)
    s = sum(np.dot(cross(ring[i] - c, ring[(i + 1) % k] - c), normal) for i in range(k))
    if s < 0:
        ring = ring[::-1].copy()
    mb.tri_fan_cap(ring, c, normal, mat)


def pod_shell(K, hull):
    """The nose pod: the cabin's arch, plated below the sill and over the roof, glass above the sill in the canopy, dark ribbed frames over the glass."""
    ys = YS_AFT[:-1] + YS_CAN
    rings = [ring_at(y) for y in ys]
    patches_all = []
    glass = []
    carcass = []
    for i in range(len(ys) - 1):
        for j in range(16):
            patch = Patch(rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j])
            if ys[i] >= 0.05 - 1e-6 and SILL <= j < 16 - SILL:
                glass.append(patch)
            else:
                patches_all.append((patch, f"pod{i}-{j}"))
                carcass.append(patch)
    K._carcass(hull, carcass)
    for patch in glass:
        hull.quads_metric(np.array([[patch.p00, patch.p10, patch.p11, patch.p01]]), GLASS, 2.0, np.zeros((1, 2)), 2.0)
    if not hasattr(K, "surfaces"):
        K.surfaces = []
    for patch, key in patches_all:
        if patch.lu > 0.3 and patch.lv > 0.3:
            K.surfaces.append(patch)
        if K.lod == 0 and patch.lu > 0.5 and patch.lv > 0.5:
            K.plate_patch(patch, key, mb=hull, **PLATING)
            K.edge_wear(patch, K.last_cells, K.last_tones, key, mb=hull, **F.WEAR_STYLE)
    # the aft bulkhead (a plate, ringed in steel) and the nose: glass over the sill, plate below it
    rear = rings[0]
    cap(hull, rear, np.array([0, -1.0, 0]), PLATE)
    front = rings[-1]
    cap(hull, front[SILL:16 - SILL + 1], np.array([0, 1.0, 0]), GLASS)
    cap(hull, np.concatenate([front[16 - SILL:], front[:SILL + 1]], axis=0), np.array([0, 1.0, 0]), PLATE)
    if K.lod >= 2:
        return
    # the canopy's frame: dark ribbed hoops over the glass at the cabin's own frame positions, and struts along the sill and the shoulders
    for yc in (0.40, 0.80, 1.18, 1.44):
        r = inset_poly(arch(yc), -SKIN - 0.04)
        pts = np.array([(x, EYE_Y + yc, z + FLOOR_Z) for x, z in r])[SILL:16 - SILL + 1]
        D.sweep(hull, pts, 0.052, BIO, sides=6 if K.lod == 0 else 4, caps=True)
    if K.lod == 0:
        for j in (3, 5, 7, 8, 9, 11, 13):
            yy = np.linspace(0.05, 1.45, 9)
            path = np.array([inset_poly(arch(y), -SKIN - 0.03)[j] for y in yy])
            D.sweep(hull, np.array([(x, EYE_Y + y, z + FLOOR_Z) for (x, z), y in zip(path, yy)]), 0.034, BIO, sides=5, caps=True)
        # steel collar round the pod's aft end, a worn seam where the pod meets the spine
        prof = [tuple(p) for p in inset_poly(arch(-2.35), -SKIN - 0.05)]
        hull.extrude(prof, Frame((0, EYE_Y - 2.35 + 0.02, FLOOR_Z), Y, Z), -0.03, 0.18, STEEL, tile=3.0, caps=(False, False))
        # a rib every half metre round the aft pod (the graft shows through the plating's gaps as dark frames)
        for yc in (-2.18, -1.74, -1.30, -0.86, -0.42):
            r = inset_poly(arch(yc), -SKIN - 0.035)
            pts = np.array([(x, EYE_Y + yc, z + FLOOR_Z) for x, z in r])[5:16 - 4]
            D.sweep(hull, pts, 0.04, BIO, sides=5, caps=True)


# ------------------------------------------------------------------------------------------------ the airframe
SEG = {
    "neck": [st(-1.02, 1.34, 1.16, 0.45, 0, 0.26), st(-2.4, 1.28, 1.0, 0.42, 0, 0.17)],
    "mid": [st(-2.55, 1.2, 0.9, 0.38, 0, 0.1), st(-4.3, 1.0, 0.7, 0.3, 0, 0.0)],
    "tail": [st(-4.55, 0.8, 0.56, 0.26, 0, 0.0), st(-6.5, 0.52, 0.44, 0.19, 0, 0.0)],
}
JOINTS = [[st(-2.4, 1.1, 0.8, 0.3, 0, 0.1), st(-2.55, 1.1, 0.8, 0.3, 0, 0.1)], [st(-4.3, 0.8, 0.58, 0.24), st(-4.55, 0.8, 0.58, 0.24)]]
NOSE = [st(3.1, 0.98, 0.52, 0.2, 0, -0.55), st(4.5, 0.5, 0.36, 0.16, 0, -0.62)]

# wings, starboard side, counter-clockwise seen from above (the top face looks up)
WING = [P(1.3, -3.4, 0.0), P(4.7, -1.5, -0.22), P(4.7, 1.2, -0.22), P(1.3, 2.6, 0.0)]
TIPFIN = [P(4.72, -1.35, -0.2), P(5.0, -1.2, 0.95), P(5.0, 0.3, 0.8), P(4.72, 0.9, -0.2)]
STAB = [P(0.6, -5.2, 0.12), P(2.5, -5.9, 0.0), P(2.5, -6.7, 0.0), P(0.6, -6.5, 0.12)]
FIN = [P(-0.3, -3.3, 0.7), P(-0.3, -6.5, 0.45), P(-0.3, -6.9, 2.45), P(-0.3, -4.5, 2.75)]
NAC_X, NAC_Z = 2.0, -0.12


def build(ctx):
    D.set_paint()
    K = Kit(ctx, TRIMS, SEED)
    K.surfaces = []
    lod = ctx.lod
    hull = ctx.hull
    ship = ctx.ship

    pod_shell(K, hull)

    # --- the spine: three plated segments, recessed joints, steel collars ---
    for name, sts in SEG.items():
        F.hull(K, hull, sts, name, plating=PLATING)
    for sts in JOINTS:
        K.loft(sts, mb=hull)
    F.collar(K, -2.4, -2.55, (1.28, 1.0, 0.4), inner=(1.1, 0.8, 0.3), bevel=0.02)
    F.collar(K, -4.3, -4.55, (1.0, 0.7, 0.28), inner=(0.8, 0.58, 0.24), bevel=0.02)
    # the nose block under the canopy and the sensor probe in front of it
    F.hull(K, hull, NOSE, "nose", plating=PLATING)
    F.taper(K, hull, P(0, 4.45, -0.6), P(0, 6.15, -0.6), 0.1, 0.012, sides=6, mat=STEEL)
    F.band(K, hull, P(0, 4.9, -0.6), Y, 0.14, 0.05, sides=8)

    # --- the dorsal ridge of equipment, the tall patched fin to port only, a low ventral keel to starboard, stabilisers ---
    F.solid(K, hull, [P(-0.28, -1.3, 1.18), P(0.28, -1.3, 1.18), P(0.28, -4.2, 0.82), P(-0.28, -4.2, 0.82)], 0.14, "ridge", bevel=0.03, plating=PLATING, faces=(True, False), chrome_edges=(1, 3))
    F.solid(K, hull, FIN, 0.09, "fin", bevel=0.025, plating=PLATING, normal=X, chrome_edges=(2,))
    F.solid(K, hull, [P(0.2, -1.8, -0.62), P(0.2, -4.4, -0.55), P(0.2, -4.7, -1.55), P(0.2, -2.4, -1.4)], 0.08, "keel", bevel=0.02, plating=PLATING, normal=X)
    for side in (1, -1):
        q = STAB if side > 0 else mirror(STAB)
        F.solid(K, hull, q, 0.08, "stab" + ("S" if side > 0 else "P"), bevel=0.02, plating=None, normal=Z)

    # --- the wings and their tip fins, the gun booms, the struts to the nacelles ---
    for side in (1, -1):
        sg = (lambda q: q) if side > 0 else mirror
        tag = "S" if side > 0 else "P"
        F.solid(K, hull, sg(WING), 0.3, "wing" + tag, bevel=0.04, plating=PLATING, normal=Z, chrome_edges=(2,))
        F.solid(K, hull, sg(TIPFIN), 0.08, "tipfin" + tag, bevel=0.02, plating=PLATING if side > 0 else None, normal=X * side)
        x = side * 3.19
        # the boom the needle gun hangs from: a plated tube along the leading edge, with a steel cap and a hazard band
        F.taper(K, hull, P(x, 0.2, -0.38), P(x, 2.5, -0.38), 0.3, 0.26, sides=8, mat=PLATE, phase=math.pi / 8)
        F.band(K, hull, P(x, 2.45, -0.38), Y, 0.33, 0.07, sides=10)
        F.band(K, hull, P(x, 1.0, -0.38), Y, 0.33, 0.06, sides=10)
        for sx_ in (0.9, 1.5, 2.2):          # struts from the wing root down to the nacelle
            pass
        q = [P(side * 1.0, -2.9, 0.45), P(side * 1.7, -2.9, 0.0), P(side * 1.7, -4.4, 0.0), P(side * 1.0, -4.4, 0.45)]
        F.solid(K, hull, q if side > 0 else mirror(q), 0.07, f"strut{tag}", bevel=0.015, plating=None, normal=X * side)

    # canards beside the pod (the starboard one bigger: the asymmetry), a thin lattice mast on the starboard spine and a small radiator vane to port
    F.solid(K, hull, [P(1.42, 1.1, 0.25), P(1.42, 2.7, 0.45), P(2.9, 2.5, 0.95), P(2.9, 1.3, 0.8)], 0.06, "canS", bevel=0.015, plating=PLATING, normal=Z, lod2=False)
    F.solid(K, hull, mirror([P(1.42, 1.3, 0.25), P(1.42, 2.5, 0.4), P(2.4, 2.35, 0.75), P(2.4, 1.4, 0.65)]), 0.05, "canP", bevel=0.012, plating=None, normal=Z, lod2=False)
    if lod <= 1:
        K.lattice([(-2.4, 1.05), (-2.9, 2.1), (-3.9, 2.1), (-4.3, 0.95)], 0.55, thickness=0.05, beam=0.05, braces=2)
        F.solid(K, hull, [P(-0.9, -3.0, 0.85), P(-0.9, -4.4, 0.7), P(-1.9, -4.5, 1.45), P(-1.9, -3.1, 1.6)], 0.04, "radP", bevel=0.01, plating=None, lod2=False)
    # --- the engine nacelles: a plain human cowl in front, a dark grown shell behind it ---
    for side in (1, -1):
        x = side * NAC_X
        c0, c1, c2 = P(x, -2.2, NAC_Z), P(x, -4.6, NAC_Z), P(x, -6.35, NAC_Z)
        F.taper(K, hull, c0, c1, 0.98, 0.9, sides=8, mat=PLATE, phase=math.pi / 8)
        F.band(K, hull, P(x, -2.25, NAC_Z), Y, 1.02, 0.1, sides=12)
        F.band(K, hull, P(x, -3.5, NAC_Z), Y, 0.96, 0.06, sides=12, mat=PLATE_D)
        D.ribbed_shell(hull, c1 + P(0, 0.05, 0), c2, 0.86, 0.72, 6, 0.2, segs=14 if lod == 0 else 8, key=f"nac{side}", taper_end=0.0, lod=lod)
        if lod == 0:
            if side > 0:                                    # a hazard band round the cowl's lip, a code under the wing
                D.hazard_band(K, P(x - 0.52 * 0.0, -2.35, NAC_Z + 0.99), X, Z, 0.001, lift=0.0) if False else None
            D.glyph_band(K, P(x - 0.45, -4.85, NAC_Z + 0.9), Y, Z, 1.2, "glyph_b", lift=0.02, scale=0.5)
        # the graft's living tendrils: dark tubes from the shell forward to the spine across the strut
        if lod <= 1:
            for k, dz in enumerate((0.15, -0.15)):
                D.sweep(hull, D.arc_path(P(x * 0.86, -4.7, NAC_Z + 0.5 + dz), P(side * 1.0, -3.3, 0.3 + dz), 0.14, n=6), 0.035, BIO, sides=5)

    # --- hardware that breaks the outline: aerials, the sensor boom, a tank and a dish ---
    F.whip(K, hull, P(0.3, -1.1, 1.3), P(0.4, -1.5, 2.6), 0.014)
    F.whip(K, hull, P(-0.8, 0.3, 1.5), P(-1.0, 0.0, 2.3), 0.012)
    F.whip(K, hull, P(-0.3, -6.6, 2.4), P(-0.35, -6.9, 3.1), 0.012)
    F.whip(K, hull, P(5.0, 0.3, 0.8), P(5.1, 1.6, 0.85), 0.012)
    F.whip(K, hull, P(-5.0, 0.3, 0.8), P(-5.1, 0.9, 0.85), 0.012)
    if lod <= 1:
        D.sweep(hull, np.array([P(0.0, 0.5, 1.78), P(0.04, -0.6, 1.62), P(-0.04, -1.2, 1.4), P(0.03, -2.0, 1.26), P(-0.03, -3.0, 1.12), P(0.03, -4.0, 0.9), P(-0.02, -5.0, 0.72), P(0.0, -6.2, 0.52)]),
                0.058, BIO, sides=6 if lod == 0 else 4, r_end=0.03)
    # a fuel tank slung under the starboard wing, strapped to its pylon; the port side carries a sensor pod instead
    F.taper(K, hull, P(2.55, -1.4, -0.82), P(2.55, 1.0, -0.82), 0.3, 0.3, sides=10, mat=PLATE_E)
    F.taper(K, hull, P(2.55, 1.0, -0.82), P(2.55, 1.35, -0.82), 0.3, 0.1, sides=10, mat=PLATE_E)
    F.taper(K, hull, P(2.55, -1.75, -0.82), P(2.55, -1.4, -0.82), 0.1, 0.3, sides=10, mat=PLATE_E)
    for yy in (-0.9, 0.5):
        F.band(K, hull, P(2.55, yy, -0.82), Y, 0.325, 0.07, sides=12)
    F.solid(K, hull, [P(2.4, -0.7, -0.15), P(2.7, -0.7, -0.15), P(2.7, 0.3, -0.15), P(2.4, 0.3, -0.15)], 0.06, "pylonS", bevel=0.01, plating=None)
    hull.box(Frame(P(2.55, 0.0, -0.5), Y, Z), (0.1, 1.2, 0.62), PLATE, chamfer=0.02, tile=3.0)
    F.taper(K, hull, P(-2.5, -1.0, -0.62), P(-2.5, 1.1, -0.62), 0.22, 0.2, sides=8, mat=PLATE_C)
    F.taper(K, hull, P(-2.5, 1.1, -0.62), P(-2.5, 1.5, -0.62), 0.2, 0.06, sides=8, mat=STEEL)
    hull.box(Frame(P(-2.5, 0.0, -0.4), Y, Z), (0.1, 1.0, 0.44), PLATE, chamfer=0.02, tile=3.0)

    # --- the used look: repair plates welded on, pipe and cable laid on the outside, hatches, light bars, codes ---
    if lod == 0:
        for patch in list(K.surfaces):
            if patch.lu * patch.lv > 1.4 and patch.lu > 0.8 and patch.lv > 0.8:
                D.repair_patch(K, hull, patch, f"rp{len(K.surfaces)}{int(patch.p00[0] * 7)}{int(patch.p00[1] * 5)}", count=1 if patch.lu * patch.lv < 4 else 2, size=(0.45, 1.1), height=(0.018, 0.04))
        for (x, y, z, sc, nm) in ((0.0, -2.0, 1.215, 0.17, "hatch_a"), (0.0, -3.4, 0.95, 0.17, "hatch_b"), (0.0, -5.1, 0.7, 0.14, "vent")):
            F.hatch(K, P(x, y, z), Y, Z, scale=sc, name=nm, lift=0.01)
        K.strip(P(1.215, -1.5, 0.0), Y, X, 2.0, "light_bar", lift=0.0, scale=0.28)
        K.strip(P(-1.215, -1.5, 0.0), -Y, -X, 2.0, "light_bar", lift=0.0, scale=0.28)
        K.strip(P(1.3, -1.45, -0.28), Y, X, 2.6, "cable", lift=0.0, scale=0.3)
        K.strip(P(-1.3, -1.4, -0.28), -Y, -X, 2.6, "cable", lift=0.0, scale=0.3)
        D.pipe_run(K, hull, [P(1.0, -2.6, 0.95), P(1.0, -3.4, 0.9), P(0.75, -4.2, 0.74)], 0.045, "pipeA", bracket=0.8)
        D.pipe_run(K, hull, [P(-0.9, -2.5, 0.92), P(-0.9, -3.8, 0.86)], 0.04, "pipeB", bracket=0.8)
        D.hazard_band(K, P(4.6, -1.3, -0.07), -Y, Z, 2.4, lift=0.0, scale=0.55) if False else None
        # stencilled code on the wing, an emblem on the fin, a code on the forward nacelle cowl
        K.paint_quad("code_3", P(3.4, -0.2, 0.158), Y, Z, size=(1.1, 0.38), lift=0.0)
        K.paint_quad("code_0", P(-3.3, -0.2, 0.158), -Y, Z, size=(1.1, 0.38), lift=0.0)
        K.paint_quad("emblem", P(0.0, -5.0, 0.0) + P(-0.355, 0.0, 1.55), -Y, -X, size=(0.9, 0.9), lift=0.0)
        K.paint_quad("chevron_0", P(4.0, 0.45, 0.158), -Y * 0.0 + X, Z, size=(1.6, 0.34), lift=0.0)
        K.wear_quad("streak_0", P(3.0, -1.3, 0.158), Y, Z, size=(4.0, 0.7), lift=0.0)
        K.wear_quad("rust_1", P(-1.1, -3.2, 0.9), Y, Z, size=(3.0, 0.5), lift=0.0)
        K.wear_quad("heat_0", P(NAC_X, -5.0, NAC_Z + 0.9), Y, Z, size=(2.4, 1.1), lift=0.0)
        K.wear_quad("heat_0", P(-NAC_X, -5.0, NAC_Z + 0.9), Y, Z, size=(2.4, 1.1), lift=0.0)

    # ============================================================================================ subsystems
    # engines: the bell sits in the grown shell's rear face
    for side, nm in ((-1, "P"), (1, "S")):
        fr = Frame(P(side * NAC_X, -6.3, NAC_Z), -Y, Z)
        with ctx.subsystem(nm, "engine", fr, radius=1.73, hp=1.0, tags=["propulsion"]) as sb:
            D.fighter_engine(K, sb, fr, f"eng{nm}", throat=0.26, exit_r=0.54, length=1.05, s=0.22)
    # the needle guns on the booms; the chin cannon under the nose
    for side, nm in ((-1, "P"), (1, "S")):
        fr = Frame(P(side * 3.19, 2.24, -0.36), Y, Z)
        with ctx.subsystem(nm, "mount", fr, radius=1.2, hp=0.5, tags=["gun:primary", "weapon"]) as sb:
            D.needle_gun(K, sb, fr, f"needle{nm}", length=1.4, r=0.07)
    fr = Frame(P(0, 2.9, -0.92), Y, Z)
    with ctx.subsystem("H", "mount", fr, radius=1.32, hp=1.0, tags=["gun:heavy", "weapon"]) as sb:
        D.needle_gun(K, sb, fr, "hammer", length=1.5, r=0.08, heavy=True)
    fr = Frame(P(0, 0.95, roof_z(-0.55) + FLOOR_Z + SKIN - 0.02), Y, Z)
    with ctx.subsystem("eye", "sensors", fr, radius=1.43, hp=1.0, tags=["radar"]) as sb:
        D.sensor_eye(K, sb, fr, "eye", r=0.26, tall=0.34)

    # ============================================================================================ lights: warm and few; nav red to port, green to starboard
    if lod <= 1:
        K.light(P(-4.72, 0.0, -0.2), (-1, 0.2, 0.2), LIGHT_RED, 0.14, "nav")
        K.light(P(4.72, 0.0, -0.2), (1, 0.2, 0.2), LIGHT_GREEN, 0.14, "nav")
        K.light(P(-0.3, -6.7, 2.5), Z, LIGHT_WHITE, 0.12, "strobe", blink="strobe")
        K.light(P(0.0, -3.0, -0.72), -Z, LIGHT_RED, 0.12, "strobe", blink="strobe")
        K.light(P(5.0, 1.0, 0.75), X, LIGHT_AMBER, 0.1, "running")
        K.light(P(-5.0, 0.7, 0.8), -X, LIGHT_AMBER, 0.1, "running")
        for side in (1, -1):
            K.light(P(side * 1.43, 0.8, 0.0), (side, 0.3, 0.1), LIGHT_AMBER, 0.11, "running")
            K.light(P(side * NAC_X, -2.0, NAC_Z + 1.02), Z, LIGHT_AMBER, 0.09, "running")
            K.light(P(side * (NAC_X + 0.7), -5.5, NAC_Z + 0.55), (side, 0, 0.5), LIGHT_COLD, 0.08, "running")
        K.light(P(0.0, 2.2, 1.08), Z, LIGHT_COLD, 0.07, "running")
        # rows of small lamps along the wings' leading edges and the nacelles' shells: the lamps that give the scale away from a distance
        for side in (1, -1):
            for x in np.linspace(1.9, 4.3, 5):
                K.light(P(side * x, 2.6 - (x - 1.3) * 0.41 - 0.15, 0.17 - (x - 1.3) * 0.065), (0, 0.3, 1), LIGHT_AMBER, 0.07, "running")
            for yy in (-3.2, -4.0, -4.8):
                K.light(P(side * (NAC_X + 0.55), yy, NAC_Z + 0.78), (side, 0, 1), LIGHT_COLD if yy < -3.5 else LIGHT_AMBER, 0.07, "running")

    # --- the collision proxy ---
    if lod == 0:
        for name, sts in SEG.items():
            K.loft(sts, mb=ship.collision)
        K.loft(NOSE, mb=ship.collision)
        ship.collision.box(Frame(P(0, 1.1, 0.4), Y, Z), (2.8, 4.2, 2.4), PLATE, chamfer=0.4)
        for side in (1, -1):
            q = WING if side > 0 else mirror(WING)
            F.solid(K, ship.collision, q, 0.3, "colw", bevel=0.01, plating=None, normal=Z)
            F.taper(K, ship.collision, P(side * NAC_X, -2.2, NAC_Z), P(side * NAC_X, -6.4, NAC_Z), 0.95, 0.8, sides=8)
        F.solid(K, ship.collision, FIN, 0.1, "colf", bevel=0.01, plating=None, normal=X)
    return K


def main():
    preview = "--preview" in sys.argv
    ship = mnship.Ship(SHIP_ID, "Vanguard fighter", ART, D.MATERIALS, manifest_dir=RES, material_set=MATERIAL_SET)
    kits = {}
    for lod in (0, 1, 2):
        ship.build_lod(lod, lambda ctx: kits.__setitem__(ctx.lod, build(ctx)))
    K0 = kits[0]
    sites = [("spine", P(0.0, -3.0, 0.84), Z, 0.42), ("flank-stbd", P(1.3, -1.8, 0.1), X, 0.4), ("wing-stbd", P(3.2, -0.3, 0.16), Z, 0.45),
             ("wing-port", P(-2.8, -1.2, 0.15), Z, 0.4), ("pod-roof", P(0.0, 0.5, roof_z(-0.9) + FLOOR_Z + SKIN), Z, 0.4), ("belly", P(0.0, -2.2, -0.8), -Z, 0.4)]
    for name, p, n, r in sites:
        F.add_site(K0, ship, name, p, n, r)
    for side in (1, -1):
        ship.add_hook("fire", Frame(P(side * NAC_X, -6.4, NAC_Z), -Y, Z), 0.8)
    ship.add_hook("vent", Frame(P(0.0, -2.0, 1.22), Z, Y), 0.6)
    ship.extra["damagePoints"] = F.damage_points(K0.surfaces, 30, SEED)
    ship.extra["lodScreenHeights"] = [0.10, 0.03, 0.006]
    ship.extra["scorchScale"] = 0.12
    ship.extra["engineFollowsShip"] = True
    ship.extra["engineLights"] = {"SUB_engine_P": {"z": 0.6, "candela": 7000.0, "range": 14.0, "fade": 140.0}, "SUB_engine_S": {"z": 0.6, "candela": 7000.0, "range": 14.0, "fade": 140.0}}
    stats = ship.export()
    print(f"[MN] bounds {stats['bounds']}")
    if preview:
        out = os.environ.get("MN_PREVIEW_DIR", os.path.join(ROOT, "art_src", "diaspora", "_preview", SHIP_ID))
        mnpreview.render_views(out, [
            ("three_quarter", (11, -13, 6.5), (0, -0.5, 0), 40),
            ("side", (22, 0, 0.4), (0, -0.5, 0.2), 40),
            ("top", (0, -0.5, 22), (0, -0.5, 0), 40),
            ("front", (0, 20, 3), (0, 0, 0), 40),
            ("aft", (7, -14, 3), (0, -3, 0), 40),
            ("close", (4.5, 6.0, 2.5), (0.5, 1.5, 0.3), 40),
            ("pod", (4.5, 5.5, 2.4), (0.0, 1.5, 0.5), 30),
        ], size=(1280, 720))
        mnpreview.render_views(out + "_wreck", [("wreck_aft", (5, -9, 3), (0, -3, 0), 40), ("wreck_front", (6, 8, 2), (1.5, 2.5, -0.2), 40)], size=(1280, 720),
                               show=("__destroyed", "__debris", "__breach"))


main()
