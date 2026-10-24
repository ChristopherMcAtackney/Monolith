"""The Small Mercy (diaspora_cruiser), about 350 m: the diaspora's carrier-cruiser, the ship the player's squadron screens. A patched human hull with the alien allies' living
technology grafted into it, the opposite of Earth's perfect black-and-chrome corvette:

    - a long stack of plated segments in bone, olive, rust and faded-teal paint of different yards, repair plates welded over old damage, rows of lit windows in warm light,
    - the joints between the segments are grown, not built: round dark ribbed necks with teal glyph bands, and a living crest of dark fins along the forward spine,
    - two engine nacelles hung from pylons, each a plain human cowl in front and a dark ribbed shell round a steel bell behind it, with amber flames,
    - four hangar bays (two a side, not level: the asymmetry) lit warm inside, with hazard stripes and a blast door hanging open,
    - flak batteries and anti-fighter turrets on the flanks, decks and keel, two alien beam lances (dorsal forward, ventral aft), the bridge off to port under an arch of
      grown ribs, a reactor that is a glowing teal heart in a cage of ribs, a sensor crest of dark vanes on the prow,
    - on the starboard flank a cargo truss with strapped containers, a radiator wing, a name and a hull number stencilled big.
All of those are named subsystems with wrecks and debris; the hull has damage sites, FX hooks and sampled scorch points (the contract in art_src/PIPELINE.md).

    tools\\blender.ps1 art_src\\diaspora_hull\\build_diaspora_hull_textures.py     (once, or when the textures change)
    tools\\blender.ps1 art_src\\diaspora\\build_diaspora_cruiser.py                (writes the FBX and manifest; then tools\\unity.ps1 Monolith.EditorTools.Setup.All)
    tools\\blender.ps1 art_src\\diaspora\\build_diaspora_cruiser.py -- --preview   (also renders Workbench views)

Axes while building: x right (starboard), y forward (the bow), z up.
"""
import json
import math
import os
import sys

import numpy as np

from blender_lib import mnmesh, mnship, mnkit, mnparts, mnpreview, mnfighter as F, mndiaspora as D, mndiaspora_cap as C
from blender_lib.mnkit import Kit, station, complement, PLATE, TRIM, LIGHTS, ENGINE, CHROME, PLATE_B, PLATE_C, PLUME, PLATE_D, PLATE_E, WEAR, PAINT
from blender_lib.mnkit import LIGHT_WHITE, LIGHT_COLD, LIGHT_RED, LIGHT_GREEN, LIGHT_AMBER, LIGHT_BLUE
from blender_lib.mnmesh import Frame, norm, cross, chamfered_rect, regular_poly
from blender_lib.mndiaspora import BIO, GLYPH, GLASS, STEEL, LIGHT_TEAL

ROOT = os.environ.get("MN_ROOT", os.getcwd())
ART = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "DiasporaShips")
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "ShipArt")
TRIMS = json.load(open(os.path.join(ROOT, "art_src", "diaspora_hull", "trims.json")))
MATERIAL_SET = "Assets/Monolith/Art/DiasporaHull/dia_hull.materials.json"
SHIP_ID = "diaspora_cruiser"
SEED = 3131
ZHAT = np.array([0.0, 0.0, 1.0])
XHAT = np.array([1.0, 0.0, 0.0])
YHAT = np.array([0.0, 1.0, 0.0])

# ------------------------------------------------------------------------------------------------ the hull, aft to bow
st = station
SEG = {
    "tail": [st(-150, 17, 13, 4.5), st(-120, 19, 14, 5.0)],
    "reactor": [st(-116, 24, 17, 5.5), st(-92, 26, 18.5, 6.0), st(-70, 27, 19, 6.0)],
    "main": [st(-48, 31, 22, 6.5), st(-26, 36, 24, 7.0), st(36, 36, 24, 7.0), st(50, 30, 21.5, 6.5)],
    "fwd": [st(66, 28, 19, 6.0), st(98, 26, 18, 5.5), st(118, 21, 14.5, 4.5)],
    "prow": [st(132, 15.5, 11, 4.0), st(162, 11, 8.5, 3.2)],
}
# the grown joints: round ribbed necks between the blocks: (y0, y1, radius, ribs)
NECKS = [(-70.0, -48.0, 15.5, 6), (50.0, 66.0, 16.0, 5), (118.0, 132.0, 11.0, 4), (-120.0, -116.0, 12.0, 1)]
NAC_X = 35.0
NAC_Z = 0.0
WINDOWS = {
    "tail": [(2.0, "windows_slit")],
    "reactor": [(5.0, "windows_a"), (-3.0, "windows_b")],
    "main": [(16.5, "windows_a"), (-16.5, "windows_b")],
    "fwd": [(5.5, "windows_a"), (-1.5, "windows_b")],
}
PLATING = {
    "t": dict(rows=(5, 16), cols=(3.5, 11), height=(0.15, 0.45), sub_prob=0.4, chamfer=0.14),
    "b": dict(rows=(5, 16), cols=(3.5, 11), height=(0.15, 0.4), chamfer=0.14),
    "r": dict(rows=(4, 13), cols=(3, 9), height=(0.12, 0.4), sub_prob=0.3, chamfer=0.12),
    "l": dict(rows=(4, 13), cols=(3, 9), height=(0.12, 0.4), sub_prob=0.3, chamfer=0.12),
    "tr": dict(rows=(5, 12), cols=(2.5, 8), height=(0.14, 0.35), sub_prob=0.25, chamfer=0.12),
    "tl": dict(rows=(5, 12), cols=(2.5, 8), height=(0.14, 0.35), sub_prob=0.25, chamfer=0.12),
    "br": dict(rows=(5, 12), cols=(2.5, 8), height=(0.14, 0.35), chamfer=0.12),
    "bl": dict(rows=(5, 12), cols=(2.5, 8), height=(0.14, 0.35), chamfer=0.12),
}
# hangar bays: (side, y centre, z centre, width along the ship); starboard and port are not level
HANGARS = [("P", 1, -10.0, 1.0, 24.0), ("P", 2, 24.0, -2.0, 24.0), ("S", 1, -14.0, -1.0, 24.0), ("S", 2, 20.0, 0.0, 26.0)]
BAY_H, BAY_D = 13.0, 12.0


