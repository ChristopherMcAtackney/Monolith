#!/usr/bin/env python
"""Bake a signed-distance-field font atlas for the HUD from a TrueType file (SIL Open Font License fonts only: see docs/licences/fonts.md).

  python -I tools/hud/make_font_atlas.py --font "art_src/hud_fonts/Oxanium/Oxanium[wght].ttf" --weight 500 --id oxanium --out Game/Assets/Monolith/Resources/Fonts

Writes  <id>.sdf.bytes      raw 8-bit atlas, atlasW x atlasH, row 0 at the TOP, 128 = the glyph's edge, 255 = `spread` pixels inside it, 0 = `spread` pixels outside it
        <id>.fontmeta.json  the metrics (see HudFont.cs): every glyph's cell in the atlas, its advance and bearings, all in atlas pixels at `size` pixels to the em.

Needs only Pillow (no numpy): the distance transform is Felzenszwalb's, in pure Python, spread over the CPU's cores. A font bakes in a minute or two;
the result is committed, so this runs only when a font or the character set changes.
"""
import argparse
import json
import math
import multiprocessing as mp
import os
import sys

from PIL import Image, ImageDraw, ImageFont

INF = 1e20

# ASCII, then the extras the HUD may print. A character the font lacks is skipped (the HUD falls back to "?").
EXTRA = "°·×–—…‘’“”«»±≈→←↑↓•▲▼■□◆◇−½"
CHARS = "".join(chr(c) for c in range(32, 127)) + EXTRA


def edt1d(f, n, v, z, d):
    k = 0
    v[0] = 0
    z[0] = -INF
    z[1] = INF
    for q in range(1, n):
        while True:
            p = v[k]
            s = ((f[q] + q * q) - (f[p] + p * p)) / (2.0 * q - 2.0 * p)
            if s <= z[k]:
                k -= 1
            else:
                break
        k += 1
        v[k] = q
        z[k] = s
        z[k + 1] = INF
    k = 0
    for q in range(n):
        while z[k + 1] < q:
            k += 1
        p = v[k]
        d[q] = (q - p) * (q - p) + f[p]


def edt2d(mask, w, h):
    """Squared distance from every pixel to the nearest pixel where mask is True (a flat list, row-major)."""
    f = [0.0 if m else INF for m in mask]
    n = max(w, h)
    v = [0] * n
    z = [0.0] * (n + 1)
    d = [0.0] * n
    col = [0.0] * h
    for x in range(w):
        for y in range(h):
            col[y] = f[y * w + x]
        edt1d(col, h, v, z, d)
        for y in range(h):
            f[y * w + x] = d[y]
    row = [0.0] * w
    for y in range(h):
        base = y * w
        for x in range(w):
            row[x] = f[base + x]
        edt1d(row, w, v, z, d)
        for x in range(w):
            f[base + x] = d[x]
    return f


def load_font(path, size, weight):
    f = ImageFont.truetype(path, size)
    if weight is not None:
        try:
            f.set_variation_by_axes([weight])
        except Exception:
            pass
    return f


