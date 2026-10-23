#!/usr/bin/env python
"""Contact sheet / frame strip from PNG stills, using ffmpeg's xstack (no Pillow needed).

  python tools/juice/sheet.py --out strip.png --cols 4 --width 480 [--label] frame1.png frame2.png ...

Every input is scaled to --width (aspect kept) and laid out left to right, top to bottom, on black.
--gap pixels of black between cells. Needs ffmpeg on PATH.
"""
import argparse
import shutil
import struct
import subprocess
import sys


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"{path}: not a PNG")
    return struct.unpack(">II", head[16:24])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--cols", type=int, default=4)
    ap.add_argument("--width", type=int, default=480)
    ap.add_argument("--gap", type=int, default=4)
    ap.add_argument("--label", action="store_true", help="burn the file name into each cell")
    ap.add_argument("files", nargs="+")
    a = ap.parse_args()
    ff = shutil.which("ffmpeg")
    if not ff:
        raise SystemExit("ffmpeg not found on PATH")
    w0, h0 = png_size(a.files[0])
    cw = a.width
    ch = round(cw * h0 / w0 / 2) * 2
    n = len(a.files)
    cols = min(a.cols, n)
    rows = (n + cols - 1) // cols
    cmd = [ff, "-y", "-v", "error"]
    for f in a.files:
        cmd += ["-i", f]
    parts = []
    for i in range(n):
        label = ""
        if a.label:
            name = a.files[i].replace("\\", "/").split("/")[-1].rsplit(".", 1)[0].replace(":", "")
            font = r"C\:/Windows/Fonts/arial.ttf"
            label = f",drawtext=fontfile='{font}':text='{name}':x=8:y=6:fontsize={max(12, cw // 40)}:fontcolor=white:box=1:boxcolor=black@0.55:boxborderw=3"
        parts.append(f"[{i}:v]scale={cw}:{ch}:flags=lanczos{label}[s{i}]")
    layout = "|".join(f"{(i % cols) * (cw + a.gap)}_{(i // cols) * (ch + a.gap)}" for i in range(n))
    stack = "".join(f"[s{i}]" for i in range(n)) + f"xstack=inputs={n}:layout={layout}:fill=black"
    cmd += ["-filter_complex", ";".join(parts) + ";" + stack, "-frames:v", "1", a.out]
    r = subprocess.run(cmd)
    if r.returncode:
        sys.exit(r.returncode)
    print(f"{a.out}: {n} frames, {cols}x{rows}, {cols * (cw + a.gap) - a.gap}x{rows * (ch + a.gap) - a.gap}")


if __name__ == "__main__":
    main()
