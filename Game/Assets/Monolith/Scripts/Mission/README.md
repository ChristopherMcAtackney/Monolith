# Missions (`Monolith.Mission`)

A mission is one JSON file, `<name>.mission.json`, scripted the way FS2's missions are: ships and wings that arrive on cues, and events
that fire when a condition tree holds (almost never on a plain timer). `MissionRuntime` runs it on the 60 Hz sim (order `Mission`, after
weapons and damage), logs the pacing timeline and publishes the directives and objectives on `GameWorld.Mission` for the HUD. The assembly
references Core only; the game does its spawning, AI orders and effects through `IMissionHost` (`Game/Mission/GameMissionHost.cs`). Built-in
missions live in `Resources/Missions/`; the playtest folder's `Missions/` copies are read first, so they can be edited without a build.

| File | What |
|---|---|
| `MissionFile` | The format: reading, checking (misspelt fields, unknown names, conditions, actions are errors, all reported at once), `MissionLibrary` (find a mission by name or path) |
| `MissionConditions`, `MissionActions` | Every condition and action |
| `MissionRuntime` | Arrivals, the arrival cap, wing waves, departures, events, objectives, directives, the end, the pacing sample |
| `MissionRadio`, `MissionLog` | The radio queue and the music state (`MissionMusic`); the timeline (events.csv, pacing.csv) |
| `MissionHost` | `IMissionHost` (what the game does), `ShipSpawn`, `ShipOrder`, the cabin hook `ICabin` and its stand-in `CabinStub` (headless runs; the real one is `Monolith.Cabin`'s `CabinSystem`) |

Run: `Monolith.exe -mnMission dogfight` (a name or a path; `-mnDifficulty`, `-mnAutopilot`, `-mnInvulnerable`, `-mnSkipBriefing`,
`-mnPacingLog dir`), or a capture plan with `"scene": "mission", "sceneOptions": { "mission": "escort", "pilot": "ai" }`. Checks:
`-mnMissionSelfTest`, `-mnMissionAutoPlay <mission> -mnOut dir`, `-mnDogfightPortCheck seconds -mnOut dir` (see the end).

## The file

```json
{ "format": "monolith-mission/1", "id": "escort", "name": "The Crossing", "difficulty": "Medium", "player": "Sable 1",
  "teams": ["diaspora", "earth"],                         // team 0 is the player's side
  "arrivalCap": { "team": "earth", "value": [3, 4, 6, 8, 10] },  // capped wings wait while this many of them are alive (per level)
  "briefing": { "title": "...", "stages": ["..."], "questions": [ { "q": "What are the objectives", "a": "..." } ] },
  "debriefing": [ { "when": { "objective": "warden" }, "text": "..." } ],
  "music": { "start": "ambient", "auto": true, "tracks": { "battle2": "oort_battle_2" } },
  "pacing": { "hulls": ["Tamsin", "Lector"], "quietRange": 2500 },
  "ships": [ ... ], "wings": [ ... ], "objectives": [ ... ], "messages": [ ... ], "events": [ ... ],
  "autoplay": { "seconds": 900, "expect": { ... } } }
```
Any field ending in `Why`, and `about` and `note`, is a comment: say where a number came from. The HUD's font has letters, digits and
`. , : - + / ( ) ' %` only: no question or exclamation marks in text the player sees.

**Ships** (single ships) and **wings** (groups whose ships are named `<wing> 1`, `<wing> 2`... across all waves):
```json
{ "name": "Warden", "class": "earth_corvette", "team": "earth", "hullScale": 0.5, "hull": 100, "selfHeal": false, "invulnerable": false,
  "ai": "drone", "role": "fighter", "wingman": false, "ammo": { "torpedo_heavy": 2 },
  "capital": { "standoff": 2700, "band": 400, "bearing": 65, "throttle": 0.3, "turretProfile": "earth_turret", "turretWaitScale": 1.5,
               "beamWaitScale": 4, "holdBeams": false, "stationary": false, "weapons": { "SUB_aaf_F01": "beam_slash" } },
  "look": { "hull": { "length": 460, "width": 110, "height": 90, "seed": 23 } },
  "goal": { "order": "attack", "target": "Small Mercy" }, "fallback": { "order": "guard", "target": "Sable 1" },
  "arrival": { "cue": { "destroyed": "Choir", "delay": 6 }, "delay": 0, "at": [900, 60, 3000], "rot": [0, -115, 0], "speed": 0.3, "effect": "jump" },
  "departure": { "cue": { "hull": "Warden", "below": 20 }, "to": [0, 0, 9000], "jumpAfter": 3 } }

{ "name": "Choir", "team": "earth", "capped": true, "waves": 1, "waveThreshold": 0, "waveDelay": 5, "wingmen": false,
  "ships": [ { "class": "earth_interceptor", "ai": "drone", "count": 3 },
             { "class": "earth_fighter", "ai": "drone", "count": 1, "goal": { "order": "attack", "target": "Small Mercy" } } ],
  "goal": { "order": "engage" },
  "arrival": { "cue": false, "at": [-2400, 300, 2600], "facing": "Small Mercy", "formation": "v", "spacing": [45, 8, 40], "speed": 0.9 } }
```
- `hull` is the hull at arrival in percent (a damaged freighter); `hullScale` scales the class's hull. `ai` is an AI profile (`Resources/AI/ai.json`;
  default `drone` for hostiles, `wingman` for ours); `role: "bomber"` flies torpedo runs. A big ship (a corvette or capital class) gets a capital
  helm and gunners from `capital` (or the defaults). `wingman: true` puts a ship under the quick menu's orders. A class with a Blender model in
  `Resources/ShipArt` uses it; any other big ship gets a placeholder hull (`look.hull`, else the class's size).
- **Arrival**: no `cue` (or `true`) is present at the start, without a flash; a condition arrives when it holds, plus `delay`, with the jump flash
  (`effect: "none"` for none); `false` arrives only through an `arrive` action. Where: `at` (a point), `near` a ship (with `offset` in its frame, or
  `distance` in a direction fixed by the name), or `bay` (out of a ship). Facing: `facing` (a ship, or a direction), `rot` (Euler degrees), else the
  anchor ship's heading. A wing's ships take places in `formation` (`v`, `line`, `column`, `none`) at `spacing`, or exact `offsets`.
  `speed` is a share of top speed at arrival, `throttle` defaults to it.
- **Waves** (FS2's): a wing with `waves: 3` sends its next wave `waveDelay` seconds after the current one is down to `waveThreshold` ships.
- **The arrival cap**: a `capped` wing of the cap's team queues its ships while the team's capped fighters alive are at the cap; they come in,
  oldest first, as room is made (the dogfight's "never more than N at once").
- **Orders** (`goal`, `fallback`, the `order` action): `attack` (with `part`: a name, or a list where the first still standing is attacked),
  `disable` (engines), `disarm` (its first weapon part), `guard`, `form` (on a leader), `engage` (free fight), `bomb` (torpedo runs),
  `waypoints` (`path`, `speed`, `then`: `stop`, `loop` or `depart`), `depart` (`to`). A target may be a wing: ship *k* of the ordered wing takes
  ship *k* of the target wing. `fallback` is what the ship goes back to when an order is done; guard, form, bomb and engage are their own
  fallback unless one is given. An order a ship is already carrying out changes nothing.

**Objectives**: `{ "id": "warden", "kind": "primary" | "secondary" | "bonus", "text": "Destroy the Warden", "hidden": false, "complete": {cond}, "fail": {cond} }`.
Checked every tick until settled (also by the `objective` action); a hidden bonus shows once met.

**Messages**: `{ "id": "bombers", "from": "Small Mercy", "fallbackFrom": "#EARTH", "text": "...", "priority": "high", "voice": "vo/mercy_04.wav", "seconds": 4 }`.
`from` is a ship, `"<any wingman>"` (a living wingman, else the player) or `"#Name"` (off the battlefield). A message from a ship that is gone
is dropped unless `fallbackFrom` can say it. One line at a time on the channel: high priority first, low dropped when busy. `voice` is the slot for
the recording (raised as `radio_voice`; nothing plays it yet).

**Events** (FS2's):
```json
{ "name": "Psalm in", "when": { "and": [ { "arrived": "Choir" }, { "destroyedPercent": "Choir", "atLeast": 66 } ] }, "delay": 5,
  "do": [ { "do": "arrive", "ship": "Psalm" }, { "do": "radio", "message": "earth_wave_2" } ],
  "repeat": 1, "interval": 1, "after": "Choir in", "afterDelay": 0, "failWhen": { "destroyed": "Small Mercy" },
  "directive": { "text": "Destroy Psalm", "show": { "arrived": "Psalm", "any": true } }, "log": true }
```
An event waits for `after` (plus `afterDelay`), then evaluates `when` every tick; when it holds, its actions run `delay` seconds later.
`repeat` (−1 for ever) re-arms it `interval` seconds after it fires. `failWhen` ends it unfired. A `directive` shows on the HUD while the event
is live (and `show` holds), then "done" or "failed" for a few seconds. `log: false` keeps a frequent check out of the timeline.
Events are evaluated in file order each tick.

## Conditions

`true`, `false`, or an object whose first operator key is the operator and whose other keys are its modifiers. Names are ships, wings, wing
members (`"Choir 2"`) or `"<player>"`. Delays are seconds after the fact first became true.

| Condition | Example | True when |
|---|---|---|
| `and`, `or`, `not` | `{ "and": [ {...}, {...} ] }`, `{ "not": {...} }` | |
| `time` | `{ "time": 4 }` | the mission has run 4 s |
| `arrived` | `{ "arrived": "Choir", "delay": 2, "any": true }` | the ship (all of a wing's first wave; `any`: one of them) arrived, 2 s ago |
| `destroyed` | `{ "destroyed": ["Warden", "Lector"], "delay": 6 }` | all of them destroyed (every ship of a wing, all waves), 6 s ago |
| `departed` | `{ "departed": "Tamsin" }` | jumped out or docked |
| `disabled` | `{ "disabled": "Warden", "delay": 1 }` | every engine destroyed |
| `disarmed` | `{ "disarmed": "Warden" }` | every turret destroyed (a fighter: its guns) |
| `destroyedPercent` | `{ "destroyedPercent": "Choir", "atLeast": 66 }` | that share of the wing's ships (all waves) destroyed |
| `hull` | `{ "hull": "Lector", "below": 70 }` (or `above`) | hull percentage of an arrived ship (destroyed: 0) |
| `subsystem` | `{ "subsystem": "Warden", "part": "SUB_aaf_F01", "below": 50 }`, `{ ..., "parts": [...], "destroyed": true }`, `{ ..., "kind": "engine", "below": 30 }` | a part's health (several: their mean), or all of them destroyed |
| `range` | `{ "range": "<player>", "to": "Warden", "below": 2000 }`, `"to": [0, 0, 9000]` | distance between two ships, or a ship and a point |
| `hostilesNear` | `{ "hostilesNear": "Tamsin", "within": 1500, "atLeast": 2, "small": true }` | that many hostile fighters (small: false counts capitals) near it |
| `teamAlive` | `{ "teamAlive": "earth", "atMost": 0, "small": false }` | ships of a team alive (atMost / atLeast) |
| `event` | `{ "event": "Bombers in", "delay": 3.5, "state": "fired" }` | another event fired (or `failed`, or `pending`), with a delay |
| `objective` | `{ "objective": "warden", "is": "complete" }` | `complete`, `failed` or `pending` |
| `beaming` | `{ "beaming": "Warden" }` | it is charging or firing a heavy beam |
| `autopilot` | `{ "autopilot": true }` | the AI flies the player's ship (soaks, checks: lets a mission tell its autopilot what to go after) |
| `difficultyAtLeast` | `{ "difficultyAtLeast": "Hard" }` | |
| `seated`, `powered` | `{ "seated": false }`, `{ "powered": false }` | the pilot out of the seat; the ship dark (**the cabin**) |
| `detected` | `{ "detected": "Lector" }`, `{ "detected": "<any>" }` | a scan from that ship found the player (**the cabin**) |
| `needsRepair` | `{ "needsRepair": "<player>", "part": "engine_port" }` | a knocked-out part (or any destroyed part of the player's ship) still needs a hand repair (**the cabin**) |

## Actions

| Action | Example | Does |
|---|---|---|
| `arrive` | `{ "do": "arrive", "ship": "Psalm" }` | a ship, or a wing's next wave, comes in now (jump flash and sound through the juice: `jump_in`) |
| `depart` | `{ "do": "depart", "ship": "Tamsin", "to": [0, 0, 9000], "jumpAfter": 3 }`, `"bay": "Mercy"` | flies out and jumps (`jump_out`), or docks |
| `radio` | `{ "do": "radio", "message": "bombers" }`, or inline `{ "do": "radio", "from": "#Command", "text": "...", "priority": "high" }` | queues a message |
| `objective` | `{ "do": "objective", "id": "bombers", "set": "complete" }` | `complete`, `failed`, `pending`, `show`, `hide` |
| `order` | `{ "do": "order", "ship": "Vigil", "order": "guard", "target": "Censer", "standing": true }` | an AI order to a ship or every ship of a wing (standing: also its fallback) |
| `beams` | `{ "do": "beams", "ship": "Small Mercy", "free": true }` | FS2's beam-free / beam-lock |
| `music` | `{ "do": "music", "state": "battle3", "for": 30 }`, `{ "do": "music", "auto": true }`, `{ "do": "music", "sting": "enemy_arrival" }` | the music state (`ambient`, `battle1..3`, `victory`, `failure`, `death`) |
| `end` | `{ "do": "end", "result": "victory", "text": "AREA CLEAR" }` | `victory`, `defeat`, `neutral`; the debrief follows |
| `cond` | `{ "do": "cond", "cases": [ { "if": {...}, "then": [...] }, { "else": [...] } ] }` | the first case that holds |
| `invulnerable` | `{ "do": "invulnerable", "ship": "Tamsin", "on": true }` | damage never kills it |
| `log` | `{ "do": "log", "text": "..." }` | a line in the timeline |
| `scan` | `{ "do": "scan", "from": "Lector", "seconds": 12, "range": 6000 }` | a scan sweep (**the cabin**: the light crawls through the canopy; a powered player in range is found, a dark one is missed; the stub: found if powered and in range) |
| `callOut` | `{ "do": "callOut", "text": "Get up. Pull the breaker.", "prompt": "Leave the seat" }` | a radio line and a prompt to leave the seat (**the cabin** shows "hold View: stand up" at the seat) |
| `knockOut` | `{ "do": "knockOut", "ship": "<player>", "part": "engine" }` | destroys a part (a name or a kind) so it needs a hand repair (**the cabin**: at the engine or the arms panel) |
| `cabinLights` | `{ "do": "cabinLights", "state": "dark" }` | `normal`, `dark`, `emergency`, `alert` (**the cabin**: the instruments' glow and the red strips) |

Built-in: the player's death ends the mission ("SHIP LOST") unless `"playerDeathEnds": false`. Without an `end`, a mission runs on.

## The cabin

`Monolith.Cabin` (`Scripts/Cabin/README.md`) is the pilot as a person in the ship; its `CabinSystem` implements `ICabin` and the game
(`Game/Mission/MissionScene`) passes it to `MissionRuntime`. A mission file can carry a `cabin` block:

```json
"cabin": { "start": "table", "standAnytime": true,
           "rack": { "guns": ["needle", "hammer"], "missiles": ["missile_heat", "missile_lock", "torpedo_heavy"],
                     "defaultGuns": ["needle", "hammer"], "defaultMissiles": ["missile_heat", "missile_lock"] } }
```

`start: "table"` begins the mission with the sim held, the pilot at the plan table (the briefing on it), the weapon rack open and the loadout
to choose; sitting down and pressing launch starts the clock. The rack's choice feeds the ship's guns and missiles before launch.

What the conditions and actions do for real: `seated` is false while the pilot is up; `powered` is the main breaker; `detected` is decided when a
sweep's light crosses the ship (powered, or dark less than five seconds, inside the range); any destroyed part of the player's ship is a
`needsRepair` until the pilot's hands have knit it (it comes back at 25 percent health); `callOut` puts a "hold View: stand up" prompt at the seat; `scan` sends the
light through the canopy (with the hull's creak four seconds before); `knockOut` destroys the part for the cabin to hold; `cabinLights` sets the
glow. A mission never needs the cabin: with `-mnNoCabin`, in an AI-flown run, or from a mission with no use for it, `CabinStub` answers (always
seated and powered, a scan detects a powered player in range, a knock-out destroys the part and the AI flying "repairs" it after 8 s).
`Resources/Missions/cabin.mission.json` ("Dead Water") is the test: quiet drift, a call to go dark, the Lector's sweep that misses a dark ship
and finds a powered one, a hit that takes the port engine while drones come in, a briefing at the plan table and the loadout at the rack.

## Music, juice, HUD

The runtime only publishes: `music_state` / `music_sting` and `GameWorld.Mission.Music` (the audio system will play them; auto follows how
many hostiles are close or on the player), `jump_in` / `jump_out` (`Juice/Fx/Jumps` draws them: a scene attaches it to its juice director with
`Jumps.Attach`, the mission scene does), `radio_line` (the HUD shows it), the directives and
objectives on `GameWorld.Mission` (`CombatHud` draws the directives; the scene shows the briefing, the objectives on pause and the debrief).
Event ids and fields: `Core/MissionBoard.cs`.

## Checks

- `-mnMissionSelfTest`: every built-in mission loads and a broken one is refused with each problem named; every condition and action on a scripted
  world (`Resources/MissionTests/selftest.mission.json`: the test kills ships and parts at set times and checks the tick each event fires);
  each built-in mission runs a minute headless; the dogfight's port matches the hand-written dogfight to the bit for 240 s.
- `-mnMissionAutoPlay <mission> -mnOut dir [-mnSeconds n] [-mnDifficulty x] [-mnMortal]`: the AI flies the player (invulnerable unless the
  mission's `autoplay.invulnerable` is false) until the end; then the mission's `autoplay.expect` is asserted: `result` (any of), `fired`,
  `notFired`, `order` (the ones that fired, in that order), `longestQuietMax`, `maxEngagingMax`, `maxThreatsMax`, `threatsAvgMin`; plus no errors,
  no bad numbers, no stuck AI. Writes `events.csv`, `pacing.csv`, `autoplay.txt`, `autoplay.json`, `radio.txt`.
- `-mnDogfightPortCheck seconds -mnOut dir [-mnDifficulty x]`: `DogfightBattle` against `dogfight.mission.json`, every ship's state compared
  once a second.
