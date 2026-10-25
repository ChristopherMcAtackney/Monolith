r"""A before/after sheet of the comets' captures: one row a framing, the baseline on the left and this build on the right, with the framing's name.

    python -I tools/ship/make_comets_sheet.py <before dir> <after dir> <out.png> [--names a,b,c] [--width 760] [--cols 1]
    python -I tools/ship/make_comets_sheet.py --grid <dir> <out.png> [--names a,b,c] [--width 640] [--cols 3]       (one directory's stills in a grid: a look-development contact sheet)

Without --names it takes the four framings of the brief (start view, big at 2 km, small at 200 m, close pass at 50 m) in order.
"""
import argparse
import os

from PIL import Image, ImageDraw, ImageFont

BRIEF = ["start_cockpit", "start_view", "big_2km_a", "small_200m_a", "pass_50m_a"]


def font(size):
    for f in ("C:/Windows/Fonts/consola.ttf", "C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def tile(path, w):
    im = Image.open(path).convert("RGB")
    h = int(round(im.height * w / im.width))
    return im.resize((w, h), Image.LANCZOS)


def label(im, text):
    d = ImageDraw.Draw(im)
    f = font(18)
    d.rectangle([0, 0, 12 + 10 * len(text), 28], fill=(0, 0, 0))
    d.text((6, 4), text, fill=(235, 235, 235), font=f)
    return im


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--grid", action="store_true")
    ap.add_argument("--names", default=None)
    ap.add_argument("--width", type=int, default=760)
    ap.add_argument("--cols", type=int, default=0)
    a = ap.parse_args()
    names = a.names.split(",") if a.names else None
    if a.grid:
        d, out = a.paths
        names = names or sorted(os.path.splitext(f)[0] for f in os.listdir(d) if f.endswith(".png"))
        cols = a.cols or 3
        tiles = [label(tile(os.path.join(d, n + ".png"), a.width), n) for n in names]
        th = tiles[0].height
        rows = (len(tiles) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * a.width + (cols + 1) * 4, rows * th + (rows + 1) * 4), (24, 24, 24))
        for i, t in enumerate(tiles):
            sheet.paste(t, (4 + (i % cols) * (a.width + 4), 4 + (i // cols) * (th + 4)))
        sheet.save(out)
        print("wrote", out, sheet.size)
        return
    before, after, out = a.paths
    names = names or BRIEF
    rows = []
    for n in names:
        b, c = tile(os.path.join(before, n + ".png"), a.width), tile(os.path.join(after, n + ".png"), a.width)
        rows.append((n, label(b, "before: " + n), label(c, "after: " + n)))
    th = rows[0][1].height
    sheet = Image.new("RGB", (2 * a.width + 12, len(rows) * (th + 4) + 4), (24, 24, 24))
    for i, (n, b, c) in enumerate(rows):
        y = 4 + i * (th + 4)
        sheet.paste(b, (4, y))
        sheet.paste(c, (8 + a.width, y))
    sheet.save(out)
    print("wrote", out, sheet.size)


main()
