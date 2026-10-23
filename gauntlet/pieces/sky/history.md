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

## Round 2 (builder, 2026-10-23): a camera, not ideal optics

Study first (`reference/critic_pool/space_and_sky/expanse`, `capital_ship_close/expanse`, `gunfire_and_tracers/expanse`; viewed and sampled, nothing copied):
- With the Sun in frame (`7_ATYOm3nNc_0046`) the whole picture is veiled in cool lavender-blue haze, the dark corners are about (30 to 40, 40 to 55, 65 to 95) of 255, and there are several broad cyan-teal horizontal streaks of different height, width and length, not one hairline. The ship in front is washed by it.
- Without the Sun (`_0048`, `_0050`, the Rocinante frame) the black really is near zero ((0, 2, 3)), with a dense scatter of tiny dots of very mixed brightness. So the floor and the haze come from the glare and the grade of that shot, not from a fixed lift everywhere: a small tinted floor, and the veil and haze doing the rest.
- Bright points have soft round halos and a few small streaks; faint ones are most of the count and barely above the floor.

What changed:
- **New system `Monolith.Look`** (`Scripts/Render/Look`, shader and nothing else in `Look/Resources/MonolithLook`): `CameraLook.Enable(camera)` adds two HDRP custom passes that run only for that camera, with `LookOptions.Enabled` and `.Intensity` (master, 0 to 1) to switch off or turn down. `SkyRig.Enable` turns it on and feeds it the Sun (`SkyOptions.CameraLook`, options in `SkyOptions.Look`); a scene with no sky can call `CameraLook.Enable(camera)` for the film alone. The HUD must not get grain: draw it in a screen-space-overlay canvas or by another camera; the look is the 3D camera only.
- **Lens pass** (before post-processing, added over everything, so it veils ships): a wide cool veil lifting the frame around the Sun, a very wide haze that reaches the corners, power-law wings and a tight halo (red a little tighter and blue a little wider: a colour fringe); a thin pale streak and three broad tinted ones, uneven in thickness, brightness, length (longer to one side), hue (teal drifting toward violet) and wandering a little off the line; four ghosts on the line through the middle of the picture (round and six-sided, tinted); and a procedural lens-dust map (fine specks, fibres, scratches, smudges, bokeh discs, fixed seed, fixed to the screen) that lights up only where the glare is strong. The Sun's visibility is measured from depth, once per vertex: a hull in front dims the core halo, the thin streak and the ghosts and leaves the wide veil (the scatter around its edge). The Sun's core stays in the sky pass. These replace round 1's `SunHalo`, `SunVeil` and `SunStreak` options.
- **Film pass** (after post-processing, display values): a graded black floor (about (3, 6, 11) of 255 at the centre, (2, 4, 8) at the corners; a cool navy that is lighter toward the middle), a 10% vignette, and fine film grain (std about 2.3 of 255, clumps a little over a pixel, mostly neutral, strongest in shadows and mids, seeded by the frame so a replay matches). Floor and grain are applied in display-encoded values; in linear they exploded near black.
- **Stars read like a lens, not a plot**: brightness now follows real flux ratios (gamma 1.0, gain 0.7 at magnitude 6), so a few stars are bright and big (halo, wings, and a faint horizontal streak on the brightest ones) and most are barely above the floor; the catalogue goes to V 8.8. The stars fainter than the floor sit in the grain.
- Round 1's black lift in the sky pass is now 0 (the ACES toe swallowed it); the grade owns the floor.

