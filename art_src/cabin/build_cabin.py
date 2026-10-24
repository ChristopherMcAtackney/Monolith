"""The pilot's cabin: a very small, worn human hull with grafted alien-ally tech, built by script (art_src/PIPELINE.md, "The cabin").

    tools\\blender.ps1 art_src\\cabin\\build_cabin_textures.py     (once, or when the textures change)
    tools\\blender.ps1 art_src\\cabin\\build_cabin.py              (writes cabin.fbx and cabin.layout.json; then tools\\unity.ps1 Monolith.EditorTools.Setup.All)
    tools\\blender.ps1 art_src\\cabin\\build_cabin.py -- --preview (also renders Workbench views from inside, into $MN_PREVIEW_DIR)

Axes while building: x right (starboard), y forward (the canopy), z up. Origin: the aft deck's floor under the seat, y = 0 at the seated eye. All the
numbers below are metres. The layout JSON (Unity axes, cabin space) is what the game reads for walking, interactions, lights and displays.

Reading list (study only; nothing copied): Soyuz descent module and orbital module interiors, Apollo command module, ISS Russian segment and Destiny lab.
What they share and this cabin takes: ribbed structural frames every half metre with panels between them, handrails, cable bundles tied along the
ceiling, labelled switch banks, placards, worn paint at every touch point, hatches with wheels, lamps in cages. The alien-ally graft (Independence Day's
technology, not its bodies): dark, ribbed, wet-looking biomechanical frames and housings with spare glyph lights, kept rough.
"""
import json
import math
import os
import sys

import numpy as np

from blender_lib import mnmesh, mncabin, mnpreview
from blender_lib.mnmesh import Frame, norm, cross
from blender_lib.mncabin import CabinAsset, loft, tube, cyl, bevel_box, fan_cap, torus, plate, quad_face, noise2, poly3, to_unity, f_unity

ROOT = os.environ.get("MN_ROOT", os.getcwd())
ART = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "Cabin")
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "Cabin")
TRIMS_JSON = os.path.join(ROOT, "art_src", "cabin", "cabin_trims.json")
MATERIAL_SET = "Assets/Monolith/Art/Cabin/cabin.materials.json"
SEED = 2207

MATS = ["mn_cab_plate", "mn_cab_bio", "mn_cab_floor", "mn_cab_trim", "mn_cab_metal", "mn_cab_fabric", "mn_cab_glass", "mn_cab_screen", "mn_cab_glyph",
        "mn_cab_tissue", "mn_cab_lamp_white", "mn_cab_lamp_amber", "mn_cab_lamp_red", "mn_cab_rubber"]
PLATE, BIO, FLOOR, TRIM, METAL, FABRIC, GLASS, SCREEN, GLYPH, TISSUE, LAMP_W, LAMP_A, LAMP_R, RUBBER = range(len(MATS))

X = np.array([1.0, 0.0, 0.0])
Y = np.array([0.0, 1.0, 0.0])
Z = np.array([0.0, 0.0, 1.0])

TRIMS = json.load(open(TRIMS_JSON)) if os.path.exists(TRIMS_JSON) else {"pieces": {}}


def rect(name, fallback=(0.0, 0.0, 1.0, 1.0)):
    p = TRIMS["pieces"].get(name)
    return tuple(p["uv"]) if p else fallback


def size_m(name, fallback=(0.3, 0.3)):
    p = TRIMS["pieces"].get(name)
    return tuple(p["metres"]) if p else fallback


def sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def F(origin, fwd=(0, 1, 0), up=(0, 0, 1)):
    return Frame(np.asarray(origin, np.float64), np.asarray(fwd, np.float64), np.asarray(up, np.float64))


def V(*a):
    return np.array(a, np.float64)


# ------------------------------------------------------------------------------------------------ the layout
Y_AFT, Y_FRONT, Y_CANOPY = -2.35, 1.45, 0.05
WALL_X = 1.28
EYE = V(0.0, 0.0, 1.32)                              # the seated eye (the camera, when the pilot sits)
LEVELS = [                                           # walkable floor: x0, x1, y0, y1 (cabin y = Unity z), height
    (-1.10, 1.10, -2.31, -1.30, 0.00),
    (-1.10, 1.10, -1.30, -1.05, 0.15),
    (-1.10, 1.10, -1.05, -0.55, 0.30),
    (-1.04, 1.04, -0.55, 1.20, 0.30),
]
TABLE_C = (0.0, -1.80)
TABLE_R = 0.44
STAND_AT = V(0.0, -0.78, 0.30)                       # where the pilot stands up to (behind the seat, on the dais)
EYE_STAND = 1.64                                     # a standing eye above the floor it stands on
EYE_CROUCH = 1.12


def roof_z(y):
    """The height of the roof's centre along the cabin: low over the plan table, lifting over the seat and the canopy's bubble, a little lower at the nose."""
    return 2.36 + 0.26 * float(sstep(-1.45, -0.35, y)) - 0.10 * float(sstep(0.7, 1.45, y))


def arch_half(y):
    """The right half of the cabin's cross-section at length ``y`` as (x, z) points from the floor edge up to the roof's centre. The canopy (the front
    half) narrows toward the nose and its sill drops, so the seated view is open glass."""
    k = 1.0 - 0.20 * sstep(0.30, 1.45, y)
    zr = roof_z(y)
    lift = zr - 2.36
    zs = float(np.clip(1.25 - 0.48 * y, 0.55, 1.25))     # the sill: glass above it, wall below (the canopy, front half)
    return [(1.02 * k, 0.0), (1.14 * k, 0.12), (1.28 * k, 0.45), (1.28 * k, zs), (1.26 * k, 1.50 + 0.30 * lift), (1.12 * k, 1.85 + 0.60 * lift),
            (0.90 * k, 2.14 + 0.85 * lift), (0.46 * k, zr - 0.02), (0.0, zr)]


def arch(y):
    """The whole cross-section, left floor edge up over the roof and down to the right floor edge: 17 (x, z) points, clockwise seen from +y."""
    h = np.array(arch_half(y), np.float64)
    left = np.stack([-h[:, 0], h[:, 1]], axis=1)
    right = h[:-1][::-1]
    return np.concatenate([left, right], axis=0)


def inset_poly(poly, d):
    """An open polygon (clockwise: inward is to the right of travel) moved inward by ``d`` (negative: outward), corners kept by the bisector."""
    p = np.asarray(poly, np.float64)
    e = np.diff(p, axis=0)
    e /= np.linalg.norm(e, axis=1, keepdims=True)
    n = np.stack([e[:, 1], -e[:, 0]], axis=1)             # inward normal of each edge
    vn = np.zeros_like(p)
    vn[0], vn[-1] = n[0], n[-1]
    vn[1:-1] = n[:-1] + n[1:]
    vn /= np.linalg.norm(vn, axis=1, keepdims=True)
    cosv = np.empty(len(p))
    cosv[0], cosv[-1] = 1.0, 1.0
    cosv[1:-1] = np.maximum(0.35, np.einsum("ij,ij->i", vn[1:-1], n[:-1]))
    return p + vn * (d / cosv)[:, None]


def arch3(y, d=0.0):
    a = arch(y)
    if d != 0.0:
        a = inset_poly(a, d)
    return poly3(a, y)


def wall_point(y, j, t=0.0, d=0.0):
    """A point on the cabin's inner skin at length y, between arch vertices j and j+1 (t along the edge), moved ``d`` into the cabin."""
    a = inset_poly(arch(y), d) if d else arch(y)
    p = a[j] + (a[j + 1] - a[j]) * t
    return V(p[0], y, p[1])


# ------------------------------------------------------------------------------------------------ pieces

