using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Collision
{
    /// <summary>
    /// Ship against ship, and ship against capital hull (order <see cref="SimOrder.Collision"/>). Each hull is a few boxes
    /// (<see cref="HitShape"/>); two ships whose boxes overlap bump: the closing speed along the contact normal gives an impulse
    /// that goes through the flight model's knock hook (<see cref="GameWorld.Knocks"/>), so the damping loosens and the nose is
    /// shoved, and a damage request for each ship (<see cref="DamageKind.Collision"/>, which bypasses shields) scaled by the energy
    /// lost in the contact: <c>0.5 x reduced mass x closing speed^2 x (1 - e^2)</c> times the rules' energy-to-damage factor, shared out by
    /// mass (the light ship takes nearly all of it). A graze of a capital ship hurts but does not kill; a head-on at full speed does.
    /// </summary>
    public sealed class CollisionSystem : ISimSystem
    {
        readonly GameWorld _world;
        readonly CombatRules _rules;
        readonly Dictionary<long, float> _lastHit = new Dictionary<long, float>();
        readonly Obb[] _boxesA = new Obb[16], _boxesB = new Obb[16];
        readonly List<long> _stale = new List<long>();
        float _now;

        public CollisionSystem(GameWorld world, CombatRules rules = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _rules = rules ?? CombatRules.Default;
        }

        public int Order => SimOrder.Collision;
        /// <summary>Contacts so far (tests and the range's counters).</summary>
        public int Contacts { get; private set; }

        public void Step(in SimFrame frame)
        {
            _now = frame.Time;
            var all = _world.Combat.All;
            int n = all.Count;
            for (int i = 0; i < n; i++)
            {
                var a = all[i];
                if (!a.Alive || a.Ship == null) continue;
                for (int j = i + 1; j < n; j++)
                {
                    var b = all[j];
                    if (!b.Alive || b.Ship == null) continue;
                    Test(a, b);
                }
            }
            if (_lastHit.Count > 256)
            {
                _stale.Clear();
                foreach (var kv in _lastHit) if (_now - kv.Value > 5f) _stale.Add(kv.Key);
                foreach (var k in _stale) _lastHit.Remove(k);
            }
        }

        void Test(ShipCombat ca, ShipCombat cb)
        {
            var a = ca.Ship; var b = cb.Ship;
            float reach = Mathf.Max(ca.Shape.BoundRadius, a.Radius) + Mathf.Max(cb.Shape.BoundRadius, b.Radius);
            if ((a.Position - b.Position).sqrMagnitude > reach * reach) return;

            int na = ShipHits.Boxes(a, ca, _boxesA), nb = ShipHits.Boxes(b, cb, _boxesB);
            float bestDepth = 0f; Vector3 bestNormal = Vector3.up, contact = Vector3.zero;
            for (int i = 0; i < na; i++)
                for (int j = 0; j < nb; j++)
                {
                    if (!Obb.Overlap(_boxesA[i], _boxesB[j], out Vector3 n, out float depth)) continue;
                    if (depth <= bestDepth) continue;
                    bestDepth = depth; bestNormal = n;
                    Vector3 pa = _boxesA[i].Centre - n * _boxesA[i].Extent(n);
                    Vector3 pb = _boxesB[j].Centre + n * _boxesB[j].Extent(n);
                    contact = 0.5f * (pa + pb);
                }
            if (bestDepth <= 0f) return;

            long key = Key(a.Id, b.Id);
            _lastHit.TryGetValue(key, out float last);
            bool cooling = _now - last < _rules.CollisionPairCooldown;

            // Closing speed along the normal (which points from b towards a).
            Vector3 rel = a.Velocity - b.Velocity;
            float closing = -Vector3.Dot(rel, bestNormal);
            float ma = Mathf.Max(1f, a.Mass), mb = Mathf.Max(1f, b.Mass);
            float inv = 1f / ma + 1f / mb;

            if (closing < _rules.CollisionMinClosing)
            {
                // Overlapping but not hitting (touching, or already parting): make sure they come apart.
                if (!cooling && closing < _rules.CollisionMinSeparation * 0.5f && bestDepth > 0.2f)
                {
                    float dv = _rules.CollisionMinSeparation - Mathf.Max(0f, -closing);
                    Push(ca, cb, bestNormal, Mathf.Max(0f, dv) / inv, contact, 0f);
                }
                return;
            }
            if (cooling) return;
            _lastHit[key] = _now;
            Contacts++;

            float e = Mathf.Max(_rules.CollisionRestitution, _rules.CollisionMinSeparation / closing);
            float j2 = (1f + e) * closing / inv;
            // The biggest speed change a contact gives is capped, so ploughing into a capital ship throws you back hard but not absurdly.
            float maxJ = _rules.CollisionMaxKnockSpeed * Mathf.Min(ma, mb);
            j2 = Mathf.Min(j2, maxJ);
            float shock = Mathf.Clamp01((closing - 25f) / 80f);
            Push(ca, cb, bestNormal, j2, contact, shock);

            // Damage: the energy lost, shared by mass (the light ship takes the rest).
            float mu = ma * mb / (ma + mb);
            float lost = 0.5f * mu * closing * closing * (1f - _rules.CollisionRestitution * _rules.CollisionRestitution);
            float total = lost * _rules.CollisionEnergyToDamage;
            Hurt(ca, cb, total * mb / (ma + mb), contact, bestNormal);
            Hurt(cb, ca, total * ma / (ma + mb), contact, -bestNormal);
            GameEvents.RaiseAt(CombatEvents.Collision, contact, bestNormal, null, closing, a.Id, b.Id);
        }

        void Push(ShipCombat ca, ShipCombat cb, Vector3 n, float impulse, Vector3 contact, float shock)
        {
            if (impulse <= 0f) return;
            var a = ca.Ship; var b = cb.Ship;
            _world.Knocks.Add(new ShipKnock
            {
                Ship = a.Id, Impulse = n * impulse, Point = Quaternion.Inverse(a.Rotation) * (contact - a.Position), Shock = shock,
            });
            _world.Knocks.Add(new ShipKnock
            {
                Ship = b.Id, Impulse = -n * impulse, Point = Quaternion.Inverse(b.Rotation) * (contact - b.Position), Shock = shock,
            });
        }

        void Hurt(ShipCombat victim, ShipCombat other, float amount, Vector3 contact, Vector3 normal)
        {
            if (amount < 0.05f) return;
            _world.Damage.Add(new DamageRequest
            {
                Ship = victim.Id, Attacker = other.Id, Weapon = "collision", Amount = amount,
                ShieldMult = 0f, HullMult = 1f, SubsystemMult = 0.5f,
                Point = contact, Direction = -normal, Normal = normal, Part = -1, Kind = DamageKind.Collision, BypassShield = true,
            });
        }

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    }
}
