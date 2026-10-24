"""mndiaspora: the kit for the diaspora's ships (art_src/diaspora). It sits on the capital kit (mnkit, mnparts) and the small-ship kit (mnfighter) and changes the look.

Earth's ships are perfect: matte black, chrome, hard geometry, harsh white light. The diaspora's are the opposite, and a battle has to read at a glance:
    warm            bone, olive, rust and faded-teal paint where Earth has black; dull steel where Earth has chrome; amber and warm-white lamps where Earth has white
    patched         plates of different batches, repair plates welded over holes (different paint, bolts and a weld bead round them), seams, pipes and cable runs
                    laid on the outside, cargo containers strapped to a spine, stencilled codes, hazard stripes, rust bleeding from the fasteners
    part living     the grafted alien-ally technology: dark, ribbed, wet-looking shells and frames (the BIO material) with spare teal glyph bands, kept rough and spare
Still FS2's language (design section 9): layered silhouettes of plated segments, lit window rows and small coloured running lights for scale, spines and fins
and asymmetry, engines as glowing recessed exhausts. Never wedges, greebled bricks or flat engine discs.

Material slots: the capital kit's twelve (the paint batches are RUST, BONE, OLIVE, STEEL and TEAL, in the kit's tone order) plus BIO, GLYPH and GLASS.
"""
import math
import zlib

import numpy as np

from . import mnmesh, mnparts, mnkit, mnfighter as F
from .mnkit import (PLATE, TRIM, LIGHTS, ENGINE, CHROME, PLATE_B, PLATE_C, PLUME, PLATE_D, PLATE_E, WEAR, PAINT, TONE_MATS, Kit,
                    LIGHT_WHITE, LIGHT_COLD, LIGHT_RED, LIGHT_GREEN, LIGHT_AMBER, LIGHT_BLUE, LIGHT_EMBER)
from .mnmesh import Frame, Patch, MeshBuilder, norm, cross, chamfered_rect, regular_poly
from .mnfighter import X, Y, Z, perp

BIO, GLYPH, GLASS = 12, 13, 14
STEEL = CHROME                                   # the dull brushed steel (the capital kit's "chrome" slot)
RUST_T, BONE_T, OLIVE_T, STEEL_T, TEAL_T = range(5)          # indices into TONE_MATS: PLATE_B (rust), PLATE (bone), PLATE_C (olive), PLATE_D (bare steel), PLATE_E (faded teal)
MATERIALS = ["mn_dia_plate", "mn_dia_trim", "mn_dia_lights", "mn_dia_engine", "mn_dia_chrome", "mn_dia_plate_b", "mn_dia_plate_c", "mn_dia_plume",
             "mn_dia_plate_d", "mn_dia_plate_e", "mn_dia_wear", "mn_dia_paint", "mn_dia_bio", "mn_dia_glyph", "mn_dia_glass"]
LIGHT_TEAL = LIGHT_COLD                          # the lights palette's second swatch is the graft's teal in this set
LIGHT_WARM = LIGHT_WHITE                         # and the first is warm white

PAINT_W = (0.12, 0.40, 0.20, 0.08, 0.20)         # rust, bone, olive, steel, teal: which batch a region of hull is mostly painted in


def set_paint(weights=PAINT_W, follow=0.62):
    """Make plates the diaspora's mix (call before building): each region of hull (a flat about as long as a segment) has a dominant batch, most of its plates
    follow it and the rest are from other yards, so a hull reads as patched from the outside."""
    def tones(self, key, m, rng):
        region = str(key).rsplit("-", 1)[0]
        dom = int(self.rng("tone", region).choice(5, p=weights))
        pick = rng.choice(5, size=m, p=weights)
        return np.where(rng.random(m) < follow, dom, pick).astype(int)
    mnkit.Kit.tones = tones


# ------------------------------------------------------------------------------------------------ small geometry

