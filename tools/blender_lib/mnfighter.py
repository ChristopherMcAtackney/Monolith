"""mnfighter: the small-ship kit for Earth's fighters and bombers (art_src/earth_fighters). It sits on the capital-ship kit (mnkit, mnparts) and changes the
scale: plates of 0.3 to 1.5 m, plated blades instead of lofted decks, engines a fifth the size of a corvette's, guns, sensors and missile racks that are
separate, destroyable parts. Same conventions as the rest of the pipeline (art_src/PIPELINE.md): x starboard, y forward (the nose), z up; every subsystem
has an intact look, a wreck and debris; every builder draws in ship space through a frame.

What makes a fighter read at distance (the TIE lesson, in FS2's language): a silhouette of plated parts that is different for each class, spines and
fins with some asymmetry, hardware that breaks the outline (antennas, sensor clusters, hardpoints), glowing recessed exhausts, harsh white running lights.
Never wedges, never a brick covered in random greebles, never a flat engine disc.
"""
import math

import numpy as np

from . import mnmesh, mnparts
from .mnkit import (PLATE, TRIM, LIGHTS, ENGINE, CHROME, PLATE_B, PLATE_C, PLUME, PLATE_D, PLATE_E, WEAR, PAINT, TONE_MATS, Kit, station,
                    LIGHT_WHITE, LIGHT_COLD, LIGHT_RED, LIGHT_GREEN, LIGHT_AMBER, LIGHT_BLUE, LIGHT_EMBER)
from .mnmesh import Frame, Patch, MeshBuilder, norm, cross, chamfered_rect, regular_poly

X = np.array([1.0, 0.0, 0.0])
Y = np.array([0.0, 1.0, 0.0])
Z = np.array([0.0, 0.0, 1.0])

# plate style for a fighter: plates of many sizes, a few carrying smaller ones, gaps of 4 cm, relief of 2 to 5 cm
PLATING = dict(rows=(0.5, 1.7), cols=(0.35, 1.2), gap=0.045, height=(0.018, 0.055), chamfer=0.026, margin=0.04, min_size=0.2, sub_prob=0.22,
               sub=(0.24, 0.55), sub_height=(0.008, 0.024))
WEAR_STYLE = dict(min_w=0.45, min_l=0.55, min_width=0.07, chip_p=0.2, grime_p=0.1)
FIGHTER_TONES = (0.34, 0.46, 0.14, 0.05, 0.01)        # Earth's small ships are matte black: deep and graphite paint, a little mid grey


def _fighter_tones(self, key, m, rng):
    """Paint tone index (0 deep .. 4 pale) for m plates of one region: Earth's small ships are matte black, so deep and graphite carry them, mid grey is an accent
    and the light tones are rare (the capital kit lets a region run pale)."""
    region = str(key).rsplit("-", 1)[0]
    dom = int(self.rng("tone", region).choice(3, p=(0.36, 0.5, 0.14)))
    w = np.exp(-np.abs(np.arange(5) - dom) * 2.2) + np.array([0.01, 0.01, 0.01, 0.0, 0.0])
    t = rng.choice(5, size=m, p=w / w.sum())
    accent = rng.random(m) < 0.035
    t[accent] = rng.choice(3, size=int(accent.sum()))
    return t


def dark_paint():
    """Make a small ship's plates matte black (call before building): the capital kit's tone choice, replaced."""
    from . import mnkit
    mnkit.TONE_DOMINANT = FIGHTER_TONES
    mnkit.Kit.tones = _fighter_tones


def perp(n):
    n = norm(n)
    a = Z if abs(n[2]) < 0.9 else X
    return norm(cross(a, n))


def lerp(a, b, t):
    return np.asarray(a, np.float64) * (1 - t) + np.asarray(b, np.float64) * t


# ------------------------------------------------------------------------------------------------ plated solids

def _oriented(P, hint):
    """A quad's corners in the order that makes its normal point along ``hint``."""
    P = np.asarray(P, np.float64)
    N = cross(P[2] - P[0], P[3] - P[1])
    return P if np.dot(N, hint) >= 0 else P[::-1]


