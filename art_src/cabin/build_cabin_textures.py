"""The cabin's texture set: worn human hull panels, tread plate, dark ribbed biomechanical graft, steel, webbing, a hardware trim sheet, living tissue, glyphs.
Generated, deterministic, no stock images.

    tools\\blender.ps1 art_src\\cabin\\build_cabin_textures.py                  (all; about a minute)
    tools\\blender.ps1 art_src\\cabin\\build_cabin_textures.py -- plate bio       (only these sets)

Writes Game/Assets/Monolith/Art/Cabin/<set>_{albedo,normal,mask[,emissive]}.png (Unity imports them by suffix), cabin.materials.json (the HDRP recipes
CabinArtSetup reads) and art_src/cabin/cabin_trims.json (the trim sheet's rectangles, read by the model script). Mask maps follow HDRP: R metallic,
G occlusion, B detail mask, A smoothness. Pixel density: about 1 mm a pixel on the tiling surfaces (the plate tile is 2 m in 2,048 px).
"""
import json
import math
import os
import sys
import time

import numpy as np

from blender_lib import mntex, mnpaint
from blender_lib.mntex import Layers, Atlas, spectral_noise, blur, smoothstep
from blender_lib.mnpaint import draw_segment, scratches, soft_dot, chips, grime_streaks, disc, ring, rivet, screw, text

ROOT = os.environ.get("MN_ROOT", os.getcwd())
OUT = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "Cabin")
TRIMS_JSON = os.path.join(ROOT, "art_src", "cabin", "cabin_trims.json")
os.makedirs(OUT, exist_ok=True)
T0 = time.time()


def log(msg):
    print(f"[MN] cabin textures {time.time() - T0:5.1f}s  {msg}", flush=True)


def rngf(seed):
    return np.random.default_rng(seed)


def save(name, arrs, keys=("albedo", "normal", "mask", "emissive")):
    for k in keys:
        if k in arrs and arrs[k] is not None and (k != "emissive" or arrs[k].max() > 0):
            mntex.write_png(os.path.join(OUT, f"{name}_{k}.png"), arrs[k])
    log(f"wrote {name}")


def periodic_dist(coord, marks, size):
    """Distance from each coordinate to the nearest mark on a wrapping axis of length ``size``."""
    d = np.abs(coord[..., None] - np.asarray(marks)[None, :])
    d = np.minimum(d, size - d)
    return d.min(axis=-1)


# ------------------------------------------------------------------------------------------------ the wall plate

def make_plate(size=2048, seed=101):
    """Painted steel hull panels, a 2 m tile at 2,048 px: a grid of panels with recessed seams and rows of rivets, a muted grey-olive paint that has been scuffed,
    chipped back to primer and bare steel at the edges, streaked with grime under every fastener, and darkened in the corners and at the foot of every panel.
    Smoothness wanders from panel to panel (repainted ones are glossier) and in scuffs and grime."""
    rng = rngf(seed)
    s = size / 1024.0
    L = Layers(size, size, albedo=(0.33, 0.345, 0.30), metal=0.0, smooth=0.42, ao=1.0)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    # the panel layout: periodic, so the tile repeats cleanly
    cols = np.array([0.0, 0.26, 0.50, 0.77]) * size
    rows = np.array([0.0, 0.36, 0.68]) * size
    dxs = periodic_dist(xx, cols, size)
    dys = periodic_dist(yy, rows, size)
    seam = np.minimum(dxs, dys)
    ix = np.digitize((xx % size), np.sort(cols[1:])) % 4
    iy = np.digitize((yy % size), np.sort(rows[1:])) % 3
    pid = ix * 3 + iy
    tone = 1.0 + rng.normal(0, 0.045, 12)
    sm_off = rng.normal(0, 0.035, 12)
    raise_px = rng.uniform(0.0, 1.6, 12)
    L.alb *= tone[pid][:, :, None]
    L.smo += sm_off[pid]
    L.hgt += raise_px[pid]
    # large and mid scale paint drift, blotches of touched-up paint
    L.tint_noise(spectral_noise(size, size, rng, beta=3.0, fmax=5), 0.07)
    L.tint_noise(spectral_noise(size, size, rng, beta=2.4, fmin=5, fmax=26), 0.05)
    n_blot = spectral_noise(size, size, rng, beta=2.2, fmin=24, fmax=120)
    L.tint_noise(n_blot, 0.04)
    L.tint_noise(spectral_noise(size, size, rng, beta=1.0, fmin=300), 0.02)               # orange peel
    L.smo += 0.03 * spectral_noise(size, size, rng, beta=2.4, fmin=3, fmax=40) + 0.07 * smoothstep(0.6, 1.4, n_blot)
    L.hgt += 0.12 * spectral_noise(size, size, rng, beta=1.0, fmin=300) + 0.15 * n_blot
    # seams: a groove with a soft lip each side
    groove = 1.0 - smoothstep(1.6 * s, 4.8 * s, seam)
    lip = smoothstep(4.8 * s, 12 * s, seam)
    L.hgt += -4.0 * groove + 1.3 * lip
    L.alb *= (1.0 - 0.55 * groove)[:, :, None]
    L.ao *= 1.0 - 0.75 * groove
    L.smo -= 0.18 * groove
    # grime at the foot of each panel and in its corners
    fx = (xx % size)
    fy = np.zeros_like(fx)
    bounds_y = np.r_[np.sort(rows), size]
    for a, b in zip(bounds_y[:-1], bounds_y[1:]):
        m = (yy >= a) & (yy < b)
        fy[m] = ((yy[m] - a) / (b - a))
    foot = smoothstep(0.78, 1.0, fy)
    L.alb *= (1.0 - 0.30 * foot)[:, :, None]
    L.smo -= 0.12 * foot
    corner = np.exp(-(seam / (22 * s)) ** 2)
    L.alb *= (1.0 - 0.14 * corner)[:, :, None]
    # rivets along every seam, both sides, with a little jitter; grime streaks run down from some of them
    pts = []
    pitch = 70 * s
    for x0 in cols:
        for y in np.arange(0, size, pitch):
            for side in (-1, 1):
                pts.append((x0 + side * 15 * s + rng.normal(0, 0.5), y + rng.normal(0, 1.0)))
    for y0 in rows:
        for x in np.arange(0, size, pitch):
            for side in (-1, 1):
                pts.append((x + rng.normal(0, 1.0), y0 + side * 15 * s + rng.normal(0, 0.5)))
    for (x, y) in pts:
        rivet(L, x % size, y % size, 3.4 * s, bright=rng.uniform(0.34, 0.5), rng=rng)
    grime_streaks(L, rng, 150, length=(30 * s, 150 * s), width=(1.2 * s, 4.0 * s), points=[pts[i] for i in rng.choice(len(pts), 150, replace=False)])
    # access screws in the panel corners
    for x0 in cols:
        for y0 in rows:
            for dx, dy in ((26, 26), (-26, 26), (26, -26), (-26, -26)):
                screw(L, (x0 + dx * s) % size, (y0 + dy * s) % size, 3.6 * s, angle=rng.uniform(0, math.pi))
    # wear: chips bunched along the seams, scuffs and scratches everywhere
    near = []
    for _ in range(420):
        if rng.random() < 0.5:
            x = rng.choice(cols) + rng.normal(0, 14 * s); y = rng.uniform(0, size)
        else:
            y = rng.choice(rows) + rng.normal(0, 14 * s); x = rng.uniform(0, size)
        near.append((x % size, y % size))
    chips(L, rng, 420, size=(1.2 * s, 4.2 * s), points=near)
    chips(L, rng, 40, size=(1.5 * s, 5.0 * s))
    scratches(L, rng, 70 * s * s, length=(60 * s, 420 * s), width=(0.6 * s, 1.1 * s), angle=0, spread=12, alpha=(0.1, 0.4))
    scratches(L, rng, 40 * s * s, length=(60 * s, 300 * s), width=(0.6 * s, 1.1 * s), angle=90, spread=12, alpha=(0.1, 0.4))
    scratches(L, rng, 300 * s * s, length=(8 * s, 50 * s), width=(0.5 * s, 0.9 * s), angle=0, spread=180, alpha=(0.08, 0.35), gloss=0.22)
    # soft scuff marks where shoulders and hands rub: pale, glossy, wide
    for _ in range(60):
        x, y = rng.uniform(0, size), rng.uniform(0, size)
        soft_dot(L, x, y, rng.uniform(14, 46) * s, rng.uniform(6, 16) * s, rng.uniform(0.15, 0.4), rng, albedo=(0.5, 0.5, 0.46), smooth=0.62, metal=0.1, height_add=-0.2)
    # specks and pinholes
    for _ in range(int(1200 * s * s)):
        soft_dot(L, rng.uniform(0, size), rng.uniform(0, size), rng.uniform(0.4, 1.0) * s, rng.uniform(0.4, 1.0) * s, rng.uniform(0.2, 0.6), rng, albedo=(0.05, 0.05, 0.05), ao=0.6, height_add=-0.4)
    L.alb = np.clip(L.alb, 0, 1)
    return L


