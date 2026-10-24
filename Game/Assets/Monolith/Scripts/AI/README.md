# AI (`Monolith.AI`)

Fighters, bombers, wingmen, capital ships and their turrets, after FS2's `ai_chase`, `ai_big_strafe`, guard, formation and turret code
(studied for behaviour, not copied). It references only Core: every ship is flown through its `ControlInput` (stick, throttle, buttons,
`TargetShip`, `TargetPartPlusOne`, `SelectMissilePlusOne`) and every turret through a `FireRequest`, exactly as the pad and the range's scripts
do (`Scripts/Weapons/README.md`). Data: `Resources/AI/ai.json` (profiles, difficulty tables, tuning, each number with its FS2 source or a note)
and `Resources/AI/radio.json` (placeholder radio lines).

| File | What |
|---|---|
| `AiDirector` | The shared side (sim order just before the pilots): the brains, per-tick lists (capital hulls, fighters, who targets whom, guided weapons in flight from `missile_track`), the player's wing, radio, stats. Sets the difficulty's player damage scales in `GameWorld`. |
| `AiPilot` | One fighter's or bomber's brain (an `IPilotSource`): goals (engage, attack, disable, guard, form, bomb) and modes (fight, evade, extend, missile evasion, peel off, strafe, bomb run, form, escort, recover). |
| `AiSteer`, `AiAim` | Turning to a direction knowing the ship's turn rates and lag (`ShipState.TurnRate`, `TurnLag`), with the aim point's own turn rate fed forward; lead along a turning target's curve. |
| `CapitalAi` | `CapitalPilot` (a capital ship's helm: hold a standoff, keep the enemy capital at a bearing that brings broadside and beams to bear, stop dead with every engine gone) and `TurretGunnery` (every turret part, on FS2's turret rules: see below). |
| `Wing` | The four quick-menu orders (`ControlButtons.OrderAttack/Disable/Cover/Form`), with a radio acknowledgement each. |
| `AiStats`, `AiRadio` | Measurements (time on tail, gun shots and hits, missiles, kills, peels, evasions, hull collisions, stuck time) by skill key; radio lines as `radio_line` events. |

## The four skill knobs (FS2's ai.tbl, design section 7)

Profiles hold accuracy (0..1) and evasion, courage, patience (0..100) per difficulty level; `Resolve` normalises them and adds the level's tables.
- **Accuracy**: FS2's aim error (`(1 - accuracy) x aiming error x aimErrorMetres`, a fresh random offset every ~1 s, up to five times bigger while
  the target has only just come into range); how much of a turning target's curve the pilot reads when leading it; FS2's firing cone
  (`0.90 + accuracy/10 - radius/distance`).
- **Evasion**: the chance to break when hit with the shield down, or when someone sits on its tail (a hard break turn towards the attacker with
  jinks, then the pilot turns on it); how soon and how hard it jinks; reacting to missile warnings; with FS2's per-level countermeasure chance, decoys.