def solid(K, mb, quad, thick, key, bevel=0.03, plating=PLATING, faces=(True, True), chrome_edges=(), edge_r=0.014, wear=True, normal=None, lod2=True):
    """A flat armoured solid with a bevelled rim: ``quad`` (4 corners, counter-clockwise seen from the face whose normal is the +normal) with a thickness. The
    carcass is the dark underlayer (so the gaps between plates read as depth); the two faces carry small plates (``plating`` None for bare); ``chrome_edges``
    lists edge indices (corner i to i+1) that get a chrome rail; ``lod2`` False leaves a small piece out of the lowest LOD; ``normal`` fixes which way the top face looks whatever the corner order. Returns the top patch and
    the bottom patch."""
    if K.lod >= 2 and not lod2:
        return None, None
    q = np.asarray(quad, np.float64)
    n = norm(cross(q[1] - q[0], q[3] - q[0]))
    if normal is not None and np.dot(n, normal) < 0:          # the caller says which way the top face looks: reorder the corners to match (edge indices follow the new order)
        q = q[::-1]
        n = -n
    c = q.mean(axis=0)
    h = thick * 0.5
    b = min(bevel, h * 0.9)
    e0 = min(np.linalg.norm(q[1] - q[0]), np.linalg.norm(q[3] - q[0]), np.linalg.norm(q[2] - q[1]), np.linalg.norm(q[3] - q[2]))
    k = max(0.5, 1.0 - b * 1.7 / max(e0 * 0.5, 1e-3))
    ins = c + (q - c) * k
    A_t, A_b = q + n * (h - b), q - n * (h - b)
    T, B = ins + n * h, ins - n * h
    carcass = []

    def add(P, hint):
        P = _oriented(P, hint)
        carcass.append(Patch(P[0], P[1], P[2], P[3]))
    far = K.lod >= 2                      # the lowest LOD is a plain slab: four sides and two faces, no bevel
    if far:
        A_t, A_b, T, B = q + n * h, q - n * h, q + n * h, q - n * h
    for i in range(4):
        j = (i + 1) % 4
        m = 0.5 * (q[i] + q[j]) - c
        out = norm(m - n * np.dot(m, n))
        add([A_b[i], A_b[j], A_t[j], A_t[i]], out)
        if not far:
            add([A_t[i], A_t[j], T[j], T[i]], out + n)
            add([B[i], B[j], A_b[j], A_b[i]], out - n)
    add(T, n)
    add(B[::-1], -n)
    K._carcass(mb, carcass)
    top = Patch(T[0], T[1], T[2], T[3])
    bot = Patch(B[0], B[3], B[2], B[1])
    if not hasattr(K, "surfaces"):
        K.surfaces = []
    K.surfaces += [top, bot] if faces[1] else [top]
    for i in chrome_edges:
        j = (i + 1) % 4
        K.pipe(A_t[i], A_t[j], edge_r, sides=4, mb=mb, caps=False)
    if plating is not None and K.lod == 0:
        for flag, patch, tag in ((faces[0], top, "t"), (faces[1], bot, "b")):
            if not flag:
                continue
            K.plate_patch(patch, f"{key}-{tag}", mb=mb, **plating)
            if wear:
                K.edge_wear(patch, K.last_cells, K.last_tones, f"{key}-{tag}", mb=mb, **WEAR_STYLE)
    return top, bot