# ------------------------------------------------------------------------------------------------ the floor

def make_floor(size=1024, seed=111):
    """Diamond tread plate, a 1 m tile: raised lugs 24 mm long in crossing pairs on a 32 mm pitch, bright worn tops, dark grease and grit in the valleys, a few oil
    stains, scrapes where boots have dragged."""
    rng = rngf(seed)
    s = size / 1024.0
    L = Layers(size, size, albedo=(0.20, 0.205, 0.21), metal=0.9, smooth=0.38, ao=1.0)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    pitch = 32.0 * s
    ci, cj = np.floor(xx / pitch), np.floor(yy / pitch)
    lx, ly = xx - (ci + 0.5) * pitch, yy - (cj + 0.5) * pitch
    flip = ((ci + cj) % 2 == 0)
    a = np.where(flip, math.radians(45), math.radians(-45))
    ux = lx * np.cos(a) + ly * np.sin(a)
    uy = -lx * np.sin(a) + ly * np.cos(a)
    half_len, half_w = 11.5 * s, 3.2 * s
    dx = np.maximum(np.abs(ux) - (half_len - half_w), 0.0)
    d = np.sqrt(dx * dx + uy * uy) - half_w
    lug = 1.0 - smoothstep(-1.2 * s, 1.2 * s, d)
    L.hgt += 3.2 * s * lug * (0.75 + 0.25 * smoothstep(-half_w, 0, -d))
    n_wear = spectral_noise(size, size, rng, beta=2.0, fmin=3, fmax=60)
    # lug tops: bright, worn bare steel where traffic is; valleys: dark grime
    worn = smoothstep(0.1, 1.2, n_wear + 0.4) * lug
    L.alb = L.alb * (1 - lug[:, :, None] * 0.0)
    L.alb += (lug * (0.16 + 0.25 * worn))[:, :, None]
    L.alb *= (1.0 - 0.45 * (1.0 - lug))[:, :, None]
    L.smo = 0.30 + 0.35 * lug * (0.5 + 0.5 * worn) - 0.1 * (1.0 - lug)
    L.ao = 1.0 - 0.6 * (1.0 - lug)
    L.tint_noise(spectral_noise(size, size, rng, beta=2.4, fmin=2, fmax=30), 0.12)
    for _ in range(int(14 * s * s)):                                   # oil and coolant stains
        soft_dot(L, rng.uniform(0, size), rng.uniform(0, size), rng.uniform(30, 90) * s, rng.uniform(20, 60) * s, rng.uniform(0.15, 0.5), rng, albedo=(0.04, 0.035, 0.03), smooth=0.78, metal=0.1, rough=0.5)
    scratches(L, rng, 90 * s * s, length=(40 * s, 260 * s), width=(0.8 * s, 1.6 * s), angle=0, spread=70, bright=0.7, alpha=(0.15, 0.5), depth=0.3)
    chips(L, rng, 26, size=(1.5 * s, 3.5 * s))
    L.alb = np.clip(L.alb, 0, 1)
    return L


# ------------------------------------------------------------------------------------------------ the grafted bio frames

