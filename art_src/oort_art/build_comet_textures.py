r"""The surfaces of the Oort ice field's comets (art_src/comets/build_comets.py): a very dark, dusty, granular crust with a network of fractures, the smooth pale dust that fills the hollows,
and a fine grain for the detail map. The third layer, frost and exposed ice, is the outpost's (oort_ice_*). Generated, deterministic, no stock images.

    tools\blender.ps1 art_src\oort_art\build_comet_textures.py        (about a minute)

Writes Game/Assets/Monolith/Art/EarthHull/comet_{crust,dust}_{albedo,normal,mask}.png (2048^2 and 1024^2, one tile every 150 m of a comet's own size, box-projected on the mesh),
comet_detail.png (1024^2, one tile every 4 m, on the second UV set) and comets.materials.json: ONE layered HDRP material, ``mn_comet_surface``, whose layers (crust, dust, ice) the mesh's
vertex colour blends (R the dust's weight, G the ice's). They live in the Earth hull's folder because the ice layer's textures do (a materials file reads textures from its own folder).

Mask maps follow HDRP: R metallic, G occlusion, B detail mask, A smoothness. Albedo values are sRGB display values: 0.2 is a 3.4 percent reflectance, a comet's crust is 4 percent or less.
What a nucleus looks like close up (Rosetta's 67P): not pores and not bubbles, but a dusty, finely granular surface of clods a metre or three across ("goosebumps"), polygonal fracture
networks, fine hairline cracks, and smooth dust where it has settled; and nearly one colour, a dark grey-brown, with almost no contrast in albedo.
"""
import json
import math
import os
import time

import numpy as np

from blender_lib import mntex
from blender_lib.mntex import spectral_noise, blur, smoothstep

ROOT = os.environ.get("MN_ROOT", os.getcwd())
OUT = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "EarthHull")
T0 = time.time()


def log(msg):
    print(f"[MN] comet textures {time.time() - T0:5.1f}s  {msg}", flush=True)


