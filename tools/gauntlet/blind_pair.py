"""Make blind A/B pairs for a gauntlet critic round.

Both images in a pair go through the same pipeline (centre crop to a shared aspect, scale to the same
width, JPEG at the same quality) so format, size and compression give nothing away. Which side is ours
is random and recorded only in gauntlet/keys/<round>.json.

  python -I tools/gauntlet/blind_pair.py <round> <aspect> <ours.png> <ref.jpg> [<ours> <ref> ...]

aspect is width/height, e.g. 1.7778 or 2.0. Reference frames with letterbox bars should be cropped by
the aspect choice (2.0 removes typical 2:1 bars from a 16:9 frame).
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
    rnd, aspect, files = sys.argv[1], float(sys.argv[2]), sys.argv[3:]
    if len(files) % 2:
        sys.exit("give ours/ref pairs")
    blind = os.path.join(ROOT, "gauntlet", "blind", rnd)
    keys = os.path.join(ROOT, "gauntlet", "keys")
    os.makedirs(blind, exist_ok=True)
    os.makedirs(keys, exist_ok=True)
    key = {"round": rnd, "aspect": aspect, "pairs": []}
    for i in range(0, len(files), 2):
        n = i // 2 + 1
        ours, ref = files[i], files[i + 1]
        ours_side = random.choice("AB")
        ref_side = "B" if ours_side == "A" else "A"
        export(ours, os.path.join(blind, f"pair{n}_{ours_side}.jpg"), aspect)
        export(ref, os.path.join(blind, f"pair{n}_{ref_side}.jpg"), aspect)
        key["pairs"].append({"pair": n, "ours": ours_side, "ours_file": ours, "ref_file": ref})
    with open(os.path.join(keys, f"{rnd}.json"), "w", encoding="utf-8") as f:
        json.dump(key, f, indent=2)
    print(f"{len(key['pairs'])} pairs in {blind}")


if __name__ == "__main__":
    main()