- **Courage**: how hurt a pilot gets before it pulls out to mend (`peelHullNoCourage` 50% hull at 0 to `peelHullFullCourage` 15% at 100). A
  `fearless` profile (Earth's drones, courage zero by design) never pulls out.
- **Patience**: how long a firing line is held before shooting and how far the firing cone narrows to the target's size; leading only from
  closer in; closing slower behind a target (no overshoot); less breaking off when the shots do not land.

Difficulty (FS2's five levels): Earth's pilots turn slower (`turnTimeScale`), fire slower (`hostileFireDelay`; our side `friendlyFireDelay`), aim
from a picture of the target refreshed every `predictDelay` s (FS2's `Skill_level_delay`), and no more than `maxPlayerAttackers` go for the
player at once (`maxPlayerHomers` missiles, and only in `missileChanceOnPlayer` of every seven ten-second windows unless the player is targeting
that pilot, FS2's `check_ok_to_fire`). Missile reloads add FS2's half a second per AI class below the top (`aiClass`, `fs2AiClasses`). The player
takes `playerDamageScale` of weapon damage (`playerPartDamageScale` on parts), and its wingmen `wingDamageScale` (ours). An `unhandicapped`
profile (the ascended ace) ignores the turn, fire and picture handicaps.

## Capital-ship turrets (FS2's turret code, `aiturret.cpp`)

The capital ship is dangerous but beatable by taking its turrets out one by one:
- **Field of fire.** A turret fires only within its arc off its own up (FS2's fov test, `dot + radius / (distance + radius)` against the arc's
  cosine), and only where its own hull is not in the way: `turretArcGun` 80 deg, `turretArcFlak` 70, `turretArcBeam` 100. The corvette keeps lanes
  no gun covers (close along its length off the bow and stern, low over its deck, under its keel); destroying the turrets on one face silences it.
- **Targets.** Guns and flak first shoot down torpedoes coming at their side's capital ships (flak bursts hurt torpedoes); then each turret takes
  the nearest enemy in its arc, every turret of the same ship already on it adding a tenth to its distance (FS2), bombers and ships attacking this
  one counting nearer, and never more of one ship's turrets on one target than `turretsOnTarget` (FS2's 3 + skill squared) or, on the player,
  `turretsOnPlayer` (ours, tighter: 2, 2, 3, 5, 8). The rest turn on the wingmen, the bombers' targets, and, for lasers, an enemy capital in reach.
  A fresh choice every `turretRetargetSeconds` (FS2's two seconds), at once when the target leaves the arc.
- **Aim.** The gunner leads from a picture of its target refreshed at random intervals up to `turretAimUpdate` (longer on easier levels), so a
  weave in between is missed; the error is `range x (1 - accuracy) x turretAimError x aimingError`, `turretFreshError` times bigger when the target
  has just come into the arc, settling over `turretSettleSeconds` while it stays (loitering is punished, passing through is not). Flak goes into
  the target's path, scattered the same way, never where it would burst on our own side; it bursts at its range, not by proximity (FS2).
- **Rate.** The weapon's wait (FS2's Terran Turret, 275 m/s every 1 s; Heavy Flak every 1.5 s) x the level's fire delay x the scene's scale.
- **Beams** keep their three-second charge; they fire only at enemy capital ships, one beam per ship at a time.

Our side's gunners are not handicapped by the level (the aim update and settle are the best level's).

## Using it

```csharp
var ai = new AiDirector(world, AiData.Default, level); world.Sim.Register(ai); world.Sim.Register(new TurretGunnery(ai));
var p = ai.AddPilot(shipId, "drone", AiRole.Fighter);                 // attaches itself as the ship's pilot
p.SetGoal(new AiGoal { Kind = GoalKind.Attack, Target = capitalId, Part = -1 });
ai.AddCapital(capitalId, new CapitalOrders { TurretProfile = "earth_turret", Weapons = { ["SUB_aaf_F01"] = "beam_slash" } });
ai.PlayerWing.Add(ai.AddPilot(wingmanId, "wingman", AiRole.Fighter)); // the quick menu's orders now reach it
```
Remove a dead ship with `ai.Remove(id)` (the dogfight does it from the reaper). The dogfight scene (`Game/Dogfight`) is the worked example.

## Checks

`Monolith.exe -batchmode -nographics -mnAiSelfTest` (37 checks: data, steering, a dogfight to the kill, capital hulls never touched, missiles,
evasion and decoys, peeling off and drones not, a bomber's runs, turrets and silenced turrets, the turret rules (the cap on the player, the rest on
a wingman, a stripped face silent, the blind lane off the bow, lasers on a capital, flak punishing loitering), beams and silenced beams, every
wing order); `-mnAiArena gauntlet/shots/ai_arena.json -mnOut dir` (AI against AI, knob sweeps); `-mnDogfightSoak 3600 -mnOut dir [-mnSoakMortal]`
(the dogfight bare, for an hour: stuck ships, hull collisions, errors, pacing, wins and losses, what hurt the Mercy and the wingmen);
`-mnDogfightSoak 0 -mnWardenRun 10 -mnOut dir [-mnNoFighters] [-mnWingOrder cover|attack|none]` (a mortal AI-flown player stripping the
Warden's turrets: time alive, time within 1 km, turrets destroyed, turrets and shots on it, as stripping thins the fire).
