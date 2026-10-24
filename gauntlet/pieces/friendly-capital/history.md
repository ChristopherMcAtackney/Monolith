# Piece: friendly-capital (the Small Mercy, the diaspora's carrier-cruiser)

Owner of the Mercy's model (`diaspora_cruiser`) and its parts. The look, the texture set and the kit are shared with `gauntlet/pieces/alien-fighter`; the bar is the
same (design section 9, "Your side's look"), here at 350 m: a patched human hull with the alien allies' living technology grafted into it, against Earth's corvette.
Code: `art_src/diaspora/build_diaspora_cruiser.py` (the model), `tools/blender_lib/mndiaspora.py` and `mndiaspora_cap.py` (the kit and the capital parts),
`tools/ship/make_diaspora_plan.py` and `make_diaspora_action.py` (capture plans), `gauntlet/missions/mercy_death_test.mission.json` (a test mission in which she
starts at 2 percent hull, for the death captures).

## Round 1 (builder, 2026-10-24)

### What was built
`diaspora_cruiser` (the ship id is the class id): 369 m of hull from the horns' tips to the nacelle shells (nose +196.8, tail -172), 383 m to the bells' lips, 107 m
across (the hangars and the crane), 93.5 m high (the mast and the sail fin). Hull LOD0 80,768 triangles, LOD1 11,766, LOD2 2,366 (screen heights 0.16, 0.05, 0.012, the corvette's); the 21
subsystems add 38,294 intact triangles (drawn at LOD0 only), 9,856 of wrecks and 1,748 of debris; 15 materials, 65 lights, 24 FX hooks, 8 hull damage sites,
72 scorch points, a 416-triangle collision proxy.

Aft to bow: two nacelles on pylons (a plain human cowl, then a dark ribbed grown shell round a 14 m steel bell with an amber flame, teal glyph bands, tendrils
across the pylon); a cargo-and-radiator tail with lattice masts, a starboard cargo truss carrying strapped containers with their stencilled codes (one slot left empty),
three radiator panels to port and one to starboard; the reactor block, a plated flank with rows of warm windows and the reactor itself on its back, a glowing teal
core in a cage of dark ribs between two steel coolant stacks; a **grown joint**, a round dark ribbed neck with glyph light, between each pair of blocks; the main hull
with the four hangar bays (two a side, not level, 24 to 26 m wide and 13 m high, open boxes of plated walls standing 12 m out, lit warm inside along the ceiling, a worked deck,
hazard stripes on the lip and a blast door hanging open above), a flight deck and a command deck stepped up and back and shifted to starboard, a tall patched sail fin to
port with a lattice mast over it, a ventral keel, a flat boat pod under the port side, a gantry crane over the starboard bays, the name SMALL MERCY and a hull number
stencilled big on pads; the forward hull with the bridge off to port under an arch of seven grown ribs, the dark living crest of curved gills along its spine, the
forward beam lance; the prow with its docking collar and ram, the sensor crest of seven dark vanes, and two long ribbed horns of the graft rising ahead of it.
A vein of dark tubes runs along the port flank, patched plates (rust, steel, teal, olive) are welded over old damage on every flat.

Subsystems and what they are for in combat: `SUB_engine_P/S` (the bells), `SUB_flak_P01/P04/S01/S04` (batteries on the reactor and forward flanks), `SUB_aaf_D01..D03`
and `V01..V03` (six anti-fighter guns, dorsal and ventral), `SUB_turret_fore` and `SUB_turret_aft` (the two **beam lances**: a human gimbal carrying a long dark
ribbed barrel with a glyph band and a glowing teal lens), `SUB_bridge_main`, `SUB_reactor_main`, `SUB_sensors_crest`, and the four **hangar bays**
`SUB_hangar_P1/P2/S1/S2` (decorative: they burn and break for the show, they are not combat parts). Every one has an intact look, a wreck and debris
(wreck stumps, torn plates, embers; the lances snap to a ribbed stump). For the juice's staged capital death: parts that go one after another, 24 FX hooks (fire at the bells
and the hangars, vents at the reactor, sparks and vents at eight hull sites), baked break-up sections (`diaspora_cruiser.sections.asset`, 19 section meshes) and the
hull-damage plan, all through `IShipDamage` / `ShipArtJuiceDamage` as the corvette's.

