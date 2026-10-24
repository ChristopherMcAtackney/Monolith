"""The Oort Cloud's ice: the textures and material recipes for the comet outpost's nucleus (art_src/oort_art/build_earth_outpost.py): a very dark, dusty crust
(comet nuclei are among the darkest things in the solar system) broken by frost and exposed ice, the frost's own set, a fine grain for the detail map, and the soft
shells of the outgassing jets. Generated, deterministic, no stock images.

    tools\\blender.ps1 art_src\\oort_art\\build_oort_textures.py        (about a minute)

Writes Game/Assets/Monolith/Art/EarthHull/oort_{rock,ice}_{albedo,normal,mask}.png (2048^2, one tile every 160 m: the mesh is box-projected), oort_detail.png (1024^2, one
tile every 4 m, on the second UV set), oort_jet_emissive.png (256^2: four 64 px columns, one nested shell each) and earth_oort.materials.json: the Earth hull set's
recipes (the outpost's station is Earth's black and chrome, so it reads the same textures) plus mn_oort_rock, mn_oort_ice and mn_oort_jet. They live in the Earth hull's
folder so one materials file can name both (ShipArtSetup reads a texture from the folder of the file that names it).

Mask maps follow HDRP: R metallic, G occlusion, B detail mask, A smoothness.
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
    print(f"[MN] oort textures {time.time() - T0:5.1f}s  {msg}", flush=True)


def worley(h, w, cells, rng):
    """Periodic cellular noise: returns (F1, F2) distances to the two nearest feature points, in cell units (a grid of ``cells`` x ``cells`` jittered points)."""
    pts = rng.random((cells, cells, 2)).astype(np.float32)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    gy, gx = yy / h * cells, xx / w * cells
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


def pack(alb, hgt, smo, ao, strength, metal=None):
    nrm = mntex.normal_from_height(hgt, strength)
    mask = np.stack([np.zeros_like(smo) if metal is None else metal, ao, np.ones_like(smo), smo], axis=2)
    return mntex.to_u8(alb), mntex.to_u8(nrm), mntex.to_u8(mask)


def make_rock(size=2048, seed=401):
    """The crust: dust-dark and lumpy ("popcorn": rounded blobs with sharp valleys between them, at three scales), a loose network of long fractures, drifts of paler dust, pits with
    rims, thin ice veins along the fractures and a scatter of ice specks poking through. Statistically uniform (no large-scale blotches), because the mesh is box-projected and the
    tile repeats every 160 m: anything bigger than a few metres in the albedo would show as a grid. Albedo values are sRGB display values: 0.2 is a 4 percent reflectance."""
    rng = np.random.default_rng(seed)
    s = size / 2048.0
    n = lambda **k: spectral_noise(size, size, rng, **k)
    l1 = n(beta=1.5, fmin=14, fmax=46)
    l2 = n(beta=1.5, fmin=56, fmax=150)
    l3 = n(beta=1.2, fmin=190, fmax=480)
    grain = n(beta=0.7, fmin=500)
    lumps = -np.abs(l1) * 1.15 + 0.35 * l1
    lumps2 = -np.abs(l2) * 0.9 + 0.2 * l2
    fr = n(beta=2.4, fmin=3, fmax=13)
    zone = smoothstep(0.1, 0.9, n(beta=2.5, fmin=1, fmax=6))
    crack = (1.0 - smoothstep(0.0, 0.05, np.abs(fr))) * (0.35 + 0.65 * zone)
    fr2 = n(beta=2.0, fmin=14, fmax=50)
    crack2 = (1.0 - smoothstep(0.0, 0.06, np.abs(fr2))) * zone * 0.8
    hgt = 7.0 * lumps + 3.2 * lumps2 + 1.8 * l3 + 0.6 * grain - 5.0 * crack - 2.2 * crack2
    dust = smoothstep(-0.4, 1.3, n(beta=2.6, fmin=2, fmax=20) * 0.8 + 0.5 * l1)
    base = np.array([0.232, 0.222, 0.208], np.float32)
    alb = base[None, None, :] * (1.0 + 0.20 * l1[:, :, None] + 0.14 * l2[:, :, None] + 0.10 * l3[:, :, None] + 0.04 * grain[:, :, None])
    alb = alb * (1 - 0.30 * dust[:, :, None]) + np.array([0.30, 0.28, 0.255], np.float32)[None, None, :] * (0.30 * dust[:, :, None])
    alb *= (1.0 - 0.55 * crack - 0.35 * crack2)[:, :, None]
    stain = smoothstep(0.9, 1.9, n(beta=2.4, fmin=3, fmax=30) + 0.4 * l1)
    alb = alb * (1 - 0.28 * stain[:, :, None]) + np.array([0.20, 0.158, 0.125], np.float32)[None, None, :] * (0.28 * stain[:, :, None])
    vein = np.clip(crack * 1.2, 0, 1) * smoothstep(0.1, 1.2, n(beta=2.6, fmax=8))
    alb = alb * (1 - 0.75 * vein[:, :, None]) + np.array([0.46, 0.52, 0.60], np.float32)[None, None, :] * (0.75 * vein[:, :, None])
    hgt += 1.5 * vein
    ao = np.clip(1.0 - 0.55 * crack - 0.35 * crack2 - 0.22 * np.clip(-lumps * 0.3, 0, 1) - 0.1 * np.clip(-l2, 0, None), 0.12, 1.0)
    smo = np.clip(0.20 + 0.05 * l2 + 0.04 * grain + 0.35 * vein - 0.10 * dust, 0.05, 0.8)
    for _ in range(int(900 * s * s)):                                  # pits with raised rims; a few have ice in the bottom
        x, y = rng.uniform(0, size), rng.uniform(0, size)
        r = float(np.clip(rng.lognormal(2.3, 0.6), 4, 46)) * s
        pad = int(2.4 * r) + 3
        x0, y0 = int(x) - pad, int(y) - pad
        xs = (np.arange(x0, x0 + 2 * pad) % size)[None, :]
        ys = (np.arange(y0, y0 + 2 * pad) % size)[:, None]
        d = np.sqrt((np.arange(x0, x0 + 2 * pad)[None, :] + 0.5 - x) ** 2 + (np.arange(y0, y0 + 2 * pad)[:, None] + 0.5 - y) ** 2) / r
        bowl = np.clip(1.0 - d * d, 0, 1) ** 1.3
        rim = np.exp(-((d - 1.12) / 0.16) ** 2)
        hgt[ys, xs] += -r * 0.45 * bowl + r * 0.12 * rim
        ao[ys, xs] *= 1.0 - 0.45 * bowl
        alb[ys, xs, :] *= (1.0 - 0.35 * bowl)[:, :, None]
        if rng.random() < 0.14:
            k = bowl * 0.8
            alb[ys, xs, :] = alb[ys, xs, :] * (1 - k[:, :, None]) + np.array([0.52, 0.58, 0.66], np.float32) * k[:, :, None]
            smo[ys, xs] = smo[ys, xs] * (1 - k) + 0.65 * k
    for _ in range(int(2600 * s * s)):                                 # ice specks
        x, y = int(rng.integers(0, size)), int(rng.integers(0, size))
        r = int(rng.integers(1, 4))
        k = rng.uniform(0.35, 0.85)
        ys = (np.arange(y - r, y + r + 1) % size)[:, None]
        xs = (np.arange(x - r, x + r + 1) % size)[None, :]
        alb[ys, xs, :] = alb[ys, xs, :] * (1 - k) + np.array([0.60, 0.66, 0.74], np.float32) * k
        smo[ys, xs] = 0.7
        hgt[ys, xs] += 0.9
    return pack(np.clip(alb, 0, 1), hgt, smo, ao, 1.0)


def make_ice(size=1024, seed=402):
    """Frost and exposed ice in a dust: a dirty blue-white, glossier than the crust, with wind-scoured ridges, shadowed pits, blue-dark fractures and the crust showing through."""
    rng = np.random.default_rng(seed)
    s = size / 2048.0
    n = lambda **k: spectral_noise(size, size, rng, **k)
    l1 = n(beta=1.5, fmin=12, fmax=48)
    l2 = n(beta=1.4, fmin=60, fmax=180)
    fine = n(beta=1.0, fmin=300)
    scour = n(beta=1.8, fmin=6, fmax=140, sx=1.0, sy=0.12)             # long ridges one way: wind and sublimation
    fr = n(beta=2.3, fmin=4, fmax=16)
    crack = 1.0 - smoothstep(0.0, 0.06, np.abs(fr))
    hgt = 4.5 * l1 + 2.2 * l2 + 2.0 * scour + 0.9 * fine - 4.0 * crack
    base = np.array([0.54, 0.60, 0.68], np.float32)
    alb = base[None, None, :] * (1.0 + 0.12 * l1[:, :, None] + 0.08 * l2[:, :, None] + 0.05 * fine[:, :, None])
    alb = alb * (1 - 0.6 * crack[:, :, None]) + np.array([0.22, 0.34, 0.50], np.float32)[None, None, :] * (0.6 * crack[:, :, None])
    dust = smoothstep(0.1, 1.2, n(beta=2.4, fmin=3, fmax=40) * 0.9 + 0.3 * scour)
    alb = alb * (1 - 0.55 * dust[:, :, None]) + np.array([0.21, 0.19, 0.17], np.float32)[None, None, :] * (0.55 * dust[:, :, None])
    ao = np.clip(1.0 - 0.6 * crack - 0.1 * np.clip(-l2, 0, None), 0.2, 1.0)
    smo = np.clip(0.55 + 0.10 * l2 - 0.28 * dust + 0.08 * scour, 0.15, 0.9)
    for _ in range(int(5000 * s * s)):
        x, y = int(rng.integers(0, size)), int(rng.integers(0, size))
        alb[y, x, :] = np.minimum(1.0, alb[y, x, :] * 1.3)
        smo[y, x] = 0.95
        hgt[y, x] += 1.2
    return pack(np.clip(alb, 0, 1), hgt, smo, ao, 1.0)


def make_detail(size=1024, seed=403):
    """HDRP detail map, one tile every 4 m: R albedo (0.5 neutral), G normal Y, B smoothness (0.5 neutral), A normal X. Dust grains, pores, hairline cracks."""
    rng = np.random.default_rng(seed)
    h = 0.6 * spectral_noise(size, size, rng, beta=0.9, fmin=60) + 0.9 * spectral_noise(size, size, rng, beta=1.8, fmin=8, fmax=160)
    f1, f2 = worley(size, size, 60, rng)
    h -= 1.4 * (1.0 - smoothstep(0.02, 0.09, f2 - f1))
    nrm = mntex.normal_from_height(h, 0.8)
    alb = np.clip(0.5 + 0.10 * spectral_noise(size, size, rng, beta=1.2, fmin=4) + 0.06 * spectral_noise(size, size, rng, beta=0.8, fmin=100), 0, 1)
    smo = np.clip(0.5 + 0.08 * spectral_noise(size, size, rng, beta=1.6, fmin=4), 0, 1)
    return mntex.to_u8(np.stack([alb, nrm[:, :, 1], smo, nrm[:, :, 0]], axis=2))


def make_jet(w=256, h=256, seed=404):
    """The outgassing jets: four 64 px columns, each a nested additive shell (0 the faint wide haze, 1 dust, 2 brighter dust, 3 the dense core); u round the shell, v from the
    vent (row 0) outward. Soft: the shells' edges fall away smoothly and the streaks run along the jet."""
    rng = np.random.default_rng(seed)
    t = (np.arange(h)[:, None] + 0.5) / h
    out = np.zeros((h, w, 3), np.float32)
    cols = [np.array([0.40, 0.46, 0.58]), np.array([0.55, 0.60, 0.70]), np.array([0.72, 0.76, 0.84]), np.array([0.92, 0.94, 0.98])]
    fall = (2.2, 2.8, 3.6, 4.6)
    strength = (0.09, 0.18, 0.34, 0.55)
    for k in range(4):
        streak = 1.0 + 0.45 * spectral_noise(h, 64, rng, beta=2.2, fmin=2, sy=0.10)
        clump = np.clip(1.0 + 0.35 * spectral_noise(h, 64, rng, beta=2.0, fmin=1, fmax=6), 0.3, 1.8)
        tear = smoothstep(0.55 + 0.1 * spectral_noise(h, 64, rng, beta=2.0, fmin=1, fmax=6), 1.0, t)
        f = np.exp(-t * fall[k]) * (1 - np.clip(tear, 0, 1))
        out[:, k * 64:(k + 1) * 64] = cols[k][None, None, :] * (f * strength[k] * streak * clump)[..., None]
    return mntex.to_u8(mntex.srgb_encode(np.clip(out, 0, 1)))


