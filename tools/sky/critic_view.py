"""Make critic-sized copies of captures (1280 wide, JPEG quality 3) and report how much noise survives in their dark areas.

Run:  python -I tools/sky/critic_view.py <capture folder> [name ...]    (all pngs if no names; writes <folder>/critic/<name>.jpg)
The blind critic sees frames scaled to 1280 wide and saved as JPEG (ffmpeg -q:v 3), so judge them at that size, not at 1920. For each frame
it prints the standard deviation of the luma in a dark patch (the darkest 120 x 80 block, found on a coarse grid), before and after.
Needs ffmpeg (WinGet install path below, or on PATH). Standard library plus Pillow.
"""
import glob
import os
import shutil
import statistics
import subprocess
import sys

from PIL import Image

FFMPEG = os.environ.get("FFMPEG") or shutil.which("ffmpeg") or os.path.expandvars(
    r"%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe")


def darkest_patch_std(path, w=120, h=80):
    im = Image.open(path).convert("L")
    W, H = im.size
    px = im.load()
    best = None
    for y0 in range(0, H - h, 40):
        for x0 in range(0, W - w, 60):
            vals = [px[x, y] for y in range(y0, y0 + h, 2) for x in range(x0, x0 + w, 2)]
            m = statistics.mean(vals)
            if best is None or m < best[0]:
                best = (m, statistics.pstdev(vals))
    return best


def main():
    folder = sys.argv[1]
    names = sys.argv[2:] or [os.path.splitext(os.path.basename(p))[0] for p in sorted(glob.glob(os.path.join(folder, "*.png")))]
    out = os.path.join(folder, "critic")
    os.makedirs(out, exist_ok=True)
    for n in names:
        src = os.path.join(folder, n + ".png")
        dst = os.path.join(out, n + ".jpg")
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-i", src, "-vf", "scale=1280:-2", "-q:v", "3", dst], check=True)
        a = darkest_patch_std(src)
        b = darkest_patch_std(dst)
        print(f"{n}: darkest block mean {a[0]:.1f} std {a[1]:.2f}  ->  at 1280 jpeg: mean {b[0]:.1f} std {b[1]:.2f}")


if __name__ == "__main__":
    main()