def build_shell(A):
    shell = A.mesh("CABIN_SHELL")
    frame_mb = A.mesh("CABIN_FRAME")
    glass = A.mesh("CABIN_GLASS")

    # The skin. Aft section (all plate), then the canopy section (plate below the sill, glass above).
    ys_aft = [Y_AFT, -1.60, -0.80, Y_CANOPY]
    rings = np.array([arch3(y) for y in ys_aft])
    loft(shell, rings, PLATE, flip=True, tile=1.0, detail=0.5)
    ys_can = list(np.linspace(Y_CANOPY, Y_FRONT, 9))
    can = np.array([arch3(y) for y in ys_can])
    N = can.shape[1]
    sill_l, sill_r = 3, N - 1 - 3
    loft(shell, can[:, 0:sill_l + 1], PLATE, flip=True, closed=False, tile=1.0, detail=0.5)
    loft(shell, can[:, sill_r:N], PLATE, flip=True, closed=False, tile=1.0, detail=0.5)
    loft(glass, can[:, sill_l:sill_r + 1], GLASS, flip=True, closed=False, tile=1.0, detail=1.0)
    # the nose: glass above the sill, plate (the console's face) below it
    nose = can[-1]
    fan_cap(glass, nose[sill_l:sill_r + 1], -Y, GLASS)
    low = np.concatenate([nose[sill_r:N], nose[0:sill_l + 1]], axis=0)
    fan_cap(shell, low, -Y, PLATE)
    # the aft bulkhead
    fan_cap(shell, arch3(Y_AFT), Y, PLATE, tile=1.0)

    # Ribs: dark ribbed bio frames every half metre, hooping the whole section (the alien-ally graft). In the canopy they are the frame.
    rib_ys = [-2.18, -1.74, -1.30, -0.86, -0.42, 0.02]
    for y in rib_ys:
        rib(A.mesh("CABIN_SHELL"), y, 0.10, 0.075)
    for y in (0.40, 0.80, 1.18, 1.44):
        rib(frame_mb, y, 0.09, 0.07, canopy=True)
    # Longitudinal canopy struts: along the sill and the shoulders.
    for j in (3, 5, 7, 8, 9, 11, 13):
        pi = np.array([inset_poly(arch(y), 0.025)[j] for y in ys_can])
        path = np.stack([pi[:, 0], np.array(ys_can), pi[:, 1]], axis=1)
        tube(frame_mb, path, 0.035, 8, BIO, caps=True, smooth=True)
    return shell, frame_mb, glass


def rib(mb, y, width, depth, canopy=False):
    """One hoop of the bio frame: a ridged band round the cross-section, rising to a spine."""
    y0, y1 = y - width * 0.5, y + width * 0.5
    sect = [(y0, -0.012), (y0 + 0.012, depth * 0.45), (y - width * 0.18, depth * 0.92), (y, depth), (y + width * 0.18, depth * 0.92), (y1 - 0.012, depth * 0.45), (y1, -0.012)]
    rings = np.array([poly3(inset_poly(arch(y), dd), yy) for yy, dd in sect])
    loft(mb, rings, BIO, flip=True, smooth=True, closed=False, tile=0.5, detail=0.25)


def build_floor(A):
    sh = A.mesh("CABIN_SHELL")
    for x0, x1, y0, y1, z in LEVELS:
        bevel_box(sh, F(((x0 + x1) / 2, (y0 + y1) / 2, z - 0.04)), (x1 - x0, y1 - y0, 0.08), FLOOR, 0.008, tile=1.0, detail=0.5)
    # risers
    for y, z0, z1 in ((-1.30, 0.0, 0.15), (-1.05, 0.15, 0.30)):
        bevel_box(sh, F((0, y + 0.012, (z0 + z1) / 2 + 0.005)), (2.20, 0.04, z1 - z0 - 0.01), METAL, 0.006, tile=0.5)
        bevel_box(sh, F((0, y - 0.004, z1 + 0.002)), (2.20, 0.05, 0.016), METAL, 0.005, tile=0.5)      # the nosing
    # floor ribs and cable troughs under grating are in the texture; here, two floor lamps in cages are added with the lights
    return sh


def build_seat(A):
    s = A.mesh("SEAT")
    base_z = 0.30
    bevel_box(s, F((0, 0.02, base_z + 0.12)), (0.46, 0.56, 0.24), METAL, 0.012, tile=0.5)                  # pedestal
    bevel_box(s, F((0, 0.14, 0.545)), (0.54, 0.62, 0.11), FABRIC, 0.03, tile=0.5)                         # pan
    for sx in (-1, 1):
        bevel_box(s, F((sx * 0.30, 0.12, 0.62)), (0.07, 0.60, 0.16), FABRIC, 0.025, tile=0.5)              # bolsters
        bevel_box(s, F((sx * 0.30, 0.06, 0.50)), (0.05, 0.50, 0.04), METAL, 0.01, tile=0.5)
    ang = math.radians(24)
    bf = F((0, -0.05, 0.60), (0, math.cos(ang), math.sin(ang)), (0, -math.sin(ang), math.cos(ang)))
    bevel_box(s, bf, (0.54, 0.12, 0.92), FABRIC, 0.035, centre=(0, 0, 0.46), tile=0.5)                     # back
    bevel_box(s, bf, (0.42, 0.04, 0.78), METAL, 0.01, centre=(0, -0.07, 0.46), tile=0.5)
    hf = F(bf.p(0, 0.0, 1.0), bf.f, bf.u)
    bevel_box(s, hf, (0.36, 0.11, 0.24), FABRIC, 0.04, tile=0.5)                                             # headrest
    for sx in (-1, 1):
        cyl(s, hf.p(sx * 0.17, 0.0, -0.20), hf.p(sx * 0.17, 0.0, -0.45), 0.012, segs=8, mat=METAL)             # headrest posts
        # armrests
        bevel_box(s, F((sx * 0.37, 0.10, 0.66)), (0.08, 0.50, 0.05), METAL, 0.012, tile=0.5)
        cyl(s, V(sx * 0.37, -0.12, 0.60), V(sx * 0.37, -0.12, 0.38), 0.02, segs=8, mat=METAL)
        # lap harness: two straps from the shoulders over the back to a buckle
        path = np.array([bf.p(sx * 0.16, 0.07, 0.95), bf.p(sx * 0.17, 0.07, 0.55), bf.p(sx * 0.15, 0.07, 0.10), V(sx * 0.08, 0.28, 0.62)])
        tube(s, path, 0.022, 6, FABRIC, caps=True, smooth=True, tile=0.2)
    bevel_box(s, F((0, 0.34, 0.60)), (0.10, 0.08, 0.04), METAL, 0.01, tile=0.3)                              # buckle
    # stick and throttle on the armrests
    cyl(s, V(0.37, 0.12, 0.68), V(0.37, 0.14, 0.90), 0.016, 0.012, 8, METAL)
    bevel_box(s, F((0.37, 0.145, 0.93), (0, 1, 0.2), (0, -0.2, 1)), (0.05, 0.05, 0.07), RUBBER, 0.015, tile=0.3)
    bevel_box(s, F((-0.37, 0.05, 0.71)), (0.06, 0.16, 0.05), RUBBER, 0.012, tile=0.3)
    return s


def build_dash(A, lay):
    d = A.mesh("DASH")
    # The wedge under the canopy: its top slopes down toward the glass so the seated view never sees it.
    ang = math.atan2(0.62 - 0.95, 1.25 - 0.55)
    fr = F((0, 0.90, 0.78), (0, math.cos(ang), math.sin(ang)), (0, -math.sin(ang), math.cos(ang)))
    bevel_box(d, fr, (1.60, 0.82, 0.20), PLATE, 0.02, tile=1.0)
    # the main display, set into the slope, facing up and back toward the standing pilot
    n_up = fr.u
    c = fr.p(0, 0.0, 0.103)
    # the viewer is behind and above: the frame's fwd points into the console (down the slope), up points toward the pilot's top
    sf = F(c + n_up * 0.004, -n_up, fr.f)
    # bezel (bio)
    box_bezel(d, sf, 0.80, 0.30, 0.03)
    disp = A.mesh("DISPLAY_dash")
    quad_face(disp, sf, 0.74, 0.25, SCREEN, centre=(0, 0.0, 0))
    lay["displays"].append({"name": "DISPLAY_dash", "w": 1024, "h": 352, "aspect": 0.74 / 0.25})
    # switch banks either side
    for sx in (-1, 1):
        bevel_box(d, F(fr.p(sx * 0.62, 0.0, 0.11), fr.f, fr.u), (0.20, 0.50, 0.025), TRIM, 0.006, tile=0.5)
    return d