def make_bio(size=1024, seed=121):
    """Dark ribbed biomechanical shell, a 1 m tile (u runs round the rib, v across it): bulging segments every 12 cm like a spine, fine ridges along the length between
    them, a few tendons, domain-warped so nothing is regular; near black with a cold violet and a dried-brown cast, glossy on the crests and damp in the grooves."""
    rng = rngf(seed)
    s = size / 1024.0
    L = Layers(size, size, albedo=(0.030, 0.029, 0.036), metal=0.0, smooth=0.55, ao=1.0)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    wx = 22.0 * s * spectral_noise(size, size, rng, beta=2.6, fmax=8)
    wy = 14.0 * s * spectral_noise(size, size, rng, beta=2.6, fmax=8)
    X = (xx + wx) % size
    Y = (yy + wy) % size
    seg_period = 128.0 * s
    seg = 0.5 + 0.5 * np.cos(2 * math.pi * X / seg_period)
    bulge = smoothstep(0.1, 0.95, seg)
    fine = np.abs(np.sin(math.pi * Y / (21.0 * s))) ** 0.55                       # fine ridges along u
    ridge_mod = 0.55 + 0.45 * spectral_noise(size, size, rng, beta=2.0, fmin=2, fmax=14)
    cross = 1.0 - smoothstep(0.0, 0.18, 1.0 - seg)                                # the groove between segments
    h = 3.0 * s * bulge + 1.7 * s * fine * ridge_mod * (0.35 + 0.65 * bulge) - 2.6 * s * cross
    # tendons: a few thin raised cords running across the segments
    for _ in range(9):
        y0 = rng.uniform(0, size)
        amp, ph, fr = rng.uniform(4, 14) * s, rng.uniform(0, math.tau), rng.uniform(1.0, 2.5)
        xs = np.arange(size, dtype=np.float32)
        ys = y0 + amp * np.sin(2 * math.pi * fr * xs / size + ph)
        for x in range(0, size, 3):
            draw_segment(L, (x, ys[x]), (x + 3, ys[min(x + 3, size - 1)]), rng.uniform(2.0, 4.0) * s, k=0.9, height_add=2.0 * s, smooth_add=0.2)
    L.hgt += h + 0.5 * spectral_noise(size, size, rng, beta=1.2, fmin=40)
    crest = smoothstep(0.35, 1.0, bulge + 0.3 * fine)
    L.alb += (crest * 0.030)[:, :, None] * np.array([0.9, 0.95, 1.2], np.float32)
    L.alb += 0.012 * smoothstep(0.4, 1.4, spectral_noise(size, size, rng, beta=2.2, fmin=3, fmax=30))[:, :, None] * np.array([1.4, 0.8, 0.5], np.float32)
    L.smo = 0.40 + 0.42 * crest + 0.06 * spectral_noise(size, size, rng, beta=2.0, fmin=3, fmax=40)
    L.ao = 1.0 - 0.55 * cross - 0.25 * (1 - fine) * (1 - bulge)
    L.tint_noise(spectral_noise(size, size, rng, beta=1.0, fmin=200), 0.10)
    scratches(L, rng, 22, length=(30 * s, 140 * s), width=(0.6 * s, 1.0 * s), angle=0, spread=30, bright=0.26, metal=0.0, gloss=0.2, depth=0.2, alpha=(0.1, 0.35))
    for _ in range(int(60 * s * s)):                                         # wet beads
        soft_dot(L, rng.uniform(0, size), rng.uniform(0, size), rng.uniform(1.2, 3.0) * s, rng.uniform(1.2, 3.0) * s, 0.8, rng, albedo=(0.05, 0.05, 0.06), smooth=0.97, height_add=0.8)
    L.alb = np.clip(L.alb, 0, 1)
    return L


# ------------------------------------------------------------------------------------------------ steel

def make_metal(size=1024, seed=131):
    """Brushed dark steel, a 1 m tile: the grain runs along u, with a polish where hands have been, fine scratches, a few pits and smears."""
    rng = rngf(seed)
    s = size / 1024.0
    L = Layers(size, size, albedo=(0.50, 0.505, 0.52), metal=1.0, smooth=0.60, ao=1.0)
    band = spectral_noise(size, size, rng, beta=2.0, fmin=1, fmax=60, sx=0.03, sy=1.0)
    L.tint_noise(band, 0.07)
    L.tint_noise(spectral_noise(size, size, rng, beta=2.6, fmin=2, fmax=14), 0.08)
    L.smo += 0.05 * band + 0.1 * smoothstep(0.4, 1.4, spectral_noise(size, size, rng, beta=2.4, fmin=2, fmax=20))
    L.hgt += 0.35 * spectral_noise(size, size, rng, beta=0.8, fmin=14, sx=0.03, sy=1.0)
    scratches(L, rng, 160 * s * s, length=(40 * s, 400 * s), width=(0.5 * s, 1.0 * s), angle=0, spread=4, bright=0.8, metal=1.0, gloss=0.2, depth=0.3, alpha=(0.1, 0.4))
    scratches(L, rng, 60 * s * s, length=(10 * s, 70 * s), width=(0.5 * s, 0.9 * s), angle=0, spread=180, bright=0.8, metal=1.0, gloss=0.2, depth=0.3, alpha=(0.1, 0.4))
    for _ in range(int(30 * s * s)):                                         # oil smears and dirt
        soft_dot(L, rng.uniform(0, size), rng.uniform(0, size), rng.uniform(8, 40) * s, rng.uniform(5, 18) * s, rng.uniform(0.1, 0.4), rng, albedo=(0.06, 0.05, 0.045), metal=0.3, smooth=0.55, rough=0.5)
    for _ in range(int(140 * s * s)):
        soft_dot(L, rng.uniform(0, size), rng.uniform(0, size), rng.uniform(0.5, 1.4) * s, rng.uniform(0.5, 1.4) * s, 0.7, rng, albedo=(0.08, 0.08, 0.08), ao=0.5, height_add=-0.5)
    L.alb = np.clip(L.alb, 0, 1)
    return L


def make_fabric(size=1024, seed=141):
    """Webbing and padded vinyl in dull olive-drab, a 25 cm tile: a twill weave at 0.5 mm threads, dirt in the grooves, polished at the edges where it is touched."""
    rng = rngf(seed)
    L = Layers(size, size, albedo=(0.20, 0.19, 0.145), metal=0.0, smooth=0.22, ao=1.0)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    p = 8.0
    warp = 0.5 + 0.5 * np.sin(2 * math.pi * (xx + yy * 0.5) / p)
    weft = 0.5 + 0.5 * np.sin(2 * math.pi * (yy - xx * 0.5) / (p * 1.4))
    thread = np.maximum(warp * 0.8, weft * 0.65)
    L.hgt += 2.2 * thread + 0.6 * spectral_noise(size, size, rng, beta=1.0, fmin=60)
    L.alb *= (0.78 + 0.34 * thread)[:, :, None]
    L.ao = 0.55 + 0.45 * thread
    L.tint_noise(spectral_noise(size, size, rng, beta=2.4, fmin=2, fmax=24), 0.12)
    L.tint_noise(spectral_noise(size, size, rng, beta=1.6, fmin=40), 0.05)
    L.smo += 0.05 * spectral_noise(size, size, rng, beta=2.0, fmin=3, fmax=30)
    for _ in range(40):
        soft_dot(L, rng.uniform(0, size), rng.uniform(0, size), rng.uniform(30, 110), rng.uniform(20, 70), rng.uniform(0.15, 0.4), rng, albedo=(0.06, 0.05, 0.04), smooth=0.15, rough=0.5)
    L.alb = np.clip(L.alb, 0, 1)
    return L


