# Sky data sources and licences

Everything the sky draws from outside the repo's own code. Checked 2026-10-21 to 2026-10-23. Downloads live in
`art_src/sky/downloads/` (git-ignored; `python -I tools/sky/fetch_sources.py` fetches them again). The two processed files that
ship are in `Resources/MonolithSky/`: `stars.bytes` (3.4 MB) and `milkyway.exr` (17.6 MB).

## The short version, and the risk

Every real-sky source here comes from ESA missions or from NASA's use of them, and ESA's data licence is **non-commercial**
(CC BY-NC 3.0 IGO). That is fine for the current placeholder stage and for a free project. If Monolith is ever sold, get ESA's
written permission (the "Terms and Conditions for the use of data in the ESA space science archives" the Gaia page points to) or
swap the data. The cleanest swap is the Yale Bright Star Catalogue alone (about 9,000 stars, no stated restriction); every row in
`stars.bytes` carries a flag (1 = Yale, 2 = Gaia) so a Yale-only file is a one-line filter in `tools/sky/build_stars.py`. The Milky Way
map has no equivalent and would need replacing.

## Sources

### 1. NASA SVS "Deep Star Maps 2020" (the Milky Way)
- Page: https://svs.gsfc.nasa.gov/4851/ (Scientific Visualization Studio, Ernie Wright, visualizer). Released 2020-09-09.
- File used: `milkyway_2020_4k.exr`, 4096 x 2048, equirectangular, ICRF equatorial, 0h RA in the centre, RA increasing to the left,
  half-float, "Milky Way background with the bright (Hipparcos, Tycho-2) stars removed".
  URL: https://svs.gsfc.nasa.gov/vis/a000000/a004800/a004851/milkyway_2020_4k.exr, 36,436,668 bytes,
  sha256 `2eb802d6e68d170b410f766c7fec07f7518619f6b6708fdc81e9302d93e74fdb`.
  (The 8k version, 137 MB, was also downloaded to compare and is not used: it cannot be committed.)
- Built from ESA Gaia DR2 (about 1.7 billion stars), with Hipparcos-2 and Tycho-2 for the bright end.
- Licence: the page states no licence. NASA's media guidelines (https://www.nasa.gov/nasa-brand-center/images-and-media/) say NASA content
  is generally not subject to copyright in the US; credit NASA and do not imply endorsement. Requested credit:
  **"NASA/Goddard Space Flight Center Scientific Visualization Studio. Gaia DR2: ESA/Gaia/DPAC"**.
  The Gaia and Hipparcos data underneath are ESA's (see 2 and 4), so treat the map as carrying ESA's non-commercial licence.
- Processing (`tools/sky/build_milkyway.py`): isolated bright pixels clamped (276 of 8.4 million), mantissas rounded to 5 bits so ZIP
  compression halves the file, written as 16-bit RGB EXR. Values stay in the source's units (0 to 1).

### 2. ESA Gaia DR3 (the fainter catalogue stars, V 6.3 to about 9)
- Archive: https://gea.esac.esa.int/archive/ , queried through the TAP service `https://gea.esac.esa.int/tap-server/tap/sync` with
  `SELECT ra, dec, pmra, pmdec, phot_g_mean_mag, bp_rp FROM gaiadr3.gaia_source WHERE phot_g_mean_mag >= 6 AND < 9`,
  in five slices (the endpoint silently truncates at 50,000 rows), 170,662 rows. sha256 of each slice is not recorded because the archive
  can change its row order; the row counts are in `tools/sky/fetch_sources.py`.
- Licence: **CC BY-NC 3.0 IGO**. The Gaia licence page (https://www.cosmos.esa.int/web/gaia-users/license) says "Gaia data are distributed under the
  CC BY-NC 3.0 IGO license" and sends commercial use to ESA's archive terms. Acknowledgement: **"This work has made use of data from the
  European Space Agency (ESA) mission Gaia (https://www.cosmos.esa.int/gaia), processed by the Gaia Data Processing and Analysis
  Consortium (DPAC, https://www.cosmos.esa.int/web/gaia/dpac/consortium)."**
- Used as: position (moved from epoch 2016.0 to J2000 with the proper motion), G magnitude converted to V with the DR3 documentation's cubic
  in BP-RP, BP-RP turned into a rough B-V and then a blackbody colour. Stars brighter than V 6.3 are dropped (Gaia photometry saturates there
  and the Yale catalogue covers them), and so is any Gaia star within 15 arcsec of a Yale star.

### 3. Yale Bright Star Catalogue, 5th revised edition (the brightest stars, about 9,100 to V 6.5)
- Hoffleit, D. and Warren, W.H. Jr., 1991 (preliminary 5th edition), distributed by NASA's Astronomical Data Center and CDS (VizieR V/50).
  File: `bsc5.dat.gz` from http://tdc-www.harvard.edu/catalogs/bsc5.dat.gz (590,228 bytes,
  sha256 `0471ced07fc241e625613baa689b2fa5f4b59fd733f731003e57d2d436036cdb`). J2000 positions, V and B-V as listed.
- Licence: **none stated** in the catalogue's ReadMe or on the pages checked; it has been freely distributed by NASA ADC and CDS since 1991.
  It is a table of astronomical measurements. Cite it as above. Treat "no stated restriction" as the licence, not as a grant.

### 4. Not used, but checked
- **HYG database** (https://github.com/astronexus/HYG-Database): ready-made, about 120,000 stars. Its own licence is CC BY-SA (share-alike) and its
  data are Hipparcos, Yale and Gliese, so the Hipparcos terms (below) reach it. We built from the primary catalogues instead.
- **ESA Hipparcos and Tycho catalogues** (https://www.cosmos.esa.int/web/hipparcos/catalogues): **CC BY-NC 3.0 IGO**, credit "ESA". Same non-commercial term as Gaia.
- **ESO or ESA Gaia sky-colour panoramas**: painted-looking composites, not wanted.

## What the numbers are
- `stars.bytes`: 176,413 stars, 9,096 Yale plus 167,317 Gaia, brightest first (Sirius, V -1.46), faintest V 10.3 (red Gaia stars).
  Counts below V 2 / 4 / 6 / 7 / 8 / 9: 48 / 513 / 5,023 / 14,758 / 44,571 / 129,604, which match the real sky (about 48, 520, 4,800 to 5,000, 14,000 to 15,000, 42,000 to 45,000).
  Layout in `tools/sky/build_stars.py`. The game draws those brighter than `SkyOptions.LimitMagnitude` (7.6 by default).
- Colours are blackbody approximations from B-V (Ballesteros 1978, Kang et al. 2002 locus); Gaia colours go BP-RP to B-V by a rounded main-sequence table, so
  colours of fainter stars are approximate.

## Rebuilding
```
python -I tools/sky/fetch_sources.py            # downloads (add --8k for the big map if you want to experiment)
python -I tools/sky/build_stars.py               # -> Resources/MonolithSky/stars.bytes
powershell -NoProfile -ExecutionPolicy Bypass -File tools\blender.ps1 tools\sky\build_milkyway.py   # -> milkyway.exr
powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 Monolith.EditorTools.SkySetup.Run
```
