"""mnparts: the ship's separable parts, each with an intact look, a wrecked look and debris (so the combat system can call them destroyed).

Every builder takes the kit ``K``, the subsystem context ``sb`` (from ``ctx.subsystem(...)``) and a frame: origin on the hull (or at the
pivot), local y = where it points, local z = out of the surface. They draw in SHIP space through the frame. Detail by LOD: LOD0 builds
the real thing as separate objects, LOD1 skips (subsystems are separate objects there too, drawn by the LOD0 meshes), LOD2 a box
baked into the hull.
"""
import math

import numpy as np

from . import mnmesh
from .mnkit import PLATE, TRIM, LIGHTS, ENGINE, LIGHT_EMBER, LIGHT_COLD, LIGHT_WHITE, LIGHT_RED, LIGHT_GREEN
from .mnmesh import Frame, MeshBuilder, norm, cross, chamfered_rect


def _perp(n):
    n = norm(n)
    a = np.array([0, 0, 1.0]) if abs(n[2]) < 0.9 else np.array([1.0, 0, 0])
    return norm(cross(a, n))


# ------------------------------------------------------------------------------------------------ wreck helpers

def chunk(K, mb, centre, size, rng, rect_name="burnt", jitter=0.28):
    """A torn lump: a box with its eight corners knocked about, burnt metal on every face."""
    s = np.asarray(size, np.float64) / 2.0
    corners = np.array([[(i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1] for i in range(8)], np.float64) * s
    corners = corners * (1.0 + rng.uniform(-jitter, jitter, (8, 3)))
    corners = corners + np.asarray(centre, np.float64)
    c0 = corners.mean(axis=0)
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 2, 6, 4), (1, 5, 7, 3), (0, 4, 5, 1), (2, 3, 7, 6)]
    rect = K.rect(rect_name)
    u0, v0, u1, v1 = rect
    quads, uvs = [], []
    for f in faces:
        q = corners[list(f)]
        n = cross(q[2] - q[0], q[3] - q[1])
        if np.dot(n, q.mean(axis=0) - c0) < 0:
            q = q[::-1]
        quads.append(q)
        w, h = (u1 - u0) * rng.uniform(0.3, 0.5), (v1 - v0) * rng.uniform(0.3, 0.5)
        ox, oy = u0 + (u1 - u0 - w) * rng.random(), v0 + (v1 - v0 - h) * rng.random()
        uvs.append([(ox, oy), (ox + w, oy), (ox + w, oy + h), (ox, oy + h)])
    P = np.array(quads)
    N = norm(cross(P[:, 2] - P[:, 0], P[:, 3] - P[:, 1]))
    mb.quads(P, np.array(uvs), P[:, :, :2] * 0.5, TRIM, N)


def torn_plate(K, mb, origin, normal, size, rng, thickness=0.12, rect_name="burnt"):
    """A ragged plate of burnt metal standing in the air at ``origin``, its face along ``normal``."""
    n = norm(normal)
    fr = Frame(origin, n, _perp(n))
    k = int(rng.integers(5, 9))
    ang = (np.arange(k) + rng.uniform(-0.3, 0.3, k)) * 2 * math.pi / k
    r = size * rng.uniform(0.4, 1.0, k)
    prof = np.stack([np.cos(ang) * r, np.sin(ang) * r], axis=1)
    rect = K.rect(rect_name)
    mb.extrude(prof, fr, 0, thickness, TRIM, rect=rect, caps=(True, True), cap_mat=TRIM, cap_rect=rect)


def wreck_stump(K, mb, fr, radius, height, key, plates=5, embers=3, rect_name="burnt"):
    """What is left where a turret or housing stood: a scorched, ragged floor, torn plates leaning out of it, glowing embers."""
    rng = K.rng("stump", key)
    rect = K.rect(rect_name)
    k = 9
    ang = np.linspace(0, 2 * math.pi, k, endpoint=False) + rng.uniform(-0.15, 0.15, k)
    r = radius * rng.uniform(0.75, 1.1, k)
    prof = np.stack([np.sin(ang) * r, np.cos(ang) * r], axis=1)
    mb.extrude(prof, Frame(fr.p(0, 0, 0.1), fr.u, fr.f), 0, 0.5 + 0.2 * radius * 0.3, TRIM, rect=rect, caps=(True, True), cap_mat=TRIM, cap_rect=rect)
    for i in range(plates):
        th = rng.uniform(0, 2 * math.pi)
        rr = radius * rng.uniform(0.35, 0.9)
        phi = math.radians(rng.uniform(35, 80))
        dirn = fr.d(math.cos(th) * math.sin(phi), math.sin(th) * math.sin(phi), math.cos(phi) * 0.4)
        z = rng.uniform(0.25, 0.7) * height
        o = fr.p(math.cos(th) * rr, math.sin(th) * rr, 0.5 + z)
        torn_plate(K, mb, o, dirn, radius * rng.uniform(0.35, 0.7), rng, rect_name=rect_name)
    for i in range(embers):
        th = rng.uniform(0, 2 * math.pi)
        rr = radius * rng.uniform(0.1, 0.6)
        o = fr.p(math.cos(th) * rr, math.sin(th) * rr, 0.62)
        _ember(K, mb, o, fr.u, rng.uniform(0.18, 0.35))


