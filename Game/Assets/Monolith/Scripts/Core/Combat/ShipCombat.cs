using System;
using System.Collections.Generic;
using UnityEngine;

namespace Monolith.Core
{
    /// <summary>The power choice (design section 5): one quick pick that favours shields, guns or engines, or none of them.</summary>
    public enum PowerMode { Balanced = 0, Shields = 1, Guns = 2, Engines = 3 }

    public enum LockPhase { None = 0, Acquiring = 1, Locked = 2 }

    /// <summary>What the loss of parts has done to a ship, for the AI, the HUD and the mission system to read.</summary>
    [Flags]
    public enum CombatFlags
    {
        None = 0,
        EnginesDown = 1 << 0,        // every engine destroyed: the ship limps (fighters) or drifts (capitals)
        EnginesCrippled = 1 << 1,    // some engines gone: speed capped
        SensorsDown = 1 << 2,        // static on the radar and target box
        CommsDown = 1 << 3,
        BridgeDown = 1 << 4,
        ReactorDown = 1 << 5,        // no shield recharge, weapon energy barely comes back
        PrimaryOffline = 1 << 6,     // the fast gun's mount is destroyed
        HeavyOffline = 1 << 7,       // the heavy gun's mount is destroyed
        LaunchersOffline = 1 << 8,   // every missile launcher destroyed
        TurretsSilenced = 1 << 9,    // every turret destroyed
    }

    /// <summary>A box of a ship's hull for hit tests, in the ship's own frame.</summary>
    [Serializable]
    public struct HitBox
    {
        public Vector3 Centre, Half;
    }

    /// <summary>The simple solid a ship presents to bolts and to other ships: a few boxes and (for shielded ships) an ellipsoid bubble.</summary>
    public sealed class HitShape
    {
        public HitBox[] Boxes = new HitBox[0];
        /// <summary>Zero radii: no shield bubble.</summary>
        public Vector3 ShieldCentre, ShieldRadii;
        public float BoundRadius;
        public bool HasBubble => ShieldRadii.x > 0.01f;

        /// <summary>The bubble the juice draws round a fighter, from the hull's extents (the same proportions JuiceDirector.RegisterShip uses).</summary>
        public static Vector3 BubbleFor(Vector3 halfExtents) =>
            new Vector3(Mathf.Max(halfExtents.x * 1.55f, 3f), Mathf.Max(halfExtents.y * 1.9f, halfExtents.x * 0.9f, 2.6f), Mathf.Max(halfExtents.z * 1.4f, 4f));
    }

    /// <summary>One destroyable part of a ship (owner: Damage; <see cref="Cooldown"/>: Weapons).</summary>
    public sealed class CombatPart
    {
        public int Index;
        /// <summary>Unique on the ship: a model's "SUB_flak_P02", or "engine_port".</summary>
        public string Name;
        /// <summary>engine, sensors, comms, bridge, reactor, launcher, mount (a gun's mount), or a turret kind: flak, aaf, turret.</summary>
        public string Kind;
        public Vector3 LocalPosition, LocalForward = Vector3.forward;
        public float Radius;
        public float Hp, MaxHp;
        /// <summary>For a gun mount: "primary" or "heavy".</summary>
        public string Gun;
        /// <summary>For a turret: the weapon it fires.</summary>
        public string Weapon;
        /// <summary>Seconds until a turret may fire again (owner: whoever fires the turret).</summary>
        public float Cooldown;
        public float LastDamagedTime = -999f;
        /// <summary>The health last told to the visual (it is only told about changes of a tenth or more).</summary>
        public float ShownHealth01 = 1f;

        public bool Destroyed => Hp <= 0f;
        public float Health01 => MaxHp > 0f ? Mathf.Clamp01(Hp / MaxHp) : 0f;
        public bool IsTurret => Kind == "flak" || Kind == "aaf" || Kind == "turret";
        public Vector3 WorldPosition(ShipState ship) => ship.Position + ship.Rotation * LocalPosition;
        public Vector3 WorldForward(ShipState ship) => ship.Rotation * LocalForward;
    }

    /// <summary>One slot of limited ammunition on a ship: a missile type.</summary>
    public sealed class MissileSlot
    {
        public string Weapon, Name;
        public int Ammo, AmmoMax;
        /// <summary>Sim time this launcher may fire again.</summary>
        public float ReadyAt;
    }

    /// <summary>
    /// Everything combat publishes about one ship, once per tick, for everyone else to read (HUD, targeting, the AI, missions). Each
    /// group of fields names its one writer; the rest only read. Created by <c>DamageSystem.Register</c>, looked up by ship id in
    /// <see cref="GameWorld.Combat"/>.
    /// </summary>
    public sealed class ShipCombat
    {
        public int Id;
        public ShipState Ship;
        public string DefId, SizeClass;

