"""Earth hull texture set: matte black armour, chrome, lit windows, burnt metal, scorch decals. Generated, deterministic, no stock images.

    tools\\blender.ps1 art_src\\earth_hull\\build_earth_hull_textures.py

Writes (all 8-bit PNG, Unity imports them by suffix: _albedo/_emissive colour, _normal normal map, _mask/_detail linear data):
    Game/Assets/Monolith/Art/EarthHull/
        earth_plate_{albedo,normal,mask}.png    1024^2, tiles every 12 m: armour plate paint, grime, scratches, chips   (material MN_EarthPlate)
        earth_trim_{albedo,normal,mask,emissive}.png  2048^2 atlas of kit pieces (windows, hatches, vents, chrome, labels ...) (MN_EarthTrim)
        earth_detail.png                        512^2 tiling micro detail (scratch grain), laid on everything by UV1 at 2 m
        earth_lights_emissive.png               a palette of running-light colours
        earth_engine_emissive.png               the exhaust bell's heat gradient
        earth_damage_{albedo,normal,mask,emissive}.png  2048^2, 4 x 4 cells of scorch, blast, crater and streak decals (HDRP decal projectors)
        earth_hull.materials.json               the HDRP material recipes ShipArtSetup reads
    art_src/earth_hull/trims.json               atlas rectangles (UV) and metric sizes of every trim piece, read by model scripts

Mask maps follow HDRP: R metallic, G ambient occlusion, B detail mask, A smoothness.
"""
import json
import math
import os
import sys
import time

import numpy as np

from blender_lib import mntex
from blender_lib.mntex import Layers, Atlas, spectral_noise, blur, smoothstep, cover

ROOT = os.environ.get("MN_ROOT", os.getcwd())
OUT = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "EarthHull")
TRIMS_JSON = os.path.join(ROOT, "art_src", "earth_hull", "trims.json")
os.makedirs(OUT, exist_ok=True)

T0 = time.time()


def log(msg):
    print(f"[MN] textures {time.time() - T0:5.1f}s  {msg}", flush=True)


def rngf(seed):
    return np.random.default_rng(seed)


# ------------------------------------------------------------------------------------------------ painting helpers

def draw_segment(L, p0, p1, width, wrap=True, **kw):
    """A soft line, drawn again shifted by the canvas size where it crosses an edge so tiling textures stay seamless."""
    w, h = L.w, L.h
    xs = (p0[0], p1[0])
    ys = (p0[1], p1[1])
    for ox in ((-w, 0, w) if wrap else (0,)):
        if max(xs) + ox < -4 or min(xs) + ox > w + 4:
            continue
        for oy in ((-h, 0, h) if wrap else (0,)):
            if max(ys) + oy < -4 or min(ys) + oy > h + 4:
                continue
            L.line((p0[0] + ox, p0[1] + oy), (p1[0] + ox, p1[1] + oy), width, **kw)


def scratches(L, rng, n, length=(30, 260), width=(0.7, 1.5), angle=90.0, spread=14.0, bright=0.72, metal=0.85, depth=0.7, gloss=0.28, alpha=(0.15, 0.6)):
    """Long thin scratches mostly along ``angle`` (degrees, 90 = along the image's vertical = the ship's length): bright bare metal in a groove."""
    for _ in range(n):
        ln = rng.uniform(*length)
        a = math.radians(angle + rng.normal(0, spread) + (90 if rng.random() < 0.12 else 0))
        x0, y0 = rng.uniform(0, L.w), rng.uniform(0, L.h)
        x1, y1 = x0 + math.cos(a) * ln, y0 + math.sin(a) * ln
        wd = rng.uniform(*width)
        al = rng.uniform(*alpha)
        draw_segment(L, (x0, y0), (x1, y1), wd, k=al, albedo=(bright, bright, bright * 1.02), metal=metal, height_add=-depth * al, smooth_add=gloss * al)
    return L


def chips(L, rng, n, size=(1.5, 5.5), edge_bias=None, bright=0.62, metal=0.9, smooth=0.55):
    """Paint chips: small irregular blobs of bare metal."""
    for _ in range(n):
        x, y = rng.uniform(0, L.w), rng.uniform(0, L.h)
        r = rng.uniform(*size)
        for k in range(rng.integers(1, 4)):
            dx, dy = rng.normal(0, r * 0.7, 2)
            rr = r * rng.uniform(0.4, 1.0)
            for ox in (-L.w, 0, L.w):
                for oy in (-L.h, 0, L.h):
                    xx, yy = x + dx + ox, y + dy + oy
                    if -rr < xx < L.w + rr and -rr < yy < L.h + rr:
                        L.rect(xx - rr, yy - rr * rng.uniform(0.6, 1.2), xx + rr, yy + rr, albedo=(bright, bright * 0.99, bright * 0.97), metal=metal, smooth=smooth,
                               height_add=-0.5)


def soft_mask_noise(L, rng, beta, lo, hi, fmin=0.0, fmax=None, sx=1.0, sy=1.0):
    n = spectral_noise(L.h, L.w, rng, beta=beta, fmin=fmin, fmax=fmax, sx=sx, sy=sy)
    return smoothstep(lo, hi, n)


def grain_height(L, rng, amount, fmin=60):
    L.hgt += amount * spectral_noise(L.h, L.w, rng, beta=0.5, fmin=fmin)


def vertical_gradient(L, y0, y1, c0, c1, **kw):
    """Fill rows y0..y1 with a gradient from c0 to c1 (scalar or colour)."""
    ys = np.arange(int(y0), int(y1))
    t = ((ys + 0.5 - y0) / max(y1 - y0, 1e-6))[:, None]
    return t


# ------------------------------------------------------------------------------------------------ the plate tile

PAINT_TONES = {          # name -> sRGB value of the paint: deep black, graphite (the default), mid grey, light grey, pale cool grey
    "deep": 0.20, "graphite": 0.31, "mid": 0.45, "light": 0.60, "pale": 0.73,
}


def make_plate(size=1024, seed=11, tone=0.31):
    """Matte black armour paint: one satin coat, faintly different from plate to plate, with sparse fine scratches and a few chips down to
    bare steel. Albedo is the final value (dark charcoal), not a pattern to be tinted."""
    rng = rngf(seed)
    L = Layers(size, size, albedo=(tone, tone * 1.01, tone * 1.045), metal=0.0, smooth=0.42, ao=1.0)
    n_big = spectral_noise(size, size, rng, beta=3.2, fmax=5)
    n_mid = spectral_noise(size, size, rng, beta=2.4, fmin=5, fmax=24)
    n_fine = spectral_noise(size, size, rng, beta=1.0, fmin=150)
    n_streak = spectral_noise(size, size, rng, beta=2.0, fmin=2, fmax=60, sx=1.0, sy=0.06)      # long along the image's vertical = the ship's length
    L.tint_noise(n_big, 0.035)
    L.tint_noise(n_mid, 0.022)
    L.tint_noise(n_fine, 0.012)
    L.tint_noise(n_streak, 0.04)
    # satin paint: smoothness wanders a little and is duller under the long grime streaks
    L.smo += 0.035 * n_mid + 0.03 * n_big - 0.07 * np.clip(n_streak, 0, None)
    # orange peel in the height field (pixels of relief): a fine skin, not stone
    L.hgt += 0.07 * n_fine + 0.14 * spectral_noise(size, size, rng, beta=1.6, fmin=18, fmax=110)
    chips(L, rng, 36, size=(0.9, 2.6))
    scratches(L, rng, 64, length=(60, 480), width=(0.6, 1.0), angle=90, alpha=(0.1, 0.4))
    scratches(L, rng, 14, length=(20, 100), width=(0.5, 0.9), angle=0, spread=40, alpha=(0.1, 0.35))
    L.ao *= 1.0 - 0.03 * np.clip(n_mid, 0, None)
    return L


# ------------------------------------------------------------------------------------------------ chrome tile

def make_chrome(size=512, seed=71):
    """Brushed chrome that tiles every 4 m (u along the brushing): calm, polished, a little uneven, with a few hairline scratches."""
    rng = rngf(seed)
    L = Layers(size, size, albedo=(0.86, 0.87, 0.90), metal=1.0, smooth=0.95, ao=1.0)
    band = spectral_noise(size, size, rng, beta=2.2, fmin=1, fmax=36, sx=0.04, sy=1.0)
    L.tint_noise(band, 0.02)
    L.smo += 0.02 * band
    L.hgt += 0.16 * spectral_noise(size, size, rng, beta=0.8, fmin=14, sx=0.03, sy=1.0)
    for _ in range(26):
        x0, y0 = rng.uniform(0, size), rng.uniform(0, size)
        ln = rng.uniform(20, 160)
        a = math.radians(rng.normal(0, 4))
        draw_segment(L, (x0, y0), (x0 + math.cos(a) * ln, y0 + math.sin(a) * ln), 0.7, k=0.3, albedo=(0.95, 0.96, 0.98), smooth_add=-0.08, height_add=-0.2)
    return L


# ------------------------------------------------------------------------------------------------ detail map

