# Piece: the HUD (rough alien tech, refined; three styles for the user to pick from)

The user's note (2026-10-24), on the placeholder HUD the combat builder put up: "I'd much prefer a curved thicker bar for the two main bars (like arcs of a
circle), and just generally, the font is all squished up and hard to read. It needs to be more refined overall." Design section 9 (the HUD direction bullet,
"The player's tech is rough and minimal", "Your side's look") sets the style: single-colour line graphics, restrained glow, terse text, a hint of the
Independence Day invaders' glyphs as ornament only. The user judges by eye and picks one of the styles; their playtests overrule every critic, so this piece has
no critic rounds. After seeing it on screen the user said "it looks AWESOME!" and asked for a playtest; it was most likely Halo (amber), the default and the style captured most, so Halo stays the default and was not changed after that.
Code: `Scripts/Hud/` (the drawing kit the cabin shares), `Scripts/HudUi/` (the HUD; README there), `Scripts/Game/Hud/` (the shared switches and the checks),
`Resources/Shaders/MN_Lines.shader` passes 3 and 4, fonts in `Resources/Fonts/` (licences: `docs/licences/fonts.md`). Tools: `tools/hud/`.

## Round 1 (builder, 2026-10-24)

### What was built
- **A real HUD in its own assembly with a data interface** (`Monolith.HudUi`): `HudReader` reads `GameWorld` into a `HudFrame` (plain data), `HudRenderer` draws a frame and
  reads nothing else, `PilotHud` is the component. The placeholder `Monolith.CombatHud` assembly and `FlightHud` are gone; the dogfight, range, flight, mission and juice
  scenes build `PilotHud` instead. The kit (`Monolith.Hud`) was rebuilt: a canvas of distance-field shapes and type (`HudLines`), the font atlas (`HudFont`), the styles,
  the radar, the map and the pass.
