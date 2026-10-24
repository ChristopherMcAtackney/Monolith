# Piece: the Oort Cloud strike ("The Oculus", `oort.mission.json`)

The brief: the vertical slice's mission (design section 3, all seven beats; sections 6, 7 and 9), built from the existing systems and whatever
art exists, with clearly named placeholders for the comet outpost and the destroyer that a real model replaces by class name without touching
the mission file. Arrivals 4 to 6 km out, sometimes on opposite sides at once; no stretch over about 30 s without a threat in combat; hostiles
on the player within the cap; 10 to 15 minutes on Medium. Radio in the crew's voice, plain and grounded (the user rewrites every line in the
dialogue spreadsheet, and **no voice is generated until the user approves the words**: every line here is a draft pending approval).
Data: `Resources/Missions/oort.mission.json`. The user plays it: `Playtest\Monolith_2026-10-24_oort\Play.bat` (starts the Oculus).

## Round 1 (builder, 2026-10-24)

### The mission as played (Medium, the AI flying the player; times from the auto-play on main with the real Mercy model)
1. **Quiet opening among drifting ice** (0:00 to 1:02). The wing drops out of its jump seven kilometres short of the outpost, in an ice field
   (160 rocks, many small, few large, four big ones in the distance; the lane to the outpost kept clear). The Mercy talks the flight in; the
   outpost is the bright point dead ahead. No hostiles.
2. **Going dark** (1:02 to 1:42). Sable 2 calls the sweep: get up and pull the breaker (a call-out with the cabin's prompt). At 1:24 the outpost
   sweeps the cloud for 14 s at 14 km. Missed: Sable 2 says stay dark, the outpost reads ice, the hidden bonus is won, the outpost stays
   asleep until the strike. Seen (always, headless: the stub is powered): the outpost says so, its guns wake at once, its scramble comes 14 s
   later and its warning starts 4 s later. Not a failure either way. Shooting at the outpost before the strike wakes it the same way and
   skips the go-dark beat.
3. **The strike** (1:42). Power up and sit down: weapons free. The outpost's guns wake (8 s after the strike if it was asleep), the warning
   starts (the comms-array directive), and three interceptors scramble out of the outpost's hangar (Introit, three waves while the hangar
   stands; the Mercy points out the hangar).
4. **The fight opens up** (2:36 to 5:31). Earth's waves on FS2's two-thirds rule with a 70 s time-out, under the cap (5 on Medium): Kyrie
   from the port side, Gloria and Credo together on opposite sides of the Mercy five kilometres out, Agnus from behind. The comms array:
   knocked out at 2:48 in this run ("Nobody further in heard that"). If it is not, the warning is half out at 3:27 and out at 5:12, and a
   heavier wing (Dies Irae: three heavy fighters and two bombers) jumps in behind the Mercy 100 s later: harder, not failed, and the debrief
   says the warning got out. The corvette Cantor jumps in four kilometres off the Mercy's port bow at 3:36; Sable 3 goes for it; hurt below
   40% it breaks off and jumps out (5:10).
