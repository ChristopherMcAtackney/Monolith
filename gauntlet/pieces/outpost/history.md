# Piece: outpost (the Oculus, Earth's early-warning station in a comet nucleus; `earth_outpost`)

Owner of the comet outpost's model (class `earth_outpost`, the GTI Arcadia's numbers), the rock kit it is made with (`tools/blender_lib/mnrock.py`), the Oort ice textures and
materials, and the staged death it has in the Oort mission. It shares the Earth look, the capital kit and the pipeline with `gauntlet/pieces/human-capitals` (the corvette and the
destroyer). Code: `art_src/oort_art/build_earth_outpost.py` (the model), `build_oort_textures.py` (rock, frost, jets), `wire_combat_defs.py` (the combat def's part kinds and hit
boxes), `tools/blender_lib/mnrock.py`, `mnearth.py`, `tools/ship/make_oortart_plan.py` (capture plans), `gauntlet/missions/outpost_death_test.mission.json` (the death lab).

## Round 1 (builder, 2026-10-24)

### What was built
The mission placed an ice rock and a placeholder station hull seven kilometres ahead; this replaces both with one model (the mission asks for `earth_outpost` by class and
`GameMissionHost` takes the ShipArt prefab, so `oort.mission.json` did not change: confirmed, the auto-play runs it and the placeholder's `look` is simply unused).

**The nucleus** is a dark, dusty, two-lobed rock 1,122 m long, 757 m deep and 603 m tall (a smooth union of two superellipsoids, the big lobe to the west, a smaller one east
with a neck between), bumped by lumps, ridges, strata and 18 craters, then carved: a flat shelf across the top of the big lobe for the station, a cliff on the face the
approach looks at with the hangar cut into it, six pits for the guns, six vents for the jets. The crust is dust-dark (albedo 0.23: a real comet's 4 percent reflectance is the
floor, the game wants it to read), pale frost fills the big craters and the vents, and 150 loose boulders (a displaced icosphere sunk into the surface, 2 to 24 m) stand on it.
Six **outgassing jets** (200 to 560 m long, four nested additive shells each in a soft dust texture) stream from the vents; they are the first thing the eye finds from afar.

**The station** is Earth's cold black and chrome (the corvette's plates, windows, chrome rails and paint, at the station's scale), 450 m end to end, bored into the shelf (the spine's
underside is 14 m below it, anchor blocks and shoring driven into the rock along both flanks): a long armoured **spine** of six segments with chrome collars, armour ribs, belts
of plate, window rows in broken runs and three stepped decks (an ops tower on the middle one); **the Eye**, a 195 m gimbal ring on two pylons with 56 lamps round its inner lip and
a ball in it whose front is a phased-array iris round a recessed red pupil (concentric red, amber and white: it reads as an eye from kilometres); **the comms tower**, 215 m of
lattice with a 80 m chrome dish tilted toward the approach, four small dishes on a crossbar, whip antennas and a red beacon: the tall thin needle with a bright disc is the only
shape like it in the mission, which is the point (it is the first big decision); four **docking arms** (plated sleeves on trusses with a chrome knuckle and rams, a docking ring and
a clamp of three fingers driven into the ice, found by casting onto the real rock); radiator vanes and a lattice sail; a sensor boom to the west; and the **hangar**, an armoured
bezel 14 m proud of the cliff round a mouth 100 m by 52, a tunnel 80 m into the ice with ribs, light bars, window strips and a lit deck, a half-lowered shutter on rams, hazard
marks, amber beacons and a boom from each corner with a lamp at its tip, and a row of lit masts along the approach lane.

Subsystems (22, each with an intact look, a wreck and debris): `SUB_comms_array` (kind comms; the part sits at the dish, radius 52 m, so a shot at the dish counts), `SUB_sensors_eye`
and `SUB_sensors_boom` (sensors), `SUB_hangar_main` (hangar; its pivot is at the mouth and points out, so a launch comes out 94 m in front of it), `SUB_flak_F01..F06` (six
batteries on the spine and decks), `SUB_aaf_G01..G06` (six anti-fighter guns in pits in the ice, each in a plated collar with lamps), and six `SUB_vent_*` (the jets: decoration,
they break for the show and are not combat parts). 26 FX hooks, 10 hull damage sites (eight on the spine, two on the rock), 84 scorch points, a 1,328-triangle collision proxy.

