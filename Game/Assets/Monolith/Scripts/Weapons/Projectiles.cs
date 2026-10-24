using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Weapons
{
    public sealed partial class WeaponSystem
    {
        // ------------------------------------------------------------------------------------------------ flying everything

        void StepProjectiles(float dt)
        {
            // Torpedoes in flight, for bolts to shoot down.
            _torpedoes.Clear();
            for (int i = 0; i < _proj.Count; i++)
            {
                var p = _proj[i];
                if (p.Alive && p.Kind == ProjectileKind.Missile && p.Def.Hp > 0f) _torpedoes.Add(p);
            }

            for (int i = 0; i < _proj.Count; i++)
            {
                var p = _proj[i];
                if (!p.Alive) continue;
                if (p.JustSpawned) { p.JustSpawned = false; continue; }   // it exists from the end of the tick it was fired in
                p.Age += dt;
                p.PrevPos = p.Pos;
                switch (p.Kind)
                {
                    case ProjectileKind.Bolt: StepBolt(p, dt); break;
                    case ProjectileKind.Flak: StepFlak(p, dt); break;
                    case ProjectileKind.Missile: StepMissile(p, dt); break;
                    case ProjectileKind.Decoy: StepDecoy(p, dt); break;
                }
            }

            // Sweep out the dead (swap-remove keeps it cheap) and give them back to the pool.
            for (int i = _proj.Count - 1; i >= 0; i--)
            {
                if (_proj[i].Alive) continue;
                var dead = _proj[i];
                _proj[i] = _proj[_proj.Count - 1];
                _proj.RemoveAt(_proj.Count - 1);
                _pool.Push(dead);
            }
        }

        // ------------------------------------------------------------------------------------------------ bolts

        void StepBolt(Projectile p, float dt)
        {
            if (p.Age > p.Life) { p.Alive = false; return; }
            p.Pos = p.PrevPos + p.Vel * dt;

            // The first thing the path crosses this tick: a ship (shield, hull or part), or a torpedo.
            float bestT = float.MaxValue; ShipHit bestHit = default; ShipCombat bestShip = null; Projectile bestTorpedo = null;
            var all = _world.Combat.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (!c.Alive || c.Ship == null || c.Id == p.Owner || !Teams.Hostile(p.Team, c.Ship.Team)) continue;
                if (ShipHits.Segment(c.Ship, c, p.PrevPos, p.Pos, out var hit) && hit.T < bestT) { bestT = hit.T; bestHit = hit; bestShip = c; }
            }
            for (int i = 0; i < _torpedoes.Count; i++)
            {
                var t = _torpedoes[i];
                if (!t.Alive || !Teams.Hostile(p.Team, t.Team)) continue;
                if (Geo.SegmentSphere(p.PrevPos, p.Pos, t.Pos, 6f, out float tt, out _) && tt < bestT) { bestT = tt; bestShip = null; bestTorpedo = t; }
            }

            if (bestTorpedo != null)
            {
                Vector3 at = Vector3.Lerp(p.PrevPos, p.Pos, bestT);
                bestTorpedo.Hp -= p.Def.Damage * p.Def.HullMult;
                p.Alive = false;
                GameEvents.RaiseAt(CombatEvents.HitHull, at, -p.Vel.normalized, p.Def.EventId, p.Def.Damage, GameWorld.NoShip, p.Owner);
                if (bestTorpedo.Hp <= 0f) ShootDown(bestTorpedo, p.Owner);
                return;
            }
            if (bestShip != null)
            {
                ShotsHit++;
                var r = new DamageRequest
                {
                    Ship = bestShip.Id, Attacker = p.Owner, Weapon = p.Def.EventId, Amount = p.Def.Damage,
                    ShieldMult = p.Def.ShieldMult, HullMult = p.Def.HullMult, SubsystemMult = p.Def.SubsystemMult,
                    Point = bestHit.Point, Direction = p.Vel.normalized, Normal = bestHit.Normal, Part = bestHit.Part,
                    Kind = DamageKind.Projectile, OnShield = bestHit.OnShield,
                };
                _world.Damage.Add(r);
                p.Alive = false;
                return;
            }
            NearMiss(p);
        }

        /// <summary>A bolt passing close to the local player's ship raises the near-miss event once (shake, whoosh, rumble).</summary>
        void NearMiss(Projectile p)
        {
            if (p.Passed) return;
            var me = _world.Ships.Get(_world.LocalPlayerShip);
            if (me == null || me.Id == p.Owner || !Teams.Hostile(p.Team, me.Team)) return;
            Vector3 closest = Geo.ClosestOnSegment(p.PrevPos, p.Pos, me.Position);
            float d = Vector3.Distance(closest, me.Position);
            if (d > 38f) return;
            if (Vector3.Dot(me.Position - p.Pos, p.Vel) > 0f) return;       // still approaching: wait until it has passed
            p.Passed = true;
            GameEvents.RaiseAt(CombatEvents.NearMiss, closest, p.Vel.normalized, p.Def.EventId, d, p.Owner, me.Id);
        }

        // ------------------------------------------------------------------------------------------------ flak

        void StepFlak(Projectile p, float dt)
        {
            p.Pos = p.PrevPos + p.Vel * dt;
            bool burst = p.Age >= p.FuseTime || p.Age > p.Life;
            if (!burst)
            {
                // Proximity: burst when passing near a hostile hull; direct strikes burst too.
                var all = _world.Combat.All;
                for (int i = 0; i < all.Count && !burst; i++)
                {
                    var c = all[i];
                    if (!c.Alive || c.Ship == null || c.Id == p.Owner || !Teams.Hostile(p.Team, c.Ship.Team)) continue;
                    if (ShipHits.Segment(c.Ship, c, p.PrevPos, p.Pos, out var hit)) { p.Pos = hit.Point; burst = true; break; }
                    Vector3 near = Geo.ClosestOnSegment(p.PrevPos, p.Pos, c.Ship.Position);
                    if (ShipHits.DistanceToHull(c.Ship, c, near, out _) <= _rules.FlakProximity) { p.Pos = near; burst = true; }
                }
            }
            if (!burst) return;
            p.Alive = false;
            GameEvents.RaiseAt(CombatEvents.FlakBurst, p.Pos, Vector3.zero, p.Def.EventId, p.Def.BlastOuter * 0.5f, p.Owner);
            Blast(p, p.Pos, GameWorld.NoShip);
        }

        // ------------------------------------------------------------------------------------------------ decoys

        void StepDecoy(Projectile p, float dt)
        {
            // A decoy is thrown clear and hangs in space: it sheds the ship's speed in about a second, so the ship flies away from it.
            p.Vel *= Mathf.Exp(-dt / 0.8f);
            p.Pos = p.PrevPos + p.Vel * dt;
            if (p.Age > p.Life) { p.Alive = false; return; }
            RaiseTrack(p, "missile_decoy");
        }

        // ------------------------------------------------------------------------------------------------ missiles

        void StepMissile(Projectile m, float dt)
        {
            var def = m.Def;
            if (m.Age > m.Life)
            {
                // Out of fuel: it burns out harmlessly.
                m.Alive = false;
                GameEvents.RaiseAt(CombatEvents.Explosion, m.Pos, Vector3.zero, "small", 8f);
                return;
            }
            Guide(m, dt);
            m.Pos = m.PrevPos + m.Vel * dt;

            // A decoy that fooled it: it goes off where the decoy is (or was, once it has burned out).
            if (m.Diverted)
            {
                Vector3 near = Geo.ClosestOnSegment(m.PrevPos, m.Pos, m.DecoyPoint);
                if (Vector3.Distance(near, m.DecoyPoint) < 8f)
                {
                    m.Alive = false;
                    for (int i = 0; i < _proj.Count; i++) if (_proj[i].Kind == ProjectileKind.Decoy && _proj[i].Id == m.DecoyId) _proj[i].Alive = false;
                    GameEvents.RaiseAt(CombatEvents.DecoySuccess, near, Vector3.zero, def.EventId, 0f, m.Target, m.Owner);
                    GameEvents.RaiseAt(CombatEvents.Explosion, near, Vector3.zero, "small", Mathf.Clamp(def.BlastInner * 0.8f, 8f, 60f));
                    Blast(m, near, GameWorld.NoShip);
                    return;
                }
            }

            // The first hostile hull the flight crosses (the shield first, if it is up).
            float bestT = float.MaxValue; ShipHit bestHit = default; ShipCombat victim = null;
            var all = _world.Combat.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (!c.Alive || c.Ship == null || c.Id == m.Owner || !Teams.Hostile(m.Team, c.Ship.Team)) continue;
                if (ShipHits.Segment(c.Ship, c, m.PrevPos, m.Pos, out var hit) && hit.T < bestT) { bestT = hit.T; bestHit = hit; victim = c; }
            }
            if (victim != null)
            {
                ShotsHit++;
                m.Alive = false;
                var r = new DamageRequest
                {
                    Ship = victim.Id, Attacker = m.Owner, Weapon = def.EventId, Amount = def.Damage,
                    ShieldMult = def.ShieldMult, HullMult = def.HullMult, SubsystemMult = def.SubsystemMult,
                    Point = bestHit.Point, Direction = m.Vel.normalized, Normal = bestHit.Normal, Part = bestHit.Part,
                    Kind = DamageKind.Projectile, OnShield = bestHit.OnShield,
                };
                _world.Damage.Add(r);
                GameEvents.RaiseAt(CombatEvents.Explosion, bestHit.Point, Vector3.zero, def.BlastInner >= 60f ? "large" : "small", Mathf.Clamp(def.BlastInner * 0.9f, 8f, 160f));
                Blast(m, bestHit.Point, victim.Id);
                return;
            }

            RaiseTrack(m);
            // Warn whoever it is homing on.
            if (m.Target != GameWorld.NoShip && !m.Diverted)
            {
                var tc = _world.Combat.Get(m.Target);
                if (tc != null && tc.Alive)
                {
                    float d = Vector3.Distance(m.Pos, tc.Ship.Position);
                    if (d <= _rules.WarningRange) AddIncoming(tc, m, d);
                }
            }
        }

        void AddIncoming(ShipCombat tc, Projectile m, float d)
        {
            tc.IncomingMissiles++;
            if (d < tc.IncomingNearest) { tc.IncomingNearest = d; tc.IncomingDirection = (m.Pos - tc.Ship.Position).normalized; }
            Vector3 rel = m.Vel - tc.Ship.Velocity;
            float closing = Vector3.Dot(rel, (tc.Ship.Position - m.Pos).normalized);
            float eta = closing > 1f ? d / closing : 99f;
            if (eta < tc.IncomingEta) tc.IncomingEta = eta;
            if (!m.Warned)
            {
                m.Warned = true;
                GameEvents.RaiseAt(CombatEvents.MissileWarning, m.Pos, Vector3.zero, m.Def.EventId, d, tc.Id, m.Owner);
            }
        }

        void FinishWarnings(IReadOnlyList<ShipCombat> ships)
        {
            for (int i = 0; i < ships.Count; i++)
            {
                var c = ships[i];
                if (c.IncomingMissiles == 0) { c.IncomingNearest = 0f; c.IncomingEta = 0f; }
            }
        }

        /// <summary>A torpedo (or any missile with hit points) was shot down: it goes off harmlessly, far from what it was aimed at.</summary>
        void ShootDown(Projectile m, int by)
        {
            if (!m.Alive) return;
            m.Alive = false;
            GameEvents.RaiseAt(CombatEvents.ShotDown, m.Pos, Vector3.zero, m.Def.EventId, 0f, m.Owner, by);
            GameEvents.RaiseAt(CombatEvents.Explosion, m.Pos, Vector3.zero, "medium", Mathf.Clamp(m.Def.BlastInner * 0.4f, 12f, 60f));
        }

        // ------------------------------------------------------------------------------------------------ blasts

        /// <summary>
        /// A detonation: every ship within the weapon's outer blast radius of its hull takes damage falling off linearly from the inner
        /// radius (the direct-hit ship already took the hit itself, but its parts near the blast take theirs here) and a shove.
        /// </summary>
        void Blast(Projectile m, Vector3 point, int directVictim)
        {
            var def = m.Def;
            if (def.BlastOuter <= 0f || def.Damage <= 0f) return;
            // FS2's flak is the capital ships' answer to bombs: a burst near a hostile torpedo hurts it as a bolt would, and may shoot it down.
            if (m.Kind == ProjectileKind.Flak)
                for (int i = 0; i < _torpedoes.Count; i++)
                {
                    var t = _torpedoes[i];
                    if (!t.Alive || !Teams.Hostile(m.Team, t.Team)) continue;
                    float td = Vector3.Distance(t.Pos, point);
                    if (td >= def.BlastOuter) continue;
                    t.Hp -= def.Damage * def.HullMult * (td <= def.BlastInner ? 1f : 1f - (td - def.BlastInner) / Mathf.Max(0.01f, def.BlastOuter - def.BlastInner));
                    if (t.Hp <= 0f) ShootDown(t, m.Owner);
                }
            var all = _world.Combat.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (!c.Alive || c.Ship == null) continue;
                var ship = c.Ship;
                float d = ShipHits.DistanceToHull(ship, c, point, out Vector3 near);
                if (d >= def.BlastOuter) continue;
                float f = d <= def.BlastInner ? 1f : 1f - (d - def.BlastInner) / Mathf.Max(0.01f, def.BlastOuter - def.BlastInner);
                Vector3 away = near - point;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : (ship.Position - point).normalized;

                if (c.Id != directVictim)
                {
                    _world.Damage.Add(new DamageRequest
                    {
                        Ship = c.Id, Attacker = m.Owner, Weapon = def.EventId, Amount = def.Damage * f,
                        ShieldMult = def.ShieldMult, HullMult = def.HullMult, SubsystemMult = def.SubsystemMult,
                        Point = near, Direction = away, Normal = away, Part = c.Shield > 0f ? -1 : -2, Kind = DamageKind.Blast, OnShield = c.Shield > 0f,
                    });
                }
                // Parts near the blast take it too (a torpedo that goes off on the hull kills the turrets round the hole). The blast spreads its
                // subsystem damage over the parts it reaches, the three nearest at most (FS2 spends a blast's subsystem damage on the nearest
                // subsystems first), so one flak burst cannot strip a small ship of everything and one torpedo on a cruiser's flank does not wear
                // down every part from the bow to the stern.
                if (c.Shield <= 0f && def.SubsystemMult > 0f)
                {
                    int n0 = -1, n1 = -1, n2 = -1; float d0 = float.MaxValue, d1 = float.MaxValue, d2 = float.MaxValue;
                    for (int k = 0; k < c.Parts.Count; k++)
                    {
                        var part = c.Parts[k];
                        if (part.Destroyed) continue;
                        float pd = Mathf.Max(0f, Vector3.Distance(part.WorldPosition(ship), point) - part.Radius);
                        if (pd >= def.BlastOuter) continue;
                        if (pd < d0) { n2 = n1; d2 = d1; n1 = n0; d1 = d0; n0 = k; d0 = pd; }
                        else if (pd < d1) { n2 = n1; d2 = d1; n1 = k; d1 = pd; }
                        else if (pd < d2) { n2 = k; d2 = pd; }
                    }
                    int reached = (n0 >= 0 ? 1 : 0) + (n1 >= 0 ? 1 : 0) + (n2 >= 0 ? 1 : 0);
                    float share = reached > 0 ? 1f / reached : 1f;
                    for (int j = 0; j < reached; j++)
                    {
                        int k = j == 0 ? n0 : j == 1 ? n1 : n2;
                        float pd = j == 0 ? d0 : j == 1 ? d1 : d2;
                        var part = c.Parts[k];
                        float pf = pd <= def.BlastInner ? 1f : 1f - (pd - def.BlastInner) / Mathf.Max(0.01f, def.BlastOuter - def.BlastInner);
                        _world.Damage.Add(new DamageRequest
                        {
                            Ship = c.Id, Attacker = m.Owner, Weapon = def.EventId, Amount = def.Damage * pf * share,
                            ShieldMult = 0f, HullMult = 0f, SubsystemMult = def.SubsystemMult,
                            Point = part.WorldPosition(ship), Direction = away, Normal = away, Part = k, Kind = DamageKind.Blast, BypassShield = true,
                        });
                    }
                }
                if (def.BlastForce > 0f && ship.Mass > 0f)
                {
                    Vector3 impulse = (ship.Position - point).normalized * (def.BlastForce * 1000f * f);
                    var local = Quaternion.Inverse(ship.Rotation) * (near - ship.Position);
                    _world.Knocks.Add(new ShipKnock { Ship = c.Id, Impulse = impulse, Point = local, Shock = def.Shockwave > 0f ? Mathf.Clamp01(f * 0.8f) : 0f });
                }
            }
        }
    }
}
