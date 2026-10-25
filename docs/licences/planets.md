# Planets and moons: sources and licences

Everything the backdrop planets are drawn from that is not our own code. Checked 2026-10-25. The downloads live in `art_src/jupiter/downloads/` (git-ignored; `python -I tools/jupiter/fetch_sources.py` fetches
them again and checks their sha256). The processed textures that ship are small (the whole set is under 7 MB) and are in `Game/Assets/Monolith/Sky/Resources/MonolithSky/Bodies/`.

## The short version

Every map is a NASA or USGS work from a NASA mission, with no restriction beyond giving the credit. Nothing here is non-commercial, share-alike or attribution-only-by-a-private-party.
A map that carried CC BY or a "please ask first" was left out (see "Looked at and not used").

| What | Source | Credit to give | Licence as stated by the source |
|---|---|---|---|
| Jupiter's albedo map | NASA Photojournal **PIA07782**, "Cassini's Best Maps of Jupiter (Cylindrical Map)" | NASA/JPL/Space Science Institute | NASA/JPL: free to use for any purpose without prior permission, with credit (JPL Image Use Policy); NASA-made, not copyrighted in the US |
| Io's map | USGS Astrogeology, **Io Galileo SSI / Voyager Color Merged Global Mosaic 1km** | USGS Astrogeology Science Center; NASA/JPL-Caltech (Galileo SSI, Voyager 1) | Access constraints "public domain", use constraints "None" |
| Europa's map | USGS Astrogeology, **Europa Voyager / Galileo SSI Global Mosaic 500m** | USGS Astrogeology Science Center; NASA/JPL-Caltech (Galileo SSI, Voyager 1 and 2) | Access constraints "None", use constraints "None" (a U.S. Government work) |

## Sources

### 1. Jupiter: Cassini's best maps of Jupiter, PIA07782
- Page: <https://photojournal.jpl.nasa.gov/catalog/PIA07782> (now served by <https://science.nasa.gov/photojournal/>); image added 2006-03-27; credit on the page: **NASA/JPL/Space Science Institute**.
- File: `PIA07782.tif`, 3601 x 1801 pixels (0.1 degree a pixel, 360 degrees of longitude by 180 of latitude, planetocentric), 19,456,427 bytes,
  sha256 `f92ffc7a2fa0235c47ec8f1f168dd36bffeb6acc318292b237878a00a48cf7d9`. URL: <https://assets.science.nasa.gov/content/dam/science/psd/photojournal/pia/pia07/pia07782/PIA07782.tif>