def make_detail(size=512, seed=21):
    """HDRP detail map: R albedo (0.5 = neutral), G normal Y, B smoothness (0.5 = neutral), A normal X."""
    rng = rngf(seed)
    L = Layers(size, size, albedo=(0.5, 0.5, 0.5), metal=0, smooth=0.5)
    h = 0.5 * spectral_noise(size, size, rng, beta=0.8, fmin=40) + 0.8 * spectral_noise(size, size, rng, beta=2.0, fmin=6, fmax=70)
    L.hgt = h
    for _ in range(90):
        a = math.radians(90 + rng.normal(0, 25))
        ln = rng.uniform(20, 160)
        x0, y0 = rng.uniform(0, size), rng.uniform(0, size)
        draw_segment(L, (x0, y0), (x0 + math.cos(a) * ln, y0 + math.sin(a) * ln), rng.uniform(0.6, 1.2), height_add=-0.9, smooth_add=0.2)
    nrm = mntex.normal_from_height(L.hgt, 0.9)
    alb = np.clip(0.5 + 0.10 * spectral_noise(size, size, rng, beta=1.2, fmin=4) - 0.20 * np.clip(-(L.hgt - L.hgt.mean()) / 3, 0, 1), 0, 1)
    smo = np.clip(0.5 + 0.12 * spectral_noise(size, size, rng, beta=1.0, fmin=3) + L.smo * 0 + (L.smo - 0.5), 0, 1)
    out = np.stack([alb, nrm[:, :, 1], smo, nrm[:, :, 0]], axis=2)
    return mntex.to_u8(out)


# ------------------------------------------------------------------------------------------------ trim pieces
PX = 128    # default pixels per metre in the trim sheet


def px(m, density=PX):
    return int(round(m * density))


def piece_underlayer():
    rng = rngf(31)
    L = Layers(64, 64, albedo=(0.30, 0.30, 0.32), metal=0.0, smooth=0.18, ao=0.55)
    L.tint_noise(spectral_noise(64, 64, rng, beta=1.5), 0.06)
    L.hgt += spectral_noise(64, 64, rng, beta=1.2) * 0.5
    return L


def piece_edge_paint(tile_m=8.0, h_m=0.25, density=PX):
    """Narrow strip for the walls and chamfers of raised plates: paint with chips of bare metal, more along the lip."""
    rng = rngf(32)
    w, h = px(tile_m, density), max(px(h_m, density), 16)
    L = Layers(w, h, albedo=(0.36, 0.365, 0.38), metal=0.0, smooth=0.42, ao=0.8)
    L.tint_noise(spectral_noise(h, w, rng, beta=1.4, fmin=8), 0.06)
    L.hgt += 0.5 * spectral_noise(h, w, rng, beta=1.0, fmin=40)
    # worn lip: the upper rows are brighter bare metal in patches
    lip = np.clip(spectral_noise(h, w, rng, beta=1.0, fmin=30) * 0.5 + 0.1, 0, 1) * np.linspace(0, 1, h)[:, None] ** 1.5
    lip = smoothstep(0.25, 0.55, lip)
    L.alb = L.alb * (1 - lip[:, :, None]) + np.array([0.74, 0.75, 0.78]) * lip[:, :, None]
    L.met = L.met * (1 - lip) + 0.95 * lip
    L.smo = L.smo * (1 - lip) + 0.62 * lip
    return L


def piece_chrome_brushed(w_m=8.0, h_m=2.0, density=64):
    rng = rngf(33)
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.86, 0.87, 0.9), metal=1.0, smooth=0.84, ao=1.0)
    band = spectral_noise(h, w, rng, beta=2.2, fmin=1, fmax=40, sx=0.05, sy=1.0)          # brushing: long along u
    L.tint_noise(band, 0.02)
    L.smo += 0.025 * band
    L.hgt += 0.25 * spectral_noise(h, w, rng, beta=0.6, fmin=20, sx=0.04, sy=1.0)
    for _ in range(int(w_m * h_m * 2)):                                                      # hairline scratches along u
        x0, y0 = rng.uniform(0, w), rng.uniform(0, h)
        ln = rng.uniform(10, 120)
        a = math.radians(rng.normal(0, 5))
        draw_segment(L, (x0, y0), (x0 + math.cos(a) * ln, y0 + math.sin(a) * ln), 0.6, albedo=(0.95, 0.96, 0.98), smooth_add=-0.1, height_add=-0.25)
    # a faint tarnish at the lowest rows (heat blueing)
    L.alb[:, :, 2] *= 1.0 + 0.04 * np.clip(spectral_noise(h, w, rng, beta=3, fmax=4), 0, 2)
    return L


def piece_chrome_panel(m=4.0, density=64):
    rng = rngf(34)
    s = px(m, density)
    L = Layers(s, s, albedo=(0.85, 0.86, 0.9), metal=1.0, smooth=0.8, ao=1.0)
    n = 4
    cell = s // n
    for iy in range(n):
        for ix in range(n):
            tone = 1.0 + rng.normal(0, 0.035)
            L.rect(ix * cell, iy * cell, (ix + 1) * cell, (iy + 1) * cell, alb_mul=np.full((cell, cell), tone), smooth_add=rng.normal(0, 0.03))
            # seam: a dark groove on the left and top edge of every panel
            L.rect(ix * cell, iy * cell, ix * cell + 2, (iy + 1) * cell, albedo=(0.12, 0.12, 0.13), metal=0.2, smooth=0.25, ao=0.35, height=-1.6)
            L.rect(ix * cell, iy * cell, (ix + 1) * cell, iy * cell + 2, albedo=(0.12, 0.12, 0.13), metal=0.2, smooth=0.25, ao=0.35, height=-1.6)
            for bx, by in ((6, 6), (cell - 8, 6), (6, cell - 8), (cell - 8, cell - 8)):
                L.rect(ix * cell + bx - 1.5, iy * cell + by - 1.5, ix * cell + bx + 1.5, iy * cell + by + 1.5, albedo=(0.7, 0.71, 0.74), height=1.0, smooth=0.6)
    L.tint_noise(spectral_noise(s, s, rng, beta=2.0, fmin=2, fmax=30), 0.03)
    scratches(L, rng, int(m * m * 2), length=(10, 80), width=(0.6, 1.0), angle=0, spread=70, bright=0.92, metal=1.0, gloss=-0.05, depth=0.15, alpha=(0.1, 0.35))
    return L


def _dark_metal_base(L, rng, tone=0.22):
    L.alb[:] = np.array([tone, tone, tone * 1.04], np.float32)
    L.met[:] = 0.55
    L.smo[:] = 0.42
    L.tint_noise(spectral_noise(L.h, L.w, rng, beta=1.6, fmin=4), 0.08)
    L.hgt += 0.4 * spectral_noise(L.h, L.w, rng, beta=0.8, fmin=30)


def piece_windows(name, w_m, rows, win_w, win_h, pitch, gap_every, density=PX, seed=40, tint=(0.80, 0.90, 1.0), off_rate=0.10):
    """A strip of lit windows (tiles along u). rows: number of window rows. Each window: dark bezel, glass, emissive cold white with
    per-window brightness, a soft inner glow, a few dark ones, and blocks separated by gaps."""
    rng = rngf(seed)
    row_h = win_h + 0.34
    h_m = rows * row_h + 0.12
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.30, 0.30, 0.33), metal=0.55, smooth=0.4, ao=0.8)
    _dark_metal_base(L, rng, 0.30)
    # frame strip: a slightly proud band behind each row
    for r in range(rows):
        y0 = px(0.06 + r * row_h, density)
        y1 = y0 + px(row_h - 0.04, density)
        L.rect(0, y0, w, y1, albedo=(0.12, 0.12, 0.13), metal=0.3, smooth=0.3, height=1.5, ao=0.6)
        n_win = int(w_m / pitch)
        for i in range(n_win):
            if (i % gap_every) == gap_every - 1:       # gap between blocks
                continue
            cx = (i + 0.5) * w / n_win
            ww, wh = px(win_w, density), px(win_h, density)
            xa, xb = cx - ww / 2, cx + ww / 2
            ya = (y0 + y1) / 2 - wh / 2
            yb = ya + wh
            on = rng.random() > off_rate
            br = (0.45 + 0.55 * rng.random()) if on else 0.0
            # glass: sunk, dark; emissive inside
            L.bevel_rect(xa, ya, xb, yb, -2.2, 2.0, albedo=(0.03, 0.04, 0.05), metal=0.2, smooth=0.9, ao=1.0)
            if on:
                # inner glow: bright core, falling off to the frame
                ys, xs, c = cover(h, w, xa + 1.5, ya + 1.5, xb - 1.5, yb - 1.5)
                if c.size:
                    gx = (np.arange(xs.start, xs.stop) + 0.5)[None, :]
                    gy = (np.arange(ys.start, ys.stop) + 0.5)[:, None]
                    fx = 1 - np.abs((gx - cx) / (ww / 2)) ** 3
                    fy = 1 - np.abs((gy - (ya + yb) / 2) / (wh / 2)) ** 3
                    g = np.clip(fx, 0, 1) * np.clip(fy, 0, 1) * 0.75 + 0.25
                    L.paint((ys, xs), c, emis=(np.array(tint)[None, None, :] * (br * g)[:, :, None]), albedo=(0.5, 0.55, 0.6))
            # a mullion on wide windows
            if win_w > 0.7 and on:
                L.rect(cx - 1, ya, cx + 1, yb, albedo=(0.1, 0.1, 0.11), emis=(0, 0, 0), height=1.0)
    return L, w_m, h_m


