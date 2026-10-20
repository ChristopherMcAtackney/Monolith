# Gauntlet protocol (Monolith)

The lead session runs this loop for every piece on the board. The design is
`docs/superpowers/specs/2026-10-20-monolith-vertical-slice-design.md`; the build order is its section 12.

## The bar
The Expanse's space battles, with FreeSpace 2's big, meaty lasers everywhere (user, 2026-10-20).
- Look pieces: our in-game stills against real frames from The Expanse's battle scenes, blind.
- Laser and juice pieces: also against real footage of FS2's capital-ship beam battles, for the weight
  and density of fire.
- Cabin pieces: against photos of real spacecraft interiors.
- Measurable half: flight traces match FS2's curves (design section 4 and the appendix); 60 fps at
  1080p on the RTX 4070 Ti, 1% lows included.
- The user's playtests overrule every critic.

## One round
1. **Builder** (fresh agent) reads the design, `tools/PIPELINE.md`, `docs/architecture/README.md` and
   `gauntlet/pieces/<id>/history.md`. It fixes the biggest remaining gap first, captures from a fresh
   build into `gauntlet/captures/<id>/r<N>/`, and reports which captures are its best honest attempt.
   It never picks reference frames and never sees verdict keys.
2. **Lead** pairs captures with reference frames of the same subject and framing, labels stripped.
   Keys go to `gauntlet/keys/` only.
3. **Critic** (fresh agent, no project context) sees only the blind pairs (or, for measured pieces, the
   raw traces and numbers). It says which is better, how sure it is, and names the single biggest
   giveaway or gap. Harsh; praise is useless.
4. **Lead** records the verdict and gap in `gauntlet/pieces/<id>/history.md`, updates the board, and
   commits a checkpoint.

A piece wins when the critic picks ours in at least 2 of 3 pairs (or calls all three a coin-flip), two
rounds running against different reference frames. Measured pieces win when the numbers pass.

## Lessons carried over from Trap Door
- The verdict is the signal, the prescription is noise. Builders treat the critic's giveaway as a
  pointer and decide the fix from the references and measurements.
- Never fake a capture: every capture comes from the built game as the player sees it.

## Agent rules (user)
Sonnet builders and critics by default. Opus only for hard code, and only with the user's OK; the user
approved Opus for the flight model, the AI and the mission system (2026-10-21). At most two builders at
once. No timer heartbeats: the lead wakes when an agent finishes. Builders commit
locally with explicit paths and never push; the lead pushes each checkpoint to origin (user, 2026-10-21).
Reference clips (The Expanse, FS2 footage) are downloaded with yt-dlp into `reference/` (gitignored,
critics only), with the user's OK of 2026-10-21.

## The board
https://claude.ai/artifact/THAZKCkV2CwctB8FSAks7H. Source `gauntlet/progress/index.html`; its data
(pieces, log, meta) lives in the artifact's database and is updated by the lead.