def hull(K, mb, stations, key, plating=PLATING, skip=(), caps=(True, True), rail_r=0.02, wear=True):
    """A faceted fuselage: chamfered-rectangle stations joined by planar bands (the capital kit's loft), every flat plated small, chrome rails on the corners.
    Returns the bands (a list of dicts edge name -> Patch)."""
    bands = K.loft(stations, caps=caps, mb=mb)
    if not hasattr(K, "surfaces"):
        K.surfaces = []
    for bi, band in enumerate(bands):
        for en, patch in band.items():
            if patch.lu > 0.3 and patch.lv > 0.3:
                K.surfaces.append(patch)
            if en in skip or K.lod > 0 or patch.lu < 0.5 or patch.lv < 0.5:
                continue
            K.plate_patch(patch, f"{key}-{bi}-{en}", mb=mb, **plating)
            if wear:
                K.edge_wear(patch, K.last_cells, K.last_tones, f"{key}-{bi}-{en}", mb=mb, **WEAR_STYLE)
    if rail_r and K.lod <= 1:
        for a, b in zip(stations[:-1], stations[1:]):
            pa, pb = chamfered_rect(a["hw"], a["hh"], a["c"]), chamfered_rect(b["hw"], b["hh"], b["c"])
            for e in range(8):
                p0 = np.array([pa[e][0] + a["cx"], a["y"], pa[e][1] + a["cz"]])
                p1 = np.array([pb[e][0] + b["cx"], b["y"], pb[e][1] + b["cz"]])
                K.pipe(p0, p1, rail_r, sides=4, mb=mb, caps=False)
    return bands


def taper(K, mb, p0, p1, r0, r1, sides=8, mat=PLATE, caps=(True, True), rect=None, tile=4.0, phase=0.0, sx=1.0, sz=1.0, lod2=True):
    """A tapered prism (a nacelle, a boom, a pylon, a missile's body) from p0 to p1: radius r0 at p0 and r1 at p1. ``lod2`` False leaves it out of the lowest LOD."""
    if K.lod >= 2 and not lod2:
        return
    d = np.asarray(p1, float) - np.asarray(p0, float)
    ln = np.linalg.norm(d)
    fr = Frame(p0, d, perp(d) if abs(d[2] / ln) > 0.9 else Z)
    prof = regular_poly(sides, 1.0, phase, sx, sz)
    mb.extrude(prof * r0, fr, 0.0, ln, mat, s0=(1.0, 1.0), s1=(r1 / max(r0, 1e-6), r1 / max(r0, 1e-6)), caps=caps, tile=tile, rect=rect)


def band(K, mb, p, axis, r, w, sides=12, mat=CHROME, tile=3.0):
    """A thin collar round something along ``axis`` (a chrome ring on a nacelle or a boom). Not at the lowest LOD."""
    if K.lod >= 2:
        return
    a = norm(axis)
    fr = Frame(np.asarray(p, float) - a * w / 2, a, perp(a) if abs(a[2]) > 0.9 else Z)
    mb.extrude(regular_poly(sides, r), fr, 0.0, w, mat, tile=tile, caps=(False, False))


def collar(K, *args, **kw):
    """A chrome collar where two hull segments join (the capital kit's ring), at LOD0 and LOD1 only."""
    if K.lod < 2:
        K.ring(*args, **kw)


def whip(K, mb, p0, p1, r=0.014, tip=True):
    """An antenna: a thin chrome rod with a small bead at the tip. Not at the lowest LOD."""
    if K.lod >= 2:
        return
    K.pipe(p0, p1, r, sides=4 if K.lod else 5, mb=mb, caps=True)
    if tip and K.lod == 0:
        d = norm(np.asarray(p1, float) - np.asarray(p0, float))
        mb.box(Frame(np.asarray(p1, float), d, perp(d)), (r * 4, r * 5, r * 4), CHROME, tile=2.0)


def hatch(K, centre, run, n, scale=0.28, name="hatch_a", lift=0.012):
    """A trim-sheet hatch, vent or panel at small scale, laid flat on a surface (LOD0)."""
    K.decal(centre, run, n, name, scale=scale, lift=lift)


def light(K, pos, normal, colour=LIGHT_WHITE, size=0.12, kind="running", blink=None):
    K.light(np.asarray(pos, float), normal, colour, size, kind, blink)