def make_rubber(size=256, seed=151):
    rng = rngf(seed)
    L = Layers(size, size, albedo=(0.030, 0.030, 0.032), metal=0.0, smooth=0.30, ao=1.0)
    L.hgt += 0.8 * spectral_noise(size, size, rng, beta=1.0, fmin=20)
    L.tint_noise(spectral_noise(size, size, rng, beta=2.0, fmin=2, fmax=20), 0.15)
    L.smo += 0.06 * spectral_noise(size, size, rng, beta=2.0, fmin=2, fmax=20)
    return L


def make_tissue(size=1024, seed=161):
    """Living tissue in the ship's sockets, a 40 cm tile: wet, dark red-brown, a net of veins and folds, a lighter bruise-pink in places."""
    rng = rngf(seed)
    s = size / 1024.0
    L = Layers(size, size, albedo=(0.17, 0.045, 0.040), metal=0.0, smooth=0.82, ao=1.0)
    n1 = spectral_noise(size, size, rng, beta=2.4, fmin=2, fmax=18)
    n2 = spectral_noise(size, size, rng, beta=1.6, fmin=8, fmax=80)
    folds = 1.0 - np.abs(spectral_noise(size, size, rng, beta=2.0, fmin=3, fmax=22))
    veins = np.clip(1.0 - np.abs(spectral_noise(size, size, rng, beta=2.3, fmin=6, fmax=50)) * 2.2, 0, 1) ** 3
    L.hgt += 5.0 * s * folds + 1.8 * s * veins + 0.8 * n2
    L.alb *= (1.0 + 0.25 * n1)[:, :, None]
    L.alb += (0.10 * smoothstep(0.2, 1.4, n1))[:, :, None] * np.array([1.0, 0.35, 0.3], np.float32)
    L.alb = L.alb * (1 - 0.5 * veins[:, :, None]) + veins[:, :, None] * 0.5 * np.array([0.10, 0.012, 0.02], np.float32)
    L.smo = 0.70 + 0.2 * folds - 0.1 * (1 - folds) + 0.05 * n2
    L.ao = 1.0 - 0.5 * (1 - folds) ** 2
    for _ in range(int(220 * s * s)):
        soft_dot(L, rng.uniform(0, size), rng.uniform(0, size), rng.uniform(1.5, 4.5) * s, rng.uniform(1.5, 4.5) * s, 0.8, rng, albedo=(0.30, 0.1, 0.08), smooth=0.98, height_add=1.0)
    L.alb = np.clip(L.alb, 0, 1)
    return L


def make_glyph(w=2048, h=128, seed=171):
    """A strip of invented glyphs, emissive only: each cell is a few strokes and dots of an unreadable script, in cold teal, some bright, some dim, tiling along u."""
    rng = rngf(seed)
    L = Layers(w, h, albedo=(0.012, 0.014, 0.016), metal=0.0, smooth=0.55, ao=1.0)
    cell = 64
    n = w // cell
    for i in range(n):
        x0 = i * cell + 10
        inten = rng.choice([0.25, 0.4, 0.6, 0.85, 1.0], p=[0.2, 0.25, 0.25, 0.2, 0.1])
        col = np.array([0.30, 0.95, 0.82]) * inten
        top, bot = 22, h - 22
        mid = 0.5 * (top + bot)
        for _ in range(int(rng.integers(3, 7))):
            kind = rng.choice(["line", "line", "arc", "dot", "tick"])
            if kind == "line":
                a = rng.choice([0, 45, 90, 135]) * math.pi / 180
                ln = rng.uniform(14, 40)
                cx, cy = x0 + rng.uniform(8, 34), rng.uniform(top + 10, bot - 10)
                draw_segment(L, (cx - math.cos(a) * ln / 2, cy - math.sin(a) * ln / 2), (cx + math.cos(a) * ln / 2, cy + math.sin(a) * ln / 2), 3.0, emis=col, albedo=(0.05, 0.12, 0.11))
            elif kind == "arc":
                cx, cy, r = x0 + rng.uniform(14, 30), rng.uniform(top + 14, bot - 14), rng.uniform(8, 17)
                a0 = rng.uniform(0, math.tau)
                span = rng.uniform(1.6, 4.4)
                pts = [(cx + r * math.cos(a0 + span * t / 8), cy + r * math.sin(a0 + span * t / 8)) for t in range(9)]
                for p, q in zip(pts[:-1], pts[1:]):
                    draw_segment(L, p, q, 3.0, emis=col, albedo=(0.05, 0.12, 0.11))
            elif kind == "dot":
                disc(L, x0 + rng.uniform(6, 40), rng.uniform(top, bot), rng.uniform(2.2, 4.2), emis=col, albedo=(0.05, 0.12, 0.11))
            else:
                cx = x0 + rng.uniform(6, 40)
                draw_segment(L, (cx, mid - 24), (cx, mid - 14), 3.0, emis=col)
                draw_segment(L, (cx, mid + 14), (cx, mid + 24), 3.0, emis=col)
    return L