### Wiring
- Mission: no change (the class picks the model). `Resources/Combat/ships/earth_outpost.json` got `partKinds` (comms 3 percent of the hull, sensors 2, hangar 4, flak 0.8 with
  `flak_gun`, aaf 0.8 with `turret_laser`: the stand-ins' shares, unchanged; the stand-in parts stay for the headless lab) and new `geometry` (one broad box over the nucleus at 0.95
  of its bounds, and the station's blocks). **Why one broad box:** a capital's beam sweeps a line along the face of the hit shape's bounding box at 0.9 of its half-extent
  (`Weapons/Beams.cs`, `PlanSweep`) and hurts only what the line meets, so the boxes must fill that face. With the three lobes' inscribed boxes (my first version) the Patience's
  beams missed most of the time: the outpost fell at 782 s instead of 589 s in the auto-play. `wire_combat_defs.py` writes both and prints the outward normal the AI will give each
  part (a turret's arc is measured off the nearest box face).
- The scenery rocks (`Game/Mission/IceRocks.cs`): **offered, not imposed.** If `ShipArt/Materials/MN_OortRock` exists, the mission's field rocks take that surface (a little paler)
  instead of the generated pale ice, so they match the nucleus; the shapes, placement and spin are untouched. Delete the 8 lines at the top of `IceRocks.Material` to go back.
  The pale ice rocks were the brightest things in the opening, brighter than the outpost; dark rocks leave the station's lights as the thing to look at.
- `ShipArtSetup`: the shadow-off rule for additive glows now also covers `MN_OortJet` (one condition).
- Juice sections: `earth_outpost.sections.asset` (22 section meshes, 8 slabs along the nucleus's depth).

### The staged death
Tried the juice's capital death as it is, on the lab mission (`outpost_death_test.mission.json`: the outpost at 2 percent hull, the Mercy's beams free; `make_oortart_plan.py
outpost_death`, 30 frames). It works on this model without a special case: the station's parts go one after another (the Eye's pupil, the tower, the guns, the hangar), each with its
wreck and debris; at 3.1 s the baked hull sections let go, slabs of rock with the station's pieces riding the nearest slab, burning along the cuts with their cores showing (the inner
skin); at 6.7 s the final blast (radius 525 m, sized to the nucleus by the juice) swallows it, a shockwave goes out to 3.6 km, and the slabs tumble out of a smoke ball. It does
not "fall" in the sense of a station tearing loose from a rock that stays: the comet goes too. I judged that the right ending for a 40,000-hit-point outpost that a cruiser is
beaming (a rock that survives its station would also have to be left standing for the rest of the mission), and did not add a death of its own; if the user wants the nucleus to
survive, the place is `Explosions.CapitalDeath`: a ship look that says "keep the hull, throw the parts".

### Budgets
Hull LOD0 252,401 triangles (rock 81,920, boulders about 36,000, the station about 130,000), LOD1 37,558, LOD2 3,546 (screen heights 0.07, 0.02, 0.006: it is a kilometre across, so
LOD0 reaches out to about 8 km); the 22 subsystems add 58,404 intact triangles (LOD0 and LOD1), 12,172 of wrecks and 1,408 of debris; 15 materials; FBX 28.4 MB; textures 51 MB of
PNG in `Art/EarthHull/oort_*` (the 2,048 px rock set and a 1,024 px frost set, the detail map, the jet atlas), the sections asset 19 MB. All under the 50 MB-a-file line.

### Captures (`gauntlet/captures/oort-art/r1/`, 1920x1080, HUD off, nothing retouched; `critic/` holds the 1280 JPEGs)
In the real Oort mission: `outpost_7km_quiet_drift` (the money shot: the opening's camera from the player's start, the outpost dead ahead in the ice field) and `_wide`, `outpost_4km`,
`outpost_2km`, `outpost_1km`, `outpost_close_eye`, `_hangar`, `_comms_tower`, `_station_three_quarter`, `outpost_from_above`, `_flank_west`, `_flank_east`, `_jets`, `_rock_belly`,
`outpost_lod1_25km` (the far LOD); the death in five frames (`outpost_death_04 .. 24`). Plans: `gauntlet/shots/oortart_outpost.json`, `oortart_outpost_death.json`.

