"""mndiaspora_cap: the diaspora's capital-ship parts (art_src/diaspora/build_diaspora_cruiser.py), on the kit in mndiaspora.

Every builder follows the pipeline contract (art_src/PIPELINE.md): it draws in ship space through a frame, writes the intact look into ``sb.part(name)``, the wreck into
``sb.variant("destroyed")`` and the detachable chunks through ``mnparts.debris_set``; at LOD2 it adds one box to the hull, at LOD1 nothing.
"""
import math

import numpy as np

from . import mnparts
from .mnkit import PLATE, TRIM, LIGHTS, ENGINE, CHROME, PLATE_B, PLATE_C, PLUME, PLATE_D, PLATE_E, WEAR, PAINT
from .mnmesh import Frame, norm, cross
from .mndiaspora import (BIO, GLYPH, GLASS, STEEL, LIGHT_TEAL, sweep, arc_path, ribbed_shell, bio_hoop)


def bio_fin(mb, poly_yz, x, thickness, mat=BIO, tile=2.0):
    """A dark, thick fin standing in the y-z plane at ``x`` (the graft's crest and gills): the polygon (y, z) extruded along x in the BIO material."""
    fr = Frame((x, 0, 0), (1, 0, 0), (0, 0, 1))
    prof = [(-y, z) for y, z in poly_yz]
    mb.extrude(prof, fr, -thickness / 2, thickness / 2, mat, caps=(True, True), cap_mat=mat, tile=tile)


def gun_turret(K, sb, fr, key, s=1.0, barrels=2, barrel_len=4.6, barrel_r=0.26):
    """Flak battery or anti-fighter gun, the diaspora's: the capital kit's turret (a seated ring, a turntable body, steel barrels) with a ring of patched plates round the
    seat. Wreck and debris as the kit's."""
    mnparts.gun_turret(K, sb, fr, key, s=s, barrels=barrels, barrel_len=barrel_len, barrel_r=barrel_r)
    if sb.lod == 0:
        base = sb.part("base")
        for k in range(8):
            a = 2 * math.pi * k / 8
            p = fr.p(math.sin(a) * 2.45 * s, math.cos(a) * 2.45 * s, 0.25 * s)
            base.box(Frame(p, fr.d(math.sin(a), math.cos(a), 0.0), fr.u), (0.5 * s, 0.35 * s, 0.5 * s), PLATE_D if k % 2 else PLATE_B, tile=2.0, chamfer=0.05 * s)


