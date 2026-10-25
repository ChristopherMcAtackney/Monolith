# Piece: comets (the Oort ice field's nuclei; `comet_a` .. `comet_e`)

Owner of the five comet models the Oort mission's scenery is made of, their surface (one layered crust, dust and ice material and its textures), the field code that scatters them, and the capture plans that
show them. Code: `tools/blender_lib/mncomet.py` (the machinery), `art_src/comets/build_comets.py` (the five bodies), `art_src/oort_art/build_comet_textures.py` (crust, dust, detail, the material recipe),
`Game/Assets/Monolith/Editor/CometArtSetup.cs` (material and prefabs), `Game/Assets/Monolith/Scripts/Game/Mission/CometField.cs` (the field) and the three lines in `IceRocks.Scenery` that call it,
`tools/ship/make_comets_plan.py`, `make_comets_sheet.py`, `make_comets_tour.py`, `check_comets_layout.py`. The outpost's own nucleus (`gauntlet/pieces/outpost`) is not one of these: it stays the outpost's.

## Round 1 (builder, 2026-10-25)

### The brief
The user played the Oort mission and said the comets in the ice field "look kinda shit... really lame, like placeholder graphics". They were: 160 field rocks and 6 single ones made in code by `IceRocks.cs`,
a noise-displaced, cratered icosphere (one of twelve seeds) on a pitted-foam texture. The instruction: do not model 166 rocks; model about five, scripted in Blender, and vary them in the game; keep the field's
layout, the clear lanes, the slow tumble and "drawn only" exactly; two large and distinctive, three smaller and plainer; real comet surfaces; detail at more than one scale; LODs.

### What was built

