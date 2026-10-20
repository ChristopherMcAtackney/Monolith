# Architecture

Monolith is built code-first. The only scene in the build is an empty `Boot` scene; everything else is constructed from
C# at start-up. Each system lives in `Game/Assets/Monolith/Scripts/<System>/` with its own assembly definition
(`Monolith.<System>`). Design intent is in `docs/superpowers/specs/`; how to run things is in `tools/PIPELINE.md`.

## Assemblies

**Core** (`Monolith.Core`) is the foundation that every other assembly may reference and that references nothing of ours.
It holds the fixed 60 Hz `Sim` clock (ordered `ISimSystem` steps, an `Alpha` for render interpolation, `SimDriver` to feed
it), `GameWorld` (shared state, one named owner per field), `GameEvents` (named events: `Raise` and `Subscribe`),
`CommandLine` (`-mn` flags), `MnLog` (`[MN]` log lines), `ControlInput` (pitch, yaw, roll, throttle, buttons) with
`IPilotSource` and `Pilots` (every ship's input each tick, whoever is flying), and `CoreSelfTest`.

**Capture** (`Monolith.Capture`) is the headless picture-taker. In the built player, `-mnCapture plan.json -mnOut dir`
renders stills at exact resolutions with fixed game time and runs real-time perf runs, writing PNGs, `perf_*.json` and
`capture_manifest.json`. It shoots through `GameWorld.MainCamera` and knows nothing else about the game.

**Game** (`Monolith.Game`) is the composition root. `GameBootstrap` runs after the Boot scene loads and creates
`GameRoot`, which builds the clock, the world and every system, and registers them. Today it also builds the smoke scene
(`SmokeWorld`: camera, sun, sphere and box), a stand-in until the Render system exists. It is the only assembly allowed to
reference the others.

**Editor** (`Monolith.Editor`, namespace `Monolith.EditorTools`, editor only) is scripted project set-up and building:
`Batch.Run` wrapper, `Setup.Project` (HDRP asset and global settings, colour space, Input System, player settings, the
Boot scene), `Build.Player`. It is run by `tools/unity.ps1`; nobody opens the editor.

## Rules

1. Systems talk through `GameWorld` (state) and `GameEvents` (things that happened), never by referencing each other.
   Only `Game` references system assemblies, and only to build and wire them.
2. A new system is a new folder, a new asmdef, and a few lines in `GameRoot.Build` plus a reference in
   `Monolith.Game.asmdef`. Do not put a second system in an existing assembly.
3. Anything that affects gameplay runs inside an `ISimSystem.Step` on the fixed clock and reads time from the
   `SimFrame`, never from `UnityEngine.Time`. Rendering reads `GameWorld.Alpha` to interpolate.
4. Every ship is flown through a `ControlInput` from an `IPilotSource` (player, AI, script, later remote). Nothing
   assumes there is only one local pilot.
5. Each `GameWorld` field names its owner, the one system allowed to write it. Add a field only when a second system
   needs to read it.
6. Content is data (ships, weapons, missions), not code. Anything the player build needs at runtime from an asset goes
   in a `Resources` folder or is referenced by one, and is created by a script in `Editor`.
7. Keep this file short. One paragraph per assembly; detail belongs in a doc next to the system's code.