def box_bezel(mb, frame, w, h, wall, mat=BIO):
    """A raised ribbed bezel round a display: four bevel boxes, hard and dark."""
    d = 0.026
    bevel_box(mb, frame, (w + 2 * wall, d, wall), mat, 0.008, centre=(0, 0, h * 0.5 + wall * 0.5), tile=0.4)
    bevel_box(mb, frame, (w + 2 * wall, d, wall), mat, 0.008, centre=(0, 0, -h * 0.5 - wall * 0.5), tile=0.4)
    bevel_box(mb, frame, (wall, d, h), mat, 0.008, centre=(w * 0.5 + wall * 0.5, 0, 0), tile=0.4)
    bevel_box(mb, frame, (wall, d, h), mat, 0.008, centre=(-w * 0.5 - wall * 0.5, 0, 0), tile=0.4)


def build_side_consoles(A, lay):
    names = {-1: "left", 1: "right"}
    for sx in (-1, 1):
        mb = A.mesh("DASH")
        # a wall-hung ledge on the dais, top sloped toward the pilot
        ang = math.radians(16)
        fr = F((sx * (WALL_X - 0.15), 0.10, 0.98), (0, math.cos(ang), math.sin(ang)), (0, -math.sin(ang), math.cos(ang)))
        bevel_box(mb, fr, (0.24, 1.10, 0.16), PLATE, 0.015, tile=1.0)
        # display
        c = fr.p(0, 0.15, 0.081)
        sf = F(c + fr.u * 0.004, -fr.u, fr.f)
        box_bezel(mb, sf, 0.17, 0.30, 0.022)
        disp = A.mesh(f"DISPLAY_{names[sx]}")
        quad_face(disp, sf, 0.16, 0.285, SCREEN)
        lay["displays"].append({"name": f"DISPLAY_{names[sx]}", "w": 384, "h": 640, "aspect": 0.16 / 0.285})
        # switch strip
        bevel_box(mb, F(fr.p(0, -0.30, 0.088), fr.f, fr.u), (0.20, 0.30, 0.02), TRIM, 0.005, tile=0.5)
        bevel_box(mb, F((sx * (WALL_X - 0.08), 0.10, 0.62)), (0.12, 1.00, 0.9), PLATE, 0.015, tile=1.0)                  # pedestal against the wall
    return


def build_table(A, lay):
    t = A.mesh("TABLE")
    c = V(TABLE_C[0], TABLE_C[1], 0.0)
    # a hexagonal pedestal table at waist height with a sloping bezel round a flat display
    top_z = 0.98
    cyl(t, V(c[0], c[1], 0.0), V(c[0], c[1], top_z - 0.08), 0.20, 0.17, 12, METAL)
    cyl(t, V(c[0], c[1], 0.0), V(c[0], c[1], 0.10), 0.30, 0.20, 12, BIO)
    for i in range(6):
        a = i / 6.0 * 2 * math.pi
        cyl(t, V(c[0] + 0.1 * math.sin(a), c[1] + 0.1 * math.cos(a), top_z - 0.1), V(c[0] + 0.30 * math.sin(a), c[1] + 0.30 * math.cos(a), top_z - 0.16), 0.022, 0.02, 8, BIO)
    # the collar: a smooth ribbed ring rising to the display
    rings = []
    for r, z in ((TABLE_R, top_z - 0.06), (TABLE_R + 0.02, top_z), (TABLE_R - 0.04, top_z + 0.045), (TABLE_R - 0.095, top_z + 0.035)):
        rings.append([V(c[0] + r * math.sin(a), c[1] + r * math.cos(a), z) for a in np.linspace(0, 2 * math.pi, 48, endpoint=False)])
    loft(t, np.array(rings), BIO, smooth=True, flip=False, tile=0.5, detail=0.25)
    t.disc(F((c[0], c[1], 0.0), (0, 0, 1), (0, 1, 0)), TABLE_R - 0.095, top_z + 0.036, METAL, segs=48, facing=1.0, tile=1.0)
    sc = A.mesh("DISPLAY_table")
    sf = F((c[0], c[1], top_z + 0.040), (0, 0, -1), (0, -1, 0))     # the viewer stands forward of the table, looks down and aft: screen-up is aft (-y)
    quad_face(sc, sf, 0.70, 0.70, SCREEN, centre=(0, 0, 0))
    lay["displays"].append({"name": "DISPLAY_table", "w": 1024, "h": 1024, "aspect": 1.0})
    # a lamp arm and a clip rail on the table's edge
    bevel_box(t, F((c[0] + 0.33, c[1] - 0.26, top_z + 0.02)), (0.06, 0.20, 0.03), METAL, 0.008, tile=0.3)
    return c


def build_rack(A, lay):
    r = A.mesh("RACK")
    x = -WALL_X + 0.02
    yc = -1.30
    # a cabinet on the port wall: 2 gun bays on top, 2 missile bays below, each 0.9 x 0.17 wide in the wall plane
    bevel_box(r, F((x + 0.11, yc, 1.05)), (0.22, 1.02, 1.55), PLATE, 0.02, tile=1.0)
    bays = [("gun0", yc - 0.24, 1.55, "gun"), ("gun1", yc + 0.24, 1.55, "gun"), ("msl0", yc - 0.24, 0.86, "missile"), ("msl1", yc + 0.24, 0.86, "missile")]
    bay_info = []
    for name, by, bz, kind in bays:
        # a recess is faked with a dark bezel and a cradle; the module sits on its pivot
        fr = F((x + 0.225, by, bz), (-1, 0, 0), (0, 0, 1))      # the viewer looks along -x, toward the port wall; up is z
        box_bezel(r, fr, 0.34, 0.17 if kind == "gun" else 0.24, 0.025)
        bevel_box(r, F((x + 0.20, by, bz - (0.12 if kind == "gun" else 0.16))), (0.05, 0.40, 0.02), METAL, 0.006, tile=0.3)
        bevel_box(r, F((x + 0.20, by, bz + (0.12 if kind == "gun" else 0.16))), (0.05, 0.40, 0.02), METAL, 0.006, tile=0.3)
        # the label display above each bay
        lf = F((x + 0.226, by, bz + (0.17 if kind == "gun" else 0.22)), (-1, 0, 0), (0, 0, 1))
        disp = A.mesh(f"DISPLAY_rack_{name}")
        quad_face(disp, lf, 0.30, 0.07, SCREEN)
        lay["displays"].append({"name": f"DISPLAY_rack_{name}", "w": 384, "h": 96, "aspect": 0.30 / 0.07})
        # the module itself: a pivot so the game can pull it out and seat it
        pv = F((x + 0.18, by, bz), (-1, 0, 0), (0, 0, 1))
        mod = A.mesh(f"RACK_MODULE_{name}", pivot=pv)
        build_module(mod, pv, kind)
        A.empty(f"ANCHOR_rack_{name}_grip", F((x + 0.26, by, bz), (-1, 0, 0), (0, 0, 1)))
        bay_info.append({"id": name, "kind": kind, "pos": to_unity(V(x + 0.26, by, bz)), "grip": f"ANCHOR_rack_{name}_grip"})
    lay["rackBays"] = bay_info
    return r