Captures (`gauntlet/captures/sky/r2/`, fresh build, all of r1's frames plus):
`starfield_nomw` (south galactic pole, no Milky Way), `sun_only` and `sun_only_edge` (glare with no stars or Milky Way: `gauntlet/shots/sky_sunonly.json`), `hull_crossing_sun_a` (Sun just past the hull's end) and `_b` (Sun behind the hull). The turns are `turn_slow_*` and `turn_fast_*` as before.

Cost (1080p, RTX 4070 Ti, same view; GPU ms from the capture harness): bare scene 0.596; the camera look alone 0.767; sky and look together 0.812, so about +0.22 ms (1.3% of a 60 fps frame): average 1425 against 1214 fps, 1% low 1073 against 1033. The look costs 0.17 of that: a copy of the picture and two full-screen passes. (`r2_baseline`, `r2_lookonly`, `r2`.)

Stars in motion, measured again (717 to 739 stars tracked; the turn with the look's grain and dust off is `r2_shimmer`): total light varies 4.0% median (6.0% p95), crawl 0.029 px median; with the grain on it is 4.6% and 0.047 px, so the grain adds a little twinkle and no crawl. The peak pixel still varies 1.5x between best and worst sub-pixel position, as before.

My read, honestly:
- The two sun pairs should be closer: there is a veil, uneven tinted streaks, ghosts, dust that glows near the Sun, a haze that reaches the corners and a graded floor with grain. The show's Sun frames are still brighter and bluer: in `sun_toward` the sky reaches about (23, 28, 37) of 255 a fifth of a frame above the Sun, (44, 48, 58) in the middle of the frame, and (3 to 5, 6 to 8, 10 to 14) in the far corners, where the show's `XuqEX1PnG9I_0004` sits near (20 to 27, 25 to 37, 21 to 41) and `7_ATYOm3nNc_0046` at (30 to 40, 40 to 55, 65 to 95). Turn `VeilEnergy`, `HazeEnergy` and `VeilTint` up if the critic still names haze. I held it back so a Sun frame stays readable.
- `sun_only` is mine alone to judge (nothing in the show is just glare); the streak is still a long thin line with soft siblings, and a critic may find the ghosts too tidy (round discs and one hexagon). No two-layer anamorphic bloom shapes, no flare "sparkle" rings.
- The grain is a procedural hash, not a scan of film; at 1:1 it is believable, at a glance it is a hint of noise. The floor is deliberately small: the no-Sun show frames are near zero.
- Dust is plausible but sparse near the Sun (the fibre and scratch are faint). A canopy with real dirt would show more.
- Not done: halation (red bleed around bright lights), vertical bloom bands on explosions, lens dust lit by lasers and explosions (it only reacts to the Sun), chromatic aberration at the edges, motion blur.

## Round 2 verdict (lead, 2026-10-23)
Fresh blind critic, 3 pairs vs The Expanse: **lost 3/3** (certain, likely, certain). Ours: A, B, B.
- Pairs: sun_wide vs 7_ATYOm3nNc_0190 (2:1); sun_only vs 7wFt319UB2o_0004; starfield_nomw vs 7wFt319UB2o_0011.
- Progress: pair 2 was only "likely", and the critic said the real frame "looks like a stock flare overlay".
- Biggest giveaway now: procedural precision rather than optics. Hairline one-pixel rims on the ring ghosts and a clean hexagon; a streak of constant width edge to edge with no falloff; stars as crisp dots with no point-spread halo or bloom; the black reads clean and flat (the grain does not survive the critic's 1280-wide JPEG, so check frames at that size).
- Gap for round 3: everything optical should be soft, uneven and falling off: soft-edged ghosts (or fewer), tapered streaks, a real point-spread function on bright and mid stars, and a noise floor that still reads after downscaling. Pointer, not prescription.

## Round 3 (builder, 2026-10-24): soft, uneven, falling off

Checked first at the critic's size: `python -I tools/sky/critic_view.py <folder>` scales each frame to 1280 wide and saves it as JPEG (ffmpeg `-q:v 3`) and prints the luma standard deviation of the darkest 120 x 80 block before and after. Round 2 held 1.0 to 1.4 at 1920 and only 0.5 to 0.6 after the JPEG: the grain did not survive, so it did read flat. For scale, the same measurement on the references' darkest blocks: `XuqEX1PnG9I_0004` and `_0005` 2.1 and 2.2, `7wFt319UB2o_0004` 1.6, but `7wFt319UB2o_0011` only 0.2 and `7_ATYOm3nNc_0155` 0.0. So the show's black is not noisy everywhere; I matched the glare frames (about 1.5 to 2.3), not the flat ones.

What changed (all in `Monolith.Look` and the sky star shader; no shared files):
- **Ghosts**: no rim and no clean polygon. Three soft glows (a Gaussian body, lopsided and lumpy, one a hexagon blurred and taken halfway to a circle), tinted blue, warm and pale violet, and they no longer vanish when the Sun is near the middle of the frame. The lens-dust bokeh is soft and lumpy too, and the dust fibres no longer curl into rings (that was the hairline circle in round 2's `sun_only`); scratches and fibres are fainter and a little wider.
- **Streaks**: fat near the Sun and thinning with distance, fading to nothing well before the edge of the frame (no constant-width line), broken into patches along their length, longer on one side, drifting teal to violet. Distances are in reach units: thin streak about 0.25 of the frame height each way, broad ones about 0.9.
- **Stars have a point-spread function**: the sharp core, a halo that even middling stars get a little of (a magnitude 3 to 5 star now glows), and wide wings on the bright ones (up to about 40 px), plus the horizontal streak on the brightest. Dim stars stay dots.
- **Black that survives**: grain is two scales, single-pixel static plus clumps of about 2 px (so a clump is still over a pixel after scaling to 1280), at a standard deviation near 3.6 of 255 (measured, the old noise function was weaker than its constant said), and a slow fixed mottle of a level or two over about 160 px. At 1280 JPEG the darkest block now measures 1.4 to 2.3 (`sun_only` 1.5, `starfield_nomw` 1.7, `milkyway_core` 2.3). The veil tint is a little bluer.

Cost (1080p, RTX 4070 Ti, same view): bare scene 0.590 ms GPU; sky and look together 0.897, so +0.31 ms (1.8% of a 60 fps frame); look alone 0.826. Round 2 was +0.22: the extra is the heavier grain and the bigger star halos (average 1416 against 1106 fps, 1% low 1060 against 1017).

Stars in motion: with the look's grain and dust off the stars are unchanged (563 tracked, total light varies 3.7% median, crawl 0.026 px, peak 1.5x between best and worst phase). With the look on, the grain now moves the faint stars about as much as the stars move (total light 11.8% median, crawl 0.17 px) because it is per frame noise of about 3.6 levels over stars that peak at 10 to 30: that is film-like twinkle, not crawling, but it is real. `LookOptions.GrainAmount` and `.Intensity` set it; lower them if the flight playtest finds it busy.

My read, honestly: ghosts, streaks and star halos now fall off and are uneven, so the "overlay" and "data plot" giveaways should be weaker, but I cannot judge noise texture by eye better than the critic. The Sun frames are still less vivid and less blue than the show's blue clips (corners (3 to 5, 6 to 8, 10 to 14) against the show's 20 to 90). The mottle is a hint; a real sensor shows more banding. Dust still reacts only to the Sun. The ghosts may now be too faint to read as ghosts at all in some frames; they are there in `sun_toward` and `sun_wide`.
