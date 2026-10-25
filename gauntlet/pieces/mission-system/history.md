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

## Round 2: the escort regression (builder, 2026-10-24)

The lead's check on `Playtest\Monolith_2026-10-24_death` failed the escort's event order: "Opening 2.0, Matins in 58.3, Lauds in 106.9, Prime in
261.5, Terce in -, Spooling 259.9, Tamsin away 283.9". The freighter spooled before Prime and Terce never came; the mission still ended in a victory
with its climax gone.

### The cause
The escort ran on two clocks that nothing tied together: Earth's waves advance on kills (the two-thirds rule), but the climax fired on the
freighter's travel alone (within 300 m of the spool point). In round 1 they lined up only because the Tamsin's engines got shot up and it limped
in at 459 s. After AI round 2 (turret fire, wingman survival) and Earth's real fighters (c7dd6e5, afe0002), its engines are no longer hit, so it
arrives at full speed at 260 s; and kills are slower: two free-roaming Lauds drones drifted 0.5 to 1.5 km off and lived 100 s, so the
two-thirds rule on a four-ship wing (three kills) stalled from 156 s to 256 s. The boost was not the cause (the freighter's speed is its own).
A baseline of the old file on the new build at every level showed it was hollow everywhere, not only on Medium: on Very easy and Easy Matins was
never two-thirds destroyed, so Lauds never came and the freighter sailed through after one wave; on Hard the freighter died; on Insane two waves.

### The fix (mission data only; no code)
- **Waves:** FS2's two-thirds rule first, plus FS2's usual time-out: the next wave 5 s after the current one is two-thirds destroyed, or 75 s
  after it was called (from the call, not the arrival, so the arrival cap holding a wave back cannot stall the next).
- **The climax follows the fight:** the Tamsin stops its waypoints short as before; reaching the jump point ("Holding") it creeps on with its
  drive cold instead of sailing past. Its arrival there is Earth's cue for a final push: Terce's second wave jumps in at once if the wave rule
  has not sent it. The drive spools 8 s after that second wave is in (a safety net 120 s after Terce's first). Terce, Earth's last throw, is no
  longer held back by the arrival cap (behind a cap full of survivors it waited minutes).
- **Bombers:** still called by the Lector's hull dropping below 70% (Thurible) and 40% (Incense). Left alone, the Lector also throws Thurible
  in when the Tamsin reaches the jump point, so the bombers are a beat of every crossing (in its climax), not only of runs where the escort
  happens to go for the corvette.
- **The freighter survives one bomber wave:** six times a Triton's hull (21,000 at its 70% start) against two torpedoes (13,600). Difficulty
  scales only the player's damage, so before this two torpedoes killed it even on Very easy.
- **The auto-play's plan flies like an escort:** bombers, or two or more fighters within a kilometre of the Tamsin, first; the Lector when the
  freighter is clear; otherwise stay on the Tamsin. (Round 1's plan went for the Lector first, which left the freighter alone once waves kept
  coming.)
- Two new radio lines (the Tamsin holding, Earth's last throw), voiced with the audio pipeline (three clips; the other 169 untouched).

### The escort now (AI flying an invulnerable player; `gauntlet/captures/mission-system/r2/planE/`, `final/`)
| Level | Result | Order |
|---|---|---|
| Very easy | victory 315 s | Matins 58, Lauds 133, Prime 208, Terce 283, Terce's second wave 283, Spooling 291, away 315 |
| Easy | victory 315 s | the same as Very easy |
| Medium | victory 292 s | Matins 58, Lauds 107, Prime 182, Terce 257, second wave 260, Spooling 268, away 292 |
| Hard | victory 293 s | Matins 58, Lauds 133, Prime 208, Terce 261, second wave 261, Spooling 269, away 293 |
| Insane | victory 293 s | Matins 58, Lauds 103, Prime 178, Terce 253, second wave 261, Spooling 269, away 293 |
Thurible comes at the climax (about 259 s) on every level; Lantern on Medium and up, Wick on Insane (the relay is conditional on losses, so
the easy levels never need it). The Medium expectations pass in full. Very easy and Easy fail only "Lantern here" (no loss ever calls the
relay), Insane only "at most 4 on the player" (Insane has no attacker cap): both expected; the expectations are Medium's.

### Playing it with the boost (235 m/s), not yet fixed
- Distances collapse: a wave arriving 2 to 2.5 km from the route is reached in about 10 s and the Lector, 1.8 km off, in 8 s, so the escort's
  pull between the freighter and the corvette is weak; the player can visit every threat and be back before it matters. Levers, all data:
  arrivals 4 to 6 km out and on opposite sides at once, the Lector's standoff at 3 to 4 km, the bombers launching from farther.
- Torpedoes (65 m/s) are easy prey at boost speed; the bombers' threat to the freighter is mostly the AI escort's problem, not the player's.
- The 58 s quiet opening is long when the ship crosses a kilometre in four seconds.
- The AI flying the player gets few kills (0 to 5) under the escort plan; the auto-play shows the beats fire and resolve, not the balance.
- The hidden bonus ("without the Lector finding you") fails in every auto-play: the AI never goes dark. Expected; the real cabin makes it
  possible by hand.

## Round 3: the escort at every level (builder, 2026-10-25)

Worktree `escort-wt` from main at 9deb7eb. The brief: the escort's auto-play passed on Medium and failed on the other levels: "Lantern here" at Very easy and
Easy, 11 Earth fighters on the player at Hard. Its expectations were written for Medium. For each failure: is the expectation wrong (a), or is the mission or the AI
at fault (b)? Measured, not judged: every number below is the AI flying an invulnerable player, and the sweeps are **100 seeds a level** with `-mnSeed n` (30 for the first
look, `captures/mission-system/r3/base30`; 100 for before and after, `base100` and `final100`), because one run a level would have said "Medium is fine". New tools:
`tools\mission_sweep.ps1` runs a mission at every level and seed in parallel (500 escort runs in 100 s) and `tools\mission_sweep_summary.py` prints, per level, the passes,
the length, the longest quiet, the most hostiles on the player, whether the Tamsin lives, and each failed check with its seeds.

### 1. "Lantern here" at Very easy and Easy: (a), an expectation that was wrong, and wrong at Medium too
The relay is written to answer a loss: Lantern jumps in 6 s after Sable 2 dies, or when the Tamsin falls below 45%; Wick 8 s after Lantern is lost. The briefing says
so ("on call, one after the other, if you lose ground") and so does the design (section 7: allied ships arrive in a relay as earlier ones die). Over the first 150
runs Lantern came in exactly the runs where one trigger held, and in none of the others:

| 30 runs a level | Sable 2 lost | the Tamsin's lowest hull | Lantern came |
|---|---|---|---|
| Very easy | 0 | 49 to 65% (never under 45) | 0 |
| Easy | 0 | 35 to 56% | 17: every run that dipped under 45% (34.7 to 44.7), none of the 13 that stayed over (45.1 to 56.3) |
| Medium | 1 | 13 to 50% | 26: the same split (the 4 without: 45.1, 46.4, 47.8, 47.9%) |
| Hard | 27 | 0 to 48% | 30 |
| Insane | 30 | 0 to 36% | 30 |

So "Lantern here" was never a beat of every run, and Medium passed it only because seed 0 happens to hurt the freighter (over 100 seeds Medium has no Lantern in 10).
A per-level list would not have fixed it: Medium is flaky too. Nor is it a broken mission for an Easy player: when a trigger holds the relay comes at Very easy and Easy.
Proof: the same file with the Tamsin starting at 40% (the second trigger true at once) gives Lantern in 10 of 10 runs at Very easy and 10 of 10 at Easy
(`captures/mission-system/r3/relay_tamsin`); the Sable 2 trigger is exercised in 58 of the 150 runs, Lantern exactly 6.0 s after in 53 of them and sooner in the other
five (the Tamsin's hull got there first). (A try with Sable 2 at 2% hull did not kill it at Very easy or Easy: the wingmen's mercy, `wingDamageScale` 0.25 and 0.5, and
their courage keep it alive.)

**Fix, in the check:** `autoplay.expect.firedIf` (new, `MissionFile.FiredIfDef`; in the Mission README): `{ "if": "Sable 2 lost", "then": "Lantern here", "within": 10 }`
means "if `if` fired and the mission ran on `within` seconds more, `then` must have fired by then; if it never fired nothing is owed". The escort now says that for
Sable 2's loss, for the Tamsin falling under 45% (a new bookkeeping event, "Tamsin low", with no actions) and Lantern, and for Lantern's loss (another, "Lantern lost") and
Wick. The relay's wiring is asserted at every level and every seed, and "Lantern here" is out of `fired`. The self-test has two new checks (the rule and its edge cases,
an unknown event refused).

### 2. 11 Earth fighters on the player at Hard: (b), the mission's ending, not the fight
Hard's cap on the player is 5 (`maxPlayerAttackers` `[2, 3, 4, 5, 99]`). Splitting every run's pacing at the moment the Tamsin is gone: **before** it jumps or dies, the most on
the player was at most 5 in all 30 Hard runs (Easy 3, Medium 4, as the caps say); the 10, 11 and 12 (11 of 30 runs; Insane 12 to 15 in 9 runs; one Very easy run 3) all came in
the seconds after it had jumped, in the four seconds before "Tamsin away" ends the mission. The cause: with the Tamsin gone, every drone that was on it has to choose again, nothing
else of ours is left in the fight (in the Hard run looked at, Sable 2 and both relay flights were dead by then), and the AI's cap gives way when the player is the only target there is
(`AiPilot.BestTarget`: the nearest other enemy anywhere before one too many on the player, else the capped player). Fifteen drones, one target. That is real, if brief: a human
playing Hard sees the freighter jump and eleven fighters turn on them.
**Fix, mission data:** a new event, "Earth breaks off", on the Tamsin having departed: every Earth wing departs (`jumpAfter` 3, as the Oculus' "Earth withdraws"; wings not yet in are
stood down), so the drones jump out as the freighter does and the pile-on never forms. Nothing before the jump changes: 149 of the 150 runs of the first sweep are identical to the
hundredth of a second, event by event, up to the Tamsin leaving (the 150th is the Thurible fix below), and every run ends at the same time. `maxEngagingMax` is now per level,
`[2, 3, 4, 5, 99]` (the AI's own cap, as the Oculus), not Medium's 4 for every level. Hard's most on the player: 13 -> 5.

### 3. Found on the way
- **No bombers in 3 of 100 Medium runs (b), fixed.** The Thurible bombers were meant to be a beat of every crossing (the Lector throws them in when the Tamsin reaches the jump point,
  "Holding"). When Earth's last wing was already in, "Spooling" fired in the same tick the Tamsin arrived and "Holding" (which says "and not Spooling") never did, so a Medium run could cross
  without a bomber. The cue is now Holding or Spooling. Thurible in: Medium 97 -> 100 of 100 (they then have the 20 s of the spool).
- **Very easy, one run in 100: 3 on the player against a cap of 2.** Not the AI's cap, which held at 2: an Acolyte heavy escorting the bombers had dropped the player as its target (its pilot's
  target was none), but `ShipState.Target` is only written, never cleared, when a pilot has no target (`TargetingSystem`: a `TargetShip` of 0 means "leave it"), and the pacing counts
  `Ship.Target`. A lag in a measure, and a stale target that the weapons read too. Left as it is: clearing it is the targeting and AI builders' call and moves every AI number measured so far.
- **Quiet stretches of 31, 35 and 38 s (Medium, 3 runs in 100) and 36 s (Very easy, 1), left.** All at 80 to 130 s, between the first wave's last stragglers (1 to 3 ships, 2.5 to 7.6 km from
  the player, who is guarding the freighter while they fight Sable 2 far off) and the next arrival. The lever is Lauds' time-out (70 s from Matins; 55 s would end these by about 113 s but
  moves everything after it).