def _ember(K, mb, pos, normal, size):
    n = norm(normal)
    fr = Frame(pos, n, _perp(n))
    mb.box(fr, (size, size * 0.4, size), LIGHTS, centre=(0, size * 0.2, 0), rect=K.light_rect(LIGHT_EMBER), caps=True)


def debris_set(K, sb, fr, key, specs):
    """Register detachable pieces. specs: list of dicts: size (x, y, z), at (local x, y, z where it sat), speed (m/s), kind 'chunk'|'tube'|'plate'."""
    rng = K.rng("debris", key)
    for sp in specs:
        mb = MeshBuilder("debris")
        if sp.get("kind") == "tube":
            L = sp["size"][1]
            K.pipe((0, -L / 2, 0), (0, L / 2, 0), sp["size"][0] / 2, mb=mb, rect_name="chrome_brushed")
        elif sp.get("kind") == "plate":
            torn_plate(K, mb, (0, 0, 0), (0, 0, 1), max(sp["size"]) * 0.5, rng, thickness=0.15)
        else:
            chunk(K, mb, (0, 0, 0), sp["size"], rng)
        d = np.array([rng.normal(0, 0.8), rng.normal(0.3, 0.8), rng.uniform(0.6, 1.4)])
        d = d / np.linalg.norm(d)
        v = d * sp.get("speed", 6.0) * rng.uniform(0.7, 1.3)
        spin = rng.normal(0, 70, 3)
        sb.debris_piece(mb, sp["at"], v, spin)


# ------------------------------------------------------------------------------------------------ gun turrets

def gun_turret(K, sb, fr, key, s=1.0, barrels=2, barrel_len=4.6, barrel_r=0.26, heavy=False):
    """Flak battery or anti-fighter gun: a seated ring, a turntable body with an armoured mantlet, chrome barrels with muzzle brakes."""
    rng = K.rng("turret", key)
    if sb.lod == 2:
        sb.part().box(fr, (3.4 * s, 4.6 * s, 2.4 * s), PLATE, centre=(0, 0.5 * s, 1.2 * s), chamfer=0.5 * s)
        return
    if sb.lod == 1:
        return
    base, head = sb.part("base"), sb.part("head")
    chrome = K.rect("chrome_brushed")
    # seat: a low chamfered pad with a chrome ring on it
    base.box(fr, (4.4 * s, 4.4 * s, 0.5 * s), PLATE, centre=(0, 0, 0.25 * s), chamfer=0.9 * s)
    axis = Frame(fr.p(0, 0, 0.5 * s), fr.u, fr.f)
    base.lathe([(2.35 * s, 0.0), (2.35 * s, 0.22 * s), (2.0 * s, 0.5 * s), (1.55 * s, 0.5 * s)], axis, TRIM, segs=24, rect=chrome)
    hz = 1.4 * s
    body = chamfered_rect(1.4 * s, 0.85 * s, 0.4 * s)
    bf = Frame(fr.p(0, 0, hz), fr.f, fr.u)
    head.extrude(body, bf, -1.6 * s, 1.1 * s, PLATE, tile=5.0, cap_mat=PLATE)
    head.extrude(body, bf, 1.1 * s, 1.75 * s, TRIM, s0=(1.0, 1.0), s1=(0.72, 0.78), rect=K.rect("chrome_panel"), caps=(False, True), cap_mat=PLATE)
    # a raised sight block on top and ammunition boxes on the flanks
    head.box(bf, (1.0 * s, 1.2 * s, 0.55 * s), PLATE, centre=(0.4 * s, -0.3 * s, 0.95 * s), chamfer=0.15 * s, tile=5.0)
    for sx in (-1, 1):
        head.box(bf, (0.5 * s, 1.5 * s, 0.9 * s), PLATE, centre=(sx * 1.65 * s, -0.5 * s, -0.05 * s), chamfer=0.12 * s, tile=5.0)
    xs = [0.0] if barrels == 1 else [-0.62 * s, 0.62 * s] if barrels == 2 else [-0.9 * s, 0.0, 0.9 * s]
    for x in xs:
        y0, y1 = 1.5 * s, (1.5 + barrel_len) * s
        K.pipe(fr.p(x, y0, hz + 0.05 * s), fr.p(x, y1, hz + 0.05 * s), barrel_r * s, mb=head, sides=8)
        head.box(fr, (barrel_r * 2.9 * s, 0.7 * s, barrel_r * 2.9 * s), TRIM, centre=(x, y1 - 0.35 * s, hz + 0.05 * s), rect=chrome)
    # ---- wreck: the ring scorched, a stump, one barrel bent over, torn plates and embers
    d = sb.variant("destroyed")
    burnt = K.rect("burnt")
    d.box(fr, (4.4 * s, 4.4 * s, 0.5 * s), PLATE, centre=(0, 0, 0.25 * s), chamfer=0.9 * s)
    d.lathe([(2.35 * s, 0.0), (2.35 * s, 0.22 * s), (2.0 * s, 0.5 * s), (1.55 * s, 0.5 * s)], axis, TRIM, segs=24, rect=burnt)
    wreck_stump(K, d, fr, 1.9 * s, 1.5 * s, key, plates=5, embers=3)
    K.pipe(fr.p(-0.6 * s, 0.4 * s, 0.9 * s), fr.p(-1.1 * s, 2.2 * s, 2.1 * s), barrel_r * s, mb=d, rect_name="burnt", sides=8)
    # ---- debris
    debris_set(K, sb, fr, key, [
        {"kind": "tube", "size": (barrel_r * 2 * s, barrel_len * 0.7 * s, 0), "at": (0.62 * s, 2.6 * s, hz + 0.3 * s), "speed": 9},
        {"kind": "chunk", "size": (1.9 * s, 2.2 * s, 1.1 * s), "at": (0.0, 0.0, hz + 0.2 * s), "speed": 6},
        {"kind": "plate", "size": (1.6 * s, 1.6 * s, 0.2), "at": (-0.8 * s, -0.8 * s, hz + 0.5 * s), "speed": 8},
        {"kind": "chunk", "size": (0.9 * s, 0.9 * s, 0.7 * s), "at": (1.3 * s, -0.6 * s, hz), "speed": 11},
    ])


