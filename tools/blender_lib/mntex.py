"""mntex: a small numpy texture toolkit for scripted PBR textures (Blender's bundled Python has numpy and no Pillow).

Conventions
  * Arrays are float32, row 0 at the TOP of the image (as in a PNG), x to the right. UV v = 1 - row / height.
  * A ``Layers`` canvas holds what HDRP's Lit shader wants: albedo (sRGB display values 0..1), height (arbitrary
    units, 1.0 = one pixel of relief), metallic, smoothness, ambient occlusion and emission (linear, 0..1 relative).
  * ``Layers.finish`` packs them: albedo RGB, normal RGB (tangent space, +Y up as Unity reads it), mask RGBA
    (R metallic, G occlusion, B detail mask, A smoothness) and emissive RGB, all uint8.
  * Every filter wraps round the edges, so anything made on a canvas tiles; the atlas packer pads pieces itself.
"""
import math
import struct
import zlib

import numpy as np


# ------------------------------------------------------------------------------------------------ PNG

def write_png(path, a):
    """a: uint8 (h, w), (h, w, 1), (h, w, 3) or (h, w, 4), row 0 at the top. Picks the smaller of the None/Sub/Up filters."""
    a = np.ascontiguousarray(a)
    assert a.dtype == np.uint8
    if a.ndim == 2:
        a = a[:, :, None]
    h, w, c = a.shape
    ctype = {1: 0, 3: 2, 4: 6}[c]
    rows = a.reshape(h, w * c)

    def filtered(kind):
        if kind == 0:
            f = rows
        elif kind == 1:   # Sub: byte minus the byte one pixel to the left
            f = rows.copy()
            f[:, c:] = rows[:, c:] - rows[:, :-c]
        else:             # Up
            f = rows.copy()
            f[1:] = rows[1:] - rows[:-1]
        return np.concatenate([np.full((h, 1), kind, np.uint8), f], axis=1).tobytes()

    best = None
    for kind in (1, 2):
        data = zlib.compress(filtered(kind), 9)
        if best is None or len(data) < len(best):
            best = data

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, ctype, 0, 0, 0)))
        f.write(chunk(b"IDAT", best))
        f.write(chunk(b"IEND", b""))


def srgb_encode(lin):
    x = np.clip(lin, 0.0, 1.0)
    return np.where(x <= 0.0031308, 12.92 * x, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def srgb_decode(s):
    x = np.clip(s, 0.0, 1.0)
    return np.where(x <= 0.04045, x / 12.92, np.power((x + 0.055) / 1.055, 2.4))


def to_u8(x):
    return (np.clip(x, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0 + 1e-12), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# ------------------------------------------------------------------------------------------------ noise and filters

def spectral_noise(h, w, rng, beta=2.0, fmin=0.0, fmax=None, sx=1.0, sy=1.0):
    """Periodic noise with a 1/f^beta spectrum between fmin and fmax (cycles per image; fmax None = Nyquist).
    sx, sy stretch the frequency axes: sx 0.1 gives features ten times longer along x. Zero mean, unit standard deviation."""
    white = rng.standard_normal((h, w)).astype(np.float32)
    spec = np.fft.rfft2(white)
    fy = np.fft.fftfreq(h)[:, None] * h / max(sy, 1e-6)
    fx = np.fft.rfftfreq(w)[None, :] * w / max(sx, 1e-6)
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    filt = np.power(f, -beta * 0.5)
    filt[0, 0] = 0.0
    if fmin > 0:
        filt *= smoothstep(fmin * 0.5, fmin, f)
    if fmax is not None:
        filt *= 1.0 - smoothstep(fmax, fmax * 1.5, f)
    out = np.fft.irfft2(spec * filt, s=(h, w)).astype(np.float32)
    out -= out.mean()
    out /= (out.std() + 1e-9)
    return out


def blur(a, sigma):
    """Periodic Gaussian blur of a 2-D (or h, w, c) array by FFT. sigma in pixels (a scalar, or (sy, sx))."""
    if np.isscalar(sigma):
        sigma = (sigma, sigma)
    if a.ndim == 3:
        return np.stack([blur(a[:, :, i], sigma) for i in range(a.shape[2])], axis=2)
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.rfftfreq(w)[None, :]
    g = np.exp(-2.0 * (math.pi ** 2) * ((fx * sigma[1]) ** 2 + (fy * sigma[0]) ** 2))
    return np.fft.irfft2(np.fft.rfft2(a) * g, s=(h, w)).astype(np.float32)


def normal_from_height(height, strength=1.0):
    """Tangent-space normal map (h, w, 3) in 0..1 from a height field (units: pixels of relief). Wraps. Unity reads +Y up."""
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    drow = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    nx = -dx * strength
    ny = drow * strength                       # rows grow downward, so +Y (up) is the row difference
    nz = np.ones_like(nx)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx * inv * 0.5 + 0.5, ny * inv * 0.5 + 0.5, nz * inv * 0.5 + 0.5], axis=2).astype(np.float32)