def seg_at(name, y):
    sts = SEG[name]
    for a, b in zip(sts[:-1], sts[1:]):
        if a["y"] <= y <= b["y"]:
            return mnkit.lerp_station(a, b, (y - a["y"]) / (b["y"] - a["y"]))
    raise ValueError((name, y))


def top_pt(name, y, x=0.0, lift=0.0):
    s = seg_at(name, y)
    return np.array([x, y, s["cz"] + s["hh"] + lift])


def bottom_pt(name, y, x=0.0, lift=0.0):
    s = seg_at(name, y)
    return np.array([x, y, s["cz"] - s["hh"] - lift])


def flank_pt(name, y, z, side, lift=0.0):
    s = seg_at(name, y)
    return np.array([side * (s["hw"] + lift) + s["cx"], y, z])


def pt_at(patch, z=None, y=None):
    u = 0.0 if z is None else (patch.p00[2] - z) / (patch.p00[2] - patch.p10[2])
    v = 0.0 if y is None else (y - patch.p00[1]) / (patch.p01[1] - patch.p00[1])
    return patch.pt(u, v)


def flank_strips(K, patch, rows, margin=3.0):
    """Window rows on a flank flat, laid in broken runs (7 to 46 m long, with gaps) so no two rows repeat; the gaps hold cable bundles. Returns the u-intervals (metres from
    the top edge) the rows occupy, for the plating to leave free."""
    gaps = []
    n = patch.normal()
    run = norm(cross(ZHAT, n))
    top = patch.p00[2]
    rng = K.rng("win", tuple(np.round(patch.p00, 1)))
    for zc, name in rows:
        ph = K.piece_m(name)[1]
        zb = zc - ph / 2
        if zb < patch.p10[2] + 0.5 or zb + ph > top - 0.5:
            continue
        ya, yb = patch.p00[1] + margin, patch.p01[1] - margin
        if yb - ya < 6:
            continue
        runs = []
        y = ya + rng.uniform(0.0, 6.0)
        while y < yb - 5.0:
            ln = min(rng.uniform(7.0, 40.0), yb - y)
            if ln >= 5.0:
                runs.append((y, y + ln))
            y += ln + rng.uniform(3.0, 18.0)
        if len(runs) > 2 and rng.random() < 0.3:
            runs.pop(int(rng.integers(len(runs))))

        def lay(y0, y1, piece, scale=1.0):
            pa, pb = pt_at(patch, zb, y0), pt_at(patch, zb, y1)
            if np.dot(pb - pa, run) < 0:
                pa, pb = pb, pa
            K.strip(pa, run, n, float(np.linalg.norm(pb - pa)), piece, lift=0.03, scale=scale)
        for y0, y1 in runs:
            lay(y0, y1, name)
        for y0, y1 in complement(ya, yb, runs, 1.5):
            lay(y0, y1, "cable", ph / 0.5)
        gaps.append((top - (zb + ph) - 0.45, top - zb + 0.45))
    return gaps


def plate_region(K, patch, key, style, holes, mb=None):
    """Plate a flat except where ``holes`` (rectangles (u0, u1, v0, v1) in metres on the patch) are: the flat is cut along every hole's edges and the cells that are not in
    a hole are plated."""
    us = sorted(set([0.0, patch.lu] + [min(max(h[i], 0.0), patch.lu) for h in holes for i in (0, 1)]))
    vs = sorted(set([0.0, patch.lv] + [min(max(h[i], 0.0), patch.lv) for h in holes for i in (2, 3)]))
    count = 0
    for i in range(len(us) - 1):
        for j in range(len(vs) - 1):
            ua, ub, va, vb = us[i], us[i + 1], vs[j], vs[j + 1]
            cu, cv = (ua + ub) / 2, (va + vb) / 2
            if any(h[0] <= cu <= h[1] and h[2] <= cv <= h[3] for h in holes) or ub - ua < 1.2 or vb - va < 1.2:
                continue
            K.plate_patch(patch, f"{key}-{i}.{j}", u_range=(ua, ub), v_range=(va, vb), mb=mb, **style)
            if K.lod == 0:
                K.scatter_details(patch, K.last_cells, f"{key}-{i}.{j}")
                K.wear(patch, K.last_cells, f"{key}-{i}.{j}")
                K.edge_wear(patch, K.last_cells, K.last_tones, f"{key}-{i}.{j}")
            count += 1
    return count