def bake_glyph(args):
    path, weight, size, spread, ss, ch = args
    hi = load_font(path, size * ss, weight)
    adv = hi.getlength(ch) / ss
    if ch == " ":
        return ch, adv, None
    l, t, r, b = hi.getbbox(ch, anchor="ls")
    if r <= l or b <= t:
        return ch, adv, None
    pad = spread * ss + ss
    x0 = (l // ss) * ss - pad
    y0 = (t // ss) * ss - pad
    x1 = -(-r // ss) * ss + pad
    y1 = -(-b // ss) * ss + pad
    w, h = x1 - x0, y1 - y0
    img = Image.new("L", (w, h), 0)
    ImageDraw.Draw(img).text((-x0, -y0), ch, font=hi, fill=255, anchor="ls")
    px = list(img.getdata())
    inside = [p >= 128 for p in px]
    outside = [not i for i in inside]
    d_to_in = edt2d(inside, w, h)       # for outside pixels: distance to the nearest inside pixel
    d_to_out = edt2d(outside, w, h)     # for inside pixels: distance to the nearest outside pixel
    cw, chh = w // ss, h // ss
    cell = bytearray(cw * chh)
    for cy in range(chh):
        for cx in range(cw):
            # the centre sample of the ss x ss block: a distance between pixel centres, less half a pixel to reach the edge
            sx = cx * ss + ss // 2
            sy = cy * ss + ss // 2
            i = sy * w + sx
            if inside[i]:
                sd = (math.sqrt(d_to_out[i]) - 0.5) / ss
            else:
                sd = -(math.sqrt(d_to_in[i]) - 0.5) / ss
            v = 0.5 + sd / (2.0 * spread)
            cell[cy * cw + cx] = max(0, min(255, int(round(v * 255.0))))
    # bearings in atlas pixels (y up from the baseline): the cell's left and top edges
    bx = x0 / ss
    by = -y0 / ss
    return ch, adv, (bytes(cell), cw, chh, bx, by)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--font", required=True)
    ap.add_argument("--weight", type=float, default=None, help="variable font: the weight axis value")
    ap.add_argument("--id", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--size", type=int, default=64, help="pixels to the em in the atlas")
    ap.add_argument("--spread", type=int, default=10, help="pixels of distance stored each side of the edge")
    ap.add_argument("--ss", type=int, default=3, help="supersampling of the shape the distance is measured on")
    ap.add_argument("--atlas", type=int, nargs=2, default=[1024, 512])
    ap.add_argument("--family", default="")
    ap.add_argument("--licence", default="SIL Open Font License 1.1")
    ap.add_argument("--source", default="")
    a = ap.parse_args()

    jobs = [(a.font, a.weight, a.size, a.spread, a.ss, ch) for ch in CHARS]
    with mp.Pool(max(1, (os.cpu_count() or 2) - 1)) as pool:
        results = pool.map(bake_glyph, jobs, chunksize=2)

    # drop characters the font does not draw (they come out as the .notdef box)
    hi = load_font(a.font, a.size * a.ss, a.weight)
    notdef = hi.getmask("￿")
    nd_bytes = bytes(notdef)

    items = []
    for ch, adv, g in results:
        if g is not None and ord(ch) > 126:
            m = hi.getmask(ch)
            if m.size == notdef.size and bytes(m) == nd_bytes:
                print("skip (not in the font):", ascii(ch))
                continue
        items.append((ch, adv, g))
    drawn = sorted([it for it in items if it[2] is not None], key=lambda it: -it[2][2])
    aw, ah = a.atlas
    atlas = bytearray(aw * ah)         # 0 = far outside: the atlas starts empty
    x = y = rowh = 0
    gap = 1
    glyphs = {}
    for ch, adv, (cell, cw, chh, bx, by) in drawn:
        if x + cw + gap > aw:
            x = 0
            y += rowh + gap
            rowh = 0
        if y + chh > ah:
            sys.exit("atlas too small: use --atlas 1024 2048")
        for r in range(chh):
            atlas[(y + r) * aw + x:(y + r) * aw + x + cw] = cell[r * cw:(r + 1) * cw]
        glyphs[ch] = {"x": x, "y": y, "w": cw, "h": chh, "adv": round(adv, 3), "bx": round(bx, 3), "by": round(by, 3)}
        x += cw + gap
        rowh = max(rowh, chh)
    for ch, adv, g in items:
        if g is None:
            glyphs[ch] = {"x": 0, "y": 0, "w": 0, "h": 0, "adv": round(adv, 3), "bx": 0, "by": 0}

    # vertical metrics, measured on the real shapes
    def top(chars):
        return max(-hi.getbbox(c, anchor="ls")[1] / a.ss for c in chars)
    cap = top("HEIT")
    xh = top("xzvw")
    asc, desc = hi.getmetrics()
    meta = {
        "format": "monolith-hud-font/1", "id": a.id, "family": a.family, "licence": a.licence, "source": a.source, "weight": a.weight,
        "size": a.size, "spread": a.spread, "atlasW": aw, "atlasH": ah,
        "ascent": round(asc / a.ss, 3), "descent": round(desc / a.ss, 3), "capHeight": round(cap, 3), "xHeight": round(xh, 3),
        "glyphs": glyphs,
    }
    os.makedirs(a.out, exist_ok=True)
    with open(os.path.join(a.out, a.id + ".sdf.bytes"), "wb") as f:
        f.write(bytes(atlas))
    with open(os.path.join(a.out, a.id + ".fontmeta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, separators=(",", ":"))
    prev = os.path.join(os.environ.get("TEMP", "."), a.id + "_atlas_preview.png")
    Image.frombytes("L", (aw, ah), bytes(atlas)).save(prev)
    print(f"{a.id}: {len(glyphs)} glyphs, atlas used to row {y + rowh} of {ah}, cap height {cap:.1f}, x-height {xh:.1f}, ascent {asc/a.ss:.1f}, descent {desc/a.ss:.1f}; preview {prev}")


if __name__ == "__main__":
    mp.freeze_support()
    main()
