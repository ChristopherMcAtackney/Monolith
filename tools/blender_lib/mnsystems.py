"""mnsystems: the larger separable parts that are not guns or engines: sensor arrays, comms masts, reactor stacks, launcher banks, the bridge.
Same contract as mnparts: kit ``K``, subsystem context ``sb``, a frame, a key; intact look, wreck, debris."""
import math

import numpy as np

from .mnkit import PLATE, TRIM, LIGHTS, ENGINE, CHROME
from .mnmesh import Frame, norm, cross, chamfered_rect
from .mnparts import wreck_stump, debris_set, torn_plate, _ember


def sensor_dish(K, sb, fr, key, r=6.0):
    """A phased-array disc on a fork. ``fr``: origin at the mount, local y the way the array faces."""
    if sb.lod == 2:
        sb.part().box(fr, (r * 2, 2.0, r * 2), PLATE, centre=(0, 0, 0), chamfer=r * 0.4)
        return
    if sb.lod == 1:
        return
    base, head = sb.part("base"), sb.part("head")
    chrome = K.rect("chrome_brushed")
    base.box(fr, (3.4, 3.4, 3.0), PLATE, centre=(0, -1.0, -1.5), chamfer=0.7)
    for sx in (-1, 1):
        base.box(fr, (0.7, 1.2, r * 1.15), PLATE, centre=(sx * r * 0.62, -0.7, 0.0), chamfer=0.2, tile=5.0)
    head.lathe([(0.9, -2.2), (r * 0.5, -1.7), (r * 0.92, -0.7), (r, -0.1), (r, 0.35)], fr, CHROME, segs=36, tile=3.0)
    head.lathe([(r, 0.35), (r * 0.94, 0.45)], fr, CHROME, segs=36, tile=3.0)
    head.disc(fr, r * 0.94, 0.45, TRIM, segs=36, facing=1.0, rect=K.rect("sensor_grid"))
    head.box(fr, (0.9, 3.0, 0.9), PLATE, centre=(0, 1.6, 0), chamfer=0.2)
    d = sb.variant("destroyed")
    burnt = K.rect("burnt")
    d.box(fr, (3.4, 3.4, 3.0), PLATE, centre=(0, -1.0, -1.5), chamfer=0.7)
    d.box(fr, (0.7, 1.2, r * 1.15), PLATE, centre=(r * 0.62, -0.7, 0.0), chamfer=0.2, tile=5.0)
    d.lathe([(0.9, -2.2), (r * 0.5, -1.7), (r * 0.92, -0.7), (r, -0.1)], fr, TRIM, segs=20, rect=burnt, arc=(200, 330))
    wreck_stump(K, d, Frame(fr.p(0, -1.0, 0.1), fr.f, fr.u), 2.2, 1.6, key, plates=4, embers=3)
    debris_set(K, sb, fr, key, [
        {"kind": "chunk", "size": (r * 1.1, 0.8, r * 1.1), "at": (r * 0.3, 0.3, 0.0), "speed": 7},
        {"kind": "plate", "size": (r, r, 0.2), "at": (-r * 0.4, 0.2, r * 0.3), "speed": 9},
        {"kind": "chunk", "size": (1.4, 1.2, 1.6), "at": (-r * 0.6, -0.7, 0.0), "speed": 6},
    ])


def sensor_panel(K, sb, fr, key, w=10.0, h=9.0):
    """A flat phased-array panel on a plate (the fin's lattice): a raised frame with the sensor grid tiled over it. fr: origin at the panel
    centre on the surface, local z out of it, local y = up the panel."""
    if sb.lod == 2:
        sb.part().box(fr, (w, h, 0.6), PLATE, centre=(0, 0, 0.3))
        return
    if sb.lod == 1:
        return
    base = sb.part("base")
    base.box(fr, (w, h, 0.5), PLATE, centre=(0, 0, 0.25), chamfer=0.2, tile=6.0)
    K.tile_piece("sensor_grid", fr.p(-w / 2 + 0.3, -h / 2 + 0.3, 0.52), fr.r, fr.f, w - 0.6, h - 0.6, mb=base)
    d = sb.variant("destroyed")
    burnt = K.rect("burnt")
    d.box(fr, (w, h, 0.3), TRIM, centre=(0, 0, 0.15), rect=burnt)
    wreck_stump(K, d, Frame(fr.p(0, 0, 0.2), fr.f, fr.u), min(w, h) * 0.35, 1.0, key, plates=4, embers=3)
    debris_set(K, sb, fr, key, [
        {"kind": "plate", "size": (w * 0.4, h * 0.4, 0.2), "at": (w * 0.2, h * 0.15, 0.5), "speed": 6},
        {"kind": "chunk", "size": (2.2, 2.0, 0.8), "at": (-w * 0.2, -h * 0.1, 0.5), "speed": 8},
    ])