**The five models** (native sizes in metres; `fit` is how far a rock of the mission's radius reaches, in radii, the old rocks reached about 1.2 to 1.3):

| id | what it is | size | triangles by level of detail | notes |
|---|---|---|---|---|
| `comet_a` | the **bilobe** (67P): a big lobe with cliffs, terraces, one long scarp and wide craters, a head with two steep terraced pits, a smooth dusty neck between them | 1,251 x 677 x 747 m | 445,688 / 127,568 / 38,091 / 5,730 / 1,280 | large; 2,600 boulders; 59% dust |
| `comet_b` | the **long terraced** one (Tempel 1, Borrelly): a great scarp along its length, stepped plateaus, a smooth plain on one flank, six craters of 46 to 120 m, a pair of pits | 1,350 x 588 x 605 m | 445,673 / 126,948 / 37,851 / 5,685 / 1,280 | large; 2,600 boulders; 54% dust |
| `comet_c` | a **lumpy potato** with dish craters (Halley) and a pond of dust | 321 x 239 x 204 m | 112,518 / 30,885 / 5,794 / 1,280 | plain; 700 boulders |
| `comet_d` | **round and pitted** (Wild 2): six flat-floored, steep-walled pits and two wide shallow craters | 249 x 228 x 206 m | 109,099 / 27,559 / 5,406 / 1,280 | plain; 650 boulders |
| `comet_e` | a **split fragment**: planar faces, one fresh cliff with layered bedding, rubble | 290 x 254 x 203 m | 111,439 / 31,658 / 5,906 / 1,280 | plain; 650 boulders; the least dust (16%) |

LOD screen heights: the large two 0.3 / 0.14 / 0.05 / 0.016 (cull 0.002), the plain three 0.2 / 0.065 / 0.02 (cull 0.0025). FBX 35.5 + 35.0 + 8.5 + 8.0 + 8.2 MB (welded, one material slot, two UV sets,
a vertex colour); textures 31.7 MB of PNG in `Art/EarthHull/comet_*`. Files all under the 50 MB line; the repo grew by about 117 MB.

**The surface.** One HDRP **LayeredLit** material, `MN_CometSurface`: layer 0 the crust (very dark dusty brown-grey, albedo 0.20 sRGB = 3.4%, clods of a metre or three, a wandering fracture network that
comes and goes, hairline cracks, now and then a pale fleck), layer 1 the dust (a little paler, smooth, a few pebbles), layer 2 frost and exposed ice (the outpost's ice maps, dimmed: a bright patch, not a
lamp). The mesh's vertex colour is the blend (R dust, G ice), so a dust drift or a patch of frost has no edge a face can show; a patch is moved off the middle of its hollow and broken up by noise. A tiling
detail map (4 m) on UV1 adds grain. No pore or bubble texture (the old rocks had one: it read as pumice).

**The shapes** (`mncomet`, numpy, deterministic, no hand placement): a smooth union of superellipsoid lobes (cut by planes for the fragment), found along each icosphere direction by marching; then, along the shape's own
normal: lumps, rough and smooth provinces, **terraces and scarps that rise over a set number of metres** (a cliff of chosen steepness wherever the noise's gradient lies, so the mesh is never asked for a wall it cannot
hold), craters (flat dusty floor, steep wall, raised rim, an apron; stretched along a direction of their own so none is a circle), pits (steep, terraced walls), fractures, `rubble` (sharp-crested noise at 110, 55, 27 and
13 m), dust that fills every hollow (floors lifted toward the surface round them); then boulders (power-law sizes, thick at the foot of cliffs and in hollows, a share in clusters, sunk into the surface), ice sites. Every
level of detail is the same fields at a coarser icosphere (7, 6, 5, 4, 3 for the large two; 6, 5, 4, 3 for the others), with the noise a level cannot carry left out, so the silhouettes agree.

**The field** (`CometField`, called from `IceRocks.Scenery`). A mission rock says where it is, its radius, stretch and spin; it keeps saying only that. What it LOOKS like comes from a random stream of its own, so
the mission's stream (positions, radii, stretches, rotations, spins) is read in the same order as before and never moves:
- the model: a rock of radius 420 m or more is always one of the two large ones; 100 to 420 m, 40% large; under 100 m, 14% large; a plain one otherwise. The 166 rocks of the Oort mission got `comet_a` 16, `comet_b` 20,
  `comet_c` 43, `comet_d` 46, `comet_e` 41; the six single rocks (1,400 / 950 / 760 / 520 / 140 / 90 m) are b, b, a, a, d, d;
- a turn of the model inside the rock's own stretch (a uniform random rotation of a child under the holder that carries the mission's stretch, so the same model is stretched along other features each time), a mirror (50%), a few
  percent of extra stretch (0.9 to 1.12 a axis) and size (0.94 to 1.08), and one of fourteen tints of the surface (brightness 0.8 to 1.18, warm to cool -3.5 to +4.5%); 84 of the 166 are mirrored;
- models are centred on their volume and scaled by their bound, so a rock of radius R reaches about `fit` x R as the old rocks did; each rock tumbles about its centre as before (`Spin`, untouched);
- GPU instancing is on for the material and its fourteen tint copies; draws go through the models' LOD groups (the farthest rocks are 1,280 triangles). No collider anywhere.
If the comet assets are missing the field draws the old generated rocks (`CometField.Available`), which is also how the self-test builds the layout the old way to compare.

### The layout did not move (proof)
`-mnLogScenery` logs every rock's index, place, radius and stretch. The baseline build's log and this build's log (`captures/comets/r1/before/player.log` and `after/player.log`) agree for all **166 rocks**
(`python -I tools/ship/check_comets_layout.py before/player.log after/player.log`: IDENTICAL). The mission self-test builds the Oort field with the comets and without and compares every rock's place and turn: equal,
and no collider anywhere in the field.