def make_skin(size=1024, seed=201):
    """The pilot's skin, a 20 cm tile (0.2 mm a pixel): ashen, with a cold violet cast where the blood runs near the surface and a darker banding across the knuckles' direction,
    fine pores and creases, a few paler flecks. Human but not baseline: nothing here is a tan."""
    rng = rngf(seed)
    s = size / 1024.0
    L = Layers(size, size, albedo=(0.47, 0.405, 0.405), metal=0.0, smooth=0.42, ao=1.0)
    n1 = spectral_noise(size, size, rng, beta=2.6, fmin=1, fmax=10)
    n2 = spectral_noise(size, size, rng, beta=1.8, fmin=6, fmax=60)
    band = np.sin(2 * math.pi * (np.arange(size)[None, :] / size * 5.0 + 0.6 * n1)) * 0.5 + 0.5
    L.alb *= (1.0 + 0.07 * n1 + 0.04 * n2 - 0.07 * band)[:, :, None]
    L.alb += (0.03 * smoothstep(0.2, 1.4, n2))[:, :, None] * np.array([0.5, 0.2, 0.9], np.float32)
    veins = np.clip(1.0 - np.abs(spectral_noise(size, size, rng, beta=2.3, fmin=3, fmax=30)) * 2.4, 0, 1) ** 3
    L.alb = L.alb * (1 - 0.18 * veins[:, :, None]) + 0.18 * veins[:, :, None] * np.array([0.25, 0.22, 0.32], np.float32)
    L.hgt += 0.8 * spectral_noise(size, size, rng, beta=1.0, fmin=120)
    # pores: tiny dark dimples; creases: short fine lines mostly across the tile
    for _ in range(int(2600 * s * s)):
        soft_dot(L, rng.uniform(0, size), rng.uniform(0, size), rng.uniform(0.8, 1.7) * s, rng.uniform(0.8, 1.7) * s, rng.uniform(0.3, 0.7), rng, albedo=(0.30, 0.24, 0.25), height_add=-1.0, ao=0.7)
    for _ in range(int(140 * s * s)):
        a = rng.normal(0, 0.25)
        ln = rng.uniform(40, 160) * s
        x0, y0 = rng.uniform(0, size), rng.uniform(0, size)
        draw_segment(L, (x0, y0), (x0 + math.cos(a) * ln, y0 + math.sin(a) * ln), rng.uniform(1.2, 2.4) * s, k=0.4, albedo=(0.30, 0.25, 0.26), height_add=-1.4 * s, ao_mul=0.8)
    L.smo += 0.05 * n2
    L.alb = np.clip(L.alb, 0, 1)
    return L


def make_nail(size=256, seed=211):
    rng = rngf(seed)
    L = Layers(size, size, albedo=(0.085, 0.07, 0.065), metal=0.0, smooth=0.70, ao=1.0)
    ridge = np.abs(np.sin(np.arange(size)[None, :] * math.pi / 14.0)) ** 0.7
    L.hgt += 1.2 * ridge
    L.tint_noise(spectral_noise(size, size, rng, beta=2.0, fmin=2, fmax=16), 0.2)
    return L


def make_screen_blank(size=4):
    L = Layers(size, size, albedo=(0, 0, 0), metal=0.0, smooth=0.8)
    return L


# ------------------------------------------------------------------------------------------------ the detail map

def make_detail(size=512, seed=181):
    """HDRP detail map, a 50 cm tile at 1 mm a pixel: R albedo (0.5 neutral), G normal Y, B smoothness (0.5 neutral), A normal X."""
    rng = rngf(seed)
    s = size / 512.0
    L = Layers(size, size, albedo=(0.5, 0.5, 0.5), metal=0, smooth=0.5)
    h = 0.5 * spectral_noise(size, size, rng, beta=0.8, fmin=60) + 0.7 * spectral_noise(size, size, rng, beta=2.0, fmin=6, fmax=120) + 0.4 * spectral_noise(size, size, rng, beta=1.4, fmin=20, fmax=250)
    L.hgt = h
    for _ in range(int(90 * s * s)):
        a = rng.uniform(0, math.tau)
        ln = rng.uniform(8, 60) * s
        x0, y0 = rng.uniform(0, size), rng.uniform(0, size)
        draw_segment(L, (x0, y0), (x0 + math.cos(a) * ln, y0 + math.sin(a) * ln), rng.uniform(0.5, 1.0) * s, height_add=-0.6 * s, smooth_add=0.2)
    nrm = mntex.normal_from_height(L.hgt, 0.8 / s)
    alb = np.clip(0.5 + 0.08 * spectral_noise(size, size, rng, beta=1.2, fmin=4) + 0.05 * spectral_noise(size, size, rng, beta=0.8, fmin=80), 0, 1)
    smo = np.clip(0.5 + 0.06 * spectral_noise(size, size, rng, beta=2.0, fmin=2, fmax=30) + 0.05 * spectral_noise(size, size, rng, beta=1.0, fmin=30), 0, 1)
    return mntex.to_u8(np.stack([alb, nrm[:, :, 1], smo, nrm[:, :, 0]], axis=2))


# ------------------------------------------------------------------------------------------------ the trim sheet

PX_M = 1000          # pixels a metre in the trim sheet


def px(m):
    return int(round(m * PX_M))


def _base_panel(w, h, rng, tone=0.16, smooth=0.35):
    L = Layers(w, h, albedo=(tone, tone * 1.02, tone * 0.98), metal=0.0, smooth=smooth, ao=1.0)
    L.tint_noise(spectral_noise(h, w, rng, beta=2.2, fmin=2, fmax=20), 0.10)
    L.hgt += 0.4 * spectral_noise(h, w, rng, beta=1.0, fmin=30)
    return L


def _age(L, rng, amount=1.0, grime=0.25):
    g = smoothstep(0.1, 1.3, spectral_noise(L.h, L.w, rng, beta=2.2, fmin=2, fmax=30)) * grime * amount
    L.alb *= (1.0 - g)[:, :, None]
    L.smo -= 0.10 * g
    chips(L, rng, int(max(2, L.w * L.h / 9000 * amount)), size=(0.8, 2.0))
    scratches(L, rng, int(L.w * L.h / 6000 * amount), length=(8, 60), width=(0.5, 0.9), angle=0, spread=70, bright=0.62, metal=0.8, gloss=0.1, depth=0.3, alpha=(0.1, 0.4))
    return L


