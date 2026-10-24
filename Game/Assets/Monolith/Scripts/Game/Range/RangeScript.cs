using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Plays the player's side of a run from a capture plan (<c>"sceneOptions": { "pilot": "scripted", "script": [ ... ] }</c>) through
    /// the same <see cref="ControlInput"/> as the pad, so a lock and a kill in a capture are the real systems doing it. Steps run at game
    /// time <c>t</c> seconds. Held: <c>fire</c> (fast gun), <c>heavy</c>, <c>missile</c> (the fire button), <c>decoy</c>, <c>aim</c>.
    /// One-tick taps: <c>cycle</c> (next missile), <c>select</c> slot, <c>target</c> name [part], <c>targetNext</c>, <c>targetAttacker</c>,
    /// <c>subsystem</c>, <c>power</c> mode. Set-up: <c>place</c>, <c>face</c> (look at a ship from a distance), <c>input</c> (sticks, throttle),
    /// <c>invulnerable</c>, <c>map</c> (the HUD's battle map on or off), <c>menu</c> (the quick menu). Checks that write PASS or FAIL lines to the log: <c>expect</c>. Debug: <c>damage</c>, <c>log</c>, <c>say</c>, <c>summary</c>.
    /// <c>aim</c> steers the nose at the lead point (or the target, or a named part) with the pitch and yaw sticks and can hold the fire button
    /// when lined up: it is the scripted stand-in for a pilot's hands.
    /// </summary>
    public sealed class RangeScript : IPilotSource, ISimSystem
    {
        sealed class ScriptStep { public int Tick; public JObject Json; public bool Done; }

        readonly GameWorld _world;
        readonly CombatRange _range;
        readonly List<ScriptStep> _steps = new List<ScriptStep>();
        ControlInput _held;
        ControlButtons _taps;
        int _tapTarget, _tapPart, _tapSelect;
        bool _fire, _heavy, _missile, _decoy, _menu;
        // aim
        string _aimMode = "off";
        string _aimPartName;
        float _aimGain = 3.5f, _aimTolerance = 1.2f, _aimFireRange = 900f;
        bool _aimAutoFire;
        string _aimGun = "fire";
        float _holdRange;                       // 0 = off: keep this far from the target by trimming the throttle
        Vector3 _prevDir;
        bool _prevDirValid, _trace;
        float _lastErr, _lastDist, _lastTol;
        bool _lastFiring;

        public int Passed { get; private set; }
        public int Failed { get; private set; }

        public RangeScript(GameWorld world, CombatRange range, JArray steps)
        {
            _world = world; _range = range;
            foreach (var t in steps)
            {
                var o = (JObject)t;
                _steps.Add(new ScriptStep { Tick = Mathf.RoundToInt((float)(o["t"] ?? 0) * Sim.Hz), Json = o });
            }
            _steps.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            LogEvents();
        }

        public int Order => SimOrder.Pilots + 40;

        static readonly string[] Logged =
        {
            CombatEvents.LockAcquired, CombatEvents.LockLost, CombatEvents.ShipDestroyed, CombatEvents.SubsystemDestroyed, CombatEvents.ShieldDown,
            CombatEvents.MissileWarning, CombatEvents.DecoyLaunched, CombatEvents.DecoySuccess, CombatEvents.ShotDown, CombatEvents.Collision,
            CombatEvents.PartDamaged, CombatEvents.PowerChanged, CombatEvents.TargetChanged,
        };

        readonly Dictionary<string, int> _seenEvents = new Dictionary<string, int>();

        void LogEvents()
        {
            foreach (var id in Logged)
                GameEvents.Subscribe(id, e =>
                {
                    _seenEvents.TryGetValue(e.Id, out int n);
                    _seenEvents[e.Id] = n + 1;
                    if (!_trace) return;
                    string who = _world.Ships.Get(e.Entity)?.Callsign ?? e.Entity.ToString();
                    string other = _world.Ships.Get(e.Other)?.Callsign ?? (e.Other == GameWorld.NoShip ? "-" : e.Other.ToString());
                    MnLog.Info($"RANGE EVENT t={e.Tick * Sim.Dt:0.00} {e.Id} {who} <- {other} {e.Subject} {e.Value:0.##}");
                });
        }

        public void Step(in SimFrame frame)
        {
            if (_trace && frame.Tick % 30 == 0)
                MnLog.Info($"RANGE TRACE t={frame.Time:0.00} aim error {_lastErr:0.00} deg (fires inside {_lastTol:0.00}), range {_lastDist:0} m, firing={_lastFiring}");
            foreach (var s in _steps)
            {
                if (s.Done || s.Tick > frame.Tick) continue;
                s.Done = true;
                try { Run(s.Json); }
                catch (Exception e) { MnLog.Error($"RANGE script step at t={s.Json["t"]} ({s.Json["do"]}) failed: {e.Message}"); Failed++; }
            }
        }

        ShipState Find(string name)
        {
            foreach (var s in _world.Ships.All) if (s.Callsign == name) return s;
            return null;
        }

        static Vector3 V3(JToken t) { var a = (JArray)t; return new Vector3((float)a[0], (float)a[1], (float)a[2]); }

        void Run(JObject o)
        {
            string what = (string)o["do"];
            var me = _world.Ships.Get(_world.LocalPlayerShip);
            switch (what)
            {
                case "place":
                {
                    Vector3 pos = V3(o["pos"]);
                    Vector3 rot = o["lookAt"] != null ? Quaternion.LookRotation(V3(o["lookAt"]) - pos, Vector3.up).eulerAngles : V3(o["rot"]);
                    _range.PlacePlayer(pos, rot, (float)(o["speed"] ?? 0.3f), (float)(o["throttle"] ?? 0.3f));
                    _held.Throttle = (float)(o["throttle"] ?? 0.3f);
                    break;
                }
                case "face":
                {
                    var ship = Find((string)o["name"]) ?? throw new ArgumentException($"no ship called '{(string)o["name"]}'");
                    Vector3 dir = o["from"] != null ? V3(o["from"]).normalized : new Vector3(0.1f, 0.08f, -1f).normalized;
                    Vector3 pos = ship.Position + dir * (float)(o["distance"] ?? 300f);
                    Vector3 lookAt = ship.Position + (o["offset"] != null ? V3(o["offset"]) : Vector3.zero);
                    _range.PlacePlayer(pos, Quaternion.LookRotation(lookAt - pos, Vector3.up).eulerAngles, (float)(o["speed"] ?? 0.3f), (float)(o["throttle"] ?? 0.3f));
                    _held.Throttle = (float)(o["throttle"] ?? 0.3f);
                    break;
                }
                case "input":
                    _held.Pitch = (float)(o["pitch"] ?? 0f); _held.Yaw = (float)(o["yaw"] ?? 0f); _held.Roll = (float)(o["roll"] ?? 0f);
                    if (o["throttle"] != null) _held.Throttle = (float)o["throttle"];
                    if (o["afterburner"] != null) _held.Buttons = (bool)o["afterburner"] ? _held.Buttons | ControlButtons.Afterburner : _held.Buttons & ~ControlButtons.Afterburner;
                    break;
                case "facePart":
                {
                    // Put the player the given distance from a named part of a ship, looking at it ("from" is a world direction from the part).
                    var ship = Find((string)o["name"]) ?? throw new ArgumentException($"no ship called '{(string)o["name"]}'");
                    var tc = _world.Combat.Get(ship.Id);
                    var part = tc?.FindPart((string)o["part"]) ?? throw new ArgumentException($"no part '{(string)o["part"]}' on {ship.Callsign}");
                    Vector3 at = part.WorldPosition(ship);
                    Vector3 pos = at + V3(o["from"]).normalized * (float)(o["distance"] ?? 300f);
                    _range.PlacePlayer(pos, Quaternion.LookRotation(at - pos, Vector3.up).eulerAngles, (float)(o["speed"] ?? 0.3f), (float)(o["throttle"] ?? 0.3f));
                    _held.Throttle = (float)(o["throttle"] ?? 0.3f);
                    break;
                }
                case "trace": _trace = (bool)(o["on"] ?? true); break;
                case "menu": _menu = (bool)(o["on"] ?? true); break;
                case "overlay": _range.ShowPause((bool)(o["on"] ?? true)); break;
                case "map": if (_range.Hud != null) _range.Hud.MapSwitch = (bool)(o["on"] ?? true); break;
                case "fire": _fire = (bool)(o["on"] ?? true); break;
                case "heavy": _heavy = (bool)(o["on"] ?? true); break;
                case "missile": _missile = (bool)(o["on"] ?? true); break;
                case "decoy": _decoy = (bool)(o["on"] ?? true); break;
                case "cycle": _taps |= ControlButtons.CycleMissile; break;
                case "select": _tapSelect = (int)o["slot"] + 1; break;
                case "targetNext": _taps |= ControlButtons.TargetNext; break;
                case "targetAttacker": _taps |= ControlButtons.TargetAttacker; break;
                case "targetReticle": _taps |= ControlButtons.TargetReticle; break;
                case "subsystem": _taps |= ControlButtons.SubsystemNext; break;
                case "target":
                {
                    var t = Find((string)o["name"]) ?? throw new ArgumentException($"no ship called '{(string)o["name"]}'");
                    _tapTarget = t.Id;
                    if (o["part"] != null)
                    {
                        var tc = _world.Combat.Get(t.Id);
                        var part = tc?.FindPart((string)o["part"]) ?? throw new ArgumentException($"no part '{(string)o["part"]}' on {t.Callsign}");
                        _tapPart = part.Index + 1;
                    }
                    break;
                }
                case "power":
                {
                    switch (((string)o["mode"] ?? "balanced").ToLowerInvariant())
                    {
                        case "shields": _taps |= ControlButtons.PowerShields; break;
                        case "guns": _taps |= ControlButtons.PowerGuns; break;
                        case "engines": _taps |= ControlButtons.PowerEngines; break;
                        default: _taps |= ControlButtons.PowerBalanced; break;
                    }
                    break;
                }
                case "aim":
                    _aimMode = ((string)o["at"] ?? "lead").ToLowerInvariant();
                    _aimPartName = (string)o["part"];
                    _aimGain = (float)(o["gain"] ?? 3.5f);
                    _aimTolerance = (float)(o["tolerance"] ?? 1.2f);
                    _aimFireRange = (float)(o["fireRange"] ?? 900f);
                    _aimAutoFire = (bool)(o["autoFire"] ?? false);
                    _aimGun = ((string)o["gun"] ?? "fire").ToLowerInvariant();
                    _holdRange = (float)(o["holdRange"] ?? 0f);
                    _prevDirValid = false;
                    break;
                case "invulnerable":
                {
                    var c = _world.Combat.Get(_world.LocalPlayerShip);
                    if (c != null) c.Invulnerable = (bool)(o["on"] ?? true);
                    break;
                }
                case "damage":
                {
                    var ship = Find((string)o["ship"]) ?? throw new ArgumentException($"no ship called '{(string)o["ship"]}'");
                    var c = _world.Combat.Get(ship.Id);
                    if (c == null) throw new ArgumentException($"{ship.Callsign} has no combat state");
                    int part = -1;
                    if (o["part"] != null) part = c.FindPart((string)o["part"])?.Index ?? throw new ArgumentException($"no part '{(string)o["part"]}'");
                    Vector3 point = ship.Position + (me != null ? (me.Position - ship.Position).normalized * ship.Radius * 0.5f : Vector3.zero);
                    if (part >= 0) point = c.Parts[part].WorldPosition(ship);
                    _world.Damage.Add(new DamageRequest
                    {
                        Ship = ship.Id, Attacker = _world.LocalPlayerShip, Weapon = "debug", Amount = (float)o["amount"],
                        ShieldMult = (float)(o["shieldMult"] ?? 1f), HullMult = (float)(o["hullMult"] ?? 1f), SubsystemMult = (float)(o["subsystemMult"] ?? 1f),
                        Point = point, Direction = (point - ship.Position).normalized, Normal = (point - ship.Position).normalized, Part = part, Kind = DamageKind.Debug,
                        BypassShield = (bool)(o["bypassShield"] ?? false),
                    });
                    break;
                }
                case "log": LogState((string)o["ship"] ?? "player"); break;
                case "say": MnLog.Info("RANGE SAY t=" + _world.Time.ToString("0.00", CultureInfo.InvariantCulture) + " " + (string)o["text"]); break;
                case "expect": Expect(o); break;
                case "summary":
                    MnLog.Info($"RANGE SCRIPT SUMMARY: {Passed} passed, {Failed} failed");
                    break;
                default: throw new ArgumentException($"unknown script step '{what}'");
            }
        }

        // ------------------------------------------------------------------------------------------------ checks and logs

        void LogState(string name)
        {
            ShipState ship = name == "player" ? _world.Ships.Get(_world.LocalPlayerShip) : Find(name);
            if (ship == null) { MnLog.Info($"RANGE STATE {name}: gone"); return; }
            var c = _world.Combat.Get(ship.Id);
            if (c == null) { MnLog.Info($"RANGE STATE {name}: no combat state"); return; }
            var sb = new System.Text.StringBuilder();
            sb.Append($"RANGE STATE t={_world.Time:0.00} {ship.Callsign}: hull {c.Hull:0}/{c.HullMax:0} shield {c.Shield:0}/{c.ShieldMax:0} energy {c.Energy:0.0}/{c.EnergyMax:0} alive={c.Alive}");
            sb.Append($" speedScale={ship.SpeedScale:0.00} flags={c.Flags} power={c.Power} lock={c.Lock}({c.LockProgress01:0.00}) incoming={c.IncomingMissiles}({c.IncomingNearest:0} m, eta {c.IncomingEta:0.0}) decoys={c.Decoys}");
            foreach (var m in c.Missiles) sb.Append($" {m.Weapon}={m.Ammo}");
            for (int i = 0; i < c.Parts.Count; i++) if (c.Parts[i].Health01 < 0.999f) sb.Append($" [{c.Parts[i].Name} {c.Parts[i].Health01:0.00}]");
            MnLog.Info(sb.ToString());
        }

        void Expect(JObject o)
        {
            string what = (string)o["what"];
            string name = (string)o["ship"] ?? "player";
            ShipState ship = name == "player" ? _world.Ships.Get(_world.LocalPlayerShip) : Find(name);
            var c = ship != null ? _world.Combat.Get(ship.Id) : null;
            bool ok; string detail;
            switch (what)
            {
                case "dead": ok = ship == null || c == null || !c.Alive; detail = $"{name} dead"; break;
                case "alive": ok = c != null && c.Alive; detail = $"{name} alive"; break;
                case "lock": ok = c != null && c.Lock == LockPhase.Locked; detail = $"{name} holds a lock (progress {(c != null ? c.LockProgress01 : 0f):0.00})"; break;
                case "acquiring": ok = c != null && c.Lock == LockPhase.Acquiring; detail = $"{name} is building a lock ({(c != null ? c.LockProgress01 : 0f):0.00})"; break;
                case "partDestroyed":
                {
                    var p = c?.FindPart((string)o["part"]);
                    ok = p != null && p.Destroyed; detail = $"{name} part {(string)o["part"]} destroyed";
                    break;
                }
                case "partIntact":
                {
                    var p = c?.FindPart((string)o["part"]);
                    ok = p != null && !p.Destroyed; detail = $"{name} part {(string)o["part"]} intact";
                    break;
                }
                case "incoming": ok = c != null && c.IncomingMissiles > 0; detail = $"{name} has a missile inbound ({(c != null ? c.IncomingMissiles : 0)})"; break;
                case "saw":
                {
                    _seenEvents.TryGetValue((string)o["event"], out int n);
                    ok = n >= (int)(o["atLeast"] ?? 1); detail = $"event {(string)o["event"]} was raised {n} time(s)";
                    break;
                }
                case "hullBelow": ok = c != null && c.Hull01 < (float)o["value"]; detail = $"{name} hull below {(float)o["value"]:0.00} (is {(c != null ? c.Hull01 : 0f):0.00})"; break;
                case "hullAbove": ok = c != null && c.Hull01 > (float)o["value"]; detail = $"{name} hull above {(float)o["value"]:0.00} (is {(c != null ? c.Hull01 : 0f):0.00})"; break;
                case "shieldBelow": ok = c != null && c.Shield01 < (float)o["value"]; detail = $"{name} shield below {(float)o["value"]:0.00} (is {(c != null ? c.Shield01 : 0f):0.00})"; break;
                case "speedScaleBelow": ok = ship != null && ship.SpeedScale < (float)o["value"]; detail = $"{name} speed scale below {(float)o["value"]:0.00} (is {(ship != null ? ship.SpeedScale : 0f):0.00})"; break;
                case "flag": ok = c != null && (c.Flags & (CombatFlags)Enum.Parse(typeof(CombatFlags), (string)o["flag"])) != 0; detail = $"{name} has flag {(string)o["flag"]}"; break;
                case "target": ok = me() is ShipState m && m.Target != GameWorld.NoShip && _world.Ships.Get(m.Target)?.Callsign == name; detail = $"player targets {name}"; break;
                default: throw new ArgumentException($"unknown expectation '{what}'");
            }
            if (ok) Passed++; else Failed++;
            MnLog.Info($"RANGE EXPECT {(ok ? "PASS" : "FAIL")} t={_world.Time:0.00}: {detail}");
        }

        ShipState me() => _world.Ships.Get(_world.LocalPlayerShip);

        // ------------------------------------------------------------------------------------------------ the pilot

        public ControlInput Sample(in SimFrame frame)
        {
            var c = _held;
            if (_fire) c.Buttons |= ControlButtons.PrimaryFire;
            if (_heavy) c.Buttons |= ControlButtons.SecondaryFire;
            if (_missile) c.Buttons |= ControlButtons.MissileFire;
            if (_decoy) c.Buttons |= ControlButtons.Countermeasure;
            if (_menu) c.Buttons |= ControlButtons.Orders;
            c.Buttons |= _taps; _taps = ControlButtons.None;
            c.TargetShip = _tapTarget; _tapTarget = 0;
            c.TargetPartPlusOne = _tapPart; _tapPart = 0;
            c.SelectMissilePlusOne = _tapSelect; _tapSelect = 0;
            if (_aimMode != "off") Aim(ref c);
            return c;
        }

        /// <summary>Point the nose at the lead point (or the target, or a part of it) with the sticks, and fire when lined up.</summary>
        void Aim(ref ControlInput c)
        {
            var ship = me();
            var combat = _world.Combat.Get(_world.LocalPlayerShip);
            var t = ship != null ? _world.Ships.Get(ship.Target) : null;
            if (ship == null || combat == null || t == null) return;
            Vector3 aimAt = t.Position;
            var tc = _world.Combat.Get(t.Id);
            string partName = _aimPartName;
            if (partName != null && tc != null) { var p = tc.FindPart(partName); if (p != null && !p.Destroyed) aimAt = p.WorldPosition(t); }
            else if (ship.TargetPart >= 0 && tc != null && ship.TargetPart < tc.Parts.Count && !tc.Parts[ship.TargetPart].Destroyed) aimAt = tc.Parts[ship.TargetPart].WorldPosition(t);

            Vector3 dir = (aimAt - ship.Position).normalized;
            if (_aimMode == "lead")
            {
                float speed = _aimGun == "heavy" ? combat.HeavySpeed : combat.PrimarySpeed;
                if (speed > 1f && Geo.SolveIntercept(ship.Position, ship.Velocity, aimAt, t.Velocity, speed, out Vector3 d, out _, out _)) dir = d;
            }
            Vector3 local = Quaternion.Inverse(ship.Rotation) * dir;
            float yawErr = Mathf.Atan2(local.x, local.z), pitchErr = Mathf.Atan2(local.y, local.z);
            // Feed-forward of the line-of-sight rate (the aim direction's drift, measured in this tick's ship frame so the ship's own turning
            // does not enter it) plus a proportional term on the error: the stick asks for the rate that keeps the nose on the aim point.
            float losYaw = 0f, losPitch = 0f;
            if (_prevDirValid)
            {
                Vector3 l1 = Quaternion.Inverse(ship.Rotation) * _prevDir;
                losYaw = Mathf.DeltaAngle(Mathf.Atan2(l1.x, l1.z) * Mathf.Rad2Deg, yawErr * Mathf.Rad2Deg) * Mathf.Deg2Rad / Sim.Dt;
                losPitch = Mathf.DeltaAngle(Mathf.Atan2(l1.y, l1.z) * Mathf.Rad2Deg, pitchErr * Mathf.Rad2Deg) * Mathf.Deg2Rad / Sim.Dt;
            }
            _prevDir = dir; _prevDirValid = true;
            var turn = Monolith.Flight.ShipCatalog.Load(ship.ClassId).MaxTurnRate;
            c.Yaw = Mathf.Clamp((losYaw + yawErr * _aimGain) / turn.y, -1f, 1f);
            c.Pitch = Mathf.Clamp((losPitch + pitchErr * _aimGain) / turn.x, -1f, 1f);
            float err = Vector3.Angle(ship.Forward, dir);
            float dist = Vector3.Distance(ship.Position, aimAt);
            if (_holdRange > 0f)
            {
                var cls = Monolith.Flight.ShipCatalog.Load(ship.ClassId);
                float follow = Mathf.Max(0f, t.ForwardSpeed) / Mathf.Max(1f, cls.TopSpeed);
                c.Throttle = Mathf.Clamp01(follow + (dist - _holdRange) * 0.0025f);
            }
            // The hull is a few metres across: the lined-up cone narrows with range.
            float tol = Mathf.Min(_aimTolerance * 3f, Mathf.Max(_aimTolerance * 0.5f, Mathf.Atan2(4.5f, Mathf.Max(30f, dist)) * Mathf.Rad2Deg * 0.9f));
            _lastErr = err; _lastDist = dist; _lastTol = tol;
            _lastFiring = _aimAutoFire && err < tol && dist < _aimFireRange;
            if (_lastFiring) c.Buttons |= _aimGun == "heavy" ? ControlButtons.SecondaryFire : ControlButtons.PrimaryFire;
        }
    }
}
