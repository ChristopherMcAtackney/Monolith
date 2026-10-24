"""mnpaint: painting helpers for scripted PBR textures, on top of mntex's ``Layers`` canvas (the earth hull script has its own copies of the first few;
this module is the shared version for the cabin and anything after it). Everything wraps round the canvas edges, so what is painted tiles.

    draw_segment, scratches, soft_dot, chips, grime_streaks    wear
    disc, ring, rivet, screw                                   small hardware
    text                                                       stroke-font lettering (the same 4 x 8 glyphs as the game's HUD)
    height_normal                                              a height field to a tangent-space normal map
"""
import math

import numpy as np

from . import mntex
from .mntex import Layers, spectral_noise, smoothstep

PRIMER = (0.145, 0.118, 0.10)


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


def scratches(L, rng, n, length=(30, 260), width=(0.7, 1.5), angle=90.0, spread=14.0, bright=0.72, metal=0.85, depth=0.7, gloss=0.28, alpha=(0.15, 0.6), wrap=True):
    for _ in range(int(n)):
        ln = rng.uniform(*length)
        a = math.radians(angle + rng.normal(0, spread) + (90 if rng.random() < 0.12 else 0))
        x0, y0 = rng.uniform(0, L.w), rng.uniform(0, L.h)
        x1, y1 = x0 + math.cos(a) * ln, y0 + math.sin(a) * ln
        wd = rng.uniform(*width)
        al = rng.uniform(*alpha)
        draw_segment(L, (x0, y0), (x1, y1), wd, wrap=wrap, k=al, albedo=(bright, bright, bright * 1.02), metal=metal, height_add=-depth * al, smooth_add=gloss * al, ao_mul=0.85)
    return L


def soft_dot(L, x, y, rx, ry, k, rng, wrap=True, rough=0.28, edge=0.35, **kw):
    """An irregular blot (never a square): an ellipse whose outline wobbles, with an anti-aliased soft rim."""
    phi1, phi2, phi3 = rng.uniform(0, math.tau, 3)
    a1, a2, a3 = rng.uniform(0.4, 1.0) * rough, rng.uniform(0.3, 0.9) * rough, rng.uniform(0.2, 0.7) * rough
    m = max(rx, ry) * (1.0 + rough * 2.2) + 1.5
    for ox in ((-L.w, 0, L.w) if wrap else (0,)):
        if x + ox + m < 0 or x + ox - m > L.w:
            continue
        for oy in ((-L.h, 0, L.h) if wrap else (0,)):
            if y + oy + m < 0 or y + oy - m > L.h:
                continue
            x0, x1 = max(int(math.floor(x + ox - m)), 0), min(int(math.ceil(x + ox + m)), L.w)
            y0, y1 = max(int(math.floor(y + oy - m)), 0), min(int(math.ceil(y + oy + m)), L.h)
            if x1 <= x0 or y1 <= y0:
                continue
            gx = (np.arange(x0, x1, dtype=np.float32) + 0.5 - (x + ox))[None, :]
            gy = (np.arange(y0, y1, dtype=np.float32) + 0.5 - (y + oy))[:, None]
            th = np.arctan2(gy / ry, gx / rx)
            r = np.sqrt((gx / rx) ** 2 + (gy / ry) ** 2) / (1.0 + a1 * np.sin(3 * th + phi1) + a2 * np.sin(5 * th + phi2) + a3 * np.sin(8 * th + phi3))
            c = np.clip((1.0 - r) / max(edge, 1e-3), 0.0, 1.0).astype(np.float32)
            if c.max() <= 0:
                continue
            L.paint((slice(y0, y1), slice(x0, x1)), c * k, **kw)


def chips(L, rng, n, size=(1.5, 5.5), bright=0.62, metal=0.9, smooth=0.55, primer=PRIMER, points=None):
    """Paint chips: a flake of top paint lifts and shows the primer; inside it a smaller scrape goes to bare metal. ``points`` (n, 2) places them (else random)."""
    for i in range(int(n)):
        if points is not None:
            x, y = points[i % len(points)]
        else:
            x, y = rng.uniform(0, L.w), rng.uniform(0, L.h)
        r = rng.uniform(*size)
        for _ in range(rng.integers(1, 4)):
            dx, dy = rng.normal(0, r * 0.7, 2)
            rr = r * rng.uniform(0.4, 1.0)
            ar = rng.uniform(0.6, 1.2)
            soft_dot(L, x + dx, y + dy, rr * 1.5, rr * 1.5 * ar, 1.0, rng, albedo=primer, metal=0.15, smooth=0.3, height_add=-0.35)
            soft_dot(L, x + dx + rr * 0.2, y + dy, rr * 0.7, rr * 0.7 * ar, 1.0, rng, albedo=(bright, bright * 0.99, bright * 0.97), metal=metal, smooth=smooth, height_add=-0.3)