5. **Escalation** (5:31 to 7:32). The destroyer Archon jumps in on the Mercy's starboard bow, the far side from the corvette, four and a half
   kilometres out, inside its beams' reach within a minute. Its bombers (Thurible, three, with two heavy escorts) launch from its hangar
   18 s later and go for the Mercy; its own fighters (Benedictus, three at a time while its hangar stands) wait on the arrival cap, which on
   Medium stays full, so they only get in on Insane in these runs. Half a minute in, a heavy hit takes the player's drive: "Get up and seat
   that drive. I'll cover you" (a hand repair; Sable 2 guards you meanwhile). The Archon's beams reach the Mercy ("We're hit. That went
   through three decks"); the Mercy's own are locked (FS2's beam-lock) until the destroyer's are silenced.
6. **Climax** (7:32 to 9:49). The player strips the Archon's beam turrets (or its engines): the Mercy's beams come free ("Ours are free"),
   and Earth's last wing (Libera) is called to cover the destroyer (it too waits on the cap, and in these runs never gets in). **With the
   real Mercy model the destroyer's slashes have usually cut the Mercy's own two beam lances by then** (see below), so the Mercy says so
   ("So are ours. Patience, we need you now") and the Patience jumps in eight seconds later, three and a half kilometres off the outpost's
   face; her helm takes the nearer big ship first. In this run she kills the Archon (8:24), then opens on the outpost; its last defenders
   (Sanctus) jump in; the wing is sent on the outpost; "The eye closes. Others will open." The outpost falls at 9:49, Earth withdraws
   (wings not yet arrived are stood down), the Mercy is safe. With working lances the Mercy kills the Archon itself and the Patience comes
   once it is dead (or 90 s after its beams went down).
7. **Debrief** (10:08): the plan table's debrief lines say what happened: the outpost, the comms array (or that the warning got out), the
   sweep (missed or found), the Mercy, the Archon.

| Level | Result | Time | Longest quiet | Most on the player | Mercy lowest |
|---|---|---|---|---|---|
| Very easy | victory (the Mercy kills the Archon) | 11:57 | 9 s | 2 | 93% |
| Easy | victory (the Archon escapes at the withdrawal) | 11:31 | 9 s | 3 | 54% |
| **Medium** | **victory** (the Patience kills the Archon) | **10:08** | **9 s** | **4** (cap 4) | **72%** |
| Hard | victory (the Archon escapes) | 10:35 | 9 s | 6 (cap 5) | 37% |
| Insane | the Mercy lost to the destroyer's beams | 8:25 | 0 s | 7 | 0% |
| Medium, unseen at the sweep (`-mnGoDark`) | victory, the bonus won | 10:21 | 9 s | 4 | 54% |
| Medium, the comms array ignored (the warning gets out) | victory | 9:45 | 9 s | 4 | 73% |
| Hard, the comms array ignored | the Mercy lost | 10:45 | 5 s | 5 | 0% |

Before rebasing on the real Mercy model (the placeholder Mercy, whose lances survived), the same mission ran 10:54 on Medium with the Mercy
killing the destroyer in 85 s, 11:14 to 11:51 on the other levels, and Insane won once and lost once: those runs are in
`captures/oort-mission/r1/autoplay_prerebase/` and `pacing_prerebase/`.

The pacing charts are in `captures/oort-mission/r1/pacing_*.png` (Earth fighters alive and on the player, the five big ships' hulls, the
beats shaded, the key events marked); the full timelines in `captures/oort-mission/r1/autoplay/*/autoplay.txt` and `events.csv`.

**The Mercy's beam lances (for the art and AI builders).** The real Small Mercy's two beam lances (`SUB_turret_fore` at [0, 17, 104],
`SUB_turret_aft` at [0, -18.3, -96], each 1.5% of 80,000 hull) are destroyed early: in this mission by the destroyer's slash beams at 6:43
on Medium (a probe event on `subsystem Small Mercy kind turret destroyed`), so once the beam-lock is lifted the Mercy has no beams. The same
happens in the dogfight (the lances are gone at 2:34), and that is why **the dogfight's auto-play now fails on main without this work**
(8248eb3 built alone: the last 94 s, from the bombers' death to the Warden's, have no fighter left and the Warden dies to Sable 3's guns,
not the Mercy's beams). With the placeholder (lances on spikes at y = 44 off the hull) they survived. A stationary Mercy's lances, when
intact, still kill a 20,000-hull destroyer in under a minute at five of six bearings (only dead astern is slow), so the arcs are fine: it
is how exposed and how weak they are.

### The cast
- Ours: Sable 1 (the player), Sable 2 and 3, the carrier **Small Mercy** and the cruiser **Patience** (both the real
  diaspora cruiser model since the side-ships merge), the relay flights Lantern (human) and Wick (the alien allies), as in the escort.
- Earth's: the outpost **Oculus**, the corvette **Cantor** (the Warden-class `earth_corvette`: the real Blender model), the destroyer
  **Archon**; wings named for the parts of the Mass: Introit (the outpost's scramble), Kyrie, Gloria, Credo, Agnus, Sanctus (the outpost's last
  defenders), Benedictus (the destroyer's fighters), Libera (Earth's last wing), Thurible (the destroyer's bombers) and Acolyte (their escort),
  Dies Irae (the heavier response if the warning gets out). Earth's voice is cold and scriptural; ours is plain.

### What was built for it (the mission system)
- **Placeholders swapped by class name.** Two new classes, `earth_outpost` (the GTI Arcadia's numbers: hull 100,000, a nucleus about
  1.1 km by 0.76 by 0.94 with the station on top; parts: comms array, scanner, hangar on the ice face, 4 flak on the station, 6 guns dug into
  the ice) and `earth_destroyer` (the GTD Hecate's: speed 15, hull 100,000, 1000 by 180 by 150 m; parts: two beam turrets, 6 flak, 6 guns, 3
  engines, a hangar under the bow, bridge, sensors, comms, reactor). `check_weapons.py` 153 of 153. The mission draws them from its `look`
  (`look.rock`: an ice nucleus with the station hull on it at `hull.offset`; the destroyer a long placeholder hull). A model of either class in
  `Resources/ShipArt` replaces the look with no change to the mission, which addresses parts by kind (`partKind`, `subsystem ... kind`), never
  by a placeholder's part names.
- **Hangars launch.** A bay arrival comes out of the ship's first living `hangar` part, pointing out of it, without a jump flash; a ship whose
  hangars are all destroyed launches nothing (the arrival is scrubbed: counted as arrived and destroyed, logged). Shutting the outpost's
  hangar stops its scramble waves; shutting the Archon's stops its bombers and fighters.
- **Stations that wake.** `capital.holdTurrets` keeps a capital's guns and flak silent until the new `turrets` action (the outpost before it is
  alerted). A two-line change in `AI/CapitalAi.cs` (the hold sits before any random draw, so the dogfight's port is still bit-identical).
- **Orders by part kind** (`partKind`: the target's first living part of that kind). **A withdrawn wing stops feeding in**: `depart` stands
  down ships of it not yet arrived (later waves, ones the cap holds back); they never come, read as departed and never as arrived.
- **Scenery** (`scenery`: single rocks and power-law fields with clear lanes), drawn by `Game/Mission/IceRocks.cs`: an icosphere pushed out by
  noise with craters, each triangle mapped on its own plane (a triplanar look on the house's plated material, copied with generated ice
  textures), tumbling slowly. Drawn only: nothing collides with it, so the lanes ships fly stay clear.
- **Pacing ranges** (`pacing.ranges`: distances between ships logged each second), used to tune where the big ships meet.
- **Lines pending approval** (`"linesPending": true` on the mission, or `"pending": true` per line; `"intensity"` per line): see below.
- **Auto-play `-mnGoDark`**: the stub goes dark for every sweep, so the unseen path can be run headless.
- Self-test: 73 checks (new: turrets held and woken, scenery read, a launch from the hangar part, an order by part kind, a hangar destroyed
  scrubs the next launch, a wing ordered out before it arrives never comes, the dark stub, a pending line counted and an approved unvoiced line
  still failing).

### The lines: 59 radio lines, all drafts pending the user's approval, none voiced
56 messages and 3 call-outs (21 calm, 20 tense, 15 frantic, each tagged), plus 3 briefing pages, 3 briefing questions and 7 debrief lines.
Speakers: the Small Mercy 18, Sable 2 10 (+3 call-outs), the Oculus 9, Sable 3 8, any wingman 4, the Archon 3, the Cantor 1, Earth 1,
Lantern 1, Wick 1. Kept plain: no quips; Earth's are short and liturgical ("The cloud is counted. Every grain of it.", "Lie down, remnant. It
is easier."). They play as on-screen radio text with the squelch.

**How "pending" works** (the check understands it, the build does not fail): the mission carries `"linesPending": true`; any line can say
`"pending": true` or `false` for itself. The voice-coverage checks (`-mnMissionSelfTest`, `-mnAudioSelfTest`) count pending lines ("78 pending
the user's approval, not voiced on purpose") instead of failing; an approved line with no recording still fails. `collect_lines.py` skips
them ("59 line(s) pending the user's approval"), so `tools/audio/voice.ps1` never voices them. New speakers are cast in
`audio_src/voice/cast.json` (Oculus as the Lector's voice, Cantor and Archon as the Warden's, Patience as the Mercy's); a draft's speaker not
yet in the voice index counts as pending too. To approve: set `"pending": false` on the line (or drop `linesPending`), run `voice.ps1`,
rebuild. A preview of the spreadsheet with the Oort lines (marked "pending approval") is `captures/oort-mission/r1/dialogue_preview.xlsx`;
`design/dialogue/monolith_dialogue.xlsx` was left alone (it holds the user's rewrite).

Also fixed: `tools/audio/voice.ps1`'s `wslpath` (it lost the path's backslashes through the WSL shell; noted in the audio history). No
voice generation was run.

### Music
States and stings through the mission's actions: ambient through the opening and the dark (battle 1 for 20 s if the sweep finds you), battle 1
and auto at the strike, battle 2 when the corvette arrives, battle 3 for the destroyer, the warning's response and the climax, ambient as Earth
withdraws, victory or failure at the end. Track names for the user's music (`oort_ambient`, `oort_battle_1..3`, `oort_victory`,
`oort_failure`: drop `music\oort_battle_2.mp3` etc. in the playtest's Audio folder); without them the built-in score plays.

### Checks (all on the final build)
Mission self-test 73 of 73; auto-plays: cabin, escort and oort pass, the dogfight fails its 30 s quiet limit (94 s) exactly as main does
without this work (the Mercy's lances, above); the dogfight's port is the same to the bit (90 s, and 240 s in the self-test); AI 37 of 37, combat 98 of 98, cabin 75 of 75, HUD 90 of 90, audio 104 checks pass (with the 78 pending pairs counted);
flight 8 of 8 (the vanguard fighter's afterburner metrics differ from the Ulysses as before: informational, the boost is ours).

### Captures (`captures/oort-mission/r1/`)
- `hud/`: 18 HUD-on moments from the AI's cockpit (the opening, the go-dark call-out and prompt, the sweep, weapons free, the scramble, the
  comms directive, the corvette, the split, the destroyer, the bombers, the heavy hit and the hand-repair prompt, the beam hit, the beams
  down, the Patience, the outpost dying, the fall, the end).
- `look_outpost/`: the outpost in its nucleus from three angles, the ice field from the start, a big rock, the Mercy in the ice, the scramble
  and the outpost under fire. `look_archon/`: the destroyer's jump-in seen from beside the Mercy, its bombers, the beams, its death.
  `look_finale/`: the Patience's jump-in off the outpost's face, her beams on it, its fall.
- `pacing_medium.png`, `pacing_medium_unseen.png`, `pacing_medium_warning_out.png`, `pacing_hard.png`; `autoplay/` (every level and the two
  paths), `checks/`, `selftest/`.

### Plays poorly / least sure of
- **The Mercy cannot finish the destroyer with the real model** (its lances die to the destroyer's slashes, above): the Patience is called
  at once and the Archon escapes at the withdrawal on Easy and Hard. Fixing the lances (higher, tougher, or off the centreline) brings the
  intended climax back and adds about 45 s on Medium.
- **Insane loses the Mercy** with the AI flying (the destroyer's beams kill it before the AI strips them; before the real model, one run won
  at 11:54). On Medium the Mercy only drops to about 72%: the AI strips the beams in two minutes; a human who chases bombers longer will see
  much more danger.
- **Medium is at the short end** (10:08 with the AI flying; 10:54 when the Mercy's lances live). A human will likely be slower.
- **Two arenas.** The outpost and the Mercy are nine kilometres apart (half a minute at boost). Earth's waves go for the Mercy, so a player who
  stays at the outpost (the hangar, the comms array) is alone with its guns: one "warning ignored" run before the rebase had a 63 s stretch
  with no fighter within 3 km (the outpost's flak was firing, but the pacing measure counts fighters only). Deliberate (real choices), but it
  may feel empty.
- **The destroyer's fighters and Earth's last wing rarely come.** Benedictus and Libera are capped, and the cap is still full of the
  opening waves when they are called, so they queue and are stood down at the withdrawal. Uncapping them would add up to six fighters in the
  escalation and climax; I left them capped because the pressure there is already 5 to 10 fighters.
- **The cap is soft.** Medium's cap of four on the player holds in the main run, but goal-driven escorts can make it five (the unseen run).
- **The capital fight is slow by nature.** Capitals turn and close at 4 to 5 m/s; the hulls are scaled down (outpost 40%, destroyer 20%,
  corvette 12% of the FS2 numbers) so the climax lands in two minutes. With real models and parts this wants re-tuning.
- **The placeholders.** The outpost is a placeholder station hull sitting on a generated ice rock, not built into it; its comms array,
  turrets and hangar are only points on it (they work, and the HUD's subsystem view shows them). The destroyer is a long box hull.
  The captures were taken before the rebase, so they show the placeholder Mercy and Patience and the pre-rebase timeline. The ice
  is a procedural texture that reads as dark crust with ice veins; close up it is busy and a little streaky. The HUD shows the class as
  "Earth comet outpost (placeholder)".
- The go-dark beat can only be judged in the real cabin (headless, the stub is always powered, so the main auto-play always takes the "seen"
  path; `-mnGoDark` runs the other).
- The heavy hit's call-out prompt stays on the HUD after the stub's automatic repair (the real cabin clears it when the pilot stands).
- Every line is a first draft for the user's rewrite.

### How to run
- Play: `Playtest\Monolith_2026-10-24_oort\Play.bat` (the Oculus; `Play.bat oort hard window`, and the other missions as before). Edit
  `Missions\oort.mission.json` beside it and play again without a build.
- Check: `Monolith.exe -batchmode -nographics -mnMissionAutoPlay oort -mnOut dir [-mnDifficulty Hard] [-mnGoDark]`; `tools\mission.ps1`.
- Captures: `tools\capture.ps1 -Plan gauntlet\shots\oort_r1_hud.json` (and `_look_outpost`, `_look_archon`, `_look_finale`). The captured
  run follows the headless one to the tick until about 9:40, then drifts a few seconds: re-time late stills from the capture's own log.

### Gotchas found
- A playtest or build's `Missions\` folder overrides the built-in mission, in the self-test too: clear it before judging a build's own file.
  `-mnMissionAutoPlay` takes a path, which avoids touching the folder while a capture runs.
- Mapping a texture per vertex onto the plane its normal faces smeared it into bands where a triangle's corners chose different planes; per
  triangle (split corners, smooth normals kept) is clean.
- Stood-down and scrubbed ships carry an arrival time so wing arithmetic adds up; "arrived" conditions must ignore stood-down ones, or an
  "X here" radio line fires as Earth withdraws.
- The capital helm closes on the nearest hostile capital at its standoff, so where Earth's big ships arrive decides where the fight is:
  arrivals `near` the Mercy at an offset keep them in beam reach wherever the Mercy has drifted.

## Round 2 (builder, 2026-10-24): the climax on every level, Earth's late wings, the outpost at 7 km

Worktree `oort-tune-wt` from main at 2ab0e28 (AI round 3). Measured, not judged: every number below is the AI flying an invulnerable player
ship, and most are **sweeps of 30 runs a level** with the new `-mnSeed n` (a salt on every random seed of the sim; 0 is the old run, bit for
bit), because one run a level hides how often the climax fails (seed 0 alone said "Medium is fine, Hard escapes"; ten seeds said Hard lost a
lance in 10 of 10 and the Archon escaped in 6). Sweeps: `captures/oort-mission/r2/sweep_base` (main, 10 seeds), `sweep_final` (this round,
30 seeds), `sweep_uncapped` (the rejected alternative below).

### 1. The climax: why the Archon escaped, and the three fixes
The auto-play report now ends with every big ship's fate, lowest hull, hull damage by source and **every part it lost, when, to whom and with
what** (`MissionAutoPlay.CapitalWatch`). On main, at Hard, the Mercy lost a lance in 10 of 10 runs (torpedoes 10 lances, the Archon's slashes
8), usually before the player had stripped the Archon's beams (Hard: 460 to 550 s, the Archon arriving at 340 to 362 s). Three causes:
- **A torpedo wore down every part of the Mercy.** `Weapons/Projectiles.cs` `Blast` gave every part within the 270 m blast a third of the
  subsystem damage (the README's "three at most" was only the divisor), so a torpedo anywhere on the 350 m hull took about 1,900 off both
  lances; on Hard the bombers' torpedoes (half the Mercy's hull damage in some runs) took the lances with them, and in one run a single blast
  at 469 s finished every engine and turret at once. Now the **three nearest parts** share it (FS2 spends a blast on the nearest subsystems
  first; round 3 flagged it). Combat 98/98, the dogfight's port the same to the bit.
- **The Mercy wasted her beams on the corvette.** Her beam turrets took the nearest Earth capital in reach; on Hard the Cantor is often still
  about when the Archon is crippled, and in Hard seed 17 her first beams (496 to 517 s) cut 17 of the Cantor's parts while it was running
  anyway, and the Archon took its first beam 43 s after the lock lifted. New (mission + AI): `beams` takes a `target`, FS2's beam-fire:
  `{ "do": "beams", "ship": "Small Mercy", "free": true, "target": "Archon" }`: the beam turrets (and the helm) go for that ship whenever it is
  alive, in reach and in a turret's arc, and for the nearest otherwise (`CapitalOrders.BeamTarget`; the self-test checks it).
- **The Patience took the kill.** With one lance left the Mercy needs 90 to 110 s on Hard; the Patience's time-out was 90 s (its own note
  said "two minutes"), so she jumped in on top and finished the Archon in 4 of 30. Now 120 s; she still comes at once when both lances are
  gone ("So are ours. Patience, we need you now").

The lances' placement (centreline, dorsal fore and ventral aft) and round 3's 10% toughness are unchanged: a side slash still passes within the
30 m cut radius of both (the hull is 56 to 94 m wide), but 8,000 hp is four or five passes, and with the blast fixed the player strips the
beams first in 29 of 30 Hard runs. The Mercy's approach was looked at and left: she fights at 3.3 to 3.6 km, inside the Archon's 4 km reach,
which is what makes the beam threat (and "We're hit. That went through three decks") real; standing her off would leave the player nothing
to save her from. The withdrawal was left too: once the climax works the Archon is dead before the outpost falls (Hard: the Archon at 560 to
680 s, the outpost 600 to 770 s); it only escapes when both lances go before its beams do (1 of 30, Hard).

| 30 runs a level | main (10 runs): Archon killed / by the Mercy / escaped | round 2: Archon killed / by the Mercy / escaped | lance lost | Mercy's end hull mean (min) | length mean |
|---|---|---|---|---|---|
| Very easy | 10 / 10 / 0 | 30 / 30 / 0 | 0 of 30 | 93% (88%) | 11:00 |
| Easy | 10 / 10 / 0 | 30 / 30 / 0 | 0 of 30 | 87% (77%) | 10:32 |
| **Medium** | 10 / 8 / 0 | **30 / 29 (the player's last hit in the other) / 0** | 2 of 30 | **70% (40%)** | **10:43** |
| Hard | 4 / 1 / 6 | 29 / 29 / 1 | one in 17, both in 1 | 34% (0%: lost once) | 11:25 |

Medium is not trivial: the Mercy ends at 40 to 87% (main, 10 runs: 46 to 86%), the player is at the cap of four attackers 40 to 50% of the
escalation (main 20%), and the Archon dies 50 to 90 s after its beams go. On Hard the Mercy is in real danger (mean 34%, under 20% in 8 of
30, lost once, after the Archon died, to torpedoes and fighters), as round 3 found; the AI player strips beams before it chases bombers.

### 2. Earth's late wings: they arrive, with the cap freed for them
Measured on main (10 runs): the opening waves' stragglers fill the arrival cap until the withdrawal (Gloria, Credo and Agnus ships still
arriving one at a time at 596 s on Medium), so the destroyer's fighters (Benedictus, called at the Archon + 50 s) and Earth's last wing
(Libera, crippled + 12 s) queued behind them and were stood down: none arrived on Very easy, Easy and Hard, 1.1 a run on Medium. The
escalation (the Archon's arrival to its crippling) on Medium: 7.6 threats on average (max 10), 2.8 on the player (max 3.9).
- **Uncapped** (tried, rejected): Medium 9.6 threats in the escalation and 12.9 in the climax, 3.3 on the player, at the cap half the time,
  the Mercy's low 61% on average; **Hard lost the Mercy in 22 of 30**.
- **The cap freed for them** (kept): new `capPriority` on a capped wing: its ships take the next free places ahead of waiting ships of a lower
  priority (Libera 2, Benedictus 1, the rest 0). The cap, and so the pressure, is unchanged. Benedictus now 2.6 / 2.9 / 3.8 / 0.9 ships a run
  (Very easy / Easy / Medium / Hard), Libera 2.1 / 2.1 / 2.3 / 0.5 of 3, and both radio beats fire. The escalation on Medium: 7.8 threats (max
  10), 3.0 on the player (max 4, Medium's cap), at the cap 40% of the time; Hard 9.2 threats, 4.1 on the player (max 5, Hard's cap). On Hard
  the kills come slowly, so the queue rarely frees even for them. Libera's first ship comes before the Archon dies in 94 of 97 runs where it
  comes at all.

### 3. The outpost at 7 km (see `gauntlet/pieces/outpost/history.md`, round 2)
Lit shapes big enough to cover a pixel at 7.2 km, in `art_src/oort_art/build_earth_outpost.py`: the Eye's two rims as continuous rings of
light, lit bands on the decks, a lit strip at the spine's foot, beacon globes on masts at the spine's ends. Before and after from the built
game, HUD off: `captures/oort-mission/r2/outpost7km_before/` and `outpost7km_after/` (start_view: the player's eye point and 55 degree view
turned onto the station; start_cockpit: the seat camera at 3.5 s; money_7km, round 1's framing; start_tele, 12 degrees), side by side in
`captures/oort-mission/r2/outpost7km_before_after.png`; closer frames in `outpost_close_after/sheet.png`. Plan: `gauntlet/shots/oort_r2_outpost7km.json`.

### 4. Small things
- The HUD's class names: "Earth comet outpost", "Earth destroyer", "Diaspora cruiser" (all three still said "(placeholder)" with their real
  models in; `Resources/Ships/*.json`, whose roles now say where the model is).
- The auto-play's `maxEngagingMax` takes one number per level (`[2, 3, 4, 5, 99]` in the Oculus: the AI's own cap on the player), so the
  Hard run no longer fails on "5 <= 4". The other missions still give one number.

### Checks (this worktree's build)
Mission self-test **81 of 81** (76 + 5: a higher capPriority goes ahead, capPriority on an uncapped wing is refused, three wait behind a cap of
one, the per-level expectation, beam-fire takes the far corvette over the near one). `tools\mission.ps1 -Exe` at four levels (seed 0):

| | oort | dogfight | escort | cabin |
|---|---|---|---|---|
| Very easy | PASS 653.8 s, quiet 9 s, Mercy 96%, the Mercy kills the Archon at 528 s | PASS 427.4 s | FAIL: 'Lantern here' expected but no wingman is lost (same on main) | PASS 120.2 s |
| Easy | PASS 630.2 s, quiet 9 s, Mercy 84%, the Mercy kills it at 526 s | PASS 381.9 s | FAIL: the same (same on main) | PASS 140.6 s |
| Medium | PASS 671.4 s, quiet 9 s, Mercy 44%, the Mercy kills it at 573 s | PASS 410.6 s | PASS 296.3 s | PASS 142.5 s |
| Hard | PASS 692.2 s, quiet 23 s, Mercy 44%, the Mercy kills it at 584 s (main: FAIL, it escaped) | PASS, the Mercy lost at 459.5 s (main: FAIL on 5 on the player) | FAIL: 11 on the player (same on main) | PASS 127.6 s |

Port check SAME at Very easy, Easy, Medium and Hard (900 s each: every ship to the bit, timelines identical). AI 39/39, combat 98/98, HUD
90/90, cabin 75/75, audio PASS (150 checks). The dogfight, 20 seeds a level: Very easy, Easy and Medium won 20 of 20 each (longest quiet 30,
22, 18 s); on Hard the Mercy is lost in 16 and 4 are unfinished at 1,500 s (round 3's known Hard gap). The Oculus' 30-seed gate passes 29,
27, 30 and 28 of 30: the misses are a 32 to 45 s quiet in 3 runs on Very easy and Easy (the player crossing from the outpost to the corvette;
main's first 10 seeds had the same, 27 and 35 s), one Easy run with 4 on the player against Easy's 3, and the Hard escape and Mercy loss.

### Known gaps
- **Hard**: the Archon escapes when both lances go before its beams (1 of 30), and the Mercy can be lost after the Archon dies (1 of 30). The
  AI player strips beams before it chases bombers; a human who goes for the bombers first saves the lances from torpedoes but leaves the
  slashes longer. Levers if Hard wants softening: the Thurible's torpedoes (two each) or the lance share (10%).
- **Two arenas** (round 1): on Very easy and Easy, 3 runs in 60 have a 32 to 45 s quiet while the player crosses from the outpost to the
  corvette; the outpost's guns fire at the player meanwhile, but the measure counts fighters only.
- Benedictus and Libera come in one ship at a time as places free up, not as a formed wing; on Hard about one ship of each a run.
- The blast fix is global: every missile, flak burst and torpedo now spends its part damage on the three nearest parts. The dogfight's port
  and every self-test pass and its Hard result is unchanged (the Mercy is lost), but its balance was not re-soaked here.
- No new radio lines. Benedictus' and Libera's existing drafts now play (pending the user's approval: text and squelch).
- Not tested by hand.

### How to run
- Play: `Playtest\Monolith_2026-10-24_oort2\Play.bat` (the Oculus; OORT2.txt says what changed).
- Sweep: `Monolith.exe -batchmode -nographics -mnMissionAutoPlay oort -mnOut dir -mnDifficulty Hard -mnSeed 7`, once a seed (30 seeds at four
  levels is about two minutes on 12 processes); `autoplay.txt` ends with the capital ships' report.
- Captures: `tools\capture.ps1 -Plan gauntlet\shots\oort_r2_outpost7km.json -Kind stills`.
