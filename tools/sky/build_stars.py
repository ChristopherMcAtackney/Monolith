"""Build Game/Assets/Monolith/Sky/Resources/MonolithSky/stars.bytes from the downloaded catalogues.

Run:  python -I tools/sky/build_stars.py          (after tools/sky/fetch_sources.py)
Sources (see Game/Assets/Monolith/Sky/SOURCES.md for licences):
  Yale Bright Star Catalogue 5th ed. (bsc5.dat.gz)  - every star to about V 6.5, J2000 positions, V and B-V.
  Gaia DR3 (gaia_*.csv, G 6.0 to 9.0)                - the fainter stars; ra/dec are epoch 2016.0, moved back to J2000 with the proper motions.
Gaia stars brighter than V 6.3 are dropped (the Yale catalogue has them, and Gaia photometry saturates there), and so is any
Gaia star within 15 arcsec of a Yale one (the same star).

File layout (little endian), sorted brightest first so a limiting magnitude is just a shorter draw:
  8 bytes   magic "MNSKY001"
  uint32    star count N
  uint32    reserved (0)
  N * float4  x, y, z, V      x,y,z: unit vector in the equatorial frame (x to RA 0h, y to RA 6h, z to the north celestial pole)
  N * uint32  r | g<<8 | b<<16 | flags<<24     r,g,b: linear colour scaled so the largest is 255 (blackbody from B-V);
                                                flags: 1 = Yale BSC, 2 = Gaia
Standard library only.
"""
import csv
import glob
import gzip
import json
import math
import os
import struct

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DL = os.path.join(ROOT, "art_src", "sky", "downloads")
OUT = os.path.join(ROOT, "Game", "Assets", "Monolith", "Sky", "Resources", "MonolithSky", "stars.bytes")

# ---- colour ----------------------------------------------------------------------------------------------------------

def bv_to_temperature(bv):
    bv = max(-0.4, min(2.0, bv))
    return 4600.0 * (1.0 / (0.92 * bv + 1.7) + 1.0 / (0.92 * bv + 0.62))   # Ballesteros 1978


def blackbody_linear_srgb(t):
    """Planckian locus xy (Kang et al. 2002, 1667-25000 K) -> linear sRGB with luminance Y = 1."""
    t = max(1667.0, min(25000.0, t))
    if t < 4000.0:
        x = -0.2661239e9 / t**3 - 0.2343589e6 / t**2 + 0.8776956e3 / t + 0.179910
    else:
        x = -3.0258469e9 / t**3 + 2.1070379e6 / t**2 + 0.2226347e3 / t + 0.240390
    if t < 2222.0:
        y = -1.1063814 * x**3 - 1.34811020 * x**2 + 2.18555832 * x - 0.20219683
    elif t < 4000.0:
        y = -0.9549476 * x**3 - 1.37418593 * x**2 + 2.09137015 * x - 0.16748867
    else:
        y = 3.0817580 * x**3 - 5.87338670 * x**2 + 3.75112997 * x - 0.37001483
    X, Y, Z = x / y, 1.0, (1.0 - x - y) / y
    r = 3.2404542 * X - 1.5371385 * Y - 0.4985314 * Z
    g = -0.9692660 * X + 1.8760108 * Y + 0.0415560 * Z
    b = 0.0556434 * X - 0.2040259 * Y + 1.0572252 * Z
    return max(r, 0.0), max(g, 0.0), max(b, 0.0)


_BPRP_BV = [(-0.5, -0.25), (0.0, 0.0), (0.45, 0.30), (0.82, 0.65), (1.04, 0.82), (1.45, 1.15), (1.85, 1.40), (2.5, 1.52), (4.0, 1.70)]


def bprp_to_bv(x):
    """Approximate B-V from Gaia BP-RP by interpolating main-sequence anchor points (Mamajek table, rounded)."""
    pts = _BPRP_BV
    if x <= pts[0][0]:
        return pts[0][1]
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        if x <= x1:
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0)
    return pts[-1][1]


def g_to_v(g, bprp):
    """Gaia DR3 documentation, G - V as a cubic in BP-RP (valid roughly -0.5..2.75)."""
    x = max(-0.5, min(2.75, bprp))
    return g - (-0.02704 + 0.01424 * x - 0.2156 * x * x + 0.01426 * x**3)


def pack_colour(bv, flags):
    r, g, b = blackbody_linear_srgb(bv_to_temperature(bv))
    m = max(r, g, b, 1e-6)
    return int(round(255 * r / m)) | int(round(255 * g / m)) << 8 | int(round(255 * b / m)) << 16 | flags << 24


