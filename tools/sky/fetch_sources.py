"""Fetch the raw source data for the Monolith sky into art_src/sky/downloads (git-ignored).

Run:  python -I tools/sky/fetch_sources.py [--8k]
Idempotent: files that already exist with the right size are kept. Licences and credits: Game/Assets/Monolith/Sky/SOURCES.md
Standard library only.
"""
import os
import sys
import urllib.parse
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEST = os.path.join(ROOT, "art_src", "sky", "downloads")

SVS = "https://svs.gsfc.nasa.gov/vis/a000000/a004800/a004851/"
GAIA_TAP = "https://gea.esac.esa.int/tap-server/tap/sync"
BSC = "http://tdc-www.harvard.edu/catalogs/bsc5.dat.gz"

# Gaia DR3 slices by G magnitude (the synchronous endpoint silently truncates at 50,000 rows, so keep slices below that): 6.0..9.0 is ~170k stars.
GAIA_SLICES = [(6.0, 7.0), (7.0, 8.0), (8.0, 8.5), (8.5, 8.75), (8.75, 9.0)]


def get(url, dest, data=None):
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        print("have", os.path.basename(dest))
        return
    print("fetch", url if data is None else url + " (query)", "->", os.path.basename(dest))
    req = urllib.request.Request(url, data=data, headers={"User-Agent": "monolith-sky-fetch/1"})
    with urllib.request.urlopen(req, timeout=600) as r, open(dest + ".part", "wb") as f:
        while True:
            chunk = r.read(1 << 20)
            if not chunk:
                break
            f.write(chunk)
    os.replace(dest + ".part", dest)


def main():
    want8k = "--8k" in sys.argv
    get(SVS + "milkyway_2020_4k.exr", os.path.join(DEST, "milkyway_2020_4k.exr"))
    if want8k:
        get(SVS + "milkyway_2020_8k.exr", os.path.join(DEST, "milkyway_2020_8k.exr"))
    get(BSC, os.path.join(DEST, "bsc5.dat.gz"))
    for lo, hi in GAIA_SLICES:
        q = (f"SELECT ra, dec, pmra, pmdec, phot_g_mean_mag AS g, bp_rp FROM gaiadr3.gaia_source "
             f"WHERE phot_g_mean_mag >= {lo} AND phot_g_mean_mag < {hi}")
        body = urllib.parse.urlencode({"REQUEST": "doQuery", "LANG": "ADQL", "FORMAT": "csv", "QUERY": q}).encode()
        get(GAIA_TAP, os.path.join(DEST, f"gaia_{lo:g}_{hi:g}.csv"), body)


if __name__ == "__main__":
    main()
