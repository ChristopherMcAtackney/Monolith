using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Targeting
{
    /// <summary>
    /// Target selection for every piloted ship (order <see cref="SimOrder.Targeting"/>; owner of <see cref="ShipState.Target"/> and
    /// <see cref="ShipState.TargetPart"/>). The player's buttons and an AI's requests go through the same <see cref="ControlInput"/>:
    /// <see cref="ControlButtons.TargetNext"/> cycles the hostiles nearest first, <see cref="ControlButtons.TargetAttacker"/> picks the
    /// nearest ship attacking this one, <see cref="ControlButtons.TargetReticle"/> the ship nearest the nose, and
    /// <see cref="ControlButtons.SubsystemNext"/> steps through the target's parts (nearest the nose first) and back to the whole ship.
    /// <see cref="ControlInput.TargetShip"/> and <see cref="ControlInput.TargetPartPlusOne"/> set a target directly. A target that has
    /// died is dropped.
    /// </summary>
    public sealed class TargetingSystem : ISimSystem
    {
        /// <summary>How far off the nose "target ahead" looks, degrees.</summary>
        public const float AheadConeDegrees = 30f;
        /// <summary>A ship that hit you this recently counts as an attacker, seconds.</summary>
        public const float AttackerMemory = 15f;

        readonly GameWorld _world;
        readonly List<ShipState> _candidates = new List<ShipState>();
        readonly List<int> _parts = new List<int>();

        public TargetingSystem(GameWorld world) { _world = world; }

        public int Order => SimOrder.Targeting;

        public void Step(in SimFrame frame)
        {
            var ships = _world.Ships;
            var pilots = _world.Pilots;
            for (int i = 0; i < ships.All.Count; i++)
            {
                var s = ships.All[i];
                // A target that is gone or dead is dropped.
                if (s.Target != GameWorld.NoShip)
                {
                    var t = ships.Get(s.Target);
                    var tc = _world.Combat.Get(s.Target);
                    if (t == null || (tc != null && !tc.Alive)) { SetTarget(s, GameWorld.NoShip, -1); }
                    else if (s.TargetPart >= 0 && (tc == null || s.TargetPart >= tc.Parts.Count || tc.Parts[s.TargetPart].Destroyed)) s.TargetPart = -1;
                }

                var now = pilots.Current(s.Id);
                var before = pilots.Previous(s.Id);
                if (now.TargetShip > 0 && now.TargetShip != s.Target && ships.Get(now.TargetShip) != null) SetTarget(s, now.TargetShip, -1);
                if (now.TargetPartPlusOne > 0 && s.Target != GameWorld.NoShip)
                {
                    var tc = _world.Combat.Get(s.Target);
                    int pi = now.TargetPartPlusOne - 1;
                    if (tc != null && pi < tc.Parts.Count && !tc.Parts[pi].Destroyed && s.TargetPart != pi) SetTarget(s, s.Target, pi);
                }
                if (now.Pressed(before, ControlButtons.TargetReticle)) { int t = Ahead(s); if (t != GameWorld.NoShip) SetTarget(s, t, -1); }
                else if (now.Pressed(before, ControlButtons.TargetNext)) SetTarget(s, NextHostile(s), -1);
                else if (now.Pressed(before, ControlButtons.TargetAttacker)) { int t = Attacker(s); if (t != GameWorld.NoShip) SetTarget(s, t, -1); }
                else if (now.Pressed(before, ControlButtons.SubsystemNext)) NextPart(s);
            }
        }

        void SetTarget(ShipState s, int target, int part)
        {
            if (s.Target == target && s.TargetPart == part) return;
            bool changed = s.Target != target || s.TargetPart != part;
            s.Target = target;
            s.TargetPart = part;
            if (!changed) return;
            Vector3 at = _world.Ships.Get(target)?.Position ?? s.Position;
            GameEvents.RaiseAt(CombatEvents.TargetChanged, at, Vector3.zero, null, part + 1, s.Id, target);
        }

        bool Alive(ShipState o)
        {
            var c = _world.Combat.Get(o.Id);
            return c == null || c.Alive;
        }

        /// <summary>The ship nearest the nose within the cone (any ship but this one: a wingman can be targeted too).</summary>
        int Ahead(ShipState self)
        {
            int best = GameWorld.NoShip;
            float bestAngle = AheadConeDegrees;
            foreach (var o in _world.Ships.All)
            {
                if (o == self || !Alive(o)) continue;
                float a = Vector3.Angle(self.Forward, o.Position - self.Position);
                if (a < bestAngle) { bestAngle = a; best = o.Id; }
            }
            return best;
        }

        /// <summary>The next hostile after the current target, nearest first.</summary>
        int NextHostile(ShipState self)
        {
            _candidates.Clear();
            foreach (var o in _world.Ships.All)
                if (o != self && Teams.Hostile(self.Team, o.Team) && Alive(o)) _candidates.Add(o);
            if (_candidates.Count == 0) return GameWorld.NoShip;
            _candidates.Sort((a, b) =>
            {
                int c = (a.Position - self.Position).sqrMagnitude.CompareTo((b.Position - self.Position).sqrMagnitude);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            int at = _candidates.FindIndex(c => c.Id == self.Target);
            return _candidates[(at + 1) % _candidates.Count].Id;
        }

        /// <summary>The nearest hostile that has this ship as its target or has hit it lately; pressing again moves to the next one out.</summary>
        int Attacker(ShipState self)
        {
            var mine = _world.Combat.Get(self.Id);
            _candidates.Clear();
            foreach (var o in _world.Ships.All)
            {
                if (o == self || !Teams.Hostile(self.Team, o.Team) || !Alive(o)) continue;
                bool attacking = o.Target == self.Id || (mine != null && mine.LastAttacker == o.Id && _world.Time - mine.LastAttackerTime < AttackerMemory);
                if (attacking) _candidates.Add(o);
            }
            if (_candidates.Count == 0) return GameWorld.NoShip;
            _candidates.Sort((a, b) => (a.Position - self.Position).sqrMagnitude.CompareTo((b.Position - self.Position).sqrMagnitude));
            int at = _candidates.FindIndex(c => c.Id == self.Target);
            return _candidates[(at + 1) % _candidates.Count].Id;
        }

        /// <summary>The next part of the target, nearest the nose first; after the last, back to the whole ship.</summary>
        void NextPart(ShipState self)
        {
            var t = _world.Ships.Get(self.Target);
            var tc = _world.Combat.Get(self.Target);
            if (t == null || tc == null || tc.Parts.Count == 0) return;
            _parts.Clear();
            for (int i = 0; i < tc.Parts.Count; i++) if (!tc.Parts[i].Destroyed) _parts.Add(i);
            if (_parts.Count == 0) { SetTarget(self, self.Target, -1); return; }
            Vector3 fwd = self.Forward, pos = self.Position;
            _parts.Sort((x, y) =>
            {
                float ax = Vector3.Angle(fwd, tc.Parts[x].WorldPosition(t) - pos), ay = Vector3.Angle(fwd, tc.Parts[y].WorldPosition(t) - pos);
                int c = ax.CompareTo(ay);
                return c != 0 ? c : x.CompareTo(y);
            });
            int at = self.TargetPart < 0 ? -1 : _parts.IndexOf(self.TargetPart);
            int next = at + 1;
            SetTarget(self, self.Target, next >= _parts.Count ? -1 : _parts[next]);
        }
    }

    /// <summary>
    /// The numbers the HUD aims with, from this tick's settled positions (order just before presentation): where to point the nose with
    /// the fast gun to hit the target, how far it is, whether it is hostile. Published in <see cref="ShipCombat"/> (owner: Targeting).
    /// </summary>
    public sealed class AimSystem : ISimSystem
    {
        readonly GameWorld _world;
        public AimSystem(GameWorld world) { _world = world; }
        public int Order => SimOrder.Presentation - 10;

        public void Step(in SimFrame frame)
        {
            var all = _world.Combat.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                c.HasLead = false;
                var s = c.Ship;
                if (s == null || !c.Alive) continue;
                var t = _world.Ships.Get(s.Target);
                if (t == null) { c.TargetDistance = 0f; c.TargetHostile = false; continue; }
                c.TargetDistance = Vector3.Distance(s.Position, t.Position);
                c.TargetHostile = Teams.Hostile(s.Team, t.Team);

                // Lead for the fast gun, else the heavy one: the bolt takes our velocity as well, so aim to the solved direction.
                float speed = c.PrimaryOnline ? c.PrimarySpeed : c.HeavyOnline ? c.HeavySpeed : 0f;
                if (speed <= 1f) continue;
                Vector3 aimAt = t.Position;
                var tc = _world.Combat.Get(t.Id);
                if (s.TargetPart >= 0 && tc != null && s.TargetPart < tc.Parts.Count && !tc.Parts[s.TargetPart].Destroyed) aimAt = tc.Parts[s.TargetPart].WorldPosition(t);
                if (!Geo.SolveIntercept(s.Position, s.Velocity, aimAt, t.Velocity, speed, out Vector3 dir, out float time, out Vector3 meet)) continue;
                var gun = WeaponCatalog.Get(c.PrimaryOnline ? c.PrimaryGun : c.HeavyGun);
                if (gun != null && time > gun.Life) continue;                       // out of range: no lead to show
                c.HasLead = true;
                c.LeadTime = time;
                c.LeadPoint = s.Position + dir * Vector3.Distance(s.Position, meet);
            }
        }
    }
}
