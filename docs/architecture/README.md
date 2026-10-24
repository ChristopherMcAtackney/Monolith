# Architecture

Monolith is built code-first. The only scene in the build is an empty `Boot` scene; everything else is constructed from
C# at start-up. Each system lives in `Game/Assets/Monolith/Scripts/<System>/` with its own assembly definition
(`Monolith.<System>`). Design intent is in `docs/superpowers/specs/`; how to run things is in `tools/PIPELINE.md`.

## Assemblies

**Core** (`Monolith.Core`) is the foundation that every other assembly may reference and that references nothing of ours.
It holds the fixed 60 Hz `Sim` clock (ordered `ISimSystem` steps, an `Alpha` for render interpolation, `SimDriver` to feed
it), `GameWorld` (shared state, one named owner per field), `GameEvents` (named events: `Raise` and `Subscribe`),
`CommandLine` (`-mn` flags), `MnLog` (`[MN]` log lines), `ControlInput` (pitch, yaw, roll, throttle, buttons) with
`IPilotSource` and `Pilots` (every ship's input each tick, whoever is flying), `ShipState`/`ShipRegistry` (every ship's
published flight state, `GameWorld.Ships`), `ShipKnock`/`KnockQueue` (shoves for Flight to apply, `GameWorld.Knocks`),
`ScriptedPilot` (keyed inputs, optionally looping) and `CoreSelfTest`. Events can say where they happened
(`GameEvents.RaiseAt`: position, direction, a second ship); `CombatEvents` names the ones weapons, AI and missions raise
(`weapon_fired`, `hit_shield`, `hit_hull`, `ship_destroyed`, `beam_fire`, `flak_burst`...) and what each field means.

**Capture** (`Monolith.Capture`) is the headless picture-taker. In the built player, `-mnCapture plan.json -mnOut dir`
renders stills at exact resolutions with fixed game time and runs real-time perf runs, writing PNGs, `perf_*.json` and
`capture_manifest.json`. It shoots through `GameWorld.MainCamera` and knows nothing else about the game.

**Flight** (`Monolith.Flight`) moves every ship once per tick from its `ControlInput`, FS2's way (exponential chases,
auto-bank, slip, afterburner fuel, match speed, knocks), and publishes `ShipState`. Ship numbers are data
(`Resources/Ships/<class>.json`, every value with its source; shared constants in `Resources/Flight/flight_rules.json`).
`-mnFlightScenarios` replays the feel scenarios and writes traces for `tools/fs2ref/compare.py`. Detail:
`Scripts/Flight/README.md`.

**Input** (`Monolith.Input`) is the local player's pad and keyboard as an `IPilotSource` (`PlayerPilot`), with mappings,
dead zones and response curves in `Resources/Input/controls.json` (or `-mnControls file`).

**Targeting** (`Monolith.Targeting`) sets `ShipState.Target` and `TargetPart` from the pad's buttons or an AI's `ControlInput.TargetShip` (cycle hostiles nearest first, nearest attacker, the ship in the reticle, a target's parts nearest the nose first), and publishes the lead point and distance the HUD aims with.

**Weapons** (`Monolith.Weapons`) fires from every ship's `ControlInput` buttons (the pad, an AI and a script are the same to it) and from turret `FireRequest`s, and flies every projectile on the tick with swept hit tests: two guns, flak, three missiles (a heat-seeker, a lock-on with a lock tone, a heavy torpedo that can be shot down) and decoys. Weapon numbers are `Resources/Combat/weapons.json`, each copied from FS2's table (checked by `tools/combat/check_weapons.py`) or marked as ours. **Damage** (`Monolith.Damage`) resolves every hit: the shield bubble and its recharge from the power choice, hull, named parts and what losing each one does, self-healing on the player's side, deaths (`ship_destroyed` for the juice). **Collision** (`Monolith.Collision`) bumps ship against ship and against capital hulls through the flight model's knock hook and hurts by speed and mass. Their shared data (`ShipCombat`, `WeaponDef`, ship definitions, hit shapes, the damage and fire queues, the `IShipVisual` the art side implements) lives in Core's `Combat` folder; the AI contract and the details are in `Scripts/Weapons/README.md`.

**CombatHud** (`Monolith.CombatHud`) is the plain playtest combat HUD (lead pip, target box and readout, lock, missile warning, shield, hull and energy, power, ammunition, damage you feel, the quick menu) and the lock and warning tones. It reads `GameWorld` only and is kept small so the rough alien-tech HUD can replace it.

**AI** (`Monolith.AI`) flies every computer-controlled ship through its `ControlInput` and fires every capital turret and beam through
`FireRequest`s, after FS2's AI: fighters and bombers (`AiPilot`: get on the tail with lead, guns and missiles, evade, break off and re-engage,
peel off by courage, strafe and bomb capital ships, guard, form up), capital helms and turret gunnery (`CapitalAi`), the player's wing and the
quick-menu orders (`Wing`), measurements (`AiStats`). FS2's four skill knobs per profile and its five difficulty levels are data
(`Resources/AI/ai.json`); the difficulty also sets `GameWorld.PlayerDamageScale` and the wingmen's `ShipCombat.DamageTakenScale`, which Damage
applies. Capital turrets follow FS2's turret rules (fields of fire, a cap on the guns on the player, targets spread, aim that settles while a target
loiters). Detail: `Scripts/AI/README.md`.

