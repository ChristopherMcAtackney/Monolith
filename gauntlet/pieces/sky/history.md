# Piece: sky (stars, Milky Way, the Sun, the fill light)

Owner of the scene's environment lighting: the Sun (hard key light), the cold fill from the sky, exposure, tonemap and bloom.
Bar: The Expanse's space frames, blind. Code: `Game/Assets/Monolith/Scripts/Render/Sky` (assembly `Monolith.Sky`), data in
`Game/Assets/Monolith/Sky/Resources/MonolithSky`, sources and licences in `Game/Assets/Monolith/Sky/SOURCES.md`.

## Round 1 (builder, 2026-10-23)

### Using it
One call from any scene: `SkyRig.Enable(camera)`; options in `SkyOptions` (every field commented), `rig.Apply()` after changing one.
It makes the Sun (HDRP directional light), a volume that selects the sky and bakes the fill into HDRP's environment lighting, and a
second volume with fixed exposure (EV 14), ACES and bloom (`ConfigureExposure`/`ConfigureLook` = false leaves those to the scene).
The camera is set to draw the sky. Test scene: capture plans with `"scene": "sky"` (`Game/SkyTestWorld.cs`: a grey metal hull with
emissive light strips); `"sceneOptions": {...}` in a plan overrides any `SkyOptions` field by name. `"scene": "sky_off"` is the same
scene with the sky not drawn, for cost measurements.