### Captures (`gauntlet/captures/comets/r1/`, 1920x1080, HUD off, the cabin out, the real Oort mission with the AI flying, nothing retouched)
`before/` (the baseline build, main at 0e9ad06 plus the logging flag) and `after/` (the frozen playtest build) hold the same thirteen framings from the same cameras (`plan.json` in each; `python tools/ship/make_comets_plan.py stills`):
`start_cockpit` (the seat camera as the mission opens, fov 140), `start_view` (a 55 degree view from the same point toward the outpost), `start_wide_left`, **`big_2km_a`** and **`big_2km_b`** (a big comet about 2 km off its
far reach, the Sun at 80 and 72 degrees), **`small_200m_a`** and `small_200m_b` (a field rock of 28 m and one of 22 m at 200 m), **`pass_50m_a`** and `pass_50m_b` (field rocks of 43 and 46 m at 50 m),
`mid_600m`, `mid_150m` (the field's biggest rocks at 600 and 150 m), `field_side_1km`, `field_side_wide`. Side by side: `comets_before_after.png` (the four framings of the brief and the start camera) and
`comets_before_after_more.png` (the others). The Sun is fixed in this mission (behind the player's start), so from the start the field is seen full on and flat; the rest are placed to catch it at 55 to 100 degrees,
where relief shows.

**Best honest attempts** (the lead picks the pairs): `after/big_2km_b.png` (the long terraced one at the terminator, 2 km: cliffs, pits, boulders, the frost patch), `after/big_2km_a.png` (the bilobe among the field),
`after/pass_50m_a.png` and `after/pass_50m_b.png` (a close pass: clods, fractures, drifts of dust, a bright frost patch in a pit), `after/small_200m_a.png`, `after/mid_150m.png` (a wide crater with frost in its floor),
`after/start_view.png`. The weakest: `start_cockpit.png` (the 140 degree seat view makes every rock a few pixels) and `mid_600m` (the Sun nearly behind the rock: mostly shadow).

### Perf (RTX 4070 Ti, 1920x1080, vsync off, the AI flying; `captures/comets/r1/perf/`)
| run | this build: average, 1% low, worst, p99 | baseline (main): average, 1% low, worst, p99 |
|---|---|---|
| the start view (the seat camera as the mission opens, 0:03 to 0:23) | **512 fps, 223 fps, 59 ms, 3.4 ms** | 499 fps, 74 fps, 83 ms, 9.7 ms |
| the drift through the field (0:46 to 1:06) | **551 fps, 272 fps, 10 ms, 3.3 ms** | 490 fps, 78 fps, 35 ms, 10.1 ms |
| the strike: the outpost's guns and the waves out of its hangar (2:19 to 2:44) | **506 fps, 280 fps, 12 ms, 3.2 ms** | 352 fps, 71 fps, 21 ms, 11.7 ms |
| the Archon's fight, the busiest moment (5:32 to 6:02) | **295 fps, 70 fps, 133 ms, 9.3 ms** | 288 fps, 45 fps, 155 ms, 18.8 ms |
| still camera 300 m off a big comet at its highest level of detail | **352 fps, 84 fps, 17 ms, 9.1 ms** | 321 fps, 52 fps, 25 ms, 15.7 ms |
| still camera 60 m off a 43 m rock | **401 fps, 83 fps, 19 ms, 8.9 ms** | 390 fps, 63 fps, 32 ms, 12.2 ms |

(GPU 1.8 to 2.5 ms and main thread 1.8 to 3.3 ms a frame in both builds, in step.) Both builds were run back to back with `tools\capture.ps1 -Kind perf` (`gauntlet/shots/comets_r1_perf.json`, six runs, real time, the AI flying)
on a machine that other builders' headless tests were using at the same time: that shows in the 1% lows and worst frames of both (the baseline's lows of 45 to 78 fps and worst frames of up to 155 ms are the other
processes; a hitch of 130 to 155 ms is in the Archon's fight in both builds, and a first baseline pass made alone earlier read 681 fps / 1% low 334 for the start view, 600 / 284 in the drift, 510 / 248
in the strike and 190 / 22 in the Archon's fight). What it says: **the comets cost nothing; the start view, the drift, the strike and the Archon's fight all hold 60 fps with the 1% lows above 69 fps in this build**
(223, 272, 280 and 69.5), and a close view of the biggest comet at its 446k-triangle level runs 352 fps (1% low 84), faster than the old rocks' 321 (1% low 52) (an earlier quiet pair read 559 against 376 fps).
The cost the new rocks do have is memory and load: the build is 832 MB (747 MB before), the five models are in the loading screen's preload list; a played start to flyable took 7.2 s against the baseline's
8.8 s in two runs one after the other.

### Checks (the frozen playtest build, `captures/comets/r1/checks/`)
All on the frozen playtest copy, `Playtest/Monolith_2026-10-25_comets/Monolith.exe` (every number from `captures/comets/r1/checks/`):
- **Mission self-test 94 of 94** (81 on main and 13 new: the five models are there, two large and three plain, each with 4 or 5 LODs falling in triangles and no collider, the same seed makes the same rock and 80 rocks make 80
  different ones in all five models and about half mirrored, a big rock is always a large model, the Oort field built with the comets and without has every rock's place and turn equal, no collider in the field, all five in the field).
- `tools\mission.ps1 -Exe ...` at Medium, the AI flying an invulnerable player ship: **cabin PASS** (victory "AREA CLEAR" 142.5 s), **dogfight PASS** ("AREA CLEAR" 410.6 s, longest quiet 7 s), **escort PASS** ("TAMSIN AWAY" 296.3 s),
  **oort PASS** (victory "OUTPOST DOWN" at 671.4 s, longest quiet 9 s, 17 ships at most, threats 10 at most, most on the player 4: the same 671.4 s as the round-2 table on main, so the field's looks changed nothing
  the mission can see).
- AI self-test **39 of 39**, combat **98 of 98**, HUD **90 of 90**, cabin **75 of 75**, audio **PASS (150 checks)**.
- Layout: `check_comets_layout.py` IDENTICAL for all 166 rocks.

### Honest read
What works: the rocks are different from each other (five shapes, two of them big and recognisably comets, turned, mirrored and stretched), dark, rough, granular and cracked rather than pitted, with real relief at the
terminator (cliffs, pits with raised rims, terraces, boulders), smooth paler dust in the hollows and a few frost patches in the shade; from the start the field reads as drifting ice and rock of many shapes and not
as a scatter of identical pale pebbles; close up (50 m) the surface holds. They cost nothing: the close views run faster than the old rocks did (fewer, welded vertices).
What does not, or not yet:
1. **No jets or dust haze on the field comets.** The outpost's jets are static additive shells that glow the same from every side; on a rock that tumbles in a hard Sun they read as glowing tubes, not dust. Not done on
   purpose (the brief said only if it reads as real); a lit, volumetric haze that thins with the phase angle is the right way and is not cheap.
2. **Two big shapes only.** The four big single rocks are two of each; mirrored, turned and stretched they differ, but someone who flies to two of them will see the family.
3. **Vertex-limited detail on the biggest rocks.** The large two at their highest level have 3.8 m edges (11 m on a 1,400 m rock): from 500 m the limb is slightly faceted, and the dust-to-crust blend follows the mesh.
   Boulders are geometry only at the highest level; at 2 km the boulder fields are texture-less bumps of a few pixels.
4. **Crust tile.** The crust texture repeats every 150 m of the model's own size (box-projected, statistically uniform): not visible at 50 m, but a very keen eye will find the fracture network repeating on the biggest rocks.
5. **Dark and a little brown.** Albedo 3.4% crust and 7% dust under a fixed exposure: the lit faces average about (66, 59, 55) of 255 in the 2 km shots. It is what the references look like with their exposure left alone, and
   nothing else in the frame has to compete with the outpost's lights; the user may want it paler.
6. **The pale blurred rocks at the edge of the frame** (near the camera, lit full on) are the game's depth-of-field and sun-facing phase, as in the baseline's frames; they are not changed here.
7. `comet_e` still has some of the loaf about it (flat faces from the cut planes), and the ice patches on pit floors can look speckled at 150 m.
8. Not judged by a critic, not seen by the user.

### How to rebuild
`tools\blender.ps1 art_src\oort_art\build_comet_textures.py` (45 s), `tools\blender.ps1 art_src\comets\build_comets.py` (about 1 minute; `-- --only a,b --preview a --level 6 --no-export` for quick Cycles looks in
`art_src/comets/_preview`), then `tools\unity.ps1 Monolith.EditorTools.Setup.Project` (it also re-makes the ship prefabs with unrelated differences, as on main: put those back with `git checkout --
Game/Assets/Monolith/Resources/ShipArt`; `Monolith.EditorTools.CometArtSetup.Run` alone does only the comets, in 15 s) and `tools\unity.ps1 Monolith.EditorTools.Build.Player`. `JuiceSetup.BakeSections` is not needed (no ship
changed). Captures: `python tools/ship/make_comets_plan.py stills|perf`, `tools\capture.ps1 -Plan gauntlet\shots\comets_r1.json -Out ... -Kind stills -PlayerArgs "-mnLogScenery"`; portraits of each model from a
log: `python -I tools/ship/make_comets_tour.py <player.log>`; sheets `python -I tools/ship/make_comets_sheet.py before after out.png`.

### Gotchas found
- A Blender script's `open(path, "w")` writes CRLF on Windows: use `newline="\n"` for anything committed (the repo is LF).
- `Build.Player` run straight after adding a new script file can report "name does not exist" from a stale first compile pass and still build; run `-CompileOnly` first, or build twice.
- HDRP LayeredLit's vertex-colour layer mask is `layerMask map x vertex colour` (keyword `_LAYER_MASK_VERTEX_COLOR_MUL`, `_VertexColorMode` 1); with no mask map set the colour alone is the weight (layer 1 = R, 2 = G, 3 = B).
  LayeredLit has no object-space triplanar (`objectSpaceMapping` is false in its data code), so the models carry box-projected UVs.
- Unity's SetColor on a LayeredLit `_BaseColor0..2` is the multiplier used for the tints, raw (not converted).
- Displacing along the shape's normal by more than the local radius of curvature makes spikes; the terraces therefore take a height and a width in metres instead of an amplitude.

## Round 1 verdict (lead, 2026-10-25)
Blind round `comets_r1`: three pairs, three separate critics, each pair one of our comets (cropped to the body, greyscale) against a real
spacecraft photo (greyscale as they are): big_2km_b against Rosetta's 67P NavCam mosaic of 19 September 2014, pass_50m_b against 67P from
19 km on 5 October 2014, mid_150m against Stardust's Wild 2. References in `reference/comets/` (critics only). Keys: `gauntlet/keys/comets_r1.json`.

**Lost 3 of 3, every critic "certain".** The giveaways agree:
- Lighting: soft, low-contrast, flat; the real nuclei have one hard sun, bright sunlit faces and black cast shadows with a clean terminator.
- Surface: "one uniform fine noise texture", "a repetitive field of pitted, blobby cavities"; the real ones have multi-scale fractured
  geometry: sharp cliffs, talus and boulder fields, smooth dust-mantled plains.
- Context: stars and the Milky Way behind a sunlit body (no real exposure records both), and the scattered small rocks. That one is the sky's
  game-wide choice (The Expanse shows stars too), not the comets'.
- Pair 3: a bright speck in the pit's bowl read as a "bullseye".

Read as pointers, not prescriptions: round 2's biggest lever is the light (contrast and a hard terminator on the rock), then large-scale
fracture geometry over fine noise. Note: in the real Oort Cloud the Sun is only a bright star; the mission's Sun is an artistic choice, and
the references were all taken within a few AU of it.
