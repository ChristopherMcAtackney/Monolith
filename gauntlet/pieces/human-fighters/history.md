# Piece: human-fighters (Earth's fighters: interceptor, line fighter, heavy fighter, bomber)

Owner of Earth's small-ship models, the small-ship kit they are built with, and how they are wired in as the real models for Earth's fighter classes
everywhere. Bar: the same as the capitals (`gauntlet/pieces/human-capitals`), at fighter scale: cold and precise, matte black and chrome, hard geometry,
harsh white running lights, drone-like; FS2's design language (layered silhouettes of plated parts, spines and fins with some asymmetry, glowing recessed
exhausts, each class readable by its silhouette alone). Code: `tools/blender_lib/mnfighter.py` (the kit), `art_src/earth_fighters/build_earth_<class>.py`
(one script per class), `tools/ship/make_fighter_plan.py` (capture plans), `Game/Assets/Monolith/Scripts/ShipArt` (runtime, shared with the corvette).
The pipeline notes are in `art_src/PIPELINE.md` ("Small ships: the fighter kit").

## Round 1 (builder, 2026-10-24)

### What was built
Four models, no new textures (they use the `earth_hull` set: 12 HDRP materials, the plate batches, chrome, trim, lights, engine, plume, damage decals).

| id | name | class slot | length x span x height | hull LOD0/1/2 tris | intact parts at LOD0 | silhouette |
|---|---|---|---|---|---|---|
| `earth_interceptor` | Seraph | interceptor (Ulysses class) | 10.6 x 8.6 x 3.8 m | 3,934 / 1,804 / 884 | 5,840 | a long needle fuselage with two swept main wings and a tall canted fin: a narrow Y from above, a dart from the side |
| `earth_fighter` | Dominion | line fighter | 14.6 x 12.5 x 5.1 m | 6,436 / 1,828 / 772 | 6,352 | a crossbar: broad straight wings and a split tail across a deep spine, a T from above |
| `earth_heavy` | Throne | heavy fighter (Hercules class) | 13.6 x 12.4 x 6.2 m | 8,000 / 2,660 / 1,044 | 6,352 | a blunt armoured body with side pods and a raised sensor arch, a heavy bar from above |
| `earth_bomber` | Censer | bomber (Artemis class) | 18.6 x 15.7 x 6.3 m | 12,892 / 3,512 / 1,092 | 5,952 | long and flat with twin trailing spines and a belly cradle for the torpedo: a trident from above |

LOD screen heights 0.10 / 0.03 / 0.006 of the screen. Collision proxy 156 to 472 triangles. 12 materials each. Each model has 7 subsystems (two engines,
two or three gun mounts, a sensor eye, a missile rack; the bomber has a torpedo cradle and a comms mast instead of the heavy gun), 13 to 17 FX hooks, 5 to
7 hull damage sites and 28 to 44 scorch points. The wrecks add about 1,900 triangles and the debris about 450 per ship, drawn only when a part is gone.

How they are made. The small-ship kit is the capital kit at a different scale: `solid()` makes a bevelled, plated, flat-faced solid from a quad (every
wing, fin, pod and slab of armour, so nothing is a bare wedge: it has a thickness, a chamfer and plates), `hull()` lofts stations with plating and rails,
then spines, whips (antennas), collars and hoops break the outline. Plate sizes vary on every face (rows 0.5 to 1.7 m, columns 0.35 to 1.2 m, a fifth of
the plates carry smaller plates), wear is sparse (edge chips on plate corners, a little grime), the paint is a custom set of matte black tones (deep,
graphite, mid) with a few accent plates. Engines are the capital bell at a fifth of the size, recessed with a hot throat and a short additive flare
(`plume_len` 0.62). White running lights on every wingtip and fin, a white ring light on the sensor eye, stencilled hull codes on the flanks.

### Destruction
Every fighter has engines, gun mounts, a sensor eye and a missile rack as named subsystems with an intact look, a wreck (torn open, scorched, a few
embers) and debris pieces. `ShipDamage` does the hull (scorch decals sized by the manifest's `scorchScale`, 0.11 to 0.17 of the capital size, then
breaches). Combat reads the parts through `partKinds` in the ship def (engine 17.5 percent of the hull, mount 20 or 10, sensors 10, launcher 8; the bomber's
engines 7.5 and its comms 10): an engine part to the speed cap, sensors to static, a launcher to `LaunchersOffline`, a mount tagged `gun:primary` to
`PrimaryOffline`, `gun:heavy` to `HeavyOffline`, comms to `CommsDown`. The data stand-in parts stay for the headless lab (no model there); the port check
(hand-written dogfight against the mission system) still matches to the bit.

