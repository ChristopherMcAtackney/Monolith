using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Weapons
{
    /// <summary>One beam being charged or fired (read-only to everyone but the weapons system).</summary>
    public sealed class BeamShot
    {
        public enum Phase { Charging, Firing }

        public int Ship, Part = -1, Target;
        public WeaponDef Def;
        public Phase State;
        public float T, DamageCarry;
        /// <summary>Where the sweep starts and ends, in the target's own frame (on the face of its hull that looks at the muzzle).</summary>
        public Vector3 SweepA, SweepB;
        public Vector3 Origin;
        /// <summary>The world direction the beam points this tick, and where it last touched something.</summary>
        public Vector3 Aim, Touch;
        public int Touching = GameWorld.NoShip;
        public int Ticks;
    }

    public sealed partial class WeaponSystem
    {
        // ------------------------------------------------------------------------------------------------ beams
        //
        // A capital's heavy beam (FS2's slash beams) is fired like any turret, through a FireRequest naming a beam weapon and a target ship.
        // It charges for the weapon's warm-up (beam_charge), then burns for its life (beam_fire), sweeping a line along the face of the target's
        // hull that looks at the muzzle and raising beam_aim every tick so the juice draws it where it really is (beam_end when it stops). While it
        // burns, every damage interval it hurts the first hostile thing on its line, the target or anything that flies through it, and cuts the
        // parts near where it touches. A ship fires one beam at a time (the juice keys beams by their ship); the turret's part is busy for the
        // charge, the burn and the fire wait, and a beam whose turret is destroyed stops at once.

        readonly List<BeamShot> _beams = new List<BeamShot>(4);

        /// <summary>Beams charging or burning now.</summary>
        public IReadOnlyList<BeamShot> Beams => _beams;
        /// <summary>Beams fired so far (charged and burned), for checks.</summary>
        public int BeamsFired { get; private set; }

        /// <summary>Drop every shot, missile, decoy and beam in flight (a scene reset). Beams end with beam_end so the juice lets go of them.</summary>
        public void ClearAll()
        {
            for (int i = _beams.Count - 1; i >= 0; i--) EndBeam(i, null);
            foreach (var p in _proj) { p.Alive = false; _pool.Push(p); }
            _proj.Clear();
            _torpedoes.Clear();
        }

        bool HasBeam(int ship)
        {
            for (int i = 0; i < _beams.Count; i++) if (_beams[i].Ship == ship) return true;
            return false;
        }

        /// <summary>A beam request from <see cref="DrainRequests"/>: starts charging if the ship has no beam going and the target is in reach.</summary>
        void StartBeam(in FireRequest r, ShipCombat c, CombatPart part, WeaponDef def)
        {
            if (HasBeam(c.Id)) return;
            var target = _world.Ships.Get(r.Target);
            var tc = _world.Combat.Get(r.Target);
            if (target == null || tc == null || !tc.Alive || !Teams.Hostile(c.Ship.Team, target.Team)) return;
            var b = new BeamShot { Ship = c.Id, Part = part != null ? part.Index : -1, Target = target.Id, Def = def, Origin = r.Origin, State = BeamShot.Phase.Charging };
            Vector3 muzzle = BeamMuzzle(b, c);
            float reach = Vector3.Distance(muzzle, target.Position) - Mathf.Max(tc.Shape.BoundRadius, target.Radius) * 0.6f;
            if (reach > def.BeamRange) return;
            b.Aim = (target.Position - muzzle).normalized;
            _beams.Add(b);
            if (part != null) part.Cooldown = def.BeamWarmup + def.Life + def.FireWait;
            GameEvents.RaiseAt(CombatEvents.BeamCharge, muzzle, b.Aim, def.EventId, Mathf.Max(0.1f, def.BeamWarmup), c.Id, target.Id);
        }

        Vector3 BeamMuzzle(BeamShot b, ShipCombat c)
        {
            if (b.Part >= 0 && b.Part < c.Parts.Count)
            {
                var p = c.Parts[b.Part];
                return p.WorldPosition(c.Ship) + p.WorldForward(c.Ship) * (p.Radius * 0.5f);
            }
            return c.Ship.Position + c.Ship.Rotation * b.Origin;
        }

        void StepBeams(float dt)
        {
            for (int i = _beams.Count - 1; i >= 0; i--)
            {
                var b = _beams[i];
                var c = _world.Combat.Get(b.Ship);
                bool sourceOk = c != null && c.Alive && c.Ship != null && (b.Part < 0 || (b.Part < c.Parts.Count && !c.Parts[b.Part].Destroyed));
                var target = _world.Ships.Get(b.Target);
                var tc = _world.Combat.Get(b.Target);
                bool targetOk = target != null && tc != null && tc.Alive;
                if (!sourceOk || (!targetOk && b.State == BeamShot.Phase.Charging))
                {
                    EndBeam(i, c);
                    continue;
                }
                Vector3 muzzle = BeamMuzzle(b, c);
                b.T += dt;
                if (b.State == BeamShot.Phase.Charging)
                {
                    if (b.T < b.Def.BeamWarmup) continue;
                    b.State = BeamShot.Phase.Firing;
                    b.T = 0f;
                    PlanSweep(b, muzzle, target, tc);
                    BeamsFired++;
                    GameEvents.RaiseAt(CombatEvents.BeamFire, muzzle, b.Aim, b.Def.EventId, b.Def.Life, b.Ship, b.Target);
                }

                // Burning: follow the sweep across the target (or hold the last line if the target has died), touch, hurt, cut.
                if (targetOk)
                {
                    float k = Mathf.Clamp01(b.T / Mathf.Max(0.05f, b.Def.Life));
                    k = k * k * (3f - 2f * k);
                    Vector3 aimAt = target.Position + target.Rotation * Vector3.Lerp(b.SweepA, b.SweepB, k);
                    Vector3 d = aimAt - muzzle;
                    if (d.sqrMagnitude > 1f) b.Aim = d.normalized;
                }
                GameEvents.RaiseAt(CombatEvents.BeamAim, muzzle, b.Aim, b.Def.EventId, 0f, b.Ship, b.Target);
                b.DamageCarry += dt;
                while (b.DamageCarry >= b.Def.BeamDamageInterval)
                {
                    b.DamageCarry -= b.Def.BeamDamageInterval;
                    BeamTick(b, c, muzzle);
                }
                if (b.T >= b.Def.Life) EndBeam(i, c);
            }
        }

        void EndBeam(int index, ShipCombat source)
        {
            var b = _beams[index];
            _beams.RemoveAt(index);
            Vector3 at = source != null && source.Ship != null ? BeamMuzzle(b, source) : b.Touch;
            GameEvents.RaiseAt(CombatEvents.BeamEnd, at, b.Aim, b.Def.EventId, 0f, b.Ship, b.Target);
        }

        /// <summary>A line along the face of the target's hull that looks at the muzzle, about three quarters of its length, either way along.</summary>
        void PlanSweep(BeamShot b, Vector3 muzzle, ShipState target, ShipCombat tc)
        {
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            foreach (var box in tc.Shape.Boxes) { min = Vector3.Min(min, box.Centre - box.Half); max = Vector3.Max(max, box.Centre + box.Half); }
            if (tc.Shape.Boxes.Length == 0) { min = -Vector3.one * target.Radius * 0.5f; max = -min; }
            Vector3 centre = 0.5f * (min + max), ext = 0.5f * (max - min);
            Vector3 m = Quaternion.Inverse(target.Rotation) * (muzzle - target.Position) - centre;
            bool side = Mathf.Abs(m.x) / Mathf.Max(1f, ext.x) >= Mathf.Abs(m.y) / Mathf.Max(1f, ext.y);
            float lateral = (Rand01() - 0.5f) * 0.6f;
            Vector3 onFace = side ? new Vector3(Mathf.Sign(m.x) * ext.x * 0.9f, lateral * ext.y, 0f) : new Vector3(lateral * ext.x, Mathf.Sign(m.y) * ext.y * 0.9f, 0f);
            float z0 = -0.75f * ext.z, z1 = 0.75f * ext.z;
            if (Rand01() < 0.5f) { float t = z0; z0 = z1; z1 = t; }
            b.SweepA = centre + onFace + new Vector3(0f, 0f, z0);
            b.SweepB = centre + onFace + new Vector3(0f, 0f, z1);
            Vector3 start = target.Position + target.Rotation * b.SweepA;
            b.Aim = (start - muzzle).normalized;
        }

        /// <summary>One damage tick: the first hostile hull on the line takes the beam's damage and the parts near the touch are cut.</summary>
        void BeamTick(BeamShot b, ShipCombat source, Vector3 muzzle)
        {
            var def = b.Def;
            Vector3 end = muzzle + b.Aim * def.BeamRange;
            float bestT = float.MaxValue; ShipHit best = default; ShipCombat victim = null;
            var all = _world.Combat.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (!c.Alive || c.Ship == null || c.Id == b.Ship || !Teams.Hostile(source.Ship.Team, c.Ship.Team)) continue;
                if (ShipHits.Segment(c.Ship, c, muzzle, end, out var hit) && hit.T < bestT) { bestT = hit.T; best = hit; victim = c; }
            }
            b.Ticks++;
            if (victim == null) { b.Touching = GameWorld.NoShip; return; }
            b.Touching = victim.Id; b.Touch = best.Point;
            _world.Damage.Add(new DamageRequest
            {
                Ship = victim.Id, Attacker = b.Ship, Weapon = def.EventId, Amount = def.Damage,
                ShieldMult = def.ShieldMult, HullMult = def.HullMult, SubsystemMult = 0f,
                Point = best.Point, Direction = b.Aim, Normal = best.Normal, Part = -2, Kind = DamageKind.Beam, OnShield = best.OnShield,
            });
            if (best.OnShield || def.SubsystemMult <= 0f) return;
            // The beam is fat: every part close to where it touches is cut (as wide as the juice draws it, so what you see go is gone).
            for (int k = 0; k < victim.Parts.Count; k++)
            {
                var p = victim.Parts[k];
                if (p.Destroyed) continue;
                if (Vector3.Distance(p.WorldPosition(victim.Ship), best.Point) > p.Radius + def.BeamCutRadius) continue;
                _world.Damage.Add(new DamageRequest
                {
                    Ship = victim.Id, Attacker = b.Ship, Weapon = def.EventId, Amount = def.Damage,
                    ShieldMult = 0f, HullMult = 0f, SubsystemMult = def.SubsystemMult,
                    Point = p.WorldPosition(victim.Ship), Direction = b.Aim, Normal = best.Normal, Part = k, Kind = DamageKind.Beam, BypassShield = true,
                });
            }
        }
    }
}