def hoop(K, mb, centre, axis, r_out, r_in, depth, segs, key, up=Z, bevel=0.025, plating=None, chrome_rim=True):
    """A ring standing round ``axis`` at ``centre``: ``segs`` armoured segments (each a flat annular trapezoid solid, plated on both faces), a chrome rim on
    the outer edge. A faceted ring, not a smooth one: perfect hard geometry."""
    ax = norm(axis)
    e1 = norm(np.asarray(up, float) - ax * np.dot(up, ax))
    e2 = cross(ax, e1)
    for i in range(segs):
        a0, a1 = 2 * math.pi * i / segs, 2 * math.pi * (i + 1) / segs
        pts = []
        for r, a in ((r_in, a0), (r_out, a0), (r_out, a1), (r_in, a1)):
            pts.append(np.asarray(centre, float) + (e1 * math.cos(a) + e2 * math.sin(a)) * r)
        quad = np.array(pts)
        # make the face normal point along the axis: the quad's own order gives n = (q1-q0) x (q3-q0)
        n = cross(quad[1] - quad[0], quad[3] - quad[0])
        if np.dot(n, ax) < 0:
            quad = quad[::-1]
        solid(K, mb, quad, depth, f"{key}-{i}", bevel=bevel, plating=plating, chrome_edges=(1,) if chrome_rim and False else ())
    if chrome_rim and K.lod <= 1:
        for i in range(segs):
            a0, a1 = 2 * math.pi * i / segs, 2 * math.pi * (i + 1) / segs
            p0 = np.asarray(centre, float) + (e1 * math.cos(a0) + e2 * math.sin(a0)) * (r_out + 0.012)
            p1 = np.asarray(centre, float) + (e1 * math.cos(a1) + e2 * math.sin(a1)) * (r_out + 0.012)
            K.pipe(p0, p1, 0.03, sides=4, mb=mb, caps=False)


# ------------------------------------------------------------------------------------------------ subsystems

def _stump(K, d, fr, radius, key, plates=4, embers=2, s=0.22):
    mnparts.wreck_stump(K, d, fr, radius, radius * 0.9, key, plates=plates, embers=embers, s=s)


def fighter_engine(K, sb, fr, key, throat=0.2, exit_r=0.42, length=0.9, s=0.2):
    """A fighter's engine: the capital kit's recessed bell, a fifth the size, drawn with fewer segments and a thinner flare so twenty of them are cheap."""
    mnparts.engine_nozzle(K, sb, fr, key, throat=throat, exit_r=exit_r, length=length, s=s, bell_segs=20, bell_n=6, plume_shells=4, plume_segs=12, plume_len=0.62)


def needle_gun(K, sb, fr, key, length=1.3, r=0.065, heavy=False):
    """A fixed gun on a hardpoint: an armoured housing, twin chrome barrels (one fat barrel for the heavy gun), a muzzle brake. ``fr``: origin at the housing's
    rear, y = the way it fires, z = up. Wreck: the housing torn open and scorched, a bent barrel stub; debris: a barrel and housing chunks."""
    if sb.lod == 2:
        sb.part().box(fr, (r * 5.2, length + 0.3, r * 5.2), PLATE, centre=(0, length * 0.5, 0), chamfer=r)
        return
    if sb.lod == 1:
        return
    base = sb.part("base")
    hw = r * (3.4 if heavy else 2.6)
    base.box(fr, (hw * 2, length * 0.52, hw * 1.7), PLATE, centre=(0, length * 0.26, 0), chamfer=hw * 0.35, tile=3.0)
    base.box(fr, (hw * 1.2, length * 0.2, hw * 2.1), CHROME, centre=(0, length * 0.46, 0), chamfer=hw * 0.2, tile=2.0)
    xs = [0.0] if heavy else [-r * 1.25, r * 1.25]
    for x in xs:
        K.pipe(fr.p(x, length * 0.4, 0), fr.p(x, length * 0.96, 0), r * (1.5 if heavy else 1.0), sides=8, mb=base)
        base.box(fr, (r * (4.0 if heavy else 2.8), r * 2.2, r * (4.0 if heavy else 2.8)), CHROME, centre=(x, length * 0.96, 0), tile=2.0, chamfer=r * 0.4)
    d = sb.variant("destroyed")
    d.box(fr, (hw * 2, length * 0.3, hw * 1.4), TRIM, centre=(0, length * 0.15, 0), chamfer=hw * 0.3, rect=K.rect("burnt"))
    _stump(K, d, fr.moved(0, length * 0.1, hw * 0.5), hw * 1.3, key, plates=3, embers=2)
    K.pipe(fr.p(r, length * 0.2, 0), fr.p(r * 3.0, length * 0.55, r * 3.0), r, sides=6, mb=d, rect_name="burnt")
    mnparts.debris_set(K, sb, fr, key, [
        {"kind": "tube", "size": (r * 2, length * 0.6, 0), "at": (r, length * 0.7, 0), "speed": 6},
        {"kind": "chunk", "size": (hw * 1.6, length * 0.3, hw), "at": (0, length * 0.3, 0), "speed": 4},
        {"kind": "plate", "size": (hw * 1.5, hw * 1.5, 0.05), "at": (-hw, length * 0.1, hw * 0.5), "speed": 5},
    ])


