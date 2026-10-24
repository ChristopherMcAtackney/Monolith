using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.AI
{
    /// <summary>How a capital ship fights (data, from the scene).</summary>
    public sealed class CapitalOrders
    {
        /// <summary>Range it tries to hold from the enemy's capital ship, metres, give or take <see cref="Band"/>.</summary>
        public float Standoff = 2600f, Band = 400f;
        /// <summary>Degrees off the bow it keeps the enemy at (90 is broadside), so its turrets and beams bear.</summary>
        public float Bearing = 70f;
        public float Throttle = 0.35f;
        /// <summary>The gunners' AI profile (turrets use its accuracy) and the scene's slowing of their fire on top of the difficulty's.</summary>
        public string TurretProfile = "earth_turret";
        public float TurretWaitScale = 1f, BeamWaitScale = 1f;
        /// <summary>Parts that fire something other than their part kind's weapon: part name to weapon id (a capital's beam turrets).</summary>
        public Dictionary<string, string> Weapons = new Dictionary<string, string>();
        /// <summary>Mission control: beams held (FS2's beam-free / beam-lock).</summary>
        public bool HoldBeams;
        /// <summary>Mission control: every gun and flak turret holds its fire (a station that has not woken up yet); beams follow <see cref="HoldBeams"/>.</summary>
        public bool HoldTurrets;
        /// <summary>Holds still (no manoeuvring), for tests and stationary emplacements.</summary>
        public bool Stationary;
    }

    /// <summary>
    /// A capital ship's helm (FS2 cruisers and corvettes): it turns slowly to keep the enemy capital ship at a bearing that brings its
    /// broadside and beams to bear, holds a standoff range, and keeps clear of other capital ships. Its guns are <see cref="TurretGunnery"/>'s.
    /// With every engine destroyed it is dead in space: no helm at all.
    /// </summary>
    public sealed class CapitalPilot : IPilotSource
    {
        readonly AiDirector _ai;
        public readonly int Ship;
        public readonly CapitalOrders Orders;
        internal List<TurretGunnery.Turret> Turrets;

        public CapitalPilot(AiDirector ai, int ship, CapitalOrders orders) { _ai = ai; Ship = ship; Orders = orders ?? new CapitalOrders(); }

        public ControlInput Sample(in SimFrame frame)
        {
            var input = default(ControlInput);
            var me = _ai.World.Ships.Get(Ship);
            var mc = _ai.World.Combat.Get(Ship);
            if (me == null || mc == null || !mc.Alive || Orders.Stationary) return input;
            if ((mc.Flags & CombatFlags.EnginesDown) != 0) return input;
            input.Throttle = Orders.Throttle;

            ShipCombat enemy = null; float bd = float.MaxValue;
            foreach (var b in _ai.Big)
            {
                if (!Teams.Hostile(me.Team, b.Ship.Team)) continue;
                float d = (b.Ship.Position - me.Position).sqrMagnitude;
                if (d < bd) { bd = d; enemy = b; }
            }
            Vector3 heading = me.Forward;
            if (enemy != null)
            {
                Vector3 flat = Vector3.ProjectOnPlane(enemy.Ship.Position - me.Position, Vector3.up);
                float dist = flat.magnitude;
                if (dist > 1f)
                {
                    flat /= dist;
                    float bearing = Orders.Bearing;
                    if (dist > Orders.Standoff + Orders.Band) bearing = Mathf.Max(10f, bearing - 40f);
                    else if (dist < Orders.Standoff - Orders.Band) bearing = Mathf.Min(150f, bearing + 40f);
                    float side = Vector3.Dot(flat, me.Rotation * Vector3.right) >= 0f ? 1f : -1f;
                    heading = Quaternion.AngleAxis(-side * bearing, Vector3.up) * flat;
                }
            }
            // Keep clear of other capital ships.
            foreach (var b in _ai.Big)
            {
                if (b.Id == Ship) continue;
                Vector3 r = b.Ship.Position - me.Position;
                float clear = mc.Shape.BoundRadius + b.Shape.BoundRadius + 500f;
                if (r.sqrMagnitude < clear * clear && Vector3.Dot(r, me.Forward) > 0f) heading = (heading - r.normalized * 2f).normalized;
            }
            AiSteer.TurnTo(me, heading, 1f, ref input, Vector3.up, 0f);
            return input;
        }
    }

    /// <summary>
    /// Every AI capital ship's turrets (order just before weapons), on FS2's turret rules. Each living turret part picks its own target and
    /// fires through a <see cref="FireRequest"/>, so a destroyed turret is silent and a stripped face of the ship goes quiet.
    /// <list type="bullet">
    /// <item>Field of fire: a turret fires only within its arc off its own up (FS2's turret fov test; the arc per kind is data) and where its own
    /// hull does not stand in the way, so the ship has lanes no turret covers.</item>
    /// <item>Targets (FS2's turret target choice): anti-fighter guns first shoot down torpedoes coming at their side's capital ships; then each
    /// turret takes the nearest enemy in its arc, every turret of this ship already on that enemy adding a tenth to its distance (bombers, and
    /// ships attacking this one, count nearer), and never more of one ship's turrets on one target than the level allows, fewer on the player;
    /// lasers also fire at enemy capital ships in reach. Beam turrets go for enemy capital ships, one beam at a time per ship. A fresh choice
    /// about every two seconds, or at once when the target leaves the arc.</item>
    /// <item>Aim: the gunner leads from a picture of its target that it refreshes at random intervals (longer on easier levels), so weaving beats
    /// it; its error grows with range and falls with accuracy and the level, and is several times bigger when a target has just come into the
    /// arc, settling over a few seconds while it stays there (loitering in a turret's arc is punished, passing through is not). Flak is aimed
    /// into the target's path and scattered the same way.</item>
    /// <item>Rate: the weapon's fire wait times the level's fire delay (FS2 scales turret waits by skill), times the scene's pacing scale.</item>
    /// </list>
    /// </summary>
    public sealed class TurretGunnery : ISimSystem
    {
        internal enum Kind { Gun, Flak, Beam }

        internal sealed class Turret
        {
            public int Part;
            public WeaponDef Def;
            public Kind Kind;
            public int Target = GameWorld.NoShip, Torpedo = -1;
            public float RetargetAt, NextFireAt, ErrUntil, LastFireAt = -999f;
            public Vector3 Err;
            /// <summary>Out of the hull at the turret, ship-local (from the hull box it sits on): the turret's up.</summary>
            public Vector3 Normal;
            /// <summary>Cosine of the turret's field of fire off <see cref="Normal"/>.</summary>
            public float CosArc;
            /// <summary>The gunner's picture of its target (refreshed now and then, extrapolated in between) and since when the target has been in the arc.</summary>
            public Vector3 SeenPos, SeenVel;
            public float SeenAt, NextLookAt, TrackSince;
        }

        readonly AiDirector _ai;
        readonly Dictionary<int, int> _on = new Dictionary<int, int>();
        uint _rng = 0x51ED270Bu;
        float _now;

        public TurretGunnery(AiDirector ai) { _ai = ai; }
        public int Order => SimOrder.Weapons - 10;
        /// <summary>Shots requested so far, by kind (checks).</summary>
        public int GunShots, FlakShots, BeamShots, TorpedoShots;
        /// <summary>Gun and flak shots requested so far at each ship (checks, the Warden runs).</summary>
        public readonly Dictionary<int, int> ShotsAt = new Dictionary<int, int>();

        /// <summary>How many living turrets, on every AI capital ship, have fired at <paramref name="target"/> in the last <paramref name="window"/> seconds.</summary>
        public int FiringAt(int target, float window = 2f)
        {
            int n = 0;
            foreach (var cap in _ai.CapitalPilots)
            {
                if (cap.Turrets == null) continue;
                var c = _ai.World.Combat.Get(cap.Ship);
                if (c == null || !c.Alive) continue;
                foreach (var t in cap.Turrets)
                    if (t.Target == target && _now - t.LastFireAt <= window && !c.Parts[t.Part].Destroyed) n++;
            }
            return n;
        }

        /// <summary>How many of a capital ship's living turrets have <paramref name="target"/> as their target now (checks).</summary>
        public int TurretsOn(int capital, int target)
        {
            int n = 0;
            foreach (var cap in _ai.CapitalPilots)
            {
                if (cap.Ship != capital || cap.Turrets == null) continue;
                var c = _ai.World.Combat.Get(cap.Ship);
                if (c == null || !c.Alive) continue;
                foreach (var t in cap.Turrets) if (t.Target == target && !c.Parts[t.Part].Destroyed) n++;
            }
            return n;
        }

        float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / 16777216f; }
        Vector3 RandUnit() { var v = new Vector3(Rand() * 2f - 1f, Rand() * 2f - 1f, Rand() * 2f - 1f); return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.up; }
        int On(int target) => _on.TryGetValue(target, out int n) ? n : 0;
        void Count(int target, int delta) { if (target != GameWorld.NoShip) _on[target] = On(target) + delta; }

        public void Step(in SimFrame frame)
        {
            _now = frame.Time;
            foreach (var cap in _ai.CapitalPilots)
            {
                var c = _ai.World.Combat.Get(cap.Ship);
                if (c == null || !c.Alive || c.Ship == null) continue;
                if (cap.Turrets == null) cap.Turrets = Build(c, cap.Orders, _ai.Data.Tuning);
                var skill = _ai.Data.Resolve(cap.Orders.TurretProfile, _ai.Level, _ai.IsHostileSide(c.Ship.Team));
                _on.Clear();
                foreach (var t in cap.Turrets) if (t.Target != GameWorld.NoShip && !c.Parts[t.Part].Destroyed) Count(t.Target, 1);
                foreach (var t in cap.Turrets) Work(cap, c, t, skill);
            }
        }

        static List<Turret> Build(ShipCombat c, CapitalOrders orders, AiTuning tuning)
        {
            var list = new List<Turret>();
            foreach (var p in c.Parts)
            {
                string weapon = orders.Weapons.TryGetValue(p.Name, out var w) ? w : p.IsTurret ? p.Weapon : null;
                var def = WeaponCatalog.Get(weapon);
                if (def == null) continue;
                var kind = def.IsBeam ? Kind.Beam : def.Kind == WeaponKind.Flak ? Kind.Flak : def.Kind == WeaponKind.Gun ? Kind.Gun : (Kind)(-1);
                if ((int)kind < 0) continue;
                float arc = kind == Kind.Beam ? tuning.TurretArcBeam : kind == Kind.Flak ? tuning.TurretArcFlak : tuning.TurretArcGun;
                list.Add(new Turret { Part = p.Index, Def = def, Kind = kind, Normal = OutwardNormal(c, p.LocalPosition), CosArc = Mathf.Cos(arc * Mathf.Deg2Rad) });
            }
            MnLog.Info($"AI turrets on {c.Ship.Callsign}: {list.Count} ({list.FindAll(t => t.Kind == Kind.Flak).Count} flak, {list.FindAll(t => t.Kind == Kind.Gun).Count} guns, {list.FindAll(t => t.Kind == Kind.Beam).Count} beams)");
            return list;
        }

        void Work(CapitalPilot cap, ShipCombat c, Turret t, Skill skill)
        {
            var part = c.Parts[t.Part];
            if (part.Destroyed)
            {
                if (t.Target != GameWorld.NoShip) { Count(t.Target, -1); t.Target = GameWorld.NoShip; }
                t.Torpedo = -1;
                return;
            }
            if (part.Cooldown > 0f || _now < t.NextFireAt) return;
            if (t.Kind == Kind.Beam && (cap.Orders.HoldBeams || _ai.Beaming(c.Id))) return;
            if (t.Kind != Kind.Beam && cap.Orders.HoldTurrets) return;
            var ship = c.Ship;
            Vector3 from = part.WorldPosition(ship);

            if (_now >= t.RetargetAt || !Valid(c, t, part, from))
            {
                t.RetargetAt = _now + (t.Kind == Kind.Beam ? 3f : _ai.Data.Tuning.TurretRetargetSeconds * (0.9f + 0.2f * Rand()));
                Pick(c, t, from, part, skill);
            }
            if (t.Target == GameWorld.NoShip && t.Torpedo < 0) { t.NextFireAt = _now + 0.4f + 0.3f * Rand(); return; }

            float scale = skill.FireDelay * (t.Kind == Kind.Beam ? cap.Orders.BeamWaitScale : cap.Orders.TurretWaitScale) * (0.9f + 0.2f * Rand());
            if (t.Torpedo >= 0)
            {
                AiDirector.Torpedo torp = null;
                foreach (var x in _ai.Torpedoes) if (x.Id == t.Torpedo) { torp = x; break; }
                if (torp == null) { t.Torpedo = -1; return; }
                Vector3 tp = torp.Position + torp.Velocity * Sim.Dt;
                if (!Geo.SolveIntercept(from, ship.Velocity, tp, torp.Velocity, t.Def.Speed, out _, out _, out var meet)) return;
                Vector3 aim = meet + RandUnit() * (Vector3.Distance(from, meet) * (1f - skill.Accuracy) * 0.01f);
                if (!Sees(c, part, t, from, aim, 0f)) { t.Torpedo = -1; t.RetargetAt = 0f; return; }
                if (t.Kind == Kind.Flak && NearFriend(c.Ship.Team, aim, t.Def.BlastOuter + 20f)) { t.NextFireAt = _now + 0.3f; return; }
                _ai.World.Fire.Add(new FireRequest { Ship = c.Id, Part = t.Part, Weapon = t.Def.Id, AimPoint = aim, Target = GameWorld.NoShip });
                t.NextFireAt = _now + t.Def.FireWait * scale;
                TorpedoShots++;
                return;
            }

            var target = _ai.World.Ships.Get(t.Target);
            if (target == null) return;
            if (t.Kind == Kind.Beam)
            {
                var bc = _ai.World.Combat.Get(t.Target);
                if (!Sees(c, part, t, from, target.Position, bc != null ? bc.Shape.BoundRadius : 0f)) { t.RetargetAt = 0f; t.NextFireAt = _now + 1f; return; }
                _ai.World.Fire.Add(new FireRequest { Ship = c.Id, Part = t.Part, Weapon = t.Def.Id, AimPoint = target.Position, Target = target.Id });
                t.NextFireAt = _now + (t.Def.BeamWarmup + t.Def.Life + t.Def.FireWait * scale);
                BeamShots++;
                return;
            }

            // Guns and flak. The gunner's picture of the target: refreshed at random intervals, extrapolated in between (a weave in between is missed).
            if (_now >= t.NextLookAt)
            {
                t.SeenPos = target.Position; t.SeenVel = target.Velocity; t.SeenAt = _now;
                t.NextLookAt = _now + skill.TurretAimUpdate * (0.25f + 0.75f * Rand());
            }
            Vector3 seenNow = t.SeenPos + t.SeenVel * (_now - t.SeenAt);
            if (!Geo.SolveIntercept(from, ship.Velocity, seenNow, t.SeenVel, t.Def.Speed, out _, out _, out var at)) return;
            float dist = Vector3.Distance(from, at);
            // The aim settles while the target stays in this turret's arc (from turretFreshError x down to 1 x).
            float settle = skill.TurretSettle > 0.01f ? Mathf.Clamp01((_now - t.TrackSince) / skill.TurretSettle) : 1f;
            float fresh = Mathf.Lerp(_ai.Data.Tuning.TurretFreshError, 1f, settle);
            if (_now >= t.ErrUntil) { t.Err = RandUnit() * Mathf.Sqrt(Rand()); t.ErrUntil = _now + 0.8f + 0.6f * Rand(); }
            Vector3 aimPoint;
            if (t.Kind == Kind.Flak)
            {
                // Into the target's path: past the intercept along its velocity, and scattered so the bursts bracket it.
                float spread = _ai.Data.Tuning.FlakSpreadMetres * (1.5f - skill.Accuracy) * (0.6f + 0.4f * skill.AimingError) * fresh;
                aimPoint = at + t.SeenVel * (_ai.Data.Tuning.FlakLeadSeconds * Rand()) + RandUnit() * (spread * Mathf.Sqrt(Rand()));
                if (NearFriend(c.Ship.Team, aimPoint, Mathf.Max(70f, t.Def.BlastOuter + 10f))) { t.NextFireAt = _now + 0.5f; return; }
            }
            else aimPoint = at + t.Err * (dist * (1f - skill.Accuracy) * _ai.Data.Tuning.TurretAimError * skill.AimingError * fresh);
            if (!Sees(c, part, t, from, aimPoint, 0f)) { t.RetargetAt = 0f; t.NextFireAt = _now + 0.3f; return; }
            _ai.World.Fire.Add(new FireRequest { Ship = c.Id, Part = t.Part, Weapon = t.Def.Id, AimPoint = aimPoint, Target = target.Id });
            t.NextFireAt = _now + t.Def.FireWait * scale;
            t.LastFireAt = _now;
            ShotsAt[target.Id] = (ShotsAt.TryGetValue(target.Id, out int sn) ? sn : 0) + 1;
            if (t.Kind == Kind.Flak) FlakShots++; else GunShots++;
        }

        /// <summary>The turret's target is still worth shooting: alive, hostile, in reach and in its field of fire.</summary>
        bool Valid(ShipCombat c, Turret t, CombatPart part, Vector3 from)
        {
            if (t.Torpedo >= 0) { foreach (var x in _ai.Torpedoes) if (x.Id == t.Torpedo) return true; return false; }
            var tc = _ai.World.Combat.Get(t.Target);
            if (tc == null || !tc.Alive || tc.Ship == null || !Teams.Hostile(c.Ship.Team, tc.Ship.Team)) return false;
            bool big = AiDirector.IsBig(tc);
            float radius = big ? tc.Shape.BoundRadius : tc.Ship.Radius;
            if (t.Kind != Kind.Beam && Vector3.Distance(from, tc.Ship.Position) - (big ? radius * 0.6f : 0f) > t.Def.Range) return false;
            return Sees(c, part, t, from, tc.Ship.Position, radius);
        }

        void Pick(ShipCombat c, Turret t, Vector3 from, CombatPart part, Skill skill)
        {
            int old = t.Target;
            if (old != GameWorld.NoShip) Count(old, -1);
            t.Target = GameWorld.NoShip; t.Torpedo = -1;
            float range = t.Def.Range * 0.95f;
            float best = float.MaxValue;
            if (t.Kind == Kind.Beam)
            {
                foreach (var b in _ai.Big)
                {
                    if (!Teams.Hostile(c.Ship.Team, b.Ship.Team)) continue;
                    float d = Vector3.Distance(from, b.Ship.Position) - b.Shape.BoundRadius * 0.6f;
                    if (d > t.Def.Range || d >= best || !Sees(c, part, t, from, b.Ship.Position, b.Shape.BoundRadius)) continue;
                    best = d; t.Target = b.Id;
                }
                Took(t, old);
                return;
            }
            // Anti-fighter guns and flak (FS2's answer to bombs): torpedoes coming at this ship (or a friendly capital) come first.
            if (t.Kind == Kind.Gun || t.Kind == Kind.Flak)
            {
                foreach (var x in _ai.Torpedoes)
                {
                    if (!x.IsTorpedo || !Teams.Hostile(c.Ship.Team, x.Team)) continue;
                    var tt = _ai.World.Combat.Get(x.Target);
                    if (tt == null || tt.Ship.Team != c.Ship.Team) continue;
                    float d = Vector3.Distance(from, x.Position);
                    if (d > range || !Sees(c, part, t, from, x.Position, 0f)) continue;
                    float s = d * 0.25f;
                    if (s < best) { best = s; t.Torpedo = x.Id; }
                }
                if (t.Torpedo >= 0) { Took(t, old); return; }
            }
            // FS2: the nearest, each turret already on it adding a tenth, never more turrets of this ship on one target than the level allows.
            int player = _ai.World.LocalPlayerShip;
            foreach (var s in _ai.Small)
            {
                if (!Teams.Hostile(c.Ship.Team, s.Ship.Team)) continue;
                float d = Vector3.Distance(from, s.Ship.Position);
                if (d > range || d < 30f) continue;
                int n = On(s.Id);
                if (n >= (s.Id == player ? skill.TurretsOnPlayer : skill.TurretsOnTarget)) continue;
                float score = d * (1f + 0.1f * n);
                if (s.SizeClass == CombatEvents.SizeBomber) score *= 0.6f;
                if (s.Ship.Target == c.Id) score *= 0.7f;
                if (score >= best || !Sees(c, part, t, from, s.Ship.Position, s.Ship.Radius)) continue;
                best = score; t.Target = s.Id;
            }
            // Lasers also fire at enemy capital ships in reach (after any fighter in reach).
            if (t.Kind == Kind.Gun)
                foreach (var b in _ai.Big)
                {
                    if (!Teams.Hostile(c.Ship.Team, b.Ship.Team)) continue;
                    float d = Vector3.Distance(from, b.Ship.Position) - b.Shape.BoundRadius * 0.6f;
                    if (d > range) continue;
                    float score = (2000f + d) * (1f + 0.1f * On(b.Id));
                    if (score >= best || !Sees(c, part, t, from, b.Ship.Position, b.Shape.BoundRadius)) continue;
                    best = score; t.Target = b.Id;
                }
            Took(t, old);
        }

        /// <summary>Count the turret's new choice; a new target starts its time in the arc and gets a fresh look.</summary>
        void Took(Turret t, int old)
        {
            int now = t.Torpedo >= 0 ? GameWorld.NoShip : t.Target;
            if (now == GameWorld.NoShip) return;
            Count(now, 1);
            if (now != old) { t.TrackSince = _now; t.NextLookAt = _now; }
        }

        /// <summary>A flak burst there would hurt our own side: a friendly fighter within <paramref name="r"/>, or a friendly capital's hull (this ship's own included).</summary>
        bool NearFriend(int team, Vector3 p, float r)
        {
            foreach (var s in _ai.Small)
                if (s.Ship.Team == team && (s.Ship.Position - p).sqrMagnitude < r * r) return true;
            foreach (var b in _ai.Big)
            {
                if (b.Ship.Team != team) continue;
                float reach = b.Shape.BoundRadius + r;
                if ((b.Ship.Position - p).sqrMagnitude > reach * reach) continue;
                if (ShipHits.DistanceToHull(b.Ship, b, p, out _) < r) return true;
            }
            return false;
        }

        /// <summary>
        /// The turret can fire at the point: it lies in the turret's field of fire (FS2's fov test: the cosine off the turret's up, plus
        /// radius / (distance + radius) for a target of that radius, at least the arc's cosine) and no part of its own hull stands in the way.
        /// </summary>
        static bool Sees(ShipCombat c, CombatPart part, Turret t, Vector3 from, Vector3 to, float radius)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1f) return true;
            Vector3 n = c.Ship.Rotation * t.Normal;
            if (Vector3.Dot(d / len, n) + radius / (len + radius) < t.CosArc) return false;
            return Unblocked(c, part, n, from, to);
        }

        static bool Unblocked(ShipCombat c, CombatPart part, Vector3 n, Vector3 from, Vector3 to)
        {
            var ship = c.Ship;
            Vector3 start = from + n * (part.Radius + 2f);
            var inv = Quaternion.Inverse(ship.Rotation);
            Vector3 a = inv * (start - ship.Position), b = inv * (to - ship.Position);
            foreach (var box in c.Shape.Boxes)
                if (Geo.SegmentBox(a, b, box.Centre, box.Half * 0.97f, out _, out _)) return false;
            return true;
        }

        /// <summary>
        /// A turret on this hull face could see the point at all: it lies in the turret's half of space (out from the hull face it sits on, a
        /// little past level) and no part of its own hull stands between them (the checks' looser test; the gunnery uses each turret's arc).
        /// </summary>
        public static bool LineClear(ShipCombat c, CombatPart part, Vector3 localNormal, Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1f) return true;
            Vector3 n = c.Ship.Rotation * localNormal;
            if (Vector3.Dot(d / len, n) < -0.35f) return false;
            return Unblocked(c, part, n, from, to);
        }

        /// <summary>The outward normal of the hull box face nearest a ship-local point (a turret's "up").</summary>
        public static Vector3 OutwardNormal(ShipCombat c, Vector3 p)
        {
            Vector3 best = Vector3.up; float bestDist = float.MaxValue;
            foreach (var box in c.Shape.Boxes)
            {
                Vector3 q = p - box.Centre;
                Vector3 h = Vector3.Max(box.Half, Vector3.one * 0.01f);
                Vector3 outside = new Vector3(Mathf.Max(0f, Mathf.Abs(q.x) - h.x), Mathf.Max(0f, Mathf.Abs(q.y) - h.y), Mathf.Max(0f, Mathf.Abs(q.z) - h.z));
                float dist = outside.magnitude;
                if (dist <= 0f) dist = -Mathf.Min(h.x - Mathf.Abs(q.x), Mathf.Min(h.y - Mathf.Abs(q.y), h.z - Mathf.Abs(q.z)));   // inside: how deep (negative)
                if (dist >= bestDist) continue;
                bestDist = dist;
                float rx = Mathf.Abs(q.x) / h.x, ry = Mathf.Abs(q.y) / h.y, rz = Mathf.Abs(q.z) / h.z;
                if (rx >= ry && rx >= rz) best = new Vector3(Mathf.Sign(q.x), 0f, 0f);
                else if (ry >= rz) best = new Vector3(0f, Mathf.Sign(q.y), 0f);
                else best = new Vector3(0f, 0f, Mathf.Sign(q.z));
            }
            return best;
        }
    }
}