### Wiring (shared files, kept small)
- Missions use a model when `ShipArtCatalog.Exists(class)` (already in `GameMissionHost`): the dogfight, escort and cabin missions got the models by the data alone.
- `DogfightBattle.SpawnFighter` (the hand-written dogfight), `CombatRange.SpawnDrone` (`range.json` drones got `def` and `model`; Reaper 2, a Myrmidon, stays a
  placeholder) and `JuiceSandbox` (fighter slots may name a model) have the same branch.
- `ShipCombatFactory.AddModelParts` replaces the stand-in's parts with the model's when the def has `partKinds`; `ShipArtVisual.Parts` reads the `gun:` tag.
- `JuiceSetup.BakeSections` bakes hull break-up sections for every ShipArt prefab, so the fighters break up like the corvette (17 to 18 section meshes each).
- Engines follow the ship: a `ShipHandle` (Core, put on the visual by `ShipViews.Register`) lets `EngineGlow` read the world ship's `Throttle` and
  `AfterburnerOn`: an engine sits at 0.4 to 1.0 with the throttle and flares (core brighter, plume 70 percent longer) on the afterburner. Corvette: not set.
- The cabin self-test's scripted pilot makes a third repair visit (t=150) because the drones die later with real parts to shoot at; the checks themselves are unchanged.

### Captures (`gauntlet/captures/human-fighters/r1/`, HUD off, critic size in `critic/`)
Each class alone: `<class>_three_quarter`, `_front_quarter`, `_side_silhouette`, `_top_silhouette`, `_raking`, `_raking_close`, `_damaged_three_quarter`,
`_damaged_rake`. Together for scale in front of the corvette's bow: `together_scale_formation`, `_close`, `_with_corvette`. In mission:
`action_drones_attacking` (an interceptor boosted and firing, the best frame), `action_drones_attacking_wide`, `action_drone_shield_hit`.
Plans: `gauntlet/shots/fighters_<class>_{hero,side,rake,damage}.json`, `fighters_together.json`, `fighters_r1_action.json` and `fighters_r1_action_b.json`.

### Numbers
- Busiest dogfight moment (`gauntlet/shots/render_dogfight_perf.json`, stress mode, 1920x1080, RTX 4070 Ti, `-mnPerfSync`): average 185.2 fps, 1 percent
  low 53.5 fps, p50 5.5 ms, p95 7.6 ms, p99 8.3 ms, GPU 2.32 ms, main-thread CPU 5.6 ms. With the placeholders: 187.6 fps, 1 percent low 58.8. The models
  cost about 1.3 percent of the frame rate. The one slow frame (608 ms at 15.4 s of the run) is in the placeholder build too (about 550 ms); it is the
  wave spawn, not the models, and it alone sets the 1 percent low.
- Checks on the final build: combat self-test 90 of 90, AI 37 of 37, mission self-test 62 of 62 and `tools/mission.ps1` (cabin victory at 114.7 s,
  dogfight at 642.4 s, escort at 283.9 s), cabin 75 of 75, audio 104 checks pass, dogfight port check SAME, flight (`tools/flight.ps1`) Ulysses,
  Myrmidon, Hercules and Artemis 35 of 35 (the Vanguard info row 31 of 35, as before).
- Playtest: `Playtest\Monolith_2026-10-24_drones\Play.bat <mission> [window] [difficulty]` (escort and dogfight both start and run; `Missions\` is editable).

### Honest read
What works: the four classes read apart from the top view and from the tail view the player sees most; black plating with chrome edges, white lights and
bright bells; the damaged sets (embers in the torn hull, a missing engine, debris) hold up at critic size; the action frames read as Earth's drones in a
fight, with the engine light on the hull. The kit made four ships quickly because every part is a function.

Weaknesses, plainly:
- Matte black is dark: at 100 m or more the fighters are lit shapes with blue engines and little else (`together_scale_close`); the bloom of their white
  lights does the work. More specular on the plates would help, but it would soften the look.
- Side-on, the four share a layout (engines aft, spine, wings out) and look more alike than from above. The next round should push one class off the
  pattern (a forward-swept interceptor, a split-hull heavy).
- The four bells are the same bell scaled; the plume is still a hard-edged cone close up (shorter and thinner than the capital's).
- The sensor ring light is a bright white ring that reads as a game marker in raking shots.
- The textures are the capital set at a small scale (the plate tile is 12 m), so small plates show a coarse wear. A dedicated small-ship tile would be sharper.
- Not mine, seen while capturing: the juice's shield-hit flash is an opaque pale disc (`action_drone_shield_hit`); 18 breach meshes warn "not readable"
  and vanish whole instead of breaking (the fighters' `DMG_site_NN__breach` meshes included). `MakeShadowVolume` in `DogfightScene` is unused.
