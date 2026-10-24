using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Monolith.Core;
using UnityEngine;

namespace Monolith.AI
{
    /// <summary>
    /// What the AI's pilots did, by skill (profile @ difficulty, the <see cref="Skill.Key"/>): time on a target's tail, gun shots and hits, missiles,
    /// kills and losses, collisions with capital hulls, and time stuck. The arena and the soak read it; nothing in play depends on it.
    /// "On the tail" is: within <c>tailRange</c> of a fighter or bomber target, the target within <c>tailNoseDegrees</c> of the nose and this
    /// ship within <c>tailAspectDegrees</c> of the target's tail.
    /// </summary>
    public sealed class AiStats : IDisposable
    {
        public sealed class Line
        {
            public string Key;
            public int Ships;
            public int Shots, Hits, Missiles, Torpedoes, Kills, Deaths, HullCollisions, Peels, Evades;
            public float Damage, FightSeconds, TailSeconds, StuckSeconds, AliveSeconds, PeelHullSum;
            public float PeelHull => Peels > 0 ? PeelHullSum / Peels : 0f;
            public float HitRate => Shots > 0 ? (float)Hits / Shots : 0f;
            public float TailShare => FightSeconds > 0.01f ? TailSeconds / FightSeconds : 0f;
        }

        readonly AiDirector _ai;
        readonly Dictionary<string, Line> _lines = new Dictionary<string, Line>();
        readonly Dictionary<int, string> _keyOf = new Dictionary<int, string>();
        readonly Dictionary<int, float> _slowFor = new Dictionary<int, float>();
        readonly List<IDisposable> _subs = new List<IDisposable>();
        long _lastHitKey;

        public IReadOnlyDictionary<string, Line> Lines => _lines;
        /// <summary>Longest time any one AI ship has been slower than 3 m/s (stuck), seconds.</summary>
        public float LongestStuck { get; private set; }
        public string LongestStuckShip { get; private set; }
        public int HullCollisions { get; private set; }

        public AiStats(AiDirector ai)
        {
            _ai = ai;
            _subs.Add(GameEvents.Subscribe(CombatEvents.WeaponFired, OnFired));
            _subs.Add(GameEvents.Subscribe(CombatEvents.ShipDestroyed, OnDestroyed));
        }

        public void Dispose() { foreach (var s in _subs) s.Dispose(); _subs.Clear(); }

        public void Reset()
        {
            _lines.Clear(); _slowFor.Clear(); LongestStuck = 0f; LongestStuckShip = null; HullCollisions = 0;
            foreach (var kv in _keyOf) Get(kv.Value).Ships++;
        }

        /// <summary>Count a ship's deeds under a skill key (also any non-AI ship, such as the player's, under a name).</summary>
        public void Track(int ship, string key)
        {
            _keyOf[ship] = key;
            Get(key).Ships++;
        }

        public Line Get(string key)
        {
            if (!_lines.TryGetValue(key, out var l)) _lines[key] = l = new Line { Key = key };
            return l;
        }

        Line Of(int ship) => _keyOf.TryGetValue(ship, out var k) ? Get(k) : null;

        void OnFired(GameEvent e)
        {
            var l = Of(e.Entity);
            if (l == null || string.IsNullOrEmpty(e.Subject)) return;
            var w = WeaponCatalog.Get(e.Subject);
            if (w != null && w.Kind == WeaponKind.Gun) l.Shots++;
        }

        public void OnMissile(int owner, bool torpedo)
        {
            var l = Of(owner);
            if (l == null) return;
            if (torpedo) l.Torpedoes++; else l.Missiles++;
        }

        /// <summary>A gun hit counts once even when it breaks a shield (shield and hull events in the same tick).</summary>
        public void OnHit(GameEvent e)
        {
            var l = Of(e.Other);
            if (l == null || string.IsNullOrEmpty(e.Subject)) return;
            l.Damage += e.Value;
            var w = WeaponCatalog.Get(e.Subject);
            if (w == null || w.Kind != WeaponKind.Gun) return;
            long key = ((long)e.Tick << 32) ^ ((long)e.Other << 16) ^ e.Entity ^ ((long)e.Subject.GetHashCode() << 48);
            if (key == _lastHitKey) return;
            _lastHitKey = key;
            l.Hits++;
        }

        public void OnCollision(GameEvent e)
        {
            var a = _ai.World.Combat.Get(e.Entity); var b = _ai.World.Combat.Get(e.Other);
            if (a == null || b == null) return;
            bool aBig = AiDirector.IsBig(a), bBig = AiDirector.IsBig(b);
            if (aBig == bBig) return;
            int small = aBig ? b.Id : a.Id;
            var l = Of(small);
            if (l == null) return;
            l.HullCollisions++;
            HullCollisions++;
        }

