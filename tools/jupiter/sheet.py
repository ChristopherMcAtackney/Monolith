"""A contact sheet of the stills in a folder, for choosing frames: python -I tools/jupiter/sheet.py <dir> <out.png> [--cols 4] [--width 480] [--prefix cockpit_]"""
import os
import sys

from PIL import Image, ImageDraw

d, out = sys.argv[1], sys.argv[2]
cols = int(sys.argv[sys.argv.index("--cols") + 1]) if "--cols" in sys.argv else 4
w = int(sys.argv[sys.argv.index("--width") + 1]) if "--width" in sys.argv else 480
prefix = sys.argv[sys.argv.index("--prefix") + 1] if "--prefix" in sys.argv else ""
files = sorted(f for f in os.listdir(d) if f.endswith(".png") and f.startswith(prefix))
ims = [(f, Image.open(os.path.join(d, f)).convert("RGB")) for f in files]
if not ims:
    sys.exit("no pngs")
h = int(w * ims[0][1].height / ims[0][1].width)
rows = (len(ims) + cols - 1) // cols
sheet = Image.new("RGB", (cols * w, rows * h), (20, 20, 20))
dr = ImageDraw.Draw(sheet)
for i, (f, im) in enumerate(ims):
    x, y = (i % cols) * w, (i // cols) * h
    sheet.paste(im.resize((w, h), Image.LANCZOS), (x, y))
    dr.text((x + 6, y + 4), f[:-4], fill=(255, 255, 0))
sheet.save(out)
print("sheet", out, len(ims))