def bay_holes(patch, side, name):
    """Rectangles on a flank patch where the hangar bays stand (so the plates leave them free)."""
    holes = []
    if name != "main":
        return holes
    n = patch.normal()
    for sd, idx, yc, zc, w in HANGARS:
        if (sd == "S") != (side > 0):
            continue
        top = patch.p00[2]
        v0 = yc - w / 2 - 2.2 - patch.p00[1]
        v1 = yc + w / 2 + 2.2 - patch.p00[1]
        u0 = top - (zc + BAY_H / 2 + 2.2)
        u1 = top - (zc - BAY_H / 2 - 2.2)
        holes.append((u0, u1, v0, v1))
    return holes


def build_bands(K, bands, name, side_holes=True):
    """Window rows, the repair plates and the plating for every flat of every band of a segment."""
    K.surfaces = getattr(K, "surfaces", [])
    for bi, band in enumerate(bands):
        for en, patch in band.items():
            if patch.lu > 4.0 and patch.lv > 4.0:
                K.surfaces.append(patch)
            gaps = []
            holes = []
            if en in ("r", "l"):
                gaps = flank_strips(K, patch, WINDOWS.get(name)) if WINDOWS.get(name) and K.lod == 0 else []
                holes = [(a, b, 0.0, patch.lv) for a, b in gaps] + bay_holes(patch, 1 if en == "r" else -1, name)
            style = PLATING.get(en, PLATING["t"])
            if K.lod == 0:
                plate_region(K, patch, f"{name}{bi}{en}", style, holes)
                D.repair_patch(K, K.hull, patch, f"rp-{name}{bi}{en}", count=max(1, int(patch.lu * patch.lv / 500)), size=(2.5, 7.0), height=(0.08, 0.2), margin=1.5,
                               bolt_r=0.09, weld_scale=2.6)
            else:
                K.plate_patch(patch, f"{name}{bi}{en}", **style)


def rails(K, stations, radius=0.3):
    for a, b in zip(stations[:-1], stations[1:]):
        pa = chamfered_rect(a["hw"], a["hh"], a["c"])
        pb = chamfered_rect(b["hw"], b["hh"], b["c"])
        for e in range(8):
            p0 = np.array([pa[e][0] + a["cx"], a["y"], pa[e][1] + a["cz"]])
            p1 = np.array([pb[e][0] + b["cx"], b["y"], pb[e][1] + b["cz"]])
            K.pipe(p0, p1, radius, sides=4, caps=False)