        // ------------------------------------------------------------------ defence (owner: Damage)
        public bool Alive = true;
        /// <summary>Debug / mission flag: damage is taken and shown but never kills.</summary>
        public bool Invulnerable;
        /// <summary>The difficulty's mercy on this ship (x every weapon hit it takes; 1 for most ships). The AI sets it for the player's wingmen.</summary>
        public float DamageTakenScale = 1f;
        public float Hull, HullMax, Shield, ShieldMax;
        /// <summary>Seconds since the ship last took damage (self-healing waits on this).</summary>
        public float TimeSinceDamage = 999f;
        public float TimeSinceShieldHit = 999f;
        /// <summary>True for the player's side: the diaspora's living tech mends hull and parts after a few quiet seconds.</summary>
        public bool SelfHeals;
        public PowerMode Power = PowerMode.Balanced;
        /// <summary>What the power choice does right now: recharge multipliers for shields, guns and engines.</summary>
        public float ShieldPower = 1f, GunPower = 1f, EnginePower = 1f;
        public CombatFlags Flags;
        /// <summary>0..1: how much static the sensors put on the radar and target box (0 = clear, 1 = sensors gone).</summary>
        public float SensorStatic;
        public float EngineHealth01 = 1f;
        public int Killer = GameWorld.NoShip;
        /// <summary>The last ship that hurt this one and when (sim time); "target nearest attacker" uses it.</summary>
        public int LastAttacker = GameWorld.NoShip;
        public float LastAttackerTime = -999f;
        public HitShape Shape = new HitShape();
        public readonly List<CombatPart> Parts = new List<CombatPart>();
        public IShipVisual Visual;
        /// <summary>Seconds of simulated time this ship was last hurt (any damage).</summary>
        public float LastDamagedTime = -999f;
        /// <summary>Bookkeeping for the visual: hull damage shown so far, and hull damage taken since the last scorch was drawn (owner: Damage).</summary>
        public float HullShown, HullVisualAccum;

        public float Hull01 => HullMax > 0f ? Mathf.Clamp01(Hull / HullMax) : 0f;
        public float Shield01 => ShieldMax > 0f ? Mathf.Clamp01(Shield / ShieldMax) : 0f;

        // ------------------------------------------------------------------ arms (owner: Weapons)
        /// <summary>Weapon ids, or null when the ship has no such gun.</summary>
        public string PrimaryGun, HeavyGun;
        public string PrimaryName, HeavyName;
        public Vector3[] PrimaryHardpoints = new Vector3[0], HeavyHardpoints = new Vector3[0];
        /// <summary>Muzzle speeds (m/s), for the lead indicator.</summary>
        public float PrimarySpeed, HeavySpeed;
        public bool PrimaryOnline, HeavyOnline;
        public float Energy, EnergyMax, EnergyRechargePerSecond;
        public readonly List<MissileSlot> Missiles = new List<MissileSlot>();
        public int SelectedMissile;
        public int Decoys, DecoysMax;
        /// <summary>Sim time the next decoy may be released.</summary>
        public float DecoyReadyAt;
        public float PrimaryReadyAt, HeavyReadyAt;
        /// <summary>Which hardpoint fires next (guns alternate).</summary>
        public int PrimaryNext, HeavyNext;
        /// <summary>Lock-on progress on the targeted ship for the selected missile.</summary>
        public LockPhase Lock;
        public float LockProgress01;
        public int LockTarget = GameWorld.NoShip;
        /// <summary>The weapon the lock above belongs to (changing missile type starts again).</summary>
        public string LockWeapon;
        /// <summary>Seconds the target has been out of the lock cone (a made lock survives a moment of that).</summary>
        public float LockLostFor;
        /// <summary>Hostile missiles flying at this ship, the distance of the nearest and seconds until it arrives.</summary>
        public int IncomingMissiles;
        public float IncomingNearest, IncomingEta;
        /// <summary>World direction from this ship to the nearest incoming missile (the warning shows which way to look).</summary>
        public Vector3 IncomingDirection;
        /// <summary>Hostile ships currently building a lock on this one (they hold it in their sights).</summary>
        public int BeingLocked;
        public bool LockedOnMe;

        public MissileSlot SelectedSlot => SelectedMissile >= 0 && SelectedMissile < Missiles.Count ? Missiles[SelectedMissile] : null;

        // ------------------------------------------------------------------ aim (owner: Targeting)
        /// <summary>Where to aim the nose with the primary gun to hit the targeted ship (world space), valid when <see cref="HasLead"/>.</summary>
        public bool HasLead;
        public Vector3 LeadPoint;
        public float LeadTime, TargetDistance;
        public bool TargetHostile;

        public CombatPart FindPart(string name)
        {
            for (int i = 0; i < Parts.Count; i++) if (Parts[i].Name == name) return Parts[i];
            return null;
        }