def comms_mast(K, sb, fr, key, height=28.0):
    """A lattice mast with cross-arms and small dishes. fr: origin at the foot, local z = up the mast."""
    if sb.lod == 2:
        sb.part().box(fr, (2.0, 2.0, height), PLATE, centre=(0, 0, height / 2))
        return
    if sb.lod == 1:
        return
    base = sb.part("base")
    chrome = K.rect("chrome_brushed")
    base.box(fr, (4.2, 4.2, 1.2), PLATE, centre=(0, 0, 0.6), chamfer=0.9)
    K.pipe(fr.p(0, 0, 1.0), fr.p(0, 0, height), 0.7, mb=base, sides=8)
    K.pipe(fr.p(0, 0, height), fr.p(0, 0, height + 3.5), 0.18, mb=base, sides=5)
    for t in (0.35, 0.6, 0.85):
        z = height * t
        w = 5.5 - 2.2 * t
        K.pipe(fr.p(-w, 0, z), fr.p(w, 0, z), 0.22, mb=base, sides=5)
        K.pipe(fr.p(0, -w * 0.8, z + 0.3), fr.p(0, w * 0.8, z + 0.3), 0.22, mb=base, sides=5)
        for sx in (-1, 1):
            f2 = Frame(fr.p(sx * w, 0, z), (sx * 0.4, 0.2, 0.9), (0, 0, 1))
            base.lathe([(0.15, -0.6), (1.0, -0.1), (1.1, 0.12)], f2, CHROME, segs=14, tile=3.0)
    for k in range(3):
        K.pipe(fr.p(math.cos(k * 2.1) * 1.8, math.sin(k * 2.1) * 1.8, 1.0), fr.p(0, 0, height * 0.5), 0.12, mb=base, sides=4)
    d = sb.variant("destroyed")
    d.box(fr, (4.2, 4.2, 1.2), PLATE, centre=(0, 0, 0.6), chamfer=0.9)
    cut = height * 0.28
    K.pipe(fr.p(0, 0, 1.0), fr.p(0, 0, cut), 0.7, mb=d, rect_name="burnt", sides=8)
    K.pipe(fr.p(0, 0, cut), fr.p(2.6, 1.0, cut + 6.5), 0.55, mb=d, rect_name="burnt", sides=6)
    wreck_stump(K, d, Frame(fr.p(0, 0, 0.6), fr.f, fr.u), 2.0, 1.2, key, plates=3, embers=2)
    debris_set(K, sb, fr, key, [
        {"kind": "tube", "size": (1.2, height * 0.45, 0), "at": (1.0, 0.0, height * 0.65), "speed": 5},
        {"kind": "tube", "size": (0.5, 8.0, 0), "at": (-1.0, 0.5, height * 0.8), "speed": 8},
        {"kind": "chunk", "size": (2.0, 2.0, 1.4), "at": (0.0, 0.0, height * 0.5), "speed": 6},
    ])