# ------------------------------------------------------------------------------------------------ the ship at one LOD
def build(ctx):
    D.set_paint()
    K = Kit(ctx, TRIMS, SEED)
    K.surfaces = []
    lod = ctx.lod
    hull = ctx.hull
    ship = ctx.ship
    bands_of = {}

    # --- the plated blocks ---
    for name, sts in SEG.items():
        bands = K.loft(sts)
        bands_of[name] = bands
        build_bands(K, bands, name)
        if lod <= 1:
            rails(K, sts, 0.28)

    # --- the flight deck and the command decks, layered slabs stepping up and back, shifted to starboard (asymmetry) ---
    deck1 = [st(-44, 20, 2.8, 1.0, 5, 25.3), st(-38, 28, 2.8, 1.3, 5, 25.3), st(26, 28, 2.8, 1.3, 5, 25.3), st(33, 20, 2.8, 1.0, 5, 25.3)]
    deck2 = [st(-26, 11, 2.0, 0.9, 9, 30.1), st(-21, 14, 2.0, 1.0, 9, 30.1), st(8, 14, 2.0, 1.0, 9, 30.1), st(13, 10, 2.0, 0.9, 9, 30.1)]
    for nm, dk in (("deck1", deck1), ("deck2", deck2)):
        bands = K.loft(dk)
        build_bands(K, bands, nm)
        if lod <= 1:
            rails(K, dk, 0.2)
    # a tall patched sail fin to port with a lattice mast above it, a low lattice mast to starboard, a ventral keel under the main hull
    K.fin([(-44.0, 24.0), (-40.0, 38.0), (-8.0, 38.0), (2.0, 24.0)], -14.0, 1.4)
    if lod <= 1:
        K.lattice([(-40.0, 38.0), (-35.0, 54.0), (-16.0, 54.0), (-8.0, 38.0)], -14.0, beam=0.5, braces=4, mat=PLATE)
        K.lattice([(20.0, 28.0), (23.0, 40.0), (32.0, 40.0), (34.0, 28.0)], 20.0, beam=0.4, braces=2)
    K.fin_plates((-38.0, -10.0, 25.0, 37.0), -14.0, 1.4, "sailP", rows=(3, 7), cols=(2.5, 6))
    K.fin([(-40.0, -24.0), (-34.0, -37.0), (24.0, -39.0), (40.0, -24.0)], 0.0, 2.0)
    K.fin_plates((-32.0, 22.0, -37.0, -25.0), 0.0, 2.0, "keelM", rows=(3, 7), cols=(2.5, 6))
    K.fin([(72.0, -20.0), (78.0, -30.0), (110.0, -31.0), (116.0, -18.0)], 0.0, 1.6)

    # --- the grown joints: round, dark and ribbed, with a glyph band; a steel collar at each end, and a few cables over them ---
    for i, (y0, y1, r, ribs) in enumerate(NECKS):
        if ribs <= 1:
            K.ring(y0, y1, (r + 5.0, r + 3.5, 3.5), inner=(r + 3.0, r + 2.0, 2.5), plate=False)
            continue
        D.ribbed_shell(hull, P(0, y0, 0), P(0, y1, 0), r, r * 0.98, ribs, 0.14, segs=28 if lod == 0 else 12, key=f"neck{i}", lod=lod)
        if lod == 0:
            rng = K.rng("neckcables", i)
            for k in range(5):
                a = rng.uniform(0, 2 * math.pi)
                D.sweep(hull, D.arc_path(P(math.sin(a) * r * 1.02, y0 - 0.5, math.cos(a) * r * 1.02), P(math.sin(a + 0.3) * r * 1.02, y1 + 0.5, math.cos(a + 0.3) * r * 1.02), 1.2, n=6,
                                          up=P(math.sin(a), 0, math.cos(a))), 0.3, STEEL if k % 2 else BIO, sides=6)
            for k in range(4):
                a = k * math.pi / 2 + 0.4
                K.strip(P(math.sin(a) * r * 1.0, y0 + 1.0, math.cos(a) * r * 1.0), YHAT, P(math.sin(a), 0, math.cos(a)), y1 - y0 - 2.0, "glyph_a", lift=0.1, scale=1.6) if "glyph_a" in K.trims else None
        else:
            hull.extrude(regular_poly(8, r), Frame((0, 0, 0), YHAT, ZHAT), y0, y1, TRIM, tile=6.0, rect=None) if False else None

    # --- chrome collars become worn steel collars where blocks meet the necks ---
    for y0, y1, o in ((-48.0, -45.0, (30.0, 20.5, 6.0)), (-70.0, -67.0, (25.0, 17.8, 5.5)), (47.0, 50.0, (29.0, 19.5, 6.0)), (66.0, 69.0, (26.0, 17.5, 5.5)), (115.0, 118.0, (19.0, 13.0, 4.0)),
                      (132.0, 135.0, (14.5, 10.3, 3.8))):
        K.ring(y0, y1, o, inner=(o[0] - 2.5, o[1] - 2.0, o[2] - 0.5), bevel=0.9)

    # --- armour ribs standing proud round the reactor and forward hulls, not evenly spaced ---
    if lod <= 1:
        rng = K.rng("ribs")
        for name, ys in (("reactor", (-110.0, -101.0, -88.0, -78.0)), ("fwd", (72.0, 84.0, 91.0, 104.0, 113.0)), ("main", (-38.0, -33.0, 40.0, 45.0))):
            for y in ys:
                s0 = seg_at(name, y)
                K.ring(y, y + 1.6, (s0["hw"] + 1.4, s0["hh"] + 1.4, s0["c"] + 0.8), inner=(s0["hw"], s0["hh"], s0["c"]), plate=True)

    # --- the living crest: dark gills standing along the forward spine and over the prow, and a spine of ribs along the main hull's top ---
    if lod <= 1:
        rng = K.rng("crest-fins")
        for i, y in enumerate(np.arange(70.0, 116.0, 5.0)):
            z0 = top_pt("fwd", y)[2] - 0.3
            h = 9.0 + 14.0 * math.sin((y - 70.0) / 46.0 * math.pi) * rng.uniform(0.8, 1.1)
            C.bio_fin(hull, [(y, z0), (y + 0.3, z0 + h * 0.45), (y + 1.0, z0 + h * 0.8), (y + 2.6, z0 + h), (y + 3.4, z0 + h * 0.78), (y + 3.0, z0 + h * 0.58), (y + 4.2, z0 + h * 0.36), (y + 4.8, z0)], 0.0, 0.9)
        for i, y in enumerate(np.arange(-40.0, 26.0, 11.0)):
            z0 = 28.0
            C.bio_fin(hull, [(y, z0), (y + 0.5, z0 + 3.0), (y + 1.5, z0 + 4.0), (y + 2.6, z0 + 2.8), (y + 3.0, z0)], -1.0, 0.7)
        if lod == 0:
            D.glyph_band(K, np.array([-0.55, 70.0, top_pt("fwd", 90.0)[2] + 0.7]), YHAT, ZHAT, 0.01, "glyph_a") if False else None

    # --- the port vein: a bundle of dark tubes along the port flank of the forward and main hulls on brackets, with a glyph band ---
    if lod <= 1:
        rng = K.rng("vein")
        for k in range(3):
            z = 6.0 - 3.2 * k
            pts = [np.array([-seg_at("main", -20.0)["hw"] - 2.4 - 0.4 * k, -34.0, z]), np.array([-seg_at("main", 0.0)["hw"] - 2.6 - 0.4 * k, 0.0, z + 1.0]),
                   np.array([-seg_at("main", 30.0)["hw"] - 2.6 - 0.4 * k, 30.0, z]), np.array([-seg_at("fwd", 66.0)["hw"] - 2.2, 62.0, z * 0.6])]
            D.sweep(hull, np.array(pts), 0.9 - 0.15 * k, BIO, sides=8 if lod == 0 else 5)

    # --- the radiator wing aft (patched panels on a lattice, three to port, one to starboard) and a lattice mast on the tail ---
    def radiator(side, y0, y1, z, reach):
        x0, x1 = side * 19.0, side * (19.0 + reach)
        cx = (x0 + x1) / 2
        hull.box(Frame((cx, (y0 + y1) / 2, z), YHAT, ZHAT), (reach, y1 - y0, 0.7), PLATE_C if side < 0 else PLATE_E, chamfer=0.2, tile=6.0)
        if lod <= 1:
            K.pipe((x1, y0 + 0.8, z - 0.5), (x1, y0 + 0.8, z + 0.5), 0.35, sides=4)
            for y in (y0 + 5.0, (y0 + y1) / 2, y1 - 5.0):
                K.pipe((x0, y, z), (x1, y, z), 0.28, sides=4)
        if lod == 0:
            K.tile_piece("windows_big", (min(x0, x1), y0 + 0.5, z + 0.36), XHAT, YHAT, reach, y1 - y0 - 1.0, scale=1.6) if False else None
    for z, y0, y1, reach in ((-7.5, -146.0, -122.0, 15.0), (-1.0, -148.0, -126.0, 17.0), (6.5, -144.0, -124.0, 13.0)):
        radiator(-1, y0, y1, z, reach)
    radiator(1, -146.0, -124.0, 2.0, 14.0)
    if lod <= 1:
        K.lattice([(-148.0, 13.0), (-142.0, 32.0), (-130.0, 32.0), (-124.0, 13.0)], 7.0, beam=0.5, braces=3)
        K.lattice([(-60.0, 19.0), (-54.0, 36.0), (-40.0, 36.0), (-34.0, 24.0)], -12.0, beam=0.5, braces=3, mat=PLATE)

    # --- the starboard cargo truss: a truss along the reactor flank carrying strapped containers on both sides ---
    tx, tz = 33.0, -3.0
    K.truss(np.array([tx, -113.0, tz]), np.array([tx, -62.0, tz]), 1.6, 6.0)
    if lod <= 1:
        kinds = ("container_a", "container_b", "container_c")
        for i, y in enumerate(np.arange(-108.0, -66.0, 13.0)):
            for sx, (dz, kd) in zip((1, -1), ((0.0, kinds[i % 3]), (0.0, kinds[(i + 1) % 3]))):
                if sx < 0 and i % 2 == 1:
                    continue                                      # a gap in the line: not every slot is full
                D.container(K, hull, Frame((tx + sx * 3.8, y, tz + dz), YHAT, ZHAT), (5.2, 12.0, 5.2), kd, f"cont{i}{sx}", straps=lod == 0)

    # --- the nacelles: a plain human cowl in front, a grown dark shell behind it, hung from plated pylons ---
    for side in (1, -1):
        x = side * NAC_X
        nac = [st(-128, 11.5, 11.5, 4.5, x, NAC_Z), st(-150, 11.2, 11.2, 4.4, x, NAC_Z)]
        bands = K.loft(nac, caps=(True, False))
        build_bands(K, bands, "nac")
        if lod <= 1:
            rails(K, nac, 0.24)
        K.ring(-128.0, -125.0, (13.2, 13.2, 4.8), inner=(11.5, 11.5, 4.4), centre=(x, NAC_Z), bevel=0.8)
        D.ribbed_shell(hull, np.array([x, -149.0, NAC_Z]), np.array([x, -172.0, NAC_Z]), 11.0, 8.6, 9, 0.18, segs=26 if lod == 0 else 12, key=f"nacshell{side}", lod=lod)
        if lod == 0:
            D.glyph_band(K, np.array([x - 1.1, -153.0, NAC_Z + 11.4]), YHAT, ZHAT, 14.0, "glyph_a", lift=0.2, scale=1.3)
            D.glyph_band(K, np.array([x - 1.1, -153.0, NAC_Z - 11.6]), YHAT, -ZHAT, 14.0, "glyph_b", lift=0.2, scale=1.3)
        # pylons: two plated slabs from the reactor's flank to the nacelle's cowl
        for y0, y1 in ((-134.0, -122.0), (-146.0, -138.0)):
            q = np.array([[side * 19.0, y0, -8.0], [side * 24.0, y0, -9.0], [side * 24.0, y1, -9.0], [side * 19.0, y1, -8.0]])
            hull.box(Frame(((side * 21.5), (y0 + y1) / 2, 0.0), YHAT, ZHAT), (side * -1 * 0 + 11.0, y1 - y0, 3.0), PLATE_D, chamfer=0.4, tile=6.0)
        if lod == 0:
            for k in range(4):                                    # the nacelle's living tendrils across the pylon to the hull
                D.sweep(hull, D.arc_path(np.array([side * 25.0, -146.0 + 3.0 * k, 3.5 - 2.0 * k]), np.array([side * 17.0, -140.0 + 3.0 * k, 5.0 - 2.5 * k]), 2.0, n=6, up=ZHAT),
                        0.5, BIO, sides=6)

    # --- the prow: a ram of worn steel, a docking collar with a ring of lamps, a heat-scarred nose plate ---
    pf = Frame((0, 0, 0), YHAT, ZHAT)
    hull.extrude(regular_poly(8, 7.5), pf, 160.0, 176.0, STEEL, s1=(0.25, 0.25), tile=3.0, caps=(False, True), cap_mat=STEEL)
    K.ring(160.0, 164.0, (12.5, 9.5, 3.4), inner=(11.0, 8.5, 3.0), bevel=0.8)
    if lod <= 1:
        hull.lathe([(7.0, 160.2), (7.8, 160.5), (7.8, 162.5), (7.0, 162.8)], pf, STEEL, segs=40 if lod == 0 else 16, tile=3.0)
        for k in range(6):
            a = k * math.pi / 3 + math.pi / 6
            K.pipe((math.sin(a) * 2.5, 161.5, math.cos(a) * 2.5), (math.sin(a) * 7.0, 161.5, math.cos(a) * 7.0), 0.25, sides=6 if lod == 0 else 4)

    # --- markings: the name and the hull number big on the starboard flank, the mark on the top deck, hazard stripes on the lips ---
    if lod == 0:
        s_main = seg_at("main", 10.0)
        K.slab_box(Frame((s_main["hw"] + 0.35, 4.0, 12.5), YHAT, ZHAT), (0.7, 33.0, 4.9), mat=PLATE_C, chamfer=0.12, tile=8.0)
        K.decal(np.array([s_main["hw"] + 0.72, 4.0, 12.5]), YHAT, XHAT, "name_mercy", size=(31.0, 4.65), lift=0.0)
        K.slab_box(Frame((s_main["hw"] + 0.2, 90.0, 4.0), YHAT, ZHAT), (0.5, 11.0, 3.9), mat=PLATE_C, chamfer=0.1, tile=8.0) if False else None
        K.paint_quad("hull_code", np.array([seg_at("fwd", 96.0)["hw"] + 0.4, 96.0, 5.0]), YHAT, XHAT, size=(14.0, 4.6), lift=0.0)
        K.paint_quad("emblem", np.array([0.0, -2.0, top_pt("main", -2.0)[2] + 0.05]), YHAT, ZHAT, size=(15.0, 15.0), lift=0.0)
        for sx in (-1, 1):
            K.wear_quad("heat_0", np.array([sx * (19.2 + 0.05), -140.0, 0.0]), YHAT, XHAT * sx, lift=0.02, size=(18.0, 12.0))

    # ============================================================================================ subsystems
    # engines: one big bell in each nacelle's shell, an amber flame
    for side, nm in ((-1, "P"), (1, "S")):
        fr = Frame((side * NAC_X, -171.0, NAC_Z), -YHAT, ZHAT)
        with ctx.subsystem(nm, "engine", fr, radius=13.0, hp=1.0, tags=["propulsion"]) as sb:
            mnparts.engine_nozzle(K, sb, fr, f"eng{nm}", throat=3.6, exit_r=7.2, length=13.0, s=2.0, bell_segs=36, bell_n=10, plume_shells=8, plume_segs=16, plume_len=1.0)

    # flak batteries on the reactor and forward flanks
    for side, tag in ((-1, "P"), (1, "S")):
        for i, (nm, y) in enumerate((("reactor", -100.0), ("reactor", -80.0), ("fwd", 76.0), ("fwd", 106.0))):
            if i not in (0, 3):
                continue
            p = flank_pt(nm, y, 3.0, side, lift=0.05)
            fr = K.frame_on(p, XHAT * side, (0, 1, 0.12))
            with ctx.subsystem(f"{tag}{i + 1:02d}", "flak", fr, radius=4.4, tags=["anti-fighter", "flak"]) as sb:
                C.gun_turret(K, sb, fr, f"flak{tag}{i}", s=1.0, barrels=2, barrel_len=4.2)
    # anti-fighter guns on the top and bottom decks
    sites = [("D01", np.array([-8.0, -34.0, 28.15]), ZHAT), ("D02", np.array([18.0, 20.0, 28.15]), ZHAT), ("D03", top_pt("fwd", 84.0, 8.0, 0.05), ZHAT),
             ("V01", bottom_pt("main", -30.0, 12.0, 0.05), -ZHAT), ("V02", bottom_pt("main", 20.0, -14.0, 0.05), -ZHAT), ("V03", bottom_pt("fwd", 90.0, 4.0, 0.05), -ZHAT)]
    for nm, p, n in sites:
        fr = K.frame_on(p, n, (0, 1, 0))
        with ctx.subsystem(nm, "aaf", fr, radius=5.2, hp=1.0, tags=["anti-fighter", "laser"]) as sb:
            C.gun_turret(K, sb, fr, f"aaf{nm}", s=1.5, barrels=2, barrel_len=4.6)
    # the two beam lances: dorsal forward, ventral aft
    for nm, p, n in (("fore", top_pt("fwd", 104.0, 0.0, 0.05) + np.array([0.0, 0.0, 0.0]), ZHAT), ("aft", bottom_pt("reactor", -96.0, 0.0, 0.05), -ZHAT)):
        fr = K.frame_on(p, n, (0, 1, 0))
        with ctx.subsystem(nm, "turret", fr, radius=8.0, hp=1.0, tags=["beam", "anti-capital"]) as sb:
            C.beam_lance(K, sb, fr, f"beam{nm}", length=24.0, r=2.4)
    # the four hangar bays (not combat parts: they burn and break for the show)
    for sd, idx, yc, zc, w in HANGARS:
        side = 1 if sd == "S" else -1
        p = flank_pt("main", yc, zc, side, lift=0.0)
        fr = K.frame_on(p, XHAT * side, (0, 1, 0))
        with ctx.subsystem(f"{sd}{idx}", "hangar", fr, radius=w * 0.62, hp=1.0, tags=["hangar"]) as sb:
            C.hangar_bay(K, sb, fr, f"hangar{sd}{idx}", w=w, h=BAY_H, depth=BAY_D)
    # the bridge off to port on the forward hull, the reactor's heart on the reactor's top, the sensor crest on the prow
    fr = K.frame_on(top_pt("fwd", 80.0, -7.0, 0.05), ZHAT, (0, 1, 0))
    with ctx.subsystem("main", "bridge", fr, radius=12.0, hp=1.0, tags=["command"]) as sb:
        C.bridge_pod(K, sb, fr, "bridge", w=15.0, l=22.0, h=6.5)
    fr = K.frame_on(top_pt("reactor", -92.0, 0.0, 0.05), ZHAT, (0, 1, 0))
    with ctx.subsystem("main", "reactor", fr, radius=11.0, hp=1.0, tags=["power"]) as sb:
        C.reactor_heart(K, sb, fr, "reactor", r=8.0)
    fr = K.frame_on(top_pt("prow", 146.0, 0.0, 0.05), ZHAT, (0, 1, 0))
    with ctx.subsystem("crest", "sensors", fr, radius=9.0, hp=1.0, tags=["radar"]) as sb:
        C.sensor_crest(K, sb, fr, "crest", r=7.0)

    # ============================================================================================ lights: warm, and the grafts' cold teal
    if lod <= 1:
        K.light(np.array([-12.0, 150.0, 2.0]), (-1, 0.2, 0), LIGHT_RED, 0.9, "nav")
        K.light(np.array([12.0, 150.0, 2.0]), (1, 0.2, 0), LIGHT_GREEN, 0.9, "nav")
        K.light(np.array([-NAC_X - 11.3, -140.0, 0.0]), (-1, 0, 0), LIGHT_RED, 1.0, "nav")
        K.light(np.array([NAC_X + 11.3, -140.0, 0.0]), (1, 0, 0), LIGHT_GREEN, 1.0, "nav")
        K.light(np.array([0.0, -20.0, bottom_pt("main", -20.0)[2] - 0.2]), -ZHAT, LIGHT_WHITE, 1.2, "strobe", blink="strobe")
        K.light(top_pt("fwd", 112.0) + np.array([0.0, 0.0, 6.0]), ZHAT, LIGHT_WHITE, 1.0, "strobe", blink="strobe")
        K.light(np.array([0.0, 178.0, 0.0]), YHAT, LIGHT_WHITE, 0.9, "strobe", blink="strobe")
        for y in np.arange(-100.0, 100.0, 11.0):
            for sx in (-1, 1):
                nm = "reactor" if y < -50 else "main" if y < 48 else "fwd"
                if nm == "fwd" and y < 66:
                    continue
                s_ = seg_at(nm, min(max(y, SEG[nm][0]["y"]), SEG[nm][-1]["y"]))
                K.light(np.array([sx * (s_["hw"] - 4.0), y, s_["cz"] + s_["hh"] + 0.1]), ZHAT, LIGHT_AMBER if int(y) % 22 else LIGHT_WHITE, 0.5, "running")
        for k, (y0, y1, r, ribs) in enumerate(NECKS[:3]):
            for a in (0.0, math.pi / 2, math.pi, 3 * math.pi / 2):
                K.light(np.array([math.sin(a + 0.8) * (r + 0.2), (y0 + y1) / 2, math.cos(a + 0.8) * (r + 0.2)]), (math.sin(a + 0.8), 0, math.cos(a + 0.8)), LIGHT_TEAL, 0.6, "running")
        for sd, idx, yc, zc, w in HANGARS:
            side = 1 if sd == "S" else -1
            for dy in (-w / 2 - 1.5, w / 2 + 1.5):
                K.light(flank_pt("main", yc + dy, zc + BAY_H / 2 + 1.2, side, lift=BAY_D + 0.6), (side, 0, 0.3), LIGHT_AMBER, 0.8, "running")

    # --- the collision proxy: the segments and the big appendages as coarse boxes ---
    if lod == 0:
        for name, sts in SEG.items():
            K.loft(sts, mb=ship.collision)
        for i, (y0, y1, r, ribs) in enumerate(NECKS[:3]):
            ship.collision.box(Frame((0, (y0 + y1) / 2, 0), YHAT, ZHAT), (r * 1.8, y1 - y0, r * 1.8), PLATE, chamfer=r * 0.4)
        for side in (1, -1):
            ship.collision.box(Frame((side * NAC_X, -148.0, NAC_Z), YHAT, ZHAT), (23.0, 46.0, 23.0), PLATE, chamfer=4.0)
            ship.collision.box(Frame((side * 21.5, -134.0, 0.0), YHAT, ZHAT), (12.0, 22.0, 6.0), PLATE)
    return K


