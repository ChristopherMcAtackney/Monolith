"""mnheavy: the parts of Earth's destroyer that make it dangerous and big: the heavy beam cannons (a turret, a caged barrel of coils, a red lens: the parts a pilot is told to
silence), and the chin hangar (a plated pod under the fore hull with doors hung open, launching bombers). Same contract as mnparts: the kit ``K``, the subsystem context ``sb``, a
frame, a key; an intact look, a wreck and debris. The subsystem's own frame (``sb.frame``) is where combat reads the part: for a cannon that is the MUZZLE (the juice starts the
beam's charge glow and the beam itself at the part's position), so the debris is described relative to it with ``loc``.
"""
import math

import numpy as np

from . import mnparts
from .mnkit import PLATE, TRIM, LIGHTS, ENGINE, CHROME, PLATE_B, PLATE_C, PLATE_D, PLATE_E, WEAR, PAINT, LIGHT_RED, LIGHT_EMBER, LIGHT_COLD, LIGHT_WHITE, LIGHT_AMBER
from .mnmesh import Frame, MeshBuilder, norm, cross, chamfered_rect

Z = np.array([0.0, 0.0, 1.0])


def loc(fr, wp):
    """A ship-space point in a frame's own coordinates (x right, y forward, z up): what a part's debris is placed in."""
    q = np.asarray(wp, float) - fr.o
    return (float(q @ fr.r), float(q @ fr.f), float(q @ fr.u))


def _lamp(K, mb, pos, normal, colour, size):
    if K.lod > 1:
        return
    n = norm(normal)
    up = Z if abs(n[2]) < 0.9 else np.array([0.0, 1.0, 0.0])
    mb.box(Frame(pos, n, up), (size, size * 0.55, size), LIGHTS, centre=(0, size * 0.25, 0), rect=K.light_rect(colour), caps=True)


def cannon_axes(fr, pitch_deg, r):
    """The barrel's axis and up in ship space for a mount frame ``fr`` (z out of the hull, y along the ship), with the barrel raised ``pitch_deg`` off the hull."""
    p = math.radians(pitch_deg)
    ax = norm(fr.d(0.0, math.cos(p), math.sin(p)))
    up = norm(fr.d(0.0, -math.sin(p), math.cos(p)))
    return ax, up