### What it is
- **Stars**: a real catalogue, 176,413 stars (Yale Bright Star Catalogue for V < 6.5, Gaia DR3 to about V 9), correct positions
  (checked: Orion's belt, Betelgeuse orange upper left and Rigel blue-white lower right land where they should), V brightness, and
  colour from B-V. Drawn as one small quad per star in the sky pass: each star is a Gaussian core integrated exactly over each pixel (so
  its total light stays put as it slides over the pixel grid) plus a faint wide halo for bright ones; brightness follows real flux
  ratios raised to 0.85 so Sirius and a magnitude-7 star are both on screen. Sizes are in screen pixels, so 1080p and 4K get the same look.
- **Milky Way**: NASA SVS Deep Star Maps 2020, the Gaia-built diffuse band with the bright stars removed (the catalogue puts those back
  as points). Real dust lanes, real colour; contrast-stretched (gain 0.55, exponent 1.5) so the faint background drops to black and the
  band keeps its structure, desaturated to 0.7 and tinted cool.
- **The Sun**: ecliptic longitude 152, latitude +5 (an arbitrary place among the stars, far from the band so Sun frames are clean).
  A point, not a disc: a 0.8 px core, a soft halo, a very wide faint veil and a thin horizontal anamorphic streak (the show's
  signature), all in the sky pass. The same direction drives the directional light (100,000 lux, hard shadows, warm white).
- **Fill light** (the user's lighting direction, 2026-10-23): the sky bakes a cold blue-grey fill (about 5,500 nits at its brightest) into HDRP's
  environment, so every surface gets diffuse fill from the ambient probe and shine from the reflection cubemap. It is 1.7x stronger facing away from the
  Sun than toward it, and the Milky Way adds its own light, so a shadow side has a gradient instead of one flat tone. Shadow sides read
  about (26, 31, 45) of 255 against a black sky of about (2, 3, 4), lit faces about (130, 125, 125). The tonemapper's toe is brutal on
  low values (a fill of 1,500 nits came out at 5/255), which is why the number is large.
- **Exposure and bloom**: fixed EV 14 (a mid-grey surface in full Sun lands mid-tone, ships stay readable). Bloom starts at 0.9, so lit
  hulls stay clean while Sun glints, emissive lights and the brightest stars glow.
- **Cost**: all in HDRP's sky pass: one full-screen draw plus about 28,600 star quads (the ones brighter than V 7.6; the catalogue is sorted
  so the limit is just a shorter draw). 1080p on the RTX 4070 Ti, same scene with and without the sky: GPU 0.687 ms against 0.590 ms (+0.10 ms,
  0.6% of a 60 fps frame), average 1390 against 1419 fps, 1% low 1093 against 1080, p99 0.886 against 0.884 ms
  (`gauntlet/captures/sky/r1/perf_sky_on_toward_sun.json`, `r1_baseline/perf_sky_off_toward_sun.json`). The lighting cubemap is baked once.

### Motion: shimmer and crawl, measured
`turn_slow_00..23` turns the camera 0.005 degrees a frame (an eighth of a pixel) over a star-rich spot; `tools/sky/analyze_shimmer.py`
tracks 451 stars through it (`captures/sky/r1/shimmer.json`):

| measure | median | 95th percentile |
|---|---|---|
| a star's total light, relative std over the turn | 3.8% | 6.1% |
| crawl: centroid scatter around a straight line | 0.031 px | 0.082 px |
| peak pixel, brightest over dimmest | 1.52x | 1.82x |

Stars do not swim or crawl and do not blink in and out. What remains is the one thing an unfiltered pixel grid always does: a point's peak
pixel is brighter when it lands on a pixel centre than on a corner (1.5x at sigma 0.75, theory 1.45; a plain point-sampled Gaussian of the same size
is 1.8x, and sigma 0.65 measured 1.63x with energy 5.2% and crawl 0.039 px). Dim stars therefore twinkle a little as they move, as stars do in real video.
`StarSigmaPx` trades this against crispness (1.0 gives 1.25x and visibly softer dots). `turn_fast_00..11` is a 1.5 degree per frame turn (90 degrees a second) for the eye.

### Captures (`gauntlet/captures/sky/r1/`, all 1920x1080 from a fresh build, nothing retouched)
`milkyway_core`, `milkyway_rift` (the band), `starfield_quiet`, `starfield_orion`, `sun_toward`, `sun_centered`, `sun_edge` (Sun just outside the frame),
`sun_wide` (75 degree vertical), `hull_lit`, `hull_lit_field` (sunlit side), `hull_shadow`, `hull_shadow_field` (Sun behind the hull, shadow side facing the camera),
`hull_backlit` (hull in front of the Sun), `turn_slow_*`, `turn_fast_*`. Best three by my own eye: `milkyway_core`, `sun_wide`, `hull_shadow_field`.

### My read against the Expanse frames (no critic has seen these; the lead picks pairs)
What I looked at: `reference/critic_pool/space_and_sky/expanse` (7 frames). They are soft AV1 at 1280x720, so I judged structure, not texture.
- **Stars**: the show has sparse tiny dim dots with a few brighter ones. Ours match that density and size, are cleaner and sharper (a critic can
  see that they are not compressed), and carry real colour (orange Betelgeuse). Two of the seven show frames are a stock-looking blue flare over a
  field and are not a useful comparison for stars.
- **Milky Way**: real and good (dust lanes, grain from unresolved stars), better than any painted nebula. But the show hardly ever shows one, so a
  frame with a bright band may read as "too much sky" next to it; a quiet frame is safer. `MilkyWayGain` controls it.
- **Sun**: the show's Sun is a hot core in a wide, hazy, coloured glow (frames `XuqEX1PnG9I_0004/0005`, `7_ATYOm3nNc_0155/0190`): the glow fills a third of the frame,
  the dark area around it is lifted to about (20 to 35) of 255 in grey-green, and there is bokeh dust. Ours is a pinpoint with a restrained halo and a thin streak, which is
  what the brief asked for, and it is the biggest visible difference. If the critic names the Sun's glow as the giveaway, raise `SunVeil` and `SunHalo`,
  and lift the haze (the show's dark is not black near the Sun).
- **Black level**: the show's dark areas away from glare are about (0 to 10) of 255, ours about (2 to 4): comparable. Near the Sun theirs lifts a lot and ours does not.
- **Hull and light**: hard key, cold readable shadow side, emissive lights that bloom. The hull is a plain box, so this judges exposure and fill, not surface. The glare
  does not veil a ship that crosses the Sun: the hull cuts it with a clean edge (`hull_backlit`); the show lets glare wash over ships.
- **Not done**: film grain, dust motes, lens bokeh, glare over ships (a custom pass), and the Sun's streak is sky-only. Colour grade is mine, not matched to the show's teal.

### Things worth knowing
- HDRP's `GetSkyViewDirWS` points from the sky back toward the camera. Using it unflipped put the Milky Way 180 degrees from the stars (found by rendering the galactic centre
  and anticentre and comparing the map's brightness); the shader now flips it.
- The sky frame is the ecliptic: world +Y is ecliptic north, and `SkyOptions.SkyRotationEuler` rotates stars, Milky Way and Sun together.
- The map is 4096 x 2048 (about 11 pixels per degree at the equator), a little soft when magnified at narrow fields of view; the 8k map is 137 MB and cannot be committed.
- ESA data is non-commercial (see `SOURCES.md`).

## Round 1 verdict (lead, 2026-10-23)
Blind critic, 3 pairs vs The Expanse (space_and_sky): **lost 3/3** (certain, certain, likely). Ours was A in all three (random).
- Pair 1 sun_wide vs XuqEX1PnG9I_0004; pair 2 sun_toward vs 7_ATYOm3nNc_0155 (that reference also has a ship, a weak match); pair 3 starfield_quiet vs 7wFt319UB2o_0011.
- Biggest giveaway (consistent across all three): ideal optics on crushed black. Pure-zero black with no graded floor, no grain; the Sun's flare is a clean hairline of constant width over a smooth halo, with no ghosts, dust, haze or colour fringing. The faint stars read as an even sprinkle of 1px dots, "a data plot".
- Gap for round 2: the frame needs a camera. A graded, slightly lifted and tinted black floor with fine grain; sun glare with wide halo wings, a tinted, uneven anamorphic streak, ghosts and lens dust (the cockpit canopy justifies dirt). Treat the critic's wording as a pointer, not a prescription; don't mimic video compression.
