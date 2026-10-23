"""Measure how stable the stars are across a slow turn: shimmer (brightness changing as a star slides over pixels) and crawl (the
star position wandering off a straight line).

Run:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\blender.ps1 tools\sky\analyze_shimmer.py -- <frames folder> [prefix] [step_px]
  frames folder  e.g. gauntlet\captures\sky\r1      prefix default "turn_slow_"      step_px default 0.1295 (0.005 degrees at 40 degrees vertical FOV, 1080p)
Writes <frames folder>\shimmer.json and prints [MN] lines. (Blender is only used because its bundled Python has numpy and an image reader.)

Method: find the stars in the first frame (local maxima above 40/255 that are alone in their 11x11 neighbourhood), follow each through the
sequence by looking for the brightest pixel near where it should be, and per frame measure
  energy   the star's light summed over a 9x9 window (decoded to linear, background removed)
  peak     the brightest pixel
  centroid the light-weighted position
A star that is a stable point has constant energy; one that shimmers does not. Crawl is the scatter of the centroid around the straight
line it should follow. Both are reported as medians and 95th percentiles over the stars.
"""
import glob
import json
import os
import sys

import bpy
import numpy as np


def load_frames(folder, prefix):
    folder = os.path.abspath(folder)
    files = sorted(glob.glob(os.path.join(folder, prefix + "*.png")))
    if not files:
        raise SystemExit(f"no {prefix}*.png in {folder}")
    frames = []
    for f in files:
        img = bpy.data.images.load(f)
        img.colorspace_settings.name = "Non-Color"      # raw encoded values, no colour management
        w, h = img.size
        a = np.empty(w * h * img.channels, np.float32)
        img.pixels.foreach_get(a)
        a = a.reshape(h, w, img.channels)[..., :3][::-1]    # row 0 = top
        frames.append(a)
        bpy.data.images.remove(img)
    return files, frames


def to_linear_luma(a):
    lin = np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)
    return lin @ np.array([0.2126, 0.7152, 0.0722], np.float32)


def local_maxima(lum, threshold, margin=24):
    h, w = lum.shape
    cand = []
    core = lum[margin:h - margin, margin:w - margin]
    ys, xs = np.nonzero(core > threshold)
    for y, x in zip(ys + margin, xs + margin):
        win = lum[y - 1:y + 2, x - 1:x + 2]
        if lum[y, x] < win.max():
            continue
        # alone in an 11x11 box: nothing else more than a quarter as bright
        box = lum[y - 5:y + 6, x - 5:x + 6].copy()
        box[4:7, 4:7] = 0
        if box.max() > 0.25 * lum[y, x]:
            continue
        cand.append((y, x))
    return cand


def measure(lum, y, x, r=4):
    win = lum[y - r:y + r + 1, x - r:x + r + 1]
    border = np.concatenate([win[0], win[-1], win[:, 0], win[:, -1]])
    bg = np.median(border)
    light = np.clip(win - bg, 0, None)
    e = light.sum()
    if e <= 0:
        return None
    yy, xx = np.mgrid[-r:r + 1, -r:r + 1]
    return e, win.max() - bg, x + (light * xx).sum() / e, y + (light * yy).sum() / e


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        raise SystemExit("usage: analyze_shimmer.py -- <folder> [prefix] [step_px]")
    folder = os.path.abspath(args[0])
    prefix = args[1] if len(args) > 1 else "turn_slow_"
    step = float(args[2]) if len(args) > 2 else 0.1295
    files, frames = load_frames(folder, prefix)
    lums = [to_linear_luma(f) for f in frames]
    n = len(lums)

    # 8-bit threshold 40/255 in linear light is about 0.021
    stars = local_maxima(lums[0], 0.021)
    tracks = []
    for (y0, x0) in stars:
        x_exp, ok = float(x0), True
        rec = []
        for i in range(n):
            # the camera yaws right, so the sky slides left by `step` px per frame (near the centre of the frame)
            xe = x0 - step * i
            yi, xi = y0, int(round(xe))
            win = lums[i][yi - 2:yi + 3, xi - 2:xi + 3]
            if win.size == 0:
                ok = False
                break
            dy, dx = np.unravel_index(np.argmax(win), win.shape)
            yi, xi = yi - 2 + dy, xi - 2 + dx
            m = measure(lums[i], yi, xi)
            if m is None:
                ok = False
                break
            rec.append(m)
        if ok and len(rec) == n:
            tracks.append(np.array(rec))      # n x (energy, peak, cx, cy)

    if len(tracks) < 20:
        raise SystemExit(f"only {len(tracks)} stars could be tracked")
    T = np.stack(tracks)                       # stars x frames x 4
    E, P, CX, CY = T[..., 0], T[..., 1], T[..., 2], T[..., 3]
    e_rel = E.std(axis=1) / E.mean(axis=1)
    p_rel = P.std(axis=1) / P.mean(axis=1)
    p_ratio = P.max(axis=1) / np.maximum(P.min(axis=1), 1e-9)
    # crawl: residual of the centroid from a straight line in frame index
    idx = np.arange(n, dtype=np.float64)
    res = []
    for cx in CX:
        coef = np.polyfit(idx, cx, 1)
        res.append((cx - np.polyval(coef, idx)).std())
    res = np.array(res)
    slope = np.array([np.polyfit(idx, cx, 1)[0] for cx in CX])
    # the brightest quarter of the tracked stars, where an 8-bit picture has the most range to show a change
    order = np.argsort(-P.mean(axis=1))
    top = order[: max(len(order) // 4, 10)]

    def q(a, p):
        return float(np.percentile(a, p))

    out = {
        "frames": n,
        "stepPxPerFrame": step,
        "starsTracked": int(len(tracks)),
        "energyRelStd_median": q(e_rel, 50), "energyRelStd_p95": q(e_rel, 95),
        "peakRelStd_median": q(p_rel, 50), "peakRelStd_p95": q(p_rel, 95),
        "peakMaxOverMin_median": q(p_ratio, 50), "peakMaxOverMin_p95": q(p_ratio, 95),
        "crawlPx_median": q(res, 50), "crawlPx_p95": q(res, 95),
        "measuredSlopePxPerFrame_median": float(np.median(slope)),
        "brightQuarter": {
            "stars": int(len(top)),
            "energyRelStd_median": q(e_rel[top], 50), "energyRelStd_p95": q(e_rel[top], 95),
            "peakRelStd_median": q(p_rel[top], 50), "peakRelStd_p95": q(p_rel[top], 95),
            "crawlPx_median": q(res[top], 50), "crawlPx_p95": q(res[top], 95),
        },
    }
    with open(os.path.join(folder, "shimmer.json"), "w") as f:
        json.dump(out, f, indent=1)
    for k, v in out.items():
        print(f"[MN] {k}: {v}")


main()