# ------------------------------------------------------------------------------------------------ engines

def engine_nozzle(K, sb, fr, key, throat=2.6, exit_r=6.0, length=13.0, hull_y=0.0):
    """A recessed engine: a housing set into the hull, a bell flaring from a hot throat, ribbed chrome outside, and inside a glowing gradient
    (white at the throat, through blue to violet at the lip) with a bright combustion disc. ``fr``: origin at the hull face, y = the way the
    exhaust goes (out the back)."""
    if sb.lod == 2:
        sb.part().box(fr, (exit_r * 2, length, exit_r * 2), PLATE, centre=(0, length / 2, 0), chamfer=exit_r * 0.4)
        return
    if sb.lod == 1:
        return
    base = sb.part("base")
    chrome = K.rect("chrome_brushed")
    inner_rect = (0.0, 1.0, 1.0, 0.52)
    disc_rect = (0.1, 0.02, 0.9, 0.48)
    N = 10
    t = np.linspace(0, 1, N)
    prof_in = [(throat + (exit_r - throat) * (tt ** 0.62), 1.0 + length * tt) for tt in t]
    wall = 0.45
    prof_out = [(r + wall, y) for r, y in prof_in]
    # outer bell (chrome) and the thick lip ring
    base.lathe(prof_out, fr, TRIM, segs=40, rect=chrome)
    base.lathe([(prof_out[-1][0], prof_out[-1][1]), (prof_out[-1][0], prof_out[-1][1] + 0.35), (prof_in[-1][0], prof_out[-1][1] + 0.35)], fr, TRIM, segs=40, rect=chrome)
    base.lathe([(prof_in[-1][0], prof_out[-1][1] + 0.35), (prof_in[-1][0], prof_out[-1][1])], fr, TRIM, segs=40, rect=chrome)
    # inner bell: emissive gradient, normals toward the axis
    base.lathe(prof_in, fr, ENGINE, segs=40, rect=inner_rect, flip=True)
    # combustion disc, recessed at the throat, facing out
    base.disc(fr, throat, 1.0, ENGINE, segs=32, facing=1.0, rect=disc_rect)
    # ribs round the bell
    for tt in (0.25, 0.5, 0.75):
        r = throat + (exit_r - throat) * (tt ** 0.62) + wall
        y = 1.0 + length * tt
        base.lathe([(r, y - 0.18), (r + 0.3, y - 0.1), (r + 0.3, y + 0.1), (r, y + 0.18)], fr, TRIM, segs=40, rect=chrome)
    # the housing: a chamfered black block set into the hull round the throat, with a chrome collar
    hb = exit_r * 1.25
    base.box(fr, (hb * 2, 1.6, hb * 2), PLATE, centre=(0, 0.3, 0), chamfer=hb * 0.3, tile=6.0)
    base.lathe([(throat + wall + 0.1, 0.9), (exit_r * 0.78, 0.9), (exit_r * 0.78, 1.4), (throat + wall + 0.1, 1.4)], fr, TRIM, segs=40, rect=chrome)
    # ---- wreck: the bell torn open on one side, dead and dark with a few embers; no gradient
    d = sb.variant("destroyed")
    burnt = K.rect("burnt")
    rng = K.rng("engine-wreck", key)
    a0 = rng.uniform(0, 360)
    prof_cut = [(r, y * 0.0 + y) for r, y in prof_in[:6]]
    prof_cut_out = [(r + wall, y) for r, y in prof_cut]
    d.lathe(prof_cut_out, fr, TRIM, segs=28, rect=burnt, arc=(a0, a0 + 250))
    d.lathe(prof_cut, fr, TRIM, segs=28, rect=burnt, flip=True, arc=(a0, a0 + 250))
    d.box(fr, (hb * 2, 1.6, hb * 2), PLATE, centre=(0, 0.3, 0), chamfer=hb * 0.3, tile=6.0)
    d.disc(fr, throat * 1.1, 1.0, TRIM, segs=24, facing=1.0, rect=burnt)
    for i in range(4):
        th = rng.uniform(0, 2 * math.pi)
        rr = throat * rng.uniform(0.2, 0.9)
        _ember(K, d, fr.p(math.cos(th) * rr, 1.05, math.sin(th) * rr), fr.f, rng.uniform(0.25, 0.5))
    for i in range(4):
        th = a0 + 250 + rng.uniform(5, 100)
        r_ = prof_cut[-1][0] + wall
        torn_plate(K, d, fr.p(math.sin(math.radians(th)) * r_, prof_cut[-1][1] + rng.uniform(0, 2), math.cos(math.radians(th)) * r_),
                   fr.d(math.sin(math.radians(th)), 0.3, math.cos(math.radians(th))), exit_r * rng.uniform(0.18, 0.35), rng, thickness=0.2)
    debris_set(K, sb, fr, key, [
        {"kind": "chunk", "size": (exit_r * 0.9, 3.0, exit_r * 0.5), "at": (exit_r * 0.8, 1.0 + length * 0.8, 0.0), "speed": 7},
        {"kind": "plate", "size": (exit_r, exit_r, 0.2), "at": (-exit_r * 0.7, 1.0 + length * 0.9, exit_r * 0.3), "speed": 9},
        {"kind": "chunk", "size": (2.2, 2.2, 1.6), "at": (0.0, 3.0, -exit_r * 0.5), "speed": 10},
    ])