def grime_streaks(L, rng, n, length=(40, 220), width=(1.5, 5.0), alpha=(0.05, 0.22), colour=(0.07, 0.055, 0.045), down=True, points=None):
    """Dirt that has run down a surface from a point (a rivet, a seam): thin soft vertical streaks, darker at the head."""
    for i in range(int(n)):
        if points is not None:
            x, y = points[i % len(points)]
            x += rng.normal(0, 1.5)
        else:
            x, y = rng.uniform(0, L.w), rng.uniform(0, L.h)
        ln = rng.uniform(*length)
        a = rng.uniform(*alpha)
        wd = rng.uniform(*width)
        steps = max(2, int(ln / 12))
        for s in range(steps):
            t0, t1 = s / steps, (s + 1) / steps
            draw_segment(L, (x, y + ln * t0), (x, y + ln * t1), wd * (1.0 - 0.5 * t0), k=a * (1.0 - t0) ** 1.5, albedo=colour, smooth_add=-0.2, ao_mul=0.95)


def disc(L, x, y, r, rng=None, wrap=True, soft=0.9, k=1.0, **kw):
    """A round disc with an anti-aliased rim (a rivet head, a lamp, a hole)."""
    m = r + 2
    for ox in ((-L.w, 0, L.w) if wrap else (0,)):
        if x + ox + m < 0 or x + ox - m > L.w:
            continue
        for oy in ((-L.h, 0, L.h) if wrap else (0,)):
            if y + oy + m < 0 or y + oy - m > L.h:
                continue
            x0, x1 = max(int(math.floor(x + ox - m)), 0), min(int(math.ceil(x + ox + m)), L.w)
            y0, y1 = max(int(math.floor(y + oy - m)), 0), min(int(math.ceil(y + oy + m)), L.h)
            if x1 <= x0 or y1 <= y0:
                continue
            gx = (np.arange(x0, x1, dtype=np.float32) + 0.5 - (x + ox))[None, :]
            gy = (np.arange(y0, y1, dtype=np.float32) + 0.5 - (y + oy))[:, None]
            d = np.sqrt(gx * gx + gy * gy)
            c = np.clip((r - d) / soft + 0.5, 0.0, 1.0).astype(np.float32)
            if c.max() <= 0:
                continue
            L.paint((slice(y0, y1), slice(x0, x1)), c * k, **kw)


def ring(L, x, y, r0, r1, wrap=True, soft=0.9, k=1.0, **kw):
    """An annulus between radii r0 and r1."""
    m = r1 + 2
    for ox in ((-L.w, 0, L.w) if wrap else (0,)):
        if x + ox + m < 0 or x + ox - m > L.w:
            continue
        for oy in ((-L.h, 0, L.h) if wrap else (0,)):
            if y + oy + m < 0 or y + oy - m > L.h:
                continue
            x0, x1 = max(int(math.floor(x + ox - m)), 0), min(int(math.ceil(x + ox + m)), L.w)
            y0, y1 = max(int(math.floor(y + oy - m)), 0), min(int(math.ceil(y + oy + m)), L.h)
            if x1 <= x0 or y1 <= y0:
                continue
            gx = (np.arange(x0, x1, dtype=np.float32) + 0.5 - (x + ox))[None, :]
            gy = (np.arange(y0, y1, dtype=np.float32) + 0.5 - (y + oy))[:, None]
            d = np.sqrt(gx * gx + gy * gy)
            c = np.clip(np.minimum(r1 - d, d - r0) / soft + 0.5, 0.0, 1.0).astype(np.float32)
            if c.max() <= 0:
                continue
            L.paint((slice(y0, y1), slice(x0, x1)), c * k, **kw)


def rivet(L, x, y, r, bright=0.5, wrap=True, rng=None):
    """A domed rivet head: a raised disc, lighter at the top, with a dark ring of shadow round it."""
    ring(L, x, y, r * 0.95, r * 1.7, wrap=wrap, k=0.55, albedo=(0.03, 0.03, 0.03), ao=0.55)
    disc(L, x, y, r, wrap=wrap, albedo=(bright, bright * 0.98, bright * 0.94), metal=0.7, smooth=0.5, height_add=1.8 + r * 0.35, ao=1.0)
    disc(L, x - r * 0.3, y - r * 0.35, r * 0.4, wrap=wrap, k=0.5, albedo=(min(1, bright * 1.5),) * 3, smooth=0.75)


