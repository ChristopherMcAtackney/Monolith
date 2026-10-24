# Combat: weapons, targeting, damage, collisions, and how an AI drives a ship

Four small systems and a HUD, wired together in `Game/Combat/CombatRig.cs`, all talking through `GameWorld` and `GameEvents` (design section 5).
Shared data types, the weapon table and the ship definitions are in `Core/Combat/` (so the HUD, the AI and the missions can read them without
referencing a system). Numbers are data with sources: `Resources/Combat/weapons.json`, `rules.json`, `ships/<class id>.json`.

| Assembly | Order | Owns |
|---|---|---|
| `Monolith.Targeting` `TargetingSystem` | 200 | `ShipState.Target`, `ShipState.TargetPart` (hostiles cycle, nearest attacker, reticle, parts) |
| `Monolith.Weapons` `WeaponSystem` | 400 | guns, flak, missiles, torpedoes, decoys, locks, missile warnings, turret fire requests; every projectile in flight |
| `Monolith.Collision` `CollisionSystem` | 500 | ship against ship and capital hull: bump, knock, damage |
| `Monolith.Damage` `DamageSystem` | 520 | shields, hull, parts, power choice, self-healing, deaths, what the parts do |
| `Monolith.Targeting` `AimSystem` | 890 | the lead point and target distance for the HUD |
| `Monolith.HudUi` | draws | the HUD (`Scripts/HudUi/README.md`) and the lock and missile-warning stand-in tones |

## One tick

