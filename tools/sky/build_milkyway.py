"""Build Game/Assets/Monolith/Sky/Resources/MonolithSky/milkyway.exr from NASA SVS Deep Star Maps 2020 (milkyway_2020_4k.exr).

Run:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\blender.ps1 tools\sky\build_milkyway.py
(Blender is only used because its bundled Python has numpy and an EXR reader/writer; no editing happens in Blender.)

The source is the diffuse Milky Way with the bright (Hipparcos and Tycho-2) stars already taken out, an equirectangular
map in ICRF equatorial coordinates, 0h RA in the middle with RA increasing to the left, north up. This script:
  - clamps the few isolated pixels that are far brighter than their surroundings (leftover star cores),
  - rounds the half-float mantissas to 5 bits so ZIP compression makes the file about 18 MB instead of about 36 MB (a 3% step
    in brightness, far below what 8-bit output can show),
  - writes a 16-bit RGB EXR (ZIP), same orientation as the source.
The pixel values are left in the source's units (0 to 1; the sky shader multiplies by a gain).
"""
import os
import sys

import bpy
import numpy as np

ROOT = os.environ["MN_ROOT"]
sys.path.insert(0, os.path.join(ROOT, "tools", "sky"))
SRC = os.path.join(ROOT, "art_src", "sky", "downloads", "milkyway_2020_4k.exr")
OUT = os.path.join(ROOT, "Game", "Assets", "Monolith", "Sky", "Resources", "MonolithSky", "milkyway.exr")
PREVIEW = os.environ.get("MN_SKY_PREVIEW")   # optional folder for a preview png

MANTISSA_BITS = 5


def box_blur(a, r):
    """Mean over a (2r+1)^2 window, horizontal wrap, vertical edge clamp."""
    k = 2 * r + 1
    p = np.concatenate([a[..., -r:], a, a[..., :r]], axis=-1)
    c = np.cumsum(p, axis=-1, dtype=np.float64)
    c = np.concatenate([np.zeros_like(c[..., :1]), c], axis=-1)
    h = (c[..., k:] - c[..., :-k]) / k
    p = np.concatenate([h[:r][::-1], h, h[-r:][::-1]], axis=0)
    c = np.cumsum(p, axis=0, dtype=np.float64)
    c = np.concatenate([np.zeros_like(c[:1]), c], axis=0)
    return ((c[k:] - c[:-k]) / k).astype(np.float32)


def quantise(x, bits):
    m, e = np.frexp(x)
    s = float(2 ** bits)
    return np.ldexp(np.round(m * s) / s, e).astype(np.float32)


def main():
    img = bpy.data.images.load(SRC)
    w, h = img.size
    a = np.empty(w * h * img.channels, dtype=np.float32)
    img.pixels.foreach_get(a)
    rgb = a.reshape(h, w, img.channels)[..., :3].copy()          # row 0 is the bottom (south), as Unity imports it
    bpy.data.images.remove(img)

    lum = rgb @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    local = box_blur(lum, 4)
    cap = 4.0 * local + 0.02
    scale = np.minimum(1.0, cap / np.maximum(lum, 1e-9))
    changed = int((scale < 0.999).sum())
    rgb *= scale[..., None]
    print(f"[MN] milkyway {w}x{h}: clamped {changed} pixels ({100.0 * changed / (w * h):.4f}%), mean {rgb.mean():.5f}, max {rgb.max():.4f}")

    rgb = quantise(rgb, MANTISSA_BITS)
    rgba = np.concatenate([rgb, np.ones((h, w, 1), np.float32)], axis=-1)
    out = bpy.data.images.new("mw", w, h, alpha=False, float_buffer=True, is_data=True)
    out.pixels.foreach_set(rgba.ravel())
    scene = bpy.context.scene
    st = scene.render.image_settings
    st.file_format, st.color_mode, st.color_depth, st.exr_codec = "OPEN_EXR", "RGB", "16", "ZIP"
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    out.save_render(OUT, scene=scene)
    print(f"[MN] wrote {OUT} ({os.path.getsize(OUT) / 1048576:.1f} MB)")

    if PREVIEW:
        from pngio import write_png, encode_display
        f = w // 1024
        p = rgb.reshape(h // f, f, w // f, f, 3).mean(axis=(1, 3))[::-1]
        write_png(os.path.join(PREVIEW, "milkyway_processed_x4.png"), encode_display(p, 4.0))


main()
