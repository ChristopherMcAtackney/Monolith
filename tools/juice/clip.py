#!/usr/bin/env python
"""Turn a numbered series of stills (name_000.png, name_001.png ...) into a clip and a frame strip.

  python tools/juice/clip.py --dir captures/dir --prefix cap --fps 30 --clip cap.mp4 --strip cap_strip.png --count 12 --cols 4 --width 480

The clip is H.264. The strip picks --count frames spread evenly through the series. Needs ffmpeg on PATH.
"""
import argparse
import glob
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", required=True)
    ap.add_argument("--prefix", required=True)
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--clip")
    ap.add_argument("--strip")
    ap.add_argument("--count", type=int, default=12)
    ap.add_argument("--cols", type=int, default=4)
    ap.add_argument("--width", type=int, default=480)
    a = ap.parse_args()
    ff = shutil.which("ffmpeg")
    if not ff:
        raise SystemExit("ffmpeg not found on PATH")
    files = sorted(glob.glob(os.path.join(a.dir, a.prefix + "_[0-9][0-9][0-9].png")))
    if not files:
        raise SystemExit(f"no {a.prefix}_NNN.png frames in {a.dir}")
    if a.clip:
        r = subprocess.run([ff, "-y", "-v", "error", "-framerate", str(a.fps), "-i", os.path.join(a.dir, a.prefix + "_%03d.png"),
                            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "16", a.clip])
        if r.returncode:
            sys.exit(r.returncode)
        print(f"{a.clip}: {len(files)} frames at {a.fps} fps = {len(files) / a.fps:.1f} s")
    if a.strip:
        n = min(a.count, len(files))
        pick = [files[round(i * (len(files) - 1) / max(1, n - 1))] for i in range(n)]
        r = subprocess.run([sys.executable, os.path.join(HERE, "sheet.py"), "--out", a.strip, "--cols", str(a.cols), "--width", str(a.width), "--label"] + pick)
        if r.returncode:
            sys.exit(r.returncode)


if __name__ == "__main__":
    main()