### Wiring
Missions use a model by class (`GameMissionHost`), so the dogfight's Mercy (and the mission self-test's) is the model; `DogfightBattle.SpawnFriendly` has the same
branch as `SpawnCorvette` (the hand-written dogfight, which the port check holds tick for tick to the mission system's); `JuiceSandbox`'s friendly "Ravager" is the
Mercy. `Resources/Combat/ships/diaspora_cruiser.json`: `partKinds` (flak 0.833 with `flak_gun`, aaf 0.833 with `turret_laser`, turret 1.5 with `beam_slash`, engine 5,
bridge 2, reactor 2, sensors 5: the Deimos' shares, unchanged; the 17 stand-in parts stay for the headless lab), and the hull boxes follow the model (the
placeholder was 460 x 110 x 90 m). Hull points, shield (none), power and the turret and beam rules are untouched.

### Captures (`gauntlet/captures/diaspora-ships/r1/`, HUD off, critic size in `critic/`)
`mercy_three_quarter`, `_bow_quarter`, `_far_three_quarter`, `_side_silhouette`, `_top_silhouette`, `_raking`, `_raking_close`, `_hangars_close`, `_engines_close`,
`_bridge_close`, `_crest_close`, `_damaged_three_quarter`, `_damaged_close`; `contrast_mercy_and_corvette`, `contrast_pair`, `contrast_from_above` (with Earth's
Warden); in the mission: `action_mercy_beam` (the dorsal lance firing at the Warden), `action_mercy_under_fire`, and the death in three frames
(`death_mercy_beam_hit`, `death_mercy_breaking_up`, `death_mercy_final_blast`). Plans: `gauntlet/shots/diaspora_mercy_{hero,side,rake,damage,death}.json`,
`diaspora_contrast.json`, `diaspora_action_mercy*.json`.

### Numbers and checks
The same as the fighter's run (the dogfight stress with both models: 183.0 fps average, 1 percent low 58.4, GPU 2.42 ms; every self-test and check passes, see
`gauntlet/pieces/alien-fighter/history.md`). The staged death works on the model: the hull breaks into 7 baked sections, the final blast is 220 m
(`gauntlet/captures/diaspora-ships/_w` was cleared; the frames are `death_*` above).

One thing to look at, not mine to fix: the dogfight's auto-play outcome moved with the new part layout. Before: victory at 642 s, Sable 2 lost, never quiet longer than
2 s. Now: victory at 612 s, no losses, and a 75 s stretch at the end with only the Warden left. The simulation is chaotic (with the old Mercy numbers and only the new
wingmen it ends at 402 s), so this is not a regression to chase here; the pacing round (the AI builder) owns the balance and should re-tune with these models in.

### Honest read
What works: next to Earth's corvette (`contrast_*`) the two could not be confused: warm bone, olive, rust and teal patchwork and a lit, lived-in clutter of
containers, masts, hangars and windows against grey-white hard plates; the glowing teal reactor heart and the dark ribbed joints say "part living" without any
creature in them; and she dies well (`death_*`).
Weaknesses, plainly:
- At a distance she is still a stack of blocks: the layered silhouette is there (decks, sail fin, boat pod, horns, nacelles) but the main hull is a big plated
  box, and from the side she reads as a very stout barge. A forked or swept prow, or a main hull with more taper, would help.
- The hangars are empty boxes lit warm: no fighters on the decks, no deck crew, nothing to launch.
- The dark living crest is a row of thin curved blades (it reads as a comb), and the grown necks repeat the same ribs; the "alive" parts are tidy.
- The textures are the capital kit's plate tile (12 m) re-coloured: small plates have a coarse wear, and the same rust blotch repeats across the five batches.
- The turrets' `Forward` is the ship's forward (the capital kit's convention, as the corvette's), not the surface normal the old stand-ins had; the range's gunners
  (a lab) use it for "my own side of the hull", the dogfight's turret AI does not.