def sensor_eye(K, sb, fr, key, r=0.28, tall=0.34):
    """The sensor cluster: a chrome-ringed dark lens in an armoured housing, a harsh white ring light round it, two short antennas. ``fr``: origin on the hull,
    y = forward, z = out of the surface."""
    if sb.lod == 2:
        sb.part().box(fr, (r * 2.4, r * 2.6, tall), PLATE, centre=(0, 0, tall / 2), chamfer=r * 0.3)
        return
    if sb.lod == 1:
        return
    base, head = sb.part("base"), sb.part("head")
    base.box(fr, (r * 2.6, r * 2.8, tall * 0.5), PLATE, centre=(0, 0, tall * 0.25), chamfer=r * 0.35, tile=3.0)
    ax = Frame(fr.p(0, 0, tall * 0.5), fr.u, fr.f)
    head.lathe([(r * 1.12, 0.0), (r * 1.12, tall * 0.35), (r * 0.98, tall * 0.5)], ax, CHROME, segs=16, tile=2.0)
    head.lathe([(r * 0.98, tall * 0.5), (r * 0.7, tall * 0.55), (r * 0.5, tall * 0.74), (r * 0.12, tall * 0.84)], ax, ENGINE, segs=16, rect=(0.0, 1.0, 1.0, 0.52))
    head.lathe([(r * 1.0, tall * 0.5), (r * 1.0, tall * 0.54), (r * 0.9, tall * 0.54), (r * 0.9, tall * 0.5)], ax, LIGHTS, segs=16, rect=K.light_rect(LIGHT_WHITE))
    for sx, ln in ((-1, 0.5), (1, 0.34)):
        K.pipe(fr.p(sx * r * 1.55, -r * 0.5, tall * 0.4), fr.p(sx * r * 1.7, -r * 0.5 - ln * 0.4, tall * 0.4 + ln), r * 0.07, sides=4, mb=head)
    d = sb.variant("destroyed")
    d.box(fr, (r * 2.6, r * 2.8, tall * 0.34), TRIM, centre=(0, 0, tall * 0.17), chamfer=r * 0.35, rect=K.rect("burnt"))
    _stump(K, d, fr.moved(0, 0, tall * 0.3), r * 1.4, key, plates=3, embers=2)
    mnparts.debris_set(K, sb, fr, key, [
        {"kind": "chunk", "size": (r * 1.8, r * 1.8, tall * 0.7), "at": (0, 0, tall * 0.8), "speed": 5},
        {"kind": "plate", "size": (r * 1.6, r * 1.6, 0.04), "at": (r, -r, tall), "speed": 7},
        {"kind": "tube", "size": (0.03, 0.7, 0), "at": (-r * 1.5, -r * 0.5, tall * 1.2), "speed": 8},
    ])


