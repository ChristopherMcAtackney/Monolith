# Missions (`Monolith.Mission`)

A mission is one JSON file, `<name>.mission.json`, scripted the way FS2's missions are: ships and wings that arrive on cues, and events
that fire when a condition tree holds (almost never on a plain timer). `MissionRuntime` runs it on the 60 Hz sim (order `Mission`, after
weapons and damage), logs the pacing timeline and publishes the directives and objectives on `GameWorld.Mission` for the HUD. The assembly
references Core only; the game does its spawning, AI orders and effects through `IMissionHost` (`Game/Mission/GameMissionHost.cs`). Built-in
missions live in `Resources/Missions/` (`dogfight`; `escort`, "The Crossing"; `cabin`, "Dead Water"; `oort`, "The Oculus", the slice's
strike on the outpost in the comet); the playtest folder's `Missions/` copies are read first, so they can be edited without a build.

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
  "pacing": { "hulls": ["Tamsin", "Lector"], "quietRange": 2500, "ranges": ["Small Mercy | Archon"] },  // ranges: distances logged each second
  "linesPending": true,                                   // every radio line is a draft awaiting the user's approval (see Messages)
  "scenery": { "rocks": [ ... ], "fields": [ ... ] },     // drawn only: ice and rock (see Scenery)
  "ships": [ ... ], "wings": [ ... ], "objectives": [ ... ], "messages": [ ... ], "events": [ ... ],
  "autoplay": { "seconds": 900, "expect": { ... } } }
```
Any field ending in `Why`, and `about` and `note`, is a comment: say where a number came from. The HUD's fonts have every printable ASCII character (and a few symbols),
so text the player sees can use mixed case and any punctuation; `-mnHudSelfTest` checks every character in the mission files has a glyph.

**Ships** (single ships) and **wings** (groups whose ships are named `<wing> 1`, `<wing> 2`... across all waves):
```json
{ "name": "Warden", "class": "earth_corvette", "team": "earth", "hullScale": 0.5, "hull": 100, "selfHeal": false, "invulnerable": false,
  "ai": "drone", "role": "fighter", "wingman": false, "ammo": { "torpedo_heavy": 2 },
  "capital": { "standoff": 2700, "band": 400, "bearing": 65, "throttle": 0.3, "turretProfile": "earth_turret", "turretWaitScale": 1.5,
               "beamWaitScale": 4, "holdBeams": false, "holdTurrets": false, "stationary": false, "weapons": { "SUB_aaf_F01": "beam_slash" } },
  "look": { "hull": { "length": 460, "width": 110, "height": 90, "seed": 23, "offset": [0, 0, 0] }, "rock": { "radius": 420, "seed": 7, "stretch": [1.3, 0.85, 1.1] } },
  "goal": { "order": "attack", "target": "Small Mercy" }, "fallback": { "order": "guard", "target": "Sable 1" },
  "arrival": { "cue": { "destroyed": "Choir", "delay": 6 }, "delay": 0, "at": [900, 60, 3000], "rot": [0, -115, 0], "speed": 0.3, "effect": "jump" },
  "departure": { "cue": { "hull": "Warden", "below": 20 }, "to": [0, 0, 9000], "jumpAfter": 3 } }

{ "name": "Choir", "team": "earth", "capped": true, "capPriority": 0, "waves": 1, "waveThreshold": 0, "waveDelay": 5, "wingmen": false,
  "ships": [ { "class": "earth_interceptor", "ai": "drone", "count": 3 },
             { "class": "earth_fighter", "ai": "drone", "count": 1, "goal": { "order": "attack", "target": "Small Mercy" } } ],
  "goal": { "order": "engage" },
  "arrival": { "cue": false, "at": [-2400, 300, 2600], "facing": "Small Mercy", "formation": "v", "spacing": [45, 8, 40], "speed": 0.9 } }
