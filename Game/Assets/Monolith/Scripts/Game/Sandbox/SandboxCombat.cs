using System;
using System.Collections.Generic;
using Monolith.Core;
using Monolith.Flight;
using Monolith.Juice;
using Monolith.View;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>One ship in the juice sandbox: what it is, what it has left, how to bring it back.</summary>
    public sealed class SandboxShip
    {
        public int Id;
        public string Name;
        public int Team;
        public string Kind;            // fighter, bomber, cruiser, destroyer, wingman
        public float Shield, ShieldMax, Hull, HullMax;
        public bool Alive;
        public float RespawnAt;
        public Func<SandboxShip> Respawn;
        public bool Capital => Kind == "cruiser" || Kind == "destroyer";
        public string SizeClass => Capital ? CombatEvents.SizeCapital : Kind == "bomber" ? CombatEvents.SizeBomber : CombatEvents.SizeFighter;
    }

    /// <summary>
    /// A stand-in for the weapons, collision and damage systems that do not exist yet, so the juice can be tuned against something
    /// that behaves like a fight: the player's guns on RT, bolts that travel at FS2 speeds and hit shields then hulls, targets
    /// that die and come back, and an optional ambient battle (turrets trading heavy lasers, fighters firing). It raises exactly the
    /// events a real weapons system will (<see cref="CombatEvents"/>) and nothing else reaches the juice from here.
    /// </summary>
    public sealed class SandboxCombat : ISimSystem
    {
        sealed class Bolt
        {
            public Vector3 Origin, Dir;
            public float Speed, T0, Life, Damage;
            public int Shooter, Team;
            public string Weapon;
            public bool Heavy, Passed;
        }

        sealed class Timed { public float At; public Action Do; }

        readonly GameWorld _world;
        readonly FlightSystem _flight;
        readonly JuiceDirector _juice;
        readonly List<SandboxShip> _ships = new List<SandboxShip>();
        readonly Dictionary<int, SandboxShip> _byId = new Dictionary<int, SandboxShip>();
        readonly List<Bolt> _bolts = new List<Bolt>(256);
        readonly List<Timed> _timed = new List<Timed>();
        readonly System.Random _rng = new System.Random(7771);
        int _player;
        float _playerCooldown;
        bool _leftGun;
        float _nextAmbient;
        float[] _nextShot = new float[16];

        public bool Battle;
        public bool Blocked;                       // the panel is open: the guns are quiet
        public int PlayerShots;
        public int Kills;
        public string LastAction = "";
        public IReadOnlyList<SandboxShip> Ships => _ships;

        public int Order => SimOrder.Weapons + 10;

        public SandboxCombat(GameWorld world, FlightSystem flight, JuiceDirector juice, int playerShip)
        {
            _world = world; _flight = flight; _juice = juice; _player = playerShip;
        }

        public void SetPlayer(int id) { _player = id; }

        public SandboxShip Add(SandboxShip s)
        {
            _ships.Add(s);
            _byId[s.Id] = s;
            s.Alive = true;
            return s;
        }

        float Now => _world.Time;
        float Rand(float a, float b) => a + (b - a) * (float)_rng.NextDouble();

        public void Schedule(float seconds, Action a) => _timed.Add(new Timed { At = Now + seconds, Do = a });

        // ------------------------------------------------------------------------------------------------ the tick

        public void Step(in SimFrame frame)
        {
            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                if (Now >= _timed[i].At)
                {
                    var a = _timed[i].Do; _timed.RemoveAt(i);
                    try { a(); } catch (Exception e) { MnLog.Error("SANDBOX timed action threw: " + e); }
                }
            }

            PlayerGuns();
            if (Battle) AmbientFire();
            StepBolts();

            // Targets that died come back (a fresh ship takes the old one's place).
            List<Func<SandboxShip>> due = null;
            foreach (var s in _ships)
                if (!s.Alive && s.Respawn != null && Now >= s.RespawnAt) { (due ??= new List<Func<SandboxShip>>()).Add(s.Respawn); s.Respawn = null; }
            if (due != null) foreach (var spawn in due) spawn();
            _ships.RemoveAll(s => !s.Alive && s.Respawn == null);
        }

        // ------------------------------------------------------------------------------------------------ the player's guns

        void PlayerGuns()
        {
            _playerCooldown -= Sim.Dt;
            var me = _world.Ships.Get(_player);
            if (me == null || Blocked) return;
            var input = _world.Pilots.Current(_player);
            if (!input.Held(ControlButtons.PrimaryFire) || _playerCooldown > 0f) return;
            _playerCooldown = 0.115f;
            _leftGun = !_leftGun;
            Vector3 muzzle = me.Position + me.Rotation * new Vector3(_leftGun ? -2.9f : 2.9f, -0.35f, 3.4f);
            Vector3 aimPoint = me.Position + me.Rotation * new Vector3(0f, 0f, 650f);
            // Aim a little at whatever the target is, if it is close to the nose (a lead indicator would do this).
            var t = _world.Ships.Get(me.Target);
            if (t != null)
            {
                Vector3 to = t.Position - me.Position;
                if (Vector3.Angle(me.Forward, to) < 7f)
                {
                    float tof = to.magnitude / 620f;
                    aimPoint = t.Position + t.Velocity * tof;
                }
            }
            Fire(me.Id, 0, "laser_player", muzzle, (aimPoint - muzzle).normalized, 620f, 14f, 2.6f, false, me.Target);
            PlayerShots++;
        }

        /// <summary>Fire a bolt by hand (the sandbox's near miss and volley buttons).</summary>
        public void FireScripted(int shooter, int team, string weapon, Vector3 muzzle, Vector3 dir, float speed, float damage, float life, bool heavy = false)
            => Fire(shooter, team, weapon, muzzle, dir, speed, damage, life, heavy);

        void Fire(int shooter, int team, string weapon, Vector3 muzzle, Vector3 dir, float speed, float damage, float life, bool heavy, int target = GameWorld.NoShip)
        {
            if (_bolts.Count > 260) return;
            _bolts.Add(new Bolt { Origin = muzzle, Dir = dir, Speed = speed, T0 = (_world.Tick + 1) * Sim.Dt, Life = life, Damage = damage, Shooter = shooter, Team = team, Weapon = weapon, Heavy = heavy });
            GameEvents.RaiseAt(CombatEvents.WeaponFired, muzzle, dir, weapon, speed, shooter, target);
        }

        // ------------------------------------------------------------------------------------------------ bolts

        void StepBolts()
        {
            float tNow = (_world.Tick + 1) * Sim.Dt;       // the end of this tick
            if (_bolts.Count > 0) Physics.SyncTransforms();   // ships' views move every frame; their collision proxies must follow before a ray is cast
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var b = _bolts[i];
                float age = tNow - b.T0;
                if (age > b.Life) { _bolts.RemoveAt(i); continue; }
                float a0 = Mathf.Max(0f, age - Sim.Dt);
                Vector3 p0 = b.Origin + b.Dir * (b.Speed * a0), p1 = b.Origin + b.Dir * (b.Speed * age);

                // The nearest thing it crosses.
                SandboxShip hitShip = null; Vector3 hitPoint = default, hitNormal = default; float best = float.MaxValue;
                foreach (var s in _ships)
                {
                    if (!s.Alive || s.Team == b.Team) continue;
                    if (s.Id == b.Shooter) continue;
                    if (SegmentHit(p0, p1, s, out var hp, out var hn))
                    {
                        float d = (hp - p0).sqrMagnitude;
                        if (d < best) { best = d; hitShip = s; hitPoint = hp; hitNormal = hn; }
                    }
                }
                var me = _world.Ships.Get(_player);
                bool hitPlayer = false;
                if (b.Team != 0 && me != null)
                {
                    Vector3 c = me.Position; float r = 7f;
                    if (SegmentSphere(p0, p1, c, r, out var hp, out var hn)) { float d = (hp - p0).sqrMagnitude; if (d < best) { best = d; hitPlayer = true; hitPoint = hp; hitNormal = hn; } }
                    // Near miss: close, but not a hit.
                    if (!hitPlayer && !b.Passed)
                    {
                        Vector3 closest = HullGeometry.ClosestOnSegment(p0, p1, c);
                        float d = Vector3.Distance(closest, c);
                        if (d < 38f && d > r && Vector3.Dot(c - p0, b.Dir) > 0f && Vector3.Dot(c - p1, b.Dir) < 0f + 1f)
                        {
                            b.Passed = true;
                            GameEvents.RaiseAt(CombatEvents.NearMiss, closest, b.Dir, b.Weapon, d, b.Shooter, _player);
                        }
                    }
                }
                if (hitPlayer) { HitPlayer(b, hitPoint, hitNormal); _bolts.RemoveAt(i); continue; }
                if (hitShip != null) { HitShip(b, hitShip, hitPoint, hitNormal); _bolts.RemoveAt(i); }
            }
        }

        void HitShip(Bolt b, SandboxShip s, Vector3 point, Vector3 normal)
        {
            float dmg = b.Damage;
            if (s.Shield > 0f)
            {
                s.Shield = Mathf.Max(0f, s.Shield - dmg);
                GameEvents.RaiseAt(CombatEvents.HitShield, point, b.Dir, b.Weapon, dmg, s.Id, b.Shooter);
                return;
            }
            s.Hull -= dmg;
            GameEvents.RaiseAt(CombatEvents.HitHull, point, normal, b.Weapon, dmg, s.Id, b.Shooter);
            if (s.Hull <= 0f && !s.Capital) Destroy(s, b.Shooter);
        }

        void HitPlayer(Bolt b, Vector3 point, Vector3 normal)
        {
            // The player is never killed here: the sandbox is for feeling hits, not for losing.
            bool shield = _rng.NextDouble() < 0.5;
            if (shield) GameEvents.RaiseAt(CombatEvents.HitShield, point, b.Dir, b.Weapon, b.Damage, _player, b.Shooter);
            else GameEvents.RaiseAt(CombatEvents.HitHull, point, normal, b.Weapon, b.Damage, _player, b.Shooter);
        }

        bool SegmentHit(Vector3 p0, Vector3 p1, SandboxShip s, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = default;
            var st = _world.Ships.Get(s.Id);
            if (st == null) return false;
            var look = _juice.Context.LookOf(s.Id);
            Vector3 centre = st.Position, half = new Vector3(st.Radius, st.Radius, st.Radius) * 0.5f;
            if (look != null) { centre = st.Position + st.Rotation * look.LocalCentre; half = look.HalfExtents; }
            if (s.Capital)
            {
                Vector3 d = p1 - p0; float len = d.magnitude;
                if (len < 1e-4f) return false;
                // The real shape (a ship model's collision proxies) when it has one, else a box round the hull.
                if (!HullGeometry.Raycast(_juice.Context, s.Id, p0, d / len, len, out var hit, out var n, 0.9f)) return false;
                point = hit; normal = n; return true;
            }
            Vector3 radii = look != null && look.ShieldRadii.sqrMagnitude > 1f ? look.ShieldRadii * 0.92f : half * 1.4f;
            return SegmentEllipsoid(p0, p1, centre, st.Rotation, radii, out point, out normal);
        }

        static bool SegmentSphere(Vector3 p0, Vector3 p1, Vector3 c, float r, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = default;
            Vector3 d = p1 - p0; float len = d.magnitude;
            if (len < 1e-5f) return false;
            Vector3 dir = d / len, m = p0 - c;
            float b = Vector3.Dot(m, dir), cc = Vector3.Dot(m, m) - r * r;
            if (cc > 0f && b > 0f) return false;
            float disc = b * b - cc;
            if (disc < 0f) return false;
            float t = -b - Mathf.Sqrt(disc);
            if (t < 0f) t = 0f;
            if (t > len) return false;
            point = p0 + dir * t; normal = (point - c).normalized;
            return true;
        }

        static bool SegmentEllipsoid(Vector3 p0, Vector3 p1, Vector3 c, Quaternion rot, Vector3 radii, out Vector3 point, out Vector3 normal)
        {
            var inv = Quaternion.Inverse(rot);
            Vector3 a = inv * (p0 - c), b = inv * (p1 - c);
            a = new Vector3(a.x / radii.x, a.y / radii.y, a.z / radii.z);
            b = new Vector3(b.x / radii.x, b.y / radii.y, b.z / radii.z);
            if (!SegmentSphere(a, b, Vector3.zero, 1f, out var hp, out _)) { point = default; normal = default; return false; }
            Vector3 local = new Vector3(hp.x * radii.x, hp.y * radii.y, hp.z * radii.z);
            point = c + rot * local;
            var nl = new Vector3(hp.x / radii.x, hp.y / radii.y, hp.z / radii.z).normalized;
            normal = (rot * nl).normalized;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ deaths

        /// <summary>Kill a ship: raise the event the juice listens for, then take it out of the world (it comes back after a while).</summary>
        public void Destroy(SandboxShip s, int killer)
        {
            var st = _world.Ships.Get(s.Id);
            if (st == null || !s.Alive) return;
            s.Alive = false;
            GameEvents.RaiseAt(CombatEvents.ShipDestroyed, st.Position, Vector3.zero, s.SizeClass, st.Radius, s.Id, killer);
            Kills++;
            _juice.ForgetShip(s.Id);
            _flight.Despawn(s.Id);
            s.RespawnAt = Now + (s.Capital ? 17f : s.Kind == "bomber" ? 6f : 4f);
            // Anything aimed at it loses its target.
            LastAction = $"{s.Name} destroyed";
        }

        // ------------------------------------------------------------------------------------------------ the ambient battle

        void AmbientFire()
        {
            if (Now < _nextAmbient) return;
            _nextAmbient = Now + 0.07f;
            var alive = _ships.FindAll(s => s.Alive);
            for (int i = 0; i < alive.Count && i < _nextShot.Length; i++)
            {
                var s = alive[i];
                if (Now < _nextShot[i]) continue;
                var st = _world.Ships.Get(s.Id);
                if (st == null) continue;
                var foes = alive.FindAll(o => o.Team != s.Team);
                if (s.Capital)
                {
                    // Turrets: slow heavy bolts, many of them, at the other side's hull.
                    var foeCap = foes.Find(o => o.Capital);
                    _nextShot[i] = Now + Rand(0.12f, 0.34f);
                    var foeSt = foeCap != null ? _world.Ships.Get(foeCap.Id) : null;
                    if (foeSt == null) continue;
                    var look = _juice.Context.LookOf(s.Id);
                    Vector3 half = look != null ? look.HalfExtents : new Vector3(100, 80, 500);
                    int guns = _rng.NextDouble() < 0.45 ? 2 : 1;
                    for (int g = 0; g < guns; g++)
                    {
                        Vector3 local = new Vector3((_rng.NextDouble() < 0.5 ? -1f : 1f) * half.x * 0.92f, Rand(-0.2f, 0.7f) * half.y, Rand(-0.85f, 0.85f) * half.z);
                        Vector3 muzzle = st.Position + st.Rotation * local;
                        Vector3 target = foeSt.Position + foeSt.Rotation * new Vector3(Rand(-0.8f, 0.8f) * 100f, Rand(-0.6f, 0.6f) * 70f, Rand(-0.85f, 0.85f) * 520f);
                        Vector3 dir = (target - muzzle).normalized;
                        string id = s.Team == 0 ? "laser_heavy" : "laser_heavy_enemy";
                        Fire(s.Id, s.Team, id, muzzle + dir * 8f, dir, 340f, 120f, 8f, true);
                    }
                }
                else if (s.Team == 1)
                {
                    // Enemy fighters: bursts at the player's ship or, more often, at the friendly cruiser.
                    var me = _world.Ships.Get(_player);
                    bool atPlayer = me != null && _rng.NextDouble() < 0.45;
                    _nextShot[i] = Now + Rand(0.6f, 1.6f);
                    Vector3 target = atPlayer ? me.Position + me.Velocity * 0.3f : (foes.Find(o => o.Capital) is SandboxShip cap && _world.Ships.Get(cap.Id) is ShipState cs ? cs.Position + cs.Rotation * new Vector3(0, 0, Rand(-400f, 400f)) : st.Position + st.Forward * 800f);
                    int shots = _rng.Next(2, 5);
                    for (int k = 0; k < shots; k++)
                    {
                        float delay = k * 0.11f;
                        Vector3 spread = new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), 0f) * (atPlayer ? 22f : 8f);
                        int shooter = s.Id; int sTeam = s.Team;
                        Schedule(delay, () =>
                        {
                            var sst = _world.Ships.Get(shooter); if (sst == null) return;
                            Vector3 mz = sst.Position + sst.Rotation * new Vector3((k & 1) == 0 ? -2.4f : 2.4f, -0.3f, 3f);
                            Vector3 d = (target + spread - mz).normalized;
                            Fire(shooter, sTeam, "laser_enemy", mz, d, 520f, 12f, 3.2f, false);
                        });
                    }
                }
                else
                {
                    // Our wingmen: bursts at the enemy fighters.
                    var foe = foes.Find(o => !o.Capital && o.Alive);
                    _nextShot[i] = Now + Rand(0.5f, 1.3f);
                    if (foe == null) continue;
                    var foeSt = _world.Ships.Get(foe.Id);
                    if (foeSt == null) continue;
                    int shots = _rng.Next(2, 5);
                    for (int k = 0; k < shots; k++)
                    {
                        int shooter = s.Id; int target = foe.Id;
                        Schedule(k * 0.11f, () =>
                        {
                            var sst = _world.Ships.Get(shooter); var tst = _world.Ships.Get(target); if (sst == null || tst == null) return;
                            Vector3 mz = sst.Position + sst.Rotation * new Vector3((k & 1) == 0 ? -2.4f : 2.4f, -0.3f, 3f);
                            Vector3 aim = tst.Position + tst.Velocity * (Vector3.Distance(tst.Position, mz) / 620f) + new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), 0f) * 9f;
                            Fire(shooter, 0, "laser_player", mz, (aim - mz).normalized, 620f, 14f, 2.6f, false);
                        });
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ helpers for the actions

        public SandboxShip Find(Func<SandboxShip, bool> pred) { foreach (var s in _ships) if (s.Alive && pred(s)) return s; return null; }
        public SandboxShip Get(int id) => _byId.TryGetValue(id, out var s) ? s : null;

        /// <summary>The best candidate in front of the player: the HUD target if it fits, else the one nearest the nose.</summary>
        public SandboxShip Pick(Func<SandboxShip, bool> fits)
        {
            var me = _world.Ships.Get(_player);
            if (me == null) return null;
            var targeted = Get(me.Target);
            if (targeted != null && targeted.Alive && fits(targeted)) return targeted;
            SandboxShip best = null; float bestAngle = float.MaxValue;
            foreach (var s in _ships)
            {
                if (!s.Alive || !fits(s)) continue;
                var st = _world.Ships.Get(s.Id);
                if (st == null) continue;
                float a = Vector3.Angle(me.Forward, st.Position - me.Position);
                if (a < bestAngle) { bestAngle = a; best = s; }
            }
            return best;
        }

        /// <summary>A point on the surface of a ship facing <paramref name="from"/> (for hits raised by hand), with the outward normal.</summary>
        public bool SurfaceFacing(SandboxShip s, Vector3 from, out Vector3 point, out Vector3 normal)
        {
            var st = _world.Ships.Get(s.Id);
            point = st != null ? st.Position : Vector3.zero; normal = Vector3.up;
            if (st == null) return false;
            Vector3 aim = st.Position + st.Rotation * (s.Capital ? new Vector3(0f, 0f, Rand(-0.4f, 0.4f) * 500f) : Vector3.zero);
            Vector3 dir = (aim - from).normalized;
            if (SegmentHit(from, aim + dir * 5f, s, out point, out normal)) return true;
            point = aim; normal = -dir;
            return true;
        }

        public int PlayerId => _player;
    }
}
