using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.Core;
using Monolith.Input;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Monolith.Cabin
{
    /// <summary>What the pilot asks of the cabin this tick: walk, look, the context button, the stand-up hold.</summary>
    public struct CabinInputFrame
    {
        /// <summary>x strafes right, y walks forward (-1..1, shaped).</summary>
        public Vector2 Move;
        /// <summary>x turns right, y looks up (-1..1, shaped).</summary>
        public Vector2 Look;
        /// <summary>The context button: held, and the tick it went down.</summary>
        public bool Context, ContextPressed;
        /// <summary>A second button: the tick it went down (step back from a panel, the table's previous page).</summary>
        public bool Back;
        /// <summary>The stand-up / sit-down button is held.</summary>
        public bool StandHeld;
        /// <summary>The seat's switch to the battle map (a double tap of the stand button, or a key): the tick it fired.</summary>
        public bool MapToggle;
        /// <summary>Before launch, in the seat: the confirm button went down (launch).</summary>
        public bool Launch;
    }

    /// <summary>Where the cabin's input comes from: the pad and keyboard, or a script (tests, captures).</summary>
    public interface ICabinInput
    {
        CabinInputFrame Sample(float dt);
    }

    /// <summary>
    /// The pad and keyboard as the cabin's input: left stick walks, right stick looks, A is the context button, B steps back, and holding View stands you up or
    /// sits you down (a double tap of View is the seat's battle map). Shaping and the mapping are in cabin.json's controls; the pad is read first, the keyboard
    /// as a fallback (W S A D walk, the arrow keys look, E context, Q back, hold Z to stand or sit, B the map, Enter launches).
    /// </summary>
    public sealed class PadCabinInput : ICabinInput
    {
        readonly CabinConfig.Controls _c;
        bool _prevContext, _prevBack, _prevStand, _prevLaunch, _prevMapKey;
        float _lastTapTime = -10f, _clock;
        bool _standWasDown;

        public PadCabinInput(CabinConfig cfg) { _c = cfg.controls; }

        public CabinInputFrame Sample(float dt)
        {
            _clock += dt;
            var pad = Gamepad.current;
            var kb = Keyboard.current;
            var f = default(CabinInputFrame);
            var p = _c.pad; var k = _c.keyboard;
            Vector2 move = Vector2.zero, look = Vector2.zero;
            bool context = false, back = false, stand = false, launch = false, mapKey = false;
            if (pad != null)
            {
                move = StickShaping.Radial(Stick(pad, p.move), p.deadZone, 0.04f, p.moveExponent);
                look = StickShaping.Radial(Stick(pad, p.look), p.deadZone, 0.04f, p.lookExponent);
                context |= Down(pad, p.context);
                back |= Down(pad, p.back);
                stand |= Down(pad, p.standSit);
                launch |= Down(pad, p.context);
            }
            if (kb != null)
            {
                move += new Vector2(Key(kb, k.right) - Key(kb, k.left), Key(kb, k.forward) - Key(kb, k.backward));
                look += new Vector2(Key(kb, k.lookRight) - Key(kb, k.lookLeft), Key(kb, k.lookUp) - Key(kb, k.lookDown)) * 0.6f;
                context |= Down(kb, k.context);
                back |= Down(kb, k.back);
                stand |= Down(kb, k.standSit);
                launch |= Down(kb, k.launch);
                mapKey |= Down(kb, k.map);
            }
            f.Move = Vector2.ClampMagnitude(move, 1f);
            f.Look = Vector2.ClampMagnitude(look, 1f);
            f.Context = context;
            f.ContextPressed = context && !_prevContext;
            f.Back = back && !_prevBack;
            f.StandHeld = stand;
            f.Launch = launch && !_prevLaunch;
            // The map: a double tap of the stand button (two quick presses, neither long enough to stand you up), or the key.
            bool standDown = stand && !_prevStand;
            if (standDown)
            {
                if (_clock - _lastTapTime < p.mapDoubleTapSeconds) { f.MapToggle = true; _lastTapTime = -10f; }
                else _lastTapTime = _clock;
            }
            if (mapKey && !_prevMapKey) f.MapToggle = true;
            _prevContext = context; _prevBack = back; _prevStand = stand; _prevLaunch = launch; _prevMapKey = mapKey;
            return f;
        }

        static Vector2 Stick(InputDevice d, string path) => !string.IsNullOrEmpty(path) && d.TryGetChildControl(path) is StickControl s ? s.ReadValue() : Vector2.zero;
        static bool Down(InputDevice d, string path) => !string.IsNullOrEmpty(path) && d.TryGetChildControl(path) is ButtonControl b && b.isPressed;
        static float Key(InputDevice d, string path) => Down(d, path) ? 1f : 0f;
    }

    /// <summary>
    /// A scripted pilot for the cabin (self-tests, capture plans): steps by time. Each step is an object with <c>t</c> (seconds of cabin time) and <c>do</c>:
    /// <c>stand</c> (holds the stand button until up), <c>sit</c>, <c>walk</c> (<c>to</c>: an interactable id, or <c>[x, z]</c> in cabin space; walks and stops there),
    /// <c>face</c> (<c>at</c>: an interactable id, or <c>yaw</c> / <c>pitch</c> in degrees), <c>use</c> (<c>seconds</c>: holds the context button), <c>press</c> (a tap),
    /// <c>back</c> (a tap of the back button), <c>map</c> (the seat's map), <c>launch</c>, <c>wait</c>. Steps run one after another, each starting no earlier than its <c>t</c>.
    /// </summary>
    public sealed class ScriptedCabinInput : ICabinInput
    {
        sealed class Step { public float T; public string Do; public JObject J; public float Seconds; }

        readonly List<Step> _steps = new List<Step>();
        int _i;
        float _clock, _stepStart = -1f;
        CabinSystem _cabin;
        bool _ctxPrev;
        int _via;
        public bool Finished => _i >= _steps.Count;
        public string Log;
        public bool Verbose = true;

        public ScriptedCabinInput(JArray steps)
        {
            foreach (JObject j in steps)
                _steps.Add(new Step { T = (float?)j["t"] ?? 0f, Do = (string)j["do"] ?? throw new InvalidOperationException("a cabin script step needs 'do'"), J = j, Seconds = (float?)j["seconds"] ?? 0f });
        }

        public void Bind(CabinSystem cabin) { _cabin = cabin; }

        public CabinInputFrame Sample(float dt)
        {
            _clock += dt;
            var f = default(CabinInputFrame);
            if (_cabin == null || _i >= _steps.Count) { _ctxPrev = false; return f; }
            var s = _steps[_i];
            if (_clock < s.T) return f;
            if (_stepStart < 0f) _stepStart = _clock;
            float since = _clock - _stepStart;
            bool done = false;
            switch (s.Do)
            {
                case "wait": done = since >= s.Seconds; break;
                case "stand":
                    f.StandHeld = true;
                    done = _cabin.State == CabinBoard.Phase.Standing;
                    break;
                case "sit":
                    // Walk back to the seat's place first, then hold.
                    if (_cabin.State == CabinBoard.Phase.Standing && !_cabin.AtSeat) { f = WalkToward(_cabin.SeatStandPoint(), dt); break; }
                    f.StandHeld = true;
                    done = _cabin.State == CabinBoard.Phase.Seated;
                    break;
                case "walk":
                {
                    // optional "via": [[x, z], ...] points to go through first (the plan table is in the way of a straight line)
                    Vector2 to = Target(s.J["to"]);
                    var via = s.J["via"] as JArray;
                    if (via != null && _via < via.Count)
                    {
                        Vector2 v = new Vector2((float)via[_via][0], (float)via[_via][1]);
                        f = WalkToward(v, dt);
                        if ((v - _cabin.Position2).magnitude < 0.12f) _via++;
                        break;
                    }
                    f = WalkToward(to, dt);
                    done = (to - _cabin.Position2).magnitude < 0.06f || since > 25f;
                    break;
                }
                case "face":
                {
                    float yaw = _cabin.Yaw, pitch = _cabin.Pitch;
                    if (s.J["at"] != null)
                    {
                        var it = _cabin.Layout.Find((string)s.J["at"]);
                        Vector3 eye = _cabin.EyeCabin;
                        Vector3 d = it.Pos - eye;
                        yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                        // a person aims at a wall panel with a glance down or up, not with their chin on their chest: the aim cone (about 37 degrees) forgives the rest
                        pitch = Mathf.Clamp(Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -45f, 40f);
                    }
                    if (s.J["yaw"] != null) yaw = (float)s.J["yaw"];
                    if (s.J["pitch"] != null) pitch = (float)s.J["pitch"];
                    done = _cabin.AimToward(yaw, pitch, dt, ref f);
                    break;
                }
                case "use":
                    f.Context = true;
                    f.ContextPressed = !_ctxPrev;
                    done = since >= s.Seconds;
                    break;
                case "press":
                    f.Context = since < 0.1f; f.ContextPressed = !_ctxPrev && since < 0.1f;
                    done = since >= 0.2f;
                    break;
                case "back": f.Back = true; done = true; break;
                case "map": f.MapToggle = true; done = true; break;
                case "launch": f.Launch = true; done = true; break;
                default: throw new InvalidOperationException($"unknown cabin script step '{s.Do}'");
            }
            _ctxPrev = f.Context;
            if (done)
            {
                _i++; _stepStart = -1f; _via = 0; Log += $"[{_clock:0.00}] {s.Do} done; ";
                if (Verbose) MnLog.Info($"CABIN script {_clock:0.00} s: {s.Do}{(s.J["to"] != null ? " " + s.J["to"] : "")}{(s.J["at"] != null ? " " + s.J["at"] : "")} done; the pilot is at {_cabin.Position2} facing {_cabin.Yaw:0}/{_cabin.Pitch:0}, {_cabin.State}, {_cabin.Action}");
            }
            return f;
        }

        Vector2 Target(JToken t)
        {
            if (t.Type == JTokenType.Array) return new Vector2((float)t[0], (float)t[1]);
            string id = (string)t;
            var it = _cabin.Layout.Find(id) ?? throw new InvalidOperationException($"no interactable '{id}'");
            return it.Stand;
        }

        CabinInputFrame WalkToward(Vector2 to, float dt)
        {
            var f = default(CabinInputFrame);
            Vector2 here = _cabin.Position2;
            Vector2 d = to - here;
            if (d.magnitude < 0.05f) return f;
            float want = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            float err = Mathf.DeltaAngle(_cabin.Yaw, want);
            f.Look.x = Mathf.Clamp(err / 30f, -1f, 1f);
            f.Move.y = Mathf.Abs(err) < 40f ? Mathf.Clamp(d.magnitude / 0.3f, 0.2f, 1f) : 0f;
            return f;
        }
    }
}