def piece_windows_slit(w_m=12.0, density=PX, seed=44):
    rng = rngf(seed)
    h_m = 0.5
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.18, 0.18, 0.2), metal=0.5, smooth=0.4, ao=0.8)
    _dark_metal_base(L, rng, 0.2)
    n = 5
    for i in range(n):
        xa = (i + 0.08) * w / n
        xb = (i + 0.92) * w / n
        ya, yb = h * 0.3, h * 0.7
        L.bevel_rect(xa, ya, xb, yb, -2.0, 1.5, albedo=(0.03, 0.04, 0.05), smooth=0.9)
        ys, xs, c = cover(h, w, xa + 1.5, ya + 1.5, xb - 1.5, yb - 1.5)
        if c.size:
            gx = (np.arange(xs.start, xs.stop) + 0.5)[None, :]
            f = 0.55 + 0.45 * (1 - np.abs((gx - (xa + xb) / 2) / ((xb - xa) / 2)) ** 4)
            L.paint((ys, xs), c, emis=np.array([0.85, 0.93, 1.0])[None, None, :] * (f * (0.7 + 0.3 * rng.random()))[:, :, None] * np.ones((ys.stop - ys.start, 1, 1)))
    return L, w_m, h_m


def piece_windows_big(density=PX, seed=45):
    """A bridge canopy: a wide band of tall panes with a bright cold interior."""
    rng = rngf(seed)
    w_m, h_m = 8.0, 2.6
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.16, 0.16, 0.18), metal=0.5, smooth=0.4, ao=0.7)
    _dark_metal_base(L, rng, 0.18)
    n = 6
    for i in range(n):
        xa = (i + 0.06) * w / n
        xb = (i + 0.94) * w / n
        ya, yb = h * 0.14, h * 0.86
        L.bevel_rect(xa, ya, xb, yb, -3.0, 3.0, albedo=(0.03, 0.035, 0.045), smooth=0.92)
        ys, xs, c = cover(h, w, xa + 3, ya + 3, xb - 3, yb - 3)
        gx = (np.arange(xs.start, xs.stop) + 0.5)[None, :]
        gy = (np.arange(ys.start, ys.stop) + 0.5)[:, None]
        fx = 1 - np.abs((gx - (xa + xb) / 2) / ((xb - xa) / 2)) ** 4
        fy = 1 - np.abs((gy - (ya + yb) / 2) / ((yb - ya) / 2)) ** 3
        g = (np.clip(fx, 0, 1) * np.clip(fy, 0, 1) * 0.8 + 0.2) * (0.75 + 0.25 * rng.random())
        L.paint((ys, xs), c, emis=np.array([0.82, 0.92, 1.0])[None, None, :] * g[:, :, None], albedo=(0.5, 0.55, 0.6))
        # glazing bars
        L.rect((xa + xb) / 2 - 2, ya, (xa + xb) / 2 + 2, yb, albedo=(0.1, 0.1, 0.11), emis=(0, 0, 0), height=1.5)
    return L, w_m, h_m


def piece_vent(w_m=4.0, h_m=2.0, density=64):
    rng = rngf(46)
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.2, 0.2, 0.22), metal=0.5, smooth=0.4, ao=0.8)
    _dark_metal_base(L, rng, 0.22)
    L.rect(0, 0, w, 3, albedo=(0.7, 0.72, 0.76), metal=1, smooth=0.75, height=1.5)
    L.rect(0, h - 3, w, h, albedo=(0.7, 0.72, 0.76), metal=1, smooth=0.75, height=1.5)
    L.rect(0, 0, 3, h, albedo=(0.7, 0.72, 0.76), metal=1, smooth=0.75, height=1.5)
    L.rect(w - 3, 0, w, h, albedo=(0.7, 0.72, 0.76), metal=1, smooth=0.75, height=1.5)
    slats = 9
    for i in range(slats):
        y = 8 + i * (h - 16) / slats
        L.rect(6, y, w - 6, y + (h - 16) / slats * 0.55, albedo=(0.02, 0.02, 0.025), ao=0.2, height=-3.0, smooth=0.2, metal=0.1)
        L.rect(6, y + (h - 16) / slats * 0.55, w - 6, y + (h - 16) / slats, albedo=(0.32, 0.33, 0.36), metal=0.7, smooth=0.55, height=2.0)
    return L, w_m, h_m


def piece_hatch(name, m=2.0, density=64, seed=47, hazard=False):
    rng = rngf(seed)
    s = px(m, density)
    L = Layers(s, s, albedo=(0.2, 0.2, 0.22), metal=0.5, smooth=0.4, ao=0.8)
    _dark_metal_base(L, rng, 0.24)
    pad = s * 0.1
    L.bevel_rect(pad, pad, s - pad, s - pad, 2.2, 3.0, albedo=(0.12, 0.12, 0.13), smooth=0.35, metal=0.45)
    L.rect(pad, pad, s - pad, pad + 2, albedo=(0.78, 0.8, 0.84), metal=1.0, smooth=0.7)
    L.rect(pad, s - pad - 2, s - pad, s - pad, albedo=(0.78, 0.8, 0.84), metal=1.0, smooth=0.7)
    L.rect(pad, pad, pad + 2, s - pad, albedo=(0.78, 0.8, 0.84), metal=1.0, smooth=0.7)
    L.rect(s - pad - 2, pad, s - pad, s - pad, albedo=(0.78, 0.8, 0.84), metal=1.0, smooth=0.7)
    # handle and bolts
    hx, hy = s * 0.5, s * 0.5
    L.bevel_rect(hx - s * 0.16, hy - s * 0.035, hx + s * 0.16, hy + s * 0.035, 3.0, 2.0, albedo=(0.82, 0.84, 0.88), metal=1.0, smooth=0.7)
    for bx in (pad + 8, s - pad - 8):
        for by in (pad + 8, s - pad - 8):
            L.rect(bx - 2, by - 2, bx + 2, by + 2, albedo=(0.7, 0.72, 0.76), metal=1, height=1.5, smooth=0.6)
    if hazard:
        for i in range(8):
            x = pad + 6 + i * (s - 2 * pad - 12) / 8
            L.line((x, s - pad - 5), (x + 7, s - pad - 16), 3.0, albedo=(0.9, 0.9, 0.92), metal=0, smooth=0.4)
    return L, m, m


def piece_conduit(w_m=6.0, h_m=0.8, density=PX):
    rng = rngf(48)
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.2, 0.2, 0.22), metal=0.5, smooth=0.4, ao=0.6)
    _dark_metal_base(L, rng, 0.2)
    pipes = 4
    for i in range(pipes):
        y0 = h * (i / pipes) + 2
        y1 = h * ((i + 1) / pipes) - 2
        ys, xs, c = cover(h, w, 0, y0, w, y1)
        gy = (np.arange(ys.start, ys.stop) + 0.5)[:, None]
        t = (gy - (y0 + y1) / 2) / ((y1 - y0) / 2)
        round_h = np.sqrt(np.clip(1 - t * t, 0, 1)) * 6.0
        tone = 0.35 + 0.35 * (i % 2)
        L.paint((ys, xs), c, albedo=(tone, tone, tone * 1.04), metal=0.9 if i % 2 else 0.5, smooth=0.6, height=np.broadcast_to(round_h, (ys.stop - ys.start, w)))
    for k in range(int(w_m / 1.5)):
        x = (k + 0.5) * w / int(w_m / 1.5)
        L.bevel_rect(x - 5, 0, x + 5, h, 4.0, 2.5, albedo=(0.72, 0.74, 0.78), metal=1.0, smooth=0.65)
    return L, w_m, h_m


def piece_fasteners(w_m=8.0, h_m=0.3, density=PX):
    rng = rngf(49)
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.2, 0.2, 0.22), metal=0.4, smooth=0.4, ao=0.8)
    _dark_metal_base(L, rng, 0.22)
    n = int(w_m / 0.25)
    for i in range(n):
        x = (i + 0.5) * w / n
        L.bevel_rect(x - 4, h / 2 - 4, x + 4, h / 2 + 4, 2.5, 1.5, albedo=(0.7, 0.72, 0.76), metal=1.0, smooth=0.6)
    return L, w_m, h_m


# --- stencil lettering ---
GLYPHS = {
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "C": ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
    "D": ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    "T": ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    "X": ["10001", "01010", "00100", "00100", "00100", "01010", "10001"],
    "-": ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
    "0": ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    "2": ["01110", "10001", "00001", "00110", "01000", "10000", "11111"],
    "3": ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
    "4": ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
    "5": ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
    "6": ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
    "7": ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
    "8": ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
    "9": ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
}