        void OnDestroyed(GameEvent e)
        {
            var dead = Of(e.Entity);
            if (dead != null) dead.Deaths++;
            var killer = Of(e.Other);
            if (killer != null && e.Other != e.Entity) killer.Kills++;
        }

        public void Peeled(int ship, float hull01) { var l = Of(ship); if (l != null) { l.Peels++; l.PeelHullSum += hull01; } }
        public void Evaded(int ship) { var l = Of(ship); if (l != null) l.Evades++; }

        public void Step(in SimFrame frame)
        {
            var t = _ai.Data.Tuning;
            float cosNose = Mathf.Cos(t.TailNoseDegrees * Mathf.Deg2Rad), cosAspect = Mathf.Cos(t.TailAspectDegrees * Mathf.Deg2Rad);
            foreach (var p in _ai.Pilots)
            {
                var me = _ai.World.Ships.Get(p.Ship);
                var mc = _ai.World.Combat.Get(p.Ship);
                if (me == null || mc == null || !mc.Alive) continue;
                var l = Of(p.Ship);
                if (l == null) continue;
                l.AliveSeconds += frame.Dt;
                // Stuck: crawling for a long time (not one parked on purpose in formation behind a stopped leader).
                if (me.Velocity.magnitude < 3f && me.TopSpeed > 5f)
                {
                    _slowFor.TryGetValue(p.Ship, out float s); s += frame.Dt; _slowFor[p.Ship] = s;
                    if (s >= 3f && s < 3f + frame.Dt * 1.5f)
                    {
                        var tgt = _ai.World.Ships.Get(me.Target);
                        MnLog.Info($"AI slow: {me.Callsign} {me.Velocity.magnitude:0.0} m/s for 3 s, mode {p.ModeName}, goal {p.Goal.Kind}, throttle {me.Throttle:0.00}, speed scale {me.SpeedScale:0.00}, flags {mc.Flags}, target {tgt?.Callsign ?? "-"} at {(tgt != null ? Vector3.Distance(tgt.Position, me.Position) : 0f):0} m going {(tgt != null ? tgt.ForwardSpeed : 0f):0} m/s");
                    }
                    l.StuckSeconds += frame.Dt;
                    if (s > LongestStuck) { LongestStuck = s; LongestStuckShip = me.Callsign; }
                }
                else _slowFor[p.Ship] = 0f;

                var target = _ai.World.Ships.Get(me.Target);
                var tc = _ai.World.Combat.Get(me.Target);
                if (target == null || tc == null || !tc.Alive || AiDirector.IsBig(tc) || !Teams.Hostile(me.Team, target.Team)) continue;
                l.FightSeconds += frame.Dt;
                if (OnTail(me, target, t.TailRange, cosNose, cosAspect)) l.TailSeconds += frame.Dt;
            }
            // The player's ship too, when it is tracked (the soak flies it with an AI and counts it under its own key).
            var player = _ai.World.Ships.Get(_ai.World.LocalPlayerShip);
            if (player != null && _ai.PilotOf(player.Id) == null)
            {
                var l = Of(player.Id);
                var tgt = _ai.World.Ships.Get(player.Target);
                var tc = _ai.World.Combat.Get(player.Target);
                if (l != null && tgt != null && tc != null && tc.Alive && !AiDirector.IsBig(tc) && Teams.Hostile(player.Team, tgt.Team))
                {
                    l.FightSeconds += frame.Dt;
                    if (OnTail(player, tgt, t.TailRange, cosNose, cosAspect)) l.TailSeconds += frame.Dt;
                }
            }
        }

        public static bool OnTail(ShipState me, ShipState target, float range, float cosNose, float cosAspect)
        {
            Vector3 to = target.Position - me.Position;
            float d = to.magnitude;
            if (d > range || d < 1f) return false;
            Vector3 u = to / d;
            return Vector3.Dot(me.Forward, u) >= cosNose && Vector3.Dot(-target.Forward, -u) >= cosAspect;
        }

        public string Table()
        {
            var sb = new StringBuilder();
            sb.AppendLine("profile@difficulty                 ships  kills  lost  tail%  shots  hits  hit%  missiles  torps  peels  evades  hullhits  stuck s");
            var keys = new List<string>(_lines.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                var l = _lines[k];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-34} {1,5} {2,6} {3,5} {4,6:0.0} {5,6} {6,5} {7,5:0.0} {8,9} {9,6} {10,6} {11,7} {12,9} {13,8:0.0}",
                    k, l.Ships, l.Kills, l.Deaths, l.TailShare * 100f, l.Shots, l.Hits, l.HitRate * 100f, l.Missiles, l.Torpedoes, l.Peels, l.Evades, l.HullCollisions, l.StuckSeconds));
            }
            return sb.ToString();
        }
    }
}