def beam_lance(K, sb, fr, key, length=22.0, r=2.4):
    """The alien-ally beam emplacement: a human gimbal (a plated seat, a steel pivot, two cheek plates) carrying a long dark ribbed barrel with teal glyph bands and a
    glowing lens, coolant pipes strapped along it. ``fr``: origin on the hull, z = out of the surface, y = the way it points at rest. Wreck: the gimbal torn, the
    barrel snapped to a ribbed stump; debris: barrel sections, cheek plates."""
    if sb.lod == 2:
        sb.part().box(fr, (r * 4.4, length + 4.0, r * 4.2), PLATE, centre=(0, length * 0.4, r * 1.4), chamfer=r * 0.4)
        return
    if sb.lod == 1:
        return
    base, gun = sb.part("base"), sb.part("barrel")
    zc = r * 1.6
    base.box(fr, (r * 4.8, r * 4.8, 1.0), PLATE, centre=(0, 0, 0.5), chamfer=r * 0.9, tile=6.0)
    base.lathe([(r * 2.5, 0.0), (r * 2.5, 0.5), (r * 2.15, 1.0), (r * 1.7, 1.0)], Frame(fr.p(0, 0, 1.0), fr.u, fr.f), STEEL, segs=24, tile=3.0)
    for sx in (-1, 1):
        base.box(fr, (0.9, r * 3.0, r * 2.6), PLATE_D if sx > 0 else PLATE, centre=(sx * r * 1.55, -0.2, zc), chamfer=0.25, tile=4.0)
        base.lathe([(r * 0.62, 0.0), (r * 0.62, 0.9), (r * 0.8, 1.05)], Frame(fr.p(sx * r * 1.9, 0, zc), fr.r * sx, fr.f), STEEL, segs=14, tile=2.0)
    base.box(fr, (r * 2.2, r * 1.2, r * 0.9), PLATE_C, centre=(0, -r * 1.6, zc - 0.2), chamfer=0.2, tile=4.0)
    p0, p1 = fr.p(0, -r * 0.6, zc), fr.p(0, length, zc)
    ribbed_shell(gun, p0, p1, r * 0.95, r * 0.55, 10, 0.2, segs=14, key=key)
    gun.lathe([(r * 0.58, length), (r * 0.7, length + 0.1), (r * 0.7, length + 0.7), (r * 0.5, length + 0.8)], Frame(fr.p(0, 0, zc), fr.f, fr.u), STEEL, segs=14, tile=2.0)
    gun.disc(Frame(fr.p(0, 0, zc), fr.f, fr.u), r * 0.42, length + 0.75, LIGHTS, segs=14, facing=1.0, rect=K.light_rect(LIGHT_TEAL))
    if K.lod == 0 and "glyph_a" in K.trims:
        K.strip(fr.p(-0.55, 1.0, zc + r * 0.9), fr.f, fr.u, length * 0.82, "glyph_a", lift=0.0, scale=0.9, mb=gun)
    for sx in (-1, 1):
        K.pipe(fr.p(sx * r * 0.85, 0.2, zc + r * 0.55), fr.p(sx * r * 0.62, length * 0.88, zc + r * 0.4), 0.2, sides=6, mb=gun)
    d = sb.variant("destroyed")
    d.box(fr, (r * 4.8, r * 4.8, 0.8), TRIM, centre=(0, 0, 0.4), chamfer=r * 0.9, rect=K.rect("burnt"))
    mnparts.wreck_stump(K, d, fr, r * 2.0, r * 1.8, key, plates=6, embers=4)
    ribbed_shell(d, fr.p(0.6, 0.0, zc + 0.4), fr.p(r * 1.4, length * 0.34, zc + r * 1.2), r * 0.95, r * 0.8, 3, 0.2, segs=10, key=key + "w")
    mnparts.debris_set(K, sb, fr, key, [
        {"kind": "tube", "size": (r * 1.4, length * 0.42, 0), "at": (0, length * 0.62, zc), "speed": 9},
        {"kind": "tube", "size": (r * 0.8, length * 0.25, 0), "at": (0.5, length * 0.9, zc), "speed": 13},
        {"kind": "chunk", "size": (r * 2.4, r * 2.6, r * 1.6), "at": (0, 0, zc), "speed": 6},
        {"kind": "plate", "size": (r * 2.0, r * 2.0, 0.3), "at": (-r * 1.5, 0, zc), "speed": 8},
        {"kind": "plate", "size": (r * 2.0, r * 2.0, 0.3), "at": (r * 1.5, 0, zc), "speed": 8},
    ])


BAY_PLATING = dict(rows=(2.2, 6.5), cols=(1.8, 5.5), gap=0.1, height=(0.08, 0.26), chamfer=0.1, margin=0.2, min_size=0.5, sub_prob=0.3, sub=(0.8, 2.0), sub_height=(0.04, 0.1))


