# Monolith

An original space combat game in the genre of FreeSpace 2 and TIE Fighter, in Unity 2022.3.26f1 with HDRP. The player flies for the human
diaspora, coming home to fight Earth's would-be gods. First person only.

## Start here
- The design: `docs/superpowers/specs/2026-10-20-monolith-vertical-slice-design.md`
- How work is built and judged (builders, blind critics, the bar, agent rules): `gauntlet/PROTOCOL.md`
- Each piece's history, numbers and known gaps: `gauntlet/pieces/<piece>/history.md`
- The progress board: https://claude.ai/artifact/THAZKCkV2CwctB8FSAks7H (its data lives in the artifact's database)
- Building, running, captures and every command-line flag: `tools/PIPELINE.md`; the code's layout: `docs/architecture/README.md`
- The mission format: `Game/Assets/Monolith/Scripts/Mission/README.md`
- Playtest launcher words: `tools/audio/OPTIONS.txt`

## Standing rules (the user's)
- Never copy FreeSpace 2 or TIE Fighter code, assets or text. Their installs and the open-source code are reference only.
- No Unity Asset Store ship models; ships are modelled by script in Blender. Stock textures only as clearly marked placeholders.
- Never generate voice files for a line the user has not approved. New lines are written as text with `"pending": true` and go
  through the dialogue sheet (`design/dialogue/monolith_dialogue.xlsx`).
- First person only, never a third-person view. No first-person hands. Look is inverted by default.
- At most two builders at once. Sonnet by default; Opus only with the user's OK. Builders work in their own git worktrees, commit
  locally with explicit paths and never push; the lead verifies each piece on its own frozen playtest build, merges and pushes.
- When the user asks a question, answer it; don't build or change things they didn't ask for.
- The user's playtests overrule every critic.
