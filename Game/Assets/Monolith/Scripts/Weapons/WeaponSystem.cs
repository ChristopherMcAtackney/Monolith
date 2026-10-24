using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Weapons
{
    public enum ProjectileKind { Bolt, Flak, Missile, Decoy }

    /// <summary>One shot, missile or decoy in flight (read-only to everyone but the weapons system).</summary>
    public sealed class Projectile
    {
        public int Id;
        public ProjectileKind Kind;
        public WeaponDef Def;
        public int Owner, Team;
        public Vector3 Pos, PrevPos, Vel;
        public float Age, Life;
        public bool Alive, JustSpawned;
        // Guided.
        public int Target = GameWorld.NoShip;
        public int TargetPart = -1;
        /// <summary>Fooled by a decoy: it flies on to where the decoy was and goes off there (it does not look for its old target again).</summary>
        public bool Diverted;
        public int DecoyId;
        public Vector3 DecoyPoint, DecoyVel;
        public float Hp;
        public float OutOfCone, ReacquireIn;
        public bool Warned, Passed;
        // Flak.
        public float FuseTime;
    }

    /// <summary>
    /// Guns, flak, missiles, torpedoes and decoys (order <see cref="SimOrder.Weapons"/>). It reads every ship's <see cref="ControlInput"/> buttons
    /// (player, AI or script alike) and the ship's <see cref="ShipCombat"/>, fires, and flies the projectiles on the fixed tick with swept
    /// hit tests, so nothing tunnels at any speed. A hit becomes a <see cref="DamageRequest"/> (resolved the same tick by the Damage system);
    /// the effects come from <see cref="CombatEvents"/> the shot raises (<c>weapon_fired</c>, <c>missile_track</c>, <c>near_miss</c>, <c>explosion</c>,
    /// <c>flak_burst</c>). Turrets and mounts not driven by buttons fire through <see cref="GameWorld.Fire"/>.
    /// Each shot inherits its shooter's velocity (as in FS2), so the lead indicator and the bolt agree.
    /// </summary>
    public sealed partial class WeaponSystem : ISimSystem
    {
        readonly GameWorld _world;
        readonly CombatRules _rules;
        readonly List<Projectile> _proj = new List<Projectile>(256);
        readonly Stack<Projectile> _pool = new Stack<Projectile>();
        readonly List<FireRequest> _requests = new List<FireRequest>();
        readonly List<Projectile> _torpedoes = new List<Projectile>(8);
        int _nextId = 1;
        uint _rng;
        float _now;

        /// <param name="seed">Seeds the decoy rolls (tests vary it; the game uses the default so runs repeat).</param>
        public WeaponSystem(GameWorld world, CombatRules rules = null, uint seed = 0x2545F491u)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _rules = rules ?? CombatRules.Default;
            seed = SimSeed.Apply(seed);
            _rng = seed == 0 ? 1u : seed;
        }

        public int Order => SimOrder.Weapons;
        public IReadOnlyList<Projectile> Projectiles => _proj;
        public int ShotsFired { get; private set; }
        public int ShotsHit { get; private set; }

        float Rand01()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        Projectile Rent()
        {
            var p = _pool.Count > 0 ? _pool.Pop() : new Projectile();
            p.Id = _nextId++;
            p.Alive = true; p.JustSpawned = true; p.Age = 0f;
            p.Target = GameWorld.NoShip; p.TargetPart = -1; p.Diverted = false; p.DecoyId = 0; p.Hp = 0f;
            p.OutOfCone = 0f; p.ReacquireIn = 0f; p.Warned = false; p.Passed = false; p.FuseTime = 0f;
            _proj.Add(p);
            return p;
        }

        // ------------------------------------------------------------------------------------------------ the tick

        public void Step(in SimFrame frame)
        {
            float dt = frame.Dt;
            _now = frame.Time;
            var ships = _world.Combat.All;

            for (int i = 0; i < ships.Count; i++)
            {
                var c = ships[i];
                c.IncomingMissiles = 0; c.IncomingNearest = float.MaxValue; c.IncomingEta = float.MaxValue;
                c.BeingLocked = 0; c.LockedOnMe = false;
            }
            for (int i = 0; i < ships.Count; i++)
            {
                var c = ships[i];
                for (int k = 0; k < c.Parts.Count; k++) if (c.Parts[k].Cooldown > 0f) c.Parts[k].Cooldown -= dt;
                if (c.Alive && c.Ship != null) StepShip(c, dt);
            }
            DrainRequests();
            StepBeams(dt);
            StepProjectiles(dt);
            FinishWarnings(ships);
        }

        // ------------------------------------------------------------------------------------------------ one ship's arms

        void StepShip(ShipCombat c, float dt)
        {
            var ship = c.Ship;
            var input = _world.Pilots.Current(ship.Id);
            var prev = _world.Pilots.Previous(ship.Id);

            float recharge = c.EnergyRechargePerSecond * c.GunPower * ((c.Flags & CombatFlags.ReactorDown) != 0 ? _rules.ReactorDownEnergy : 1f);
            c.Energy = Mathf.Min(c.EnergyMax, c.Energy + recharge * dt);
            c.PrimaryOnline = c.PrimaryGun != null && (c.Flags & CombatFlags.PrimaryOffline) == 0;
            c.HeavyOnline = c.HeavyGun != null && (c.Flags & CombatFlags.HeavyOffline) == 0;

            if (input.SelectMissilePlusOne > 0 && input.SelectMissilePlusOne <= c.Missiles.Count) c.SelectedMissile = input.SelectMissilePlusOne - 1;
            if (input.Pressed(prev, ControlButtons.CycleMissile)) CycleMissile(c);
            UpdateLock(c, ship, dt);

            if (input.Held(ControlButtons.PrimaryFire) && c.PrimaryOnline) FireGun(c, ship, c.PrimaryGun, c.PrimaryHardpoints, ref c.PrimaryNext, ref c.PrimaryReadyAt);
            if (input.Held(ControlButtons.SecondaryFire) && c.HeavyOnline) FireGun(c, ship, c.HeavyGun, c.HeavyHardpoints, ref c.HeavyNext, ref c.HeavyReadyAt);
            if (input.Held(ControlButtons.MissileFire)) FireMissile(c, ship);
            if (input.Held(ControlButtons.Countermeasure)) ReleaseDecoy(c, ship);
        }

        static bool Ready(float now, float readyAt) => now >= readyAt - 1e-4f;

        /// <summary>The next ready slot with ammunition after the current one (a stale selection falls forward too).</summary>
        public static void CycleMissile(ShipCombat c)
        {
            int n = c.Missiles.Count;
            if (n == 0) return;
            for (int k = 1; k <= n; k++)
            {
                int i = (c.SelectedMissile + k) % n;
                if (c.Missiles[i].Ammo > 0) { c.SelectedMissile = i; return; }
            }
            c.SelectedMissile = (c.SelectedMissile + 1) % n;       // all empty: still cycle so the HUD shows it
        }

        // ------------------------------------------------------------------------------------------------ guns

        void FireGun(ShipCombat c, ShipState ship, string weaponId, Vector3[] hardpoints, ref int next, ref float readyAt)
        {
            var def = WeaponCatalog.Get(weaponId);
            if (def == null) return;
            if (!Ready(_now, readyAt)) return;
            if (c.Energy < def.Energy) return;                         // dry: the HUD shows the empty energy bar
            // Firing on schedule keeps the rate exact (no drift); a gap restarts the clock.
            readyAt = (_now - readyAt < Sim.Dt * 1.5f && readyAt > 0f) ? readyAt + def.FireWait : _now + def.FireWait;
            c.Energy -= def.Energy;

            Vector3 local = hardpoints != null && hardpoints.Length > 0 ? hardpoints[next++ % hardpoints.Length] : new Vector3(0f, 0f, ship.Radius * 0.4f);
            Vector3 origin = ship.Position + ship.Rotation * local;
            Vector3 aimAt = ConvergencePoint(ship);
            Vector3 dir = aimAt - origin;
            dir = dir.sqrMagnitude < 1e-4f ? ship.Forward : dir.normalized;
            Spawn(def, ship, origin, dir, aimAt, ship.Target);
        }

        /// <summary>Where the guns converge: at the target's range if it is roughly ahead, else a default range.</summary>
        Vector3 ConvergencePoint(ShipState ship)
        {
            float range = _rules.GunConvergenceRange;
            var t = _world.Ships.Get(ship.Target);
            if (t != null)
            {
                Vector3 to = t.Position - ship.Position;
                float along = Vector3.Dot(to, ship.Forward);
                if (along > 60f && Vector3.Angle(ship.Forward, to) < 25f) range = Mathf.Clamp(along, 120f, 3500f);
            }
            return ship.Position + ship.Forward * range;
        }

        /// <summary>Fire a gun or flak shell from a point along a direction (the shot takes the ship's velocity as well).</summary>
        Projectile Spawn(WeaponDef def, ShipState ship, Vector3 origin, Vector3 dir, Vector3 aimAt, int target)
        {
            var p = Rent();
            p.Kind = def.Kind == WeaponKind.Flak ? ProjectileKind.Flak : ProjectileKind.Bolt;
            p.Def = def; p.Owner = ship.Id; p.Team = ship.Team;
            p.Pos = p.PrevPos = origin;
            p.Vel = ship.Velocity + dir * def.Speed;
            p.Life = def.Life;
            if (p.Kind == ProjectileKind.Flak) p.FuseTime = Mathf.Clamp(Vector3.Distance(origin, aimAt) / def.Speed, 0.15f, def.Life);
            ShotsFired++;
            Vector3 v = p.Vel;
            GameEvents.RaiseAt(CombatEvents.WeaponFired, origin, v.normalized, def.EventId, v.magnitude, ship.Id, target);
            return p;
        }

        // ------------------------------------------------------------------------------------------------ turrets and scripted fire

        void DrainRequests()
        {
            _requests.Clear();
            _world.Fire.Drain(_requests);
            foreach (var r in _requests)
            {
                var c = _world.Combat.Get(r.Ship);
                if (c == null || !c.Alive || c.Ship == null) continue;
                var def = WeaponCatalog.Get(r.Weapon);
                if (def == null) { MnLog.Warn($"fire request for unknown weapon '{r.Weapon}'"); continue; }
                Vector3 origin;
                CombatPart part = null;
                if (r.Part >= 0)
                {
                    if (r.Part >= c.Parts.Count) continue;
                    part = c.Parts[r.Part];
                    if (part.Destroyed || part.Cooldown > 0f) continue;
                    origin = part.WorldPosition(c.Ship) + part.WorldForward(c.Ship) * (part.Radius * 0.5f);
                }
                else origin = c.Ship.Position + c.Ship.Rotation * r.Origin;
                if (def.IsBeam) { StartBeam(r, c, part, def); continue; }
                Vector3 dir = r.AimPoint - origin;
                dir = dir.sqrMagnitude < 1e-4f ? c.Ship.Forward : dir.normalized;
                if (def.IsGuided)
                {
                    var slot = c.Missiles.Find(m => m.Weapon == def.Id);
                    if (slot != null && (slot.Ammo <= 0 || !Ready(_now, slot.ReadyAt))) continue;
                    if (slot != null) { slot.Ammo--; slot.ReadyAt = _now + def.FireWait; }
                    LaunchMissile(def, c, origin, dir, r.Target, -1);
                }
                else Spawn(def, c.Ship, origin, dir, r.AimPoint, r.Target);
                if (part != null) part.Cooldown = def.FireWait;
            }
        }
    }
}