def reactor_stacks(K, sb, fr, key, n=3, spacing=6.0, radius=1.7, height=6.5):
    """The reactor's heat-exchange stacks in a vented housing. fr: origin on the hull, local z = out of it, local y along the ship."""
    if sb.lod == 2:
        sb.part().box(fr, (n * spacing + 2, 8.0, height), PLATE, centre=(0, 0, height / 2))
        return
    if sb.lod == 1:
        return
    base = sb.part("base")
    chrome = K.rect("chrome_brushed")
    w = n * spacing
    base.box(fr, (w + 2.0, 12.0, 1.6), PLATE, centre=(0, 0, 0.8), chamfer=0.6, tile=6.0)
    for i in range(n):
        x = (i - (n - 1) / 2) * spacing
        ax = Frame(fr.p(x, 0, 1.6), fr.u, fr.f)
        base.lathe([(radius * 1.25, 0), (radius * 1.25, 0.6), (radius, 0.9), (radius, height * 0.85), (radius * 1.2, height * 0.9), (radius * 1.2, height)], ax, CHROME, segs=20, tile=3.0)
        base.disc(ax, radius * 1.2, height, TRIM, segs=20, facing=1.0, rect=K.rect("vent"))
        for k in range(3):
            y0 = height * (0.25 + 0.2 * k)
            base.lathe([(radius * 1.08, y0), (radius * 1.3, y0 + 0.2), (radius * 1.08, y0 + 0.4)], ax, CHROME, segs=20, tile=3.0)
    for sy in (-1, 1):
        K.decal(fr.p(0, sy * 4.2, 1.62), fr.r, fr.u, "vent", scale=1.5, mb=base)
    d = sb.variant("destroyed")
    rng = K.rng("stacks-wreck", key)
    burnt = K.rect("burnt")
    d.box(fr, (w + 2.0, 12.0, 1.6), PLATE, centre=(0, 0, 0.8), chamfer=0.6, tile=6.0)
    for i in range(n):
        x = (i - (n - 1) / 2) * spacing
        ax = Frame(fr.p(x, 0, 1.6), fr.u, fr.f)
        hh = height * rng.uniform(0.15, 0.5)
        d.lathe([(radius * 1.25, 0), (radius * 1.25, 0.6), (radius, 0.9), (radius, hh)], ax, TRIM, segs=14, rect=burnt, arc=(0, 300))
        wreck_stump(K, d, Frame(fr.p(x, 0, 1.6), fr.f, fr.u), radius * 1.3, 1.4, (key, i), plates=3, embers=2)
    debris_set(K, sb, fr, key, [
        {"kind": "tube", "size": (radius * 2, height * 0.6, 0), "at": (-spacing, 0.0, 3.0), "speed": 6},
        {"kind": "chunk", "size": (3.0, 3.0, 2.2), "at": (0.0, 0.0, 2.5), "speed": 7},
        {"kind": "plate", "size": (3.0, 3.0, 0.2), "at": (spacing, 1.0, 3.0), "speed": 9},
        {"kind": "chunk", "size": (1.6, 1.6, 1.2), "at": (spacing * 0.5, -1.5, 4.0), "speed": 12},
    ])


def launcher_bank(K, sb, fr, key, cols=3, rows=2):
    """A vertical-launch bank: an armoured block with sealed hatches. fr: origin on the hull, local z out, local y along the ship."""
    w, l, h = cols * 2.4 + 1.2, rows * 2.6 + 1.4, 2.0
    if sb.lod == 2:
        sb.part().box(fr, (w, l, h), PLATE, centre=(0, 0, h / 2))
        return
    if sb.lod == 1:
        return
    base = sb.part("base")
    base.box(fr, (w, l, h), PLATE, centre=(0, 0, h / 2), chamfer=0.45, tile=6.0)
    for i in range(cols):
        for j in range(rows):
            c = fr.p((i - (cols - 1) / 2) * 2.4, (j - (rows - 1) / 2) * 2.6, h + 0.03)
            K.decal(c, fr.f, fr.u, "hatch_b", scale=1.0, mb=base)
    d = sb.variant("destroyed")
    burnt = K.rect("burnt")
    d.box(fr, (w, l, h * 0.7), PLATE, centre=(0, 0, h * 0.35), chamfer=0.45, tile=6.0)
    rng = K.rng("launcher-wreck", key)
    for i in range(cols):
        for j in range(rows):
            cx, cy = (i - (cols - 1) / 2) * 2.4, (j - (rows - 1) / 2) * 2.6
            if rng.random() < 0.6:
                d.box(fr, (1.9, 2.1, 0.2), TRIM, centre=(cx, cy, h * 0.7 + 0.1), rect=burnt)
                _ember(K, d, fr.p(cx, cy, h * 0.7 + 0.25), fr.u, 0.45)
            else:
                torn_plate(K, d, fr.p(cx, cy, h * 0.7 + 0.5), fr.d(rng.normal(0, 0.4), rng.normal(0, 0.4), 1.0), 1.1, rng)
    wreck_stump(K, d, Frame(fr.p(0, 0, h * 0.7), fr.f, fr.u), min(w, l) * 0.42, 1.4, key, plates=3, embers=2)
    debris_set(K, sb, fr, key, [
        {"kind": "plate", "size": (2.2, 2.2, 0.2), "at": (0.0, 0.0, h + 0.2), "speed": 9},
        {"kind": "chunk", "size": (2.0, 2.6, 1.2), "at": (-1.2, 0.5, h), "speed": 7},
        {"kind": "plate", "size": (2.2, 2.2, 0.2), "at": (1.5, -1.0, h + 0.2), "speed": 11},
    ])


