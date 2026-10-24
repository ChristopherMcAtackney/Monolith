# Piece: the mission and event system

The brief: make missions data, not code, scripted the way FS2's are (reactive triggers, almost never plain timers), so the slice's Oort
Cloud mission and every mission after it are files (design sections 3, 5, 6, 7, 9, 11 item 5, the appendix's "Mission structure" and
"Music states"). Code: `Scripts/Mission` (its own assembly, Core only; `Scripts/Mission/README.md` has the format and every condition and
action), the game's side in `Scripts/Game/Mission`, the jump flash in `Scripts/Juice/Fx/Jumps.cs`. Data: `Resources/Missions/*.mission.json`.
The user judges by hand: `Playtest\Monolith_2026-10-24_missions\` (`Play.bat escort`, `Play.bat dogfight`).

## Round 1 (builder, 2026-10-24)

### What was built
- **The format** (`monolith-mission/1`, one JSON file per mission): the cast (ships, and wings of groups with waves and a wave threshold,
  AI profiles and roles, loadouts, a starting hull, capital orders, goals and fallbacks, arrival cues with a delay and a place: a point,
  near a ship at an offset or a distance, or out of a ship's bay, facing a ship, in a formation; departure cues), the arrival cap per
  difficulty, objectives (primary, secondary, bonus; hidden until met), events (a condition tree, a delay, repeat count and interval,
  chaining with a delay, failWhen, a directive), radio messages (speaker, fallback speaker, text, priority, a voice-clip slot), the briefing
  (stages and TIE Fighter's questions), debrief stages with conditions, music (start state, auto, track names per state), the pacing hulls,
  and an `autoplay` block (what the auto-play check expects). Misspelt fields, unknown names, conditions and actions are refused, all of them
  at once, with where they are; fields ending in "Why" are comments for sources.
- **24 conditions**: and, or, not, time, arrived (all of a wing's first wave, or any), destroyed, departed, disabled, disarmed (each with a
  delay; a wing means every ship of every wave), destroyedPercent, hull below/above, subsystem (a part, several parts' mean, a kind; below,
  above, or all destroyed), range (ship to ship or point), hostilesNear, teamAlive, event (fired, failed or pending, with a delay),
  objective state, beaming, autopilot, difficultyAtLeast, and the cabin's four: seated, powered, detected (by a scan from a ship), needsRepair.
- **15 actions**: arrive (a ship or a wing's next wave, with a jump flash and sound through the juice), depart (fly out and jump, or dock),
  radio, objective (complete, fail, pending, show, hide), order (attack with a part list, disable, disarm, guard, form, engage, bomb,
  waypoints then stop/loop/depart, depart; to a ship or a whole wing; standing or not), beams (free or hold), music (a state, for a while or
  until told, auto, a sting), end (victory, defeat, neutral, with a banner), cond (FS2's), invulnerable, log, and the cabin's four: scan,
  callOut, knockOut, cabinLights.
- **The runtime** (`MissionRuntime`, the sim at order Mission, after weapons and damage): each tick it lets in capped arrivals that fit,
  jumps departing ships, runs arrival and departure cues and wing waves, settles objectives, ends on the player's death, evaluates every
  event in file order, plays the radio queue (one line at a time; high first, low dropped when busy; a dead ship's line goes to its fallback
  or is dropped), and steps the cabin. Once a second: the pacing sample (threats alive, hostiles on the player, the nearest hostile, quiet,
  hulls, the music state) and the music (auto: battle 1, 2, 3 by how many hostiles are close or on the player, back to ambient after 8 quiet
  seconds; stings on arrivals and objectives; victory, failure, death at the end). It publishes the directives, objectives, music and result
  on `GameWorld.Mission`; `CombatHud` draws the directives (left, under the info lines) and the cabin's prompt; the scene shows the briefing,
  the objectives on pause and the debrief. It writes `events.csv` (every arrival, death, departure, event, objective, radio line, music
  change, cabin note) and `pacing.csv`.
- **The game's side** (`GameMissionHost`): spawns a ship with flight, the combat rig, its look (a Blender model when ShipArt has one, else a
  placeholder hull or fighter) and its pilot (the AI's fighter brain, a capital helm and gunners, the autopilot for the player in checks),
  sets orders (idempotent: an order a ship is already carrying out changes nothing), flies ships out (`FlyOutPilot`) and along waypoints
  (`WaypointPilot`), frees and holds beams, destroys and gives back parts for the cabin stub. Each spawn makes the same calls in the same
  order as the dogfight's own code.
- **The cabin is stubbed** (`ICabin`, `CabinStub`): always seated and powered; a scan detects the powered player if in range during the
  sweep; a knock-out destroys the part and it needs a hand repair until repaired (headless runs with the AI flying repair it after 8 s);
  lighting and the call-out prompt are recorded and raised as events. The real cabin passes its own `ICabin` to the runtime.
- **The jump flash** (`Juice/Fx/Jumps`, attached by the mission scene; `JuiceDirector` is untouched): a soft blown-out cold core, a long thin
  anamorphic streak, a small haze, a light on nearby hulls, the sound and a little shake, sized by the ship. A first look for the juice's
  sandbox to tune (an earlier ring version read as soap bubbles and was dropped).
- **The runner**: `-mnMission <name or path>` plays any mission (briefing, pause, debrief, fly again); a capture plan runs one with
  `"scene": "mission"`; `-mnMissionAutoPlay <name>` runs one headless with the AI flying the player and asserts its event log; `tools/mission.ps1`
  runs the self-test and every mission's auto-play. The playtest folder's `Missions/` files are read before the built-in ones, so a mission can
  be edited and played again without a build.

### The dogfight, ported (`Resources/Missions/dogfight.mission.json`)
The same ships at the same places, the six waves on FS2's two-thirds rule under the per-level cap (capped wings), the bomber wing and its
escort twenty seconds after the third wave is called, the beam-lock freed when the Warden's beam turrets fall, the win and the two losses, the
radio lines, and the AI-flown player's plan for soaks (a repeating `autopilot` event with FS2's `cond`). New on top, reading the fight and
changing nothing in it: objectives, directives, the briefing and debrief, the music.

**It plays the same, to the bit.** `-mnDogfightPortCheck` runs the hand-written `DogfightBattle` and the mission file on fresh worlds with the
AI flying an invulnerable player (as the soak does) and compares every ship's id, name, alive, hull, shield, position, velocity and rotation,
bit for bit, once a second, plus the spawn and death timeline:

| Run | Hand-written | Mission file | Samples identical | Timeline |
|---|---|---|---|---|
| Medium, 1,500 s | lost ("SMALL MERCY LOST") at 867.9 s | defeat at 867.9 s | 1,500 of 1,500 | identical, 53 entries |
| Easy, 1,500 s | won ("AREA CLEAR") at 823.2 s | victory at 823.2 s | 1,500 of 1,500 | identical, 72 entries |

The Medium loss at 868 s, wave 5, is the AI round's own first soak loop. The self-test repeats the check for 240 s on every run.

### The second mission, from data alone (`Resources/Missions/escort.mission.json`, "The Crossing")
Quality bar 5. The commit that adds it holds no code: the mission file and a freighter class (`diaspora_freighter`, FS2's GTFR Triton
numbers, five of them checked by `tools/combat/check_weapons.py`, now 143 of 143). Escort the Tamsin, a freighter starting at 70% hull,
along waypoints to a jump point: a quiet opening that turns when the freighter is a kilometre and a half along (a range trigger); Earth's
waves on the two-thirds rule under a smaller cap, the last one a two-wave wing; the picket corvette Lector arriving on a range trigger,
sweeping for life signs (the cabin's scan: being seen calls two heavies, the "Compline" wing on a detected cue); bomber waves on the Lector's
hull thresholds (70%, 40%: FS2's "Slaying Ravana" pattern); our flights in a relay (Lantern when Sable 2 dies or the Tamsin is below 45%,
Wick eight seconds after Lantern is lost); the freighter stopping its waypoints short and a `depart` with a twenty-second spool to the jump.
The AI-flown auto-play wins on Easy (286 s), Medium (483 s) and Hard (318 s).

Two code fixes came out of building it, each its own commit and neither a new feature: a ship that jumped out read as 0% hull and destroyed
in the hull and subsystem conditions (the "Tamsin hurt" line fired as it jumped; now false, with a self-test check), and the scene's info
line showed a departed ship as LOST (now GONE).

### Checks (the final build, on main at 879c30c plus this piece)
- `-mnMissionSelfTest`: 59 of 59. The format (every built-in mission loads; a broken mission is refused with eight problems named), every
  condition and action on a scripted world (`Resources/MissionTests/selftest.mission.json`: the test kills ships and parts at set times and
  checks the tick each event fires: time, chaining, repeats, and/or/not, failWhen, event delays, range, arrival cues with delays, the arrival
  cap and the queue, wing waves, destroyedPercent with an event delay, hull, subsystems, disabled, objectives, directives, the scan, knock-out
  and repair, cond, wing orders, standing orders, depart, waypoints then depart, teamAlive, hostilesNear, difficulty, autopilot, beaming, the
  end, the jump events, radio priority and dropping, fallback speakers, music, the debrief, the timeline), each mission a minute headless, and
  the dogfight's port for 240 s.
- `-mnMissionAutoPlay`: dogfight PASS (defeat at 867.9 s; longest quiet 3 s; threats max 11, avg 8.1; at most 4 on the player), escort PASS
  (victory at 483.0 s; longest quiet 0 s; threats max 8, avg 5.2; at most 4 on the player). Outputs in `gauntlet/captures/mission-system/r1/final/missions/`.
- `tools/flight.ps1` 35 of 35 on every class; `-mnCombatSelfTest` 90 of 90; `-mnAiSelfTest` 31 of 31.
- The playtest folder: a mission copied into its `Missions/` (and nowhere else) loads and runs; the windowed launch Play.bat makes starts the
  briefing with the pad found and no errors.

### Performance (RTX 4070 Ti, 1920x1080, cockpit and HUD, real time, the AI flying; `perf_*/`)
The mission system costs nothing measurable: the hand-written dogfight scene and the same fight as a mission, back to back and then in the
other order, gave 215 / 198 fps and then 181 / 192 fps on average (median frame 3.8 / 4.0 ms, then 4.1 / 4.2 ms; GPU 2.5-2.7 ms in all four).
The escort's busiest stretch (t=300 s: the bombers, the Lector, the relay): 200 fps average. **The 1% lows were 33 to 45 fps in every run,
both scenes alike**: two Blender jobs of the other builders were saturating the CPU through the session (the AI round measured 383 fps and a
216 fps 1% low on a quiet machine). Worth re-measuring on a quiet machine before reading anything into them.

### Captures (`gauntlet/captures/mission-system/r1/`, plans `gauntlet/shots/mission_r1_*.json`)
- `dogfight/`: the dogfight mission from the cockpit with the HUD (the opening radio, the directives "Destroy the Warden's beam turrets" and
  "Destroy Choir", the fight), and a perf run. `jump/`: Choir's three interceptors jumping in, from 300 m.
- `escort_hud/`: the whole Crossing from the cockpit every 30-40 s, the AI flying (the Lector's arrival line, the bombers called below 70%,
  the directives growing and settling, "TAMSIN AWAY"). `escort_jump/`: the Lector jumping in (the corvette silhouetted by its own flash).
  `escort_jumpout/`: the Tamsin jumping out (the flash at the frame's edge, the escorts beside it). `escort_look/`: riding behind the Tamsin
  (the spectator camera falls back to the cockpit once the ship has gone, so the jump-out needed the fixed camera).
- `port*/` and `final/`: the comparisons, the auto-play reports and timelines, the regression checks.

### Known gaps and what is weakest
- **The cabin is a stub.** Seated and powered are always true unless a test sets them, so the Lector's scan always finds the player; the
  hand repair is only "after 8 s" in headless runs. The four conditions and actions are wired and documented; the cabin piece implements `ICabin`.
- **The music only publishes states** (`music_state`, `music_sting`, the board): nothing plays yet. Voice clips are a slot (`radio_voice`).
- **The jump flash is a first look**, not judged; capital ships pop in rather than sliding out of a hole as in FS2.
- **Bay arrivals and departures** place a ship at a point by the carrier's hull (no hangar parts yet) and dock by distance.
- **The AI-flown player is a weak escort** (few kills); the auto-play proves the triggers fire and the mission resolves, not the balance.
  The escort's balance is untested by hand: the freighter's hull scale, the bombers' single torpedo each and the cap are the levers, all data.
- `DogfightScene` still runs the hand-written `DogfightBattle` (its stress mode is the perf run); retiring it for the mission file is the
  lead's call now that the two are proven identical.
- The HUD font has no question or exclamation marks, so briefing questions go without them.

### Next
The user's playtest of the escort; then the slice's Oort Cloud mission as a file (the outpost, the go-dark beat on the cabin's hook, the
comms array decision as a cond on `subsystem`), the audio system on the music states, the cabin on `ICabin`.
