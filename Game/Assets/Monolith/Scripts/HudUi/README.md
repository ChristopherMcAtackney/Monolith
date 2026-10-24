# The HUD (`Monolith.HudUi`, drawing kit `Monolith.Hud`)

The player's HUD, the battle map, the radar and the cabin's displays share one look: single-colour line graphics with a restrained glow, one accent colour for
warnings, terse tracked type, and a hint of the aliens' glyphs as ornament only (design section 9). It replaces the placeholder flight and combat HUDs.

## The pieces

| Where | What |
|---|---|
| `Monolith.Hud` (the kit, `Scripts/Hud/`) | What draws: `HudLines` (the canvas: lines, boxes, triangles, rings, thick arc bands, cells, brackets, ellipses, type from a distance-field font), `HudFont` (a baked font atlas and its metrics), `HudStyle` and `HudStyles` (the looks, the one in use, `-mnHudStyle`, `hud.json`), `HudRadar` and `HudMap` (the radar and the battle map), `HudContacts` and `HudMissiles` (what is on them), `HudPass` (the custom pass that puts a canvas on the finished frame), `HudCollapse` (the cabin's fold of the HUD into its displays). The cabin's displays draw with this kit too. Shader: `Resources/Shaders/MN_Lines.shader`, passes 3 (shapes) and 4 (type). Fonts: `Resources/Fonts/`, licences in `docs/licences/fonts.md`. |
| `Monolith.HudUi` (this folder) | The HUD itself: `HudFrame` (everything it draws, as plain data), `HudReader` (reads `GameWorld` into a frame: the one place the HUD reads the game), `HudRenderer` (draws a frame: the layout; reads nothing else), `PilotHud` (the component: owns the notices, the radio feed, the overlay, the banner, the info lines and the footer; reads, draws, and puts it on the frame after post-processing), `CombatAudio` (the lock and missile-warning stand-in tones, silent when the audio system owns them). |
| `Game/Hud/` | `HudControls` (what every playable scene shares: F9 and the pause screen's HUD style switch, the battle map switch for scenes with no cabin, the invert switches), `HudSelfTest` (`-mnHudSelfTest`). |

## The data interface

`HudFrame` is the whole interface. Nothing in `HudRenderer` touches the game, so the HUD is drawn from made-up frames in the checks, at any screen size, in any style.
Screen positions in a frame are pixels from the top left of the screen it was read for; the target and its part carry both where they are and how big they look.

```csharp
var frame = new HudFrame();
HudReader.Read(world, camera, frame, Time.unscaledTime);   // fills the world's part of the frame
canvas.Clear(HudRenderer.ScaleFor(w, h), HudStyles.Current);
renderer.Draw(canvas, frame);                              // builds the picture
canvas.Commit();                                           // uploads it; HudPass draws it
```

What the scenes can set on `PilotHud`: `Info` (lines at the top left), `Banner` and `BannerSub` (big, in the middle), `Overlay` (the pause, briefing and debrief screen: a tab splits a key from what it
does, a blank line is a gap, `#Name` starts a section; the first line is the title), `Footer`, `MapSwitch` (the battle map, for scenes with no cabin), `UsePadGlyphs`, `Scale`.
The cabin's map switch (`GameWorld.Cabin.MapOpen`, seated) opens the same map. Radio text comes from `radio_line` events (`SPEAKER: text` is split at the first colon).

## The layout (1080p, scaled by the screen; FreeSpace 2's, kept spare)

```
 class, info lines                  radio (top centre)                    TARGET CARD
 notices                            MISSILE INBOUND                       class, name, hull, shield,
                                                                          range, part, lock and its tone
 DIRECTIVES
                    SPEED  (   reticle   )  WEAPON ENERGY
                    the two thick arcs                                    
 SYSTEMS (damage)          SHIELD ( radar ) HULL                          GUNS AND MISSILES
                              power choice
```

- **The two main bars** are thick curved bands, segments of one circle round the reticle. Left: your speed (the thick band, on a scale up to the afterburner's top speed, with a tick where
  cruising ends and the afterburner's range, drawn dimmer, begins) and the throttle setting (the thin band beside it, on the same speed scale, so the thick band chases its marker; ticks at the
  D-pad's thirds). Right: weapon energy (thick) and afterburner fuel (thin). Their numbers sit outside them, large; the middle is left clear for the target. Why: speed and throttle belong together
  because one is what you ask for and the other what you get; fuel and energy belong together because both are what you spend; and the pair is read in a glance either side of the reticle without looking down.
- **The radar** (bottom centre) is FreeSpace 2's: a tilted disc, you at the centre, ahead is up, stems rise and drop for contacts above and below your plane, missiles in flight are triangles (blinking in the warning colour when
  one is coming for you), with the sensors damaged the contacts shiver and drop out and static fills the disc. The shield and the hull are two short thick arcs either side of it; the power choice is under it.
- **The target** is boxed where it is; the card top right says what it is. The lock closes in as a diamond while it builds, and the card's lock row shows the progress in cells and a small tone mark that beats at the rate the lock
  tone beeps and holds steady once the lock is made.
- **Warnings** are the only thing in the accent colour: an incoming missile (a plate, a pointer round the reticle toward it, brackets when it is dead ahead, the frame of the screen at the last seconds), being locked on,
  a destroyed system, low hull, low energy.
- **The quick menu** (hold Y): power on the D-pad and the wingman orders on A, B, X and LB (or the keyboard's keys), the current choice marked. **The battle map** (View twice, B, or the cabin's double tap): the whole map over the
  view, with a key to its symbols; the game does not pause, and the radio, the directives, the notices and the missile warning stay up.

## The styles

`halo` (amber, Oxanium, thick segmented arcs close in), `meridian` (pale cyan, B612, wide continuous arcs with a fine scale, no ornament), `cipher` (green-teal, Jura, heavy plate arcs with rows of glyphs).
A style is a `HudStyle` in `Hud/HudStyle.cs`: colours (written as sRGB; the HUD's target is sRGB so they are converted), the typeface, the type scale in cap heights, tracking, the glow, the arc geometry and the glyph density.
`-mnHudStyle name`, F9 at any time, the pause screen (D-pad up and down, H), or `hud.json` next to the game (`{"style": "halo", "textScale": 1.0}`; `-mnHudText 1.2` sets the type size).

## Type and light

Four sizes: label (small tracked capitals), body (running text), value (readings) and display (the big numbers), cap heights 13, 16.5, 23 and 48 px at 1080p, never smaller than 0.72 of that on a small
screen. The type is a signed-distance-field atlas (`tools/hud/make_font_atlas.py`), drawn by pass 4 of `MN_Lines`: the edge is one pixel wide at any size, with a dark keyline round every letter and line so it reads over a
bright hull. The HUD's render target is sRGB and blends in linear light, so the shader treats the alpha of a lit colour as how bright it should look (raised to 2.2); plates and shadows keep their alpha as the share of the picture they hide.

## Checks

`Monolith.exe -batchmode -nographics -mnHudSelfTest [-mnOut dir]` (89 checks): the fonts and their licence, that every character in the mission and ship files has a glyph, the type scale, the styles, the gauges' and the radar's
geometry, a real world read into a frame (speed, throttle, fuel, hull, shield, energy, the guns, missiles and decoys, the target, the contacts, the sensors' static, the systems list, a missile in flight, the mission and the cabin's boards),
the layout of every style at eight screen shapes in six states (nothing off screen, no two panels or words overlapping, the middle of the screen clear), the pause screens' switches, and the CPU cost of a frame. `-mnHudCost` logs the
HUD's own CPU time per frame in a running game. Capture plans: `gauntlet/shots/hud/hud_r1_*.json` (made from `gauntlet/shots/specs/hud_r1_*.json` by `tools/juice/make_plan.py`); `tools/hud/capture_moments.ps1` takes every moment in every style and makes the side-by-side sheets.