def P(x, y, z):
    return np.array([x, y, z], float)


def hull_extras(ship, K0):
    """Damage sites, FX hooks and sampled hull points for scorch decals."""
    sites = [
        ("main-stbd-upper", flank_pt("main", -34.0, 12.0, 1, 0.05), XHAT, 5.5),
        ("main-port-lower", flank_pt("main", 6.0, -14.0, -1, 0.05), -XHAT, 5.0),
        ("deck-stbd", np.array([20.0, -10.0, 28.15]), ZHAT, 5.5),
        ("deck-aft", top_pt("reactor", -76.0, -12.0, 0.05), ZHAT, 4.5),
        ("fwd-top", top_pt("fwd", 92.0, 8.0, 0.05), ZHAT, 5.0),
        ("fwd-stbd", flank_pt("fwd", 94.0, 3.0, 1, 0.05), XHAT, 4.5),
        ("reactor-flank", flank_pt("reactor", -108.0, -4.0, -1, 0.05), -XHAT, 4.5),
        ("belly", bottom_pt("main", 4.0, -6.0, 0.05), -ZHAT, 5.0),
    ]
    for name, p, n, r in sites:
        site_fr = Frame(p, n, _perp(n))
        build_fr = Frame(p, _perp(n), n)
        ship.add_site(site_fr, r, mnparts.breach(Kit(ship.lods[0], TRIMS, SEED), build_fr, r, name))
        ship.add_hook("spark", Frame(p + norm(n) * 0.8, n, _perp(n)), 1.0)
        ship.add_hook("vent", Frame(p + norm(n) * 0.5 + _perp(n) * r * 0.5, n, _perp(n)), 2.0)
    for side in (-1, 1):
        ship.add_hook("fire", Frame((side * NAC_X, -172.0, NAC_Z), -YHAT, ZHAT), 3.0)
    for sd, idx, yc, zc, w in HANGARS:
        side = 1 if sd == "S" else -1
        ship.add_hook("fire", Frame(flank_pt("main", yc, zc, side, lift=BAY_D * 0.8), XHAT * side, _perp(XHAT * side)), 2.5)
    for x in (-5.0, 5.0):
        ship.add_hook("vent", Frame(top_pt("reactor", -92.0, x, 8.0), ZHAT, YHAT), 2.5)
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
    for i in idx:
        p = pts[i][0]
        if all(np.linalg.norm(p - c[0]) > 14.0 for c in chosen):
            chosen.append(pts[i])
        if len(chosen) >= count:
            break
    return [{"p": mnship.to_unity(p), "n": mnship.to_unity(n)} for p, n, _ in chosen]


