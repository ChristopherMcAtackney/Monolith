"""Build the two moon maps the backdrop can show: Game/Assets/Monolith/Sky/Resources/MonolithSky/Bodies/io_map.jpg and europa_map.jpg

Sources (both USGS Astrogeology, public domain; docs/licences/planets.md):
  Io      Galileo SSI / Voyager colour merged global mosaic, 1 km a pixel; the 1024 x 512 browse image is used as it is (a moon is 10 to 30 pixels across on screen).
  Europa  Voyager / Galileo SSI global mosaic, 500 m a pixel, black and white; the 1024 x 512 image. The 16 black rows at the south pole (no data) are filled with the rows
          above, mirrored and blurred; the grey is given a faint warm white (ours: Europa's ice is white to the eye with brown lines the black and white mosaic cannot show).
Run:  python -I tools/jupiter/fetch_sources.py   then   python -I tools/jupiter/build_moons.py   (Pillow only)
"""
import os
import shutil

from PIL import Image, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "art_src", "jupiter", "downloads")
OUT = os.path.join(ROOT, "Game", "Assets", "Monolith", "Sky", "Resources", "MonolithSky", "Bodies")


def main():
    os.makedirs(OUT, exist_ok=True)
    io = Image.open(os.path.join(SRC, "io_browse_full.jpg")).convert("RGB")
    assert io.size == (1024, 512)
    io.save(os.path.join(OUT, "io_map.jpg"), quality=95, subsampling=0)

    eu = Image.open(os.path.join(SRC, "europa_500m_1024.jpg")).convert("L")
    assert eu.size == (1024, 512)
    px = eu.load()
    first_dark = 512
    while first_dark > 0 and sum(px[x, first_dark - 1] for x in range(0, 1024, 8)) / 128 < 8:
        first_dark -= 1
    print("europa: no data from row", first_dark)
    blur = eu.filter(ImageFilter.GaussianBlur(3))
    for y in range(first_dark, 512):
        k = y - first_dark + 1
        src = max(first_dark - k, 0)
        row = blur.crop((0, src, 1024, src + 1)).transform((1024, 1), Image.AFFINE, (1, 0, 512, 0, 1, 0))     # half way round, so no mirrored copy beside its original
        eu.paste(row, (0, y))
    rgb = Image.merge("RGB", (eu.point(lambda v: min(255, int(v * 1.00))), eu.point(lambda v: int(v * 0.975)), eu.point(lambda v: int(v * 0.94))))
    rgb.save(os.path.join(OUT, "europa_map.jpg"), quality=95, subsampling=0)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