def hangar_bay(K, sb, fr, key, w=24.0, h=13.0, depth=12.0, door=True):
    """A hangar bay on a flank: a big open box standing out from the hull (plated top, bottom and two end walls, the hull as its back), lit warm inside along the ceiling,
    a worked deck, hazard stripes on the lip, a blast door hanging open above. ``fr``: origin at the opening's centre on the hull surface, z = out of the hull, y = along the
    ship, x = across (vertical, sign either way). Wreck: the walls torn, the interior scorched and burning; debris: the door and wall plates."""
    if sb.lod == 2:
        sb.part().box(fr, (h + 3.2, w + 3.2, depth), PLATE, centre=(0, 0, depth / 2), chamfer=0.8)
        return
    if sb.lod == 1:
        return
    from .mnfighter import solid
    t = 1.6
    up = 1.0 if fr.r[2] > 0 else -1.0                      # the sign of local x that points up in the ship
    frame_part, inner, doorp = sb.part("frame"), sb.part("interior"), sb.part("door")

    def wall(q, thick, tag, out):
        solid(K, frame_part, np.array(q, float), thick, f"{key}-{tag}", bevel=0.18, plating=BAY_PLATING, normal=out, edge_r=0.1, lod2=False)
    xs, zs = h / 2 + t / 2, depth
    for sx, tag in ((1, "x+"), (-1, "x-")):
        q = [fr.p(sx * xs, -w / 2 - t, 0), fr.p(sx * xs, w / 2 + t, 0), fr.p(sx * xs, w / 2 + t, zs), fr.p(sx * xs, -w / 2 - t, zs)]
        wall(q, t, tag, fr.r * sx)
    for sy, tag in ((1, "y+"), (-1, "y-")):
        q = [fr.p(-h / 2, sy * (w / 2 + t / 2), 0), fr.p(h / 2, sy * (w / 2 + t / 2), 0), fr.p(h / 2, sy * (w / 2 + t / 2), zs), fr.p(-h / 2, sy * (w / 2 + t / 2), zs)]
        wall(q, t, tag, fr.f * sy)
    lip = 0.6
    for sx in (-1, 1):
        frame_part.box(fr, (lip, w + 2 * t + 1.0, lip), STEEL, centre=(sx * (h / 2 + t + lip / 2 - 0.3), 0, depth + lip / 2 - 0.1), tile=3.0, chamfer=0.1)
    for sy in (-1, 1):
        frame_part.box(fr, (h + 2 * t, lip, lip), STEEL, centre=(0, sy * (w / 2 + t + lip / 2 - 0.3), depth + lip / 2 - 0.1), tile=3.0, chamfer=0.1)
    o = fr.p(-h / 2, -w / 2, 0.1)
    xdir, ydir = fr.f, fr.r
    n = cross(norm(xdir), norm(ydir))
    if np.dot(n, fr.u) < 0:
        o = fr.p(-h / 2, w / 2, 0.1)
        xdir = -fr.f
    if "deck" in K.trims:
        K.tile_piece("deck", o, xdir, ydir, w, h, mb=inner, lift=0.0, scale=1.0)
    if "light_bar" in K.trims:
        for fx in (-0.3, 0.05, 0.38):
            K.strip(fr.p(h * fx, -w * 0.4, 0.2), fr.f, fr.u, w * 0.8, "light_bar", lift=0.0, scale=2.0, mb=inner)
    if "deck" in K.trims:
        floor = -up * (h / 2 - 0.05)                                       # the floor is the low side: local x = -up * h / 2
        o = fr.p(floor, -w / 2, 0.0)
        xdir, ydir = fr.f, fr.u
        n = cross(norm(xdir), norm(ydir))
        if np.dot(n, fr.r * up) < 0:
            o = fr.p(floor, w / 2, 0.0)
            xdir = -fr.f
        K.tile_piece("deck", o, xdir, ydir, w, depth, mb=inner, lift=0.0, scale=1.0)
    if "light_bar" in K.trims:
        for sy in (-0.25, 0.25):
            K.strip(fr.p(up * (h / 2 - 0.1), w * sy - 4.0, 0.5), fr.f, -fr.r * up, 8.0, "light_bar", lift=0.0, scale=3.0, mb=inner)
    if "hazard" in K.trims:
        K.strip(fr.p(-up * (h / 2 + t + 0.02), -w / 2 - t, depth + 0.3), fr.f, fr.r * up, w + 2 * t, "hazard", lift=0.0, scale=1.8, mb=frame_part)
    if door:
        hx, dh = up * (h / 2 + t), h * 0.55
        q = [fr.p(hx, -w * 0.46, depth + 0.3), fr.p(hx, w * 0.46, depth + 0.3), fr.p(hx + up * dh * 0.62, w * 0.46, depth + 0.3 + dh * 0.9), fr.p(hx + up * dh * 0.62, -w * 0.46, depth + 0.3 + dh * 0.9)]
        solid(K, doorp, np.array(q, float), 0.8, f"{key}-door", bevel=0.12, plating=BAY_PLATING, normal=fr.r * up + fr.u * 0.3, edge_r=0.08, lod2=False)
    d = sb.variant("destroyed")
    burnt = K.rect("burnt")
    for sx in (-1, 1):
        d.box(fr, (t, w * 0.55, depth * 0.55), TRIM, centre=(sx * (h / 2 + t / 2), sx * -w * 0.2, depth * 0.27), chamfer=0.25, rect=burnt)
    d.box(fr, (h, t, depth * 0.45), TRIM, centre=(0, w / 2 + t / 2, depth * 0.22), chamfer=0.25, rect=burnt)
    d.box(fr, (h * 0.9, w * 0.9, 0.4), TRIM, centre=(0, 0, 0.3), chamfer=0.2, rect=burnt)
    mnparts.wreck_stump(K, d, fr.moved(0, 0, 0.4), min(h, w) * 0.45, h * 0.5, key, plates=7, embers=7)
    mnparts.debris_set(K, sb, fr, key, [
        {"kind": "plate", "size": (w * 0.5, h * 0.5, 0.5), "at": (up * h * 0.6, 0, depth + 2.0), "speed": 8},
        {"kind": "plate", "size": (w * 0.3, h * 0.4, 0.5), "at": (-up * h * 0.5, w * 0.2, depth), "speed": 10},
        {"kind": "chunk", "size": (t * 2, w * 0.3, depth * 0.6), "at": (h * 0.6, -w * 0.25, depth * 0.5), "speed": 7},
        {"kind": "chunk", "size": (h * 0.5, t * 2, depth * 0.5), "at": (0, w * 0.55, depth * 0.4), "speed": 9},
    ])