# ------------------------------------------------------------------------------------------------ breach (hull damage site)

def breach(K, fr, radius, key):
    """The look of a hull torn open: a scorched crater floor that fills the plating, ragged petals of plate bent out of it, embers.
    Built in ship space around the site's frame (y = outward normal... here local z is used as out-of-surface: pass a frame with u = the normal)."""
    mb = MeshBuilder("breach")
    rng = K.rng("breach", key)
    rect = K.rect("burnt")
    k = 12
    ang = np.linspace(0, 2 * math.pi, k, endpoint=False) + rng.uniform(-0.12, 0.12, k)
    r = radius * rng.uniform(0.62, 0.92, k)
    prof = np.stack([np.sin(ang) * r, np.cos(ang) * r], axis=1)
    mb.extrude(prof, Frame(fr.p(0, 0, 0.45), fr.u, fr.f), 0, 0.4, TRIM, rect=rect, caps=(True, True), cap_mat=TRIM, cap_rect=rect)
    # a second, smaller, deeper-looking floor and a ring of petals
    mb.extrude(prof * 0.55, Frame(fr.p(0, 0, 0.8), fr.u, fr.f), 0, 0.15, TRIM, rect=rect, caps=(True, True), cap_mat=TRIM, cap_rect=rect)
    for i in range(11):
        th = (i + rng.uniform(-0.3, 0.3)) * 2 * math.pi / 11
        rr = radius * rng.uniform(0.7, 1.0)
        phi = math.radians(rng.uniform(25, 60))
        nrm = fr.d(-math.cos(th) * math.sin(phi), -math.sin(th) * math.sin(phi), math.cos(phi))
        o = fr.p(math.cos(th) * rr, math.sin(th) * rr, 0.6 + radius * rng.uniform(0.08, 0.3))
        torn_plate(K, mb, o, nrm, radius * rng.uniform(0.25, 0.5), rng, thickness=0.18)
    for i in range(6):
        th = rng.uniform(0, 2 * math.pi)
        rr = radius * rng.uniform(0.1, 0.55)
        _ember(K, mb, fr.p(math.cos(th) * rr, math.sin(th) * rr, 0.98), fr.u, rng.uniform(0.25, 0.6))
    return mb
