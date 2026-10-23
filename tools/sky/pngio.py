"""Tiny PNG writer (8-bit RGB) so Blender's bundled Python (numpy, no Pillow) can save preview images. Standard library + numpy."""
import struct
import zlib

import numpy as np


def write_png(path, rgb8):
    """rgb8: uint8 array (h, w, 3), row 0 at the top."""
    h, w, c = rgb8.shape
    assert c == 3 and rgb8.dtype == np.uint8
    raw = np.concatenate([np.zeros((h, 1), np.uint8), rgb8.reshape(h, w * 3)], axis=1).tobytes()

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 6)))
        f.write(chunk(b"IEND", b""))


def encode_display(lin, gain=1.0, gamma_srgb=True):
    """Linear float (h, w, 3) -> uint8 sRGB-encoded."""
    x = np.clip(lin * gain, 0.0, 1.0)
    if gamma_srgb:
        x = np.where(x <= 0.0031308, 12.92 * x, 1.055 * np.power(x, 1 / 2.4) - 0.055)
    return (np.clip(x, 0, 1) * 255.0 + 0.5).astype(np.uint8)