def build_module(mb, pv, kind):
    """A weapon module as a canister seated in a bay: the pivot's y points into the wall (-x), so the module runs along its local y."""
    if kind == "gun":
        cyl(mb, pv.p(0, -0.18, 0), pv.p(0, 0.20, 0), 0.055, 0.05, 14, METAL)
        cyl(mb, pv.p(0, 0.20, 0), pv.p(0, 0.28, 0), 0.05, 0.03, 12, METAL, tile=0.3)
        for i in range(5):
            torus(mb, F(pv.p(0, -0.14 + i * 0.07, 0), pv.f, pv.u), 0.056, 0.008, BIO, segs=16, tube_segs=6)
        bevel_box(mb, F(pv.p(0.045, -0.02, 0.0), pv.f, pv.u), (0.03, 0.2, 0.05), LAMP_A, 0.004, tile=0.2)
    else:
        cyl(mb, pv.p(0, -0.20, 0), pv.p(0, 0.34, 0), 0.07, 0.065, 14, PLATE)
        cyl(mb, pv.p(0, 0.34, 0), pv.p(0, 0.44, 0), 0.065, 0.02, 12, METAL)
        for i in range(4):
            torus(mb, F(pv.p(0, -0.12 + i * 0.12, 0), pv.f, pv.u), 0.071, 0.01, BIO, segs=16, tube_segs=6)
        bevel_box(mb, F(pv.p(0.07, 0.1, 0.0), pv.f, pv.u), (0.02, 0.18, 0.04), LAMP_R, 0.004, tile=0.2)


def build_breaker(A, lay):
    b = A.mesh("BREAKER")
    x = WALL_X - 0.02
    y = -1.40
    zc = 1.30
    # a guarded slide switch in a ribbed housing on the starboard wall, chest high: up is on, down is off
    bevel_box(b, F((x - 0.07, y, 1.22)), (0.14, 0.46, 0.78), PLATE, 0.015, tile=1.0)
    fr = F((x - 0.145, y, zc), (1, 0, 0), (0, 0, 1))
    box_bezel(b, fr, 0.20, 0.52, 0.03)
    bevel_box(b, F((x - 0.15, y, zc)), (0.016, 0.040, 0.50), METAL, 0.004, tile=0.3)                    # the slot
    bevel_box(b, F((x - 0.15, y, zc - 0.34)), (0.02, 0.30, 0.10), TRIM, 0.004, tile=0.3)
    for dz, mat in ((0.22, LAMP_A), (-0.22, LAMP_R)):
        bevel_box(b, F((x - 0.153, y + 0.075, zc + dz)), (0.010, 0.024, 0.024), mat, 0.003, tile=0.1)
    # the lever rides the slot: its pivot is the slot's middle, the game slides it up and down
    pv = F((x - 0.16, y, zc), (1, 0, 0), (0, 0, 1))
    lev = A.mesh("BREAKER_LEVER", pivot=pv)
    bevel_box(lev, pv, (0.030, 0.034, 0.12), METAL, 0.005, centre=(0.0, 0.0, 0.0), tile=0.3)
    bevel_box(lev, pv, (0.05, 0.11, 0.06), RUBBER, 0.014, centre=(-0.034, 0.0, 0.0), tile=0.3)
    A.empty("ANCHOR_breaker_grip", F((x - 0.205, y, zc + 0.17), (1, 0, 0), (0, 0, 1)))
    A.empty("ANCHOR_breaker_grip_off", F((x - 0.205, y, zc - 0.17), (1, 0, 0), (0, 0, 1)))
    disp = A.mesh("DISPLAY_breaker")
    quad_face(disp, F((x - 0.152, y, zc + 0.36), (1, 0, 0), (0, 0, 1)), 0.24, 0.10, SCREEN)
    lay["displays"].append({"name": "DISPLAY_breaker", "w": 384, "h": 160, "aspect": 2.4})
    return b


def build_panel(A, lay, name, side, y, z, w, h, label):
    """A repair access panel: a hatch hinged on its lower edge, and behind it a socket of living tissue with a module that is pressed home."""
    p = A.mesh(f"PANEL_{name}")
    x = side * (WALL_X - 0.02)
    inward = -side
    fr = F((x + inward * 0.012, y, z), (-inward, 0, 0), (0, 0, 1))        # fwd points into the wall: the viewer's frame
    box_bezel(p, fr, w, h, 0.03)
    # the recess: a dark tissue-lined socket
    soc = A.mesh(f"PANEL_{name}_SOCKET")
    depth = 0.10
    for dz in (-0.22, 0.0, 0.22):
        pass
    bevel_box(soc, F((x + inward * 0.002, y, z)), (0.02, w * 0.92, h * 0.9), TISSUE, 0.004, tile=0.3)
    cyl(soc, V(x + inward * 0.01, y - w * 0.28, z), V(x + inward * 0.03, y - w * 0.28, z), 0.05, 0.035, 12, TISSUE)
    cyl(soc, V(x + inward * 0.01, y + w * 0.28, z), V(x + inward * 0.03, y + w * 0.28, z), 0.05, 0.035, 12, TISSUE)
    for k in range(3):
        yy = y + (k - 1) * w * 0.28
        path = np.array([V(x + inward * 0.01, yy, z - h * 0.4), V(x + inward * 0.03, yy + 0.02, z), V(x + inward * 0.012, yy - 0.01, z + h * 0.4)])
        tube(soc, path, 0.012, 6, TISSUE, caps=True, smooth=True, tile=0.2)
    # the module: a ribbed cylinder lying askew in its socket until it is pressed in
    pv = F((x + inward * 0.05, y, z), (-inward, 0, 0), (0, 0, 1))
    mod = A.mesh(f"PANEL_{name}_MODULE", pivot=pv)
    cyl(mod, pv.p(0, -0.04, 0), pv.p(0, 0.22, 0), 0.058, 0.05, 14, BIO)
    for i in range(5):
        torus(mod, F(pv.p(0, 0.0 + i * 0.04, 0), pv.f, pv.u), 0.061, 0.007, METAL, segs=16, tube_segs=6)
    cyl(mod, pv.p(0, -0.10, 0), pv.p(0, -0.04, 0), 0.03, 0.058, 12, METAL)
    # knit: the glowing tissue that closes round the module while it seats (the game fades it in)
    knit = A.mesh(f"PANEL_{name}_KNIT", pivot=pv)
    torus(knit, F(pv.p(0, 0.02, 0), pv.f, pv.u), 0.07, 0.014, GLYPH, segs=20, tube_segs=6)
    torus(knit, F(pv.p(0, 0.09, 0), pv.f, pv.u), 0.066, 0.012, GLYPH, segs=20, tube_segs=6)
    # the hatch: hinged on its lower edge, a plate with a handle, hanging open when the panel is in use
    hinge = F((x + inward * 0.03, y, z - h * 0.5), (-inward, 0, 0), (0, 0, 1))
    door = A.mesh(f"PANEL_{name}_DOOR", pivot=hinge)
    # drawn closed, in the wall plane, hinge at its lower edge: the pivot's local x runs along y... for a wall on +x this is the hinge axis
    bevel_box(door, F((x + inward * 0.034, y, z)), (0.026, w + 0.04, h + 0.04), PLATE, 0.012, tile=1.0)
    bevel_box(door, F((x + inward * 0.052, y, z + h * 0.30)), (0.02, 0.18, 0.03), METAL, 0.006, tile=0.3)
    bevel_box(door, F((x + inward * 0.040, y, z - h * 0.25), ), (0.012, w * 0.7, 0.08), TRIM, 0.003, tile=0.3)
    A.empty(f"ANCHOR_panel_{name}_grip", F((x + inward * 0.10, y, z), (-inward, 0, 0), (0, 0, 1)))
    disp = A.mesh(f"DISPLAY_panel_{name}")
    quad_face(disp, F((x + inward * 0.030, y, z + h * 0.5 + 0.075), (-inward, 0, 0), (0, 0, 1)), w, 0.09, SCREEN)
    lay["displays"].append({"name": f"DISPLAY_panel_{name}", "w": 384, "h": 128, "aspect": w / 0.09})
    return p


