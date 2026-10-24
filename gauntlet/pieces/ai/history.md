# Piece: AI and the first dogfight

The brief: make enemies and wingmen fight like FS2 and give the user a dogfight to play (design sections 1, 5, 7, 8 and the appendix's AI
skill note). The user judges by hand in the dogfight (`-mnScene dogfight`); the playtest build is `Playtest\Monolith_2026-10-24_dogfight\`.
Code: `Scripts/AI` (its own assembly, Core only; `Scripts/AI/README.md`), the dogfight in `Scripts/Game/Dogfight`, heavy beams in
`Scripts/Weapons/Beams.cs`. Data: `Resources/AI/ai.json`, `Resources/AI/radio.json`, `Resources/Dogfight/dogfight.json`.

## Round 1 (builder, 2026-10-24)

### What was built
- **Fighter AI** (`AiPilot`, FS2's `ai_chase` and friends studied in ptitSeb/freespace2 `aicode.cpp` and `aibig.cpp`, our own code), every
  ship flown only through `ControlInput`. It picks a target the way FS2's `find_enemy` does (nearest, attackers of itself and bombers on a run
  first, spread across targets by `(attackers + 2) / 2`, no more than the level's cap on the player), gets on its tail with lead, leading
  along a turning target's curve as far as its accuracy reads it, with the aim point's own turn rate fed forward to the stick; fires the guns
  inside FS2's firing cone (narrowed and held by patience), the heavy gun once shields are down, heat-seekers in the cone, lock-ons once
  locked, with FS2's reload rules; matches speed close behind (patient pilots close slower and do not overshoot); passes a head-on target to
  one side and turns after it; breaks off when it gets nowhere (FS2's get-away). When hit with its shield down or sat on, it breaks hard into
  the attacker with jinks and then turns on it (chance and quality from evasion), finishes a target it has half killed first, evades missiles
  across their path and throws decoys (FS2's per-level countermeasure chance). It pulls out to mend when hull falls below what courage allows,
  jinking all the way home (to its capital ship, under its guns) and comes back; `fearless` drones never do. It keeps off capital hulls (its
  path and its wanted path over the next 1.5 to 4 s against every capital's boxes, grown by a margin) and off other fighters, and does not
  chase a fighter into an enemy capital's gun envelope unless fearless or ordered. Strafes capital ships (FS2's attack run and retreat), and
  comes round to a part's own side of the hull before it dives at it.
- **Skill knobs and difficulty** (`Resources/AI/ai.json`): FS2's four knobs per profile per level, copied from ai.tbl where named (drone:
  Captain aim, Lieutenant evasion and patience, courage 0 and fearless; drone_heavy; drone_bomber; wingman: Major; the **ascended ace**:
  General in every knob, fearless and `unhandicapped`; autopilot: Colonel, for soaks; turret profiles). The five levels carry FS2's own tables:
  turn time scale (Earth turns at 1/3 on Very easy), aiming error, in-range time, Skill_level_delay (Earth aims from a picture of its target
  2 / 1.5 / 1.33 / 0.5 / 0 s old), retarget time, countermeasure chance, hostile and friendly fire delay (ship.cpp), max attackers and homers
  on the player, and the player's damage scale (shiphit.cpp: 25/50/65/85/100% of weapon damage, parts 20..100% on top, applied by Damage
  through `GameWorld.PlayerDamageScale`).
- **Bombers**: line up on a capital ship from a flank, select the torpedo, hold the nose on it through FS2's lock time, launch, peel away,
  come round for the next run; with the torpedoes spent they go home and guard their own capital. Escorted (escorts guard a bomber: fight
  whatever threatens it, else hold a place by it).
- **Capital ships and turrets** (`CapitalAi`): the helm holds a standoff and keeps the enemy capital at a bearing that brings broadside and
  beams to bear (100 s turns: slow and heavy), dead in space with every engine gone. Every turret part (the corvette's real model parts) picks
  its own target, leads it and fires through `FireRequest`, so a destroyed turret is silent: flak into the target's path (scattered by
  accuracy, never near a friendly), anti-fighter guns at fighters and bombers and at torpedoes coming at their ship, a turret only firing out
  of its own side of the hull. **Heavy beams** (new `kind: "beam"` in the weapons system, FS2's TerSlash: 3 s charge, 2 s burn, 350 per 0.17 s
  tick): the Warden's two forward dorsal turrets (the model tags them "beam") charge and slash the Mercy through the juice (`beam_charge`,
  `beam_fire`, `beam_aim` every tick, `beam_end`), hurting whatever crosses them and cutting the parts they touch; destroy those two turrets and
  the Warden's beams stop and the Mercy's come free (FS2's beam-lock).
- **Wingmen**: the same AI on our side, Major-class, taking the quick menu's four orders through the player's `ControlInput` (pad: hold Y, then
  A, B, X, LB; keyboard F1 to F4): attack my target (and its part, if one is targeted), disable my target (its engines; done, they say so and go
  back to covering you), cover me (fight whatever attacks you, else stay by you), form up (formation slots on you, holding the gap). Each is
  acknowledged on the radio in the crew's dry voice (`radio.json`, placeholder); the HUD shows radio lines at the top and the last order in the
  quick menu. They pull out to mend when hurt ("I'm cut up. Pulling out to mend.") and come back.
- **The dogfight** (`-mnScene dogfight`, `DogfightBattle` the rules, `DogfightScene` the view, `Resources/Dogfight/dogfight.json` the layout):
  the player (Sable 1) and two wingmen beside our cruiser the **Small Mercy** (a placeholder hull in FS2's design language, plated, lit windows,
  running lights, recessed exhausts; turrets, flak, two beam turrets, engines, bridge as combat parts; GTCv Deimos hull) against Earth's
  corvette the **Warden** (the real model and its 32 parts, hull scaled to 40,000; flak and guns live) and six waves of Earth's drones
  (Seraph interceptors and Dominion fighters, on the FS2 classes' numbers) arriving from every side on FS2's rule (the next wave 5 s after the
  current one is two-thirds destroyed, never more fighters alive than the level's cap 3/4/6/8/10), a bomber wing (three Censers, two Throne
  escorts) making torpedo runs on the Mercy with the third wave, Earth's scriptural taunts and our crews' lines on the radio. It ends in a plain
  win (the Warden destroyed and every wave cleared: "AREA CLEAR") or loss ("SHIP LOST" / "SMALL MERCY LOST"), A or Enter to fly again, B or F10
  to quit. Pause lists the controls and changes the difficulty for the next run. The Milky Way stall is hidden with `SkyWarmUp` as in the range.
- **Measurements**: `-mnAiSelfTest` (31 checks), `-mnAiArena` (AI against AI with knob sweeps, `gauntlet/shots/ai_arena.json`),
  `-mnDogfightSoak` (the dogfight bare for an hour, the AI flying an invulnerable player ship, loops back to back) and the pacing log
  (`-mnPacingLog`, and every soak loop): threats alive, hostiles on the player, nearest hostile, quiet stretches.
- **A capture fix that matters to everyone**: stills with `pos`/`rot`/`lookAt` in scenes with the cockpit camera (flight, range, dogfight) were
  rendering from the cockpit, because HDRP takes the camera's matrices before `beginCameraRendering`; `CaptureCamera` now also re-applies the
  pose after every LateUpdate. Earlier range and flight look shots taken with a fixed pose may have been cockpit views.

### Skill knobs against outcomes (`gauntlet/captures/ai/r1/arena/arena.txt`, 30 runs of up to 180 s each, 1.5 km apart)
| Match | Result |
|---|---|
| ascended ace v drone, Medium | ace wins 30-0, mean 30 s; hit rate 38% v 15%; tail 16% v 15% |
| wingman v drone, Medium | wingman wins 30-0, mean 31 s; hit rate 30% v 15% |
| drone Insane v drone Very easy | Insane wins 30-0, mean 17 s; hit rate 41% v 8% |
| ace v two drones, Medium | ace 9 wins, drones 19, 2 time-outs |
| drone v drone, Medium | 27 of 30 time out (both aim from a 1.3 s old picture): hit rates 9% each |
| accuracy 0.2 / 0.5 / 0.8 / 1.0 (drone v drone, Hard) | wins 7 / 13 / 13 / 20 of 30; hit rate 17 / 21 / 25 / 31% |
| patience 0 / 0.5 / 1.0 (Hard) | wins 11 / 15 / 21; hit rate 20 / 27 / 30%; shots fired 4833 / 4266 / 3966 |
| evasion 0 / 0.5 / 1.0 (Hard) | the opponent's hit rate on it 25 / 23 / 21%; wins 16 / 14 / 15 (evasion trades offence for defence) |
| courage 0 / 0.5 / 1.0 (wingman v ace) | pulls out at 38 / 23 / 9% hull on average |

### Pacing and the soak (`gauntlet/captures/ai/r1/soak/`, Medium, 3,600 s of sim, AI flying an invulnerable player ship)
- Three loops finished: lost (the Mercy) at 868 s and 1,044 s, both at wave 5 of 6; won at 1,134 s. Longest stretch without a threat 3, 0 and
  27 s (design: under about 30 s); threats alive up to 11, on average 5 to 8; hostiles on the player at once at most 4 (Medium's cap); in the
  won loop the player had 4 on it 727 of 1,129 s.
- Wave timing in a loop: wave 1 at 4 s, then 45 to 375 s apart depending on how fast the wing kills (the next wave on the two-thirds rule).
- Never stuck (longest under 3 m/s: 0.5 s), **no AI fighter touched a capital hull** in the hour, no errors, no bad numbers. Turrets fired
  6,184 gun bolts, 2,162 flak shells, 122 shots at torpedoes and 68 beams.
- On Hard (`soak_hard/`) the invulnerable autopilot, alone once the wingmen were down, could not finish the last wave in the hour (it thrashes
  between attackers): nothing stuck, no hull contacts, no errors.

### Checks
AI self-test 31 of 31; `-mnCombatSelfTest` 90 of 90; `tools/flight.ps1` 35 of 35 on every class (the `TurnRate`/`TurnLag` published in
`ShipState` changed nothing); `check_weapons.py` 138 of 138 FS2 numbers match (the slash beam, the Earth classes and the Mercy's Deimos numbers).

### Performance (RTX 4070 Ti, 1920x1080, cockpit and HUD, real time; `gauntlet/captures/ai/r1/perf*`)
- Busiest (stress start: the first three waves, the bomber wing and its escort at once, uncapped, both capitals' turrets, flak and beams):
  274 fps average, 1% low 171 fps, worst frame 10.7 ms, p99 5.5 ms, GPU 1.9 ms, main thread 3.6 ms, sim 60.0 Hz.
- The loop's opening: 383 fps average, 1% low 216 fps, worst 29.9 ms (one frame: the juice cutting a dying ship into debris, logged as a
  34 ms hitch with 194 debris pieces by `-mnJuiceHitchLog`).

### Captures (`gauntlet/captures/ai/r1/`, from the final build; plans `gauntlet/shots/ai_r1_*.json`)
HUD off: `look_beam_on_the_mercy` (the Warden's slash beam hitting the Mercy), `look_warden_under_attack` and `look_warden_turrets` (riding the
Warden's sunlit side while the player's ship attacks its beam turrets: its guns firing back, a part blown out venting), `look_bomber_run`
(behind a Censer on its run, the Mercy under fire ahead), `look_drone_on_the_attack` (behind a drone firing on our wing). HUD on:
`gameplay_hud_attack_run` (an attack run on the Warden's beam turret: target box with the part, lead, the Mercy's warning on the radio) and
`gameplay_hud_turret_killed`. The subfolders hold every shot of each sequence.

### Known gaps and what is weakest
- **Balance is untested by hand.** In the AI-only soak the Mercy is lost in two loops of three on Medium; wingmen are usually both lost by
  the middle of a run. Levers, all data: the Mercy's hull, the Warden's `beamWaitScale`, the bombers' torpedoes, the waves and the cap.
- The Earth corvette's dark hull all but vanishes beyond about 500 m (its look is the corvette piece's); look shots ride close and sunlit.
- Two-against-one the ace still loses more than it wins, and the invulnerable autopilot thrashes against three or four drones (Hard soak).
- Earth's fighters are placeholder hulls on FS2 classes' numbers; the radio is text only; turrets see by hull boxes, not the model.
- Fireballs close to a camera show square quad edges in some look shots (the juice's).

### Next
The user's playtest; then tune from it (the Mercy's survival, wingman survival, how many drones sit on the player), aces in a mission, the
mission system driving the AI's goals (`SetGoal`, `CapitalOrders.HoldBeams`) from events.

## Round 2 (builder, 2026-10-24): the Warden's guns

The user's playtest: "as soon as I go near it, it fires 50 million lasers - no chance at all". The fix is FS2's capital-ship feel, dangerous
but beatable by taking the turrets out one by one, and the balance round around it (the Mercy lost in two Medium runs of three, the wingmen
gone by the middle of a run, four drones on the player). Studied: fs2open `aiturret.cpp` (turret target choice, ownage caps, fov test, aim
update, recheck time), `aicode.cpp` (`set_secondary_fire_delay`, `check_ok_to_fire`), `modelread.cpp` (turret fov default), FS2's weapons.tbl
(Terran Turret, Terran Huge Turret, Standard and Heavy Flak, AAAf, TerSlash) and the Deimos' loadout in ships.tbl. Behaviour only, no code.

### What changed
- **Turret gunnery** (`CapitalAi.TurretGunnery`) on FS2's rules. Every turret has a field of fire off its own up (`turretArcGun` 80 deg,
  `turretArcFlak` 70, `turretArcBeam` 100; FS2's test with its radius slack) and fires only where its own hull is clear: the corvette keeps lanes
  no gun covers and a stripped face goes quiet. Targets: torpedoes at our side's capitals first (guns and now flak), then the nearest enemy in
  the arc with FS2's tenth per turret already on it, never more of one ship's turrets on one target than `turretsOnTarget` (FS2's 3 + skill
  squared) or, on the player, `turretsOnPlayer` (2, 2, 3, 5, 8, ours); lasers fire on an enemy capital in reach; FS2's two-second recheck.
  Aim: a picture of the target refreshed at random up to `turretAimUpdate` (1.5 to 0.2 s by level) and led from in between, the error 3x on a
  target just come into the arc and settling over `turretSettleSeconds` (6 to 1.5 s) while it stays: weaving and passing through are spared,
  loitering is not. Flak never bursts where it would hurt its own side. Our side's gunners get the best level's aim. Bolts stay FS2's Terran
  Turret (275 m/s, every 1 s x the level's fire delay x the scene's 1.5); beams keep their 3 s charge.
- **Flak**: bursts at its fused range or on a direct strike, as FS2's (`rules.json flakProximity` 25 m -> 0: with it, badly aimed flak hit as
  hard as settled flak), and its bursts hurt hostile torpedoes (FS2's flak is the answer to bombs).
- **The autopilot's attack runs on parts** (also wingmen told to attack a part): it steered at where the part was, so its bolts landed 15 to
  18 m behind it on the hull (the Warden moves) and in 284 s and 56 hits it never touched a turret. It now flies the nose onto the lead and
  fires in a cone the part's size.
- **Wingmen**: `wingDamageScale` (ours, the player's mercy shared with the wing it commands, through `ShipCombat.DamageTakenScale`), and
  `peelMaxSeconds` 30 -> 60 so a wingman that pulls out comes back mended (it came back at 40% hull and died in the next fight).
- **Missiles**: FS2's reload autoscale by AI class (+0.5 s per class below General: a Captain drone waits 2.5 s more) and FS2's chance to fire
  homing missiles at the player (`missileChanceOnPlayer`, 1 to 5 of every 7 ten-second windows by level, always if the player targets it).
- **Measuring**: `-mnWardenRun` (`WardenRun.cs`: a mortal AI-flown player strips the Warden's turrets, beam turrets first, fighting off any
  two on its tail; the wing told to cover it), `DamageLedger` (damage by source), and the soak now reports wins and losses, what hurt the
  Mercy, the wingmen and the player, hostiles on each, the Mercy's kills and when the first wingman fell. Six new self-test checks.

dogfight.json and dogfight.mission.json are untouched (the tuning is ai.json and rules.json), so the port check holds as it was.

### Numbers, before (main at eb09e7e) and after; 10 Warden runs per row, soaks 12,000 s of sim each
| Warden runs, mortal AI player stripping turrets | before | after |
|---|---|---|
| Medium, waves on: alive (mean) / within 1 km / turrets by the player | 40 s / 16 s / 0.0 (died 10 of 10) | 194 s / 125 s / 6.4 (died 1 of 10; 3+ in 10 of 10) |
| Medium, waves on: turrets on it at once (most / mean close), turret shots a second close | 10 / 5.9, 2.9 | 3 / 1.4, 0.7 |
| Medium, no fighters | 138 s, 0.0 turrets, died 10 of 10 | 181 s, 9.1 turrets, died 0 of 10 |
| Hard, waves on | 29 s, 0.0 turrets, died 10 of 10 | 80 s, 2.8 turrets, died 10 of 10 |
| Easy, waves on | 183 s, 0.0 turrets, died 6 of 10 | 183 s, 9.1 turrets, died 0 of 10 |

Stripping thins the fire (Medium, waves on): 1.10 turret shots a second at the player within 1 km with 0-2 of the Warden's turrets down,
0.57 with 6 or more down (Hard 2.63 -> 1.02, Easy 0.49 -> 0.37). Told "attack my target" instead of "cover me", the wing joins the turret
runs and leaves the fighters to the player: died 7 of 10, 2.6 turrets.

| Soak (loops won / the Mercy lost / the player's ship lost) | before | after |
|---|---|---|
| Medium, invulnerable autopilot | 7 of 12 / 5 / - | 17 of 17 / 0 / - |
| Medium, mortal autopilot (charges the beam turrets whatever is about) | 0 of 306 / 0 / 306 (dead at about 30 s) | 11 of 23 (48%) / 0 / 12 |
| Easy, invulnerable / mortal | 13 of 13 / 0 of 71 (70 ship lost) | 24 of 24 / 24 of 24 |
| Very easy, invulnerable / mortal | - | 18 of 18 / 18 of 18 |
| Hard, invulnerable / mortal | 2 of 2 / 0 of 373 | 1 of 4 (Mercy lost 3) / 0 of 108 |
| Insane, invulnerable / mortal | - | 0 of 4 (Mercy lost 4) / 0 of 398 |
| Medium: wingmen alive at the end / first wingman lost | 0.0 / 19 to 292 s (median 130) | 1.1 / 144 to 451 s, none lost in 6 loops of 17 |

The Mercy's damage on Medium is now nearly all Earth's fighters' guns (about 90%; beams the rest); torpedoes are shot down. The player's on
Medium is fighters (guns 50-70%, missiles 20-50%); the Warden's turrets and flak are under 10% of it.

### Checks
AI self-test 37 of 37 (31 + the turret rules: the cap on the player, the rest on a wingman, a stripped face silent, the blind lane off the bow,
lasers on a capital in reach, flak punishing loitering 20 -> 37 hp/s over 16 s); combat 90 of 90; mission 59 of 59 (its port check SAME over
240 s); `-mnDogfightPortCheck 900`: SAME at all 900 samples, both won at 879.4 s, identical timelines; auto-plays: dogfight victory at 879.4 s,
escort victory at 283.1 s, every assertion passes; flight.ps1 35 of 35 for every class and the player's fighter.

### Playtest
`Playtest\Monolith_2026-10-24_balance\`: `Play.bat dogfight` or `Play.bat escort` (`[window] [veryeasy|easy|medium|hard|insane]`); the
mission files are in its Missions folder; BALANCE.txt says what changed and what to look for.

### Known gaps
- Nothing here is tested by hand; the numbers are an AI pilot's. A human weaves (the turrets' aim is built to miss that) but loiters more.
- Hard and Insane soaks stall: the invulnerable autopilot cannot clear Hard drones (6.8% of the time on a tail), a wave lasts an hour and the
  Mercy dies of attrition. A human on Hard dies first (mortal: 0 of 108). Hard may now be too hard; `turretsOnPlayer` and the drones' tables are
  the levers.
- The wingmen's mercy is ours, not FS2's (FS2 spares only the player).
- The escort mission was only auto-played.
