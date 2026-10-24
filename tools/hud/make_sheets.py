#!/usr/bin/env python
"""Side-by-side sheets of the HUD captures: one image per moment with every style next to the others, labelled.

  python -I tools/hud/make_sheets.py --root gauntlet/captures/hud/r1 --styles halo,meridian,cipher [--width 960]

Reads <root>/<style>/<moment>.png, writes <root>/sheets/<moment>.png (the styles in a row) and <root>/sheets/_all.png (every moment, one row each).
"""
import argparse
import os
from PIL import Image, ImageDraw, ImageFont


def font(size):
    for f in ("C:/Windows/Fonts/segoeui.ttf", "C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True)
    ap.add_argument("--styles", default="halo,meridian,cipher")
    ap.add_argument("--width", type=int, default=960)
    a = ap.parse_args()
    styles = a.styles.split(",")
    moments = sorted({f[:-4] for s in styles for f in os.listdir(os.path.join(a.root, s)) if f.endswith(".png")}) if all(os.path.isdir(os.path.join(a.root, s)) for s in styles) else []
    out = os.path.join(a.root, "sheets")
    os.makedirs(out, exist_ok=True)
    lab, gap = 34, 6
    fl = font(20)
    rows = []
    for m in moments:
        imgs = []
        for s in styles:
            p = os.path.join(a.root, s, m + ".png")
            if os.path.exists(p):
                im = Image.open(p).convert("RGB")
                h = int(im.height * a.width / im.width)
                imgs.append((s, im.resize((a.width, h), Image.LANCZOS)))
        if not imgs:
            continue
        w = len(imgs) * a.width + (len(imgs) - 1) * gap
        h = imgs[0][1].height + lab
        sheet = Image.new("RGB", (w, h), (12, 12, 14))
        d = ImageDraw.Draw(sheet)
        for i, (s, im) in enumerate(imgs):
            x = i * (a.width + gap)
            sheet.paste(im, (x, lab))
            d.text((x + 10, 6), f"{s.upper()}   {m}", font=fl, fill=(230, 230, 230))
        sheet.save(os.path.join(out, m + ".png"))
        rows.append(sheet)
    if rows:
        W = max(r.width for r in rows)
        H = sum(r.height for r in rows) + gap * (len(rows) - 1)
        allimg = Image.new("RGB", (W, H), (12, 12, 14))
        y = 0
        for r in rows:
            allimg.paste(r, (0, y))
            y += r.height + gap
        allimg.save(os.path.join(out, "_all.png"))
    print(f"{len(rows)} sheets in {out}")


if __name__ == "__main__":
    main()