def piece_switch_bank(rng, w_m=0.32, h_m=0.12, n=8, names=None, seed=0):
    w, h = px(w_m), px(h_m)
    L = _base_panel(w, h, rng, 0.12)
    L.rect(2, 2, w - 2, h - 2, albedo=(0.14, 0.14, 0.13))
    names = names or ["MAIN", "BUS A", "BUS B", "AUX", "COMMS", "SENSE", "GRAFT", "TRIM"]
    pitch = (w - 40) / n
    for i in range(n):
        cx = 20 + pitch * (i + 0.5)
        cy = h * 0.58
        ring(L, cx, cy, 8, 13, wrap=False, albedo=(0.5, 0.5, 0.48), metal=0.9, smooth=0.5, height_add=1.0)
        disc(L, cx, cy, 7.5, wrap=False, albedo=(0.05, 0.05, 0.05), height_add=-1.0)
        up = rng.random() < 0.6
        tip = cy + (-17 if up else 17)
        draw_segment(L, (cx, cy), (cx, tip), 4.5, wrap=False, albedo=(0.62, 0.62, 0.6), metal=0.9, smooth=0.55, height_add=2.0)
        disc(L, cx, tip, 4.8, wrap=False, albedo=(0.7, 0.7, 0.68), metal=0.9, smooth=0.6, height_add=2.5)
        text(L, names[i % len(names)], cx, 12, 9, width=1.2, align="center", wrap=False, albedo=(0.78, 0.78, 0.72), smooth=0.3)
        lamp_col = [(0.95, 0.55, 0.12), (0.3, 0.9, 0.7), (0.9, 0.12, 0.06)][int(rng.integers(0, 3))]
        if rng.random() < 0.7:
            disc(L, cx, h - 12, 3.2, wrap=False, albedo=(0.05, 0.05, 0.05), emis=tuple(np.array(lamp_col) * rng.uniform(0.3, 1.0)))
    draw_segment(L, (6, 4), (w - 6, 4), 1.2, wrap=False, albedo=(0.3, 0.3, 0.28))
    return _age(L, rng, 1.0)


def piece_dials(rng, w_m=0.34, h_m=0.12, n=3):
    w, h = px(w_m), px(h_m)
    L = _base_panel(w, h, rng, 0.10)
    r = h * 0.36
    for i in range(n):
        cx, cy = w * (i + 0.5) / n, h * 0.5
        ring(L, cx, cy, r, r + 5, wrap=False, albedo=(0.5, 0.5, 0.48), metal=0.9, smooth=0.6, height_add=2.0)
        disc(L, cx, cy, r, wrap=False, albedo=(0.06, 0.065, 0.06), smooth=0.8, height_add=-1.2)
        for t in range(13):
            a = math.radians(-135 + 270 * t / 12)
            ln = 7 if t % 3 == 0 else 4
            draw_segment(L, (cx + math.sin(a) * (r - 3), cy - math.cos(a) * (r - 3)), (cx + math.sin(a) * (r - 3 - ln), cy - math.cos(a) * (r - 3 - ln)), 1.2, wrap=False, albedo=(0.8, 0.8, 0.74))
        a = math.radians(rng.uniform(-110, 80))
        draw_segment(L, (cx, cy), (cx + math.sin(a) * (r - 6), cy - math.cos(a) * (r - 6)), 1.8, wrap=False, albedo=(0.9, 0.55, 0.12), emis=(0.3, 0.12, 0.0))
        disc(L, cx, cy, 3, wrap=False, albedo=(0.5, 0.5, 0.5), metal=1.0)
        text(L, ["BUS V", "TEMP", "FLOW"][i % 3], cx, cy + r * 0.35, 7, width=1.0, align="center", wrap=False, albedo=(0.7, 0.7, 0.66))
    return _age(L, rng, 1.0)


def piece_sliders(rng, w_m=0.30, h_m=0.14, n=6):
    w, h = px(w_m), px(h_m)
    L = _base_panel(w, h, rng, 0.11)
    for i in range(n):
        x = 18 + (w - 36) * i / (n - 1)
        draw_segment(L, (x, 14), (x, h - 14), 3.2, wrap=False, albedo=(0.02, 0.02, 0.02), height_add=-1.5)
        for t in range(6):
            yy = 14 + (h - 28) * t / 5
            draw_segment(L, (x - 7, yy), (x - 4, yy), 1.0, wrap=False, albedo=(0.7, 0.7, 0.65))
        ky = 14 + (h - 28) * rng.uniform(0.1, 0.9)
        L.rect(x - 7, ky - 5, x + 7, ky + 5, albedo=(0.55, 0.55, 0.52), metal=0.9, smooth=0.55, height_add=2.5)
        draw_segment(L, (x - 5, ky), (x + 5, ky), 1.0, wrap=False, albedo=(0.1, 0.1, 0.1))
    return _age(L, rng, 1.0)


def piece_placard(rng, w_m, h_m, lines, tone=0.22, border=None, big=False):
    w, h = px(w_m), px(h_m)
    L = _base_panel(w, h, rng, tone, smooth=0.28)
    if border:
        L.rect(2, 2, w - 2, h - 2, albedo=border)
        L.rect(6, 6, w - 6, h - 6, albedo=(tone, tone, tone * 0.95))
    th = (h - 24) / (len(lines) + 0.3)
    th = min(th * 0.78, 26 if not big else 44)
    y = 12 + (h - 24 - len(lines) * th * 1.35) * 0.5
    for ln in lines:
        text(L, ln, w * 0.5, y, th, width=max(1.1, th * 0.12), align="center", wrap=False, albedo=(0.85, 0.84, 0.78) if tone < 0.3 else (0.08, 0.08, 0.07), smooth=0.25)
        y += th * 1.35
    for dx, dy in ((8, 8), (w - 8, 8), (8, h - 8), (w - 8, h - 8)):
        screw(L, dx, dy, 3.0, angle=rng.uniform(0, math.pi), wrap=False)
    return _age(L, rng, 1.2)


def piece_hazard(rng, w_m=0.60, h_m=0.05):
    w, h = px(w_m), px(h_m)
    L = Layers(w, h, albedo=(0.7, 0.55, 0.08), metal=0.0, smooth=0.3)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    stripe = (np.floor((xx + yy) / (h * 0.9)) % 2)
    L.alb = np.where(stripe[:, :, None] > 0.5, np.array([0.05, 0.05, 0.045], np.float32), np.array([0.72, 0.56, 0.08], np.float32)) * (0.9 + 0.1 * rng.random((h, w, 1)))
    return _age(L, rng, 1.8, grime=0.35)


def piece_vent(rng, w_m=0.30, h_m=0.20):
    w, h = px(w_m), px(h_m)
    L = _base_panel(w, h, rng, 0.10)
    n = 9
    for i in range(n):
        y = 14 + (h - 28) * i / (n - 1)
        L.rect(12, y - 5, w - 12, y + 5, albedo=(0.012, 0.012, 0.012), ao=0.4, height_add=-3.0)
        draw_segment(L, (12, y + 6), (w - 12, y + 6), 2.0, wrap=False, albedo=(0.30, 0.30, 0.28), metal=0.6, height_add=2.0)
    for dx, dy in ((8, 8), (w - 8, 8), (8, h - 8), (w - 8, h - 8)):
        screw(L, dx, dy, 3.2, angle=rng.uniform(0, math.pi), wrap=False)
    return _age(L, rng, 1.0)