def main():
    preview = "--preview" in sys.argv
    ship = mnship.Ship(SHIP_ID, "Small Mercy (diaspora cruiser)", ART, D.MATERIALS, manifest_dir=RES, material_set=MATERIAL_SET)
    kits = {}
    for lod in (0, 1, 2):
        ship.build_lod(lod, lambda ctx: kits.__setitem__(ctx.lod, build(ctx)))
    hull_extras(ship, kits[0])
    ship.extra["damagePoints"] = damage_points()
    ship.extra["lodScreenHeights"] = [0.16, 0.05, 0.012]
    ship.extra["engineLights"] = {f"SUB_engine_{nm}": {"z": 7.0, "candela": 420000.0, "range": 55.0} for nm in ("P", "S")}
    stats = ship.export()
    print(f"[MN] bounds {stats['bounds']}")
    if preview:
        out = os.environ.get("MN_PREVIEW_DIR", os.path.join(ROOT, "art_src", "diaspora", "_preview", SHIP_ID))
        mnpreview.render_views(out, [
            ("side", (330, -10, 20), (0, 0, 8), 38),
            ("three_quarter", (260, -330, 120), (0, -10, 8), 38),
            ("top", (0, -10, 520), (0, -10, 0), 38),
            ("aft", (60, -330, 40), (0, -170, 0), 40),
            ("bow", (120, 330, 60), (0, 140, 0), 40),
            ("close", (90, -60, 50), (20, -30, 10), 45),
            ("hangar", (100, -5, 12), (36, 0, 0), 40),
        ])
        mnpreview.render_views(out + "_wreck", [("wreck_close", (60, -150, 50), (0, -125, 5), 45), ("wreck_three", (260, -330, 120), (0, -10, 8), 38)],
                               show=("__destroyed", "__debris", "__breach"))


main()