def sweep(mb, path, r, mat=BIO, sides=8, r_end=None, radii=None, caps=True, tile=2.0):
    """A smooth round tube along a polyline ``path`` (a vein, a cable, a rib): radius ``r`` (to ``r_end``, or per point in ``radii``), shared smooth normals,
    metric UVs (``tile`` metres a repeat). Parallel-transport frames, so a curved path does not twist."""
    P = np.asarray(path, np.float64)
    n = len(P)
    rad = np.linspace(r, r if r_end is None else r_end, n) if radii is None else np.asarray(radii, np.float64)
    T = np.zeros((n, 3))
    T[1:-1] = P[2:] - P[:-2]
    T[0] = P[1] - P[0]
    T[-1] = P[-1] - P[-2]
    T = norm(T)
    Ns, N = [], perp(T[0])
    for i in range(n):
        N = norm(N - T[i] * np.dot(N, T[i]))
        Ns.append(N)
    Ns = np.array(Ns)
    Bs = cross(T, Ns)
    along = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))]
    pos, nrm, uv, tris = [], [], [], []
    for i in range(n):
        for j in range(sides + 1):
            a = 2 * math.pi * j / sides
            d = math.cos(a) * Ns[i] + math.sin(a) * Bs[i]
            pos.append(P[i] + d * rad[i])
            nrm.append(d)
            uv.append((j / sides * 2 * math.pi * rad[i] / tile, along[i] / tile))
    row = sides + 1
    for i in range(n - 1):
        for j in range(sides):
            a = i * row + j
            tris.append((a, a + 1, a + row + 1))
            tris.append((a, a + row + 1, a + row))
    if caps:
        for end, nn in ((0, -T[0]), (n - 1, T[-1])):
            c = len(pos)
            pos.append(P[end])
            nrm.append(nn)
            uv.append((0.5, 0.5))
            for j in range(sides + 1):
                a = 2 * math.pi * j / sides
                d = math.cos(a) * Ns[end] + math.sin(a) * Bs[end]
                pos.append(P[end] + d * rad[end])
                nrm.append(nn)
                uv.append((0.5 + 0.5 * math.cos(a) * rad[end] / tile, 0.5 + 0.5 * math.sin(a) * rad[end] / tile))
            for j in range(sides):
                a, b = c + 1 + j, c + 2 + j
                if np.dot(cross(pos[a] - pos[c], pos[b] - pos[c]), nn) < 0:
                    a, b = b, a
                tris.append((c, a, b))
    uv = np.array(uv)
    mb.add_raw(np.array(pos), np.array(nrm), uv, uv * 1.0, np.array(tris, np.int64), mat)


def arc_path(a, b, bow, n=7, up=None):
    """A gently bowed path from a to b: the middle is pushed ``bow`` metres along ``up`` (a drooping cable, a vein over a hull)."""
    a, b = np.asarray(a, float), np.asarray(b, float)
    up = Z if up is None else np.asarray(up, float)
    return np.array([a + (b - a) * t + up * bow * math.sin(math.pi * t) for t in np.linspace(0, 1, n)])