def _missile(K, mb, p, d, r, length, torpedo=False):
    """One missile or torpedo: a chrome-tipped dark body with four small fins; ``p`` = tail, ``d`` = flight direction."""
    d = norm(d)
    fr = Frame(p, d, perp(d))
    prof = [(r * 0.55, 0.0), (r, length * 0.06), (r, length * 0.72), (r * 0.82, length * 0.88), (r * 0.46, length * 0.97), (0.0, length)]
    mb.lathe(prof, fr, PLATE_B, segs=8 if not torpedo else 10, tile=3.0)
    mb.lathe([(r * 1.001, length * 0.62), (r * 1.001, length * 0.67), (r * 0.99, length * 0.67), (r * 0.99, length * 0.62)], fr, CHROME, segs=8, tile=2.0)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        o = fr.d(math.sin(a), 0, math.cos(a))
        mb.box(Frame(p + o * r * 1.0 + d * length * 0.1, d, o), (r * 0.12, length * 0.17, r * 1.2), PLATE, tile=2.0, chamfer=r * 0.02)


def missile_rack(K, sb, fr, key, cols=2, rows=2, r=0.07, length=1.5, spacing=0.26, torpedo=False):
    """A missile rack: an armoured cradle (a spine and cross-bars) carrying cols x rows missiles, tails to the rear, with chrome clamps. ``fr``: origin at the
    rack's centre line on the hull, y = forward, z = out of the hull (down for a belly rack). Wreck: a bent cradle, one missile stub, embers; debris: two
    missiles."""
    w = (cols - 1) * spacing
    h = (rows - 1) * spacing
    if sb.lod == 2:
        sb.part().box(fr, (w + r * 3, length + 0.2, h + r * 3), PLATE, centre=(0, 0, 0), chamfer=r)
        return
    if sb.lod == 1:
        return
    base, miss = sb.part("base"), sb.part("missiles")
    base.box(fr, (w + r * 4.2, length * 0.9, 0.07), PLATE, centre=(0, 0, -(h * 0.5 + r * 1.15)), chamfer=0.02, tile=3.0)
    for k in range(3):
        y = -length * 0.35 + length * 0.35 * k
        base.box(fr, (w + r * 4.4, 0.07, h + r * 3.4), PLATE, centre=(0, y, 0), chamfer=0.02, tile=3.0)
        base.box(fr, (w + r * 4.8, 0.045, h + r * 3.8), CHROME, centre=(0, y + 0.06, 0), tile=2.0)
    for i in range(cols):
        for j in range(rows):
            x = (i - (cols - 1) / 2) * spacing
            z = (j - (rows - 1) / 2) * spacing
            _missile(K, miss, fr.p(x, -length * 0.5, z), fr.f, r, length, torpedo)
    d = sb.variant("destroyed")
    d.box(fr, (w + r * 4.2, length * 0.5, 0.06), TRIM, centre=(-0.05, -length * 0.2, -(h * 0.5 + r * 1.15)), chamfer=0.02, rect=K.rect("burnt"))
    d.box(fr, (w + r * 3.0, 0.07, h * 0.6 + r * 2.4), TRIM, centre=(0.06, -length * 0.1, -spacing * 0.3), chamfer=0.02, rect=K.rect("burnt"))
    _stump(K, d, fr.moved(0, 0, 0), max(w, h, r * 3) * 0.9, key, plates=3, embers=2)
    spec = []
    for i in range(min(2, cols * rows)):
        spec.append({"kind": "tube", "size": (r * 2, length * 0.8, 0), "at": ((i - 0.5) * spacing, 0, 0), "speed": 7 + 3 * i})
    spec.append({"kind": "chunk", "size": (w + r * 3, length * 0.3, 0.15), "at": (0, -length * 0.2, 0), "speed": 4})
    mnparts.debris_set(K, sb, fr, key, spec)


# ------------------------------------------------------------------------------------------------ sampling

