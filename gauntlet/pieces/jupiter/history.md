# Piece: jupiter (a planet behind the fight)

Owner of the backdrop planet: Jupiter, with Io and Europa, drawn by the sky for any mission that names one. The user's brief (2026-10-25): "I want to see planet battlescapes with planets looming
very large in the view ... I want photorealism and scale." Bar: The Expanse's space battles with FS2's meaty lasers; the planet itself is judged blind against real Juno, Cassini and Voyager
photographs of Jupiter. Code: `Game/Assets/Monolith/Scripts/Render/Sky/` (`BackdropOptions`, `BackdropState`, `BodyDef`; the sky's `SkyRig`, `MonolithSky`, `MonolithSkyRenderer` carry it) and
`Game/Assets/Monolith/Sky/Resources/MonolithSky/` (`MonolithPlanet.hlsl`, the fourth pass of `MonolithSky.shader`, and `Bodies/`: the maps and `*.body.json`). Mission data: `Scripts/Mission/README.md`
("Backdrop"), the test mission `Resources/Missions/jupiter.mission.json`, "Jupiter Skirmish (test)". Sources and licences: `docs/licences/planets.md`.

## Round 1 (builder, 2026-10-25)

### Using it
A mission file gets one block (see the Mission README for every field):
```json
"backdrop": { "body": "jupiter", "distanceKm": 200000, "direction": { "azimuth": 24, "elevation": -9 },
              "sun": { "azimuth": -100, "elevation": 6, "distanceAu": 5.2 }, "grsLongitude": -22,
              "look": { "planetshine": 3, "ambientScale": 0.3 }, "moons": [ { "body": "io", "orbitDeg": 300 }, { "body": "europa", "orbitDeg": 205 } ] }
```
Any scene can do the same with `SkyOptions.Backdrop` (the sky test scene, `"scene": "sky"`, takes it in a plan's `sceneOptions`). `Play.bat jupiter` plays the test mission; it is also on the start screen
("Jupiter Skirmish (test)", after Dead Water). `-mnNoBackdrop` plays any mission with a plain sky.

### Where it is drawn, and why that cannot break
The planet is an analytic sphere in the sky pass (`MonolithPlanet.hlsl`), seen from one fixed point at a distance in planet radii. The brief asked for no depth-precision artefacts, no clipping, correct order behind
every ship, beam and explosion, and the stars hidden behind the disc; drawing it as geometry at 200,000 km would fight the 100 km far plane and a 24-bit depth buffer, so it is not geometry:
- **No depth, no far plane, no size limit.** Each pixel intersects its view ray with the sphere (the impact parameter from a cross product, so it stays exact at the limb).
- **Behind everything.** HDRP's sky pass draws only where the depth buffer is still at the far plane, so every ship, beam, explosion and particle (all drawn into or over the scene earlier or later in their own passes) is in front of it. Shown in the captures: the Warden's beams and fireballs crossing the disc, fighters
  silhouetted against it, the Mercy in front of its limb.
- **No parallax.** At 200,000 km a flight of a few kilometres moves the planet by under a thousandth of a pixel, which is what sells the scale: the ships carry it.
- **Stars, Milky Way and Sun hidden behind the disc.** The sky's pass order is background (Milky Way, Sun point), planet (premultiplied over it), stars; the star pass asks `PlanetAlpha` for each star's direction and drops
  or dims it (disc, edge anti-aliasing and the thin haze), so no star shows through the planet.
- **Anti-aliasing at the limb is analytic** (the signed distance to the limb in pixels), so SMAA has nothing to fight; the texture is sampled with analytic gradients (the neighbours' rays), so the limb filters like a sphere
  (anisotropic) instead of shimmering.

### Distance and size: 200,000 km, 42 degrees
The brief gave 150,000 to 250,000 km (30 to 55 degrees). Disc size in the cockpit's own 55 degree frame (`gauntlet/shots/jupiter_r1_dist_*.json`, sheet in `gauntlet/captures/jupiter/r1/distance/`):

| distance from the centre | altitude over the clouds | disc | share of the frame height | reads as |
|---|---|---|---|---|
| 110,000 km | 38,500 km | 81 degrees | 147 percent | it fills the windscreen: you never see it whole |
| 150,000 km | 78,500 km | 57 degrees | 104 percent | overflows the frame by a hair |
| **200,000 km** | **128,500 km** | **41.9 degrees** | **76 percent** (49 percent of the 86 degree width) | **it looms, and still fits whole when you look at it** |
| 250,000 km | 178,500 km | 33 degrees | 60 percent | a big moon more than a world |
| 400,000 km | 328,500 km | 21 degrees | 37 percent | a planet in the distance |

200,000 km (2.8 radii) is the largest the whole disc fits the cockpit's frame with a margin, so the planet can be framed whole (the full-disc captures) and still dominates; the curvature of the limb is strong (a 21 degree limb radius),
which is what gives the scale; and at that range it does not parallax however far the fight drifts. It is also an altitude of 128,500 km, 1.8 radii over the cloud tops, so Io (421,700 km) and Europa (671,100 km) orbit outside it and are small discs beside it.

### Light: the Sun at 5.2 AU
Sunlight at Jupiter is 1/27.04 of Earth's: 3,698 lux against 100,000. The camera's exposure is opened by the same factor (2 log2 5.2 = 4.76 stops), which for everything the Sun lights (the planet, the ships) is exactly
the picture the game's reference exposure already shows (EV 14, 100,000 lux): the open iris and the dim Sun cancel. So the Sun's lux and the exposure stay at the reference and nothing authored in screen units (engine glow, windows,
explosions, lasers, the cabin's lights, the lens look) changes; `BackdropState.PhysicalSunLux` and `ExposureOpenStops` report the real numbers and the self-test asserts them. (The alternative, literally 3,698 lux and EV 9.24, would have made
every emissive and every juice light 27 times too bright on screen and needed them all re-authored.)

**Planetshine.** The sky's lighting cubemap, which HDRP turns into the ambient probe (the diffuse fill) and the reflection probe (the shine on chrome), includes the planet's disc, shaded the same way and at the radiance a
white surface has in the Sun's light (reflectance x 100,000 lux / pi, nits): so the ships get a big, soft, warm light from its direction with its phase (a lit disc on one side and a dark one on the other, which is the right hemisphere of fill and
a hole in the sky's own fill). The physical amount is small, about 2 percent of the Sun's on a hull facing the planet (Jupiter's Bond albedo is 0.5 and the disc is 2.8 radii away), which lands in the tonemapper's toe (+1.9 of 255 on a grey
hull face turned to the planet and in the Sun's shadow, `r1/evidence/planetshine/`), so the mission asks for `planetshine: 3` (+6.0 there) and turns the sky's cold fill down to 30 percent (`ambientScale`), so the planet's warm light is the main fill on its side. This is a taste setting,
not physics, and it is named so. Ships keep the Sun's shadows (the planet adds no shadow-casting light).

### The look (one planet, nothing that looks procedural)
- **The map.** NASA/JPL/SSI's Cassini map of Jupiter (PIA07782, December 2000; 0.1 degree a pixel, "close to the colours the eye would see"), polar gaps filled with haze, resampled to 8192 x 4096 and compressed as BC7
  (43 MB with mips). Public domain. Details and the two moon maps (USGS, public domain): `docs/licences/planets.md`.
- **Sunlight**: reflectance (the map's linear value x 1.15) x Lambert / Lommel-Seeliger mix (limb darkening: the disc falls to 45 percent at the limb at full phase) through a **soft terminator** (a softplus of the cosine to the Sun, about 20 degrees wide, reddening
  toward the dark: the light reaches past the geometric terminator the way it does through real cloud and haze). The **night side is black** (the tail is a thousandth of the day side at 30 degrees past the terminator).
- **Haze**: a thin blue column over the clouds that grows toward the limb, and the same haze seen edge-on outside the disc, a thin bright rim a few pixels thick (160 km scale height, a few times the real gas's, so it survives 1080p), lit with a wider twilight
  and forward scattering so a crescent glows at its rim and the dark limb stays dark.
- **Detail finer than the map**: where one texel is bigger than a pixel (close-ups below about 25 degrees of field of view) three octaves of noise stretched along the bands are added, strongest where the map itself has structure at its finest scale (belt edges and
  turbulence), faint in the smooth zones; it fades in only as the camera zooms in and is not saved in any texture. At 7 degrees it is still soft: the map is 120 km a pixel and a pixel there is 15 km.
- **Slow motion**: the planet turns 0.06 degree a second (real 0.01), the bands slide past one another at 120 times Cassini's and Hubble's zonal winds (an eastward equatorial jet of about 100 m/s, then alternating jets; our smooth fit, marked as ours in the body file),
  and the Great Red Spot's oval turns inside its shear collar (once in 12 minutes). At 300 times the winds, a static map sheared into herringbone streaks after 20 minutes and smeared by 30 (`r1/evidence/time_sheet.png`); the real clouds are renewed by turbulence and a map cannot be, so the default is 120, which is clean for the longest mission.
- **Moons**: Io and Europa as small lit discs on their real orbits (in the planet's equator plane, 421,700 and 671,100 km out) going round 100 times faster than real; a moon in the planet's shadow goes dark, one behind the disc is hidden, one in front of the Sun's side throws its
  shadow on the clouds (a hard black dot with a penumbra from the Sun's disc: the Jupiter Skirmish has Io's shadow come onto the lit clouds about 4.4 minutes in and cross the edge of the Great Red Spot at about 4.7, `gauntlet/captures/jupiter/r1/planet_shadow`). For that the test mission's Sun is 6 degrees off the planet's equatorial plane, which is where
  Jupiter's Sun always is (the tilt of its axis is 3 degrees), not the 35 degrees I first picked for the ships' light.
- **No ring.** Jupiter's ring is a thousandth as bright as its cloud tops and invisible against it in sunlight; it would show only backlit, from inside the planet's shadow. It was left out because it would not read as real.

### The test mission
`jupiter.mission.json` is `dogfight.mission.json` with a new id and name, the backdrop block, and one more briefing stage ("(Test) The same fight as The Dogfight, in front of Jupiter."): the same five ships, nine wings, 26 events,
16 radio lines word for word (so every line has the clip it already had; no voice made, no line written; `audio_src/voice/collect_lines.py` has the same intensity tags for `jupiter:` as `dogfight:`, and the voice index gained only the new source names). The Warden stands 3 km ahead of the player in front of the planet's upper limb at the start,
the Mercy behind, Earth's waves arriving round them, with Jupiter four fifths lit and the Sun behind the player's left shoulder.

### What I built and how to rebuild
```
python -I tools/jupiter/fetch_sources.py && python -I tools/jupiter/build_map.py && python -I tools/jupiter/build_moons.py   # the maps (Pillow only)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 Monolith.EditorTools.BodySetup.Run                      # checks the import (BC7, 14 mips)
python -I tools/jupiter/make_plans.py && python -I tools/jupiter/make_battle_plans.py                                       # the capture plans
powershell -NoProfile -ExecutionPolicy Bypass -File tools\jupiter\capture_r1.ps1 -Out gauntlet\captures\jupiter\r1          # every still of the round
```
The source TIFF (19 MB) is not in git; the processed map is (5.6 MB).

### Numbers
All from the final build of this worktree (`Game/Builds/Win/Monolith.exe`, Unity 2022.3.26f1, RTX 4070 Ti, 2026-10-25); logs in `gauntlet/captures/jupiter/r1/checks/`.

| check | result |
|---|---|
| `-mnMissionSelfTest` | **98 of 98** (the new `backdrop` group is 15 of them, and Jupiter Skirmish is one more load and one more headless minute): the mission and its backdrop block load; it is the Dogfight's fight (5 ships, 9 wings, 26 events, 16 radio lines word for word); 41.89 degrees across at 200,000 km, 76 percent of the 55 degree frame height and 49 percent of the width; the Sun at 5.2 AU is 3,698 lux with the camera opened 4.76 stops; phase angle 56 degrees (78 percent lit); the frame is orthonormal and the pole 2.2 degrees from the world's up; the moons are placed (Io 0.39 degrees across, Europa 0.21); winds -30 to 131 m/s and still at the poles; the map is 8192 x 4096; three broken backdrops are refused with every problem named, a small good one loads; every radio line still has its clip |
| `-mnStartSelfTest` (headless) | **109 of 109** (the list is now five missions: ... Dead Water, Jupiter Skirmish (test); the test's hard-coded list and row indices were updated) |
| `-mnStartSelfTest -mnStartTour jupiter:Medium:pause,dogfight:Easy:end,jupiter:Hard:pause` (windowed, audio muted) | **63 of 63**: Jupiter launches from the screen, plays, goes back (the scenario's world, audio system, command line and clock as they were, no growth in objects or listeners), the Dogfight, Jupiter again |
| `tools\mission.ps1 -Exe ... -Difficulty Medium` | self-test PASS, then every mission's auto-play PASS: cabin (victory 142.5 s), **dogfight (victory at 410.6 s, 8 kills, no losses)**, escort (victory 296.3 s), **jupiter (victory at 410.6 s, 8 kills, no losses: bit for bit the Dogfight's fight, the planet changes nothing in the sim)**, oort (victory 671.4 s, Sable 2 and 3 lost as before) |
| `-mnAiSelfTest` | 39 of 39 |
| `-mnCombatSelfTest` | 115 of 115 |
| `-mnHudSelfTest` | 125 of 125 |
| `-mnCabinSelfTest` | 75 of 75 |
| `-mnAudioSelfTest` | pass, 150 checks, 38 s (the Jupiter lines are the Dogfight's, so no clip is missing and none was made) |
| `python -I audio_src/voice/collect_lines.py --check` | in date (the index only gained the `jupiter:` source names) |

**Performance** (1920 x 1080 window, vsync off, the real game: HUD on, motion dust on, the AI flying the player on Medium, `capture.ps1 -Kind perf`; `r1/perf/`):

| run (40 s each, back to back) | average | 1% low | p99 | GPU avg | CPU main | worst frame |
|---|---|---|---|---|---|---|
| Jupiter Skirmish, opening (3 to 43 s in) | 389 fps | 184 fps | 4.4 ms | 1.97 ms | 2.55 ms | 23.4 ms (once) |
| Jupiter Skirmish, middle | 440 fps | 255 fps | 3.7 ms | 1.73 ms | 2.26 ms | 5.9 ms |
| Jupiter Skirmish, late | 529 fps | 315 fps | 3.0 ms | 1.71 ms | 1.88 ms | 11.4 ms |
| the Dogfight, the same three windows, no planet | 465 / 444 / 533 fps | 232 / 251 / 308 fps | 3.8 / 3.6 / 3.0 ms | 1.82 / 1.71 / 1.68 ms | 2.12 / 2.24 / 1.86 ms | 12.3 / 29.2 / 12.8 ms |

The battle holds 60 fps with a wide margin: the worst 1% low is 184 fps (5.4 ms), the sim ran at 60.0 Hz throughout. **The planet's own cost** is below what a presented run can resolve (0.92 ms against 0.91 ms GPU in the sky test scene, the frame
limited by the CPU); with `-mnPerfSync` (every frame waits for the GPU, no overlap) the same view with and without the planet over three runs each is **1.803 ms against 1.770 ms per frame, +0.033 ms** (GPU average 1.17 against 1.13 ms, +0.037 ms): about
**0.035 ms, 0.2 percent of a 60 fps frame**, for a disc that fills three quarters of the frame height (`r1/perf/`, `jupiter_r1_perf_planet.json` and `_noplanet.json`). In the battle the GPU average is 0.02 ms above the planet-less Dogfight in the middle and late windows (1.725 against 1.710,
1.707 against 1.684) and 0.14 ms in the opening, which also has the one 23 ms frame (the first draw of the planet and its 43 MB texture in a capture run: a played start does that behind the loading screen, in the sky warm-up); an in-battle A/B under `-mnPerfSync` (`-mnNoBackdrop`, the same sim ticks) moves
by 10 percent from run to run on the sim's own cost, more than the planet's. The planet's map is 43 MB of video memory (BC7 with mips), the two moon maps about 1.3 MB.

**Planetshine, measured** (`r1/evidence/planetshine/`): a hull face turned to the planet and in the Sun's shadow reads 12.9 of 255 with no planetshine, 14.8 at the physical amount (about 2 percent of the Sun's), 18.9 at the mission's three times: grey 0.5 paint, so a white hull
would carry about twice that.

### Captures (`gauntlet/captures/jupiter/r1/`, 1920 x 1080 from the built game, HUD off, nothing retouched)
`gauntlet/captures/jupiter/r1/`: `planet/` (the planet alone with the mission's Sun: full disc at 60 and 85 degrees, limb, terminator and Spot close-ups, the moons, the test hull), `planet_half/` (half lit), `planet_crescent/` (a 7 percent crescent: not a good attempt, see below),
`planet_shadow/` (Io's shadow crossing the lit clouds, 235 to 315 s in; it is on the disc from 4.4 to 4.8 minutes), `distance/` (the same view at 110, 150, 200, 250 and 400 thousand km, with `distance_sheet.png`), `cockpit/` (the player's own 55 degree view, the AI flying, 16 moments), the rigs that ride a ship (`mercy_jupiter`,
`mercy_jupiter_close`, `warden_jupiter`, `warden_jupiter_close`, `fighter_jupiter`, `mercy`, `mercy_side`, `wing`: 16 moments each, 2 to 120 s), `sheets/` (a contact sheet of each folder), `evidence/` (the planetshine on and off, the long-time drift at two wind scales), `perf/`, `checks/`.
Every still is 1920 x 1080 from the built game (HUD off, no cockpit dust, nothing retouched; stills render offscreen on a fixed 60 Hz tick, so a rerun of a plan matches).

**My best honest attempts, collected in `r1/best/` (25 files):**
- Jupiter alone: `01_planet_full_disc` (42 degrees, phase 56), `02_planet_full_disc_wide_field` (85 degree field, Milky Way at the edge), `03_planet_half_lit_full_disc`, `04_limb_close` (14 degrees), `05_terminator_close_half_lit` and `06_terminator_close_mission_phase` (the soft terminator),
  `07_limb_north_close`, `08_great_red_spot`, `09_io_shadow_on_the_spot`, `10_io_close`, `11_europa_close`.
- The battle: `12`, `13`, `14` (the Small Mercy in front of the planet, three moments), `15_battle_beam_on_the_mercy`, `16`, `17` (hit), `18` (firing: the Warden before the planet), `19_battle_fighters_and_bolts_across_jupiter`, `20_battle_mercy_and_warden_wide`,
  the cockpit: `21` to `24`, and `25_warden_silhouette_against_the_planet`.
- My own ranking of those for the blind comparison against real photographs: Jupiter alone, 01, 03, 06 and 08; the battle, 12, 14 and 16.

### My read, honestly
- **The planet alone is the strongest piece.** The disc, the bands, the Spot, the soft terminator reddening into black, the thin blue limb and the black night side read as a photograph of Jupiter, because the map is one: it is Cassini's own picture of the planet wrapped on a sphere, lit by a model
  that follows the real planet's limb darkening and haze. What a critic will see first, I think: the colour is the Cassini map's (pale cream and blue-white zones, orange-brown belts), calmer than the saturated Juno processing that circulates; and the frame is clean (analytic edges, no lens softness), which `CameraLook`'s grain and the camera's
  exposure only partly hide.
- **Close-ups are the weak point.** At 14 degrees (the limb and terminator close-ups) the map is 3 to 4 times magnified and the added detail is faint: it looks soft, not wrong. Below about 8 degrees the Spot is a soft painting. A sharper map is the one real fix (see the gaps).
- **Ships against the planet carry the scale** (`12` to `19`): hulls in sunlight three quarters from behind with the planet's light in the fill, fighters as black shapes across the cloud tops, beams and fireballs over the disc, the planet 42 degrees behind them. The planetshine is a taste setting (three times the physical amount) and shows on the faces turned to the planet; from the
  usual side (the planet behind the ship) it shows as a warm edge, not a wash. I could not make the ships look as if they stood in the planet's glow without lifting the amount well past physical.
- **The crescent attempt does not work as a showcase**: with the Sun 31 degrees from the planet the Sun's lens glare (the sky piece's, bold on purpose) dominates the frame and the lit sliver is thin because the viewer, only 2.8 radii out, sees less than a hemisphere; it is in `planet_crescent/`, not in `best/`.
- **The stars vanish near the planet**: the camera look's meter dims stars when a lot of light is in the frame, so around a lit Jupiter the sky is nearly black with only the brightest stars, as a camera exposed for the planet would show; the Milky Way shows when you look away from it.
- The one thing I checked for and could not break: depth order. The planet is behind every ship, beam, explosion and smoke puff in all 144 battle stills.

### Known gaps
- **The map is Jupiter of December 2000** (the Spot larger and rounder than today's, no Oval BA, the poles faked with haze beyond 82 degrees south and 88 north). A newer, sharper map (JunoCam, 10 pixels a degree, CC BY with a request to ask the makers first, or Hubble OPAL, CC BY 4.0) is the obvious next source; both are noted in `docs/licences/planets.md`.
- **Detail beyond the map is noise, modulated by the map's own structure**; it is not a super-resolution of the real cloud tops, so past 8 degrees of field of view it stops helping.
- **No ring, no aurora, no lightning, no night-side glow**: the night side is black on purpose (the brief), the ring is invisible at this phase, and the others would be seen only up close or at night.
- **The winds are a static map sheared**: at 120 times the winds the belts are combed by 30 minutes; at 300 times they are streaked by 20 and smeared by 30 (`r1/evidence/time_sheet.png`, top row 120, bottom row 300, at 0, 10, 20 and 30 minutes). The real clouds are renewed, a map cannot be; a mission longer than 20 minutes wants `windScale` lower, or a reset (not built).
- **Moons**: two at most, a few pixels across at this range, their maps 1024 x 512 (Europa's is black and white, warmed by hand); Io's shadow is a hard dot with the penumbra the Sun's disc makes (0.1 degree), with no cloud-top parallax; there is no planetshine on a moon's night side, no moon-on-moon eclipse, and a moon's own shadow on the planet is the only shadow the planet takes (ships do not shadow it).
- **The planet does not shadow the ships** (nothing between the Sun and a ship in Jupiter's shadow dims the Sun) and the Sun's light is the same wherever the fight drifts: right for a fight a few kilometres wide.
- **Planetshine at three times is taste**, and the same cubemap carries the planet into the chrome's reflections three times too bright; if a ship's reflection of the planet looks hot, lower `planetshine` and raise the sky's `ambientScale`.
- **In the cabin** the eye opens to the dim cabin and the planet burns out toward white through the canopy (`r1/evidence/cabin/cabin_standing_fore.png`: a white disc with the bands showing only at the rim), as every sunlit thing outside does; the sky's stars and Milky Way are exposure-compensated and the planet, being as bright as a hull, is not. A compensated planet would keep its bands (one line in `FragPlanet`).
- **Follow shots in a mission plan need an explicit `fov`**: the sky's warm-up leaves the camera at 140 degrees for its first frames and the capture reads its base field of view from it (existing; noted in `tools/PIPELINE.md`). I did not change the capture harness.
- **The start-screen self-test lists the missions by a hard-coded name list and row numbers**; I updated it for five missions and the next mission added will break it again.
- Only Jupiter, Io and Europa have body files. Another planet is a map, a `.body.json` (radius, pole, winds, atmosphere) and a mission block; a body with no belts and zones would want its own look numbers.