```
- `hull` is the hull at arrival in percent (a damaged freighter); `hullScale` scales the class's hull. `ai` is an AI profile (`Resources/AI/ai.json`;
  default `drone` for hostiles, `wingman` for ours); `role: "bomber"` flies torpedo runs. A big ship (a corvette or capital class) gets a capital
  helm and gunners from `capital` (or the defaults). `wingman: true` puts a ship under the quick menu's orders. A class with a Blender model in
  `Resources/ShipArt` uses it; any other big ship gets a placeholder hull (`look.hull`, else the class's size), moved by `look.hull.offset`;
  `look.rock` adds a rock nucleus (an icy asteroid of that radius, `stretch` per axis) with the hull on it: a station dug into a comet. The
  look is only the placeholder: a model of the class in `ShipArt` replaces it whole, with no change to the mission (address parts by `partKind`,
  not by a placeholder's part names). `holdTurrets: true` keeps every gun and flak turret silent until the `turrets` action wakes them (a
  station that has not been alerted); beams follow `holdBeams`.
- **Arrival**: no `cue` (or `true`) is present at the start, without a flash; a condition arrives when it holds, plus `delay`, with the jump flash
  (`effect: "none"` for none); `false` arrives only through an `arrive` action. Where: `at` (a point), `near` a ship (with `offset` in its frame, or
  `distance` in a direction fixed by the name), or `bay` (out of a ship: from its first living part of kind `hangar`, pointing out along the
  part's normal, without a jump flash; a ship that has hangar parts and has lost them all launches nothing: the arrival is scrubbed, counted as
  arrived and destroyed at once so wing rules still add up, and logged as `scrubbed`; a ship with no hangar part launches from its hull). Facing: `facing` (a ship, or a direction), `rot` (Euler degrees), else the
  anchor ship's heading. A wing's ships take places in `formation` (`v`, `line`, `column`, `none`) at `spacing`, or exact `offsets`.
  `speed` is a share of top speed at arrival, `throttle` defaults to it.
- **Waves** (FS2's): a wing with `waves: 3` sends its next wave `waveDelay` seconds after the current one is down to `waveThreshold` ships.
- **The arrival cap**: a `capped` wing of the cap's team queues its ships while the team's capped fighters alive are at the cap; they come in,
  oldest first, as room is made (the dogfight's "never more than N at once"). `capPriority` (a capped wing only; default 0) puts a wing's ships
  ahead of waiting ships of a lower priority: a beat's own reinforcements (the Oort destroyer's fighters, Earth's last wing) take the next free
  places before the stragglers of earlier waves, with the cap, and so the pressure, unchanged.
- **Orders** (`goal`, `fallback`, the `order` action): `attack` (with `part`: a name, or a list where the first still standing is attacked;
  or `partKind`: the target's first living part of that kind, e.g. `comms`, `hangar`, `turret`, `engine`, so a mission survives a model swap),
  `disable` (engines), `disarm` (its first weapon part), `guard`, `form` (on a leader), `engage` (free fight), `bomb` (torpedo runs),
  `waypoints` (`path`, `speed`, `then`: `stop`, `loop` or `depart`), `depart` (`to`). A target may be a wing: ship *k* of the ordered wing takes
  ship *k* of the target wing. `fallback` is what the ship goes back to when an order is done; guard, form, bomb and engage are their own
  fallback unless one is given. An order a ship is already carrying out changes nothing.

**Objectives**: `{ "id": "warden", "kind": "primary" | "secondary" | "bonus", "text": "Destroy the Warden", "hidden": false, "complete": {cond}, "fail": {cond} }`.
Checked every tick until settled (also by the `objective` action); a hidden bonus shows once met.

**Messages**: `{ "id": "bombers", "from": "Small Mercy", "fallbackFrom": "#EARTH", "text": "...", "priority": "high", "intensity": "frantic", "pending": true, "voice": "vo/mercy_04.wav", "seconds": 4 }`.
`from` is a ship, `"<any wingman>"` (a living wingman, else the player) or `"#Name"` (off the battlefield). A message from a ship that is gone
is dropped unless `fallbackFrom` can say it. One line at a time on the channel: high priority first, low dropped when busy. `voice` is the slot for
the recording (raised as `radio_voice`; nothing plays it yet). `intensity` (`calm`, `tense`, `frantic`) is how the line is delivered when it is
voiced (the voice pipeline's direction and radio filter; Earth's voices are always calm).

**Lines pending the user's approval** (design: the dialogue process; no voice is made for a line the user has not approved): `"pending": true`
on a message or an inline `radio` / `callOut`, or `"linesPending": true` on the mission for every line in it (a line's own `"pending": false`
overrides it). A pending line plays as on-screen radio text with the squelch. The voice-coverage checks (`-mnMissionSelfTest`,
`-mnAudioSelfTest`) count it as pending instead of failing; its speaker needs a voice in `audio_src/voice/cast.json` (a speaker new to the
voice index is counted as pending too until the next voice run). `audio_src/voice/collect_lines.py` skips it, so `tools/audio/voice.ps1` never
voices it, and `tools/dialogue/export_sheet.py` marks it "pending approval" in the spreadsheet. To approve: set `"pending": false` on the
line (or drop `linesPending`), then run `tools\audio\voice.ps1` and rebuild.

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

**Scenery** (drawn only: nothing collides with it, so keep the lanes ships fly clear):
```json
"scenery": { "rocks": [ { "at": [-5200, 1300, 6200], "radius": 1400, "seed": 3, "stretch": [1.3, 0.8, 1.1], "spin": 0.05 } ],
             "fields": [ { "centre": [400, 0, 3600], "size": [9000, 2600, 11000], "count": 160, "seed": 11, "radiusMin": 4, "radiusMax": 160,
                           "spin": 1.2, "clear": [[0, 0, 0, 500], [700, 300, 7200, 1300]] } ] }