- **The freighter on Hard: lost in 18 of 100 runs** (at 252 s on average; Medium 1 of 100, Very easy and Easy 0, Insane 92). Always to fighter guns (100% of the hull damage) with the Lector at 100%:
  the Compline heavies and the Lauds and Prime attackers that the AI escort (2.0 kills a run on Hard) does not stop; 5 of the 82 survivors end under 5% hull. Not changed: it is a balance call and
  these are only an AI pilot's numbers. The lever is the Tamsin's `hullScale` (6 now), measured over 100 seeds: 7 gives Hard 90 of 100, 8 gives 98 of 100 (Medium 100 of 100 either way, its lowest hull
  mean 38% and 40% against 35%); the cost is that the freighter reads as tougher, and at 8 it lives through two bomber waves (its comment says it should not).
- **Insane** (not tuned): 8 of 100. The Tamsin is lost in 92 (at 243 s on average), so "Spooling" is reached in 34 and the bombers in 40; no cap on the player, 3 to 10 attackers.

### The escort by level (100 seeds each, the AI flying an invulnerable player; `captures/mission-system/r3/base100` and `final100`)
| Level | passes before -> after | length mean (min to max) | longest quiet mean (max) | most on the player mean (max) | the Tamsin lives | Lantern / Wick came |
|---|---|---|---|---|---|---|
| Very easy | 0 -> **98** | 301 s (291 to 315) | 2.7 s (36) | 2.0 (3) | 100 of 100 | 0 / 0 |
| Easy | 53 -> **100** | 296 s (291 to 315) | 3.0 s (24) | 2.8 (3) | 100 of 100 | 53 / 0 |
| Medium | 83 -> **96** | 295 s (278 to 315) | 4.4 s (38) | 2.8 (4) | 99 of 100 | 90 / 0 |
| Hard | 19 -> **82** | 296 s (225 to 315) | 1.3 s (17) | 4.3 (5; was 13) | 82 of 100 | 100 / 52 |
| Insane | 0 -> 8 | 247 s (199 to 315) | 0.3 s (4) | 4.5 (10; no cap) | 8 of 100 | 100 / 94 |