Flight moves ships; `WeaponSystem` reads every ship's `ControlInput` (player pad, AI, script: it cannot tell), fires, and flies the shots with
swept hit tests (a shot's path this tick against the ship's boxes, parts and shield bubble: nothing tunnels at any speed; a shot inherits its
shooter's velocity, as in FS2). A hit becomes a `DamageRequest` in `GameWorld.Damage`; collisions add theirs; `DamageSystem` resolves them the
same tick and raises the events the juice listens for. Nothing is drawn here: `hit_shield`, `hit_hull`, `weapon_fired`, `missile_track`,
`near_miss`, `explosion`, `flak_burst`, `subsystem_destroyed`, `ship_destroyed` (table in `Core/CombatEvents.cs`) are all the juice needs.

## Damage maths

Against a shield, `damage x shieldMult` comes off the shield; what is left goes to the hull as `x hullMult` and, near the impact, to the nearest part
(or the part the shot struck) as `x subsystemMult`. A shield bubble is the FS2 shield total times `shieldBubbleScale` (0.5: FS2 splits it over four
quadrants and fire lands mostly on one). Blasts (missiles, flak, torpedoes) fall off linearly from the inner to the outer radius and spend their
part damage on the nearest parts only, FS2's way (the three nearest to the burst, by surface distance, each falling off with its own distance and
sharing the damage evenly; the rest of the parts within the radius take none), while the hull takes its damage as ever. Flak bursts at the range its turret set (or on a direct strike; `flakProximity` 0, as
FS2's flak), and a burst hurts hostile torpedoes near it as a bolt would, so flak turrets shoot bombs down. The difficulty spares the player's
ship (`GameWorld.PlayerDamageScale`) and, through `ShipCombat.DamageTakenScale`, its wingmen. Collisions bypass the shield: `0.5 x reduced mass x closing^2 x (1 - e^2)` times
`collisionEnergyToDamage`, shared by mass, plus a knock through `GameWorld.Knocks` (flight loosens its damping for up to 2 s).

## Parts and what they do

A ship's parts are `ShipCombat.Parts` (name, kind, ship-local position, radius, hp). Hit points are a share of the hull, FS2's way. Kinds:
`engine` (damaged engines cap `ShipState.SpeedScale`, flight reads it; none left: a fighter limps at 30%, a capital drifts), `sensors` (static on the
radar and target box: `ShipCombat.SensorStatic`), `mount` with `Gun` primary or heavy (lost: that gun is offline), `launcher` (all lost: no missiles),
`flak` / `aaf` / `turret` (a turret: lost, it is silenced), `comms`, `bridge`, `reactor` (lost: no shield, little weapon energy). `ShipCombat.Flags`
says what has gone. Damaged parts mend on the player's side after `healDelay`; destroyed ones never do (a hand repair, a cabin piece).

## How an AI drives a ship

Everything through `ControlInput` (see `Core/ControlInput.cs`):

```csharp
var s = world.Ships.Get(me); var c = world.Combat.Get(me);                  // this ship, and its combat state
input.TargetShip = victim.Id; input.TargetPartPlusOne = part.Index + 1;     // pick a target (and a part) without the player's buttons
input.Buttons |= ControlButtons.PrimaryFire | ControlButtons.SecondaryFire; // fast gun, heavy gun (held)
input.SelectMissilePlusOne = 2; input.Buttons |= ControlButtons.MissileFire;  // pick a slot; a lock-on weapon fires once c.Lock == LockPhase.Locked
input.Buttons |= ControlButtons.Countermeasure;                             // release a decoy (react to c.IncomingMissiles, c.IncomingEta)
// Aim with Geo.SolveIntercept(s.Position, s.Velocity, target.Position, target.Velocity, c.PrimarySpeed, out dir, out t, out meet): dir is the
// direction to point the nose (shots take the ship's velocity); c.LeadPoint / c.HasLead are the same thing for the ship's current target.
```

Turrets and anything not driven by buttons: `world.Fire.Add(new FireRequest { Ship = id, Part = partIndex, Weapon = "flak_gun", AimPoint = p, Target = t })`
(the part must be alive; its cooldown is the weapon's wait). `Game/Range/Gunners.cs` is a small worked example (emplacements flown through `ControlInput`,
a capital's turrets through `FireRequest`). State to read: `ShipCombat` (hull, shield, energy, ammo, lock, incoming missiles, parts, flags,
`LastAttacker`); `Teams.Hostile(a, b)` says who fights whom. Bring a ship into a fight with `CombatRig.Spawn(state, defId, visual, transform, juice,
modelParts, hullScale)`; a class with no `Resources/Combat/ships/<id>.json` gets a plain fallback. Dead ships are removed by `ShipReaper` a tick later.

**Heavy beams** are turret weapons too (`kind: "beam"` in `weapons.json`; `beam_slash` is FS2's TerSlash): a `FireRequest` naming a beam and a
target ship starts the charge (`beam_charge`, the weapon's warm-up), then it burns for its life (`beam_fire`), sweeping a line along the face of
the target's hull that looks at the muzzle and raising `beam_aim` every tick so the juice draws it where it is, then `beam_end`. Every damage
interval it hurts the first hostile hull on its line (a fighter that flies through it included) and cuts every part within `beamCutRadius` of
where it touches (as wide as the juice draws it, so what visibly goes is gone). One beam per ship at a time; the turret is busy for the charge,
the burn and the fire wait; destroying the turret stops the beam. `WeaponSystem.Beams` lists them, `ClearAll()` empties the field (a reset).

The game's AI (`Scripts/AI/README.md`) is the full worked example: fighters, bombers and wingmen through `ControlInput`, every capital turret and
beam through `FireRequest`.

## Showing damage: the visual hook (`Core/Combat/ShipCombat.cs`, `IShipVisual`)

Combat owns every rule and hit point; a ship's visual only shows the result. `IShipVisual`: `SetPartHealth(name, 0..1)` (damaged look below half),
`DestroyPart(name)` (the wreck and its debris), `HullHit(point, normal, radius, severity)` (a scorch), `SetHullDamage(0..1)` (accumulated damage:
more and bigger scorches, then breaches), `ResetDamage()`. Part names are the model's own (`SUB_flak_P02`). `Game/Combat/ShipVisuals.cs` adapts the
art side's `ShipDamage` (`IShipDamage`, see `art_src/PIPELINE.md`) to it and reads a model's subsystems as combat parts (`ShipArtVisual.Parts`);
placeholders get `PlaceholderVisual`. When the juice is given the model's `ShipArtJuiceDamage` it draws hull scorches and part wrecks itself on the
events, and `ShipArtVisual` then leaves those two to it.

## Checks

`Monolith.exe -batchmode -nographics -mnCombatSelfTest` (115 checks on the real sim: fire rates, speeds, damage maths, no tunnelling, lead,
shield recharge, power, engines and sensors and mounts, healing, locks, missiles, decoys, torpedoes shot down, blasts and the three nearest parts, collisions, targeting, deaths);
`-mnScene range -mnPadDrive` presses a virtual pad (42 checks, on a range with no emplacements or turrets: their fire would shove the ship mid-step); `python -I tools/combat/check_weapons.py` compares every number copied from FS2 with
FS2's own tables (104 of 104). The range scene: `-mnScene range`; capture plans `"scene": "range"` (steps in `Game/Range/RangeScript.cs`).
