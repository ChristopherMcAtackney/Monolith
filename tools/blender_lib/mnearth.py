"""mnearth: what Earth's big builds share (the comet outpost and the destroyer): hull segments lofted in a frame of their own and plated in world space, flank window
rows laid in broken runs, chrome rails, and a few structure helpers (an armoured block, a lattice tower, a clamp arm). The corvette's script keeps its own copies of the
first three (it predates this); the geometry they make is the same language.

Everything draws into ``K.hull`` (or the ``mb`` given) in SHIP space; ``frame`` says where a local build axis (the station's spine runs along the frame's y) sits.
"""
import math

import numpy as np

from . import mnmesh
from .mnkit import (PLATE, TRIM, LIGHTS, ENGINE, CHROME, PLATE_B, PLATE_C, PLATE_D, PLATE_E, WEAR, PAINT, complement, lerp_station,
                    LIGHT_WHITE, LIGHT_COLD, LIGHT_RED, LIGHT_GREEN, LIGHT_AMBER, LIGHT_BLUE, LIGHT_EMBER)
from .mnmesh import Frame, Patch, MeshBuilder, norm, cross, chamfered_rect, regular_poly

X = np.array([1.0, 0.0, 0.0])
Y = np.array([0.0, 1.0, 0.0])
Z = np.array([0.0, 0.0, 1.0])

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


def loft_placed(K, frame, stations, caps=(True, True)):
    """Loft hull segments whose axis is ``frame``'s y (stations as in ``K.loft``: y along the axis, hw/hh/c, cx right, cz up in the frame) and put them in ship space.
    The carcass is drawn; the returned bands are lists (per band) of dicts edge name -> Patch, in ship space, for plating."""
    scratch = MeshBuilder("scratch")
    bands = K.loft(stations, caps=caps, mb=scratch)
    K.hull.merge(scratch.transformed_copy(frame))
    out = []
    for band in bands:
        d = {}
        for en, p in band.items():
            d[en] = Patch(frame.p(*p.p00), frame.p(*p.p10), frame.p(*p.p11), frame.p(*p.p01), p.label)
        out.append(d)
    return out


def station_ring(frame, s):
    """A station's chamfered rectangle as 8 points in ship space."""
    prof = chamfered_rect(s["hw"], s["hh"], s["c"])
    return np.array([frame.p(x + s["cx"], s["y"], z + s["cz"]) for x, z in prof])


def rails(K, frame, stations, radius=0.3, sides=4):
    """Chrome ridge lines along every corner of a hull segment."""
    for a, b in zip(stations[:-1], stations[1:]):
        ra, rb = station_ring(frame, a), station_ring(frame, b)
        for e in range(8):
            K.pipe(ra[e], rb[e], radius, sides=sides, caps=False)


def pt_at(patch, z=None, y=None):
    """The point on a patch at world height z (or at fraction along v: y is then the world coordinate along the patch's v axis... not used when z is None)."""
    u = 0.0 if z is None else (patch.p00[2] - z) / (patch.p00[2] - patch.p10[2])
    return patch.pt(u, 0.0 if y is None else y)


def flank_strips(K, patch, rows, margin=3.0, key="", win_scale=1.0):
    """Window rows on a vertical flank, laid in broken runs (7 to 46 m long, with gaps filled by conduit bundles) so no two rows repeat. Works for either winding
    of the flat. Returns the u-intervals (metres from the patch's u=0 edge) the rows occupy, for the plating to leave free."""
    gaps = []
    n = patch.normal()
    run = norm(cross(Z, n))
    zt, zb_ = max(patch.p00[2], patch.p10[2]), min(patch.p00[2], patch.p10[2])
    rng = K.rng("win", key, tuple(np.round(patch.p00, 1)))
    Lu = patch.lu
    for zc, name in rows:
        ph = K.piece_m(name)[1] * win_scale
        zb = zc - ph / 2
        if zb < zb_ + 0.5 or zb + ph > zt - 0.5:
            continue
        u = (patch.p00[2] - zb) / (patch.p00[2] - patch.p10[2])
        ya, yb = margin, patch.lv - margin
        if yb - ya < 6:
            continue
        runs = []
        y = ya + rng.uniform(0.0, 6.0)
        while y < yb - 5.0:
            ln = min(rng.uniform(7.0, 46.0), yb - y)
            if ln >= 5.0:
                runs.append((y, y + ln))
            y += ln + rng.uniform(3.0, 16.0)
        if len(runs) > 2 and rng.random() < 0.3:
            runs.pop(int(rng.integers(len(runs))))
        lv = patch.lv

        def lay(y0, y1, piece, scale=1.0):
            pa, pb = patch.pt(u, y0 / lv), patch.pt(u, y1 / lv)
            if np.dot(pb - pa, run) < 0:
                pa, pb = pb, pa
            K.strip(pa, run, n, float(np.linalg.norm(pb - pa)), piece, lift=0.03, scale=scale * win_scale)
        for y0, y1 in runs:
            lay(y0, y1, name)
        for y0, y1 in complement(ya, yb, runs, 1.5):
            lay(y0, y1, "conduit", ph / 0.8 / win_scale)
        ua, ub = sorted(((zt - (zb + ph)) - 0.45, (zt - zb) + 0.45))
        # convert heights (distance down from the top) to u distances along the patch (which may run up or down)
        if patch.p00[2] >= patch.p10[2]:
            gaps.append((ua, ub))
        else:
            tot = Lu
            gaps.append((tot - ub, tot - ua))
    return gaps