def unit(ra_deg, dec_deg):
    ra, dec = math.radians(ra_deg), math.radians(dec_deg)
    c = math.cos(dec)
    return c * math.cos(ra), c * math.sin(ra), math.sin(dec)

# ---- catalogues ------------------------------------------------------------------------------------------------------

def read_bsc():
    stars = []
    with gzip.open(os.path.join(DL, "bsc5.dat.gz"), "rt", encoding="latin-1") as f:
        for line in f:
            if len(line) < 115 or not line[75:77].strip() or not line[102:107].strip():
                continue   # novae and other non-stars have no position or magnitude
            try:
                ra = (int(line[75:77]) + int(line[77:79]) / 60.0 + float(line[79:83]) / 3600.0) * 15.0
                dec = int(line[84:86]) + int(line[86:88]) / 60.0 + int(line[88:90]) / 3600.0
                if line[83] == "-":
                    dec = -dec
                v = float(line[102:107])
            except ValueError:
                continue
            bv_s = line[109:114].strip()
            bv = float(bv_s) if bv_s else 0.65
            stars.append((ra, dec, v, bv))
    return stars


def read_gaia():
    stars = []
    for path in sorted(glob.glob(os.path.join(DL, "gaia_*.csv"))):
        if not os.path.basename(path)[5].isdigit():
            continue
        with open(path, newline="") as f:
            for row in csv.DictReader(f):
                ra, dec = float(row["ra"]), float(row["dec"])
                if row["pmra"] and row["pmdec"]:                    # epoch 2016.0 -> 2000.0
                    pmra, pmdec = float(row["pmra"]), float(row["pmdec"])
                    dec -= pmdec * 16.0 / 3.6e6
                    ra -= pmra * 16.0 / (3.6e6 * max(math.cos(math.radians(dec)), 1e-3))
                g = float(row["g"])
                bprp = float(row["bp_rp"]) if row["bp_rp"] else 0.82
                v = g_to_v(g, bprp) if row["bp_rp"] else g
                stars.append((ra % 360.0, dec, v, bprp_to_bv(bprp)))
    return stars


def main():
    bsc = read_bsc()
    gaia = read_gaia()

    # Match Gaia against Yale on a 1 degree grid.
    grid = {}
    for ra, dec, v, bv in bsc:
        grid.setdefault((int(ra), int(dec + 90)), []).append(unit(ra, dec))
    cos15 = math.cos(math.radians(15.0 / 3600.0))
    kept, dropped_bright, dropped_match = [], 0, 0
    for ra, dec, v, bv in gaia:
        if v < 6.3:
            dropped_bright += 1
            continue
        u = unit(ra, dec)
        i, j = int(ra), int(dec + 90)
        matched = False
        for di in (-1, 0, 1):
            for dj in (-1, 0, 1):
                for w in grid.get(((i + di) % 360, j + dj), ()):
                    if u[0] * w[0] + u[1] * w[1] + u[2] * w[2] > cos15:
                        matched = True
                        break
                if matched:
                    break
            if matched:
                break
        if matched:
            dropped_match += 1
        else:
            kept.append((ra, dec, v, bv))

    rows = [(ra, dec, v, bv, 1) for ra, dec, v, bv in bsc] + [(ra, dec, v, bv, 2) for ra, dec, v, bv in kept]
    rows.sort(key=lambda r: r[2])
    n = len(rows)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "wb") as f:
        f.write(b"MNSKY001")
        f.write(struct.pack("<II", n, 0))
        for ra, dec, v, bv, fl in rows:
            x, y, z = unit(ra, dec)
            f.write(struct.pack("<ffff", x, y, z, v))
        for ra, dec, v, bv, fl in rows:
            f.write(struct.pack("<I", pack_colour(bv, fl)))

    counts = [sum(1 for r in rows if r[2] < m) for m in (2, 4, 6, 7, 8, 9)]
    info = {"stars": n, "bsc": len(bsc), "gaia_in": len(gaia), "gaia_kept": len(kept), "gaia_dropped_brighter_than_V6.3": dropped_bright,
            "gaia_dropped_matched_to_bsc": dropped_match, "brightest": rows[0][2], "count_below_V_2_4_6_7_8_9": counts,
            "bytes": os.path.getsize(OUT)}
    print(json.dumps(info, indent=1))


if __name__ == "__main__":
    main()