def reactor_heart(K, sb, fr, key, r=8.5):
    """The reactor: a living heart. A cage of dark ribs round a glowing teal core on a plated seat, a coolant stack of steel either side, pipes. ``fr``: origin on the
    hull, z = out of the surface. Wreck: the cage burst, the core dark, embers; debris: ribs and a stack."""
    if sb.lod == 2:
        sb.part().box(fr, (r * 3.4, r * 2.4, r * 1.9), PLATE, centre=(0, 0, r * 0.9), chamfer=r * 0.4)
        return
    if sb.lod == 1:
        return
    base, cage, core = sb.part("base"), sb.part("cage"), sb.part("core")
    base.box(fr, (r * 3.0, r * 2.6, 1.2), PLATE, centre=(0, 0, 0.6), chamfer=r * 0.35, tile=6.0)
    base.lathe([(r * 1.25, 1.2), (r * 1.25, 1.9), (r * 1.1, 2.3)], Frame(fr.p(0, 0, 0.0), fr.u, fr.f), STEEL, segs=24, tile=3.0)
    ax = Frame(fr.p(0, 0, 2.2), fr.u, fr.f)
    core.lathe([(0.0, 0.0), (r * 0.42, 0.0), (r * 0.46, r * 0.4), (r * 0.46, r * 1.55), (r * 0.36, r * 1.85), (0.0, r * 2.0)], ax, LIGHTS, segs=14, rect=K.light_rect(LIGHT_TEAL))
    for rad, z in ((1.12, 0.35), (1.0, 0.95), (0.8, 1.45), (0.5, 1.82)):
        bio_hoop(cage, fr.p(0, 0, 2.2 + r * z), fr.u, r * rad, 0.55, 0.45, segs=22)
    for k in range(8):
        a = 2 * math.pi * k / 8 + 0.2
        pts = []
        for rad, z in ((1.12, 0.0), (1.12, 0.35), (1.0, 0.95), (0.8, 1.45), (0.5, 1.82), (0.1, 2.02)):
            pts.append(fr.p(math.cos(a) * r * rad, math.sin(a) * r * rad, 2.2 + r * z))
        sweep(cage, np.array(pts), 0.34, BIO, sides=6)
    for sx in (-1, 1):
        stack = fr.p(sx * r * 1.9, 0.0, 0.0)
        K.pipe(stack + fr.u * 1.2, stack + fr.u * (r * 1.3), r * 0.28, sides=10, mb=base)
        base.box(fr, (r * 0.75, r * 0.75, 0.7), STEEL, centre=(sx * r * 1.9, 0, r * 1.35), tile=2.0, chamfer=0.15)
        for zz in (0.35, 0.8):
            bio_hoop(base, stack + fr.u * (r * zz), fr.u, r * 0.3, 0.4, 0.12, segs=10, mat=PLATE_D)
        sweep(base, arc_path(stack + fr.u * (r * 1.2), fr.p(sx * r * 0.4, 0, 2.2 + r * 1.0), r * 0.35, n=6, up=fr.u), 0.22, STEEL, sides=6)
    d = sb.variant("destroyed")
    d.box(fr, (r * 3.0, r * 2.6, 0.9), TRIM, centre=(0, 0, 0.45), chamfer=r * 0.35, rect=K.rect("burnt"))
    mnparts.wreck_stump(K, d, fr, r * 1.3, r * 1.2, key, plates=7, embers=6)
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.5
        sweep(d, np.array([fr.p(math.cos(a) * r * 1.1, math.sin(a) * r * 1.1, 2.3), fr.p(math.cos(a) * r * 1.3, math.sin(a) * r * 1.3, 2.3 + r * 0.7),
                           fr.p(math.cos(a) * r * 1.5, math.sin(a) * r * 1.6, 2.3 + r * 0.9)]), 0.3, BIO, sides=5)
    mnparts.debris_set(K, sb, fr, key, [
        {"kind": "chunk", "size": (r * 0.8, r * 0.8, r * 0.8), "at": (0, 0, 2.2 + r), "speed": 9},
        {"kind": "tube", "size": (r * 0.3, r * 1.4, 0), "at": (r * 1.0, 0, 2.2 + r * 0.8), "speed": 8},
        {"kind": "tube", "size": (r * 0.3, r * 1.2, 0), "at": (-r * 0.9, 0.2, 2.2 + r * 0.7), "speed": 11},
        {"kind": "plate", "size": (r * 1.2, r * 1.2, 0.3), "at": (0, -r * 0.9, 1.5), "speed": 6},
    ])


