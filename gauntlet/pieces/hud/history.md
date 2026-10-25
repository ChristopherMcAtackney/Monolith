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

## Round 2 (builder, 2026-10-25): the HUD scaled back and made see-through

The user's note, from a playtest: "I think the hud is maybe a little too big, or maybe it needs a little opacity? It's too dominating. Can you try scaling it back a bit and adding opacity (keep these as settings internally I guess so we can tweak later)".
No critic round: the user judges by eye. Before and after captures are for them.

### What was built
- **`scale` and `opacity` in `hud.json`**, beside `style` and `textScale` (`HudStyles.Scale`, `HudStyles.Opacity`; `-mnHudScale` and `-mnHudOpacity` for one run; limits 0.5 to 1.5 and 0.25 to 1; a missing, unreadable or wrong-typed file means the defaults). Saving the style (F9) keeps both numbers and any other key the file has, and writes them into a file that had none, so they are there to find.
- **Defaults: scale 0.86, opacity 0.80** (a seventh smaller, a fifth see-through). Why these: tried 0.90/0.85, 0.86/0.80 and 0.80/0.72 on the same dogfight moments in Halo. 0.90/0.85 still reads as the same HUD; 0.80/0.72 makes the small labels (HULL, LOCK, THROTTLE) hard to read over a bright nebula; 0.86/0.80 is clearly less there and still reads every number.
  At 1080p the smallest type is an 11.2 px cap height (label; 13 at the design size), 14.2 (body; 16.5), 19.8 (value; 23) and 41 (display; 48); the dimmest text keeps 50% of full brightness; the HUD covers **79% of the screen area it did** (shapes 0.85 to 0.69 Mpx, type 0.38 to 0.28 Mpx, Halo).
- **How it is applied**: `PilotHud` draws at `ScaleFor(screen) x scale` and sets `HudLines.Opacity`, which scales the alpha of every vertex as it is written (`HudLines.V`, one compare a vertex); the glow and the dark keyline come from the same alpha in the shader, so they thin with the ink. The same for all three styles.
- **Warnings stay at full strength** (my call): any vertex in the style's accent colour (an incoming missile, its plate, pointer and brackets; being locked on; hull critical; a lost system; energy low; the damage frame) is exempt from the opacity. At 0.8 everything else steps back and the warning, untouched, becomes the loudest thing on the screen by more than before: the right way round for a warning, and the only thing in the accent colour by design. (The dark plate behind a warning is dimmed like every plate; its colour and keyline are not.) They are scaled with the layout.
- **The pause, briefing and debrief screens and the battle map keep the design size and full strength**: they are screens the player opens to read, not the HUD that sits on the view. (Measured: the pause screen is pixel-identical before and after; the battle map differs by 0.3 to 0.65% of its pixels, the sweep line's real-time clock, which differs by 0.63% between two runs of the old build.)
- **The type's small-screen floor follows the scale** (`HudLines.TypeFloor`, 0.72 of the design size times the scale). The layout check found this: with the old fixed floor, at 1280x720 and 1280x1024 the arms list ran 'EMBER' into '12' because the layout had shrunk and the words had not.
- `tools/hud/capture_moments.ps1 -PlanGlob`, `tools/hud/make_compare.py` (two runs side by side), plan `gauntlet/shots/hud/hud_r2_dogfight.json` (the dogfight flown by the AI, six moments: opening radio and a locked Warden, a burn, notices, the Warden's hull behind the gauges, a lock, an enemy fighter).

### Before and after (the same dogfight moments, HUD on, three styles; before from the build at main 0e9ad06, after from this build)
`gauntlet/captures/start_hud/before/<style>/`, `.../after/<style>/`, and side by side: `gauntlet/captures/start_hud/compare/<moment>.png` (a row a style, before left, after right) and full size `compare/pairs/<style>_<moment>.png`.
Moments: `dog_t15_open`, `dog_t27_burn`, `dog_t36_notices`, `dog_t39_hull`, `dog_t48_lock`, `dog_t57_fighter`, `missile_closing` and `missile_warning` (the combat range: an incoming missile, to show the warning staying loud), `pause_screen` and `battle_map`.

### Checks
- `-mnHudSelfTest`: **122 of 122** (90 before): the new ones are the defaults' range; where the numbers come from (none, hud.json, the flag over the file, the limits, an unreadable file, wrong types); what a save keeps; opacity 0.5 halving the alpha of 13,162 ink, plate and shadow vertices and leaving 1,410 warning vertices untouched (0 wrong, three styles, a frame with an incoming missile, a lock on you and a lost system); the type readable at 1080p; the dimmest text at least 48%; the HUD covering at most 85% of the area; the layout at eight screen shapes in six states at the default scale (nothing off screen, no overlaps, the middle clear); the cost of the opacity.
- Mission self-test 81 of 81, the four missions auto-played on Medium: events.csv and pacing.csv byte-identical to the build at main. AI 39/39, combat 98/98, cabin 75/75, loading 55/55, audio 150 checks, pad drive 42/42.
- **Cost** (the combat range's 20 s battle, 1920x1080, GPU-synced, `-mnHudCost`, three interleaved runs of each, medians): the HUD's CPU time **0.094 ms a frame** on the old build, **0.096** on the new at scale 1 and opacity 1, **0.099** at the defaults (the old build's own runs scatter 0.093 to 0.100, the new's 0.096 to 0.106: the opacity's compare is about 3 microseconds a frame); frame time p50 4.113 / 4.098 / 4.113 ms, GPU 2.33 / 2.35 / 2.35 ms; vertices 3,005 to 2,984. Building a busy frame: 0.255 ms at opacity 1, 0.258 ms at 0.8. So: not measurably more.

### Not done, and why
- No in-game control for the two numbers (the user asked for settings kept internally): edit `hud.json` or pass the flags. The pause screen has no row for them.
- The cabin's seated HUD (the HUD folding into the cabin's displays) was not recaptured: it draws the same HUD, so the scale and opacity apply, but the displays themselves are not part of it.
- The target's name tag still sits where the target is and can still meet the gauge numbers (an old gap, unchanged).
