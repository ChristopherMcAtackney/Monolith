"""Build the Jupiter albedo map the game ships: Game/Assets/Monolith/Sky/Resources/MonolithSky/Bodies/jupiter_map.jpg

Source: NASA/JPL/Space Science Institute, "Cassini's Best Maps of Jupiter (Cylindrical Map)", PIA07782 (public domain, see docs/licences/planets.md).
3601 x 1801 pixels, 0.1 degree each, 360 degrees of longitude by 180 of latitude, true-ish colour, from Cassini's flyby of December 2000.

Run:  python -I tools/jupiter/fetch_sources.py     (downloads the TIFF into art_src/jupiter/downloads, git-ignored)
      python -I tools/jupiter/build_map.py [--size 8192]      (Pillow only)

What it does, in order:
  1. Fills the two ends of the map that Cassini never saw clearly: rows above 88.4 N are a flat grey, and everything south of 82.2 S is a flat grey.
     The fill is the nearest real rows, mirrored across the edge and shifted half way round, blurred more and more toward the pole until it is the
     local mean colour, so the cap is a soft haze and not a flat grey disc. (The poles are only ever seen at a grazing angle and barely at all.)
  2. Drops the wrap column (the map repeats its first column as its last), so the texture wraps without a seam.
  3. Resamples to the output size with Lanczos (the map is 0.1 degree a pixel; 8192 x 4096 is 0.05).
  4. Writes a JPEG (quality 95, 4:4:4). Unity re-compresses it (BC7) on import.
The detail that goes beyond the map's own resolution is added by the sky shader at run time, fading in only where a texel is bigger than a pixel.
"""
import os
import sys

from PIL import Image, ImageFilter, ImageChops

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "art_src", "jupiter", "downloads", "PIA07782.tif")
OUT = os.path.join(ROOT, "Game", "Assets", "Monolith", "Sky", "Resources", "MonolithSky", "Bodies", "jupiter_map.jpg")

TOP_PAD_ROWS = 16          # rows 0..15 are flat padding (above 88.4 N)
BOTTOM_FIRST_PAD = 1722    # rows 1722..1800 are flat padding (below 82.2 S)


def blurred_wrap(img, radius):
    """Gaussian blur that wraps around horizontally (the map is a cylinder)."""
    w, h = img.size
    pad = min(w // 2, int(radius * 4) + 2)
    wide = Image.new("RGB", (w + 2 * pad, h))
    wide.paste(img.crop((w - pad, 0, w, h)), (0, 0))
    wide.paste(img, (pad, 0))
    wide.paste(img.crop((0, 0, pad, h)), (pad + w, 0))
    wide = wide.filter(ImageFilter.GaussianBlur(radius))
    return wide.crop((pad, 0, pad + w, h))


def fill_poles(img):
    w, h = img.size
    img = img.copy()
    # Cap rows: for each padded row, take the mirrored real row, shifted half way round, with a blur that grows toward the pole.
    radii = [0.0, 3.0, 8.0, 20.0, 45.0, 110.0]
    blurs = [img if r == 0.0 else blurred_wrap(img, r) for r in radii]
    mean = img.crop((0, BOTTOM_FIRST_PAD - 120, w, BOTTOM_FIRST_PAD - 1)).resize((1, 1), Image.BOX).getpixel((0, 0))
    meanTop = img.crop((0, TOP_PAD_ROWS, w, TOP_PAD_ROWS + 120)).resize((1, 1), Image.BOX).getpixel((0, 0))

    def rows(first, last, edge, direction, polar_mean):
        n = last - first + 1
        for i, y in enumerate(range(first, last + 1)):
            t = (i + 1) / n                                    # 0 at the real edge, 1 at the pole
            src_y = edge - direction * (i + 1)                 # mirrored real row
            src_y = max(TOP_PAD_ROWS, min(BOTTOM_FIRST_PAD - 1, src_y))
            k = t * (len(radii) - 1)
            a = int(k); b = min(a + 1, len(radii) - 1); f = k - a
            row_a = blurs[a].crop((0, src_y, w, src_y + 1))
            row_b = blurs[b].crop((0, src_y, w, src_y + 1))
            row = Image.blend(row_a, row_b, f)
            row = ImageChops.offset(row, w // 2, 0)           # half way round: no mirrored copy of the features beside it
            row = Image.blend(row, Image.new("RGB", (w, 1), polar_mean), t ** 1.5 * 0.7)
            img.paste(row, (0, y))

    rows(BOTTOM_FIRST_PAD, h - 1, BOTTOM_FIRST_PAD - 1, 1, mean)
    rows(0, TOP_PAD_ROWS - 1, TOP_PAD_ROWS, -1, meanTop)
    # The seam between the real rows and the fill: a short vertical blur across it.
    for edge in (TOP_PAD_ROWS, BOTTOM_FIRST_PAD):
        band = img.crop((0, edge - 5, w, edge + 5)).filter(ImageFilter.GaussianBlur(1.6))
        img.paste(Image.blend(img.crop((0, edge - 5, w, edge + 5)), band, 0.8), (0, edge - 5))
    return img


def main():
    size = 8192
    if "--size" in sys.argv:
        size = int(sys.argv[sys.argv.index("--size") + 1])
    im = Image.open(SRC).convert("RGB")
    w, h = im.size
    assert (w, h) == (3601, 1801), (w, h)
    # The first and last columns are the same meridian.
    diff = ImageChops.difference(im.crop((0, 0, 1, h)), im.crop((w - 1, 0, w, h))).getbbox()
    print("wrap column identical:", diff is None)
    im = im.crop((0, 0, 3600, h))
    im = fill_poles(im)
    im = im.resize((size, size // 2), Image.LANCZOS)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    im.save(OUT, quality=95, subsampling=0, optimize=True)
    print("wrote", OUT, im.size, round(os.path.getsize(OUT) / 1048576, 2), "MB")


if __name__ == "__main__":
    main()