def stencil(L, text, x, y, cell, color=(0.88, 0.9, 0.94), spacing=1.0, gap=0.0, **kw):
    """Blocky stencil letters: each glyph pixel becomes a ``cell`` px square, with a ``gap`` px stencil bridge removed."""
    cx = x
    for ch in text:
        g = GLYPHS.get(ch)
        if g is None:
            cx += cell * 3
            continue
        for ry, row in enumerate(g):
            for rx, v in enumerate(row):
                if v == "1":
                    L.rect(cx + rx * cell, y + ry * cell, cx + (rx + 1) * cell - gap, y + (ry + 1) * cell - gap, albedo=color, metal=0.0, smooth=0.45, **kw)
        cx += cell * (6 * spacing)
    return cx


def piece_label_code(density=48):
    """Hull code stencilled in cold white on a black plate: 'EC-114'."""
    rng = rngf(50)
    w_m, h_m = 12.0, 4.0
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.2, 0.2, 0.22), metal=0.0, smooth=0.4, ao=0.9)
    L.alb[:] = np.array([0.285, 0.288, 0.30])
    L.tint_noise(spectral_noise(h, w, rng, beta=2.0, fmin=2), 0.05)
    L.hgt += 0.4 * spectral_noise(h, w, rng, beta=1.0, fmin=30)
    text = "EC-114"
    cell = h * 0.62 / 7
    total = cell * (6 * len(text) - 1)
    x0 = (w - total) / 2
    y0 = (h - cell * 7) / 2
    # soft grime on the paint under the letters, then the stencil
    stencil(L, text, x0, y0, cell, color=(0.85, 0.88, 0.93), gap=max(1.0, cell * 0.08), height_add=0.6)
    scratches(L, rng, 60, length=(10, 60), width=(0.6, 1.0), angle=0, spread=60, bright=0.55, metal=0.8, gloss=0.1, depth=0.3)
    return L, w_m, h_m


def piece_label_emblem(density=48):
    """Earth's mark: a thin ring, an inner ring, and one vertical bar through both that runs beyond them. Cold white on black."""
    rng = rngf(51)
    m = 8.0
    s = px(m, density)
    L = Layers(s, s, albedo=(0.285, 0.288, 0.30), metal=0.0, smooth=0.4, ao=0.9)
    L.tint_noise(spectral_noise(s, s, rng, beta=2.0, fmin=2), 0.05)
    L.hgt += 0.4 * spectral_noise(s, s, rng, beta=1.0, fmin=30)
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float32)
    cx = cy = s / 2
    r = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    ring = lambda r0, wd: 1 - smoothstep(wd * 0.5 - 0.8, wd * 0.5 + 0.8, np.abs(r - r0))
    mark = np.maximum(ring(s * 0.40, s * 0.035), ring(s * 0.27, s * 0.018))
    bar = (1 - smoothstep(s * 0.012, s * 0.012 + 1.6, np.abs(xx - cx))) * (1 - smoothstep(s * 0.46, s * 0.46 + 1.6, np.abs(yy - cy)))
    cross = (1 - smoothstep(s * 0.012, s * 0.012 + 1.6, np.abs(yy - cy))) * (1 - smoothstep(s * 0.27, s * 0.27 + 1.6, np.abs(xx - cx))) * 0.0
    m_ = np.clip(np.maximum(mark, np.maximum(bar, cross)), 0, 1)
    L.alb = L.alb * (1 - m_[:, :, None]) + np.array([0.88, 0.9, 0.95]) * m_[:, :, None]
    L.smo = L.smo * (1 - m_) + 0.5 * m_
    L.hgt += 0.8 * m_
    return L, m, m


def piece_code(text, seed, w_m=3.2, h_m=1.1, density=48):
    """A small stencilled panel code (A-14 ...) in cold white on the armour paint, on a slightly raised plate."""
    rng = rngf(seed)
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.23, 0.232, 0.245), metal=0.0, smooth=0.4, ao=0.9)
    L.tint_noise(spectral_noise(h, w, rng, beta=2.0, fmin=2), 0.05)
    L.hgt += 0.4 * spectral_noise(h, w, rng, beta=1.0, fmin=30)
    cell = h * 0.56 / 7
    total = cell * (6 * len(text) - 1)
    stencil(L, text, (w - total) / 2, (h - cell * 7) / 2, cell, color=(0.80, 0.83, 0.88), gap=max(0.8, cell * 0.08), height_add=0.5)
    L.rect(2, 2, w - 2, 4, albedo=(0.5, 0.5, 0.54), height=1.0)
    L.rect(2, h - 4, w - 2, h - 2, albedo=(0.5, 0.5, 0.54), height=1.0)
    return L, w_m, h_m


def piece_chevrons(w_m=6.0, h_m=1.2, density=48):
    """A band of cold white chevrons on dark paint (Earth's warning band: precise, never hazard yellow)."""
    rng = rngf(58)
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.20, 0.202, 0.215), metal=0.0, smooth=0.4, ao=0.9)
    L.tint_noise(spectral_noise(h, w, rng, beta=2.0, fmin=2), 0.05)
    n = int(w_m / 0.6)
    for i in range(n):
        x = (i + 0.5) * w / n
        for dx0, dx1 in ((0, 0.28), (0.28, 0.0)):
            pass
        L.line((x - h * 0.28, h * 0.12), (x + h * 0.16, h * 0.5), 4.0, albedo=(0.78, 0.80, 0.85))
        L.line((x + h * 0.16, h * 0.5), (x - h * 0.28, h * 0.88), 4.0, albedo=(0.78, 0.80, 0.85))
    L.rect(0, 0, w, 3, albedo=(0.5, 0.5, 0.54)); L.rect(0, h - 3, w, h, albedo=(0.5, 0.5, 0.54))
    return L, w_m, h_m


def piece_panel_big(w_m=6.0, h_m=4.0, density=48):
    """A large inset access panel: a raised frame, a recessed field with seams and bolt rows and one stencil line."""
    rng = rngf(59)
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.26, 0.262, 0.275), metal=0.0, smooth=0.4, ao=0.8)
    L.tint_noise(spectral_noise(h, w, rng, beta=2.0, fmin=2), 0.06)
    L.hgt += 0.4 * spectral_noise(h, w, rng, beta=1.0, fmin=30)
    L.bevel_rect(4, 4, w - 4, h - 4, 1.6, 5.0, albedo=(0.30, 0.302, 0.315))
    L.bevel_rect(16, 16, w - 16, h - 16, -2.4, 4.0, albedo=(0.17, 0.172, 0.185), ao=0.8)
    for x in np.linspace(26, w - 26, int(w_m * 2.2)):
        for y in (10, h - 10):
            L.rect(x - 2, y - 2, x + 2, y + 2, albedo=(0.55, 0.56, 0.6), metal=1.0, height=1.4, smooth=0.6)
    L.rect(w * 0.5 - 1, 18, w * 0.5 + 1, h - 18, albedo=(0.08, 0.08, 0.09), height=-1.5)
    cell = h * 0.14 / 7
    stencil(L, "A-27", w * 0.12, h * 0.22, cell, color=(0.74, 0.77, 0.82), gap=0.6)
    return L, w_m, h_m


def piece_label_stripe(density=48):
    """A cold white band with a thinner one beside it, for edges of decks and the flanks of gun galleries."""
    rng = rngf(52)
    w_m, h_m = 12.0, 1.2
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.285, 0.288, 0.30), metal=0.0, smooth=0.4, ao=0.9)
    L.tint_noise(spectral_noise(h, w, rng, beta=2.0, fmin=2), 0.05)
    L.rect(0, h * 0.14, w, h * 0.46, albedo=(0.86, 0.88, 0.93), smooth=0.5)
    L.rect(0, h * 0.58, w, h * 0.70, albedo=(0.86, 0.88, 0.93), smooth=0.5)
    L.rect(0, h * 0.82, w, h * 0.86, albedo=(0.86, 0.88, 0.93), smooth=0.5)
    scratches(L, rng, 40, length=(20, 120), width=(0.6, 1.0), angle=0, spread=20, bright=0.5, metal=0.8, gloss=0.1, depth=0.3)
    return L, w_m, h_m


def piece_sensor_grid(m=3.0, density=96, seed=53):
    rng = rngf(seed)
    s = px(m, density)
    L = Layers(s, s, albedo=(0.2, 0.2, 0.22), metal=0.3, smooth=0.5, ao=0.7)
    _dark_metal_base(L, rng, 0.2)
    n = 9
    cell = (s - 16) / n
    for iy in range(n):
        for ix in range(n):
            x0, y0 = 8 + ix * cell, 8 + iy * cell
            tone = 0.04 + 0.025 * rng.random()
            L.bevel_rect(x0 + 2, y0 + 2, x0 + cell - 2, y0 + cell - 2, -1.5, 1.5, albedo=(tone, tone * 1.1, tone * 1.5), metal=0.25, smooth=0.9, ao=1.0)
            L.rect(x0 + cell * 0.4, y0 + cell * 0.4, x0 + cell * 0.6, y0 + cell * 0.6, albedo=(0.5, 0.55, 0.65), metal=0.8, smooth=0.8)
    L.rect(0, 0, s, 5, albedo=(0.75, 0.77, 0.8), metal=1, smooth=0.7, height=2)
    L.rect(0, s - 5, s, s, albedo=(0.75, 0.77, 0.8), metal=1, smooth=0.7, height=2)
    L.rect(0, 0, 5, s, albedo=(0.75, 0.77, 0.8), metal=1, smooth=0.7, height=2)
    L.rect(s - 5, 0, s, s, albedo=(0.75, 0.77, 0.8), metal=1, smooth=0.7, height=2)
    return L, m, m