def screw(L, x, y, r, angle=0.0, bright=0.55, wrap=True):
    """A slotted screw head set into a panel: a dark ring, a metal disc and a slot."""
    ring(L, x, y, r * 0.9, r * 1.5, wrap=wrap, k=0.6, albedo=(0.02, 0.02, 0.02), ao=0.5, height_add=-0.8)
    disc(L, x, y, r, wrap=wrap, albedo=(bright, bright, bright * 0.96), metal=0.85, smooth=0.55, height_add=0.5)
    dx, dy = math.cos(angle) * r * 0.8, math.sin(angle) * r * 0.8
    draw_segment(L, (x - dx, y - dy), (x + dx, y + dy), max(1.0, r * 0.28), wrap=wrap, albedo=(0.05, 0.05, 0.05), height_add=-1.2, ao_mul=0.5)


# ------------------------------------------------------------------------------------------------ lettering

_GLYPHS = {
    '0': ["00 40 48 08 00", "08 40"], '1': ["11 20 28", "08 48"], '2': ["00 40 44 04 08 48"], '3': ["00 40 48 08", "14 44"],
    '4': ["00 04 44", "40 48"], '5': ["40 00 04 44 48 08"], '6': ["40 00 08 48 44 04"], '7': ["00 40 48"], '8': ["00 40 48 08 00", "04 44"],
    '9': ["44 04 00 40 48 08"], 'A': ["08 00 40 48", "04 44"], 'B': ["00 30 41 43 34 04", "34 45 47 38 08 00"], 'C': ["40 00 08 48"],
    'D': ["00 30 42 46 38 08 00"], 'E': ["40 00 08 48", "04 34"], 'F': ["40 00 08", "04 34"], 'G': ["40 00 08 48 44 24"],
    'H': ["00 08", "40 48", "04 44"], 'I': ["00 40", "20 28", "08 48"], 'J': ["40 48 08 06"], 'K': ["00 08", "40 04 48"], 'L': ["00 08 48"],
    'M': ["08 00 24 40 48"], 'N': ["08 00 48 40"], 'O': ["00 40 48 08 00"], 'P': ["08 00 40 44 04"], 'Q': ["00 40 48 08 00", "26 48"],
    'R': ["08 00 40 44 04", "24 48"], 'S': ["40 00 04 44 48 08"], 'T': ["00 40", "20 28"], 'U': ["00 08 48 40"], 'V': ["00 28 40"],
    'W': ["00 08 24 48 40"], 'X': ["00 48", "40 08"], 'Y': ["00 24 40", "24 28"], 'Z': ["00 40 08 48"], '.': ["27 28"], ',': ["27 18"],
    ':': ["22 23", "25 26"], '-': ["04 44"], '+': ["04 44", "22 26"], '/': ["08 40"], '>': ["00 44 08"], '<': ["40 04 48"], '(': ["30 12 16 38"],
    ')': ["10 32 36 18"], '=': ["13 33", "15 35"], '%': ["08 40", "00 11", "37 48"], "'": ["20 22"], ' ': [],
}
GLYPH_H = 8.0
GLYPH_ADV = 6.0


def text_width(s, height):
    u = height / GLYPH_H
    return (len(s) * GLYPH_ADV - (GLYPH_ADV - 4.0)) * u if s else 0.0


def text(L, s, x, y, height, width=1.2, align="left", wrap=False, **kw):
    """Stroke-font lettering with its top-left at (x, y), ``height`` pixels tall. Returns the width drawn."""
    u = height / GLYPH_H
    w = text_width(s, height)
    if align == "center":
        x -= w * 0.5
    elif align == "right":
        x -= w
    cx = x
    for ch in s.upper():
        for stroke in _GLYPHS.get(ch, []):
            pts = [(int(p[0]) * u + cx, int(p[1]) * u + y) for p in stroke.split(" ")]
            for a, b in zip(pts[:-1], pts[1:]):
                draw_segment(L, a, b, width, wrap=wrap, **kw)
        cx += GLYPH_ADV * u
    return w


def height_normal(h, strength=1.0):
    return mntex.normal_from_height(h, strength)
