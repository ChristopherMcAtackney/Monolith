# Juice explosion library (flipbooks)

Real fire-and-smoke fluid simulations for the juice system (`Game/Assets/Monolith/Scripts/Juice`), made the way photoreal games make explosions: simulate, render, play the
frames as a sprite sequence, light them in the engine.

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\blender.ps1 art_src\juice_fx\build_flip.py -TimeoutMin 120 -- --variant fire_a --res 192 --preview
```

`build_flip.py` runs headless in Blender 5.2: it builds a lumpy cluster source (several spheres, displaced, with a turbulence field through the domain), bakes a Mantaflow gas
simulation (fire plus smoke, no gravity: it is space), renders the volume with Cycles on the GPU (OptiX) as line integrals of the simulation's `flame` and `density`
fields, and packs 36 frames (denser early, a blast opens fast) into one atlas.

| Output (`Game/Assets/Monolith/Resources/Juice/Flip/`) | |
|---|---|
| `<variant>.png` | 6 x 6 frames of 512 px in one 3072 px RGBA8 image, **not colour**: R the burning gas (sqrt-encoded), G the smoke, B and A the slope of the smoke's surface (0.5 = flat), so the engine can light the smoke with the Sun and the sky fill |
| `<variant>.json` | frame times (seconds), the playable span (`startSeconds` to `seconds`: the burst's still beginning is skipped; a smoke variant starts after the fire is gone), scales, the radius that holds 90 per cent of the mass (half the frame = 1) |

Variants (`VARIANTS` in the script): `fire_a` (a round burst), `fire_b` (broad and slow, ends early), `fire_c` (a tall jet), `fire_d` (a big slow billow), `smoke_a`
(a burst that burns out into grey smoke: the cooling variant). Add one by adding a line. `--res` is the simulation grid (192 is the library; 64 to 96 for a quick look),
`--reuse` re-renders an existing bake (the cache is in the temp folder), `--frames` caps the simulation. A variant takes about 8 to 12 minutes at res 192 (the bake is most of it).
`tools\juice\flip_sheet.py` makes a contact sheet of the library.

Size: 5 variants, 13 MB of PNG on disk (1.9 to 3.2 MB each), 45 MB of video memory (BC7, one texture array). The importer (`JuiceFlipImporter` in `Editor/JuiceSetup.cs`)
sets them linear, no mipmaps, clamped, BC7, 4096 max.

In the game: `FlipLibrary` (Fx/Flipbooks.cs) loads them, `Emitters.Flipbook` plays one (turned and flipped at random), shader kind 8 in `MN_Fx.shader` shades it.