def piece_heat_tiles(m=4.0, density=64, seed=54):
    rng = rngf(seed)
    s = px(m, density)
    L = Layers(s, s, albedo=(0.2, 0.2, 0.22), metal=0.1, smooth=0.35, ao=0.8)
    n = 8
    cell = s / n
    for iy in range(n):
        for ix in range(n):
            tone = 0.11 + 0.035 * rng.random()
            L.bevel_rect(ix * cell + 2, iy * cell + 2, (ix + 1) * cell - 2, (iy + 1) * cell - 2, 2.0, 3.0, albedo=(tone, tone, tone * 1.05), metal=0.1, smooth=0.3 + 0.1 * rng.random())
            L.rect(ix * cell, iy * cell, (ix + 1) * cell, iy * cell + 2, albedo=(0.03, 0.03, 0.035), ao=0.2, height=-2.0)
            L.rect(ix * cell, iy * cell, ix * cell + 2, (iy + 1) * cell, albedo=(0.03, 0.03, 0.035), ao=0.2, height=-2.0)
    L.tint_noise(spectral_noise(s, s, rng, beta=2.0, fmin=2), 0.12)
    return L, m, m


def piece_radiator(w_m=8.0, h_m=4.0, density=48, seed=55):
    """A radiator vane face: chrome heat pipes between dark fins."""
    rng = rngf(seed)
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.2, 0.2, 0.22), metal=0.2, smooth=0.35, ao=0.8)
    _dark_metal_base(L, rng, 0.2)
    pipes = 16
    for i in range(pipes):
        y0 = h * i / pipes
        y1 = h * (i + 1) / pipes
        # pipe: chrome tube near the lower edge of each cell
        py0, py1 = y0 + (y1 - y0) * 0.1, y0 + (y1 - y0) * 0.38
        ys, xs, c = cover(h, w, 0, py0, w, py1)
        gy = (np.arange(ys.start, ys.stop) + 0.5)[:, None]
        t = (gy - (py0 + py1) / 2) / ((py1 - py0) / 2)
        rh = np.sqrt(np.clip(1 - t * t, 0, 1)) * 5.0
        L.paint((ys, xs), c, albedo=(0.55, 0.57, 0.6), metal=1.0, smooth=0.7, height=np.broadcast_to(rh, (ys.stop - ys.start, w)))
        # fin: dark ribbed surface with panel variation
        tone = 0.16 + 0.05 * rng.random()
        L.rect(0, py1, w, y1, albedo=(tone, tone, tone * 1.06), metal=0.4, smooth=0.38)
    for k in range(int(w_m / 2.0)):
        x = (k + 0.5) * w / int(w_m / 2.0)
        L.bevel_rect(x - 3, 0, x + 3, h, 3.0, 2.0, albedo=(0.72, 0.74, 0.78), metal=1.0, smooth=0.65)
    L.tint_noise(spectral_noise(h, w, rng, beta=2.0, fmin=3), 0.06)
    return L, w_m, h_m


def piece_light_bar(w_m=8.0, density=PX):
    rng = rngf(56)
    h_m = 0.25
    w, h = px(w_m, density), px(h_m, density)
    L = Layers(w, h, albedo=(0.18, 0.18, 0.2), metal=0.5, smooth=0.4, ao=0.8)
    _dark_metal_base(L, rng, 0.2)
    L.bevel_rect(2, h * 0.2, w - 2, h * 0.8, -2.0, 1.5, albedo=(0.5, 0.55, 0.6), smooth=0.8)
    L.rect(2, h * 0.28, w - 2, h * 0.72, emis=(0.85, 0.93, 1.0))
    n = int(w_m / 0.5)
    for i in range(n):
        L.rect((i + 0.45) * w / n, h * 0.2, (i + 0.55) * w / n, h * 0.8, albedo=(0.1, 0.1, 0.11), emis=(0.45, 0.5, 0.55))
    return L, w_m, h_m


def piece_burnt(m=4.0, density=64, seed=57):
    """Scorched, torn metal: charcoal soot, a few raw bright gashes and a sparse web of glowing cracks. For wrecks and breaches."""
    rng = rngf(seed)
    s = px(m, density)
    L = Layers(s, s, albedo=(0.085, 0.082, 0.08), metal=0.35, smooth=0.25, ao=0.55)
    L.tint_noise(spectral_noise(s, s, rng, beta=2.0, fmin=2), 0.30)
    L.hgt += 1.8 * spectral_noise(s, s, rng, beta=1.6, fmin=6, fmax=80)
    gash = soft_mask_noise(L, rng, 2.2, 1.1, 1.8, fmin=4, fmax=40)
    L.alb = L.alb * (1 - gash[:, :, None] * 0.8) + np.array([0.55, 0.55, 0.57]) * gash[:, :, None] * 0.8
    L.met = np.clip(L.met + gash * 0.55, 0, 1)
    L.smo += gash * 0.3
    cr = np.abs(spectral_noise(s, s, rng, beta=2.0, fmin=6, fmax=60))
    crack = 1 - smoothstep(0.0, 0.035, cr)
    crack *= smoothstep(0.2, 1.4, spectral_noise(s, s, rng, beta=2.5, fmax=6))
    L.em = np.stack([crack * 1.0, crack * 0.30, crack * 0.05], axis=2)
    L.alb *= (1 - 0.8 * crack)[:, :, None]
    L.hgt -= 2.0 * crack
    return L, m, m


# ------------------------------------------------------------------------------------------------ the trim atlas

def make_trim_atlas(size=2048):
    A = Atlas(size, size, pad=8, albedo=(0.2, 0.2, 0.22))
    info = {}

    def add(name, L, w_m, h_m, strength=1.0, tile_u=False):
        A.place(name, L, w_m, h_m, normal_strength=strength, tile_u=tile_u)
        log(f"  trim {name}: {L.w}x{L.h}px, {w_m:g} x {h_m:g} m")

    # longest strips first (shelf packing)
    L, w_m, h_m = piece_windows("windows_a", 12.0, 1, 0.55, 0.62, 0.95, 7, seed=40)
    add("windows_a", L, w_m, h_m, 1.0, True)
    L, w_m, h_m = piece_windows("windows_b", 12.0, 2, 0.8, 0.5, 1.5, 6, seed=41, off_rate=0.14)
    add("windows_b", L, w_m, h_m, 1.0, True)
    L, w_m, h_m = piece_windows_slit(12.0)
    add("windows_slit", L, w_m, h_m, 1.0, True)
    L, w_m, h_m = piece_light_bar(8.0)
    add("light_bar", L, w_m, h_m, 1.0, True)
    L, w_m, h_m = piece_fasteners(8.0)
    add("fasteners", L, w_m, h_m, 1.0, True)
    L, w_m, h_m = piece_conduit(6.0)
    add("conduit", L, w_m, h_m, 1.0, True)
    L = piece_edge_paint(8.0, 0.25)
    add("edge_paint", L, 8.0, 0.25, 1.0, True)
    L = piece_chrome_brushed(8.0, 2.0)
    add("chrome_brushed", L, 8.0, 2.0, 1.0, True)
    L, w_m, h_m = piece_label_stripe()
    add("label_stripe", L, w_m, h_m, 1.0, True)
    L, w_m, h_m = piece_label_code()
    add("label_code", L, w_m, h_m)
    L, w_m, h_m = piece_windows_big()
    add("windows_big", L, w_m, h_m)
    L, w_m, h_m = piece_radiator()
    add("radiator", L, w_m, h_m)
    L, w_m, h_m = piece_label_emblem()
    add("label_emblem", L, w_m, h_m)
    add("chrome_panel", piece_chrome_panel(), 4.0, 4.0)
    L, w_m, h_m = piece_heat_tiles()
    add("heat_tiles", L, w_m, h_m)
    L, w_m, h_m = piece_burnt()
    add("burnt", L, w_m, h_m)
    L, w_m, h_m = piece_vent()
    add("vent", L, w_m, h_m)
    L, w_m, h_m = piece_sensor_grid()
    add("sensor_grid", L, w_m, h_m)
    L, w_m, h_m = piece_hatch("hatch_a", 2.0, seed=47)
    add("hatch_a", L, w_m, h_m)
    L, w_m, h_m = piece_hatch("hatch_b", 2.0, seed=48, hazard=True)
    add("hatch_b", L, w_m, h_m)
    L, w_m, h_m = piece_vent(6.0, 3.0, 48)
    add("vent_big", L, w_m, h_m)
    for nm, text, sd in (("code_a", "A-14", 81), ("code_b", "D-07", 82), ("code_c", "E-23", 83), ("code_d", "T-58", 84)):
        L, w_m, h_m = piece_code(text, sd)
        add(nm, L, w_m, h_m)
    L, w_m, h_m = piece_chevrons()
    add("chevrons", L, w_m, h_m)
    L, w_m, h_m = piece_panel_big()
    add("panel_big", L, w_m, h_m)
    add("underlayer", piece_underlayer(), 0.5, 0.5)
    return A


