using System;
using System.Collections.Generic;
using Monolith.Core;
using Monolith.Flight;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Drives the sandbox from a capture plan so a run is repeatable: <c>"sceneOptions": { "script": [ { "t": 2.5, "do": "event", "name": "fighter_kill" }, ... ] }</c>.
    /// Steps, by game time <c>t</c> in seconds:
    /// <list type="bullet">
    /// <item><c>place</c> <c>pos</c> <c>rot</c> (Euler) or <c>lookAt</c> (a point), <c>speed</c> (fraction of top speed) <c>throttle</c>: put the player's ship there.</item>
    /// <item><c>input</c> <c>pitch yaw roll throttle afterburner</c>: hold these stick values from now on.</item>
    /// <item><c>fire</c> <c>on</c>: hold or release the guns.</item>
    /// <item><c>event</c> <c>name</c>: one of the sandbox's events (fighter_kill, bomber_kill, capital_death, shield_hit, hull_hit, near_miss, beam_strike, flak, missile, volley, you_shield, you_hull).</item>
    /// <item><c>face</c> <c>name</c> <c>distance</c> <c>from</c>: place the player that far from the named ship, on that side of it, looking at it, and target it.</item>
    /// <item><c>battle</c> <c>on</c>: the ambient battle. <c>target</c> <c>name</c>: target a ship by callsign. <c>raise</c> <c>id</c> <c>pos</c> <c>dir</c> <c>subject</c> <c>value</c>: raise any combat event by hand.</item>
    /// </list>
    /// The plan's stills and perf runs are timed against the same clock.
    /// </summary>
    public sealed class SandboxScript : IPilotSource, ISimSystem
    {
        sealed class ScriptStep { public int Tick; public JObject Json; public bool Done; }

        readonly GameWorld _world;
        readonly FlightSystem _flight;
        readonly JuiceSandbox _box;
        readonly List<ScriptStep> _steps = new List<ScriptStep>();
        ControlInput _held;
        bool _fire;

        public SandboxScript(GameWorld world, FlightSystem flight, JuiceSandbox box, JArray steps)
        {
            _world = world; _flight = flight; _box = box;
            foreach (var t in steps)
            {
                var o = (JObject)t;
                _steps.Add(new ScriptStep { Tick = Mathf.RoundToInt((float)(o["t"] ?? 0) * Sim.Hz), Json = o });
            }
            _steps.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        }

        public int Order => SimOrder.Pilots + 40;

        Vector3 _lastVel;
        bool _velInit;

        public void Step(in SimFrame frame)
        {
            // Log every shove the player gets (a shockwave through GameWorld.Knocks): a speed change that the pad did not ask for.
            var me = _world.Ships.Get(_world.LocalPlayerShip);
            if (me != null)
            {
                if (_velInit)
                {
                    float dv = (me.Velocity - _lastVel).magnitude;
                    if (dv > 0.5f && !(_held.Throttle > 0f)) MnLog.Info($"SCRIPT the player was shoved: velocity changed by {dv:0.0} m/s on tick {frame.Tick} (knock {me.Knock:0.00})");
                }
                _lastVel = me.Velocity; _velInit = true;
            }
            foreach (var s in _steps)
            {
                if (s.Done || s.Tick > frame.Tick) continue;
                s.Done = true;
                try { Run(s.Json); }
                catch (Exception e) { MnLog.Error($"SCRIPT step at t={s.Json["t"]} failed: {e.Message}"); }
            }
        }

        void Run(JObject o)
        {
            string what = (string)o["do"];
            switch (what)
            {
                case "place":
                {
                    Vector3 pos = _box.L(V3(o["pos"]));
                    Quaternion rot = o["lookAt"] != null ? Quaternion.LookRotation(_box.L(V3(o["lookAt"])) - pos, Vector3.up) : _box.L(Quaternion.Euler(V3(o["rot"])));
                    _box.PlacePlayerWorld(pos, rot, (float)(o["speed"] ?? 0.3f), (float)(o["throttle"] ?? 0.3f));
                    _held = default; _velInit = false;
                    break;
                }
                case "input":
                    _held.Pitch = (float)(o["pitch"] ?? 0f); _held.Yaw = (float)(o["yaw"] ?? 0f); _held.Roll = (float)(o["roll"] ?? 0f);
                    if (o["throttle"] != null) _held.Throttle = (float)o["throttle"];
                    if (o["afterburner"] != null) _held.Buttons = (bool)o["afterburner"] ? _held.Buttons | ControlButtons.Afterburner : _held.Buttons & ~ControlButtons.Afterburner;
                    break;
                case "fire": _fire = (bool)(o["on"] ?? true); break;
                case "event": _box.Trigger((string)o["name"]); break;
                case "battle": _box.SetBattle((bool)(o["on"] ?? true)); break;
                case "face":
                {
                    // Put the player in front of a ship, looking at it: { "do": "face", "name": "Reaper 1", "distance": 160, "from": [0.1, 0.08, -1] }
                    string name = (string)o["name"];
                    ShipState ship = null;
                    foreach (var s in _world.Ships.All) if (s.Callsign == name) { ship = s; break; }
                    if (ship == null) throw new ArgumentException($"no ship called '{name}'");
                    Vector3 dir = o["from"] != null ? _box.L(V3(o["from"])).normalized : _box.L(new Vector3(0.1f, 0.08f, -1f)).normalized;
                    Vector3 pos = ship.Position + dir * (float)(o["distance"] ?? 200f);
                    var look = Quaternion.LookRotation(ship.Position - pos, Vector3.up);
                    _box.PlacePlayerWorld(pos, look, 0f, 0f);
                    _world.Ships.Get(_world.LocalPlayerShip).Target = ship.Id;
                    _held = default; _velInit = false;
                    break;
                }
                case "target":
                {
                    string name = (string)o["name"];
                    foreach (var s in _world.Ships.All) if (s.Callsign == name) { _world.Ships.Get(_world.LocalPlayerShip).Target = s.Id; break; }
                    break;
                }
                case "raise":
                    GameEvents.RaiseAt((string)o["id"], _box.L(V3(o["pos"])), o["dir"] != null ? _box.L(V3(o["dir"])) : Vector3.zero, (string)o["subject"], (float)(o["value"] ?? 0f),
                                       (int)(o["entity"] ?? GameWorld.NoShip), (int)(o["other"] ?? GameWorld.NoShip));
                    break;
                default: throw new ArgumentException($"unknown script step '{what}'");
            }
        }

        public ControlInput Sample(in SimFrame frame)
        {
            var c = _held;
            if (_fire) c.Buttons |= ControlButtons.PrimaryFire;
            return c;
        }

        static Vector3 V3(JToken t) { var a = (JArray)t; return new Vector3((float)a[0], (float)a[1], (float)a[2]); }
    }
}