def bridge_block(K, sb, fr, key, w=17.0, l=24.0, h=5.6):
    """The command block: a low armoured block on the deck with a lit canopy band across the front and slit windows down the flanks.
    fr: origin at the block's centre on the deck, local y = forward."""
    if sb.lod == 2:
        sb.part().box(fr, (w, l, h), PLATE, centre=(0, 0, h / 2), chamfer=1.5)
        return
    if sb.lod == 1:
        return
    base = sb.part("base")
    prof = chamfered_rect(w / 2, h / 2, 1.5)
    bf = Frame(fr.p(0, 0, h / 2), fr.f, fr.u)
    base.extrude(prof, bf, -l / 2, l / 2 - 3.0, PLATE, tile=6.0, cap_mat=PLATE)
    base.extrude(prof, bf, l / 2 - 3.0, l / 2, PLATE, s0=(1.0, 1.0), s1=(0.86, 0.72), tile=6.0, cap_mat=PLATE)
    # lit canopy band on the (sloped) front face, slit windows on both flanks
    K.decal(fr.p(0, l / 2 + 0.02, h * 0.5), fr.r, fr.f, "windows_big", lift=0.05, mb=base, size=(w * 0.74, h * 0.4))
    for sx in (-1, 1):
        n = fr.r * sx
        run = cross(fr.u, n)
        if np.dot(run, fr.f) < 0:
            start = fr.p(sx * (w / 2 + 0.0), l / 2 - 4.0, h * 0.42)
        else:
            start = fr.p(sx * (w / 2 + 0.0), -l / 2 + 4.0, h * 0.42)
        K.strip(start, run, n, l - 8.0, "windows_slit", lift=0.03, mb=base)
    d = sb.variant("destroyed")
    burnt = K.rect("burnt")
    d.extrude(prof, bf, -l / 2, l / 2 - 8.0, PLATE, tile=6.0, cap_mat=PLATE, s1=(1, 0.8))
    d.box(fr, (w * 0.8, l * 0.28, h * 0.5), TRIM, centre=(0, l / 2 - 8.0 + 3.0, h * 0.25), rect=burnt)
    wreck_stump(K, d, Frame(fr.p(0, l * 0.18, h * 0.9), fr.f, fr.u), w * 0.36, 3.0, key, plates=6, embers=5)
    debris_set(K, sb, fr, key, [
        {"kind": "plate", "size": (w * 0.3, 2.6, 0.3), "at": (-w * 0.2, l * 0.5, h * 0.55), "speed": 7},
        {"kind": "chunk", "size": (4.0, 3.0, 2.0), "at": (w * 0.2, l * 0.45, h * 0.7), "speed": 6},
        {"kind": "plate", "size": (3.4, 3.4, 0.3), "at": (0.0, l * 0.3, h), "speed": 10},
        {"kind": "chunk", "size": (1.8, 1.6, 1.4), "at": (-w * 0.1, l * 0.35, h * 0.9), "speed": 13},
    ])