def bridge_pod(K, sb, fr, key, w=15.0, l=22.0, h=6.5):
    """The bridge: a low plated block with a long lit window band round its front and flanks, a command blister behind, an arch of grown ribs over it with a glyph band
    along the ridge, aerials. ``fr``: origin on the hull, y = forward, z = out. Wreck: the block caved in, the ribs bent; debris: ribs, the blister, plates."""
    if sb.lod == 2:
        sb.part().box(fr, (w, l, h + 3.0), PLATE, centre=(0, 0, (h + 3.0) / 2), chamfer=1.2)
        return
    if sb.lod == 1:
        return
    base, rib, glass = sb.part("base"), sb.part("ribs"), sb.part("windows")
    base.box(fr, (w, l, h), PLATE, centre=(0, 0, h / 2), chamfer=1.4, tile=8.0)
    base.box(fr, (w * 0.74, l * 0.5, h * 0.42), PLATE_D, centre=(0, -l * 0.12, h + h * 0.21 - 0.05), chamfer=0.8, tile=6.0)
    base.box(fr, (w * 1.06, l * 0.12, 0.8), STEEL, centre=(0, l / 2 - 1.2, h * 0.5), tile=3.0)
    base.box(fr, (w + 0.8, l - 3.0, 0.5), PLATE_B, centre=(0, 0, 0.5), chamfer=0.15, tile=6.0)
    if "windows_a" in K.trims:
        K.strip(fr.p(-w / 2 + 0.7, l / 2 + 0.03, h * 0.45), fr.r, fr.f, w - 1.4, "windows_a", lift=0.0, scale=1.25, mb=glass)
        for sx in (-1, 1):
            K.strip(fr.p(sx * (w / 2 + 0.03), -l / 2 + 2.0 if sx < 0 else l / 2 - 2.0, h * 0.45), fr.f * (1 if sx < 0 else -1), fr.r * sx, l - 4.0, "windows_a", lift=0.0, scale=1.25, mb=glass)
    rng = K.rng("bridge", key)
    for k in range(7):
        y = -l * 0.42 + k * (l * 0.84 / 6)
        up = h + 3.2 + 2.2 * math.sin(math.pi * k / 6) + rng.uniform(-0.3, 0.4)
        pts = [fr.p(-w * 0.5, y, h * 0.5), fr.p(-w * 0.52, y + rng.uniform(-0.2, 0.2), h * 0.95), fr.p(-w * 0.3, y, up * 0.96), fr.p(0.0, y, up), fr.p(w * 0.3, y, up * 0.96),
               fr.p(w * 0.52, y + rng.uniform(-0.2, 0.2), h * 0.95), fr.p(w * 0.5, y, h * 0.5)]
        sweep(rib, np.array(pts), 0.34, BIO, sides=6)
    sweep(rib, np.array([fr.p(0.0, -l * 0.46, h + 3.0), fr.p(0.0, 0.0, h + 5.4), fr.p(0.0, l * 0.46, h + 3.1)]), 0.3, BIO, sides=6)
    if "glyph_a" in K.trims:
        K.strip(fr.p(-0.55, -l * 0.40, h + 5.0), fr.f, fr.u, l * 0.8, "glyph_a", lift=0.0, scale=0.9, mb=rib)
    for sx, ln in ((-1, 7.0), (1, 4.5)):
        K.pipe(fr.p(sx * w * 0.3, -l * 0.3, h + 1.0), fr.p(sx * w * 0.34, -l * 0.34, h + 1.0 + ln), 0.1, sides=4, mb=base)
    d = sb.variant("destroyed")
    d.box(fr, (w, l, h * 0.55), TRIM, centre=(0, 0, h * 0.27), chamfer=1.4, rect=K.rect("burnt"))
    mnparts.wreck_stump(K, d, fr.moved(0, 0, h * 0.5), w * 0.38, h, key, plates=8, embers=6)
    for k in range(3):
        y = -l * 0.3 + k * l * 0.3
        sweep(d, np.array([fr.p(-w * 0.5, y, h * 0.5), fr.p(-w * 0.55, y, h * 1.1), fr.p(-w * 0.3, y + 1.0, h * 1.9)]), 0.34, BIO, sides=5)
    mnparts.debris_set(K, sb, fr, key, [
        {"kind": "chunk", "size": (w * 0.5, l * 0.35, h * 0.6), "at": (0, 0, h + 1.5), "speed": 6},
        {"kind": "tube", "size": (0.6, w * 0.9, 0), "at": (0, l * 0.2, h + 3.5), "speed": 9},
        {"kind": "plate", "size": (w * 0.6, l * 0.3, 0.4), "at": (w * 0.3, -l * 0.2, h), "speed": 8},
        {"kind": "chunk", "size": (w * 0.25, l * 0.2, h * 0.5), "at": (-w * 0.3, l * 0.3, h * 0.8), "speed": 10},
    ])