def cover(h, w, x0, y0, x1, y1):
    """Anti-aliased coverage of the rectangle [x0, x1) x [y0, y1) in pixel units, over the whole canvas, as the bounding
    slice plus a small array: returns (ys, xs, c) where c has the slice's shape."""
    ix0, ix1 = max(int(math.floor(x0)), 0), min(int(math.ceil(x1)), w)
    iy0, iy1 = max(int(math.floor(y0)), 0), min(int(math.ceil(y1)), h)
    if ix1 <= ix0 or iy1 <= iy0:
        return slice(0, 0), slice(0, 0), np.zeros((0, 0), np.float32)
    xs = np.arange(ix0, ix1, dtype=np.float32)
    ys = np.arange(iy0, iy1, dtype=np.float32)
    cx = np.clip(np.minimum(xs + 1, x1) - np.maximum(xs, x0), 0, 1)
    cy = np.clip(np.minimum(ys + 1, y1) - np.maximum(ys, y0), 0, 1)
    return slice(iy0, iy1), slice(ix0, ix1), (cy[:, None] * cx[None, :]).astype(np.float32)


def segment_coverage(h, w, p0, p1, width, soft=0.75):
    """Soft line segment: returns (ys, xs, c). Distance to the segment, so ends are round."""
    (x0, y0), (x1, y1) = p0, p1
    r = width * 0.5 + soft + 1
    ix0, ix1 = max(int(math.floor(min(x0, x1) - r)), 0), min(int(math.ceil(max(x0, x1) + r)), w)
    iy0, iy1 = max(int(math.floor(min(y0, y1) - r)), 0), min(int(math.ceil(max(y0, y1) + r)), h)
    if ix1 <= ix0 or iy1 <= iy0:
        return slice(0, 0), slice(0, 0), np.zeros((0, 0), np.float32)
    xs = (np.arange(ix0, ix1, dtype=np.float32) + 0.5)[None, :]
    ys = (np.arange(iy0, iy1, dtype=np.float32) + 0.5)[:, None]
    dx, dy = x1 - x0, y1 - y0
    l2 = dx * dx + dy * dy + 1e-9
    t = np.clip(((xs - x0) * dx + (ys - y0) * dy) / l2, 0, 1)
    d = np.sqrt((xs - (x0 + t * dx)) ** 2 + (ys - (y0 + t * dy)) ** 2)
    c = 1.0 - smoothstep(width * 0.5 - soft * 0.5, width * 0.5 + soft * 0.5, d)
    return slice(iy0, iy1), slice(ix0, ix1), c.astype(np.float32)


# ------------------------------------------------------------------------------------------------ painting canvas