- What it is (the page's own words, shortened): colour maps built from 36 narrow-angle camera images taken on 2000-12-11 and 12 as Cassini neared Jupiter on its way to Saturn (two colours, 750 nm near-infrared and 451 nm blue,
  the map's colours "close to those the human eye would see"); "the most detailed global color maps of Jupiter ever produced"; the smallest visible features are about 120 km across. The north edge (above 88.4 N) and
  everything south of 82.2 S is flat grey padding, because Cassini never saw the poles clearly.
- Licence: the Photojournal publishes it under the **JPL Image Use Policy** (<https://www.jpl.nasa.gov/jpl-image-use-policy>): "unless otherwise noted, images and video on JPL public web sites may be used for any purpose without prior permission",
  with the credit as the caption gives it. It is NASA-made (NASA's media guidelines: NASA content is generally not subject to copyright in the US). The caption carries no third-party ownership notice. The Space Science Institute named in the credit
  is the Cassini imaging team's home, working under the NASA mission.
- What we do to it (`tools/jupiter/build_map.py`): fill the two polar gaps with soft haze made from the nearest real rows (mirrored across the edge, shifted half way round, blurred more and more toward the pole until it is the local mean
  colour), drop the repeated wrap column, resample with Lanczos to 8192 x 4096, save as a JPEG (quality 95, 4:4:4; Unity compresses it to BC7 on import). The game adds fine detail beyond the map's resolution at run time
  (`MonolithPlanet.hlsl`, "Where one map texel is bigger than a pixel..."), only where the camera zooms in far enough for a texel to be larger than a pixel; none of it is saved into the texture.
- Not claimed: that it shows Jupiter as it is now. It is Jupiter of December 2000: the Great Red Spot is larger and rounder than today's, and the Southern Equatorial Belt is as it was then.

### 2. Io: USGS Galileo SSI / Voyager colour merged global mosaic, 1 km
- Page: <https://astrogeology.usgs.gov/search/map/io_galileo_ssi_voyager_color_merged_global_mosaic_1km>. Access constraints "public domain", use constraints "None". Data: Galileo SSI and Voyager 1 ISS images (the USGS page lists the references).
- File used: the product's 1024 x 512 browse image (`full.jpg`), 201,833 bytes, sha256 `238b50008a15fe440771e0e86bb6223e2fc92457e988d0ca4bd69ebb510e0823`
  (<https://astrogeology.usgs.gov/ckan/dataset/f6924861-ce9c-490d-8a4b-7812a20f2de5/resource/a9fab679-8081-4144-9f58-45848836c8f5/download/full.jpg>). A moon is 5 to 20 pixels across on screen, so the full 1 km mosaic (189 MB) is not needed.
- Processing: none (copied to `io_map.jpg` by `tools/jupiter/build_moons.py`).

### 3. Europa: USGS Voyager / Galileo SSI global mosaic, 500 m
- Page: <https://astrogeology.usgs.gov/search/map/europa_voyager_galileo_ssi_global_mosaic_500m>. Access constraints "None", use constraints "None". Data: Galileo SSI and Voyager images (USGS Geologic Investigations Series I-2757).
- File used: the product's 1024 x 512 image (`europa_voyager_galileossi_global_mosaic_500m_1024.jpg`), 71,532 bytes, sha256 `96453723e4dd7d7b709cbc097282b9292360908139c72a617b0d26d9ae9ce5f9`
  (<https://astrogeology.usgs.gov/ckan/dataset/4080036f-afc5-422e-abe9-1c0c8e4f98ea/resource/3647e7b3-425e-4dcf-951b-cc4a22fb0129/download/europa_voyager_galileossi_global_mosaic_500m_1024.jpg>).
- Processing (`tools/jupiter/build_moons.py`): it is black and white, so it is given a faint warm white (R 1.00, G 0.975, B 0.94; ours: Europa's ice is white to the eye with brown lines the black and white mosaic cannot show);
  the 16 black rows at the south pole (no data) are filled with the rows above, mirrored, blurred and shifted half way round.

### 4. Plain facts (no licence needed, sources named in the body files)
Radii, spin poles and orbit sizes and periods in `Bodies/*.body.json`: the IAU Working Group on Cartographic Coordinates and Rotational Elements, 2015 report (Archinal et al. 2018, Celestial Mechanics and Dynamical
Astronomy 130:22), and NASA's planetary satellite fact sheet. The zonal wind profile is our own smooth fit in the shape of the published Cassini and Hubble measurements (Porco et al. 2003, Science 299:1541; Vasavada and Showman 2005,
Reports on Progress in Physics 68:1935); it is not data and is marked so in the file. The Sun's distance from Jupiter (5.2 AU) is a plain fact.

## Looked at and not used
- **JunoCam global maps** (Gerald Eichstadter and John Rogers, from NASA/JPL/SwRI/MSSS raw images; <https://www.missionjuno.swri.edu/junocam/think-tank/maps-archive>): sharper and newer, but released as **CC BY** with a request to
  contact the maker before research or commercial use. Not NASA-made, so not taken. If a newer map is wanted, ask John Rogers first and credit him.
- **Hubble OPAL global maps** (NASA, ESA, A. Simon, M. H. Wong and the OPAL team; <https://archive.stsci.edu/hlsp/opal>): good photometry, similar resolution to Cassini's; the images ESA/Hubble releases carry **CC BY 4.0**. Credit would be required
  and the NASA-only status of the Cassini map is simpler. Not taken; it is the obvious second source if the Spot's present size is wanted.
- **Solar System Scope textures** (CC BY 4.0), **NASA Visible Earth**-style derived maps and any "enhanced" or painted Jupiter map from the web: not NASA-made, not taken.
- **Io and Europa full-resolution mosaics** (USGS, 189 MB and about 400 MB): same licence as the browse images; not needed at 5 to 20 pixels.

## Rebuilding
```
python -I tools/jupiter/fetch_sources.py      # downloads (sizes and sha256 checked)
python -I tools/jupiter/build_map.py          # -> Bodies/jupiter_map.jpg (8192 x 4096)
python -I tools/jupiter/build_moons.py        # -> Bodies/io_map.jpg, europa_map.jpg (1024 x 512)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 Monolith.EditorTools.BodySetup.Run     # checks the import: BC7, 14 mips
```
(Pillow only. The map's size is a power of two on purpose: a 7200 x 3600 JPEG imported as uncompressed RGBA32, 138 MB, because Unity does not compress a non-power-of-two texture with `npotScale` off; at 8192 x 4096 it
is BC7, 43 MB with its mips.)