def piece_panel_blank(rng, w_m=0.30, h_m=0.30):
    w, h = px(w_m), px(h_m)
    L = _base_panel(w, h, rng, 0.19)
    L.rect(0, 0, w, 3, albedo=(0.02, 0.02, 0.02), height_add=-3)
    L.rect(0, h - 3, w, h, albedo=(0.02, 0.02, 0.02), height_add=-3)
    L.rect(0, 0, 3, h, albedo=(0.02, 0.02, 0.02), height_add=-3)
    L.rect(w - 3, 0, w, h, albedo=(0.02, 0.02, 0.02), height_add=-3)
    for dx, dy in ((16, 16), (w - 16, 16), (16, h - 16), (w - 16, h - 16)):
        screw(L, dx, dy, 5.0, angle=rng.uniform(0, math.pi), wrap=False)
    L.rect(w * 0.5 - 38, h * 0.5 - 7, w * 0.5 + 38, h * 0.5 + 7, albedo=(0.04, 0.04, 0.04), height_add=-4, ao=0.4)
    draw_segment(L, (w * 0.5 - 34, h * 0.5 + 9), (w * 0.5 + 34, h * 0.5 + 9), 2.5, wrap=False, albedo=(0.6, 0.6, 0.58), metal=0.9, height_add=1.5)
    return _age(L, rng, 1.4)


def piece_id_plate(rng, w_m=0.12, h_m=0.06, line="MK 7 / SERIAL 0481-C"):
    w, h = px(w_m), px(h_m)
    L = Layers(w, h, albedo=(0.55, 0.55, 0.52), metal=0.9, smooth=0.5)
    L.hgt += 0.4 * spectral_noise(h, w, rng, beta=1.0, fmin=10)
    text(L, line, w * 0.5, h * 0.3, h * 0.28, width=1.0, align="center", wrap=False, albedo=(0.05, 0.05, 0.05), metal=0.0, height_add=-1.0)
    for dx in (6, w - 6):
        screw(L, dx, h * 0.5, 2.6, angle=rng.uniform(0, math.pi), wrap=False)
    return _age(L, rng, 1.0)


def piece_arrows(rng, w_m=0.30, h_m=0.06):
    w, h = px(w_m), px(h_m)
    L = _base_panel(w, h, rng, 0.14)
    for i in range(5):
        x = 22 + i * (w - 44) / 4
        draw_segment(L, (x - 10, h * 0.5), (x + 10, h * 0.5), 3, wrap=False, albedo=(0.82, 0.8, 0.7))
        draw_segment(L, (x + 3, h * 0.5 - 8), (x + 12, h * 0.5), 3, wrap=False, albedo=(0.82, 0.8, 0.7))
        draw_segment(L, (x + 3, h * 0.5 + 8), (x + 12, h * 0.5), 3, wrap=False, albedo=(0.82, 0.8, 0.7))
    return _age(L, rng, 1.4)


def piece_stencil(rng, w_m, h_m, label, tone=(0.80, 0.78, 0.68)):
    w, h = px(w_m), px(h_m)
    L = _base_panel(w, h, rng, 0.12)
    text(L, label, w * 0.5, h * 0.18, h * 0.64, width=max(2.0, h * 0.07), align="center", wrap=False, albedo=tone, smooth=0.3)
    return _age(L, rng, 1.8, grime=0.35)


def make_trim_atlas(size=2048):
    rng = rngf(190)
    atlas = Atlas(size, size, pad=8, albedo=(0.06, 0.06, 0.06))
    items = [
        ("switch_bank_a", piece_switch_bank(rng, names=["MAIN", "BUS A", "BUS B", "AUX", "COMMS", "SENSE", "GRAFT", "TRIM"]), 0.32, 0.12),
        ("switch_bank_b", piece_switch_bank(rng, 0.32, 0.12, 8, ["PUMP", "HEAT", "O2", "FAN", "LAMP", "SEAL", "LOCK", "VENT"]), 0.32, 0.12),
        ("switch_bank_c", piece_switch_bank(rng, 0.32, 0.12, 6, ["TIE", "FEED", "GRAFT", "BIAS", "BEAM", "RELAY"]), 0.32, 0.12),
        ("dials", piece_dials(rng), 0.34, 0.12),
        ("sliders", piece_sliders(rng), 0.30, 0.14),
        ("placard_warn", piece_placard(rng, 0.20, 0.14, ["CAUTION", "LIVE TISSUE", "KEEP HANDS CLEAR"], 0.62, border=(0.78, 0.6, 0.08)), 0.20, 0.14),
        ("placard_main", piece_placard(rng, 0.26, 0.10, ["MAIN BUS", "DO NOT OPEN", "UNDER LOAD"], 0.20), 0.26, 0.10),
        ("label_breaker", piece_placard(rng, 0.34, 0.12, ["MAIN BREAKER", "UP ON  DOWN OFF"], 0.18, big=True), 0.34, 0.12),
        ("label_rack", piece_placard(rng, 0.30, 0.08, ["ARMS RACK", "SEAT ONE PER BAY"], 0.20), 0.30, 0.08),
        ("label_repair", piece_placard(rng, 0.30, 0.08, ["SEAT MODULE", "HOLD UNTIL KNIT"], 0.22), 0.30, 0.08),
        ("hazard", piece_hazard(rng), 0.60, 0.05),
        ("vent", piece_vent(rng), 0.30, 0.20),
        ("panel_blank", piece_panel_blank(rng), 0.30, 0.30),
        ("id_plate_a", piece_id_plate(rng, line="MK 7 / 0481-C"), 0.12, 0.06),
        ("id_plate_b", piece_id_plate(rng, line="GRAFT 3 / LIVING"), 0.12, 0.06),
        ("arrows", piece_arrows(rng), 0.30, 0.06),
        ("stencil_aft", piece_stencil(rng, 0.30, 0.10, "AFT 14"), 0.30, 0.10),
        ("stencil_hatch", piece_stencil(rng, 0.24, 0.08, "HATCH 2"), 0.24, 0.08),
    ]
    for name, piece, mw, mh in items:
        atlas.place(name, piece, mw, mh, normal_strength=1.0)
    out = atlas.finish(1.0)
    pieces = {k: {"uv": [round(v, 6) for v in it["uv"]], "metres": it["metres"]} for k, it in atlas.items.items()}
    with open(TRIMS_JSON, "w") as f:
        json.dump({"about": "Cabin trim sheet rectangles (u0, v0, u1, v1; v up) and real sizes in metres. Written by build_cabin_textures.py.", "pieces": pieces}, f, indent=1)
    return out