```
Single rocks, and fields of `count` rocks scattered through a box with sizes on a power law (many small, few large), kept out of each `clear`
sphere (`[x, y, z, radius]`). `spin` is degrees per second at the largest size (smaller ones tumble faster). The rocks are icy asteroids
(`Game/Mission/IceRocks`: a displaced, cratered sphere in dark crust with ice in the cracks, a copy of the flight's plated material).

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
| `depart` | `{ "do": "depart", "ship": "Tamsin", "to": [0, 0, 9000], "jumpAfter": 3 }`, `"bay": "Mercy"` | flies out and jumps (`jump_out`), or docks; ships of it not yet arrived (later waves, ones the cap holds back) are stood down: they never come, read as departed and never as arrived |
| `radio` | `{ "do": "radio", "message": "bombers" }`, or inline `{ "do": "radio", "from": "#Command", "text": "...", "priority": "high" }` | queues a message |
| `objective` | `{ "do": "objective", "id": "bombers", "set": "complete" }` | `complete`, `failed`, `pending`, `show`, `hide` |
| `order` | `{ "do": "order", "ship": "Vigil", "order": "guard", "target": "Censer", "standing": true }` | an AI order to a ship or every ship of a wing (standing: also its fallback) |
| `beams` | `{ "do": "beams", "ship": "Small Mercy", "free": true }`, `"target": "Archon"` | FS2's beam-free / beam-lock; with a `target`, FS2's beam-fire: the ship's beams (and its helm) go for that ship first whenever it is alive, in reach and in a beam turret's arc, and for the nearest enemy capital otherwise |
| `turrets` | `{ "do": "turrets", "ship": "Oculus", "fire": true }` | a capital's guns and flak wake (or hold; beams follow `beams`) |
| `music` | `{ "do": "music", "state": "battle3", "for": 30 }`, `{ "do": "music", "auto": true }`, `{ "do": "music", "sting": "enemy_arrival" }` | the music state (`ambient`, `battle1..3`, `victory`, `failure`, `death`) |
| `theme` | `{ "do": "theme", "name": "lector" }` fades a theme track in over the base score; `{ "do": "theme", "name": "lector", "on": false }` fades it out. Optional: `fadeIn`, `fadeOut` (seconds), `priority` (the higher one plays when two are on), `volume` (dB), `mode` (`duck`: the base score drops under it; `replace`: the base falls silent), `duck` (dB) | the music's **second layer** (below). Built-in themes: `lector` (Earth's picket corvette), `destroyer` (Earth's capital ship), `outpost` (an isolated station) |
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
`needsRepair` until the pilot has knit it (hold at the panel; the module seats itself on screen) (it comes back at 25 percent health); `callOut` puts a "hold View: stand up" prompt at the seat; `scan` sends the
light through the canopy (with the hull's creak four seconds before); `knockOut` destroys the part for the cabin to hold; `cabinLights` sets the
glow. A mission never needs the cabin: with `-mnNoCabin`, in an AI-flown run, or from a mission with no use for it, `CabinStub` answers (always
seated and powered, a scan detects a powered player in range, a knock-out destroys the part and the AI flying "repairs" it after 8 s).
`Resources/Missions/cabin.mission.json` ("Dead Water") is the test: quiet drift, a call to go dark, the Lector's sweep that misses a dark ship
and finds a powered one, a hit that takes the port engine while drones come in, a briefing at the plan table and the loadout at the rack.

## Music, juice, HUD

**Themes (the music's second layer).** The base score (`music` actions and `auto`) follows the fight: ambient, battle 1 to 3. A mission moment can lay a *theme* over it: `{ "do": "theme", "name": "lector" }` in the event that
brings the Lector in, and `{ "do": "theme", "name": "lector", "on": false }` in the event that ends it (the Lector destroyed or gone). The theme fades in (seconds set per theme, `fadeIn` here overrides), takes over (the base keeps playing
underneath, ducked or, with `"mode": "replace"`, silent), and fades back to the base when it goes off. Two themes on at once: the higher `priority` plays, the other waits and comes back when it ends. A theme stops by itself when the
mission ends or starts again. The name is letters, digits and underscores. The audio finds the sound by `"<mission id>/<name>"` then `"<name>"` in the player's `music.json` (their own MP3, WAV or OGG, see `tools/audio/MUSIC.txt`), and
falls back to the synthesised theme of that name if there is one (`lector`, `destroyer`, `outpost`); a theme nobody has a sound for plays nothing and is logged once. An event can switch several: use one `theme` action per theme.
So a mission needs no music file to use a theme, and the player can give any of them a track.

The runtime only publishes: `music_state` / `music_sting` and `GameWorld.Mission.Music` (the audio system will play them; auto follows how
many hostiles are close or on the player), `jump_in` / `jump_out` (`Juice/Fx/Jumps` draws them: a scene attaches it to its juice director with
`Jumps.Attach`, the mission scene does), `radio_line` (the HUD shows it), the directives and
objectives on `GameWorld.Mission` (the HUD draws the directives; the scene shows the briefing, the objectives on pause and the debrief on its overlay screen).
Event ids and fields: `Core/MissionBoard.cs`.

## Checks

- `-mnMissionSelfTest`: every built-in mission loads and a broken one is refused with each problem named; every condition and action on a scripted
  world (`Resources/MissionTests/selftest.mission.json`: the test kills ships and parts at set times and checks the tick each event fires);
  each built-in mission runs a minute headless; the dogfight's port matches the hand-written dogfight to the bit for 240 s.
- `-mnMissionAutoPlay <mission> -mnOut dir [-mnSeconds n] [-mnDifficulty x] [-mnMortal] [-mnSeed n]`: the AI flies the player (invulnerable unless the
  mission's `autoplay.invulnerable` is false) until the end; then the mission's `autoplay.expect` is asserted: `result` (any of), `fired`,
  `notFired`, `firedIf` (`[ { "if": "Sable 2 lost", "then": "Lantern here", "within": 10 } ]`: a beat that answers something else, like a relay that comes
  after a loss: when `if` fired and the mission ran on `within` seconds more, `then` must have fired by then; if `if` never fired nothing is owed, so the
  rule holds at every level and every seed, where a plain `fired` would demand the loss), `order` (the ones that fired, in that order), `longestQuietMax`,
  `maxEngagingMax` (a number, or one per difficulty level: the AI's own cap on the player is `[2, 3, 4, 5, 99]`), `maxThreatsMax`,
  `threatsAvgMin`; plus no errors, no bad numbers, no stuck AI. Writes `events.csv`, `pacing.csv` (with the `pacing.ranges` distances), `autoplay.txt`,
  `autoplay.json`, `radio.txt`; the report ends with the `pacing.hulls` ships' fates, lowest hulls, hull damage by source and every part they lost (when,
  to whom, with what). `-mnGoDark`: the stub goes dark for every scan sweep (a player who obeys the call), so a mission's unseen path can be run.
  `-mnSeed n` salts every random seed of the sim (weapons, turret gunners, AI pilots; `Core/Sim.cs` `SimSeed`), so one mission can be played with many
  different dice: 0, the default, is the same run as before it existed. The Oort round 2 sweeps ran 30 seeds a level this way.
  `tools\mission_sweep.ps1 -Exe <exe> -Mission escort -Levels "Very easy,Easy,Medium,Hard,Insane" -Seeds 30 -Out dir` runs every level x seed in parallel
  (`-Mission` can be a path to a mission file, to try an edit without a build) and prints, per level, the passes out of the runs, the length, the longest quiet,
  the most hostiles on the player, whether the `pacing.hulls` ship survived, and each failed check with its seeds (`tools\mission_sweep_summary.py dir`).
- `-mnDogfightPortCheck seconds -mnOut dir [-mnDifficulty x]`: `DogfightBattle` against `dogfight.mission.json`, every ship's state compared
  once a second.
