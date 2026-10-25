"""Fetch the raw Jupiter source data into art_src/jupiter/downloads (git-ignored). Licences and credits: docs/licences/planets.md

Run:  python -I tools/jupiter/fetch_sources.py
Idempotent: a file that already exists with the right size is kept. Standard library only.
"""
import hashlib
import os
import sys
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEST = os.path.join(ROOT, "art_src", "jupiter", "downloads")

# name, url, bytes, sha256
FILES = [
    ("PIA07782.tif",
     "https://assets.science.nasa.gov/content/dam/science/psd/photojournal/pia/pia07/pia07782/PIA07782.tif",
     19456427, "f92ffc7a2fa0235c47ec8f1f168dd36bffeb6acc318292b237878a00a48cf7d9"),
    # USGS Astrogeology: Io, Galileo SSI / Voyager colour merged global mosaic 1 km (the 1024 x 512 browse image) and Europa, Voyager / Galileo SSI global mosaic 500 m (the 1024 browse image)
    ("io_browse_full.jpg",
     "https://astrogeology.usgs.gov/ckan/dataset/f6924861-ce9c-490d-8a4b-7812a20f2de5/resource/a9fab679-8081-4144-9f58-45848836c8f5/download/full.jpg",
     201833, "238b50008a15fe440771e0e86bb6223e2fc92457e988d0ca4bd69ebb510e0823"),
    ("europa_500m_1024.jpg",
     "https://astrogeology.usgs.gov/ckan/dataset/4080036f-afc5-422e-abe9-1c0c8e4f98ea/resource/3647e7b3-425e-4dcf-951b-cc4a22fb0129/download/europa_voyager_galileossi_global_mosaic_500m_1024.jpg",
     71532, "96453723e4dd7d7b709cbc097282b9292360908139c72a617b0d26d9ae9ce5f9"),
]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    os.makedirs(DEST, exist_ok=True)
    for name, url, size, digest in FILES:
        path = os.path.join(DEST, name)
        if os.path.exists(path) and os.path.getsize(path) == size and sha256(path) == digest:
            print("have", name)
            continue
        print("fetch", url)
        req = urllib.request.Request(url, headers={"User-Agent": "monolith-jupiter-fetch/1"})
        with urllib.request.urlopen(req, timeout=600) as r, open(path + ".part", "wb") as f:
            while True:
                b = r.read(1 << 20)
                if not b:
                    break
                f.write(b)
        os.replace(path + ".part", path)
        if sha256(path) != digest:
            print("WARNING: sha256 of", name, "is", sha256(path), "(expected", digest + ")")
            sys.exit(1)
        print("ok", name, os.path.getsize(path), "bytes")


if __name__ == "__main__":
    main()