# ------------------------------------------------------------------------------------------------ materials

def write_materials():
    def m(blender, name, **kw):
        d = {"blender": blender, "name": name}
        d.update(kw)
        return d
    tex = lambda base: dict(baseMap=f"{base}_albedo", normalMap=f"{base}_normal", maskMap=f"{base}_mask")
    det = dict(detailMap="cab_detail", uvDetail=1, detailTiling=[1, 1], detailAlbedo=0.55, detailNormal=0.25, detailSmoothness=0.5)
    mats = [
        m("mn_cab_plate", "MN_CabPlate", baseColor=[1, 1, 1], **tex("cab_plate"), normalScale=0.9, smoothnessRemap=[0, 0.85], specularAA=True, **det),
        m("mn_cab_bio", "MN_CabBio", baseColor=[1, 1, 1], **tex("cab_bio"), normalScale=1.0, smoothnessRemap=[0, 0.95], specularAA=True, **det),
        m("mn_cab_floor", "MN_CabFloor", baseColor=[1, 1, 1], **tex("cab_floor"), normalScale=1.0, smoothnessRemap=[0, 0.9], specularAA=True, **det),
        m("mn_cab_trim", "MN_CabTrim", baseColor=[1, 1, 1], **tex("cab_trim"), emissiveMap="cab_trim_emissive", emissiveColor=[1, 1, 1], emissiveNits=900, normalScale=1.0, smoothnessRemap=[0, 1.0], specularAA=True, **det),
        m("mn_cab_metal", "MN_CabMetal", baseColor=[1, 1, 1], **tex("cab_metal"), normalScale=1.0, smoothnessRemap=[0, 0.9], specularAA=True, **det),
        m("mn_cab_fabric", "MN_CabFabric", baseColor=[1, 1, 1], **tex("cab_fabric"), normalScale=1.0, smoothnessRemap=[0, 0.5], specularAA=True),
        m("mn_cab_glass", "MN_CabGlass", surface="glass", baseColor=[0.55, 0.62, 0.66], alpha=0.07, metallic=0.0, smoothness=0.97, doubleSided=True),
        m("mn_cab_screen", "MN_CabScreen", baseColor=[0.0, 0.0, 0.0], metallic=0.0, smoothness=0.85, emissiveMap="cab_screen_emissive", emissiveColor=[1, 1, 1], emissiveNits=900),
        m("mn_cab_skin", "MN_CabSkin", baseColor=[1, 1, 1], **tex("cab_skin"), normalScale=0.8, smoothnessRemap=[0, 0.7]),
        m("mn_cab_nail", "MN_CabNail", baseColor=[1, 1, 1], **tex("cab_nail"), normalScale=1.0, smoothnessRemap=[0, 0.9]),
        m("mn_cab_glyph", "MN_CabGlyph", baseColor=[1, 1, 1], **tex("cab_glyph"), emissiveMap="cab_glyph_emissive", emissiveColor=[1, 1, 1], emissiveNits=700, normalScale=1.0, smoothnessRemap=[0, 0.8]),
        m("mn_cab_tissue", "MN_CabTissue", baseColor=[1, 1, 1], **tex("cab_tissue"), normalScale=1.0, smoothnessRemap=[0, 0.97]),
        m("mn_cab_lamp_white", "MN_CabLampWhite", baseColor=[0.02, 0.02, 0.02], metallic=0.0, smoothness=0.9, emissiveColor=[0.95, 0.97, 1.0], emissiveNits=2500),
        m("mn_cab_lamp_amber", "MN_CabLampAmber", baseColor=[0.02, 0.015, 0.01], metallic=0.0, smoothness=0.9, emissiveColor=[1.0, 0.55, 0.14], emissiveNits=2500),
        m("mn_cab_lamp_red", "MN_CabLampRed", baseColor=[0.02, 0.005, 0.005], metallic=0.0, smoothness=0.9, emissiveColor=[1.0, 0.08, 0.04], emissiveNits=1800),
        m("mn_cab_rubber", "MN_CabRubber", baseColor=[1, 1, 1], **tex("cab_rubber"), normalScale=1.0, smoothnessRemap=[0, 0.5]),
    ]
    with open(os.path.join(OUT, "cabin.materials.json"), "w") as f:
        json.dump({"format": "monolith-ship-materials/1", "about": "HDRP material recipes for the pilot's cabin. baseColor and emissiveColor are sRGB display values; emissiveNits scales the emissive map. CabinArtSetup makes the assets.", "materials": mats}, f, indent=1)
    log("wrote cabin.materials.json")


# ------------------------------------------------------------------------------------------------ main

def main():
    only = [a for a in sys.argv[sys.argv.index("--") + 1:]] if "--" in sys.argv else []
    want = lambda n: not only or n in only
    if want("plate"):
        L = make_plate(); save("cab_plate", L.finish(1.0)); log("plate done")
    if want("floor"):
        L = make_floor(); save("cab_floor", L.finish(1.0))
    if want("bio"):
        L = make_bio(); save("cab_bio", L.finish(1.0))
    if want("metal"):
        L = make_metal(); save("cab_metal", L.finish(0.8))
    if want("fabric"):
        L = make_fabric(); save("cab_fabric", L.finish(1.0))
    if want("rubber"):
        L = make_rubber(); save("cab_rubber", L.finish(0.3))
    if want("tissue"):
        L = make_tissue(); save("cab_tissue", L.finish(1.0))
    if want("glyph"):
        L = make_glyph(); save("cab_glyph", L.finish(0.5))
    if want("skin"):
        L = make_skin(); save("cab_skin", L.finish(1.0))
    if want("nail"):
        L = make_nail(); save("cab_nail", L.finish(1.0))
    if want("screen"):
        L = make_screen_blank(); save("cab_screen", L.finish(1.0), keys=("emissive",))
        mntex.write_png(os.path.join(OUT, "cab_screen_emissive.png"), np.zeros((4, 4, 3), np.uint8))
    if want("detail"):
        mntex.write_png(os.path.join(OUT, "cab_detail.png"), make_detail()); log("wrote cab_detail")
    if want("trim"):
        save("cab_trim", make_trim_atlas())
    write_materials()


main()