**Mission** (`Monolith.Mission`) runs a mission file (`Resources/Missions/<name>.mission.json`, one per mission; or `Missions/` beside the
game, read first) on the sim at order `Mission`: ships and wings that arrive on cues and wave rules (with a jump flash: the juice's
`Fx/Jumps` draws `jump_in` and `jump_out`), FS2-style events (a condition tree, delays, repeats, chaining, directives) whose actions bring ships in, send radio, set objectives,
give AI orders, free or hold beams, change the music state and end the mission, and the pacing timeline. It references Core only: the game
spawns and orders ships for it through `IMissionHost` (`Game/Mission/GameMissionHost`), and it publishes the directives, objectives, music
state and result on `GameWorld.Mission` for the HUD. The cabin's conditions and actions go through `ICabin` (`Monolith.Cabin` implements it; a stub answers in headless runs). `-mnMission
<name>` plays one (`Game/Mission/MissionScene`). Detail: `Scripts/Mission/README.md`.

**Cabin** (`Monolith.Cabin`, `Scripts/Cabin/`) is the pilot as a person in the ship: hold View and the eye rises out of the seat, the HUD folds into the cabin's own displays, the
left stick walks a small worn hull with grafted dark biomechanical tech, the right stick looks, and one context button does the physical thing you are looking at: pull the main breaker
(the ship goes dark and a sweeping scan finds a powered ship and misses a dark one), hold to knit a destroyed part back with your hands, choose the guns and missiles at the weapon
rack before launch, read the briefing and a retro blip map at the plan table. The ship holds its last heading and speed while the pilot is up. The logic (`CabinSystem`) is plain
data and maths steppable headless, publishes `GameWorld.Cabin` (`CabinBoard`) for the camera, the sky's exposure and the HUD, and implements the mission's `ICabin`. `CabinRig`
presents it: the Blender-scripted cabin on its own HDRP light layer, lights set in screen units against the eye's exposure, displays drawn into render textures with the HUD's own
line drawing, procedural hands with two-bone IK. Numbers in `Resources/Cabin/cabin.json`. Detail: `Scripts/Cabin/README.md`, `art_src/PIPELINE.md`.

**Audio** (`Monolith.Audio`, `Scripts/Audio/`) is the game's own software mixer and everything that plays through it: music, effects and radio voices, with Unity used only as the sound card (`OnAudioFilterRead`). The mixer, a lookahead limiter and a BS.1770 loudness meter are plain C# (`Dsp/`) that the offline tools compile too (`tools/audio`: the analog synth that renders the MIDI score, the sound-effect designs, the radio filter, the checks). `AudioSystem` listens to the game's events (the mission's `music_state` / `music_sting`, `radio_line`, arrivals, decoys, locks, hits on the player) and reads `GameWorld` for the cockpit (engine, afterburner, alarms, lock tone and missile warning); the juice's own effects reach it through `IJuiceAudio` (`Game/Audio/JuiceAudioAdapter`). Five buses with player volumes saved beside the game; voice ducks music and effects, big moments duck the rest. A player's own MP3, WAV or OGG in the `Audio/` folder beside the game replaces any built-in sound by name, live. It can also render without a sound card in step with the sim and record the mix (captures). Detail: `Scripts/Audio/README.md`, `tools/audio/README.md`.

**View** (`Monolith.View`) is the player's first-person camera at the interpolated ship pose, FS2-style motion dust and
`ShipViews` (every other ship's visual at its interpolated pose). Settings in `Resources/View/view.json`. No external camera.

**Hud** (`Monolith.Hud`) draws the flight HUD (reticle, speed and throttle, afterburner fuel, target bracket) as line
graphics after post-processing, so captures show it. Both View and Hud draw through HDRP custom passes with
`Resources/Shaders/MN_Lines.shader`.

**Juice** (`Monolith.Juice`) turns combat events into light, fire, sparks, debris, camera shake, pad rumble and sound, and
knows nothing about who raised them. One HDRP custom pass draws every glow, bolt, beam, spark, fireball and shield ripple in
the HDR buffer before post-processing (`Resources/Juice/MN_Fx.shader`); explosions are a library of simulated fire and smoke (Blender Mantaflow, rendered with Cycles into flipbook atlases by `art_src/juice_fx`, played as sprite sequences, several per blast, with the smoke lit by the Sun and fill; raymarched turbulent volumes from a 64 cubed noise are the fallback) that light nearby hulls with real lights; hull visuals registered with
`JuiceDirector.RegisterShip` are cut into lit pieces when they die, or, for a ship model that has real parts, handed to it through
`IJuiceShipDamage` (the Game assembly adapts `ShipDamage` to it: hits scorch the plating, beams blow parts off, a capital ship loses its
parts one by one and then breaks along cuts baked by `JuiceSetup.BakeSections`); a capital ship dies in stages and its shockwave shoves ships
through `GameWorld.Knocks`. Its sounds go through `IJuiceAudio`, which the audio system implements (the code-built placeholders remain as the fallback when there is no audio system). Settings (presets, switches, levels) are
`juice.json` next to the game, falling back to `Resources/Juice/juice_defaults.json`. Detail: `Scripts/Juice/README.md`.

**ShipArt** (`Monolith.ShipArt`) is how a Blender model becomes a ship in the game. `ShipArtSetup` (editor) turns each model script's output into an HDRP material set, a prefab with a LOD group, a collision proxy and a `ShipSubsystem` on every named part (flak, guns, engines, bridge, sensors, comms, reactor, launchers), each with an intact look, a wreck and debris, plus FX hooks and hull damage sites. `ShipArtCatalog.Instantiate(id)` loads one; `ShipDamage` (`IShipDamage`) is what combat calls to destroy parts and scorch the hull; `EngineGlow` flickers an engine's core, flare and the cold point light it throws on the hull around the nozzle, and follows a throttle. Nothing in it knows game rules. The pipeline, conventions and how to add a ship: `art_src/PIPELINE.md`.

**RenderQuality** (`Monolith.RenderQuality`, `Scripts/Render/Quality/`) is the one shared render-quality profile: the Sun's cascade layout and shadow-map resolution, contact shadows, ambient occlusion, micro shadows, and the optional ray-traced AO and Sun shadows. `SkyRig.Enable` builds it next to the Sun, so every scene that turns the sky on has it and no scene builds a shadow volume of its own; `SkyOptions.Quality` holds the options and `-mnQuality '{json}'` (or a file path) overrides them on any player run. The pipeline asset's matching limits (cascade cap, filter quality, ray-tracing support) are set by `Hdrp.EnsureAsset`.

**Game** (`Monolith.Game`) is the composition root. `GameBootstrap` runs after the Boot scene loads and creates
`GameRoot`, which builds the clock, the world and every system, and registers them. Today it also builds the smoke scene
(`SmokeWorld`: camera, sun, sphere and box) and, with `-mnScene flight` or a plan's `"scene": "flight"`, the flight
playtest (`FlightPlaytest`: the Sky, placeholder ships from `PlaceholderShips`, layout in `Resources/Flight/playtest.json`) or,
with `-mnScene juice`, the juice sandbox (`Sandbox/JuiceSandbox`: the fighter, targets, two Earth corvettes as the capital ships, stand-in weapons and
the panel; `"hud": false` in a plan's `sceneOptions` or `-mnNoHud` leaves the HUD out of look captures) or, with `-mnScene range`, the combat range (`Range/CombatRange`: the real weapons, damage and HUD against drones, emplacements and the real corvette), or, with `-mnScene dogfight`, the dogfight (`Dogfight/DogfightScene` and the rules in `DogfightBattle`: the player and two wingmen against Earth's waves, the Warden and the Small Mercy, a bomber wing, a plain win or loss; `AiLab`, `AiArena`, `DogfightSoak` and `AiSelfTest` run the AI headless), or, with `-mnMission <name>`, any mission (`Mission/MissionScene`; `MissionAutoPlay`, `DogfightPortCheck` and `MissionSelfTest` run missions headless). `Combat/CombatRig` builds the combat systems for every scene and `Combat/CombatSelfTest` checks them. It is the only assembly allowed to reference the others.

**Editor** (`Monolith.Editor`, namespace `Monolith.EditorTools`, editor only) is scripted project set-up and building:
`Batch.Run` wrapper, `Setup.Project` (HDRP asset and global settings, colour space, Input System, player settings, the
Boot scene, and through `FlightAssets` the flight playtest's base materials), `Build.Player`. It is run by
`tools/unity.ps1`; nobody opens the editor.

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
