#!/usr/bin/env python
"""Contact sheet of the explosion flipbook library (what each atlas holds, lit roughly as the engine does).

  python tools/juice/flip_sheet.py --out library.png [--frames 0,4,8,12,18,24,30,35] [--width 256] [variant ...]

Reads Game/Assets/Monolith/Resources/Juice/Flip/<variant>.png and .json. One row per variant, one column per frame; the fire goes through the same temperature ramp as
the shader, the smoke is a grey lit from the upper left by its own slope. A guide to what is in the atlases, not a render of the game.
"""
import argparse
import json
import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
FLIP = os.path.join(ROOT, 'Game', 'Assets', 'Monolith', 'Resources', 'Juice', 'Flip')


def ramp(T):
    r = min(max(T * 2.6, 0), 1)
    g = min(max((T - 0.30) * 1.55, 0), 1)
    b = min(max((T - 0.78) * 1.9, 0), 1)
    lum = 0.10 + T * T * 1.25
    return r * lum, g * lum, b * lum


def tone(v):
    v = v * 1.7
    v = v / (1 + v)
    return int(255 * min(1, v ** (1 / 2.2)))


def render_tile(tile, heat=1.15):
    r, g, b, a = tile.split()
    lut = [[0] * 256 for _ in range(3)]
    for i in range(256):
        e = (i / 255.0) ** 2
        T = e * heat
        rr, gg, bb = ramp(T)
        lut[0][i], lut[1][i], lut[2][i] = tone(rr), tone(gg), tone(bb)
    fire = Image.merge('RGB', [r.point(lut[0]), r.point(lut[1]), r.point(lut[2])])
    # smoke: opacity from G; a lit grey from the slope (B = x slope, A = y slope)
    alpha = g.point([int(255 * (1 - math.exp(-3.2 * i / 255.0))) for i in range(256)])
    lit = ImageChops.add(b.point(lambda v: int(max(0, (v - 128) * -1.0 + 70))), a.point(lambda v: int(max(0, (v - 128) * 1.0 + 70))))
    lit = lit.point(lambda v: min(255, int(v * 0.8 + 38)))
    smoke = Image.merge('RGB', [lit, lit, lit])
    out = Image.composite(smoke, fire, alpha.point(lambda v: v // 2))
    return ImageChops.add(out, fire.point(lambda v: v // 2))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('variants', nargs='*')
    ap.add_argument('--out', required=True)
    ap.add_argument('--frames', default='0,4,8,12,18,24,30,35')
    ap.add_argument('--width', type=int, default=256)
    a = ap.parse_args()
    names = a.variants or sorted(f[:-5] for f in os.listdir(FLIP) if f.endswith('.json'))
    cols = [int(x) for x in a.frames.split(',')]
    w = a.width
    sheet = Image.new('RGB', (len(cols) * w, len(names) * w), (0, 0, 0))
    d = ImageDraw.Draw(sheet)
    for row, name in enumerate(names):
        meta = json.load(open(os.path.join(FLIP, name + '.json')))
        atlas = Image.open(os.path.join(FLIP, name + '.png')).convert('RGBA')
        g, fp = meta['grid'], meta['frame']
        for col, k in enumerate(cols):
            k = min(k, meta['frames'] - 1)
            gy, gx = divmod(k, g)
            tile = atlas.crop((gx * fp, gy * fp, (gx + 1) * fp, (gy + 1) * fp))
            img = render_tile(tile).resize((w, w), Image.LANCZOS)
            sheet.paste(img, (col * w, row * w))
            d.text((col * w + 4, row * w + 4), f"{name} {k} ({meta['times'][k]:.2f}s)", fill=(200, 200, 200))
    sheet.save(a.out)
    print(f"{a.out}: {len(names)} variants x {len(cols)} frames")


if __name__ == '__main__':
    main()