        /// <summary>The first living part of a kind, or null.</summary>
        public CombatPart FirstPart(string kind)
        {
            for (int i = 0; i < Parts.Count; i++) if (Parts[i].Kind == kind && !Parts[i].Destroyed) return Parts[i];
            return null;
        }
    }

    /// <summary>Every ship's combat state by ship id (owner: Damage adds and removes).</summary>
    public sealed class CombatRegistry
    {
        readonly Dictionary<int, ShipCombat> _byId = new Dictionary<int, ShipCombat>();
        readonly List<ShipCombat> _all = new List<ShipCombat>();

        public IReadOnlyList<ShipCombat> All => _all;
        public int Count => _all.Count;

        public void Add(ShipCombat c)
        {
            if (_byId.ContainsKey(c.Id)) throw new ArgumentException($"ship {c.Id} already has combat state");
            _byId.Add(c.Id, c);
            _all.Add(c);
        }

        public bool Remove(int id)
        {
            if (!_byId.TryGetValue(id, out var c)) return false;
            _byId.Remove(id);
            _all.Remove(c);
            return true;
        }

        public ShipCombat Get(int id) => id != GameWorld.NoShip && _byId.TryGetValue(id, out var c) ? c : null;
    }

    public enum DamageKind { Projectile, Blast, Collision, Beam, Debug }

    /// <summary>
    /// A request to hurt a ship, queued by weapons and collisions and resolved by the Damage system the same tick: shields first, then the
    /// hull and (near the impact) a part. Multipliers come from the weapon (<see cref="WeaponDef"/>).
    /// </summary>
    public struct DamageRequest
    {
        public int Ship, Attacker;
        public string Weapon;
        public float Amount;
        public float ShieldMult, HullMult, SubsystemMult;
        public Vector3 Point, Direction, Normal;
        /// <summary>The part the projectile struck directly, or -1 (then the nearest part to the impact point takes the part damage).</summary>
        public int Part;
        public DamageKind Kind;
        /// <summary>Hits that go round the shield: collisions, and the leftover of a hit that already broke through.</summary>
        public bool BypassShield;
        /// <summary>The point is on the shield bubble (not the hull): events say shield or hull accordingly.</summary>
        public bool OnShield;
    }

    /// <summary>Pending damage (owner: anyone adds; the Damage system drains each tick).</summary>
    public sealed class DamageQueue
    {
        readonly List<DamageRequest> _pending = new List<DamageRequest>();
        public int Count => _pending.Count;
        public void Add(in DamageRequest r) => _pending.Add(r);
        public void Drain(List<DamageRequest> into) { into.AddRange(_pending); _pending.Clear(); }
    }

    /// <summary>
    /// A request to fire a weapon from a ship that is not driven by its pilot's buttons: a turret on a part, a scripted launcher. The weapons
    /// system checks the part is alive, takes the cooldown, and fires from the part's position towards <see cref="AimPoint"/>.
    /// </summary>
    public struct FireRequest
    {
        public int Ship;
        /// <summary>Index into <see cref="ShipCombat.Parts"/> to fire from, or -1 to fire from <see cref="Origin"/> (ship-local).</summary>
        public int Part;
        public string Weapon;
        public Vector3 Origin;
        /// <summary>World point to aim at (the caller applies any lead).</summary>
        public Vector3 AimPoint;
        /// <summary>For guided weapons: the ship to home on.</summary>
        public int Target;
    }

    /// <summary>Pending turret / mount fire requests (owner: anyone adds; Weapons drains).</summary>
    public sealed class FireQueue
    {
        readonly List<FireRequest> _pending = new List<FireRequest>();
        public int Count => _pending.Count;
        public void Add(in FireRequest r) => _pending.Add(r);
        public void Drain(List<FireRequest> into) { into.AddRange(_pending); _pending.Clear(); }
    }

    /// <summary>
    /// What the art side implements so combat can show damage (the contract is in art_src/PIPELINE.md's "Subsystems, destruction and
    /// damage"; Game's adapters wrap ShipArt's <c>IShipDamage</c> and the placeholder ships). Combat owns every rule and hit point; this
    /// only shows the result. Names are the combat part names (a model's SUB_ names).
    /// </summary>
    public interface IShipVisual
    {
        /// <summary>A part's health changed: 1 intact, 0 destroyed. Below the damaged threshold a model may show a half-wrecked look.</summary>
        void SetPartHealth(string partName, float health01);
        /// <summary>A part was destroyed: show the wreck, throw debris, stop its lights.</summary>
        void DestroyPart(string partName);
        /// <summary>A hit on the hull: scorch the plating there.</summary>
        void HullHit(Vector3 worldPoint, Vector3 worldNormal, float radiusMetres, float severity01);
        /// <summary>The hull's overall damage 0..1: more and bigger scorches, breaches, vents.</summary>
        void SetHullDamage(float damage01);
        /// <summary>Back to a factory-fresh ship.</summary>
        void ResetDamage();
    }
}