def build_aft(A, lay):
    m = A.mesh("CABIN_SHELL")
    # the aft hatch: a round hatch with a wheel, off to the starboard side of the bulkhead
    hf = F((0.62, Y_AFT + 0.02, 1.15), (0, -1, 0), (0, 0, 1))
    torus(m, F((0.62, Y_AFT + 0.03, 1.15), (0, 1, 0), (0, 0, 1)), 0.42, 0.045, METAL, segs=36, tube_segs=10)
    m.disc(F((0.62, 0, 1.15)), 0.40, Y_AFT + 0.05, PLATE, segs=36, facing=1.0, tile=1.0)
    for i in range(3):
        a = i * 2 * math.pi / 3
        cyl(m, V(0.62, Y_AFT + 0.06, 1.15), V(0.62 + 0.30 * math.sin(a), Y_AFT + 0.06, 1.15 + 0.30 * math.cos(a)), 0.016, segs=8, mat=METAL)
    torus(m, F((0.62, Y_AFT + 0.06, 1.15), (0, 1, 0), (0, 0, 1)), 0.30, 0.016, METAL, segs=28, tube_segs=6)
    cyl(m, V(0.62, Y_AFT + 0.02, 1.15), V(0.62, Y_AFT + 0.08, 1.15), 0.05, segs=10, mat=METAL)
    return m


def build_ceiling(A):
    c = A.mesh("CABIN_SHELL")
    # conduits and a cable tray along the roof, and the bio spine
    ys = np.linspace(Y_AFT + 0.04, Y_CANOPY - 0.05, 14)
    for x, dz, r, mat in ((-0.30, 0.08, 0.035, METAL), (-0.45, 0.14, 0.028, RUBBER), (0.34, 0.08, 0.04, METAL), (0.50, 0.17, 0.025, RUBBER)):
        zr = np.array([roof_z(y) - dz - 0.02 * abs(x) * 0 for y in ys])
        tube(c, np.stack([np.full(len(ys), x), ys, zr], axis=1), r, 10, mat, caps=True, smooth=True)
    for y in np.arange(-2.2, 0.0, 0.18):
        bevel_box(c, F((0.0, y, roof_z(y) - 0.05)), (0.20, 0.03, 0.02), BIO, 0.005, tile=0.2)
    sy = np.linspace(Y_AFT + 0.1, Y_CANOPY - 0.1, 12)
    tube(c, np.stack([np.zeros(len(sy)), sy, np.array([roof_z(y) - 0.03 for y in sy])], axis=1), 0.07, 12, BIO, caps=True, smooth=True, tile=0.3)


def anchors_and_layout(A, lay, table_c):
    seat_eye = EYE
    A.empty("ANCHOR_seat_eye", F(EYE, (0, 1, 0), (0, 0, 1)))
    A.empty("ANCHOR_stand", F(STAND_AT, (0, -1, 0), (0, 0, 1)))
    A.empty("ANCHOR_table_view", F((table_c[0], table_c[1] + 0.62, 1.64), (0, -0.6, -0.8), (0, -0.8, 0.6)))
    lay["eye"] = {"pos": to_unity(EYE), "forward": [0.0, 0.0, 1.0]}
    lay["stand"] = {"pos": to_unity(STAND_AT), "faceYawDeg": 180.0}
    lay["eyeHeights"] = {"stand": EYE_STAND, "crouch": EYE_CROUCH, "seated": float(EYE[2])}
    lay["levels"] = [{"x0": a, "x1": b, "z0": c, "z1": d, "y": e} for a, b, c, d, e in LEVELS]
    lay["obstacles"] = [
        {"x": 0.0, "z": 0.05, "r": 0.34, "why": "the seat"},
        {"x": float(table_c[0]), "z": float(table_c[1]), "r": TABLE_R + 0.06, "why": "the plan table"},
    ]
    lay["bounds"] = {"min": to_unity(V(-WALL_X, Y_AFT, 0.0)), "max": to_unity(V(WALL_X, Y_FRONT, 2.4))}
    lay["interactables"] = [
        {"id": "seat", "kind": "seat", "pos": to_unity(V(0.0, 0.0, 0.6)), "stand": [0.0, float(STAND_AT[1])], "faceYawDeg": 0.0, "radius": 1.0},
        {"id": "table", "kind": "table", "pos": to_unity(V(table_c[0], table_c[1], 0.98)), "stand": [float(table_c[0]), float(table_c[1]) + 0.94], "faceYawDeg": 180.0, "radius": 0.9, "grip": "ANCHOR_table_view"},
        {"id": "breaker", "kind": "breaker", "pos": to_unity(V(WALL_X - 0.2, -1.40, 1.30)), "stand": [WALL_X - 0.68, -1.25], "faceYawDeg": 90.0, "radius": 0.8, "grip": "ANCHOR_breaker_grip"},
        {"id": "panel_engine", "kind": "panel", "pos": to_unity(V(WALL_X - 0.1, -0.5, 0.84)), "stand": [WALL_X - 0.50, -0.5], "faceYawDeg": 90.0, "radius": 0.8, "grip": "ANCHOR_panel_engine_grip", "crouch": True},
        {"id": "panel_arms", "kind": "panel", "pos": to_unity(V(-WALL_X + 0.1, 0.05, 1.20)), "stand": [-WALL_X + 0.50, 0.05], "faceYawDeg": -90.0, "radius": 0.8, "grip": "ANCHOR_panel_arms_grip", "crouch": True},
    ]
    for b in lay["rackBays"]:
        lay["interactables"].append({"id": "rack_" + b["id"], "kind": "rack", "bay": b["id"], "baykind": b["kind"], "pos": b["pos"],
                                     "stand": [-WALL_X + 0.58, -1.46] if b["pos"][2] < -1.3 else [-WALL_X + 0.64, -1.00], "faceYawDeg": -90.0, "radius": 0.9, "grip": b["grip"],
                                     "crouch": b["kind"] == "missile"})


def lights(lay, table_c):
    """Lights the game builds (HDRP point and spot lights, no shadows unless marked): name, kind, position (cabin, Blender axes), colour, intensity (candela), range."""
    L = []

    def add(name, kind, pos, colour, cd, rng, group, shadow=False, spot=None):    # cd: glow, in screen units on a mid-grey surface a metre away
        d = {"name": name, "kind": kind, "pos": to_unity(V(*pos)), "colour": colour, "glow": cd, "range": rng, "group": group, "shadow": shadow}
        if spot:
            d["spot"] = {"dir": to_unity(V(*spot[0])), "angle": spot[1]}
        L.append(d)
    amber, teal, white, red = [1.0, 0.62, 0.28], [0.35, 0.85, 0.80], [0.95, 0.97, 1.0], [1.0, 0.10, 0.06]
    add("dash", "point", (0.0, 0.62, 1.12), amber, 0.16, 2.6, "instrument")
    add("left", "point", (-0.95, 0.0, 1.25), amber, 0.10, 1.8, "instrument")
    add("right", "point", (0.95, 0.0, 1.25), amber, 0.10, 1.8, "instrument")
    add("table", "point", (table_c[0], table_c[1], 1.35), teal, 0.20, 2.8, "instrument", shadow=True)
    add("rack", "point", (-0.90, -1.30, 1.45), amber, 0.12, 1.6, "instrument")
    add("breaker", "point", (0.95, -1.40, 1.75), amber, 0.10, 1.3, "instrument")
    add("panel_engine", "point", (0.95, -0.50, 0.95), teal, 0.08, 1.2, "instrument")
    add("panel_arms", "point", (-0.95, 0.05, 1.55), teal, 0.08, 1.2, "instrument")
    add("emerg_fore", "point", (0.0, -0.20, 2.40), red, 0.20, 3.0, "emergency")
    add("emerg_aft", "point", (0.0, -1.60, 2.12), red, 0.20, 3.0, "emergency")
    add("emerg_floor_l", "point", (-0.95, -1.20, 0.25), red, 0.10, 1.5, "emergency")
    add("emerg_floor_r", "point", (0.95, -1.20, 0.25), red, 0.10, 1.5, "emergency")
    add("work", "spot", (0.0, -1.20, 2.20), white, 0.4, 3.4, "work", shadow=True, spot=((0.0, 0.2, -1.0), 85.0))
    lay["lights"] = L


