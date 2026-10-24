using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Weapons
{
    public sealed partial class WeaponSystem
    {
        // ------------------------------------------------------------------------------------------------ lock-on

        /// <summary>
        /// Builds, holds and drops the lock for the selected missile on the targeted ship. A lock-on weapon needs <see cref="WeaponDef.LockTime"/>
        /// seconds with the target inside <see cref="WeaponDef.LockCone"/> degrees of the nose and inside its range; leaving the cone drains a
        /// lock being built faster than it built, and a made lock survives a moment of it. A heat-seeker needs no lock: it reports "locked"
        /// whenever the target is inside its view cone (the seeker tone).
        /// </summary>
        void UpdateLock(ShipCombat c, ShipState ship, float dt)
        {
            var slot = c.SelectedSlot;
            var def = slot != null && slot.Ammo > 0 ? WeaponCatalog.Get(slot.Weapon) : null;
            var target = _world.Ships.Get(ship.Target);
            var tc = _world.Combat.Get(ship.Target);
            if (def == null || !def.IsGuided || target == null || tc == null || !tc.Alive || !Teams.Hostile(ship.Team, target.Team))
            {
                DropLock(c, def);
                return;
            }
            if (c.LockWeapon != def.Id)
            {
                // A different missile type: the tone and the lock start again (a heat seeker's seek tone is not a lock-on lock).
                c.LockWeapon = def.Id; c.Lock = LockPhase.None; c.LockProgress01 = 0f; c.LockTarget = GameWorld.NoShip; c.LockLostFor = 0f;
            }
            Vector3 to = target.Position - ship.Position;
            float range = to.magnitude;
            float angle = Vector3.Angle(ship.Forward, to);
            bool ok = range <= def.Range * 1.02f && angle <= (def.NeedsLock ? def.LockCone : def.ViewCone * 0.5f);

            if (!def.NeedsLock)
            {
                c.LockTarget = ok ? target.Id : GameWorld.NoShip;
                c.Lock = ok ? LockPhase.Locked : LockPhase.None;
                c.LockProgress01 = ok ? 1f : 0f;
                return;
            }

            if (c.LockTarget != target.Id)
            {
                c.LockTarget = target.Id; c.LockProgress01 = 0f; c.Lock = LockPhase.None; c.LockLostFor = 0f;
            }
            if (ok)
            {
                c.LockLostFor = 0f;
                float before = c.LockProgress01;
                c.LockProgress01 = Mathf.Min(1f, c.LockProgress01 + dt / def.LockTime);
                var was = c.Lock;
                c.Lock = c.LockProgress01 >= 1f ? LockPhase.Locked : LockPhase.Acquiring;
                if (was != LockPhase.Locked && c.Lock == LockPhase.Locked)
                    GameEvents.RaiseAt(CombatEvents.LockAcquired, target.Position, Vector3.zero, def.Id, 0f, ship.Id, target.Id);
            }
            else if (c.Lock == LockPhase.Locked)
            {
                c.LockLostFor += dt;
                if (c.LockLostFor > _rules.LockLoseSeconds) { LoseLock(c, ship, def, target.Id); }
            }
            else if (c.Lock == LockPhase.Acquiring)
            {
                c.LockProgress01 -= dt * _rules.LockDecayMultiplier / def.LockTime;
                if (c.LockProgress01 <= 0f) { c.LockProgress01 = 0f; c.Lock = LockPhase.None; }
            }
            if (c.Lock != LockPhase.None)
            {
                var victim = tc;
                victim.BeingLocked++;
                if (c.Lock == LockPhase.Locked) victim.LockedOnMe = true;
            }
        }

        void LoseLock(ShipCombat c, ShipState ship, WeaponDef def, int target)
        {
            c.Lock = LockPhase.None; c.LockProgress01 = 0f; c.LockLostFor = 0f;
            GameEvents.RaiseAt(CombatEvents.LockLost, ship.Position, Vector3.zero, def.Id, 0f, ship.Id, target);
        }

        static void DropLock(ShipCombat c, WeaponDef def)
        {
            c.Lock = LockPhase.None; c.LockProgress01 = 0f; c.LockTarget = GameWorld.NoShip; c.LockLostFor = 0f; c.LockWeapon = null;
        }

        // ------------------------------------------------------------------------------------------------ launching

        void FireMissile(ShipCombat c, ShipState ship)
        {
            var slot = c.SelectedSlot;
            if (slot == null || slot.Ammo <= 0 || !Ready(_now, slot.ReadyAt)) return;
            if ((c.Flags & CombatFlags.LaunchersOffline) != 0) return;
            var def = WeaponCatalog.Get(slot.Weapon);
            if (def == null) return;

            int target = GameWorld.NoShip;
            if (def.NeedsLock)
            {
                // No lock, no launch: the tone has to be steady first.
                if (c.Lock != LockPhase.Locked || c.LockTarget != ship.Target || ship.Target == GameWorld.NoShip) return;
                target = c.LockTarget;
            }
            else if (c.Lock == LockPhase.Locked) target = c.LockTarget;     // a heat seeker launched with the target in its cone follows it

            slot.Ammo--;
            slot.ReadyAt = _now + def.FireWait;
            float r = Mathf.Max(ship.Radius, 2f);
            // A slow torpedo is let go well clear of the nose, so its exhaust glow does not sit on the lens for seconds.
            float clear = def.Kind == WeaponKind.Torpedo ? 18f : 3f;
            Vector3 origin = ship.Position + ship.Rotation * new Vector3(0f, -0.12f * r, 0.45f * r + clear);
            LaunchMissile(def, c, origin, ship.Forward, target, target != GameWorld.NoShip && c.LockTarget == target ? ship.TargetPart : -1);
        }

        Projectile LaunchMissile(WeaponDef def, ShipCombat owner, Vector3 origin, Vector3 dir, int target, int targetPart)
        {
            var ship = owner.Ship;
            var p = Rent();
            p.Kind = ProjectileKind.Missile; p.Def = def; p.Owner = ship.Id; p.Team = ship.Team;
            p.Pos = p.PrevPos = origin;
            p.Vel = ship.Velocity + dir * (def.Speed * (def.Kind == WeaponKind.Torpedo ? 0.8f : 0.35f));
            p.Life = def.Life; p.Hp = def.Hp;
            p.Target = target; p.TargetPart = targetPart;
            ShotsFired++;
            RaiseTrack(p);
            return p;
        }

        void RaiseTrack(Projectile p)
        {
            GameEvents.RaiseAt(CombatEvents.MissileTrack, p.Pos, p.Vel, p.Def.EventId, p.Id, p.Owner, p.Target);
        }

        // ------------------------------------------------------------------------------------------------ decoys

        void ReleaseDecoy(ShipCombat c, ShipState ship)
        {
            var def = WeaponCatalog.Get("decoy");
            if (def == null || c.Decoys <= 0 || !Ready(_now, c.DecoyReadyAt)) return;
            c.Decoys--;
            c.DecoyReadyAt = _now + def.FireWait;

            var d = Rent();
            d.Kind = ProjectileKind.Decoy; d.Def = def; d.Owner = ship.Id; d.Team = ship.Team;
            Vector3 back = -ship.Forward;
            Vector3 side = ship.Rotation * new Vector3(Rand01() * 2f - 1f, Rand01() * 2f - 1f, 0f) * 0.5f;
            d.Pos = d.PrevPos = ship.Position + back * Mathf.Max(ship.Radius * 0.6f, 3f);
            d.Vel = ship.Velocity + (back + side).normalized * def.Speed;
            d.Life = def.Life + (def.LifeMax - def.Life) * Rand01();
            GameEvents.Raise(CombatEvents.DecoyLaunched, null, 0f, ship.Id);

            // Every missile homing on this ship and close enough rolls once to be fooled.
            for (int i = 0; i < _proj.Count; i++)
            {
                var m = _proj[i];
                if (m.Kind != ProjectileKind.Missile || !m.Alive || m.Target != ship.Id || m.Diverted) continue;
                if (Vector3.Distance(m.Pos, ship.Position) > def.DecoyRange) continue;
                if (Rand01() < m.Def.DecoyChance) { m.Diverted = true; m.DecoyId = d.Id; m.DecoyPoint = d.Pos; m.DecoyVel = d.Vel; m.TargetPart = -1; }
            }
            // The decoy is drawn as a slow, hot, missile-like streak (the juice shows "missile_decoy" without a launch bang).
            RaiseTrack(d, "missile_decoy");
        }

        void RaiseTrack(Projectile p, string subject)
        {
            GameEvents.RaiseAt(CombatEvents.MissileTrack, p.Pos, p.Vel, subject, p.Id, p.Owner, GameWorld.NoShip);
        }

        // ------------------------------------------------------------------------------------------------ guidance

        /// <summary>Steers one missile for a tick: lead pursuit on its target (or the decoy that fooled it), limited by its turn rate.</summary>
        void Guide(Projectile m, float dt)
        {
            var def = m.Def;
            float speed = m.Vel.magnitude;
            Vector3 dir = speed > 0.01f ? m.Vel / speed : Vector3.forward;

            // Where the thing it is chasing is.
            bool have = false; Vector3 tp = default, tv = default;
            if (m.Diverted)
            {
                // Follow the decoy while it burns; after that, fly on to where it was.
                bool burning = false;
                for (int i = 0; i < _proj.Count; i++)
                    if (_proj[i].Kind == ProjectileKind.Decoy && _proj[i].Id == m.DecoyId && _proj[i].Alive) { m.DecoyPoint = _proj[i].Pos; m.DecoyVel = _proj[i].Vel; burning = true; break; }
                if (!burning) m.DecoyVel = Vector3.zero;
                have = true; tp = m.DecoyPoint; tv = m.DecoyVel;
            }
            if (!have && m.Target != GameWorld.NoShip)
            {
                var ts = _world.Ships.Get(m.Target);
                var tc = _world.Combat.Get(m.Target);
                if (ts == null || tc == null || !tc.Alive) m.Target = GameWorld.NoShip;
                else
                {
                    tp = ts.Position; tv = ts.Velocity;
                    if (m.TargetPart >= 0 && m.TargetPart < tc.Parts.Count && !tc.Parts[m.TargetPart].Destroyed) tp = tc.Parts[m.TargetPart].WorldPosition(ts);
                    have = true;
                }
            }
            // A heat seeker with no target looks for one in its cone.
            if (!have && !m.Diverted && def.Kind == WeaponKind.HeatMissile)
            {
                m.ReacquireIn -= dt;
                if (m.ReacquireIn <= 0f) { m.ReacquireIn = 0.1f; m.Target = FindHeatTarget(m, dir); }
            }

            Vector3 want = dir;
            if (have)
            {
                Vector3 to = tp - m.Pos;
                if (def.Kind == WeaponKind.HeatMissile && Vector3.Angle(dir, to) > def.ViewCone * 0.5f)
                {
                    // Outside the seeker's view cone: it cannot follow, and gives up after a moment (dodging works).
                    m.OutOfCone += dt;
                    if (m.OutOfCone > _rules.MissileTrackLoseSeconds) { m.Target = GameWorld.NoShip; have = false; }
                }
                else m.OutOfCone = 0f;
                if (have)
                {
                    want = to.sqrMagnitude > 1e-4f ? to.normalized : dir;
                    if (Geo.SolveIntercept(m.Pos, Vector3.zero, tp, tv, Mathf.Max(speed, def.Speed * 0.5f), out Vector3 aim, out float tt, out _) && tt < 4f) want = aim;
                }
            }
            Vector3 nd = Vector3.RotateTowards(dir, want, def.TurnRate * dt, 0f);
            float s = speed + (def.Speed - speed) * (1f - Mathf.Exp(-dt / Mathf.Max(0.05f, def.AccelTime)));
            m.Vel = nd * s;
        }

        int FindHeatTarget(Projectile m, Vector3 dir)
        {
            var def = m.Def;
            int best = GameWorld.NoShip; float bestAngle = def.ViewCone * 0.5f;
            var all = _world.Combat.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (!c.Alive || c.Ship == null || c.Id == m.Owner || !Teams.Hostile(m.Team, c.Ship.Team)) continue;
                Vector3 to = c.Ship.Position - m.Pos;
                if (to.sqrMagnitude > def.Range * def.Range * 1.2f) continue;
                float a = Vector3.Angle(dir, to);
                if (a < bestAngle) { bestAngle = a; best = c.Id; }
            }
            return best;
        }
    }
}
