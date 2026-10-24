# Piece: combat (weapons, targeting, damage, parts, collisions, the combat HUD and the range)

The brief: the core combat loop of an FS2/TIE-style game, in separate assemblies wired through `GameRoot`, with clean interfaces an
AI builder can drive through `ControlInput`. The numbers are FS2's (from its own tables) unless marked as ours. The user judges by
hand in the combat range (`-mnScene range`); the playtest build is `Playtest\Monolith_<date>_range\`.
Code: `Scripts/Weapons`, `Damage`, `Collision`, `Targeting`, `CombatHud` (one assembly each), the shared data in `Scripts/Core/Combat`,
the composition in `Scripts/Game/Combat` and the range in `Scripts/Game/Range`. The combat and AI-contract doc is
`Scripts/Weapons/README.md`; the data is `Resources/Combat/` (`weapons.json`, `rules.json`, `range.json`, `ships/*.json`).

## Round 1 (builder, 2026-10-24)

### What was built
- **Weapons** (data in `weapons.json`; every number is `{value, unit, fs2: "<weapon>|<field>"}` or `{..., source}`).
  Two guns: **Needle** (fast, anti-fighter, strong against shields) and **Hammer** (slow, heavy, poor against shields, best against hull
  and parts). Three missiles with limited ammunition: **Ember** (heat-seeker, needs only the target in the view cone, easy to decoy),
  **Lance** (lock-on: lock time inside a cone with a steady tone, then it homes hard) and **Anvil** (heavy torpedo for capital ships,
  slow, about 2% against shields, and it **can be shot down**: 40 hp). **Decoys**. Plus the emplacement and capital weapons (turret
  laser, flak). Shots inherit the shooter's velocity, guns converge on the target's range, and hits are **swept** (a shot's path this tick
  against the ship's boxes and shield bubble, the segment start shifted by the ship's own displacement): nothing tunnels at any speed.
- **Targeting** (`TargetingSystem`, `AimSystem`): cycle hostiles nearest first, the nearest attacker, the ship in the reticle, a target's
  parts (nearest the nose first, then the whole ship), the **lead point** (`Geo.SolveIntercept`) and a terse readout, the lock state and
  the incoming-missile warning. An AI can set `ControlInput.TargetShip` and `TargetPartPlusOne` instead of pressing buttons.
- **Damage** (`DamageSystem`): one **shield bubble** that recharges (rate set by the power choice), the **power menu** (favour guns,
  shields or engines, or balanced), hull, **parts you feel** (engines cap your speed, sensors add static to the target box, a lost mount
  silences that gun, launchers gone means no missiles, a reactor loss starves the shield and the weapon energy), **self-healing** on the
  player's side only (damaged parts mend after a few quiet seconds; destroyed ones never do), and **deaths** that raise `ship_destroyed`
  for the juice (a capital ship's goes through the juice's staged sequence and its shockwave).
- **Destructible parts and visible damage on every ship class**, with the visual hook `IShipVisual` (Core). See "Parts" below.
- **Collisions** (`CollisionSystem`): ship against ship and against the capital hull by oriented-box SAT, through the flight model's knock
  hook (`GameWorld.Knocks`). The damage is the energy lost in the contact, shared by mass and bypassing shields, so **a graze hurts
  and does not kill** (about 15 hp at 15 m/s; about 110 hp at 40 m/s; a 70 m/s ram kills).
- **Pad controls** in `controls.json` on the Squadrons-style layout (nothing moved), see "Pad".
- **The plain combat HUD** (`Monolith.CombatHud`, its own small assembly so the real alien-tech HUD can replace it): lead pip, target
  bracket and readout, lock UI, the missile warning arrow, shield/hull/energy bars, the power row, ammunition, damage lines, the quick
  menu, notices. A steady **lock tone** and a **missile warning tone** (synthesised in code with `OnAudioFilterRead`).
- **The combat range** (`-mnScene range`): the real systems against five scripted drones (three fighters, a bomber, a heavy fighter),
  three emplacements that shoot back (Laser 1, Flak 1, Rack 1) and the real Earth corvette "Warden" with its 32 parts (12 anti-fighter
  guns, 8 flak, 5 engines, 2 launchers, 2 sensors, bridge, comms, reactor), scaled to 12,000 hull in the range (FS2 80,000) so two
  torpedoes can kill it. Scripted drones and emplacements come back after a while. A scripted pilot (`RangeScript`) plays the player's
  side through the same `ControlInput` for captures and checks.
- **Juice integration**: the combat systems raise exactly the events the juice listens for (`weapon_fired`, `hit_shield`, `hit_hull`,
  `near_miss`, `explosion`, `flak_burst`, `subsystem_destroyed`, `ship_destroyed`) and one new one, `missile_track` (every tick per guided
  missile: position, heading, speed), so missiles fly where they really are. My hunks in the juice builder's files: `Fx/Projectiles.cs`
  (the `hammer` and `hammer_enemy` looks, `LaunchEffects`, `OnTrack`, the tracked-missile path in `StepMissile`) and one line in
  `JuiceDirector.cs`. When the juice is given the model's `IJuiceShipDamage` it draws the scorches and part wrecks itself and the visual
  adapter leaves those two to it (no double decals).

### Weapon numbers against FS2 (`tools/combat/check_weapons.py`: 104 numbers copied from FS2 compared with its own tables, 104 match)
Names are ours; FS2's are in the table as `fs2Weapon`. "ours" marks a number that is not FS2's, with its reason in `weapons.json`.

| Weapon (FS2 source) | Speed m/s | Wait s | Damage | Shield / hull / part x | Life s | Notes |
|---|---|---|---|---|---|---|
| Needle (Mekhu HL-7) | 485 | 0.15 | 12 | 0.8 / 0.9 / 0.3 | 2 | energy 0.2, all FS2 |
| Hammer (blend: Kayser, Prometheus R, Maxim, Akheton SDG) | 650 | 0.45 | 40 (ours) | 0.3 (ours) / 1.3 / 1.0 | 2 | inside the appendix ranges; energy 1.6 |
| Turret laser (Terran Turret) | 275 | 1.0 | 35 | 0.85 / 1.25 / 0.75 | 5 | capital and emplacement turrets, dodgeable |
| Flak (Heavy Flak) | 750 | 1.5 | 50 | 1 / 1 / 0.4 | 1.5 | blast 30 to 60 m, force 70 |
| Ember (Rockeye) | 190 | 0.5 | 45 | 0.8 / 1 / 0.8 | 10 | turn 0.85 s per 360 deg, view cone 100, blast 20 to 40; decoy chance 0.85 (ours) |
| Lance (Harpoon) | 250 | 2.0 | 100 | 0.8 / 1 / 0.5 | 5 | turn 1.0 s, lock 2 s, blast 20 to 40; lock cone 12 deg and decoy chance 0.45 (ours) |
| Anvil (Helios, lock from Cyclops) | 65 | 6 (ours; FS2 30) | 6800 | 0.02 / 1 / 0.85 | 30 | lock 5 s, blast 100 to 270, shockwave 75 m/s; hp 40, cone 15 deg, decoy 0.1 (ours) |
| Decoy (Type One) | 30 back | 0.5 | 0 | | 1 to 2 | decoy range 350 m (ours) |

Ship numbers are FS2's too: the player's Vanguard fighter carries the GTF Ulysses defence (hull 180, shield 380, power output 2.0, weapon
energy 80, 25 decoys; 12 Ember, 6 Lance, 2 Anvil on the range) and the corvette the GTCv Deimos hull (80,000) with its turret and part
shares (turret 0.833% of hull each, engines, sensors and comms 5%). The shield **bubble** is the FS2 total times `shieldBubbleScale`
0.5 (FS2 splits it over four quadrants and fire lands mostly on one), which is ours and tuned by feel. Shield recharge 4% a second at
balanced power (25 s from empty; 14 s favouring shields; 42 s starving them): ours. Damage maths and every rule constant are in
`rules.json` with their sources.

### Parts: how damage works and the interface
- `ShipCombat.Parts` is the list of `CombatPart` (name, kind, ship-local position, radius, hp). Hit points are a share of the hull, FS2's
  way (`hpPercent`). A hit on the shield bubble does `damage x shieldMult` to the shield; what is left goes to the hull
  (`x hullMult`) and, near the impact, to the nearest part within 2 radii (or the part the shot struck) as `x subsystemMult`.
  Blasts fall off linearly from the inner to the outer radius, share their part damage among at most three parts and hurt every ship,
  friendlies and the shooter included.
- Kinds and what losing them does: `engine` (caps `ShipState.SpeedScale`, which Flight multiplies into the goal speed: a fighter with
  none limps at 30%, a capital drifts), `sensors` (`SensorStatic` on the target box), `mount` (that gun offline), `launcher`, `flak`,
  `aaf` and `turret` (silenced), `comms`, `bridge`, `reactor`. `ShipCombat.Flags` says what has gone.
- **The visual hook** `IShipVisual` (Core/Combat/ShipCombat.cs): `SetPartHealth(name, 0..1)`, `DestroyPart(name)`,
  `HullHit(point, normal, radius, severity)`, `SetHullDamage(0..1)`, `ResetDamage()`. Combat owns every rule and hit point; the visual
  only shows the result. The art side's `ShipDamage` (`IShipDamage`) is adapted to it by `ShipArtVisual` (no art file was edited);
  a ship with no art gets `PlaceholderVisual`. Part names are the model's own (`SUB_flak_P02`).
- Combat parts on the corvette come from the model's `ShipSubsystem`s (`ShipArtVisual.Parts`), and its hit boxes follow the hull
  stations in `build_earth_corvette.py` because the model's collision proxy is not readable at run time.
- An AI drives a ship through `ControlInput` only (`Buttons`, `TargetShip`, `TargetPartPlusOne`, `SelectMissilePlusOne`) and turrets through
  `world.Fire.Add(new FireRequest {...})`; `Geo.SolveIntercept` and `ShipCombat.LeadPoint` give the aim; `Teams.Hostile(a, b)` says who
  fights whom. Worked examples: `Game/Range/Gunners.cs`. Full contract: `Scripts/Weapons/README.md`.

### Pad (Xbox; keyboard fallbacks in `controls.json`; nothing of the Squadrons-style layout moved)
RT fast gun, LB heavy gun, RB fire the selected missile (lock-on weapons fire once the tone is steady), **R3** (click the aim stick) next
missile type, B decoys, A next hostile, LT the ship in the reticle, **View** the nearest attacker, X match speed, L3 afterburner,
D-pad throttle presets. **Y tap** steps through the target's parts; **Y held** (after 0.25 s) opens the **quick menu**: D-pad up favour
guns, left favour shields, right favour engines, down balanced; A, B, X, LB inside it are the wingman orders (attack my target, disable
my target, cover me, form up), **reserved** for the AI builder: the buttons are in `ControlInput` (18 to 21) and do nothing yet. While the
menu is open the buttons it names do only what the menu says. Start pauses (and lists these); Start resumes, Y respawns, B quits.

### Checks and captures
- `-mnCombatSelfTest`: 90 of 90 (fire rates, speeds, damage maths, no tunnelling, lead, shield recharge, power, engines, sensors and
  mounts, healing, locks, missiles, decoys, torpedoes shot down, blasts, collisions, targeting, deaths). `-mnScene range -mnPadDrive`:
  18 of 18 (a virtual pad presses every combat control; it needed a longer gap after each release, because a headless run draws a frame
  every millisecond, faster than the 60 Hz sim). `check_weapons.py`: 104 of 104. Flight regression (`tools\flight.ps1`): 35 of 35 metrics
  on every class, so the `SpeedScale` hook changed nothing.
- Captures in `gauntlet/captures/combat/r1/` (git-ignored), made from the final build with the plans `gauntlet/shots/combat_r1_*.json`
  (specs in `gauntlet/shots/specs/`): **kill** (a lock builds and is made, a missile away and hitting, a gun on the shield, the shield
  down, a part damaged, hull critical, the kill), **parts** (a flak turret targeted, hurt, destroyed with its wreck and debris, the next
  one), **capital** (a lock, a torpedo away, closing and hitting, the turrets gone, the staged death), **missile** (the warning arrow,
  the missile closing, a decoy, the decoy taking it), **damage** (under fire, the shield gone, hull and parts, parts lost, the quick
  menu, clear of fire, mending), and **perf**. Every scripted expectation in them passes (lock, kill, part destroyed, decoy success, speed
  scale below 0.8, sensors and gun flags, hull above 4%).
- **Perf** (a 22 s real-time fight at 1920x1080: lock, missiles, gun fire, a decoy, the heavy gun on the bomber, with the drones, three
  emplacements and the corvette's turrets all shooting; RTX 4070 Ti): average 409.7 fps, 1% low 237.8 fps, worst frame 11.0 ms, p99 3.7 ms,
  GPU 1.7 ms and main thread 2.4 ms on average, the sim at 60.04 Hz.
- **The first-view stall** (a finding that is not mine to fix): the first time the Sky's Milky Way comes into view the frame takes about
  0.55 s. It shows in any scene (found by bisecting a hitch that looked like combat), so the range shows the Sky from four wide looks in its
  first ticks (`SkyWarmUp`) and the stall happens before you can see it. It is a Sky-system issue and will be there in the other scenes.

### Known gaps and what is weakest
- **Placeholder fighters have no visual parts**: they only lose their exhaust glow when an engine goes. The corvette is the one ship with
  real parts, wrecks and decals; the rest wait for their models.
- The corvette's hit boxes are approximated from the model's station list (the collision proxy mesh is not readable at run time), so a shot
  near the edge of a wing or the prow can miss where the model says it hit.
- Part hit points on a fighter are small (a share of 180 hp), so flak strips a fighter's parts quickly. Heat-seekers turn fast (FS2's 0.85 s
  per 360 deg). Flak fireballs close to the lens are still big (the juice's domain).
- **The feel is unverified by hand**: the pad, the rumble and the tones could not be felt in this environment, so they were checked with
  a virtual pad and the logs. There is no radar yet, and the Sky's glare can sit over a target.
- A player's death shows a banner only (the cabin death sequence is a later piece). Wingman orders are reserved, not wired. Drones and
  emplacements are scripted (no AI): the next builder replaces `Gunners.cs` and the drone loops.
- One decision to revisit by hand: the shield bubble scale (0.5), the shield recharge and the power split are tuned numbers, not FS2's.

### Next
The user's playtest verdict on weapon weight, the lock and warning cues, the fight length and the quick menu; then the AI builder drives
ships through `ControlInput` (see `Scripts/Weapons/README.md`), the loadout piece picks two guns and two missile types, and the real
HUD replaces `Monolith.CombatHud`.