What still fails: Very easy 2 (one 36 s quiet, one 3 on the player); Medium 4 (three quiet stretches of 31 to 38 s, one lost freighter); Hard 18 and Insane 92 (the freighter lost).
`tools\mission.ps1 -Exe` at seed 0: the escort passes at Very easy (305.6 s, 2 on the player), Easy (290.8 s, 3), Medium (296.3 s, 3) and Hard (315.3 s, 5), the Tamsin jumping out in all four;
Insane loses it at 268.6 s.

### Checks (this worktree's build)
`tools\mission.ps1 -Exe` at Very easy, Easy, Medium and Hard, all four missions, seed 0: **16 of 16 PASS**. Nothing that passed on main fails, and every number but the escort's is the
one the Oculus round 2 recorded (Very easy / Easy / Medium / Hard: oort 653.8 / 630.2 / 671.4 / 692.2 s; dogfight 427.4 / 381.9 / 410.6 s, and on Hard the Mercy lost at 459.5 s, as before;
cabin 120.2 / 140.6 / 142.5 / 127.6 s). The escort, which failed on main at Very easy and Easy ("Lantern here") and at Hard (11 on the player), passes. Mission self-test **96 of 96**
(94 + the 2 above). Port check SAME at Very easy, Easy, Medium and Hard (900 s each, every ship to the bit, timelines identical). AI 39 of 39, combat 115 of 115, HUD 125 of 125, start screen
109 of 109, cabin 75 of 75, audio PASS (150 checks; no radio line added, no voice made).

### Known gaps
- The Hard freighter (18 of 100) and the 31 to 38 s quiet stretches (4 of 400 runs at the four easier levels) are balance, with the levers above; the stale `ShipState.Target` is the AI builders'.
- The Lector does not leave with Earth's fighters; the mission ends four seconds after the jump anyway.
- A player who goes dark never meets Compline: the auto-play's stub is always powered, and `-mnGoDark` (the unseen path) was not swept here.
- Not tested by hand. Every line is still a draft pending the user's approval; no line was added.

### How to run
- Sweep: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\mission_sweep.ps1 -Exe <exe> -Mission escort -Levels "Very easy,Easy,Medium,Hard,Insane" -Seeds 100 -Out dir`
  (`-Mission` takes a path, so an edited file can be tried without a build; `python -I tools\mission_sweep_summary.py dir --events "Lantern here,Wick here"` counts the runs that fired each event).
- Play: `Playtest\Monolith_2026-10-25_escort\Play.bat escort hard` (ESCORT.txt says what to look for).