def beam_cannon(K, sb, fr, key, length=58.0, r=8.0, pitch=30.0):
    """A heavy beam cannon on a barbette. ``fr``: origin on the hull under the mount, z = out of the surface, y = the way the barrel points at rest (along the hull);
    the barrel is raised ``pitch`` degrees. ``r`` is the bore's radius (the barbette is 3 r across). The subsystem frame is at the muzzle."""
    R = r * 3.1
    ax, up = cannon_axes(fr, pitch, r)
    pivot = fr.p(0.0, 0.0, R * 0.95)                                       # where the barrel swings
    bf = Frame(pivot, ax, up)
    if sb.lod == 2:
        sb.part().box(fr, (R * 2.2, R * 2.2, R * 1.2), PLATE, centre=(0, 0, R * 0.6), chamfer=R * 0.3)
        sb.part().box(bf, (r * 3.4, length, r * 3.4), PLATE_B, centre=(0, length * 0.5, 0), chamfer=r * 0.6)
        return
    if sb.lod == 1:
        return
    rng = K.rng("cannon", key)
    base, body, barrel, lens = sb.part("base"), sb.part("body"), sb.part("barrel"), sb.part("lens")
    # ---- the seat and the barbette: a plated pad, a chrome-lipped drum
    base.box(fr, (R * 2.5, R * 2.5, R * 0.18), PLATE_B, centre=(0, 0, R * 0.09), chamfer=R * 0.5, tile=8.0)
    drum = Frame(fr.p(0, 0, R * 0.18), fr.u, fr.f)
    base.lathe([(R * 1.08, 0.0), (R * 1.08, R * 0.34), (R * 0.94, R * 0.5), (R * 0.82, R * 0.5)], drum, PLATE, segs=40, tile=6.0)
    base.lathe([(R * 1.1, R * 0.30), (R * 1.1, R * 0.40), (R * 0.97, R * 0.47)], drum, CHROME, segs=40, tile=3.0)
    for k in range(10):                                                     # bolt blocks round the drum, different sizes
        a = 2 * math.pi * k / 10 + rng.uniform(-0.12, 0.12)
        q = fr.p(math.cos(a) * R * 1.2, math.sin(a) * R * 1.2, R * 0.18)
        base.box(Frame(q, norm(fr.d(math.cos(a), math.sin(a), 0)), fr.u), (rng.uniform(1.2, 2.6), rng.uniform(1.6, 3.0), rng.uniform(1.0, 2.2)), PLATE_C if k % 3 else CHROME, centre=(0, 0, 0.7), chamfer=0.2, tile=4.0)
    # ---- the turret body: a chamfered block along the barrel with side radiators, a sensor blister and hazard marks
    prof = chamfered_rect(R * 0.78, R * 0.62, R * 0.26)
    body.extrude(prof, bf, -R * 1.0, R * 0.95, PLATE, tile=6.0, cap_mat=PLATE)
    body.extrude(prof, bf, R * 0.95, R * 1.35, PLATE_B, s0=(1.0, 1.0), s1=(0.62, 0.62), tile=6.0, caps=(False, True), cap_mat=PLATE)
    body.lathe([(r * 1.3, R * 1.3), (r * 1.7, R * 1.38), (r * 1.7, R * 1.55), (r * 1.3, R * 1.65)], bf, CHROME, segs=28, tile=3.0)      # the collar round the barrel's root
    for sx in (-1, 1):
        for k in range(3):
            body.box(bf, (0.5, R * (1.5 - 0.12 * k), R * 0.34), PLATE_C, centre=(sx * (R * 0.8 + 0.3 + 0.55 * k), -R * 0.05, -R * 0.25 + R * 0.28 * k), chamfer=0.1, tile=4.0)
        if K.lod == 0:
            K.tile_piece("radiator", bf.p(sx * (R * 0.82 + 0.35), -R * 0.8, -R * 0.5), bf.f, bf.u, R * 1.4, R * 0.9, mb=body, scale=1.0) if sx > 0 else \
                K.tile_piece("radiator", bf.p(sx * (R * 0.82 + 0.35), -R * 0.8, -R * 0.5), bf.u, bf.f, R * 0.9, R * 1.4, mb=body, scale=1.0)
    K.dome(Frame(bf.p(-R * 0.22, -R * 0.2, R * 0.66), bf.u, bf.f), r * 0.9, mb=body)
    K.paint_quad("chevron_0", bf.p(R * 0.2, -R * 0.1, R * 0.63), bf.f, bf.u, size=(R * 1.1, R * 0.7), lift=0.02, mb=body)
    for sx in (-1, 1):
        _lamp(K, body, bf.p(sx * R * 0.7, R * 0.85, R * 0.52), bf.u, LIGHT_RED, 1.6)
    # ---- the barrel: a cage. A dark bore, rings of chrome coil with red bands lit between them, four rails, then the flared muzzle with its lens
    L = length
    bore0, bore1 = R * 1.3, L - 6.0
    K.pipe(bf.p(0, bore0, 0), bf.p(0, bore1, 0), r * 0.62, mb=barrel, sides=12, mat=PLATE)
    ring_y = np.linspace(R * 1.55, L - 9.0, 6)
    for i, y in enumerate(ring_y):
        rr = r * (1.85 + 0.12 * i)
        w = 1.1 + 0.1 * i
        barrel.lathe([(rr, y - w), (rr + 0.45, y - w * 0.5), (rr + 0.45, y + w * 0.5), (rr, y + w)], bf, CHROME, segs=28, tile=3.0)
        barrel.lathe([(rr - 0.3, y - w), (rr - 0.3, y + w), (rr * 0.7, y + w), (rr * 0.7, y - w)], bf, PLATE_B, segs=28, tile=3.0)
        if i < len(ring_y) - 1:
            yb = 0.5 * (y + ring_y[i + 1])
            barrel.lathe([(r * 0.95, yb - 0.8), (r * 0.95, yb + 0.8)], bf, LIGHTS, segs=20, rect=K.light_rect(LIGHT_RED))
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        K.pipe(bf.p(math.cos(a) * r * 1.5, bore0 + 2.0, math.sin(a) * r * 1.5), bf.p(math.cos(a) * r * 1.7, L - 6.0, math.sin(a) * r * 1.7), 0.5, mb=barrel, sides=5)
    for k in range(3):                                                      # cable runs down the side
        a = 0.6 + k * 2.0
        K.pipe(bf.p(math.cos(a) * r * 2.0, R * 1.2, math.sin(a) * r * 2.0), bf.p(math.cos(a) * r * 1.4, R * 1.2 + 14.0, math.sin(a) * r * 1.4), 0.3, mb=barrel, sides=4)
    # the muzzle: a flared chrome bell, dark inside, with a red lens that the charge glow gathers on
    my = L - 6.0
    barrel.lathe([(r * 0.9, my), (r * 1.6, my + 1.5), (r * 2.4, my + 5.0), (r * 2.55, my + 6.0)], bf, CHROME, segs=32, tile=3.0)
    lens.lathe([(r * 0.7, my + 0.2), (r * 1.5, my + 2.0), (r * 2.3, my + 5.5)], bf, TRIM, segs=32, rect=K.rect("chrome_brushed"), flip=True)
    lens.disc(bf, r * 1.0, my + 0.6, LIGHTS, segs=24, facing=1.0, rect=K.light_rect(LIGHT_RED))
    for k in range(8):
        a = 2 * math.pi * k / 8
        _lamp(K, lens, bf.p(math.cos(a) * r * 2.6, my + 6.2, math.sin(a) * r * 2.6), bf.f, LIGHT_RED, 1.0)
    # ---- wreck: the barrel snapped and hanging, the body torn open, the drum scorched
    d = sb.variant("destroyed")
    burnt = K.rect("burnt")
    d.box(fr, (R * 2.5, R * 2.5, R * 0.18), PLATE_B, centre=(0, 0, R * 0.09), chamfer=R * 0.5, tile=8.0)
    d.lathe([(R * 1.08, 0.0), (R * 1.08, R * 0.34), (R * 0.94, R * 0.5)], drum, TRIM, segs=28, rect=burnt)
    d.extrude(prof, bf, -R * 1.0, R * 0.2, TRIM, s1=(0.92, 0.84), rect=burnt, caps=(True, False), cap_rect=burnt, cap_mat=TRIM)
    K.pipe(bf.p(0, R * 0.2, 0), bf.p(r * 1.2, R * 0.2 + L * 0.28, -r * 1.2), r * 0.7, mb=d, rect_name="burnt", sides=8)
    for i in range(2):
        yy = R * 0.5 + i * 7.0
        d.lathe([(r * 1.9, yy - 1.0), (r * 1.9, yy + 1.0), (r * 1.4, yy + 1.0), (r * 1.4, yy - 1.0)], bf, TRIM, segs=18, rect=burnt, arc=(20, 260))
    mnparts.wreck_stump(K, d, Frame(fr.p(0, 0, R * 0.5), fr.f, fr.u), R * 0.9, R * 0.8, key, plates=8, embers=7)
    for k in range(4):
        mnparts.torn_plate(K, d, fr.p(rng.uniform(-R, R), rng.uniform(-R, R), R * rng.uniform(0.6, 1.2)), fr.d(rng.normal(0, 0.5), rng.normal(0, 0.5), 1.0), R * 0.55, rng, thickness=0.4)
    mf = sb.frame
    mnparts.debris_set(K, sb, mf, key, [
        {"kind": "tube", "size": (r * 1.4, L * 0.35, 0), "at": loc(mf, bf.p(0, L * 0.55, 0)), "speed": 9},
        {"kind": "tube", "size": (r * 1.0, L * 0.2, 0), "at": loc(mf, bf.p(0.5, L * 0.85, 0)), "speed": 13},
        {"kind": "chunk", "size": (R * 0.9, R * 1.0, R * 0.7), "at": loc(mf, bf.p(0, R * 0.4, R * 0.3)), "speed": 6},
        {"kind": "plate", "size": (R * 0.9, R * 0.9, 0.5), "at": loc(mf, bf.p(R * 0.8, 0, 0)), "speed": 8},
        {"kind": "plate", "size": (R * 0.9, R * 0.9, 0.5), "at": loc(mf, bf.p(-R * 0.8, 0, 0)), "speed": 9},
        {"kind": "chunk", "size": (r * 2.4, r * 2.0, r * 2.4), "at": loc(mf, bf.p(0, L - 4.0, 0)), "speed": 12}])
    return bf, my