### Perf (`-mnPerfSync`, 1920x1080, RTX 4070 Ti, `r1/perf/`)
The Oort mission's busiest moments, the AI flying: the outpost's fight seen from 560 m in front of its face at 2:06 (the strike, the waves out of the hangar, the guns): **222.1 fps average,
1 percent low 136.2, worst frame 19.7 ms** (p99 6.4 ms); and the player's view in the Archon's fight at 2:58 after that (both big ships, the Mercy, bombers and escort up), fov 140:
**222.5 fps, 1 percent low 106.3, worst 28.9 ms**, GPU 2.69 ms, CPU main 4.49 ms. (The first run's `gpuAvgMs` is a bad reading from the capture tool, the second is real.)

### Honest read
What works: at 7 km the outpost is the one thing in the frame that is not ice (a lobed black rock with a hot red point on top of it, an amber mouth, three white jets); from 2 km the Eye
is a ring of 56 white lamps round a recessed lens with a red, amber and white pupil: it reads as an eye, and the tower with its chrome dish is the only needle in the mission; the
station sits in the rock (shelf, anchors, clamps in the ice) and the arms break its outline. The rock keeps its scale (craters of 10 to 110 m, boulders of 2 to 24 m, a frost-filled
pit round every gun) and the crust no longer shows a grid. The jets are the cheapest big thing in it.
What does not: (1) at 7 km the station is about 25 pixels tall, so its lights are a handful of dots and the rock is a dark lump: it says "comet with a thing on it", not "outpost";
more light at that range (window runs are real, but a lit band cannot be wider than its trim piece) is the next lever, probably a beacon ring or lit foundation strip that the juice's
bloom can use. (2) The rock's close surface is a repeating 160 m tile with a 4 m detail map: from 100 m the crust is convincing, from 20 m it is a texture on a polygon (the 9 m
triangles show on the silhouette). (3) The spine's plates are the corvette's, so the station reads as "a corvette's hull, bigger", not as a thing designed to stand still; its
windows are in bands, not in the clusters a base would have. (4) The jets are static additive shells: they do not move. (5) The hangar's interior is dark apart from one lit back wall and
two ceiling strips; the bay is 80 m deep with nothing in it (no parked ships). (6) The death is the juice's: the nucleus goes with the station (see above).
A critic who compares the close frames with `capital_ship_close` will find the same modular-plate look the corvette was marked down for; the destroyer and the outpost use the corvette's
round-4 plating, not a new idea.

### How to rebuild
`tools\blender.ps1 art_src\oort_art\build_oort_textures.py`, then `build_earth_outpost.py`, `python art_src/oort_art/wire_combat_defs.py`, then `tools\unity.ps1
Monolith.EditorTools.Setup.Project`, `Monolith.EditorTools.JuiceSetup.BakeSections`, `Monolith.EditorTools.Build.Player`. Captures: `tools\ship\make_oortart_plan.py outpost`
(also `outpost_death`, `perf`), then `tools\capture.ps1 -Plan gauntlet\shots\oortart_outpost.json -Kind stills`.

### Checks (the worktree's own build, `gauntlet/captures/oort-art/checks/`)
Combat 98 of 98, AI 37 of 37, Mission 73 of 73, Cabin 75 of 75, Audio 104 checks, HUD 90 of 90 (all OK); flight 8 of 8 (the vanguard's afterburner metrics differ from the Ulysses as
before: informational); the dogfight port check SAME (all 90 samples bit for bit, the spawn and death timeline identical); `tools\mission.ps1`: cabin PASS, escort PASS, **oort PASS**
(victory "OUTPOST DOWN" at 631.7 s, every beat in order, 18 ships at most, threats 11 at most, longest quiet 9 s), **dogfight FAIL** ("longest stretch without a threat in the fight 94 s <= 30 s":
the sim ends with only the Warden alive for 94 s). The dogfight failure is not from this work: with the regenerated shared prefabs restored byte for byte the result is the same, the
dogfight uses neither new class, and the Mercy round recorded the same failure (75 s then); the AI pacing round owns it.