# ------------------------------------------------------------------------------------------------ wear overlays

WEAR_PPM = 56


def _wear_piece(kind, w_m, h_m, seed):
    """-> (rgb (h,w,3), alpha (h,w)) for one overlay. Long axis along x (laid along the ship)."""
    rng = rngf(seed)
    w, h = int(w_m * WEAR_PPM), int(h_m * WEAR_PPM)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    u, v = xx / w, yy / h
    nx = spectral_noise(h, w, rng, beta=2.4, fmin=2, fmax=14)
    nf = 0.45 * spectral_noise(h, w, rng, beta=1.8, fmin=8, fmax=60)
    if kind in ("streak", "streak_light"):     # grime (or pale dust) streaks that run along the ship: several thin ones of different strength and length
        a = np.zeros((h, w), np.float32)
        for _ in range(int(rng.integers(3, 7))):
            yc = rng.uniform(0.15, 0.85)
            sig = rng.uniform(0.04, 0.16)
            x0, x1 = sorted(rng.uniform(0.0, 1.0, 2))
            x1 = max(x1, x0 + 0.35)
            along = smoothstep(x0 - 0.05, x0 + 0.12, u) * (1 - smoothstep(x1 - 0.2, x1 + 0.05, u))
            a += rng.uniform(0.25, 0.7) * np.exp(-((v - yc) / sig) ** 2) * along
        a *= np.clip(0.65 + 0.45 * nx, 0, 1.2) * np.clip(0.8 + 0.4 * nf, 0, 1.2)
        rgb = np.array([0.020, 0.020, 0.024], np.float32) if kind == "streak" else np.array([0.34, 0.35, 0.38], np.float32)
        a = np.clip(a, 0, 0.78 if kind == "streak" else 0.55)
    elif kind == "scorch":                     # a soft burn: dark core, ragged edge
        r = np.sqrt(((u - 0.5) / 0.5) ** 2 + ((v - 0.5) / 0.5) ** 2) * (1.0 + 0.30 * nx)
        a = np.clip(1 - r, 0, 1) ** 1.3 * np.clip(0.7 + 0.5 * nf, 0, 1.2)
        rgb = np.array([0.018, 0.017, 0.018], np.float32)
        a = np.clip(a * 0.9, 0, 0.82)
    elif kind == "scuff":                      # paint worn off along an edge: pale scratches toward the low side of the strip
        a = np.zeros((h, w), np.float32)
        for _ in range(int(w_m * 16)):
            x0 = rng.uniform(0, w); y0 = rng.uniform(0, h * 0.55); ln = rng.uniform(w * 0.02, w * 0.25)
            y_ = int(y0)
            xs_ = slice(int(x0), int(min(x0 + ln, w)))
            a[y_:y_ + 1, xs_] = np.maximum(a[y_:y_ + 1, xs_], rng.uniform(0.3, 0.9))
        a = blur(a, 0.6)
        a *= np.clip(1.3 - v * 1.6, 0, 1) * np.clip(0.6 + 0.6 * nf, 0, 1.4)
        a = np.clip(a * 1.3, 0, 0.9)
        rgb = np.array([0.46, 0.47, 0.50], np.float32)
    elif kind in ("patch_light", "patch_dark"):
        e = np.minimum(np.minimum(u, 1 - u) * w_m, np.minimum(v, 1 - v) * h_m)
        a = smoothstep(0.02, 0.10 + 0.04 * (nx + 1), e) * (0.55 + 0.10 * nf)
        rgb = np.array([0.42, 0.43, 0.47], np.float32) if kind == "patch_light" else np.array([0.012, 0.012, 0.014], np.float32)
        a = np.clip(a, 0, 0.62)
    elif kind == "stain":                      # a coolant or oil blot, elongated along the ship, with a darker rim
        r = np.sqrt(((u - 0.45) / 0.45) ** 2 + ((v - 0.5) / 0.5) ** 2) * (1.0 + 0.35 * nx)
        a = np.clip(1.15 - r, 0, 1) ** 0.8 * (0.45 + 0.35 * np.clip(r, 0, 1)) * np.clip(0.7 + 0.5 * nf, 0, 1.2)
        rgb = np.array([0.035, 0.030, 0.026], np.float32)
        a = np.clip(a, 0, 0.7)
    elif kind == "heat":                       # engine heat: a blued, bronzed tint fading away from the engine end (the left of the piece)
        a = (1 - smoothstep(0.0, 1.0, u)) ** 1.2 * np.clip(0.85 + 0.25 * nx, 0.3, 1.2) * np.exp(-((v - 0.5) / 0.55) ** 2)
        rgb = np.array([0.11, 0.09, 0.21], np.float32)
        a = np.clip(a * 0.8, 0, 0.75)
    else:
        raise ValueError(kind)
    a = blur(a.astype(np.float32), 0.9)
    rgbf = np.broadcast_to(rgb, (h, w, 3)).copy()
    if kind == "heat":                         # bronze in the core, blue at the edge
        mix = np.clip(a / 0.7, 0, 1)[..., None]
        rgbf = rgbf * (1 - mix) + np.array([0.20, 0.13, 0.08], np.float32) * mix
    return rgbf, a


def make_wear_atlas(size=2048):
    pieces = [("streak_%d" % i, "streak", 9.0, 1.5, 300 + i) for i in range(6)]
    pieces += [("streak_light_%d" % i, "streak_light", 9.0, 1.5, 370 + i) for i in range(4)]
    pieces += [("scorch_%d" % i, "scorch", 4.0, 4.0, 310 + i) for i in range(4)]
    pieces += [("scuff_%d" % i, "scuff", 7.0, 0.9, 320 + i) for i in range(4)]
    pieces += [("patch_light_%d" % i, "patch_light", 5.0, 3.0, 330 + i) for i in range(3)]
    pieces += [("patch_dark_%d" % i, "patch_dark", 5.0, 3.0, 340 + i) for i in range(3)]
    pieces += [("stain_%d" % i, "stain", 6.0, 3.0, 350 + i) for i in range(3)]
    pieces += [("heat_%d" % i, "heat", 10.0, 6.0, 360 + i) for i in range(2)]
    img = np.zeros((size, size, 4), np.float32)
    pad = 6
    x = y = pad
    row = 0
    items = {}
    for name, kind, w_m, h_m, sd in pieces:
        rgb, a = _wear_piece(kind, w_m, h_m, sd)
        h, w = a.shape
        if x + w + pad > size:
            x = pad
            y += row + 2 * pad
            row = 0
        if y + h + pad > size:
            raise RuntimeError("wear atlas full at " + name)
        img[y:y + h, x:x + w, :3] = rgb
        img[y:y + h, x:x + w, 3] = a
        # replicate the edge into the pad so filtering and mips do not pull a neighbour in
        img[y - pad:y, x:x + w] = img[y:y + 1, x:x + w]
        img[y + h:y + h + pad, x:x + w] = img[y + h - 1:y + h, x:x + w]
        img[y - pad:y + h + pad, x - pad:x] = img[y - pad:y + h + pad, x:x + 1]
        img[y - pad:y + h + pad, x + w:x + w + pad] = img[y - pad:y + h + pad, x + w - 1:x + w]
        items[name] = {"uv": [(x + 0.5) / size, 1 - (y + h - 0.5) / size, (x + w - 0.5) / size, 1 - (y + 0.5) / size], "metres": [w_m, h_m], "kind": kind}
        x += w + 2 * pad
        row = max(row, h)
    rgb8 = mntex.to_u8(mntex.srgb_encode(img[:, :, :3]))
    a8 = mntex.to_u8(img[:, :, 3:4])
    log(f"  wear atlas: {len(pieces)} pieces, rows to y={y + row}")
    return np.concatenate([rgb8, a8], axis=2), items


# ------------------------------------------------------------------------------------------------ engine and lights

