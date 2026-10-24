# Piece: alien-fighter (the player's fighter: the Vanguard, flown by the Sable wingmen)

Owner of the diaspora's fighter model and of the diaspora's look (the hull texture set and the kit that builds it; the other half of the look, the capital, is
`gauntlet/pieces/friendly-capital`). Bar: design section 9, "Your side's look": worn, used-future human hulls, practical and patched, in the spirit of the
Nostromo and The Expanse's Belter ships, with alien-ally technology grafted on (dark, ribbed, biomechanical shapes and glyph-like light, rough and spare), the
opposite of Earth's perfect matte black and chrome, so a battle reads at a glance; still FS2's design language (layered silhouettes of plated segments, running
lights for scale, recessed glowing exhausts, never wedges or greebled bricks). Code: `tools/blender_lib/mndiaspora.py` (the kit), `art_src/diaspora_hull`
(the texture set), `art_src/diaspora/build_vanguard_fighter.py` (the model), `tools/ship/make_diaspora_plan.py` and `make_diaspora_action.py` (capture plans).
Notes for the pipeline: `art_src/PIPELINE.md`, "The diaspora's ships".

## Round 1 (builder, 2026-10-24)

### What was built
`vanguard_fighter` (the ship id is the class id, so every mission that names the class gets it): 13.1 m long, 10.3 m across, 4.7 m high, built in Blender 5.2 by
script. Hull LOD0 10,692 triangles, LOD1 4,192, LOD2 1,188 (screen heights 0.10, 0.03, 0.006); the six parts add 5,928 intact and 1,740 wreck triangles; 15 materials
(12 of the capital kit's slots plus BIO, GLYPH and GLASS), 29 lights, 15 FX hooks, 6 hull damage sites, 30 scorch points, a 308-triangle collision proxy.

The design (nothing here is a wedge: every wing, fin and pod is a bevelled plated solid with a thickness):
- **The nose pod is the cabin's own.** Its arch, its tall canopy bubble over a sill that falls toward the nose, and the dark ribbed frames over the glass are
  `art_src/cabin/build_cabin.py`'s numbers turned inside out (a 0.13 m skin round the same cross-sections), placed so the seated eye of `view.json` (0, 0.4, 1.5)
  is where the cabin's eye is. The player never sees it from outside, but the canopy a wingman shows is the one they sit in.
- **A worn, patched airframe behind it**: three plated spine segments with recessed joints and steel collars, plates of five paint batches (bone, olive, rust,
  faded teal and bare repair steel, the mix set per region so it reads as patchwork), repair plates welded over damage (a degree off square, with a weld bead and
  a bolt at each corner), rows of pipe and cable laid on the outside, hatches, a tall patched fin to port only, a low keel, a lattice mast and a radiator vane
  (the asymmetry), a fuel tank slung under the starboard wing and a sensor pod under the port one, stencilled codes and hazard stripes.
- **Broad wings** with the needle guns on plated booms at the leading edge (so the muzzles are where the combat data has them, (+-3.19, -0.35, 3.64)), tip fins,
  canards of different sizes, rows of small amber lamps along the leading edges and nacelles, red and green navigation lights.
- **The graft**: two engine nacelles hung from the wing roots, a plain human cowl in front and behind it a dark, ribbed, grown shell round a worn steel bell
  that burns amber (Earth's flames are cold blue-white; ours are amber, so engines tell friend from foe at a glance), a living vein of dark tube from the sensor eye over the
  spine to the tail, tendrils across the struts, a teal glyph band on each shell, the sensor eye (a dark ribbed alien eye in a plated seat with a teal ring light).

Subsystems (the contract in `art_src/PIPELINE.md`): `SUB_engine_P/S` (the bells), `SUB_mount_P/S` (the needle guns, tag `gun:primary`, weight 0.5), `SUB_mount_H` (the chin
cannon, tag `gun:heavy`) and `SUB_sensors_eye`; each has an intact look, a wreck (torn, scorched, embers) and debris. Hull damage goes through `ShipDamage`
(`scorchScale` 0.12, the progressive scorch plan, breaches at six sites).

### The texture set (shared with the capital)
`art_src/diaspora_hull/build_diaspora_hull_textures.py` is the Earth set (`art_src/earth_hull`) re-graded and extended, so the kit works on it unchanged: the
same atlas layout and piece names, so every kit function (plating, windows, strips, decals, wear, paint) runs on it. Plate paint in five batches at 2048 px per 12 m
tile (rust oxide, bone-olive main coat, olive drab, faded teal, and a bare repair steel with its own relief and its own metal and gloss maps), with rust bleeding
from the fasteners, blisters, thin paint rubbed to the undercoat, chips to the primer; dull brushed steel where Earth has chrome; a 4096 trim atlas (warm window
rows, light bars, hatches, vents, cable, weld bead, hazard stripe, three cargo-container sides with stencilled codes, a hangar deck, the ship's name, and the teal glyph strips);
aged paint stencils in yellow-ochre and bone with the diaspora's mark (an open ring and a seed leaving through the gap); wear overlays (rust, brown grime, sand,
soot); amber engine and flare; a lamp palette (warm white, graft teal, red, green, amber). The grafted tech reuses the cabin's own dark ribbed bio texture and
glyph strip (copied as `dia_bio_*` and `dia_glyph_*`), so the hull, the cabin and the HUD's glyphs are one language.

### Wiring
The ship id is the class id: `GameMissionHost` already uses a model when `ShipArtCatalog.Exists(class)`, so the dogfight, escort and cabin missions got the
wingmen by data alone. Added: `partKinds` in `Resources/Combat/ships/vanguard_fighter.json` (engine 17.5, mount 20, sensors 10 percent of the hull per unit weight,
the stand-in parts' shares, so nothing about the fighter's toughness changed); `JuiceSandbox`'s two Vanguard slots show the model; the juice's hull break-up sections
are baked (`vanguard_fighter.sections.asset`, 17 section meshes); the engines follow the world ship's throttle and afterburner (`engineFollowsShip`).
The player's own ship is never drawn (first person only), so it keeps the stand-in parts, as before; flight and combat numbers are untouched (hardpoint positions too).

### Captures (`gauntlet/captures/diaspora-ships/r1/`, HUD off, critic size in `critic/`)
`vanguard_three_quarter`, `_front_quarter`, `_mid_three_quarter`, `_side_silhouette`, `_top_silhouette`, `_raking`, `_raking_close`, `_damaged_three_quarter`,
`_damaged_rake`; `formation_lead`, `formation_aft`, `formation_front` (a finger-four of Vanguards); `contrast_*` (with the Mercy and Earth's corvette);
`action_wingman_firing`, `action_wingman_boost` (a Sable wingman in the dogfight). Plans: `gauntlet/shots/diaspora_vanguard_{hero,side,rake,damage}.json`,
`diaspora_formation.json`, `diaspora_action_wingman.json`.

### Numbers and checks
Dogfight stress (`gauntlet/shots/render_dogfight_perf.json`, 1920x1080, RTX 4070 Ti, `-mnPerfSync`): average 183.0 fps, 1 percent low 58.4 fps, p99 12.4 ms,
GPU 2.42 ms, main-thread CPU 5.5 ms (Earth's fighters round: 185.2 fps and a 608 ms wave-spawn hitch that is gone in this run). Combat self-test 90 of 90, AI
37 of 37, HUD 90 of 90, mission self-test 62 of 62, cabin 75 of 75, audio 104 checks, the dogfight port check SAME, flight (`tools/flight.ps1`) the four gate ships
35 of 35 (the Vanguard's info row 31 of 35, as before).

### Honest read
What works: the fighter is unmistakably not Earth's. Warm patchwork plates, a dark grown nacelle and amber flames against black blades and blue-white flames;
close up it carries detail at four scales (plates, welds and bolts, pipe and cable, a hatch's vents); the canopy is the cabin's.
Weaknesses, plainly:
- In shadow and from behind (the angle a wingman is mostly seen from) the hull falls to dark brown and black; what carries it then is the amber flames, the
  lamp rows and the shape. More fill on the shadow side would help, and that is the sky's job, not the model's.
- The canopy glass is a flat dark teal with a faint glow: there is no environment to reflect, so it never looks like glass; it reads as a tinted dome.
- The silhouette is broad and blunt (the cabin's roomy arch sets the nose pod, which is 2.9 m wide and 3.1 m tall); against Earth's blades that is the point,
  but it is a gunship, not a dart.
- Every Sable wingman is the same model with the same codes (one stencil per wing), so no callsign on the hull.
- The paint overlay (stencils, chips) reads strongly as ochre blocks in the Workbench previews; in the game it is right, but the previews mislead.
