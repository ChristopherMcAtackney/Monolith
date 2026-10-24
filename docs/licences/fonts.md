# HUD typefaces and their licence

The HUD, the battle map and the cabin's displays set their type in three typefaces, one per HUD style. All three are free fonts under the
**SIL Open Font License, Version 1.1** (<https://openfontlicense.org>), which allows use, study, modification and redistribution, including
inside a game, provided the licence text and copyright notice travel with the font and the font is not sold on its own. None of the three declares a
Reserved Font Name. Each font's own licence file, with its copyright line, is kept beside the font file it came with.

| Style | Typeface | Designers | Copyright and licence file | Source files in the repo | Baked atlas the game loads |
|---|---|---|---|---|---|
| Halo | **Oxanium** (variable weight, set at 500) | Severin Meyer | Copyright 2019 The Oxanium Project Authors (<https://github.com/sevmeyer/oxanium>), `art_src/hud_fonts/Oxanium/OFL.txt` | `art_src/hud_fonts/Oxanium/Oxanium[wght].ttf` | `Game/Assets/Monolith/Resources/Fonts/oxanium.*` |
| Meridian | **B612** (Regular) | Nicolas Chauveau, Thomas Paillot, Jonathan Favre-Lamarine, Jean-Luc Vinot (Airbus and ENAC: drawn for aircraft cockpit screens) | Copyright 2012 The B612 Project Authors (<https://github.com/polarsys/b612>), `art_src/hud_fonts/B612/OFL.txt` | `art_src/hud_fonts/B612/B612-Regular.ttf` | `Game/Assets/Monolith/Resources/Fonts/b612.*` |
| Cipher | **Jura** (variable weight, set at 600) | Daniel Johnson, Cyreal | Copyright 2019 The Jura Project Authors (<https://github.com/ossobuffo/jura>), `art_src/hud_fonts/Jura/OFL.txt` | `art_src/hud_fonts/Jura/Jura[wght].ttf` | `Game/Assets/Monolith/Resources/Fonts/jura.*` |

The font files were taken on 2026-10-24 from the Google Fonts repository (<https://github.com/google/fonts>, directories `ofl/oxanium`, `ofl/b612`,
`ofl/jura`), which carries each font under its upstream licence file unchanged.

## What the game ships

The game does not load the `.ttf` files. `tools/hud/make_font_atlas.py` bakes each one into a signed-distance-field atlas (`<id>.sdf.bytes`: 8-bit
distances, 128 at a glyph's edge) and its metrics (`<id>.fontmeta.json`: where each glyph is, how far it advances), both under
`Game/Assets/Monolith/Resources/Fonts/`. The metrics file names the family, the licence and the source, and the game checks at start (`-mnHudSelfTest`)
that each font reports the SIL Open Font License. An atlas is a bitmap derived from the font outlines (a "Modified Version" in the licence's terms: the
shapes are the font's, the container is ours); it is distributed with the licence text above and this note, is not sold separately, and does not use the
font's name as a product name.

To change a font or its character set (ASCII plus a few symbols), run, from the repo root:

```
python -I tools/hud/make_font_atlas.py --font "art_src/hud_fonts/Oxanium/Oxanium[wght].ttf" --weight 500 --id oxanium --family Oxanium --source https://github.com/sevmeyer/oxanium --out Game/Assets/Monolith/Resources/Fonts
python -I tools/hud/make_font_atlas.py --font "art_src/hud_fonts/B612/B612-Regular.ttf" --id b612 --family B612 --source https://github.com/polarsys/b612 --atlas 1024 1024 --out Game/Assets/Monolith/Resources/Fonts
python -I tools/hud/make_font_atlas.py --font "art_src/hud_fonts/Jura/Jura[wght].ttf" --weight 600 --id jura --family Jura --source https://github.com/ossobuffo/jura --out Game/Assets/Monolith/Resources/Fonts
```

(Pillow only; about a minute each.) The three licence texts are also copied into `Game/Assets/Monolith/Resources/Fonts/OFL-<Family>.txt`, so they are inside every build, and a playtest folder carries them as `Licences\`.

## Why these three

The user's note (2026-10-24) was that the placeholder type was "squished up and hard to read". The placeholder was a four-by-eight stroke font. The three
replacements are all wide, open, evenly spaced faces with real lowercase, digits that are as wide as the capitals are tall, and a clear difference
between letters that look alike (I, l, 1, 0, O): Oxanium for a squared, slightly alien look, B612 because it was drawn for glass-cockpit readability,
and Jura for a lighter, rounded-square, glyph-like look. The type is also tracked (small tracked capitals for labels, a little extra space in running text).