def main():
    os.makedirs(OUT, exist_ok=True)
    a, n, m = make_rock()
    save("oort_rock_albedo", a); save("oort_rock_normal", n); save("oort_rock_mask", m)
    a, n, m = make_ice()
    save("oort_ice_albedo", a); save("oort_ice_normal", n); save("oort_ice_mask", m)
    save("oort_detail", make_detail())
    save("oort_jet_emissive", make_jet())
    earth = json.load(open(os.path.join(OUT, "earth_hull.materials.json")))
    mats = earth["materials"] + [
        {"blender": "mn_oort_rock", "name": "MN_OortRock", "baseColor": [1.0, 1.0, 1.0], "baseMap": "oort_rock_albedo", "normalMap": "oort_rock_normal", "maskMap": "oort_rock_mask",
         "normalScale": 1.3, "metallicRemap": [0, 0], "smoothnessRemap": [0, 0.55], "aoRemap": [0, 1],
         "detailMap": "oort_detail", "uvDetail": 1, "detailTiling": [1, 1], "detailAlbedo": 0.55, "detailNormal": 0.55, "detailSmoothness": 0.5, "specularAA": True},
        {"blender": "mn_oort_ice", "name": "MN_OortIce", "baseColor": [0.84, 0.86, 0.9], "baseMap": "oort_ice_albedo", "normalMap": "oort_ice_normal", "maskMap": "oort_ice_mask",
         "normalScale": 1.0, "metallicRemap": [0, 0], "smoothnessRemap": [0, 0.92], "aoRemap": [0, 1],
         "detailMap": "oort_detail", "uvDetail": 1, "detailTiling": [1, 1], "detailAlbedo": 0.4, "detailNormal": 0.4, "detailSmoothness": 0.4, "specularAA": True},
        {"blender": "mn_oort_jet", "name": "MN_OortJet", "shader": "HDRP/Unlit", "surface": "additive", "emissiveMap": "oort_jet_emissive", "emissiveColor": [1.0, 1.0, 1.0],
         "emissiveNits": 4200, "noShadows": True},
    ]
    earth = dict(earth)
    earth["materials"] = mats
    earth["about"] = "The Earth hull set's recipes plus the Oort Cloud's ice (art_src/oort_art): the comet outpost's materials."
    with open(os.path.join(OUT, "earth_oort.materials.json"), "w") as f:
        json.dump(earth, f, indent=1)
    log("done")


main()