class Layers:
    """One piece or texture under construction. Fill it with the painting calls, then ``finish`` it."""

    def __init__(self, w, h, albedo=(0.5, 0.5, 0.5), metal=0.0, smooth=0.5, ao=1.0):
        self.w, self.h = w, h
        self.alb = np.empty((h, w, 3), np.float32)
        self.alb[:] = np.asarray(albedo, np.float32)
        self.hgt = np.zeros((h, w), np.float32)
        self.met = np.full((h, w), metal, np.float32)
        self.smo = np.full((h, w), smooth, np.float32)
        self.ao = np.full((h, w), ao, np.float32)
        self.em = np.zeros((h, w, 3), np.float32)
        self.dm = np.ones((h, w), np.float32)         # detail mask (1 = the tiling detail map applies)

    # -- painting ---------------------------------------------------------------------------------------------
    def paint(self, sl, c, albedo=None, height=None, height_add=None, metal=None, smooth=None, ao=None, ao_mul=None, emis=None, emis_add=None,
              detail=None, alb_mul=None, smooth_add=None):
        """Blend values over a region with coverage ``c`` (0..1). sl = (ys, xs) slices. Any argument may be a scalar, a
        colour tuple, or an array broadcastable to the region."""
        ys, xs = sl
        if c.size == 0:
            return
        cc = c[:, :, None]

        def mix(arr, val, three=False):
            val = np.asarray(val, np.float32)
            reg = arr[ys, xs]
            if three:
                arr[ys, xs] = reg * (1 - cc) + (val if val.ndim == 3 else val.reshape(1, 1, -1)) * cc
            else:
                arr[ys, xs] = reg * (1 - c) + val * c

        if albedo is not None: mix(self.alb, albedo, True)
        if alb_mul is not None:
            m = np.asarray(alb_mul, np.float32)
            m = m if m.ndim == 2 else m.reshape(1, 1)
            self.alb[ys, xs] *= 1.0 + (m[:, :, None] - 1.0) * cc
        if height is not None: mix(self.hgt, height)
        if height_add is not None: self.hgt[ys, xs] += np.asarray(height_add, np.float32) * c
        if metal is not None: mix(self.met, metal)
        if smooth is not None: mix(self.smo, smooth)
        if smooth_add is not None: self.smo[ys, xs] += np.asarray(smooth_add, np.float32) * c
        if ao is not None: mix(self.ao, ao)
        if ao_mul is not None: self.ao[ys, xs] *= 1.0 + (np.asarray(ao_mul, np.float32) - 1.0) * c
        if emis is not None: mix(self.em, emis, True)
        if detail is not None: mix(self.dm, detail)

    def rect(self, x0, y0, x1, y1, k=1.0, **kw):
        """k scales the coverage (a translucent coat)."""
        ys, xs, c = cover(self.h, self.w, x0, y0, x1, y1)
        self.paint((ys, xs), c * k, **kw)
        return ys, xs, c

    def line(self, p0, p1, width, soft=0.75, k=1.0, **kw):
        ys, xs, c = segment_coverage(self.h, self.w, p0, p1, width, soft)
        self.paint((ys, xs), c * k, **kw)

    def bevel_rect(self, x0, y0, x1, y1, depth, bevel, **kw):
        """A raised (depth > 0) or sunk (depth < 0) rectangle with a sloped rim ``bevel`` pixels wide in the height field."""
        ys, xs, c = cover(self.h, self.w, x0, y0, x1, y1)
        if c.size == 0:
            return
        gx = (np.arange(xs.start, xs.stop, dtype=np.float32) + 0.5)[None, :]
        gy = (np.arange(ys.start, ys.stop, dtype=np.float32) + 0.5)[:, None]
        edge = np.minimum(np.minimum(gx - x0, x1 - gx), np.minimum(gy - y0, y1 - gy))
        ramp = np.clip(edge / max(bevel, 1e-3), 0, 1)
        self.paint((ys, xs), c, height=self.hgt[ys, xs] * (1 - ramp) + depth * ramp, **kw)

    # -- whole-canvas modulations --------------------------------------------------------------------------------
    def tint_noise(self, noise, amount):
        """Multiply albedo by 1 + amount * noise (noise zero mean, unit std)."""
        self.alb *= (1.0 + amount * noise)[:, :, None]

    def finish(self, normal_strength=1.0):
        """-> dict of uint8 arrays: albedo (h,w,3), normal (h,w,3), mask (h,w,4), emissive (h,w,3)."""
        nrm = normal_from_height(self.hgt, normal_strength)
        mask = np.stack([self.met, self.ao, self.dm, self.smo], axis=2)
        return {
            "albedo": to_u8(self.alb),
            "normal": to_u8(nrm),
            "mask": to_u8(mask),
            "emissive": to_u8(srgb_encode(self.em)),
        }