def plate_bands(K, bands, key, windows=None, styles=None, skip=(), detail=True, window_edges=("r", "l"), extra_gaps=None, win_scale=1.0):
    for bi, band in enumerate(bands):
        for en, patch in band.items():
            if en in skip:
                continue
            gaps = []
            if windows and en in window_edges:
                gaps = flank_strips(K, patch, windows, key=f"{key}-{bi}-{en}", win_scale=win_scale)
            if extra_gaps and en in extra_gaps:
                gaps = gaps + list(extra_gaps[en])
            style = (styles or PLATING).get(en, {})
            for gi, (a, b) in enumerate(complement(0.0, patch.lu, gaps, 1.5)):
                K.plate_patch(patch, f"{key}-{bi}-{en}-{gi}", u_range=(a, b), **style)
                if detail and K.lod == 0:
                    K.scatter_details(patch, K.last_cells, f"{key}-{bi}-{en}-{gi}")
                    K.wear(patch, K.last_cells, f"{key}-{bi}-{en}-{gi}")
                    K.edge_wear(patch, K.last_cells, K.last_tones, f"{key}-{bi}-{en}-{gi}")


def collar(K, frame, y0, y1, outer, inner=None, plate=False):
    """A chrome (or armour) collar round a lofted segment: ``K.ring`` in the frame, through a scratch builder."""
    scratch = MeshBuilder("collar")
    K.ring(y0, y1, outer, inner=inner, mb=scratch, plate=plate)
    K.hull.merge(scratch.transformed_copy(frame))


def block(K, frame_or_pts, size, mat=PLATE, centre=(0, 0, 0), chamfer=0.4, tile=8.0, mb=None):
    fr = frame_or_pts
    (mb or K.hull).box(fr, size, mat, centre=centre, tile=tile, chamfer=chamfer)


def lattice_tower(K, base, height, w0, w1, bays, rod=0.55, mb=None, sides=4, platforms=(), plat_r=None):
    """A square lattice tower standing on ``base`` (ship space), tapering from half-width w0 to w1: four chrome legs, a ring of braces and a diagonal in every bay, an optional
    armoured platform at fractions of the height. Returns the leg-top points."""
    mb = mb or K.hull
    base = np.asarray(base, float)
    corners = np.array([(1, 1), (-1, 1), (-1, -1), (1, -1)], float)
    levels = []
    for i in range(bays + 1):
        t = i / bays
        w = w0 + (w1 - w0) * t
        z = height * t
        levels.append([base + np.array([cx * w, cy * w, z]) for cx, cy in corners])
    s = sides if K.lod == 0 else 4
    for k in range(4):
        K.pipe(levels[0][k], levels[-1][k], rod * 1.25, mb=mb, sides=s, caps=False)
    if K.lod <= 1:
        for i, lv in enumerate(levels):
            if i > 0 and i < bays and K.lod == 1:
                pass
            for k in range(4):
                K.pipe(lv[k], lv[(k + 1) % 4], rod * 0.8, mb=mb, sides=4, caps=False)
            if i < bays:
                nx = levels[i + 1]
                for k in range(4):
                    a, b = (k, (k + 1) % 4)
                    if (i + k) % 2 == 0:
                        K.pipe(lv[a], nx[b], rod * 0.65, mb=mb, sides=4, caps=False)
                    else:
                        K.pipe(lv[b], nx[a], rod * 0.65, mb=mb, sides=4, caps=False)
    for f in platforms:
        i = int(round(f * bays))
        t = i / bays
        w = w0 + (w1 - w0) * t
        fr = Frame(base + np.array([0, 0, height * t]), Y, Z)
        mb.box(fr, (w * 2 + 3.0, w * 2 + 3.0, 1.2), PLATE, centre=(0, 0, 0), chamfer=0.4, tile=6.0)
    return levels[-1]


def clamp_claw(K, mb, foot, normal, along, r=6.0, length=22.0, key=""):
    """A chrome-and-armour claw gripping a surface: three fingers curving from a body into the surface at ``foot`` (normal = out of the surface). Fingers are tapered pipes."""
    n = norm(normal)
    a = norm(along - n * np.dot(along, n))
    b = cross(n, a)
    body = Frame(foot + n * (length * 0.55), a, n)
    mb.box(body, (r * 2.2, r * 2.6, length * 0.5), PLATE, chamfer=r * 0.3, tile=6.0)
    for k in range(3):
        ang = k * 2 * math.pi / 3 + 0.4
        out = a * math.cos(ang) + b * math.sin(ang)
        p0 = foot + n * (length * 0.8) + out * r * 0.8
        p1 = foot + n * (length * 0.4) + out * r * 1.7
        p2 = foot - n * (length * 0.25) + out * r * 1.1
        K.pipe(p0, p1, r * 0.34, mb=mb, sides=6 if K.lod == 0 else 4)
        K.pipe(p1, p2, r * 0.27, mb=mb, sides=6 if K.lod == 0 else 4)