def muzzle_frame(fr, length, r, pitch):
    """The frame of a cannon's muzzle (the part's pivot), given the mount frame: origin at the lens, y along the barrel."""
    R = r * 3.1
    ax, up = cannon_axes(fr, pitch, r)
    bf = Frame(fr.p(0.0, 0.0, R * 0.95), ax, up)
    return Frame(bf.p(0.0, length - 5.4, 0.0), ax, up)


def chin_hangar(K, sb, fr, key, w=62.0, l=118.0, h=24.0):
    """The hangar pod: a plated box hanging off the hull with its far face open (the bombers launch out of it), lit ceiling and strips, a chrome lip with hazard marks,
    two door slabs hung open on rams. ``fr``: origin at the opening's centre on the hull surface, z = out of the hull (away from it: the pod's depth), y along the ship.
    The wreck: the walls torn, a door hanging, rubble and embers in the bay."""
    if sb.lod == 2:
        sb.part().box(fr, (w + 8.0, l + 6.0, h), PLATE, centre=(0, 0, h / 2), chamfer=2.0)
        return
    if sb.lod == 1:
        return
    rng = K.rng("chin", key)
    frame_p, inner, door = sb.part("frame"), sb.part("interior"), sb.part("doors")
    t = 2.4
    # the walls: two long sides, the two ends, plated on their outer faces
    walls = [("l", (-w / 2 - t / 2, 0.0), (t, l + 2 * t, h)), ("r", (w / 2 + t / 2, 0.0), (t, l + 2 * t, h)), ("f", (0.0, l / 2 + t / 2), (w, t, h)), ("a", (0.0, -l / 2 - t / 2), (w, t, h))]
    for nm, (cx, cy), size in walls:
        frame_p.box(fr, size, PLATE_B, centre=(cx, cy, h / 2), chamfer=0.4, tile=8.0)
    for sx in (-1, 1):
        p0 = fr.p(sx * (w / 2 + t + 0.05), -l / 2 + 6.0, 4.0)
        run = norm(fr.d(0, 1, 0))
        n = fr.r * sx
        K.strip(p0, run, n, l - 12.0, "windows_a", lift=0.0, mb=frame_p)
        K.strip(fr.p(sx * (w / 2 + t + 0.05), -l / 2 + 6.0, h - 6.0), run, n, l - 12.0, "conduit", lift=0.0, scale=1.2, mb=frame_p)
        # a row of plates standing proud on the outer face
        patch_pts = [fr.p(sx * (w / 2 + t + 0.02), -l / 2 + 2.0, 8.0), fr.p(sx * (w / 2 + t + 0.02), l / 2 - 2.0, 8.0), fr.p(sx * (w / 2 + t + 0.02), l / 2 - 2.0, h - 8.0), fr.p(sx * (w / 2 + t + 0.02), -l / 2 + 2.0, h - 8.0)]
        from .mnmesh import Patch
        pa = Patch(*(patch_pts if sx > 0 else [patch_pts[1], patch_pts[0], patch_pts[3], patch_pts[2]]))
        if np.dot(pa.normal(), n) < 0:
            pa = Patch(pa.p10, pa.p00, pa.p01, pa.p11)
        K.plate_patch(pa, f"chinside{key}{sx}", rows=(4.0, 9.0), cols=(3.0, 6.0), height=(0.2, 0.45), margin=0.4, mb=frame_p)
    # the lip: chrome round the opening, a hazard band, beacons at the corners
    for nm, size, c in (("l", (1.4, l + 2 * t, 1.6), (-w / 2 - t * 0.2, 0, h - 0.4)), ("r", (1.4, l + 2 * t, 1.6), (w / 2 + t * 0.2, 0, h - 0.4)),
                        ("f", (w, 1.4, 1.6), (0, l / 2 + t * 0.2, h - 0.4)), ("a", (w, 1.4, 1.6), (0, -l / 2 - t * 0.2, h - 0.4))):
        frame_p.box(fr, size, CHROME, centre=c, tile=3.0, chamfer=0.3)
    for sx in (-1, 1):
        for sy in (-1, 1):
            _lamp(K, frame_p, fr.p(sx * (w / 2 + t), sy * (l / 2 + t), h + 0.2), fr.u, LIGHT_AMBER, 2.2)
    K.paint_quad("chevron_1", fr.p(0, l / 2 + t + 0.02, h * 0.5), fr.r, fr.f, size=(w * 0.8, 7.0), lift=0.0, mb=frame_p)
    # the bay: a lit ceiling (against the hull), light bars, ribs, launch rails
    ceil = fr.p(0, 0, 0.25)
    n_ = fr.u                                                                # the ceiling faces the opening: away from the hull
    u_ = norm(np.cross(fr.f, n_))
    v_ = np.cross(n_, u_)
    cw, cl = w - 2.0, l - 2.0
    q = [ceil - u_ * cw / 2 - v_ * cl / 2, ceil + u_ * cw / 2 - v_ * cl / 2, ceil + u_ * cw / 2 + v_ * cl / 2, ceil - u_ * cw / 2 + v_ * cl / 2]
    inner.quad_rect(q[0], q[1], q[2], q[3], K.rect("underlayer"), TRIM)      # the dark ceiling, then lit strips across it
    for xo in (-0.62, -0.31, 0.0, 0.31, 0.62):
        for yo, ln in ((-0.30, l * 0.34), (0.22, l * 0.40)):
            c0 = ceil + u_ * (xo * cw * 0.5) + v_ * (yo * cl)
            sw = 4.4
            pp = [c0 - u_ * sw / 2 - v_ * ln / 2 + n_ * 0.1, c0 + u_ * sw / 2 - v_ * ln / 2 + n_ * 0.1, c0 + u_ * sw / 2 + v_ * ln / 2 + n_ * 0.1, c0 - u_ * sw / 2 + v_ * ln / 2 + n_ * 0.1]
            inner.quad_rect(pp[0], pp[1], pp[2], pp[3], K.light_rect(LIGHT_AMBER), LIGHTS)
    for k in range(7):
        y = -l / 2 + 6.0 + k * (l - 12.0) / 6 + rng.uniform(-1.0, 1.0)
        inner.box(fr, (w - 1.0, 1.6, 1.4), PLATE_C, centre=(0, y, 1.2), chamfer=0.25, tile=5.0)
        for sx in (-1, 1):
            inner.box(fr, (1.4, 1.6, h - 3.0), PLATE_C, centre=(sx * (w / 2 - 0.9), y, h / 2 + 1.0), chamfer=0.2, tile=5.0)
    for sx in (-0.55, 0.0, 0.55):
        K.pipe(fr.p(sx * w * 0.5, -l / 2 + 4.0, 3.2), fr.p(sx * w * 0.5, l / 2 - 4.0, 3.2), 0.6, mb=inner, sides=6)
    for sx in (-1, 1):
        K.strip(fr.p(sx * (w / 2 - 0.05), -l / 2 + 5.0, h * 0.55), fr.f, -fr.r * sx, l - 10.0, "windows_big", lift=0.0, mb=inner)
    # the doors: two slabs hung open at an angle from the pod's long walls, on rams
    ang = math.radians(48.0)
    for sx in (-1, 1):
        hinge = fr.p(sx * (w / 2 + t + 1.0), 0.0, h - 1.0)
        along = fr.r * sx * math.cos(ang) + fr.u * math.sin(ang)             # the way the slab runs from its hinge
        nrm = -fr.r * sx * math.sin(ang) + fr.u * math.cos(ang)              # its face
        dw = w * 0.46
        door.box(Frame(hinge + along * dw / 2, fr.f, nrm), (dw, l * 0.9, 1.6), PLATE_B, chamfer=0.35, tile=8.0)
        for sy in (-0.3, 0.3):
            K.pipe(fr.p(sx * (w / 2 + t + 0.5), l * sy, h * 0.5), hinge + along * dw * 0.55 + fr.f * (l * sy), 0.9, mb=door, sides=6)
    # ---- wreck: the far end and one wall torn, rubble in the bay, one door hanging, embers
    d = sb.variant("destroyed")
    burnt = K.rect("burnt")
    d.box(fr, walls[0][2], PLATE_B, centre=(walls[0][1][0], walls[0][1][1], h / 2), chamfer=0.4, tile=8.0)
    d.box(fr, (w, t, h * 0.6), PLATE_B, centre=(0, -l / 2 - t / 2, h * 0.3), chamfer=0.4, tile=8.0)
    d.box(fr, (t, l * 0.4, h * 0.5), TRIM, centre=(w / 2 + t / 2, -l * 0.28, h * 0.25), rect=burnt)
    for k in range(12):
        p = fr.p(rng.uniform(-w / 2, w / 2), rng.uniform(-l / 2, l / 2), rng.uniform(1.0, 5.0))
        mnparts.chunk(K, d, p, (rng.uniform(2.0, 7.0), rng.uniform(2.0, 7.0), rng.uniform(1.5, 4.0)), rng)
    mnparts.wreck_stump(K, d, Frame(fr.p(0, 0, 1.5), fr.f, fr.u), min(w, l) * 0.45, h * 0.6, key, plates=9, embers=9)
    hinge = fr.p(w / 2 + t + 1.0, 0.0, h - 1.0)
    d.box(Frame(hinge + fr.r * 3.0 - fr.u * (w * 0.2), fr.f, norm(fr.r * 0.2 + fr.u * 0.98)), (w * 0.46, l * 0.7, 1.6), TRIM, chamfer=0.35, rect=burnt)
    sf = sb.frame
    mnparts.debris_set(K, sb, sf, key, [
        {"kind": "plate", "size": (w * 0.5, l * 0.3, 0.7), "at": loc(sf, fr.p(0, l * 0.1, h * 0.5)), "speed": 8}, {"kind": "chunk", "size": (w * 0.3, l * 0.2, h * 0.5), "at": loc(sf, fr.p(w * 0.2, -l * 0.25, h * 0.4)), "speed": 6},
        {"kind": "plate", "size": (w * 0.4, l * 0.25, 0.7), "at": loc(sf, fr.p(-w * 0.4, 0.0, h)), "speed": 10}, {"kind": "chunk", "size": (w * 0.2, l * 0.15, h * 0.4), "at": loc(sf, fr.p(0, l * 0.35, h * 0.4)), "speed": 11},
        {"kind": "tube", "size": (1.4, l * 0.3, 0), "at": loc(sf, fr.p(0, 0, h * 0.8)), "speed": 7}])