def sensor_crest(K, sb, fr, key, r=7.0):
    """The sensor crest on the prow: a fan of seven tall dark ribbed vanes on a plated seat with a steel dish at its foot. ``fr``: origin on the hull, y = forward, z = out.
    Wreck: broken stumps; debris: vanes."""
    if sb.lod == 2:
        sb.part().box(fr, (r * 1.6, r * 1.2, r * 1.5), PLATE, centre=(0, 0, r * 0.7), chamfer=r * 0.2)
        return
    if sb.lod == 1:
        return
    base, vanes = sb.part("base"), sb.part("vanes")
    base.box(fr, (r * 2.0, r * 1.6, 1.0), PLATE, centre=(0, 0, 0.5), chamfer=0.5, tile=5.0)
    rng = K.rng("crest", key)
    for k in range(7):
        a = math.radians(-60 + 20 * k)
        hgt = r * (1.5 + 0.5 * math.cos(a * 1.6)) * rng.uniform(0.9, 1.1)
        x0 = math.sin(a) * r * 0.9
        pts = [fr.p(x0, 0.0, 1.0), fr.p(x0 * 1.15, -r * 0.18, hgt * 0.5), fr.p(x0 * 1.3, -r * 0.3, hgt * 0.9), fr.p(x0 * 1.34, -r * 0.34, hgt)]
        sweep(vanes, np.array(pts), 0.52, BIO, sides=6, r_end=0.12)
    for z in (r * 0.55, r * 0.95):
        sweep(vanes, np.array([fr.p(math.sin(math.radians(-60 + 20 * k)) * r * (0.9 + 0.4 * z / r), -r * 0.12 * z / r, z) for k in range(7)]), 0.16, STEEL, sides=5)
    vanes.lathe([(0.0, 0.0), (r * 0.45, r * 0.07), (r * 0.62, r * 0.2)], Frame(fr.p(0, r * 0.9, 1.8), norm(fr.d(0.0, 0.7, 0.7)), fr.u), STEEL, segs=14, tile=2.0)
    d = sb.variant("destroyed")
    d.box(fr, (r * 2.0, r * 1.6, 0.6), TRIM, centre=(0, 0, 0.3), chamfer=0.5, rect=K.rect("burnt"))
    mnparts.wreck_stump(K, d, fr.moved(0, 0, 0.3), r * 0.9, r * 0.7, key, plates=4, embers=3)
    for k in (1, 4):
        a = math.radians(-60 + 20 * k)
        sweep(d, np.array([fr.p(math.sin(a) * r * 0.9, 0.0, 1.0), fr.p(math.sin(a) * r * 1.0, 0.4, r * 0.6)]), 0.5, BIO, sides=5)
    mnparts.debris_set(K, sb, fr, key, [
        {"kind": "tube", "size": (1.0, r * 1.2, 0), "at": (r * 0.3, 0, r * 1.0), "speed": 8},
        {"kind": "tube", "size": (1.0, r * 1.0, 0), "at": (-r * 0.5, 0, r * 0.9), "speed": 10},
        {"kind": "chunk", "size": (r * 0.6, r * 0.5, r * 0.4), "at": (0, r * 0.7, 2.0), "speed": 7},
    ])