def damage_points(surfaces, count=28, seed=1):
    """Points on the hull's flat faces, spread out (farthest-point thinning), for the progressive scorch plan: [{p, n}] in Unity ship space."""
    from .mnship import to_unity
    rng = np.random.default_rng(seed)
    pts = []
    for patch in surfaces:
        area = patch.lu * patch.lv
        if area < 0.12:
            continue
        for _ in range(max(1, int(area / 0.5))):
            u, v = rng.uniform(0.2, 0.8, 2)
            pts.append((patch.pt(u, v), patch.normal(), area))
    idx = rng.permutation(len(pts))
    chosen = []
    spacing = 1.6
    while len(chosen) < count and spacing > 0.2:
        chosen = []
        for i in idx:
            p = pts[i][0]
            if all(np.linalg.norm(p - c[0]) > spacing for c in chosen):
                chosen.append(pts[i])
            if len(chosen) >= count:
                break
        spacing *= 0.8
    return [{"p": to_unity(p), "n": to_unity(n)} for p, n, _ in chosen]


def add_site(K, ship, name, p, n, r, hooks=True):
    """A hull damage site at ``p`` (outward normal ``n``): the torn-open look, and the spark and vent hooks that go with it."""
    site_fr = Frame(p, n, perp(n))
    build_fr = Frame(p, perp(n), n)
    ship.add_site(site_fr, r, mnparts.breach(K, build_fr, r, name, s=0.18))
    if hooks:
        ship.add_hook("spark", Frame(np.asarray(p, float) + norm(n) * 0.12, n, perp(n)), 0.4)
        ship.add_hook("vent", Frame(np.asarray(p, float) + norm(n) * 0.08 + perp(n) * r * 0.5, n, perp(n)), 0.6)


def comms_array(K, sb, fr, key, height=1.4, r=0.3):
    """The comms array: an armoured base, a chrome mast with three cross-arms and a small dish, a whip. ``fr``: origin on the hull, y = forward, z = out of the
    surface. Wreck: the mast snapped off short, a scorched base; debris: the dish, the upper mast, a plate."""
    if sb.lod == 2:
        sb.part().box(fr, (r * 2, r * 2, height), PLATE, centre=(0, 0, height / 2), chamfer=r * 0.3)
        return
    if sb.lod == 1:
        return
    base, mast = sb.part("base"), sb.part("mast")
    base.box(fr, (r * 2.6, r * 2.6, height * 0.16), PLATE, centre=(0, 0, height * 0.08), chamfer=r * 0.4, tile=3.0)
    K.pipe(fr.p(0, 0, height * 0.15), fr.p(0, 0, height), r * 0.12, sides=6, mb=mast)
    for k, (zf, wd) in enumerate(((0.45, 0.55), (0.65, 0.4), (0.82, 0.28))):
        mast.box(fr, (r * wd * 2.4, r * 0.1, r * 0.1), CHROME, centre=(0, 0, height * zf), tile=2.0)
    ax = Frame(fr.p(r * 0.35, 0, height * 0.9), norm(fr.d(0.6, 0.5, 0.7)), fr.u)
    mast.lathe([(0.0, 0.0), (r * 0.5, r * 0.07), (r * 0.62, r * 0.18)], ax, CHROME, segs=10, tile=2.0)
    K.pipe(fr.p(-r * 0.4, 0, height * 0.35), fr.p(-r * 0.7, -r * 0.3, height * 1.35), r * 0.035, sides=4, mb=mast)
    d = sb.variant("destroyed")
    d.box(fr, (r * 2.6, r * 2.6, height * 0.1), TRIM, centre=(0, 0, height * 0.05), chamfer=r * 0.4, rect=K.rect("burnt"))
    K.pipe(fr.p(0, 0, height * 0.05), fr.p(r * 0.3, r * 0.1, height * 0.4), r * 0.12, sides=5, mb=d, rect_name="burnt")
    _stump(K, d, fr.moved(0, 0, height * 0.1), r * 1.2, key, plates=2, embers=2)
    mnparts.debris_set(K, sb, fr, key, [
        {"kind": "tube", "size": (r * 0.25, height * 0.7, 0), "at": (0, 0, height * 0.7), "speed": 6},
        {"kind": "chunk", "size": (r * 1.1, r * 1.1, r * 0.4), "at": (r * 0.35, 0, height * 0.9), "speed": 8},
        {"kind": "plate", "size": (r * 1.4, r * 1.4, 0.04), "at": (-r * 0.5, 0, height * 0.3), "speed": 5},
    ])