# ------------------------------------------------------------------------------------------------ atlas

class Atlas:
    """Packs pieces (Layers) into one sheet with a shelf packer, pads each by replicating its edge (so mips and filtering
    do not bleed a neighbour in), and reports each piece's UV rectangle."""

    def __init__(self, w, h, pad=8, albedo=(0.05, 0.05, 0.05)):
        self.w, self.h, self.pad = w, h, pad
        self.base = Layers(w, h, albedo=albedo, metal=0.0, smooth=0.3, ao=1.0)
        self.items = {}
        self._x = pad
        self._y = pad
        self._row = 0
        self.normal_strength = {}

    def place(self, name, piece, metres_w, metres_h, normal_strength=1.0, tile_u=False):
        """Blit ``piece`` (a Layers) into the next free spot. metres_w/h: the piece's real size, used to place it on a mesh."""
        pw, ph = piece.w, piece.h
        if self._x + pw + self.pad > self.w:
            self._x = self.pad
            self._y += self._row + 2 * self.pad
            self._row = 0
        if self._y + ph + self.pad > self.h:
            raise RuntimeError(f"atlas full placing {name} ({pw}x{ph}) at y={self._y}")
        x, y = self._x, self._y
        self._x += pw + 2 * self.pad
        self._row = max(self._row, ph)
        b = self.base
        pad = self.pad
        # Bake this piece's relief into the shared height field with its own strength by scaling it, so one global
        # strength serves the whole sheet.
        k = normal_strength
        for src, dst in ((piece.alb, b.alb), (piece.met, b.met), (piece.smo, b.smo), (piece.ao, b.ao), (piece.em, b.em), (piece.dm, b.dm)):
            dst[y:y + ph, x:x + pw] = src
        b.hgt[y:y + ph, x:x + pw] = piece.hgt * k
        # Edge padding: tiling strips wrap along u, others replicate their rim.
        for dst in (b.alb, b.met, b.smo, b.ao, b.em, b.dm, b.hgt):
            reg_y0, reg_y1, reg_x0, reg_x1 = y, y + ph, x, x + pw
            if tile_u:
                dst[reg_y0:reg_y1, reg_x0 - pad:reg_x0] = dst[reg_y0:reg_y1, reg_x1 - pad:reg_x1]
                dst[reg_y0:reg_y1, reg_x1:reg_x1 + pad] = dst[reg_y0:reg_y1, reg_x0:reg_x0 + pad]
            else:
                dst[reg_y0:reg_y1, reg_x0 - pad:reg_x0] = dst[reg_y0:reg_y1, reg_x0:reg_x0 + 1]
                dst[reg_y0:reg_y1, reg_x1:reg_x1 + pad] = dst[reg_y0:reg_y1, reg_x1 - 1:reg_x1]
            dst[reg_y0 - pad:reg_y0, reg_x0 - pad:reg_x1 + pad] = dst[reg_y0:reg_y0 + 1, reg_x0 - pad:reg_x1 + pad]
            dst[reg_y1:reg_y1 + pad, reg_x0 - pad:reg_x1 + pad] = dst[reg_y1 - 1:reg_y1, reg_x0 - pad:reg_x1 + pad]
        # UV rectangle (u0, v0, u1, v1) with v up; shrink by half a pixel so bilinear never reads past the rim.
        hp = 0.5
        self.items[name] = {
            "px": [x, y, pw, ph],
            "uv": [(x + hp) / self.w, 1.0 - (y + ph - hp) / self.h, (x + pw - hp) / self.w, 1.0 - (y + hp) / self.h],
            "metres": [metres_w, metres_h],
            "tile_u": tile_u,
        }

    def finish(self, normal_strength=1.0):
        return self.base.finish(normal_strength)