- **The two main bars are thick curved bands**, segments of one circle round the reticle: left, speed (thick, on a scale to the afterburner's top speed, a mark where cruising ends) with the throttle
  setting (thin, same scale, so the band chases the marker); right, weapon energy (thick) with afterburner fuel (thin). Numbers set outside them, large, the middle left clear. The choice of pairs: what you ask for beside what you get, and the two things you spend.
  Halo cuts the thick band into 24 cells (21 px thick, 232 px out), Meridian draws it continuous with a fine scale (13 px, 292 px out), Cipher as a heavy plate (28 px, 256 px out).
- **Reticle and lead pip; the target box** where the target is, its name and range beside it, the part's own ring, an off-screen pointer; the lock closing in as a diamond, the lock word on a plate
  (dark letters on solid ink when made); **the target card** (class, name, hull and shield in cells, range, speed, the part, the lock row with its progress and a tone mark that beats at the lock tone's rate and holds when it is made).
- **The incoming-missile warning** (a plate with distance and seconds, a pointer round the reticle, brackets when dead ahead, the frame of the screen in the last 2.5 s, blinking), being locked on, the radar's missile blips.
- **An FS2-style radar** (tilted disc, stems up and down for above and below your plane, missiles as triangles, the range stepping by itself; sensors damaged: blips shiver and drop out, static fills it, "interference", then "no signal").
- **Shield and hull** as two short thick arcs either side of the radar, the **power choice** under it, **guns, missiles (selected one marked, reload shown), decoys** bottom right, the **systems you feel** bottom left, the **directives**
  (active first, a tick or a cross, never more than fit), the **radio** (speaker over the line, two at a time), the **notices**, the info lines.
- **The quick menu** (hold Y): power on the D-pad and the wingman orders on A, B, X, LB (the keyboard's keys when there is no pad), the current choice marked.
- **The battle map**: retro blips, a sweeping scan line, a key; it does not pause; the radio, directives, notices and missile warning stay up. Switch: View twice or B (scenes with no cabin), the cabin's own double tap (it used to hide the HUD and draw the map from `CabinOverlay`; it now just turns `MapOpen` on and the HUD draws the map; the plan table's map is the kit's `HudMap` in the same style).
- **Type**: three open-licence typefaces baked into signed-distance-field atlases (`tools/hud/make_font_atlas.py`: Oxanium, B612, Jura; all SIL OFL 1.1, recorded in `docs/licences/fonts.md` with the licence texts in the repo and the build), drawn by a new shader pass that gives a
  one-pixel edge at any size, a restrained glow and a dark keyline. Four sizes (label 13, body 16.5, value 23, display 48 px cap height at 1080p, floor 0.72 of that on a small screen), tracked labels, mixed case for sentences.
  The old font was a 4 x 8 stroke font with 6-unit advance.
- **Three styles**, switchable at run time (`-mnHudStyle`, F9, the pause screen, `hud.json`): **halo** (amber; Oxanium; cells; fewest ornaments), **meridian** (pale cyan; B612, drawn for cockpit screens; continuous arcs and a fine scale; no ornament), **cipher** (green-teal; Jura; plate arcs and rows of alien glyphs).
  One accent for warnings in each (red-orange, orange, red-orange).
- **The pause screens of the dogfight, flight and range scenes now have the invert switches** (`invert.flight`, `invert.cabinLook` in controls.json, LB and RB or I and O) and the HUD style; all four scenes share `HudControls`.
  The mission scene's own copy of the switches was replaced by the shared one. The pause, briefing and debrief screens are drawn on a plate in the HUD's own type; the flight HUD steps back behind them.
- **The cabin's displays use the same type and style**: the dash, systems, comms, plan table, rack, breaker and repair panels draw with the kit's canvas, in the chosen style's typeface and ink (the table and panels in its second tone, the warning in its accent),
  text wrapped by measured width.

### Checks (build from this tree: `gauntlet/captures/hud/r1/selftests/`)
- `-mnHudSelfTest`: **90 of 90**: fonts and licence, every character in the mission and ship files has a glyph in every font, the type scale and that nothing is condensed or tight, the styles differ, the gauges' and radar's geometry, a real world read into a frame
  (speed, throttle, fuel, hull, shield, energy, guns, missiles, decoys, target, contacts, sensors, systems, a missile in flight, the mission and cabin boards), the layout of 3 styles x 8 screen shapes (1280x720, 1366x768, 1600x900, 1920x1080, 2560x1440, 3840x2160, 2560x1080, 1280x1024) x 6 states (nothing off screen, no two panels or two words overlapping, the middle clear, the type never tiny), long names and long lists, the pause screens' switches.
  (It found real faults on the way: a header that met the radio at small widths, a directive list that ran off the bottom, a map title under the radio, 5:4 screens too narrow for a layout fitted by height. All fixed.)
- `-mnScene range -mnPadDrive` **30 of 30** (20 before): now also drives the battle map's double tap, Start, LB and RB on the pause screen (both invert switches flip and flip back, the screen shows the new setting), D-pad up and down (the HUD style), Start again, and F9 through virtual devices.
- Combat self-test 90/90. AI 37/37. Mission 61/61. Cabin 75/75. Flight (`tools/flight.ps1`) 35 of 35 on each of Ulysses, Myrmidon, Hercules, Artemis; vanguard (info only) 31 of 35, the same four afterburner metrics as before.
- **Perf** (1920x1080, GPU-synced `-mnPerfSync`, the combat range's 22 s battle, HUD on against HUD off, three pairs interleaved, medians): the HUD's own CPU time is **0.09 ms a frame** (`-mnHudCost`: read 0.004, build 0.08, upload 0.004) with the rebuild capped at 120 Hz; the frame is **+0.12 ms GPU** and the frame time **+0.09 ms at the median**.
  Before the 120 Hz cap the CPU was 0.2 ms, and the frame +0.3 ms. The machine was shared with another builder, so single runs scatter by tens of percent: the medians are the honest figure. Overdraw: the busiest frame covers 0.43 Mpx of shapes and 0.18 Mpx of type on a 2.07 Mpx screen.
- **Captures** (`gauntlet/captures/hud/r1/<style>/` and the side-by-side sheets `gauntlet/captures/hud/r1/sheets/`): cruising with a target locked (locking, locked), a missile incoming (warning, closing), boosting (ignition, hold), the battle map, the quick menu, sensors damaged (damaged, gone), and the pause screens of the range, dogfight, flight and mission scenes.

### Weaknesses, said plainly
- Nobody has flown it with a pad in hand: it has been read in stills, driven by a virtual pad, and checked by numbers. The type size, and which of the three reads best, are the user's call (`-mnHudText` and `hud.json` `textScale` change the size without a rebuild).
- A target off to one side can still put its name tag over the gauges' numbers or the arc itself (the tag goes where the target is); the layout checks leave world-anchored type out for that reason.
- The glyph ornament is invented, not copied from the film, and is deliberately faint: on Halo it is nearly absent. The radar is small (276 px wide) by design; blips are 3.8 px.
- The HUD rebuild is capped at 120 Hz to hold the cost down; on a 240 Hz display the bracket updates every other frame (the picture behind it still moves at the full rate).
- The font atlases are 0.5 to 1 MB each (raw 8-bit) in `Resources/Fonts`.
- Colours are authored in sRGB and converted, because the HUD's target is sRGB and blends in linear light (found when the first alphas looked far too bright: a linear 0.2 shows as half bright, so the shader raises the alpha of lit colours to the 2.2).
- The cabin's displays keep their old tuned sizes with the new type and are small in the frame (the dash, systems and comms screens are a few hundred pixels wide in the cabin shots, `gauntlet/captures/hud/cabin*`): they read, but the table's header and the dash's small lines are fine print at a distance. Only the plan table, the rack and breaker, the repair panels, the systems and comms screens were looked at closely; the dash's own face was seen from across the cabin only.