def worley(h, w, cells, rng, warp=None):
    """Periodic cellular noise: (F1, F2) distances to the two nearest feature points, in cell units. ``warp`` (two periodic fields, in cells) bends the cell boundaries."""
    pts = rng.random((cells, cells, 2)).astype(np.float32)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    gy, gx = yy / h * cells, xx / w * cells
    if warp is not None:
        gy = gy + warp[0]
        gx = gx + warp[1]
    cy, cx = np.floor(gy).astype(int), np.floor(gx).astype(int)
    f1 = np.full((h, w), 9.0, np.float32)
    f2 = np.full((h, w), 9.0, np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            ny, nx = (cy + dy) % cells, (cx + dx) % cells
            py = (cy + dy) + pts[ny, nx, 0]
            px = (cx + dx) + pts[ny, nx, 1]
            d = np.sqrt((gy - py) ** 2 + (gx - px) ** 2)
            f2 = np.minimum(f2, np.maximum(f1, d))
            f1 = np.minimum(f1, d)
    return f1, f2


def save(name, arr):
    path = os.path.join(OUT, name + ".png")
    mntex.write_png(path, arr)
    log(f"wrote {name}.png {arr.shape[1]}x{arr.shape[0]}  {os.path.getsize(path) / 1024:.0f} KB")


def pack(alb, hgt, smo, ao, strength):
    nrm = mntex.normal_from_height(hgt, strength)
    mask = np.stack([np.zeros_like(smo), ao, np.ones_like(smo), smo], axis=2)
    return mntex.to_u8(alb), mntex.to_u8(nrm), mntex.to_u8(mask)


def make_crust(size=2048, seed=611):
    """The consolidated crust: clods ("goosebumps") at three scales, a wandering network of fractures that comes and goes (patches of it, wide in places and hairline in others, broken by gaps,
    its cells bent by noise so no two are alike), long gentle undulations, a fine grain; one dark brown-grey with a couple of percent variation, a little darker in the cracks and the clod
    valleys, with now and then a paler fleck of dust."""
    rng = np.random.default_rng(seed)
    n = lambda **k: spectral_noise(size, size, rng, **k)
    s = size / 2048.0

    def warp(cells, amp):
        return (amp * n(beta=2.6, fmin=2, fmax=9) / cells * cells * 0.5, amp * n(beta=2.6, fmin=2, fmax=9) * 0.5)

    f1, f2 = worley(size, size, int(52 * s) or 8, rng, warp(52, 0.5))
    clod = (1.0 - smoothstep(0.0, 0.7, f1)) ** 0.85
    g1, g2 = worley(size, size, int(140 * s) or 16, rng, warp(140, 0.4))
    clod2 = 1.0 - smoothstep(0.0, 0.66, g1)
    k1, k2 = worley(size, size, int(300 * s) or 24, rng)
    clod3 = 1.0 - smoothstep(0.0, 0.6, k1)
    p1, p2 = worley(size, size, int(9 * s) or 4, rng, warp(9, 1.1))
    patch = smoothstep(-0.45, 0.3, n(beta=2.6, fmin=1, fmax=5) * 0.7 + 0.35 * n(beta=2.0, fmin=3, fmax=10))      # where the fracture network is
    wid = 0.014 + 0.05 * smoothstep(-0.5, 1.2, n(beta=2.4, fmin=3, fmax=12))
    gap = 1.0 - smoothstep(0.6, 1.2, n(beta=1.8, fmin=8, fmax=40))                                               # breaks in the lines
    crack = (1.0 - smoothstep(0.0, wid, p2 - p1)) * patch * gap
    q1, q2 = worley(size, size, int(30 * s) or 6, rng, warp(30, 0.9))
    patch2 = smoothstep(-0.3, 0.5, n(beta=2.6, fmin=1, fmax=6))
    crack2 = (1.0 - smoothstep(0.0, 0.06, q2 - q1)) * patch2 * gap
    hair = n(beta=2.2, fmin=40, fmax=160)
    hairline = (1.0 - smoothstep(0.0, 0.035, np.abs(hair))) * 0.7
    waves = n(beta=2.2, fmin=2, fmax=14)
    grain = n(beta=0.8, fmin=260)
    mid = n(beta=1.6, fmin=20, fmax=90)
    hgt = 6.5 * clod + 3.2 * clod2 + 1.4 * clod3 + 4.5 * waves + 1.2 * mid + 1.0 * grain - 6.0 * crack - 3.0 * crack2 - 1.6 * hairline
    l1, l2 = n(beta=1.5, fmin=6, fmax=30), n(beta=1.4, fmin=40, fmax=140)
    base = np.array([0.203, 0.197, 0.191], np.float32)
    redder = smoothstep(0.1, 1.3, n(beta=2.6, fmin=1, fmax=8))
    base = base[None, None, :] * (1.0 - 0.12 * redder[:, :, None]) + np.array([0.226, 0.200, 0.180], np.float32)[None, None, :] * (0.12 * redder[:, :, None])
    alb = base * (1.0 + 0.05 * l1[:, :, None] + 0.045 * l2[:, :, None] + 0.04 * grain[:, :, None] + 0.12 * (clod[:, :, None] - 0.5) + 0.05 * (clod2[:, :, None] - 0.5))
    alb *= (1.0 - 0.30 * crack - 0.16 * crack2 - 0.12 * hairline)[:, :, None]
    dust = smoothstep(0.7, 2.2, n(beta=2.4, fmin=3, fmax=30) + 0.5 * l1)                # paler dust settled on the clods
    alb = alb * (1 - 0.20 * dust[:, :, None]) + np.array([0.285, 0.268, 0.250], np.float32)[None, None, :] * (0.20 * dust[:, :, None])
    ao = np.clip(1.0 - 0.5 * crack - 0.26 * crack2 - 0.38 * np.clip(0.55 - clod, 0, 1) - 0.1 * np.clip(-waves * 0.3, 0, 1), 0.15, 1.0)
    smo = np.clip(0.22 + 0.03 * mid + 0.03 * grain - 0.07 * dust + 0.05 * crack, 0.05, 0.5)
    for _ in range(int(1100 * s * s)):                                                    # paler flecks: dust and the odd grain of ice, soft and round
        x, y = rng.uniform(0, size), rng.uniform(0, size)
        r = float(rng.uniform(0.9, 2.2))
        pad = int(2.6 * r) + 2
        x0, y0 = int(x) - pad, int(y) - pad
        xs = (np.arange(x0, x0 + 2 * pad) % size)[None, :]
        ys = (np.arange(y0, y0 + 2 * pad) % size)[:, None]
        d = np.sqrt((np.arange(x0, x0 + 2 * pad)[None, :] + 0.5 - x) ** 2 + (np.arange(y0, y0 + 2 * pad)[:, None] + 0.5 - y) ** 2) / r
        k = np.clip(1.0 - d * d, 0, 1) ** 1.2 * rng.uniform(0.25, 0.7)
        col = np.array([0.40, 0.42, 0.45], np.float32) if rng.random() < 0.12 else np.array([0.31, 0.29, 0.27], np.float32)
        alb[ys, xs, :] = alb[ys, xs, :] * (1 - k[:, :, None]) + col * k[:, :, None]
    return pack(np.clip(alb, 0, 1), hgt, smo, ao, 1.0)


def make_dust(size=1024, seed=612):
    """The fill: smooth, a little paler than the crust, nearly flat: broad soft swells, a fine grain, faint streaks where the dust has crept, a scatter of small pebbles."""
    rng = np.random.default_rng(seed)
    n = lambda **k: spectral_noise(size, size, rng, **k)
    s = size / 1024.0
    swell = n(beta=2.4, fmin=2, fmax=12)
    creep = n(beta=2.0, fmin=4, fmax=40, sx=1.0, sy=0.2)
    grain = n(beta=0.8, fmin=180)
    mid = n(beta=1.6, fmin=14, fmax=70)
    hgt = 3.2 * swell + 1.2 * creep + 0.8 * mid + 0.7 * grain
    alb = np.array([0.272, 0.260, 0.248], np.float32)[None, None, :] * (1.0 + 0.045 * swell[:, :, None] + 0.03 * grain[:, :, None] + 0.025 * creep[:, :, None])
    smo = np.clip(0.14 + 0.03 * swell - 0.04 * grain, 0.04, 0.4)
    ao = np.clip(1.0 - 0.12 * np.clip(-creep * 0.5, 0, 1), 0.4, 1.0)
    for _ in range(int(1100 * s * s)):                                                    # pebbles: small domes
        x, y = rng.uniform(0, size), rng.uniform(0, size)
        r = float(rng.uniform(1.5, 5.5))
        pad = int(2.2 * r) + 2
        x0, y0 = int(x) - pad, int(y) - pad
        xs = (np.arange(x0, x0 + 2 * pad) % size)[None, :]
        ys = (np.arange(y0, y0 + 2 * pad) % size)[:, None]
        d = np.sqrt((np.arange(x0, x0 + 2 * pad)[None, :] + 0.5 - x) ** 2 + (np.arange(y0, y0 + 2 * pad)[:, None] + 0.5 - y) ** 2) / r
        dome = np.clip(1.0 - d * d, 0, 1) ** 0.7
        hgt[ys, xs] += r * 0.6 * dome
        ao[ys, xs] *= 1.0 - 0.3 * np.clip(1.0 - np.abs(d - 1.1) * 4, 0, 1)
        alb[ys, xs, :] *= (1.0 + 0.2 * dome * rng.uniform(-0.6, 1.0))[:, :, None]
    return pack(np.clip(alb, 0, 1), hgt, smo, ao, 0.55)


def make_detail(size=1024, seed=613):
    """HDRP detail map, one tile every 4 m: R albedo (0.5 neutral), G normal Y, B smoothness (0.5 neutral), A normal X. Grain, granules and hairline cracks: no pores."""
    rng = np.random.default_rng(seed)
    n = lambda **k: spectral_noise(size, size, rng, **k)
    h = 0.9 * n(beta=0.9, fmin=70) + 1.0 * n(beta=1.7, fmin=10, fmax=180)
    g1, g2 = worley(size, size, 90, rng)
    h += 1.2 * (1.0 - smoothstep(0.0, 0.7, g1))
    hair = n(beta=2.3, fmin=10, fmax=50)
    h -= 1.6 * (1.0 - smoothstep(0.0, 0.03, np.abs(hair)))
    nrm = mntex.normal_from_height(h, 0.8)
    alb = np.clip(0.5 + 0.09 * n(beta=1.2, fmin=4) + 0.05 * n(beta=0.8, fmin=100), 0, 1)
    smo = np.clip(0.5 + 0.07 * n(beta=1.6, fmin=4), 0, 1)
    return mntex.to_u8(np.stack([alb, nrm[:, :, 1], smo, nrm[:, :, 0]], axis=2))


def layer(base, normal, mask, **kw):
    d = {"baseColor": [1.0, 1.0, 1.0], "baseMap": base, "normalMap": normal, "maskMap": mask, "metallicRemap": [0, 0], "aoRemap": [0, 1], "uvDetail": 1, "detailTiling": [1, 1]}
    d.update(kw)
    return d


def main():
    os.makedirs(OUT, exist_ok=True)
    a, n, m = make_crust()
    save("comet_crust_albedo", a); save("comet_crust_normal", n); save("comet_crust_mask", m)
    a, n, m = make_dust()
    save("comet_dust_albedo", a); save("comet_dust_normal", n); save("comet_dust_mask", m)
    save("comet_detail", make_detail())
    mats = [{"blender": "mn_comet_surface", "name": "MN_CometSurface", "shader": "HDRP/LayeredLit", "vertexColors": "multiply", "enableInstancing": True,
             "layers": [
                 layer("comet_crust_albedo", "comet_crust_normal", "comet_crust_mask", normalScale=1.0, smoothnessRemap=[0, 0.5], tiling=[1, 1], detailMap="comet_detail",
                       detailAlbedo=0.5, detailNormal=0.7, detailSmoothness=0.4, specularAA=True),
                 layer("comet_dust_albedo", "comet_dust_normal", "comet_dust_mask", normalScale=0.7, smoothnessRemap=[0, 0.4], tiling=[0.5, 0.5], detailMap="comet_detail",
                       detailAlbedo=0.4, detailNormal=0.5, detailSmoothness=0.3, specularAA=True),
                 layer("oort_ice_albedo", "oort_ice_normal", "oort_ice_mask", baseColor=[0.60, 0.62, 0.66], normalScale=1.0, smoothnessRemap=[0, 0.6], tiling=[1, 1], detailMap="comet_detail",
                       detailAlbedo=0.3, detailNormal=0.4, detailSmoothness=0.3, specularAA=True)]}]
    with open(os.path.join(OUT, "comets.materials.json"), "w") as f:
        json.dump({"about": "The comets' one layered surface material (art_src/oort_art/build_comet_textures.py): crust, dust and ice, blended by the vertex colour.", "materials": mats}, f, indent=1)
    log("done")


main()