# ------------------------------------------------------------------------------------------------ the detail pass
# Everything below is hardware and graft laid on the structure above: wall panels with their placards, cable runs and handrails, lamp cages, the bio spine's glyph
# strip, tendrils between the grafted tech and the ribs, hazard marks on the steps, the labels at each station. Deterministic (a seeded generator), no noise.

RIBS = [-2.18, -1.74, -1.30, -0.86, -0.42, 0.02]


def arch_point(y, z, side=1):
    """The cabin's inner skin at length y and height z on the right (side +1) or left (-1): the point, the inward normal and the way up the surface."""
    h = np.array(arch_half(y), np.float64)
    for i in range(len(h) - 1):
        z0, z1 = h[i][1], h[i + 1][1]
        if z1 > z0 and z0 - 1e-9 <= z <= z1 + 1e-9:
            t = (z - z0) / (z1 - z0)
            x = h[i][0] + (h[i + 1][0] - h[i][0]) * t
            tang = h[i + 1] - h[i]
            tang = tang / np.linalg.norm(tang)
            n = V(-tang[1] * side, 0.0, tang[0])               # inward (right wall: -x), tilted with the slope
            up = V(tang[0] * side, 0.0, tang[1])
            return V(side * x, y, z), n, up
    raise ValueError((y, z))


def wall_piece(mb, side, name, y, z, scale=1.0, lift=0.004, mat=None):
    """A trim-sheet piece laid on the wall, centred at (y, z), reading correctly for someone standing in the cabin."""
    rect_ = rect(name)
    w, h = size_m(name)
    w, h = w * scale, h * scale
    p, n, up = arch_point(y, z, side)
    xdir = np.cross(-n, up)
    org = p + n * lift - xdir * (w * 0.5) - up * (h * 0.5)
    plate(mb, org, xdir, up, w, h, rect_, TRIM if mat is None else mat)


def floor_piece(mb, name, x, y, z, yaw=0.0, scale=1.0):
    """A trim piece lying on a floor (facing up), its width along +x rotated by yaw (degrees)."""
    rect_ = rect(name)
    w, h = size_m(name)
    w, h = w * scale, h * scale
    a = math.radians(yaw)
    xdir, ydir = V(math.cos(a), math.sin(a), 0.0), V(-math.sin(a), math.cos(a), 0.0)
    plate(mb, V(x, y, z + 0.003) - xdir * (w * 0.5) - ydir * (h * 0.5), xdir, ydir, w, h, rect_, TRIM)


def band(mb, ring_a, ring_b, mat, u_tile, flip=False, voff=0.0, vspan=1.0):
    """A strip between two rows of points (same count), UV u in repeats of ``u_tile`` metres along the row, v across."""
    A_, B_ = np.asarray(ring_a, np.float64), np.asarray(ring_b, np.float64)
    n = len(A_)
    seg = np.linalg.norm(np.diff(A_, axis=0), axis=1)
    u = np.r_[0.0, np.cumsum(seg)] / u_tile
    P = np.stack([A_[:-1], A_[1:], B_[1:], B_[:-1]], axis=1)
    UV = np.zeros((n - 1, 4, 2))
    UV[:, 0, 0] = u[:-1]; UV[:, 1, 0] = u[1:]; UV[:, 2, 0] = u[1:]; UV[:, 3, 0] = u[:-1]
    UV[:, 0, 1] = voff; UV[:, 1, 1] = voff; UV[:, 2, 1] = voff + vspan; UV[:, 3, 1] = voff + vspan
    N = mnmesh.norm(mnmesh.cross(P[:, 2] - P[:, 0], P[:, 3] - P[:, 1]))
    if flip:
        P = P[:, ::-1]; UV = UV[:, ::-1]; N = -N
    mb.quads(P, UV, UV * 0.5, mat, N)


def _ball_m(mb, c, r, mat=None):
    prof = [(r * math.sin(t), r * math.cos(t)) for t in np.linspace(math.pi, 0, 6)]
    mb.lathe(prof, F(c), METAL if mat is None else mat, segs=8, tile=0.1, crease=100.0)


def bolt_row(mb, p0, p1, n, spacing=0.07, r=0.007, mat=None, lift=0.004):
    """A row of bolt heads along a line, standing off a surface with normal n."""
    p0, p1 = np.asarray(p0, np.float64), np.asarray(p1, np.float64)
    L = np.linalg.norm(p1 - p0)
    k = max(2, int(L / spacing) + 1)
    n = norm(n)
    for i in range(k):
        c = p0 + (p1 - p0) * (i / (k - 1)) + n * lift
        fr = Frame(c, n, V(0, 0, 1) if abs(n[2]) < 0.9 else V(1, 0, 0))
        mb.lathe([(0.0, 0.0), (r, 0.0), (r, 0.003), (r * 0.7, 0.005), (0.0, 0.005)], fr, mat if mat is not None else METAL, segs=8, tile=0.1, crease=30.0)


def lamp_cage(mb, lamp_mb, centre, normal, r=0.07, mat_lamp=None):
    """A caged lamp standing off a surface: a lens, a wire cage of rings and bars, a base ring."""
    n = norm(normal)
    fr = Frame(np.asarray(centre, np.float64), n, V(0, 1.0, 0) if abs(n[1]) < 0.9 else V(1.0, 0, 0))
    cyl(lamp_mb, fr.p(0, 0.0, 0), fr.p(0, r * 0.7, 0), r * 0.62, r * 0.5, 14, mat_lamp if mat_lamp is not None else LAMP_R, caps=True, smooth=True)
    torus(mb, fr, r * 0.95, r * 0.07, METAL, segs=18, tube_segs=5)
    for k in range(6):
        a = k * math.pi / 3
        cyl(mb, fr.p(r * 0.95 * math.sin(a), 0.0, r * 0.95 * math.cos(a)), fr.p(r * 0.62 * math.sin(a), r * 0.95, r * 0.62 * math.cos(a)), r * 0.045, segs=5, mat=METAL)
    torus(mb, Frame(fr.p(0, r * 0.95, 0), fr.f, fr.u), r * 0.62, r * 0.06, METAL, segs=14, tube_segs=5)


def handrail(mb, p0, p1, n, posts=3, r=0.017, standoff=0.07):
    """A handrail between two points, standing off a surface (normal n) on posts."""
    p0, p1 = np.asarray(p0, np.float64), np.asarray(p1, np.float64)
    n = norm(n)
    a, b = p0 + n * standoff, p1 + n * standoff
    cyl(mb, a, b, r, segs=10, mat=METAL)
    for i in range(posts):
        t = i / max(1, posts - 1)
        q = p0 + (p1 - p0) * t
        cyl(mb, q + n * 0.002, q + n * standoff, r * 0.8, segs=8, mat=METAL)
        torus(mb, Frame(q + n * 0.004, n, V(0, 0, 1) if abs(n[2]) < 0.9 else V(1, 0, 0)), r * 1.6, r * 0.5, METAL, segs=10, tube_segs=4)
    for q in (a, b):
        _ball_m(mb, q, r * 1.02)


def cable_run(mb, pts, r=0.012, mat=None, clip_every=0.28):
    """A cable through points (smooth), with a clip every so often."""
    pts = np.asarray(pts, np.float64)
    tube(mb, pts, r, 8, RUBBER if mat is None else mat, caps=True, smooth=True, tile=0.2)
    for i in range(len(pts) - 1):
        d = pts[i + 1] - pts[i]
        L = np.linalg.norm(d)
        if L < 1e-6:
            continue
        for t in np.arange(clip_every * 0.5, L, clip_every):
            c = pts[i] + d / L * t
            fr = Frame(c, d / L, V(0, 0, 1) if abs(d[2] / L) < 0.9 else V(1, 0, 0))
            torus(mb, fr, r * 1.35, r * 0.34, METAL, segs=10, tube_segs=4)


