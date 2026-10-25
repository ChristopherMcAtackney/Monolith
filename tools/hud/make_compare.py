#!/usr/bin/env python
"""Before and after, side by side: the same HUD moments from two captures (tools/hud/capture_moments.ps1 into two roots).

  python -I tools/hud/make_compare.py --before gauntlet/captures/hud/r2/before --after gauntlet/captures/hud/r2/after --out gauntlet/captures/hud/r2/compare [--width 960]

Reads <root>/<style>/<moment>.png in each root. Writes, in <out>:
  <moment>.png          one image per moment: a row for each style, the before on the left and the after on the right, scaled to --width each
  pairs/<style>_<moment>.png   the same pair at full size (before | after), for judging the type
  diff.txt              for each pair, the share of the screen that changed (the pause screen and the battle map keep the old size and strength, so those pairs read identical)
"""
import argparse
import os

from PIL import Image, ImageChops, ImageDraw, ImageFont


def font(size):
    for f in ("C:/Windows/Fonts/segoeui.ttf", "C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--before", required=True)
    ap.add_argument("--after", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--styles", default="halo,meridian,cipher")
    ap.add_argument("--width", type=int, default=960)
    a = ap.parse_args()
    styles = a.styles.split(",")
    os.makedirs(os.path.join(a.out, "pairs"), exist_ok=True)
    moments = sorted({f[:-4] for s in styles for f in os.listdir(os.path.join(a.before, s)) if f.endswith(".png")})
    fl, fs = font(22), font(16)
    lab, gap = 30, 6
    report = []
    for m in moments:
        rows = []
        for s in styles:
            pb, pa = os.path.join(a.before, s, m + ".png"), os.path.join(a.after, s, m + ".png")
            if not (os.path.exists(pb) and os.path.exists(pa)):
                continue
            ib, ia = Image.open(pb).convert("RGB"), Image.open(pa).convert("RGB")
            # full-size pair
            pair = Image.new("RGB", (ib.width * 2 + gap, ib.height + lab), (12, 12, 14))
            pair.paste(ib, (0, lab)); pair.paste(ia, (ib.width + gap, lab))
            d = ImageDraw.Draw(pair)
            d.text((10, 4), f"BEFORE   {s.upper()}   {m}", font=fl, fill=(230, 230, 230))
            d.text((ib.width + gap + 10, 4), f"AFTER   {s.upper()}   {m}", font=fl, fill=(230, 230, 230))
            pair.save(os.path.join(a.out, "pairs", f"{s}_{m}.png"))
            h = int(ib.height * a.width / ib.width)
            rows.append((s, ib.resize((a.width, h), Image.LANCZOS), ia.resize((a.width, h), Image.LANCZOS)))
            # how much of the screen changed, and how bright the changed pixels were
            diff = ImageChops.difference(ib, ia).convert("L")
            changed = sum(1 for v in diff.getdata() if v > 12) / (ib.width * ib.height)
            report.append(f"{s:9s} {m:18s} changed {changed * 100:5.2f}% of the screen" + ("   (identical)" if diff.getbbox() is None else ""))
        if not rows:
            continue
        w = a.width * 2 + gap
        sheet = Image.new("RGB", (w, sum(r[1].height + lab for r in rows)), (12, 12, 14))
        y = 0
        d = ImageDraw.Draw(sheet)
        for s, ib, ia in rows:
            d.text((10, y + 4), f"BEFORE   {s.upper()}   {m}", font=fs, fill=(230, 230, 230))
            d.text((a.width + gap + 10, y + 4), f"AFTER   {s.upper()}   {m}", font=fs, fill=(230, 230, 230))
            sheet.paste(ib, (0, y + lab)); sheet.paste(ia, (a.width + gap, y + lab))
            y += ib.height + lab
        sheet.save(os.path.join(a.out, m + ".png"))
    with open(os.path.join(a.out, "diff.txt"), "w") as f:
        f.write("\n".join(report) + "\n")
    print(f"{len(moments)} moments, {len(report)} pairs in {a.out}")


if __name__ == "__main__":
    main()
