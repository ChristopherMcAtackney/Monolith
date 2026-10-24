using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Damage
{
    /// <summary>
    /// Shields, hull, parts, power, self-healing and death (order just after <see cref="SimOrder.Collision"/>). It resolves every
    /// <see cref="DamageRequest"/> weapons and collisions queued this tick: a shield bubble takes <c>damage x shieldMult</c> first, what gets
    /// through hurts the hull (<c>x hullMult</c>) and the part nearest the impact (<c>x subsystemMult</c>). Parts that fall below half show a
    /// damaged look, at zero they are destroyed with a real effect (<see cref="CombatFlags"/>, engines capping <see cref="ShipState.SpeedScale"/>,
    /// sensors putting static on the radar and target box, a lost mount taking a gun offline, a silenced turret) and a
    /// <c>subsystem_destroyed</c> event; at zero hull the ship dies and raises <c>ship_destroyed</c> (the juice stages the big ones). It
    /// raises <c>hit_shield</c> and <c>hit_hull</c> for the juice, tells the ship's <see cref="IShipVisual"/> what to show, reads the power
    /// choice from the pilot's buttons, recharges the shield from it and, on the player's side, mends hull and parts after a few quiet seconds.
    /// </summary>
    public sealed class DamageSystem : ISimSystem
    {
        readonly GameWorld _world;
        readonly CombatRules _rules;
        readonly List<DamageRequest> _pending = new List<DamageRequest>(64);
        float _now;

        public DamageSystem(GameWorld world, CombatRules rules = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _rules = rules ?? CombatRules.Default;
        }

        public int Order => SimOrder.Collision + 20;
        public CombatRules Rules => _rules;
        /// <summary>Ships that died so far (the range's score).</summary>
        public int Deaths { get; private set; }

        // ------------------------------------------------------------------------------------------------ registering ships

        /// <summary>
        /// Give a spawned ship its combat state from <paramref name="def"/> (parts, shape, shields, arms, ammunition) and make it
        /// fight. <paramref name="selfHeal"/> defaults to the player's side (team 0). The visual, if any, is told what to show.
        /// </summary>
        public ShipCombat Register(ShipState ship, ShipCombatDef def, IShipVisual visual = null, bool? selfHeal = null)
        {
            if (ship == null) throw new ArgumentNullException(nameof(ship));
            if (def == null) throw new ArgumentNullException(nameof(def));
            var c = ShipCombatFactory.Create(ship, def, _rules, selfHeal ?? ship.Team == 0, visual);
            _world.Combat.Add(c);
            ship.SpeedScale = 1f;
            ship.TargetPart = -1;
            Recompute(c, true);
            visual?.ResetDamage();
            return c;
        }

        public void Unregister(int shipId) => _world.Combat.Remove(shipId);

        /// <summary>Back to factory state at full health and full magazines (respawn, the range's reset).</summary>
        public void Reset(int shipId)
        {
            var c = _world.Combat.Get(shipId);
            if (c == null) return;
            c.Alive = true; c.Killer = GameWorld.NoShip;
            c.Hull = c.HullMax; c.Shield = c.ShieldMax; c.HullShown = 0f; c.HullVisualAccum = 0f;
            c.TimeSinceDamage = c.TimeSinceShieldHit = 999f;
            c.LastAttacker = GameWorld.NoShip;
            foreach (var p in c.Parts) { p.Hp = p.MaxHp; p.ShownHealth01 = 1f; p.Cooldown = 0f; }
            c.Energy = c.EnergyMax;
            foreach (var m in c.Missiles) m.Ammo = m.AmmoMax;
            c.Decoys = c.DecoysMax; c.SelectedMissile = 0;
            c.Lock = LockPhase.None; c.LockProgress01 = 0f; c.LockTarget = GameWorld.NoShip;
            SetPower(c, PowerMode.Balanced, false);
            Recompute(c, true);
            c.Visual?.ResetDamage();
        }

        // ------------------------------------------------------------------------------------------------ the tick

        public void Step(in SimFrame frame)
        {
            _now = frame.Time;
            _pending.Clear();
            _world.Damage.Drain(_pending);
            for (int i = 0; i < _pending.Count; i++)
            {
                try { Apply(_pending[i]); }
                catch (Exception e) { MnLog.Error($"damage request on ship {_pending[i].Ship} threw: {e}"); }
            }
            var ships = _world.Combat.All;
            for (int i = 0; i < ships.Count; i++) Tick(ships[i], frame.Dt);
        }

        void Tick(ShipCombat c, float dt)
        {
            if (!c.Alive || c.Ship == null) return;
            c.TimeSinceDamage += dt;
            c.TimeSinceShieldHit += dt;

            // The power choice: a tap steps through the modes, the quick menu sets one.
            var input = _world.Pilots.Current(c.Id);
            var prev = _world.Pilots.Previous(c.Id);
            if (input.Pressed(prev, ControlButtons.PowerShields)) SetPower(c, PowerMode.Shields, true);
            else if (input.Pressed(prev, ControlButtons.PowerGuns)) SetPower(c, PowerMode.Guns, true);
            else if (input.Pressed(prev, ControlButtons.PowerEngines)) SetPower(c, PowerMode.Engines, true);
            else if (input.Pressed(prev, ControlButtons.PowerBalanced)) SetPower(c, PowerMode.Balanced, true);
            else if (input.Pressed(prev, ControlButtons.Power)) SetPower(c, (PowerMode)(((int)c.Power + 1) % 4), true);

            // The shield refills from the shield share of the power (nothing while the reactor is down, or while it is under fire).
            if (c.ShieldMax > 0f && c.Shield < c.ShieldMax && c.TimeSinceShieldHit >= _rules.ShieldRegenDelay && (c.Flags & CombatFlags.ReactorDown) == 0)
                c.Shield = Mathf.Min(c.ShieldMax, c.Shield + c.ShieldMax * _rules.ShieldRegenPerSecond * c.ShieldPower * dt);

            // Living tech: after a few quiet seconds hull and damaged parts mend, slowly. Destroyed parts stay destroyed.
            if (c.SelfHeals && c.TimeSinceDamage >= _rules.HealDelay)
            {
                if (c.Hull < c.HullMax)
                {
                    c.Hull = Mathf.Min(c.HullMax, c.Hull + c.HullMax * _rules.HealHullPerSecond * dt);
                    UpdateHullVisual(c);
                }
                bool changed = false;
                for (int i = 0; i < c.Parts.Count; i++)
                {
                    var p = c.Parts[i];
                    if (p.Destroyed || p.Hp >= p.MaxHp) continue;
                    p.Hp = Mathf.Min(p.MaxHp, p.Hp + p.MaxHp * _rules.HealPartPerSecond * dt);
                    changed = true;
                    TellVisualPart(c, p);
                }
                if (changed) Recompute(c, false);
            }
        }

        // ------------------------------------------------------------------------------------------------ resolving a hit

        void Apply(in DamageRequest r)
        {
            var c = _world.Combat.Get(r.Ship);
            if (c == null || !c.Alive || c.Ship == null || r.Amount <= 0f) return;
            bool partOnly = r.ShieldMult <= 0f && r.HullMult <= 0f;
            // The difficulty's mercy on the player (FS2: weapon hits on the player's ship are scaled by the skill level; collisions are not).
            float amount = r.Amount, partScale = 1f;
            if (r.Ship == _world.LocalPlayerShip && r.Kind != DamageKind.Collision && r.Kind != DamageKind.Debug)
            {
                amount *= _world.PlayerDamageScale;
                partScale = _world.PlayerPartDamageScale;
            }
            else if (r.Kind != DamageKind.Collision && r.Kind != DamageKind.Debug) amount *= c.DamageTakenScale;
            float remaining = amount;

            if (r.Attacker != GameWorld.NoShip && r.Attacker != c.Id) { c.LastAttacker = r.Attacker; c.LastAttackerTime = _now; }
            c.TimeSinceDamage = 0f;
            c.LastDamagedTime = _now;

            // 1. The shield bubble takes it first.
            if (!r.BypassShield && c.Shield > 0f && r.ShieldMult > 0f)
            {
                float onShield = amount * r.ShieldMult;
                c.TimeSinceShieldHit = 0f;
                if (onShield <= c.Shield)
                {
                    c.Shield -= onShield;
                    GameEvents.RaiseAt(CombatEvents.HitShield, r.Point, r.Direction, r.Weapon, amount, c.Id, r.Attacker);
                    return;
                }
                // It breaks the bubble and the rest goes through.
                remaining = amount * (1f - c.Shield / onShield);
                c.Shield = 0f;
                GameEvents.RaiseAt(CombatEvents.HitShield, r.Point, r.Direction, r.Weapon, amount - remaining, c.Id, r.Attacker);
                GameEvents.RaiseAt(CombatEvents.ShieldDown, r.Point, r.Direction, r.Weapon, 0f, c.Id, r.Attacker);
            }

            // 2. The part nearest the impact.
            int partIndex = r.Part >= 0 && r.Part < c.Parts.Count ? r.Part : r.Part == -2 ? -1 : NearestPart(c, r.Point);
            if (partIndex >= 0 && r.SubsystemMult > 0f) DamagePart(c, c.Parts[partIndex], remaining * r.SubsystemMult * partScale, r.Attacker);

            // 3. The hull.
            if (!partOnly)
            {
                float hullDamage = remaining * r.HullMult;
                if (hullDamage > 0f)
                {
                    c.Hull -= hullDamage;
                    if (c.Invulnerable && c.Hull < 1f) c.Hull = 1f;
                    GameEvents.RaiseAt(CombatEvents.HitHull, r.Point, r.Normal, r.Weapon, hullDamage, c.Id, r.Attacker);
                    c.HullVisualAccum += hullDamage;
                    if (c.Visual != null && c.HullVisualAccum >= c.HullMax * 0.004f + 5f)
                    {
                        float sev = Mathf.Clamp01(c.HullVisualAccum / (c.HullMax * 0.05f + 20f));
                        c.Visual.HullHit(r.Point, r.Normal, 1.2f + 3.5f * sev, sev);
                        c.HullVisualAccum = 0f;
                    }
                    UpdateHullVisual(c);
                }
            }
            if (c.Hull <= 0f && c.Alive && !c.Invulnerable) Kill(c, r.Attacker);
        }

        static int NearestPartInReach(ShipCombat c, Vector3 world, float reach)
        {
            int best = -1; float bestScore = float.MaxValue;
            var ship = c.Ship;
            for (int i = 0; i < c.Parts.Count; i++)
            {
                var p = c.Parts[i];
                if (p.Destroyed) continue;
                float d = Vector3.Distance(p.WorldPosition(ship), world);
                if (d > p.Radius * reach) continue;
                float score = d - p.Radius;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        int NearestPart(ShipCombat c, Vector3 world) => NearestPartInReach(c, world, _rules.PartReach);

        void DamagePart(ShipCombat c, CombatPart p, float amount, int attacker)
        {
            if (p.Destroyed || amount <= 0f) return;
            float before = p.Health01;
            p.Hp -= amount;
            p.LastDamagedTime = _now;
            TellVisualPart(c, p);
            if (p.Destroyed)
            {
                p.Hp = 0f;
                c.Visual?.DestroyPart(p.Name);
                Vector3 pos = p.WorldPosition(c.Ship);
                Vector3 outward = p.WorldForward(c.Ship);
                GameEvents.RaiseAt(CombatEvents.SubsystemDestroyed, pos, outward, p.Name, Mathf.Max(6f, p.Radius * 2.4f), c.Id, attacker);
                Recompute(c, false);
            }
            else if (before >= _rules.PartDamagedBelow && p.Health01 < _rules.PartDamagedBelow)
            {
                GameEvents.RaiseAt(CombatEvents.PartDamaged, p.WorldPosition(c.Ship), Vector3.zero, p.Name, p.Health01, c.Id, attacker);
                Recompute(c, false);
            }
            else Recompute(c, false);
        }

        void TellVisualPart(ShipCombat c, CombatPart p)
        {
            if (c.Visual == null || p.Destroyed) return;
            float h = p.Health01;
            if (Mathf.Abs(h - p.ShownHealth01) < 0.1f && !(h >= 1f && p.ShownHealth01 < 1f)) return;
            p.ShownHealth01 = h;
            c.Visual.SetPartHealth(p.Name, h);
        }

        void UpdateHullVisual(ShipCombat c)
        {
            if (c.Visual == null) return;
            float d = 1f - c.Hull01;
            if (Mathf.Abs(d - c.HullShown) < 0.01f) return;
            c.HullShown = d;
            c.Visual.SetHullDamage(d);
        }

        // ------------------------------------------------------------------------------------------------ death

        void Kill(ShipCombat c, int killer)
        {
            c.Alive = false;
            c.Hull = 0f;
            c.Killer = killer;
            Deaths++;
            var ship = c.Ship;
            GameEvents.RaiseAt(CombatEvents.ShipDestroyed, ship.Position, Vector3.zero, c.SizeClass, ship.Radius, c.Id, killer);
        }

        // ------------------------------------------------------------------------------------------------ what the parts do

        /// <summary>
        /// Work out the effects of the parts that are left: the flags the AI and HUD read, the speed cap from the engines, the sensor static.
        /// </summary>
        void Recompute(ShipCombat c, bool initial)
        {
            var flags = CombatFlags.None;
            float engHp = 0f, engMax = 0f, senHp = 0f, senMax = 0f;
            int launchers = 0, launchersAlive = 0, turrets = 0, turretsAlive = 0, engines = 0, enginesAlive = 0;
            bool primaryMount = false, heavyMount = false, primaryAlive = false, heavyAlive = false;
            foreach (var p in c.Parts)
            {
                bool alive = !p.Destroyed;
                switch (p.Kind)
                {
                    case "engine": engines++; engMax += p.MaxHp; if (alive) { enginesAlive++; engHp += p.Hp; } break;
                    case "sensors": senMax += p.MaxHp; if (alive) senHp += p.Hp; break;
                    case "comms": if (!alive) flags |= CombatFlags.CommsDown; break;
                    case "bridge": if (!alive) flags |= CombatFlags.BridgeDown; break;
                    case "reactor": if (!alive) flags |= CombatFlags.ReactorDown; break;
                    case "launcher": launchers++; if (alive) launchersAlive++; break;
                    case "mount":
                        if (p.Gun == "heavy") { heavyMount = true; heavyAlive |= alive; }
                        else { primaryMount = true; primaryAlive |= alive; }
                        break;
                    default:
                        if (p.IsTurret) { turrets++; if (alive) turretsAlive++; }
                        break;
                }
            }
            if (engines > 0 && enginesAlive == 0) flags |= CombatFlags.EnginesDown;
            else if (engines > 0 && engHp < engMax - 0.01f) flags |= CombatFlags.EnginesCrippled;
            if (senMax > 0f && senHp <= 0f) flags |= CombatFlags.SensorsDown;
            if (launchers > 0 && launchersAlive == 0) flags |= CombatFlags.LaunchersOffline;
            if (turrets > 0 && turretsAlive == 0) flags |= CombatFlags.TurretsSilenced;
            if (primaryMount && !primaryAlive) flags |= CombatFlags.PrimaryOffline;
            if (heavyMount && !heavyAlive) flags |= CombatFlags.HeavyOffline;

            c.Flags = flags;
            c.EngineHealth01 = engMax > 0f ? Mathf.Clamp01(engHp / engMax) : 1f;
            c.SensorStatic = senMax > 0f ? 1f - Mathf.Clamp01(senHp / senMax) : 0f;
            float limp = (c.SizeClass == CombatEvents.SizeFighter || c.SizeClass == CombatEvents.SizeBomber) ? _rules.EngineLimpFraction : 0f;
            float engineScale = engMax > 0f ? 1f - (1f - limp) * (1f - c.EngineHealth01) : 1f;
            c.Ship.SpeedScale = engineScale * (c.Power == PowerMode.Engines ? 1f + _rules.PowerEngineSpeedBonus : 1f);
            if ((flags & CombatFlags.ReactorDown) != 0) c.Shield = 0f;
        }

        // ------------------------------------------------------------------------------------------------ the power choice

        /// <summary>Favour shields, guns or engines (or none): the favoured system recharges faster, the other two slower.</summary>
        public void SetPower(ShipCombat c, PowerMode mode, bool announce)
        {
            if (announce && c.Power == mode) return;
            c.Power = mode;
            float hi = _rules.PowerFavoured, lo = _rules.PowerUnfavoured;
            c.ShieldPower = mode == PowerMode.Balanced ? 1f : mode == PowerMode.Shields ? hi : lo;
            c.GunPower = mode == PowerMode.Balanced ? 1f : mode == PowerMode.Guns ? hi : lo;
            c.EnginePower = mode == PowerMode.Balanced ? 1f : mode == PowerMode.Engines ? hi : lo;
            if (c.Ship != null) Recompute(c, false);
            if (announce) GameEvents.Raise(CombatEvents.PowerChanged, mode.ToString(), (float)(int)mode, c.Id);
        }
    }
}