def build_walls(A, lay):
    rng = np.random.default_rng(SEED)
    sh = A.mesh("CABIN_SHELL")
    pan = A.mesh("CABIN_PANELS")
    lamps = A.mesh("CABIN_LAMPS")
    bays = [(RIBS[i] + 0.06, RIBS[i + 1] - 0.06) for i in range(len(RIBS) - 1)]
    names = ["placard_main", "placard_warn", "id_plate_a", "id_plate_b", "arrows", "label_repair"]
    for side in (1, -1):
        for bi, (ya, yb) in enumerate(bays):
            yc = 0.5 * (ya + yb)
            wy = yb - ya
            # an access panel: a shallow bevel box on the wall, with a blank-panel texture over it
            for zc, hh in ((1.00, 0.42), (1.62, 0.30)):
                if rng.random() < 0.55 or bi in (1, 3):
                    p, n, up = arch_point(yc, zc, side)
                    fr = Frame(p + n * 0.012, -n, up)                           # f into the wall, u up the wall
                    bevel_box(pan, fr, (wy * 0.86, 0.024, hh), PLATE, 0.008, tile=1.0)
                    wall_piece(pan, side, "panel_blank", yc, zc, scale=min(0.9, wy * 0.86 / 0.30, hh / 0.30), lift=0.027)
            # a placard, a plate, or an arrow strip
            pick = names[int(rng.integers(0, len(names)))]
            zc = float(rng.choice([0.78, 1.38, 1.9]))
            if rng.random() < 0.8:
                wall_piece(pan, side, pick, yc, zc, scale=min(1.0, wy * 0.9 / size_m(pick)[0]), lift=0.004)
            # bolts on the rib flanks
            for yr in (ya - 0.03, yb + 0.03):
                for zb in (0.62, 1.12, 1.62):
                    p, n, up = arch_point(yr, zb, side)
                    bolt_row(pan, p, p + up * 0.001, n, r=0.008, lift=0.006)
        # a kick plate along the foot of the wall, a handrail at hand height, cable runs above it
        for y0, y1 in ((-2.20, -1.10), (-0.70, 0.0)):
            pa, na, upa = arch_point(y0, 0.62, side)
            pb, nb, upb = arch_point(y1, 0.62, side)
            bevel_box(pan, Frame((pa + pb) * 0.5 + na * 0.014, V(0, 1, 0), V(0, 0, 1)), (0.03, y1 - y0, 0.26), METAL, 0.008, tile=0.6)
            pa, na, _ = arch_point(y0, 1.05, side)
            pb, nb, _ = arch_point(y1, 1.05, side)
            handrail(pan, pa, pb, na, posts=4)
            for z, rr in ((1.42, 0.016), (1.50, 0.011)):
                pa, na, _ = arch_point(y0, z, side)
                pb, nb, _ = arch_point(y1, z, side)
                cable_run(pan, [pa + na * 0.03 + V(0, 0.05, 0), (pa + pb) * 0.5 + na * 0.035 + V(0, 0, 0.012), pb + na * 0.03 - V(0, 0.05, 0)], r=rr)
    # the aft bulkhead: panels, the hatch's labels, a hazard strip, a stowed bottle and bracket, cable
    for xs, zc in ((-0.85, 1.55), (0.0, 1.95), (0.85, 1.65)):
        bevel_box(pan, F((xs, Y_AFT + 0.016, zc)), (0.55, 0.03, 0.42), PLATE, 0.010, tile=1.0)
        plate(pan, V(xs - 0.14, Y_AFT + 0.0335, zc - 0.14), V(1, 0, 0), V(0, 0, 1), 0.28, 0.28, rect("panel_blank"), TRIM)
    plate(pan, V(0.62 - 0.15, Y_AFT + 0.004, 1.74), V(1, 0, 0), V(0, 0, 1), 0.30, 0.10, rect("stencil_aft"), TRIM)
    plate(pan, V(0.62 - 0.12, Y_AFT + 0.004, 0.60), V(1, 0, 0), V(0, 0, 1), 0.24, 0.08, rect("stencil_hatch"), TRIM)
    plate(pan, V(-0.90, Y_AFT + 0.004, 0.52), V(1, 0, 0), V(0, 0, 1), 0.60, 0.05, rect("hazard"), TRIM)
    cyl(pan, V(-0.62, Y_AFT + 0.10, 0.62), V(-0.62, Y_AFT + 0.10, 1.12), 0.075, segs=14, mat=METAL)
    cyl(pan, V(-0.62, Y_AFT + 0.10, 1.12), V(-0.62, Y_AFT + 0.10, 1.20), 0.04, 0.025, 10, METAL)
    for zb in (0.78, 1.0):
        torus(pan, F((-0.62, Y_AFT + 0.10, zb), (0, 0, 1), (0, 1, 0)), 0.082, 0.008, METAL, segs=14, tube_segs=4)
    bevel_box(pan, F((-0.62, Y_AFT + 0.045, 0.90)), (0.20, 0.03, 0.04), METAL, 0.006, tile=0.3)
    for x in (-0.35, -0.28):
        cable_run(pan, [V(x, Y_AFT + 0.04, 0.12), V(x + 0.02, Y_AFT + 0.05, 0.9), V(x - 0.03, Y_AFT + 0.05, 1.6), V(x, Y_AFT + 0.04, 2.0)], r=0.018, clip_every=0.4)
    # emergency lamps in cages along the ceiling and above the hatch
    for y in (-2.0, -1.0, -0.2):
        lamp_cage(sh, lamps, V(0.0, y, roof_z(y) - 0.04), V(0, 0, -1), r=0.075, mat_lamp=LAMP_R)
    lamp_cage(sh, lamps, V(0.62, Y_AFT + 0.02, 1.98), V(0, 1, 0), r=0.06, mat_lamp=LAMP_A)
    # steps: hazard on the nosings, a floor hatch outline, a cable trough along the aisle
    for y, z in ((-1.30, 0.15), (-1.05, 0.30)):
        floor_piece(pan, "hazard", 0.0, y - 0.012, z, 0.0, scale=1.6)
    bevel_box(pan, F((0.0, -1.75, 0.012)), (0.70, 0.55, 0.024), METAL, 0.01, tile=0.6)
    for k in range(4):
        _ball_m(pan, V(-0.30 + k * 0.2, -1.75 - 0.2, 0.026), 0.012)
    cable_run(pan, [V(0.5, -0.9, 0.31), V(0.4, -0.2, 0.31), V(0.3, 0.15, 0.62), V(0.15, 0.2, 0.62)], r=0.016, clip_every=0.35)