def make_engine_emissive(size=256):
    """Top half: the bell's inside (u around, v from the throat at row 0 to the lip): a dull bronze ember close to the throat and dark beyond, so the
    bell reads as a deep funnel. Bottom half: the combustion disc, seen end-on: a flat hot white-blue disc with a hard edge and a saturated blue rim."""
    rng = rngf(61)
    em = np.zeros((size, size, 3), np.float32)
    half = size // 2
    t = np.clip((np.arange(half)[:, None] + 0.5) / half, 0, 1)
    ember = np.array([0.55, 0.22, 0.08], np.float32); blue = np.array([0.20, 0.34, 0.9], np.float32)
    near = np.exp(-t * 9.0)
    col = (ember * (1 - np.clip(t / 0.08, 0, 1))[..., None] + blue * np.clip(t / 0.08, 0, 1)[..., None] * 0.25)
    streak = 1.0 + 0.35 * spectral_noise(half, size, rng, beta=2.0, fmin=2, sy=0.2)
    em[:half] = col * (near * 0.10 + 0.004)[..., None] * streak[..., None]
    yy, xx = np.mgrid[0:half, 0:size].astype(np.float32)
    r = np.sqrt(((xx - size / 2) / (size / 2)) ** 2 + ((yy - half / 2) / (half / 2)) ** 2)
    hard = 1 - smoothstep(0.80, 0.84, r)
    rim = smoothstep(0.55, 0.80, r)
    hot = np.array([1.0, 0.97, 0.93], np.float32)
    disc = hot[None, None, :] * (1 - rim)[..., None] + np.array([0.35, 0.58, 1.0], np.float32)[None, None, :] * rim[..., None]
    disc *= (1.0 + 0.10 * spectral_noise(half, size, rng, beta=1.5, fmin=3))[..., None]
    em[half:] = disc * hard[..., None]
    return mntex.to_u8(mntex.srgb_encode(np.clip(em, 0, 1)))


def make_plume_emissive(w=256, h=256):
    """Plume: four 64 px columns, each one nested additive shell (0 faint outer violet, 1 blue, 2 blue-white, 3 white core), u around the shell and v from
    the nozzle (row 0) outward. Short and hard: it falls off fast, the core carries shock diamonds, every shell is streaked and tears at its end."""
    rng = rngf(62)
    t = (np.arange(h)[:, None] + 0.5) / h
    out = np.zeros((h, w, 3), np.float32)
    cols = [np.array([0.36, 0.34, 0.95]), np.array([0.30, 0.52, 1.0]), np.array([0.62, 0.80, 1.0]), np.array([1.0, 0.97, 0.94])]
    fall_rate = (3.2, 4.2, 5.2, 6.5)
    strength = (0.06, 0.20, 0.62, 1.0)
    for k in range(4):
        streak = 1.0 + 0.30 * spectral_noise(h, 64, rng, beta=2.2, fmin=2, sy=0.12)
        tear = smoothstep(0.55 + 0.1 * spectral_noise(h, 64, rng, beta=2.0, fmin=1, fmax=6), 1.0, t)       # the end of the shell breaks up
        fall = np.exp(-t * fall_rate[k]) * (1 - np.clip(tear, 0, 1))
        diamonds = 1.0
        if k >= 2:
            diamonds = 0.62 + 0.38 * np.cos(np.pi * (t * (9 if k == 2 else 7))) ** 2 * np.exp(-t * 2.2)
        c = cols[k][None, None, :] * (fall * strength[k] * diamonds)[..., None] * streak[..., None]
        out[:, k * 64:(k + 1) * 64] = c
    return mntex.to_u8(mntex.srgb_encode(np.clip(out, 0, 1)))


def make_lights_emissive():
    """A palette: 8 swatches, 16 px each in a 128 x 16 strip (white, cold white, red, green, amber, blue, orange ember, violet)."""
    cols = [(1.0, 1.0, 1.0), (0.80, 0.90, 1.0), (1.0, 0.07, 0.05), (0.07, 1.0, 0.22), (1.0, 0.62, 0.18), (0.25, 0.45, 1.0), (1.0, 0.36, 0.08), (0.6, 0.35, 1.0)]
    out = np.zeros((16, 128, 3), np.float32)
    for i, c in enumerate(cols):
        out[:, i * 16:(i + 1) * 16] = c
    return mntex.to_u8(mntex.srgb_encode(out)), cols


# ------------------------------------------------------------------------------------------------ damage decals

def make_damage_atlas(size=2048):
    """4 x 4 cells of 512: rows are small scorches, blasts, craters, streaks; each row has four variants. Albedo carries alpha (the
    decal's coverage); the mask map's B (opacity) follows it; the emissive layer holds glowing embers in craters and blasts."""
    cell = size // 4
    alb = np.zeros((size, size, 4), np.float32)
    hgt = np.zeros((size, size), np.float32)
    met = np.zeros((size, size), np.float32)
    smo = np.full((size, size), 0.2, np.float32)
    ao = np.ones((size, size), np.float32)
    emi = np.zeros((size, size, 3), np.float32)
    yy, xx = np.mgrid[0:cell, 0:cell].astype(np.float32)
    u = (xx - cell / 2) / (cell / 2)
    v = (yy - cell / 2) / (cell / 2)
    r = np.sqrt(u * u + v * v)
    ang = np.arctan2(v, u)

    def cell_noise(rng, beta, fmin=0, fmax=None):
        return spectral_noise(cell, cell, rng, beta=beta, fmin=fmin, fmax=fmax)

    for row in range(4):
        for col in range(4):
            rng = rngf(900 + row * 10 + col)
            cov = np.zeros((cell, cell), np.float32)
            relief = np.zeros((cell, cell), np.float32)
            ember = np.zeros((cell, cell), np.float32)
            bare = np.zeros((cell, cell), np.float32)
            wob = 1.0 + 0.28 * np.sin(ang * rng.integers(3, 7) + rng.uniform(0, 6)) + 0.18 * cell_noise(rng, 1.5, 2, 24)
            if row == 0:         # small scorch: soot fading outward with a few radial flecks
                rad = 0.55 * wob * (0.8 + 0.3 * rng.random())
                cov = np.clip(1 - r / rad, 0, 1) ** 0.9
                cov *= np.clip(0.7 + 0.5 * cell_noise(rng, 1.4, 2, 14), 0, 1.2)
                relief = -0.8 * np.clip(1 - r / (rad * 0.4), 0, 1)
            elif row == 1:       # blast scorch: wide, streaked outward, pitted
                rad = 0.8 * wob
                streaks = 0.5 + 0.5 * np.sin(ang * 14 + cell_noise(rng, 1.0, 3, 20) * 2)
                cov = np.clip(1 - r / rad, 0, 1) ** 0.7 * (0.55 + 0.45 * streaks)
                cov *= np.clip(0.65 + 0.5 * cell_noise(rng, 1.4, 2, 16), 0, 1.2)
                pits = smoothstep(1.2, 1.7, cell_noise(rng, 0.5, 40)) * (r < rad)
                relief = -2.5 * pits - 1.0 * np.clip(1 - r / (rad * 0.3), 0, 1)
                ember = smoothstep(1.5, 2.2, cell_noise(rng, 1.0, 20)) * (r < rad * 0.45) * 0.5
            elif row == 2:       # crater: a torn pit with a raised, bright rim and glowing cracks
                rad = 0.62 * wob
                inner = np.clip(1 - r / rad, 0, 1)
                rim = np.exp(-((r - rad * 0.98) / (rad * 0.14)) ** 2)
                cov = np.clip(inner * 4 + rim * 1.2 + 0.0, 0, 1) * np.clip(1.15 - r / (rad * 1.5), 0, 1)
                cov = np.maximum(cov, np.clip(1 - r / (rad * 1.6), 0, 1) ** 1.2 * 0.7)
                relief = -9.0 * smoothstep(0.0, 0.9, inner) + 5.0 * rim + 1.5 * cell_noise(rng, 1.5, 6, 50) * inner
                crackn = np.abs(cell_noise(rng, 2.0, 3, 22))
                crack = blur((1 - smoothstep(0.0, 0.16, crackn)).astype(np.float32), 1.6) * smoothstep(0.1, 0.6, inner)
                ember = crack * 1.0 + 0.35 * np.clip(inner - 0.6, 0, 1)
                bare = rim * 0.9
            else:                # streak: a long soot trail from a small impact at one end
                along = (u * math.cos(0.4 * (col - 1.5)) + v * math.sin(0.4 * (col - 1.5)))
                across = (-u * math.sin(0.4 * (col - 1.5)) + v * math.cos(0.4 * (col - 1.5)))
                head = np.exp(-((along + 0.62) ** 2 + across ** 2) / 0.03)
                tail = np.exp(-(across / (0.10 + 0.12 * np.clip(along + 0.7, 0, 1.6))) ** 2) * np.clip((along + 0.62) / 1.5, 0, 1) ** 0.5 * np.clip(1 - (along + 0.62) / 1.3, 0, 1)
                cov = np.clip(head * 1.6 + tail * 0.9, 0, 1) * np.clip(0.6 + 0.7 * cell_noise(rng, 1.5, 4, 70), 0.2, 1.2)
                relief = -2.0 * head
                ember = head * 0.5
            cov = np.clip(cov, 0, 1)
            cov *= 1.0 - smoothstep(0.82, 0.99, np.maximum(np.abs(u), np.abs(v)))             # fade to nothing at the cell's border
            sy, sx = row * cell, col * cell
            # blistered ash round the edge (pale on dark paint, so a scorch reads), a charred dark core, bare bright metal on rims
            core = smoothstep(0.55, 0.95, cov)
            ash = 0.50 + 0.07 * cell_noise(rng, 1.4, 2, 12)
            soot = np.clip(ash * (1.0 - 0.78 * core) + 0.30 * bare, 0.03, 0.75)
            alb[sy:sy + cell, sx:sx + cell, 0] = soot * 1.12
            alb[sy:sy + cell, sx:sx + cell, 1] = soot
            alb[sy:sy + cell, sx:sx + cell, 2] = soot * 0.9
            alb[sy:sy + cell, sx:sx + cell, 3] = np.clip(cov * 1.1, 0, 1)
            hgt[sy:sy + cell, sx:sx + cell] = relief
            met[sy:sy + cell, sx:sx + cell] = np.clip(0.25 + 0.7 * bare, 0, 1)
            smo[sy:sy + cell, sx:sx + cell] = np.clip(0.14 + 0.35 * bare, 0.05, 0.8)
            ao[sy:sy + cell, sx:sx + cell] = np.clip(1 - 0.7 * np.clip(-relief / 8, 0, 1), 0.3, 1)
            e = np.clip(blur(ember.astype(np.float32), 1.4), 0, 1.5)
            emi[sy:sy + cell, sx:sx + cell, 0] = e
            emi[sy:sy + cell, sx:sx + cell, 1] = e * 0.36
            emi[sy:sy + cell, sx:sx + cell, 2] = e * 0.07
    nrm = mntex.normal_from_height(hgt, 1.1)
    opacity = alb[:, :, 3]
    mask = np.stack([met, ao, opacity, smo], axis=2)
    return {
        "albedo": np.concatenate([mntex.to_u8(alb[:, :, :3]), mntex.to_u8(alb[:, :, 3:4])], axis=2),
        "normal": mntex.to_u8(nrm),
        "mask": mntex.to_u8(mask),
        "emissive": mntex.to_u8(mntex.srgb_encode(emi)),
    }