def ribbed_shell(mb, p0, p1, r0, r1, ribs, depth, mat=BIO, segs=16, key="", jitter=0.25, taper_end=0.0, tile=2.0, lod=0):
    """A dark ribbed shell along p0..p1 (the graft): ``ribs`` raised hoops with grooves between, each a slightly different height, so it reads as grown, not
    machined. Radius ``r0`` at p0 to ``r1`` at p1; the last ``taper_end`` fraction closes into a rounded end."""
    p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
    d = p1 - p0
    ln = float(np.linalg.norm(d))
    fr = Frame(p0, d, perp(d) if abs(d[2] / ln) > 0.9 else Z)
    if lod >= 2:
        mb.lathe([(r0, 0.0), (r1 * (1.0 + depth * 0.5), ln)], fr, mat, segs=max(6, segs // 2), tile=tile)
        return
    rng = np.random.default_rng(zlib.crc32(repr(("ribbed", key)).encode()))
    prof = []
    phases = (0.0, 0.2, 0.34, 0.62, 0.78)
    amp = [1.0 + rng.uniform(-jitter, jitter) for _ in range(ribs)]
    for i in range(ribs):
        for k, ph in enumerate(phases):
            t = (i + ph) / ribs
            base = r0 + (r1 - r0) * t
            bump = (0.0, 0.0, 1.0, 1.0, 0.0)[k] * amp[i]
            rr = base * (1.0 + depth * bump)
            if taper_end > 0 and t > 1.0 - taper_end:
                rr *= max(0.15, math.sqrt(max(0.0, (1.0 - t) / taper_end)))
            prof.append((rr, ln * t))
    prof.append((max(r1 * (0.2 if taper_end > 0 else 1.0), 0.02), ln))
    mb.lathe(prof, fr, mat, segs=segs, tile=tile, crease=35.0)


def bio_hoop(mb, centre, axis, r, width, depth, segs=20, mat=BIO):
    """One raised rib standing round ``axis`` at ``centre`` (a graft rib on a nacelle, a frame hooped round a hull flat)."""
    a = norm(axis)
    fr = Frame(np.asarray(centre, float) - a * width / 2, a, perp(a) if abs(a[2]) > 0.9 else Z)
    mb.lathe([(r, 0.0), (r + depth, width * 0.22), (r + depth, width * 0.78), (r, width)], fr, mat, segs=segs, tile=2.0)


def glyph_band(K, p0, run, n, length, name="glyph_a", lift=0.03, scale=1.0, mb=None):
    """A band of teal glyphs (the trim sheet's emissive strip) laid along ``run`` on a surface facing ``n``. LOD0 and LOD1."""
    if K.lod >= 2 or name not in K.trims:
        return 0
    return K.strip(p0, run, n, length, name, lift=lift, scale=scale, mb=mb)


def weld(K, p0, p1, n, lift=0.012, scale=1.0, mb=None):
    """A weld bead laid between two points on a surface facing ``n`` (LOD0)."""
    if K.lod >= 1 or "weld" not in K.trims:
        return
    p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
    d = p1 - p0
    ln = float(np.linalg.norm(d))
    if ln < 0.05:
        return
    run = d / ln
    y = cross(n, run)
    K.strip(p0 - y * (K.piece_m("weld")[1] * scale * 0.5), run, n, ln, "weld", lift=lift, scale=scale, mb=mb)


def repair_patch(K, mb, patch, key, count=2, size=(0.6, 1.6), height=(0.02, 0.05), margin=0.3, bolts=True, welds=True, tones=(RUST_T, STEEL_T, TEAL_T, OLIVE_T), weights=(0.2, 0.45, 0.2, 0.15),
                 bolt_r=0.02, weld_scale=None):
    """Plates welded over damage: ``count`` irregular rectangles on ``patch`` in a different batch of paint (or bare steel), a degree or two off square, with a bead
    of weld round the edge and a bolt at each corner. LOD0 only. Returns the number laid."""
    if K.lod > 0:
        return 0
    rng = K.rng("repair", key)
    lu, lv = patch.lu, patch.lv
    n = patch.normal()
    made = 0
    for i in range(count):
        w = float(rng.uniform(*size))
        l = w * float(rng.uniform(0.7, 1.9))
        if lu < w + 2 * margin or lv < l + 2 * margin:
            continue
        uc = float(rng.uniform(w / 2 + margin, lu - w / 2 - margin))
        vc = float(rng.uniform(l / 2 + margin, lv - l / 2 - margin))
        c = patch.pt(uc / lu, vc / lv)
        eu, ev = patch.eu(uc / lu, vc / lv), patch.ev(uc / lu, vc / lv)
        ang = float(rng.uniform(-0.09, 0.09))
        ca, sa = math.cos(ang), math.sin(ang)
        e1 = eu * ca + ev * sa
        e2 = -eu * sa + ev * ca
        corners = np.array([c - e1 * w / 2 - e2 * l / 2, c + e1 * w / 2 - e2 * l / 2, c + e1 * w / 2 + e2 * l / 2, c - e1 * w / 2 + e2 * l / 2])
        h = float(rng.uniform(*height))
        tone = int(rng.choice(tones, p=np.asarray(weights) / np.sum(weights)))
        off = rng.uniform(0, 1, (1, 2))
        mb.slabs(corners[None], n[None], np.array([h]), np.array([min(0.025, h * 0.9)]), TONE_MATS[tone], TRIM, tile=12.0, tile_off=off,
                 edge_rect=(K.rect("edge_wall"), K.rect("edge_chamfer")), edge_tile=8.0)
        top = corners + n * (h + 0.004)
        if welds:
            for a in range(4):
                weld(K, top[a], top[(a + 1) % 4], n, lift=0.0, scale=weld_scale or (0.5 if min(w, l) < 1.0 else 0.8), mb=mb)
        if bolts:
            for a in range(4):
                p = top[a] + (c - top[a]) * min(0.18, 0.12 / max(np.linalg.norm(c - top[a]), 0.05) * 1.0)
                K.pipe(p, p + n * bolt_r * 1.3, bolt_r, sides=5, mb=mb)
        made += 1
    return made


def pipe_run(K, mb, pts, r, key, bracket=1.6, mat=STEEL, sides=6, bracket_size=1.0):
    """A pipe laid on the outside of a hull along a polyline, with a clamp bracket every ``bracket`` metres and a flange at each bend (LOD0 and LOD1 for the pipe,
    LOD0 for the clamps)."""
    if K.lod >= 2:
        return
    P = [np.asarray(p, float) for p in pts]
    for a, b in zip(P[:-1], P[1:]):
        K.pipe(a, b, r, sides=sides if K.lod == 0 else 4, mb=mb)
    if K.lod == 0:
        for a, b in zip(P[:-1], P[1:]):
            d = b - a
            ln = float(np.linalg.norm(d))
            if ln < 0.2:
                continue
            u = d / ln
            for k in range(int(ln / bracket)):
                c = a + u * ((k + 0.5) * bracket + 0.1)
                mb.box(Frame(c, u, perp(u)), (r * 3.0 * bracket_size, r * 1.1, r * 3.0 * bracket_size), PLATE_D, tile=2.0, chamfer=r * 0.3)
        for p in P[1:-1]:
            mb.box(Frame(p, Y, Z), (r * 2.8, r * 2.8, r * 2.8), STEEL, tile=2.0, chamfer=r * 0.5)


def container(K, mb, frame, size, kind, key, straps=True):
    """A shipping container strapped to a hull: ``frame`` at its centre (y along its length, z up), ``size`` (width, length, height) in metres; ``kind`` is a trim piece
    (container_a rust red, _b blue-grey, _c sand). Corrugated sides from the trim sheet, plain plate ends and top, steel straps. LOD1: a plain box."""
    w, l, h = size
    if K.lod >= 1:
        mb.box(frame, (w, l, h), TONE_MATS[BONE_T], tile=4.0, chamfer=0.04)
        return
    hw, hl, hh = w / 2, l / 2, h / 2
    rect = K.rect(kind)
    pw, ph = K.piece_m(kind)

    def p(x, y, z):
        return frame.p(x, y, z)
    for side in (-1, 1):
        a, b = p(side * hw, -hl, -hh), p(side * hw, hl, -hh)
        c, d = p(side * hw, hl, hh), p(side * hw, -hl, hh)
        if side > 0:
            mb.quad_rect(a, b, c, d, rect, TRIM, uspan=(0.0, min(1.0, l / pw)), vspan=(0.0, min(1.0, h / ph)))
        else:
            mb.quad_rect(b, a, d, c, rect, TRIM, uspan=(0.0, min(1.0, l / pw)), vspan=(0.0, min(1.0, h / ph)))
    P = np.array([[p(-hw, -hl, hh), p(hw, -hl, hh), p(hw, hl, hh), p(-hw, hl, hh)],            # top
                  [p(-hw, hl, -hh), p(hw, hl, -hh), p(hw, hl, hh), p(-hw, hl, hh)],              # front end
                  [p(hw, -hl, -hh), p(-hw, -hl, -hh), p(-hw, -hl, hh), p(hw, -hl, hh)]])         # back end
    mb.quads_metric(P, TONE_MATS[OLIVE_T if kind != "container_c" else BONE_T], 6.0, np.zeros((3, 2)), 2.0)
    if straps:
        for t in (-0.28, 0.28):
            mb.box(frame, (w + 0.06, 0.07, h + 0.06), STEEL, centre=(0, l * t, 0), tile=2.0)


def hazard_band(K, p0, run, n, length, lift=0.02, scale=1.0, mb=None):
    """A diagonal yellow-and-black hazard stripe band along ``run`` on a surface facing ``n``. LOD0 and LOD1."""
    if K.lod >= 2 or "hazard" not in K.trims:
        return 0
    return K.strip(p0, run, n, length, "hazard", lift=lift, scale=scale, mb=mb)


# ------------------------------------------------------------------------------------------------ subsystems for small ships

def _stump(K, d, fr, radius, key, plates=4, embers=2, s=0.22):
    mnparts.wreck_stump(K, d, fr, radius, radius * 0.9, key, plates=plates, embers=embers, s=s)


def sensor_eye(K, sb, fr, key, r=0.3, tall=0.36):
    """The player-side sensor cluster: a dark ribbed alien eye in a worn plated seat, a ring of teal light round the lens, a whip aerial and a small steel dish. ``fr``:
    origin on the hull, y = forward, z = out of the surface. Wreck: the seat scorched, the eye gone; debris: the eye, a rib, the dish."""
    if sb.lod == 2:
        sb.part().box(fr, (r * 2.4, r * 2.6, tall), PLATE, centre=(0, 0, tall / 2), chamfer=r * 0.3)
        return
    if sb.lod == 1:
        return
    base, head = sb.part("base"), sb.part("head")
    base.box(fr, (r * 2.7, r * 2.9, tall * 0.4), PLATE, centre=(0, 0, tall * 0.2), chamfer=r * 0.35, tile=3.0)
    base.lathe([(r * 1.25, tall * 0.4), (r * 1.25, tall * 0.5), (r * 1.1, tall * 0.55)], Frame(fr.p(0, 0, 0), fr.u, fr.f), STEEL, segs=14, tile=2.0)
    ax = Frame(fr.p(0, 0, tall * 0.5), fr.u, fr.f)
    prof = [(r * 1.05, 0.0), (r * 1.08, tall * 0.1), (r * 0.92, tall * 0.18), (r * 1.0, tall * 0.3), (r * 0.8, tall * 0.42), (r * 0.78, tall * 0.5),
            (r * 0.45, tall * 0.62), (r * 0.1, tall * 0.68)]
    head.lathe(prof, ax, BIO, segs=16, tile=1.0, crease=35.0)
    head.lathe([(r * 0.46, tall * 0.62), (r * 0.5, tall * 0.6), (r * 0.5, tall * 0.57), (r * 0.46, tall * 0.57)], ax, LIGHTS, segs=16, rect=K.light_rect(LIGHT_TEAL))
    head.disc(ax, r * 0.28, tall * 0.665, LIGHTS, segs=12, facing=1.0, rect=K.light_rect(LIGHT_TEAL))
    for sx, ln in ((-1, 0.62), (1, 0.4)):
        K.pipe(fr.p(sx * r * 1.55, -r * 0.5, tall * 0.4), fr.p(sx * r * 1.75, -r * 0.5 - ln * 0.4, tall * 0.4 + ln), r * 0.07, sides=4, mb=head)
    head.lathe([(0.0, 0.0), (r * 0.5, r * 0.05), (r * 0.62, r * 0.17)], Frame(fr.p(r * 1.3, r * 1.0, tall * 0.45), norm(fr.d(0.5, 0.4, 0.75)), fr.u), STEEL, segs=10, tile=2.0)
    d = sb.variant("destroyed")
    d.box(fr, (r * 2.7, r * 2.9, tall * 0.3), TRIM, centre=(0, 0, tall * 0.15), chamfer=r * 0.35, rect=K.rect("burnt"))
    _stump(K, d, fr.moved(0, 0, tall * 0.28), r * 1.4, key, plates=3, embers=2)
    mnparts.debris_set(K, sb, fr, key, [
        {"kind": "chunk", "size": (r * 1.7, r * 1.7, tall * 0.7), "at": (0, 0, tall * 0.8), "speed": 5},
        {"kind": "plate", "size": (r * 1.6, r * 1.6, 0.04), "at": (r, -r, tall), "speed": 7},
        {"kind": "tube", "size": (0.03, 0.7, 0), "at": (-r * 1.5, -r * 0.5, tall * 1.2), "speed": 8},
    ])


def needle_gun(K, sb, fr, key, length=1.4, r=0.065, heavy=False):
    """A fixed gun on a hardpoint, the diaspora's: a worn plated housing with a hazard band, steel barrels, a dark ribbed alien feed (a living ammunition vein)
    running back into the wing, a muzzle brake. ``fr``: origin at the housing's rear, y = the way it fires. Wreck: the housing torn open, a bent barrel stub."""
    if sb.lod == 2:
        sb.part().box(fr, (r * 5.2, length + 0.3, r * 5.2), PLATE, centre=(0, length * 0.5, 0), chamfer=r)
        return
    if sb.lod == 1:
        return
    base = sb.part("base")
    hw = r * (3.5 if heavy else 2.7)
    base.box(fr, (hw * 2, length * 0.55, hw * 1.75), PLATE, centre=(0, length * 0.275, 0), chamfer=hw * 0.35, tile=3.0)
    base.box(fr, (hw * 1.25, length * 0.2, hw * 2.15), PLATE_D, centre=(0, length * 0.5, 0), chamfer=hw * 0.2, tile=2.0)
    base.box(fr, (hw * 2.06, length * 0.05, hw * 1.8), PLATE_E, centre=(0, length * 0.17, 0), tile=2.0)
    xs = [0.0] if heavy else [-r * 1.3, r * 1.3]
    for x in xs:
        K.pipe(fr.p(x, length * 0.42, 0), fr.p(x, length * 0.97, 0), r * (1.5 if heavy else 1.0), sides=8, mb=base)
        base.box(fr, (r * (4.0 if heavy else 2.8), r * 2.2, r * (4.0 if heavy else 2.8)), STEEL, centre=(x, length * 0.97, 0), tile=2.0, chamfer=r * 0.4)
    ribbed_shell(base, fr.p(0, -length * 0.05, hw * 0.9), fr.p(0, length * 0.3, hw * 0.9), r * 1.0, r * 1.15, 3, 0.45, segs=8, key=key)
    d = sb.variant("destroyed")
    d.box(fr, (hw * 2, length * 0.3, hw * 1.4), TRIM, centre=(0, length * 0.15, 0), chamfer=hw * 0.3, rect=K.rect("burnt"))
    _stump(K, d, fr.moved(0, length * 0.1, hw * 0.5), hw * 1.3, key, plates=3, embers=2)
    K.pipe(fr.p(r, length * 0.2, 0), fr.p(r * 3.0, length * 0.55, r * 3.0), r, sides=6, mb=d, rect_name="burnt")
    mnparts.debris_set(K, sb, fr, key, [
        {"kind": "tube", "size": (r * 2, length * 0.6, 0), "at": (r, length * 0.7, 0), "speed": 6},
        {"kind": "chunk", "size": (hw * 1.6, length * 0.3, hw), "at": (0, length * 0.3, 0), "speed": 4},
        {"kind": "plate", "size": (hw * 1.5, hw * 1.5, 0.05), "at": (-hw, length * 0.1, hw * 0.5), "speed": 5},
    ])


def fighter_engine(K, sb, fr, key, throat=0.22, exit_r=0.46, length=0.95, s=0.2, graft=True):
    """A fighter's engine: the capital kit's recessed bell (steel here), a fifth the size, a short amber flare, with a ring of grown alien ribs clamped round its
    throat and a steel collar. Wreck and debris as the kit's."""
    mnparts.engine_nozzle(K, sb, fr, key, throat=throat, exit_r=exit_r, length=length, s=s, bell_segs=20, bell_n=6, plume_shells=4, plume_segs=12, plume_len=0.62)
    if graft and sb.lod == 0:
        base = sb.part("base")
        for k, y in enumerate((0.18, 0.5)):
            bio_hoop(base, fr.p(0, y, 0), fr.f, exit_r * (1.45 - 0.2 * k), 0.09, 0.05, segs=14)