def build_graft(A, lay):
    """The alien-ally graft laid over the human ship: glyph strips on the ribs and the spine, tendrils from the spine into the tech it powers, ganglia at the joints."""
    g = A.mesh("CABIN_GRAFT")
    bio = A.mesh("CABIN_SHELL")
    # glyph strips: a thin lit band on the inner crest of every other rib, over the roof and shoulders only
    for yy in RIBS[0::2] + [0.40, 1.18]:
        sect = poly3(inset_poly(arch(yy), 0.097), yy)
        ring_a = sect[3:14]
        a = np.array([[p[0], p[1] - 0.012, p[2]] for p in ring_a])
        b = np.array([[p[0], p[1] + 0.012, p[2]] for p in ring_a])
        band(g, a, b, GLYPH, 1.0, flip=False)
    # the spine's own strip, on its underside
    sy = np.linspace(Y_AFT + 0.12, Y_CANOPY - 0.12, 40)
    left = np.array([[-0.025, y, roof_z(y) - 0.105] for y in sy])
    right = np.array([[0.025, y, roof_z(y) - 0.105] for y in sy])
    band(g, left, right, GLYPH, 0.8, flip=True)
    # tendrils: from the spine down to the table's collar and to the dash, smooth, dark, a little wet
    for sx in (-0.20, 0.0, 0.20):
        pts = np.array([V(sx * 0.5, -2.05, roof_z(-2.05) - 0.09), V(sx * 1.1, -2.14, roof_z(-2.1) - 0.40), V(sx * 1.5, -2.22, 1.55), V(sx * 1.7, -2.22, 1.12), V(sx * 1.4, -2.17, 1.03)])
        tube(bio, pts, np.array([0.022, 0.020, 0.018, 0.016, 0.014]), 8, BIO, caps=True, smooth=True, tile=0.3)
    for sx in (-0.45, 0.45):
        pts = np.array([V(sx * 0.2, 0.0, roof_z(0.0) - 0.09), V(sx * 0.6, 0.45, roof_z(0.4) - 0.30), V(sx * 1.1, 0.80, 1.55), V(sx * 1.2, 1.05, 1.0), V(sx * 1.15, 1.05, 0.90)])
        tube(bio, pts, np.array([0.024, 0.022, 0.020, 0.018, 0.014]), 8, BIO, caps=True, smooth=True, tile=0.3)
    # ganglia: smooth nodules where the ribs meet the shoulders
    for yy in RIBS:
        for sx in (-1, 1):
            p, n, up = arch_point(yy, 1.55, sx)
            fr = Frame(p + n * 0.04, n, V(0, 1, 0))
            bio.lathe([(0.0, -0.025), (0.035, -0.012), (0.05, 0.015), (0.03, 0.04), (0.0, 0.05)], fr, BIO, segs=10, tile=0.2, crease=100.0)


def build_station_details(A, lay):
    """Labels and hardware at each station: the breaker, the rack, the two panels, the table, the dash and the consoles."""
    pan = A.mesh("CABIN_PANELS")
    # breaker: label above, the placard, a hazard band
    x = WALL_X - 0.02
    yb = -1.40
    wall_piece(pan, 1, "label_breaker", yb, 0.82, scale=0.9, lift=0.0)
    wall_piece(pan, 1, "placard_main", yb, 1.82, scale=0.9, lift=0.0)
    plate(pan, V(x - 0.152, yb + 0.23, 0.98), V(0, -1, 0), V(0, 0, 1), 0.46, 0.04, rect("hazard"), TRIM)
    # rack: label, hazard
    xr = -WALL_X + 0.02
    plate(pan, V(xr + 0.226, -1.30 - 0.17, 0.30), V(0, 1, 0), V(0, 0, 1), 0.34, 0.09, rect("label_rack"), TRIM)
    plate(pan, V(xr + 0.226, -1.30 - 0.30, 2.12), V(0, 1, 0), V(0, 0, 1), 0.60, 0.05, rect("hazard"), TRIM)
    # the two repair panels: instruction labels and a caution plate
    wall_piece(pan, 1, "label_repair", -0.50, 0.98, scale=0.8, lift=0.0)
    wall_piece(pan, -1, "label_repair", 0.05, 1.62, scale=0.8, lift=0.0)
    wall_piece(pan, 1, "placard_warn", -0.50, 0.30, scale=0.7, lift=0.0)
    # side consoles: switch banks and dials on their sloped tops
    ang = math.radians(16)
    for sx in (-1, 1):
        fr = F((sx * (WALL_X - 0.15), 0.10, 0.98), (0, math.cos(ang), math.sin(ang)), (0, -math.sin(ang), math.cos(ang)))
        for name, off in (("switch_bank_a", -0.40), ("dials", 0.45)):
            w, h = size_m(name)
            sc = 0.20 / w if w > 0.20 else 1.0
            c = fr.p(0.0, off, 0.0805)
            xd, yd = fr.r * sx, fr.f
            plate(pan, c - xd * (w * sc * 0.5) - yd * (h * sc * 0.5), xd, yd, w * sc, h * sc, rect(name), TRIM)
    # the dash wedge: switch banks, sliders and dials either side of the main display
    ang2 = math.atan2(0.62 - 0.95, 1.25 - 0.55)
    fr = F((0, 0.90, 0.78), (0, math.cos(ang2), math.sin(ang2)), (0, -math.sin(ang2), math.cos(ang2)))
    for name, x0, y0 in (("switch_bank_b", -0.55, 0.0), ("switch_bank_c", 0.55, 0.0), ("sliders", -0.33, -0.28), ("dials", 0.33, -0.28)):
        w, h = size_m(name)
        sc = 0.22 / w
        c = fr.p(x0, y0, 0.1035)
        plate(pan, c - fr.r * (w * sc * 0.5) - fr.f * (h * sc * 0.5), fr.r, fr.f, w * sc, h * sc, rect(name), TRIM)


def main():
    preview = "--preview" in sys.argv
    A = CabinAsset("cabin", MATS, ART, RES, MATERIAL_SET)
    lay = {"format": "monolith-cabin/1", "about": "Written by art_src/cabin/build_cabin.py. Cabin space, Unity axes (x right, y up, z forward); metres.", "displays": []}
    build_shell(A)
    build_floor(A)
    build_seat(A)
    build_dash(A, lay)
    build_side_consoles(A, lay)
    tc = build_table(A, lay)
    build_rack(A, lay)
    build_breaker(A, lay)
    build_panel(A, lay, "engine", 1, -0.50, 0.84, 0.36, 0.40, "ENGINE")
    build_panel(A, lay, "arms", -1, 0.05, 1.20, 0.36, 0.40, "ARMS")
    build_aft(A, lay)
    build_ceiling(A)
    build_walls(A, lay)
    build_graft(A, lay)
    build_station_details(A, lay)
    anchors_and_layout(A, lay, tc)
    lights(lay, tc)
    A.export(lay)
    if preview:
        out = os.environ.get("MN_PREVIEW_DIR", os.path.join(ROOT, "art_src", "cabin", "_preview"))
        cols = {"mn_cab_plate": (0.35, 0.36, 0.33, 1), "mn_cab_bio": (0.06, 0.06, 0.07, 1), "mn_cab_floor": (0.25, 0.25, 0.25, 1), "mn_cab_trim": (0.55, 0.55, 0.5, 1),
                "mn_cab_metal": (0.6, 0.6, 0.62, 1), "mn_cab_fabric": (0.3, 0.28, 0.22, 1), "mn_cab_glass": (0.6, 0.8, 0.9, 1), "mn_cab_screen": (0.1, 0.8, 0.7, 1),
                "mn_cab_glyph": (0.1, 0.9, 0.9, 1), "mn_cab_tissue": (0.5, 0.1, 0.1, 1), "mn_cab_lamp_white": (1, 1, 1, 1), "mn_cab_lamp_amber": (1, 0.6, 0.1, 1),
                "mn_cab_lamp_red": (1, 0.1, 0.1, 1), "mn_cab_rubber": (0.03, 0.03, 0.03, 1)}
        mnpreview.render_views(out, [
            ("aft_to_fore", (0.1, -2.25, 1.7), (0.0, 0.6, 1.2), 32),
            ("fore_to_aft", (0.0, 0.9, 1.7), (-0.1, -1.8, 1.0), 32),
            ("table", (0.3, -1.0, 1.7), (-0.2, -1.9, 0.9), 40),
            ("starboard", (-0.8, -0.8, 1.6), (1.1, -1.2, 1.1), 40),
            ("port", (0.8, -0.8, 1.6), (-1.1, -1.4, 1.1), 40),
            ("seat_eye", (0.0, 0.0, 1.32), (0.0, 1.5, 1.3), 38),
            ("overview", (2.6, -3.4, 2.8), (0.0, -0.3, 1.0), 40),
        ], colours=cols, lens=24.0, hide=(), sun=(0.3, 0.2, -0.9))


main()