# ------------------------------------------------------------------------------------------------ write everything

def save(name, arr):
    path = os.path.join(OUT, name + ".png")
    mntex.write_png(path, arr)
    log(f"wrote {name}.png {arr.shape[1]}x{arr.shape[0]}x{arr.shape[2] if arr.ndim == 3 else 1}  {os.path.getsize(path) / 1024:.0f} KB")


def main():
    only = set(a for a in sys.argv[sys.argv.index("--") + 1:]) if "--" in sys.argv else set()

    def want(k):
        return not only or k in only

    if want("plate"):
        for name, tone in PAINT_TONES.items():
            plate = make_plate(tone=tone).finish(normal_strength=0.9)
            suffix = "" if name == "graphite" else "_" + name
            save(f"earth_plate{suffix}_albedo", plate["albedo"])
            if name == "graphite":
                save("earth_plate_normal", plate["normal"])
                save("earth_plate_mask", plate["mask"])
    if want("chrome"):
        ch = make_chrome().finish(normal_strength=0.6)
        for k in ("albedo", "normal", "mask"):
            save(f"earth_chrome_{k}", ch[k])
    if want("detail"):
        save("earth_detail", make_detail())
    if want("trim"):
        atlas = make_trim_atlas()
        out = atlas.finish(normal_strength=1.0)
        for k in ("albedo", "normal", "mask", "emissive"):
            save(f"earth_trim_{k}", out[k])
        old = json.load(open(TRIMS_JSON)) if os.path.exists(TRIMS_JSON) else {}
        data = {"size": [atlas.w, atlas.h], "pieces": atlas.items}
        if "wear" in old:
            data["wear"] = old["wear"]
        with open(TRIMS_JSON, "w") as f:
            json.dump(data, f, indent=1)
        log("wrote trims.json")
    if want("wear"):
        wear, items = make_wear_atlas()
        save("earth_wear_albedo", wear)
        wd = json.load(open(TRIMS_JSON)) if os.path.exists(TRIMS_JSON) else {"pieces": {}}
        wd["wear"] = items
        with open(TRIMS_JSON, "w") as f:
            json.dump(wd, f, indent=1)
        log("wrote wear pieces to trims.json")
    if want("engine"):
        save("earth_engine_emissive", make_engine_emissive())
    if want("plume"):
        save("earth_plume_emissive", make_plume_emissive())
    if want("lights"):
        arr, cols = make_lights_emissive()
        save("earth_lights_emissive", arr)
    if want("damage"):
        dmg = make_damage_atlas()
        for k in ("albedo", "normal", "mask", "emissive"):
            save(f"earth_damage_{k}", dmg[k])
    materials = {
        "format": "monolith-ship-materials/1",
        "about": "HDRP material recipes for Earth's hull set. baseColor and emissiveColor are sRGB display values; emissiveNits scales the emissive map. ShipArtSetup makes the assets.",
        "materials": [
            {"blender": "mn_earth_plate", "name": "MN_EarthPlate", "baseColor": [0.85, 0.85, 0.87], "baseMap": "earth_plate_albedo", "normalMap": "earth_plate_normal", "maskMap": "earth_plate_mask",
             "normalScale": 0.5, "metallicRemap": [0, 1], "smoothnessRemap": [0, 0.75], "aoRemap": [0, 1],
             "detailMap": "earth_detail", "uvDetail": 1, "detailTiling": [1, 1], "detailAlbedo": 0.6, "detailNormal": 0.2, "detailSmoothness": 0.6, "specularAA": True},
            {"blender": "mn_earth_trim", "name": "MN_EarthTrim", "baseColor": [1.0, 1.0, 1.0], "baseMap": "earth_trim_albedo", "normalMap": "earth_trim_normal", "maskMap": "earth_trim_mask",
             "normalScale": 1.0, "metallicRemap": [0, 1], "smoothnessRemap": [0, 1], "aoRemap": [0, 1],
             "detailMap": "earth_detail", "uvDetail": 1, "detailTiling": [1, 1], "detailAlbedo": 0.5, "detailNormal": 0.2, "detailSmoothness": 0.5, "specularAA": True,
             "emissiveMap": "earth_trim_emissive", "emissiveColor": [1.0, 1.0, 1.0], "emissiveNits": 42000},
            {"blender": "mn_earth_lights", "name": "MN_EarthLights", "baseColor": [0.02, 0.02, 0.02], "metallic": 0.0, "smoothness": 0.6,
             "emissiveMap": "earth_lights_emissive", "emissiveColor": [1.0, 1.0, 1.0], "emissiveNits": 150000},
            {"blender": "mn_earth_chrome", "name": "MN_EarthChrome", "baseColor": [1.0, 1.0, 1.0], "baseMap": "earth_chrome_albedo", "normalMap": "earth_chrome_normal", "maskMap": "earth_chrome_mask",
             "normalScale": 1.0, "metallicRemap": [0, 1], "smoothnessRemap": [0, 1.0], "aoRemap": [0, 1], "specularAA": True},
            {"blender": "mn_earth_plume", "name": "MN_EarthPlume", "shader": "HDRP/Unlit", "surface": "additive", "emissiveMap": "earth_plume_emissive",
             "emissiveColor": [1.0, 1.0, 1.0], "emissiveNits": 48000, "noShadows": True},
            {"blender": "mn_earth_engine", "name": "MN_EarthEngine", "baseColor": [0.02, 0.02, 0.02], "metallic": 0.0, "smoothness": 0.4,
             "emissiveMap": "earth_engine_emissive", "emissiveColor": [1.0, 1.0, 1.0], "emissiveNits": 150000},
        ],
        "decal": {"name": "MN_EarthDamageDecal", "baseMap": "earth_damage_albedo", "normalMap": "earth_damage_normal", "maskMap": "earth_damage_mask",
                  "emissiveMap": "earth_damage_emissive", "emissiveNits": 14000, "affectEmission": True},
    }
    plate = materials["materials"][0]
    plate["baseColor"] = [1.0, 1.0, 1.0]
    smooth = {"mn_earth_plate": 0.74, "mn_earth_plate_b": 0.70, "mn_earth_plate_c": 0.70, "mn_earth_plate_d": 0.62, "mn_earth_plate_e": 0.58}
    for blender, name, tex in (("mn_earth_plate_b", "MN_EarthPlateDeep", "earth_plate_deep_albedo"), ("mn_earth_plate_c", "MN_EarthPlateMid", "earth_plate_mid_albedo"),
                               ("mn_earth_plate_d", "MN_EarthPlateLight", "earth_plate_light_albedo"), ("mn_earth_plate_e", "MN_EarthPlatePale", "earth_plate_pale_albedo")):
        v = dict(plate)
        v.update({"blender": blender, "name": name, "baseMap": tex})
        materials["materials"].append(v)
    plate["name"] = "MN_EarthPlate"
    for m in materials["materials"]:
        if m["blender"] in smooth:
            m["smoothnessRemap"] = [0, smooth[m["blender"]]]
    materials["materials"].append({"blender": "mn_earth_wear", "name": "MN_EarthWear", "shader": "HDRP/Lit", "surface": "alpha", "baseColor": [1.0, 1.0, 1.0],
                                   "baseMap": "earth_wear_albedo", "metallic": 0.0, "smoothness": 0.30})
    with open(os.path.join(OUT, "earth_hull.materials.json"), "w") as f:
        json.dump(materials, f, indent=1)
    log("done")


main()
