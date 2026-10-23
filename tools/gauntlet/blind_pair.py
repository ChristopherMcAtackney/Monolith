"""Make blind A/B pairs for a gauntlet critic round.

Both images in a pair go through the same pipeline (centre crop to a shared aspect, scale to the same
width, JPEG at the same quality) so format, size and compression give nothing away. Which side is ours
is random and recorded only in gauntlet/keys/<round>.json.

  python -I tools/gauntlet/blind_pair.py <round> <aspect> <ours.png> <ref.jpg> [<aspect> <ours> <ref> ...]

aspect is width/height, e.g. 1.7778 or 2.0, given per pair. Reference frames with letterbox bars should
be cropped by the aspect choice (2.0 removes typical 2:1 bars from a 16:9 frame).
"""
import json
import os
import random
import subprocess
import sys

FFMPEG = r"C:\Users\chris\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe"
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WIDTH = 1280


def export(src, dst, aspect):
    # crop the largest centred box of the given aspect, then scale to WIDTH, then JPEG q3
    vf = (f"crop='min(iw,ih*{aspect})':'min(ih,iw/{aspect})',"
          f"scale={WIDTH}:-2,setsar=1")
    subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", src,
                    "-vf", vf, "-q:v", "3", dst], check=True)


def main():
    rnd, rest = sys.argv[1], sys.argv[2:]
    if not rest or len(rest) % 3:
        sys.exit("give <aspect> <ours> <ref> triples")
    blind = os.path.join(ROOT, "gauntlet", "blind", rnd)
    keys = os.path.join(ROOT, "gauntlet", "keys")
    os.makedirs(blind, exist_ok=True)
    os.makedirs(keys, exist_ok=True)
    key = {"round": rnd, "pairs": []}
    for i in range(0, len(rest), 3):
        n = i // 3 + 1
        aspect, ours, ref = float(rest[i]), rest[i + 1], rest[i + 2]
        ours_side = random.choice("AB")
        ref_side = "B" if ours_side == "A" else "A"
        export(ours, os.path.join(blind, f"pair{n}_{ours_side}.jpg"), aspect)
        export(ref, os.path.join(blind, f"pair{n}_{ref_side}.jpg"), aspect)
        key["pairs"].append({"pair": n, "aspect": aspect, "ours": ours_side,
                             "ours_file": ours, "ref_file": ref})
    with open(os.path.join(keys, f"{rnd}.json"), "w", encoding="utf-8") as f:
        json.dump(key, f, indent=2)
    print(f"{len(key['pairs'])} pairs in {blind}")


if __name__ == "__main__":
    main()
